using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Swip.App.Art;
using Swip.Shared.Art;
using static Swip.App.Controls.VisorUi;

namespace Swip.App.Controls;

/// <summary>
/// Editor de píxeles del Visor, dentro de la misma ventana: ocupa el sitio del visor y enseña TODOS los cuadros del clip
/// (los de una acción o transición) para colorearlos y corregirlos a la vez. La paleta y las herramientas están a la
/// derecha y debajo, Aceptar, Cancelar y Restablecer. Lo aceptado se guarda en la carpeta compartida
/// (<see cref="Art.ArtStore"/>) y el atlas lo usa en lugar del cuadro de la hoja, en todas las sesiones.
/// El cuadro original no se pierde: «Restablecer el original» borra los retoques del clip.
/// </summary>
public sealed partial class VisorWindow
{
    private enum Tool { Pencil, Eraser, Picker, Fill }

    private sealed class EdFrame
    {
        public byte[] Px = Array.Empty<byte>();
        public byte[] Orig = Array.Empty<byte>();
        public WriteableBitmap? Wb;
        public Grid? Canvas;
    }

    private bool _editOpen, _editDirty, _painting, _edGridOn;
    private string _editClip = "";
    private int _edZoomSel, _edZoom = 2, _lastX = -1, _lastY = -1, _paintFrame = -1;
    private Int32Rect _crop;
    private readonly List<EdFrame> _edFrames = new();
    private readonly Stack<List<(int Frame, byte[] Px)>> _undo = new();
    private Grid _editPanel = null!;
    private ScrollViewer _edScroll = null!;
    private WrapPanel _edWrap = null!;
    private TextBlock _edInfo = null!;
    private Tool _tool = Tool.Pencil;
    private Color _color = Color.FromRgb(0xFC, 0x88, 0x25);
    private Color? _picked;
    private Border _colorSwatch = null!, _pickedSwatch = null!;
    private TextBox _hexBox = null!;
    private WrapPanel _palette = null!;
    private const int MaxUndo = 60, CropMargin = 4;

    private Grid BuildEditorPanel()
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });

        // Todos los cuadros del clip, repartidos en filas para que quepan sin desplazarse.
        _edWrap = new WrapPanel();
        _edScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _edWrap,
        };
        _edScroll.SizeChanged += (_, _) => { if (_editOpen) RebuildEditorFrames(); };
        content.Children.Add(_edScroll);

        var paletteCard = BuildPaletteCard();
        Grid.SetColumn(paletteCard, 2);
        content.Children.Add(paletteCard);
        grid.Children.Add(content);

        // Debajo: aceptar y cancelar.
        var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var accept = Pill("Aceptar", AcceptEditor);
        Paint(accept, true);
        buttons.Children.Add(accept);
        buttons.Children.Add(Pill("Cancelar", CancelEditor));
        buttons.Children.Add(Pill("Restablecer el original", RestoreOriginal));
        buttons.Children.Add(Pill("Deshacer", Undo));
        _edInfo = new TextBlock { Foreground = Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 6) };
        buttons.Children.Add(_edInfo);
        Grid.SetRow(buttons, 1);
        grid.Children.Add(buttons);
        return grid;
    }

    private UIElement BuildPaletteCard()
    {
        var border = NewCard("Paleta y herramientas", out var card);
        border.Margin = new Thickness(0, 0, 0, 0);

        card.Children.Add(Label("herramienta"));
        card.Children.Add(Segments(new[] { "Lápiz", "Borrador", "Gotero", "Relleno" }, 0, i => _tool = (Tool)i));

        var colors = new WrapPanel();
        colors.Children.Add(Label("color"));
        _colorSwatch = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), BorderBrush = Muted, Margin = new Thickness(0, 0, 8, 6) };
        colors.Children.Add(_colorSwatch);
        var (hexFrame, hex) = Field(76);
        _hexBox = hex;
        _hexBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) UseHex(); };
        colors.Children.Add(hexFrame);
        colors.Children.Add(Pill("Usar", UseHex, compact: true));
        card.Children.Add(colors);

        card.Children.Add(Label("colores del dibujo (clic: usar · clic derecho: a cambiar)"));
        _palette = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        card.Children.Add(_palette);

        var change = new WrapPanel();
        change.Children.Add(Label("a cambiar"));
        _pickedSwatch = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), BorderBrush = Muted, Margin = new Thickness(0, 0, 8, 6) };
        change.Children.Add(_pickedSwatch);
        change.Children.Add(Pill("Reemplazar en todos los cuadros", ReplaceColor, compact: true));
        card.Children.Add(change);

        card.Children.Add(Label("zoom"));
        card.Children.Add(Segments(new[] { "Ajustar", "2", "3", "4", "6" }, 0, i => { _edZoomSel = new[] { 0, 2, 3, 4, 6 }[i]; RebuildEditorFrames(); }));
        var gridBtn = Pill("marcos y cuadrícula", () => { });
        gridBtn.Click += (_, _) => { _edGridOn = !_edGridOn; Paint(gridBtn, _edGridOn); RebuildEditorFrames(); };
        Paint(gridBtn, false);
        card.Children.Add(gridBtn);

        card.Children.Add(new TextBlock
        {
            Text = "Botón izquierdo: pinta con la herramienta elegida en cualquier cuadro. Botón derecho: gotero. Ctrl+Z deshace. " +
                   "Nada se guarda hasta pulsar Aceptar.",
            Foreground = Muted, FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 0, 0),
        });
        SetColor(_color);
        return border;
    }

    // ---------------------------------------------------------------- abrir y cerrar
    private void OpenEditor()
    {
        if (_editOpen || _plan.Count == 0) return;
        string clip = _atlas.FrameCount(_selClip) > 0 ? _selClip : CurStep.Clip;
        int n = _atlas.FrameCount(clip);
        if (n == 0) return;
        _editClip = clip;
        LoadEditorFrames();
        _editOpen = true;
        _viewPanel.Visibility = Visibility.Collapsed;
        _editPanel.Visibility = Visibility.Visible;
        SetStatus(null);
        RefreshPalette();
        RebuildEditorFrames();
    }

    private void CloseEditor()
    {
        _editOpen = false;
        _editDirty = false;
        _painting = false;
        _undo.Clear();
        _edFrames.Clear();
        _edWrap.Children.Clear();
        _closeWarned = false;
        _editPanel.Visibility = Visibility.Collapsed;
        _viewPanel.Visibility = Visibility.Visible;
        _shownKey = "";
    }

    /// <summary>Lee de nuevo los cuadros del clip (con lo ya retocado) y calcula la zona que cubre su contenido.</summary>
    private void LoadEditorFrames()
    {
        _edFrames.Clear();
        _undo.Clear();
        _editDirty = false;
        int gw = _atlas.GridWidth, gh = _atlas.GridHeight;
        int minX = gw, minY = gh, maxX = -1, maxY = -1;
        for (int f = 0; f < _atlas.FrameCount(_editClip); f++)
        {
            var px = _atlas.FramePixels(_skin, _editClip, f);
            _edFrames.Add(new EdFrame { Px = px, Orig = (byte[])px.Clone() });
            for (int y = 0; y < gh; y++)
                for (int x = 0; x < gw; x++)
                {
                    if (px[(y * gw + x) * 4 + 3] == 0) continue;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
        }
        if (maxX < 0) { minX = 0; minY = 0; maxX = gw - 1; maxY = gh - 1; }
        minX = Math.Max(0, minX - CropMargin); minY = Math.Max(0, minY - CropMargin);
        maxX = Math.Min(gw - 1, maxX + CropMargin); maxY = Math.Min(gh - 1, maxY + CropMargin);
        _crop = new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        string cat = _skin == CatSkins.Gray ? "gato gris" : "gato naranja";
        _edInfo.Text = $"Editando los {_edFrames.Count} cuadros de «{_editClip}» del {cat}.";
    }

    /// <summary>Dibuja todos los cuadros con el mayor zoom con el que caben a la vez (o el elegido).</summary>
    private void RebuildEditorFrames()
    {
        if (!_editOpen) return;
        double w = _edScroll.ActualWidth - 6, h = _edScroll.ActualHeight - 4;
        int n = _edFrames.Count;
        int zoom = _edZoomSel;
        if (zoom == 0)
        {
            zoom = 1;
            if (w > 0 && h > 0)
                for (int z = 8; z >= 1; z--)
                {
                    double cw = _crop.Width * z + 16, ch = _crop.Height * z + 34;
                    int cols = Math.Max(1, (int)(w / cw));
                    int rows = (n + cols - 1) / cols;
                    if (rows * ch <= h) { zoom = z; break; }
                }
        }
        _edZoom = zoom;

        _edWrap.Children.Clear();
        int gw = _atlas.GridWidth, gh = _atlas.GridHeight;
        for (int i = 0; i < n; i++)
        {
            int index = i;
            var fr = _edFrames[i];
            fr.Wb = new WriteableBitmap(gw, gh, 96, 96, PixelFormats.Bgra32, null);
            fr.Wb.WritePixels(new Int32Rect(0, 0, gw, gh), fr.Px, gw * 4, 0);
            var img = new Image
            {
                Source = fr.Wb, Stretch = Stretch.Fill, Width = gw * zoom, Height = gh * zoom, SnapsToDevicePixels = true,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(-_crop.X * zoom, -_crop.Y * zoom, 0, 0),
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            var canvas = new Grid
            {
                Width = _crop.Width * zoom, Height = _crop.Height * zoom, ClipToBounds = true, Cursor = Cursors.Cross,
                Background = Card,
            };
            canvas.Children.Add(img);
            if (_edGridOn)
            {
                var overlay = new Image
                {
                    Source = ZoneOverlay.Get("cat", gw, gh, _atlas.ExtraZones, _atlas.Main, zoom, cellLines: zoom >= 4),
                    Stretch = Stretch.None, IsHitTestVisible = false, SnapsToDevicePixels = true,
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(-_crop.X * zoom, -_crop.Y * zoom, 0, 0),
                };
                RenderOptions.SetBitmapScalingMode(overlay, BitmapScalingMode.NearestNeighbor);
                canvas.Children.Add(overlay);
            }
            canvas.MouseLeftButtonDown += (_, e) => EdDown(index, canvas, e);
            canvas.MouseMove += (_, e) => EdMove(index, canvas, e);
            canvas.MouseLeftButtonUp += (_, _) => EdUp(canvas);
            canvas.MouseRightButtonDown += (_, e) => { var (x, y) = Cell(canvas, e); Pick(index, x, y); e.Handled = true; };
            fr.Canvas = canvas;

            var card = new StackPanel { Margin = new Thickness(0, 0, 8, 6) };
            card.Children.Add(new TextBlock { Text = $"Cuadro {i + 1}", Foreground = Muted, FontSize = 10, Margin = new Thickness(2, 0, 0, 2) });
            card.Children.Add(canvas);
            _edWrap.Children.Add(card);
        }
    }

    // ---------------------------------------------------------------- pintar
    private (int X, int Y) Cell(UIElement canvas, MouseEventArgs e)
    {
        var p = e.GetPosition(canvas);
        return ((int)Math.Floor(p.X / _edZoom) + _crop.X, (int)Math.Floor(p.Y / _edZoom) + _crop.Y);
    }

    private bool Inside(int x, int y) => x >= 0 && y >= 0 && x < _atlas.GridWidth && y < _atlas.GridHeight;

    private void PushUndo(params int[] frames)
    {
        _undo.Push(frames.Select(f => (f, (byte[])_edFrames[f].Px.Clone())).ToList());
        if (_undo.Count > MaxUndo)
        {
            var keep = _undo.Reverse().Skip(_undo.Count - MaxUndo).ToArray();   // descarta lo más antiguo
            _undo.Clear();
            foreach (var s in keep) _undo.Push(s);
        }
        _editDirty = true;
    }

    private void EdDown(int frame, Grid canvas, MouseButtonEventArgs e)
    {
        var (x, y) = Cell(canvas, e);
        if (!Inside(x, y)) return;
        switch (_tool)
        {
            case Tool.Picker: Pick(frame, x, y); return;
            case Tool.Fill:
                PushUndo(frame);
                FloodFill(_edFrames[frame], x, y, _color);
                Rewrite(frame);
                return;
        }
        PushUndo(frame);
        _painting = true;
        _paintFrame = frame;
        _lastX = x; _lastY = y;
        canvas.CaptureMouse();
        PaintLine(frame, x, y, x, y);
    }

    private void EdMove(int frame, Grid canvas, MouseEventArgs e)
    {
        if (!_painting || _paintFrame != frame || e.LeftButton != MouseButtonState.Pressed) return;
        var (x, y) = Cell(canvas, e);
        if (x == _lastX && y == _lastY) return;
        PaintLine(frame, _lastX, _lastY, x, y);
        _lastX = x; _lastY = y;
    }

    private void EdUp(Grid canvas)
    {
        _painting = false;
        _paintFrame = -1;
        canvas.ReleaseMouseCapture();
    }

    /// <summary>Línea de Bresenham: sin huecos aunque el ratón vaya rápido.</summary>
    private void PaintLine(int frame, int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (true)
        {
            SetPixel(frame, x0, y0, _tool == Tool.Eraser ? null : _color);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private void SetPixel(int frame, int x, int y, Color? color)
    {
        if (!Inside(x, y)) return;
        var fr = _edFrames[frame];
        int i = (y * _atlas.GridWidth + x) * 4;
        byte b = 0, g = 0, r = 0, a = 0;
        if (color is { } c) { b = c.B; g = c.G; r = c.R; a = 255; }
        fr.Px[i] = b; fr.Px[i + 1] = g; fr.Px[i + 2] = r; fr.Px[i + 3] = a;
        fr.Wb?.WritePixels(new Int32Rect(x, y, 1, 1), new[] { b, g, r, a }, 4, 0);
    }

    private void Rewrite(int frame)
    {
        var fr = _edFrames[frame];
        fr.Wb?.WritePixels(new Int32Rect(0, 0, _atlas.GridWidth, _atlas.GridHeight), fr.Px, _atlas.GridWidth * 4, 0);
    }

    private void FloodFill(EdFrame fr, int x, int y, Color color)
    {
        var px = fr.Px;
        int gw = _atlas.GridWidth, gh = _atlas.GridHeight;
        int start = (y * gw + x) * 4;
        byte tb = px[start], tg = px[start + 1], tr = px[start + 2], ta = px[start + 3];
        if (ta == 255 && tb == color.B && tg == color.G && tr == color.R) return;   // ya es de ese color
        bool Same(int i) => ta == 0 ? px[i + 3] == 0 : px[i + 3] != 0 && px[i] == tb && px[i + 1] == tg && px[i + 2] == tr;
        var stack = new Stack<(int, int)>();
        stack.Push((x, y));
        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Pop();
            if (cx < 0 || cy < 0 || cx >= gw || cy >= gh) continue;
            int i = (cy * gw + cx) * 4;
            if (!Same(i)) continue;
            px[i] = color.B; px[i + 1] = color.G; px[i + 2] = color.R; px[i + 3] = 255;
            stack.Push((cx + 1, cy)); stack.Push((cx - 1, cy)); stack.Push((cx, cy + 1)); stack.Push((cx, cy - 1));
        }
    }

    private void Pick(int frame, int x, int y)
    {
        if (!Inside(x, y)) return;
        int i = (y * _atlas.GridWidth + x) * 4;
        var px = _edFrames[frame].Px;
        if (px[i + 3] == 0) return;
        var c = Color.FromRgb(px[i + 2], px[i + 1], px[i]);
        _picked = c;
        _pickedSwatch.Background = new SolidColorBrush(c);
        SetColor(c);
    }

    private void Undo()
    {
        if (!_editOpen || _undo.Count == 0) return;
        foreach (var (frame, px) in _undo.Pop())
        {
            _edFrames[frame].Px = px;
            Rewrite(frame);
        }
        _editDirty = _undo.Count > 0;
        RefreshPalette();
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

    /// <summary>Los colores más usados de todos los cuadros, para elegirlos con un clic.</summary>
    private void RefreshPalette()
    {
        _palette.Children.Clear();
        var counts = new Dictionary<int, int>();
        foreach (var fr in _edFrames)
            for (int i = 0; i + 3 < fr.Px.Length; i += 4)
            {
                if (fr.Px[i + 3] == 0) continue;
                int key = (fr.Px[i + 2] << 16) | (fr.Px[i + 1] << 8) | fr.Px[i];
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        foreach (var key in counts.OrderByDescending(k => k.Value).Take(40).Select(k => k.Key))
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

    /// <summary>Cambia el color «a cambiar» por el color actual en todos los cuadros del clip (se puede deshacer).</summary>
    private void ReplaceColor()
    {
        if (_picked is not { } from) { SetStatus("Primero elige con el gotero (o con clic derecho en la paleta) el color que quieres cambiar."); return; }
        var to = _color;
        if (from == to) return;
        var changed = new List<int>();
        for (int f = 0; f < _edFrames.Count; f++)
            if (ReplaceIn((byte[])_edFrames[f].Px.Clone(), from, to)) changed.Add(f);
        if (changed.Count == 0) { SetStatus("Ese color no aparece en los cuadros."); return; }
        PushUndo(changed.ToArray());
        foreach (int f in changed)
        {
            ReplaceIn(_edFrames[f].Px, from, to);
            Rewrite(f);
        }
        SetStatus(null);
        RefreshPalette();
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

    // ---------------------------------------------------------------- aceptar, cancelar, restablecer
    private void AcceptEditor()
    {
        if (!_editOpen) return;
        try
        {
            int saved = 0;
            for (int f = 0; f < _edFrames.Count; f++)
            {
                var fr = _edFrames[f];
                if (fr.Px.AsSpan().SequenceEqual(fr.Orig)) continue;
                _store.SaveFrame(_skin, _editClip, f, fr.Px, _atlas.GridWidth, _atlas.GridHeight);
                _atlas.SetOverride(_skin, _editClip, f, (byte[])fr.Px.Clone());
                saved++;
            }
            if (saved > 0) _host.ArtChanged();
            CloseEditor();
            DrawingChanged();
            SetStatus(saved > 0 ? $"{saved} cuadros guardados: ya se ven así en el gato." : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SetStatus("No se pudieron guardar los cuadros: " + ex.Message);
        }
    }

    private void CancelEditor()
    {
        if (!_editOpen) return;
        CloseEditor();
        SetStatus(null);
    }

    private void RestoreOriginal()
    {
        if (!_editOpen) return;
        try
        {
            for (int f = 0; f < _edFrames.Count; f++)
            {
                _store.DeleteFrame(_skin, _editClip, f);
                _atlas.SetOverride(_skin, _editClip, f, null);
            }
            _host.ArtChanged();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("No se pudo borrar el retoque: " + ex.Message);
            return;
        }
        LoadEditorFrames();
        RefreshPalette();
        RebuildEditorFrames();
        DrawingChanged();
        SetStatus("Cuadros restablecidos al original.");
    }
}
