namespace Swip.Shared.Engine;

/// <summary>
/// Un comportamiento del gato: qué clip muestra, en qué temperamentos puede ocurrir, qué exige del
/// mundo, qué movimiento hace y qué efectos lanza. Es solo datos; lo ejecuta <see cref="SwipWorld"/>.
/// </summary>
/// <param name="MaxTime">0 = dura una pasada del clip (acciones de una sola vez).</param>
public sealed record Behavior(
    string Id,
    string Clip,
    CatMode[] Modes,
    string Description,
    Trigger Trigger = Trigger.Random,
    Motion Motion = Motion.None,
    Cond Needs = Cond.None,
    Cond Forbids = Cond.None,
    double Weight = 1,
    double MinTime = 0,
    double MaxTime = 0,
    PoseStyle Style = PoseStyle.Normal,
    EffectSpec[]? Effects = null)
{
    public IReadOnlyList<EffectSpec> EffectList => Effects ?? Array.Empty<EffectSpec>();
}

/// <summary>Catálogo completo de comportamientos planificados.</summary>
public static class Behaviors
{
    private static readonly CatMode[] S = { CatMode.Sleep };
    private static readonly CatMode[] P = { CatMode.Play };
    private static readonly CatMode[] W = { CatMode.Walk };
    private static readonly CatMode[] PW = { CatMode.Play, CatMode.Walk };
    private static readonly CatMode[] SPW = { CatMode.Sleep, CatMode.Play, CatMode.Walk };

    private const Cond Ground = Cond.OnGround;
    private const Cond BoxReady = Cond.BoxFree | Cond.BoxGrounded;

    private static EffectSpec[] Fx(params EffectSpec[] e) => e;

    private static EffectSpec E(EffectKind kind, Anchor anchor, double interval = 0, double at = 0, int count = 1) =>
        new(kind, anchor, interval, at, count);

    public static readonly IReadOnlyList<Behavior> All = new Behavior[]
    {
        // ---------------- Durmiendo (sesión cerrada): nunca se desplaza ----------------
        new("sleep_curl", "sleep_curl", S, "Duerme enroscado.", Forbids: Cond.InBox,
            Weight: 6, MinTime: 6, MaxTime: 14, Style: PoseStyle.Breathe,
            Effects: Fx(E(EffectKind.Zzz, Anchor.Head, interval: 1.6))),
        new("sleep_turn", "sleep_turn", S, "Se da la vuelta dormido.", Motion: Motion.TurnAround,
            Forbids: Cond.InBox),
        new("sleep_stretch", "sleep_stretch", S, "Se estira sin despertar.", Forbids: Cond.InBox),
        new("sleep_ear", "sleep_ear", S, "Mueve una oreja.", Forbids: Cond.InBox, Weight: 1.2),
        new("sleep_tail", "sleep_tail", S, "Sacude la cola.", Forbids: Cond.InBox, Weight: 1.2),
        new("sleep_dream", "sleep_dream", S, "Sueña: tiembla un poco.", Motion: Motion.Jitter,
            Forbids: Cond.InBox, MinTime: 3, MaxTime: 6,
            Effects: Fx(E(EffectKind.Zzz, Anchor.Head, interval: 1.0))),
        new("sleep_in_box", "sleep_box", S, "Duerme dentro de la caja abierta.", Needs: Cond.InBox,
            Weight: 6, MinTime: 6, MaxTime: 14, Style: PoseStyle.Breathe,
            Effects: Fx(E(EffectKind.Zzz, Anchor.Head, interval: 1.8))),
        new("sleep_snuggle", "sleep_curl", S, "Se acurruca mirando a otro gato dormido.",
            Motion: Motion.FaceCat, Needs: Ground | Cond.CatNearSleeping, Weight: 3, MinTime: 8, MaxTime: 14,
            Style: PoseStyle.Breathe,
            Effects: Fx(E(EffectKind.Heart, Anchor.Head, at: 0.6), E(EffectKind.Zzz, Anchor.Head, interval: 1.6))),
        new("sleep_petted", "sleep_petted", S, "Al acariciarlo abre un ojo, bosteza y sigue durmiendo.",
            Trigger: Trigger.Pet, Effects: Fx(E(EffectKind.Heart, Anchor.Head, at: 0))),

        // ---------------- Jugando en su sitio (segundo plano): nunca camina ----------------
        new("play_yarn", "play_yarn", P, "Juega con el ovillo.", Forbids: Cond.InBox,
            Weight: 5, MinTime: 5, MaxTime: 10),
        new("play_tail_chase", "play_tail_chase", P, "Persigue su cola dando vueltas.", Motion: Motion.Spin,
            Forbids: Cond.InBox, Weight: 1.5, MinTime: 2.5, MaxTime: 4),
        new("play_hunt_fly", "play_hunt_fly", P, "Caza una mosca imaginaria.", Forbids: Cond.InBox,
            Weight: 1.5, MinTime: 3, MaxTime: 5, Effects: Fx(E(EffectKind.Fly, Anchor.Head, at: 0))),
        new("play_hop", "play_hop", P, "Salta en el sitio.", Motion: Motion.Hop, Forbids: Cond.InBox,
            Weight: 1.5),
        new("play_roll", "play_roll", P, "Se revuelca panza arriba.", Needs: Ground, Weight: 0.8),
        new("groom_paw", "groom_paw", PW, "Se lame la pata.", Forbids: Cond.InBox,
            Weight: 1.5, MinTime: 3, MaxTime: 5),
        new("groom_face", "groom_face", PW, "Se limpia la cara.", Forbids: Cond.InBox,
            Weight: 1.5, MinTime: 3, MaxTime: 5),
        new("sit_look", "sit_look", PW, "Sentado, mirando alrededor.", Forbids: Cond.InBox,
            Weight: 1.5, MinTime: 3, MaxTime: 6),
        new("yawn", "yawn", PW, "Bosteza.", Forbids: Cond.InBox),
        new("knead", "knead", P, "Amasa ronroneando.", Forbids: Cond.InBox, MinTime: 3, MaxTime: 5,
            Effects: Fx(E(EffectKind.Note, Anchor.Head, interval: 1.5))),

        // ---------------- Caminando (sesión activa): recorre la pantalla ----------------
        new("walk", "walk", W, "Camina.", Motion: Motion.Walk, Needs: Ground, Weight: 5, MinTime: 3, MaxTime: 7),
        new("trot", "trot", W, "Trota.", Motion: Motion.Trot, Needs: Ground, Weight: 2, MinTime: 2, MaxTime: 4),
        new("run", "run", W, "Corre una ráfaga corta.", Motion: Motion.Run, Needs: Ground,
            MinTime: 1, MaxTime: 2, Effects: Fx(E(EffectKind.Dust, Anchor.Feet, interval: 0.15))),
        new("sniff", "sniff", W, "Olfatea el suelo.", Needs: Ground, Weight: 1.5, MinTime: 2, MaxTime: 3.5),
        new("stretch_yoga", "stretch_yoga", W, "Se estira.", Needs: Ground),
        new("turn_around", "turn_around", W, "Media vuelta.", Motion: Motion.TurnAround, Needs: Ground),

        // ---------------- Caja ----------------
        new("box_push", "box_push", W, "Empuja la caja.", Motion: Motion.PushBox, Needs: Ground | BoxReady,
            Weight: 1.5, MaxTime: 7),
        new("box_jump_on", "jump", W, "Salta encima de la caja cerrada.", Motion: Motion.JumpOntoBox,
            Needs: Ground | BoxReady | Cond.BoxClosed, Weight: 1.2, MaxTime: 8),
        new("box_jump_in", "jump", W, "Se mete de un salto en la caja abierta.", Motion: Motion.JumpIntoBox,
            Needs: Ground | BoxReady | Cond.BoxOpen, Weight: 1.5, MaxTime: 8),
        new("box_exit_top", "jump", W, "Baja de la caja de un salto.", Motion: Motion.JumpOffBox,
            Needs: Cond.OnBox, MaxTime: 3),
        new("box_exit_in", "jump", W, "Sale de la caja de un salto.", Motion: Motion.JumpOffBox,
            Needs: Cond.InBox, MaxTime: 3),
        new("box_rub", "box_rub", W, "Se frota contra la caja.", Motion: Motion.RubBox,
            Needs: Ground | Cond.BoxGrounded, MinTime: 2, MaxTime: 4, Style: PoseStyle.Sway,
            Effects: Fx(E(EffectKind.Heart, Anchor.Head, at: 1.2))),
        new("box_circle", "walk", W, "Rodea la caja.", Motion: Motion.CircleBox,
            Needs: Ground | Cond.BoxGrounded, Weight: 0.8, MaxTime: 10),
        new("box_scratch", "box_scratch", PW, "Araña el cartón.", Motion: Motion.ScratchBox,
            Needs: Ground | Cond.NearBox | Cond.BoxGrounded, MinTime: 2, MaxTime: 3.5,
            Effects: Fx(E(EffectKind.Scratch, Anchor.BoxSide, interval: 0.35))),
        new("box_peek", "box_peek", PW, "Dentro de la caja: se asoma y se esconde.", Needs: Cond.InBox,
            Weight: 4, MinTime: 3, MaxTime: 6),
        new("box_paw_out", "box_paw_out", PW, "Saca una pata por el borde de la caja.", Needs: Cond.InBox,
            Weight: 2, MinTime: 2, MaxTime: 4),
        new("box_ambush", "ambush_pop", W, "Se esconde en la caja y sale de golpe.", Motion: Motion.Ambush,
            Needs: Cond.InBox, Weight: 1.5, MinTime: 3, MaxTime: 6),
        new("box_sit_watch", "sit_look", PW, "Sentado encima de la caja, vigilando.", Needs: Cond.OnBox,
            Weight: 4, MinTime: 4, MaxTime: 8),

        // ---------------- Cursor ----------------
        new("cursor_watch", "sit_look", PW, "Mira el cursor.", Motion: Motion.FaceCursor, Needs: Cond.CursorNear,
            Weight: 3, MinTime: 2, MaxTime: 4),
        new("cursor_follow", "trot", W, "Sigue al cursor.", Motion: Motion.FollowCursor,
            Needs: Ground | Cond.CursorNear, Weight: 2, MaxTime: 4),
        new("cursor_swat", "cursor_swat", PW, "Lanza un zarpazo al cursor.", Motion: Motion.FaceCursor,
            Needs: Cond.CursorVeryNear, Forbids: Cond.InBox, Weight: 4),

        // ---------------- Entre gatos ----------------
        new("cats_sniff", "sniff", W, "Se acerca a otro gato y lo olfatea.", Motion: Motion.ApproachCat,
            Needs: Ground | Cond.CatNear, Weight: 2, MaxTime: 5,
            Effects: Fx(E(EffectKind.Heart, Anchor.Head, at: 2.5))),
        new("cats_invite", "play_hop", W, "Invita a jugar a un gato que está jugando.", Motion: Motion.Hop,
            Needs: Ground | Cond.CatNearPlaying, Weight: 2,
            Effects: Fx(E(EffectKind.Note, Anchor.Head, at: 0.1))),

        // ---------------- Reacciones y transiciones ----------------
        new("petted_happy", "petted_happy", PW, "Contento al acariciarlo.", Trigger: Trigger.Pet,
            Effects: Fx(E(EffectKind.Heart, Anchor.Head, at: 0.05), E(EffectKind.Heart, Anchor.Head, at: 0.3))),
        new("play_invited", "play_hop", P, "Acepta la invitación y salta.", Trigger: Trigger.Invited,
            Motion: Motion.Hop, Effects: Fx(E(EffectKind.Note, Anchor.Head, at: 0.1))),
        new("wake_up", "wake_up", PW, "Se despierta y se estira.", Trigger: Trigger.WakeUp,
            Effects: Fx(E(EffectKind.Sparkle, Anchor.Head, at: 0.3))),
        new("to_sit", "to_sit", P, "Deja de caminar y se sienta.", Trigger: Trigger.ToBackground),
        new("to_active", "stretch_yoga", W, "Se levanta y se estira para caminar.", Trigger: Trigger.ToActive),
        new("to_sleep", "to_sleep", S, "Bosteza, da una vuelta y se enrosca.", Trigger: Trigger.ToSleep,
            Effects: Fx(E(EffectKind.Zzz, Anchor.Head, at: 1.0))),
        new("land", "land", SPW, "Aterriza tras caer.", Trigger: Trigger.Land,
            Effects: Fx(E(EffectKind.Dust, Anchor.Feet, at: 0, count: 4))),
        new("attention", "sit_look", PW, "Te mira mientras su menú está abierto.", Trigger: Trigger.Attention,
            Motion: Motion.FaceCursor, MinTime: 9999, MaxTime: 9999),
        new("carry", "carry", SPW, "Colgando mientras lo arrastras.", Trigger: Trigger.Carry,
            MinTime: 9999, MaxTime: 9999),
    };

    public static Behavior Get(string id) => All.First(b => b.Id == id);

    /// <summary>El comportamiento de un disparador para un temperamento, si existe.</summary>
    public static Behavior? ForTrigger(Trigger trigger, CatMode mode) =>
        All.FirstOrDefault(b => b.Trigger == trigger && b.Modes.Contains(mode));
}
