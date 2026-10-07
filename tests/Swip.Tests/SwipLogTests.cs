using Swip.Shared;

namespace Swip.Tests;

public class SwipLogTests
{
    [Fact]
    public void Write_agrega_lineas_con_fecha()
    {
        using var dir = new TempDir();
        var log = new SwipLog(dir.File("x.log"));

        log.Write("hola");
        log.Write("adios");

        var lines = File.ReadAllLines(dir.File("x.log"));
        Assert.Equal(2, lines.Length);
        Assert.EndsWith(" hola", lines[0]);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} ", lines[0]);
    }

    [Fact]
    public void Rota_al_llegar_al_limite_y_el_tamano_queda_acotado()
    {
        using var dir = new TempDir();
        string path = dir.File("x.log");
        var log = new SwipLog(path, maxBytes: 2_000);

        for (int i = 0; i < 500; i++) log.Write($"linea numero {i:D4} con algo de relleno para ocupar espacio");

        Assert.True(File.Exists(path + ".1"));
        // Como mucho ~2 archivos de límite + una línea de margen cada uno.
        long total = new FileInfo(path).Length + new FileInfo(path + ".1").Length;
        Assert.True(total < 2_000 * 2 + 400, $"el log ocupa {total} bytes");
        // Lo más reciente se conserva.
        Assert.Contains("linea numero 0499", File.ReadAllText(path));
    }

    [Fact]
    public void WriteOnChange_omite_mensajes_identicos_consecutivos()
    {
        using var dir = new TempDir();
        var log = new SwipLog(dir.File("x.log"));

        Assert.True(log.WriteOnChange("pub", "6 apps"));
        Assert.False(log.WriteOnChange("pub", "6 apps"));
        Assert.False(log.WriteOnChange("pub", "6 apps"));
        Assert.True(log.WriteOnChange("pub", "7 apps"));
        Assert.True(log.WriteOnChange("pub", "6 apps"));     // vuelve a cambiar: se anota

        Assert.Equal(3, File.ReadAllLines(dir.File("x.log")).Length);
    }

    [Fact]
    public void WriteOnChange_distingue_por_clave()
    {
        using var dir = new TempDir();
        var log = new SwipLog(dir.File("x.log"));

        Assert.True(log.WriteOnChange("sesion-1", "6 apps"));
        Assert.True(log.WriteOnChange("sesion-4", "6 apps"));
    }

    [Fact]
    public void Escritura_concurrente_no_lanza_ni_pierde_lineas()
    {
        using var dir = new TempDir();
        var log = new SwipLog(dir.File("x.log"), maxBytes: 10_000_000);

        Parallel.For(0, 8, t => { for (int i = 0; i < 50; i++) log.Write($"t{t} i{i}"); });

        Assert.Equal(400, File.ReadAllLines(dir.File("x.log")).Length);
    }

    [Fact]
    public void Un_destino_imposible_no_lanza_excepcion()
    {
        var log = new SwipLog(Path.Combine(Path.GetTempPath(), "no-existe-\0-invalido", "x.log"));
        log.Write("no debe romper la app");
    }
}
