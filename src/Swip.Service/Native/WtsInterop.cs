using System.Runtime.InteropServices;

namespace Swip.Service.Native;

/// <summary>
/// Llamadas P/Invoke a las APIs de Windows Terminal Services (WTS) y afines.
/// Estas son las funciones que requieren privilegios de SYSTEM para operar entre sesiones,
/// por eso viven en el servicio y no en el gato.
/// </summary>
internal static class WtsInterop
{
    public static readonly IntPtr WTS_CURRENT_SERVER_HANDLE = IntPtr.Zero;

    public enum WTS_CONNECTSTATE_CLASS
    {
        WTSActive,
        WTSConnected,
        WTSConnectQuery,
        WTSShadow,
        WTSDisconnected,
        WTSIdle,
        WTSListen,
        WTSReset,
        WTSDown,
        WTSInit,
    }

    public enum WTS_INFO_CLASS
    {
        WTSUserName = 5,
        WTSDomainName = 7,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WTS_SESSION_INFO
    {
        public int SessionId;
        [MarshalAs(UnmanagedType.LPWStr)] public string pWinStationName;
        public WTS_CONNECTSTATE_CLASS State;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WTS_PROCESS_INFO
    {
        public int SessionId;
        public int ProcessId;
        [MarshalAs(UnmanagedType.LPWStr)] public string pProcessName;
        public IntPtr pUserSid;
    }

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool WTSEnumerateSessions(
        IntPtr hServer,
        int Reserved,
        int Version,
        out IntPtr ppSessionInfo,
        out int pCount);

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool WTSEnumerateProcesses(
        IntPtr hServer,
        int Reserved,
        int Version,
        out IntPtr ppProcessInfo,
        out int pCount);

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool WTSQuerySessionInformation(
        IntPtr hServer,
        int sessionId,
        WTS_INFO_CLASS wtsInfoClass,
        out IntPtr ppBuffer,
        out int pBytesReturned);

    /// <summary>
    /// Conecta la sesión de origen a la consola física, es decir cambia a esa sesión.
    /// Llamado desde SYSTEM con contraseña vacía, Windows realiza el cambio sin pedir credenciales.
    /// </summary>
    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool WTSConnectSession(
        int LogonId,
        int TargetLogonId,
        string pPassword,
        bool bWait);

    [DllImport("wtsapi32.dll")]
    public static extern void WTSFreeMemory(IntPtr pMemory);

    [DllImport("kernel32.dll")]
    public static extern int WTSGetActiveConsoleSessionId();
}
