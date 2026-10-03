using System.Diagnostics;
using System.IO;

namespace Swip.App.Services;

/// <summary>
/// Registro de diagnóstico del gato, en %ProgramData%\Swip\app-s{sesión}.log (un archivo por
/// sesión para que los gatos de distintas sesiones no se pisen). Best-effort: nunca lanza.
/// </summary>
public static class AppLog
{
    private static readonly string Path = BuildPath();

    private static string BuildPath()
    {
        try
        {
            string dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Swip");
            Directory.CreateDirectory(dir);
            int session = Process.GetCurrentProcess().SessionId;
            return System.IO.Path.Combine(dir, $"app-s{session}.log");
        }
        catch { return string.Empty; }
    }

    public static void Write(string message)
    {
        if (string.IsNullOrEmpty(Path)) return;
        try
        {
            File.AppendAllText(Path,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch { /* el log es best-effort */ }
    }
}
