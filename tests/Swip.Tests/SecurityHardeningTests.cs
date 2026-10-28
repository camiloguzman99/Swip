using Swip.Shared;

namespace Swip.Tests;

public class SecurityHardeningTests
{
    [Fact]
    public void La_lista_de_apps_publicada_se_acota()
    {
        var apps = Enumerable.Range(0, 500).Select(i => new AppInfo { ProcessName = "app" + i, CpuPercent = 5, RamPercent = 1 }).ToList();
        Assert.Equal(AppInfoSanitizer.MaxApps, AppInfoSanitizer.Clean(apps).Count);
    }

    [Fact]
    public void Los_textos_se_recortan_y_pierden_los_caracteres_de_control()
    {
        var clean = AppInfoSanitizer.Clean(new[]
        {
            new AppInfo { ProcessName = new string('a', 1000), WindowTitle = "linea1\r\nlinea2\0" + new string('b', 1000) },
        })[0];
        Assert.Equal(AppInfoSanitizer.MaxNameChars, clean.ProcessName.Length);
        Assert.DoesNotContain('\n', clean.WindowTitle);
        Assert.DoesNotContain('\0', clean.WindowTitle);
        Assert.Equal(AppInfoSanitizer.MaxTitleChars, clean.WindowTitle!.Length);
    }

    [Fact]
    public void Los_porcentajes_quedan_entre_0_y_100_y_los_NaN_son_cero()
    {
        var clean = AppInfoSanitizer.Clean(new[]
        {
            new AppInfo { ProcessName = "x", CpuPercent = double.NaN, RamPercent = 9999 },
            new AppInfo { ProcessName = "y", CpuPercent = -4, RamPercent = double.PositiveInfinity },
        });
        Assert.Equal(0, clean[0].CpuPercent);
        Assert.Equal(100, clean[0].RamPercent);
        Assert.Equal(0, clean[1].CpuPercent);
        Assert.Equal(0, clean[1].RamPercent);
    }

    [Fact]
    public void Se_descartan_las_entradas_nulas_o_sin_nombre()
    {
        var clean = AppInfoSanitizer.Clean(new AppInfo?[] { null, new AppInfo { ProcessName = " " }, new AppInfo { ProcessName = "ok" } });
        Assert.Single(clean);
        Assert.Equal("ok", clean[0].ProcessName);
    }

    [Fact]
    public void El_registro_no_escribe_si_el_archivo_es_un_enlace()
    {
        string dir = Path.Combine(Path.GetTempPath(), "swip-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string target = Path.Combine(dir, "victima.txt");
            File.WriteAllText(target, "original");
            string link = Path.Combine(dir, "service.log");
            try { File.CreateSymbolicLink(link, target); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { return; }   // sin permiso para crear enlaces: nada que probar
            new SwipLog(link).Write("no debe llegar a la victima");
            Assert.Equal("original", File.ReadAllText(target));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}

public class SettingsSizeTests
{
    [Fact]
    public void Un_settings_json_enorme_se_ignora()
    {
        string dir = Path.Combine(Path.GetTempPath(), "swip-set-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, "{\"RefreshSeconds\": 99, \"Padding\": \"" + new string('x', (int)Swip.Shared.Settings.SettingsFile.MaxBytes) + "\"}");
            Assert.Equal(10, new Swip.Shared.Settings.SettingsFile(path).Load().RefreshSeconds);   // valor por defecto
            File.WriteAllText(path, "{\"RefreshSeconds\": 99}");
            Assert.Equal(99, new Swip.Shared.Settings.SettingsFile(path).Load().RefreshSeconds);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
