using Swip.Shared;

namespace Swip.Tests;

public class LabelFormatTests
{
    [Fact]
    public void Una_palabra_va_en_una_sola_linea()
    {
        Assert.Equal("Camilo", LabelFormat.Split("Camilo"));
    }

    // El caso pedido: dos palabras, dos filas.
    [Fact]
    public void Dos_palabras_van_una_por_linea()
    {
        Assert.Equal("Concepto\nGeneral", LabelFormat.Split("Concepto General"));
    }

    [Theory]
    [InlineData("  Concepto   General  ", "Concepto\nGeneral")]   // espacios de más
    [InlineData("Concepto\tGeneral", "Concepto\nGeneral")]        // tabulador
    [InlineData("  Camilo  ", "Camilo")]
    public void Ignora_los_espacios_sobrantes(string entrada, string esperado)
    {
        Assert.Equal(esperado, LabelFormat.Split(entrada));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_nombre_vacio_da_texto_vacio(string entrada)
    {
        Assert.Equal(string.Empty, LabelFormat.Split(entrada));
    }

    [Theory]
    [InlineData("Juan Carlos Perez", "Juan Carlos\nPerez")]       // 11 / 5 es mejor que 4 / 12
    [InlineData("Ana de la Cruz", "Ana de\nla Cruz")]             // 6 / 7 
    [InlineData("Maria Jose Rodriguez Gomez", "Maria Jose\nRodriguez Gomez")]
    public void Tres_o_mas_palabras_se_reparten_en_dos_lineas_parejas(string entrada, string esperado)
    {
        Assert.Equal(esperado, LabelFormat.Split(entrada));
    }

    [Theory]
    [InlineData("Una dos tres cuatro cinco seis")]
    [InlineData("a b c")]
    public void Nunca_hay_mas_de_dos_lineas(string entrada)
    {
        Assert.True(LabelFormat.Split(entrada).Count(c => c == '\n') <= 1);
    }

    [Fact]
    public void No_pierde_ni_cambia_palabras()
    {
        string resultado = LabelFormat.Split("Juan Carlos Perez Lopez");

        Assert.Equal("Juan Carlos Perez Lopez", resultado.Replace('\n', ' '));
    }
}
