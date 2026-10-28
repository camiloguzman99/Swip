using System.Text.Json;
using Swip.Shared.Engine;

namespace Swip.Shared.Art;

/// <summary>Retoque de tiempo de un clip: fotogramas por segundo y, opcionalmente, avance por cuadro (en casillas).</summary>
public sealed record ClipTimingEntry(double Fps, int[]? Advance);

/// <summary>
/// Los retoques de tiempo de los clips (la duración de cada transición, por ejemplo) que se hacen en el Visor.
/// Viven en %ProgramData%\Swip\art\timing.json, compartido por todas las sesiones, y se aplican sobre los clips
/// de art/clips.json. Como esa carpeta la puede escribir cualquier usuario, todo lo que se lee se valida: un clip que
/// no existe se ignora y los fps y el avance se limitan a rangos razonables.
/// </summary>
public static class ClipTimingFile
{
    public const double MinFps = 0.5, MaxFps = 60;
    public const int MaxAdvance = 64, MaxAdvanceLength = 64;
    private const int MaxFileBytes = 256 * 1024;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static double ClampFps(double fps) => double.IsFinite(fps) ? Math.Clamp(fps, MinFps, MaxFps) : MinFps;

    /// <summary>Lee y valida el JSON. Cualquier error (JSON roto, archivo enorme) devuelve «sin retoques».</summary>
    public static Dictionary<string, ClipTimingEntry> Parse(string json, ClipLibrary library)
    {
        var result = new Dictionary<string, ClipTimingEntry>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxFileBytes) return result;
        try
        {
            var file = JsonSerializer.Deserialize<FileShape>(json, Options);
            foreach (var (id, entry) in file?.Clips ?? new())
            {
                if (entry is null || !library.Contains(id) || !double.IsFinite(entry.Fps)) continue;
                int[]? advance = null;
                if (entry.Advance is { Length: > 0 } a && a.Length <= MaxAdvanceLength)
                    advance = a.Select(v => Math.Clamp(v, -MaxAdvance, MaxAdvance)).ToArray();
                result[id] = new ClipTimingEntry(ClampFps(entry.Fps), advance);
            }
        }
        catch (JsonException) { /* archivo roto: sin retoques */ }
        return result;
    }

    public static string Serialize(IReadOnlyDictionary<string, ClipTimingEntry> entries) =>
        JsonSerializer.Serialize(new FileShape { Clips = entries.ToDictionary(e => e.Key, e => (ClipTimingEntry?)e.Value) }, Options);

    /// <summary>Aplica los retoques a la biblioteca; los clips que no aparecen vuelven a su valor original.</summary>
    public static void Apply(ClipLibrary library, IReadOnlyDictionary<string, ClipTimingEntry> entries)
    {
        foreach (var clip in library.All.ToList())
        {
            if (entries.TryGetValue(clip.Id, out var e)) library.Override(clip.Id, e.Fps, e.Advance);
            else library.Reset(clip.Id);
        }
    }

    private sealed class FileShape
    {
        public Dictionary<string, ClipTimingEntry?> Clips { get; set; } = new();
    }
}
