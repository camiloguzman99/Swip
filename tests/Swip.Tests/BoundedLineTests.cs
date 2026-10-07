using Swip.Shared;

namespace Swip.Tests;

public class BoundedLineTests
{
    private static Task<string?> Read(string input, int max = 100, CancellationToken ct = default) =>
        BoundedLine.ReadAsync(new StringReader(input), max, ct);

    [Fact]
    public async Task Lee_una_linea_terminada_en_salto()
    {
        Assert.Equal("{\"kind\":1}", await Read("{\"kind\":1}\nresto"));
    }

    [Fact]
    public async Task Quita_el_retorno_de_carro()
    {
        Assert.Equal("hola", await Read("hola\r\n"));
    }

    [Fact]
    public async Task Fin_del_flujo_sin_salto_devuelve_lo_leido()
    {
        Assert.Equal("sin salto", await Read("sin salto"));
    }

    [Fact]
    public async Task Flujo_vacio_devuelve_null()
    {
        Assert.Null(await Read(""));
    }

    [Fact]
    public async Task Una_linea_mayor_que_el_tope_se_rechaza()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(new string('x', 101) + "\n", max: 100));
    }

    [Fact]
    public async Task Una_linea_justo_en_el_tope_se_acepta()
    {
        Assert.Equal(100, (await Read(new string('x', 100) + "\n", max: 100))!.Length);
    }

    // El caso del fallo original: un cliente que conecta y nunca envía nada.
    [Fact]
    public async Task Un_cliente_que_no_envia_nada_se_corta_con_el_timeout()
    {
        using var pipe = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.In);
        using var reader = new StreamReader(pipe);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => BoundedLine.ReadAsync(reader, 100, cts.Token));
    }
}
