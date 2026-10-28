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
using static Swip.App.Controls.VisorUi;

namespace Swip.App.Controls;

/// <summary>Lo que el Visor necesita de la ventana de los gatos: el tamaño de las piezas y avisar de los retoques.</summary>
public interface IVisorHost
{
    /// <summary>Px lógicos por casilla del dibujo (<see cref="ArtScale.CellSize"/>).</summary>
    double CellSize { get; }

    /// <summary>Cambia el tamaño de las piezas: se guarda en los ajustes compartidos y se aplica en todas las sesiones.</summary>
    void SetCellSize(double value);

    /// <summary>Se retocó un cuadro o un tiempo (ya guardado en la carpeta compartida): hay que releerlo y avisar a las demás sesiones.</summary>
    void ArtChanged();
}

/// <summary>
/// El Visor (opción del menú de la caja): una ventana emergente con el estilo de las tarjetas de los menús.
/// Tiene una pestaña por gato. A la izquierda, las acciones básicas y debajo las transiciones entre ellas; a la
/// derecha (75 % del ancho), el visor:
///   - al elegir una acción se ve esa acción; al elegir otra acción básica se ve la transición desde la anterior
///     y se termina en la elegida (varias transiciones encadenadas si hace falta);
///   - se puede cambiar el tiempo de cada transición (y el avance por cuadro de cada clip);
///   - se puede colorear y corregir el dibujo cuadro a cuadro (editor de píxeles, ver VisorWindow.Editor.cs);
///   - se puede cambiar el tamaño de las piezas.
/// El gato avanza solo cuando cambia el cuadro (igual que <c>SwipWorld</c>), sobre un suelo marcado cada 20 casillas:
/// si los pies resbalan sobre las marcas, el avance de ese clip está mal.
/// Los datos de estado salen de Assets/atlas/visor.json (tools/art/build_viewer.py); los cuadros, fps y avance, del
/// atlas y de los clips del motor.
/// </summary>
public sealed partial class VisorWindow : Window
{
    private static VisorWindow? _open;

    /// <summary>True mientras el Visor está abierto (la ventana de los gatos no se reafirma "encima" para no taparlo).</summary>
    public static bool IsOpen => _open is not null;

    /// <summary>
    /// Pausa o reanuda la animación del Visor abierto: con la sesión fuera de pantalla (otro usuario, pantalla bloqueada)
    /// nadie lo ve, así que no hace falta dibujar. No cierra nada: no se pierden los retoques sin guardar.
    /// </summary>
    public static void SetActive(bool active)
    {
        if (_open is null) return;
        if (active) _open._timer.Start();
        else _open._timer.Stop();
    }

    /// <summary>Abre el Visor, o lo trae al frente si ya está abierto.</summary>
    public static void ShowOrActivate(bool blur, IVisorHost host)
    {
        if (_open is not null)
        {
            _open.Activate();
            return;
        }
        _open = new VisorWindow(blur, host);
        _open.Show();
        _open.Activate();
    }

    // ---------------------------------------------------------------- datos
    internal sealed record VisorClip(string Quality, string Note, string Origin, string Wired, string Desc);
    internal sealed record VisorNeeded(string Group, string Name, int Frames, string Detail);
    internal sealed record VisorAction(string Id, string Name, string Clip);
    internal sealed class VisorData
    {
        public Dictionary<string, VisorClip> Clips { get; set; } = new();
        public List<VisorNeeded> Needed { get; set; } = new();
        public List<VisorAction> Actions { get; set; } = new();
        public List<TransitionEdge> Transitions { get; set; } = new();
        public List<string[]> Direct { get; set; } = new();
        public List<string[]> MissingTransitions { get; set; } = new();
    }

    private static readonly Dictionary<string, string> QualityText = new()
    {
        ["ok"] = "Listo", ["prestada"] = "Pose prestada", ["falta"] = "Falta",
    };

    private readonly CatAtlas _atlas = CatAtlas.Default;
    private readonly ClipLibrary _clips = ClipLibrary.Default;
    private readonly ArtStore _store = new();
    private readonly IVisorHost _host;
    private readonly VisorData _data;
    private readonly bool _blur;
    private readonly bool _layered;
    private Dictionary<string, ClipTimingEntry> _timing = new();

    // ---------------------------------------------------------------- interfaz
    private Border _root = null!;
    private StackPanel _list = null!;
    private TextBox _search = null!;
    private TextBlock _summary = null!;
    private StackPanel _clipView = null!, _missingView = null!, _strip = null!, _timingRows = null!, _tabs = null!;
    private TextBlock _title = null!, _badge = null!, _desc = null!, _readout = null!, _info = null!, _chain = null!;
    private TextBlock _missingTitle = null!, _missingDesc = null!, _sizeInfo = null!, _status = null!;
    private TextBox _promptBox = null!, _allMissing = null!, _sizeBox = null!;
    private Image _frameImage = null!, _overlayImage = null!;
    private GroundView _ground = null!;
    private Grid _stage = null!;
    private Button _playButton = null!;
    private StackPanel _sizeSegments = null!;
    private readonly Dictionary<string, Button> _filters = new();
    private readonly List<Border> _thumbs = new();
    private readonly Dictionary<string, bool> _flags = new();

    // ---------------------------------------------------------------- estado
    private string _skin = CatSkins.Orange;
    private string _query = "";
    private int _zoom = 4;
    private double _speed = 1;
    private string? _action;                        // acción básica en la que está el gato (destino de la reproducción)
    private VisorNeeded? _curMissing;
    private string? _curMissingTransition;
    private string? _selectedKey;                   // fila seleccionada de la lista
    private bool _playing = true;
    private double _t, _holdUntil, _last;
    private int _tick = -1, _frame, _stepIdx;
    private double _camX;
    private string _shownKey = "";
    private string _stripClip = "";

    private sealed record PlanStep(string Clip, bool Reverse, bool Loop, string Label);
    private List<PlanStep> _plan = new();

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = new();

    private VisorWindow(bool blur, IVisorHost host)
    {
        _blur = blur;
        _host = host;
        _data = LoadData();
        _layered = MenuCorners.Choose(Environment.OSVersion.Version.Build, blur) == MenuCornerMode.Layered;
        _timing = _store.LoadTiming(_clips);

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
        Width = Math.Min(1560, work.Width - 40);
        Height = Math.Min(920, work.Height - 40);

        Content = BuildUi();
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += (_, _) => { _timer.Stop(); _open = null; };
        KeyDown += OnKeyDown;
        _timer.Tick += (_, _) => Step();

        RefreshSummary();
        RefreshList();
        _allMissing.Text = MissingText();
        UpdateSizeInfo();
        var first = _data.Actions.FirstOrDefault(a => a.Id == "walk") ?? _data.Actions.FirstOrDefault();
        if (first is not null) ShowAction(first.Id);
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

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;   // en un cuadro de texto, Esc y Ctrl+Z son del propio cuadro
        if (e.Key == Key.Escape) Close();
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && _editOpen) Undo();
    }

    private bool _closeWarned;

    /// <summary>Con cambios del editor sin guardar, el primer intento de cerrar avisa; el segundo los descarta.</summary>
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_editOpen && _editDirty && !_closeWarned)
        {
            _closeWarned = true;
            e.Cancel = true;
            SetStatus("Hay cambios del cuadro sin guardar. Pulsa Cerrar otra vez para descartarlos, o guárdalos antes.");
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

        // Encabezado arrastrable, como el de cada gato; con las dos pestañas (una por gato).
        var header = new Border { Style = (Style)Application.Current.FindResource("ListCard"), Cursor = Cursors.SizeAll };
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        var hg = new Grid();
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock
        {
            Text = "Visor", Foreground = White, FontWeight = FontWeights.Bold, FontSize = 13,
            FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0),
        };
        hg.Children.Add(title);
        _tabs = Segments(new[] { "Gato naranja", "Gato gris" }, 0, i =>
        {
            if (_editDirty) { SetStatus("Guarda o descarta los cambios del cuadro antes de cambiar de gato."); Select(_tabs!, SkinIndex()); return; }
            _skin = i == 0 ? CatSkins.Orange : CatSkins.Gray;
            _shownKey = "";
            BuildStrip();
            if (_editOpen) LoadEditorFrame(_editClip, _editFrame);
            RefreshTimingRows();
        });
        _tabs.Margin = new Thickness(0);
        Grid.SetColumn(_tabs, 1);
        hg.Children.Add(_tabs);
        _summary = new TextBlock { Foreground = Muted, FontSize = 11, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_summary, 2);
        hg.Children.Add(_summary);
        var close = Pill("Cerrar", () => Close());
        close.Margin = new Thickness(0);
        Grid.SetColumn(close, 3);
        hg.Children.Add(close);
        header.Child = hg;
        grid.Children.Add(header);

        // Cuerpo: la lista ocupa el 25 % y el visor el 75 %.
        var body = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        Grid.SetRow(body, 1);
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        body.Children.Add(BuildLeft());
        var right = BuildRight();
        Grid.SetColumn(right, 2);
        body.Children.Add(right);
        grid.Children.Add(body);
        return _root;
    }

    private int SkinIndex() => _skin == CatSkins.Gray ? 1 : 0;

    private UIElement BuildLeft()
    {
        var dock = new DockPanel();
        var (searchFrame, search) = Field(100, mono: false);
        _search = search;
        _search.Width = double.NaN;
        _search.TextChanged += (_, _) => { _query = _search.Text.Trim().ToLowerInvariant(); RefreshList(); };
        searchFrame.Margin = new Thickness(0, 0, 0, 6);
        DockPanel.SetDock(searchFrame, Dock.Top);
        dock.Children.Add(searchFrame);
        _list = new StackPanel();
        dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list });
        return dock;
    }

    private UIElement BuildRight()
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var stack = new StackPanel();
        scroll.Content = stack;

        // ---- vista de una acción o transición
        _clipView = new StackPanel();
        var titleLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 2) };
        _badge = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        _title = new TextBlock { Foreground = White, FontWeight = FontWeights.Bold, FontSize = 15, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
        _desc = new TextBlock { Foreground = Muted, FontSize = 11, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        titleLine.Children.Add(_badge);
        titleLine.Children.Add(_title);
        titleLine.Children.Add(_desc);
        _clipView.Children.Add(titleLine);
        _chain = new TextBlock { Foreground = Muted, FontSize = 11, Margin = new Thickness(2, 0, 0, 6), TextWrapping = TextWrapping.Wrap };
        _clipView.Children.Add(_chain);

        _stage = new Grid { Background = Stage, ClipToBounds = true };
        _ground = new GroundView();
        _frameImage = new Image { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Stretch = Stretch.Fill, SnapsToDevicePixels = true };
        _overlayImage = new Image { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Stretch = Stretch.None, IsHitTestVisible = false, SnapsToDevicePixels = true };
        RenderOptions.SetBitmapScalingMode(_frameImage, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetBitmapScalingMode(_overlayImage, BitmapScalingMode.NearestNeighbor);
        _stage.Children.Add(_ground);
        _stage.Children.Add(_frameImage);
        _stage.Children.Add(_overlayImage);
        _clipView.Children.Add(new Border { CornerRadius = new CornerRadius(9), ClipToBounds = true, Child = _stage, Margin = new Thickness(0, 0, 0, 8) });

        var play = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        _playButton = Pill("Pausa", () => { _playing = !_playing; });
        play.Children.Add(_playButton);
        play.Children.Add(Pill("Reiniciar", () => { _stepIdx = 0; ResetStep(); _playing = true; }));
        play.Children.Add(Pill("◀", () => { _playing = false; SetFrame((_frame - 1 + CurFrameCount) % CurFrameCount); }));
        play.Children.Add(Pill("▶", () => { _playing = false; SetFrame((_frame + 1) % CurFrameCount); }));
        play.Children.Add(Label("velocidad"));
        play.Children.Add(Segments(new[] { "¼", "½", "1", "2" }, 2, i => _speed = new[] { 0.25, 0.5, 1, 2 }[i]));
        play.Children.Add(Label("zoom"));
        play.Children.Add(Segments(new[] { "2", "3", "4", "5", "6" }, 2, i => { _zoom = new[] { 2, 3, 4, 5, 6 }[i]; _shownKey = ""; }));
        play.Children.Add(Flag("suelo avanza", "mov", true));
        play.Children.Add(Flag("espejo", "mirror", false));
        play.Children.Add(Flag("marcos", "zones", false));
        _readout = new TextBlock { Foreground = Muted, FontSize = 11, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 6) };
        play.Children.Add(_readout);
        _clipView.Children.Add(play);

        _strip = new StackPanel { Orientation = Orientation.Horizontal };
        _clipView.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _strip, Margin = new Thickness(0, 0, 0, 6),
        });

        // tiempos
        var timingBorder = NewCard("Tiempos (se guardan y valen para los dos gatos)", out var timingCard);
        _timingRows = new StackPanel();
        timingCard.Children.Add(_timingRows);
        timingCard.Children.Add(new TextBlock
        {
            Text = "Cambia los fps o la duración (segundos que dura el clip entero) y pulsa Intro. El avance son las casillas que " +
                   "avanza el gato al mostrar cada cuadro, separadas por comas.",
            Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 2, 0, 0),
        });
        _clipView.Children.Add(timingBorder);

        // editor de píxeles (Editor.cs) y tamaño de las piezas
        _clipView.Children.Add(BuildEditorCard());
        _clipView.Children.Add(BuildSizeCard());

        var infoBorder = NewCard("Ficha", out var infoCard);
        _info = new TextBlock { Foreground = Text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 0) };
        infoCard.Children.Add(_info);
        _clipView.Children.Add(infoBorder);
        _status = new TextBlock { Foreground = Frozen(0xFFFF8A65), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 0, 6), Visibility = Visibility.Collapsed };
        _clipView.Children.Add(_status);
        stack.Children.Add(_clipView);

        // ---- vista de algo que falta
        _missingView = new StackPanel { Visibility = Visibility.Collapsed };
        var mBorder = NewCard("", out var mCard);
        _missingTitle = new TextBlock { Foreground = White, FontWeight = FontWeights.Bold, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 6) };
        _missingDesc = new TextBlock { Foreground = Text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 8) };
        mCard.Children.Add(_missingTitle);
        mCard.Children.Add(_missingDesc);
        mCard.Children.Add(new TextBlock { Text = "Texto para pedirlo a Gemini:", Foreground = Muted, FontSize = 11, Margin = new Thickness(2, 0, 0, 4) });
        _promptBox = ReadOnlyBox(120);
        mCard.Children.Add(_promptBox);
        var mb = Pill("Copiar texto", () => CopyText(_promptBox.Text));
        mb.HorizontalAlignment = HorizontalAlignment.Left;
        mCard.Children.Add(mb);
        _missingView.Children.Add(mBorder);
        stack.Children.Add(_missingView);

        var allBorder = NewCard("Lista de lo que falta (para pedir a Gemini)", out var allCard);
        _allMissing = ReadOnlyBox(150);
        allCard.Children.Add(_allMissing);
        var allBtn = Pill("Copiar la lista", () => CopyText(_allMissing.Text));
        allBtn.HorizontalAlignment = HorizontalAlignment.Left;
        allCard.Children.Add(allBtn);
        stack.Children.Add(allBorder);
        return scroll;
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

    private bool On(string key) => _flags.TryGetValue(key, out var v) && v;

    private void SetStatus(string? text)
    {
        _status.Text = text ?? "";
        _status.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void CopyText(string text)
    {
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException) { /* el portapapeles lo tiene otra aplicación */ }
    }

    // ---------------------------------------------------------------- lista de la izquierda
    private void RefreshSummary()
    {
        int ok = _clips.All.Count(c => QualityOf(c.Id) == "ok");
        _summary.Text = $"{ok} movimientos listos · {_clips.All.Count - ok} con pose prestada · {_data.Needed.Count + _data.MissingTransitions.Count} sin arte";
    }

    private string QualityOf(string clip) => _data.Clips.TryGetValue(clip, out var c) ? c.Quality : "prestada";
    private VisorClip? InfoOf(string clip) => _data.Clips.TryGetValue(clip, out var c) ? c : null;
    private VisorAction? ActionById(string id) => _data.Actions.FirstOrDefault(a => a.Id == id);
    private string ActionName(string id) => ActionById(id)?.Name ?? id;
    private bool Matches(string text) => _query.Length == 0 || text.ToLowerInvariant().Contains(_query);

    private void RefreshList()
    {
        _list.Children.Clear();
        double width = Math.Max(150, (Width - 24) / 4 - 46);   // la lista es el 25 % del ancho (menos relleno y barra)

        _list.Children.Add(GroupLabel("Acciones básicas"));
        foreach (var a in _data.Actions.Where(a => Matches(a.Name + " " + a.Clip)))
        {
            var action = a;
            string key = "a:" + action.Id;
            _list.Children.Add(ListItem(action.Name, QualityOf(action.Clip), action.Clip, () => { _selectedKey = key; ShowAction(action.Id); RefreshList(); }, _selectedKey == key, width));
        }

        _list.Children.Add(GroupLabel("Transiciones"));
        foreach (var t in _data.Transitions.Where(t => Matches(ActionName(t.From) + " " + ActionName(t.To) + " " + t.Clip)))
        {
            var tr = t;
            string key = $"t:{tr.Clip}:{tr.From}:{tr.To}";
            string name = $"{ActionName(tr.From)} → {ActionName(tr.To)}" + (tr.Reverse ? " ↺" : "");
            _list.Children.Add(ListItem(name, QualityOf(tr.Clip), tr.Clip, () => { _selectedKey = key; ShowTransition(tr); RefreshList(); }, _selectedKey == key, width));
        }

        var missingTr = _data.MissingTransitions.Where(p => p.Length == 2 && Matches(ActionName(p[0]) + " " + ActionName(p[1]))).ToList();
        var missingNeeded = _data.Needed.Where(n => Matches(n.Name + " " + n.Detail)).ToList();
        if (missingTr.Count + missingNeeded.Count > 0) _list.Children.Add(GroupLabel("Faltan (sin arte)"));
        foreach (var p in missingTr)
        {
            var pair = p;
            string key = $"m:{pair[0]}:{pair[1]}";
            _list.Children.Add(ListItem($"{ActionName(pair[0])} → {ActionName(pair[1])}", "falta", "transición", () => { _selectedKey = key; SelectMissingTransition(pair[0], pair[1]); RefreshList(); }, _selectedKey == key, width));
        }
        foreach (var n in missingNeeded)
        {
            var item = n;
            string key = "n:" + item.Name;
            _list.Children.Add(ListItem(item.Name, "falta", item.Group, () => { _selectedKey = key; SelectMissing(item); RefreshList(); }, _selectedKey == key, width));
        }
    }

    // ---------------------------------------------------------------- qué reproducir: acciones, transiciones y cadenas
    private ClipDef? ClipOf(string id) => _clips.Contains(id) ? _clips[id] : null;

    private TransitionGraph Graph => _graph ??= new TransitionGraph(_data.Transitions,
        _data.Direct.Where(d => d.Length == 2).Select(d => (d[0], d[1])));
    private TransitionGraph? _graph;

    private void ShowAction(string id)
    {
        var a = ActionById(id);
        if (a is null || ClipOf(a.Clip) is null) return;
        var steps = new List<PlanStep>();
        string note = "";
        if (_action is not null && _action != id)
        {
            var path = Graph.FindPath(_action, id);
            if (path is null) note = $"No hay transición dibujada de «{ActionName(_action)}» a «{a.Name}»: se corta directamente (aparece en «Faltan»).";
            else foreach (var t in path) steps.Add(new PlanStep(t.Clip, t.Reverse, false, $"{ActionName(t.From)} → {ActionName(t.To)}"));
        }
        steps.Add(new PlanStep(a.Clip, false, true, a.Name));
        StartPlan(steps, id, note);
    }

    private void ShowTransition(TransitionEdge t)
    {
        var from = ActionById(t.From);
        var to = ActionById(t.To);
        if (from is null || to is null || ClipOf(t.Clip) is null) return;
        var steps = new List<PlanStep>
        {
            new(from.Clip, false, false, from.Name),                                   // un ciclo de la acción de partida
            new(t.Clip, t.Reverse, false, $"{from.Name} → {to.Name}"),
            new(to.Clip, false, true, to.Name),                                       // y termina en la de llegada
        };
        StartPlan(steps, t.To, "");
    }

    private void StartPlan(List<PlanStep> steps, string finalAction, string note)
    {
        if (_editOpen && _editDirty) { SetStatus("Guarda o descarta los cambios del cuadro antes de cambiar de movimiento."); return; }
        _curMissing = null; _curMissingTransition = null;
        _clipView.Visibility = Visibility.Visible; _missingView.Visibility = Visibility.Collapsed;
        _plan = steps;
        _action = finalAction;
        _stepIdx = 0;
        ResetStep();
        _playing = true;
        SetStatus(note.Length > 0 ? note : null);
        var final = steps[^1];
        string q = QualityOf(final.Clip);
        _badge.Text = QualityText[q];
        _badge.Foreground = Dots[q];
        _title.Text = steps.Count == 1 ? steps[0].Label : $"{steps[0].Label} … {final.Label}";
        _chain.Text = steps.Count == 1
            ? "Acción básica (se repite)."
            : "Reproduce: " + string.Join("  →  ", steps.Select(s => s.Loop ? $"{s.Label} (se repite)" : s.Label));
        _desc.Text = InfoOf(final.Clip)?.Desc ?? "";
        RefreshTimingRows();
        EnterStep();
        if (_editOpen) LoadEditorFrame(CurStep.Clip, 0);
    }

    private PlanStep CurStep => _plan[Math.Min(_stepIdx, _plan.Count - 1)];
    private ClipDef CurClipDef => _clips[CurStep.Clip];
    private int CurFrameCount => Math.Max(1, CurClipDef.FrameCount);

    /// <summary>Al empezar un paso: la tira de miniaturas y la ficha pasan a ser las de su clip.</summary>
    private void EnterStep()
    {
        string clip = CurStep.Clip;
        if (_stripClip != clip) { _stripClip = clip; BuildStrip(); }
        var def = CurClipDef;
        var info = InfoOf(clip);
        var sb = new StringBuilder();
        sb.AppendLine($"Clip «{clip}»: {def.FrameCount} cuadros · {(def.Loop ? "bucle" : "una vez")} · {def.Fps:0.##} fps · {def.Length:0.00} s");
        if (info is not null)
        {
            sb.AppendLine($"Origen del arte: {info.Origin}");
            sb.AppendLine(info.Wired.Length > 0 ? $"Motor: lo usa el comportamiento «{info.Wired}»" : "Motor: tiene arte pero ningún comportamiento lo usa todavía");
            if (info.Note.Length > 0) sb.AppendLine($"Qué mejorar: {info.Note}");
        }
        _info.Text = sb.ToString().TrimEnd();
        RefreshChainMarks();
    }

    private void RefreshChainMarks()
    {
        if (_plan.Count <= 1) return;
        _chain.Text = "Reproduce: " + string.Join("  →  ", _plan.Select((s, i) =>
            (i == _stepIdx ? "▶ " : "") + (s.Loop ? $"{s.Label} (se repite)" : s.Label)));
    }

    private void SelectMissing(VisorNeeded n)
    {
        _curMissing = n; _curMissingTransition = null;
        ShowMissingView($"{n.Name} · {n.Group}", n.Detail,
            Prompt($"One row, {n.Frames} frames side by side", $"Action: {n.Name}. {n.Detail}"));
    }

    private void SelectMissingTransition(string from, string to)
    {
        _curMissing = null; _curMissingTransition = from + ">" + to;
        string a = ActionName(from), b = ActionName(to);
        ShowMissingView($"Transición {a} → {b} · Faltan los cuadros",
            "Esta transición no tiene arte: el visor la enlaza con un corte directo. Pide a Gemini los cuadros intermedios (8 a 10).",
            Prompt("One row, 8 to 10 frames side by side", $"Action: smooth transition of the cat from «{a}» to «{b}». The first frame is exactly the «{a}» pose and the last frame is exactly the «{b}» pose; the frames in between show the natural movement."));
    }

    private void ShowMissingView(string title, string detail, string prompt)
    {
        if (_editOpen && _editDirty) { SetStatus("Guarda o descarta los cambios del cuadro antes de cambiar."); return; }
        _clipView.Visibility = Visibility.Collapsed; _missingView.Visibility = Visibility.Visible;
        _missingTitle.Text = title;
        _missingDesc.Text = detail;
        _promptBox.Text = prompt;
    }

    private static string Prompt(string rows, string action) =>
        "Same cat, same flat 2D pixel-art style, same palette, same scale and same baseline as the attached sheet.\n" +
        $"{rows}, magenta #FF00FF background, no grid lines, no shadows, no text, at least 12% empty margin around every frame (tail, ears and paws never touch the cell edge).\n" +
        "Smooth animation: consecutive frames differ by a SMALL change; where the motion is big add intermediate frames.\n" +
        "Strict profile facing RIGHT unless said otherwise. Black outline only on the outer silhouette. Collar with red heart tag visible when the neck is visible.\n" +
        action;

    private string MissingText()
    {
        var sb = new StringBuilder("TRANSICIONES QUE FALTAN\n");
        foreach (var p in _data.MissingTransitions.Where(p => p.Length == 2)) sb.AppendLine($"  - {ActionName(p[0])} → {ActionName(p[1])}");
        sb.AppendLine("\nACCIONES QUE FALTAN (sin arte propio)");
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

    // ---------------------------------------------------------------- miniaturas
    private void BuildStrip()
    {
        _strip.Children.Clear();
        _thumbs.Clear();
        if (_plan.Count == 0) return;
        var def = CurClipDef;
        for (int i = 0; i < def.FrameCount; i++)
        {
            int index = i;
            var img = new Image { Source = _atlas.Frame(_skin, def.Id, i), Stretch = Stretch.Uniform, Width = _atlas.GridWidth * 0.8, Height = _atlas.GridHeight * 0.8 };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            var g = new Grid();
            g.Children.Add(img);
            g.Children.Add(new TextBlock { Text = (i + 1).ToString(CultureInfo.InvariantCulture) + (_atlas.IsEdited(_skin, def.Id, i) ? " ✎" : ""), Foreground = White, FontSize = 9, Margin = new Thickness(2, 0, 0, 0) });
            var border = new Border { Background = Stage, BorderThickness = new Thickness(2), BorderBrush = Transparent, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 4, 0), Child = g, Cursor = Cursors.Hand };
            border.MouseLeftButtonUp += (_, _) => PickFrame(index);
            _strip.Children.Add(border);
            _thumbs.Add(border);
        }
    }

    private void PickFrame(int index)
    {
        _playing = false;
        SetFrame(index);
        if (_editOpen) LoadEditorFrame(CurStep.Clip, index);
    }

    // ---------------------------------------------------------------- tiempos
    private void RefreshTimingRows()
    {
        _timingRows.Children.Clear();
        foreach (string id in _plan.Select(s => s.Clip).Distinct())
        {
            var def = _clips[id];
            var original = _clips.Original(id);
            string clipId = id;
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
            row.Children.Add(new TextBlock { Text = id, Foreground = White, FontFamily = new FontFamily("Consolas"), FontSize = 12, Width = 130, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) });
            row.Children.Add(new TextBlock { Text = $"{def.FrameCount} cuadros", Foreground = Muted, FontSize = 11, Width = 78, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) });

            row.Children.Add(Label("fps"));
            var (fpsFrame, fpsBox) = Field(60);
            fpsBox.Text = def.Fps.ToString("0.##", CultureInfo.InvariantCulture);
            row.Children.Add(fpsFrame);
            row.Children.Add(Label("duración (s)"));
            var (durFrame, durBox) = Field(60);
            durBox.Text = (def.FrameCount / def.Fps).ToString("0.00", CultureInfo.InvariantCulture);
            row.Children.Add(durFrame);
            row.Children.Add(Label("avance"));
            var (advFrame, advBox) = Field(150);
            advBox.Text = string.Join(",", def.Advance is { Length: > 0 } a ? a : new[] { 0 });
            row.Children.Add(advFrame);

            var reset = Pill("Restablecer", () => { ResetTiming(clipId); RefreshTimingRows(); EnterStep(); }, compact: true);
            reset.IsEnabled = _timing.ContainsKey(id);
            reset.ToolTip = $"Vuelve a {original.Fps:0.##} fps";

            void Commit(bool fromDuration)
            {
                double fps = def.Fps;
                if (fromDuration && TryNumber(durBox.Text, out double seconds) && seconds > 0) fps = def.FrameCount / seconds;
                else if (!fromDuration && TryNumber(fpsBox.Text, out double f) && f > 0) fps = f;
                var advance = ParseAdvance(advBox.Text);
                ApplyTiming(clipId, fps, advance);
                var now = _clips[clipId];
                fpsBox.Text = now.Fps.ToString("0.##", CultureInfo.InvariantCulture);
                durBox.Text = (now.FrameCount / now.Fps).ToString("0.00", CultureInfo.InvariantCulture);
                advBox.Text = string.Join(",", now.Advance is { Length: > 0 } adv ? adv : new[] { 0 });
                reset.IsEnabled = _timing.ContainsKey(clipId);
            }
            fpsBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(false); };
            fpsBox.LostFocus += (_, _) => Commit(false);
            durBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(true); };
            durBox.LostFocus += (_, _) => Commit(true);
            advBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(false); };
            advBox.LostFocus += (_, _) => Commit(false);

            row.Children.Add(reset);
            _timingRows.Children.Add(row);
        }
    }

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private static int[]? ParseAdvance(string text)
    {
        var list = new List<int>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) list.Add(v);
        return list.Count > 0 ? list.ToArray() : null;
    }

    /// <summary>Guarda el retoque de tiempo de un clip y lo aplica al motor (también en las demás sesiones).</summary>
    private void ApplyTiming(string clipId, double fps, int[]? advance)
    {
        var original = _clips.Original(clipId);
        double clamped = ClipTimingFile.ClampFps(fps);
        int[]? adv = advance?.Select(v => Math.Clamp(v, -ClipTimingFile.MaxAdvance, ClipTimingFile.MaxAdvance)).Take(ClipTimingFile.MaxAdvanceLength).ToArray();
        bool sameAdvance = adv is null || (original.Advance is { } o && o.SequenceEqual(adv));
        if (Math.Abs(clamped - original.Fps) < 1e-6 && sameAdvance) _timing.Remove(clipId);
        else _timing[clipId] = new ClipTimingEntry(clamped, adv);
        SaveTimingAndApply(clipId);
    }

    private void ResetTiming(string clipId)
    {
        _timing.Remove(clipId);
        SaveTimingAndApply(clipId);
    }

    private void SaveTimingAndApply(string clipId)
    {
        try
        {
            _store.SaveTiming(_timing);
            ClipTimingFile.Apply(_clips, _timing);
            _host.ArtChanged();
            SetStatus(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ClipTimingFile.Apply(_clips, _timing);   // vale en esta sesión aunque no se pueda guardar
            SetStatus("No se pudo guardar el tiempo en la carpeta compartida: " + ex.Message);
        }
        if (_plan.Count > 0 && CurStep.Clip == clipId) EnterStep();
    }

    // ---------------------------------------------------------------- tamaño de las piezas
    private static readonly double[] SizePresets = { 0.25, 0.35, 0.5, 0.75, 1.0, 1.5 };

    private UIElement BuildSizeCard()
    {
        var border = NewCard("Tamaño de las piezas (gato y caja)", out var card);
        var row = new WrapPanel();
        int nearest = Array.IndexOf(SizePresets, SizePresets.OrderBy(p => Math.Abs(p - _host.CellSize)).First());
        _sizeSegments = Segments(SizePresets.Select(p => $"{p * 100:0}%").ToArray(), nearest, i => ApplySize(SizePresets[i]));
        row.Children.Add(_sizeSegments);
        row.Children.Add(Label("casilla (px)"));
        var (frame, box) = Field(60);
        _sizeBox = box;
        _sizeBox.Text = _host.CellSize.ToString("0.###", CultureInfo.InvariantCulture);
        _sizeBox.KeyDown += (_, e) => { if (e.Key == Key.Enter && TryNumber(_sizeBox.Text, out double v)) ApplySize(v); };
        row.Children.Add(frame);
        row.Children.Add(Pill("Aplicar", () => { if (TryNumber(_sizeBox.Text, out double v)) ApplySize(v); }));
        card.Children.Add(row);
        _sizeInfo = new TextBlock { Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 0) };
        card.Children.Add(_sizeInfo);
        return border;
    }

    private void ApplySize(double value)
    {
        value = ArtScale.Clamp(value);
        _host.SetCellSize(value);
        _sizeBox.Text = value.ToString("0.###", CultureInfo.InvariantCulture);
        UpdateSizeInfo();
    }

    private void UpdateSizeInfo()
    {
        double c = _host.CellSize;
        int index = Array.FindIndex(SizePresets, p => Math.Abs(p - c) < 1e-6);
        if (index >= 0) Select(_sizeSegments, index);
        _sizeInfo.Text = $"Cada casilla del dibujo mide {c:0.###} px: el gato (100 casillas de alto) se ve de {100 * c:0} px y la caja (66) de {66 * c:0} px. " +
                         "No cambia la cantidad de píxeles del dibujo. Vale para todas las sesiones; entre 0,25 y 1,5.";
    }

    // ---------------------------------------------------------------- reproducción (como el motor)
    private void ResetStep() { _t = 0; _tick = -1; _holdUntil = 0; _frame = 0; }

    private void SetFrame(int i)
    {
        _frame = i;
        _t = (i + 0.01) / CurClipDef.Fps;
        _tick = i;
    }

    /// <summary>Cuadro (de 0 a n-1) que toca al cambio número <paramref name="tick"/> del paso actual, con la inversa aplicada.</summary>
    private int DisplayFrame(int tick)
    {
        var step = CurStep;
        var def = CurClipDef;
        int n = Math.Max(1, def.FrameCount);
        int f = tick <= 0 ? 0 : (step.Loop && def.Loop) ? tick % n : Math.Min(tick, n - 1);
        return step.Reverse ? n - 1 - f : f;
    }

    private void Step()
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Min(0.1, now - _last);
        _last = now;
        if (_plan.Count == 0) return;

        var def = CurClipDef;
        if (_playing && now > _holdUntil && !_editOpen)
        {
            _t += dt * _speed;
            int target = (int)Math.Floor(_t * def.Fps);
            while (_tick < target)
            {
                _tick++;
                int f = DisplayFrame(_tick);
                if (On("mov") && _tick > 0) _camX += def.AdvanceOf(f);
                var step = CurStep;
                if (!step.Loop && _tick >= def.FrameCount)
                {
                    if (_stepIdx < _plan.Count - 1) { _stepIdx++; ResetStep(); EnterStep(); }
                    else _playing = false;
                    break;
                }
                if (step.Loop && !def.Loop && _tick >= def.FrameCount)      // acción de una sola pasada: se repite tras una pausa
                {
                    _holdUntil = now + 0.6;
                    ResetStep();
                    break;
                }
            }
        }

        _frame = DisplayFrame(Math.Max(0, _tick));
        if (_playButton.Content as string != (_playing ? "Pausa" : "Reproducir")) _playButton.Content = _playing ? "Pausa" : "Reproducir";
        for (int i = 0; i < _thumbs.Count; i++)
            _thumbs[i].BorderBrush = i == _frame ? Accent : Transparent;
        double speed = (def.Advance is { Length: > 0 } adv ? adv.Average() : 0) * def.Fps;
        _readout.Text = $"cuadro {_frame + 1}/{def.FrameCount} · avance {def.AdvanceOf(_frame)} · velocidad {speed:0} casillas/s";
        Draw();
    }

    private void Draw()
    {
        int gw = _atlas.GridWidth, gh = _atlas.GridHeight;
        string clip = CurStep.Clip;
        string key = $"{_skin}|{clip}|{_frame}|{_zoom}|{On("mirror")}|{On("zones")}|{_atlas.IsEdited(_skin, clip, _frame)}|{_drawRevision}";
        if (key != _shownKey)
        {
            _shownKey = key;
            // Se pide el cuadro a tamaño 1:1 y lo amplía WPF (vecino más cercano): así no se guardan en la caché del atlas
            // imágenes enormes por cada zoom (un cuadro a zoom 6 ocupa más de 3 MB).
            _frameImage.Source = _atlas.Frame(_skin, clip, _frame, On("mirror"));
            _frameImage.Width = gw * _zoom; _frameImage.Height = gh * _zoom;
            _overlayImage.Source = On("zones") ? ZoneOverlay.Get("cat", gw, gh, _atlas.ExtraZones, _atlas.Main, _zoom) : null;
            _overlayImage.Width = gw * _zoom; _overlayImage.Height = gh * _zoom;
            _stage.Height = gh * _zoom + 26;
        }
        _ground.Set(_camX, _zoom, _atlas.BaseRows);
    }

    private int _drawRevision;

    /// <summary>El dibujo de algún cuadro cambió (se guardó un retoque): rehacer la imagen y las miniaturas.</summary>
    private void DrawingChanged()
    {
        _drawRevision++;
        _stripClip = "";
        if (_plan.Count > 0) EnterStep();
    }

    // ---------------------------------------------------------------- suelo con marcas
    /// <summary>Línea del suelo con marcas cada 20 casillas que se desplazan con el avance del gato.</summary>
    private sealed class GroundView : FrameworkElement
    {
        private double _camX, _baseRows;
        private int _zoom = 4;
        private bool _ready;
        private static readonly Pen LinePen = MakePen(0x59FFFFFF);
        private static readonly Brush LabelBrush = Frozen(0x8CFFFFFF);

        private static Pen MakePen(uint argb)
        {
            var p = new Pen(Frozen(argb), 1);
            p.Freeze();
            return p;
        }

        public void Set(double camX, int zoom, double baseRows)
        {
            if (_ready && camX == _camX && zoom == _zoom) return;
            _ready = true;
            _camX = camX; _zoom = zoom; _baseRows = baseRows;
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
