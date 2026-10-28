# Swip 🐱

Un gato pixel art por cada cuenta de usuario de tu equipo, viviendo sobre la barra de tareas de
Windows 11. Te deja **cambiar rápido entre sesiones** (por ejemplo, empresa y personal) **sin
escribir la contraseña** y **ver qué aplicaciones tiene abiertas** cada sesión.

---

## Qué hace

- 🐱 **Un gato por cuenta de usuario** (si hay 5 usuarios, 5 gatos), tenga o no la sesión abierta.
  Cada gato está ligado a su usuario y **no cambia**: no hay selector de color ni variantes. El
  primer usuario es el gato naranja y el siguiente el atigrado gris; se asigna la primera vez y queda
  guardado en `settings.json` (`Cats.<usuario>.Color`: `orange` o `gray`), igual en todas las sesiones.
- 😺 **Un temperamento por estado de la cuenta**, y dentro de él muchas acciones distintas
  (ver [Motor de movimientos](#motor-de-movimientos)):

  | Estado de la cuenta                  | Temperamento | Regla                                         |
  |--------------------------------------|--------------|-----------------------------------------------|
  | Sesión cerrada (sin sesión iniciada) | Duerme       | Nunca se desplaza por su cuenta               |
  | Iniciada pero en el otro escritorio  | Juega        | Se queda en su sitio, nunca camina            |
  | Sesión activa (en pantalla ahora)    | Camina       | El único que recorre la pantalla y busca la caja |

- 📦 **Una caja de cartón** en la esquina: con clic derecho abre el menú de Swip. A los gatos les
  gustan más las cajas que las casas.
- 🫥 **Click-through ajustado a cada figura**: el espacio vacío (y el margen transparente de cada
  dibujo) deja pasar los clics al escritorio; solo la figura visible del gato o de la caja los captura.
- 🔍 **Visor** (opción de la caja): una ventana normal de aplicación (con el icono de Swip en la barra de tareas, no se
  queda encima de las demás) de fondo blanco o negro según el tono de Windows, con tres círculos arriba a la derecha
  (ampliar, minimizar y cerrar). A la izquierda, en una lista, las **14 acciones** y las transiciones entre posturas (en
  rojo las que aún no tienen arte) y abajo el **selector de gato** (la cara del gato y el color de su sesión); a la
  derecha, el visor: al elegir una acción se ve esa acción precedida de las transiciones que haría el motor desde la postura
  anterior, y al elegir una transición se ve de una postura a la otra. Debajo, los controles, los cuadros del clip y, en
  dos columnas, los **tiempos** (fps o duración y avance por cuadro) y el **tamaño de las piezas**. «Editar dibujo» cambia
  el visor por el editor de píxeles dentro de la misma ventana: **todos los cuadros** del clip a la vez, la paleta y las
  herramientas (lápiz, borrador, gotero, relleno, reemplazo de color, deshacer) a la derecha y **Aceptar / Cancelar /
  Restablecer** debajo. Lo retocado se guarda en `%ProgramData%\Swip\art` (tiempos en `timing.json`, cuadros en
  `<gato>\<clip>_<cuadro>.png`) y el tamaño en `settings.json`, así que lo ven todas las sesiones. La calidad del arte
  sale de `src/Swip.App/Assets/atlas/visor.json` (`tools/art/build_viewer.py`) y el plan completo está en
  **[`docs/art/PLAN_TRANSICIONES.md`](docs/art/PLAN_TRANSICIONES.md)**.
- 🏷️ **Etiquetas** (opción de la caja): el nombre del usuario **sobre** cada gato, en el color de
  énfasis de Windows; si tiene dos palabras, en dos filas centradas. No se puede seleccionar ni
  estorba al clic.
- 🎨 **Mismo aspecto en todas las sesiones**: el gato de cada usuario, la posición de los gatos y de
  la caja, y las etiquetas se comparten y se reflejan al instante entre sesiones.

### Controles

| Acción                               | Resultado                                                           |
|--------------------------------------|---------------------------------------------------------------------|
| Clic izquierdo en un gato            | Lo acaricias: corazón, y reacciona según su estado (dormido abre un ojo y sigue durmiendo) |
| Clic izquierdo MANTENIDO en un gato  | Lo cargas y arrastras; al soltar cae por gravedad                   |
| Clic derecho en un gato              | Menú de ese usuario (ver abajo)                                     |
| Clic izquierdo en la caja            | Se abre / se cierra, nada más                                       |
| Clic derecho en la caja              | Menú de Swip: **Etiquetas**, **Visor**, **Actualizar**, **Salir**   |
| Arrastrar la caja                    | La mueves; también cae por gravedad                                 |
| Soltar un gato sobre la caja         | Caja cerrada: se sienta **encima**. Caja abierta: se mete **dentro** |

**Menú de un gato** (3 niveles, arrastrable por el encabezado; panel negro con **desenfoque** del
fondo y un tinte negro al 20 % (80 % de transparencia); sin borde de color y filas sin tarjetas de
fondo. Esquinas redondeadas: en **Windows 11** las redondea el propio DWM, que recorta también el
desenfoque con el borde suavizado (para eso el menú usa una ventana no "layered"). En Windows 10 se
usa un recorte por región que el desenfoque no respeta, así que en las esquinas puede asomar un
resto. `app-s{N}.log` anota qué modo se usó y el resultado de cada paso. Si el desenfoque se ve mal en tu equipo, pon `"MenuBlur": false` en
`C:\ProgramData\Swip\settings.json` y vuelve el fondo normal con un 30 % de transparencia; los
colores se cambian en `MenuPanelBrush` / `MenuPanelBlurBrush`, en `App.xaml`):
1. Nombre de usuario.
2. Apps con ventana de esa sesión, con **CPU %** y **RAM %**. Se miden como el Administrador de
   tareas: cada app suma sus procesos **y los que cuelgan de ellos** (p. ej. los procesos
   `msedgewebview2` del Outlook nuevo o de WhatsApp; el Explorador no se lleva los de las apps que
   abrió), la RAM es la **memoria privada** (sin contar las páginas compartidas) en % de la RAM
   física, y la CPU se mide durante 1 s como % del total de la CPU. Por debajo del 10 % se muestra
   un decimal. El menú tarda ~1 s en rellenarse por esa medición.
3. **Cambiar a esta sesión** (sin confirmación); en la sesión actual el mismo botón sale en gris
   como **En esta sesión**, y si la cuenta no tiene sesión, **Iniciar sesión**.

Qué cuenta como "app abierta": lo que verías en la barra de tareas o en Alt+Tab. Se descartan las
apps de la Tienda suspendidas (Windows deja su ventana "visible" pero oculta), el marco
`ApplicationFrameHost` (se muestra la app real que hay dentro), el escritorio, la barra de tareas
y el menú Inicio; el **Explorador de archivos** sí cuenta. Las ventanas en otro escritorio virtual
también cuentan. Qué se aceptó y qué se descartó queda en `app-s{N}.log` (solo procesos y clases).

---

## Motor de movimientos

Todo lo que se mueve vive en `Swip.Shared/Engine` (`SwipWorld`), sin depender de WPF, así que se
prueba entero: física de gatos y caja, el cerebro de cada gato, los movimientos y los efectos. La
app solo traduce clics y arrastres en llamadas al motor y dibuja lo que este le dice.

- **Posturas y transiciones:** el gato siempre está en una postura (bolita, de lado, acostado, sentado, de pie,
  caminando, trotando o corriendo). Para pasar de una a otra recorre el camino más corto del grafo de transiciones
  (`Behaviors.Edges`): de bolita a correr es despertar, levantarse y echar a correr, nunca de golpe. Los pasos sin arte
  son cortes directos y salen en rojo en el Visor. Ver [`docs/art/PLAN_TRANSICIONES.md`](docs/art/PLAN_TRANSICIONES.md).
- **Cerebro:** cuando un gato termina lo que hace, el planificador elige la siguiente acción entre las permitidas para su
  temperamento y su situación, con pesos: prefiere las que no obligan a cambiar de postura, no repite la anterior, respeta
  los **enfriamientos** (aseo 60 s, trote 30 s, carrera 60 s, saltos 50 s, caja 75 s) y tras algo que cansa casi siempre
  descansa. Los reposos duran de 10 a 70 s; el gato de la pantalla activa descansa menos que el de segundo plano.
  El aterrizaje, el menú abierto y el cambio de sesión tienen prioridad.
- **Acciones** (14, en `Behaviors.cs`): sentado, acostado, de pie, durmiendo de bolita, durmiendo de lado, caminar,
  trotar, correr, lamerse la pata, saltar al mismo nivel, saltar a la caja, meterse en la caja, salirse de la caja y
  chocar con la caja y empujarla. Dormido nunca se desplaza; jugando nunca camina.
- **Caja:** si la van a cerrar con un gato despierto dentro, primero se levanta y sale de un salto y después se cierra la
  tapa (uno dormido se queda encima). Al chocar con la caja para empujarla, esta se sacude y levanta polvo.
- **Efectos de movimiento** (sin arte adicional): respiración al dormir, tambaleo de la caja, saltos en arco, sombra en
  el suelo o en la tapa, y partículas: zzz, corazones al acariciarlo, polvo al correr y al aterrizar.

### Arte: hojas de sprites

- `art/clips.json` es la **fuente única** de los clips (fotogramas, velocidad, descripción y la
  receta de su fotograma de referencia). La leen el motor y el generador.
- `python3 tools/art/build_atlas.py` genera `src/Swip.App/Assets/atlas/` (una hoja por piel y
  `atlas.json` con la caja visible de cada fotograma), las hojas de referencia
  `docs/art/reference/<clip>.png` y la lista **[`docs/art/ARTE_PENDIENTE.md`](docs/art/ARTE_PENDIENTE.md)**.
- Los clips sin arte final funcionan ya con **fotogramas de referencia** derivados de las poses
  dibujadas (`art/poses/`). Para sustituirlos, mira `ARTE_PENDIENTE.md`.
- Cada gato tiene su propio dibujo (`orange`, `gray`); no hay retintado ni más variantes
  (`Swip.Shared/Art/CatSkins.cs` decide cuál le toca a cada usuario).
- Para revisar el motor sin abrir la app:
  `dotnet run --project tools/SimTrace -- trace.json 120` y
  `python3 tools/art/render_trace.py trace.json salida.png 10,40,70`.

## Por qué hacen falta dos piezas

Windows aísla las sesiones a propósito. **Cambiar de sesión sin contraseña** solo es posible desde
un proceso con privilegios de **SYSTEM** (`WTSConnectSession`): Swip no guarda ninguna contraseña.

```
┌─────────────────────────────┐   named pipe    ┌───────────────────────────────┐
│  Swip.App  (una por sesión) │ ◄────────────►  │  Swip.Service (como SYSTEM)   │
│  - gato / ventana           │  Global\        │  - WTSEnumerateSessions       │
│    transparente             │  SwipServicePipe│  - WTSConnectSession (cambio) │
│  - enumera SUS ventanas     │                 │  - caché de apps por sesión   │
│  - publica sus apps         │                 │  - vigilante: relanza el gato │
│  asInvoker (sin privilegios)│                 │                               │
└─────────────────────────────┘                 └───────────────────────────────┘
```

| Proyecto        | Qué es                                                                       |
|-----------------|------------------------------------------------------------------------------|
| `Swip.Shared`   | Contratos de IPC, ajustes compartidos, caché de apps, log rotativo.          |
| `Swip.Service`  | Servicio de Windows (SYSTEM): sesiones, cambio de sesión, caché, vigilante.  |
| `Swip.App`      | Los gatos (WPF): ventana transparente, pixel art, animación, menús.          |
| `Swip.Tests`    | Pruebas automáticas de la lógica compartida (se ejecutan en el CI).          |

### Cómo se ven las apps de otra sesión

Un servicio en la sesión 0 **no puede enumerar las ventanas de otro escritorio**. Por eso cada
gato enumera, en su propio proceso, las apps con ventana de **su** sesión y las **publica** al
servicio cada ~8 s; el gato de otra sesión las lee de esa caché.

Limitación de Windows a tener presente: **una sesión que no está en pantalla no puede ver sus
propias ventanas**. Por eso Swip muestra de una sesión en segundo plano **lo último que tenía
abierto cuando estuvo en pantalla**. Una app que abras o cierres en esa sesión mientras está en
segundo plano no se refleja hasta que vuelvas a entrar en ella.

### Qué hace Swip cuando la sesión no está en pantalla

Si cambias de usuario o bloqueas, el gato de esa sesión **deja de animarse, refrescar y publicar**
(nadie lo ve). Justo antes de irse **guarda dónde está cada gato** (también el que camina), y al
volver a pantalla la otra sesión **relee la ubicación y el estado**: esas posiciones, los ajustes
compartidos, el tamaño del área de trabajo (monitor, resolución, barra de tareas) y el estado de
todas las sesiones. Así los gatos aparecen en el mismo sitio en las dos sesiones.

---

## Requisitos

- Windows 11
- Permisos de administrador **solo para instalar el servicio** (una vez).
- [.NET 8 SDK](https://dotnet.microsoft.com/download) solo si instalas compilando desde el código.

## Instalación y actualización

**Desde el paquete ya compilado (recomendado).** Descarga `Swip-win-x64.zip` del release
`latest`, extráelo y, en PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File setup.ps1
```

`setup.ps1` se auto-eleva, **recrea el servicio desde cero** (resuelve el caso de una versión
vieja que quedó corriendo con el `.exe` en uso), copia los binarios y deja el arranque
automático para **todos** los usuarios.

**Compilando desde el repositorio** (necesita el SDK de .NET 8):

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1
powershell -ExecutionPolicy Bypass -File uninstall.ps1   # para quitarlo todo
```

**Actualizar.** Clic derecho en la caja → **Actualizar**, o `update.ps1`. El botón lo ejecuta **en
segundo plano**: sin ventana de PowerShell, solo el aviso de UAC (hace falta administrador para
parar el servicio y escribir en Archivos de programa). Va así:

1. Descarga el release `latest` y **verifica su SHA-256**; lo extrae. El gato **sigue funcionando**
   durante todo esto, y si algo falla en este punto no se toca nada.
2. Solo entonces para el servicio y el gato, reemplaza los archivos y arranca el servicio, que
   relanza el gato en cada sesión (sin privilegios de administrador). Si ves que el gato
   desaparece unos segundos y vuelve, ha ido bien.

El resultado queda en `C:\Program Files\Swip\logs\update.log`. Si falla, el script muestra un aviso con
el motivo. Lanzado a mano (`update.ps1` sin `-Silent`) muestra la consola, con una pausa al final.

### Arranque y cierre

- Swip arranca en cada sesión mediante un acceso directo en el inicio común (todos los usuarios).
- Además el servicio **comprueba cada 30 s** que haya un gato en cada sesión de usuario y lo
  relanza si falta (por ejemplo en la otra sesión tras una actualización).
- Hay **una sola instancia por sesión**.
- **Salir** cierra el gato de esa sesión y el servicio **no lo relanza** hasta que esa sesión
  se cierre o el servicio se reinicie (p. ej. al actualizar).

### Registros y diagnóstico

Con tamaño acotado (rotan a `.1` al pasar de 256 KB). Los que escriben el servicio y el actualizador (que corren
con privilegios) están en la carpeta de la instalación, `C:\Program Files\Swip\logs\`, donde solo los administradores
pueden escribir: así un usuario no puede usarlos para sobrescribir otros archivos. Los de cada gato, en
`C:\ProgramData\Swip\`.

| Archivo                                  | Contenido                                                         |
|------------------------------------------|-------------------------------------------------------------------|
| `Program Files\Swip\logs\service.log`   | Servicio: arranque, relanzamientos, publicaciones y lecturas.     |
| `Program Files\Swip\logs\update.log`    | Actualizador (modo silencioso).                                   |
| `ProgramData\Swip\app-s{N}.log`          | El gato de la sesión `N`: pausas/reanudaciones y apps publicadas. |

Para ver todas las sesiones que Windows reporta y cuáles cuenta Swip como usuario (consola de
**administrador**):

```powershell
& "$env:ProgramFiles\Swip\Service\Swip.Service.exe" --diagnose
```

Si no aparece el gato de otra cuenta: comprueba con `qwinsta` que tiene sesión (`Disc` = en
segundo plano). Una cuenta sin sesión iniciada aparece dormida, sin opción de cambio, con el
botón **Iniciar sesión**.

---

## Seguridad

- El servicio corre como SYSTEM pero **no almacena contraseñas**; el cambio de sesión se apoya en
  que SYSTEM puede conectar sesiones directamente. **Consecuencia deliberada:** cualquier usuario
  interactivo de ESTE equipo puede pedir el cambio a cualquier sesión abierta, sin contraseña. Encaja
  con un equipo cuyas cuentas son todas tuyas; no lo instales donde haya cuentas de otras personas.
- El pipe permite acceso solo a **SYSTEM** y a los **usuarios interactivos**.
- **Cada cliente solo puede publicar su propia lista y salir de su propia sesión**: el servicio usa
  la sesión que Windows reporta del pipe (`GetNamedPipeClientSessionId`), no la que declare el cliente.
- El servidor atiende cada conexión por separado con un **timeout de 15 s**, un **máximo de 16
  a la vez** y una **línea de petición acotada**: un cliente colgado o malicioso no bloquea el
  cambio de sesión ni agota la memoria.
- Swip solo lee **títulos de ventana y nombres de proceso**, nunca el contenido de las ventanas.
- **Integridad de la actualización:** el release publica `Swip-win-x64.zip.sha256` y `update.ps1`
  se niega a instalar un zip que no coincida. Esto protege de descargas corruptas, incompletas o
  de un zip sustituido sin su hash. **No** protege de quien pueda escribir en el release de
  GitHub (podría cambiar zip y hash a la vez): para eso haría falta firmar con una clave que no
  viva en GitHub. Además el release se compila desde la rama de desarrollo en cada push, y lo que
  instala corre como SYSTEM: confía en quien tenga acceso de escritura a este repositorio.

## Alcance y limitaciones

**Fuera de alcance (por decisión):** compartir portapapeles o archivos entre sesiones; ver las
dos sesiones a la vez en pantalla (Windows de escritorio solo muestra una).

**Limitaciones conocidas:**
- La ventana es transparente a pantalla completa del área de trabajo; WPF dibuja las ventanas
  transparentes por software. No se ha medido su coste en CPU.
- El cambio de sesión hereda el comportamiento de "Cambio rápido de usuario": la sesión anterior
  queda abierta en segundo plano.
- Lo que muestra una sesión en segundo plano es lo último que vio (ver arriba). Tras reiniciar o
  actualizar el servicio la caché está vacía hasta que visites esa sesión.
- El gato sentado en la caja no se comparte entre sesiones ni se recuerda al reiniciar (en la otra sesión
  aparece en el suelo, en la X donde estaba).
- 34 de los 38 clips usan aún fotogramas de **referencia** (poses existentes con escalas y giros):
  se mueven bien, pero no son el arte final.
- WPF solo compila en Windows; el CI (`windows-latest`) compila y ejecuta las pruebas.

## Desarrollo

```powershell
dotnet test tests\Swip.Tests\Swip.Tests.csproj    # pruebas (también funcionan en Linux)
```

Cubren la lógica donde un fallo pierde datos o congela cosas (escrituras concurrentes de los
ajustes entre procesos, caché de apps, rotación del log, lector de líneas con tope) y el motor de
movimientos: las reglas de cada temperamento en simulaciones largas, que cada acción con la caja
llegue a ocurrir, las transiciones, los efectos, la coherencia entre catálogo, clips y atlas, y las
paletas.

## Próxima fase

- **Arte final** de los 34 clips con referencia (ver `docs/art/ARTE_PENDIENTE.md`).
- Gatos de **forma fija** (sin engordar/adelgazar) a partir de un **modelo 3D con estilo pixel
  art**: renderizado a hojas de sprites con el mismo formato del atlas, o en tiempo real.
