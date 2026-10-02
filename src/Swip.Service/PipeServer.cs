using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Swip.Shared;

namespace Swip.Service;

/// <summary>
/// Servidor de named pipe del servicio. Atiende una petición por conexión (modelo simple
/// petición/respuesta) y delega en <see cref="SessionManager"/>.
/// </summary>
internal sealed class PipeServer
{
    private readonly SessionManager _sessions;
    private readonly ILogger _log;

    public PipeServer(SessionManager sessions, ILogger log)
    {
        _sessions = sessions;
        _log = log;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = CreatePipe();
                await server.WaitForConnectionAsync(ct);
                await HandleConnectionAsync(server, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Error atendiendo una conexión; se reintenta.");
                await Task.Delay(500, ct);
            }
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        // ACL: SYSTEM control total; usuarios interactivos pueden leer/escribir para poder
        // consultar y pedir el cambio. El gato corre en la sesión interactiva del usuario.
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            IpcProtocol.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: security);
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true);
        using var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        string? line = await reader.ReadLineAsync(ct);
        if (line is null)
            return;

        IpcResponse response;
        try
        {
            var request = JsonSerializer.Deserialize<IpcRequest>(line, IpcProtocol.Json)
                ?? throw new InvalidOperationException("Petición vacía.");
            response = Dispatch(request);
        }
        catch (Exception ex)
        {
            response = new IpcResponse { Ok = false, Error = ex.Message };
        }

        await writer.WriteLineAsync(JsonSerializer.Serialize(response, IpcProtocol.Json));
    }

    private IpcResponse Dispatch(IpcRequest request) => request.Kind switch
    {
        RequestKind.ListSessions =>
            new IpcResponse { Ok = true, Sessions = _sessions.GetSessions() },

        RequestKind.ListUsers =>
            new IpcResponse { Ok = true, Users = _sessions.GetUsers() },

        RequestKind.ListWindowedApps =>
            new IpcResponse { Ok = true, Apps = _sessions.GetWindowedApps(request.TargetSessionId) },

        RequestKind.SwitchToSession => SwitchResponse(request.TargetSessionId),

        _ => new IpcResponse { Ok = false, Error = "Petición no reconocida." },
    };

    private IpcResponse SwitchResponse(int targetSessionId)
    {
        _sessions.SwitchToSession(targetSessionId);
        return new IpcResponse { Ok = true };
    }
}
