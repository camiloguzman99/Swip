# Arte pendiente

Generado por `tools/art/build_atlas.py` desde `art/clips.json`. No editar a mano.

Cada clip de esta lista ya funciona en la app con fotogramas de **referencia**, derivados
de las poses dibujadas (dormir, caminar, jugar, cargado) con escalas, giros y desplazamientos.
Son una guía de pose y de tiempos, no el arte final.

**Cómo entregar el arte de un clip:**

1. Dibuja cada fotograma en un lienzo de **269×379 px**, fondo transparente, con los pies
   tocando el borde inferior y el gato centrado (igual que los dibujos actuales).
2. Guárdalos como `art/final/<piel>/<clip>_<n>.png` (piel = `orange` y `gray`; n desde 0).
3. En `art/clips.json` cambia el `status` del clip a `final`.
4. Ejecuta `python3 tools/art/build_atlas.py`.

El script reduce cada fotograma a una **cuadrícula de 80×64 casillas** en T (1 casilla = 1 px
del gato en la app): principal de 48×48 con los pies sobre la base, 16 columnas a cada lado
para correr o estirarse y 16 filas debajo para lo que cuelga. La guarda en letras en `art/grid/<piel>/<clip>.txt` (paleta en
`art/grid/<piel>/paleta.txt`). Esas cuadrículas también se pueden retocar a mano: cambia
`fuente: auto` por `fuente: mano` en el archivo y ya no se regenerará.

Los colores negro, blanco, crema y chocolate se generan solos a partir de la piel naranja:
no hace falta dibujarlos. La hoja de referencia de cada clip está en `docs/art/reference/<clip>.png`.

**Total:** 34 clips, 95 fotogramas por piel (190 contando naranja y atigrado).

| Clip | Fotogramas | Velocidad | Qué debe mostrar | Referencia actual |
|---|---|---|---|---|
| `sleep_turn` | 3 | 6 fps, una vez | Se da la vuelta dormido: se encoge, gira y queda mirando al otro lado. | #0: pose sleep #0, escala 0.95×1.05<br>#1: pose sleep #1, escala 1×1.08, giro -6°<br>#2: pose sleep #0, espejado |
| `sleep_stretch` | 3 | 4 fps, una vez | Estira patas delanteras y traseras sin despertar. | #0: pose sleep #0, escala 1.12×0.92<br>#1: pose sleep #0, escala 1.25×0.85<br>#2: pose sleep #1, escala 1.1×0.95 |
| `sleep_ear` | 3 | 5 fps, una vez | Mueve una oreja dormido. | #0: pose sleep #0<br>#1: pose sleep #0, escala 1×1.03<br>#2: pose sleep #0 |
| `sleep_tail` | 3 | 5 fps, una vez | Sacude la punta de la cola dormido. | #0: pose sleep #0, desplazado -2,0<br>#1: pose sleep #1, desplazado 2,0<br>#2: pose sleep #0 |
| `sleep_dream` | 2 | 8 fps, bucle | Sueña: patitas que tiemblan, bigotes que se mueven. | #0: pose sleep #0, desplazado -1,0<br>#1: pose sleep #0, desplazado 1,0 |
| `sleep_petted` | 4 | 4 fps, una vez | Al acariciarlo abre un ojo, bosteza y vuelve a dormir. | #0: pose sleep #1<br>#1: pose sleep #1, escala 1×1.07<br>#2: pose sleep #1, escala 1×1.07, giro -4°<br>#3: pose sleep #0 |
| `sleep_box` | 2 | 1.1 fps, bucle | Dormido dentro de la caja abierta, asomando orejas. | #0: pose sleep #0, escala 0.9×1<br>#1: pose sleep #1, escala 0.9×1 |
| `play_tail_chase` | 4 | 8 fps, bucle | Persigue su cola dando vueltas en el sitio. | #0: pose walk #0, giro -8°<br>#1: pose walk #1, giro 8°, espejado<br>#2: pose walk #0, giro -8°, espejado<br>#3: pose walk #1, giro 8° |
| `play_hunt_fly` | 3 | 5 fps, bucle | Sigue con la mirada una mosca y le lanza zarpazos. | #0: pose play #0, escala 1×1.05<br>#1: pose play #1, giro -10°<br>#2: pose play #0, giro 6° |
| `play_hop` | 4 | 8 fps, una vez | Se agacha y salta en el sitio. | #0: pose play #0, escala 1.1×0.85<br>#1: pose play #0, escala 0.92×1.12<br>#2: pose play #1, escala 0.95×1.08<br>#3: pose play #0, escala 1.08×0.9 |
| `play_roll` | 4 | 5 fps, una vez | Se tumba y se revuelca panza arriba. | #0: pose sleep #0<br>#1: pose sleep #0, giro 60°<br>#2: pose sleep #1, giro 170°<br>#3: pose sleep #1, escala 1×0.95, giro 175° |
| `groom_paw` | 2 | 3 fps, bucle | Se lame la pata delantera. | #0: pose play #0, giro -6°<br>#1: pose play #1, giro -6° |
| `groom_face` | 2 | 3 fps, bucle | Se limpia la cara con la pata. | #0: pose play #0, giro 6°<br>#1: pose play #1, giro 6° |
| `sit_look` | 2 | 1.2 fps, bucle | Sentado, mirando alrededor. | #0: pose play #0<br>#1: pose play #0, escala 1×1.015 |
| `yawn` | 4 | 4 fps, una vez | Bosteza con la boca muy abierta. | #0: pose play #0<br>#1: pose play #0, escala 0.95×1.08<br>#2: pose play #0, escala 0.95×1.08<br>#3: pose play #0 |
| `knead` | 2 | 4 fps, bucle | Amasa con las patas delanteras. | #0: pose play #0, escala 1×0.97<br>#1: pose play #1 |
| `trot` | 2 | 7 fps, bucle | Trota. | #0: pose walk #0, escala 1.05×1<br>#1: pose walk #1, escala 1.05×1 |
| `run` | 2 | 12 fps, bucle | Corre una ráfaga corta, cuerpo estirado. | #0: pose walk #0, escala 1.12×0.92<br>#1: pose walk #1, escala 1.12×0.92 |
| `sniff` | 2 | 3 fps, bucle | Se para a olfatear el suelo. | #0: pose walk #0, giro 8°<br>#1: pose walk #1, giro 8° |
| `stretch_yoga` | 4 | 3 fps, una vez | Se estira: patas delante, trasero arriba. | #0: pose walk #0, escala 1.2×0.8<br>#1: pose walk #0, escala 1.3×0.74<br>#2: pose walk #0, escala 1.3×0.74<br>#3: pose walk #1 |
| `turn_around` | 2 | 8 fps, una vez | Media vuelta rápida. | #0: pose walk #0, escala 0.7×1<br>#1: pose walk #0, escala 0.55×1, espejado |
| `box_push` | 2 | 6 fps, bucle | Empuja la caja con la cabeza, inclinado hacia delante. | #0: pose walk #0, escala 1.1×1, giro 10°<br>#1: pose walk #1, escala 1.1×1, giro 10° |
| `jump` | 3 | 7 fps, una vez | Salto: se agacha, vuela estirado, aterriza. | #0: pose walk #0, escala 1.1×0.8<br>#1: pose walk #1, escala 1.15×0.9, giro -15°<br>#2: pose walk #0, escala 1.05×0.85 |
| `box_rub` | 2 | 4 fps, bucle | Se frota contra la caja. | #0: pose walk #0, giro -5°<br>#1: pose walk #1, giro 5° |
| `box_scratch` | 3 | 6 fps, bucle | Araña el cartón, estirado hacia arriba. | #0: pose walk #0, giro -14°<br>#1: pose walk #1, giro -14°<br>#2: pose walk #0, giro -9° |
| `box_peek` | 3 | 2 fps, bucle | Dentro de la caja: se asoma y se esconde. | #0: pose play #0, desplazado 0,40<br>#1: pose play #0<br>#2: pose play #0, desplazado 0,40 |
| `box_paw_out` | 2 | 4 fps, bucle | Dentro de la caja: saca una pata por el borde. | #0: pose play #1<br>#1: pose play #0 |
| `ambush_pop` | 3 | 8 fps, una vez | Emboscada: sale de golpe de la caja. | #0: pose play #0, desplazado 0,60<br>#1: pose play #0, escala 1×1.15<br>#2: pose play #0 |
| `cursor_swat` | 3 | 8 fps, una vez | Zarpazo al cursor. | #0: pose play #0<br>#1: pose play #1, giro -15°<br>#2: pose play #0 |
| `petted_happy` | 3 | 6 fps, una vez | Contento al acariciarlo: se estira hacia la mano. | #0: pose play #0<br>#1: pose play #0, escala 1×1.08<br>#2: pose play #1 |
| `wake_up` | 4 | 4 fps, una vez | Se despierta: abre los ojos, se estira y bosteza. | #0: pose sleep #1<br>#1: pose sleep #1, escala 1.15×0.9<br>#2: pose play #0, escala 1×1.05<br>#3: pose play #0 |
| `to_sit` | 2 | 5 fps, una vez | Pasa de caminar a sentarse. | #0: pose walk #0, escala 1×0.9<br>#1: pose play #0 |
| `to_sleep` | 4 | 3 fps, una vez | Bosteza, da una vuelta y se enrosca para dormir. | #0: pose play #0, escala 1×0.95<br>#1: pose play #0, escala 1×0.9<br>#2: pose sleep #1, escala 1.1×0.9<br>#3: pose sleep #0 |
| `land` | 2 | 8 fps, una vez | Aterriza tras caer: se aplasta y se sacude. | #0: pose carry #0, escala 1.15×0.8<br>#1: pose walk #0, escala 1.05×0.9 |

## Ya con arte final

- `sleep_curl`: Dormido enroscado, respiración lenta (base del estado).
- `play_yarn`: Juega con el ovillo en su sitio (base del estado).
- `walk`: Camina despacio (base del estado). Ciclo de 8 fotogramas armado por piezas (tools/art/pieces.py).
- `carry`: Colgando mientras lo arrastras.
