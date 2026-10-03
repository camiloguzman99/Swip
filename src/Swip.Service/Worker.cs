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

        // Al arrancar (incluye tras una actualización), relanzar el gato en todas las sesiones
        // que no lo tengan, para que reaparezca también en la otra sesión.
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(3000, stoppingToken); sessions.RelaunchAppInAllSessions(); }
            catch { }
        }, stoppingToken);

        var pipe = new PipeServer(sessions, _log);
        await pipe.RunAsync(stoppingToken);
        _log.LogInformation("Servicio Swip detenido.");
    }
}
