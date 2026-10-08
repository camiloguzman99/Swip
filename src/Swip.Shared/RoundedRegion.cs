namespace Swip.Shared;

/// <summary>Parámetros en PÍXELES para <c>CreateRoundRectRgn</c> (región de la ventana con esquinas redondeadas).</summary>
/// <param name="Right">Borde derecho EXCLUSIVO: las regiones de GDI no incluyen el último píxel, por eso es ancho + 1.</param>
/// <param name="Bottom">Borde inferior EXCLUSIVO (alto + 1).</param>
public readonly record struct RoundedRegionSize(int Right, int Bottom, int EllipseWidth, int EllipseHeight);

/// <summary>
/// El desenfoque de Windows cubre TODA la ventana (un rectángulo), pero el panel de los menús solo
/// rellena su forma redondeada. Se recorta la ventana a esa forma; aquí la aritmética, aparte para
/// poder probarla.
/// </summary>
public static class RoundedRegion
{
    /// <summary>
    /// Píxeles de radio de MÁS que tiene la región respecto al panel. El recorte de Windows tiene el
    /// borde duro y el tinte del panel lo dibuja WPF suavizado: donde el tinte queda a medio cubrir
    /// en una curva, el desenfoque se veía sin tinte como un hilo claro. Con la región algo más
    /// cerrada en las esquinas, el desenfoque nunca llega a esa franja suavizada. (Los bordes rectos
    /// no se ven afectados.)
    /// </summary>
    public const int CornerExtraPx = 3;

    /// <param name="widthPx">Ancho REAL de la ventana en píxeles (no el calculado a partir del panel).</param>
    /// <returns>Null si la ventana aún no tiene tamaño.</returns>
    public static RoundedRegionSize? FromPixels(
        int widthPx, int heightPx, double cornerRadiusDip, double dpiScaleX, double dpiScaleY)
    {
        if (widthPx <= 0 || heightPx <= 0) return null;

        // CreateRoundRectRgn recibe el DIÁMETRO de la elipse de cada esquina, no el radio; y no
        // puede ser mayor que el lado, o la forma sale deformada.
        int ellipseWidth = Math.Min(Round(2 * cornerRadiusDip * dpiScaleX) + 2 * CornerExtraPx, widthPx);
        int ellipseHeight = Math.Min(Round(2 * cornerRadiusDip * dpiScaleY) + 2 * CornerExtraPx, heightPx);

        return new RoundedRegionSize(widthPx + 1, heightPx + 1, ellipseWidth, ellipseHeight);
    }

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
