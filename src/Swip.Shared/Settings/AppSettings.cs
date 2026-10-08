namespace Swip.Shared.Settings;

/// <summary>
/// Preferencias de Swip, compartidas por TODAS las sesiones del equipo en
/// %ProgramData%\Swip\settings.json (mismo aspecto y posición en cada usuario).
/// </summary>
public sealed class AppSettings
{
    /// <summary>Segundos entre refrescos automáticos de sesiones y apps.</summary>
    public int RefreshSeconds { get; set; } = 10;

    /// <summary>Mostrar la etiqueta con el nombre de la sesión bajo cada gato.</summary>
    public bool ShowLabels { get; set; } = true;

    /// <summary>
    /// Desenfoque del fondo de los menús (con el 60% de transparencia). Si en tu equipo se ve mal,
    /// ponlo a false: los menús vuelven al fondo normal con el 30% de transparencia.
    /// </summary>
    public bool MenuBlur { get; set; } = true;

    /// <summary>Posición de la caja dentro de la franja (null = esquina inferior izquierda).</summary>
    public double? BoxLeft { get; set; }
    public double? BoxTop { get; set; }

    /// <summary>Preferencias por gato (clave = nombre de usuario): color, gordura y posición.</summary>
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
