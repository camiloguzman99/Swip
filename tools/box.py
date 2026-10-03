#!/usr/bin/env python3
# Caja de carton 16x16. Slots: . transp | o contorno | b carton | d sombra | l claro |
#   i interior(oscuro) | t cinta/marca
W=H=16
CLOSED=[
 "................",
 "................",
 "................",
 "................",
 "..oooooooooooo..",
 "..olllllllllbo..",
 "..obbbbbbbbbdo..",
 "..obbbbbbbbbdo..",
 "..obbbbbbbbbdo..",
 "..obbbbbbtbbdo..",
 "..obbbbbbtbbdo..",
 "..obbbbbbbbbdo..",
 "..obbbbbbbbbdo..",
 "..obbbbbbbbbdo..",
 "..obbbbbbbbbdo..",
 "..oooooooooooo..",
]
OPEN=[
 "oo..........oo..",
 "oooo......oooo..",
 ".ioooo..ooooi...",
 "..oiioooooiio...",
 "..oiiiiiiiiiio..",
 "..oiiiiiiiiiio..",
 "..oooooooooooo..",
 "..obbbbbbbbbdo..",
 "..obbbbbbbbbdo..",
 "..obbtbbbbbbdo..",
 "..obbbtbbbbbdo..",
 "..obbbbbbbbbdo..",
 "..obbbbbbbbbdo..",
 "..obbbbbbbbbdo..",
 "..obbbbbbbbbdo..",
 "..oooooooooooo..",
]
frames={"CLOSED":CLOSED,"OPEN":OPEN}
allowed=set(".obdlit")
ok=True
for n,g in frames.items():
    if len(g)!=H: print("FAIL rows",n,len(g)); ok=False
    for i,r in enumerate(g):
        if len(r)!=W: print(f"FAIL {n} r{i} len {len(r)}: '{r}'"); ok=False
        bad=set(r)-allowed
        if bad: print(f"FAIL {n} r{i} chars {bad}"); ok=False
print("VALID" if ok else "ERRORS")
vis={".":" ","o":"#","b":"@","d":"x","l":"o","i":".","t":"="}
for n,g in frames.items():
    print("---",n,"---")
    for r in g: print("".join(vis[c] for c in r))
