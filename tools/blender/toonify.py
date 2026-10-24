"""
Combina las pasadas de swip_cats.py (albedo, luz, ids) en pixel art:
  - sombreado en 3 bandas (sombra / medio / luz) en lugar de degradado;
  - contorno exterior oscuro (del color de la pieza, oscurecido) y líneas interiores
    donde cambia de pieza (cabeza/cuerpo/pata/cola) o de gato;
  - opcional: escala ×N con vecino más cercano para verlo grande.
Uso: python3 tools/blender/toonify.py <carpeta> [zoom]
"""
import glob
import os
import sys

from PIL import Image

BANDS = ((0.42, 0.72), (0.78, 1.0))   # (umbral de luz, factor de brillo): por debajo del primer umbral = sombra


def shade(albedo, light):
    w, h = albedo.size
    a, l = albedo.load(), light.load()
    out = Image.new("RGBA", (w, h))
    o = out.load()
    for y in range(h):
        for x in range(w):
            r, g, b, al = a[x, y]
            if al < 128:
                o[x, y] = (0, 0, 0, 0)
                continue
            lum = sum(l[x, y][:3]) / (3 * 255)
            f = 1.0 if lum >= BANDS[0][0] else BANDS[0][1]
            if lum < 0.16:
                f = 0.58
            if f < 1:   # sombra con matiz frío-cálido: baja más el azul que el rojo
                r, g, b = int(r * (f + 0.08)), int(g * f), int(b * (f - 0.04))
            o[x, y] = (min(255, r), min(255, g), max(0, b), 255)
    return out


def outline(img, ids):
    w, h = img.size
    p, q = img.load(), ids.load()
    out = img.copy()
    o = out.load()
    def ink(c):
        return (int(c[0] * 0.30), int(c[1] * 0.22), int(c[2] * 0.20), 255)
    for y in range(h):
        for x in range(w):
            if p[x, y][3] == 0:
                # contorno exterior: píxel vacío junto a uno lleno
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < w and 0 <= ny < h and p[nx, ny][3]:
                        o[x, y] = ink(p[nx, ny])
                        break
                continue
            # línea interior: cambia el grupo de pieza respecto al vecino de abajo/derecha (1 px)
            me = q[x, y][:2]
            for dx, dy in ((1, 0), (0, 1)):
                nx, ny = x + dx, y + dy
                if nx < w and ny < h and p[nx, ny][3] and abs(q[nx, ny][0] - me[0]) + abs(q[nx, ny][1] - me[1]) > 10:
                    # se dibuja en el píxel que queda más "delante" (más claro en la pasada de luz no importa: el de arriba)
                    o[x, y] = ink(p[x, y]) if dy == 0 else ink(p[nx, ny])
                    break
    return out


def main():
    folder = sys.argv[1]
    zoom = int(sys.argv[2]) if len(sys.argv) > 2 else 1
    for alb_path in sorted(glob.glob(os.path.join(folder, "*_albedo.png"))):
        base = alb_path[: -len("_albedo.png")]
        albedo = Image.open(alb_path).convert("RGBA")
        light = Image.open(base + "_light.png").convert("RGBA")
        ids = Image.open(base + "_ids.png").convert("RGBA")
        art = outline(shade(albedo, light), ids)
        box = art.getchannel("A").getbbox()
        if box:   # el render se hace con margen de sobra; aquí se recorta al contenido (+2 px)
            art = art.crop((box[0] - 2, box[1] - 2, box[2] + 2, box[3] + 2))
        art.save(base + ".png")
        if zoom > 1:
            art.resize((art.width * zoom, art.height * zoom), Image.NEAREST).save(base + f"_x{zoom}.png")
        print(os.path.basename(base), art.size)


if __name__ == "__main__":
    main()
