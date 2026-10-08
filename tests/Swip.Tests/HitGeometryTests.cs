using Swip.Shared;

namespace Swip.Tests;

public class HitGeometryTests
{
    // Imagen dibujada de 34x48 en (10, 5). La parte visible ocupa la MITAD IZQUIERDA y la inferior.
    private static readonly Area Image = new(10, 5, 34, 48);
    private static readonly Area LeftHalf = new(0.0, 0.5, 0.5, 0.5);

    [Fact]
    public void Sin_espejo_la_zona_es_la_mitad_izquierda_de_la_imagen()
    {
        var a = HitGeometry.HitArea(Image, LeftHalf, mirrored: false);

        Assert.Equal(10, a.X, 6);
        Assert.Equal(17, a.Width, 6);
        Assert.Equal(5 + 24, a.Y, 6);
        Assert.Equal(24, a.Height, 6);
    }

    // El fallo real: mirando a la izquierda la zona acababa fuera del gato.
    [Fact]
    public void Con_espejo_la_mitad_visible_pasa_a_la_derecha_y_sigue_DENTRO_de_la_imagen()
    {
        var a = HitGeometry.HitArea(Image, LeftHalf, mirrored: true);

        Assert.Equal(10 + 17, a.X, 6);          // mitad derecha de la imagen
        Assert.Equal(17, a.Width, 6);
        Assert.True(a.X >= Image.X && a.X + a.Width <= Image.X + Image.Width + 1e-9,
            "la zona de clic debe quedar dentro de la imagen dibujada");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Una_imagen_completamente_visible_da_siempre_la_imagen_entera(bool mirrored)
    {
        var a = HitGeometry.HitArea(Image, new Area(0, 0, 1, 1), mirrored);

        Assert.Equal(Image, a);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Una_caja_centrada_no_cambia_con_el_espejo(bool mirrored)
    {
        var a = HitGeometry.HitArea(Image, new Area(0.25, 0.1, 0.5, 0.8), mirrored);

        Assert.Equal(10 + 0.25 * 34, a.X, 6);
    }

    [Fact]
    public void Espejar_dos_veces_devuelve_la_zona_original()
    {
        var normal = HitGeometry.HitArea(Image, LeftHalf, mirrored: false);
        // Espejo sobre la caja ya espejada: se obtiene dibujando la imagen con la caja invertida.
        var mirroredBox = new Area(1 - LeftHalf.X - LeftHalf.Width, LeftHalf.Y, LeftHalf.Width, LeftHalf.Height);
        var back = HitGeometry.HitArea(Image, mirroredBox, mirrored: true);

        Assert.Equal(normal.X, back.X, 6);
        Assert.Equal(normal.Width, back.Width, 6);
    }
}
