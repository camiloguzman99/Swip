using System.Diagnostics;
using System.Text.Json;
using Swip.Service.Native;
using Swip.Shared;

namespace Swip.Service;

/// <summary>
/// Modo ayudante: se ejecuta DENTRO de la sesión objetivo (lanzado por el servicio con
/// CreateProcessAsUser). Enumera las ventanas de nivel superior visibles de esa sesión,
/// las convierte en apps y las escribe como JSON en el archivo indicado para que el servicio las lea.
/// </summary>
internal static class WindowEnumeratorFile
{
    /// <summary>
    /// Punto de entrada del modo ayudante. Escribe el JSON de apps en <paramref name="outFile"/>.
    /// Devuelve el código de salida del proceso (0 ok, 1 error).
    /// </summary>
    public static int Run(string outFile)
    {
        try
        {
            var apps = Enumerate();
            File.WriteAllText(outFile, JsonSerializer.Serialize(apps, IpcProtocol.Json));
            return 0;
        }
        catch
        {
            // Un fallo del ayudante no debe tumbar nada; el servicio lo interpreta como lista vacía.
            return 1;
        }
    }

    private static List<AppInfo> Enumerate()
    {
        uint ownSession = (uint)Process.GetCurrentProcess().SessionId;

        // Dedup por nombre de proceso: una app con varias ventanas aparece una sola vez.
        var byProcess = new Dictionary<string, AppInfo>(StringComparer.OrdinalIgnoreCase);

        WindowInterop.EnumWindows((hWnd, _) =>
        {
            if (!WindowInterop.IsWindowVisible(hWnd))
                return true;

            // Las tool windows (barras flotantes, etc.) no cuentan como apps de primer plano.
            long exStyle = WindowInterop.GetWindowLong(hWnd, WindowInterop.GWL_EXSTYLE);
            if ((exStyle & WindowInterop.WS_EX_TOOLWINDOW) != 0)
                return true;

            string title = WindowInterop.GetWindowTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title))
                return true;

            WindowInterop.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0)
                return true;

            // Seguridad extra: solo ventanas de nuestra propia sesión.
            if (WindowInterop.ProcessIdToSessionId(pid, out uint winSession) && winSession != ownSession)
                return true;

            string procName;
            try
            {
                using var p = Process.GetProcessById((int)pid);
                procName = p.ProcessName;
            }
            catch
            {
                return true;
            }

            // El shell del propio escritorio no es una "app abierta" útil para mostrar.
            if (IsShellProcess(procName))
                return true;

            if (!byProcess.ContainsKey(procName))
            {
                byProcess[procName] = new AppInfo { ProcessName = procName, WindowTitle = title };
            }

            return true;
        }, IntPtr.Zero);

        return byProcess.Values
            .OrderBy(a => a.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsShellProcess(string name) => name.ToLowerInvariant() switch
    {
        "explorer" => true,
        "applicationframehost" => false, // host de apps UWP: sí tiene apps reales detrás
        "textinputhost" => true,
        "shellexperiencehost" => true,
        "searchhost" => true,
        "startmenuexperiencehost" => true,
        _ => false,
    };
}
