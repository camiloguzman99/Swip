using System.Runtime.InteropServices;

namespace Swip.App.Native;

/// <summary>
/// Medidas de la pantalla en PÍXELES REALES. La app declara reconocimiento de DPI por monitor
/// (app.manifest, PerMonitorV2): Windows ya no la estira como un mapa de bits al cambiar de pantalla
/// o de escala, y estas llamadas devuelven píxeles físicos del monitor en el que está.
/// </summary>
internal static class DisplayNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    /// <summary>Área de trabajo del monitor principal (pantalla menos la barra de tareas) y su escala.</summary>
    public readonly record struct WorkArea(RECT Pixels, double Scale);

    private const uint SPI_GETWORKAREA = 0x0030;
    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
    private const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out RECT pvParam, uint fWinIni);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(InteropNative.POINT pt, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    /// <summary>Área de trabajo del monitor principal en píxeles reales, con la escala de ese monitor.</summary>
    public static WorkArea? PrimaryWorkArea()
    {
        if (!SystemParametersInfo(SPI_GETWORKAREA, 0, out var rect, 0) || rect.Width <= 0 || rect.Height <= 0)
            return null;
        var center = new InteropNative.POINT { X = rect.Left + rect.Width / 2, Y = rect.Top + rect.Height / 2 };
        var monitor = MonitorFromPoint(center, MONITOR_DEFAULTTOPRIMARY);
        double scale = 1;
        if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpi, out _) == 0 && dpi > 0)
            scale = dpi / 96.0;
        return new WorkArea(rect, scale);
    }

    /// <summary>Escala actual de la ventana (1 = 100 %, 1.25 = 125 %…).</summary>
    public static double WindowScale(IntPtr hwnd)
    {
        uint dpi = hwnd == IntPtr.Zero ? 0 : GetDpiForWindow(hwnd);
        return dpi > 0 ? dpi / 96.0 : 1;
    }

    /// <summary>Coloca la ventana exactamente en un rectángulo de píxeles reales.</summary>
    public static void PlaceWindow(IntPtr hwnd, RECT r) =>
        SetWindowPos(hwnd, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, SWP_NOZORDER | SWP_NOACTIVATE);
}
