using Swip.Shared.Art;

namespace Swip.Tests;

public class PaletteTests
{
    [Theory]
    [InlineData(242, 145, 59, PixelRole.Fur)]      // naranja del pelaje
    [InlineData(214, 110, 35, PixelRole.Fur)]      // naranja oscuro (rayas)
    [InlineData(250, 232, 205, PixelRole.Belly)]   // crema de la barriga
    [InlineData(40, 25, 20, PixelRole.Other)]      // contorno oscuro
    [InlineData(240, 160, 175, PixelRole.Other)]   // nariz / orejas rosas
    [InlineData(40, 90, 200, PixelRole.Other)]     // ovillo azul
    [InlineData(255, 255, 255, PixelRole.Other)]   // brillo de los ojos
    public void Clasifica_los_colores_del_dibujo(byte r, byte g, byte b, PixelRole esperado)
    {
        Assert.Equal(esperado, Recolor.Classify(r, g, b));
    }

    [Fact]
    public void Las_paletas_originales_no_tocan_los_pixeles()
    {
        byte[] px = { 59, 145, 242, 255 };
        Recolor.Apply(px, CatPalettes.Get("orange"));
        Assert.Equal(new byte[] { 59, 145, 242, 255 }, px);
    }

    [Fact]
    public void El_negro_oscurece_el_pelaje_y_conserva_el_alfa_y_el_contorno()
    {
        // BGRA: pelaje naranja semitransparente + contorno oscuro.
        byte[] px = { 59, 145, 242, 128, 20, 25, 40, 255 };
        Recolor.Apply(px, CatPalettes.Get("black"));

        Assert.Equal(128, px[3]);                       // alfa intacto
        Assert.True(px[0] < 90 && px[1] < 90 && px[2] < 90, "el pelaje debe quedar oscuro");
        Assert.Equal(new byte[] { 20, 25, 40, 255 }, px[4..]); // contorno intacto
    }

    [Fact]
    public void Los_pixeles_transparentes_se_saltan()
    {
        byte[] px = { 59, 145, 242, 0 };
        Recolor.Apply(px, CatPalettes.Get("white"));
        Assert.Equal(new byte[] { 59, 145, 242, 0 }, px);
    }

    [Fact]
    public void La_rampa_interpola_entre_sus_extremos()
    {
        uint[] ramp = { 0x000000, 0xFFFFFF };
        Assert.Equal(0x000000u, Recolor.Sample(ramp, 0));
        Assert.Equal(0xFFFFFFu, Recolor.Sample(ramp, 1));
        Assert.Equal(0x808080u, Recolor.Sample(ramp, 0.5));
    }

    [Fact]
    public void Un_color_desconocido_o_antiguo_pasa_a_naranja()
    {
        Assert.Equal("orange", CatPalettes.Normalize("amarillo"));
        Assert.Equal("orange", CatPalettes.Normalize(null));
        Assert.Equal("gray", CatPalettes.Normalize("GRAY"));
    }

    [Fact]
    public void Cada_paleta_parte_de_una_piel_dibujada()
    {
        var skins = CatPalettes.Skins.ToHashSet();
        Assert.Equal(new HashSet<string> { "orange", "gray" }, skins);
        Assert.All(CatPalettes.All, p => Assert.Contains(p.Skin, skins));
    }
}
