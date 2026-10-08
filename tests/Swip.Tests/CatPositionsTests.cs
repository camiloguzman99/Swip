using Swip.Shared.Settings;

namespace Swip.Tests;

public class CatPositionsTests
{
    private static readonly IReadOnlyDictionary<string, double?> NoneKnown = new Dictionary<string, double?>();

    [Fact]
    public void Un_gato_nunca_movido_recibe_su_posicion_y_su_aspecto()
    {
        var s = new AppSettings();

        CatPositions.Apply(s, new[] { new CatSnapshot("ana", "gray", 2, 321) }, NoneKnown);

        var p = s.Cats["ana"];
        Assert.Equal(321, p.X);
        Assert.Equal("gray", p.Color);
        Assert.Equal(2, p.Fat);
    }

    [Fact]
    public void Guardar_posiciones_no_cambia_color_ni_gordura_de_un_gato_ya_guardado()
    {
        var s = new AppSettings { Cats = { ["ana"] = new CatPref { Color = "orange", Fat = 3, X = 10 } } };
        var known = new Dictionary<string, double?> { ["ana"] = 10 };

        CatPositions.Apply(s, new[] { new CatSnapshot("ana", "gray", 0, 500) }, known);

        Assert.Equal(500, s.Cats["ana"].X);
        Assert.Equal("orange", s.Cats["ana"].Color); // el snapshot trae otro color: no debe pisarlo
        Assert.Equal(3, s.Cats["ana"].Fat);
    }

    [Fact]
    public void Si_otra_sesion_movio_al_gato_desde_que_lo_vimos_no_se_pisa()
    {
        // Yo conocía X=100, pero en el disco otra sesión ya lo dejó en 700.
        var s = new AppSettings { Cats = { ["ana"] = new CatPref { X = 700 } } };
        var known = new Dictionary<string, double?> { ["ana"] = 100 };

        CatPositions.Apply(s, new[] { new CatSnapshot("ana", "orange", 0, 100) }, known);

        Assert.Equal(700, s.Cats["ana"].X);
    }

    [Fact]
    public void Un_gato_sin_posicion_guardada_y_sin_posicion_conocida_se_escribe()
    {
        var s = new AppSettings { Cats = { ["ana"] = new CatPref { Color = "gray" } } }; // X nula

        CatPositions.Apply(s, new[] { new CatSnapshot("ana", "gray", 0, 250) }, NoneKnown);

        Assert.Equal(250, s.Cats["ana"].X);
    }

    [Fact]
    public void Aplica_a_varios_gatos_a_la_vez()
    {
        var s = new AppSettings();
        var cats = new[]
        {
            new CatSnapshot("ana", "orange", 0, 10),
            new CatSnapshot("luis", "gray", 0, 20),
        };

        CatPositions.Apply(s, cats, NoneKnown);

        Assert.Equal(10, s.Cats["ana"].X);
        Assert.Equal(20, s.Cats["luis"].X);
    }

    [Fact]
    public void Funciona_de_extremo_a_extremo_con_el_archivo_compartido()
    {
        using var dir = new TempDir();
        var file = new SettingsFile(dir.File("settings.json"));
        var salida = new[] { new CatSnapshot("camilo", "orange", 0, 480) };

        // La sesión que se va guarda dónde estaban los gatos...
        file.Update(s => CatPositions.Apply(s, salida, NoneKnown));

        // ...y la que llega los lee y ve el cambio.
        var antes = new AppSettings();
        var delta = SettingsDelta.Between(antes, file.Load());
        Assert.Contains("camilo", delta.Cats);
        Assert.Equal(480, file.Load().Cats["camilo"].X);
    }
}
