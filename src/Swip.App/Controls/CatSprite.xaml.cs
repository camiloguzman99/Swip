using System.Windows;
using System.Windows.Controls;
using Swip.App.Art;
using Swip.App.World;
using Swip.Shared;
using Swip.Shared.Engine;

namespace Swip.App.Controls;

/// <summary>
/// Vista de un gato. El motor (<see cref="SwipWorld"/>) decide todo; este control solo dibuja la
/// pose: fotograma del atlas con la paleta del gato, efectos de movimiento, sombra y rótulo.
/// </summary>
public partial class CatSprite : UserControl
{
    private readonly CatAtlas _atlas;
    private double _baseSize = 48;

    // Lo último que se dibujó, para no reasignar propiedades iguales en cada fotograma.
    private string? _labelClip, _labelColor;
    private CatPose _lastPose;

    public CatAgent Agent { get; }

    public CatSprite(CatAgent agent, CatAtlas atlas, double catSize)
    {
        InitializeComponent();
        Agent = agent;
        _atlas = atlas;
        NameLabel.Text = LabelFormat.Split(agent.DisplayName); // dos palabras = dos filas
        LabelBox.SizeChanged += (_, _) => PositionLabel(_lastPose);
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
        double width = height * _atlas.Aspect;
        width *= 1.0 + Math.Clamp(Agent.FatLevel, 0, 6) * 0.14;
        Img.Height = height;
        Img.Width = width;
    }

    /// <summary>Dibuja la pose actual del gato (se llama en cada fotograma).</summary>
    public void Render()
    {
        if (Agent.Body is not { } cat) return;
        var pose = cat.Pose;

        var source = _atlas.Frame(Agent.Color, pose.Clip, pose.Frame);
        if (!ReferenceEquals(Img.Source, source)) Img.Source = source;

        double flip = pose.FacingRight ? 1 : -1;
        if (Flip.ScaleX != flip) Flip.ScaleX = flip;
        if (Squash.ScaleX != pose.ScaleX) Squash.ScaleX = pose.ScaleX;
        if (Squash.ScaleY != pose.ScaleY) Squash.ScaleY = pose.ScaleY;
        if (Tilt.Angle != pose.Rotation) Tilt.Angle = pose.Rotation;
        if (Shift.X != pose.OffsetX) Shift.X = pose.OffsetX;
        if (Shift.Y != pose.OffsetY) Shift.Y = pose.OffsetY;

        // Sombra: en la superficie (suelo o tapa de la caja), más pequeña y tenue cuanto más arriba.
        if (pose.ShadowDrop < 0)
        {
            Shadow.Visibility = Visibility.Collapsed;
        }
        else
        {
            Shadow.Visibility = Visibility.Visible;
            double k = 1 / (1 + pose.ShadowDrop / 40);
            ShadowScale.ScaleX = ShadowScale.ScaleY = k;
            ShadowShift.Y = pose.ShadowDrop;
            Shadow.Opacity = 0.25 * k;
        }

        if (_labelClip != pose.Clip || _labelColor != Agent.Color || _lastPose.OffsetY != pose.OffsetY)
        {
            _labelClip = pose.Clip;
            _labelColor = Agent.Color;
            PositionLabel(pose);
        }
        _lastPose = pose;
    }

    /// <summary>
    /// Pone el rótulo encima de la parte VISIBLE del gato (los fotogramas tienen mucho margen
    /// transparente arriba), anclado por su centro a una distancia fija
    /// (<see cref="LabelPlacement.CenterDistance"/>), y sigue los saltos.
    /// </summary>
    private void PositionLabel(CatPose pose)
    {
        if (Img.ActualHeight <= 0 || pose.Clip is null) return;
        double head = _atlas.HeadFraction(Agent.Color, pose.Clip) * Img.ActualHeight;
        LabelShift.Y = LabelPlacement.Top(head, LabelBox.ActualHeight) + pose.OffsetY;
    }

    /// <summary>
    /// Zona clicable del gato, en coordenadas de este control: la caja de sus píxeles visibles en el
    /// fotograma actual (del atlas), colocada donde está dibujada la imagen y con el espejo real.
    /// </summary>
    public Rect GetHitRect()
    {
        var fallback = new Rect(0, 0, ActualWidth, ActualHeight);
        if (Img.Source is null || Img.ActualWidth <= 0 || Img.ActualHeight <= 0 || Agent.Body is not { } cat)
            return fallback;

        Rect image;
        try
        {
            // TransformBounds (y no TranslatePoint): da el rectángulo dibujado con espejo, escala,
            // giro y desplazamientos ya aplicados.
            image = Img.TransformToAncestor(this)
                .TransformBounds(new Rect(0, 0, Img.ActualWidth, Img.ActualHeight));
        }
        catch (InvalidOperationException)
        {
            return fallback; // aún no está en el árbol visual
        }

        var opaque = _atlas.Bounds(Agent.Color, cat.Pose.Clip, cat.Pose.Frame);
        var area = HitGeometry.HitArea(
            new Area(image.X, image.Y, image.Width, image.Height),
            new Area(opaque.X, opaque.Y, opaque.Width, opaque.Height),
            mirrored: Flip.ScaleX < 0);
        return new Rect(area.X, area.Y, area.Width, area.Height);
    }

    public void ShowLabel(bool show) =>
        LabelBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
}
