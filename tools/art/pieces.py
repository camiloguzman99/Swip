#!/usr/bin/env python3
"""
Gato por piezas: recorta los dibujos originales en piezas (cola, cuerpo, cabeza, 4 patas) a su
resolución REAL de píxel y arma animaciones moviendo las piezas píxel a píxel (sin escalar ni
girar a ángulos arbitrarios), así cada fotograma conserva el dibujo original.

1. Limpieza: los dibujos (269x379) son pixel art ampliado ~x4.5 con bordes suavizados. Se toma la
   mediana del interior de cada bloque -> sprite limpio a resolución real (~48x61 caminando).
2. Piezas: un mapa de etiquetas por coordenadas decide a qué pieza pertenece cada píxel.
   Bajo la cabeza se rellena el cuerpo para que al moverla no aparezca un hueco.
3. Ciclo de caminar: patas en pares diagonales que se inclinan (desplazando filas) y se acortan
   al levantarse; el cuerpo baja 1 px en el apoyo; la cabeza le sigue con 1 fotograma de retraso;
   la cola se balancea.

Salidas:
  art/pieces/<piel>/<pieza>.png        piezas a resolución real (en su posición del lienzo)
  art/pieces/<piel>/rig.json           cajas y puntos de unión de cada pieza
  art/pieces/<piel>/walk_<n>.png       fotogramas del ciclo a resolución real
  art/final/<piel>/walk_<n>.png        los mismos, ampliados al lienzo 269x379 (para build_atlas)
  docs/art/pieces/<piel>_*.png|gif     hojas de revisión
Uso: python3 tools/art/pieces.py
"""
import json
import math
import os

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
POSES = os.path.join(ROOT, "art", "poses")
PIECES = os.path.join(ROOT, "art", "pieces")
FINAL = os.path.join(ROOT, "art", "final")
DOCS = os.path.join(ROOT, "docs", "art", "pieces")
BASE = (269, 379)

# Rejilla de cada dibujo: tamaño de bloque y desplazamiento (ajustados midiendo el error de reconstrucción).
SOURCES = {
    "orange": dict(file="cat_orange_walk_0.png", block=4.55, ox=3, oy=2),
    "gray": dict(file="cat_gray_walk_0.png", block=4.5, ox=2, oy=1),
}

# Orden de dibujo: de atrás hacia delante (patas lejanas detrás del cuerpo, cercanas delante).
ORDER = ["leg_bf", "leg_ff", "tail", "body", "head", "leg_bn", "leg_fn"]
LEGS = ["leg_bn", "leg_bf", "leg_ff", "leg_fn"]   # b = trasera, f = delantera; n = cercana, f = lejana


def label_orange(x, y):
    """Mapa de piezas en coordenadas del sprite limpio y recortado (orange walk_0, 48x61)."""
    if y <= 23 and x <= 11:
        return "tail"
    if y >= 48 and x <= 10:
        return "leg_bn"
    if y >= 51 and 11 <= x <= 19:
        return "leg_bf"
    if y >= 52 and 20 <= x <= 30:
        return "leg_ff"
    if y >= 51 and x >= 31:
        return "leg_fn"
    if x >= 25 and y <= 45:      # cabeza hasta el mentón; el pecho queda en el cuerpo (de él salen las patas)
        return "head"
    return "body"


def label_gray(x, y):
    """Mapa de piezas del atigrado gris (walk_0, 53x64): es otro dibujo, más alto y con la cola curvada."""
    if y <= 24 and x <= 12:
        return "tail"
    if y >= 48 and x <= 12:
        return "leg_bn"
    if y >= 49 and 13 <= x <= 24:
        return "leg_bf"
    if y >= 50 and 25 <= x <= 32:
        return "leg_ff"
    if y >= 49 and x >= 33:
        return "leg_fn"
    if x >= 30 and y <= 40:
        return "head"
    return "body"


LABELS = {"orange": label_orange, "gray": label_gray}


# ------------------------------------------------------------------ limpieza
def median(vals):
    vals = sorted(vals)
    return vals[len(vals) // 2]


def clean(path, s, ox, oy):
    im = Image.open(path).convert("RGBA")
    p = im.load()
    W, H = int((im.width - ox) // s), int((im.height - oy) // s)
    out = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    o = out.load()
    for j in range(H):
        for i in range(W):
            x0, y0 = ox + i * s, oy + j * s
            xs = range(int(x0 + s * .2), int(x0 + s * .8) + 1)
            ys = range(int(y0 + s * .2), int(y0 + s * .8) + 1)
            cell = [p[x, y] for y in ys for x in xs if x < im.width and y < im.height]
            solid = [c for c in cell if c[3] > 128]
            if len(solid) * 2 < len(cell):
                continue
            o[i, j] = (median([c[0] for c in solid]), median([c[1] for c in solid]),
                       median([c[2] for c in solid]), 255)
    box = out.getchannel("A").getbbox()
    return out.crop(box), box, (W, H)


# ------------------------------------------------------------------ piezas
def split(sprite, label):
    w, h = sprite.size
    src = sprite.load()
    layers = {n: Image.new("RGBA", (w, h), (0, 0, 0, 0)) for n in ORDER}
    px = {n: layers[n].load() for n in ORDER}
    for y in range(h):
        for x in range(w):
            if src[x, y][3]:
                px[label(x, y)][x, y] = src[x, y]
    fill_under(layers["body"], layers["head"])
    return layers


def fill_under(body, head, depth=2):
    """Rellena con el color del cuerpo una franja bajo el borde izquierdo/inferior de la cabeza,
    para que al subirla o moverla 1 px no se vea el fondo."""
    b, hd = body.load(), head.load()
    w, h = body.size
    for _ in range(depth):
        add = []
        for y in range(h):
            for x in range(w):
                if hd[x, y][3] and not b[x, y][3]:
                    for dx, dy in ((-1, 0), (0, -1), (0, 1), (1, 0)):
                        nx, ny = x + dx, y + dy
                        if 0 <= nx < w and 0 <= ny < h and b[nx, ny][3] and b[nx, ny][:3] != (0, 0, 0):
                            add.append((x, y, b[nx, ny]))
                            break
        for x, y, c in add:
            # solo píxeles de interior: el contorno (casi negro) no se extiende
            if sum(c[:3]) > 120:
                b[x, y] = c


def bbox(img):
    return img.getchannel("A").getbbox()


def rows_of(img):
    x0, y0, x1, y1 = bbox(img)
    return [img.crop((x0, y, x1, y + 1)) for y in range(y0, y1)], (x0, y0)


def leg(img, shear=0, shorten=0):
    """Pata deformada píxel a píxel: quita `shorten` filas del medio (rodilla doblada / apoyo) e
    inclina desplazando cada fila (la fila de arriba fija, el pie se mueve `shear` px)."""
    rows, (x0, y0) = rows_of(img)
    for _ in range(shorten):
        if len(rows) > 3:
            rows.pop(len(rows) // 2)
    n = len(rows)
    out = []
    for i, r in enumerate(rows):
        dx = round(shear * i / max(1, n - 1))
        out.append((r, x0 + dx, y0 + i))
    return out


def tail(img, sway=0):
    """Cola vertical: la base fija y la punta se desplaza `sway` px (desplazando filas)."""
    rows, (x0, y0) = rows_of(img)
    n = len(rows)
    return [(r, x0 + round(sway * (n - 1 - i) / max(1, n - 1)), y0 + i) for i, r in enumerate(rows)]


# ------------------------------------------------------------------ ciclo de caminar
WALK_FRAMES = 8
STRIDE = 3   # px reales que avanza/retrocede cada pie respecto a su posición dibujada


def walk_params(f):
    phi = 2 * math.pi * f / WALK_FRAMES
    pose = {}
    for name, phase in (("leg_bn", 0), ("leg_ff", 0), ("leg_bf", math.pi), ("leg_fn", math.pi)):
        a = phi + phase
        swing = -math.sin(a)                 # > 0: la pata va hacia delante (en el aire)
        pose[name] = dict(shear=round(STRIDE * math.cos(a)), lift=1 if swing > 0.5 else 0)
    bob = 1 if abs(math.cos(phi)) > 0.9 else 0          # apoyo con las patas abiertas: cuerpo abajo
    prev = 2 * math.pi * (f - 1) / WALK_FRAMES
    head_bob = 1 if abs(math.cos(prev)) > 0.9 else 0     # la cabeza llega 1 fotograma tarde
    pose["bob"], pose["head_bob"] = bob, head_bob
    pose["tail"] = round(math.sin(phi))
    return pose


def compose(layers, pose, size):
    w, h = size
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    bob = pose["bob"]
    for name in ORDER:
        img = layers[name]
        if bbox(img) is None:
            continue
        if name in LEGS:
            p = pose[name]
            # El tope de la pata baja con el cuerpo, el pie no: se acorta lo que baja el cuerpo.
            for r, x, y in leg(img, p["shear"], p["lift"] + bob):
                out.alpha_composite(r, (x, y + bob))
            # Pies en el suelo: realinear la base de la pata a su altura original (menos lo levantado).
        elif name == "tail":
            for r, x, y in tail(img, pose["tail"]):
                out.alpha_composite(r, (x, y + bob))
        elif name == "head":
            out.alpha_composite(img, (0, pose["head_bob"]))
        else:
            out.alpha_composite(img, (0, bob))
    return out


# ------------------------------------------------------------------ salida
def to_canvas(sprite, box, grid, s, ox, oy):
    """Devuelve el fotograma al lienzo original (269x379), con píxeles nítidos y los pies abajo."""
    big = sprite.resize((round(sprite.width * s), round(sprite.height * s)), Image.NEAREST)
    canvas = Image.new("RGBA", BASE, (0, 0, 0, 0))
    x = round(ox + box[0] * s)
    canvas.alpha_composite(big, (max(0, min(BASE[0] - big.width, x)), BASE[1] - big.height))
    return canvas


def review(frames, layers, skin):
    os.makedirs(DOCS, exist_ok=True)
    z = 6
    w, h = frames[0].size
    bg = (214, 218, 224, 255)
    # Hoja de piezas
    names = ORDER
    sheet = Image.new("RGBA", ((w * z + 12) * len(names) + 12, h * z + 24), bg)
    for i, n in enumerate(names):
        sheet.alpha_composite(layers[n].resize((w * z, h * z), Image.NEAREST), (12 + i * (w * z + 12), 12))
    sheet.save(os.path.join(DOCS, f"{skin}_piezas.png"))
    # Hoja del ciclo
    cyc = Image.new("RGBA", ((w * z + 8) * len(frames) + 8, h * z + 16), bg)
    for i, fr in enumerate(frames):
        cyc.alpha_composite(fr.resize((w * z, h * z), Image.NEAREST), (8 + i * (w * z + 8), 8))
    cyc.save(os.path.join(DOCS, f"{skin}_caminar.png"))
    # GIF caminando por el suelo (avanza 1 px real por fotograma, como el apoyo de las patas)
    gif = []
    lane = w + 40
    for k in range(WALK_FRAMES * 3):
        g = Image.new("RGBA", (lane * z // 2, (h + 4) * z // 2), bg)
        fr = frames[k % WALK_FRAMES].resize((w * z // 2, h * z // 2), Image.NEAREST)
        g.alpha_composite(fr, ((k % (lane - w)) * z // 2, 0))
        gif.append(g.convert("RGB"))
    gif[0].save(os.path.join(DOCS, f"{skin}_caminar.gif"), save_all=True, append_images=gif[1:], duration=110, loop=0)


def main():
    for skin, src in SOURCES.items():
        sprite, box, grid = clean(os.path.join(POSES, src["file"]), src["block"], src["ox"], src["oy"])
        layers = split(sprite, LABELS[skin])
        out_dir = os.path.join(PIECES, skin)
        os.makedirs(out_dir, exist_ok=True)
        rig = {"source": src, "size": list(sprite.size), "order": ORDER, "pieces": {}}
        for n, img in layers.items():
            img.save(os.path.join(out_dir, f"{n}.png"))
            rig["pieces"][n] = {"box": list(bbox(img))}
        with open(os.path.join(out_dir, "rig.json"), "w", encoding="utf-8") as f:
            json.dump(rig, f, indent=1)
        frames = [compose(layers, walk_params(i), sprite.size) for i in range(WALK_FRAMES)]
        os.makedirs(os.path.join(FINAL, skin), exist_ok=True)
        for i, fr in enumerate(frames):
            fr.save(os.path.join(out_dir, f"walk_{i}.png"))
            to_canvas(fr, box, grid, src["block"], src["ox"], src["oy"]).save(os.path.join(FINAL, skin, f"walk_{i}.png"))
        review(frames, layers, skin)
        print(skin, "piezas:", ", ".join(ORDER), "| caminar:", len(frames), "fotogramas", sprite.size)


if __name__ == "__main__":
    main()
