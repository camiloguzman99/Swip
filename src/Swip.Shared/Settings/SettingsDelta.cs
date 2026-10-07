namespace Swip.Shared.Settings;

/// <summary>
/// Qué cambió entre dos versiones de los ajustes. Lo usa cada gato para aplicar a la pantalla SOLO
/// lo que otra sesión modificó (posición de la caja, color o posición de un gato, etiquetas).
/// </summary>
public sealed class SettingsDelta
{
    /// <summary>Diferencia máxima (en píxeles) que se ignora al comparar posiciones.</summary>
    private const double Tolerance = 0.5;

    public bool ShowLabels { get; private init; }
    public bool Box { get; private init; }

    /// <summary>Claves (nombre de usuario) de los gatos nuevos o modificados.</summary>
    public IReadOnlyList<string> Cats { get; private init; } = Array.Empty<string>();

    public bool Any => ShowLabels || Box || Cats.Count > 0;

    public static SettingsDelta Between(AppSettings before, AppSettings after)
    {
        var changed = new List<string>();
        foreach (var (key, pref) in after.Cats)
        {
            if (!before.Cats.TryGetValue(key, out var old) || CatDiffers(old, pref))
                changed.Add(key);
        }

        return new SettingsDelta
        {
            ShowLabels = before.ShowLabels != after.ShowLabels,
            Box = Differs(before.BoxLeft, after.BoxLeft) || Differs(before.BoxTop, after.BoxTop),
            Cats = changed,
        };
    }

    private static bool CatDiffers(CatPref a, CatPref b) =>
        a.Color != b.Color || a.Fat != b.Fat || Differs(a.X, b.X) || Differs(a.Y, b.Y);

    private static bool Differs(double? a, double? b)
    {
        if (a is null || b is null) return a is null != b is null;
        return Math.Abs(a.Value - b.Value) > Tolerance;
    }
}
