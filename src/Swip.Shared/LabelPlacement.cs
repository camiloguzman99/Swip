namespace Swip.Shared;

/// <summary>
/// Dónde va el rótulo del nombre respecto al gato. Se ancla por el CENTRO del rótulo, no por su
/// borde inferior: con el borde inferior fijo, un nombre de dos filas (más alto) quedaba con el
/// centro más arriba que uno de una fila, y la distancia al gato no era la misma entre gatos.
/// </summary>
public static class LabelPlacement
{
    /// <summary>
    /// Distancia fija (en píxeles independientes del dispositivo) entre el CENTRO del rótulo y lo más
    /// alto del gato. Debe ser mayor que la mitad del rótulo más alto (dos filas, ~27) para que nunca
    /// toque al gato: con 24, el borde inferior queda a ~10 px (dos filas) o ~16 px (una fila).
    /// </summary>
    public const double CenterDistance = 24;

    /// <param name="visibleTop">Y de la parte visible más alta del gato, en las coordenadas del control.</param>
    /// <param name="labelHeight">Alto del rótulo (cambia con una o dos filas).</param>
    /// <returns>Y del borde SUPERIOR del rótulo, para que su centro quede a <paramref name="centerDistance"/> sobre el gato.</returns>
    public static double Top(double visibleTop, double labelHeight, double centerDistance = CenterDistance) =>
        visibleTop - centerDistance - labelHeight / 2;
}
