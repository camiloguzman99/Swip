using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Swip.App;

public partial class App : Application
{
    // Se guarda en un campo estático para que el mutex viva tanto como el proceso.
    private static Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Una sola instancia POR SESIÓN: el espacio "Local\" es privado de cada sesión, así que
        // cada usuario tiene su gato pero nunca dos. Evita duplicados cuando coinciden el acceso
        // directo de inicio y el vigilante del servicio. Un duplicado sale sin crear nada.
        _singleInstance = new Mutex(true, @"Local\Swip.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            Environment.Exit(0);
            return;
        }

        base.OnStartup(e);
        ApplyWindowsAccent();
    }

    /// <summary>Lee el color de énfasis de Windows y lo aplica a los recursos de acento.</summary>
    private void ApplyWindowsAccent()
    {
        Color accent = ReadAccentColor() ?? Color.FromRgb(0xFF, 0xC4, 0x00);

        Color hover = Lighten(accent, 0.15);
        Color fg = Luminance(accent) > 0.6 ? Color.FromRgb(0x20, 0x21, 0x24) : Colors.White;

        Resources["AccentBrush"] = Frozen(accent);
        Resources["AccentHoverBrush"] = Frozen(hover);
        Resources["AccentFgBrush"] = Frozen(fg);
    }

    private static Color? ReadAccentColor()
    {
        try
        {
            // DWM\AccentColor es un DWORD en formato ABGR (R en el byte bajo).
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int v)
            {
                byte r = (byte)(v & 0xFF);
                byte g = (byte)((v >> 8) & 0xFF);
                byte b = (byte)((v >> 16) & 0xFF);
                return Color.FromRgb(r, g, b);
            }
        }
        catch { /* sin accent: usamos el por defecto */ }
        return null;
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static double Luminance(Color c) =>
        (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    private static Color Lighten(Color c, double amount)
    {
        byte L(byte v) => (byte)Math.Clamp(v + (255 - v) * amount, 0, 255);
        return Color.FromRgb(L(c.R), L(c.G), L(c.B));
    }
}
