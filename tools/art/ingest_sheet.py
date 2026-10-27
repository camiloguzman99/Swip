#!/usr/bin/env python3
"""
Convierte las hojas de poses del gato naranja (Gemini, fondo magenta, art/gemini/orange_sheet*.jpg) a
cuadrículas de casillas listas para el atlas (art/grid/orange/<clip>.txt).

Hojas (cada fila de la hoja es una acción de 8 o 10 fotogramas):
  r  orange_sheet.jpg                 poses básicas (sentado, de pie, caminar, correr, saltar...)
  g  orange_sheet_giros.jpg           sentado / de pie / acostado girando por 8 direcciones, caminar,
                                      lamerse la pata, saltos
  t  orange_sheet_transiciones.jpg    acostado<->sentado<->de pie, arrancar y parar, correr, caminar->correr
Un fotograma se nombra <hoja><fila>c<columna> (p. ej. t3c5).

Pasos:
  1. se quitan las líneas de la cuadrícula de la hoja y el fondo magenta;
  2. cada gato se separa por componentes (los pasos largos de correr cruzan los bordes de la celda);
  3. paleta común (k-medias) para todas las hojas;
  4. reducción a casillas con la MISMA escala entre hojas: cada hoja se mide con su pose sentada de
     perfil, que debe ocupar SIT_ROWS filas. Cada casilla toma el color más frecuente de su zona
     (el contorno pesa más para que las líneas finas no desaparezcan);
  5. colocación: los pies en la base del cuadro principal (suelo común de cada fila) y el centro de
     masas en el centro del principal;
  6. art/orange_map.json dice qué fotogramas forman cada clip (con desplazamiento/espejo).

Salidas: art/grid/orange/paleta.txt, art/grid/orange/<clip>.txt ("fuente: mano"), y una hoja de
contacto por hoja con cada fotograma identificado, si se indica una carpeta.
Uso: python3 tools/art/ingest_sheet.py [carpeta_de_contacto]
"""
import json
import os
import sys

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage as ndi

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_atlas as ba  # noqa: E402  (medidas del cuadro y escritura de cuadrículas)
import walk_cycle  # noqa: E402  (patas animadas a partir de un fotograma)

ROOT = ba.ROOT
GEMINI = os.path.join(ROOT, "art", "gemini")
MAP = os.path.join(ROOT, "art", "orange_map.json")
# clave, archivo, columnas, filas, fotograma de referencia de escala (pose sentada de perfil)
SHEETS = [
    ("r", "orange_sheet.jpg", 8, 7, "r1c1"),
    ("g", "orange_sheet_giros.jpg", 8, 7, "g1c3"),
    ("t", "orange_sheet_transiciones.jpg", 10, 8, "t1c10"),
]
SIT_ROWS = round(ba.MAIN * 0.92)   # la pose sentada de perfil (la más alta) llena el 92 % del principal
PALETTE_K = 22
PALETTE_MERGE = 26
OUTLINE_WEIGHT = 1.7
MIN_AREA = 400                     # píxeles de la hoja: menos que esto es una mota
BIG_AREA = 6000                    # y más que esto es un gato (o casi todo uno)


def is_magenta(a):
    return (np.minimum(a[..., 0], a[..., 2]) - a[..., 1]) > 60


def kmeans(points, k, seed=3, iters=25):
    rng = np.random.default_rng(seed)
    centers = points[rng.choice(len(points), k, replace=False)].astype(float)
    for _ in range(iters):
        lab = ((points[:, None, :] - centers[None]) ** 2).sum(-1).argmin(1)
        for j in range(k):
            sel = points[lab == j]
            if len(sel):
                centers[j] = sel.mean(0)
    return centers


def line_positions(mask_dark, axis):
    """Posiciones (centros) de las líneas negras de la cuadrícula: filas/columnas casi enteras oscuras."""
    frac = mask_dark.mean(axis=1 - axis)
    idx = np.where(frac > 0.3)[0]
    groups, cur = [], []
    for i in idx:
        if cur and i - cur[-1] > 2:
            groups.append(cur)
            cur = []
        cur.append(i)
    if cur:
        groups.append(cur)
    return [(g[0], g[-1]) for g in groups]


def strip_lines(a, fg):
    """Quita de la máscara las líneas de la cuadrícula; lo que cruza un gato (con gato a los lados) se queda."""
    H, W = fg.shape
    dark = a.sum(-1) < 150
    out = fg.copy()
    for lo, hi in line_positions(dark, 1):          # líneas verticales (columnas)
        left = fg[:, lo - 6] if lo - 6 >= 0 else np.zeros(H, bool)       # fuera de la hoja = fondo
        right = fg[:, hi + 6] if hi + 6 < W else np.zeros(H, bool)
        for x in range(max(0, lo - 1), min(W, hi + 2)):
            out[:, x] &= ~(dark[:, x] & ~left & ~right)
    for lo, hi in line_positions(dark, 0):          # líneas horizontales (filas)
        top = fg[lo - 6, :] if lo - 6 >= 0 else np.zeros(W, bool)
        bottom = fg[hi + 6, :] if hi + 6 < H else np.zeros(W, bool)
        for y in range(max(0, lo - 1), min(H, hi + 2)):
            out[y, :] &= ~(dark[y, :] & ~top & ~bottom)
    return out


class Sprite:
    def __init__(self, name, rgb, mask, ground):
        self.name, self.rgb, self.mask, self.ground = name, rgb, mask, ground   # ground: fila del pie (en rgb)


def extract(key, file, cols, rows):
    a = np.array(Image.open(os.path.join(GEMINI, file)).convert("RGB")).astype(float)
    H, W, _ = a.shape
    fg = strip_lines(a, ~is_magenta(a))
    fg = ndi.binary_opening(fg, iterations=1)
    lab, n = ndi.label(ndi.binary_dilation(fg, iterations=1))
    objs = ndi.find_objects(lab)
    areas = ndi.sum(fg, lab, index=range(1, n + 1))
    big = [i for i in range(n) if areas[i] >= BIG_AREA]
    group = {i: i for i in big}
    for i in range(n):                      # las motas (punta de la cola, una pata suelta) se unen al gato más cercano
        if i in group or areas[i] < MIN_AREA:
            continue
        sl = objs[i]
        cy, cx = (sl[0].start + sl[0].stop) / 2, (sl[1].start + sl[1].stop) / 2
        near = min(big, key=lambda j: max(0, objs[j][0].start - sl[0].stop, sl[0].start - objs[j][0].stop) ** 2
                   + max(0, objs[j][1].start - sl[1].stop, sl[1].start - objs[j][1].stop) ** 2)
        gap = max(max(0, objs[near][0].start - sl[0].stop, sl[0].start - objs[near][0].stop),
                  max(0, objs[near][1].start - sl[1].stop, sl[1].start - objs[near][1].stop))
        if gap < 30:
            group[i] = near
    members = {}
    for i, g in group.items():
        members.setdefault(g, []).append(i)
    comps = []
    for g, ids in members.items():
        y0 = min(objs[i][0].start for i in ids); y1 = max(objs[i][0].stop for i in ids)
        x0 = min(objs[i][1].start for i in ids); x1 = max(objs[i][1].stop for i in ids)
        sl = (slice(y0, y1), slice(x0, x1))
        mask = np.isin(lab[sl], [i + 1 for i in ids]) & fg[sl]
        comps.append((sl, mask))
    rowh = H / rows
    byrow = {r: [] for r in range(rows)}
    for sl, mask in comps:
        cy = (sl[0].start + sl[0].stop) / 2
        byrow[min(rows - 1, int(cy // rowh))].append((sl, mask))
    sprites = {}
    for r in range(rows):
        items = sorted(byrow[r], key=lambda t: (t[0][1].start + t[0][1].stop))
        if len(items) > cols:
            items = sorted(items, key=lambda t: -(t[0][0].stop - t[0][0].start) * (t[0][1].stop - t[0][1].start))[:cols]
            items = sorted(items, key=lambda t: (t[0][1].start + t[0][1].stop))
        print(f"  {key} fila {r + 1}: {len(items)} fotogramas")
        bottoms = [sl[0].stop for sl, _ in items]
        ground = float(np.percentile(bottoms, 75)) if bottoms else 0.0
        # Las celdas vacías del final (p. ej. fila 1 de la hoja r): se numera por columna real.
        for order, (sl, m) in enumerate(items):
            name = f"{key}{r + 1}c{order + 1}"
            sprites[name] = Sprite(name, a[sl], m, ground - sl[0].start)
    return sprites


def build_palette(sprites):
    px = np.concatenate([s.rgb[s.mask] for s in sprites])
    sample = px[np.random.default_rng(1).choice(len(px), min(len(px), 80000), replace=False)]
    out = []
    for c in sorted(kmeans(sample, PALETTE_K).tolist(), key=ba.luminance):
        if all(ba.color_distance(c, p) > PALETTE_MERGE for p in out):
            out.append(c)
    return [tuple(int(round(v)) for v in c) for c in out]


def torso_x(s):
    """Posición del cuerpo en el fotograma (px de la hoja): la punta de la cabeza (lo más adelantado de la mitad de
    arriba, mirando a la derecha). No depende de las patas: con el centro de masas, al adelantar una pata el cuerpo
    retrocedía y el movimiento de las patas se amortiguaba."""
    ys, xs = np.nonzero(s.mask)
    top = ys <= ys.min() + (ys.max() - ys.min()) * 0.55
    return float(xs[top].max())


def centroid_x(s):
    return float(np.nonzero(s.mask)[1].mean())


def to_grid(s, palette, px_per_cell, cx=None):
    """Fotograma de la hoja -> imagen RGBA del cuadro del gato (GRID_W x GRID_H). cx = columna de la hoja que va
    al centro del principal (por defecto, el centro de masas)."""
    pal = np.array(palette, float)
    idx = ((s.rgb[:, :, None, :] - pal[None, None]) ** 2).sum(-1).argmin(-1)
    idx[~s.mask] = -1
    h, w = idx.shape
    if cx is None:
        cx = centroid_x(s)
    out = Image.new("RGBA", (ba.GRID_W, ba.GRID_H), (0, 0, 0, 0))
    o = out.load()
    for j in range(ba.GRID_H):
        row_src = s.ground + (j + 1 - ba.GRID_BASE) * px_per_cell    # borde de abajo de la fila j
        y0, y1 = int(round(row_src - px_per_cell)), int(round(row_src))
        y0, y1 = max(0, y0), min(h, y1)
        if y1 <= y0:
            continue
        for i in range(ba.GRID_W):
            col_src = cx + (i - (ba.MAIN_X + ba.MAIN / 2)) * px_per_cell
            x0, x1 = int(round(col_src)), int(round(col_src + px_per_cell))
            x0, x1 = max(0, x0), min(w, x1)
            if x1 <= x0:
                continue
            block = idx[y0:y1, x0:x1].ravel()
            solid = block[block >= 0]
            if len(solid) * 2 < block.size:
                continue
            counts = np.bincount(solid, minlength=len(palette)).astype(float)
            counts[0] *= OUTLINE_WEIGHT
            o[i, j] = (*palette[int(counts.argmax())], 255)
    return out


def best_cycle(imgs):
    """Reordena los fotogramas (el primero queda fijo) para que dos consecutivos se parezcan lo más posible,
    incluido el último con el primero: minimiza la suma de (1 - IoU) de las siluetas."""
    import itertools
    m = [np.array(i.getchannel("A")) > 0 for i in imgs]
    def d(a, b):
        return 1 - (m[a] & m[b]).sum() / max(1, (m[a] | m[b]).sum())
    best = min(itertools.permutations(range(1, len(imgs))),
               key=lambda p: sum(d(a, b) for a, b in zip((0,) + p, p + (0,))))
    print("  mejor orden:", [0] + [i for i in best])
    return [imgs[i] for i in (0,) + best]


def main():
    sprites = {}
    for key, file, cols, rows, _ in SHEETS:
        sprites.update(extract(key, file, cols, rows))
    palette = build_palette(list(sprites.values()))
    skin_dir = os.path.join(ba.GRID, "orange")
    os.makedirs(skin_dir, exist_ok=True)
    with open(os.path.join(skin_dir, "paleta.txt"), "w", encoding="utf-8") as f:
        f.write("# Paleta de la piel 'orange' (hojas de Gemini): letra -> color. '.' = transparente.\n"
                "# No cambies las letras existentes (los archivos editados a mano dependen de ellas);\n"
                "# para un color nuevo, añade una línea con la siguiente letra libre.\n")
        for k, c in enumerate(palette):
            f.write(f"{ba.LETTERS[k]} #{c[0]:02X}{c[1]:02X}{c[2]:02X}\n")
    print(f"paleta: {len(palette)} colores")

    scale = {}
    for key, _, _, _, ref in SHEETS:
        rs = sprites[ref]
        ys = np.nonzero(rs.mask.any(1))[0]
        scale[key] = (ys.max() - ys.min() + 1) / SIT_ROWS
        print(f"  hoja {key}: {scale[key]:.2f} px de hoja por casilla (pose sentada {ys.max() - ys.min() + 1} px)")
    frames = {n: to_grid(s, palette, scale[n[0]]) for n, s in sorted(sprites.items())}
    # Clips que se desplazan: se alinean por el cuerpo (cabeza), no por el centro de masas, para que las patas se
    # muevan de verdad respecto al suelo. El conjunto del clip queda centrado como antes (misma media).
    ANCHOR_BY_TORSO = {"walk", "trot", "run", "walk_start_stop", "walk_to_run", "stand_to_run", "box_push",
                       "jump", "jump_high", "to_sit", "sit_to_stand"}

    with open(MAP, encoding="utf-8") as f:
        mapping = json.load(f)
    with open(ba.CLIPS, encoding="utf-8") as f:
        clips = {c["id"]: c for c in json.load(f)["clips"]}
    for clip_id, spec in mapping.items():
        out = []
        if "proc" in spec:                       # patas animadas a partir de un solo fotograma
            pr = spec["proc"]
            base = to_grid(sprites[pr["base"]], palette, scale[pr["base"][0]])
            n = pr.get("n", 8)
            out = [ba.keep_inside(g, ba.cat_zones())
                   for g in walk_cycle.cycle(base, n, pr["stride"], pr.get("phases"))]
            names = []
        else:
            names = [r if isinstance(r, str) else r["f"] for r in spec["frames"]]
        offset = None
        if names and clip_id in ANCHOR_BY_TORSO:
            offset = float(np.mean([centroid_x(sprites[n]) - torso_x(sprites[n]) for n in names]))
        for ref in (spec["frames"] if names else []):
            name, opts = (ref, {}) if isinstance(ref, str) else (ref["f"], ref)
            img = frames[name] if offset is None else to_grid(
                sprites[name], palette, scale[name[0]], torso_x(sprites[name]) + offset)
            if opts.get("flip"):
                img = img.transpose(Image.FLIP_LEFT_RIGHT)
            dx, dy = opts.get("dx", 0), opts.get("dy", 0)
            if dx or dy:
                moved = Image.new("RGBA", img.size, (0, 0, 0, 0))
                moved.paste(img, (dx, dy), img)
                img = moved
            out.append(ba.keep_inside(img, ba.cat_zones()))
        if spec.get("reorder"):                  # el orden de la hoja no es un ciclo suave: se busca el mejor
            out = best_cycle(out)
        if len(out) != len(clips[clip_id]["reference"]):
            raise SystemExit(f"{clip_id}: {len(out)} fotogramas en el mapa, {len(clips[clip_id]['reference'])} en clips.json")
        ba.write_grid("orange", clips[clip_id], out, palette, source="mano", note=spec.get("note"))
    print(f"{len(mapping)} clips escritos")

    if len(sys.argv) > 1:
        os.makedirs(sys.argv[1], exist_ok=True)
        z = 2
        cellw, cellh = ba.GRID_W * z, ba.GRID_H * z
        for key, _, cols, rows, _ in SHEETS:
            img = Image.new("RGBA", (cols * cellw, rows * cellh), (60, 64, 72, 255))
            d = ImageDraw.Draw(img)
            for n, g in frames.items():
                if n[0] != key:
                    continue
                r, c = int(n[1:n.index("c")]) - 1, int(n[n.index("c") + 1:]) - 1
                img.alpha_composite(g.resize((cellw, cellh), Image.NEAREST), (c * cellw, r * cellh))
                d.rectangle([c * cellw, r * cellh, (c + 1) * cellw - 1, (r + 1) * cellh - 1], outline=(110, 114, 124))
                x = ba.MAIN_X * z
                d.rectangle([c * cellw + x, r * cellh, c * cellw + x + ba.MAIN * z, r * cellh + ba.MAIN * z],
                            outline=(40, 200, 70))
                d.text((c * cellw + 4, r * cellh + 3), n, fill=(255, 255, 255, 255))
            img.save(os.path.join(sys.argv[1], f"contacto_{key}.png"))


if __name__ == "__main__":
    main()
