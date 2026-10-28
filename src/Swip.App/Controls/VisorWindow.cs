using System.Diagnostics;
using System.Globalization;
using System.IO;
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
/// El Visor (opción del menú de la caja): una ventana normal de aplicación (barra de tareas, minimizar, no se queda
/// encima) con el estilo de las tarjetas de los menús. Una pestaña por gato. A la izquierda, las catorce acciones y
/// las transiciones entre posturas; a la derecha, arriba el visor y debajo los controles, los cuadros del clip y los
/// ajustes de tiempos y de tamaño. Todo cabe sin barras de desplazamiento.
///   - al elegir una acción se ve esa acción, precedida de las transiciones que el motor haría desde la postura anterior;
///   - al elegir una transición se ve de la postura de partida a la de llegada (un corte directo si falta el arte);
///   - «Editar dibujo» cambia el visor por el editor de píxeles con todos los cuadros del clip (VisorWindow.Editor.cs).
/// El gato avanza solo cuando cambia el cuadro (igual que <c>SwipWorld</c>), sobre un suelo marcado cada 20 casillas:
/// si los pies resbalan sobre las marcas, el avance de ese clip está mal.
/// La calidad del arte sale de Assets/atlas/visor.json (tools/art/build_viewer.py); las acciones y transiciones, del motor
/// (<see cref="Behaviors"/>); los cuadros, fps y avance, del atlas y de los clips.
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
            if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
            _open.Activate();
            return;
        }
        _open = new VisorWindow(blur, host);
        _open.Show();
        _open.Activate();
    }

    // ---------------------------------------------------------------- catálogo
    private sealed record ActionItem(string Id, string Name, string Behavior);

    /// <summary>Las catorce acciones, en el orden de la lista.</summary>
    private static readonly ActionItem[] Actions =
    {
        new("sit", "Sentado", "sit"),
        new("loaf", "Acostado", "loaf"),
        new("stand", "De pie", "stand"),
        new("curl", "Durmiendo de bolita", "sleep_curl"),
        new("side", "Durmiendo de lado", "sleep_side"),
        new("walk", "Caminar", "walk"),
        new("trot", "Trotar", "trot"),
        new("run", "Correr", "run"),
        new("groom", "Lamerse la pata", "groom"),
        new("leap", "Saltar al mismo nivel", "leap"),
        new("jump_box", "Saltar a la caja", "box_jump_on"),
        new("enter_box", "Meterse en la caja", "box_jump_in"),
        new("exit_box", "Salirse de la caja", "box_exit_in"),
        new("push_box", "Chocar con la caja y empujarla", "box_push"),
    };

    private static readonly Dictionary<Posture, string> PostureNames = new()
    {
        [Posture.Curl] = "Bolita", [Posture.Side] = "De lado", [Posture.Loaf] = "Acostado", [Posture.Sit] = "Sentado",
        [Posture.Stand] = "De pie", [Posture.Walk] = "Caminando", [Posture.Trot] = "Trotando", [Posture.Run] = "Corriendo",
    };

    /// <summary>El clip que muestra cada postura cuando se reposa en ella.</summary>
    private static readonly Dictionary<Posture, string> PostureClips = new()
    {
        [Posture.Curl] = "sleep_curl", [Posture.Side] = "sleep_side", [Posture.Loaf] = "loaf_idle", [Posture.Sit] = "sit_look",
        [Posture.Stand] = "stand_idle", [Posture.Walk] = "walk", [Posture.Trot] = "trot", [Posture.Run] = "run",
    };

    internal sealed record VisorClip(string Quality, string Note, string Origin, string Wired, string Desc);
    internal sealed class VisorData
    {
        public Dictionary<string, VisorClip> Clips { get; set; } = new();
    }

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
    private StackPanel _list = null!, _strip = null!, _tabs = null!;
    private Grid _viewPanel = null!;
    private TextBlock _readout = null!, _status = null!, _timingTitle = null!, _sizeInfo = null!;
    private WrapPanel _timingRow = null!;
    private TextBox _sizeBox = null!;
    private Image _frameImage = null!, _overlayImage = null!;
    private GroundView _ground = null!;
    private Grid _stage = null!;
    private Button _playButton = null!;
    private StackPanel _sizeSegments = null!;
    private readonly List<Border> _thumbs = new();
    private readonly Dictionary<string, bool> _flags = new();

    // ---------------------------------------------------------------- estado
    private string _skin = CatSkins.Orange;
    private int _zoom;                              // 0 = ajustar al alto disponible
    private double _speed = 1;
    private Posture _posture = Posture.Stand;       // postura en la que queda el gato tras lo último que se reprodujo
    private string _selClip = "walk";               // clip de lo elegido en la lista (el que se edita)
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

        // Una ventana de aplicación normal: sale en la barra de tareas y en Alt+Tab con el icono de Swip, se puede
        // minimizar y NO se queda siempre encima de las demás.
        Title = "Swip · Visor";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = _layered;
        Background = Brushes.Transparent;
        ShowInTaskbar = true;
        Topmost = false;
        ResizeMode = ResizeMode.CanMinimize;
        try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/swip.ico", UriKind.Absolute)); }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException) { /* sin icono: no pasa nada */ }
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var work = SystemParameters.WorkArea;
        Width = Math.Min(1500, work.Width - 40);
        Height = Math.Min(920, work.Height - 40);

        Content = BuildUi();
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += (_, _) => { _timer.Stop(); _open = null; };
        KeyDown += OnKeyDown;
        _timer.Tick += (_, _) => Step();

        RefreshList();
        UpdateSizeInfo();
        ShowAction(Actions.First(a => a.Id == "walk"));
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
        if (e.Key == Key.Escape && !_editOpen) Close();
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && _editOpen) Undo();
    }

    private bool _closeWarned;

    /// <summary>Con cambios del editor sin aceptar, el primer intento de cerrar avisa; el segundo los descarta.</summary>
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_editOpen && _editDirty && !_closeWarned)
        {
            _closeWarned = true;
            e.Cancel = true;
            SetStatus("Hay cambios sin aceptar. Pulsa Cerrar otra vez para descartarlos, o pulsa Aceptar antes.");
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
            if (_editOpen) { SetStatus("Pulsa Aceptar o Cancelar antes de cambiar de gato."); Select(_tabs!, SkinIndex()); return; }
            _skin = i == 0 ? CatSkins.Orange : CatSkins.Gray;
            _shownKey = "";
            BuildStrip();
        });
        _tabs.Margin = new Thickness(0);
        Grid.SetColumn(_tabs, 1);
        hg.Children.Add(_tabs);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var minimize = Pill("Minimizar", () => WindowState = WindowState.Minimized);
        var close = Pill("Cerrar", () => Close());
        close.Margin = new Thickness(0);
        buttons.Children.Add(minimize);
        buttons.Children.Add(close);
        Grid.SetColumn(buttons, 3);
        hg.Children.Add(buttons);
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
        _list = new StackPanel();
        // Sin barra visible: la lista cabe entera; si la pantalla fuera muy pequeña, la rueda del ratón la desplaza.
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = _list };
    }

    private UIElement BuildRight()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ---- visor (arriba del todo): escenario, controles, cuadros y ajustes
        _viewPanel = new Grid();
        _viewPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 3; i++) _viewPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _stage = new Grid { ClipToBounds = true, Margin = new Thickness(0, 4, 0, 6) };   // sin fondo: se ve el cristal del menú
        _ground = new GroundView();
        _frameImage = new Image { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Stretch = Stretch.Fill, SnapsToDevicePixels = true };
        _overlayImage = new Image { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Stretch = Stretch.None, IsHitTestVisible = false, SnapsToDevicePixels = true };
        RenderOptions.SetBitmapScalingMode(_frameImage, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetBitmapScalingMode(_overlayImage, BitmapScalingMode.NearestNeighbor);
        _stage.Children.Add(_ground);
        _stage.Children.Add(_frameImage);
        _stage.Children.Add(_overlayImage);
        Grid.SetRow(_stage, 0);
        _viewPanel.Children.Add(_stage);

        var play = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
        _playButton = Pill("Pausa", () => { _playing = !_playing; });
        play.Children.Add(_playButton);
        play.Children.Add(Pill("Reiniciar", () => { _stepIdx = 0; ResetStep(); _playing = true; }));
        play.Children.Add(Pill("◀", () => { _playing = false; SetFrame((_frame - 1 + CurFrameCount) % CurFrameCount); }));
        play.Children.Add(Pill("▶", () => { _playing = false; SetFrame((_frame + 1) % CurFrameCount); }));
        play.Children.Add(Label("velocidad"));
        play.Children.Add(Segments(new[] { "¼", "½", "1", "2" }, 2, i => _speed = new[] { 0.25, 0.5, 1, 2 }[i]));
        play.Children.Add(Label("zoom"));
        play.Children.Add(Segments(new[] { "Ajustar", "2", "3", "4", "6" }, 0, i => { _zoom = new[] { 0, 2, 3, 4, 6 }[i]; _shownKey = ""; }));
        play.Children.Add(Flag("suelo avanza", "mov", true));
        play.Children.Add(Flag("espejo", "mirror", false));
        play.Children.Add(Flag("marcos", "zones", false));
        play.Children.Add(Pill("Editar dibujo", OpenEditor));
        _readout = new TextBlock { Foreground = Muted, FontSize = 11, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 6) };
        play.Children.Add(_readout);
        Grid.SetRow(play, 1);
        _viewPanel.Children.Add(play);

        _strip = new StackPanel { Orientation = Orientation.Horizontal };
        var stripScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _strip, Margin = new Thickness(0, 0, 0, 2),
        };
        Grid.SetRow(stripScroll, 2);
        _viewPanel.Children.Add(stripScroll);

        // tiempos y tamaño, en dos columnas
        var cards = new Grid();
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var timingCard = BuildTimingCard();
        var sizeCard = BuildSizeCard();
        Grid.SetColumn(sizeCard, 2);
        cards.Children.Add(timingCard);
        cards.Children.Add(sizeCard);
        Grid.SetRow(cards, 3);
        _viewPanel.Children.Add(cards);
        root.Children.Add(_viewPanel);

        // ---- editor (VisorWindow.Editor.cs): ocupa el sitio del visor mientras está abierto
        var editor = BuildEditorPanel();
        editor.Visibility = Visibility.Collapsed;
        root.Children.Add(editor);
        _editPanel = editor;

        _status = new TextBlock { Foreground = Frozen(0xFFFF8A65), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 2, 0, 0), Visibility = Visibility.Collapsed };
        Grid.SetRow(_status, 1);
        root.Children.Add(_status);
        return root;
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

    // ---------------------------------------------------------------- lista de la izquierda
    private string QualityOf(string clip) => _data.Clips.TryGetValue(clip, out var c) ? c.Quality : "prestada";

    private static string PostureName(Posture p) => PostureNames.GetValueOrDefault(p, p.ToString());

    private void RefreshList()
    {
        _list.Children.Clear();
        double width = Math.Max(150, (Width - 24) / 4 - 30);   // la lista es el 25 % del ancho (menos relleno)

        _list.Children.Add(GroupLabel("Acciones"));
        foreach (var a in Actions)
        {
            var action = a;
            string key = "a:" + action.Id;
            var b = Behaviors.Get(action.Behavior);
            var item = ListItem(action.Name, QualityOf(b.Clip), "", () => { _selectedKey = key; ShowAction(action); RefreshList(); }, _selectedKey == key, width);
            item.Padding = new Thickness(8, 4, 8, 4);
            item.Margin = new Thickness(0, 0, 0, 1);
            _list.Children.Add(item);
        }

        _list.Children.Add(GroupLabel("Transiciones entre posturas"));
        var chips = new WrapPanel();
        foreach (var edge in Behaviors.Edges.Where(e => e.Via is not null))
            chips.Children.Add(Chip(edge, width));
        foreach (var edge in Behaviors.Edges.Where(e => e.Via is null))
            chips.Children.Add(Chip(edge, width));
        _list.Children.Add(chips);
    }

    /// <summary>Una transición como botón pequeño: punto verde si está dibujada (↺ si se reproduce al revés), rojo si es un corte.</summary>
    private Button Chip(PostureEdge edge, double listWidth)
    {
        string key = $"e:{edge.From}:{edge.To}";
        string quality = edge.Via is null ? "falta" : QualityOf(edge.Via.Clip);
        string name = $"{PostureName(edge.From)} → {PostureName(edge.To)}" + (edge.Via is { Reverse: true } ? " ↺" : "");
        var b = new Button { Style = RowStyle, Margin = new Thickness(0, 0, 3, 3), Padding = new Thickness(6, 3, 6, 3), FontSize = 10 };
        Rest(b, _selectedKey == key ? CardSel : Card);
        AttachHover(b);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = Dots[quality], Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = name, Foreground = Text, FontSize = 10.5 });
        b.Content = row;
        b.ToolTip = edge.Via is null ? "Sin transición dibujada: el motor la une con un corte directo." : $"Clip «{edge.Via.Clip}»";
        b.Click += (_, _) => { _selectedKey = key; ShowEdge(edge); RefreshList(); };
        return b;
    }

    // ---------------------------------------------------------------- qué reproducir: acciones y transiciones
    private ClipDef? ClipOf(string id) => _clips.Contains(id) ? _clips[id] : null;

    private void ShowAction(ActionItem item)
    {
        var b = Behaviors.Get(item.Behavior);
        if (ClipOf(b.Clip) is null) return;
        var steps = new List<PlanStep>();
        bool cut = false;
        foreach (var edge in Behaviors.Path(_posture, b.From))
        {
            if (edge.Via is { } via && ClipOf(via.Clip) is not null)
                steps.Add(new PlanStep(via.Clip, via.Reverse, false, $"{PostureName(edge.From)} → {PostureName(edge.To)}"));
            else
                cut = true;
        }
        steps.Add(new PlanStep(b.Clip, false, true, item.Name));
        _selClip = b.Clip;
        _posture = b.To == Posture.Any ? b.From : b.To;
        StartPlan(steps, cut ? "Parte del camino es un corte directo: falta dibujar esa transición (en rojo en la lista)." : null);
    }

    private void ShowEdge(PostureEdge edge)
    {
        var steps = new List<PlanStep>();
        if (PostureClips.TryGetValue(edge.From, out var fromClip) && ClipOf(fromClip) is not null)
            steps.Add(new PlanStep(fromClip, false, false, PostureName(edge.From)));   // un ciclo de la postura de partida
        if (edge.Via is { } via && ClipOf(via.Clip) is not null)
            steps.Add(new PlanStep(via.Clip, via.Reverse, false, $"{PostureName(edge.From)} → {PostureName(edge.To)}"));
        if (PostureClips.TryGetValue(edge.To, out var toClip) && ClipOf(toClip) is not null)
            steps.Add(new PlanStep(toClip, false, true, PostureName(edge.To)));          // y termina en la de llegada
        if (steps.Count == 0) return;
        _selClip = edge.Via?.Clip ?? steps[^1].Clip;
        _posture = edge.To;
        StartPlan(steps, edge.Via is null ? "Corte directo: falta dibujar esta transición, el gato salta de una postura a la otra." : null);
    }

    private void StartPlan(List<PlanStep> steps, string? note)
    {
        if (_editOpen) { SetStatus("Pulsa Aceptar o Cancelar antes de cambiar de movimiento."); return; }
        _plan = steps;
        _stepIdx = 0;
        ResetStep();
        _playing = true;
        SetStatus(note);
        EnterStep();
    }

    private PlanStep CurStep => _plan[Math.Min(_stepIdx, _plan.Count - 1)];
    private ClipDef CurClipDef => _clips[CurStep.Clip];
    private int CurFrameCount => Math.Max(1, CurClipDef.FrameCount);

    /// <summary>Al empezar un paso: la tira de cuadros y los tiempos pasan a ser los de su clip.</summary>
    private void EnterStep()
    {
        string clip = CurStep.Clip;
        if (_stripClip != clip) { _stripClip = clip; BuildStrip(); RefreshTimingRow(); }
    }

    // ---------------------------------------------------------------- cuadros del clip
    private void BuildStrip()
    {
        _strip.Children.Clear();
        _thumbs.Clear();
        if (_plan.Count == 0) return;
        var def = CurClipDef;
        for (int i = 0; i < def.FrameCount; i++)
        {
            int index = i;
            var img = new Image { Source = _atlas.Frame(_skin, def.Id, i), Stretch = Stretch.Uniform, Width = _atlas.GridWidth * 0.5, Height = _atlas.GridHeight * 0.5 };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            var g = new Grid();
            g.Children.Add(img);
            g.Children.Add(new TextBlock { Text = (i + 1).ToString(CultureInfo.InvariantCulture) + (_atlas.IsEdited(_skin, def.Id, i) ? " ✎" : ""), Foreground = White, FontSize = 9, Margin = new Thickness(2, 0, 0, 0) });
            var border = new Border { Background = Card, BorderThickness = new Thickness(2), BorderBrush = Transparent, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 4, 0), Child = g, Cursor = Cursors.Hand };
            border.MouseLeftButtonUp += (_, _) => { _playing = false; SetFrame(index); };
            _strip.Children.Add(border);
            _thumbs.Add(border);
        }
    }

    // ---------------------------------------------------------------- tiempos
    private UIElement BuildTimingCard()
    {
        var border = NewCard("Tiempos (valen para los dos gatos)", out var card);
        border.Margin = new Thickness(0, 2, 0, 0);
        _timingTitle = new TextBlock { Foreground = White, FontFamily = new FontFamily("Consolas"), FontSize = 12, Margin = new Thickness(2, 0, 0, 4) };
        card.Children.Add(_timingTitle);
        _timingRow = new WrapPanel();
        card.Children.Add(_timingRow);
        card.Children.Add(new TextBlock
        {
            Text = "Cambia los fps o la duración (segundos del clip entero) y pulsa Intro. El avance son las casillas que avanza el gato en cada cuadro, separadas por comas.",
            Foreground = Muted, FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 0),
        });
        return border;
    }

    private void RefreshTimingRow()
    {
        _timingRow.Children.Clear();
        if (_plan.Count == 0) return;
        string id = CurStep.Clip;
        var def = _clips[id];
        var original = _clips.Original(id);
        _timingTitle.Text = $"{id} · {def.FrameCount} cuadros · {(def.Loop ? "bucle" : "una vez")}";

        _timingRow.Children.Add(Label("fps"));
        var (fpsFrame, fpsBox) = Field(56);
        fpsBox.Text = def.Fps.ToString("0.##", CultureInfo.InvariantCulture);
        _timingRow.Children.Add(fpsFrame);
        _timingRow.Children.Add(Label("duración (s)"));
        var (durFrame, durBox) = Field(56);
        durBox.Text = (def.FrameCount / def.Fps).ToString("0.00", CultureInfo.InvariantCulture);
        _timingRow.Children.Add(durFrame);
        _timingRow.Children.Add(Label("avance"));
        var (advFrame, advBox) = Field(130);
        advBox.Text = string.Join(",", def.Advance is { Length: > 0 } a ? a : new[] { 0 });
        _timingRow.Children.Add(advFrame);

        var reset = Pill("Restablecer", () => { ResetTiming(id); RefreshTimingRow(); }, compact: true);
        reset.IsEnabled = _timing.ContainsKey(id);
        reset.ToolTip = $"Vuelve a {original.Fps:0.##} fps";

        void Commit(bool fromDuration)
        {
            double fps = def.Fps;
            if (fromDuration && TryNumber(durBox.Text, out double seconds) && seconds > 0) fps = def.FrameCount / seconds;
            else if (!fromDuration && TryNumber(fpsBox.Text, out double f) && f > 0) fps = f;
            ApplyTiming(id, fps, ParseAdvance(advBox.Text));
            var now = _clips[id];
            fpsBox.Text = now.Fps.ToString("0.##", CultureInfo.InvariantCulture);
            durBox.Text = (now.FrameCount / now.Fps).ToString("0.00", CultureInfo.InvariantCulture);
            advBox.Text = string.Join(",", now.Advance is { Length: > 0 } adv ? adv : new[] { 0 });
            reset.IsEnabled = _timing.ContainsKey(id);
        }
        fpsBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(false); };
        fpsBox.LostFocus += (_, _) => Commit(false);
        durBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(true); };
        durBox.LostFocus += (_, _) => Commit(true);
        advBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(false); };
        advBox.LostFocus += (_, _) => Commit(false);
        _timingRow.Children.Add(reset);
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
        SaveTimingAndApply();
    }

    private void ResetTiming(string clipId)
    {
        _timing.Remove(clipId);
        SaveTimingAndApply();
    }

    private void SaveTimingAndApply()
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
    }

    // ---------------------------------------------------------------- tamaño de las piezas
    private static readonly double[] SizePresets = { 0.25, 0.35, 0.5, 0.75, 1.0, 1.5 };

    private UIElement BuildSizeCard()
    {
        var border = NewCard("Tamaño de las piezas (gato y caja)", out var card);
        border.Margin = new Thickness(0, 2, 0, 0);
        var row = new WrapPanel();
        int nearest = Array.IndexOf(SizePresets, SizePresets.OrderBy(p => Math.Abs(p - _host.CellSize)).First());
        _sizeSegments = Segments(SizePresets.Select(p => $"{p * 100:0}%").ToArray(), nearest, i => ApplySize(SizePresets[i]));
        row.Children.Add(_sizeSegments);
        row.Children.Add(Label("casilla (px)"));
        var (frame, box) = Field(56);
        _sizeBox = box;
        _sizeBox.Text = _host.CellSize.ToString("0.###", CultureInfo.InvariantCulture);
        _sizeBox.KeyDown += (_, e) => { if (e.Key == Key.Enter && TryNumber(_sizeBox.Text, out double v)) ApplySize(v); };
        row.Children.Add(frame);
        row.Children.Add(Pill("Aplicar", () => { if (TryNumber(_sizeBox.Text, out double v)) ApplySize(v); }));
        card.Children.Add(row);
        _sizeInfo = new TextBlock { Foreground = Muted, FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 0) };
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
        _sizeInfo.Text = $"Cada casilla mide {c:0.###} px: el gato (100 casillas) se ve de {100 * c:0} px y la caja (66) de {66 * c:0} px, sin cambiar la cantidad de píxeles. Vale para todas las sesiones (de 0,25 a 1,5).";
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
        if (_plan.Count == 0 || _editOpen) return;

        var def = CurClipDef;
        if (_playing && now > _holdUntil)
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
        _readout.Text = $"{CurStep.Label} · cuadro {_frame + 1}/{def.FrameCount} · avance {def.AdvanceOf(_frame)} · {speed:0} casillas/s";
        Draw();
    }

    /// <summary>Zoom que se usa: el elegido, o el mayor que cabe en el escenario si está en «Ajustar» (o el elegido no cabe).</summary>
    private int EffectiveZoom()
    {
        int gw = _atlas.GridWidth, gh = _atlas.GridHeight;
        double h = _stage.ActualHeight, w = _stage.ActualWidth;
        if (h < 1 || w < 1) return Math.Max(1, _zoom == 0 ? 2 : _zoom);
        int fit = (int)Math.Max(1, Math.Min(Math.Floor((h - 26) / gh), Math.Floor(w / gw)));
        return _zoom == 0 ? Math.Min(fit, 8) : Math.Min(_zoom, fit);
    }

    private void Draw()
    {
        int gw = _atlas.GridWidth, gh = _atlas.GridHeight;
        int zoom = EffectiveZoom();
        string clip = CurStep.Clip;
        string key = $"{_skin}|{clip}|{_frame}|{zoom}|{On("mirror")}|{On("zones")}|{_atlas.IsEdited(_skin, clip, _frame)}|{_drawRevision}";
        if (key != _shownKey)
        {
            _shownKey = key;
            // Se pide el cuadro a tamaño 1:1 y lo amplía WPF (vecino más cercano): así no se guardan en la caché del atlas
            // imágenes enormes por cada zoom (un cuadro a zoom 6 ocupa más de 3 MB).
            _frameImage.Source = _atlas.Frame(_skin, clip, _frame, On("mirror"));
            _frameImage.Width = gw * zoom; _frameImage.Height = gh * zoom;
            _overlayImage.Source = On("zones") ? ZoneOverlay.Get("cat", gw, gh, _atlas.ExtraZones, _atlas.Main, zoom) : null;
            _overlayImage.Width = gw * zoom; _overlayImage.Height = gh * zoom;
        }
        _ground.Set(_camX, zoom, _atlas.BaseRows);
    }

    private int _drawRevision;

    /// <summary>El dibujo de algún cuadro cambió (se aceptó un retoque): rehacer la imagen y las miniaturas.</summary>
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
