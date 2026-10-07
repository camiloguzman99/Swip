using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Swip.Service.Native;

/// <summary>P/Invoke para saber de qué sesión de Windows viene un cliente del pipe.</summary>
internal static class PipeInterop
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientSessionId(SafePipeHandle pipe, out uint clientSessionId);

    /// <summary>
    /// Sesión REAL del proceso que está conectado al pipe (la reporta Windows, el cliente no puede
    /// falsearla). Null si no se pudo determinar.
    /// </summary>
    public static int? ClientSessionId(NamedPipeServerStream server) =>
        GetNamedPipeClientSessionId(server.SafePipeHandle, out uint id) ? (int)id : null;
}
