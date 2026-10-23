namespace Swip.Shared.Engine;

/// <summary>
/// Lo que la app debe dibujar de un gato en este instante: clip y fotograma, más los efectos de
/// movimiento del motor (aplastar/estirar, giro, desplazamientos, sombra).
/// </summary>
/// <param name="ScaleX">Escala horizontal con los pies como pivote (aplastar/estirar).</param>
/// <param name="Rotation">Grados, con los pies como pivote.</param>
/// <param name="OffsetY">Desplazamiento visual (negativo = arriba), p. ej. un salto en el sitio.</param>
/// <param name="ShadowDrop">Píxeles entre los pies (con el salto) y la superficie de debajo; -1 = sin sombra.</param>
/// <param name="BehindBox">True si debe dibujarse detrás de la caja (dentro de ella).</param>
public readonly record struct CatPose(
    string Clip, int Frame, bool FacingRight,
    double ScaleX, double ScaleY, double Rotation, double OffsetX, double OffsetY,
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
    public double Width { get; }
    public double Height { get; }

    public CatMode Mode { get; internal set; }
    public CatPlace Place { get; internal set; } = CatPlace.Ground;

    /// <summary>Esquina superior izquierda del dibujo.</summary>
    public double X { get; internal set; }
    public double Y { get; internal set; }
    public double Vy { get; internal set; }
    public bool FacingRight { get; internal set; } = true;

    public double CenterX => X + Width / 2;
    public double Bottom => Y + Height;

    public Behavior Current { get; internal set; } = Behaviors.Get("sleep_curl");
    /// <summary>Segundos desde que empezó el comportamiento actual.</summary>
    public double BehaviorTime { get; internal set; }
    public double BehaviorDuration { get; internal set; }

    public CatPose Pose { get; internal set; }

    /// <summary>True mientras su menú está abierto (quiere atención, salvo dormido).</summary>
    public bool Attention { get; internal set; }

    // ---- estado interno del cerebro y del movimiento ----
    internal Trigger? Pending;
    internal Behavior? Last;
    internal bool ModeInitialized;
    internal bool Finished;          // el movimiento declaró el comportamiento terminado
    internal int Phase;              // fase del movimiento (acercarse, saltar, empujar...)
    internal double PhaseTime;
    internal double StartX, StartY, TargetX, TargetY;
    internal double Speed;           // velocidad horizontal de este paso (para el clip)
    internal int Direction = 1;
    internal string? ClipOverride;   // clip de la fase (p. ej. trotar mientras se acerca a la caja)
    internal string? PartnerId;      // gato con el que interactúa
    internal double Squash;          // 0..1, se desvanece (aterrizajes)
    internal double[] NextEmit = Array.Empty<double>();
    internal bool[] Emitted = Array.Empty<bool>();
    internal CatPlace JumpTargetPlace;
}
