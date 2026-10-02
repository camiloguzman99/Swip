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
        var sessions = new SessionManager(_log);
        var pipe = new PipeServer(sessions, _log);
        await pipe.RunAsync(stoppingToken);
        _log.LogInformation("Servicio Swip detenido.");
    }
}
