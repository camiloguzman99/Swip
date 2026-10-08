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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc f, IntPtr p);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const int DWMWA_CLOAKED = 14;

    // IVirtualDesktopManager: documentado por Microsoft. Solo se usa para distinguir una ventana
    // "oculta" de una app de la Tienda suspendida (se descarta) de una que está en otro escritorio
    // virtual (es una app abierta del usuario). Solo se declara el primer método de la interfaz.
    [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, [MarshalAs(UnmanagedType.Bool)] out bool onCurrentDesktop);
    }

    [ComImport, Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")]
    private class VirtualDesktopManagerClass { }

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

    [DllImport("kernel32.dll")] private static extern uint WTSGetActiveConsoleSessionId();

    /// <summary>True si esta sesión es la que está ahora mismo en pantalla (consola activa).</summary>
    public static bool IsActiveConsoleSession()
    {
        try { return (uint)Process.GetCurrentProcess().SessionId == WTSGetActiveConsoleSessionId(); }
        catch { return false; }
    }

    /// <param name="logBreakdown">Anota en el log el desglose de cada app (para comparar con el Administrador de tareas).</param>
    public static List<AppInfo> Enumerate(bool logBreakdown = false) =>
        AppUsage.Collect(WindowedNames, logBreakdown);

    /// <summary>
    /// Nombres de proceso con ventana "de app" en la sesión actual (las que verías en la barra de
    /// tareas), con un título por nombre. Qué cuenta como app lo decide <see cref="WindowFilter"/>;
    /// aquí solo se reúnen los datos de cada ventana.
    /// </summary>
    private static Dictionary<string, string> WindowedNames()
    {
        uint ownSession = (uint)Process.GetCurrentProcess().SessionId;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Para diagnóstico: qué se aceptó y qué se descartó (solo proceso y clase, nunca títulos).
        var accepted = new List<string>();
        var rejected = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var skipped = new SortedDictionary<WindowVerdict, int>();

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true; // la inmensa mayoría: descarte barato y sin registro
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0) return true;
            if (ProcessIdToSessionId(pid, out uint sid) && sid != ownSession) return true;

            string title = TitleOf(hWnd);
            string name;
            try { using var p = Process.GetProcessById((int)pid); name = p.ProcessName; }
            catch { return true; }

            string cls = ClassOf(hWnd);

            // Las apps de la Tienda se dibujan dentro de un marco de ApplicationFrameHost: hay que
            // averiguar la app real, o todas saldrían como "Application Frame Host".
            bool unresolvedHost = false;
            if (name.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
            {
                string? real = ResolveStoreApp(hWnd, pid);
                if (real is null) unresolvedHost = true;
                else name = real;
            }

            bool cloaked = DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int c, sizeof(int)) == 0 && c != 0;
            GetWindowRect(hWnd, out var r);

            var verdict = WindowFilter.Classify(new WindowFacts(
                IsVisible: true,
                IsToolWindow: (GetWindowLong(hWnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0,
                HasTitle: !string.IsNullOrWhiteSpace(title),
                IsCloaked: cloaked,
                OnOtherVirtualDesktop: cloaked && IsOnOtherVirtualDesktop(hWnd),
                IsMinimized: IsIconic(hWnd),
                Width: r.Right - r.Left,
                Height: r.Bottom - r.Top,
                ProcessName: name,
                ClassName: cls,
                IsUnresolvedHost: unresolvedHost));

            if (verdict == WindowVerdict.Accept)
            {
                if (!result.ContainsKey(name))
                {
                    result[name] = title;
                    accepted.Add($"{name}[{cls}]");
                }
            }
            else
            {
                skipped[verdict] = skipped.GetValueOrDefault(verdict) + 1;
                // Solo los descartes "interesantes" para diagnosticar, con un tope para no llenar el log.
                bool interesting = verdict is WindowVerdict.Cloaked or WindowVerdict.Shell
                    or WindowVerdict.UnresolvedHost or WindowVerdict.TooSmall;
                if (interesting && rejected.Count < 14)
                    rejected.Add($"{name}[{cls}]:{verdict}");
            }
            return true;
        }, IntPtr.Zero);

        AppLog.WriteOnChange("windows",
            $"Ventanas aceptadas: {string.Join(", ", accepted)} | descartadas: " +
            $"{string.Join(", ", skipped.Select(kv => $"{kv.Key}={kv.Value}"))} " +
            $"({string.Join(", ", rejected)})");

        return result;
    }

    private static string TitleOf(IntPtr hWnd)
    {
        int len = GetWindowTextLength(hWnd);
        if (len <= 0) return string.Empty;
        var sb = new StringBuilder(len + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string ClassOf(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        return GetClassName(hWnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }

    /// <summary>
    /// Proceso de la app de la Tienda que hay dentro de un marco de ApplicationFrameHost: su
    /// ventana hija "Windows.UI.Core.CoreWindow" pertenece a otro proceso, el de la app real.
    /// Null si no se encuentra (p. ej. app suspendida sin ventana interior).
    /// </summary>
    private static string? ResolveStoreApp(IntPtr frame, uint hostPid)
    {
        string? found = null;
        EnumChildWindows(frame, (child, _) =>
        {
            if (ClassOf(child) != "Windows.UI.Core.CoreWindow") return true;
            GetWindowThreadProcessId(child, out uint childPid);
            if (childPid == 0 || childPid == hostPid) return true;
            try { using var p = Process.GetProcessById((int)childPid); found = p.ProcessName; }
            catch { }
            return found is null; // seguir buscando solo si aún no se resolvió
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>True solo si Windows confirma que la ventana está en un escritorio virtual que no es el actual.</summary>
    private static bool IsOnOtherVirtualDesktop(IntPtr hWnd)
    {
        try
        {
            var manager = (IVirtualDesktopManager)new VirtualDesktopManagerClass();
            return manager.IsWindowOnCurrentVirtualDesktop(hWnd, out bool onCurrent) == 0 && !onCurrent;
        }
        catch { return false; } // sin certeza, se trata como oculta (se descarta)
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
    public static List<AppInfo> Collect(Func<Dictionary<string, string>> windowedNames, bool logBreakdown = false)
    {
        var windowed = windowedNames();
        if (windowed.Count == 0) return new();

        uint ownSession = (uint)Process.GetCurrentProcess().SessionId;
        ulong totalPhys = LocalApps.TotalPhysicalMemory();
        int ncpu = Math.Max(1, Environment.ProcessorCount);

        // Procesos de cada app: los que tienen su nombre MÁS sus ayudantes (como el Administrador de tareas).
        var procsByName = new Dictionary<string, List<Process>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (app, pids) in GroupProcesses(windowed.Keys, ownSession))
        {
            var list = new List<Process>();
            foreach (int pid in pids)
            {
                try { list.Add(Process.GetProcessById(pid)); }
                catch { /* terminó entre medias */ }
            }
            procsByName[app] = list;
        }

        try
        {
            // Muestreo de CPU: 1 s, como el refresco del Administrador de tareas. Con 450 ms los
            // valores saltaban mucho de una lectura a otra.
            var t0 = new Dictionary<int, TimeSpan>();
            foreach (var p in procsByName.Values.SelectMany(l => l))
                try { t0[p.Id] = p.TotalProcessorTime; } catch { }

            var sw = Stopwatch.StartNew();
            Thread.Sleep(1000);
            sw.Stop();
            double wall = sw.Elapsed.TotalMilliseconds;

            var result = new List<AppInfo>();
            var breakdown = new List<string>();
            foreach (var (name, procs) in procsByName)
            {
                double cpu = 0;
                ulong privateBytes = 0, workingSetBytes = 0;
                foreach (var p in procs)
                {
                    try
                    {
                        p.Refresh();
                        if (t0.TryGetValue(p.Id, out var start))
                            cpu += (p.TotalProcessorTime - start).TotalMilliseconds / (wall * ncpu) * 100.0;

                        ulong workingSet = (ulong)p.WorkingSet64;
                        workingSetBytes += workingSet;
                        // Memoria privada (la del Administrador de tareas). Sumar el working set
                        // total contaba varias veces las páginas compartidas entre los procesos de
                        // una misma app; si Windows no da la privada se usa el total.
                        privateBytes += ProcessTable.PrivateWorkingSet(p.Id) ?? workingSet;
                    }
                    catch { }
                }

                // El nombre sale de los procesos con el nombre de la app, no de sus ayudantes
                // (si no, una app podría llamarse "Microsoft Edge WebView2").
                var roots = procs.Where(p => IsNamed(p, name)).ToList();
                string friendly = FriendlyName(roots, name);

                double ramPercent = totalPhys > 0 ? privateBytes / (double)totalPhys * 100.0 : 0;
                result.Add(new AppInfo
                {
                    ProcessName = friendly,
                    WindowTitle = windowed.TryGetValue(name, out var t) ? t : null,
                    CpuPercent = Math.Clamp(cpu, 0, 100),
                    RamPercent = Math.Clamp(ramPercent, 0, 100),
                });

                if (logBreakdown)
                    breakdown.Add($"{friendly}: {procs.Count} procesos ({roots.Count} propios), " +
                        $"privada {privateBytes / 1048576.0:F0} MB, working set {workingSetBytes / 1048576.0:F0} MB, " +
                        $"CPU {cpu:F1}%");
            }

            if (logBreakdown)
                AppLog.Write($"Consumo (RAM total {totalPhys / 1048576.0:F0} MB, {ncpu} CPU lógicas): " +
                    string.Join(" | ", breakdown));

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

    /// <summary>PID de cada app en esta sesión, con sus ayudantes. Si no se puede leer la tabla de procesos, solo por nombre.</summary>
    private static Dictionary<string, List<int>> GroupProcesses(IReadOnlyCollection<string> apps, uint session)
    {
        try
        {
            var table = ProcessTable.Read(session);
            if (table is { Count: > 0 })
                return ProcessGrouping.Group(table, apps);
        }
        catch { /* se cae al respaldo */ }

        var byName = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in apps)
        {
            var ids = new List<int>();
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { if (p.SessionId == session) ids.Add(p.Id); }
                catch { }
                finally { p.Dispose(); }
            }
            byName[name] = ids;
        }
        return byName;
    }

    private static bool IsNamed(Process p, string name)
    {
        try { return string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase); }
        catch { return false; } // el proceso ya terminó
    }

    // Resolver el nombre amigable abre el módulo principal y lee la versión del .exe: es lo más caro
    // de cada ronda. El nombre de una app no cambia mientras corre, así que se recuerda por proceso.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> FriendlyCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static string FriendlyName(List<Process> procs, string fallback)
    {
        // explorer.exe es también el escritorio y la barra de tareas; para el usuario es el Explorador.
        if (string.Equals(fallback, "explorer", StringComparison.OrdinalIgnoreCase))
            return "Explorador de Windows";

        if (FriendlyCache.TryGetValue(fallback, out var cached)) return cached;

        foreach (var p in procs)
        {
            try
            {
                string? d = p.MainModule?.FileVersionInfo.FileDescription;
                if (!string.IsNullOrWhiteSpace(d))
                    return FriendlyCache[fallback] = AppNames.Clean(d); // "WhatsApp.Root" -> "WhatsApp"
            }
            catch { }
        }
        // Sin descripción (o sin permiso para leerla): nombre capitalizado. No se cachea, así que
        // se reintenta en la siguiente ronda por si el módulo ya es accesible.
        return fallback.Length > 0 ? AppNames.Clean(char.ToUpper(fallback[0]) + fallback[1..]) : fallback;
    }
}
