using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Swip.Service.Native;
using Swip.Shared;

namespace Swip.Service;

/// <summary>
/// Lógica de negocio del servicio: enumerar sesiones, cambiar de sesión y leer las apps con
/// ventana de otra sesión. Todo esto requiere privilegios de SYSTEM.
/// </summary>
internal sealed class SessionManager
{
    private readonly ILogger _log;

    public SessionManager(ILogger log) => _log = log;

    /// <summary>Enumera las sesiones de usuario reales de esta máquina (una por usuario conectado).</summary>
    public List<SessionInfo> GetSessions()
    {
        var result = new List<SessionInfo>();
        int currentConsole = WtsInterop.WTSGetActiveConsoleSessionId();

        foreach (var raw in EnumerateRaw())
        {
            if (!IsUserSession(raw))
                continue;

            string display = !string.IsNullOrEmpty(raw.User)
                ? raw.User
                : (!string.IsNullOrEmpty(raw.WinStation) ? raw.WinStation : $"Sesión {raw.SessionId}");

            result.Add(new SessionInfo
            {
                SessionId = raw.SessionId,
                UserName = display,
                Domain = raw.Domain,
                State = MapState(raw.State),
                IsCurrent = raw.SessionId == currentConsole,
            });
        }

        return result;
    }

    /// <summary>
    /// Decide si una sesión representa a un usuario (y por tanto debe tener gato).
    /// Incluye la sesión con nombre de usuario y las sesiones desconectadas (usuarios en
    /// segundo plano cuyo nombre a veces no se puede consultar). Excluye la sesión de
    /// servicios (0), los listeners RDP y la pantalla de inicio de sesión (sin usuario y conectada).
    /// </summary>
    private static bool IsUserSession(RawSession s)
    {
        if (s.SessionId == 0) return false;
        if (string.Equals(s.WinStation, "Services", StringComparison.OrdinalIgnoreCase)) return false;
        if (s.State == WtsInterop.WTS_CONNECTSTATE_CLASS.WTSListen) return false;
        if (s.State == WtsInterop.WTS_CONNECTSTATE_CLASS.WTSDown) return false;

        if (!string.IsNullOrEmpty(s.User))
            return true;

        // Sin nombre de usuario: solo cuenta si está desconectada (usuario en segundo plano).
        // Una sesión Active/Connected sin usuario suele ser la pantalla de inicio de sesión.
        return s.State == WtsInterop.WTS_CONNECTSTATE_CLASS.WTSDisconnected;
    }

    private readonly record struct RawSession(
        int SessionId, string WinStation, string User, string Domain,
        WtsInterop.WTS_CONNECTSTATE_CLASS State);

    private IEnumerable<RawSession> EnumerateRaw()
    {
        if (!WtsInterop.WTSEnumerateSessions(WtsInterop.WTS_CURRENT_SERVER_HANDLE, 0, 1,
                out IntPtr buffer, out int count))
        {
            throw new InvalidOperationException(
                $"WTSEnumerateSessions falló (error {Marshal.GetLastWin32Error()}).");
        }

        var list = new List<RawSession>();
        try
        {
            int size = Marshal.SizeOf<WtsInterop.WTS_SESSION_INFO>();
            IntPtr current = buffer;
            for (int i = 0; i < count; i++)
            {
                var si = Marshal.PtrToStructure<WtsInterop.WTS_SESSION_INFO>(current);
                current += size;
                list.Add(new RawSession(
                    si.SessionId,
                    si.pWinStationName ?? string.Empty,
                    QueryString(si.SessionId, WtsInterop.WTS_INFO_CLASS.WTSUserName),
                    QueryString(si.SessionId, WtsInterop.WTS_INFO_CLASS.WTSDomainName),
                    si.State));
            }
        }
        finally
        {
            WtsInterop.WTSFreeMemory(buffer);
        }
        return list;
    }

    /// <summary>Texto de diagnóstico con TODAS las sesiones que Windows reporta (modo --diagnose).</summary>
    public string DiagnoseText()
    {
        int console = WtsInterop.WTSGetActiveConsoleSessionId();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Consola activa: sesión {console}");
        sb.AppendLine("Id | Estado        | WinStation       | Usuario            | ¿es usuario?");
        sb.AppendLine(new string('-', 78));
        foreach (var s in EnumerateRaw())
        {
            string user = string.IsNullOrEmpty(s.User) ? "(vacío)"
                : (string.IsNullOrEmpty(s.Domain) ? s.User : $"{s.Domain}\\{s.User}");
            sb.AppendLine($"{s.SessionId,2} | {s.State,-13} | {s.WinStation,-16} | {user,-18} | {(IsUserSession(s) ? "sí" : "no")}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Cambia a la sesión indicada conectándola a la consola física. Como el servicio corre como
    /// SYSTEM, Windows realiza el cambio sin pedir la contraseña.
    /// </summary>
    public void SwitchToSession(int targetSessionId)
    {
        int console = WtsInterop.WTSGetActiveConsoleSessionId();
        if (targetSessionId == console)
            return; // ya está en pantalla

        if (!WtsInterop.WTSConnectSession(targetSessionId, console, string.Empty, true))
        {
            throw new InvalidOperationException(
                $"WTSConnectSession falló (error {Marshal.GetLastWin32Error()}).");
        }
        _log.LogInformation("Cambiada la consola a la sesión {Session}.", targetSessionId);
    }

    /// <summary>
    /// Devuelve las apps con ventana visible de la sesión indicada, lanzando un ayudante dentro
    /// de esa sesión. Si algo falla devuelve una lista vacía en vez de lanzar excepción.
    /// </summary>
    public List<AppInfo> GetWindowedApps(int targetSessionId)
    {
        IntPtr userToken = IntPtr.Zero, dupToken = IntPtr.Zero, envBlock = IntPtr.Zero;
        string outFile = Path.Combine(
            Path.GetTempPath(), $"swip-apps-{targetSessionId}-{Guid.NewGuid():N}.json");

        try
        {
            if (!ProcessInterop.WTSQueryUserToken(targetSessionId, out userToken))
            {
                _log.LogWarning("WTSQueryUserToken falló para sesión {S} (error {E}).",
                    targetSessionId, Marshal.GetLastWin32Error());
                return new();
            }

            if (!ProcessInterop.DuplicateTokenEx(userToken, ProcessInterop.MAXIMUM_ALLOWED, IntPtr.Zero,
                    ProcessInterop.SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                    ProcessInterop.TOKEN_TYPE.TokenPrimary, out dupToken))
            {
                return new();
            }

            ProcessInterop.CreateEnvironmentBlock(out envBlock, dupToken, false);

            string exePath = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule!.FileName;
            string cmdLine = $"\"{exePath}\" --enumerate-windows \"{outFile}\"";

            var si = new ProcessInterop.STARTUPINFO();
            si.cb = Marshal.SizeOf<ProcessInterop.STARTUPINFO>();
            si.lpDesktop = @"winsta0\default"; // el escritorio interactivo de la sesión objetivo
            si.dwFlags = ProcessInterop.STARTF_USESHOWWINDOW;
            si.wShowWindow = ProcessInterop.SW_HIDE;

            uint flags = ProcessInterop.CREATE_UNICODE_ENVIRONMENT | ProcessInterop.CREATE_NO_WINDOW;

            if (!ProcessInterop.CreateProcessAsUser(dupToken, null, cmdLine, IntPtr.Zero, IntPtr.Zero,
                    false, flags, envBlock, null, ref si, out var pi))
            {
                _log.LogWarning("CreateProcessAsUser falló para sesión {S} (error {E}).",
                    targetSessionId, Marshal.GetLastWin32Error());
                return new();
            }

            try
            {
                using var helper = Process.GetProcessById(pi.dwProcessId);
                if (!helper.WaitForExit(5000))
                {
                    try { helper.Kill(); } catch { /* best effort */ }
                    return new();
                }
            }
            catch
            {
                // El proceso puede haber terminado antes de poder adjuntarlo; seguimos a leer el archivo.
            }
            finally
            {
                ProcessInterop.CloseHandle(pi.hProcess);
                ProcessInterop.CloseHandle(pi.hThread);
            }

            if (!File.Exists(outFile))
                return new();

            string json = File.ReadAllText(outFile);
            return JsonSerializer.Deserialize<List<AppInfo>>(json, IpcProtocol.Json) ?? new();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudieron leer las apps de la sesión {S}.", targetSessionId);
            return new();
        }
        finally
        {
            if (envBlock != IntPtr.Zero) ProcessInterop.DestroyEnvironmentBlock(envBlock);
            if (dupToken != IntPtr.Zero) ProcessInterop.CloseHandle(dupToken);
            if (userToken != IntPtr.Zero) ProcessInterop.CloseHandle(userToken);
            try { if (File.Exists(outFile)) File.Delete(outFile); } catch { /* best effort */ }
        }
    }

    private static string QueryString(int sessionId, WtsInterop.WTS_INFO_CLASS info)
    {
        if (!WtsInterop.WTSQuerySessionInformation(WtsInterop.WTS_CURRENT_SERVER_HANDLE, sessionId,
                info, out IntPtr buffer, out _))
        {
            return string.Empty;
        }
        try
        {
            return Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally
        {
            WtsInterop.WTSFreeMemory(buffer);
        }
    }

    private static SessionConnectionState MapState(WtsInterop.WTS_CONNECTSTATE_CLASS s) => s switch
    {
        WtsInterop.WTS_CONNECTSTATE_CLASS.WTSActive => SessionConnectionState.Active,
        WtsInterop.WTS_CONNECTSTATE_CLASS.WTSConnected => SessionConnectionState.Connected,
        WtsInterop.WTS_CONNECTSTATE_CLASS.WTSConnectQuery => SessionConnectionState.ConnectQuery,
        WtsInterop.WTS_CONNECTSTATE_CLASS.WTSShadow => SessionConnectionState.Shadow,
        WtsInterop.WTS_CONNECTSTATE_CLASS.WTSDisconnected => SessionConnectionState.Disconnected,
        WtsInterop.WTS_CONNECTSTATE_CLASS.WTSIdle => SessionConnectionState.Idle,
        WtsInterop.WTS_CONNECTSTATE_CLASS.WTSListen => SessionConnectionState.Listen,
        WtsInterop.WTS_CONNECTSTATE_CLASS.WTSReset => SessionConnectionState.Reset,
        WtsInterop.WTS_CONNECTSTATE_CLASS.WTSDown => SessionConnectionState.Down,
        WtsInterop.WTS_CONNECTSTATE_CLASS.WTSInit => SessionConnectionState.Init,
        _ => SessionConnectionState.Unknown,
    };
}
