namespace Swip.Shared;

/// <summary>Por qué se acepta o se descarta una ventana al listar las apps abiertas.</summary>
public enum WindowVerdict
{
    Accept,
    NotVisible,
    ToolWindow,
    NoTitle,

    /// <summary>Marco de una app de la Tienda (ApplicationFrameHost) del que no se pudo averiguar la app real.</summary>
    UnresolvedHost,

    /// <summary>Oculta por Windows ("cloaked"): app de la Tienda suspendida que conserva una ventana invisible.</summary>
    Cloaked,

    TooSmall,

    /// <summary>Parte del shell de Windows (escritorio, barra de tareas, menú Inicio, búsqueda…).</summary>
    Shell,
}

/// <summary>Lo que se sabe de una ventana de nivel superior para decidir si cuenta como "app abierta".</summary>
/// <param name="ProcessName">Proceso de la app. Para apps de la Tienda, la app real (no ApplicationFrameHost).</param>
/// <param name="IsUnresolvedHost">Es un marco de ApplicationFrameHost y no se encontró la app que aloja.</param>
public readonly record struct WindowFacts(
    bool IsVisible,
    bool IsToolWindow,
    bool HasTitle,
    bool IsCloaked,
    bool OnOtherVirtualDesktop,
    bool IsMinimized,
    int Width,
    int Height,
    string ProcessName,
    string ClassName,
    bool IsUnresolvedHost);

/// <summary>
/// Decide qué ventanas cuentan como "apps abiertas" (las que verías en la barra de tareas / Alt+Tab).
/// Separado de las llamadas a Win32 para poder probar cada decisión: el filtro simple de antes
/// (visible + título) listaba apps de la Tienda suspendidas y el marco ApplicationFrameHost, y
/// descartaba todo explorer.exe, con lo que el Explorador de archivos no aparecía.
/// </summary>
public static class WindowFilter
{
    private static readonly HashSet<string> ShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "textinputhost", "shellexperiencehost", "searchhost", "searchapp", "startmenuexperiencehost",
    };

    // Las ventanas del Explorador de archivos; el resto de las de explorer.exe son el escritorio
    // (Progman), la barra de tareas (Shell_TrayWnd, Shell_SecondaryTrayWnd), etc.
    private static readonly HashSet<string> ExplorerWindowClasses = new(StringComparer.Ordinal)
    {
        "CabinetWClass", "ExploreWClass",
    };

    public static WindowVerdict Classify(WindowFacts f)
    {
        if (!f.IsVisible) return WindowVerdict.NotVisible;
        if (f.IsToolWindow) return WindowVerdict.ToolWindow;
        if (!f.HasTitle) return WindowVerdict.NoTitle;
        if (f.IsUnresolvedHost) return WindowVerdict.UnresolvedHost;

        // Oculta por Windows = app de la Tienda suspendida. Las ventanas en OTRO escritorio virtual
        // también están "cloaked", pero esas sí son apps abiertas del usuario.
        if (f.IsCloaked && !f.OnOtherVirtualDesktop) return WindowVerdict.Cloaked;

        // Una ventana minimizada no tiene tamaño útil; las demás de 1 píxel son auxiliares.
        if (!f.IsMinimized && (f.Width <= 1 || f.Height <= 1)) return WindowVerdict.TooSmall;

        if (ShellProcesses.Contains(f.ProcessName)) return WindowVerdict.Shell;
        if (string.Equals(f.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase)
            && !ExplorerWindowClasses.Contains(f.ClassName))
            return WindowVerdict.Shell;

        return WindowVerdict.Accept;
    }
}
