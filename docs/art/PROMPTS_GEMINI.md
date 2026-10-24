# Prompts para generar el arte de Swip con Gemini

Arte 2D de los gatos, **4 o 6 fotogramas por acción**. Hay 38 acciones; cada una tiene su prompt
listo para copiar. Los prompts están en inglés porque los generadores de imágenes siguen
mejor las instrucciones de composición en inglés; la explicación de cada uno está en español.

---

## 1. Cómo trabajar (léelo antes de empezar)

1. **Primero la ficha del personaje** (sección 3). Genera una por gato (naranja y atigrado).
   Elige la que más te guste y guárdala: es la referencia de TODO lo demás.
2. **Después, las 3 poses base** (sección 4): de pie, sentado y enroscado. Casi todas las
   acciones empiezan o terminan en una de ellas; si coinciden, las transiciones encajan solas.
3. **Para cada acción**: abre un chat (o sigue en el mismo), **adjunta la ficha del personaje
   y la pose base indicada** y pega el prompt. Pide una sola imagen con todos los fotogramas.
4. Si un fotograma sale mal, pide solo la corrección ("redo frame 3: the tail must…"),
   adjuntando la imagen generada, en vez de regenerarlo todo.
5. Guarda cada resultado como `gemini/<piel>/<accion>.png` (por ejemplo
   `gemini/orange/yawn.png`) y pásamelo. **Yo me encargo de**: quitar el fondo magenta,
   cortar los fotogramas, igualar el tamaño entre acciones, apoyar los pies en la misma línea,
   ajustar la velocidad de cada clip y meterlos en la app.

**Formatos de la cuadrícula** (elige este formato de imagen en Gemini si te deja):

| Fotogramas | Cuadrícula | Formato |
|---|---|---|
| 4 | 2 columnas × 2 filas | 1:1 (cuadrado) |
| 6 | 3 columnas × 2 filas | 3:2 (horizontal) |

Los fotogramas se leen de izquierda a derecha y de arriba abajo.

**Reglas que ya van dentro de cada prompt** (por si tienes que corregir algo a mano):
- El gato mira siempre **a la derecha** (la app lo voltea cuando camina hacia la izquierda).
- **Sin caja**: la caja la dibuja la app. En las acciones con caja se describe dónde estaría.
- **Sin desplazarse**: la app mueve al gato por la pantalla; cada fotograma es la pose en su sitio.
- Fondo **magenta liso (#FF00FF)**, sin suelo, sin sombra, sin texto ni números.
- Mismo tamaño de gato en todas las celdas y en todas las acciones.

**Cambiar el estilo**: el estilo está en la primera línea de cada prompt (`STYLE: …`).
Si quieres otro estilo 2D (por ejemplo dibujo plano tipo cartoon en vez de pixel art),
cambia esa línea en la ficha del personaje y en todos los prompts por la misma frase.

---

## 2. Bloque común (ya incluido en cada prompt)

```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
```

---

## 3. Ficha del personaje (una por gato)

### 3a. Gato naranja
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Character model sheet of a chubby orange tabby cat for a 2D desktop pet game. Big round head, round soft body, short legs, long upright tail. Bright orange fur with darker orange tiger stripes on the back, head and tail; cream-white muzzle, chin, chest, belly and paws; pink nose and pink inner ears; large dark eyes with a small white highlight.
Show, on a solid flat magenta background (#FF00FF), in one row, all at the same scale and standing on the same baseline: 1) side view facing right, standing; 2) three-quarter front view; 3) front view; 4) back view; 5) head close-ups: eyes open, eyes closed (sleeping), mouth wide open (yawning), happy closed-eye smile. No text, no numbers, no shadow.
```

### 3b. Gato atigrado gris
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Character model sheet of a chubby brown-gray tabby cat for a 2D desktop pet game. Same body proportions as the attached orange cat (big round head, round soft body, short legs, long tail with a gentle curve). Gray-brown fur with dark brown tabby stripes, an "M" mark on the forehead, lighter beige chest and belly, yellow-amber eyes with a small highlight, pink nose and pink inner ears.
Show, on a solid flat magenta background (#FF00FF), in one row, all at the same scale and standing on the same baseline: 1) side view facing right, standing; 2) three-quarter front view; 3) front view; 4) back view; 5) head close-ups: eyes open, eyes closed (sleeping), mouth wide open (yawning), happy closed-eye smile. No text, no numbers, no shadow.
```
(Adjunta la ficha del naranja para que los dos gatos tengan las mismas proporciones.)

### 3c. Caja de cartón (opcional, si también quieres renovar la caja)
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing.
Two sprites of the same cardboard box for a 2D desktop pet game, side view, same scale, on a solid flat magenta background (#FF00FF): 1) closed box with a strip of packing tape on the top; 2) the same box open, top flaps folded outward, dark interior visible. The box is wide enough for a chubby cat to sit inside (the cat from the attached character sheet should fit with only its head and front paws showing). No text, no shadow.
```

---

## 4. Poses base (enlaces entre acciones)

Genera estas tres primero; luego adjúntalas cuando una acción diga "Pose base".

### 4a. De pie (`pose_stand`)
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference. Single sprite, side view facing RIGHT, solid flat magenta background (#FF00FF), no ground, no shadow, no text.
The cat stands relaxed on all four legs, head up looking forward, tail raised upright with a slight curl at the tip.
```

### 4b. Sentado (`pose_sit`)
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference. Single sprite, side view facing RIGHT (body slightly turned toward the viewer so the face is visible), solid flat magenta background (#FF00FF), no ground, no shadow, no text.
The cat sits upright on its haunches, front paws together, tail wrapped around the front paws, eyes open, calm.
```

### 4c. Enroscado durmiendo (`pose_curl`)
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference. Single sprite, side view, head on the RIGHT side, solid flat magenta background (#FF00FF), no ground, no shadow, no text.
The cat sleeps curled into a round donut shape, eyes closed, chin resting on its paws, tail wrapped around the body up to the nose.
```

---

## 5. Acciones

Cada prompt incluye ya el bloque común. "Pose base" = qué imagen adjuntar además de la ficha.
"Bucle" = el último fotograma debe enlazar con el primero; "una vez" = se reproduce y termina.

### Durmiendo

#### `sleep_curl` — Dormir enroscado · bucle · 4 fotogramas · Pose base: enroscado
Respiración lenta; es el estado de reposo, el movimiento debe ser muy sutil.
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 2x2 grid, 4 frames, read left-to-right, top-to-bottom. Looping animation: frame 4 must flow smoothly back into frame 1.
Animation "sleeping curled, slow breathing". The cat sleeps curled in a round donut, eyes closed, chin on paws, tail around the body (exactly the attached curled pose).
Frame 1: curled, body at rest. Frame 2: inhaling, the back rises very slightly. Frame 3: full inhale, back one pixel-step higher, ears relaxed. Frame 4: exhaling, back lowering toward frame 1.
```

#### `sleep_turn` — Darse la vuelta dormido · una vez · 6 fotogramas · Pose base: enroscado
Termina mirando a la izquierda: la app voltea el sprite al acabar.
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 3 columns x 2 rows, 6 frames, read left-to-right, top-to-bottom. Plays once.
Animation "turning over while asleep". Eyes stay closed the whole time.
Frame 1: curled donut, head on the right (the attached curled pose). Frame 2: the curl loosens, head lifts slightly, paws stretch a little. Frame 3: lying on its side, legs relaxed, belly half visible. Frame 4: rolling over the back, belly up, paws folded loosely in the air. Frame 5: lying on the other side, head now on the LEFT. Frame 6: curled donut again but mirrored, head on the LEFT (mirror image of frame 1).
```

#### `sleep_stretch` — Estirarse dormido · una vez · 4 fotogramas · Pose base: enroscado
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 2x2 grid, 4 frames, read left-to-right, top-to-bottom. Plays once and ends in the starting pose.
Animation "stretching without waking up". Eyes closed in every frame.
Frame 1: curled donut asleep. Frame 2: lying on its side, front paws reaching forward to the right, toes spread. Frame 3: full-body stretch, front paws forward and hind legs pushed back, body long, mouth slightly open. Frame 4: curled donut again, exactly like frame 1.
```

#### `sleep_ear` — Mover una oreja dormido · una vez · 4 fotogramas · Pose base: enroscado
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 2x2 grid, 4 frames, read left-to-right, top-to-bottom. Plays once and ends in the starting pose.
Animation "ear twitch while asleep". Body and eyes stay exactly the same (curled, eyes closed); only the ear nearest the viewer moves.
Frame 1: curled asleep, ears normal. Frame 2: the ear flattens backward. Frame 3: the ear flicks forward and slightly sideways. Frame 4: ear back to normal, identical to frame 1.
```

#### `sleep_tail` — Sacudir la cola dormido · una vez · 4 fotogramas · Pose base: enroscado
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 2x2 grid, 4 frames, read left-to-right, top-to-bottom. Plays once and ends in the starting pose.
Animation "tail-tip flick while asleep". The body stays curled with eyes closed; only the tip of the tail moves.
Frame 1: curled asleep, tail wrapped around. Frame 2: the tail tip lifts up off the body. Frame 3: the tail tip flicks and curls into a small hook. Frame 4: tail tip back down, identical to frame 1.
```

#### `sleep_dream` — Soñar · bucle · 4 fotogramas · Pose base: enroscado
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 2x2 grid, 4 frames, read left-to-right, top-to-bottom. Looping animation: frame 4 must flow smoothly back into frame 1.
Animation "dreaming". The cat sleeps lying on its side (loosely curled), eyes closed, and its paws twitch as if running in a dream; whiskers and mouth twitch slightly.
Frame 1: front paw forward, back paw back. Frame 2: paws switching, small twitch of the whiskers. Frame 3: front paw back, back paw forward, mouth slightly open. Frame 4: paws switching back toward frame 1.
```

#### `sleep_petted` — Acariciado mientras duerme · una vez · 6 fotogramas · Pose base: enroscado
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 3 columns x 2 rows, 6 frames, read left-to-right, top-to-bottom. Plays once and ends in the starting pose.
Animation "petted while sleeping". No hand is drawn.
Frame 1: curled asleep. Frame 2: one eye opens halfway, sleepy. Frame 3: lifts the head a little, both eyes half open. Frame 4: big yawn, mouth wide open, eyes squeezed shut. Frame 5: closes the mouth with a happy content smile, eyes closed. Frame 6: curled asleep again, identical to frame 1.
```

#### `sleep_box` — Dormir dentro de la caja · bucle · 4 fotogramas · Pose base: enroscado
La caja la dibuja la app delante: solo se verá lo que asome por encima del borde (cabeza y orejas).
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 2x2 grid, 4 frames, read left-to-right, top-to-bottom. Looping animation: frame 4 must flow smoothly back into frame 1.
Animation "sleeping inside a box". Do NOT draw the box: the game draws it in front of the cat and hides the lower half. The cat sleeps in a tall "loaf" position (sitting tucked, paws hidden under the chest), eyes closed, chin resting forward, so that its head and ears would stick out above the rim of a box.
Frame 1: loaf asleep. Frame 2: inhaling, head rises very slightly. Frame 3: an ear twitches. Frame 4: exhaling, back toward frame 1.
```

### Jugando

#### `play_yarn` — Jugar con el ovillo · bucle · 6 fotogramas · Pose base: sentado
Es la única acción donde se dibuja el ovillo.
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 3 columns x 2 rows, 6 frames, read left-to-right, top-to-bottom. Looping animation: frame 6 must flow smoothly back into frame 1.
Animation "playing with a ball of yarn". Include a small blue yarn ball with a short loose strand, in front of the cat's front paws, on the same baseline. The cat sits (as in the attached sitting pose) and plays with it, eyes wide and playful.
Frame 1: sitting, yarn ball in front of the paws. Frame 2: raises the front paw over the ball. Frame 3: taps the ball, it rolls slightly forward. Frame 4: leans forward and grabs the ball with both front paws. Frame 5: nibbles the ball, holding it. Frame 6: lets go, ball back in front of the paws, returning to frame 1.
```

#### `play_tail_chase` — Perseguirse la cola · bucle · 6 fotogramas · Pose base: de pie
Gira sobre sí mismo: única acción que pasa por vista de frente y de espaldas.
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place: it spins on the spot.
Layout: 3 columns x 2 rows, 6 frames, read left-to-right, top-to-bottom. Looping animation: frame 6 must flow smoothly back into frame 1.
Animation "chasing its own tail, spinning in place", a full turn in 6 views, body curved into a C shape trying to reach the tail, playful wide eyes.
Frame 1: side view facing right, head turned back toward its tail. Frame 2: three-quarter front view, body curving. Frame 3: front view, tail swinging to one side, paw reaching for it. Frame 4: side view facing LEFT, head turned back toward the tail. Frame 5: back view, tail toward the viewer. Frame 6: three-quarter back view turning back to the right.
```

#### `play_hunt_fly` — Cazar una mosca · bucle · 6 fotogramas · Pose base: sentado
La mosca la dibuja la app como partícula: no la dibujes.
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 3 columns x 2 rows, 6 frames, read left-to-right, top-to-bottom. Looping animation: frame 6 must flow smoothly back into frame 1.
Animation "hunting an invisible fly". Do not draw the fly. Big round pupils, ears forward, focused.
Frame 1: crouched low, eyes tracking something above and in front. Frame 2: head tilts up to follow it, rear end wiggling. Frame 3: rises onto the hind legs, front paw swiping up and forward. Frame 4: the other front paw swipes, both paws in the air. Frame 5: lands back on four paws, looking up. Frame 6: crouching again, returning to frame 1.
```

#### `play_hop` — Saltar en el sitio · una vez · 6 fotogramas · Pose base: de pie
También se usa para "invitar a jugar" a otro gato. La altura del salto la pone la app.
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth. Even the airborne frames keep the cat at the same height in the cell: the game moves it up and down.
Layout: 3 columns x 2 rows, 6 frames, read left-to-right, top-to-bottom. Plays once and ends standing.
Animation "playful hop in place", happy excited face.
Frame 1: standing, rear end wiggling, ready. Frame 2: deep crouch, legs bent, ears back. Frame 3: take-off, legs extended pushing off, body stretched upward. Frame 4: airborne, all four paws tucked under the round body, tail up. Frame 5: landing, front paws reaching down, legs extending. Frame 6: landed in a small squash, then standing.
```

#### `play_roll` — Revolcarse panza arriba · una vez · 6 fotogramas · Pose base: sentado
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 3 columns x 2 rows, 6 frames, read left-to-right, top-to-bottom. Plays once and ends sitting.
Animation "rolling over on its back, belly up", happy face.
Frame 1: sitting. Frame 2: flops down onto its side. Frame 3: rolls onto its back, belly up, all four paws curled in the air. Frame 4: wiggles on its back, head tilted, paws bent, eyes closed happily. Frame 5: rolls back onto its side. Frame 6: sits up again, like frame 1.
```

#### `groom_paw` — Lamerse la pata · bucle · 4 fotogramas · Pose base: sentado
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 2x2 grid, 4 frames, read left-to-right, top-to-bottom. Looping animation: frame 4 must flow smoothly back into frame 1.
Animation "licking its front paw". The cat sits; eyes half closed, relaxed.
Frame 1: front paw raised to the mouth. Frame 2: pink tongue out, licking the paw upward. Frame 3: tongue licking again, head tilted a bit more. Frame 4: tongue back in, paw slightly lower, returning to frame 1.
```

#### `groom_face` — Limpiarse la cara · bucle · 4 fotogramas · Pose base: sentado
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 2x2 grid, 4 frames, read left-to-right, top-to-bottom. Looping animation: frame 4 must flow smoothly back into frame 1.
Animation "washing its face with a paw". The cat sits; eyes closed.
Frame 1: front paw at the mouth. Frame 2: paw wiping over the cheek. Frame 3: head tilted down, paw sweeping over the ear. Frame 4: paw coming back down toward the mouth, returning to frame 1.
```

#### `sit_look` — Sentado mirando alrededor · bucle · 4 fotogramas · Pose base: sentado
Se usa también cuando mira el cursor, vigila desde la caja o te mira con el menú abierto.
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 2x2 grid, 4 frames, read left-to-right, top-to-bottom. Looping animation: frame 4 must flow smoothly back into frame 1.
Animation "sitting and looking around". The body stays still (the attached sitting pose); only the head, eyes, ears and tail tip move.
Frame 1: looking forward to the right. Frame 2: head turned toward the viewer, curious. Frame 3: slow blink, eyes closed, tail tip curls. Frame 4: looking slightly up, ears forward, returning to frame 1.
```

#### `yawn` — Bostezar · una vez · 4 fotogramas · Pose base: sentado
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 2x2 grid, 4 frames, read left-to-right, top-to-bottom. Plays once and ends in the starting pose.
Animation "big yawn" while sitting.
Frame 1: sitting, eyes getting sleepy. Frame 2: head tilting up, mouth opening. Frame 3: full yawn, mouth wide open showing the pink tongue and small fangs, eyes squeezed shut, ears back. Frame 4: mouth closed, content sleepy face, back to the sitting pose.
```

#### `knead` — Amasar · bucle · 4 fotogramas · Pose base: sentado
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 2x2 grid, 4 frames, read left-to-right, top-to-bottom. Looping animation: frame 4 must flow smoothly back into frame 1.
Animation "kneading" (making biscuits), purring, eyes half closed and blissful.
Frame 1: sitting low, left front paw pressing down, right paw raised. Frame 2: both paws level, toes spread. Frame 3: right front paw pressing down, left paw raised. Frame 4: both paws level again, returning to frame 1.
```

### Caminando

#### `walk` — Caminar · bucle · 6 fotogramas · Pose base: de pie
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 3 columns x 2 rows, 6 frames, read left-to-right, top-to-bottom. Looping animation: frame 6 must flow smoothly back into frame 1.
Animation "slow relaxed walk cycle", walking in place like a treadmill. Diagonal leg pairs move together (front-right with back-left, front-left with back-right). Tail up, head steady, the body bobs one small step down when the legs are spread and up when they pass each other.
Frame 1: contact, near front leg forward, near back leg back, legs spread, body low. Frame 2: the far legs pass under the body, body rising. Frame 3: passing position, legs under the body, body highest. Frame 4: contact, the other diagonal pair forward, legs spread, body low. Frame 5: passing, body rising. Frame 6: passing, body highest, returning to frame 1.
```

#### `trot` — Trotar · bucle · 6 fotogramas · Pose base: de pie
Se usa también para seguir al cursor.
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 3 columns x 2 rows, 6 frames, read left-to-right, top-to-bottom. Looping animation: frame 6 must flow smoothly back into frame 1.
Animation "bouncy trot cycle", in place. Faster and bouncier than walking: longer strides, diagonal leg pairs, a brief moment with only two paws touching, tail up and bouncing, ears forward, cheerful.
Frame 1: diagonal pair A reaching forward, legs wide. Frame 2: pair A planted, body pushing up. Frame 3: suspension, legs gathered under the body, body highest. Frame 4: diagonal pair B reaching forward, legs wide. Frame 5: pair B planted, body pushing up. Frame 6: suspension, legs gathered, returning to frame 1.
```

#### `run` — Correr · bucle · 6 fotogramas · Pose base: de pie
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 3 columns x 2 rows, 6 frames, read left-to-right, top-to-bottom. Looping animation: frame 6 must flow smoothly back into frame 1.
Animation "fast gallop cycle", in place. Body low and stretched, ears back, tail straight out behind, determined face.
Frame 1: fully extended, front legs reaching far forward, back legs far back, body long. Frame 2: front paws land. Frame 3: back legs swing forward under the body, spine curved. Frame 4: fully gathered, all paws close together under the body, back arched. Frame 5: back legs push off, front legs reaching forward. Frame 6: almost extended again, returning to frame 1.
```

#### `sniff` — Olfatear el suelo · bucle · 4 fotogramas · Pose base: de pie
Se usa también cuando olfatea a otro gato.
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 2x2 grid, 4 frames, read left-to-right, top-to-bottom. Looping animation: frame 4 must flow smoothly back into frame 1.
Animation "sniffing the ground". The cat stands on all fours, head lowered, nose near the ground in front of its front paws, curious.
Frame 1: head lowered, nose near the ground. Frame 2: nose twitches, whiskers forward. Frame 3: head moves a bit forward and sideways, sniffing. Frame 4: nose twitches again, returning to frame 1.
```

#### `stretch_yoga` — Estirarse · una vez · 6 fotogramas · Pose base: de pie
También es la transición al despertar para caminar.
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 3 columns x 2 rows, 6 frames, read left-to-right, top-to-bottom. Plays once and ends standing.
Animation "big cat stretch".
Frame 1: standing. Frame 2: front paws slide forward, chest going down. Frame 3: full "downward dog" stretch: chest and chin low near the ground, front legs straight forward, rear end high, tail up, eyes closed, mouth open in a little yawn. Frame 4: weight shifts forward, chest up, one back leg stretched straight behind. Frame 5: the back leg returns, body straightening. Frame 6: standing again, like frame 1.
```

#### `turn_around` — Media vuelta · una vez · 4 fotogramas · Pose base: de pie
Termina mirando a la izquierda: la app voltea el sprite al acabar.
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place: it turns on the spot.
Layout: 2x2 grid, 4 frames, read left-to-right, top-to-bottom. Plays once.
Animation "quick half turn", standing on all fours.
Frame 1: side view facing RIGHT (the attached standing pose). Frame 2: three-quarter view turning toward the viewer, head leading the turn. Frame 3: front view facing the viewer. Frame 4: side view facing LEFT (exact mirror of frame 1).
```

#### `jump` — Saltar (subir/bajar/entrar/salir de la caja) · una vez · 6 fotogramas · Pose base: de pie
La trayectoria (arco) la pone la app: los fotogramas son solo la pose de cada fase del salto.
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth. Even the airborne frames keep the cat at the same height in the cell: the game moves it along the jump arc.
Layout: 3 columns x 2 rows, 6 frames, read left-to-right, top-to-bottom. Plays once and ends standing.
Animation "jump forward and up".
Frame 1: crouch, eyes fixed on the target ahead and above, rear end wiggling. Frame 2: launch, back legs pushing, front legs lifting, body angled upward. Frame 3: airborne, body fully stretched diagonally upward, front paws reaching forward, tail straight back. Frame 4: top of the jump, legs tucked under the round body. Frame 5: descending, front legs reaching down to land, body angled slightly down. Frame 6: landing, front paws down, small squash, legs bent.
```

#### `land` — Aterrizar tras caer · una vez · 4 fotogramas · Pose base: de pie
Cuando lo sueltas después de arrastrarlo.
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 2x2 grid, 4 frames, read left-to-right, top-to-bottom. Plays once and ends standing.
Animation "landing after a fall".
Frame 1: impact, body squashed flat, legs splayed out, eyes squeezed shut. Frame 2: rebound, body stretching up, legs straightening. Frame 3: shakes its head and body, ears flapping, fur slightly ruffled. Frame 4: standing normally, a little offended expression.
```

#### `carry` — Colgando mientras lo arrastras · bucle · 4 fotogramas · Pose base: ninguna
No se dibuja ninguna mano: el cursor de la app es quien lo sujeta.
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Front view, facing the viewer. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no hands, no other objects.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with the top of its head at the same height in every cell.
Layout: 2x2 grid, 4 frames, read left-to-right, top-to-bottom. Looping animation: frame 4 must flow smoothly back into frame 1.
Animation "dangling while being carried by the scruff of the neck" (held from above the head by an unseen hand). The body hangs vertically, front paws curled in front of the chest, back legs and tail hanging loose, resigned grumpy face.
Frame 1: hanging still. Frame 2: swinging slightly to the left, legs and tail trailing to the right. Frame 3: hanging still, legs kicking a little. Frame 4: swinging slightly to the right, legs and tail trailing to the left.
```

### Transiciones entre estados

#### `wake_up` — Despertarse · una vez · 6 fotogramas · Pose base: enroscado y sentado
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 3 columns x 2 rows, 6 frames, read left-to-right, top-to-bottom. Plays once.
Animation "waking up". Starts exactly in the attached curled pose and ends exactly in the attached sitting pose.
Frame 1: curled asleep. Frame 2: lifts its head, eyes half open, sleepy. Frame 3: pushes up on the front legs, rear still down. Frame 4: front-leg stretch, chest low, rear up, eyes closed. Frame 5: sits up with a yawn, mouth open. Frame 6: sitting, eyes open, awake.
```

#### `to_sit` — Pasar de caminar a sentarse · una vez · 4 fotogramas · Pose base: de pie y sentado
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 2x2 grid, 4 frames, read left-to-right, top-to-bottom. Plays once.
Animation "sitting down". Starts exactly in the attached standing pose and ends exactly in the attached sitting pose.
Frame 1: standing. Frame 2: rear end lowering, back legs bending. Frame 3: rear down, front legs straightening, chest rising. Frame 4: sitting upright, tail wrapped around the front paws.
```

#### `to_sleep` — Irse a dormir · una vez · 6 fotogramas · Pose base: sentado y enroscado
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 3 columns x 2 rows, 6 frames, read left-to-right, top-to-bottom. Plays once.
Animation "getting ready to sleep". Starts exactly in the attached sitting pose and ends exactly in the attached curled pose.
Frame 1: sitting, sleepy eyes. Frame 2: big yawn, mouth wide open. Frame 3: stands up and turns in a small circle, three-quarter view. Frame 4: lowering the body, front legs folding. Frame 5: lying down, starting to curl, tail wrapping around. Frame 6: curled donut asleep, eyes closed (exactly the attached curled pose).
```

#### `petted_happy` — Contento al acariciarlo · una vez · 4 fotogramas · Pose base: sentado
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 2x2 grid, 4 frames, read left-to-right, top-to-bottom. Plays once and ends in the starting pose.
Animation "happy while being petted". No hand is drawn.
Frame 1: sitting. Frame 2: eyes close happily, head pushes upward as if into a hand, ears flat with pleasure. Frame 3: head tilted, rubbing upward, big content smile, tail straight up with a curl. Frame 4: back to sitting with a satisfied face.
```

### Con la caja

La caja **no se dibuja** en ningún prompt: la app la pinta. Cada prompt dice dónde estaría.

#### `box_push` — Empujar la caja · bucle · 4 fotogramas · Pose base: de pie
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 2x2 grid, 4 frames, read left-to-right, top-to-bottom. Looping animation: frame 4 must flow smoothly back into frame 1.
Animation "pushing a heavy box with its head". Do NOT draw the box: imagine a tall flat wall right in front of the cat's face. The cat leans forward, forehead and shoulders pressed against that invisible wall, back legs pushing hard, eyes squeezed shut with effort.
Frame 1: pushing, near back leg extended back. Frame 2: steps forward with the near back leg, body still leaning. Frame 3: pushing, far back leg extended back. Frame 4: steps forward with the far back leg, returning to frame 1.
```

#### `box_rub` — Frotarse contra la caja · bucle · 4 fotogramas · Pose base: de pie
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 2x2 grid, 4 frames, read left-to-right, top-to-bottom. Looping animation: frame 4 must flow smoothly back into frame 1.
Animation "rubbing its cheek against a box", affectionate. Do NOT draw the box: imagine its corner right in front of the cat's head.
Frame 1: standing, head turned, cheek pressed forward, eyes closed happily, tail straight up. Frame 2: head tilts and rubs downward, body leaning in. Frame 3: rubs the side of the head and shoulder along it, tail curling at the tip. Frame 4: head tilts back up, returning to frame 1.
```

#### `box_scratch` — Arañar la caja · bucle · 4 fotogramas · Pose base: de pie
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 2x2 grid, 4 frames, read left-to-right, top-to-bottom. Looping animation: frame 4 must flow smoothly back into frame 1.
Animation "scratching the side of a box". Do NOT draw the box: imagine a vertical wall right in front of the cat. The cat stands up on its hind legs, front paws stretched up against that wall, claws out, focused face, tail low.
Frame 1: both front paws high on the wall. Frame 2: left paw pulls down, right paw stays high. Frame 3: right paw pulls down, left paw reaches high again. Frame 4: both paws high, returning to frame 1.
```

#### `box_peek` — Asomarse desde dentro de la caja · bucle · 4 fotogramas · Pose base: sentado
La app tapa todo lo que queda por debajo del borde de la caja (más o menos a la altura del pecho del gato sentado).
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 2x2 grid, 4 frames, read left-to-right, top-to-bottom. Looping animation: frame 4 must flow smoothly back into frame 1.
Animation "peeking out of a box". Do NOT draw the box: the game draws it in front of the cat and hides everything below the cat's chest. Draw the whole cat in every frame, curious, ears up.
Frame 1: crouched very low, only the ears would show above the rim. Frame 2: rising, eyes peeking, looking around. Frame 3: standing up on hind legs, head and both front paws resting high on an invisible edge at chest height, looking out. Frame 4: ducking back down, returning to frame 1.
```

#### `box_paw_out` — Sacar una pata por el borde · bucle · 4 fotogramas · Pose base: sentado
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 2x2 grid, 4 frames, read left-to-right, top-to-bottom. Looping animation: frame 4 must flow smoothly back into frame 1.
Animation "reaching a paw out of a box", playful. Do NOT draw the box: the game hides everything below the cat's chest. The cat stands on its hind legs inside; only the top of the head, the eyes and one front paw would show above the rim.
Frame 1: eyes peeking, paws down. Frame 2: one front paw reaches up and over forward. Frame 3: the paw waves and swipes forward, toes spread. Frame 4: the paw pulls back down, returning to frame 1.
```

#### `ambush_pop` — Emboscada: salir de golpe de la caja · una vez · 4 fotogramas · Pose base: de pie
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth. The game moves the cat out of the box; draw the poses only.
Layout: 2x2 grid, 4 frames, read left-to-right, top-to-bottom. Plays once and ends standing.
Animation "surprise ambush out of a box". Do NOT draw the box.
Frame 1: crouched very low and tense, only the eyes and ears would show, mischievous look. Frame 2: explodes upward, legs pushing, front paws thrown up and wide, mouth open. Frame 3: airborne, all four legs spread like a starfish, playful "boo" face. Frame 4: lands standing, proud and playful.
```

#### `cursor_swat` — Zarpazo al cursor · una vez · 4 fotogramas · Pose base: sentado
```
STYLE: 2D pixel art sprite, crisp hard-edged pixels, clean dark outline, flat 3-tone cel shading, no gradients, no blur, no anti-aliasing, cute chubby proportions.
Use the attached character sheet as the exact reference: same cat, same proportions, colors, markings, outline and shading in every frame.
Animation sprite sheet. Side view, the cat faces RIGHT. Solid flat magenta background (#FF00FF) everywhere. No ground, no shadow, no text, no numbers, no frame borders, no other objects unless stated.
Every cell has the same size; the cat is the same size in every cell, centered horizontally, with its lowest point resting on the same baseline near the bottom of each cell. The cat stays in place (no travel across the cell): only the pose changes from frame to frame, in small consistent steps so the motion is smooth.
Layout: 2x2 grid, 4 frames, read left-to-right, top-to-bottom. Plays once and ends in the starting pose.
Animation "quick swat at something above and in front" (the mouse cursor; do not draw it).
Frame 1: sitting, eyes locked on a point above and in front, pupils big. Frame 2: raises the front paw, leaning back slightly. Frame 3: fast swipe up and forward, claws out, body stretched upward. Frame 4: back to sitting, satisfied.
```

---

## 6. Resumen

| Grupo | Acciones | Fotogramas |
|---|---|---|
| Poses base | 3 | 3 |
| Durmiendo | 8 | 36 |
| Jugando | 10 | 50 |
| Caminando | 9 | 46 |
| Transiciones | 4 | 20 |
| Caja y cursor | 7 | 28 |
| **Total por gato** | **38 acciones + 3 poses** | **183** |

Cada acción es **una sola imagen** de Gemini (la cuadrícula), así que por gato son
38 generaciones + 3 poses + 1 ficha.

**Orden recomendado** para ver resultados pronto: ficha del personaje → 3 poses base →
`walk`, `sit_look`, `sleep_curl` (los tres estados base) → transiciones (`to_sit`, `to_sleep`,
`wake_up`) → el resto.
