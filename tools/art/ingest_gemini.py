#!/usr/bin/env python3
"""
Importa el arte generado con Gemini (docs/art/PROMPTS_GEMINI.md) y lo deja listo para el atlas.

Por cada imagen de art/gemini/<piel>/<accion>.(png|jpg) descrita en art/gemini/manifest.json:
  1. quita el fondo magenta y el halo rosado que deja el JPG en los bordes;
  2. limpia el ruido del JPG ajustando los colores a una paleta común de la piel;
  3. corta la cuadrícula en fotogramas y descarta motas sueltas;
  4. iguala la escala entre acciones usando el ancho de la cabeza (Gemini no mantiene
     la misma densidad de píxel de una imagen a otra);
  5. alinea los fotogramas de cada acción: mismos pies (línea de suelo) y mismo centro.

Salidas:
  art/gemini_clean/<piel>/<accion>_<n>.png   fotogramas limpios a escala común (cabeza = HEAD px)
  docs/art/gemini/<piel>_hoja.png            todas las acciones en fila, misma escala y suelo
  docs/art/gemini/<piel>_app.png             cómo se ven al tamaño real de la app
  docs/art/gemini/<piel>_<accion>.gif        animación de cada acción
Uso: python3 tools/art/ingest_gemini.py
"""
import json
import os

from PIL import Image, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "art", "gemini")
OUT = os.path.join(ROOT, "art", "gemini_clean")
DOCS = os.path.join(ROOT, "docs", "art", "gemini")
HEAD = 100            # ancho de cabeza común (px) en los fotogramas limpios
PALETTE = 40          # colores por piel tras limpiar el JPG
APP_STAND_H = 40      # alto en pantalla del gato de pie en la app (px), para la vista previa
FPS = {"sleep_curl": 1.5, "sleep_stretch": 3}


def magenta(c):
    """Cuánto se parece a magenta: alto en el fondo, ~0 o negativo en el gato (blanco, rosa, naranja)."""
    return min(c[0], c[2]) - c[1]


def key_out(img):
    """RGB -> RGBA sin fondo; los píxeles del borde contaminados de magenta toman el color del vecino limpio."""
    rgb = img.convert("RGB")
    w, h = rgb.size
    p = rgb.load()
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    o = out.load()
    spill = []
    for y in range(h):
        for x in range(w):
            c = p[x, y]
            m = magenta(c)
            if m > 90:
                continue
            o[x, y] = (*c, 255)
            if m > 18:
                spill.append((x, y))
    for _ in range(3):                    # despill: sustituir por vecinos limpios
        rest = []
        for x, y in spill:
            near = [o[x + dx, y + dy] for dx in (-2, -1, 0, 1, 2) for dy in (-2, -1, 0, 1, 2)
                    if 0 <= x + dx < w and 0 <= y + dy < h and o[x + dx, y + dy][3]
                    and magenta(o[x + dx, y + dy]) <= 18]
            if near:
                near.sort(key=lambda c: sum(c[:3]))
                o[x, y] = near[len(near) // 2]
            else:
                rest.append((x, y))
        spill = rest
    for x, y in spill:
        o[x, y] = (0, 0, 0, 0)
    return out


def drop_specks(img, min_frac=0.004):
    """Quita manchas sueltas pequeñas (restos del fondo o motas de la IA)."""
    a = img.getchannel("A").point(lambda v: 255 if v else 0)
    w, h = a.size
    px = a.load()
    seen = bytearray(w * h)
    comps = []
    for y in range(h):
        for x in range(w):
            if px[x, y] and not seen[y * w + x]:
                stack, comp = [(x, y)], []
                seen[y * w + x] = 1
                while stack:
                    cx, cy = stack.pop()
                    comp.append((cx, cy))
                    for nx, ny in ((cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)):
                        if 0 <= nx < w and 0 <= ny < h and px[nx, ny] and not seen[ny * w + nx]:
                            seen[ny * w + nx] = 1
                            stack.append((nx, ny))
                comps.append(comp)
    if not comps:
        return img
    biggest = max(len(c) for c in comps)
    o = img.load()
    for c in comps:
        if len(c) < biggest * min_frac:
            for x, y in c:
                o[x, y] = (0, 0, 0, 0)
    return img


def cells(img, cols, rows):
    w, h = img.size
    for r in range(rows):
        for c in range(cols):
            box = (round(c * w / cols), round(r * h / rows), round((c + 1) * w / cols), round((r + 1) * h / rows))
            yield img.crop(box)


def snap(img, pal):
    """Ajusta los colores opacos a la paleta (quita el ruido del JPG) y binariza el alfa."""
    a = img.getchannel("A").point(lambda v: 255 if v >= 128 else 0)
    q = img.convert("RGB").quantize(palette=pal, dither=Image.Dither.NONE).convert("RGB")
    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    out.paste(q, mask=a)
    return out


def build_palette(images):
    """Paleta común de la piel a partir de los píxeles opacos de todas sus imágenes."""
    samples = []
    for im in images:
        small = im.copy()
        small.thumbnail((400, 400), Image.NEAREST)
        samples += [c[:3] for c in small.get_flattened_data() if c[3]]
    strip = Image.new("RGB", (len(samples), 1))
    strip.putdata(samples)
    return strip.quantize(PALETTE, method=Image.Quantize.MEDIANCUT)


def process_skin(skin, entries):
    keyed = {}
    for name, spec in entries.items():
        path = next((os.path.join(SRC, skin, name + ext) for ext in (".png", ".jpg", ".jpeg", ".webp")
                     if os.path.exists(os.path.join(SRC, skin, name + ext))), None)
        if not path:
            print(f"  falta {skin}/{name}")
            continue
        keyed[name] = (spec, key_out(Image.open(path)))
    pal = build_palette([im for _, im in keyed.values()])

    clips = {}
    for name, (spec, img) in keyed.items():
        cols, rows = spec["grid"]
        scale = HEAD / spec["head"]
        frames = []
        for cell in cells(img, cols, rows):
            cell = drop_specks(cell)
            box = cell.getchannel("A").getbbox()
            if not box:
                continue
            small = cell.resize((round(cell.width * scale), round(cell.height * scale)), Image.LANCZOS)
            small = snap(small, pal)
            frames.append((small, box, scale))
        if spec.get("fit") == "width":
            frames = fit_width(frames)
        clips[name] = align(frames, spec.get("align", "bbox"))

    out_dir = os.path.join(OUT, skin)
    os.makedirs(out_dir, exist_ok=True)
    for name, frames in clips.items():
        for i, fr in enumerate(frames):
            fr.save(os.path.join(out_dir, f"{name}_{i}.png"))
    previews(skin, clips)
    return clips


def fit_width(frames):
    """Iguala el ancho de la silueta al del primer fotograma. Para bucles en los que Gemini cambia
    el tamaño sin querer (p. ej. la respiración: la silueta casi no cambia, el motor ya añade el vaivén)."""
    ref = None
    out = []
    for f, box, scale in frames:
        b = f.getchannel("A").getbbox()
        w = b[2] - b[0]
        if ref is None:
            ref = w
        k = ref / w
        if abs(k - 1) > 0.01:
            f = f.resize((round(f.width * k), round(f.height * k)), Image.NEAREST)
        out.append((f, box, scale))
    return out


def align(frames, mode):
    """Lienzo común por acción: pies en la misma línea y mismo centro (de la silueta o de la celda)."""
    boxes = [f.getchannel("A").getbbox() for f, _, _ in frames]
    if mode == "cell":
        cx = [f.width / 2 for f, _, _ in frames]            # centro de su celda
    else:
        cx = [(b[0] + b[2]) / 2 for b in boxes]              # centro de la silueta
    left = max(c - b[0] for c, b in zip(cx, boxes))
    right = max(b[2] - c for c, b in zip(cx, boxes))
    top = max(b[3] - b[1] for b in boxes)
    W, H = int(left + right) + 4, top + 4
    out = []
    for (f, _, _), b, c in zip(frames, boxes, cx):
        canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        canvas.alpha_composite(f.crop(b), (round(left - (c - b[0])) + 2, H - 2 - (b[3] - b[1])))
        out.append(canvas)
    return out


def previews(skin, clips):
    os.makedirs(DOCS, exist_ok=True)
    bg = (214, 218, 224, 255)
    # 1) hoja: todo a la misma escala y sobre el mismo suelo
    items = [(n, f) for n, fs in clips.items() for f in fs]
    W = sum(f.width + 12 for _, f in items) + 12
    H = max(f.height for _, f in items) + 24
    sheet = Image.new("RGBA", (W, H), bg)
    x = 12
    for _, f in items:
        sheet.alpha_composite(f, (x, H - 12 - f.height))
        x += f.width + 12
    sheet.save(os.path.join(DOCS, f"{skin}_hoja.png"))
    # 2) tamaño real en la app (el gato de pie mide APP_STAND_H px), x1 y ampliado x4 para verlo
    stand = clips.get("pose_stand")
    k = APP_STAND_H / (stand[0].getchannel("A").getbbox()[3] - stand[0].getchannel("A").getbbox()[1]) if stand else 0.3
    firsts = [fs[0] for fs in clips.values()]
    smalls = [f.resize((max(1, round(f.width * k)), max(1, round(f.height * k))), Image.LANCZOS) for f in firsts]
    W = sum(s.width + 6 for s in smalls) + 6
    H = max(s.height for s in smalls) + 12
    app = Image.new("RGBA", (W, H), (32, 34, 38, 255))
    x = 6
    for s in smalls:
        app.alpha_composite(s, (x, H - 6 - s.height))
        x += s.width + 6
    big = app.resize((W * 4, H * 4), Image.NEAREST)
    combo = Image.new("RGBA", (big.width, H + 8 + big.height), (32, 34, 38, 255))
    combo.alpha_composite(app, (0, 0))
    combo.alpha_composite(big, (0, H + 8))
    combo.save(os.path.join(DOCS, f"{skin}_app.png"))
    # 3) GIF por acción animada
    for name, fs in clips.items():
        if len(fs) < 2:
            continue
        g = [Image.alpha_composite(Image.new("RGBA", f.size, bg), f).convert("RGB").resize(
            (f.width // 2, f.height // 2), Image.LANCZOS) for f in fs]
        g[0].save(os.path.join(DOCS, f"{skin}_{name}.gif"), save_all=True, append_images=g[1:],
                  duration=round(1000 / FPS.get(name, 4)), loop=0)


def main():
    with open(os.path.join(SRC, "manifest.json"), encoding="utf-8") as f:
        manifest = json.load(f)
    for skin, entries in manifest.items():
        if skin.startswith("_"):
            continue
        clips = process_skin(skin, entries)
        print(skin, {n: len(f) for n, f in clips.items()})


if __name__ == "__main__":
    main()
