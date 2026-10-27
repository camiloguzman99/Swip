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
    "walk", "trot", "run", "jump", "jump_high", "to_sit", "sit_to_stand", "to_sleep", "wake_up", "groom_paw",
    "walk_start_stop", "walk_to_run", "stand_to_run", "turn_sit8", "turn_stand8", "turn_loaf8", "sleep_curl",
    "sit_look",
}
# Clips que usan una pose parecida: qué hay que dibujar para que sean buenos.
PRESTADOS = {
    "land": "Aterrizaje amortiguado de verdad (agachado, patas dobladas); ahora usa el final del salto.",
    "groom_face": "Lavarse la cara con las dos patas; ahora repite la pose de lamerse la pata.",
    "turn_around": "Giro de 180° con los pasos intermedios; ahora salta de perfil a frente.",
    "play_yarn": "Manotear un ovillo con ambas patas; ahora reutiliza lamerse la pata.",
    "cursor_swat": "Zarpazo hacia arriba y hacia delante; ahora reutiliza lamerse la pata.",
    "box_scratch": "Arañar la pared de la caja de pie; ahora reutiliza lamerse la pata.",
    "box_peek": "Asomarse con solo cabeza y patas delanteras sobre el borde; ahora es el gato sentado hundido.",
    "box_paw_out": "Sacar una pata por encima del borde; ahora es el gato lamiéndose, hundido.",
    "ambush_pop": "Saltar de golpe desde dentro de la caja; ahora mezcla pose sentada y agachada.",
    "box_push": "Empujar con la cabeza y el pecho, patas traseras empujando; ahora son cuadros de caminar.",
    "play_hop": "Brincos de juego con cuerpo arqueado; ahora son cuadros del salto.",
    "play_hunt_fly": "Saltar y atrapar con las patas delanteras; ahora son cuadros del salto alto.",
    "sleep_turn": "Girarse dormido a otro lado; ahora espeja la bolita.",
    "sleep_stretch": "Estirarse acostado; ahora alterna bolita y pan.",
    "sleep_ear": "Mover la oreja dormido; ahora es la bolita sin cambios visibles.",
    "sleep_tail": "Mover la cola dormido; ahora es la bolita sin cambios visibles.",
    "sleep_dream": "Soñar: patas y bigotes que se mueven; ahora son dos cuadros de la bolita.",
    "sleep_petted": "Dormido recibiendo caricias (se estira y ronronea); ahora son cuadros de la bolita.",
    "sleep_box": "Dormido dentro de la caja, apoyado en el borde; ahora es la bolita normal.",
    "play_tail_chase": "Perseguirse la cola dando vueltas; ahora son cuadros sentado y de frente.",
    "play_roll": "Rodar panza arriba; ahora espeja la bolita.",
    "yawn": "Bostezo con la boca abierta; ahora es el gato sentado con los ojos cerrados.",
    "knead": "Amasar alternando las patas delanteras; ahora son dos cuadros de acostado en pan.",
    "sniff": "Caminar con la nariz pegada al suelo; ahora son cuadros de caminar y sentado.",
    "stretch_yoga": "Estirarse hacia delante con el trasero en alto; ahora mezcla poses de pie y saltando.",
    "box_rub": "Frotarse contra la caja con el costado; ahora son cuadros sentado.",
    "petted_happy": "Cabeza hacia la mano, ojos entrecerrados; ahora son poses sentado.",
    "carry": "Colgando de la nuca con las patas encogidas; ahora es el gato de pie.",
}
# Acciones sin ningún clip: grupo, nombre, cuadros, detalle.
FALTAN = [
    ("Dormir", "Dormir de lado", 6, "Acostado de lado con las patas estiradas, respirando despacio."),
    ("Dormir", "Dormir estirado (panza arriba)", 6, "Boca arriba con las patas en el aire; respira y mueve una pata."),
    ("Dormir", "Respirar acostado en pan", 6, "Acostado con las patas escondidas; el pecho sube y baja, la cola se mueve."),
    ("Aseo", "Lamerse el pecho", 6, "Sentado, baja la cabeza y lame el pecho."),
    ("Aseo", "Lamerse la pata trasera", 6, "Sentado, levanta una pata trasera y la lame."),
    ("Aseo", "Rascarse la oreja con la pata trasera", 6, "Sentado, rasca la oreja rápidamente con la pata trasera."),
    ("Aseo", "Lavarse la cara con las dos patas", 6, "Sentado, se frota la cara con las patas delanteras."),
    ("Quieto", "Mover la punta de la cola", 6, "Sentado o de pie, la cola ondula suavemente."),
    ("Quieto", "Mover las orejas y parpadear", 6, "Las orejas giran y los ojos se cierran un instante."),
    ("Quieto", "Mirar arriba y abajo", 6, "Gira la cabeza hacia arriba y hacia abajo sin mover el cuerpo."),
    ("Quieto", "Bostezar", 8, "Sentado, abre la boca del todo y la cierra."),
    ("Quieto", "Estirarse y arquear la espalda", 8, "De pie, se estira hacia delante y luego arquea la espalda."),
    ("Juego", "Acechar y menear el trasero", 8, "Agachado, con el trasero que se menea antes de saltar."),
    ("Juego", "Manotear", 6, "De pie o sentado, lanza una pata delantera hacia delante."),
    ("Juego", "Rodar panza arriba", 8, "Se tumba de costado y se revuelve boca arriba."),
    ("Juego", "Perseguirse la cola", 8, "Gira sobre sí mismo intentando alcanzar la cola."),
    ("Juego", "Saltar a atrapar", 8, "Salta con las patas delanteras extendidas hacia arriba."),
    ("Caja", "Asomarse a la caja", 6, "Solo cabeza y patas delanteras sobre el borde."),
    ("Caja", "Sacar una pata de la caja", 6, "Una pata por encima del borde, explorando."),
    ("Caja", "Saltar dentro de la caja", 8, "Salto con aterrizaje dentro; se ve de lado."),
    ("Caja", "De pie dentro de la caja", 6, "Gato de pie con solo la parte alta sobre el borde."),
    ("Caja", "Amasar", 6, "Acostado en pan, alterna las patas delanteras."),
    ("Caja", "Frotarse contra la caja", 8, "Camina pegado, frotando costado y cabeza."),
    ("Moverse", "Caminar de frente y de espaldas", 8, "Dos filas de 8 cuadros: hacia la cámara y alejándose."),
    ("Moverse", "Girar en el sitio", 6, "Perfil derecho → frente → perfil izquierdo."),
    ("Moverse", "Olfatear caminando", 8, "Camina con la cabeza baja, nariz al suelo."),
    ("Moverse", "De pie a acostado directo", 8, "De pie, se agacha y se acuesta sin pasar por sentado."),
    ("Especiales", "Colgando por debajo de la base", 6, "Sentado en el borde con la cola y las patas colgando por debajo."),
    ("Especiales", "Cargado de la nuca", 4, "Colgado de la nuca, patas encogidas, cola caída."),
    ("Especiales", "Recibir caricias", 6, "Sentado con los ojos cerrados y la cabeza hacia la mano."),
]
PRESETS = [
    ("Paseo: arrancar, caminar, trotar y correr", ["walk_start_stop", "walk", "walk", "trot", "walk_to_run", "run", "run", "walk"]),
    ("Arrancar a correr desde parado", ["stand_to_run", "run", "run", "walk_to_run"]),
    ("Sentarse, dormir y despertar", ["to_sit", "sit_look", "to_sleep", "sleep_curl", "wake_up", "sit_to_stand", "walk"]),
    ("Saltos", ["jump", "land", "jump_high", "land"]),
    ("Aseo", ["to_sit", "groom_paw", "groom_paw", "sit_to_stand"]),
]


def wired_clips():
    """clip -> comportamiento que lo usa (de Behaviors.cs: new("id", "clip", ...)) y los clips de fase."""
    path = os.path.join(ROOT, "src", "Swip.Shared", "Engine", "Behaviors.cs")
    out = {}
    if os.path.exists(path):
        for beh, clip in re.findall(r'new\("([a-z_0-9]+)",\s*"([a-z_0-9]+)"', open(path, encoding="utf-8").read()):
            out.setdefault(clip, beh)
    for phase in ("trot", "walk", "sit_look", "box_peek", "sniff", "carry"):   # fases de otros movimientos
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
    needed = [{"group": g, "name": n, "frames": k, "detail": d} for g, n, k, d in FALTAN]
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
        "needed": needed, "presets": presets,
    }
    with open(APP_DATA, "w", encoding="utf-8") as f:
        json.dump(app, f, ensure_ascii=False, indent=1)
    print(f"{OUT}: {len(html) / 1e6:.1f} MB, {len(clips)} clips "
          f"({sum(c['quality'] == 'ok' for c in clips)} listos), {len(needed)} acciones sin arte")


if __name__ == "__main__":
    main()
