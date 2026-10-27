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
import os
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ATLAS = os.path.join(ROOT, "src", "Swip.App", "Assets", "atlas")
BOX = os.path.join(ROOT, "src", "Swip.App", "Assets", "box")
CAT_H, CAT_W, BOX_H, BOX_W = 48, 34, 36, 50
SCALE = 2  # dibujar al doble para que se vea
SKIN_OF = {"activo": "orange", "fondo": "gray", "dormido": "orange"}

meta = json.load(open(os.path.join(ATLAS, "atlas.json"), encoding="utf-8"))
sheets = {s: Image.open(os.path.join(ATLAS, f)).convert("RGBA") for s, f in meta["skins"].items()}
cw, ch = meta["cell"]
box_img = {o: Image.open(os.path.join(BOX, f"box_{'open' if o else 'closed'}.png")).convert("RGBA") for o in (False, True)}


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
    img = cat_frame(skin, c["clip"], c["frame"]).resize((CAT_W * SCALE, CAT_H * SCALE), Image.NEAREST)
    if not c["facingRight"]:
        img = img.transpose(Image.FLIP_LEFT_RIGHT)
    cx = (round(c["X"] + c["ox"]) + CAT_W / 2) * SCALE
    bottom = (round(c["Y"] + c["oy"]) + CAT_H) * SCALE
    canvas.alpha_composite(img, (round(cx - img.width / 2), round(bottom - img.height)))


def draw_shadow(d, c):
    if c["shadow"] < 0:
        return
    cx = (c["X"] + CAT_W / 2) * SCALE
    y = (c["Y"] + CAT_H + c["oy"] + c["shadow"]) * SCALE
    k = 1 / (1 + c["shadow"] / 40)
    rx, ry = 14 * SCALE * k, 3 * SCALE * k
    d.ellipse([cx - rx, y - ry, cx + rx, y + ry], fill=(0, 0, 0, round(70 * k)))


def render(snapshot, width, height):
    W, H = round(width * SCALE), round(height * SCALE)
    canvas = Image.new("RGBA", (W, H), (58, 62, 70, 255))
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.line([0, H - 1, W, H - 1], fill=(120, 124, 132), width=2)

    box = snapshot["box"]
    bimg = box_img[box["Open"]].resize((BOX_W * SCALE, BOX_H * SCALE), Image.NEAREST)  # cuadrícula: casillas cuadradas
    if abs(box["Wobble"]) > 0.05:
        bimg = bimg.rotate(-box["Wobble"], resample=Image.BICUBIC, expand=True)
    bx, by = box["X"] * SCALE, box["Y"] * SCALE

    cats = snapshot["cats"]
    for c in cats:
        draw_shadow(d, c)
    canvas.alpha_composite(layer)
    for c in [c for c in cats if c["behind"]]:
        draw_cat(canvas, c, SKIN_OF.get(c["Id"], "orange"))
    canvas.alpha_composite(bimg, (round(bx + BOX_W * SCALE / 2 - bimg.width / 2), round(by + BOX_H * SCALE - bimg.height)))
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
