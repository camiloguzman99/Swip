using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Swip.Shared;

namespace Swip.App.Native;

/// <summary>
/// Desenfoque de lo que hay DETRÁS de la ventana de un menú (<c>ACCENT_ENABLE_BLURBEHIND</c>).
///
/// Se usa el desenfoque clásico y no el acrílico (<c>ACCENT_ENABLE_ACRYLICBLURBEHIND</c>) porque el
/// acrílico se convierte en un color sólido cuando la ventana no tiene el foco, y los menús de
/// Swip (ventanas emergentes que no se activan) nunca lo tienen: así se veía negro. Esta hipótesis
/// no se ha podido comprobar fuera de un Windows real; por eso se puede apagar con
/// <c>"MenuBlur": false</c> en settings.json.
/// </summary>
internal static class BlurHelper
{
    private const int ACCENT_ENABLE_BLURBEHIND = 3;
    private const int WCA_ACCENT_POLICY = 19;

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

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    /// <summary>
    /// Recorta la ventana del menú a un rectángulo redondeado del tamaño del panel. El desenfoque de
    /// Windows cubre TODA la ventana (un rectángulo) y el panel solo rellena su forma redondeada:
    /// sin recortar, asomaba un recuadro cuadrado borroso detrás de las esquinas. Hay que repetirlo
    /// cada vez que cambia el tamaño del panel (p. ej. al cargar la lista de apps).
    /// </summary>
    public static void ClipToRoundedRect(Visual visual, double widthDip, double heightDip, double cornerRadiusDip)
    {
        try
        {
            if (PresentationSource.FromVisual(visual) is not HwndSource source) return; // el menú está cerrado

            var dpi = VisualTreeHelper.GetDpi(visual);
            var size = RoundedRegion.Compute(widthDip, heightDip, cornerRadiusDip, dpi.DpiScaleX, dpi.DpiScaleY);
            if (size is null) return;

            IntPtr region = CreateRoundRectRgn(0, 0, size.Value.Right, size.Value.Bottom,
                size.Value.EllipseWidth, size.Value.EllipseHeight);
            if (region == IntPtr.Zero) return;

            // Si Windows acepta la región pasa a ser suya; si la rechaza hay que liberarla.
            if (SetWindowRgn(source.Handle, region, true) == 0)
                DeleteObject(region);
        }
        catch { /* sin recorte: se ven las esquinas cuadradas del desenfoque, pero nada se rompe */ }
    }

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
}
