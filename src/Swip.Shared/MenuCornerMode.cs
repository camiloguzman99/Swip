namespace Swip.Shared;

/// <summary>Cómo se consiguen las esquinas redondeadas de los menús con desenfoque.</summary>
public enum MenuCornerMode
{
    /// <summary>
    /// Windows 11: el propio DWM redondea la ventana (DWMWA_WINDOW_CORNER_PREFERENCE) y recorta el
    /// desenfoque con esquinas suavizadas. Exige una ventana NO "layered" (AllowsTransparency=false),
    /// porque DWM no redondea las ventanas layered.
    /// </summary>
    Dwm,

    /// <summary>
    /// Respaldo (Windows 10, o desenfoque apagado): ventana layered (AllowsTransparency=true) con
    /// recorte por región. Con desenfoque, en las esquinas asoma un resto: el desenfoque no respeta
    /// la región.
    /// </summary>
    Layered,
}

public static class MenuCorners
{
    /// <summary>Primera compilación de Windows 11, la que trae el redondeo de ventanas de DWM.</summary>
    public const int FirstWindows11Build = 22000;

    /// <summary>Radio con el que DWM redondea (DWMWCP_ROUND); el panel usa el mismo para que encaje.</summary>
    public const double DwmRadius = 8;

    public static MenuCornerMode Choose(int windowsBuild, bool blurEnabled) =>
        blurEnabled && windowsBuild >= FirstWindows11Build ? MenuCornerMode.Dwm : MenuCornerMode.Layered;
}
