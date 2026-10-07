using System.Diagnostics;
using System.IO;
using Swip.Shared;

namespace Swip.App.Services;

/// <summary>
/// Registro de diagnóstico del gato, en %ProgramData%\Swip\app-s{sesión}.log (un archivo por
/// sesión para que los gatos de distintas sesiones no se pisen). Rotativo y "best effort".
/// </summary>
public static class AppLog
{
    private static readonly SwipLog Log = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Swip",
        $"app-s{Process.GetCurrentProcess().SessionId}.log"));

    public static void Write(string message) => Log.Write(message);

    /// <summary>Escribe solo si el mensaje cambió desde la última vez con la misma clave.</summary>
    public static void WriteOnChange(string key, string message) => Log.WriteOnChange(key, message);
}
