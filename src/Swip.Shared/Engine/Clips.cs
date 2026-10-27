using System.Reflection;
using System.Text.Json;

namespace Swip.Shared.Engine;

/// <summary>
/// Un clip de animación: N fotogramas a una velocidad, en bucle o de una sola vez. Si el gato se
/// desplaza con este clip, <paramref name="Advance"/> dice cuántas casillas (px lógicos) avanza en
/// cada fotograma: el gato solo se mueve cuando cambia el fotograma, así que los pies y el
/// desplazamiento van siempre a la par. Su velocidad sale de ahí (<see cref="Speed"/>).
/// </summary>
public sealed record ClipDef(string Id, int FrameCount, double Fps, bool Loop, string Status, string Description,
    int[]? Advance = null)
{
    /// <summary>Duración de una pasada completa, en segundos.</summary>
    public double Length => FrameCount / Fps;

    /// <summary>True si el clip aún usa fotogramas de referencia y espera su arte definitivo.</summary>
    public bool IsPlaceholder => Status != "final";

    /// <summary>Fotograma a mostrar a los <paramref name="time"/> segundos de empezar.</summary>
    public int FrameAt(double time) => FrameOfTick(TickAt(time));

    /// <summary>Cuántos cambios de fotograma han pasado a los <paramref name="time"/> segundos (sin dar la vuelta).</summary>
    public int TickAt(double time) => time <= 0 ? 0 : (int)(time * Fps);

    /// <summary>Fotograma que corresponde al cambio número <paramref name="tick"/>.</summary>
    public int FrameOfTick(int tick)
    {
        if (FrameCount <= 1 || tick <= 0) return 0;
        return Loop ? tick % FrameCount : Math.Min(tick, FrameCount - 1);
    }

    /// <summary>Casillas que avanza el gato al mostrar el fotograma <paramref name="frame"/>.</summary>
    public int AdvanceOf(int frame) =>
        Advance is not { Length: > 0 } a ? 0 : a[Math.Clamp(frame, 0, FrameCount - 1) % a.Length];

    /// <summary>Velocidad media resultante (px lógicos por segundo): avance medio por fotograma × fps.</summary>
    public double Speed => Advance is { Length: > 0 } a ? a.Average() * Fps : 0;
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
            int[]? advance = null;
            if (c.TryGetProperty("advance", out var a))
                advance = a.ValueKind == JsonValueKind.Array
                    ? a.EnumerateArray().Select(v => v.GetInt32()).ToArray()
                    : new[] { a.GetInt32() };
            list.Add(new ClipDef(
                c.GetProperty("id").GetString()!,
                c.GetProperty("reference").GetArrayLength(),
                c.GetProperty("fps").GetDouble(),
                c.GetProperty("loop").GetBoolean(),
                c.GetProperty("status").GetString()!,
                c.GetProperty("description").GetString()!,
                advance));
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
