using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Swip.App.Art;

/// <summary>Los frames del gato pixel art.</summary>
public enum CatFrame
{
    SitA,
    SitB,
    Happy,
    SleepA,
    SleepB,
}

/// <summary>
/// Construye los bitmaps pixel art del gato (16x16) a partir de rejillas de caracteres validadas.
/// Los bitmaps se escalan con NearestNeighbor en el control para mantener el aspecto pixelado.
/// Leyenda: '.' transp | 'o' contorno | 'y' amarillo | 'g' sombra | 'p' rosa | 'w' blanco | 'k' pupila.
/// </summary>
public static class PixelCat
{
    public const int Size = 16;

    private static readonly Dictionary<char, Color> Palette = new()
    {
        ['.'] = Color.FromArgb(0, 0, 0, 0),
        ['o'] = Color.FromArgb(255, 0x3A, 0x2A, 0x0E),
        ['y'] = Color.FromArgb(255, 0xFF, 0xCD, 0x1A),
        ['g'] = Color.FromArgb(255, 0xE0, 0xA6, 0x00),
        ['p'] = Color.FromArgb(255, 0xFF, 0x8F, 0xB1),
        ['w'] = Color.FromArgb(255, 0xFF, 0xFF, 0xFF),
        ['k'] = Color.FromArgb(255, 0x20, 0x20, 0x20),
    };

    private static readonly string[] SitA =
    {
        "......o..o......",
        ".....oyooyo.....",
        "....oyyooyyo....",
        "....oyyyyyyyo...",
        "...oyyyyyyyyyo..",
        "...oywwyyywwyo..",
        "...oykwyyywkyo..",
        "...oyyyyyyyyyo..",
        "...oyyyppyyyyo..",
        "...oyyyyyyyyyo..",
        "...ooyyyyyyyoo..",
        "....oyyyyyyyo...",
        "....oyyyyyyyo...",
        "....oyygyygyo...",
        "....oyoyyoyyo...",
        ".....oo..oo.....",
    };

    private static readonly string[] SitB =
    {
        "......o..o......",
        ".....oyooyo.....",
        "....oyyooyyo....",
        "....oyyyyyyyo...",
        "...oyyyyyyyyyo..",
        "...oyyyyyyyyyo..",
        "...oykkyyykkyo..",
        "...oyyyyyyyyyo..",
        "...oyyyppyyyyo..",
        "...oyyyyyyyyyo..",
        "...ooyyyyyyyoo..",
        "....oyyyyyyyo...",
        "....oyyyyyyyo...",
        "....oyygyygyo...",
        "....ooyyyyoo....",
        ".....oo..oo.....",
    };

    private static readonly string[] Happy =
    {
        "......o..o......",
        ".....oyooyo.....",
        "....oyyooyyo....",
        "....oyyyyyyyo...",
        "...oyyyyyyyyyo..",
        "...oykyyyykyyo..",
        "...oyykyykyyyo..",
        "...oyyyyyyyyyo..",
        "...oyyyppyyyyo..",
        "...oyyyyyyyyyo..",
        "...ooyyyyyyyoo..",
        "....oyyyyyyyo...",
        "....oyyyyyyyo...",
        "....oyygyygyo...",
        "....oyoyyoyyo...",
        ".....oo..oo.....",
    };

    private static readonly string[] SleepA =
    {
        "................",
        "................",
        "................",
        "................",
        "......ooo.......",
        ".....oyyyoo.....",
        "..oooyyyyyyoo...",
        ".oyyyyyyyyyyyo..",
        ".oykkyyyyyykyo..",
        ".oyyyyppyyyyyyo.",
        ".oyyyyyyyyyyyyo.",
        ".ooyyyyyyyyyyoo.",
        "..oooooooooooo..",
        "................",
        "................",
        "................",
    };

    private static readonly string[] SleepB =
    {
        "................",
        "................",
        "................",
        "......ooo.......",
        ".....oyyyoo.....",
        "....oyyyyyyoo...",
        "..oooyyyyyyyyo..",
        ".oyyyyyyyyyyyo..",
        ".oykkyyyyyykyo..",
        ".oyyyyppyyyyyyo.",
        ".oyyyyyyyyyyyyo.",
        ".ooyyyyyyyyyyoo.",
        "..oooooooooooo..",
        "................",
        "................",
        "................",
    };

    private static readonly Dictionary<CatFrame, string[]> Grids = new()
    {
        [CatFrame.SitA] = SitA,
        [CatFrame.SitB] = SitB,
        [CatFrame.Happy] = Happy,
        [CatFrame.SleepA] = SleepA,
        [CatFrame.SleepB] = SleepB,
    };

    // Caché: (frame, espejado) -> bitmap.
    private static readonly Dictionary<(CatFrame, bool), BitmapSource> Cache = new();

    /// <summary>Devuelve el bitmap de un frame, opcionalmente espejado horizontalmente.</summary>
    public static BitmapSource Get(CatFrame frame, bool mirrored = false)
    {
        var key = (frame, mirrored);
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var bmp = Build(Grids[frame], mirrored);
        Cache[key] = bmp;
        return bmp;
    }

    private static BitmapSource Build(string[] grid, bool mirrored)
    {
        var wb = new WriteableBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32, null);
        int stride = Size * 4;
        var pixels = new byte[Size * stride];

        for (int y = 0; y < Size; y++)
        {
            string row = grid[y];
            for (int x = 0; x < Size; x++)
            {
                int srcX = mirrored ? Size - 1 - x : x;
                char c = srcX < row.Length ? row[srcX] : '.';
                Color color = Palette.TryGetValue(c, out var col) ? col : Palette['.'];

                int i = y * stride + x * 4;
                // Pbgra32: B, G, R, A (premultiplicado; alpha es 0 o 255 así que no hace falta multiplicar).
                pixels[i + 0] = color.B;
                pixels[i + 1] = color.G;
                pixels[i + 2] = color.R;
                pixels[i + 3] = color.A;
            }
        }

        wb.WritePixels(new System.Windows.Int32Rect(0, 0, Size, Size), pixels, stride, 0);
        wb.Freeze();
        return wb;
    }
}
