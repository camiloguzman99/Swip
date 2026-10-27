#!/usr/bin/env python3
"""
La caja de cartón dibujada casilla a casilla (pixel art generado, no reducido de un dibujo).

Cuadro de 106x86 casillas; el cuadro principal (66x66, columnas 20..85, filas 20..85) es la caja en
reposo y su borde de abajo es la base. El margen de 20 casillas a cada lado y por encima es para las
solapas, el aplastamiento y el tambaleo.

Luz desde arriba a la izquierda en todos los fotogramas: borde izquierdo y superior claros, lado
derecho y base en sombra; las caras interiores de las solapas son de un café más oscuro que las de
fuera, y la solapa delantera proyecta su sombra sobre la caja mientras se abre.

Cuatro solapas: dos laterales, la trasera (se levanta por detrás) y la delantera (gira hacia quien
mira, pasa por la vertical y cae por delante). La cinta cruza la tapa por fuera y baja por el frente:
al abrir se corta en la junta; la tira del frente se queda en la pared y se ve sobre la cara de fuera
de la delantera mientras sube.

Fotogramas (nombre -> capa delantera y capa trasera):
  lid_0 .. lid_6   abrir (lid_0 = cerrada, lid_6 = abierta): laterales y trasera hasta 150 grados,
                   delantera hasta 225
  squash_1 .. _4   cerrada con peso encima: baja 1..4 filas y se ensancha, con los lados abombados y
                   la tapa hundida en el centro cuando el peso es mayor
La capa trasera (la solapa trasera) la dibuja la app DETRÁS del gato que está dentro, y la delantera
(cuerpo y demás solapas) delante: el gato queda dentro de verdad.
"""
import math

from PIL import Image

W, H = 106, 86                    # cuadro de dibujo
MAIN_X, MAIN_Y, MAIN = 20, 20, 66  # cuadro principal
MAX_SQUASH = 4

# Paleta (luz arriba a la izquierda).
OUT = (52, 31, 16)
HI = (234, 182, 116)
LT = (216, 158, 94)
MID = (197, 138, 77)
SH = (170, 114, 62)
DK = (140, 90, 47)
DK2 = (110, 68, 35)
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
    for (x, y) in mask:
        if (x, y) in ring:
            cv.set(x, y, OUT)
            continue
        l, r = rows[y]
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
        l, r = rows[top + 1]
        for x in range(l + 1, r):
            cv.set(x, top + 1, HI)                # abierta: el canto de la pared delantera, iluminado
    return rows, top, ring


def draw_details(cv, rows, top, squash, open_):
    bottom = MAIN_Y + MAIN - 1
    l, r = rows[bottom - 4]
    cx = (l + r) // 2
    # Cinta de embalar: cruza la tapa y baja por el frente, con el final rasgado. Al abrir se corta en
    # la junta: la tira del frente se queda en la pared (su otra mitad va en el canto de las solapas).
    start, end = (top + 1, top + 10) if not open_ else (top + 2, top + 10)
    for y in range(start, end + 1):
        for x in range(cx - 2, cx + 2):
            if (y == end or (open_ and y == start)) and (x - cx) % 2 == 0:
                continue                          # bordes rasgados
            c = TAPE_HI if x == cx - 2 else TAPE_SH if x == cx + 1 else TAPE
            cv.set(x, y, c)
    # Flechas impresas de "este lado arriba" (arriba a la derecha): punta de 5 casillas y asta.
    ay = top + 12
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


# Apertura en 7 fotogramas. Ángulos de cada solapa (0 = cerrada, tumbada sobre la caja):
#   laterales y trasera hasta 150 grados (abiertas hacia fuera y hacia arriba);
#   delantera hasta 225 grados: gira hacia quien mira, pasa por la vertical y cae por delante
#   45 grados por debajo del borde, sin tapar la abertura.
LID_STEPS = 7
SIDE_MAX, BACK_MAX, FRONT_MAX = 150, 150, 225
FLAP_LEN = 18                     # fondo de las solapas delantera/trasera (filas, de canto)
PERSPECTIVE = 0.35                # cuánto se ve hacia abajo lo que apunta hacia quien mira
FLAP_IN = (150, 98, 52)           # cara interior del cartón: café más oscuro que la de fuera
FLAP_IN_SH = (126, 80, 42)
SHADE = 0.78                      # sombra de la solapa delantera sobre la caja


def flap_reach(angle, toward_viewer_when_closed):
    """Filas que se extiende la punta de una solapa delantera/trasera respecto a su bisagra en
    pantalla (negativo = por encima del borde). Giro real proyectado: la parte vertical sube y la que
    apunta hacia quien mira se ve un poco más abajo."""
    a = math.radians(angle)
    depth = FLAP_LEN * math.cos(a) * (1 if toward_viewer_when_closed else -1)
    return -FLAP_LEN * math.sin(a) + PERSPECTIVE * depth


def cast_front_shadow(cv, rows, top, angle, reach):
    """Sombra de la solapa delantera sobre la propia caja (luz arriba a la izquierda: cae hacia abajo y
    a la derecha). De pie apenas sombrea el canto; al bajar hacia quien mira, la sombra crece bajo su
    borde. Oscurece lo ya dibujado (pared, cinta, flechas), no el contorno."""
    if angle <= 0:
        return
    l, r = rows[top]
    if angle <= 90:
        rows_shaded = range(top + 1, top + 2)                         # el canto, bajo la solapa de pie
    else:
        t = (angle - 90) / (FRONT_MAX - 90)
        rows_shaded = range(top + 1, top + max(0, round(reach)) + 1 + round(1 + 3 * t))
    for y in rows_shaded:
        for x in range(l + 2, r):                                     # una casilla corrida a la derecha
            c = cv.px.get((x, y))
            if c and c != OUT:
                cv.set(x, y, tuple(int(v * SHADE) for v in c))


def draw_panel(cv, rows, top, reach, face, lit_edge, tape=None):
    """Solapa delantera o trasera vista de frente: un panel del ancho de la caja que sale del borde
    de arriba hacia arriba (reach < 0) o hacia abajo (reach > 0).
    tape: "full" = se ve su cara de fuera, con la cinta a lo largo de toda la solapa (cruzaba la
    tapa); "edge" = se ve la de dentro, y de la cinta solo el trozo que dobla por el canto."""
    n = round(abs(reach))
    if n < 1:
        return
    l, r = rows[top]
    ys = range(top - n, top + 1) if reach < 0 else range(top, top + n + 1)
    mask = {(x, y) for y in ys for x in range(l, r + 1)}
    ring = outline_of(mask)
    far = top - n if reach < 0 else top + n           # el canto libre
    cx = (l + r) // 2
    for (x, y) in mask:
        if (x, y) in ring:
            cv.set(x, y, OUT)
            continue
        c = face[0] if x < r - 3 else face[1]          # sombra a la derecha, como la caja
        if x <= l + 1:
            c = LT
        if abs(y - far) == 1:
            c = lit_edge                               # canto libre
        cv.set(x, y, c)
    if tape:
        step = -1 if reach < 0 else 1
        ys = [y for y in ys if (cx, y) in mask and (cx, y) not in ring] if tape == "full" \
            else [far - step, far - 2 * step]
        for y in ys:
            if (cx, y) in mask and (cx, y) not in ring:
                for x in range(cx - 2, cx + 2):
                    cv.set(x, y, TAPE_HI if x == cx - 2 else TAPE_SH if x == cx + 1 else TAPE)


def draw_side_flaps(cv, rows, top, angle):
    """Solapas laterales: listones de 3 casillas de grueso, del color del cartón, con la bisagra en la
    esquina de arriba (empiezan dentro del contorno de la caja: salen del borde, no del aire)."""
    if angle <= 0:
        return
    l, r = rows[top]
    length = MAIN // 2 - 2
    for side, hinge, face in (("izq", (l + 1.0, top + 1.0), (LT, MID)), ("der", (r, top + 1.0), (MID, SH))):
        a = math.radians(angle)
        dx, dy = math.cos(a), -math.sin(a)
        if side == "der":
            dx = -dx
        nx, ny = -dy, dx
        cells = set()
        for y in range(H):
            for x in range(W):
                px, py = x + 0.5 - hinge[0], y + 0.5 - hinge[1]
                along, across = px * dx + py * dy, px * nx + py * ny
                if -1.0 <= along <= length and -1.5 <= across <= 1.5:
                    cells.add((x, y))
        ring = outline_of(cells)
        for (x, y) in cells:
            if (x, y) in ring:
                cv.set(x, y, OUT)
            else:
                along = (x + 0.5 - hinge[0]) * dx + (y + 0.5 - hinge[1]) * dy
                cv.set(x, y, face[0] if along > length / 2 else face[1])


def frame(lid, squash):
    """(delantera, trasera) de un fotograma. La trasera es lo que va DETRÁS del gato que está dentro."""
    open_ = lid > 0
    front, back = Canvas(), Canvas()
    rows, top, _ = draw_body(front, squash if not open_ else 0, open_)
    draw_details(front, rows, top, squash, open_)
    if open_:
        p = lid / (LID_STEPS - 1)
        draw_panel(back, rows, top, flap_reach(BACK_MAX * p, True), (FLAP_IN, FLAP_IN_SH), LT, tape="edge")
        # Las laterales van detrás de la delantera mientras esta sube (como en una caja de verdad).
        draw_side_flaps(front, rows, top, SIDE_MAX * p)
        fa = FRONT_MAX * p
        # Mientras sube se ve su cara de fuera; al pasar de la vertical, la de dentro.
        face = (MID, SH) if fa < 90 else (FLAP_IN, FLAP_IN_SH)
        reach = flap_reach(fa, False)
        cast_front_shadow(front, rows, top, fa, reach)
        draw_panel(front, rows, top, reach, face, LT if fa < 180 else SH, tape="full" if fa < 90 else "edge")
    return front.image(), back.image()


def frames():
    out = {}
    for k in range(LID_STEPS):
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
