#!/usr/bin/env python3
# Uso: python3 tools/catart.py  -> valida y previsualiza los frames en ASCII.
# Estas rejillas (slots de color O/B/H/S/P/E/K/W) estan en src/Swip.App/Art/PixelCat.cs; manten ambas en sync.
# Slots de color (no colores fijos), para poder recolorear por gato:
# . transp | O contorno | B cuerpo medio | H highlight(claro) | S sombra | P rosa
# E ojo(saturado) | K pupila/linea | W brillo-ojo
W=H=16
def chk(name,g):
    ok=len(g)==H
    for i,r in enumerate(g):
        if len(r)!=W: print(f"FAIL {name} r{i} len {len(r)}: '{r}'"); ok=False
    return ok

SIT_A=[
 "......O..O......",
 ".....OBO.OBO....",
 "....OBPBOBPBO...",
 "....OBBBBBBBO...",
 "...OHBBBBBBBBO..",
 "...OHBWEBBEWBO..",
 "...OHBBKBBKBBO..",
 "...OHBBBBBBBBO..",
 "...OHBBPPBBBBO..",
 "...OHBBBBBBBSO..",
 "...OOHBBBBBSOO..",
 "....OHBBBBBSO...",
 "....OHBBBBBSO...",
 "....OHBSBBSBO...",
 "....OBOBBOBBO...",
 ".....OO..OO.....",
]
SIT_B=[  # parpadeo + cola al otro lado
 "......O..O......",
 ".....OBO.OBO....",
 "....OBPBOBPBO...",
 "....OBBBBBBBO...",
 "...OHBBBBBBBBO..",
 "...OHBBBBBBBBO..",
 "...OHBKKBBKKBO..",
 "...OHBBBBBBBBO..",
 "...OHBBPPBBBBO..",
 "...OHBBBBBBBSO..",
 "...OOHBBBBBSOO..",
 "....OHBBBBBSO...",
 "....OHBBBBBSO...",
 "....OHBSBBSBO...",
 "....OOBBBBOO...."[:16],
 ".....OO..OO.....",
]
HAPPY=[
 "......O..O......",
 ".....OBO.OBO....",
 "....OBPBOBPBO...",
 "....OBBBBBBBO...",
 "...OHBBBBBBBBO..",
 "...OHBKBBBBKBO..",
 "...OHKBKBBKBKO.."[:16],
 "...OHBBBBBBBBO..",
 "...OHBBPPBBBBO..",
 "...OHBBBBBBBSO..",
 "...OOHBBBBBSOO..",
 "....OHBBBBBSO...",
 "....OHBBBBBSO...",
 "....OHBSBBSBO...",
 "....OBOBBOBBO...",
 ".....OO..OO.....",
]
# Durmiendo: pegado al fondo (filas 6..15), ovillado, ojos cerrados (K)
SLEEP_A=[
 "................",
 "................",
 "................",
 "................",
 "................",
 "................",
 "................",
 ".......OOO......",
 "....OOOBBBOO....",
 "..OOHBBBBBBBOO..",
 ".OHBBBBBBBBBBBO.",
 ".OHBKKBBBBBKKBO.",
 ".OHBBBBPPBBBBSO.",
 ".OHBBBBBBBBBBSO.",
 ".OOHBBBBBBBBSOO.",
 "..OOOOOOOOOOOO..",
]
SLEEP_B=[
 "................",
 "................",
 "................",
 "................",
 "................",
 "................",
 ".......OOO......",
 "....OOOBBBOO....",
 "..OOHBBBBBBBOO..",
 ".OHBBBBBBBBBBBO.",
 ".OHBKKBBBBBKKBO.",
 ".OHBBBBPPBBBBSO.",
 ".OHBBBBBBBBBBSO.",
 ".OHBBBBBBBBBBSO.",
 ".OOHBBBBBBBBSOO.",
 "..OOOOOOOOOOOO..",
]
frames={"SIT_A":SIT_A,"SIT_B":SIT_B,"HAPPY":HAPPY,"SLEEP_A":SLEEP_A,"SLEEP_B":SLEEP_B}
allowed=set(".OBHSPEKW")
ok=True
for n,g in frames.items():
    if not chk(n,g): ok=False
    for r in g:
        bad=set(r)-allowed
        if bad: print(f"FAIL {n} chars {bad}"); ok=False
print("VALID" if ok else "ERRORS")
vis={".":" ","O":"#","B":"@","H":"o","S":"x","P":"*","E":"e","K":"+","W":"."}
for n,g in frames.items():
    print("---",n,"---")
    for r in g: print("".join(vis[c] for c in r))
