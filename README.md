# Swip 🐱

Un gato amarillo animado que vive sobre la barra de tareas de Windows 11 y te deja
**cambiar rápido entre tus dos sesiones de usuario** (por ejemplo, empresa y personal)
y **ver qué aplicaciones con ventana tiene abiertas la otra sesión**, sin tener que
escribir la contraseña.

> Estado: **Fase 1 (MVP)**. El servicio compila y está probado de compilación; la app
> del gato (WPF) se compila en Windows (no en Linux/CI por ser WPF).

---

## Qué hace

- 🐱 **Gato animado** en una ventana transparente, siempre visible, que puedes arrastrar
  sobre la barra de tareas y cuyo **tamaño puedes bloquear** según tu pantalla.
- 🖱️ **Clic en el gato** → menú con tus sesiones. La otra sesión aparece como
  *"Abierta en el otro escritorio"* y lista sus apps con ventana.
- 🔀 **Cambio rápido** a la otra sesión sin contraseña (última opción del menú).
- 👀 **Apps de la otra sesión**: solo las que tienen ventana visible (primer plano),
  nunca los procesos en segundo plano.

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
| `Swip.App`      | El gato (WPF): ventana transparente, menú, animación.              |

## Requisitos

- Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Permisos de administrador **solo para instalar el servicio** (una vez).

## Instalación y uso

Desde la raíz del repositorio, en PowerShell:

```powershell
# 1) Instalar el servicio (una sola vez, como administrador)
powershell -ExecutionPolicy Bypass -File scripts\install-service.ps1

# 2) Ejecutar el gato (como tu usuario normal)
powershell -ExecutionPolicy Bypass -File scripts\run-app.ps1
```

Para desinstalar el servicio:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\uninstall-service.ps1
```

### Controles del gato

| Acción                | Resultado                                              |
|-----------------------|-------------------------------------------------------|
| Clic izquierdo        | Abre/cierra el menú de sesiones                        |
| Arrastrar             | Mueve el gato (la posición se recuerda)                |
| Clic derecho          | Opciones: bloquear tamaño, tamaño, colocar, salir     |

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

**Incluido (Fase 1):**
- Gato animado, transparente, tamaño bloqueable, colocable sobre la barra.
- Listado de sesiones con estado y cambio sin contraseña.
- Apps con ventana de la otra sesión.

**Fuera de alcance (por decisión):**
- Compartir portapapeles o archivos entre sesiones.
- Ver las dos sesiones a la vez en pantalla (Windows de escritorio solo muestra una).

**Limitaciones conocidas:**
- WPF compila solo en Windows.
- El cambio de sesión hereda el comportamiento de "Cambio rápido de usuario" de Windows:
  la sesión anterior queda abierta en segundo plano.
- La lista de apps se toma en el momento de abrir el menú (se refresca cada ~10 s mientras
  el menú está abierto).

## Posibles mejoras (Fase 2+)

- Icono/animaciones que reaccionen a eventos (p. ej., el gato salta al cambiar de sesión).
- Atajo de teclado global para cambiar sin abrir el menú.
- Restringir la ACL del pipe a tu SID.
- Arranque automático del gato al iniciar sesión (acceso directo en `shell:startup`).
