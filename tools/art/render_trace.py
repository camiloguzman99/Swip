#!/usr/bin/env python3
"""
Dibuja una simulación del motor (salida de tools/SimTrace) con el atlas real, para revisar a ojo
los comportamientos sin abrir la app.

Uso:  python3 tools/art/render_trace.py <trace.json> <salida.png> [segundos separados por comas]
      python3 tools/art/render_trace.py <trace.json> <salida.gif> --gif <desde> <hasta>
Dibuja las mismas transformaciones que la app: espejo, aplastar/estirar y giro con los pies como
pivote, desplazamientos, sombra, gato detrás de la caja, tambaleo de la caja y partículas.
"""
import json
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ATLAS = os.path.join(ROOT, "src", "Swip.App", "Assets", "atlas")
BOX = os.path.join(ROOT, "src", "Swip.App", "Assets", "box")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_atlas as B   # mismas zonas y marcos que el generador y la app

BOX_MAIN, BOX_SIDE, BOX_TOP = B.BOX_MAIN, B.BOX_SIDE, B.BOX_TOP
BELOW = B.BELOW      # filas del cuadro por debajo de la base (sobre la barra de tareas)
SCALE = 2  # dibujar al doble para que se vea
SKIN_OF = {"activo": "orange", "fondo": "gray", "dormido": "orange"}

meta = json.load(open(os.path.join(ATLAS, "atlas.json"), encoding="utf-8"))
sheets = {s: Image.open(os.path.join(ATLAS, f)).convert("RGBA") for s, f in meta["skins"].items()}
cw, ch = meta["cell"]
MAIN_X, MAIN_Y, CAT_W, CAT_H = meta["main"]   # para el motor el gato es su cuadro principal
box_img = {f[4:-4]: Image.open(os.path.join(BOX, f)).convert("RGBA")
           for f in os.listdir(BOX) if f.startswith("box_") and f.endswith(".png")}


def box_frame(lid, squash):
    """Igual que BoxSprites.FrameName en la app."""
    return f"lid_{lid}" if lid > 0 else f"squash_{min(squash, 4)}" if squash > 0 else "lid_0"


MAX_TILT = 6


def tilt_box(img, degrees, pivot_of=None):
    """Igual que BoxSprites.Get en la app: la caja gira DENTRO de su cuadro (cada casilla toma el color
    que le toca) sobre la esquina de su base que queda apoyada (la de la capa delantera: pivot_of)."""
    deg = round(max(-MAX_TILT, min(MAX_TILT, degrees)))
    w, h = img.size
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    src, o = img.load(), out.load()
    a = math.radians(deg)
    cos, sin = math.cos(a), math.sin(a)
    # Pivote: la esquina de la fila visible más baja (la base del dibujo, no las solapas).
    alpha = (pivot_of or img).getchannel("A").load()
    bottom = max(y for y in range(h) for x in range(w) if alpha[x, y])
    row = [x for x in range(w) if alpha[x, bottom]]
    px, py = (row[-1] + 1 if deg >= 0 else row[0]), bottom + 1
    for y in range(h):
        for x in range(w):
            dx, dy = x + 0.5 - px, y + 0.5 - py
            sx, sy = dx * cos + dy * sin + px, -dx * sin + dy * cos + py
            ix, iy = math.floor(sx), math.floor(sy)
            if 0 <= ix < w and 0 <= iy < h:
                o[x, y] = src[ix, iy]
    return out


def font(size):
    p = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
    return ImageFont.truetype(p, size) if os.path.exists(p) else ImageFont.load_default()


F_SMALL, F_FX = font(11), font(15)
GLYPH = {"Zzz": "z", "Heart": "♥", "Exclaim": "!", "Sparkle": "✦", "Note": "♪", "Scratch": "/"}
COLOR = {"Zzz": (210, 220, 255), "Heart": (255, 110, 138), "Exclaim": (255, 214, 80),
         "Sparkle": (255, 240, 160), "Note": (180, 230, 255), "Scratch": (120, 80, 50),
         "Dust": (205, 195, 180), "Fly": (30, 30, 30)}


def cat_frame(skin, clip, frame):
    cell = meta["clips"][clip]["frames"][frame]["cell"]
    col, row = cell % meta["columns"], cell // meta["columns"]
    return sheets[skin].crop((col * cw, row * ch, col * cw + cw, row * ch + ch))


def draw_cat(canvas, c, skin):
    # El dibujo no se deforma: cuadrícula ampliada píxel a píxel, espejo y desplazamiento en casillas enteras.
    img = cat_frame(skin, c["clip"], c["frame"]).resize((cw * SCALE, ch * SCALE), Image.NEAREST)
    if not c["facingRight"]:
        img = img.transpose(Image.FLIP_LEFT_RIGHT)
    B.draw_zones(img, B.cat_zones(), SCALE)   # marcos, como en la app
    left = (round(c["X"] + c["ox"]) - MAIN_X) * SCALE
    top = (round(c["Y"] + c["oy"]) - MAIN_Y) * SCALE
    canvas.alpha_composite(img, (round(left), round(top)))


def draw_shadow(d, c):
    if c["shadow"] < 0:
        return
    cx = (c["X"] + CAT_W / 2) * SCALE
    y = (c["Y"] + CAT_H + c["oy"] + c["shadow"]) * SCALE
    k = 1 / (1 + c["shadow"] / 40)
    rx, ry = 14 * SCALE * k, 3 * SCALE * k
    d.ellipse([cx - rx, y - ry, cx + rx, y + ry], fill=(0, 0, 0, round(70 * k)))


def render(snapshot, width, height):
    W, H = round(width * SCALE), round((height + BELOW) * SCALE)
    canvas = Image.new("RGBA", (W, H), (58, 62, 70, 255))
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    floor = round(height * SCALE)
    d.rectangle([0, floor, W, H], fill=(32, 34, 38, 255))   # la barra de tareas, debajo del suelo

    box = snapshot["box"]
    name = box_frame(box.get("Lid", 4 if box["Open"] else 0), box.get("Squash", 0))
    front = tilt_box(box_img[name], box["Wobble"])            # tambaleo con píxeles, como en la app
    back = tilt_box(box_img[name + "_back"], box["Wobble"], pivot_of=box_img[name])
    bimg = front.resize((front.width * SCALE, front.height * SCALE), Image.NEAREST)
    bback = back.resize((back.width * SCALE, back.height * SCALE), Image.NEAREST)
    B.draw_zones(bimg, B.box_zones(), SCALE)
    bx, by = (box["X"] - BOX_SIDE) * SCALE, (box["Y"] - BOX_TOP) * SCALE

    cats = snapshot["cats"]
    for c in cats:
        draw_shadow(d, c)
    canvas.alpha_composite(layer)
    canvas.alpha_composite(bback, (round(bx), round(by)))   # interior: detrás del gato que está dentro
    for c in [c for c in cats if c["behind"]]:
        draw_cat(canvas, c, SKIN_OF.get(c["Id"], "orange"))
    canvas.alpha_composite(bimg, (round(bx), round(by)))
    for c in [c for c in cats if not c["behind"]]:
        draw_cat(canvas, c, SKIN_OF.get(c["Id"], "orange"))

    fx = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    fd = ImageDraw.Draw(fx)
    for p in snapshot["particles"]:
        a = round(255 * p["Alpha"])
        x, y = p["X"] * SCALE, p["Y"] * SCALE
        col = COLOR.get(p["kind"], (255, 255, 255)) + (a,)
        if p["kind"] in ("Dust", "Fly"):
            r = p["Size"] * SCALE / 2
            fd.ellipse([x - r, y - r, x + r, y + r], fill=col)
        else:
            fd.text((x, y), GLYPH.get(p["kind"], "*"), fill=col, font=F_FX, anchor="mm")
    for c in cats:
        fd.text(((c["X"] + CAT_W / 2) * SCALE, (c["Y"] - 8) * SCALE),
                f"{c['Id']}: {c['behavior']}", fill=(255, 214, 120, 255), font=F_SMALL, anchor="mb")
    fd.text((6, 4), f"t = {snapshot['t']:.1f} s", fill=(230, 230, 230, 255), font=F_SMALL)
    canvas.alpha_composite(fx)
    return canvas


def main():
    trace = json.load(open(sys.argv[1], encoding="utf-8"))
    out = sys.argv[2]
    frames = trace["frames"]
    if "--gif" in sys.argv:
        i = sys.argv.index("--gif")
        a, b = float(sys.argv[i + 1]), float(sys.argv[i + 2])
        imgs = [render(f, trace["width"], trace["height"]).convert("P", palette=Image.ADAPTIVE)
                for f in frames if a <= f["t"] <= b]
        imgs[0].save(out, save_all=True, append_images=imgs[1:], duration=100, loop=0)
        return
    times = [float(t) for t in sys.argv[3].split(",")] if len(sys.argv) > 3 else [10, 40, 70, 100]
    picks = [min(frames, key=lambda f: abs(f["t"] - t)) for t in times]
    rendered = [render(f, trace["width"], trace["height"]) for f in picks]
    sheet = Image.new("RGBA", (rendered[0].width, sum(r.height for r in rendered) + 4 * len(rendered)), (20, 20, 24, 255))
    y = 0
    for r in rendered:
        sheet.alpha_composite(r, (0, y))
        y += r.height + 4
    sheet.convert("RGB").save(out)


if __name__ == "__main__":
    main()
