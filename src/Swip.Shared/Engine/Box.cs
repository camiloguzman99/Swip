namespace Swip.Shared.Engine;

/// <summary>
/// La caja de cartón: objeto de referencia del mundo. Cae, se desliza al empujarla, se tambalea, abre
/// y cierra sus solapas fotograma a fotograma y se aplasta con el peso del gato que tiene encima.
/// </summary>
public sealed class BoxBody
{
    /// <summary>Fotogramas de las solapas: 0 = cerrada ... LidFrames-1 = abierta (tools/art/box_art.py).</summary>
    public const int LidFrames = 7;

    /// <summary>Filas que se aplasta como mucho (gato gordo y rebote incluidos).</summary>
    public const int MaxSquash = 4;

    /// <summary>Filas que se aplasta con un gato delgado / con uno gordo encima.</summary>
    public const int LightSquash = 2, HeavySquash = 4;

    /// <summary>Inclinación máxima del tambaleo (grados).</summary>
    public const double MaxWobble = 6;
    internal BoxBody(double width, double height, double topInsetClosed, double topInsetOpen)
    {
        Width = width;
        Height = height;
        TopInsetClosed = topInsetClosed;
        TopInsetOpen = topInsetOpen;
    }

    public double X { get; internal set; }
    public double Y { get; internal set; }
    public double Width { get; }
    public double Height { get; }
    public double Vx { get; internal set; }
    public double Vy { get; internal set; }
    /// <summary>Estado pedido: abierta o cerrada. Las solapas llegan a él fotograma a fotograma (<see cref="Lid"/>).</summary>
    public bool Open { get; internal set; }

    /// <summary>Fotograma actual de las solapas (0 = cerrada ... LidFrames-1 = abierta).</summary>
    public int Lid { get; internal set; }

    /// <summary>Filas que está aplastada ahora (0 = sin peso). Se dibuja con su propio fotograma.</summary>
    public int Squash { get; internal set; }

    internal double LidClock, SquashClock, SquashBounce;
    public bool Dragging { get; internal set; }

    /// <summary>Gato dentro o encima (como máximo uno).</summary>
    public string? OccupantId { get; internal set; }

    /// <summary>Inclinación en grados (tambaleo al arañarla o al aterrizar un gato).</summary>
    public double Wobble { get; internal set; }
    internal double WobbleVelocity;

    /// <summary>Fracción del alto del dibujo que es margen transparente arriba, por estado.</summary>
    public double TopInsetClosed { get; }
    public double TopInsetOpen { get; }

    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width / 2;

    /// <summary>
    /// Borde superior VISIBLE: el margen de cada estado y, cerrada, lo que esté aplastada (el gato que
    /// está encima baja con la tapa).
    /// </summary>
    public double VisibleTop => Y + Height * (Open ? TopInsetOpen : TopInsetClosed) + (Open ? 0 : Squash);

    /// <summary>Empujón de tambaleo (grados por segundo). Suave: la mitad del impulso.</summary>
    internal void Shake(double impulse) => WobbleVelocity += impulse * 0.5;
}
