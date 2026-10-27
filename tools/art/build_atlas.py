#!/usr/bin/env python3
"""
Genera las hojas de sprites (atlas) de los gatos y sus metadatos a partir de art/clips.json.

Para cada piel (orange, gray) y cada clip:
  - clips con status "final": usa el arte dibujado tal cual;
  - clips "placeholder": genera fotogramas de REFERENCIA aplicando la receta del clip
    (escala, giro, desplazamiento, espejo) sobre las poses ya dibujadas.

Cada fotograma se convierte después a una CUADRÍCULA (1 casilla = 1 px de la app a escala 100%) en
forma de T: el cuadro PRINCIPAL de 48x48 (los pies sobre la base, su borde de abajo), 16 columnas a
cada lado para lo que necesita más ancho (correr, estirarse) y 16 filas por debajo del principal para
lo que cuelga. Cada casilla es un color de la paleta de la piel, escrito con una letra en
  art/grid/<piel>/<clip>.txt      (fotogramas en letras; "." = transparente)
  art/grid/<piel>/paleta.txt      (letra -> color; estable entre ejecuciones)
Los archivos con "fuente: auto" se regeneran en cada ejecución. Si editas uno a mano, cambia esa
línea a "fuente: mano": a partir de ahí ese archivo es el original y se usa tal cual.

Salidas:
  src/Swip.App/Assets/atlas/cats_<piel>.png   hoja de sprites (una celda = una cuadrícula, 1:1)
  src/Swip.App/Assets/atlas/atlas.json        celda de cada fotograma + caja visible por piel
  docs/art/reference/<clip>.png               hoja de referencia para el artista
  docs/art/ARTE_PENDIENTE.md                  qué fotogramas faltan y qué debe mostrar cada uno

Para sustituir una referencia por arte definitivo: dibuja los fotogramas, guárdalos como
art/final/<piel>/<clip>_<n>.png (lienzo 269x379, pies abajo) y cambia el status del clip a "final".
Uso:  python3 tools/art/build_atlas.py
"""
import json
import math
import os
import sys
from collections import Counter

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CLIPS = os.path.join(ROOT, "art", "clips.json")
POSES = os.path.join(ROOT, "art", "poses")                           # poses dibujadas originales
FINAL = os.path.join(ROOT, "art", "final")                            # arte definitivo nuevo
OUT = os.path.join(ROOT, "src", "Swip.App", "Assets", "atlas")
REF = os.path.join(ROOT, "docs", "art", "reference")
GRID = os.path.join(ROOT, "art", "grid")
BOX_SRC = os.path.join(ROOT, "art", "box")                           # dibujos de la caja (cerrada/abierta)
BOX_OUT = os.path.join(ROOT, "src", "Swip.App", "Assets", "box")     # su cuadrícula para la app
# Caja: principal de 32x32 casillas que el cuerpo de la caja llena POR COMPLETO, y un margen de 16
# a cada lado y por encima para las solapas de la caja abierta y el tambaleo. Cuadro total 64x48,
# la base es el borde de abajo del principal (y del cuadro).
BOX_SIDE, BOX_TOP, BOX_MAIN = 16, 16, 32
BOX_W, BOX_H = BOX_MAIN + 2 * BOX_SIDE, BOX_MAIN + BOX_TOP
BOX_BODY = (58, 46, 335, 282)    # cuerpo de la caja cerrada en su dibujo (art/box): llena el principal
SKINS = ["orange", "gray"]
# Cuadro de dibujo de cada fotograma (1 casilla = 1 px lógico de la app a escala 100 %), en T:
#   principal 48x48 (MAIN_X..MAIN_X+48, filas 0..47): el gato, con los pies sobre la base (fila 47);
#   16 columnas a cada lado del principal (filas 0..47): correr, estirarse...;
#   16 filas por debajo del principal (solo bajo él): lo que cuelga (cola, algo que se cae...).
SIDE = 16
MAIN = 48
BELOW = 16
MAIN_X = SIDE
GRID_W = MAIN + 2 * SIDE
GRID_BASE = MAIN                 # filas por encima de la base
GRID_H = GRID_BASE + BELOW
# Escala del arte: píxeles del dibujo original por casilla. Con 6.4 la pose de pie más alta
# (309 px) llena las 48 filas del principal; todas las poses usan la misma escala.
ART_PX_PER_CELL = 6.4
COLUMNS = 12
ALPHA_MIN = 20
PALETTE_SIZE = 62                # colores por piel (máximo: una letra o dígito por color)
PALETTE_MIN_DIST = 30            # dos colores de la paleta deben diferenciarse al menos esto
LETTERS = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
INK = 60                         # luminancia por debajo de la cual un color es "contorno"


def load_pose(skin, base, frame):
    return Image.open(os.path.join(POSES, f"cat_{skin}_{base}_{frame}.png")).convert("RGBA")


def opaque_box(img):
    return img.getchannel("A").point(lambda v: 255 if v > ALPHA_MIN else 0).getbbox()


def render_reference(skin, spec, size):
    """Aplica la receta de un fotograma sobre una pose dibujada. Devuelve un lienzo del tamaño base."""
    w, h = size
    pose = load_pose(skin, spec["base"], spec.get("frame", 0))
    box = opaque_box(pose)
    body = pose.crop(box)
    if spec.get("flip"):
        body = body.transpose(Image.FLIP_LEFT_RIGHT)

    sx, sy = spec.get("sx", 1.0), spec.get("sy", 1.0)
    if sx != 1 or sy != 1:
        body = body.resize((max(1, round(body.width * sx)), max(1, round(body.height * sy))), Image.LANCZOS)
    if spec.get("rot"):
        body = body.rotate(-spec["rot"], resample=Image.BICUBIC, expand=True)  # positivo = horario
        body = body.crop(opaque_box(body) or (0, 0, body.width, body.height))

    # Si ya no cabe en el lienzo (estiramientos), se reduce entero manteniendo las proporciones.
    fit = min(1.0, (w - 2) / body.width, (h - 2) / body.height)
    if fit < 1:
        body = body.resize((max(1, round(body.width * fit)), max(1, round(body.height * fit))), Image.LANCZOS)

    # Pies abajo y centrado como la pose original (+ desplazamientos de la receta).
    cx = (box[0] + box[2]) / 2 + spec.get("dx", 0)
    left = round(cx - body.width / 2)
    left = max(0, min(w - body.width, left))
    top = h - body.height + spec.get("dy", 0)
    canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    canvas.alpha_composite(body, (left, max(0, top)) if top >= 0 else (left, 0))
    if top > 0 and spec.get("dy", 0) > 0:
        # Asomándose (dentro de la caja): lo que baja del lienzo se recorta.
        canvas = canvas.crop((0, 0, w, h))
    return canvas


def final_frame(skin, clip_id, index, spec, size):
    path = os.path.join(FINAL, skin, f"{clip_id}_{index}.png")
    if os.path.exists(path):
        return Image.open(path).convert("RGBA").resize(size, Image.LANCZOS)
    # Arte final de las poses originales (sleep/walk/play/carry).
    return load_pose(skin, spec["base"], spec.get("frame", 0))


def main():
    with open(CLIPS, encoding="utf-8") as f:
        data = json.load(f)
    base_w, base_h = data["baseSize"]
    clips = data["clips"]
    total = sum(len(c["reference"]) for c in clips)
    rows = math.ceil(total / COLUMNS)

    os.makedirs(OUT, exist_ok=True)
    os.makedirs(REF, exist_ok=True)

    # 1) Cuadrículas: las editadas a mano se leen; el resto se generan desde el arte (lienzo 269x379).
    grids = {s: {} for s in SKINS}          # piel -> clip -> [imagen GRID_W x GRID_H]
    pending = {s: {} for s in SKINS}        # sin paleta todavía (se ajustan en el paso 2)
    for skin in SKINS:
        for clip in clips:
            hand = read_grid(skin, clip["id"])
            if hand is not None:
                grids[skin][clip["id"]] = hand
                continue
            imgs = []
            for i, spec in enumerate(clip["reference"]):
                if clip["status"] == "final":
                    img = final_frame(skin, clip["id"], i, spec, (base_w, base_h))
                else:
                    img = render_reference(skin, spec, (base_w, base_h))
                imgs.append(on_base(to_cells(img, round(img.width / ART_PX_PER_CELL),
                                             round(img.height / ART_PX_PER_CELL))))
            pending[skin][clip["id"]] = imgs

    # 2) Paleta estable por piel y escritura de las cuadrículas automáticas en letras.
    for skin in SKINS:
        palette = load_or_build_palette(skin, [g for gs in pending[skin].values() for g in gs])
        for clip in clips:
            if clip["id"] in pending[skin]:
                frames = [snap_to_palette(g, palette) for g in pending[skin][clip["id"]]]
                grids[skin][clip["id"]] = frames
                write_grid(skin, clip, frames, palette)

    # 3) Atlas: una celda por fotograma, a tamaño real (1 casilla = 1 px).
    sheets = {s: Image.new("RGBA", (GRID_W * COLUMNS, GRID_H * rows), (0, 0, 0, 0)) for s in SKINS}
    meta = {"cell": [GRID_W, GRID_H], "base": GRID_BASE, "main": [MAIN_X, 0, MAIN, MAIN],
            "columns": COLUMNS, "grid": True,
            "skins": {s: f"cats_{s}.png" for s in SKINS}, "clips": {}}
    cell = 0
    for clip in clips:
        entry = {"status": clip["status"], "frames": []}
        n = len(clip["reference"])
        for i in range(n):
            bounds = {}
            for skin in SKINS:
                g = grids[skin][clip["id"]][min(i, len(grids[skin][clip["id"]]) - 1)]
                col, row = cell % COLUMNS, cell // COLUMNS
                sheets[skin].alpha_composite(g, (col * GRID_W, row * GRID_H))
                b = g.getchannel("A").getbbox() or (0, 0, GRID_W, GRID_H)
                bounds[skin] = [round(b[0] / GRID_W, 4), round(b[1] / GRID_H, 4),
                                round((b[2] - b[0]) / GRID_W, 4), round((b[3] - b[1]) / GRID_H, 4)]
            entry["frames"].append({"cell": cell, "bounds": bounds})
            cell += 1
        meta["clips"][clip["id"]] = entry

    for skin, sheet in sheets.items():
        sheet.save(os.path.join(OUT, f"cats_{skin}.png"), optimize=True)
    with open(os.path.join(OUT, "atlas.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, ensure_ascii=False, indent=1)

    write_reference_sheets(clips, grids)
    build_box()
    write_pending(clips)
    pending = [c for c in clips if c["status"] != "final"]
    print(f"{len(clips)} clips, {total} fotogramas por piel; "
          f"{len(pending)} clips con referencia ({sum(len(c['reference']) for c in pending)} fotogramas pendientes)")


# ------------------------------------------------------------------ cuadrícula
def luminance(c):
    return 0.3 * c[0] + 0.59 * c[1] + 0.11 * c[2]


def cat_zones():
    """Zonas del cuadro del gato (x, y, ancho, alto): laterales y de abajo (azul), principal (verde)."""
    side = [(0, 0, SIDE, MAIN), (MAIN_X + MAIN, 0, SIDE, MAIN), (MAIN_X, MAIN, MAIN, BELOW)]
    return side, (MAIN_X, 0, MAIN, MAIN)


def box_zones():
    """Zonas de la caja: el margen es todo el cuadro (azul) y el principal va dentro (verde)."""
    return [(0, 0, BOX_W, BOX_H)], (BOX_SIDE, BOX_TOP, BOX_MAIN, BOX_MAIN)


def keep_inside(img, zones):
    """Borra lo que caiga fuera de la T (las esquinas del cuadro no son zona de dibujo)."""
    side, main = zones
    allowed = Image.new("L", img.size, 0)
    d = ImageDraw.Draw(allowed)
    for x, y, w, h in side + [main]:
        d.rectangle([x, y, x + w - 1, y + h - 1], fill=255)
    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    out.paste(img, mask=Image.composite(img.getchannel("A"), Image.new("L", img.size, 0), allowed))
    return out


def on_base(cells):
    """Coloca el dibujo (lienzo original, pies abajo) centrado sobre el principal, con los pies en la
    base. Si lo visible no cabe por encima de la base (p. ej. colgando del cursor), baja lo justo y
    cuelga en la zona de abajo en vez de cortarse."""
    out = Image.new("RGBA", (GRID_W, GRID_H), (0, 0, 0, 0))
    top = GRID_BASE - cells.height
    box = cells.getchannel("A").getbbox()
    if box and top + box[1] < 0:
        top = -box[1]
    if top < 0:   # el margen vacío de arriba del lienzo sobra
        cells = cells.crop((0, -top, cells.width, cells.height))
        top = 0
    out.alpha_composite(cells, (MAIN_X + (MAIN - cells.width) // 2, top))
    return keep_inside(out, cat_zones())


def to_cells(img, grid_w, grid_h):
    """Lienzo (269x379 los gatos) -> grid_w x grid_h casillas. Cada casilla toma el color mediano de su zona,
    pero si el contorno oscuro cubre al menos el 30% de ella, la casilla es contorno: así las líneas
    finas del dibujo no desaparecen al reducir (un promedio las convertiría en un borde borroso)."""
    px = img.load()
    out = Image.new("RGBA", (grid_w, grid_h), (0, 0, 0, 0))
    o = out.load()
    for j in range(grid_h):
        y0, y1 = round(j * img.height / grid_h), round((j + 1) * img.height / grid_h)
        for i in range(grid_w):
            x0, x1 = round(i * img.width / grid_w), round((i + 1) * img.width / grid_w)
            area = (x1 - x0) * (y1 - y0)
            solid = [px[x, y] for y in range(y0, y1) for x in range(x0, x1) if px[x, y][3] > 128]
            if len(solid) * 2.5 < area:
                continue
            dark = [c for c in solid if luminance(c) < INK]
            pick = dark if len(dark) >= len(solid) * 0.3 else ([c for c in solid if luminance(c) >= INK] or solid)
            mid = len(pick) // 2
            o[i, j] = (sorted(c[0] for c in pick)[mid], sorted(c[1] for c in pick)[mid],
                       sorted(c[2] for c in pick)[mid], 255)
    return out


def pixels(im):
    """Píxeles en una lista plana (getdata está obsoleto en Pillow reciente)."""
    return getattr(im, "get_flattened_data", im.getdata)()


def palette_path(skin):
    return os.path.join(GRID, skin, "paleta.txt")


def load_or_build_palette(skin, images):
    """Paleta de la piel (lista de colores RGB, índice = letra). Si ya existe se reutiliza tal cual,
    para que las letras de los archivos editados a mano sigan significando lo mismo."""
    path = palette_path(skin)
    if os.path.exists(path):
        colors = []
        with open(path, encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if line and not line.startswith("#"):
                    letter, hexcolor = line.split()
                    colors.append(tuple(int(hexcolor[k:k + 2], 16) for k in (1, 3, 5)))
        return colors
    # Colores más frecuentes primero; uno nuevo solo entra si se distingue de los ya elegidos
    # (si no, el contorno acabaría ocupando media paleta con negros casi idénticos).
    counts = Counter(c[:3] for im in images for c in pixels(im) if c[3])
    colors = []
    for c, _ in counts.most_common():
        if all(color_distance(c, p) > PALETTE_MIN_DIST for p in colors):
            colors.append(c)
            if len(colors) == PALETTE_SIZE:
                break
    colors.sort(key=luminance)                     # de oscuro a claro: más fácil de leer a mano
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        f.write(f"# Paleta de la piel '{skin}': letra -> color. '.' = transparente.\n"
                "# No cambies las letras existentes (los archivos editados a mano dependen de ellas);\n"
                "# para un color nuevo, añade una línea con la siguiente letra libre.\n")
        for k, c in enumerate(colors):
            f.write(f"{LETTERS[k]} #{c[0]:02X}{c[1]:02X}{c[2]:02X}\n")
    return colors


def color_distance(a, b):
    """Distancia perceptual aproximada (el verde pesa más que el azul)."""
    return ((a[0] - b[0]) ** 2 * 3 + (a[1] - b[1]) ** 2 * 4 + (a[2] - b[2]) ** 2 * 2) ** 0.5


def nearest(c, palette):
    return min(range(len(palette)), key=lambda k: color_distance(c, palette[k]))


def snap_to_palette(img, palette):
    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    src, o = img.load(), out.load()
    cache = {}
    for y in range(img.height):
        for x in range(img.width):
            c = src[x, y]
            if c[3]:
                k = cache.get(c[:3])
                if k is None:
                    k = cache[c[:3]] = nearest(c, palette)
                o[x, y] = (*palette[k], 255)
    return out


def grid_path(skin, clip_id):
    return os.path.join(GRID, skin, f"{clip_id}.txt")


def write_grid(skin, clip, frames, palette):
    index = {c: k for k, c in enumerate(palette)}
    gw, gh = frames[0].size
    lines = [f"# {clip['id']} · piel {skin} · {gw}x{gh} casillas · {len(frames)} fotogramas · "
             f"{clip['fps']} fps · {'bucle' if clip['loop'] else 'una vez'}",
             f"# {clip['description']}",
             "# fuente: auto",
             "# (Si editas este archivo, cambia la línea anterior a 'fuente: mano' para que no se regenere.)"]
    for i, g in enumerate(frames):
        lines.append(f"--- {i}")
        px = g.load()
        for y in range(gh):
            lines.append("".join(LETTERS[index[px[x, y][:3]]] if px[x, y][3] else "." for x in range(gw)))
    os.makedirs(os.path.join(GRID, skin), exist_ok=True)
    with open(grid_path(skin, clip["id"]), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")


def read_grid(skin, clip_id):
    """Cuadrículas editadas a mano ('fuente: mano'). Devuelve None si el archivo es automático o no existe."""
    path = grid_path(skin, clip_id)
    if not os.path.exists(path):
        return None
    with open(path, encoding="utf-8") as f:
        text = f.read().splitlines()
    if not any(l.startswith("#") and "fuente:" in l and "mano" in l.split("fuente:")[1] for l in text):
        return None
    palette = load_or_build_palette(skin, [])
    frames, rows = [], None
    for line in text + ["--- fin"]:
        if line.startswith("#"):
            continue
        if line.startswith("---"):
            if rows is not None:
                frames.append(rows)
            rows = []
        elif rows is not None and line.strip():
            rows.append(line.rstrip())
    imgs = []
    for n, rows in enumerate(frames):
        if len(rows) != GRID_H or any(len(r) != GRID_W for r in rows):
            raise SystemExit(f"{path}: el fotograma {n} debe medir {GRID_W}x{GRID_H} casillas")
        img = Image.new("RGBA", (GRID_W, GRID_H), (0, 0, 0, 0))
        px = img.load()
        for y, r in enumerate(rows):
            for x, ch in enumerate(r):
                if ch == ".":
                    continue
                k = LETTERS.find(ch)
                if k < 0 or k >= len(palette):
                    raise SystemExit(f"{path}: letra '{ch}' fuera de la paleta (fotograma {n}, fila {y})")
                px[x, y] = (*palette[k], 255)
        imgs.append(img)
    return imgs


def build_box():
    """La caja también es una cuadrícula (ver BOX_*): el cuerpo de la caja llena el principal de 32x32
    entero y las solapas de la abierta salen al margen. Los dos dibujos usan la misma correspondencia
    dibujo -> casillas, así el cuerpo de la abierta coincide con el de la cerrada. (El cuerpo dibujado
    no es cuadrado: se ajusta al convertirlo, como dibujar la caja cuadrada; en pantalla la cuadrícula
    se muestra siempre 1:1.) La app la tambalea con píxeles, dentro de este mismo cuadro."""
    x0, y0, x1, y1 = BOX_BODY
    sx, sy = (x1 - x0) / BOX_MAIN, (y1 - y0) / BOX_MAIN     # px del dibujo por casilla
    cells = {}
    for n in ("box_closed", "box_open"):
        art = Image.open(os.path.join(BOX_SRC, f"{n}.png")).convert("RGBA")
        # Recorte del dibujo que corresponde al cuadro entero (el margen puede salirse del dibujo).
        region = (x0 - BOX_SIDE * sx, y0 - BOX_TOP * sy, x1 + BOX_SIDE * sx, y1)
        canvas = Image.new("RGBA", (round(region[2] - region[0]), round(region[3] - region[1])), (0, 0, 0, 0))
        canvas.alpha_composite(art, (round(-region[0]), round(-region[1])))
        cells[n] = to_cells(canvas, BOX_W, BOX_H)
    palette = load_or_build_palette("box", list(cells.values()))
    os.makedirs(BOX_OUT, exist_ok=True)
    for name, img in cells.items():
        grid = snap_to_palette(img, palette)
        grid.save(os.path.join(BOX_OUT, f"{name}.png"), optimize=True)
        write_grid("box", {"id": name, "fps": 0, "loop": False,
                           "description": "Caja " + ("abierta" if "open" in name else "cerrada") + "."},
                   [grid], palette)


def font(size):
    for path in ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "C:/Windows/Fonts/segoeui.ttf"):
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def write_reference_sheets(clips, grids):
    """Una hoja por clip: las cuadrículas de las dos pieles ampliadas x5, numeradas, con la descripción."""
    zoom = 5
    fw, fh = GRID_W * zoom + 4, GRID_H * zoom
    title_font, small_font = font(18), font(13)
    for clip in clips:
        frames = {s: grids[s][clip["id"]] for s in SKINS}
        n = len(clip["reference"])
        tag = "ARTE FINAL" if clip["status"] == "final" else "REFERENCIA (pendiente de arte)"
        title = f"{clip['id']}  ·  {clip['fps']} fps  ·  {'bucle' if clip['loop'] else 'una vez'}  ·  {tag}"
        # El ancho se ajusta también al texto: con pocos fotogramas el título se cortaba.
        text_w = max(title_font.getlength(title), small_font.getlength(clip["description"]))
        sheet = Image.new("RGBA", (max(fw * n + 20, round(text_w) + 24), 70 + (fh + 26) * len(SKINS)), (48, 50, 56, 255))
        d = ImageDraw.Draw(sheet)
        d.text((10, 8), title, fill=(255, 214, 120), font=title_font)
        d.text((10, 34), clip["description"], fill=(225, 225, 225), font=small_font)
        for r, skin in enumerate(SKINS):
            y = 62 + r * (fh + 26)
            for i, img in enumerate(frames[skin]):
                x = 10 + i * fw
                d.rectangle([x, y, x + fw - 4, y + fh], outline=(90, 94, 102))
                big = img.resize((GRID_W * zoom, GRID_H * zoom), Image.NEAREST)
                draw_zones(big, cat_zones(), zoom, cells=True)
                sheet.alpha_composite(big, (x, y))
                d.text((x + 4, y + fh + 4), f"{skin} #{i}", fill=(170, 170, 170), font=small_font)
        sheet.convert("RGB").save(os.path.join(REF, f"{clip['id']}.png"), optimize=True)


ZONE_BLUE, ZONE_GREEN, ZONE_MAGENTA = (0, 150, 255, 255), (40, 200, 70, 255), (255, 60, 220, 255)
CELL_LINE = (128, 128, 128, 90)  # cuadrícula interior (gris translúcido: se ve sobre claro y oscuro)


def draw_zones(img, zones, zoom, cells=False):
    """Marcos del cuadro de dibujo, como en la app: zonas en azul, principal en verde, base en magenta.
    Con cells=True dibuja también la cuadrícula interior (una línea fina entre cada casilla)."""
    # Igual que ZoneOverlay en la app: un borde en la casilla B va en el píxel B*zoom, así las zonas
    # vecinas comparten una sola línea; el verde y el magenta quedan encima del azul.
    d = ImageDraw.Draw(img)
    col = lambda b: min(b * zoom, img.width - 1)
    row = lambda b: min(b * zoom, img.height - 1)
    if cells:
        grid = Image.new("RGBA", img.size, (0, 0, 0, 0))
        g = ImageDraw.Draw(grid)
        for b in range(img.width // zoom + 1):
            g.line([col(b), 0, col(b), img.height], fill=CELL_LINE)
        for b in range(img.height // zoom + 1):
            g.line([0, row(b), img.width, row(b)], fill=CELL_LINE)
        inside = Image.new("L", img.size, 0)          # solo dentro de las zonas de dibujo
        m = ImageDraw.Draw(inside)
        for x, y, w, h in zones[0] + [zones[1]]:
            m.rectangle([col(x), row(y), col(x + w), row(y + h)], fill=255)
        grid.putalpha(Image.composite(grid.getchannel("A"), Image.new("L", img.size, 0), inside))
        img.alpha_composite(grid)
    side, main = zones
    for x, y, w, h in side:
        d.rectangle([col(x), row(y), col(x + w), row(y + h)], outline=ZONE_BLUE)
    x, y, w, h = main
    d.rectangle([col(x), row(y), col(x + w), row(y + h)], outline=ZONE_GREEN)
    d.line([col(x), row(y + h), col(x + w), row(y + h)], fill=ZONE_MAGENTA)


def describe(spec):
    parts = [f"pose {spec['base']} #{spec.get('frame', 0)}"]
    if spec.get("sx", 1) != 1 or spec.get("sy", 1) != 1:
        parts.append(f"escala {spec.get('sx', 1)}×{spec.get('sy', 1)}")
    if spec.get("rot"):
        parts.append(f"giro {spec['rot']}°")
    if spec.get("dx") or spec.get("dy"):
        parts.append(f"desplazado {spec.get('dx', 0)},{spec.get('dy', 0)}")
    if spec.get("flip"):
        parts.append("espejado")
    return ", ".join(parts)


def write_pending(clips):
    pending = [c for c in clips if c["status"] != "final"]
    lines = [
        "# Arte pendiente",
        "",
        "Generado por `tools/art/build_atlas.py` desde `art/clips.json`. No editar a mano.",
        "",
        "Cada clip de esta lista ya funciona en la app con fotogramas de **referencia**, derivados",
        "de las poses dibujadas (dormir, caminar, jugar, cargado) con escalas, giros y desplazamientos.",
        "Son una guía de pose y de tiempos, no el arte final.",
        "",
        "**Cómo entregar el arte de un clip:**",
        "",
        "1. Dibuja cada fotograma en un lienzo de **269×379 px**, fondo transparente, con los pies",
        "   tocando el borde inferior y el gato centrado (igual que los dibujos actuales).",
        "2. Guárdalos como `art/final/<piel>/<clip>_<n>.png` (piel = `orange` y `gray`; n desde 0).",
        "3. En `art/clips.json` cambia el `status` del clip a `final`.",
        "4. Ejecuta `python3 tools/art/build_atlas.py`.",
        "",
        f"El script reduce cada fotograma a una **cuadrícula de {GRID_W}×{GRID_H} casillas** en T (1 casilla = 1 px",
        f"del gato en la app): principal de {MAIN}×{MAIN} con los pies sobre la base, {SIDE} columnas a cada lado",
        f"para correr o estirarse y {BELOW} filas debajo para lo que cuelga. La guarda en letras en `art/grid/<piel>/<clip>.txt` (paleta en",
        "`art/grid/<piel>/paleta.txt`). Esas cuadrículas también se pueden retocar a mano: cambia",
        "`fuente: auto` por `fuente: mano` en el archivo y ya no se regenerará.",
        "",
        "Los colores negro, blanco, crema y chocolate se generan solos a partir de la piel naranja:",
        "no hace falta dibujarlos. La hoja de referencia de cada clip está en `docs/art/reference/<clip>.png`.",
        "",
        f"**Total:** {len(pending)} clips, {sum(len(c['reference']) for c in pending)} fotogramas por piel "
        f"({2 * sum(len(c['reference']) for c in pending)} contando naranja y atigrado).",
        "",
        "| Clip | Fotogramas | Velocidad | Qué debe mostrar | Referencia actual |",
        "|---|---|---|---|---|",
    ]
    for c in pending:
        ref = "<br>".join(f"#{i}: {describe(s)}" for i, s in enumerate(c["reference"]))
        lines.append(f"| `{c['id']}` | {len(c['reference'])} | {c['fps']} fps, "
                     f"{'bucle' if c['loop'] else 'una vez'} | {c['description']} | {ref} |")
    done = [c for c in clips if c["status"] == "final"]
    lines += ["", "## Ya con arte final", ""]
    lines += [f"- `{c['id']}`: {c['description']}" for c in done]
    os.makedirs(os.path.dirname(os.path.join(REF, "..")), exist_ok=True)
    with open(os.path.join(ROOT, "docs", "art", "ARTE_PENDIENTE.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")


if __name__ == "__main__":
    sys.exit(main())
