using System.Windows;
using System.Windows.Controls;
using Swip.App.Art;
using Swip.App.World;
using Swip.Shared;

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
        var fallback = new Rect(0, 0, ActualWidth, ActualHeight);
        if (Img.Source is null || Img.ActualWidth <= 0 || Img.ActualHeight <= 0)
            return fallback;

        Rect image;
        try
        {
            // OJO: Img.TranslatePoint(0,0) NO sirve: incluye el espejo (ScaleTransform), de modo que
            // con el gato mirando a la izquierda devolvía el borde DERECHO y la zona de clic quedaba
            // desplazada un ancho de gato fuera de la figura. TransformBounds da el mismo rectángulo
            // espejado o no (el espejo es alrededor del centro).
            image = Img.TransformToAncestor(this)
                .TransformBounds(new Rect(0, 0, Img.ActualWidth, Img.ActualHeight));
        }
        catch (InvalidOperationException)
        {
            return fallback; // aún no está en el árbol visual
        }

        var opaque = SpriteBounds.Opaque(Img.Source);
        var area = HitGeometry.HitArea(
            new Area(image.X, image.Y, image.Width, image.Height),
            new Area(opaque.X, opaque.Y, opaque.Width, opaque.Height),
            mirrored: Flip.ScaleX < 0); // lo que realmente está dibujado, no el modelo
        return new Rect(area.X, area.Y, area.Width, area.Height);
    }

    public void ShowLabel(bool show) =>
        LabelBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
}
