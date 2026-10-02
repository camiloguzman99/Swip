using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Swip.App.Art;

/// <summary>
/// Casa pixel art (16x16) para la esquina de configuración. Se construye por código, igual que
/// los gatos, y se escala con NearestNeighbor.
/// Leyenda: '.' transp | 'o' contorno | 'r' techo | 'w' techo claro | 'y' pared | 'k' marco |
///          'g' cristal | 'n' puerta.
/// </summary>
public static class PixelHouse
{
    public const int Size = 16;

    private static readonly Dictionary<char, Color> Palette = new()
    {
        ['.'] = Color.FromArgb(0, 0, 0, 0),
        ['o'] = Color.FromArgb(255, 0x3A, 0x2A, 0x0E),
        ['r'] = Color.FromArgb(255, 0xD7, 0x43, 0x2B),
        ['w'] = Color.FromArgb(255, 0xF2, 0xA3, 0x5E),
        ['y'] = Color.FromArgb(255, 0xE9, 0xC4, 0x7A),
        ['k'] = Color.FromArgb(255, 0x3A, 0x2A, 0x0E),
        ['g'] = Color.FromArgb(255, 0x8F, 0xD0, 0xFF),
        ['n'] = Color.FromArgb(255, 0x7A, 0x4A, 0x1E),
    };

    private static readonly string[] Grid =
    {
        ".......oo.......",
        "......orro......",
        ".....orwwro.....",
        "....orwwwwro....",
        "...orwwwwwwro...",
        "..orwwwwwwwwro..",
        ".orwwwwwwwwwwro.",
        "oooooooooooooooo",
        ".oyyyyyyyyyyyyo.",
        ".oyggkoyyokggyo.",
        ".oyggkoyyokggyo.",
        ".oyyyyoyyoyyyyo.",
        ".oyyynnnnnnyyyo.",
        ".oyyynggnggnyyo.",
        ".oyyynnnnnnnyyo.",
        ".ooooonnnnnooooo"[..16],
    };

    private static BitmapSource? _cached;

    public static BitmapSource Get()
    {
        if (_cached is not null) return _cached;

        var wb = new WriteableBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32, null);
        int stride = Size * 4;
        var pixels = new byte[Size * stride];

        for (int y = 0; y < Size; y++)
        {
            string row = Grid[y];
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
        _cached = wb;
        return _cached;
    }
}
