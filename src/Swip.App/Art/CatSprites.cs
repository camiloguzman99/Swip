using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Swip.App.Art;

/// <summary>Acciones del gato (cada una con 2 frames de animación).</summary>
public enum CatAction
{
    Sleep, // sesión cerrada (sin sesión)
    Walk,  // sesión activa (en pantalla ahora)
    Play,  // sesión en espera (iniciada pero en el otro escritorio)
    Carry, // mientras se arrastra el gato
}

/// <summary>
/// Carga los sprites de los gatos desde los assets (arte del usuario) por (color, acción, frame).
/// Relación de aspecto del lienzo: 271x381. Los bitmaps se cachean y congelan.
/// </summary>
public static class CatSprites
{
    public const double AspectW = 269.0;
    public const double AspectH = 379.0;

    public static readonly string[] Colors = { "orange", "gray" };

    private static readonly Dictionary<string, BitmapImage> Cache = new();

    public static string Normalize(string? color) =>
        color == "gray" ? "gray" : "orange";

    public static ImageSource Get(string color, CatAction action, int frame)
    {
        color = Normalize(color);
        frame &= 1;
        string act = action.ToString().ToLowerInvariant();
        string path = $"Assets/cats/cat_{color}_{act}_{frame}.png";
        if (!Cache.TryGetValue(path, out var bi))
        {
            bi = Load(path);
            Cache[path] = bi;
        }
        return bi;
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

    public static double TopInset(bool open) => open ? TopInsetOpen : TopInsetClosed;

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
