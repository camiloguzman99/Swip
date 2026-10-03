namespace Swip.App.Models;

/// <summary>
/// Preferencias de Swip, persistidas en %AppData%\Swip\settings.json.
/// La ventana es una franja transparente sobre la barra de tareas donde merodean los gatos.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Alto de la franja en DIP (se puede ajustar según la pantalla).</summary>
    public double StripHeight { get; set; } = 160;

    /// <summary>Ancho de la franja en DIP, o null para ocupar todo el ancho del área de trabajo.</summary>
    public double? StripWidth { get; set; }

    /// <summary>Tamaño en pantalla de cada gato (lado del sprite) en DIP.</summary>
    public double CatSize { get; set; } = 64;

    /// <summary>Si true, la franja no se recoloca automáticamente al arrancar.</summary>
    public bool PositionLocked { get; set; }

    /// <summary>Posición izquierda, o null para auto-colocar a la izquierda del área de trabajo.</summary>
    public double? Left { get; set; }

    /// <summary>Posición superior, o null para auto-colocar sobre la barra de tareas.</summary>
    public double? Top { get; set; }

    /// <summary>Segundos entre refrescos automáticos de sesiones y apps.</summary>
    public int RefreshSeconds { get; set; } = 10;

    /// <summary>Mostrar la etiqueta con el nombre de la sesión bajo cada gato.</summary>
    public bool ShowLabels { get; set; } = true;

    /// <summary>Mostrar un fondo tenue en la franja para verla (útil al moverla).</summary>
    public bool ShowStripBackground { get; set; }

    /// <summary>Posición de la caja dentro de la franja (null = esquina inferior izquierda).</summary>
    public double? BoxLeft { get; set; }
    public double? BoxTop { get; set; }

    /// <summary>Preferencias por gato (clave = nombre de usuario): color y gordura.</summary>
    public Dictionary<string, CatPref> Cats { get; set; } = new();
}

/// <summary>Preferencias persistentes de un gato concreto.</summary>
public sealed class CatPref
{
    /// <summary>Color del gato ("orange" o "gray"); null = asignar por defecto.</summary>
    public string? Color { get; set; }

    /// <summary>Nivel de gordura (0 = normal).</summary>
    public int Fat { get; set; }

    /// <summary>Posición donde se dejó el gato (null = automática).</summary>
    public double? X { get; set; }
    public double? Y { get; set; }
}
