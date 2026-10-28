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
    /// <summary>Se gira hacia el cursor, sin moverse.</summary>
    FaceCursor,
    /// <summary>Salto al mismo nivel: un brinco hacia delante por el suelo.</summary>
    Leap,
    /// <summary>Se acerca a la caja, choca con ella y la empuja.</summary>
    PushBox,
    JumpOntoBox,
    JumpIntoBox,
    JumpOffBox,
}

/// <summary>
/// La postura del cuerpo. Entre dos posturas distintas el gato no cambia de golpe: pasa por la cadena de
/// transiciones del grafo (<see cref="Behaviors.Path"/>), igual que un gato de verdad no pasa de dormir a correr.
/// </summary>
public enum Posture
{
    /// <summary>Sin postura propia: el comportamiento vale desde cualquiera y no la cambia.</summary>
    Any,
    /// <summary>Durmiendo en bolita.</summary>
    Curl,
    /// <summary>Durmiendo de lado.</summary>
    Side,
    /// <summary>Acostado en pan (despierto).</summary>
    Loaf,
    Sit,
    Stand,
    Walk,
    Trot,
    Run,
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
}

/// <summary>Qué hace que un comportamiento empiece.</summary>
public enum Trigger
{
    /// <summary>Lo elige el planificador al azar (con pesos) cuando termina el anterior.</summary>
    Random,
    /// <summary>Paso intermedio entre dos posturas: solo lo programa el planificador.</summary>
    Transition,
    Land,
    Attention,
    Carry,
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
