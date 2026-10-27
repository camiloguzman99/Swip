namespace Swip.Shared.Art;

/// <summary>
/// Cómo dibujar la cuadrícula de un gato (1 casilla = 1 px lógico) en una pantalla con escala
/// <c>scale</c> (1 = 100 %, 1.25 = 125 %…) sin deformarla:
/// <list type="bullet">
/// <item>Escala entera (100 %, 200 %, 300 %): cada casilla son exactamente <c>scale</c>×<c>scale</c>
/// píxeles reales; se amplía "vecino más cercano" y queda perfectamente nítida.</item>
/// <item>Escala intermedia (125 %, 150 %, 175 %): no hay un número entero de píxeles por casilla.
/// Se amplía primero por un factor ENTERO (<see cref="Prescale"/>) y se reduce suavemente hasta el
/// tamaño exacto ("sharp bilinear", lo que usan los emuladores para pixel art): casi nítida, sin
/// casillas de distinto grosor y con las mismas proporciones.</item>
/// </list>
/// El ancho y el alto se escalan siempre por el mismo factor: el dibujo nunca se ensancha ni se aplasta.
/// </summary>
public static class PixelScale
{
    private const double Tolerance = 0.01;

    /// <summary>True si cada casilla cabe en un número entero de píxeles reales.</summary>
    public static bool IsWhole(double scale) =>
        scale >= 1 - Tolerance && Math.Abs(scale - Math.Round(scale)) < Tolerance;

    /// <summary>
    /// Factor entero por el que se amplía la cuadrícula antes de dibujarla: 1 si la escala es entera
    /// (el dibujado ya amplía exacto), o el entero siguiente si no (después solo se reduce).
    /// </summary>
    public static int Prescale(double scale) =>
        IsWhole(scale) ? 1 : Math.Max(1, (int)Math.Ceiling(scale - Tolerance));

    /// <summary>Redondea una posición o desplazamiento (px lógicos) al píxel real más cercano.</summary>
    public static double Snap(double dip, double scale) =>
        scale <= 0 ? Math.Round(dip) : Math.Round(dip * scale) / scale;
}
