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

    /// <summary>Devuelve las cuentas de usuario del equipo (una por usuario), con su sesión si la hay.</summary>
    ListUsers,

    /// <summary>Devuelve las apps con ventana visible de la sesión indicada (desde la caché publicada).</summary>
    ListWindowedApps,

    /// <summary>El gato publica las apps de SU sesión para que otras sesiones las consulten.</summary>
    PublishApps,

    /// <summary>Conecta (cambia) a la sesión indicada. Requiere el servicio como SYSTEM.</summary>
    SwitchToSession,

    /// <summary>Muestra la pantalla de inicio de sesión para iniciar una cuenta sin sesión.</summary>
    StartLogon,
}

/// <summary>Petición del gato hacia el servicio.</summary>
public sealed class IpcRequest
{
    public RequestKind Kind { get; set; }

    /// <summary>Id de sesión objetivo para ListWindowedApps y SwitchToSession; y la sesión origen para PublishApps.</summary>
    public int TargetSessionId { get; set; }

    /// <summary>Apps publicadas por el gato (solo para PublishApps).</summary>
    public List<AppInfo> Apps { get; set; } = new();
}

/// <summary>Respuesta del servicio hacia el gato.</summary>
public sealed class IpcResponse
{
    public bool Ok { get; set; }

    /// <summary>Mensaje de error legible cuando Ok es false.</summary>
    public string? Error { get; set; }

    /// <summary>Sesiones, poblado en respuesta a ListSessions.</summary>
    public List<SessionInfo> Sessions { get; set; } = new();

    /// <summary>Usuarios del equipo, poblado en respuesta a ListUsers.</summary>
    public List<UserInfo> Users { get; set; } = new();

    /// <summary>Apps con ventana, poblado en respuesta a ListWindowedApps.</summary>
    public List<AppInfo> Apps { get; set; } = new();
}

/// <summary>
/// Una cuenta de usuario del equipo. Si el usuario tiene una sesión abierta (activa o en
/// segundo plano), <see cref="SessionId"/> &gt;= 0; si no tiene sesión iniciada, es -1.
/// </summary>
public sealed class UserInfo
{
    /// <summary>Nombre de la cuenta (sin dominio), p. ej. "camilo".</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Dominio o nombre de equipo de la cuenta.</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>Id de la sesión del usuario, o -1 si no tiene sesión iniciada.</summary>
    public int SessionId { get; set; } = -1;

    /// <summary>Estado de la sesión (solo significativo cuando HasSession es true).</summary>
    public SessionConnectionState State { get; set; }

    /// <summary>True si es el usuario cuya sesión está en pantalla ahora mismo.</summary>
    public bool IsCurrent { get; set; }

    /// <summary>True si el usuario tiene una sesión abierta en el equipo.</summary>
    public bool HasSession => SessionId >= 0;

    public string DisplayName =>
        string.IsNullOrEmpty(Domain) ? UserName : $"{Domain}\\{UserName}";
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

    /// <summary>Uso de CPU en porcentaje (0-100) del/los proceso(s) de esta app.</summary>
    public double CpuPercent { get; set; }

    /// <summary>Uso de RAM en porcentaje de la memoria física total.</summary>
    public double RamPercent { get; set; }
}
