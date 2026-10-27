using Swip.Shared.Art;

namespace Swip.Tests;

public class PixelScaleTests
{
    [Theory]
    [InlineData(1.0, true)]
    [InlineData(2.0, true)]
    [InlineData(3.0, true)]
    [InlineData(1.25, false)]
    [InlineData(1.5, false)]
    [InlineData(1.75, false)]
    [InlineData(0.5, false)]
    public void Distingue_las_escalas_enteras(double escala, bool entera)
    {
        Assert.Equal(entera, PixelScale.IsWhole(escala));
    }

    [Theory]
    [InlineData(1.0, 1)]   // 100 %: tal cual, cada casilla = 1 px
    [InlineData(2.0, 1)]   // 200 %: el dibujado ya amplía exacto x2
    [InlineData(1.25, 2)]  // 125 %: se amplía x2 y se reduce a x1.25
    [InlineData(1.5, 2)]
    [InlineData(1.75, 2)]
    [InlineData(2.25, 3)]
    public void La_ampliacion_previa_es_el_entero_siguiente_si_la_escala_no_es_entera(double escala, int factor)
    {
        Assert.Equal(factor, PixelScale.Prescale(escala));
    }

    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    public void La_ampliacion_previa_nunca_es_menor_que_la_escala(double escala)
    {
        // Si fuera menor, el sistema tendría que AMPLIAR suavizando y el pixel art se emborronaría.
        Assert.True(PixelScale.Prescale(escala) >= escala);
    }

    [Theory]
    [InlineData(10.3, 1.0, 10.0)]
    [InlineData(10.3, 1.25, 10.4)]   // 12.875 px reales -> 13 -> 10.4 px lógicos
    [InlineData(-3.3, 1.5, -10.0 / 3)]
    public void Ajusta_las_posiciones_al_pixel_real_mas_cercano(double dip, double escala, double esperado)
    {
        Assert.Equal(esperado, PixelScale.Snap(dip, escala), 6);
        Assert.Equal(Math.Round(PixelScale.Snap(dip, escala) * escala), PixelScale.Snap(dip, escala) * escala, 6);
    }
}
