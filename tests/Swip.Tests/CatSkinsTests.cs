using Swip.Shared.Art;

namespace Swip.Tests;

public class CatSkinsTests
{
    [Fact]
    public void El_primer_usuario_es_naranja_y_el_siguiente_atigrado()
    {
        Assert.Equal(CatSkins.Orange, CatSkins.Pick(null, Array.Empty<string>()));
        Assert.Equal(CatSkins.Gray, CatSkins.Pick(null, new[] { CatSkins.Orange }));
    }

    [Fact]
    public void Con_mas_usuarios_que_dibujos_se_reparten_en_orden()
    {
        Assert.Equal(CatSkins.Orange, CatSkins.Pick(null, new[] { CatSkins.Orange, CatSkins.Gray }));
        Assert.Equal(CatSkins.Gray, CatSkins.Pick(null, new[] { CatSkins.Orange, CatSkins.Gray, CatSkins.Orange }));
    }

    [Theory]
    [InlineData("orange")]
    [InlineData("gray")]
    [InlineData("GRAY")]
    public void Un_dibujo_guardado_valido_no_cambia_aunque_otro_gato_tenga_el_mismo(string saved)
    {
        Assert.Equal(saved.ToLowerInvariant(), CatSkins.Pick(saved, new[] { "orange", "gray" }));
    }

    [Theory]
    [InlineData("black")]
    [InlineData("white")]
    [InlineData("cream")]
    [InlineData("chocolate")]
    [InlineData("")]
    [InlineData(null)]
    public void Los_colores_antiguos_se_reasignan_al_primero_libre(string? old)
    {
        Assert.Equal(CatSkins.Orange, CatSkins.Pick(old, Array.Empty<string>()));
        Assert.Equal(CatSkins.Gray, CatSkins.Pick(old, new[] { CatSkins.Orange }));
        Assert.Equal(CatSkins.Orange, CatSkins.Pick(old, new[] { CatSkins.Gray }));
    }

    [Fact]
    public void Un_id_antiguo_sin_mas_pasa_a_naranja()
    {
        Assert.Equal(CatSkins.Orange, CatSkins.Normalize("black"));
        Assert.Equal(CatSkins.Gray, CatSkins.Normalize("Gray"));
    }
}
