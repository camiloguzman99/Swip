using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Swip.App.Controls;

/// <summary>
/// Piezas de interfaz del Visor con el estilo de las tarjetas de los menús (App.xaml): filas y botones ListRow,
/// tarjetas de fondo suave con esquinas redondeadas y el color de énfasis de Windows en lo activo.
/// </summary>
internal static class VisorUi
{
    // Los colores siguen el tono de Windows (claro u oscuro): ApplyTheme los fija al abrir el Visor.
    public static Brush Muted = Frozen(0xFF9AA1AE), Text = Frozen(0xFFE8EAED), White = Frozen(0xFFFFFFFF);
    public static Brush Card = Frozen(0x22FFFFFF), CardSel = Frozen(0x40FFFFFF), Hover = Frozen(0x40FFFFFF);
    public static Brush SideBg = Frozen(0xFF1C1D21), MainBg = Frozen(0xFF121316), Line = Frozen(0xFF2A2C31), GridLine = Frozen(0x59FFFFFF);
    public static readonly Brush Transparent = Brushes.Transparent;

    /// <summary>True si Windows está en tono oscuro para las aplicaciones (si no se puede leer, oscuro).</summary>
    public static bool WindowsIsDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int light || light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    public static void ApplyTheme(bool dark)
    {
        if (dark)
        {
            Muted = Frozen(0xFF9AA1AE); Text = Frozen(0xFFE8EAED); White = Frozen(0xFFFFFFFF);
            Card = Frozen(0x22FFFFFF); CardSel = Frozen(0x40FFFFFF); Hover = Frozen(0x40FFFFFF);
            SideBg = Frozen(0xFF1C1D21); MainBg = Frozen(0xFF121316); Line = Frozen(0xFF2A2C31); GridLine = Frozen(0x59FFFFFF);
        }
        else
        {
            Muted = Frozen(0xFF6B7280); Text = Frozen(0xFF1F2328); White = Frozen(0xFF000000);
            Card = Frozen(0x0F000000); CardSel = Frozen(0x1F000000); Hover = Frozen(0x1A000000);
            SideBg = Frozen(0xFFFFFFFF); MainBg = Frozen(0xFFF5F6F8); Line = Frozen(0xFFE5E7EB); GridLine = Frozen(0x66000000);
        }
    }

    public static readonly Dictionary<string, Brush> Dots = new()
    {
        ["ok"] = Frozen(0xFF5FD08A), ["prestada"] = Frozen(0xFFFFCC4D), ["falta"] = Frozen(0xFFFF7B72),
    };

    public static Brush Frozen(uint argb)
    {
        var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        b.Freeze();
        return b;
    }

    public static Style RowStyle => (Style)Application.Current.FindResource("ListRow");
    public static Brush Accent => (Brush)Application.Current.FindResource("AccentBrush");
    public static Brush AccentFg => (Brush)Application.Current.FindResource("AccentFgBrush");
    public static Brush AccentHover => (Brush)Application.Current.FindResource("AccentHoverBrush");

    /// <summary>
    /// Fondo en reposo de un botón. Como se fija como valor local, el resalte del estilo ListRow (un disparador) ya
    /// no actúa: <see cref="AttachHover"/> lo reproduce.
    /// </summary>
    public static void Rest(Button b, Brush brush)
    {
        b.Tag = brush;
        b.Background = brush;
    }

    public static void AttachHover(Button b)
    {
        b.MouseEnter += (_, _) => b.Background = ReferenceEquals(b.Tag, Accent) ? AccentHover : Hover;
        b.MouseLeave += (_, _) => b.Background = b.Tag as Brush ?? Transparent;
    }

    /// <summary>Pinta un botón como activo (color de énfasis) o normal.</summary>
    public static void Paint(Button b, bool on)
    {
        Rest(b, on ? Accent : Card);
        b.Foreground = on ? AccentFg : Text;
    }

    public static Button Pill(string text, Action click, bool compact = false)
    {
        var b = new Button
        {
            Content = text, Style = RowStyle, Foreground = Text, FontSize = 12, Margin = new Thickness(0, 0, 6, 6),
            Padding = compact ? new Thickness(8, 3, 8, 3) : new Thickness(10, 5, 10, 5),
        };
        Rest(b, Card);
        AttachHover(b);
        b.Click += (_, _) => click();
        return b;
    }

    public static TextBlock Label(string text) => new()
    {
        Text = text, Foreground = Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 6),
    };

    /// <summary>Botones en fila de los que solo uno está activo.</summary>
    public static StackPanel Segments(string[] labels, int selected, Action<int> changed)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 6) };
        var buttons = new List<Button>();
        void Repaint(int on)
        {
            for (int i = 0; i < buttons.Count; i++) Paint(buttons[i], i == on);
        }
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            var b = new Button { Content = labels[i], Style = RowStyle, Foreground = Text, Padding = new Thickness(9, 5, 9, 5), FontSize = 12, Margin = new Thickness(0, 0, 2, 0) };
            AttachHover(b);
            b.Click += (_, _) => { Repaint(index); changed(index); };
            buttons.Add(b);
            panel.Children.Add(b);
        }
        Repaint(selected);
        panel.Tag = (Action<int>)Repaint;   // para cambiar la selección desde el código
        return panel;
    }

    public static void Select(StackPanel segments, int index) => ((Action<int>)segments.Tag)(index);

    /// <summary>Cuadro de texto con el fondo de las tarjetas (el borde redondeado lo pone el contenedor).</summary>
    public static (Border Frame, TextBox Box) Field(double width, bool mono = true)
    {
        var tb = new TextBox
        {
            Width = width, Background = Transparent, Foreground = Text, CaretBrush = White, BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 5, 6, 5), FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center,
        };
        if (mono) tb.FontFamily = new FontFamily("Consolas");
        var frame = new Border { Background = Card, CornerRadius = new CornerRadius(9), Child = tb, Margin = new Thickness(0, 0, 8, 6) };
        return (frame, tb);
    }

    public static TextBox ReadOnlyBox(double height) => new()
    {
        IsReadOnly = true, Height = height, Background = Card, Foreground = Text, BorderThickness = new Thickness(0), Padding = new Thickness(8),
        FontFamily = new FontFamily("Consolas"), FontSize = 12, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 6),
    };

    /// <summary>Tarjeta (fondo suave, esquinas redondeadas) con título opcional; <paramref name="panel"/> es su contenido.</summary>
    public static Border NewCard(string title, out StackPanel panel)
    {
        panel = new StackPanel();
        if (title.Length > 0)
            panel.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), Foreground = Muted, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 6) });
        return new Border { Background = Card, CornerRadius = new CornerRadius(9), Padding = new Thickness(10), Margin = new Thickness(0, 4, 0, 6), Child = panel };
    }

    public static TextBlock GroupLabel(string text) => new()
    {
        Text = text.ToUpperInvariant(), Foreground = Muted, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 10, 0, 4),
    };

    /// <summary>Fila de una lista: punto de color, nombre y una etiqueta a la derecha.</summary>
    public static Button ListItem(string name, string quality, string tag, Action click, bool selected, double width)
    {
        var b = new Button { Style = RowStyle, Margin = new Thickness(0, 0, 0, 2), Padding = new Thickness(8, 6, 8, 6), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Rest(b, selected ? CardSel : Transparent);
        AttachHover(b);
        var g = new Grid { Width = width };   // el estilo ListRow no estira el contenido: ancho fijo
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = Dots[quality], Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        var n = new TextBlock { Text = name, Foreground = Text, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(n, 1);
        g.Children.Add(n);
        if (tag.Length > 0)
        {
            var t = new TextBlock { Text = tag, Foreground = Muted, FontSize = 11, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(t, 2);
            g.Children.Add(t);
        }
        b.Content = g;
        b.Click += (_, _) => click();
        return b;
    }
}
