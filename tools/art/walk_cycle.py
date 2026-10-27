#!/usr/bin/env python3
"""
Ciclo de caminar con las patas animadas a partir de UN fotograma del gato (cuadrícula de casillas).

Las hojas de Gemini traen fotogramas de caminar casi iguales entre sí (las patas apenas cambian), así que el
movimiento se ve quieto. Aquí se conserva el cuerpo y se reparte cada pata en su propia pieza (componentes
conectados bajo la panza) y se la balancea como un péndulo: la parte de arriba (el muslo) no se mueve y el pie se
desplaza ± stride/2, hacia atrás mientras apoya y hacia delante mientras se levanta. Las patas siguen la secuencia
lateral del paso del gato (trasera, delantera del mismo lado, trasera del otro, delantera del otro).

El avance por fotograma que hace que el pie apoyado no resbale es  stride / (fotogramas de apoyo).
"""
import math

import numpy as np
from PIL import Image
from scipy import ndimage as ndi

STANCE = 0.6          # fracción del ciclo con la pata apoyada


def leg_pieces(img, z0, ground):
    """Piezas (máscaras) de cada pata de la zona de abajo (filas >= z0), ordenadas de izquierda a derecha."""
    a = np.array(img.getchannel("A")) > 0
    zone = np.zeros_like(a)
    zone[z0:ground + 1] = a[z0:ground + 1]
    lab, n = ndi.label(zone, structure=np.ones((3, 3)))
    pieces = []
    for i in range(1, n + 1):
        m = lab == i
        if m.sum() >= 12:
            pieces.append(m)
    pieces.sort(key=lambda m: np.nonzero(m)[1].mean())
    return a, pieces


def analyse(img):
    """(z0, ground, piezas) para un fotograma. La zona de patas empieza en la fila más alta en la que todavía
    se distinguen las cuatro patas por separado (más arriba se funden con la panza y el muslo)."""
    a = np.array(img.getchannel("A")) > 0
    ys = np.nonzero(a.any(1))[0]
    top, ground = int(ys.min()), int(ys.max())
    best = None
    for z in range(ground - 5, top, -1):
        _, pieces = leg_pieces(img, z, ground)
        if len(pieces) == 4:
            best = (z, pieces)
        elif best is not None and len(pieces) < 4:
            break
    if best is None:
        raise ValueError("no se distinguen cuatro patas")
    return best[0], ground, best[1]


def leg_offset(phase, stride):
    """Desplazamiento horizontal del pie (casillas, + = hacia delante) a lo largo del ciclo (0..1)."""
    u = phase % 1.0
    if u < STANCE:                                    # apoyada: va hacia atrás a velocidad constante
        return stride / 2 - stride * (u / STANCE)
    t = (u - STANCE) / (1 - STANCE)                   # en el aire: vuelve hacia delante
    t = t * t * (3 - 2 * t)
    return -stride / 2 + stride * t


def frame(img, z0, ground, pieces, phase, stride, order=(0.5, 0.0, 0.75, 0.25)):
    """Fotograma del ciclo en la fase dada (0..1). `order`: desfase de cada pata, de izquierda a derecha
    (trasera lejana, trasera cercana, delantera lejana, delantera cercana)."""
    a = np.array(img.getchannel("A")) > 0
    px = np.array(img)
    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    o = out.load()
    legs = np.zeros_like(a)
    for m in pieces:
        legs |= m
    for y, x in zip(*np.nonzero(a & ~legs)):      # el cuerpo no se toca
        o[int(x), int(y)] = tuple(int(v) for v in px[y, x])
    h = max(1, ground - z0 + 1)
    for idx in (0, 2, 1, 3):                        # primero las lejanas, luego las cercanas
        if idx >= len(pieces):
            continue
        off = leg_offset(phase + order[idx], stride)
        for y, x in zip(*np.nonzero(pieces[idx])):
            w = (y - z0 + 1) / h
            nx = int(x + round(off * w))
            if 0 <= nx < img.width:
                o[nx, int(y)] = tuple(int(v) for v in px[y, x])
    return out


def cycle(img, n=8, stride=24, phases=None):
    z0, ground, pieces = analyse(img)
    if len(pieces) != 4:
        raise ValueError(f"se esperaban 4 patas y hay {len(pieces)}")
    ph = phases if phases is not None else [i / n for i in range(n)]
    return [frame(img, z0, ground, pieces, p, stride) for p in ph]
