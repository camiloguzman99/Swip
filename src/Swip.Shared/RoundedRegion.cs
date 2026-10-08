namespace Swip.Shared;

/// <summary>Parámetros en PÍXELES para <c>CreateRoundRectRgn</c> (región de la ventana con esquinas redondeadas).</summary>
/// <param name="Right">Borde derecho EXCLUSIVO: las regiones de GDI no incluyen el último píxel, por eso es ancho + 1.</param>
/// <param name="Bottom">Borde inferior EXCLUSIVO (alto + 1).</param>
public readonly record struct RoundedRegionSize(int Right, int Bottom, int EllipseWidth, int EllipseHeight);

/// <summary>
/// El desenfoque de Windows cubre TODA la ventana (un rectángulo), pero el panel de los menús solo
/// rellena su forma redondeada: sin borde de color se veía un recuadro cuadrado detrás. Se recorta
/// la ventana a la forma del panel; aquí la aritmética de dips a píxeles, aparte para poder probarla.
/// </summary>
public static class RoundedRegion
{
    /// <returns>Null si el panel aún no tiene tamaño.</returns>
    public static RoundedRegionSize? Compute(
        double widthDip, double heightDip, double cornerRadiusDip, double dpiScaleX, double dpiScaleY)
    {
        int width = Round(widthDip * dpiScaleX);
        int height = Round(heightDip * dpiScaleY);
        if (width <= 0 || height <= 0) return null;

        // CreateRoundRectRgn recibe el DIÁMETRO de la elipse de cada esquina, no el radio; y no
        // puede ser mayor que el lado, o la forma sale deformada.
        int ellipseWidth = Math.Min(Round(2 * cornerRadiusDip * dpiScaleX), width);
        int ellipseHeight = Math.Min(Round(2 * cornerRadiusDip * dpiScaleY), height);

        return new RoundedRegionSize(width + 1, height + 1, ellipseWidth, ellipseHeight);
    }

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
