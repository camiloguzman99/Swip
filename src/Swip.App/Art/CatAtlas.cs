using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Swip.Shared.Art;

namespace Swip.App.Art;

/// <summary>
/// Hojas de sprites de los gatos (Assets/atlas, generadas por tools/art/build_atlas.py): un PNG por
/// piel con todos los fotogramas de todos los clips, y atlas.json con la celda de cada fotograma y
/// la caja de sus píxeles visibles. Cada celda es la CUADRÍCULA del fotograma a tamaño real
/// (1 casilla = 1 px lógico). Cada gato tiene su propio dibujo fijo (<see cref="CatSkins"/>): no hay
/// retintado ni variantes. El espejo y la ampliación entera (<see cref="PixelScale.Prescale"/>) se
/// hacen aquí, sobre los píxeles, para que el dibujo siga nítido. Todo se cachea y se congela.
/// </summary>
public sealed class CatAtlas
{
    /// <summary>Proporciones del lienzo de cada fotograma (ancho / alto).</summary>
    public double Aspect { get; }

    private readonly int _cellW, _cellH, _columns;
    private readonly Dictionary<string, BitmapSource> _sheets = new();
    private readonly Dictionary<string, ClipFrames> _clips = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Skin, string Clip, int Frame, bool Mirrored, int Prescale), ImageSource> _cache = new();
    private readonly Dictionary<(string Skin, string Clip), double> _headCache = new();
    // Cuadros retocados desde el Visor (ArtStore): píxeles BGRA de la cuadrícula entera. Tienen prioridad sobre la hoja.
    private readonly Dictionary<(string Skin, string Clip, int Frame), byte[]> _overrides = new();
    private readonly Dictionary<(string Skin, string Clip, int Frame), Rect> _overrideBounds = new();
    private const byte VisibleAlpha = 20;

    private sealed record FrameMeta(int Cell, Dictionary<string, Rect> Bounds);
    private sealed record ClipFrames(FrameMeta[] Frames);

    private CatAtlas(JsonElement root)
    {
        var cell = root.GetProperty("cell");
        _cellW = cell[0].GetInt32();
        _cellH = cell[1].GetInt32();
        _columns = root.GetProperty("columns").GetInt32();
        Aspect = _cellW / (double)_cellH;
        BaseRows = root.TryGetProperty("base", out var baseRows) ? baseRows.GetInt32() : _cellH;
        Main = root.TryGetProperty("main", out var main)
            ? new Int32Rect(main[0].GetInt32(), main[1].GetInt32(), main[2].GetInt32(), main[3].GetInt32())
            : new Int32Rect(0, 0, _cellW, BaseRows);

        foreach (var skin in root.GetProperty("skins").EnumerateObject())
            _sheets[skin.Name] = LoadBitmap($"Assets/atlas/{skin.Value.GetString()}");

        foreach (var clip in root.GetProperty("clips").EnumerateObject())
        {
            var frames = clip.Value.GetProperty("frames").EnumerateArray().Select(f =>
            {
                var bounds = new Dictionary<string, Rect>();
                foreach (var b in f.GetProperty("bounds").EnumerateObject())
                    bounds[b.Name] = new Rect(b.Value[0].GetDouble(), b.Value[1].GetDouble(),
                        b.Value[2].GetDouble(), b.Value[3].GetDouble());
                return new FrameMeta(f.GetProperty("cell").GetInt32(), bounds);
            }).ToArray();
            _clips[clip.Name] = new ClipFrames(frames);
        }
    }

    private static CatAtlas? _default;
    public static CatAtlas Default => _default ??= Load();

    private static CatAtlas Load()
    {
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/atlas/atlas.json"))
            ?? throw new InvalidOperationException("Falta Assets/atlas/atlas.json");
        using var reader = new StreamReader(info.Stream);
        using var doc = JsonDocument.Parse(reader.ReadToEnd());
        return new CatAtlas(doc.RootElement);
    }

    /// <summary>
    /// Imagen de un fotograma del gato <paramref name="skin"/>, espejada si mira a la izquierda y
    /// ampliada píxel a píxel por <paramref name="prescale"/> (1 = tamaño real).
    /// </summary>
    public ImageSource Frame(string skin, string clip, int frame, bool mirrored = false, int prescale = 1)
    {
        skin = CatSkins.Normalize(skin);
        var meta = Meta(clip, frame);
        if (meta is null) return Frame(skin, "sleep_curl", 0, mirrored, prescale);
        prescale = Math.Clamp(prescale, 1, 8);
        var key = (skin, clip, frame, mirrored, prescale);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        int stride = _cellW * 4;
        var pixels = FramePixels(skin, clip, frame);

        // Espejo y ampliación entera: cada casilla pasa a ser un bloque de prescale x prescale píxeles.
        int w = _cellW * prescale, h = _cellH * prescale, outStride = w * 4;
        var output = new byte[outStride * h];
        for (int y = 0; y < h; y++)
        {
            int sy = y / prescale;
            for (int x = 0; x < w; x++)
            {
                int sx = x / prescale;
                if (mirrored) sx = _cellW - 1 - sx;
                Buffer.BlockCopy(pixels, sy * stride + sx * 4, output, y * outStride + x * 4, 4);
            }
        }
        var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, output, outStride);
        result.Freeze();
        _cache[key] = result;
        return result;
    }

    /// <summary>
    /// Píxeles BGRA (GridWidth x GridHeight) de un fotograma tal como se dibuja: el retocado si lo hay y, si no, el
    /// de la hoja. Es una copia: se puede modificar (el editor del Visor parte de aquí).
    /// </summary>
    public byte[] FramePixels(string skin, string clip, int frame)
    {
        skin = CatSkins.Normalize(skin);
        var meta = Meta(clip, frame);
        if (meta is null) { clip = "sleep_curl"; frame = 0; meta = Meta(clip, 0)!; }
        frame = Math.Clamp(frame, 0, _clips[clip].Frames.Length - 1);
        if (_overrides.TryGetValue((skin, clip, frame), out var edited)) return (byte[])edited.Clone();

        var sheet = _sheets.TryGetValue(skin, out var s) ? s : _sheets.Values.First();
        int col = meta.Cell % _columns, row = meta.Cell / _columns;
        var crop = new CroppedBitmap(sheet, new Int32Rect(col * _cellW, row * _cellH, _cellW, _cellH));
        var bgra = new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[_cellW * 4 * _cellH];
        bgra.CopyPixels(pixels, _cellW * 4, 0);
        return pixels;
    }

    /// <summary>True si el fotograma tiene un retoque guardado.</summary>
    public bool IsEdited(string skin, string clip, int frame) =>
        _overrides.ContainsKey((CatSkins.Normalize(skin), clip, frame));

    /// <summary>Cuántos fotogramas tiene un clip (0 si no existe).</summary>
    public int FrameCount(string clip) => _clips.TryGetValue(clip, out var c) ? c.Frames.Length : 0;

    /// <summary>Pone (o quita, con null) el retoque de un fotograma y borra lo que se había dibujado con el anterior.</summary>
    public void SetOverride(string skin, string clip, int frame, byte[]? bgra)
    {
        skin = CatSkins.Normalize(skin);
        var key = (skin, clip, frame);
        if (bgra is null) { _overrides.Remove(key); _overrideBounds.Remove(key); }
        else if (bgra.Length == _cellW * _cellH * 4) { _overrides[key] = bgra; _overrideBounds.Remove(key); }
        Invalidate(skin, clip, frame);
    }

    /// <summary>Vuelve a leer TODOS los retoques de la carpeta compartida (otra sesión pudo cambiarlos).</summary>
    public void ReloadOverrides(ArtStore store)
    {
        _overrides.Clear();
        _overrideBounds.Clear();
        foreach (string skin in CatSkins.All)
            foreach (var ((clip, frame), pixels) in store.LoadFrames(skin, _cellW, _cellH))
                if (_clips.TryGetValue(clip, out var c) && frame < c.Frames.Length)
                    _overrides[(skin, clip, frame)] = pixels;
        _cache.Clear();
        _headCache.Clear();
    }

    private void Invalidate(string skin, string clip, int frame)
    {
        foreach (var key in _cache.Keys.Where(k => k.Skin == skin && k.Clip == clip && k.Frame == frame).ToList())
            _cache.Remove(key);
        _headCache.Remove((skin, clip));
    }

    /// <summary>Tamaño de la cuadrícula de cada fotograma (casillas = px lógicos).</summary>
    public int GridWidth => _cellW;
    public int GridHeight => _cellH;

    /// <summary>
    /// Filas por encima de la BASE, la línea donde se apoyan los pies (48). Por debajo quedan
    /// <c>GridHeight - BaseRows</c> filas para lo que cuelga (la cola, algo que se cae...).
    /// </summary>
    public int BaseRows { get; }

    /// <summary>
    /// Cuadro PRINCIPAL dentro de la cuadrícula (100x100, verde en los marcos): el gato para el motor.
    /// A sus lados quedan 40 columnas (correr, estirarse) y debajo 30 filas (lo que cuelga).
    /// </summary>
    public Int32Rect Main { get; }

    /// <summary>Zonas extra del cuadro (laterales y de abajo), para los marcos.</summary>
    public Int32Rect[] ExtraZones => new[]
    {
        new Int32Rect(0, Main.Y, Main.X, Main.Height),
        new Int32Rect(Main.X + Main.Width, Main.Y, _cellW - Main.X - Main.Width, Main.Height),
        new Int32Rect(Main.X, Main.Y + Main.Height, Main.Width, _cellH - Main.Y - Main.Height),
    };

    /// <summary>Caja de los píxeles visibles del fotograma (0..1 sobre el lienzo, sin espejar).</summary>
    public Rect Bounds(string skin, string clip, int frame)
    {
        var meta = Meta(clip, frame);
        if (meta is null) return new Rect(0, 0, 1, 1);
        skin = CatSkins.Normalize(skin);
        int index = Math.Clamp(frame, 0, _clips[clip].Frames.Length - 1);
        if (_overrides.TryGetValue((skin, clip, index), out var edited))
        {
            if (!_overrideBounds.TryGetValue((skin, clip, index), out var box))
                _overrideBounds[(skin, clip, index)] = box = OpaqueBounds(edited);
            return box;
        }
        return meta.Bounds.TryGetValue(skin, out var r) ? r : meta.Bounds.Values.First();
    }

    /// <summary>
    /// Altura de la cabeza en el clip (fracción 0..1 desde arriba): el punto visible más alto de
    /// todos sus fotogramas. Sirve para el rótulo del nombre y para los efectos (zzz, corazones).
    /// </summary>
    public double HeadFraction(string skin, string clip)
    {
        skin = CatSkins.Normalize(skin);
        if (_headCache.TryGetValue((skin, clip), out var v)) return v;
        double top = 0.3;
        if (_clips.TryGetValue(clip, out var c) && c.Frames.Length > 0)
            top = Enumerable.Range(0, c.Frames.Length).Min(i => Bounds(skin, clip, i).Y);
        _headCache[(skin, clip)] = top;
        return top;
    }

    /// <summary>Caja de los píxeles visibles de un fotograma retocado (0..1 sobre la cuadrícula).</summary>
    private Rect OpaqueBounds(byte[] bgra)
    {
        int minX = _cellW, minY = _cellH, maxX = -1, maxY = -1;
        for (int y = 0; y < _cellH; y++)
            for (int x = 0; x < _cellW; x++)
                if (bgra[(y * _cellW + x) * 4 + 3] > VisibleAlpha)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
        if (maxX < 0) return new Rect(0, 0, 0.01, 0.01);
        return new Rect(minX / (double)_cellW, minY / (double)_cellH,
            (maxX - minX + 1) / (double)_cellW, (maxY - minY + 1) / (double)_cellH);
    }

    private FrameMeta? Meta(string clip, int frame) =>
        _clips.TryGetValue(clip, out var c) && c.Frames.Length > 0
            ? c.Frames[Math.Clamp(frame, 0, c.Frames.Length - 1)]
            : null;

    private static BitmapSource LoadBitmap(string relativePath)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.UriSource = new Uri($"pack://application:,,,/{relativePath}", UriKind.Absolute);
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }
}
