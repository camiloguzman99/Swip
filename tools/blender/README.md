# Gatos 3D → pixel art (Blender)

Prototipo para generar el arte de los gatos desde un modelo 3D articulado, en vez de dibujar
cada fotograma a mano. Mismo modelo, mismas proporciones y misma luz en todas las poses:
eso da la coherencia que falta en los fotogramas de referencia actuales.

- `swip_cats.py`: modelo del gato (cabeza grande, cuerpo redondo, rayas, barriga crema, ojos con
  brillo, orejas rosas), esqueleto (columna, cuello, cabeza, patas de 2 segmentos, cola de 6),
  la caja de cartón (abierta/cerrada), una biblioteca de poses y 8 escenas de interacción.
  Renderiza 3 pasadas por escena: color plano, luz e identificador de pieza.
- `toonify.py`: combina las pasadas en pixel art: sombreado en 2–3 bandas, contorno oscuro
  del color de la pieza, líneas interiores entre piezas y recorte al contenido.

```bash
python3.11 -m venv venv && venv/bin/pip install bpy==5.0.1 pillow
venv/bin/python tools/blender/swip_cats.py -- salida            # todas las escenas
venv/bin/python tools/blender/swip_cats.py -- salida saludo     # una sola
python3 tools/blender/toonify.py salida 3                       # pixel art (+ copia x3)
```

Resultados en `docs/art/blender/` (`interacciones.png` = hoja con todas, `comparacion_x*.png` =
tamaño frente al gato actual). Los gatos se renderizan a ~96 px de alto (el doble que en la app)
para tener sitio para ojos, nariz, orejas y rayas.

Escenas: saludo (olerse la nariz), acicalado (uno lame al otro), juego (acecho + salto),
dormir juntos, empujar la caja, dentro de la caja (asomado), encima de la caja, persecución.
