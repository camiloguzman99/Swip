namespace Swip.Shared.Engine;

/// <summary>
/// Un comportamiento del gato: qué clip muestra, en qué temperamentos puede ocurrir, desde qué postura
/// arranca y en cuál termina, qué exige del mundo y qué movimiento hace. Es solo datos; lo ejecuta <see cref="SwipWorld"/>.
/// </summary>
/// <param name="MaxTime">0 = dura una pasada del clip (acciones de una sola vez y transiciones).</param>
/// <param name="From">Postura en la que debe estar para empezar (Any = vale desde cualquiera).</param>
/// <param name="To">Postura en la que queda al terminar (Any = la misma que tenía).</param>
/// <param name="Cooldown">Segundos que deben pasar tras terminar antes de que vuelva a salir al azar.</param>
/// <param name="Active">Gasta energía (correr, saltar, asearse...): tras uno, el gato prefiere descansar.</param>
/// <param name="Reverse">Reproduce el clip al revés (p. ej. de correr a caminar con el clip de caminar a correr).</param>
public sealed record Behavior(
    string Id,
    string Clip,
    CatMode[] Modes,
    string Description,
    Trigger Trigger = Trigger.Random,
    Motion Motion = Motion.None,
    Posture From = Posture.Any,
    Posture To = Posture.Any,
    Cond Needs = Cond.None,
    Cond Forbids = Cond.None,
    double Weight = 1,
    double MinTime = 0,
    double MaxTime = 0,
    double Cooldown = 0,
    bool Active = false,
    bool Reverse = false,
    EffectSpec[]? Effects = null)
{
    public IReadOnlyList<EffectSpec> EffectList => Effects ?? Array.Empty<EffectSpec>();

    /// <summary>Postura en la que queda al terminar, dada la que tenía al empezar.</summary>
    public Posture End(Posture start) => To == Posture.Any ? (From == Posture.Any ? start : From) : To;
}

/// <summary>Un paso del grafo de posturas: con clip dibujado (<see cref="Via"/>) o un corte directo (null).</summary>
public sealed record PostureEdge(Posture From, Posture To, Behavior? Via);

/// <summary>Catálogo completo de comportamientos y grafo de transiciones entre posturas.</summary>
public static class Behaviors
{
    private static readonly CatMode[] S = { CatMode.Sleep };
    private static readonly CatMode[] PW = { CatMode.Play, CatMode.Walk };
    private static readonly CatMode[] W = { CatMode.Walk };
    private static readonly CatMode[] SPW = { CatMode.Sleep, CatMode.Play, CatMode.Walk };

    private const Cond Ground = Cond.OnGround;
    private const Cond BoxReady = Cond.BoxFree | Cond.BoxGrounded;

    private static EffectSpec[] Fx(params EffectSpec[] e) => e;

    private static EffectSpec E(EffectKind kind, Anchor anchor, double interval = 0, double at = 0, int count = 1) =>
        new(kind, anchor, interval, at, count);

    // ---------------- Transiciones: un clip por paso entre dos posturas ----------------

    private static Behavior T(string id, string clip, string text, Posture from, Posture to, bool reverse = false) =>
        new(id, clip, SPW, text, Trigger.Transition, From: from, To: to, Reverse: reverse);

    public static readonly IReadOnlyList<Behavior> Transitions = new Behavior[]
    {
        T("t_wake_up", "wake_up", "Acostado → sentado.", Posture.Loaf, Posture.Sit),
        T("t_to_sleep", "to_sleep", "Sentado → acostado.", Posture.Sit, Posture.Loaf),
        T("t_sit_to_stand", "sit_to_stand", "Sentado → de pie.", Posture.Sit, Posture.Stand),
        T("t_to_sit", "to_sit", "De pie → sentado.", Posture.Stand, Posture.Sit),
        T("t_start", "walk_start_stop", "De pie → caminando.", Posture.Stand, Posture.Walk),
        T("t_stop", "walk_start_stop", "Caminando → de pie.", Posture.Walk, Posture.Stand),
        T("t_walk_to_run", "walk_to_run", "Caminando → corriendo.", Posture.Walk, Posture.Run),
        T("t_run_to_walk", "walk_to_run", "Corriendo → caminando (al revés).", Posture.Run, Posture.Walk, reverse: true),
        T("t_stand_to_run", "stand_to_run", "De pie → corriendo.", Posture.Stand, Posture.Run),
        T("t_run_to_stand", "stand_to_run", "Corriendo → de pie (al revés).", Posture.Run, Posture.Stand, reverse: true),
    };

    /// <summary>
    /// El grafo de posturas. Los pasos sin clip (Via = null) son cortes directos porque todavía no hay arte de la
    /// transición: están en <see cref="Missing"/> y el Visor los enseña como pendientes.
    /// </summary>
    public static readonly IReadOnlyList<PostureEdge> Edges = BuildEdges();

    private static IReadOnlyList<PostureEdge> BuildEdges()
    {
        var edges = new List<PostureEdge>();
        foreach (var t in Transitions) edges.Add(new PostureEdge(t.From, t.To, t));
        void Cut(Posture a, Posture b)
        {
            edges.Add(new PostureEdge(a, b, null));
            edges.Add(new PostureEdge(b, a, null));
        }
        Cut(Posture.Curl, Posture.Side);
        Cut(Posture.Curl, Posture.Loaf);
        Cut(Posture.Side, Posture.Loaf);
        Cut(Posture.Stand, Posture.Trot);
        Cut(Posture.Walk, Posture.Trot);
        Cut(Posture.Trot, Posture.Run);
        return edges;
    }

    /// <summary>Pares de posturas que se unen con un corte porque falta dibujar la transición.</summary>
    public static IEnumerable<(Posture From, Posture To)> Missing =>
        Edges.Where(e => e.Via is null).Select(e => (e.From, e.To));

    /// <summary>
    /// El camino más corto de una postura a otra (búsqueda en anchura). Vacío si ya está o no hay camino.
    /// </summary>
    public static IReadOnlyList<PostureEdge> Path(Posture from, Posture to)
    {
        if (from == to || from == Posture.Any || to == Posture.Any) return Array.Empty<PostureEdge>();
        var prev = new Dictionary<Posture, PostureEdge>();
        var queue = new Queue<Posture>();
        queue.Enqueue(from);
        var seen = new HashSet<Posture> { from };
        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            if (at == to) break;
            foreach (var e in Edges)
            {
                if (e.From != at || !seen.Add(e.To)) continue;
                prev[e.To] = e;
                queue.Enqueue(e.To);
            }
        }
        if (!prev.ContainsKey(to)) return Array.Empty<PostureEdge>();
        var path = new List<PostureEdge>();
        for (var p = to; p != from; p = prev[p].From) path.Add(prev[p]);
        path.Reverse();
        return path;
    }

    // ---------------- Acciones ----------------

    public static readonly IReadOnlyList<Behavior> Actions = new Behavior[]
    {
        // Dormir (sesión cerrada): nunca se desplaza, largos ratos.
        new("sleep_curl", "sleep_curl", S, "Duerme en bolita.", From: Posture.Curl,
            Weight: 5, MinTime: 25, MaxTime: 70, Effects: Fx(E(EffectKind.Zzz, Anchor.Head, interval: 1.8))),
        new("sleep_side", "sleep_side", S, "Duerme de lado.", From: Posture.Side,
            Weight: 3, MinTime: 25, MaxTime: 70, Effects: Fx(E(EffectKind.Zzz, Anchor.Head, interval: 2.0))),

        // Reposo despierto: quieto, en su sitio y por largos ratos.
        new("sit", "sit_look", PW, "Sentado, mirando alrededor.", From: Posture.Sit, Weight: 5, MinTime: 10, MaxTime: 30),
        new("loaf", "loaf_idle", PW, "Acostado en pan, descansando.", From: Posture.Loaf, Weight: 2.5, MinTime: 15, MaxTime: 40),
        new("stand", "stand_idle", PW, "De pie, quieto.", From: Posture.Stand, Weight: 2, MinTime: 4, MaxTime: 10),
        new("groom", "groom_paw", PW, "Sentado, se lame la pata.", From: Posture.Sit, Weight: 2,
            MinTime: 5, MaxTime: 10, Cooldown: 60, Active: true),

        // Moverse (solo la sesión activa recorre la pantalla).
        new("walk", "walk", W, "Camina.", Motion: Motion.Walk, From: Posture.Walk, Needs: Ground,
            Weight: 5, MinTime: 6, MaxTime: 14),
        new("trot", "trot", W, "Trota.", Motion: Motion.Trot, From: Posture.Trot, Needs: Ground,
            Weight: 1, MinTime: 3, MaxTime: 6, Cooldown: 30, Active: true),
        new("run", "run", W, "Corre una ráfaga corta.", Motion: Motion.Run, From: Posture.Run, Needs: Ground,
            Weight: 0.5, MinTime: 1.5, MaxTime: 3, Cooldown: 60, Active: true,
            Effects: Fx(E(EffectKind.Dust, Anchor.Feet, interval: 0.15))),
        new("leap", "jump", W, "Salta al mismo nivel.", Motion: Motion.Leap, From: Posture.Stand, To: Posture.Stand,
            Needs: Ground, Weight: 0.5, Cooldown: 50, Active: true),

        // Caja.
        new("box_push", "box_push", W, "Choca con la caja y la empuja.", Motion: Motion.PushBox, From: Posture.Stand,
            To: Posture.Stand, Needs: Ground | BoxReady, Weight: 1, MinTime: 30, MaxTime: 30, Cooldown: 75, Active: true),
        new("box_jump_on", "jump_box", W, "Salta encima de la caja cerrada.", Motion: Motion.JumpOntoBox,
            From: Posture.Stand, To: Posture.Sit, Needs: Ground | BoxReady | Cond.BoxClosed, Weight: 1, MinTime: 30, MaxTime: 30,
            Cooldown: 75, Active: true),
        new("box_jump_in", "box_enter", W, "Se mete en la caja abierta.", Motion: Motion.JumpIntoBox,
            From: Posture.Stand, To: Posture.Sit, Needs: Ground | BoxReady | Cond.BoxOpen, Weight: 1.2, MinTime: 30, MaxTime: 30,
            Cooldown: 75, Active: true),
        new("box_exit_top", "box_exit", W, "Baja de la caja de un salto.", Motion: Motion.JumpOffBox,
            From: Posture.Stand, To: Posture.Stand, Needs: Cond.OnBox, Weight: 0.6, MaxTime: 3, Cooldown: 20, Active: true),
        new("box_exit_in", "box_exit", W, "Sale de la caja.", Motion: Motion.JumpOffBox,
            From: Posture.Stand, To: Posture.Stand, Needs: Cond.InBox, Weight: 0.6, MaxTime: 3, Cooldown: 20, Active: true),
        new("box_exit_forced", "box_exit", PW, "Sale de la caja porque la van a cerrar.", Motion: Motion.JumpOffBox,
            From: Posture.Stand, To: Posture.Stand, Needs: Cond.InBox, Weight: 0, MaxTime: 3),
    };

    // ---------------- Reacciones que no salen al azar ----------------

    public static readonly IReadOnlyList<Behavior> Reactions = new Behavior[]
    {
        new("land", "land", SPW, "Aterriza tras caer.", Trigger.Land, To: Posture.Stand,
            Effects: Fx(E(EffectKind.Dust, Anchor.Feet, at: 0, count: 4))),
        new("attention", "sit_look", PW, "Te mira mientras su menú está abierto.", Trigger.Attention,
            Motion: Motion.FaceCursor, From: Posture.Sit, MinTime: 9999, MaxTime: 9999),
        new("carry", "carry", SPW, "Colgando mientras lo arrastras.", Trigger.Carry, To: Posture.Stand,
            MinTime: 9999, MaxTime: 9999),
    };

    public static readonly IReadOnlyList<Behavior> All = Actions.Concat(Reactions).Concat(Transitions).ToArray();

    public static Behavior Get(string id) => All.First(b => b.Id == id);

    /// <summary>La reacción de un disparador para un temperamento, si existe.</summary>
    public static Behavior? ForTrigger(Trigger trigger, CatMode mode) =>
        Reactions.FirstOrDefault(b => b.Trigger == trigger && b.Modes.Contains(mode));
}
