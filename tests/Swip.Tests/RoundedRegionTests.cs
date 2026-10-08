using Swip.Shared;

namespace Swip.Tests;

public class RoundedRegionTests
{
    // Diámetro de la elipse = 2 × radio (en píxeles) + 2 × CornerExtraPx.
    private static int Ellipse(double radiusDip, double dpi) =>
        (int)Math.Round(2 * radiusDip * dpi, MidpointRounding.AwayFromZero) + 2 * RoundedRegion.CornerExtraPx;

    [Fact]
    public void El_borde_derecho_e_inferior_son_exclusivos_asi_que_llevan_un_pixel_mas()
    {
        var r = RoundedRegion.FromPixels(260, 340, 12, 1.0, 1.0)!.Value;

        Assert.Equal(261, r.Right);     // GDI no incluye el último píxel
        Assert.Equal(341, r.Bottom);
    }

    [Fact]
    public void La_esquina_de_la_region_es_algo_mas_cerrada_que_la_del_panel()
    {
        var r = RoundedRegion.FromPixels(260, 340, 12, 1.0, 1.0)!.Value;

        Assert.Equal(24 + 2 * RoundedRegion.CornerExtraPx, r.EllipseWidth);  // el panel usaría 24
        Assert.True(r.EllipseWidth > 24, "la región debe quedar dentro de la forma del panel");
        Assert.Equal(r.EllipseWidth, r.EllipseHeight);
    }

    [Fact]
    public void Con_escala_de_pantalla_150_el_radio_se_pasa_a_pixeles()
    {
        var r = RoundedRegion.FromPixels(390, 510, 12, 1.5, 1.5)!.Value;

        Assert.Equal(Ellipse(12, 1.5), r.EllipseWidth);   // 36 + extra
        Assert.Equal(391, r.Right);
        Assert.Equal(511, r.Bottom);
    }

    [Fact]
    public void Las_escalas_horizontal_y_vertical_pueden_diferir()
    {
        var r = RoundedRegion.FromPixels(200, 125, 10, 2.0, 1.25)!.Value;

        Assert.Equal(Ellipse(10, 2.0), r.EllipseWidth);
        Assert.Equal(Ellipse(10, 1.25), r.EllipseHeight);
    }

    // El tamaño es el REAL de la ventana: Windows la redimensiona después de que el panel cambie,
    // y calcularlo desde el panel dejaba la región desfasada.
    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(0, 0)]
    [InlineData(-5, 100)]
    public void Una_ventana_sin_tamano_no_tiene_region(int ancho, int alto)
    {
        Assert.Null(RoundedRegion.FromPixels(ancho, alto, 12, 1.0, 1.0));
    }

    [Fact]
    public void La_esquina_nunca_es_mayor_que_el_lado()
    {
        var r = RoundedRegion.FromPixels(10, 6, 12, 1.0, 1.0)!.Value;

        Assert.Equal(10, r.EllipseWidth);
        Assert.Equal(6, r.EllipseHeight);
    }
}
