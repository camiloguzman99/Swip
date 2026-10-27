namespace Swip.Shared.Art;

/// <summary>
/// El aspecto de cada gato. Cada gato está ligado a UN usuario y no cambia: no hay selector de color ni
/// variantes. Hay un dibujo por gato ("orange", el primero, y "gray", el siguiente); se asigna la primera
/// vez que aparece el usuario y queda guardado en los ajustes compartidos, así es el mismo en todas las
/// sesiones.
/// </summary>
public static class CatSkins
{
    public const string Orange = "orange";
    public const string Gray = "gray";

    /// <summary>Los dibujos disponibles, en el orden en que se reparten entre usuarios nuevos.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Orange, Gray };

    public static bool IsValid(string? skin) =>
        All.Any(s => string.Equals(s, skin, StringComparison.OrdinalIgnoreCase));

    /// <summary>Dibujo válido para un id guardado; los antiguos o desconocidos (negro, blanco...) pasan a naranja.</summary>
    public static string Normalize(string? skin) =>
        All.FirstOrDefault(s => string.Equals(s, skin, StringComparison.OrdinalIgnoreCase)) ?? Orange;

    /// <summary>
    /// Qué dibujo le toca a un usuario. Si ya tiene uno válido guardado, ese. Si no (usuario nuevo o un
    /// color antiguo de cuando se podía elegir), el primero libre: el naranja para el primero y el
    /// atigrado para el siguiente; con más usuarios que dibujos se vuelven a repartir en orden.
    /// </summary>
    /// <param name="saved">Lo guardado para este usuario (puede ser null o un valor antiguo).</param>
    /// <param name="takenByOthers">Dibujos que ya tienen los demás usuarios.</param>
    public static string Pick(string? saved, IEnumerable<string?> takenByOthers)
    {
        if (IsValid(saved)) return Normalize(saved);
        var taken = takenByOthers.Where(IsValid).Select(Normalize).ToList();
        var free = All.FirstOrDefault(s => !taken.Contains(s));
        return free ?? All[taken.Count % All.Count];
    }
}
