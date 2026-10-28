#!/usr/bin/env python3
"""
Genera el visor de movimientos del gato: UN solo archivo HTML (docs/art/visor_movimientos.html) con el atlas
real incrustado, para ver cada movimiento sin generar GIF.

Qué muestra:
  - la lista de movimientos (clips de art/clips.json) con su estado: listo, pose prestada o falta;
  - las acciones que todavía no tienen arte propio, con el texto listo para pedirlas a Gemini;
  - cada movimiento animado como en el motor (el gato avanza solo cuando cambia el cuadro, con el suelo
    marcado para ver si los pies resbalan), con fps y avance editables y el botón de copiar los ajustes;
  - una secuencia de movimientos para probar las transiciones.

También escribe src/Swip.App/Assets/atlas/visor.json, que lee el Visor de la app (menú de la caja).

Se regenera con:  python3 tools/art/build_viewer.py   (después de tools/art/build_atlas.py)
"""
import base64
import json
import os
import re

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ATLAS = os.path.join(ROOT, "src", "Swip.App", "Assets", "atlas")
OUT = os.path.join(ROOT, "docs", "art", "visor_movimientos.html")
APP_DATA = os.path.join(ATLAS, "visor.json")        # lo lee el Visor de la app (menú de la caja)

# Clips cuyo arte es de verdad la acción (dibujado por Gemini o animado a partir de él).
LISTOS = {
    "walk", "trot", "run", "jump", "jump_box", "to_sit", "sit_to_stand", "to_sleep", "wake_up", "groom_paw",
    "walk_start_stop", "walk_to_run", "stand_to_run", "sleep_curl", "sit_look", "stand_idle", "loaf_idle",
}
# Clips que usan una pose parecida: qué hay que dibujar para que sean buenos.
PRESTADOS = {
    "land": "Aterrizaje amortiguado de verdad (agachado, patas dobladas); ahora usa el final del salto.",
    "sleep_side": "Dormir de lado con las patas estiradas y el pecho que sube y baja; ahora son dos cuadros de la hoja de poses.",
    "box_enter": "Meterse en la caja: salto con la cabeza por delante que desaparece tras el borde; ahora reutiliza el salto.",
    "box_exit": "Salir de la caja: asoma las patas delanteras, se impulsa y cae fuera; ahora es el salto al revés.",
    "box_push": "Chocar con la caja y empujar con la cabeza y el pecho, patas traseras empujando; ahora son cuadros de caminar.",
    "carry": "Colgando de la nuca con las patas encogidas; ahora es el gato de pie.",
}
# Los pasos del grafo de posturas sin clip dibujado (cortes directos) los calcula el Visor desde el motor (Behaviors.Missing).
PRESETS = [
    ("Paseo: arrancar, caminar, trotar y correr", ["walk_start_stop", "walk", "walk", "trot", "walk_to_run", "run", "run", "walk_to_run"]),
    ("Arrancar a correr desde parado", ["stand_to_run", "run", "run", "stand_to_run"]),
    ("Sentarse, dormir y despertar", ["to_sit", "sit_look", "to_sleep", "sleep_curl", "wake_up", "sit_to_stand", "walk_start_stop", "walk"]),
    ("Saltos", ["jump", "land", "jump_box", "land"]),
    ("Aseo", ["to_sit", "groom_paw", "groom_paw", "sit_to_stand"]),
]


def wired_clips():
    """clip -> comportamiento que lo usa (de Behaviors.cs: new("id", "clip", ...) o T("id", "clip", ...)) y los clips de fase."""
    path = os.path.join(ROOT, "src", "Swip.Shared", "Engine", "Behaviors.cs")
    out = {}
    if os.path.exists(path):
        for beh, clip in re.findall(r'(?:new|T)\("([a-z_0-9]+)",\s*"([a-z_0-9]+)"', open(path, encoding="utf-8").read()):
            out.setdefault(clip, beh)
    for phase in ("trot", "walk"):   # fases de otros movimientos
        out.setdefault(phase, "fase de otro movimiento")
    return out


def origin(clip_id, mapping):
    spec = mapping.get(clip_id)
    if not spec:
        return "arte antiguo (referencia)"
    if "proc" in spec:
        return f"Gemini ({spec['proc']['base']}) con las patas animadas por el generador (tools/art/walk_cycle.py)"
    names = [f if isinstance(f, str) else f["f"] for f in spec["frames"]]
    sheets = {"r": "hoja de poses", "g": "hoja de giros", "t": "hoja de transiciones"}
    used = sorted({sheets[n[0]] for n in names})
    return f"Gemini · {', '.join(used)} · cuadros {', '.join(dict.fromkeys(names))}"


def main():
    meta = json.load(open(os.path.join(ATLAS, "atlas.json"), encoding="utf-8"))
    clips_json = json.load(open(os.path.join(ROOT, "art", "clips.json"), encoding="utf-8"))["clips"]
    mp = os.path.join(ROOT, "art", "orange_map.json")
    mapping = json.load(open(mp, encoding="utf-8")) if os.path.exists(mp) else {}
    wired = wired_clips()
    clips = []
    for c in clips_json:
        quality = "ok" if c["id"] in LISTOS else "prestada"
        clips.append({
            "id": c["id"], "fps": c["fps"], "loop": c["loop"], "advance": c.get("advance"),
            "desc": c["description"], "quality": quality, "note": PRESTADOS.get(c["id"], ""),
            "origin": origin(c["id"], mapping), "wired": wired.get(c["id"], ""),
            "frames": [f["cell"] for f in meta["clips"][c["id"]]["frames"]],
        })
    skins = {}
    for skin, file in meta["skins"].items():
        with open(os.path.join(ATLAS, file), "rb") as f:
            skins[skin] = "data:image/png;base64," + base64.b64encode(f.read()).decode("ascii")
    ids = {c["id"] for c in clips}
    presets = [{"name": n, "clips": [i for i in cl if i in ids]} for n, cl in PRESETS]
    needed = []
    data = {"meta": {"cell": meta["cell"], "base": meta["base"], "main": meta["main"], "columns": meta["columns"]},
            "clips": clips, "needed": needed, "presets": presets, "skins": skins}
    template = open(os.path.join(os.path.dirname(__file__), "visor_template.html"), encoding="utf-8").read()
    html = template.replace("/*DATA*/null", json.dumps(data, ensure_ascii=False, separators=(",", ":")))
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        f.write(html)
    # Datos para el Visor de la app (los fps, el avance y los cuadros salen del motor y del atlas, no de aquí).
    app = {
        "clips": {c["id"]: {"quality": c["quality"], "note": c["note"], "origin": c["origin"],
                            "wired": c["wired"], "desc": c["desc"]} for c in clips},
        }
    with open(APP_DATA, "w", encoding="utf-8") as f:
        json.dump(app, f, ensure_ascii=False, indent=1)
    print(f"{OUT}: {len(html) / 1e6:.1f} MB, {len(clips)} clips "
          f"({sum(c['quality'] == 'ok' for c in clips)} listos), {len(needed)} acciones sin arte")


if __name__ == "__main__":
    main()
