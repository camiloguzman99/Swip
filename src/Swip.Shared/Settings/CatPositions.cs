namespace Swip.Shared.Settings;

/// <summary>Estado de un gato en el momento de guardar su posición.</summary>
public readonly record struct CatSnapshot(string Key, string Color, int Fat, double X);

/// <summary>
/// Guarda dónde están los gatos AHORA para que la otra sesión los muestre en el mismo sitio al
/// ponerse en pantalla. Antes solo se guardaba la X cuando soltabas un gato con el ratón, así que
/// el gato que camina o el que nunca moviste aparecían en sitios distintos en cada sesión.
/// </summary>
public static class CatPositions
{
    private const double Tolerance = 0.5;

    /// <summary>
    /// Escribe la posición de cada gato en <paramref name="settings"/>.
    /// </summary>
    /// <param name="knownX">
    /// La X de cada gato tal como la conocía quien guarda. Si en el disco ya es otra, la cambió
    /// otra sesión después (p. ej. un guardado tardío al irse no debe pisar lo que movió quien
    /// acaba de llegar): ese gato se omite.
    /// </param>
    public static void Apply(AppSettings settings, IEnumerable<CatSnapshot> cats,
        IReadOnlyDictionary<string, double?> knownX)
    {
        foreach (var cat in cats)
        {
            if (!settings.Cats.TryGetValue(cat.Key, out var pref))
            {
                // Gato aún sin preferencias: se crean con su aspecto actual y esta posición.
                settings.Cats[cat.Key] = new CatPref { Color = cat.Color, Fat = cat.Fat, X = cat.X };
                continue;
            }

            knownX.TryGetValue(cat.Key, out var known);
            if (!Same(pref.X, known)) continue; // otra sesión lo movió desde que lo vimos

            pref.X = cat.X; // solo la posición: color y gordura no se tocan
        }
    }

    private static bool Same(double? a, double? b) =>
        a is null ? b is null : b is not null && Math.Abs(a.Value - b.Value) <= Tolerance;
}
