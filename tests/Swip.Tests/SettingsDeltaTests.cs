using Swip.Shared.Settings;

namespace Swip.Tests;

public class SettingsDeltaTests
{
    private static AppSettings Base() => new()
    {
        BoxLeft = 100,
        Cats = { ["camilo"] = new CatPref { Color = "orange", Fat = 1, X = 200 } },
    };

    [Fact]
    public void Sin_cambios_no_hay_nada_que_aplicar()
    {
        Assert.False(SettingsDelta.Between(Base(), Base()).Any);
    }

    [Fact]
    public void Una_diferencia_de_medio_pixel_se_ignora()
    {
        var after = Base();
        after.BoxLeft = 100.3;
        after.Cats["camilo"].X = 200.4;

        Assert.False(SettingsDelta.Between(Base(), after).Any);
    }

    [Fact]
    public void Detecta_caja_movida_y_etiquetas()
    {
        var after = Base();
        after.BoxLeft = 350;
        after.ShowLabels = false;

        var d = SettingsDelta.Between(Base(), after);

        Assert.True(d.Box);
        Assert.True(d.ShowLabels);
        Assert.Empty(d.Cats);
    }

    [Fact]
    public void Detecta_solo_el_gato_que_cambio()
    {
        var before = Base();
        before.Cats["ana"] = new CatPref { Color = "gray", X = 50 };
        var after = Base();
        after.Cats["ana"] = new CatPref { Color = "gray", X = 50 };
        after.Cats["camilo"].X = 640;                                     // lo movieron en la otra sesión

        var d = SettingsDelta.Between(before, after);

        Assert.Equal(new[] { "camilo" }, d.Cats);
    }

    [Fact]
    public void Un_gato_nuevo_cuenta_como_cambio()
    {
        var after = Base();
        after.Cats["nuevo"] = new CatPref { Color = "gray" };

        Assert.Contains("nuevo", SettingsDelta.Between(Base(), after).Cats);
    }

    [Fact]
    public void Pasar_de_sin_posicion_a_con_posicion_es_un_cambio()
    {
        var before = Base();
        before.Cats["camilo"].X = null;

        Assert.Contains("camilo", SettingsDelta.Between(before, Base()).Cats);
    }
}
