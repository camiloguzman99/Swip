using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Swip.Service;

// Modo ayudante: lanzado por el propio servicio dentro de la sesión objetivo para enumerar
// sus ventanas. No arranca el host del servicio; enumera, imprime JSON a un archivo y sale.
if (args.Length >= 2 && args[0] == "--enumerate-windows")
{
    var apps = WindowEnumeratorFile.Run(args[1]);
    return apps;
}

// Modo servicio normal.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Services.AddWindowsService(options => options.ServiceName = "SwipService");

var host = builder.Build();
host.Run();
return 0;
