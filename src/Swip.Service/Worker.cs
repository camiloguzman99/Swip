using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Swip.Service;

/// <summary>Servicio de fondo que mantiene vivo el servidor de named pipe.</summary>
internal sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _log;

    public Worker(ILogger<Worker> log) => _log = log;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("Servicio Swip iniciado.");
        // Preparar %ProgramData%\Swip con escritura para usuarios (settings compartidos + intercambio).
        SessionManager.GrantUsersModify(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Swip"));
        SessionManager.Log("Servicio iniciado.");
        var sessions = new SessionManager(_log);

        // Vigilante: asegura que el gato esté corriendo en TODAS las sesiones de usuario, no solo
        // al arrancar (también tras una actualización, si el usuario lo cierra, o si una sesión
        // inicia después). RelaunchAppInAllSessions solo lo lanza donde falta, así que es seguro
        // repetirlo. Primera pasada a los 3 s y luego cada 30 s.
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(3000, stoppingToken); } catch { }
            while (!stoppingToken.IsCancellationRequested)
            {
                try { sessions.RelaunchAppInAllSessions(); }
                catch (Exception ex) { SessionManager.Log($"Vigilante: {ex.Message}"); }
                try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
                catch { break; }
            }
        }, stoppingToken);

        var pipe = new PipeServer(sessions, _log);
        await pipe.RunAsync(stoppingToken);
        _log.LogInformation("Servicio Swip detenido.");
    }
}
