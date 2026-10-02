namespace Swip.App.Models;

/// <summary>
/// Preferencias del gato, persistidas en %AppData%\Swip\settings.json.
/// El ancho/alto se pueden bloquear para que se mantengan fijos según la pantalla.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Ancho del gato en píxeles independientes del dispositivo (DIP).</summary>
    public double Width { get; set; } = 96;

    /// <summary>Alto del gato en DIP.</summary>
    public double Height { get; set; } = 96;

    /// <summary>Si true, el tamaño queda bloqueado y no se redimensiona.</summary>
    public bool SizeLocked { get; set; } = true;

    /// <summary>Posición izquierda en pantalla, o null para auto-colocar sobre la barra de tareas.</summary>
    public double? Left { get; set; }

    /// <summary>Posición superior en pantalla, o null para auto-colocar.</summary>
    public double? Top { get; set; }

    /// <summary>Segundos entre refrescos automáticos de la lista de sesiones/apps.</summary>
    public int RefreshSeconds { get; set; } = 10;
}
