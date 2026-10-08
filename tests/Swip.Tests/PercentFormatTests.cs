using System.Globalization;
using Swip.Shared;

namespace Swip.Tests;

public class PercentFormatTests
{
    private static readonly CultureInfo Es = new("es-CO");
    private static readonly CultureInfo En = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(0.0, "0%")]
    [InlineData(0.04, "0%")]       // menos de una décima: cero
    [InlineData(0.05, "0.1%")]
    [InlineData(0.4, "0.4%")]      // antes salía "0%" y parecía un dato falso
    [InlineData(3.2, "3.2%")]
    [InlineData(9.94, "9.9%")]
    [InlineData(9.96, "10%")]      // no "10.0%"
    [InlineData(10.4, "10%")]
    [InlineData(57.6, "58%")]
    [InlineData(100.0, "100%")]
    [InlineData(140.0, "100%")]    // nunca más de 100
    [InlineData(-3.0, "0%")]
    [InlineData(double.NaN, "0%")]
    public void Formatea_con_un_decimal_por_debajo_del_diez(double valor, string esperado)
    {
        Assert.Equal(esperado, PercentFormat.Format(valor, En));
    }

    [Fact]
    public void Usa_la_coma_decimal_en_espanol()
    {
        Assert.Equal("0,4%", PercentFormat.Format(0.4, Es));
        Assert.Equal("3,2%", PercentFormat.Format(3.2, Es));
    }
}
