using System.Runtime.InteropServices;
using Swip.Shared;

namespace Swip.App.Services;

/// <summary>
/// Lectura de la tabla de procesos del sistema (con el padre de cada uno) y de la memoria PRIVADA
/// de un proceso. Es la capa de Windows de <see cref="ProcessGrouping"/> y del cálculo de RAM.
/// </summary>
internal static class ProcessTable
{
    private const uint TH32CS_SNAPPROCESS = 0x2;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint PROCESS_VM_READ = 0x0010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public UIntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public uint Low; public uint High; }

    // Contadores de memoria extendidos: PrivateWorkingSetSize es la "memoria privada" que muestra el
    // Administrador de tareas (sin contar las páginas compartidas, como las DLL de Windows).
    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_MEMORY_COUNTERS_EX2
    {
        public uint cb;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSize;
        public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage;
        public UIntPtr PeakPagefileUsage;
        public UIntPtr PrivateUsage;
        public UIntPtr PrivateWorkingSetSize;
        public ulong SharedCommitUsage;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Process32FirstW(IntPtr snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Process32NextW(IntPtr snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(IntPtr process,
        out FILETIME creation, out FILETIME exit, out FILETIME kernel, out FILETIME user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetProcessMemoryInfo(IntPtr process, ref PROCESS_MEMORY_COUNTERS_EX2 counters, uint size);

    /// <summary>
    /// Los procesos de la sesión indicada con su padre y su hora de inicio, o null si no se pudo
    /// leer la tabla (entonces se agrupa solo por nombre, como antes).
    /// </summary>
    public static List<ProcInfo>? Read(uint session)
    {
        IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return null;

        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (!Process32FirstW(snapshot, ref entry)) return null;

            var list = new List<ProcInfo>();
            do
            {
                if (entry.th32ProcessID == 0) continue; // "System Idle Process"
                if (ProcessIdToSessionId(entry.th32ProcessID, out uint sid) && sid != session) continue;

                list.Add(new ProcInfo(
                    (int)entry.th32ProcessID,
                    (int)entry.th32ParentProcessID,
                    WithoutExe(entry.szExeFile),
                    CreationTicks(entry.th32ProcessID)));
            }
            while (Process32NextW(snapshot, ref entry));
            return list;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    /// <summary>
    /// Memoria privada (working set privado) de un proceso, la misma que usa el Administrador de
    /// tareas. Null si Windows no la da (proceso protegido o sistema antiguo): se usa entonces el
    /// working set total.
    /// </summary>
    public static ulong? PrivateWorkingSet(int pid)
    {
        IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, false, (uint)pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var counters = new PROCESS_MEMORY_COUNTERS_EX2
            {
                cb = (uint)Marshal.SizeOf<PROCESS_MEMORY_COUNTERS_EX2>(),
            };
            return GetProcessMemoryInfo(handle, ref counters, counters.cb)
                ? (ulong)counters.PrivateWorkingSetSize
                : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static long CreationTicks(uint pid)
    {
        IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return 0;
        try
        {
            return GetProcessTimes(handle, out var created, out _, out _, out _)
                ? ((long)created.High << 32) | created.Low
                : 0;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    // La tabla da "msedge.exe"; Process.ProcessName y los nombres de las apps van sin extensión.
    private static string WithoutExe(string file) =>
        file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
}
