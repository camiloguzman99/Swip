using System.Windows;
using System.Windows.Controls;
using Swip.App.Art;
using Swip.App.World;

namespace Swip.App.Controls;

/// <summary>
/// Vista de un gato basada en sprites de imagen. La ventana le empuja el estado cada tick; pinta.
/// </summary>
public partial class CatSprite : UserControl
{
    private double _baseSize = 64;

    public CatAgent Agent { get; }

    public CatSprite(CatAgent agent, double catSize)
    {
        InitializeComponent();
        Agent = agent;
        NameLabel.Text = agent.DisplayName;
        SetCatSize(catSize);
    }

    /// <summary>Ajusta el tamaño del gato (alto en DIP) y aplica el ensanchado por gordura.</summary>
    public void SetCatSize(double size)
    {
        _baseSize = size;
        ApplyFat();
    }

    public void ApplyFat()
    {
        double height = _baseSize;
        double width = height * (CatSprites.AspectW / CatSprites.AspectH);
        width *= 1.0 + Math.Clamp(Agent.FatLevel, 0, 6) * 0.14;
        Img.Height = height;
        Img.Width = width;
    }

    /// <summary>
    /// Refresca el sprite (acción + frame + color) y el espejo según la dirección. Solo toca lo que
    /// cambió: reasignar propiedades iguales en cada tick invalidaba el dibujo 30 veces por segundo.
    /// </summary>
    public void Render()
    {
        var source = CatSprites.Get(Agent.Color, Agent.Action, Agent.FrameIndex);
        if (!ReferenceEquals(Img.Source, source)) Img.Source = source;

        double flip = Agent.FacingRight ? 1 : -1;
        if (Flip.ScaleX != flip) Flip.ScaleX = flip;

        var heart = Agent.IsPetting ? Visibility.Visible : Visibility.Collapsed;
        if (Heart.Visibility != heart) Heart.Visibility = heart;
    }

    /// <summary>
    /// Zona clicable del gato, en coordenadas de este control: la caja de sus píxeles visibles (no
    /// la imagen entera), colocada donde está dibujada la imagen y teniendo en cuenta el espejo.
    /// </summary>
    public Rect GetHitRect()
    {
        if (Img.Source is null || Img.ActualWidth <= 0 || Img.ActualHeight <= 0)
            return new Rect(0, 0, ActualWidth, ActualHeight);

        var box = SpriteBounds.Opaque(Img.Source);
        // Cuando mira a la izquierda la imagen se dibuja espejada, así que la caja también.
        double x = Agent.FacingRight ? box.X : 1 - box.X - box.Width;
        var origin = Img.TranslatePoint(new Point(0, 0), this);
        return new Rect(
            origin.X + x * Img.ActualWidth, origin.Y + box.Y * Img.ActualHeight,
            box.Width * Img.ActualWidth, box.Height * Img.ActualHeight);
    }

    public void ShowLabel(bool show) =>
        LabelBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
}
