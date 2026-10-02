using System.Runtime.InteropServices;

namespace Swip.App.Native;

/// <summary>
/// P/Invoke para hacer que la ventana transparente sea "click-through" salvo cuando el cursor
/// está sobre un gato. Así el espacio vacío no bloquea el escritorio ni la barra de tareas.
/// </summary>
internal static class InteropNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>Activa o desactiva el estilo click-through (WS_EX_TRANSPARENT) de la ventana.</summary>
    public static void SetClickThrough(IntPtr hWnd, bool enabled)
    {
        int ex = GetWindowLong(hWnd, GWL_EXSTYLE);
        int updated = enabled ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        if (updated != ex)
            SetWindowLong(hWnd, GWL_EXSTYLE, updated);
    }

    /// <summary>
    /// Marca la ventana como layered + tool window para que participe del click-through y no
    /// aparezca en Alt+Tab ni en la barra de tareas.
    /// </summary>
    public static void InitOverlayStyles(IntPtr hWnd)
    {
        int ex = GetWindowLong(hWnd, GWL_EXSTYLE);
        SetWindowLong(hWnd, GWL_EXSTYLE, ex | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
    }
}
