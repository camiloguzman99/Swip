using Swip.Shared;

namespace Swip.Tests;

public class RoundedRegionTests
{
    [Fact]
    public void Con_100_por_ciento_de_escala_las_medidas_son_las_del_panel_mas_uno()
    {
        var r = RoundedRegion.Compute(260, 340, 12, 1.0, 1.0)!.Value;

        Assert.Equal(261, r.Right);     // +1: GDI no incluye el último píxel
        Assert.Equal(341, r.Bottom);
        Assert.Equal(24, r.EllipseWidth);   // diámetro = 2 × radio
        Assert.Equal(24, r.EllipseHeight);
    }

    [Fact]
    public void Con_escala_de_pantalla_150_todo_se_pasa_a_pixeles()
    {
        var r = RoundedRegion.Compute(260, 340, 12, 1.5, 1.5)!.Value;

        Assert.Equal(390 + 1, r.Right);
        Assert.Equal(510 + 1, r.Bottom);
        Assert.Equal(36, r.EllipseWidth);   // 2 × 12 × 1,5
    }

    [Fact]
    public void Las_escalas_horizontal_y_vertical_pueden_diferir()
    {
        var r = RoundedRegion.Compute(100, 100, 10, 2.0, 1.25)!.Value;

        Assert.Equal(201, r.Right);
        Assert.Equal(126, r.Bottom);        // 125 + 1
        Assert.Equal(40, r.EllipseWidth);   // 20 × 2
        Assert.Equal(25, r.EllipseHeight);  // 20 × 1,25
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(0, 0)]
    [InlineData(-5, 100)]
    public void Un_panel_sin_tamano_no_tiene_region(double ancho, double alto)
    {
        Assert.Null(RoundedRegion.Compute(ancho, alto, 12, 1.0, 1.0));
    }

    [Fact]
    public void La_esquina_nunca_es_mayor_que_el_lado()
    {
        // Un panel de 10 × 6 px con radio 12: el diámetro 24 se limita al lado.
        var r = RoundedRegion.Compute(10, 6, 12, 1.0, 1.0)!.Value;

        Assert.Equal(10, r.EllipseWidth);
        Assert.Equal(6, r.EllipseHeight);
    }

    [Fact]
    public void Redondea_hacia_arriba_en_el_medio_pixel()
    {
        var r = RoundedRegion.Compute(100.5, 100.5, 12, 1.0, 1.0)!.Value;

        Assert.Equal(102, r.Right);         // 101 + 1
    }
}
