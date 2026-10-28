namespace Swip.Shared.Engine;

/// <summary>
/// Lo que la app debe dibujar de un gato en este instante: clip y fotograma, espejo y desplazamientos.
/// El dibujo nunca se deforma (ni aplastar, ni estirar, ni girar): todo el movimiento del cuerpo está
/// en los fotogramas de la cuadrícula, y el gato solo se desplaza.
/// </summary>
/// <param name="OffsetY">Desplazamiento visual (negativo = arriba), p. ej. un salto en el sitio.</param>
/// <param name="ShadowDrop">Píxeles entre los pies (con el salto) y la superficie de debajo; -1 = sin sombra.</param>
/// <param name="BehindBox">True si debe dibujarse detrás de la caja (dentro de ella).</param>
public readonly record struct CatPose(
    string Clip, int Frame, bool FacingRight,
    double OffsetX, double OffsetY,
    double ShadowDrop, bool BehindBox);

/// <summary>Un gato del mundo: cuerpo físico + cerebro (comportamiento actual) + pose a dibujar.</summary>
public sealed class Cat
{
    internal Cat(string id, double width, double height)
    {
        Id = id;
        Width = width;
        Height = height;
    }

    public string Id { get; }
    public double Width { get; internal set; }
    public double Height { get; internal set; }

    public CatMode Mode { get; internal set; }
    public CatPlace Place { get; internal set; } = CatPlace.Ground;

    /// <summary>Esquina superior izquierda del dibujo.</summary>
    public double X { get; internal set; }
    public double Y { get; internal set; }
    public double Vy { get; internal set; }
    public bool FacingRight { get; internal set; } = true;

    public double CenterX => X + Width / 2;
    public double Bottom => Y + Height;

    /// <summary>Postura en la que termina el comportamiento actual (la de partida para el siguiente).</summary>
    public Posture Posture { get; internal set; }

    public Behavior Current { get; internal set; } = Behaviors.Get("sleep_curl");
    /// <summary>Segundos desde que empezó el comportamiento actual.</summary>
    public double BehaviorTime { get; internal set; }
    public double BehaviorDuration { get; internal set; }

    public CatPose Pose { get; internal set; }

    /// <summary>True mientras su menú está abierto (quiere atención, salvo dormido).</summary>
    public bool Attention { get; internal set; }

    /// <summary>Gato gordo (aplasta más la caja al subirse) o delgado.</summary>
    public bool Heavy { get; internal set; }

    // ---- estado interno del cerebro y del movimiento ----
    internal Trigger? Pending;
    internal Behavior? Last;                 // la última acción al azar (para no repetirla seguido)
    internal Posture PrevPosture;            // la que tenía al empezarlo
    internal readonly Queue<Behavior> Queue = new();   // cadena de transiciones pendiente hasta la acción elegida
    internal readonly Dictionary<string, double> Ready = new();   // enfriamientos: id → instante en que puede repetirse
    internal bool ModeDirty;                 // cambió el estado de la sesión: replantear qué hacer
    internal bool ForceExit;                 // hay que cerrar la caja: tiene que salir de ella
    internal bool ModeInitialized;
    internal bool Finished;          // el movimiento declaró el comportamiento terminado
    internal int Phase;              // fase del movimiento (acercarse, saltar, empujar...)
    internal double PhaseTime;
    internal double StartX, StartY, TargetX, TargetY;
    internal double Speed;           // velocidad horizontal nominal mientras se desplaza (para el polvo)
    internal string? StepClip;       // clip y cambio de fotograma del último avance: el gato solo
    internal int StepTick;           // se desplaza cuando cambia el fotograma (ver SwipWorld.FrameStep)
    internal int Direction = 1;
    internal string? ClipOverride;   // clip de la fase (p. ej. trotar mientras se acerca a la caja)
        internal double[] NextEmit = Array.Empty<double>();
    internal bool[] Emitted = Array.Empty<bool>();
    internal CatPlace JumpTargetPlace;
}
