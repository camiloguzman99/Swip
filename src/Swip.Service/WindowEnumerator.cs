using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Swip.Service.Native;
using Swip.Shared;

namespace Swip.Service;

/// <summary>
/// Modo ayudante: se ejecuta DENTRO de la sesión objetivo. Enumera las apps con ventana visible,
/// con nombre amigable y CPU%/RAM% agregados por app, y escribe el resultado como JSON.
/// </summary>
internal static class WindowEnumeratorFile
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile,
                     ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX b);

    private static int _totalWindows;
    private static int _candidateWindows;

    public static int Run(string outFile)
    {
        try
        {
            var apps = Enumerate();
            File.WriteAllText(outFile, JsonSerializer.Serialize(apps, IpcProtocol.Json));
            HelperLog($"ayudante sesión={Process.GetCurrentProcess().SessionId} ventanasTotales={_totalWindows} candidatas={_candidateWindows} apps={apps.Count}");
            return 0;
        }
        catch (Exception ex)
        {
            HelperLog($"ayudante EXCEPCIÓN: {ex.Message}");
            return 1;
        }
    }

    private static void HelperLog(string msg)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Swip");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "service.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}   {msg}{Environment.NewLine}");
        }
        catch { }
    }

    private static List<AppInfo> Enumerate()
    {
        uint ownSession = (uint)Process.GetCurrentProcess().SessionId;

        _totalWindows = 0;
        _candidateWindows = 0;

        // Nombres de proceso con ventana visible (con un título por nombre).
        var windowed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        WindowInterop.EnumWindows((hWnd, _) =>
        {
            _totalWindows++;
            if (!WindowInterop.IsWindowVisible(hWnd)) return true;
            if ((WindowInterop.GetWindowLong(hWnd, WindowInterop.GWL_EXSTYLE) & WindowInterop.WS_EX_TOOLWINDOW) != 0) return true;
            string title = WindowInterop.GetWindowTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title)) return true;
            WindowInterop.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0) return true;
            if (WindowInterop.ProcessIdToSessionId(pid, out uint sid) && sid != ownSession) return true;
            _candidateWindows++;
            try
            {
                using var p = Process.GetProcessById((int)pid);
                string name = p.ProcessName;
                if (IsShell(name)) return true;
                if (!windowed.ContainsKey(name)) windowed[name] = title;
            }
            catch { }
            return true;
        }, IntPtr.Zero);

        if (windowed.Count == 0) return new();

        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        ulong totalPhys = GlobalMemoryStatusEx(ref mem) ? mem.ullTotalPhys : 0;
        int ncpu = Math.Max(1, Environment.ProcessorCount);

        // Todos los procesos de cada app en esta sesión.
        var procsByName = new Dictionary<string, List<Process>>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in windowed.Keys)
        {
            var list = new List<Process>();
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { if (p.SessionId == ownSession) list.Add(p); else p.Dispose(); }
                catch { try { p.Dispose(); } catch { } }
            }
            procsByName[name] = list;
        }

        try
        {
            var t0 = new Dictionary<int, TimeSpan>();
            foreach (var p in procsByName.Values.SelectMany(l => l))
                try { t0[p.Id] = p.TotalProcessorTime; } catch { }

            var sw = Stopwatch.StartNew();
            Thread.Sleep(450);
            sw.Stop();
            double wall = sw.Elapsed.TotalMilliseconds;

            var result = new List<AppInfo>();
            foreach (var (name, procs) in procsByName)
            {
                double cpu = 0, ram = 0;
                foreach (var p in procs)
                {
                    try
                    {
                        p.Refresh();
                        if (t0.TryGetValue(p.Id, out var start))
                            cpu += (p.TotalProcessorTime - start).TotalMilliseconds / (wall * ncpu) * 100.0;
                        ram += p.WorkingSet64;
                    }
                    catch { }
                }
                result.Add(new AppInfo
                {
                    ProcessName = FriendlyName(procs, name),
                    WindowTitle = windowed.TryGetValue(name, out var t) ? t : null,
                    CpuPercent = Math.Clamp(cpu, 0, 100),
                    RamPercent = totalPhys > 0 ? Math.Clamp(ram / totalPhys * 100.0, 0, 100) : 0,
                });
            }

            return result
                .OrderByDescending(a => a.RamPercent)
                .ThenBy(a => a.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            foreach (var p in procsByName.Values.SelectMany(l => l))
                try { p.Dispose(); } catch { }
        }
    }

    private static string FriendlyName(List<Process> procs, string fallback)
    {
        foreach (var p in procs)
        {
            try
            {
                string? d = p.MainModule?.FileVersionInfo.FileDescription;
                if (!string.IsNullOrWhiteSpace(d)) return d.Trim();
            }
            catch { }
        }
        return fallback.Length > 0 ? char.ToUpper(fallback[0]) + fallback[1..] : fallback;
    }

    private static bool IsShell(string name) => name.ToLowerInvariant() switch
    {
        "explorer" => true,
        "textinputhost" => true,
        "shellexperiencehost" => true,
        "searchhost" => true,
        "startmenuexperiencehost" => true,
        _ => false,
    };
}
