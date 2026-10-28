using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Swip.App.Art;
using Swip.Shared.Art;
using Swip.Shared.Engine;
using static Swip.App.Controls.VisorUi;

namespace Swip.App.Controls;

/// <summary>
/// Editor de píxeles del Visor: colorear y corregir el dibujo cuadro a cuadro. Lo retocado se guarda en la carpeta
/// compartida (<see cref="Art.ArtStore"/>) y el atlas lo usa en lugar del cuadro de la hoja, en todas las sesiones.
/// El cuadro original no se pierde: «Restablecer» borra el retoque.
/// </summary>
public sealed partial class VisorWindow
{
    private enum Tool { Pencil, Eraser, Picker, Fill }

    private bool _editOpen, _editDirty, _painting, _edGridOn;
    private string _editClip = "";
    private int _editFrame, _edZoom = 5, _lastX = -1, _lastY = -1;
    private byte[] _px = Array.Empty<byte>();
    private readonly Stack<byte[]> _undo = new();
    private WriteableBitmap? _wb;
    private Image _edImage = null!, _edOverlay = null!;
    private StackPanel _edBody = null!;
    private Button _edToggle = null!;
    private Tool _tool = Tool.Pencil;
    private Color _color = Color.FromRgb(0xFC, 0x88, 0x25);
    private Color? _picked;
    private Border _colorSwatch = null!, _pickedSwatch = null!;
    private TextBox _hexBox = null!;
    private WrapPanel _palette = null!;
    private TextBlock _edInfo = null!;
    private const int MaxUndo = 60;

    private UIElement BuildEditorCard()
    {
        var border = NewCard("Editar el dibujo (colorear y corregir píxeles)", out var card);
        _edToggle = Pill("Abrir el editor", ToggleEditor);
        _edToggle.HorizontalAlignment = HorizontalAlignment.Left;
        card.Children.Add(_edToggle);

        _edBody = new StackPanel { Visibility = Visibility.Collapsed };
        card.Children.Add(_edBody);

        _edInfo = new TextBlock { Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 6) };
        _edBody.Children.Add(_edInfo);

        // herramientas
        var tools = new WrapPanel();
        tools.Children.Add(Label("herramienta"));
        tools.Children.Add(Segments(new[] { "Lápiz", "Borrador", "Gotero", "Relleno" }, 0, i => _tool = (Tool)i));
        tools.Children.Add(Label("zoom"));
        tools.Children.Add(Segments(new[] { "3", "4", "5", "6", "8" }, 2, i => { _edZoom = new[] { 3, 4, 5, 6, 8 }[i]; RebuildEditorBitmap(); }));
        var gridBtn = Pill("marcos y cuadrícula", () => { });
        gridBtn.Click += (_, _) => { _edGridOn = !_edGridOn; Paint(gridBtn, _edGridOn); RefreshEditorOverlay(); };
        Paint(gridBtn, false);
        tools.Children.Add(gridBtn);
        _edBody.Children.Add(tools);

        // color
        var colors = new WrapPanel();
        colors.Children.Add(Label("color"));
        _colorSwatch = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), BorderBrush = Muted, Margin = new Thickness(0, 0, 8, 6) };
        colors.Children.Add(_colorSwatch);
        var (hexFrame, hex) = Field(80);
        _hexBox = hex;
        _hexBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) UseHex(); };
        colors.Children.Add(hexFrame);
        colors.Children.Add(Pill("Usar color", UseHex));
        colors.Children.Add(Label("a cambiar"));
        _pickedSwatch = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), BorderBrush = Muted, Margin = new Thickness(0, 0, 8, 6) };
        colors.Children.Add(_pickedSwatch);
        colors.Children.Add(Pill("Reemplazar en este cuadro", () => ReplaceColor(Scope.Frame)));
        colors.Children.Add(Pill("en este movimiento", () => ReplaceColor(Scope.Clip)));
        colors.Children.Add(Pill("en todo el gato", () => ReplaceColor(Scope.Cat)));
        _edBody.Children.Add(colors);

        _palette = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        _edBody.Children.Add(_palette);

        // acciones
        var actions = new WrapPanel();
        actions.Children.Add(Pill("Deshacer", Undo));
        actions.Children.Add(Pill("Guardar el cuadro", SaveEditorFrame));
        actions.Children.Add(Pill("Descartar cambios", DiscardEditorChanges));
        actions.Children.Add(Pill("Restablecer el original", RestoreOriginalFrame));
        _edBody.Children.Add(actions);

        // lienzo
        _edImage = new Image { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Cursor = Cursors.Cross, SnapsToDevicePixels = true };
        RenderOptions.SetBitmapScalingMode(_edImage, BitmapScalingMode.NearestNeighbor);
        _edOverlay = new Image { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, SnapsToDevicePixels = true };
        RenderOptions.SetBitmapScalingMode(_edOverlay, BitmapScalingMode.NearestNeighbor);
        var canvas = new Grid { Background = Stage };
        canvas.Children.Add(_edImage);
        canvas.Children.Add(_edOverlay);
        _edImage.MouseLeftButtonDown += EdDown;
        _edImage.MouseMove += EdMove;
        _edImage.MouseLeftButtonUp += EdUp;
        _edImage.MouseRightButtonDown += (_, e) => { var (x, y) = Cell(e); Pick(x, y); e.Handled = true; };
        _edBody.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 680, Content = canvas,
        });
        _edBody.Children.Add(new TextBlock
        {
            Text = "Botón izquierdo: pinta con la herramienta elegida. Botón derecho: gotero (toma el color y lo deja como «a cambiar»). " +
                   "Ctrl+Z deshace. Nada se guarda hasta pulsar «Guardar el cuadro».",
            Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 0, 0),
        });
        SetColor(_color);
        return border;
    }

    private void ToggleEditor()
    {
        if (_editOpen)
        {
            if (_editDirty) { SetStatus("Guarda o descarta los cambios del cuadro antes de cerrar el editor."); return; }
            _editOpen = false;
            _edBody.Visibility = Visibility.Collapsed;
            _edToggle.Content = "Abrir el editor";
            return;
        }
        if (_plan.Count == 0) return;
        _editOpen = true;
        _playing = false;
        _edBody.Visibility = Visibility.Visible;
        _edToggle.Content = "Cerrar el editor";
        LoadEditorFrame(CurStep.Clip, _frame);
    }

    /// <summary>Carga un cuadro en el editor (con lo ya retocado si lo hay). Si hay cambios sin guardar, no cambia.</summary>
    private void LoadEditorFrame(string clip, int frame)
    {
        if (_editDirty && (clip != _editClip || frame != _editFrame))
        {
            SetStatus("Guarda o descarta los cambios del cuadro antes de cambiar de cuadro.");
            return;
        }
        SetStatus(null);
        _editClip = clip;
        _editFrame = Math.Clamp(frame, 0, Math.Max(0, _atlas.FrameCount(clip) - 1));
        _px = _atlas.FramePixels(_skin, clip, _editFrame);
        _undo.Clear();
        _editDirty = false;
        RebuildEditorBitmap();
        RefreshPalette();
        string state = _atlas.IsEdited(_skin, clip, _editFrame) ? "retocado" : "original";
        _edInfo.Text = $"Editando el cuadro {_editFrame + 1} de «{clip}» del {(_skin == CatSkins.Gray ? "gato gris" : "gato naranja")} ({state}). " +
                       "Elige otro cuadro en la tira de miniaturas de arriba.";
    }

    private void RebuildEditorBitmap()
    {
        int gw = _atlas.GridWidth, gh = _atlas.GridHeight;
        if (_px.Length != gw * gh * 4) return;
        _wb = new WriteableBitmap(gw, gh, 96, 96, PixelFormats.Bgra32, null);
        _wb.WritePixels(new Int32Rect(0, 0, gw, gh), _px, gw * 4, 0);
        _edImage.Source = _wb;
        _edImage.Width = gw * _edZoom;
        _edImage.Height = gh * _edZoom;
        RefreshEditorOverlay();
    }

    private void RefreshEditorOverlay()
    {
        int gw = _atlas.GridWidth, gh = _atlas.GridHeight;
        _edOverlay.Source = _edGridOn ? ZoneOverlay.Get("cat", gw, gh, _atlas.ExtraZones, _atlas.Main, _edZoom, cellLines: _edZoom >= 4) : null;
        _edOverlay.Width = gw * _edZoom;
        _edOverlay.Height = gh * _edZoom;
    }

    private void RewriteBitmap()
    {
        int gw = _atlas.GridWidth, gh = _atlas.GridHeight;
        _wb?.WritePixels(new Int32Rect(0, 0, gw, gh), _px, gw * 4, 0);
    }

    // ---------------------------------------------------------------- pintar
    private (int X, int Y) Cell(MouseEventArgs e)
    {
        var p = e.GetPosition(_edImage);
        return ((int)Math.Floor(p.X / _edZoom), (int)Math.Floor(p.Y / _edZoom));
    }

    private bool Inside(int x, int y) => x >= 0 && y >= 0 && x < _atlas.GridWidth && y < _atlas.GridHeight;

    private void PushUndo()
    {
        _undo.Push((byte[])_px.Clone());
        if (_undo.Count > MaxUndo)
        {
            var keep = _undo.Reverse().Skip(_undo.Count - MaxUndo).ToArray();   // descarta lo más antiguo
            _undo.Clear();
            foreach (var s in keep) _undo.Push(s);
        }
        _editDirty = true;
    }

    private void EdDown(object sender, MouseButtonEventArgs e)
    {
        var (x, y) = Cell(e);
        if (!Inside(x, y)) return;
        switch (_tool)
        {
            case Tool.Picker: Pick(x, y); return;
            case Tool.Fill: PushUndo(); FloodFill(x, y, _color); RewriteBitmap(); return;
        }
        PushUndo();
        _painting = true;
        _lastX = x; _lastY = y;
        _edImage.CaptureMouse();
        PaintLine(x, y, x, y);
    }

    private void EdMove(object sender, MouseEventArgs e)
    {
        if (!_painting || e.LeftButton != MouseButtonState.Pressed) return;
        var (x, y) = Cell(e);
        if (x == _lastX && y == _lastY) return;
        PaintLine(_lastX, _lastY, x, y);
        _lastX = x; _lastY = y;
    }

    private void EdUp(object sender, MouseButtonEventArgs e)
    {
        _painting = false;
        _edImage.ReleaseMouseCapture();
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
        _wb?.WritePixels(new Int32Rect(x, y, 1, 1), new[] { b, g, r, a }, 4, 0);
    }

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
        if (!Inside(x, y)) return;
        int i = (y * _atlas.GridWidth + x) * 4;
        if (_px[i + 3] == 0) return;
        var c = Color.FromRgb(_px[i + 2], _px[i + 1], _px[i]);
        _picked = c;
        _pickedSwatch.Background = new SolidColorBrush(c);
        SetColor(c);
    }

    private void Undo()
    {
        if (!_editOpen || _undo.Count == 0) return;
        _px = _undo.Pop();
        _editDirty = _undo.Count > 0;
        RewriteBitmap();
    }

    // ---------------------------------------------------------------- color
    private void SetColor(Color c)
    {
        _color = c;
        _colorSwatch.Background = new SolidColorBrush(c);
        _hexBox.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        if (_picked is null) _pickedSwatch.Background = Transparent;
    }

    private void UseHex()
    {
        string text = _hexBox.Text.Trim().TrimStart('#');
        if (text.Length == 6 && int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v))
            SetColor(Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v));
        else
            SetStatus("El color se escribe como #RRGGBB (por ejemplo #FC8825).");
    }

    /// <summary>Los colores más usados del cuadro, para elegirlos con un clic.</summary>
    private void RefreshPalette()
    {
        _palette.Children.Clear();
        var counts = new Dictionary<int, int>();
        for (int i = 0; i + 3 < _px.Length; i += 4)
        {
            if (_px[i + 3] == 0) continue;
            int key = (_px[i + 2] << 16) | (_px[i + 1] << 8) | _px[i];
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        foreach (var key in counts.OrderByDescending(k => k.Value).Take(32).Select(k => k.Key))
        {
            var c = Color.FromRgb((byte)(key >> 16), (byte)(key >> 8), (byte)key);
            var swatch = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 4, 4), Background = new SolidColorBrush(c),
                BorderThickness = new Thickness(1), BorderBrush = Muted, Cursor = Cursors.Hand, ToolTip = $"#{c.R:X2}{c.G:X2}{c.B:X2}",
            };
            swatch.MouseLeftButtonUp += (_, _) => SetColor(c);
            swatch.MouseRightButtonUp += (_, _) => { _picked = c; _pickedSwatch.Background = new SolidColorBrush(c); };
            _palette.Children.Add(swatch);
        }
    }

    private enum Scope { Frame, Clip, Cat }

    private void ReplaceColor(Scope scope)
    {
        if (_picked is not { } from) { SetStatus("Primero elige con el gotero el color que quieres cambiar."); return; }
        var to = _color;
        if (from == to) return;
        if (scope == Scope.Frame)
        {
            PushUndo();
            ReplaceIn(_px, from, to);
            RewriteBitmap();
            RefreshPalette();
            return;
        }
        if (_editDirty) { SetStatus("Guarda o descarta los cambios del cuadro antes de reemplazar el color en varios cuadros."); return; }

        var clips = scope == Scope.Clip ? new[] { _editClip } : _clips.All.Select(c => c.Id).ToArray();
        int changed = 0;
        try
        {
            foreach (string clip in clips)
                for (int f = 0; f < _atlas.FrameCount(clip); f++)
                {
                    var pixels = _atlas.FramePixels(_skin, clip, f);
                    if (!ReplaceIn(pixels, from, to)) continue;
                    _store.SaveFrame(_skin, clip, f, pixels, _atlas.GridWidth, _atlas.GridHeight);
                    _atlas.SetOverride(_skin, clip, f, pixels);
                    changed++;
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("No se pudo guardar en la carpeta compartida: " + ex.Message);
        }
        if (changed > 0) _host.ArtChanged();
        SetStatus($"Color reemplazado en {changed} cuadros.");
        DrawingChanged();
        LoadEditorFrame(_editClip, _editFrame);
    }

    private static bool ReplaceIn(byte[] pixels, Color from, Color to)
    {
        bool any = false;
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            if (pixels[i + 3] == 0 || pixels[i] != from.B || pixels[i + 1] != from.G || pixels[i + 2] != from.R) continue;
            pixels[i] = to.B; pixels[i + 1] = to.G; pixels[i + 2] = to.R;
            any = true;
        }
        return any;
    }

    // ---------------------------------------------------------------- guardar, descartar, restablecer
    private void SaveEditorFrame()
    {
        if (!_editOpen) return;
        try
        {
            _store.SaveFrame(_skin, _editClip, _editFrame, _px, _atlas.GridWidth, _atlas.GridHeight);
            _atlas.SetOverride(_skin, _editClip, _editFrame, (byte[])_px.Clone());
            _host.ArtChanged();
            _editDirty = false;
            _undo.Clear();
            SetStatus("Cuadro guardado: ya se ve así en el gato.");
            DrawingChanged();
            LoadEditorFrame(_editClip, _editFrame);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SetStatus("No se pudo guardar el cuadro: " + ex.Message);
        }
    }

    private void DiscardEditorChanges()
    {
        if (!_editOpen) return;
        _editDirty = false;
        LoadEditorFrame(_editClip, _editFrame);
        SetStatus(null);
    }

    private void RestoreOriginalFrame()
    {
        if (!_editOpen) return;
        _store.DeleteFrame(_skin, _editClip, _editFrame);
        _atlas.SetOverride(_skin, _editClip, _editFrame, null);
        _host.ArtChanged();
        _editDirty = false;
        SetStatus("Cuadro restablecido al original.");
        DrawingChanged();
        LoadEditorFrame(_editClip, _editFrame);
    }
}
