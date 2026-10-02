#!/usr/bin/env python3
# Uso: python3 tools/catart.py  -> valida ancho/alto de los frames y los previsualiza en ASCII.
# Las mismas rejillas estan en src/Swip.App/Art/PixelCat.cs; manten ambas en sync al editar.
# Diseño y validación del pixel art del gato. Cada frame es una lista de filas.
# Leyenda: . transp | o contorno | y amarillo | g sombra | p rosa | w blanco | k pupila

W = 16
H = 16

# Gato sentado, frame A (activo, ojos abiertos)
SIT_A = [
    "......o..o......",
    ".....oyooyo.....",
    "....oyyooyyo....",
    "....oyyyyyyyo...",
    "...oyyyyyyyyyo..",
    "...oywwyyywwyo..",
    "...oykwyyywkyo..",
    "...oyyyyyyyyyo..",
    "...oyyyppyyyyo..",
    "...oyyyyyyyyyo..",
    "...ooyyyyyyyoo..",
    "....oyyyyyyyo...",
    "....oyyyyyyyo...",
    "....oyygyygyo...",
    "....oyoyyoyyo...",
    ".....oo..oo....."
]

# Frame B (activo, parpadeo + cola/patas levemente distinto para dar vida)
SIT_B = [
    "......o..o......",
    ".....oyooyo.....",
    "....oyyooyyo....",
    "....oyyyyyyyo...",
    "...oyyyyyyyyyo..",
    "...oyyyyyyyyyo..",
    "...oykkyyykkyo..",
    "...oyyyyyyyyyo..",
    "...oyyyppyyyyo..",
    "...oyyyyyyyyyo..",
    "...ooyyyyyyyoo..",
    "....oyyyyyyyo...",
    "....oyyyyyyyo...",
    "....oyygyygyo...",
    "....ooyyyyoo....",
    ".....oo..oo....."
]

# Frame feliz (interacción, ojos ^^)
HAPPY = [
    "......o..o......",
    ".....oyooyo.....",
    "....oyyooyyo....",
    "....oyyyyyyyo...",
    "...oyyyyyyyyyo..",
    "...oykyyyykyyo..",
    "...oyykyykyyyo..",
    "...oyyyyyyyyyo..",
    "...oyyyppyyyyo..",
    "...oyyyyyyyyyo..",
    "...ooyyyyyyyoo..",
    "....oyyyyyyyo...",
    "....oyyyyyyyo...",
    "....oyygyygyo...",
    "....oyoyyoyyo...",
    ".....oo..oo....."
]

# Durmiendo frame A (acostado, ojos cerrados)
SLEEP_A = [
    "................",
    "................",
    "................",
    "................",
    "......ooo.......",
    ".....oyyyoo.....",
    "..oooyyyyyyoo...",
    ".oyyyyyyyyyyyo..",
    ".oykkyyyyyykyo..",
    ".oyyyyppyyyyyyo.",
    ".oyyyyyyyyyyyyo.",
    ".ooyyyyyyyyyyoo.",
    "..oooooooooooo..",
    "................",
    "................",
    "................"
]

# Durmiendo frame B (respiración: cuerpo 1px más alto)
SLEEP_B = [
    "................",
    "................",
    "................",
    "......ooo.......",
    ".....oyyyoo.....",
    "....oyyyyyyoo...",
    "..oooyyyyyyyyo..",
    ".oyyyyyyyyyyyo..",
    ".oykkyyyyyykyo..",
    ".oyyyyppyyyyyyo.",
    ".oyyyyyyyyyyyyo.",
    ".ooyyyyyyyyyyoo.",
    "..oooooooooooo..",
    "................",
    "................",
    "................"
]

FRAMES = {
    "SIT_A": SIT_A, "SIT_B": SIT_B, "HAPPY": HAPPY,
    "SLEEP_A": SLEEP_A, "SLEEP_B": SLEEP_B,
}

ok = True
allowed = set(".oygpwk")
for name, f in FRAMES.items():
    if len(f) != H:
        print(f"[FAIL] {name}: {len(f)} filas (esperado {H})"); ok = False
    for i, row in enumerate(f):
        if len(row) != W:
            print(f"[FAIL] {name} fila {i}: ancho {len(row)} (esperado {W}) -> '{row}'"); ok = False
        bad = set(row) - allowed
        if bad:
            print(f"[FAIL] {name} fila {i}: chars inválidos {bad}"); ok = False

print("VALIDACION:", "OK" if ok else "CON ERRORES")
print()
# Vista previa ASCII (o=#, y=@, g=+, p=*, w=., k=o)
vis = {".":" ", "o":"#", "y":"@", "g":"+", "p":"*", "w":"'", "k":"o"}
for name, f in FRAMES.items():
    print(f"--- {name} ---")
    for row in f:
        print("".join(vis[c] for c in row))
    print()
