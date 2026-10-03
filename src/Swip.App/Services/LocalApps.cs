using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Swip.Shared;

namespace Swip.App.Services;

/// <summary>
/// Enumera, EN PROCESO, las apps con ventana visible de la SESIÓN ACTUAL, con nombre amigable
/// y CPU%/RAM% agregados sobre TODOS los procesos de cada app (como el Administrador de tareas).
/// </summary>
public static class LocalApps
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc f, IntPtr p);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ProcessIdToSessionId(uint pid, out uint sid);

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile,
                     ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX b);

    public static List<AppInfo> Enumerate() => AppUsage.Collect(WindowedNames);

    /// <summary>Nombres de proceso con ventana visible en la sesión actual, con un título por nombre.</summary>
    private static Dictionary<string, string> WindowedNames()
    {
        uint ownSession = (uint)Process.GetCurrentProcess().SessionId;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            if ((GetWindowLong(hWnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return true;
            int len = GetWindowTextLength(hWnd);
            if (len <= 0) return true;
            var sb = new StringBuilder(len + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            string title = sb.ToString();
            if (string.IsNullOrWhiteSpace(title)) return true;
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0) return true;
            if (ProcessIdToSessionId(pid, out uint sid) && sid != ownSession) return true;

            try
            {
                using var p = Process.GetProcessById((int)pid);
                string name = p.ProcessName;
                if (AppUsage.IsShell(name)) return true;
                if (!result.ContainsKey(name)) result[name] = title;
            }
            catch { }
            return true;
        }, IntPtr.Zero);

        return result;
    }

    internal static ulong TotalPhysicalMemory()
    {
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref mem) ? mem.ullTotalPhys : 0;
    }
}

/// <summary>Agrega CPU%/RAM% por app y resuelve el nombre amigable. Compartido por la lógica local.</summary>
internal static class AppUsage
{
    public static bool IsShell(string name) => name.ToLowerInvariant() switch
    {
        "explorer" => true,
        "textinputhost" => true,
        "shellexperiencehost" => true,
        "searchhost" => true,
        "startmenuexperiencehost" => true,
        _ => false,
    };

    public static List<AppInfo> Collect(Func<Dictionary<string, string>> windowedNames)
    {
        var windowed = windowedNames();
        if (windowed.Count == 0) return new();

        uint ownSession = (uint)Process.GetCurrentProcess().SessionId;
        ulong totalPhys = LocalApps.TotalPhysicalMemory();
        int ncpu = Math.Max(1, Environment.ProcessorCount);

        // Todos los procesos de cada app (por nombre) en la sesión actual.
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
            // Muestreo de CPU.
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
                string friendly = name;
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
                friendly = FriendlyName(procs, name);

                result.Add(new AppInfo
                {
                    ProcessName = friendly,
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
        // Sin descripción: nombre capitalizado.
        return fallback.Length > 0 ? char.ToUpper(fallback[0]) + fallback[1..] : fallback;
    }
}
