using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Swip.App.Art;
using Swip.App.Native;
using Swip.Shared;
using Swip.Shared.Art;
using Swip.Shared.Engine;

namespace Swip.App.Controls;

/// <summary>
/// El Visor (opción del menú de la caja): una ventana emergente con el estilo de las tarjetas de los
/// menús (desenfoque, tinte negro, esquinas redondeadas, filas y botones del mismo estilo) donde se ve
/// cada movimiento del gato como lo anima el motor, con la lista de los que están listos, los que usan
/// una pose prestada y los que aún no tienen arte.
///
/// - El gato avanza solo cuando cambia el cuadro (igual que <c>SwipWorld</c>), sobre un suelo marcado
///   cada 20 casillas: si los pies resbalan sobre las marcas, el avance de ese clip está mal.
/// - Los fps y el avance por cuadro se pueden editar en vivo y copiar para pasarlos a art/clips.json.
/// - Una secuencia encadena movimientos para probar las transiciones.
/// Los datos de estado salen de Assets/atlas/visor.json (tools/art/build_viewer.py); los cuadros, fps y
/// avance, del atlas y de los clips del motor.
/// </summary>
public sealed class VisorWindow : Window
{
    private static VisorWindow? _open;

    /// <summary>True mientras el Visor está abierto (la ventana de los gatos no se reafirma "encima" para no taparlo).</summary>
    public static bool IsOpen => _open is not null;

    /// <summary>Abre el Visor, o lo trae al frente si ya está abierto.</summary>
    public static void ShowOrActivate(bool blur)
    {
        if (_open is not null)
        {
            _open.Activate();
            return;
        }
        _open = new VisorWindow(blur);
        _open.Show();
        _open.Activate();
    }

    // ---------------------------------------------------------------- datos
    internal sealed record VisorClip(string Quality, string Note, string Origin, string Wired, string Desc);
    internal sealed record VisorNeeded(string Group, string Name, int Frames, string Detail);
    internal sealed record VisorPreset(string Name, string[] Clips);
    internal sealed class VisorData
    {
        public Dictionary<string, VisorClip> Clips { get; set; } = new();
        public List<VisorNeeded> Needed { get; set; } = new();
        public List<VisorPreset> Presets { get; set; } = new();
    }

    private static readonly Dictionary<string, string> QualityText = new()
    {
        ["ok"] = "Listo", ["prestada"] = "Pose prestada", ["falta"] = "Falta",
    };

    private static readonly Brush Muted = Frozen(0xFF9AA1AE), Text = Frozen(0xFFE8EAED), White = Frozen(0xFFFFFFFF);
    private static readonly Brush Card = Frozen(0x22FFFFFF), CardSel = Frozen(0x40FFFFFF), Transparent = Brushes.Transparent;
    private static readonly Dictionary<string, Brush> Dots = new()
    {
        ["ok"] = Frozen(0xFF5FD08A), ["prestada"] = Frozen(0xFFFFCC4D), ["falta"] = Frozen(0xFFFF7B72),
    };

    private static Brush Frozen(uint argb)
    {
        var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        b.Freeze();
        return b;
    }

    private readonly CatAtlas _atlas = CatAtlas.Default;
    private readonly ClipLibrary _clips = ClipLibrary.Default;
    private readonly VisorData _data;
    private readonly bool _blur;
    private readonly bool _layered;

    // ---------------------------------------------------------------- interfaz
    private Border _root = null!;
    private StackPanel _list = null!;
    private TextBox _search = null!;
    private WrapPanel _filters = null!;
    private TextBlock _summary = null!;
    private StackPanel _clipView = null!, _missingView = null!, _strip = null!;
    private TextBlock _title = null!, _badge = null!, _desc = null!, _readout = null!, _info = null!;
    private TextBox _fpsBox = null!, _advBox = null!, _promptBox = null!, _loopsBox = null!, _allMissing = null!;
    private TextBlock _missingTitle = null!, _missingDesc = null!;
    private Image _frameImage = null!, _overlayImage = null!;
    private GroundView _ground = null!;
    private Grid _stage = null!;
    private Button _playButton = null!;
    private WrapPanel _chips = null!;
    private readonly Dictionary<string, Button> _toggles = new();
    private readonly List<Border> _thumbs = new();

    // ---------------------------------------------------------------- estado
    private string _filter = "todos", _query = "", _skin = "orange";
    private int _zoom = 3;
    private double _speed = 1;
    private ClipDef? _cur;
    private VisorNeeded? _curMissing;
    private bool _playing = true, _seqOn;
    private double _t;
    private int _tick = -1, _frame, _seqIdx, _doneAt;
    private double _camX;
    private readonly List<string> _seq = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = new();
    private double _holdUntil;
    private string _shownKey = "";

    private VisorWindow(bool blur)
    {
        _blur = blur;
        _data = LoadData();
        _layered = MenuCorners.Choose(Environment.OSVersion.Version.Build, blur) == MenuCornerMode.Layered;

        Title = "Swip · Visor";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = _layered;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var work = SystemParameters.WorkArea;
        Width = Math.Min(1080, work.Width - 40);
        Height = Math.Min(740, work.Height - 40);

        Content = BuildUi();
        Loaded += OnLoaded;
        Closed += (_, _) => { _timer.Stop(); _open = null; };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        _timer.Tick += (_, _) => Step();

        foreach (var id in _data.Presets.FirstOrDefault()?.Clips ?? Array.Empty<string>()) _seq.Add(id);
        RefreshSummary();
        RefreshList();
        RefreshChips();
        _allMissing.Text = MissingText();
        Select(_clips.Contains("walk") ? _clips["walk"] : _clips.All.First());
        _clock.Start();
        _timer.Start();
    }

    private static VisorData LoadData()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/atlas/visor.json"));
            if (info is null) return new VisorData();
            using var reader = new StreamReader(info.Stream);
            return JsonSerializer.Deserialize<VisorData>(reader.ReadToEnd(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new VisorData();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new VisorData();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Igual que los menús: desenfoque y esquinas según la versión de Windows (ver MainWindow.Popup_Opened).
        bool blurred = _blur && BlurHelper.Apply(_root);
        _root.Background = (Brush)Application.Current.FindResource(blurred ? "MenuPanelBlurBrush" : "MenuPanelBrush");
        if (!_layered)
        {
            _root.CornerRadius = new CornerRadius(MenuCorners.DwmRadius);
            BlurHelper.ApplyDwmRoundedCorners(_root);
        }
        else
        {
            _root.CornerRadius = (CornerRadius)Application.Current.FindResource("MenuCornerRadius");
        }
    }

    // ---------------------------------------------------------------- construcción de la interfaz
    private UIElement BuildUi()
    {
        _root = new Border
        {
            Background = (Brush)Application.Current.FindResource("MenuPanelBrush"),
            CornerRadius = (CornerRadius)Application.Current.FindResource("MenuCornerRadius"),
            Padding = new Thickness(12),
        };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.Child = grid;

        // Encabezado arrastrable, como el de cada gato.
        var header = new Border { Style = (Style)Application.Current.FindResource("ListCard"), Cursor = Cursors.SizeAll };
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        var hg = new Grid();
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        titleRow.Children.Add(new TextBlock
        {
            Text = "Visor", Foreground = White, FontWeight = FontWeights.Bold, FontSize = 13,
            FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center,
        });
        _summary = new TextBlock { Foreground = Muted, FontSize = 11, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titleRow.Children.Add(_summary);
        hg.Children.Add(titleRow);
        var close = Pill("Cerrar", () => Close());
        close.Margin = new Thickness(0);
        Grid.SetColumn(close, 1);
        hg.Children.Add(close);
        header.Child = hg;
        grid.Children.Add(header);

        var body = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        Grid.SetRow(body, 1);
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(BuildLeft());
        var right = BuildRight();
        Grid.SetColumn(right, 2);
        body.Children.Add(right);
        grid.Children.Add(body);
        return _root;
    }

    private UIElement BuildLeft()
    {
        var dock = new DockPanel();
        _filters = new WrapPanel();
        foreach (var (key, label) in new[] { ("todos", "Todos"), ("ok", "Listos"), ("prestada", "Prestadas"), ("falta", "Faltan"), ("sinmotor", "Sin comportamiento") })
        {
            var k = key;
            var b = Pill(label, () => { _filter = k; RefreshList(); });
            _toggles["f_" + key] = b;
            _filters.Children.Add(b);
        }
        DockPanel.SetDock(_filters, Dock.Top);
        dock.Children.Add(_filters);

        _search = new TextBox
        {
            Background = Transparent, Foreground = Text, CaretBrush = White, BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 6, 8, 6), FontSize = 12,
        };
        _search.TextChanged += (_, _) => { _query = _search.Text.Trim().ToLowerInvariant(); RefreshList(); };
        var searchBox = new Border { Background = Card, CornerRadius = new CornerRadius(9), Child = _search, Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(searchBox, Dock.Top);
        dock.Children.Add(searchBox);

        _list = new StackPanel();
        dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list });
        return dock;
    }

    private UIElement BuildRight()
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var stack = new StackPanel();
        scroll.Content = stack;

        // ---- vista de un movimiento
        _clipView = new StackPanel();
        var titleLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 6) };
        _badge = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        _title = new TextBlock { Foreground = White, FontWeight = FontWeights.Bold, FontSize = 15, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
        _desc = new TextBlock { Foreground = Muted, FontSize = 11, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        titleLine.Children.Add(_badge);
        titleLine.Children.Add(_title);
        titleLine.Children.Add(_desc);
        _clipView.Children.Add(titleLine);

        _stage = new Grid { Background = Frozen(0xFF3A3E46), ClipToBounds = true };
        _ground = new GroundView();
        _frameImage = new Image { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Stretch = Stretch.None, SnapsToDevicePixels = true };
        _overlayImage = new Image { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Stretch = Stretch.None, IsHitTestVisible = false, SnapsToDevicePixels = true };
        RenderOptions.SetBitmapScalingMode(_frameImage, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetBitmapScalingMode(_overlayImage, BitmapScalingMode.NearestNeighbor);
        _stage.Children.Add(_ground);
        _stage.Children.Add(_frameImage);
        _stage.Children.Add(_overlayImage);
        var stageCard = new Border { CornerRadius = new CornerRadius(9), ClipToBounds = true, Child = _stage, Margin = new Thickness(0, 0, 0, 8) };
        _clipView.Children.Add(stageCard);

        // controles de reproducción
        var play = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        _playButton = Pill("Pausa", () => { _playing = !_playing; });
        play.Children.Add(_playButton);
        play.Children.Add(Pill("Reiniciar", () => { ResetPlayback(); _playing = true; if (_seqOn) { _seqIdx = 0; StartSeqClip(); } }));
        play.Children.Add(Pill("◀", () => { _playing = false; SetFrame((_frame - 1 + FrameCount) % FrameCount); }));
        play.Children.Add(Pill("▶", () => { _playing = false; SetFrame((_frame + 1) % FrameCount); }));
        _readout = new TextBlock { Foreground = Muted, FontSize = 11, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 6) };
        play.Children.Add(_readout);
        _clipView.Children.Add(play);

        var edit = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        _fpsBox = NumberField(60, "fps", edit);
        _advBox = NumberField(150, "avance por cuadro", edit);
        edit.Children.Add(Label("velocidad"));
        edit.Children.Add(Segments(new[] { "¼", "½", "1", "2" }, 2, i => _speed = new[] { 0.25, 0.5, 1, 2 }[i]));
        edit.Children.Add(Label("zoom"));
        edit.Children.Add(Segments(new[] { "1", "2", "3", "4", "6" }, 2, i => { _zoom = new[] { 1, 2, 3, 4, 6 }[i]; _shownKey = ""; }));
        edit.Children.Add(Label("gato"));
        edit.Children.Add(Segments(new[] { "naranja", "gris" }, 0, i => { _skin = i == 0 ? "orange" : "gray"; _shownKey = ""; BuildStrip(); }));
        _clipView.Children.Add(edit);

        var flags = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        flags.Children.Add(Flag("suelo avanza", "mov", true));
        flags.Children.Add(Flag("marcos", "zones", false));
        flags.Children.Add(Flag("cuadrícula", "cells", false));
        flags.Children.Add(Flag("espejo", "mirror", false));
        flags.Children.Add(Flag("repetir", "rep", true));
        flags.Children.Add(Pill("Copiar fps y avance", CopySettings));
        _clipView.Children.Add(flags);

        _strip = new StackPanel { Orientation = Orientation.Horizontal };
        _clipView.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _strip, Margin = new Thickness(0, 0, 0, 6),
        });

        // secuencia
        var seqBorder = NewCard("Secuencia (prueba de transiciones)", out var seqCard);
        var seqRow = new WrapPanel();
        foreach (var p in _data.Presets)
        {
            var preset = p;
            seqRow.Children.Add(Pill(preset.Name, () => { _seq.Clear(); _seq.AddRange(preset.Clips); RefreshChips(); }));
        }
        seqCard.Children.Add(seqRow);
        var seqRow2 = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        seqRow2.Children.Add(Pill("Reproducir secuencia", () =>
        {
            if (_seq.Count == 0) return;
            _seqOn = true; _seqIdx = 0; _playing = true; StartSeqClip();
        }));
        seqRow2.Children.Add(Pill("Añadir el movimiento actual", () => { if (_cur is not null) { _seq.Add(_cur.Id); RefreshChips(); } }));
        seqRow2.Children.Add(Pill("Vaciar", () => { _seq.Clear(); _seqOn = false; RefreshChips(); }));
        seqRow2.Children.Add(Label("vueltas de los que se repiten"));
        _loopsBox = NumberField(40, null, seqRow2);
        _loopsBox.Text = "2";
        seqCard.Children.Add(seqRow2);
        _chips = new WrapPanel();
        seqCard.Children.Add(_chips);
        seqCard.Children.Add(new TextBlock
        {
            Text = "Pasa de un movimiento al siguiente como lo haría el motor: el gato avanza solo cuando cambia el cuadro.",
            Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 0, 0),
        });
        _clipView.Children.Add(seqBorder);

        var infoBorder = NewCard("Ficha", out var infoCard);
        _info = new TextBlock { Foreground = Text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 0) };
        infoCard.Children.Add(_info);
        _clipView.Children.Add(infoBorder);
        stack.Children.Add(_clipView);

        // ---- vista de una acción que falta
        _missingView = new StackPanel { Visibility = Visibility.Collapsed };
        var mBorder = NewCard("", out var mCard);
        _missingTitle = new TextBlock { Foreground = White, FontWeight = FontWeights.Bold, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 6) };
        _missingDesc = new TextBlock { Foreground = Text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 8) };
        mCard.Children.Add(_missingTitle);
        mCard.Children.Add(_missingDesc);
        mCard.Children.Add(new TextBlock { Text = "Texto para pedirlo a Gemini:", Foreground = Muted, FontSize = 11, Margin = new Thickness(2, 0, 0, 4) });
        _promptBox = ReadOnlyBox(110);
        mCard.Children.Add(_promptBox);
        var mb = Pill("Copiar texto", () => CopyText(_promptBox.Text));
        mb.HorizontalAlignment = HorizontalAlignment.Left;
        mCard.Children.Add(mb);
        _missingView.Children.Add(mBorder);
        stack.Children.Add(_missingView);

        // ---- lista completa de lo que falta
        var allBorder = NewCard("Lista de lo que falta (para pedir a Gemini)", out var allCard);
        _allMissing = ReadOnlyBox(150);
        allCard.Children.Add(_allMissing);
        var allBtn = Pill("Copiar la lista", () => CopyText(_allMissing.Text));
        allBtn.HorizontalAlignment = HorizontalAlignment.Left;
        allCard.Children.Add(allBtn);
        stack.Children.Add(allBorder);
        return scroll;
    }

    // ---------------------------------------------------------------- piezas con el estilo de las tarjetas
    private static Style RowStyle => (Style)Application.Current.FindResource("ListRow");

    private Button Pill(string text, Action click)
    {
        var b = new Button
        {
            Content = text, Style = RowStyle, Padding = new Thickness(10, 5, 10, 5), FontSize = 11,
            Margin = new Thickness(0, 0, 6, 6),
        };
        Rest(b, Card);
        AttachHover(b);
        b.Click += (_, _) => click();
        return b;
    }

    private TextBlock Label(string text) => new()
    {
        Text = text, Foreground = Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 6),
    };

    /// <summary>Botones en fila de los que solo uno está activo (en el color de énfasis de Windows).</summary>
    private UIElement Segments(string[] labels, int selected, Action<int> changed)
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
            var b = new Button { Content = labels[i], Style = RowStyle, Padding = new Thickness(9, 5, 9, 5), FontSize = 11, Margin = new Thickness(0, 0, 2, 0) };
            AttachHover(b);
            b.Click += (_, _) => { Repaint(index); changed(index); };
            buttons.Add(b);
            panel.Children.Add(b);
        }
        Repaint(selected);
        return panel;
    }

    private static readonly Brush HoverBrush = Frozen(0x40FFFFFF);

    /// <summary>
    /// Fondo en reposo de un botón. Como se fija como valor local, el resalte del estilo ListRow (que es un
    /// disparador) ya no actúa: <see cref="AttachHover"/> lo reproduce.
    /// </summary>
    private static void Rest(Button b, Brush brush)
    {
        b.Tag = brush;
        b.Background = brush;
    }

    private static void AttachHover(Button b)
    {
        b.MouseEnter += (_, _) => b.Background = ReferenceEquals(b.Tag, Application.Current.FindResource("AccentBrush"))
            ? (Brush)Application.Current.FindResource("AccentHoverBrush")
            : HoverBrush;
        b.MouseLeave += (_, _) => b.Background = b.Tag as Brush ?? Transparent;
    }

    private static void Paint(Button b, bool on)
    {
        Rest(b, on ? (Brush)Application.Current.FindResource("AccentBrush") : Card);
        b.Foreground = on ? (Brush)Application.Current.FindResource("AccentFgBrush") : Text;
    }

    private UIElement Flag(string label, string key, bool initial)
    {
        var b = new Button { Content = label, Style = RowStyle, Padding = new Thickness(10, 5, 10, 5), FontSize = 11, Margin = new Thickness(0, 0, 6, 6) };
        _flags[key] = initial;
        AttachHover(b);
        Paint(b, initial);
        b.Click += (_, _) => { _flags[key] = !_flags[key]; Paint(b, _flags[key]); _shownKey = ""; };
        return b;
    }
    private readonly Dictionary<string, bool> _flags = new();
    private bool On(string key) => _flags.TryGetValue(key, out var v) && v;

    private TextBox NumberField(double width, string? label, Panel parent)
    {
        if (label is not null) parent.Children.Add(Label(label));
        var tb = new TextBox
        {
            Width = width, Background = Transparent, Foreground = Text, CaretBrush = White, BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 5, 6, 5), FontSize = 11, FontFamily = new FontFamily("Consolas"),
        };
        parent.Children.Add(new Border { Background = Card, CornerRadius = new CornerRadius(9), Child = tb, Margin = new Thickness(0, 0, 8, 6) });
        return tb;
    }

    private TextBox ReadOnlyBox(double height) => new()
    {
        IsReadOnly = true, Height = height, Background = Card, Foreground = Text, BorderThickness = new Thickness(0), Padding = new Thickness(8),
        FontFamily = new FontFamily("Consolas"), FontSize = 11, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 6),
    };

    /// <summary>Tarjeta (fondo suave, esquinas redondeadas) con un título opcional; devuelve el borde para
    /// añadirlo al padre y, en <paramref name="panel"/>, el contenedor donde se pone su contenido.</summary>
    private static Border NewCard(string title, out StackPanel panel)
    {
        panel = new StackPanel();
        if (title.Length > 0)
            panel.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), Foreground = Muted, FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 6) });
        return new Border { Background = Card, CornerRadius = new CornerRadius(9), Padding = new Thickness(10), Margin = new Thickness(0, 4, 0, 6), Child = panel };
    }

    // ---------------------------------------------------------------- lista
    private void RefreshSummary()
    {
        int ok = 0, prestada = 0;
        foreach (var c in _clips.All) { if (QualityOf(c.Id) == "ok") ok++; else prestada++; }
        _summary.Text = $"{ok} listos · {prestada} con pose prestada · {_data.Needed.Count} sin arte";
    }

    private string QualityOf(string clip) => _data.Clips.TryGetValue(clip, out var c) ? c.Quality : "prestada";
    private VisorClip? InfoOf(string clip) => _data.Clips.TryGetValue(clip, out var c) ? c : null;

    private bool Matches(string name, string detail, bool isClip, string quality, bool wired)
    {
        if (_query.Length > 0 && !name.ToLowerInvariant().Contains(_query) && !detail.ToLowerInvariant().Contains(_query)) return false;
        return _filter switch
        {
            "todos" => true,
            "sinmotor" => isClip && !wired,
            "falta" => !isClip,
            _ => isClip && quality == _filter,
        };
    }

    private void RefreshList()
    {
        foreach (var (key, b) in _toggles)
            if (key.StartsWith("f_", StringComparison.Ordinal)) Paint(b, key == "f_" + _filter);
        _list.Children.Clear();

        var missing = _data.Needed.Where(n => Matches(n.Name, n.Detail, false, "falta", false)).ToList();
        if (missing.Count > 0)
        {
            _list.Children.Add(GroupLabel("Faltan (sin arte)"));
            foreach (var n in missing)
            {
                var item = n;
                _list.Children.Add(ListItem(item.Name, "falta", item.Group, () => SelectMissing(item), _curMissing == item));
            }
        }
        foreach (var (quality, label) in new[] { ("ok", "Listos"), ("prestada", "Con pose prestada (mejorables)") })
        {
            var cs = _clips.All.Where(c => QualityOf(c.Id) == quality
                && Matches(c.Id, InfoOf(c.Id)?.Desc ?? c.Description, true, quality, !string.IsNullOrEmpty(InfoOf(c.Id)?.Wired))).ToList();
            if (cs.Count == 0) continue;
            _list.Children.Add(GroupLabel($"{label} · {cs.Count}"));
            foreach (var c in cs)
            {
                var clip = c;
                bool wired = !string.IsNullOrEmpty(InfoOf(clip.Id)?.Wired);
                _list.Children.Add(ListItem(clip.Id, quality, wired ? "" : "sin motor", () => Select(clip), _cur?.Id == clip.Id));
            }
        }
    }

    private static TextBlock GroupLabel(string text) => new()
    {
        Text = text.ToUpperInvariant(), Foreground = Muted, FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 10, 0, 4),
    };

    private UIElement ListItem(string name, string quality, string tag, Action click, bool selected)
    {
        var b = new Button { Style = RowStyle, Margin = new Thickness(0, 0, 0, 2), Padding = new Thickness(8, 6, 8, 6), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Rest(b, selected ? CardSel : Transparent);
        AttachHover(b);
        var g = new Grid { Width = 248 };   // el estilo ListRow no estira el contenido: ancho fijo de la lista
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = Dots[quality], Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        var n = new TextBlock { Text = name, Foreground = Text, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(n, 1);
        g.Children.Add(n);
        if (tag.Length > 0)
        {
            var t = new TextBlock { Text = tag, Foreground = Muted, FontSize = 10, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(t, 2);
            g.Children.Add(t);
        }
        b.Content = g;
        b.Click += (_, _) => click();
        return b;
    }

    // ---------------------------------------------------------------- selección
    private int FrameCount => _cur?.FrameCount ?? 1;

    private void Select(ClipDef clip)
    {
        _cur = clip; _curMissing = null; _seqOn = false;
        _clipView.Visibility = Visibility.Visible; _missingView.Visibility = Visibility.Collapsed;
        ShowClipInfo(clip);
        ResetPlayback();
        BuildStrip();
        RefreshList();
        RefreshChips();
    }

    private void ShowClipInfo(ClipDef clip)
    {
        var info = InfoOf(clip.Id);
        string q = QualityOf(clip.Id);
        _badge.Text = QualityText[q];
        _badge.Foreground = Dots[q];
        _title.Text = clip.Id;
        _desc.Text = info?.Desc ?? clip.Description;
        _fpsBox.Text = clip.Fps.ToString("0.##", CultureInfo.InvariantCulture);
        _advBox.Text = string.Join(",", clip.Advance is { Length: > 0 } a ? a : new[] { 0 });
        var sb = new StringBuilder();
        sb.AppendLine($"Cuadros: {clip.FrameCount} · {(clip.Loop ? "bucle" : "una vez")} · {clip.Fps:0.##} fps · {clip.Length:0.00} s");
        if (info is not null)
        {
            sb.AppendLine($"Origen del arte: {info.Origin}");
            sb.AppendLine(info.Wired.Length > 0
                ? $"Motor: lo usa el comportamiento «{info.Wired}»"
                : "Motor: tiene arte pero ningún comportamiento lo usa todavía");
            if (info.Note.Length > 0) sb.AppendLine($"Qué mejorar: {info.Note}");
        }
        _info.Text = sb.ToString().TrimEnd();
    }

    private void SelectMissing(VisorNeeded n)
    {
        _curMissing = n; _cur = null; _seqOn = false;
        _clipView.Visibility = Visibility.Collapsed; _missingView.Visibility = Visibility.Visible;
        _missingTitle.Text = $"{n.Name} · {n.Group}";
        _missingDesc.Text = n.Detail;
        _promptBox.Text = PromptFor(n);
        RefreshList();
    }

    private static string PromptFor(VisorNeeded n) =>
        "Same cat, same flat 2D pixel-art style, same palette, same scale and same baseline as the attached sheet.\n" +
        $"One row, {n.Frames} frames side by side, magenta #FF00FF background, no grid lines, no shadows, no text, at least 12% empty margin around every frame (tail, ears and paws never touch the cell edge).\n" +
        "Smooth animation: consecutive frames differ by a SMALL change; where the motion is big add intermediate frames.\n" +
        "Strict profile facing RIGHT unless said otherwise. Black outline only on the outer silhouette. Collar with red heart tag visible when the neck is visible.\n" +
        $"Action: {n.Name}. {n.Detail}";

    private string MissingText()
    {
        var sb = new StringBuilder("ACCIONES QUE FALTAN (sin arte propio)\n");
        foreach (var g in _data.Needed.GroupBy(n => n.Group))
        {
            sb.Append('\n').AppendLine(g.Key);
            foreach (var n in g) sb.AppendLine($"  - {n.Name} ({n.Frames} cuadros): {n.Detail}");
        }
        sb.AppendLine("\nMOVIMIENTOS QUE USAN UNA POSE PRESTADA (conviene dibujarlos)");
        foreach (var c in _clips.All.Where(c => QualityOf(c.Id) == "prestada"))
            sb.AppendLine($"  - {c.Id}: {InfoOf(c.Id)?.Note}");
        return sb.ToString();
    }

    private void BuildStrip()
    {
        _strip.Children.Clear();
        _thumbs.Clear();
        if (_cur is null) return;
        for (int i = 0; i < _cur.FrameCount; i++)
        {
            int index = i;
            var img = new Image { Source = _atlas.Frame(_skin, _cur.Id, i), Stretch = Stretch.Uniform, Width = _atlas.GridWidth * 0.8, Height = _atlas.GridHeight * 0.8 };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            var grid = new Grid();
            grid.Children.Add(img);
            grid.Children.Add(new TextBlock { Text = (i + 1).ToString(CultureInfo.InvariantCulture), Foreground = White, FontSize = 9, Margin = new Thickness(2, 0, 0, 0) });
            var border = new Border { Background = Frozen(0xFF3A3E46), BorderThickness = new Thickness(2), BorderBrush = Transparent, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 4, 0), Child = grid, Cursor = Cursors.Hand };
            border.MouseLeftButtonUp += (_, _) => { _playing = false; SetFrame(index); };
            _strip.Children.Add(border);
            _thumbs.Add(border);
        }
    }

    private void RefreshChips()
    {
        _chips.Children.Clear();
        if (_seq.Count == 0)
        {
            _chips.Children.Add(new TextBlock { Text = "Vacía: añade movimientos o elige una secuencia de ejemplo.", Foreground = Muted, FontSize = 11 });
            return;
        }
        for (int i = 0; i < _seq.Count; i++)
        {
            int index = i;
            var chip = new Button
            {
                Content = $"{i + 1}. {_seq[i]}  ×", Style = RowStyle, Padding = new Thickness(9, 4, 9, 4), FontSize = 11,
                Margin = new Thickness(0, 0, 6, 6),
            };
            AttachHover(chip);
            Paint(chip, _seqOn && _seqIdx == i);
            chip.Click += (_, _) => { _seq.RemoveAt(index); RefreshChips(); };
            _chips.Children.Add(chip);
        }
    }

    // ---------------------------------------------------------------- reproducción (como el motor)
    private void ResetPlayback() { _t = 0; _tick = -1; _holdUntil = 0; _frame = 0; }

    private double Fps => double.TryParse(_fpsBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? Math.Min(v, 120) : (_cur?.Fps ?? 8);

    private int[] Advance()
    {
        var list = new List<int>();
        foreach (var part in _advBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) list.Add(v);
        return list.Count > 0 ? list.ToArray() : new[] { 0 };
    }

    private int FrameOfTick(int tick)
    {
        int n = FrameCount;
        if (n <= 1 || tick <= 0) return 0;
        return _cur!.Loop ? tick % n : Math.Min(tick, n - 1);
    }

    private void SetFrame(int i)
    {
        _frame = i;
        _t = (i + 0.01) / Fps;
        _tick = i;
    }

    private void StartSeqClip()
    {
        if (_seq.Count == 0) { _seqOn = false; return; }
        var id = _seq[_seqIdx % _seq.Count];
        if (!_clips.Contains(id)) { _seqIdx = (_seqIdx + 1) % _seq.Count; return; }
        var clip = _clips[id];
        double keep = _camX;
        bool changed = _cur?.Id != clip.Id;
        _cur = clip; _curMissing = null;
        _clipView.Visibility = Visibility.Visible; _missingView.Visibility = Visibility.Collapsed;
        ShowClipInfo(clip);
        if (changed) BuildStrip();
        ResetPlayback();
        _camX = keep;
        int loops = int.TryParse(_loopsBox.Text, out int l) ? Math.Clamp(l, 1, 10) : 1;
        _doneAt = clip.Loop ? clip.FrameCount * loops : clip.FrameCount;
        RefreshChips();
        RefreshList();
    }

    private double _last;

    private void Step()
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Min(0.1, now - _last);
        _last = now;
        if (_cur is null) return;

        double fps = Fps;
        if (_playing && now > _holdUntil)
        {
            _t += dt * _speed;
            int tick = (int)Math.Floor(_t * fps);
            var adv = Advance();
            while (_tick < tick)
            {
                _tick++;
                int f = FrameOfTick(_tick);
                if (On("mov") && _tick > 0) _camX += adv[f % adv.Length];
                if (_seqOn && _tick >= _doneAt)
                {
                    _seqIdx = (_seqIdx + 1) % _seq.Count;
                    if (_seqIdx == 0 && !On("rep")) { _seqOn = false; _playing = false; RefreshChips(); break; }
                    StartSeqClip();
                    break;
                }
                if (!_seqOn && !_cur.Loop && _tick >= _cur.FrameCount)
                {
                    if (On("rep")) { _holdUntil = now + 0.6; ResetPlayback(); }
                    else _playing = false;
                    break;
                }
            }
        }

        _frame = FrameOfTick(Math.Max(0, _tick));
        if (_playButton.Content as string != (_playing ? "Pausa" : "Reproducir"))
            _playButton.Content = _playing ? "Pausa" : "Reproducir";
        for (int i = 0; i < _thumbs.Count; i++)
            _thumbs[i].BorderBrush = i == _frame ? (Brush)Application.Current.FindResource("AccentBrush") : Transparent;
        var a = Advance();
        double speed = a.Average() * fps;
        _readout.Text = $"cuadro {_frame + 1}/{FrameCount} · avance {a[_frame % a.Length]} · velocidad {speed:0} casillas/s";
        Draw();
    }

    private void Draw()
    {
        if (_cur is null) return;
        int gw = _atlas.GridWidth, gh = _atlas.GridHeight;
        bool zones = On("zones") || On("cells");
        string key = $"{_skin}|{_cur.Id}|{_frame}|{_zoom}|{On("mirror")}|{zones}|{On("cells")}";
        if (key != _shownKey)
        {
            _shownKey = key;
            _frameImage.Source = _atlas.Frame(_skin, _cur.Id, _frame, On("mirror"), _zoom);
            _frameImage.Width = gw * _zoom; _frameImage.Height = gh * _zoom;
            _overlayImage.Source = zones
                ? ZoneOverlay.Get("cat", gw, gh, _atlas.ExtraZones, _atlas.Main, _zoom, cellLines: On("cells") && _zoom >= 4)
                : null;
            _overlayImage.Width = gw * _zoom; _overlayImage.Height = gh * _zoom;
            _stage.Height = gh * _zoom + 26;
        }
        _ground.Set(_camX, _zoom, _atlas.BaseRows, gw);
    }

    private void CopySettings()
    {
        if (_cur is null) return;
        CopyText($"{{\"id\":\"{_cur.Id}\",\"fps\":{Fps.ToString("0.##", CultureInfo.InvariantCulture)},\"advance\":[{string.Join(",", Advance())}]}}");
    }

    private static void CopyText(string text)
    {
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException) { /* el portapapeles lo tiene otra aplicación */ }
    }

    // ---------------------------------------------------------------- suelo con marcas
    /// <summary>Línea del suelo con marcas cada 20 casillas que se desplazan con el avance del gato.</summary>
    private sealed class GroundView : FrameworkElement
    {
        private double _camX, _baseRows, _cellW;
        private int _zoom = 3;
        private bool _ready;
        private static readonly Pen LinePen = MakePen(0x59FFFFFF);
        private static readonly Brush LabelBrush = Frozen(0x8CFFFFFF);

        private static Pen MakePen(uint argb)
        {
            var p = new Pen(Frozen(argb), 1);
            p.Freeze();
            return p;
        }

        public void Set(double camX, int zoom, double baseRows, double cellW)
        {
            if (_ready && camX == _camX && zoom == _zoom) return;
            _ready = true;
            _camX = camX; _zoom = zoom; _baseRows = baseRows; _cellW = cellW;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, gy = Math.Round(_baseRows * _zoom) + 0.5;
            dc.DrawLine(LinePen, new Point(0, gy), new Point(w, gy));
            double centre = w / 2;
            int first = (int)Math.Floor((_camX - centre / _zoom) / 20) - 1, last = (int)Math.Ceiling((_camX + centre / _zoom) / 20) + 1;
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            for (int k = first; k <= last; k++)
            {
                double x = Math.Round(centre + (k * 20 - _camX) * _zoom) + 0.5;
                if (x < 0 || x > w) continue;
                bool big = k % 5 == 0;
                dc.DrawLine(LinePen, new Point(x, gy), new Point(x, gy + (big ? 12 : 6)));
                if (big)
                {
                    var text = new FormattedText((k * 20).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, LabelBrush, pixelsPerDip);
                    dc.DrawText(text, new Point(x + 3, gy + 8));
                }
            }
        }
    }
}
