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
/// (1 casilla = 1 px lógico). Cada paleta parte de una piel y se retiñe al cargar
/// (<see cref="Recolor"/>). El espejo y la ampliación entera (<see cref="PixelScale.Prescale"/>) se
/// hacen aquí, sobre los píxeles, para que el dibujo siga nítido. Todo se cachea y se congela.
/// </summary>
public sealed class CatAtlas
{
    /// <summary>Proporciones del lienzo de cada fotograma (ancho / alto).</summary>
    public double Aspect { get; }

    private readonly int _cellW, _cellH, _columns;
    private readonly Dictionary<string, BitmapSource> _sheets = new();
    private readonly Dictionary<string, ClipFrames> _clips = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Palette, string Clip, int Frame, bool Mirrored, int Prescale), ImageSource> _cache = new();
    private readonly Dictionary<(string Skin, string Clip), double> _headCache = new();

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
    /// Imagen de un fotograma con la paleta indicada (retinte incluido), espejada si mira a la
    /// izquierda y ampliada píxel a píxel por <paramref name="prescale"/> (1 = tamaño real).
    /// </summary>
    public ImageSource Frame(string paletteId, string clip, int frame, bool mirrored = false, int prescale = 1)
    {
        var palette = CatPalettes.Get(paletteId);
        var meta = Meta(clip, frame);
        if (meta is null) return Frame(palette.Id, "sleep_curl", 0, mirrored, prescale);
        prescale = Math.Clamp(prescale, 1, 8);
        var key = (palette.Id, clip, frame, mirrored, prescale);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var sheet = _sheets.TryGetValue(palette.Skin, out var s) ? s : _sheets.Values.First();
        int col = meta.Cell % _columns, row = meta.Cell / _columns;
        var crop = new CroppedBitmap(sheet, new Int32Rect(col * _cellW, row * _cellH, _cellW, _cellH));
        var bgra = new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);
        int stride = _cellW * 4;
        var pixels = new byte[stride * _cellH];
        bgra.CopyPixels(pixels, stride, 0);
        if (palette.Recolors) Recolor.Apply(pixels, palette);

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

    /// <summary>Tamaño de la cuadrícula de cada fotograma (casillas = px lógicos).</summary>
    public int GridWidth => _cellW;
    public int GridHeight => _cellH;

    /// <summary>
    /// Filas por encima de la BASE, la línea donde se apoyan los pies (48). Por debajo quedan
    /// <c>GridHeight - BaseRows</c> filas para lo que cuelga (la cola, algo que se cae...).
    /// </summary>
    public int BaseRows { get; }

    /// <summary>Caja de los píxeles visibles del fotograma (0..1 sobre el lienzo, sin espejar).</summary>
    public Rect Bounds(string paletteId, string clip, int frame)
    {
        var meta = Meta(clip, frame);
        if (meta is null) return new Rect(0, 0, 1, 1);
        string skin = CatPalettes.Get(paletteId).Skin;
        return meta.Bounds.TryGetValue(skin, out var r) ? r : meta.Bounds.Values.First();
    }

    /// <summary>
    /// Altura de la cabeza en el clip (fracción 0..1 desde arriba): el punto visible más alto de
    /// todos sus fotogramas. Sirve para el rótulo del nombre y para los efectos (zzz, corazones).
    /// </summary>
    public double HeadFraction(string paletteId, string clip)
    {
        string skin = CatPalettes.Get(paletteId).Skin;
        if (_headCache.TryGetValue((skin, clip), out var v)) return v;
        double top = 0.3;
        if (_clips.TryGetValue(clip, out var c) && c.Frames.Length > 0)
            top = c.Frames.Min(f => f.Bounds.TryGetValue(skin, out var r) ? r.Y : 0.3);
        _headCache[(skin, clip)] = top;
        return top;
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
