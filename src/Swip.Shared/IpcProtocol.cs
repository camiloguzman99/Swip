using System.Text.Json;

namespace Swip.Shared;

/// <summary>
/// Constantes compartidas entre el gato (Swip.App) y el servicio SYSTEM (Swip.Service).
/// La comunicación es por un named pipe local; el servicio es el servidor y el gato el cliente.
/// </summary>
public static class IpcProtocol
{
    /// <summary>
    /// Nombre del named pipe. Vive en el espacio global para que el servicio (sesión 0, SYSTEM)
    /// y el gato (tu sesión interactiva) lo compartan.
    /// </summary>
    public const string PipeName = @"Global\SwipServicePipe";

    /// <summary>Opciones de serialización JSON usadas en ambos lados del pipe.</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };
}

/// <summary>Tipos de petición que el gato envía al servicio.</summary>
public enum RequestKind
{
    /// <summary>Devuelve las sesiones de usuario activas/conectadas en esta máquina.</summary>
    ListSessions,

    /// <summary>Devuelve las apps con ventana visible de la sesión indicada.</summary>
    ListWindowedApps,

    /// <summary>Conecta (cambia) a la sesión indicada. Requiere el servicio como SYSTEM.</summary>
    SwitchToSession,
}

/// <summary>Petición del gato hacia el servicio.</summary>
public sealed class IpcRequest
{
    public RequestKind Kind { get; set; }

    /// <summary>Id de sesión objetivo para ListWindowedApps y SwitchToSession.</summary>
    public int TargetSessionId { get; set; }
}

/// <summary>Respuesta del servicio hacia el gato.</summary>
public sealed class IpcResponse
{
    public bool Ok { get; set; }

    /// <summary>Mensaje de error legible cuando Ok es false.</summary>
    public string? Error { get; set; }

    /// <summary>Sesiones, poblado en respuesta a ListSessions.</summary>
    public List<SessionInfo> Sessions { get; set; } = new();

    /// <summary>Apps con ventana, poblado en respuesta a ListWindowedApps.</summary>
    public List<AppInfo> Apps { get; set; } = new();
}

/// <summary>Estado de conexión de una sesión de Windows, tal como lo reporta WTS.</summary>
public enum SessionConnectionState
{
    Active,
    Connected,
    ConnectQuery,
    Shadow,
    Disconnected,
    Idle,
    Listen,
    Reset,
    Down,
    Init,
    Unknown,
}

/// <summary>Resumen de una sesión de usuario de Windows.</summary>
public sealed class SessionInfo
{
    public int SessionId { get; set; }

    /// <summary>Nombre de usuario de la sesión (sin dominio).</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Dominio o nombre de equipo de la cuenta.</summary>
    public string Domain { get; set; } = string.Empty;

    public SessionConnectionState State { get; set; }

    /// <summary>True si es la sesión que está en pantalla ahora mismo.</summary>
    public bool IsCurrent { get; set; }

    public string DisplayName =>
        string.IsNullOrEmpty(Domain) ? UserName : $"{Domain}\\{UserName}";
}

/// <summary>Una aplicación con ventana principal visible en otra sesión.</summary>
public sealed class AppInfo
{
    /// <summary>Nombre del ejecutable, por ejemplo "chrome".</summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>Título de la ventana principal, cuando se pudo leer.</summary>
    public string? WindowTitle { get; set; }
}
