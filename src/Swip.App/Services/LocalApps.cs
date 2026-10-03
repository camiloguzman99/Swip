using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Swip.Shared;

namespace Swip.App.Services;

/// <summary>
/// Enumera, EN PROCESO, las apps con ventana visible de la SESIÓN ACTUAL (la del propio gato),
/// con CPU% y RAM%. Es más fiable que pedírselo al servicio para la sesión en pantalla.
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

    public static List<AppInfo> Enumerate()
    {
        uint ownSession = (uint)Process.GetCurrentProcess().SessionId;
        var byProcess = new Dictionary<string, (string Title, List<int> Pids)>(StringComparer.OrdinalIgnoreCase);

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

            string name;
            try { using var p = Process.GetProcessById((int)pid); name = p.ProcessName; }
            catch { return true; }
            if (IsShell(name)) return true;

            if (!byProcess.TryGetValue(name, out var e)) e = (title, new List<int>());
            if (!e.Pids.Contains((int)pid)) e.Pids.Add((int)pid);
            byProcess[name] = e;
            return true;
        }, IntPtr.Zero);

        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        ulong totalPhys = GlobalMemoryStatusEx(ref mem) ? mem.ullTotalPhys : 0;

        var t0 = new Dictionary<int, TimeSpan>();
        foreach (var pid in byProcess.Values.SelectMany(v => v.Pids).Distinct())
        {
            try { using var p = Process.GetProcessById(pid); t0[pid] = p.TotalProcessorTime; }
            catch { }
        }
        var sw = Stopwatch.StartNew();
        Thread.Sleep(400);
        sw.Stop();
        double wall = sw.Elapsed.TotalMilliseconds;
        int ncpu = Math.Max(1, Environment.ProcessorCount);

        var result = new List<AppInfo>();
        foreach (var (name, e) in byProcess)
        {
            double cpu = 0, ram = 0;
            foreach (int pid in e.Pids)
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    if (t0.TryGetValue(pid, out var start))
                        cpu += (p.TotalProcessorTime - start).TotalMilliseconds / (wall * ncpu) * 100.0;
                    ram += p.WorkingSet64;
                }
                catch { }
            }
            result.Add(new AppInfo
            {
                ProcessName = name,
                WindowTitle = e.Title,
                CpuPercent = Math.Clamp(cpu, 0, 100),
                RamPercent = totalPhys > 0 ? Math.Clamp(ram / totalPhys * 100.0, 0, 100) : 0,
            });
        }

        return result
            .OrderByDescending(a => a.CpuPercent)
            .ThenBy(a => a.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
