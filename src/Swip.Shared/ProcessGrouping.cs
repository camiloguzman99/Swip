namespace Swip.Shared;

/// <summary>Un proceso de la tabla del sistema. <paramref name="StartTicks"/> = 0 si no se pudo leer.</summary>
public readonly record struct ProcInfo(int Pid, int ParentPid, string Name, long StartTicks);

/// <summary>
/// Reparte los procesos entre las apps abiertas como lo hace el Administrador de tareas: una app es
/// sus procesos con ese nombre MÁS los que cuelgan de ellos. Contar solo por nombre dejaba fuera a
/// los ayudantes con otro nombre: las apps basadas en WebView2 (el Outlook nuevo, WhatsApp)
/// delegan casi todo su trabajo en procesos "msedgewebview2", y por eso salían al 0%.
/// </summary>
public static class ProcessGrouping
{
    private const int MaxDepth = 32;

    // explorer.exe es el padre de casi todo lo que el usuario abre desde el menú Inicio o el
    // escritorio; si absorbiera a sus hijos, el Explorador se llevaría el consumo de todas las apps.
    private static readonly HashSet<string> NeverAbsorbChildren = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer",
    };

    /// <returns>Para cada app (con el nombre tal como se pasó en <paramref name="appNames"/>), los PID que le pertenecen.</returns>
    public static Dictionary<string, List<int>> Group(
        IReadOnlyCollection<ProcInfo> procs, IReadOnlyCollection<string> appNames)
    {
        var canonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in appNames) canonical[name] = name;

        var result = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in appNames) result[name] = new List<int>();

        var byPid = new Dictionary<int, ProcInfo>();
        foreach (var p in procs) byPid[p.Pid] = p;

        // owner[pid] = app a la que pertenece (null = a ninguna). Sirve también de memoria.
        var owner = new Dictionary<int, string?>();
        var roots = new HashSet<int>();
        foreach (var p in procs)
        {
            if (canonical.TryGetValue(p.Name, out var app))
            {
                owner[p.Pid] = app;
                roots.Add(p.Pid);
            }
        }

        foreach (var p in procs)
        {
            string? app = roots.Contains(p.Pid) ? owner[p.Pid] : Resolve(p.Pid);
            if (app is not null) result[app].Add(p.Pid);
        }
        return result;

        string? Resolve(int pid)
        {
            var path = new List<int>();
            string? found = null;
            int current = pid;

            for (int depth = 0; depth < MaxDepth; depth++)
            {
                if (owner.TryGetValue(current, out var known)) { found = known; break; }
                if (!byPid.TryGetValue(current, out var proc)) break;
                path.Add(current);

                // Subir al padre solo si existe y es creíble.
                if (proc.ParentPid <= 0 || proc.ParentPid == current) break;
                if (!byPid.TryGetValue(proc.ParentPid, out var parent)) break;
                // Un PID se reutiliza: si el "padre" empezó DESPUÉS que el hijo, no es su padre real.
                if (proc.StartTicks > 0 && parent.StartTicks > proc.StartTicks) break;
                current = parent.Pid;
            }

            // El nearest ancestro que es una app absorbe al proceso, salvo el shell.
            if (found is not null && NeverAbsorbChildren.Contains(found)) found = null;

            foreach (int visited in path) owner[visited] = found; // memoria para los siguientes
            return found;
        }
    }
}
