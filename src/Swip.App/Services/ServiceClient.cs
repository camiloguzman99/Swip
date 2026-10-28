using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Swip.Shared;

namespace Swip.App.Services;

/// <summary>
/// Cliente del named pipe: envía peticiones al servicio SYSTEM y lee la respuesta.
/// Una conexión por petición, igual que el servidor.
/// </summary>
public sealed class ServiceClient
{
    private const int ConnectTimeoutMs = 2000;

    /// <summary>Tope de la respuesta: una lista de sesiones, usuarios o apps ocupa unos pocos KB.</summary>
    private const int MaxResponseChars = 4 << 20;

    public Task<IReadOnlyList<SessionInfo>> GetSessionsAsync(CancellationToken ct = default) =>
        SendAsync(new IpcRequest { Kind = RequestKind.ListSessions }, ct,
            r => (IReadOnlyList<SessionInfo>)r.Sessions);

    public Task<IReadOnlyList<UserInfo>> GetUsersAsync(CancellationToken ct = default) =>
        SendAsync(new IpcRequest { Kind = RequestKind.ListUsers }, ct,
            r => (IReadOnlyList<UserInfo>)r.Users);

    public Task<IReadOnlyList<AppInfo>> GetWindowedAppsAsync(int sessionId, CancellationToken ct = default) =>
        SendAsync(new IpcRequest { Kind = RequestKind.ListWindowedApps, TargetSessionId = sessionId }, ct,
            r => (IReadOnlyList<AppInfo>)r.Apps);

    /// <summary>Publica las apps de ESTA sesión (el servicio identifica la sesión por el pipe).</summary>
    public Task PublishAppsAsync(IReadOnlyList<AppInfo> apps, CancellationToken ct = default) =>
        SendAsync(new IpcRequest
        {
            Kind = RequestKind.PublishApps,
            Apps = apps as List<AppInfo> ?? new List<AppInfo>(apps),
        }, ct, _ => true);

    /// <summary>Avisa al servicio de que el usuario cerró el gato: que no se lo relance.</summary>
    public Task QuitSessionAsync(CancellationToken ct = default) =>
        SendAsync(new IpcRequest { Kind = RequestKind.QuitSession }, ct, _ => true);

    public Task SwitchToSessionAsync(int sessionId, CancellationToken ct = default) =>
        SendAsync(new IpcRequest { Kind = RequestKind.SwitchToSession, TargetSessionId = sessionId }, ct,
            _ => true);

    public Task StartLogonAsync(CancellationToken ct = default) =>
        SendAsync(new IpcRequest { Kind = RequestKind.StartLogon }, ct, _ => true);

    private async Task<T> SendAsync<T>(IpcRequest request, CancellationToken ct, Func<IpcResponse, T> select)
    {
        using var client = new NamedPipeClientStream(
            ".", IpcProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        try
        {
            await client.ConnectAsync(ConnectTimeoutMs, ct);
        }
        catch (TimeoutException)
        {
            throw new ServiceUnavailableException(
                "No se pudo contactar el servicio Swip. ¿Está instalado y en ejecución?");
        }

        // Solo se habla con el servicio de verdad (sesión 0): si un proceso de usuario se hubiera adelantado a crear
        // el pipe, no se le envía nada ni se cree lo que conteste.
        if (!Native.InteropNative.PipeServerIsInServicesSession(client.SafePipeHandle))
            throw new ServiceUnavailableException("El pipe de Swip no lo atiende el servicio: se ignora.");

        using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };
        using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, leaveOpen: true);

        await writer.WriteLineAsync(JsonSerializer.Serialize(request, IpcProtocol.Json));

        string? line = await BoundedLine.ReadAsync(reader, MaxResponseChars, ct);
        if (line is null)
            throw new ServiceUnavailableException("El servicio Swip cerró la conexión sin responder.");

        var response = JsonSerializer.Deserialize<IpcResponse>(line, IpcProtocol.Json)
            ?? throw new ServiceUnavailableException("Respuesta inválida del servicio Swip.");

        if (!response.Ok)
            throw new ServiceOperationException(response.Error ?? "El servicio Swip reportó un error.");

        return select(response);
    }
}

/// <summary>El servicio no está disponible (no instalado, detenido o inaccesible).</summary>
public sealed class ServiceUnavailableException(string message) : Exception(message);

/// <summary>El servicio respondió, pero la operación falló.</summary>
public sealed class ServiceOperationException(string message) : Exception(message);
