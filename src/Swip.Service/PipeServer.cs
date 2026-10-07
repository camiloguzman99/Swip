using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Swip.Service.Native;
using Swip.Shared;

namespace Swip.Service;

/// <summary>
/// Servidor de named pipe del servicio. Una petición por conexión (modelo simple petición/respuesta)
/// que delega en <see cref="SessionManager"/>. Cada conexión se atiende en su propia tarea con un
/// tiempo máximo, así un cliente colgado no bloquea a los demás (antes se atendía de uno en uno y
/// una conexión que no enviaba nada congelaba también el cambio de sesión).
/// </summary>
internal sealed class PipeServer
{
    /// <summary>Conexiones atendidas a la vez; más allá, el servidor espera (contrapresión).</summary>
    private const int MaxConcurrent = 16;

    /// <summary>Tope de la línea de petición. Una lista de apps ocupa unos pocos KB.</summary>
    private const int MaxRequestChars = 1 << 20;

    /// <summary>Máximo de apps que se aceptan en una publicación.</summary>
    private const int MaxAppsPerPublish = 200;

    /// <summary>Tiempo máximo de una conexión completa (leer, atender y responder).</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly SessionManager _sessions;
    private readonly ILogger _log;

    public PipeServer(SessionManager sessions, ILogger log)
    {
        _sessions = sessions;
        _log = log;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        using var slots = new SemaphoreSlim(MaxConcurrent);

        while (!ct.IsCancellationRequested)
        {
            bool slotTaken = false;
            NamedPipeServerStream? server = null;
            try
            {
                await slots.WaitAsync(ct);
                slotTaken = true;

                server = CreatePipe();
                await server.WaitForConnectionAsync(ct);

                // El manejador es ahora dueño de la conexión y del hueco; este bucle sigue aceptando.
                var connected = server;
                server = null;
                slotTaken = false;
                _ = HandleAsync(connected, slots, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Error aceptando una conexión; se reintenta.");
                try { await Task.Delay(500, ct); } catch (OperationCanceledException) { break; }
            }
            finally
            {
                server?.Dispose();
                if (slotTaken) slots.Release();
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

    private async Task HandleAsync(NamedPipeServerStream server, SemaphoreSlim slots, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(RequestTimeout);
            await HandleConnectionAsync(server, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            // Cliente colgado (timeout) o servicio parándose: se cierra la conexión sin más.
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Error atendiendo una conexión.");
        }
        finally
        {
            server.Dispose();
            slots.Release();
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        // Debe leerse mientras el cliente sigue conectado.
        int? clientSession = PipeInterop.ClientSessionId(server);

        using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true);
        using var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        IpcResponse response;
        try
        {
            string? line = await BoundedLine.ReadAsync(reader, MaxRequestChars, ct);
            if (line is null)
                return;

            var request = JsonSerializer.Deserialize<IpcRequest>(line, IpcProtocol.Json)
                ?? throw new InvalidOperationException("Petición vacía.");
            response = Dispatch(request, clientSession);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            response = new IpcResponse { Ok = false, Error = ex.Message };
        }

        await writer.WriteLineAsync(JsonSerializer.Serialize(response, IpcProtocol.Json).AsMemory(), ct);
        await writer.FlushAsync(ct);
    }

    private IpcResponse Dispatch(IpcRequest request, int? clientSession) => request.Kind switch
    {
        RequestKind.ListSessions =>
            new IpcResponse { Ok = true, Sessions = _sessions.GetSessions() },

        RequestKind.ListUsers =>
            new IpcResponse { Ok = true, Users = _sessions.GetUsers() },

        RequestKind.ListWindowedApps =>
            new IpcResponse { Ok = true, Apps = _sessions.GetWindowedApps(request.TargetSessionId) },

        // Publicar y salir actúan SIEMPRE sobre la sesión real del cliente: nadie puede falsear
        // la lista de otra sesión ni impedirle el gato a otro usuario.
        RequestKind.PublishApps => OwnSession(clientSession, session =>
            _sessions.PublishApps(session, request.Apps.Take(MaxAppsPerPublish).ToList())),

        RequestKind.QuitSession => OwnSession(clientSession, _sessions.QuitSession),

        RequestKind.SwitchToSession => SwitchResponse(request.TargetSessionId),

        RequestKind.StartLogon => StartLogonResponse(),

        _ => new IpcResponse { Ok = false, Error = "Petición no reconocida." },
    };

    private static IpcResponse OwnSession(int? clientSession, Action<int> action)
    {
        if (clientSession is null)
            return new IpcResponse { Ok = false, Error = "No se pudo identificar la sesión del cliente." };
        action(clientSession.Value);
        return new IpcResponse { Ok = true };
    }

    private IpcResponse SwitchResponse(int targetSessionId)
    {
        _sessions.SwitchToSession(targetSessionId);
        return new IpcResponse { Ok = true };
    }

    private IpcResponse StartLogonResponse()
    {
        _sessions.StartLogon();
        return new IpcResponse { Ok = true };
    }
}
