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

El script reduce cada fotograma a una **cuadrícula de 180×130 casillas** en T (1 casilla = 1 px
del gato en la app): principal de 100×100 con los pies sobre la base, 40 columnas a cada lado
para correr o estirarse y 30 filas debajo para lo que cuelga. La guarda en letras en `art/grid/<piel>/<clip>.txt` (paleta en
`art/grid/<piel>/paleta.txt`). Esas cuadrículas también se pueden retocar a mano: cambia
`fuente: auto` por `fuente: mano` en el archivo y ya no se regenerará.

Cada gato tiene su propio dibujo (naranja y atigrado); no hay más colores ni variantes.
La hoja de referencia de cada clip está en `docs/art/reference/<clip>.png`.

**Total:** 20 clips, 130 fotogramas por piel (260 contando naranja y atigrado).

| Clip | Fotogramas | Velocidad | Qué debe mostrar | Referencia actual |
|---|---|---|---|---|
| `sit_look` | 2 | 1.2 fps, bucle | Sentado, mirando alrededor. | #0: pose play #0<br>#1: pose play #0, escala 1×1.015 |
| `stand_idle` | 2 | 1.0 fps, bucle | De pie quieto, visto de lado (acción básica: punto de partida de las transiciones). | #0: pose walk #0<br>#1: pose walk #0 |
| `loaf_idle` | 2 | 1.2 fps, bucle | Acostado en pan, visto de lado, respirando (acción básica). | #0: pose walk #0<br>#1: pose walk #0 |
| `trot` | 4 | 9 fps, bucle | Trota. | #0: pose walk #0, escala 1.05×1<br>#1: pose walk #0, escala 1.05×1<br>#2: pose walk #1, escala 1.05×1<br>#3: pose walk #1, escala 1.05×1 |
| `run` | 7 | 12 fps, bucle | Corre una ráfaga corta, cuerpo estirado. | #0: pose walk #0, escala 1.12×0.92<br>#1: pose walk #0, escala 1.12×0.92<br>#2: pose walk #0, escala 1.12×0.92<br>#3: pose walk #0, escala 1.12×0.92<br>#4: pose walk #1, escala 1.12×0.92<br>#5: pose walk #1, escala 1.12×0.92<br>#6: pose walk #1, escala 1.12×0.92 |
| `groom_paw` | 8 | 4 fps, bucle | Se lame la pata delantera. | #0: pose play #0, giro -6°<br>#1: pose play #0, giro -6°<br>#2: pose play #0, giro -6°<br>#3: pose play #0, giro -6°<br>#4: pose play #1, giro -6°<br>#5: pose play #1, giro -6°<br>#6: pose play #1, giro -6°<br>#7: pose play #1, giro -6° |
| `jump` | 8 | 12 fps, una vez | Salto: se agacha, vuela estirado, aterriza. | #0: pose walk #0, escala 1.1×0.8<br>#1: pose walk #0, escala 1.1×0.8<br>#2: pose walk #0, escala 1.1×0.8<br>#3: pose walk #1, escala 1.15×0.9, giro -15°<br>#4: pose walk #1, escala 1.15×0.9, giro -15°<br>#5: pose walk #1, escala 1.15×0.9, giro -15°<br>#6: pose walk #0, escala 1.05×0.85<br>#7: pose walk #0, escala 1.05×0.85 |
| `land` | 2 | 8 fps, una vez | Aterriza tras caer: se aplasta y se sacude. | #0: pose carry #0, escala 1.15×0.8<br>#1: pose walk #0, escala 1.05×0.9 |
| `box_push` | 2 | 6 fps, bucle | Empuja la caja con la cabeza, inclinado hacia delante. | #0: pose walk #0, escala 1.1×1, giro 10°<br>#1: pose walk #1, escala 1.1×1, giro 10° |
| `to_sit` | 10 | 8 fps, una vez | Pasa de caminar a sentarse (de pie -> sentado en 10 fotogramas). | #0: pose walk #0, escala 1×0.9<br>#1: pose walk #0, escala 1×0.9<br>#2: pose walk #0, escala 1×0.9<br>#3: pose walk #0, escala 1×0.9<br>#4: pose walk #0, escala 1×0.9<br>#5: pose play #0<br>#6: pose play #0<br>#7: pose play #0<br>#8: pose play #0<br>#9: pose play #0 |
| `sit_to_stand` | 10 | 8 fps, una vez | Se levanta: de sentado a de pie (10 fotogramas). | #0: pose walk #0<br>#1: pose walk #0<br>#2: pose walk #0<br>#3: pose walk #0<br>#4: pose walk #0<br>#5: pose walk #0<br>#6: pose walk #0<br>#7: pose walk #0<br>#8: pose walk #0<br>#9: pose walk #0 |
| `to_sleep` | 10 | 6 fps, una vez | Pasa de sentado a acostado (10 fotogramas); después se enrosca. | #0: pose play #0, escala 1×0.95<br>#1: pose play #0, escala 1×0.95<br>#2: pose play #0, escala 1×0.95<br>#3: pose play #0, escala 1×0.9<br>#4: pose play #0, escala 1×0.9<br>#5: pose sleep #1, escala 1.1×0.9<br>#6: pose sleep #1, escala 1.1×0.9<br>#7: pose sleep #1, escala 1.1×0.9<br>#8: pose sleep #0<br>#9: pose sleep #0 |
| `wake_up` | 10 | 6 fps, una vez | Pasa de acostado a sentado (10 fotogramas). | #0: pose sleep #1<br>#1: pose sleep #1<br>#2: pose sleep #1<br>#3: pose sleep #1, escala 1.15×0.9<br>#4: pose sleep #1, escala 1.15×0.9<br>#5: pose play #0, escala 1×1.05<br>#6: pose play #0, escala 1×1.05<br>#7: pose play #0, escala 1×1.05<br>#8: pose play #0<br>#9: pose play #0 |
| `walk_start_stop` | 10 | 8 fps, una vez | De pie, echa a andar y se detiene (10 fotogramas). | #0: pose walk #0<br>#1: pose walk #0<br>#2: pose walk #0<br>#3: pose walk #0<br>#4: pose walk #0<br>#5: pose walk #0<br>#6: pose walk #0<br>#7: pose walk #0<br>#8: pose walk #0<br>#9: pose walk #0 |
| `walk_to_run` | 9 | 10 fps, una vez | Pasa de caminar a correr (9 fotogramas). | #0: pose walk #0<br>#1: pose walk #0<br>#2: pose walk #0<br>#3: pose walk #0<br>#4: pose walk #0<br>#5: pose walk #0<br>#6: pose walk #0<br>#7: pose walk #0<br>#8: pose walk #0 |
| `stand_to_run` | 8 | 10 fps, una vez | De pie, arranca a correr (8 fotogramas). | #0: pose walk #0<br>#1: pose walk #0<br>#2: pose walk #0<br>#3: pose walk #0<br>#4: pose walk #0<br>#5: pose walk #0<br>#6: pose walk #0<br>#7: pose walk #0 |
| `sleep_side` | 2 | 1.1 fps, bucle | Dormido de lado, respirando despacio. | #0: pose sleep #0<br>#1: pose sleep #1 |
| `jump_box` | 8 | 12 fps, una vez | Salto a la caja: se agacha, sube con las patas delanteras y cae encima. | #0: pose walk #0<br>#1: pose walk #0<br>#2: pose walk #0<br>#3: pose walk #0<br>#4: pose walk #0<br>#5: pose walk #0<br>#6: pose walk #0<br>#7: pose walk #0 |
| `box_enter` | 8 | 12 fps, una vez | Se mete en la caja abierta: salta y desaparece hacia dentro. | #0: pose walk #0, escala 1.1×0.8<br>#1: pose walk #0, escala 1.1×0.8<br>#2: pose walk #0, escala 1.1×0.8<br>#3: pose walk #1, escala 1.15×0.9, giro -15°<br>#4: pose walk #1, escala 1.15×0.9, giro -15°<br>#5: pose walk #1, escala 1.15×0.9, giro -15°<br>#6: pose walk #0, escala 1.05×0.85<br>#7: pose walk #0, escala 1.05×0.85 |
| `box_exit` | 8 | 12 fps, una vez | Sale de la caja abierta: asoma y salta fuera. | #0: pose walk #0, escala 1.1×0.8<br>#1: pose walk #0, escala 1.1×0.8<br>#2: pose walk #0, escala 1.1×0.8<br>#3: pose walk #1, escala 1.15×0.9, giro -15°<br>#4: pose walk #1, escala 1.15×0.9, giro -15°<br>#5: pose walk #1, escala 1.15×0.9, giro -15°<br>#6: pose walk #0, escala 1.05×0.85<br>#7: pose walk #0, escala 1.05×0.85 |

## Ya con arte final

- `sleep_curl`: Dormido enroscado, respiración lenta (base del estado).
- `walk`: Camina despacio (base del estado). Ciclo de 8 fotogramas armado por piezas (tools/art/pieces.py).
- `carry`: Colgando mientras lo arrastras.
