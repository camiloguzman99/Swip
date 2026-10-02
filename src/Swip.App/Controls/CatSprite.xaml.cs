using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Swip.App.World;

namespace Swip.App.Controls;

/// <summary>
/// Vista de un gato. La ventana le empuja el frame y el estado en cada tick; este control solo pinta.
/// </summary>
public partial class CatSprite : UserControl
{
    /// <summary>El gato que representa este sprite.</summary>
    public CatAgent Agent { get; }

    public CatSprite(CatAgent agent, double catSize)
    {
        InitializeComponent();
        Agent = agent;
        NameLabel.Text = agent.DisplayName;
        SetCatSize(catSize);
    }

    /// <summary>Ajusta el tamaño en pantalla del gato (lado del cuadro del sprite).</summary>
    public void SetCatSize(double size)
    {
        Img.Width = size;
        Img.Height = size;
    }

    /// <summary>Refresca el frame y los adornos a partir del estado actual del agente.</summary>
    public void Render()
    {
        Img.Source = Swip.App.Art.PixelCat.Get(Agent.Frame, mirrored: !Agent.FacingRight);
        Zzz.Visibility = Agent.State == CatState.Sleeping && !Agent.IsReacting
            ? Visibility.Visible : Visibility.Collapsed;
        Heart.Visibility = Agent.IsReacting ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Muestra u oculta la etiqueta con el nombre.</summary>
    public void ShowLabel(bool show) =>
        LabelBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
}
