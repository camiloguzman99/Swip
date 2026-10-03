using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Swip.App.Native;

/// <summary>
/// Aplica el efecto acrílico (fondo translúcido con desenfoque) a la ventana de un popup,
/// usando SetWindowCompositionAttribute de Windows 10/11.
/// </summary>
internal static class AcrylicHelper
{
    private enum AccentState { ACCENT_ENABLE_ACRYLICBLURBEHIND = 4 }

    [StructLayout(LayoutKind.Sequential)]
    private struct ACCENT_POLICY
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor; // 0xAABBGGRR
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWCOMPOSITIONATTRIBDATA
    {
        public int Attribute;      // WCA_ACCENT_POLICY = 19
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

    /// <summary>Aplica acrílico negro al ~50% a la ventana que contiene al visual indicado.</summary>
    public static void Apply(Visual child)
    {
        try
        {
            if (PresentationSource.FromVisual(child) is not HwndSource source)
                return;

            var accent = new ACCENT_POLICY
            {
                AccentState = (int)AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 2,
                GradientColor = 0x2E000000, // negro muy tenue: casi solo desenfoque
                AnimationId = 0,
            };

            int size = Marshal.SizeOf(accent);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WINDOWCOMPOSITIONATTRIBDATA
                {
                    Attribute = 19, // WCA_ACCENT_POLICY
                    Data = ptr,
                    SizeOfData = size,
                };
                SetWindowCompositionAttribute(source.Handle, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch { /* si no está disponible, queda el fondo translúcido del borde */ }
    }
}
