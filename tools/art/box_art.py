#!/usr/bin/env python3
"""
La caja de cartón dibujada casilla a casilla (pixel art generado, no reducido de un dibujo).

Cuadro de 64x48 casillas; el cuadro principal (32x32, columnas 16..47, filas 16..47) es la caja en
reposo y su borde de abajo es la base. El margen de 16 casillas a cada lado y por encima es para las
solapas, el aplastamiento y el tambaleo.

Luz desde arriba a la izquierda en todos los fotogramas: borde izquierdo y superior claros, lado
derecho y base en sombra, la cara de dentro más oscura junto a la pared delantera, la solapa izquierda
iluminada y la derecha en penumbra.

Fotogramas (nombre -> capa delantera y capa trasera):
  lid_0 .. lid_4   abrir: solapas a 0, 45, 90, 125 y 150 grados (lid_0 = cerrada, lid_4 = abierta)
  squash_1 .. _4   cerrada con peso encima: baja 1..4 filas y se ensancha, con los lados abombados y
                   la tapa hundida en el centro cuando el peso es mayor
La capa trasera es el interior de la caja abierta: la app la dibuja DETRÁS del gato que está dentro
y la delantera (pared delantera, solapas) delante, así el gato queda dentro de verdad.
"""
import math

from PIL import Image

W, H = 64, 48                     # cuadro de dibujo
MAIN_X, MAIN_Y, MAIN = 16, 16, 32  # cuadro principal
LID_ANGLES = [0, 45, 90, 125, 150]
MAX_SQUASH = 4

# Paleta (luz arriba a la izquierda).
OUT = (52, 31, 16)
HI = (234, 182, 116)
LT = (216, 158, 94)
MID = (197, 138, 77)
SH = (170, 114, 62)
DK = (140, 90, 47)
DK2 = (110, 68, 35)
IN_BACK = (124, 80, 42)           # pared de dentro del fondo (le llega algo de luz)
IN_DK = (80, 50, 26)              # sombra de la pared delantera sobre el interior
TAPE = (226, 204, 150)
TAPE_HI = (242, 226, 184)
TAPE_SH = (194, 170, 116)
INK = (96, 60, 31)                # tinta impresa (flechas)
LABEL = (236, 226, 200)
LABEL_INK = (128, 116, 100)
SCUFF = (182, 124, 68)


class Canvas:
    def __init__(self):
        self.px = {}                  # (x, y) -> color

    def set(self, x, y, c):
        if 0 <= x < W and 0 <= y < H:
            self.px[(x, y)] = c

    def image(self):
        img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        for (x, y), c in self.px.items():
            img.putpixel((x, y), (*c, 255))
        return img


def outline_of(mask):
    """Casillas del borde de una forma (vecina por los 4 lados de algo que no es la forma)."""
    return {(x, y) for (x, y) in mask
            if any((x + dx, y + dy) not in mask for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))}


def body_rows(squash):
    """Para cada fila del cuerpo: (izquierda, derecha) incluidas. Con peso baja y se ensancha."""
    h = MAIN - squash
    widen = squash // 2
    top = MAIN_Y + MAIN - h
    rows = {}
    for y in range(top, MAIN_Y + MAIN):
        left, right = MAIN_X - widen, MAIN_X + MAIN - 1 + widen
        rel = (y - top) / max(1, h - 1)
        if squash >= 2 and 0.3 <= rel <= 0.8:      # lados abombados por el peso
            left, right = left - 1, right + 1
        rows[y] = (left, right)
    return rows, top


def draw_body(cv, squash, open_):
    rows, top = body_rows(squash)
    mask = {(x, y) for y, (l, r) in rows.items() for x in range(l, r + 1)}
    sag = squash >= 3
    if sag:   # la tapa se hunde en el centro bajo el peso
        l, r = rows[top]
        for x in range(l + (r - l) * 3 // 10, r - (r - l) * 3 // 10 + 1):
            mask.discard((x, top))
    ring = outline_of(mask)
    bottom = MAIN_Y + MAIN - 1
    rim = top + 4 if open_ else None              # borde de la pared delantera (caja abierta)
    for (x, y) in mask:
        if (x, y) in ring:
            cv.set(x, y, OUT)
            continue
        l, r = rows[y]
        if open_ and y < rim:
            continue                              # el hueco: lo dibuja la capa trasera
        c = MID
        if x <= l + 1:
            c = HI if x == l + 1 else c
        if x == l + 2:
            c = LT
        if x >= r - 3:
            c = SH
        if x == r - 1:
            c = DK
        if y >= bottom - 2:
            c = DK if y == bottom - 2 else DK2
        cv.set(x, y, c)
    # Tapa cerrada: superficie clara, junta central entre las dos solapas y sombra bajo el borde.
    if not open_:
        lid_top = top + 1 + (1 if sag else 0)
        l, r = rows[top + 1]
        cx = (l + r) // 2
        for x in range(l + 1, r):
            for y in (top + 1, top + 2):
                if (x, y) in mask and (x, y) not in ring:
                    cv.set(x, y, HI if y == top + 1 else LT)
            if (x, top + 3) not in ring:
                cv.set(x, top + 3, DK)            # sombra bajo el borde de la tapa
        for y in (top + 1, top + 2):
            cv.set(cx, y, SH)                     # junta entre solapas
    else:
        l, r = rows[rim]
        for x in range(l + 1, r):
            cv.set(x, rim, OUT)                   # borde superior de la pared delantera
            cv.set(x, rim + 1, HI)                # el canto le da la luz
    return rows, top, ring


def draw_details(cv, rows, top, squash, open_):
    bottom = MAIN_Y + MAIN - 1
    l, r = rows[bottom - 4]
    cx = (l + r) // 2
    if not open_:
        # Cinta de embalar: cruza la tapa y baja por el frente, con el final rasgado.
        end = top + 10
        for y in range(top + 1, end + 1):
            for x in range(cx - 2, cx + 2):
                if y == end and (x - cx) % 2 == 0:
                    continue                      # final rasgado
                c = TAPE_HI if x == cx - 2 else TAPE_SH if x == cx + 1 else TAPE
                cv.set(x, y, c)
    # Flechas impresas de "este lado arriba" (arriba a la derecha): punta de 5 casillas y asta.
    ay = top + 12 if not open_ else top + 11
    arrow = ((2, 0), (1, 1), (2, 1), (3, 1), (0, 2), (1, 2), (2, 2), (3, 2), (4, 2), (2, 3), (2, 4))
    for ax in (r - 14, r - 8):
        for dx, dy in arrow:
            cv.set(ax + dx, ay + dy, INK)
    for x in range(r - 14, r - 3):
        cv.set(x, ay + 6, INK)                    # barra bajo las flechas
    # Etiqueta abajo a la izquierda, con dos renglones.
    lx, ly = l + 4, bottom - 9
    for y in range(ly, ly + 5):
        for x in range(lx, lx + 9):
            cv.set(x, y, LABEL)
    for x in range(lx + 1, lx + 7):
        cv.set(x, ly + 1, LABEL_INK)
    for x in range(lx + 1, lx + 5):
        cv.set(x, ly + 3, LABEL_INK)
    # Marcas del cartón (siempre en el mismo sitio).
    for x, y in ((l + 7, top + 16), (l + 8, top + 16), (r - 12, bottom - 7), (l + 14, top + 21),
                 (l + 20, bottom - 5), (r - 5, top + 20)):
        if top + 5 < y < bottom - 2:
            cv.set(x, y, SCUFF)


def draw_interior(cv, rows, top):
    """Capa trasera de la caja abierta: el hueco entre el borde de atrás y la pared delantera."""
    rim = top + 4
    for y in range(top, rim):
        l, r = rows[y]
        for x in range(l, r + 1):
            if x in (l, r) or y == top:
                cv.set(x, y, OUT)                 # borde de atrás
            else:
                cv.set(x, y, IN_BACK if y <= top + 1 else IN_DK)


def flap_cells(hinge, angle, length, flip):
    """Casillas de una solapa: un listón de `length` casillas y 5 de grueso que gira en `hinge`."""
    a = math.radians(angle)
    dx, dy = math.cos(a), -math.sin(a)
    if flip:
        dx = -dx
    nx, ny = -dy, dx                              # perpendicular
    hx, hy = hinge
    cells = set()
    for y in range(H):
        for x in range(W):
            px, py = x + 0.5 - hx, y + 0.5 - hy
            along = px * dx + py * dy
            across = px * nx + py * ny
            if -0.5 <= along <= length and -2.5 <= across <= 2.5:
                cells.add((x, y))
    return cells, (dx, dy)


def draw_flaps(cv, rows, top, angle):
    l, r = rows[top]
    length = MAIN // 2                            # cada solapa mide la mitad del ancho de la caja
    for side, hinge, face in (("izq", (l + 0.5, top + 0.5), (HI, LT)), ("der", (r + 0.5, top + 0.5), (LT, MID))):
        cells, (dx, dy) = flap_cells(hinge, angle, length, flip=side == "der")
        ring = outline_of(cells)
        for (x, y) in cells:
            if (x, y) in ring:
                cv.set(x, y, OUT)
                continue
            along = (x + 0.5 - hinge[0]) * dx + (y + 0.5 - hinge[1]) * dy
            c = face[0] if along > length * 0.45 else face[1]
            if along > length - 3:                # media cinta en la punta de cada solapa
                c = TAPE_HI if side == "izq" else TAPE_SH
            cv.set(x, y, c)


def frame(lid, squash):
    """(delantera, trasera) de un fotograma."""
    open_ = lid > 0
    front, back = Canvas(), Canvas()
    rows, top, _ = draw_body(front, squash if not open_ else 0, open_)
    draw_details(front, rows, top, squash, open_)
    if open_:
        draw_interior(back, rows, top)
        draw_flaps(front, rows, top, LID_ANGLES[lid])
    return front.image(), back.image()


def frames():
    out = {}
    for k in range(len(LID_ANGLES)):
        out[f"lid_{k}"] = frame(k, 0)
    for s in range(1, MAX_SQUASH + 1):
        out[f"squash_{s}"] = frame(0, s)
    return out


if __name__ == "__main__":
    import os
    import sys
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    zoom = 8
    fs = frames()
    sheet = Image.new("RGBA", (len(fs) * (W * zoom + 10), H * zoom + 10), (58, 62, 70, 255))
    for i, (name, (f, b)) in enumerate(fs.items()):
        both = Image.alpha_composite(b, f)
        sheet.alpha_composite(both.resize((W * zoom, H * zoom), Image.NEAREST), (5 + i * (W * zoom + 10), 5))
    sheet.save(sys.argv[1] if len(sys.argv) > 1 else "box_frames.png")
