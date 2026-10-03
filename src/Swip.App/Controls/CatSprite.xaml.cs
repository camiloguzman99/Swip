using System.Windows;
using System.Windows.Controls;
using Swip.App.Art;
using Swip.App.World;

namespace Swip.App.Controls;

/// <summary>
/// Vista de un gato. La ventana le empuja el frame y el estado en cada tick; este control solo pinta.
/// </summary>
public partial class CatSprite : UserControl
{
    private double _baseSize = 64;

    /// <summary>El gato que representa este sprite.</summary>
    public CatAgent Agent { get; }

    public CatSprite(CatAgent agent, double catSize)
    {
        InitializeComponent();
        Agent = agent;
        NameLabel.Text = agent.DisplayName;
        SetCatSize(catSize);
    }

    /// <summary>Ajusta el tamaño base del gato y aplica el ensanchado por gordura.</summary>
    public void SetCatSize(double size)
    {
        _baseSize = size;
        ApplyFat();
    }

    /// <summary>Reaplica el ancho según el nivel de gordura del gato (más gordo = más ancho).</summary>
    public void ApplyFat()
    {
        Img.Height = _baseSize;
        Img.Width = _baseSize * (1.0 + Math.Clamp(Agent.FatLevel, 0, 6) * 0.14);
    }

    /// <summary>Refresca el frame y los adornos a partir del estado actual del agente.</summary>
    public void Render()
    {
        Img.Source = PixelCat.Get(Agent.Frame, mirrored: !Agent.FacingRight, theme: Agent.Color);
        Zzz.Visibility = Agent.State == CatState.Sleeping && !Agent.IsReacting
            ? Visibility.Visible : Visibility.Collapsed;
        Heart.Visibility = Agent.IsReacting ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Muestra u oculta la etiqueta con el nombre.</summary>
    public void ShowLabel(bool show) =>
        LabelBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
}
