# Swip 🐱

Un gato amarillo animado que vive sobre la barra de tareas de Windows 11 y te deja
**cambiar rápido entre tus dos sesiones de usuario** (por ejemplo, empresa y personal)
y **ver qué aplicaciones con ventana tiene abiertas la otra sesión**, sin tener que
escribir la contraseña.

> Estado: **Fase 1 (MVP)**. El servicio compila y está probado de compilación; la app
> del gato (WPF) se compila en Windows (no en Linux/CI por ser WPF).

---

## Qué hace

- 🐱 **Un gato por cuenta de usuario del equipo** (si hay 5 usuarios, 5 gatos) merodeando
  en una franja transparente sobre la barra de tareas. Diseño **pixel art**. Cada gato
  apunta a su usuario, tenga o no una sesión abierta.
- 😺 **Dos estados**: usuario con sesión activa/en pantalla → el gato se **mueve**;
  usuario en segundo plano o **sin sesión iniciada** → el gato **duerme** con sus "z z"
  animados (flotan y se desvanecen).
- 🎨 **Personalizable por gato**: 6 colores (amarillo, naranja, gris, negro, blanco, marrón)
  y nivel de **gordura** (engordar/adelgazar). Se guarda por usuario.
- 🖱️ **Clic izquierdo** → interactúas con el gato (se pone feliz y da un saltito).
- 🖱️ **Clic derecho** → menú con las **apps activas** de esa sesión y el botón de
  **cambiar de sesión** (sin contraseña).
- 👀 **Apps**: solo las que tienen ventana visible (primer plano), nunca procesos en
  segundo plano.
- 🫥 **Click-through**: el espacio vacío de la franja deja pasar los clics al escritorio
  y a la barra; solo los gatos capturan el ratón.

---

## Por qué hacen falta dos piezas

Windows aísla las sesiones a propósito. Dos operaciones que Swip necesita solo son
posibles desde un proceso con privilegios de **SYSTEM**:

1. **Cambiar de sesión sin contraseña** → `WTSConnectSession`. Desde SYSTEM, Windows
   realiza el cambio sin pedir credenciales, así que **Swip no guarda ninguna contraseña**.
2. **Leer las ventanas de otra sesión** → un servicio en la sesión 0 no puede enumerar
   las ventanas de otra sesión interactiva, así que lanza un pequeño ayudante dentro de
   esa sesión (`CreateProcessAsUser`) que las enumera y las reporta.

Por eso el proyecto tiene dos ejecutables:

```
┌─────────────────────────────┐   named pipe    ┌──────────────────────────────┐
│  Swip.App  (tu sesión)      │ ◄────────────►  │  Swip.Service (como SYSTEM)  │
│  - gato / ventana           │  Global\        │  - WTSEnumerateSessions      │
│    transparente             │  SwipServicePipe│  - WTSConnectSession (cambio)│
│  - menú y animación         │                 │  - ayudante por sesión que   │
│  asInvoker (sin privilegios)│                 │    enumera ventanas          │
└─────────────────────────────┘                 └──────────────────────────────┘
```

## Estructura

| Proyecto        | Qué es                                                              |
|-----------------|--------------------------------------------------------------------|
| `Swip.Shared`   | Contratos de IPC compartidos (peticiones/respuestas, DTOs).        |
| `Swip.Service`  | Servicio de Windows (SYSTEM): sesiones, cambio, enumeración.       |
| `Swip.App`      | Los gatos (WPF): franja transparente, pixel art, animación, menú.  |

## Requisitos

- Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Permisos de administrador **solo para instalar el servicio** (una vez).

## Instalación y uso

### Instalación de un paso (recomendada)

Desde la raíz del repositorio, en PowerShell. El instalador se auto-eleva a administrador,
compila e instala el servicio, publica el gato, crea el arranque automático y lo lanza:

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1
```

Para desinstalar todo (servicio, arranque y archivos):

```powershell
powershell -ExecutionPolicy Bypass -File uninstall.ps1
```

### Actualizar sin descargar el repo

Cada push a la rama compila la app en GitHub Actions y publica un release **`latest`**.
Para actualizar, clic derecho en cualquier gato → **Opciones → Actualizar** (se auto-eleva,
descarga, reemplaza y relanza). O manualmente:

```powershell
powershell -ExecutionPolicy Bypass -File update.ps1
```

### Solución de problemas: no aparece la otra sesión

Swip crea un gato por cada **sesión de usuario conectada**. Si solo ves un gato:

1. Confirma que Windows ve dos sesiones. En una consola escribe `qwinsta` (o `query session`):
   deberías ver tu usuario y el otro (normalmente en estado `Disc` = desconectado).
   Si solo aparece el tuyo, el otro usuario **no tiene sesión iniciada**: entra a esa cuenta
   con *Cambio rápido de usuario* (sin cerrar sesión) y volverá a quedar en segundo plano.
2. Diagnóstico de Swip (consola de **administrador**):
   ```powershell
   & "$env:ProgramFiles\Swip\Service\Swip.Service.exe" --diagnose
   ```
   Muestra todas las sesiones que Windows reporta y cuáles cuenta Swip como usuario.

### Manual (para desarrollo)

```powershell
# 1) Instalar solo el servicio (como administrador)
powershell -ExecutionPolicy Bypass -File scripts\install-service.ps1
# 2) Ejecutar el gato desde el código
powershell -ExecutionPolicy Bypass -File scripts\run-app.ps1
```

### Controles

| Acción                       | Resultado                                                      |
|------------------------------|----------------------------------------------------------------|
| Clic izquierdo en un gato    | Interactúas con el gato (reacción feliz + saltito)             |
| Clic derecho en un gato      | Menú de ese usuario: nombre, apps abiertas, color, engordar/adelgazar y, al final, cambiar |
| Clic en la 📦 (esquina)       | Abre la configuración de Swip                                   |

La **caja de cartón** 📦 de la esquina (cerrada; se abre al pulsarla, porque a los gatos les
gustan más las cajas que las casas) abre la configuración (en formato lista con bordes
redondeados): **Mover ventana** (arrastra la franja y pulsa la caja para terminar), **Hacer visible la
ventana** (fondo tenue para ubicarla), **tamaño de gatos** (+ / −), **alto de la franja**
(+ / −), **etiquetas**, **recolocar sobre la barra**, **Actualizar Swip** y **salir**. Las
preferencias se guardan en `%AppData%\Swip\settings.json`.

### Estados del gato

| Estado del usuario                      | Gato                 |
|-----------------------------------------|----------------------|
| Con sesión activa / en pantalla         | Despierto, merodeando|
| En segundo plano o sin sesión iniciada  | Durmiendo ("z z")    |

Swip enumera las **cuentas de usuario del equipo** (locales habilitadas, más cualquier
usuario con sesión abierta que sea de dominio/Microsoft/AzureAD) y crea **un gato por
cuenta**, no por sesión. Un usuario con la sesión abierta en segundo plano se puede cambiar
sin contraseña; uno sin sesión iniciada aparece dormido y sin opción de cambio.

## Seguridad

- El servicio corre como SYSTEM pero **no almacena contraseñas**: el cambio de sesión
  se apoya en que SYSTEM puede conectar sesiones directamente.
- El named pipe tiene una ACL que permite acceso a **SYSTEM** y a los **usuarios
  interactivos** de la máquina. Como tú eres el único con acceso a ambas sesiones, esto
  encaja con tu caso, pero cualquier usuario interactivo de ESTE equipo podría pedir el
  cambio. Si en el futuro quieres restringirlo a un único SID, es un cambio pequeño en
  `PipeServer.CreatePipe`.
- El ayudante de enumeración solo lee **títulos de ventana y nombres de proceso**; no
  lee contenido de las ventanas.

## Alcance y limitaciones

**Incluido (Fase 1 + Fase 2):**
- Un gato pixel art por sesión, en una franja transparente sobre la barra.
- Estados activo (merodeando) y durmiendo (sesión en segundo plano).
- Clic izquierdo = interactuar; clic derecho = menú de apps + cambio de sesión.
- Click-through del espacio vacío. Cambio de sesión sin contraseña.

**Fuera de alcance (por decisión):**
- Compartir portapapeles o archivos entre sesiones.
- Ver las dos sesiones a la vez en pantalla (Windows de escritorio solo muestra una).

**Limitaciones conocidas:**
- WPF compila solo en Windows.
- El cambio de sesión hereda el comportamiento de "Cambio rápido de usuario" de Windows:
  la sesión anterior queda abierta en segundo plano.
- La lista de apps se toma en el momento de abrir el menú (se refresca cada ~10 s mientras
  el menú está abierto).

## Posibles mejoras (Fase 3+)

- Más frames de caminar (ciclo de patas) y sprites por raza/color de gato por sesión.
- Atajo de teclado global para cambiar sin abrir el menú.
- Restringir la ACL del pipe a tu SID.
- Arranque automático del gato al iniciar sesión (acceso directo en `shell:startup`).
- Notificación visual cuando una sesión dormida tiene actividad nueva.
