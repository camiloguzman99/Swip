namespace Swip.Shared.Engine;

/// <summary>Temperamento del gato, que viene del estado de la sesión de su usuario.</summary>
public enum CatMode
{
    /// <summary>Sesión cerrada: duerme. Nunca se desplaza por su cuenta.</summary>
    Sleep,

    /// <summary>Sesión abierta en segundo plano: juega en su sitio. Nunca camina.</summary>
    Play,

    /// <summary>Sesión activa (en pantalla): el único que recorre la pantalla y busca la caja.</summary>
    Walk,
}

/// <summary>Dónde está físicamente el gato.</summary>
public enum CatPlace
{
    Ground,
    /// <summary>En el aire: cayendo tras soltarlo, o en mitad de un salto.</summary>
    Air,
    OnBox,
    InBox,
    Carried,
}

/// <summary>Qué movimiento físico acompaña a un comportamiento.</summary>
public enum Motion
{
    None,
    Walk,
    Trot,
    Run,
    /// <summary>Salto en el sitio (desplaza la pose, no el cuerpo).</summary>
    Hop,
    /// <summary>Da vueltas sobre sí mismo (perseguir la cola).</summary>
    Spin,
    /// <summary>Temblor leve (soñar).</summary>
    Jitter,
    TurnAround,
    FaceCursor,
    FollowCursor,
    ApproachCat,
    /// <summary>Se gira hacia el gato dormido más cercano, sin moverse.</summary>
    FaceCat,
    /// <summary>Araña la caja: la mira y la hace tambalear.</summary>
    ScratchBox,
    /// <summary>Va hasta la caja (solo si camina) y se frota contra ella.</summary>
    RubBox,
    PushBox,
    JumpOntoBox,
    JumpIntoBox,
    JumpOffBox,
    CircleBox,
    /// <summary>Escondido en la caja; al final sale de golpe.</summary>
    Ambush,
}

/// <summary>Condiciones del mundo que un comportamiento puede exigir o prohibir.</summary>
[Flags]
public enum Cond
{
    None = 0,
    OnGround = 1 << 0,
    OnBox = 1 << 1,
    InBox = 1 << 2,
    NearBox = 1 << 3,
    BoxOpen = 1 << 4,
    BoxClosed = 1 << 5,
    BoxFree = 1 << 6,
    BoxGrounded = 1 << 7,
    CursorNear = 1 << 8,
    CursorVeryNear = 1 << 9,
    CatNear = 1 << 10,
    CatNearSleeping = 1 << 11,
    CatNearPlaying = 1 << 12,
}

/// <summary>Qué hace que un comportamiento empiece.</summary>
public enum Trigger
{
    /// <summary>Lo elige el planificador al azar (con pesos) cuando termina el anterior.</summary>
    Random,
    Pet,
    Land,
    Attention,
    WakeUp,
    ToBackground,
    ToActive,
    ToSleep,
    Invited,
    Carry,
}

/// <summary>Estilo procedural que se suma a la pose (efectos del motor, sin arte adicional).</summary>
public enum PoseStyle
{
    Normal,
    /// <summary>Respiración: el cuerpo sube y baja muy despacio.</summary>
    Breathe,
    /// <summary>Escondido: la pose baja dentro de la caja.</summary>
    Hidden,
    /// <summary>Balanceo leve (frotarse, empujar).</summary>
    Sway,
}

/// <summary>Efectos de partículas que dibuja la app encima de los gatos.</summary>
public enum EffectKind
{
    Zzz,
    Heart,
    Dust,
    Exclaim,
    Sparkle,
    Fly,
    Note,
    Scratch,
}

/// <summary>Punto del gato del que sale un efecto.</summary>
public enum Anchor
{
    Head,
    Feet,
    /// <summary>Delante de la cara, en la dirección en que mira.</summary>
    Front,
    /// <summary>El lado de la caja que toca el gato.</summary>
    BoxSide,
}

/// <param name="Interval">Segundos entre emisiones; 0 = una sola vez, en <paramref name="At"/>.</param>
/// <param name="At">Para efectos únicos: segundos desde el inicio del comportamiento.</param>
public sealed record EffectSpec(EffectKind Kind, Anchor Anchor, double Interval = 0, double At = 0, int Count = 1);
