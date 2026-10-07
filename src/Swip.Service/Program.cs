using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Swip.Service;

// Modo diagnóstico: imprime TODAS las sesiones que Windows reporta y si Swip las cuenta como
// usuario. Ejecútalo en una consola de administrador:
//   "C:\Program Files\Swip\Service\Swip.Service.exe" --diagnose
if (args.Length >= 1 && args[0] == "--diagnose")
{
    var mgr = new SessionManager(NullLogger.Instance);
    Console.WriteLine(mgr.DiagnoseText());
    return 0;
}

// Modo servicio normal.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Services.AddWindowsService(options => options.ServiceName = "SwipService");

var host = builder.Build();
host.Run();
return 0;
