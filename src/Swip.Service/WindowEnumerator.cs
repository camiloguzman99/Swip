using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Swip.Service.Native;
using Swip.Shared;

namespace Swip.Service;

/// <summary>
/// Modo ayudante: se ejecuta DENTRO de la sesión objetivo (lanzado por el servicio con
/// CreateProcessAsUser). Enumera las ventanas de nivel superior visibles, calcula CPU% y RAM%
/// por app, y escribe el resultado como JSON en el archivo indicado.
/// </summary>
internal static class WindowEnumeratorFile
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

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
            return 1;
        }
    }

    private static List<AppInfo> Enumerate()
    {
        uint ownSession = (uint)Process.GetCurrentProcess().SessionId;

        // Procesos (pid) con ventana de nivel superior visible, agrupados por nombre.
        var byProcess = new Dictionary<string, (string Title, List<int> Pids)>(StringComparer.OrdinalIgnoreCase);

        WindowInterop.EnumWindows((hWnd, _) =>
        {
            if (!WindowInterop.IsWindowVisible(hWnd)) return true;
            long exStyle = WindowInterop.GetWindowLong(hWnd, WindowInterop.GWL_EXSTYLE);
            if ((exStyle & WindowInterop.WS_EX_TOOLWINDOW) != 0) return true;
            string title = WindowInterop.GetWindowTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title)) return true;
            WindowInterop.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0) return true;
            if (WindowInterop.ProcessIdToSessionId(pid, out uint winSession) && winSession != ownSession) return true;

            string procName;
            try { using var p = Process.GetProcessById((int)pid); procName = p.ProcessName; }
            catch { return true; }
            if (IsShellProcess(procName)) return true;

            if (!byProcess.TryGetValue(procName, out var entry))
                entry = (title, new List<int>());
            if (!entry.Pids.Contains((int)pid)) entry.Pids.Add((int)pid);
            byProcess[procName] = entry;
            return true;
        }, IntPtr.Zero);

        // Memoria física total.
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        ulong totalPhys = GlobalMemoryStatusEx(ref mem) && mem.ullTotalPhys > 0 ? mem.ullTotalPhys : 0;

        // Muestreo de CPU: tiempos de procesador al inicio.
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
        foreach (var (name, entry) in byProcess)
        {
            double cpu = 0, ramBytes = 0;
            foreach (int pid in entry.Pids)
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    if (t0.TryGetValue(pid, out var start))
                    {
                        double dms = (p.TotalProcessorTime - start).TotalMilliseconds;
                        if (wall > 0) cpu += dms / (wall * ncpu) * 100.0;
                    }
                    ramBytes += p.WorkingSet64;
                }
                catch { }
            }
            result.Add(new AppInfo
            {
                ProcessName = name,
                WindowTitle = entry.Title,
                CpuPercent = Math.Clamp(cpu, 0, 100),
                RamPercent = totalPhys > 0 ? Math.Clamp(ramBytes / totalPhys * 100.0, 0, 100) : 0,
            });
        }

        return result
            .OrderByDescending(a => a.CpuPercent)
            .ThenBy(a => a.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsShellProcess(string name) => name.ToLowerInvariant() switch
    {
        "explorer" => true,
        "textinputhost" => true,
        "shellexperiencehost" => true,
        "searchhost" => true,
        "startmenuexperiencehost" => true,
        _ => false,
    };
}
