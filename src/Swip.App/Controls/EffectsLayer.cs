using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Swip.Shared.Engine;

namespace Swip.App.Controls;

/// <summary>
/// Dibuja las partículas del motor (zzz, corazones, polvo, ¡!, notas, chispas, mosca, arañazos).
/// Reutiliza un grupo de elementos en vez de crearlos y destruirlos cada fotograma. No recibe
/// clics: está encima de todo pero es transparente al ratón.
/// </summary>
public sealed class EffectsLayer : Canvas
{
    private readonly List<TextBlock> _glyphs = new();
    private readonly List<Ellipse> _dots = new();

    private static readonly Dictionary<EffectKind, (string Text, Brush Brush)> Glyphs = new()
    {
        [EffectKind.Zzz] = ("z", Frozen(0xFFD2DCFF)),
        [EffectKind.Heart] = ("♥", Frozen(0xFFFF6E8A)),
        [EffectKind.Exclaim] = ("!", Frozen(0xFFFFD650)),
        [EffectKind.Sparkle] = ("✦", Frozen(0xFFFFF0A0)),
        [EffectKind.Note] = ("♪", Frozen(0xFFB4E6FF)),
        [EffectKind.Scratch] = ("/", Frozen(0xFF7A5232)),
    };

    private static readonly Brush DustBrush = Frozen(0xFFCDC3B4);
    private static readonly Brush FlyBrush = Frozen(0xFF1E1E1E);

    public EffectsLayer()
    {
        IsHitTestVisible = false;
    }

    public void Sync(IReadOnlyList<Particle> particles)
    {
        int glyph = 0, dot = 0;
        foreach (var p in particles)
        {
            if (p.Kind is EffectKind.Dust or EffectKind.Fly)
            {
                var e = Dot(dot++);
                double d = Math.Max(1, p.Size);
                e.Width = e.Height = d;
                e.Fill = p.Kind == EffectKind.Fly ? FlyBrush : DustBrush;
                e.Opacity = p.Alpha * (p.Kind == EffectKind.Dust ? 0.8 : 1);
                SetLeft(e, p.X - d / 2);
                SetTop(e, p.Y - d / 2);
            }
            else if (Glyphs.TryGetValue(p.Kind, out var g))
            {
                var t = Glyph(glyph++);
                if (!ReferenceEquals(t.Text, g.Text)) t.Text = g.Text;
                t.Foreground = g.Brush;
                t.FontSize = p.Size;
                t.Opacity = p.Alpha;
                SetLeft(t, p.X - p.Size * 0.3);
                SetTop(t, p.Y - p.Size * 0.6);
            }
        }
        for (int i = glyph; i < _glyphs.Count; i++) _glyphs[i].Visibility = Visibility.Collapsed;
        for (int i = dot; i < _dots.Count; i++) _dots[i].Visibility = Visibility.Collapsed;
    }

    public void Clear() => Sync(Array.Empty<Particle>());

    private TextBlock Glyph(int i)
    {
        while (_glyphs.Count <= i)
        {
            var t = new TextBlock { FontWeight = FontWeights.Bold, IsHitTestVisible = false };
            _glyphs.Add(t);
            Children.Add(t);
        }
        _glyphs[i].Visibility = Visibility.Visible;
        return _glyphs[i];
    }

    private Ellipse Dot(int i)
    {
        while (_dots.Count <= i)
        {
            var e = new Ellipse { IsHitTestVisible = false };
            _dots.Add(e);
            Children.Add(e);
        }
        _dots[i].Visibility = Visibility.Visible;
        return _dots[i];
    }

    private static Brush Frozen(uint argb)
    {
        var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        b.Freeze();
        return b;
    }
}
