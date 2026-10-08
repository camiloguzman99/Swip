using Swip.Shared;

namespace Swip.Tests;

public class WindowFilterTests
{
    // Una ventana normal y visible; cada prueba cambia solo lo que quiere comprobar.
    private static WindowFacts Normal(
        string process = "msedge", string cls = "Chrome_WidgetWin_1",
        bool visible = true, bool tool = false, bool title = true, bool cloaked = false,
        bool otherDesktop = false, bool minimized = false, int w = 1200, int h = 800,
        bool unresolvedHost = false) =>
        new(visible, tool, title, cloaked, otherDesktop, minimized, w, h, process, cls, unresolvedHost);

    [Fact]
    public void Una_ventana_normal_se_acepta()
    {
        Assert.Equal(WindowVerdict.Accept, WindowFilter.Classify(Normal()));
    }

    [Fact]
    public void Ventana_no_visible_se_descarta()
    {
        Assert.Equal(WindowVerdict.NotVisible, WindowFilter.Classify(Normal(visible: false)));
    }

    [Fact]
    public void Ventana_de_herramientas_se_descarta()
    {
        Assert.Equal(WindowVerdict.ToolWindow, WindowFilter.Classify(Normal(tool: true)));
    }

    [Fact]
    public void Ventana_sin_titulo_se_descarta()
    {
        Assert.Equal(WindowVerdict.NoTitle, WindowFilter.Classify(Normal(title: false)));
    }

    // El caso real de la captura: "Realtek Audio Console" y "Application Frame Host".
    [Fact]
    public void App_de_la_Tienda_suspendida_ventana_oculta_por_Windows_se_descarta()
    {
        var suspendida = Normal(process: "RealtekAudioConsole", cls: "ApplicationFrameWindow", cloaked: true);

        Assert.Equal(WindowVerdict.Cloaked, WindowFilter.Classify(suspendida));
    }

    [Fact]
    public void El_marco_ApplicationFrameHost_sin_app_real_no_se_lista()
    {
        var marco = Normal(process: "ApplicationFrameHost", cls: "ApplicationFrameWindow", unresolvedHost: true);

        Assert.Equal(WindowVerdict.UnresolvedHost, WindowFilter.Classify(marco));
    }

    [Fact]
    public void Una_app_de_la_Tienda_visible_se_lista_con_su_proceso_real()
    {
        // El gato resuelve el marco a la app real (p. ej. la Calculadora) antes de preguntar.
        var calculadora = Normal(process: "CalculatorApp", cls: "ApplicationFrameWindow");

        Assert.Equal(WindowVerdict.Accept, WindowFilter.Classify(calculadora));
    }

    [Fact]
    public void Ventana_en_otro_escritorio_virtual_sigue_siendo_una_app_abierta()
    {
        // Windows también la marca "cloaked", pero es una app del usuario.
        Assert.Equal(WindowVerdict.Accept, WindowFilter.Classify(Normal(cloaked: true, otherDesktop: true)));
    }

    [Theory]
    [InlineData(1, 500)]
    [InlineData(500, 1)]
    [InlineData(0, 0)]
    public void Ventanas_auxiliares_de_un_pixel_se_descartan(int w, int h)
    {
        Assert.Equal(WindowVerdict.TooSmall, WindowFilter.Classify(Normal(w: w, h: h)));
    }

    [Fact]
    public void Una_ventana_minimizada_se_acepta_aunque_su_tamano_no_sea_util()
    {
        Assert.Equal(WindowVerdict.Accept, WindowFilter.Classify(Normal(minimized: true, w: 0, h: 0)));
    }

    // El Explorador de archivos no aparecía porque se descartaba todo explorer.exe.
    [Theory]
    [InlineData("CabinetWClass")]
    [InlineData("ExploreWClass")]
    public void El_Explorador_de_archivos_se_acepta(string clase)
    {
        Assert.Equal(WindowVerdict.Accept, WindowFilter.Classify(Normal(process: "explorer", cls: clase)));
    }

    [Theory]
    [InlineData("Progman")]                 // el escritorio ("Program Manager")
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]           // la barra de tareas
    [InlineData("Shell_SecondaryTrayWnd")]  // la barra en el segundo monitor
    [InlineData("cabinetwclass")]           // la clase distingue mayúsculas: no es la del Explorador
    public void El_escritorio_y_la_barra_de_tareas_no_son_apps(string clase)
    {
        Assert.Equal(WindowVerdict.Shell, WindowFilter.Classify(Normal(process: "explorer", cls: clase)));
    }

    [Theory]
    [InlineData("TextInputHost")]
    [InlineData("ShellExperienceHost")]
    [InlineData("SearchHost")]
    [InlineData("StartMenuExperienceHost")]
    [InlineData("searchhost")]
    public void Los_procesos_del_shell_no_son_apps(string proceso)
    {
        Assert.Equal(WindowVerdict.Shell, WindowFilter.Classify(Normal(process: proceso)));
    }

    [Fact]
    public void Explorer_con_nombre_en_otras_mayusculas_se_trata_igual()
    {
        Assert.Equal(WindowVerdict.Accept, WindowFilter.Classify(Normal(process: "Explorer", cls: "CabinetWClass")));
        Assert.Equal(WindowVerdict.Shell, WindowFilter.Classify(Normal(process: "EXPLORER", cls: "Progman")));
    }

    [Fact]
    public void Cuando_hay_varios_motivos_manda_el_primero()
    {
        // Invisible Y oculta: se informa como no visible.
        Assert.Equal(WindowVerdict.NotVisible, WindowFilter.Classify(Normal(visible: false, cloaked: true)));
    }
}
