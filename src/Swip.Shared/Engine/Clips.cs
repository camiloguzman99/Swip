using System.Reflection;
using System.Text.Json;

namespace Swip.Shared.Engine;

/// <summary>Un clip de animación: N fotogramas a una velocidad, en bucle o de una sola vez.</summary>
public sealed record ClipDef(string Id, int FrameCount, double Fps, bool Loop, string Status, string Description)
{
    /// <summary>Duración de una pasada completa, en segundos.</summary>
    public double Length => FrameCount / Fps;

    /// <summary>True si el clip aún usa fotogramas de referencia y espera su arte definitivo.</summary>
    public bool IsPlaceholder => Status != "final";

    /// <summary>Fotograma a mostrar a los <paramref name="time"/> segundos de empezar.</summary>
    public int FrameAt(double time)
    {
        if (FrameCount <= 1 || time <= 0) return 0;
        int index = (int)(time * Fps);
        return Loop ? index % FrameCount : Math.Min(index, FrameCount - 1);
    }
}

/// <summary>
/// Los clips de art/clips.json (fuente única, incrustada en este ensamblado). El generador de atlas
/// (tools/art/build_atlas.py) lee el mismo archivo, así que motor y arte nunca se desincronizan.
/// </summary>
public sealed class ClipLibrary
{
    private readonly Dictionary<string, ClipDef> _clips;

    public ClipLibrary(IEnumerable<ClipDef> clips) =>
        _clips = clips.ToDictionary(c => c.Id, StringComparer.Ordinal);

    public IReadOnlyCollection<ClipDef> All => _clips.Values;

    public ClipDef this[string id] =>
        _clips.TryGetValue(id, out var clip) ? clip : throw new KeyNotFoundException($"No existe el clip '{id}'.");

    public bool Contains(string id) => _clips.ContainsKey(id);

    public static ClipLibrary Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<ClipDef>();
        foreach (var c in doc.RootElement.GetProperty("clips").EnumerateArray())
        {
            list.Add(new ClipDef(
                c.GetProperty("id").GetString()!,
                c.GetProperty("reference").GetArrayLength(),
                c.GetProperty("fps").GetDouble(),
                c.GetProperty("loop").GetBoolean(),
                c.GetProperty("status").GetString()!,
                c.GetProperty("description").GetString()!));
        }
        return new ClipLibrary(list);
    }

    private static ClipLibrary? _default;

    /// <summary>Los clips incrustados en el ensamblado (art/clips.json).</summary>
    public static ClipLibrary Default => _default ??= LoadEmbedded();

    private static ClipLibrary LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Swip.clips.json")
            ?? throw new InvalidOperationException("Falta el recurso incrustado Swip.clips.json.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
}
