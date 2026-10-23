using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Swip.Shared;

namespace Swip.App.Native;

/// <summary>
/// Desenfoque de lo que hay DETRÁS de la ventana de un menú (<c>ACCENT_ENABLE_BLURBEHIND</c>) y
/// recorte de esa ventana a la forma redondeada del panel.
///
/// Se usa el desenfoque clásico y no el acrílico (<c>ACCENT_ENABLE_ACRYLICBLURBEHIND</c>) porque el
/// acrílico se convierte en un color sólido cuando la ventana no tiene el foco, y los menús de
/// Swip (ventanas emergentes que no se activan) nunca lo tienen: así se veía negro. Se puede
/// apagar con <c>"MenuBlur": false</c> en settings.json.
/// </summary>
internal static class BlurHelper
{
    private const int ACCENT_ENABLE_BLURBEHIND = 3;
    private const int WCA_ACCENT_POLICY = 19;
    private const int WM_SIZE = 0x0005;

    [StructLayout(LayoutKind.Sequential)]
    private struct ACCENT_POLICY
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor; // sin tinte: el tinte lo pone el fondo del panel
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWCOMPOSITIONATTRIBDATA
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

    /// <summary>
    /// Windows 11: esquinas redondeadas que SÍ recortan el desenfoque. Lo hace el propio DWM
    /// (DWMWA_WINDOW_CORNER_PREFERENCE), con el borde suavizado. Requisitos:
    /// - la ventana del menú no debe ser "layered" (Popup con AllowsTransparency=false): DWM no
    ///   redondea ventanas layered, por eso la región de antes no recortaba el desenfoque;
    /// - el fondo de la ventana debe ser transparente para DWM (fondo de composición transparente +
    ///   DwmExtendFrameIntoClientArea con márgenes -1), o se vería negro en vez del desenfoque.
    /// También se quita el borde fino que DWM dibuja en las ventanas redondeadas.
    /// </summary>
    /// <returns>Texto para el log con el resultado de cada paso (0 = correcto).</returns>
    public static string ApplyDwmRoundedCorners(Visual visual)
    {
        try
        {
            if (PresentationSource.FromVisual(visual) is not HwndSource source)
                return "DWM: sin ventana";
            if (source.CompositionTarget is not null)
                source.CompositionTarget.BackgroundColor = Colors.Transparent;

            var glass = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            int extend = DwmExtendFrameIntoClientArea(source.Handle, ref glass);

            int round = DWMWCP_ROUND;
            int corners = DwmSetWindowAttribute(source.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

            int none = DWMWA_COLOR_NONE;
            int border = DwmSetWindowAttribute(source.Handle, DWMWA_BORDER_COLOR, ref none, sizeof(int));

            return $"DWM: fondo transparente={Hr(extend)}, esquinas={Hr(corners)}, sin borde={Hr(border)}";
        }
        catch (Exception ex)
        {
            return $"DWM: error {ex.Message}";
        }
    }

    private static string Hr(int hresult) => hresult == 0 ? "ok" : $"0x{hresult:X8}";

    /// <returns>True si Windows aceptó la petición (no garantiza cómo se vea: eso se comprueba a ojo).</returns>
    public static bool Apply(Visual visual)
    {
        try
        {
            if (PresentationSource.FromVisual(visual) is not HwndSource source)
                return false;

            var accent = new ACCENT_POLICY { AccentState = ACCENT_ENABLE_BLURBEHIND };
            int size = Marshal.SizeOf(accent);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WINDOWCOMPOSITIONATTRIBDATA
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = ptr,
                    SizeOfData = size,
                };
                return SetWindowCompositionAttribute(source.Handle, ref data) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch
        {
            return false; // función no disponible: el menú queda con el fondo normal
        }
    }

    /// <summary>
    /// Recorta la ventana del menú a un rectángulo redondeado. El desenfoque de Windows cubre TODA la
    /// ventana (un rectángulo) y el panel solo rellena su forma redondeada: sin recortar, asomaban las
    /// esquinas cuadradas del desenfoque.
    ///
    /// El recorte se rehace cada vez que Windows cambia el tamaño REAL de la ventana (WM_SIZE), no
    /// cuando cambia el del panel: Windows redimensiona la ventana después, y calcularlo desde el
    /// panel dejaba la región desfasada. Windows crea una ventana nueva en cada apertura, así que el
    /// gancho no se acumula.
    /// </summary>
    /// <returns>Texto para el log con lo que se hizo (o por qué no se pudo).</returns>
    public static string RoundCorners(Visual visual, double cornerRadiusDip)
    {
        // Esquinas cuadradas: la ventana ya coincide con el panel, no hay nada que recortar.
        if (cornerRadiusDip <= 0) return "esquinas cuadradas (sin recorte)";

        try
        {
            if (PresentationSource.FromVisual(visual) is not HwndSource source)
                return "sin ventana";

            var dpi = VisualTreeHelper.GetDpi(visual);

            source.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg == WM_SIZE)
                {
                    // lParam = nuevo tamaño útil: ancho en la palabra baja, alto en la alta.
                    int width = (int)((long)lParam & 0xFFFF);
                    int height = (int)(((long)lParam >> 16) & 0xFFFF);
                    SetRoundRegion(hwnd, width, height, cornerRadiusDip, dpi);
                }
                return IntPtr.Zero;
            });

            if (!GetWindowRect(source.Handle, out var rect))
                return "no se pudo leer el tamaño de la ventana";

            int w = rect.Right - rect.Left, h = rect.Bottom - rect.Top;
            return SetRoundRegion(source.Handle, w, h, cornerRadiusDip, dpi)
                ? $"ventana {w}x{h} recortada (escala {dpi.DpiScaleX:0.##})"
                : $"recorte RECHAZADO (ventana {w}x{h})";
        }
        catch (Exception ex)
        {
            return $"error al recortar: {ex.Message}";
        }
    }

    private static bool SetRoundRegion(IntPtr hwnd, int widthPx, int heightPx, double radiusDip, DpiScale dpi)
    {
        var size = RoundedRegion.FromPixels(widthPx, heightPx, radiusDip, dpi.DpiScaleX, dpi.DpiScaleY);
        if (size is null) return false;

        IntPtr region = CreateRoundRectRgn(0, 0, size.Value.Right, size.Value.Bottom,
            size.Value.EllipseWidth, size.Value.EllipseHeight);
        if (region == IntPtr.Zero) return false;

        // Si Windows acepta la región pasa a ser suya; si la rechaza hay que liberarla.
        if (SetWindowRgn(hwnd, region, true) != 0) return true;
        DeleteObject(region);
        return false;
    }
}
