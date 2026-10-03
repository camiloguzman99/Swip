using System.Collections.Concurrent;
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

        sb.AppendLine();
        sb.AppendLine("Gatos (un usuario = un gato):");
        sb.AppendLine("Usuario                    | Sesión | Estado        | ¿actual?");
        sb.AppendLine(new string('-', 70));
        foreach (var u in GetUsers())
        {
            string sess = u.HasSession ? u.SessionId.ToString() : "ninguna";
            sb.AppendLine($"{u.DisplayName,-26} | {sess,-6} | {u.State,-13} | {(u.IsCurrent ? "sí" : "no")}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Devuelve una entrada por cada CUENTA de usuario del equipo (no por sesión), uniendo las
    /// cuentas locales habilitadas con las sesiones vivas. Un usuario con sesión abierta trae su
    /// SessionId; uno sin sesión trae SessionId = -1. También incluye usuarios con sesión que no
    /// son cuentas locales (dominio / Microsoft / AzureAD).
    /// </summary>
    public List<UserInfo> GetUsers()
    {
        int console = WtsInterop.WTSGetActiveConsoleSessionId();
        string machine = Environment.MachineName;

        // Sesiones vivas agrupadas por nombre de usuario (preferimos la de consola/activa).
        var sessionByUser = new Dictionary<string, RawSession>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in EnumerateRaw())
        {
            if (!IsUserSession(raw) || string.IsNullOrEmpty(raw.User))
                continue;
            bool exists = sessionByUser.TryGetValue(raw.User, out var existing);
            bool prefer = !exists
                || raw.SessionId == console
                || (raw.State == WtsInterop.WTS_CONNECTSTATE_CLASS.WTSActive
                    && existing.State != WtsInterop.WTS_CONNECTSTATE_CLASS.WTSActive);
            if (prefer)
                sessionByUser[raw.User] = raw;
        }

        var byName = new Dictionary<string, UserInfo>(StringComparer.OrdinalIgnoreCase);

        // 1) Cuentas locales habilitadas: haya o no sesión abierta.
        List<string> locals;
        try { locals = NetApiInterop.EnumerateEnabledLocalUsers(); }
        catch { locals = new(); }

        foreach (var name in locals)
        {
            var info = new UserInfo { UserName = name, Domain = machine };
            if (sessionByUser.TryGetValue(name, out var s))
            {
                info.SessionId = s.SessionId;
                info.State = MapState(s.State);
                info.IsCurrent = s.SessionId == console;
                info.Domain = string.IsNullOrEmpty(s.Domain) ? machine : s.Domain;
            }
            byName[name] = info;
        }

        // 2) Usuarios con sesión que no son cuentas locales (dominio / Microsoft / AzureAD).
        foreach (var (name, s) in sessionByUser)
        {
            if (byName.ContainsKey(name))
                continue;
            byName[name] = new UserInfo
            {
                UserName = name,
                Domain = string.IsNullOrEmpty(s.Domain) ? machine : s.Domain,
                SessionId = s.SessionId,
                State = MapState(s.State),
                IsCurrent = s.SessionId == console,
            };
        }

        return byName.Values
            .OrderByDescending(u => u.IsCurrent)
            .ThenByDescending(u => u.HasSession)
            .ThenBy(u => u.UserName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Lanza el gato (Swip.exe) en todas las sesiones de usuario que no lo tengan ya en ejecución.
    /// Se usa al arrancar el servicio (y tras una actualización) para que el gato reaparezca en
    /// todas las sesiones, no solo en la que corrió el actualizador.
    /// </summary>
    public void RelaunchAppInAllSessions()
    {
        string? serviceExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(serviceExe)) return;
        string? installRoot = Directory.GetParent(Path.GetDirectoryName(serviceExe)!)?.FullName;
        if (installRoot is null) return;
        string appExe = Path.Combine(installRoot, "App", "Swip.exe");
        if (!File.Exists(appExe)) { Log($"RelaunchApp: no existe {appExe}"); return; }

        foreach (var raw in EnumerateRaw())
        {
            if (!IsUserSession(raw)) continue;
            try
            {
                if (IsAppRunning(raw.SessionId, "Swip")) continue;
                LaunchInSession(raw.SessionId, appExe);
                Log($"RelaunchApp: lanzado en sesión {raw.SessionId}");
            }
            catch (Exception ex) { Log($"RelaunchApp sesión {raw.SessionId}: {ex.Message}"); }
        }
    }

    private static bool IsAppRunning(int sessionId, string processNameNoExt)
    {
        if (!WtsInterop.WTSEnumerateProcesses(WtsInterop.WTS_CURRENT_SERVER_HANDLE, 0, 1,
                out IntPtr buffer, out int count))
            return false;
        try
        {
            int size = Marshal.SizeOf<WtsInterop.WTS_PROCESS_INFO>();
            IntPtr cur = buffer;
            for (int i = 0; i < count; i++)
            {
                var pi = Marshal.PtrToStructure<WtsInterop.WTS_PROCESS_INFO>(cur);
                cur += size;
                if (pi.SessionId == sessionId &&
                    string.Equals(pi.pProcessName, processNameNoExt + ".exe", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        finally { WtsInterop.WTSFreeMemory(buffer); }
        return false;
    }

    private static void LaunchInSession(int sessionId, string exePath)
    {
        IntPtr userToken = IntPtr.Zero, dupToken = IntPtr.Zero, envBlock = IntPtr.Zero;
        try
        {
            if (!ProcessInterop.WTSQueryUserToken(sessionId, out userToken)) return;
            if (!ProcessInterop.DuplicateTokenEx(userToken, ProcessInterop.MAXIMUM_ALLOWED, IntPtr.Zero,
                    ProcessInterop.SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                    ProcessInterop.TOKEN_TYPE.TokenPrimary, out dupToken)) return;
            ProcessInterop.CreateEnvironmentBlock(out envBlock, dupToken, false);

            var si = new ProcessInterop.STARTUPINFO();
            si.cb = Marshal.SizeOf<ProcessInterop.STARTUPINFO>();
            si.lpDesktop = @"winsta0\default";

            string cmd = $"\"{exePath}\"";
            ProcessInterop.CreateProcessAsUser(dupToken, null, cmd, IntPtr.Zero, IntPtr.Zero, false,
                ProcessInterop.CREATE_UNICODE_ENVIRONMENT, envBlock,
                Path.GetDirectoryName(exePath), ref si, out var pi);
            if (pi.hProcess != IntPtr.Zero) ProcessInterop.CloseHandle(pi.hProcess);
            if (pi.hThread != IntPtr.Zero) ProcessInterop.CloseHandle(pi.hThread);
        }
        finally
        {
            if (envBlock != IntPtr.Zero) ProcessInterop.DestroyEnvironmentBlock(envBlock);
            if (dupToken != IntPtr.Zero) ProcessInterop.CloseHandle(dupToken);
            if (userToken != IntPtr.Zero) ProcessInterop.CloseHandle(userToken);
        }
    }

    /// <summary>
    /// Muestra la pantalla de inicio de sesión de Windows (desconecta la sesión de consola),
    /// para que el usuario pueda iniciar una cuenta que no tiene sesión abierta.
    /// </summary>
    public void StartLogon()
    {
        int console = WtsInterop.WTSGetActiveConsoleSessionId();
        if (!WtsInterop.WTSDisconnectSession(WtsInterop.WTS_CURRENT_SERVER_HANDLE, console, true))
        {
            throw new InvalidOperationException(
                $"WTSDisconnectSession falló (error {Marshal.GetLastWin32Error()}).");
        }
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
    /// Caché de apps publicadas por cada gato, indexada por id de sesión. Cada gato enumera las
    /// apps de SU propia sesión (puede hacerlo en proceso, sin privilegios) y las publica aquí
    /// periódicamente. Así otra sesión puede leerlas sin que el servicio (sesión 0) tenga que
    /// enumerar ventanas de un escritorio ajeno, cosa que Windows no permite de forma fiable.
    /// </summary>
    private static readonly ConcurrentDictionary<int, (DateTime When, List<AppInfo> Apps)> PublishedApps = new();

    /// <summary>
    /// Tiempo máximo que se considera "fresca" una publicación de apps. Un gato en segundo plano
    /// sigue publicando (aunque no pueda ver sus ventanas) cada ~8 s refrescando la marca de
    /// tiempo de la última lista conocida, así que basta una ventana corta.
    /// </summary>
    private static readonly TimeSpan AppsFreshness = TimeSpan.FromSeconds(60);

    /// <summary>
    /// El gato de una sesión publica aquí las apps que ha enumerado en su propio escritorio.
    /// Una sesión en segundo plano (desconectada) no puede enumerar sus ventanas y publica una
    /// lista vacía; en ese caso conservamos la última lista conocida (lo que tenía abierto cuando
    /// estaba en pantalla) y solo refrescamos la marca de tiempo, para que siga visible mientras
    /// su gato siga vivo. Una sesión activa sí ve sus ventanas, así que su lista vacía es real.
    /// </summary>
    public void PublishApps(int sessionId, List<AppInfo> apps, bool activeConsole)
    {
        apps ??= new();
        if (apps.Count == 0 && !activeConsole
            && PublishedApps.TryGetValue(sessionId, out var prev) && prev.Apps.Count > 0)
        {
            // Segundo plano sin poder ver: mantener la última lista conocida, refrescar el tiempo.
            PublishedApps[sessionId] = (DateTime.UtcNow, prev.Apps);
            Log($"PublishApps sesión={sessionId} vacía en 2º plano → se mantienen {prev.Apps.Count} apps conocidas");
            return;
        }

        PublishedApps[sessionId] = (DateTime.UtcNow, apps);
        Log($"PublishApps sesión={sessionId} apps={apps.Count} activa={activeConsole}");
    }

    /// <summary>
    /// Devuelve las apps con ventana visible de la sesión indicada, leídas de la caché que
    /// publica el gato de esa sesión. Si no hay publicación reciente devuelve una lista vacía.
    /// </summary>
    public List<AppInfo> GetWindowedApps(int targetSessionId)
    {
        if (PublishedApps.TryGetValue(targetSessionId, out var entry))
        {
            var age = DateTime.UtcNow - entry.When;
            if (age <= AppsFreshness)
            {
                Log($"GetWindowedApps sesión={targetSessionId} OK apps={entry.Apps.Count} (edad={age.TotalSeconds:F0}s)");
                return entry.Apps;
            }
            Log($"GetWindowedApps sesión={targetSessionId} caché vieja (edad={age.TotalSeconds:F0}s)");
            return new();
        }

        Log($"GetWindowedApps sesión={targetSessionId} sin publicación del gato");
        return new();
    }

    /// <summary>Crea la carpeta (si falta) y concede escritura a los usuarios locales.</summary>
    internal static void GrantUsersModify(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var di = new DirectoryInfo(dir);
            var sec = di.GetAccessControl();
            var users = new System.Security.Principal.SecurityIdentifier(
                System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null);
            sec.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                users,
                System.Security.AccessControl.FileSystemRights.Modify,
                System.Security.AccessControl.InheritanceFlags.ContainerInherit |
                    System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                System.Security.AccessControl.PropagationFlags.None,
                System.Security.AccessControl.AccessControlType.Allow));
            di.SetAccessControl(sec);
        }
        catch { /* si no se puede, seguimos */ }
    }

    /// <summary>Registro en %ProgramData%\Swip\service.log para diagnóstico.</summary>
    internal static void Log(string message)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Swip");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "service.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch { /* el log es best-effort */ }
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
