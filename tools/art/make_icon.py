#!/usr/bin/env python3
"""
Genera el icono de Swip (src/Swip.App/Assets/swip.ico y swip.png): una ficha redondeada con dos flechas que se
cruzan (cambiar de sesión). No es un gato: el icono identifica al programa, no a un personaje.
Uso: python3 tools/art/make_icon.py
"""
import os

from PIL import Image, ImageDraw

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "src", "Swip.App", "Assets")
S = 1024                      # lienzo de trabajo (se reduce con suavizado)
TOP, BOTTOM = (62, 124, 132), (36, 78, 92)          # degradado de la ficha (azul verdoso)
WHITE, ORANGE = (250, 250, 250), (252, 136, 37)     # flecha de ida / de vuelta (el naranja de la gata)


def tile():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    grad = Image.new("RGBA", (S, S))
    gd = ImageDraw.Draw(grad)
    for y in range(S):
        t = y / (S - 1)
        gd.line([(0, y), (S, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(TOP, BOTTOM)) + (255,))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([40, 40, S - 40, S - 40], radius=230, fill=255)
    img.paste(grad, (0, 0), mask)
    return img


def arrow(d, y, direction, color):
    """Flecha horizontal gruesa con la punta hacia la derecha (direction=1) o la izquierda (-1)."""
    left, right, half, head = 220, S - 220, 62, 190
    if direction > 0:
        d.rounded_rectangle([left, y - half, right - head + 40, y + half], radius=half, fill=color)
        d.polygon([(right - head, y - 190), (right, y), (right - head, y + 190)], fill=color)
    else:
        d.rounded_rectangle([left + head - 40, y - half, right, y + half], radius=half, fill=color)
        d.polygon([(left + head, y - 190), (left, y), (left + head, y + 190)], fill=color)


def main():
    img = tile()
    d = ImageDraw.Draw(img)
    arrow(d, 370, 1, WHITE)
    arrow(d, 654, -1, ORANGE)
    os.makedirs(OUT, exist_ok=True)
    img.resize((256, 256), Image.LANCZOS).save(os.path.join(OUT, "swip.png"))
    sizes = [(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (24, 24), (16, 16)]
    img.resize((256, 256), Image.LANCZOS).save(os.path.join(OUT, "swip.ico"), format="ICO", sizes=sizes)
    print("icono escrito en", OUT)


if __name__ == "__main__":
    main()
