using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Swip.App.Art;
using Swip.Shared.Art;
using static Swip.App.Controls.VisorUi;

namespace Swip.App.Controls;

/// <summary>
/// Edición de píxeles sobre el propio visor: con «Editar» activado el dibujo se pausa en el cuadro elegido, aparece la
/// cuadrícula de casillas y se pinta directamente sobre el dibujo ampliado (lápiz, borrador, cuentagotas y bote de pintura)
/// con los colores del gato. Lo pintado se guarda solo en la carpeta compartida (<see cref="Art.ArtStore"/>) y el atlas lo
/// usa en lugar del cuadro de la hoja, en todas las sesiones. Botón derecho: mover el dibujo ampliado.
/// </summary>
public sealed partial class VisorWindow
{
    private enum Tool { Pencil, Eraser, Picker, Fill }

    private bool _editing, _editDirty, _painting, _panning;
    private Tool _tool = Tool.Pencil;
    private Color _color = Color.FromRgb(0xFC, 0x88, 0x25);
    private byte[] _px = Array.Empty<byte>();
    private string _edKey = "", _edSkin = "", _edClip = "";
    private int _edFrame, _lastX = -1, _lastY = -1;
    private double _panX, _panY, _panStartX, _panStartY, _panOriginX, _panOriginY;
    private readonly Stack<byte[]> _undo = new();
    private readonly Dictionary<string, List<Color>> _palettes = new();
    private readonly DispatcherTimer _flushTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private Button _editToggle = null!;
    private Border _colorSwatch = null!;
    private TextBox _hexBox = null!;
    private WrapPanel _palette = null!;
    private const int MaxUndo = 60;

    /// <summary>La imagen que se muestra y se pinta mientras se edita (null si no se edita).</summary>
    private WriteableBitmap? EditBitmap { get; set; }

    private UIElement BuildEditCard()
    {
        var border = NewCard("Dibujo", out var card);
        border.Margin = new Thickness(0, 2, 0, 0);

        _editToggle = new Button { Content = "Editar", Style = RowStyle, Padding = new Thickness(14, 6, 14, 6), FontSize = 12, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        AttachHover(_editToggle);
        Paint(_editToggle, false);
        _editToggle.Click += (_, _) => ToggleEditing();
        card.Children.Add(_editToggle);

        card.Children.Add(Segments(new[] { "Lápiz", "Borrador", "Cuentagotas", "Bote" }, 0, i => _tool = (Tool)i));

        var color = new StackPanel { Orientation = Orientation.Horizontal };
        _colorSwatch = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), BorderBrush = Muted, Margin = new Thickness(0, 0, 8, 6) };
        var (hexFrame, hex) = Field(80);
        _hexBox = hex;
        _hexBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) UseHex(); };
        _hexBox.LostFocus += (_, _) => UseHex();
        color.Children.Add(_colorSwatch);
        color.Children.Add(hexFrame);
        color.Children.Add(Pill("Deshacer", Undo, compact: true));
        card.Children.Add(FormRow("Color", color));

        card.Children.Add(GroupLabel("Colores del gato"));
        _palette = new WrapPanel();
        card.Children.Add(_palette);
        SetColor(_color);
        RefreshPalette();
        return border;
    }

    /// <summary>Engancha al escenario el pintado (botón izquierdo) y el desplazamiento del dibujo ampliado (botón derecho).</summary>
    private void HookStageEditing()
    {
        _flushTimer.Tick += (_, _) => FlushEdit();
        _stage.MouseLeftButtonDown += (_, e) => { if (_editing) StartPaint(e); };
        _stage.MouseMove += (_, e) =>
        {
            if (_painting && e.LeftButton == MouseButtonState.Pressed) ContinuePaint(e);
            else if (_panning && e.RightButton == MouseButtonState.Pressed)
            {
                var p = e.GetPosition(_stage);
                _panX = _panOriginX + (p.X - _panStartX);
                _panY = _panOriginY + (p.Y - _panStartY);
                _shownKey = "";
            }
        };
        _stage.MouseLeftButtonUp += (_, _) => EndPaint();
        _stage.MouseRightButtonDown += (_, e) =>
        {
            if (!_editing) return;
            var p = e.GetPosition(_stage);
            _panning = true;
            _panStartX = p.X; _panStartY = p.Y; _panOriginX = _panX; _panOriginY = _panY;
            _stage.CaptureMouse();
            e.Handled = true;
        };
        _stage.MouseRightButtonUp += (_, _) => { _panning = false; if (!_painting) _stage.ReleaseMouseCapture(); };
    }

    // ---------------------------------------------------------------- activar y desactivar
    private void ToggleEditing()
    {
        if (_editing) { StopEditing(); return; }
        _editing = true;
        _playing = false;
        _zoom = Math.Max(Math.Max(_zoom, FitZoom()), 5);   // ampliado, para ver y acertar las casillas
        _panX = _panY = 0;
        _edKey = "";
        Paint(_editToggle, true);
        _stage.Cursor = Cursors.Cross;
        SetStatus(null);
        RefreshPalette();
        _shownKey = "";
    }

    private void StopEditing()
    {
        FlushEdit();
        _editing = false;
        _painting = _panning = false;
        EditBitmap = null;
        _edKey = "";
        _panX = _panY = 0;
        Paint(_editToggle, false);
        _stage.Cursor = Cursors.Arrow;
        _shownKey = "";
    }

    /// <summary>Carga en memoria el cuadro que se ve (con lo ya retocado si lo hay) para pintarlo.</summary>
    private void EnsureEditFrame(string clip, int frame)
    {
        string key = $"{_skin}|{clip}|{frame}";
        if (key == _edKey && EditBitmap is not null) return;
        FlushEdit();
        int gw = _atlas.GridWidth, gh = _atlas.GridHeight;
        _px = _atlas.FramePixels(_skin, clip, frame);
        _edKey = key; _edSkin = _skin; _edClip = clip; _edFrame = frame;
        _undo.Clear();
        EditBitmap = new WriteableBitmap(gw, gh, 96, 96, PixelFormats.Bgra32, null);
        EditBitmap.WritePixels(new Int32Rect(0, 0, gw, gh), _px, gw * 4, 0);
        _shownKey = "";
    }

    // ---------------------------------------------------------------- pintar
    private (int X, int Y) CellAt(MouseEventArgs e)
    {
        var p = e.GetPosition(_frameImage);
        int zoom = Math.Max(1, _zoom);
        return ((int)Math.Floor(p.X / zoom), (int)Math.Floor(p.Y / zoom));
    }

    private bool Inside(int x, int y) => x >= 0 && y >= 0 && x < _atlas.GridWidth && y < _atlas.GridHeight;

    private void PushUndo()
    {
        _undo.Push((byte[])_px.Clone());
        if (_undo.Count > MaxUndo)
        {
            var keep = _undo.Reverse().Skip(_undo.Count - MaxUndo).ToArray();   // descarta lo más antiguo
            _undo.Clear();
            foreach (var u in keep) _undo.Push(u);
        }
    }

    private void StartPaint(MouseButtonEventArgs e)
    {
        if (EditBitmap is null) return;
        var (x, y) = CellAt(e);
        if (!Inside(x, y)) return;
        switch (_tool)
        {
            case Tool.Picker:
                Pick(x, y);
                return;
            case Tool.Fill:
                PushUndo();
                FloodFill(x, y, _color);
                Rewrite();
                MarkDirty();
                return;
        }
        PushUndo();
        _painting = true;
        _lastX = x; _lastY = y;
        _stage.CaptureMouse();
        PaintLine(x, y, x, y);
    }

    private void ContinuePaint(MouseEventArgs e)
    {
        var (x, y) = CellAt(e);
        if (x == _lastX && y == _lastY) return;
        PaintLine(_lastX, _lastY, x, y);
        _lastX = x; _lastY = y;
    }

    private void EndPaint()
    {
        if (!_painting) return;
        _painting = false;
        if (!_panning) _stage.ReleaseMouseCapture();
        MarkDirty();
    }

    /// <summary>Línea de Bresenham: sin huecos aunque el ratón vaya rápido.</summary>
    private void PaintLine(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (true)
        {
            SetPixel(x0, y0, _tool == Tool.Eraser ? null : _color);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private void SetPixel(int x, int y, Color? color)
    {
        if (!Inside(x, y)) return;
        int i = (y * _atlas.GridWidth + x) * 4;
        byte b = 0, g = 0, r = 0, a = 0;
        if (color is { } c) { b = c.B; g = c.G; r = c.R; a = 255; }
        _px[i] = b; _px[i + 1] = g; _px[i + 2] = r; _px[i + 3] = a;
        EditBitmap?.WritePixels(new Int32Rect(x, y, 1, 1), new[] { b, g, r, a }, 4, 0);
    }

    private void Rewrite() =>
        EditBitmap?.WritePixels(new Int32Rect(0, 0, _atlas.GridWidth, _atlas.GridHeight), _px, _atlas.GridWidth * 4, 0);

    private void FloodFill(int x, int y, Color color)
    {
        int gw = _atlas.GridWidth, gh = _atlas.GridHeight;
        int start = (y * gw + x) * 4;
        byte tb = _px[start], tg = _px[start + 1], tr = _px[start + 2], ta = _px[start + 3];
        if (ta == 255 && tb == color.B && tg == color.G && tr == color.R) return;   // ya es de ese color
        bool Same(int i) => ta == 0 ? _px[i + 3] == 0 : _px[i + 3] != 0 && _px[i] == tb && _px[i + 1] == tg && _px[i + 2] == tr;
        var stack = new Stack<(int, int)>();
        stack.Push((x, y));
        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Pop();
            if (cx < 0 || cy < 0 || cx >= gw || cy >= gh) continue;
            int i = (cy * gw + cx) * 4;
            if (!Same(i)) continue;
            _px[i] = color.B; _px[i + 1] = color.G; _px[i + 2] = color.R; _px[i + 3] = 255;
            stack.Push((cx + 1, cy)); stack.Push((cx - 1, cy)); stack.Push((cx, cy + 1)); stack.Push((cx, cy - 1));
        }
    }

    private void Pick(int x, int y)
    {
        int i = (y * _atlas.GridWidth + x) * 4;
        if (_px[i + 3] == 0) return;
        SetColor(Color.FromRgb(_px[i + 2], _px[i + 1], _px[i]));
    }

    private void Undo()
    {
        if (!_editing || _undo.Count == 0) return;
        _px = _undo.Pop();
        Rewrite();
        MarkDirty();
    }

    // ---------------------------------------------------------------- color y paleta
    private void SetColor(Color c)
    {
        _color = c;
        _colorSwatch.Background = new SolidColorBrush(c);
        _hexBox.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private void UseHex()
    {
        string text = _hexBox.Text.Trim().TrimStart('#');
        if (text.Length == 6 && int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v))
            SetColor(Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v));
        else
            _hexBox.Text = $"#{_color.R:X2}{_color.G:X2}{_color.B:X2}";
    }

    /// <summary>Los colores de este gato: los mismos para todas sus acciones y transiciones (los más usados de todos sus cuadros).</summary>
    private List<Color> PaletteOf(string skin)
    {
        if (_palettes.TryGetValue(skin, out var known)) return known;
        var counts = new Dictionary<int, int>();
        foreach (var clip in _clips.All)
            for (int f = 0; f < _atlas.FrameCount(clip.Id); f++)
            {
                var px = _atlas.FramePixels(skin, clip.Id, f);
                for (int i = 0; i + 3 < px.Length; i += 4)
                {
                    if (px[i + 3] == 0) continue;
                    int key = (px[i + 2] << 16) | (px[i + 1] << 8) | px[i];
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                }
            }
        var list = counts.OrderByDescending(k => k.Value).Take(48)
            .Select(k => Color.FromRgb((byte)(k.Key >> 16), (byte)(k.Key >> 8), (byte)k.Key)).ToList();
        _palettes[skin] = list;
        return list;
    }

    private void RefreshPalette()
    {
        _palette.Children.Clear();
        foreach (var c in PaletteOf(_skin))
        {
            var color = c;
            var swatch = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 4, 4), Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(1), BorderBrush = Muted, Cursor = Cursors.Hand, ToolTip = $"#{color.R:X2}{color.G:X2}{color.B:X2}",
            };
            swatch.MouseLeftButtonUp += (_, _) => SetColor(color);
            _palette.Children.Add(swatch);
        }
    }

    // ---------------------------------------------------------------- guardar
    private void MarkDirty()
    {
        _editDirty = true;
        _flushTimer.Stop();
        _flushTimer.Start();
    }

    /// <summary>Guarda en la carpeta compartida lo pintado y lo aplica al atlas y a las demás sesiones.</summary>
    private void FlushEdit()
    {
        _flushTimer.Stop();
        if (!_editDirty || _px.Length == 0) return;
        _editDirty = false;
        try
        {
            _store.SaveFrame(_edSkin, _edClip, _edFrame, _px, _atlas.GridWidth, _atlas.GridHeight);
            _atlas.SetOverride(_edSkin, _edClip, _edFrame, (byte[])_px.Clone());
            _host.ArtChanged();
            DrawingChanged();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SetStatus("No se pudo guardar el cuadro: " + ex.Message);
        }
    }
}
