namespace Swip.Shared.Engine;

/// <summary>La caja de cartón: objeto de referencia del mundo. Cae, se desliza al empujarla y se tambalea.</summary>
public sealed class BoxBody
{
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
    public bool Open { get; internal set; }
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

    /// <summary>Borde superior VISIBLE (los PNG cerrado y abierto tienen distinto margen arriba).</summary>
    public double VisibleTop => Y + Height * (Open ? TopInsetOpen : TopInsetClosed);

    /// <summary>Empujón de tambaleo (grados por segundo).</summary>
    internal void Shake(double impulse) => WobbleVelocity += impulse;
}
