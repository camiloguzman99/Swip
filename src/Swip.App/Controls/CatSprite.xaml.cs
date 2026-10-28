using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Swip.App.Art;
using Swip.App.World;
using Swip.Shared;
using Swip.Shared.Art;
using Swip.Shared.Engine;

namespace Swip.App.Controls;

/// <summary>
/// Vista de un gato. El motor (<see cref="SwipWorld"/>) decide todo; este control solo dibuja la
/// pose: la cuadrícula del fotograma con la paleta del gato, sombra y rótulo. El dibujo siempre mide
/// lo mismo en px lógicos (100 casillas de alto, a ArtScale.CellSize px cada una) y se adapta a la escala de cada pantalla sin deformarse.
/// </summary>
public partial class CatSprite : UserControl
{
    private readonly CatAtlas _atlas;

    // Lo último que se dibujó, para no reasignar propiedades iguales en cada fotograma.
    private string? _labelClip, _labelSkin;
    private CatPose _lastPose;
    private double _scale = 1;      // escala de la pantalla (1 = 100 %)
    private int _prescale = 1;      // ampliación entera previa (PixelScale.Prescale)

    public CatAgent Agent { get; }

    public CatSprite(CatAgent agent, CatAtlas atlas)
    {
        InitializeComponent();
        Agent = agent;
        _atlas = atlas;
        NameLabel.Text = LabelFormat.Split(agent.DisplayName); // dos palabras = dos filas
        LabelBox.SizeChanged += (_, _) => PositionLabel(_lastPose);

        ApplyCellSize();
    }

    /// <summary>
    /// El cuadro de dibujo mide lo que la cuadrícula: 1 casilla = ArtScale.CellSize px lógicos, siempre. Se vuelve a
    /// llamar cuando se cambia el tamaño de las piezas desde el Visor.
    /// </summary>
    public void ApplyCellSize()
    {
        double cell = ArtScale.CellSize;
        Cell.Width = Img.Width = _atlas.GridWidth * cell;
        Cell.Height = Img.Height = _atlas.GridHeight * cell;
        Width = _atlas.GridWidth * cell;
        Height = _atlas.GridHeight * cell;
        Shadow.Width = 52 * cell;          // la sombra acompaña al tamaño del gato (26 x 6 px a 0,5)
        Shadow.Height = 12 * cell;
        Shadow.Margin = new Thickness(0, _atlas.BaseRows * cell - Shadow.Height / 2, 0, 0); // sobre la base
        UpdateScale();
    }

    /// <summary>
    /// Toma la escala de la pantalla en la que está la ventana y elige cómo dibujar la cuadrícula:
    /// escala entera = vecino más cercano (nítido exacto); intermedia = ampliación entera previa y
    /// reducción suave (ver <see cref="PixelScale"/>). Llamar al cambiar de pantalla o de escala.
    /// </summary>
    public void UpdateScale(double? newScale = null)
    {
        double scale = newScale ?? VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (scale <= 0) scale = 1;
        _scale = scale;
        // Píxeles reales por casilla: con casillas de media unidad lógica, a 100 % son 0,5 (reducción suave) y a
        // 200 % exactamente 1 (nítido); las posiciones, en cambio, se ajustan con la escala de la pantalla.
        double perCell = scale * ArtScale.CellSize;
        _prescale = PixelScale.Prescale(perCell);
        RenderOptions.SetBitmapScalingMode(Img,
            PixelScale.IsWhole(perCell) ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        Img.Source = null; // se vuelve a pedir el fotograma con la ampliación nueva
        Render();
    }

    /// <summary>Escala de pantalla con la que se dibuja (para ajustar posiciones al píxel real).</summary>
    public double Scale => _scale;

    /// <summary>Dónde empieza el cuadro principal (el gato para el motor) dentro del cuadro de dibujo.</summary>
    public double MainOffsetX => _atlas.Main.X * ArtScale.CellSize;
    public double MainOffsetY => _atlas.Main.Y * ArtScale.CellSize;


    /// <summary>Dibuja la pose actual del gato (se llama en cada fotograma).</summary>
    public void Render()
    {
        if (Agent.Body is not { } cat) return;
        var pose = cat.Pose;

        var source = _atlas.Frame(Agent.Skin, pose.Clip, pose.Frame, mirrored: !pose.FacingRight, _prescale);
        if (!ReferenceEquals(Img.Source, source)) Img.Source = source;

        // Desplazamientos en píxeles reales enteros: un medio píxel emborronaría la cuadrícula.
        double ox = PixelScale.Snap(pose.OffsetX, _scale), oy = PixelScale.Snap(pose.OffsetY, _scale);
        if (Shift.X != ox) Shift.X = ox;
        if (Shift.Y != oy) Shift.Y = oy;

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
            // Pies (con el salto) + distancia hasta la superficie = la superficie: ahí va la sombra.
            ShadowShift.Y = PixelScale.Snap(pose.OffsetY + pose.ShadowDrop, _scale);
            Shadow.Opacity = 0.25 * k;
        }

        if (_labelClip != pose.Clip || _labelSkin != Agent.Skin || _lastPose.OffsetY != pose.OffsetY)
        {
            _labelClip = pose.Clip;
            _labelSkin = Agent.Skin;
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
        double head = _atlas.HeadFraction(Agent.Skin, pose.Clip) * Img.ActualHeight;
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
            // TransformBounds (y no TranslatePoint): da el rectángulo dibujado con el desplazamiento
            // (saltos) ya aplicado.
            image = Img.TransformToAncestor(this)
                .TransformBounds(new Rect(0, 0, Img.ActualWidth, Img.ActualHeight));
        }
        catch (InvalidOperationException)
        {
            return fallback; // aún no está en el árbol visual
        }

        var opaque = _atlas.Bounds(Agent.Skin, cat.Pose.Clip, cat.Pose.Frame);
        var area = HitGeometry.HitArea(
            new Area(image.X, image.Y, image.Width, image.Height),
            new Area(opaque.X, opaque.Y, opaque.Width, opaque.Height),
            mirrored: !cat.Pose.FacingRight);
        return new Rect(area.X, area.Y, area.Width, area.Height);
    }

    public void ShowLabel(bool show) =>
        LabelBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
}
