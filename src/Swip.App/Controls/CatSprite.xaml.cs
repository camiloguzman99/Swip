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

    /// <summary>Refresca el sprite (acción + frame + color) y el espejo según la dirección.</summary>
    public void Render()
    {
        Img.Source = CatSprites.Get(Agent.Color, Agent.Action, Agent.FrameIndex);
        Flip.ScaleX = Agent.FacingRight ? 1 : -1;
    }

    public void ShowLabel(bool show) =>
        LabelBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
}
