using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Swip.App.Art;

/// <summary>
/// Caja de cartón pixel art (16x16) para la esquina de configuración: cerrada por defecto y
/// abierta al pulsarla (a los gatos les gustan más las cajas que las casas 🐱📦).
/// Slots: '.' transp | 'o' contorno | 'b' cartón | 'd' sombra | 'l' claro | 'i' interior | 't' cinta.
/// </summary>
public static class PixelBox
{
    public const int Size = 16;

    private static Color C(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);

    private static readonly Dictionary<char, Color> Palette = new()
    {
        ['.'] = Color.FromArgb(0, 0, 0, 0),
        ['o'] = C(0x14, 0x14, 0x14),
        ['b'] = C(0xD9, 0x8B, 0x3C),
        ['d'] = C(0xB0, 0x6E, 0x29),
        ['l'] = C(0xE8, 0xA5, 0x52),
        ['i'] = C(0x7A, 0x4E, 0x20),
        ['t'] = C(0xC7, 0x82, 0x2F),
    };

    private static readonly string[] Closed =
    {
        "................",
        "................",
        "................",
        "................",
        "..oooooooooooo..",
        "..olllllllllbo..",
        "..obbbbbbbbbdo..",
        "..obbbbbbbbbdo..",
        "..obbbbbbbbbdo..",
        "..obbbbbbtbbdo..",
        "..obbbbbbtbbdo..",
        "..obbbbbbbbbdo..",
        "..obbbbbbbbbdo..",
        "..obbbbbbbbbdo..",
        "..obbbbbbbbbdo..",
        "..oooooooooooo..",
    };

    private static readonly string[] Open =
    {
        "oo..........oo..",
        "oooo......oooo..",
        ".ioooo..ooooi...",
        "..oiioooooiio...",
        "..oiiiiiiiiiio..",
        "..oiiiiiiiiiio..",
        "..oooooooooooo..",
        "..obbbbbbbbbdo..",
        "..obbbbbbbbbdo..",
        "..obbtbbbbbbdo..",
        "..obbbtbbbbbdo..",
        "..obbbbbbbbbdo..",
        "..obbbbbbbbbdo..",
        "..obbbbbbbbbdo..",
        "..obbbbbbbbbdo..",
        "..oooooooooooo..",
    };

    private static BitmapSource? _closed;
    private static BitmapSource? _open;

    public static BitmapSource Get(bool open)
    {
        if (open) return _open ??= Build(Open);
        return _closed ??= Build(Closed);
    }

    private static BitmapSource Build(string[] grid)
    {
        var wb = new WriteableBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32, null);
        int stride = Size * 4;
        var pixels = new byte[Size * stride];

        for (int y = 0; y < Size; y++)
        {
            string row = grid[y];
            for (int x = 0; x < Size; x++)
            {
                char c = x < row.Length ? row[x] : '.';
                Color color = Palette.TryGetValue(c, out var col) ? col : Palette['.'];
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
