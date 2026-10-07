using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Swip.App.Art;

/// <summary>
/// Caja opaca de un sprite: el rectángulo mínimo que contiene los píxeles visibles. Los PNG tienen
/// mucho margen transparente (un gato dormido ocupa solo el 40% inferior de su imagen), así que la
/// zona "clicable" debe ser esta caja y no el rectángulo completo de la imagen, o el espacio vacío
/// encima del gato se tragaría los clics de las ventanas que hay debajo.
/// Se calcula una sola vez por imagen (los bitmaps están cacheados y congelados).
/// </summary>
public static class SpriteBounds
{
    /// <summary>Píxeles con alfa por encima de esto cuentan como "visibles".</summary>
    private const byte AlphaThreshold = 20;

    private static readonly Dictionary<ImageSource, Rect> Cache = new();

    /// <summary>Caja opaca en coordenadas normalizadas (0..1 sobre el ancho y alto de la imagen).</summary>
    public static Rect Opaque(ImageSource? source)
    {
        if (source is null) return Full;
        if (Cache.TryGetValue(source, out var cached)) return cached;
        var rect = Compute(source);
        Cache[source] = rect;
        return rect;
    }

    private static readonly Rect Full = new(0, 0, 1, 1);

    private static Rect Compute(ImageSource source)
    {
        if (source is not BitmapSource bitmap) return Full;

        var bgra = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        if (w <= 0 || h <= 0) return Full;

        int stride = w * 4;
        var pixels = new byte[stride * h];
        bgra.CopyPixels(pixels, stride, 0);

        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
        {
            int row = y * stride;
            for (int x = 0; x < w; x++)
            {
                if (pixels[row + x * 4 + 3] <= AlphaThreshold) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < 0) return Full; // imagen vacía: no dejar una zona de clic de tamaño cero
        return new Rect(minX / (double)w, minY / (double)h,
            (maxX - minX + 1) / (double)w, (maxY - minY + 1) / (double)h);
    }
}
