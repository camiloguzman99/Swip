using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Swip.App.Art;

/// <summary>Carga los sprites de la caja de cartón (cerrada / abierta) desde los assets.</summary>
public static class BoxSprites
{
    public const double AspectW = 394.0;
    public const double AspectH = 282.0;

    // Margen transparente superior de cada PNG (fracción del alto del lienzo). La caja cerrada
    // empieza en y=46/282 y la abierta (con las solapas) en y=5/282; la colisión debe usar el
    // borde VISIBLE de cada estado, no el del lienzo, o el gato flota sobre la caja cerrada.
    public const double TopInsetClosed = 46.0 / 282.0;
    public const double TopInsetOpen = 5.0 / 282.0;

    private static BitmapImage? _closed;
    private static BitmapImage? _open;

    public static ImageSource Get(bool open)
    {
        if (open) return _open ??= Load("Assets/box/box_open.png");
        return _closed ??= Load("Assets/box/box_closed.png");
    }

    private static BitmapImage Load(string relativePath)
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
