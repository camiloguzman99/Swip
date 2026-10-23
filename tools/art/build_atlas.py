#!/usr/bin/env python3
"""
Genera las hojas de sprites (atlas) de los gatos y sus metadatos a partir de art/clips.json.

Para cada piel (orange, gray) y cada clip:
  - clips con status "final": usa el arte dibujado tal cual;
  - clips "placeholder": genera fotogramas de REFERENCIA aplicando la receta del clip
    (escala, giro, desplazamiento, espejo) sobre las poses ya dibujadas.

Salidas:
  src/Swip.App/Assets/atlas/cats_<piel>.png   hoja de sprites (una celda por fotograma)
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

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CLIPS = os.path.join(ROOT, "art", "clips.json")
POSES = os.path.join(ROOT, "art", "poses")                           # poses dibujadas originales
FINAL = os.path.join(ROOT, "art", "final")                            # arte definitivo nuevo
OUT = os.path.join(ROOT, "src", "Swip.App", "Assets", "atlas")
REF = os.path.join(ROOT, "docs", "art", "reference")
SKINS = ["orange", "gray"]
CELL_H = 160
COLUMNS = 12
ALPHA_MIN = 20


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
    cell_w = round(CELL_H * base_w / base_h)
    clips = data["clips"]
    total = sum(len(c["reference"]) for c in clips)
    rows = math.ceil(total / COLUMNS)

    os.makedirs(OUT, exist_ok=True)
    os.makedirs(REF, exist_ok=True)

    sheets = {s: Image.new("RGBA", (cell_w * COLUMNS, CELL_H * rows), (0, 0, 0, 0)) for s in SKINS}
    meta = {"cell": [cell_w, CELL_H], "columns": COLUMNS,
            "skins": {s: f"cats_{s}.png" for s in SKINS}, "clips": {}}
    frames_by_clip = {}

    cell = 0
    for clip in clips:
        entry = {"status": clip["status"], "frames": []}
        frames_by_clip[clip["id"]] = {s: [] for s in SKINS}
        for i, spec in enumerate(clip["reference"]):
            bounds = {}
            for skin in SKINS:
                if clip["status"] == "final":
                    img = final_frame(skin, clip["id"], i, spec, (base_w, base_h))
                else:
                    img = render_reference(skin, spec, (base_w, base_h))
                frames_by_clip[clip["id"]][skin].append(img)
                small = img.resize((cell_w, CELL_H), Image.LANCZOS)
                col, row = cell % COLUMNS, cell // COLUMNS
                sheets[skin].alpha_composite(small, (col * cell_w, row * CELL_H))
                b = opaque_box(img) or (0, 0, base_w, base_h)
                bounds[skin] = [round(b[0] / base_w, 4), round(b[1] / base_h, 4),
                                round((b[2] - b[0]) / base_w, 4), round((b[3] - b[1]) / base_h, 4)]
            entry["frames"].append({"cell": cell, "bounds": bounds})
            cell += 1
        meta["clips"][clip["id"]] = entry

    for skin, sheet in sheets.items():
        sheet.save(os.path.join(OUT, f"cats_{skin}.png"), optimize=True)
    with open(os.path.join(OUT, "atlas.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, ensure_ascii=False, indent=1)

    write_reference_sheets(clips, frames_by_clip, base_w, base_h)
    write_pending(clips)
    pending = [c for c in clips if c["status"] != "final"]
    print(f"{len(clips)} clips, {total} fotogramas por piel; "
          f"{len(pending)} clips con referencia ({sum(len(c['reference']) for c in pending)} fotogramas pendientes)")


def font(size):
    for path in ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "C:/Windows/Fonts/segoeui.ttf"):
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def write_reference_sheets(clips, frames_by_clip, base_w, base_h):
    """Una hoja por clip: los fotogramas de las dos pieles, numerados, con la descripción."""
    scale = 0.5
    fw, fh = round(base_w * scale), round(base_h * scale)
    title_font, small_font = font(18), font(13)
    for clip in clips:
        frames = frames_by_clip[clip["id"]]
        n = len(clip["reference"])
        sheet = Image.new("RGBA", (max(fw * n + 20, 420), 70 + (fh + 26) * len(SKINS)), (48, 50, 56, 255))
        d = ImageDraw.Draw(sheet)
        tag = "ARTE FINAL" if clip["status"] == "final" else "REFERENCIA (pendiente de arte)"
        d.text((10, 8), f"{clip['id']}  ·  {clip['fps']} fps  ·  {'bucle' if clip['loop'] else 'una vez'}  ·  {tag}",
               fill=(255, 214, 120), font=title_font)
        d.text((10, 34), clip["description"], fill=(225, 225, 225), font=small_font)
        for r, skin in enumerate(SKINS):
            y = 62 + r * (fh + 26)
            for i, img in enumerate(frames[skin]):
                x = 10 + i * fw
                d.rectangle([x, y, x + fw - 4, y + fh], outline=(90, 94, 102))
                small = img.resize((fw - 4, fh), Image.LANCZOS)
                sheet.alpha_composite(small, (x, y))
                d.text((x + 4, y + fh + 4), f"{skin} #{i}", fill=(170, 170, 170), font=small_font)
        sheet.convert("RGB").save(os.path.join(REF, f"{clip['id']}.png"), optimize=True)


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
