namespace Swip.Shared;

/// <summary>Rectángulo simple (sin depender de WPF) para poder probar la geometría de los clics.</summary>
public readonly record struct Area(double X, double Y, double Width, double Height);

/// <summary>
/// Geometría de la zona clicable de un sprite: dónde cae, sobre la imagen dibujada, la caja de sus
/// píxeles visibles. Separada de WPF para poder probarla; el fallo que obligó a separarla fue que
/// el espejo (gato mirando a la izquierda) desplazaba la zona de clic fuera del gato.
/// </summary>
public static class HitGeometry
{
    /// <param name="image">Rectángulo que ocupa la imagen dibujada (espejada o no, es el mismo).</param>
    /// <param name="opaque">Caja de los píxeles visibles en coordenadas normalizadas (0..1) de la imagen SIN espejar.</param>
    /// <param name="mirrored">True si la imagen se dibuja espejada horizontalmente.</param>
    public static Area HitArea(Area image, Area opaque, bool mirrored)
    {
        // Espejar la imagen alrededor de su centro invierte la caja visible en horizontal.
        double x = mirrored ? 1 - opaque.X - opaque.Width : opaque.X;
        return new Area(
            image.X + x * image.Width,
            image.Y + opaque.Y * image.Height,
            opaque.Width * image.Width,
            opaque.Height * image.Height);
    }
}
