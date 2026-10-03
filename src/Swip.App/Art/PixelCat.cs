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
/// Construye los bitmaps pixel art del gato (16x16) a partir de rejillas de "slots" de color,
/// de modo que el mismo dibujo se puede pintar en distintos colores (tema). Luz desde arriba-
/// izquierda: 'H' resalta, 'S' sombrea. Se escala con NearestNeighbor en el control.
/// Slots: '.' transp | 'O' contorno | 'B' cuerpo | 'H' claro | 'S' sombra | 'P' rosa |
///        'E' ojo (saturado) | 'K' pupila/línea | 'W' brillo del ojo.
/// </summary>
public static class PixelCat
{
    public const int Size = 16;

    /// <summary>Temas de color disponibles, en orden (para asignar por defecto a cada gato).</summary>
    public static readonly string[] Themes =
        { "amarillo", "naranja", "gris", "negro", "blanco", "marron" };

    private static Color C(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);

    // Slots comunes a todos los temas.
    private static readonly Color Transp = Color.FromArgb(0, 0, 0, 0);
    private static readonly Color Pink = C(0xFF, 0x8F, 0xB1);
    private static readonly Color EyeWhite = C(0xFF, 0xFF, 0xFF);
    private static readonly Color Pupil = C(0x20, 0x20, 0x20);

    // Cada tema define O (contorno), B (cuerpo), H (claro), S (sombra), E (ojo).
    private static readonly Dictionary<string, (Color O, Color B, Color H, Color S, Color E)> ThemeColors = new()
    {
        ["amarillo"] = (C(0x4A, 0x34, 0x10), C(0xFF, 0xCD, 0x1A), C(0xFF, 0xE6, 0x80), C(0xE0, 0xA6, 0x00), C(0x2F, 0xB3, 0x6B)),
        ["naranja"]  = (C(0x4A, 0x2A, 0x0E), C(0xF2, 0x91, 0x3B), C(0xFF, 0xC0, 0x78), C(0xCC, 0x6B, 0x22), C(0x2E, 0x7D, 0x32)),
        ["gris"]     = (C(0x26, 0x26, 0x26), C(0xAE, 0xB6, 0xBD), C(0xD9, 0xDE, 0xE2), C(0x86, 0x8E, 0x96), C(0xE6, 0xA5, 0x2C)),
        ["negro"]    = (C(0x12, 0x12, 0x12), C(0x4A, 0x4A, 0x4A), C(0x6E, 0x6E, 0x6E), C(0x2A, 0x2A, 0x2A), C(0x7B, 0xD6, 0x4B)),
        ["blanco"]   = (C(0x8A, 0x8A, 0x8A), C(0xF2, 0xF2, 0xF2), C(0xFF, 0xFF, 0xFF), C(0xCF, 0xCF, 0xCF), C(0x3B, 0x82, 0xC4)),
        ["marron"]   = (C(0x3A, 0x24, 0x10), C(0xA9, 0x74, 0x3F), C(0xC9, 0x97, 0x5C), C(0x7A, 0x51, 0x26), C(0x2E, 0x7D, 0x32)),
    };

    private static readonly string[] SitA =
    {
        "......O..O......",
        ".....OBO.OBO....",
        "....OBPBOBPBO...",
        "....OBBBBBBBO...",
        "...OHBBBBBBBBO..",
        "...OHBWEBBEWBO..",
        "...OHBBKBBKBBO..",
        "...OHBBBBBBBBO..",
        "...OHBBPPBBBBO..",
        "...OHBBBBBBBSO..",
        "...OOHBBBBBSOO..",
        "....OHBBBBBSO...",
        "....OHBBBBBSO...",
        "....OHBSBBSBO...",
        "....OBOBBOBBO...",
        ".....OO..OO.....",
    };

    private static readonly string[] SitB =
    {
        "......O..O......",
        ".....OBO.OBO....",
        "....OBPBOBPBO...",
        "....OBBBBBBBO...",
        "...OHBBBBBBBBO..",
        "...OHBBBBBBBBO..",
        "...OHBKKBBKKBO..",
        "...OHBBBBBBBBO..",
        "...OHBBPPBBBBO..",
        "...OHBBBBBBBSO..",
        "...OOHBBBBBSOO..",
        "....OHBBBBBSO...",
        "....OHBBBBBSO...",
        "....OHBSBBSBO...",
        "....OOBBBBOO....",
        ".....OO..OO.....",
    };

    private static readonly string[] Happy =
    {
        "......O..O......",
        ".....OBO.OBO....",
        "....OBPBOBPBO...",
        "....OBBBBBBBO...",
        "...OHBBBBBBBBO..",
        "...OHBKBBBBKBO..",
        "...OHKBKBBKBKO..",
        "...OHBBBBBBBBO..",
        "...OHBBPPBBBBO..",
        "...OHBBBBBBBSO..",
        "...OOHBBBBBSOO..",
        "....OHBBBBBSO...",
        "....OHBBBBBSO...",
        "....OHBSBBSBO...",
        "....OBOBBOBBO...",
        ".....OO..OO.....",
    };

    private static readonly string[] SleepA =
    {
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        ".......OOO......",
        "....OOOBBBOO....",
        "..OOHBBBBBBBOO..",
        ".OHBBBBBBBBBBBO.",
        ".OHBKKBBBBBKKBO.",
        ".OHBBBBPPBBBBSO.",
        ".OHBBBBBBBBBBSO.",
        ".OOHBBBBBBBBSOO.",
        "..OOOOOOOOOOOO..",
    };

    private static readonly string[] SleepB =
    {
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        ".......OOO......",
        "....OOOBBBOO....",
        "..OOHBBBBBBBOO..",
        ".OHBBBBBBBBBBBO.",
        ".OHBKKBBBBBKKBO.",
        ".OHBBBBPPBBBBSO.",
        ".OHBBBBBBBBBBSO.",
        ".OHBBBBBBBBBBSO.",
        ".OOHBBBBBBBBSOO.",
        "..OOOOOOOOOOOO..",
    };

    private static readonly Dictionary<CatFrame, string[]> Grids = new()
    {
        [CatFrame.SitA] = SitA,
        [CatFrame.SitB] = SitB,
        [CatFrame.Happy] = Happy,
        [CatFrame.SleepA] = SleepA,
        [CatFrame.SleepB] = SleepB,
    };

    // Caché: (frame, espejado, tema) -> bitmap.
    private static readonly Dictionary<(CatFrame, bool, string), BitmapSource> Cache = new();

    public static string NormalizeTheme(string? theme) =>
        !string.IsNullOrEmpty(theme) && ThemeColors.ContainsKey(theme) ? theme : Themes[0];

    /// <summary>Devuelve el bitmap de un frame en el tema de color indicado, opcionalmente espejado.</summary>
    public static BitmapSource Get(CatFrame frame, bool mirrored, string theme)
    {
        theme = NormalizeTheme(theme);
        var key = (frame, mirrored, theme);
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var bmp = Build(Grids[frame], mirrored, theme);
        Cache[key] = bmp;
        return bmp;
    }

    private static Color SlotColor(char c, (Color O, Color B, Color H, Color S, Color E) t) => c switch
    {
        'O' => t.O,
        'B' => t.B,
        'H' => t.H,
        'S' => t.S,
        'E' => t.E,
        'P' => Pink,
        'W' => EyeWhite,
        'K' => Pupil,
        _ => Transp,
    };

    private static BitmapSource Build(string[] grid, bool mirrored, string theme)
    {
        var colors = ThemeColors[theme];
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
                Color color = SlotColor(c, colors);

                int i = y * stride + x * 4;
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
