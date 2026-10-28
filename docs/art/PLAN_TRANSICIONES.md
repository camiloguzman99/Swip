# Plan de acciones y transiciones

Este documento describe cómo se mueve un gato: las 14 acciones, las posturas, las transiciones entre ellas y el ritmo
que debe tener. Lo implementan `Behaviors.cs` (datos) y `World.cs` (cerebro) en `src/Swip.Shared/Engine`, y lo enseña el
Visor de la caja.

## 1. Las 14 acciones

| # | Acción | Clip | Postura de partida → final | Quién | Duración | Peso | Enfriamiento | Arte |
|---|--------|------|----------------------------|-------|----------|------|--------------|------|
| 1 | Sentarse | `sit_look` | Sentado | juega, camina | 10–30 s | 5 | – | listo |
| 2 | Acostarse | `loaf_idle` | Acostado | juega, camina | 15–40 s | 2,5 | – | listo |
| 3 | Ponerse de pie | `stand_idle` | De pie | juega, camina | 4–10 s | 2 | – | listo |
| 4 | Dormir en bolita | `sleep_curl` | Bolita | duerme | 25–70 s | 5 | – | listo |
| 5 | Dormir de lado | `sleep_side` | De lado | duerme | 25–70 s | 3 | – | prestado |
| 6 | Caminar | `walk` | Caminando | camina | 6–14 s | 5 | – | listo |
| 7 | Trotar | `trot` | Trotando | camina | 3–6 s | 1 | 30 s | listo |
| 8 | Correr | `run` | Corriendo | camina | 1,5–3 s | 0,5 | 60 s | listo |
| 9 | Lamerse la pata | `groom_paw` | Sentado | juega, camina | 5–10 s | 2 | 60 s | listo |
| 10 | Saltar al mismo nivel | `jump` | De pie → de pie | camina | un salto | 0,5 | 50 s | listo |
| 11 | Saltar a la caja | `jump_box` | De pie → sentado encima | camina | un salto | 1 | 75 s | listo |
| 12 | Meterse en la caja | `box_enter` | De pie → sentado dentro | camina | un salto | 1,2 | 75 s | prestado |
| 13 | Salirse de la caja | `box_exit` | De pie → de pie (fuera) | camina; siempre si van a cerrar la caja | un salto | 0,6 | 20 s | prestado |
| 14 | Chocar con la caja y empujarla | `box_push` | De pie → de pie | camina | hasta 30 s | 1 | 75 s | prestado |

Además hay tres reacciones que no salen al azar: **aterrizar** (`land`, tras caer), **mirarte** (`attention`, con el menú
abierto) y **colgar** (`carry`, mientras lo arrastras). Acariciarlo solo suelta corazones: no cambia lo que hace.

## 2. Posturas y transiciones

Posturas: **Bolita**, **De lado**, **Acostado**, **Sentado**, **De pie**, **Caminando**, **Trotando**, **Corriendo**.

| De → a | Clip | ¿Al revés? | Estado |
|--------|------|------------|--------|
| Acostado → Sentado | `wake_up` | – | dibujada |
| Sentado → Acostado | `to_sleep` | – | dibujada |
| Sentado → De pie | `sit_to_stand` | – | dibujada |
| De pie → Sentado | `to_sit` | – | dibujada |
| De pie → Caminando | `walk_start_stop` | – | dibujada |
| Caminando → De pie | `walk_start_stop` | – | dibujada (mismo clip: arrancar y detenerse) |
| Caminando → Corriendo | `walk_to_run` | – | dibujada |
| Corriendo → Caminando | `walk_to_run` | sí (↺) | dibujada |
| De pie → Corriendo | `stand_to_run` | – | dibujada |
| Corriendo → De pie | `stand_to_run` | sí (↺) | dibujada |
| Bolita ↔ De lado | – | – | **falta** (corte directo) |
| Bolita ↔ Acostado | – | – | **falta** (corte directo) |
| De lado ↔ Acostado | – | – | **falta** (corte directo) |
| De pie ↔ Trotando | – | – | **falta** (corte directo) |
| Caminando ↔ Trotando | – | – | **falta** (corte directo) |
| Trotando ↔ Corriendo | – | – | **falta** (corte directo) |

Reglas:

1. El motor busca el camino más corto entre la postura en la que está el gato y la que pide la acción elegida
   (`Behaviors.Path`, búsqueda en anchura) y reproduce primero las transiciones y después la acción.
2. Un paso sin clip es un corte directo: se cambia de postura sin animación. Cuando se dibuje, basta añadir la transición
   a `Behaviors.Transitions` y el camino la usará solo.
3. Si lo interrumpen en mitad de una transición (cambio de sesión), se toma la postura del extremo más cercano.
4. Ejemplos de cadenas: dormir en bolita → despertar → levantarse → arrancar a andar → caminar; caminar → detenerse →
   sentarse → lamerse la pata; sentado → levantarse → arrancar a correr → correr; correr (al revés) → caminar →
   detenerse → sentarse → acostarse → bolita.

## 3. Cambios de sesión

| Cambio | Qué hace el gato |
|--------|------------------|
| Cierra la sesión | Va a una postura de dormir pasando por las transiciones (parar, sentarse, acostarse) y duerme |
| Abre la sesión en segundo plano | Despierta (acostado → sentado) y se queda en su sitio: nunca camina |
| Pasa a pantalla (activa) | Se incorpora y sale a pasear: camina, trota, explora la caja |
| Pasa de pantalla a segundo plano | Se detiene, se sienta y se queda quieto |

## 4. Ritmo realista

- Casi todo el tiempo está **quieto**: sentado, acostado o de pie, tramos de 10 a 40 s; dormido, de 25 a 70 s.
- Lo que cansa (**trotar, correr, saltar, asearse, las acciones de la caja**) tiene enfriamiento (columna de la tabla 1) y,
  tras una de ellas, la siguiente se elige entre las que descansan casi siempre (`Active`).
- No repite la acción anterior seguida y prefiere las acciones cuya postura de partida es la que ya tiene.
- El gato de la pantalla activa pasea más y descansa menos que el de segundo plano, pero nunca encadena carrera, salto y
  aseo.
- Las pruebas de `EngineTests` vigilan estas reglas: acciones por minuto, enfriamientos, descanso mayor que esfuerzo y
  que nunca se encadenen dos acciones que cansan.

## 5. La caja

- **Empujar:** el gato trota hasta el lado de la caja, **choca** (la caja se sacude y sale polvo) y la empuja al ritmo de
  las patas hasta que se cansa o llega a la pared.
- **Subirse encima / meterse dentro:** solo si la caja está libre, quieta y con la tapa cerrada (subirse) o abierta
  (meterse). Dentro o encima reposa (sentado, acostado, de pie, aseándose o durmiendo).
- **Salirse:** solo la hace el gato que camina, de vez en cuando.
- **Cerrar la caja con un gato dentro:** si está despierto, primero se levanta (si estaba sentado o acostado) y sale de un
  salto; la tapa no se cierra hasta que ha salido. Si se vuelve a abrir antes, se cancela. Si está dormido, se queda
  encima de la tapa, como siempre.

## 6. Arte que falta

Estas piezas funcionan, pero reutilizan poses de otras acciones (en el Visor aparecen como «pose prestada»):

- `sleep_side`: dormir de lado con el pecho que sube y baja.
- `box_enter` y `box_exit`: asomarse, impulsarse y desaparecer tras el borde / salir y caer fuera.
- `box_push`: empujar con la cabeza y el pecho, con las patas traseras empujando.
- `land`, `carry`.
- Las transiciones sin dibujar de la tabla 2 (bolita ↔ de lado ↔ acostado, de pie ↔ trote ↔ caminar ↔ correr).
