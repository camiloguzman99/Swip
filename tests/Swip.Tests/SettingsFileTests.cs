using Swip.Shared.Settings;

namespace Swip.Tests;

public class SettingsFileTests
{
    [Fact]
    public void Load_sin_archivo_devuelve_valores_por_defecto()
    {
        using var dir = new TempDir();
        var s = new SettingsFile(dir.File("settings.json")).Load();

        Assert.True(s.ShowLabels);
        Assert.Empty(s.Cats);
    }

    [Fact]
    public void Update_guarda_y_Load_lo_lee()
    {
        using var dir = new TempDir();
        var file = new SettingsFile(dir.File("settings.json"));

        file.Update(s => { s.BoxLeft = 120; s.ShowLabels = false; });

        var loaded = file.Load();
        Assert.Equal(120, loaded.BoxLeft);
        Assert.False(loaded.ShowLabels);
    }

    // El fallo original: cada gato guardaba SU copia entera y pisaba los cambios del otro.
    [Fact]
    public void Update_conserva_los_cambios_que_hizo_otra_sesion_entre_medias()
    {
        using var dir = new TempDir();
        var sesionA = new SettingsFile(dir.File("settings.json"));
        var sesionB = new SettingsFile(dir.File("settings.json"));

        sesionA.Update(s => s.BoxLeft = 300);                              // A mueve la caja
        var tras = sesionB.Update(s => s.ShowLabels = false);              // B (con copia vieja) cambia otra cosa

        Assert.Equal(300, tras.BoxLeft);                                   // lo de A sigue ahí
        Assert.False(tras.ShowLabels);
        Assert.Equal(300, sesionA.Load().BoxLeft);
    }

    [Fact]
    public void Update_de_un_gato_no_borra_la_posicion_ya_guardada()
    {
        using var dir = new TempDir();
        var file = new SettingsFile(dir.File("settings.json"));
        file.Update(s => s.Cats["camilo"] = new CatPref { Color = "orange", X = 410 });

        file.Update(s => s.Cats["camilo"].Color = "gray");                 // cambiar solo el color

        var pref = file.Load().Cats["camilo"];
        Assert.Equal("gray", pref.Color);
        Assert.Equal(410, pref.X);
    }

    [Fact]
    public void Updates_concurrentes_no_pierden_ninguna_escritura()
    {
        using var dir = new TempDir();
        string path = dir.File("settings.json");
        const int writers = 8, perWriter = 12;

        Parallel.For(0, writers, w =>
        {
            var file = new SettingsFile(path); // una instancia por "proceso"
            for (int i = 0; i < perWriter; i++)
                file.Update(s => s.Cats[$"gato-{w}-{i}"] = new CatPref { Fat = w });
        });

        Assert.Equal(writers * perWriter, new SettingsFile(path).Load().Cats.Count);
    }

    [Fact]
    public void Archivo_corrupto_se_trata_como_valores_por_defecto_y_se_recupera()
    {
        using var dir = new TempDir();
        string path = dir.File("settings.json");
        File.WriteAllText(path, "{ esto no es json");
        var file = new SettingsFile(path);

        Assert.True(file.Load().ShowLabels);

        file.Update(s => s.BoxLeft = 5);
        Assert.Equal(5, file.Load().BoxLeft);
    }

    [Fact]
    public void Escritura_atomica_no_deja_archivos_temporales()
    {
        using var dir = new TempDir();
        var file = new SettingsFile(dir.File("settings.json"));
        for (int i = 0; i < 5; i++) file.Update(s => s.BoxLeft = i);

        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp"));
    }

    [Fact]
    public void Sin_poder_bloquear_sigue_adelante_en_vez_de_perder_el_cambio()
    {
        using var dir = new TempDir();
        string path = dir.File("settings.json");
        var file = new SettingsFile(path, lockTimeout: TimeSpan.FromMilliseconds(100));

        // Otro proceso "se quedó" con el bloqueo.
        using var held = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        file.Update(s => s.BoxLeft = 77);

        Assert.Equal(77, file.Load().BoxLeft);
    }

    // El desenfoque se prueba en tu equipo: por defecto activado, y apagable desde settings.json.
    [Fact]
    public void MenuBlur_esta_activado_por_defecto_incluso_en_un_settings_antiguo_sin_esa_clave()
    {
        using var dir = new TempDir();
        string path = dir.File("settings.json");
        File.WriteAllText(path, "{ \"ShowLabels\": false, \"BoxLeft\": 40 }"); // sin MenuBlur

        var s = new SettingsFile(path).Load();

        Assert.True(s.MenuBlur);
        Assert.False(s.ShowLabels);
    }

    [Fact]
    public void MenuBlur_se_puede_apagar_y_se_conserva()
    {
        using var dir = new TempDir();
        var file = new SettingsFile(dir.File("settings.json"));

        file.Update(s => s.MenuBlur = false);

        Assert.False(file.Load().MenuBlur);
        file.Update(s => s.BoxLeft = 10);          // otro cambio no lo vuelve a activar
        Assert.False(file.Load().MenuBlur);
    }
}
