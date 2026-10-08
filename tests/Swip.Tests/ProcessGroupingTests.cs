using Swip.Shared;

namespace Swip.Tests;

public class ProcessGroupingTests
{
    private static ProcInfo P(int pid, int parent, string name, long start = 0) => new(pid, parent, name, start);

    private static Dictionary<string, List<int>> Group(IEnumerable<ProcInfo> procs, params string[] apps) =>
        ProcessGrouping.Group(procs.ToList(), apps);

    [Fact]
    public void Los_procesos_con_el_nombre_de_la_app_le_pertenecen()
    {
        var g = Group(new[] { P(1, 0, "msedge"), P(2, 1, "msedge"), P(3, 1, "msedge") }, "msedge");

        Assert.Equal(new[] { 1, 2, 3 }, g["msedge"].OrderBy(x => x));
    }

    // El caso real: Outlook y WhatsApp delegan el trabajo en procesos "msedgewebview2".
    [Fact]
    public void Los_ayudantes_con_otro_nombre_cuentan_para_la_app_que_los_lanzo()
    {
        var g = Group(new[]
        {
            P(10, 1, "olk"),
            P(11, 10, "msedgewebview2"), P(12, 11, "msedgewebview2"), // nieto: también cuenta
            P(20, 1, "WhatsApp.Root"), P(21, 20, "msedgewebview2"),
        }, "olk", "WhatsApp.Root");

        Assert.Equal(new[] { 10, 11, 12 }, g["olk"].OrderBy(x => x));
        Assert.Equal(new[] { 20, 21 }, g["WhatsApp.Root"].OrderBy(x => x));
    }

    // El peligro: explorer.exe es padre de casi todo lo que abre el usuario.
    [Fact]
    public void Explorer_no_absorbe_a_las_apps_que_lanzo()
    {
        var g = Group(new[]
        {
            P(100, 1, "explorer"),
            P(200, 100, "msedge"),          // lanzado desde el menú Inicio: hijo de explorer
            P(201, 200, "msedgewebview2"),
            P(300, 100, "unaApp"),          // sin ventana propia, también hijo de explorer
        }, "explorer", "msedge");

        Assert.Equal(new[] { 100 }, g["explorer"]);              // solo su propio proceso
        Assert.Equal(new[] { 200, 201 }, g["msedge"].OrderBy(x => x));
        Assert.DoesNotContain(300, g["explorer"]);
    }

    [Fact]
    public void Una_app_listada_aparte_no_se_la_lleva_su_padre()
    {
        // Edge lanzado desde Outlook (clic en un enlace): es Edge, no Outlook.
        var g = Group(new[] { P(10, 1, "olk"), P(50, 10, "msedge"), P(51, 50, "msedgewebview2") }, "olk", "msedge");

        Assert.Equal(new[] { 10 }, g["olk"]);
        Assert.Equal(new[] { 50, 51 }, g["msedge"].OrderBy(x => x));
    }

    [Fact]
    public void Gana_el_ancestro_mas_cercano()
    {
        var g = Group(new[]
        {
            P(1, 0, "launcher"), P(2, 1, "appA"), P(3, 2, "helper"), P(4, 3, "helper2"),
        }, "launcher", "appA");

        Assert.Equal(new[] { 2, 3, 4 }, g["appA"].OrderBy(x => x));
        Assert.Equal(new[] { 1 }, g["launcher"]);
    }

    [Fact]
    public void Los_procesos_sin_relacion_con_ninguna_app_no_se_asignan()
    {
        var g = Group(new[] { P(1, 0, "msedge"), P(500, 4, "svchost"), P(501, 500, "RuntimeBroker") }, "msedge");

        Assert.Equal(new[] { 1 }, g["msedge"]);
    }

    [Fact]
    public void Un_pid_reutilizado_no_cuenta_como_padre()
    {
        // El hijo (pid 7) empezó en el instante 100, pero el "padre" con pid 3 empezó en el 500:
        // es otro proceso que heredó un PID viejo, no el padre real.
        var g = Group(new[] { P(3, 0, "msedge", start: 500), P(7, 3, "helper", start: 100) }, "msedge");

        Assert.Equal(new[] { 3 }, g["msedge"]);
    }

    [Fact]
    public void Un_padre_anterior_al_hijo_si_cuenta()
    {
        var g = Group(new[] { P(3, 0, "msedge", start: 100), P(7, 3, "helper", start: 500) }, "msedge");

        Assert.Equal(new[] { 3, 7 }, g["msedge"].OrderBy(x => x));
    }

    [Fact]
    public void Sin_tiempos_de_inicio_se_confia_en_el_padre()
    {
        var g = Group(new[] { P(3, 0, "msedge"), P(7, 3, "helper") }, "msedge");

        Assert.Contains(7, g["msedge"]);
    }

    [Fact]
    public void Un_ciclo_de_padres_no_cuelga_el_programa()
    {
        var g = Group(new[] { P(1, 2, "a"), P(2, 1, "b"), P(3, 3, "c"), P(9, 0, "msedge") }, "msedge");

        Assert.Equal(new[] { 9 }, g["msedge"]);
    }

    [Fact]
    public void Los_nombres_se_comparan_sin_distinguir_mayusculas()
    {
        var g = Group(new[] { P(1, 0, "MSEdge"), P(2, 1, "Helper") }, "msedge");

        Assert.Equal(new[] { 1, 2 }, g["msedge"].OrderBy(x => x));
    }

    [Fact]
    public void Una_app_sin_procesos_devuelve_lista_vacia()
    {
        var g = Group(new[] { P(1, 0, "otra") }, "msedge");

        Assert.Empty(g["msedge"]);
    }

    [Fact]
    public void Cada_proceso_se_asigna_a_una_sola_app()
    {
        var g = Group(new[] { P(1, 0, "a"), P(2, 1, "b"), P(3, 2, "h") }, "a", "b");

        Assert.Equal(g.Values.Sum(v => v.Count), g.Values.SelectMany(v => v).Distinct().Count());
    }
}
