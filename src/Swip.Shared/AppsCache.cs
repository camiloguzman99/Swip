using System.Collections.Concurrent;

namespace Swip.Shared;

/// <summary>Resultado de consultar la caché de apps de una sesión.</summary>
public enum CacheLookup
{
    /// <summary>Hay una lista válida para devolver.</summary>
    Hit,

    /// <summary>La sesión está en pantalla pero su gato dejó de publicar: la lista no es de fiar.</summary>
    Stale,

    /// <summary>Nunca se publicó nada de esa sesión.</summary>
    Missing,
}

/// <summary>
/// Caché de las apps que publica el gato de cada sesión, indexada por id de sesión.
///
/// Dos reglas deliberadas:
/// 1) Un gato en una sesión que NO está en pantalla no puede ver sus ventanas y publica una lista
///    vacía; esa lista vacía no pisa la última conocida (lo que tenía abierto cuando estuvo en
///    pantalla). Si la sesión SÍ está en pantalla, una lista vacía es real y se guarda.
/// 2) La caducidad solo se exige a la sesión en pantalla (su gato publica cada pocos segundos; si
///    calla es que cerró). A una sesión en segundo plano su gato está pausado a propósito, así que
///    su última lista sigue valiendo hasta que la sesión desaparezca (<see cref="Prune"/>).
/// </summary>
public sealed class AppsCache
{
    private sealed record Entry(DateTime When, IReadOnlyList<AppInfo> Apps);

    private readonly ConcurrentDictionary<int, Entry> _entries = new();
    private readonly TimeSpan _activeFreshness;
    private readonly Func<DateTime> _now;

    public AppsCache(TimeSpan? activeFreshness = null, Func<DateTime>? now = null)
    {
        _activeFreshness = activeFreshness ?? TimeSpan.FromSeconds(60);
        _now = now ?? (() => DateTime.UtcNow);
    }

    /// <summary>Guarda la publicación de una sesión. Devuelve true si se conservó la lista anterior.</summary>
    public bool Publish(int sessionId, IReadOnlyList<AppInfo> apps, bool sessionIsActive)
    {
        if (apps.Count == 0 && !sessionIsActive
            && _entries.TryGetValue(sessionId, out var previous) && previous.Apps.Count > 0)
        {
            _entries[sessionId] = previous with { When = _now() };
            return true;
        }

        _entries[sessionId] = new Entry(_now(), apps);
        return false;
    }

    public CacheLookup TryGet(int sessionId, bool sessionIsActive, out IReadOnlyList<AppInfo> apps)
    {
        apps = Array.Empty<AppInfo>();
        if (!_entries.TryGetValue(sessionId, out var entry))
            return CacheLookup.Missing;

        if (sessionIsActive && _now() - entry.When > _activeFreshness)
            return CacheLookup.Stale;

        apps = entry.Apps;
        return CacheLookup.Hit;
    }

    /// <summary>Olvida las sesiones que ya no existen (un nuevo inicio de sesión tiene otro id).</summary>
    public void Prune(IReadOnlySet<int> liveSessionIds)
    {
        foreach (int id in _entries.Keys)
            if (!liveSessionIds.Contains(id))
                _entries.TryRemove(id, out _);
    }

    public int Count => _entries.Count;
}
