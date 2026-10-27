using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Swip.App.Art;
using Swip.App.Controls;
using Swip.App.Native;
using Swip.App.Services;
using Swip.App.World;
using Swip.Shared;
using Swip.Shared.Art;
using Swip.Shared.Engine;
using Swip.Shared.Settings;
using Microsoft.Win32;

namespace Swip.App;

public partial class MainWindow : Window
{
    private readonly ServiceClient _client = new();
    private readonly SettingsStore _store = new();
    private readonly Dictionary<string, CatAgent> _agents = new();
    private readonly Dictionary<string, CatSprite> _sprites = new();
    private readonly DispatcherTimer _loop = new(DispatcherPriority.Render);
    private readonly DispatcherTimer _refresh = new();
    private readonly DispatcherTimer _publish = new();
    private readonly DispatcherTimer _activity = new();          // vigila si esta sesión está en pantalla
    private readonly DispatcherTimer _settingsDebounce = new();  // agrupa avisos de cambios en settings.json
    private readonly DispatcherTimer _boundsDebounce = new();    // agrupa cambios de pantalla / barra de tareas
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Random _rng = new();

    private AppSettings _settings = new();
    private IntPtr _hwnd;
    private long _lastTicks;
    private bool _clickThrough = true;
    private CatAgent? _openAgent;
    private bool _refreshing;
    private bool _dragging; // mientras se arrastra caja/gato/menú: fuerza captura del ratón

    // Actividad de la sesión: solo se anima, refresca y publica mientras está en pantalla y sin bloquear.
    private bool _active;
    private bool _locked;
    private FileSystemWatcher? _settingsWatcher;

    // Motor de movimientos (gatos, caja, efectos) y hojas de sprites.
    private SwipWorld _world = null!;
    private CatAtlas _atlas = null!;
    private readonly RotateTransform _boxTilt = new();   // tambaleo de la caja (lo calcula el motor)
    private int _topmostTicks;                            // cuenta para reafirmar "siempre encima"
    private bool? _boxShownOpen;                          // estado de caja dibujado por última vez

    // Arrastre de la caja
    private bool _boxMoved;
    private double _boxStartLeft, _boxStartTop;
    private Point _boxGrab;

    // Arrastre de un gato
    private CatSprite? _catDrag;
    private bool _catMoved;
    private Point _catGrab;

    // Arrastre de un menú (popup)
    private System.Windows.Controls.Primitives.Popup? _menuTarget;
    private System.Windows.UIElement? _menuDragEl;
    private InteropNative.POINT _menuStartCursor;
    private double _menuStartH, _menuStartV;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
        InfoPopup.Closed += (_, _) =>
        {
            if (_openAgent is not null)
            {
                _openAgent.MenuOpen = false;
                _world?.SetAttention(_openAgent.Key, false);
            }
            _openAgent = null;
        };
        // Al cerrar la configuración, la caja se vuelve a cerrar.
        ConfigPopup.Closed += (_, _) => SetBoxOpen(false);

        _settingsDebounce.Interval = TimeSpan.FromMilliseconds(300);
        _settingsDebounce.Tick += (_, _) => { _settingsDebounce.Stop(); ApplySettingsFromDisk(); };
        _boundsDebounce.Interval = TimeSpan.FromMilliseconds(400);
        _boundsDebounce.Tick += (_, _) => { _boundsDebounce.Stop(); if (_active) ApplyStripBounds(); };

        BuildColorSwatches();

        // Los menús se centran horizontalmente y aparecen ENCIMA del objetivo.
        InfoPopup.CustomPopupPlacementCallback = PlaceCenteredAbove;
        ConfigPopup.CustomPopupPlacementCallback = PlaceCenteredAbove;
    }

    private static CustomPopupPlacement[] PlaceCenteredAbove(Size popupSize, Size targetSize, Point offset)
    {
        double x = (targetSize.Width - popupSize.Width) / 2;
        return new[]
        {
            new CustomPopupPlacement(new Point(x, -popupSize.Height - 6), PopupPrimaryAxis.Horizontal), // encima
            new CustomPopupPlacement(new Point(x, targetSize.Height + 6), PopupPrimaryAxis.Horizontal),  // debajo si no cabe
        };
    }

    // Tamaños fijos (ya no configurables): la franja es toda la pantalla.
    // La caja mide lo que su cuadrícula: 1 casilla = 1 px lógico (50x36).
    private static double BoxH => BoxSprites.AspectH;
    private static double BoxW => BoxSprites.AspectW;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        InteropNative.InitOverlayStyles(_hwnd);
        InteropNative.SetClickThrough(_hwnd, true); // empieza dejando pasar los clics
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings = _store.Load();

        // Motor: medidas en DIP = casillas. Para el motor el gato es su cuadro por ENCIMA de la base
        // (48x48): lo que cuelga por debajo (24 filas) se dibuja sobre la barra de tareas.
        _atlas = CatAtlas.Default;
        _world = new SwipWorld(new WorldConfig(
            CatWidth: _atlas.GridWidth, CatHeight: _atlas.BaseRows,
            BoxWidth: BoxW, BoxHeight: BoxH,
            BoxTopInsetClosed: BoxSprites.TopInsetClosed, BoxTopInsetOpen: BoxSprites.TopInsetOpen));
        // El atlas mide la cabeza sobre el cuadro entero (72 filas); el motor, sobre las 48 de encima de la base.
        _world.HeadFraction = clip => _atlas.HeadFraction("orange", clip) * _atlas.GridHeight / _atlas.BaseRows;
        Box.RenderTransformOrigin = new Point(0.5, 1);
        Box.RenderTransform = _boxTilt;
        BoxFrame.RenderTransformOrigin = new Point(0.5, 1);
        BoxFrame.RenderTransform = _boxTilt; // el marco gira con la caja

        SetBoxOpen(false);
        ApplyStripBounds();

        _loop.Interval = TimeSpan.FromMilliseconds(33); // ~30 fps
        _loop.Tick += Loop_Tick;

        _refresh.Interval = TimeSpan.FromSeconds(Math.Max(3, _settings.RefreshSeconds));
        _refresh.Tick += async (_, _) =>
        {
            await RefreshUsersAsync();
            if (_openAgent is not null) await LoadAppsAsync(_openAgent);
        };

        // Publicar al servicio las apps de NUESTRA propia sesión, para que el gato de la otra
        // sesión pueda mostrarlas (el servicio en sesión 0 no puede enumerarlas por sí mismo).
        _publish.Interval = TimeSpan.FromSeconds(8);
        _publish.Tick += async (_, _) => await PublishOwnAppsAsync();

        // Solo el "vigilante de actividad" corre siempre: cada 5 s comprueba si esta sesión está en
        // pantalla (red de seguridad por si se pierde algún aviso de cambio de sesión).
        _activity.Interval = TimeSpan.FromSeconds(5);
        _activity.Tick += (_, _) => EvaluateActivity();

        StartSettingsWatcher();
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;

        _activity.Start();
        EvaluateActivity(); // arranca animación, refresco y publicación solo si está en pantalla
    }

    // --- Actividad: solo trabajar mientras la sesión está en pantalla --------------

    /// <summary>
    /// Una sesión que no está en pantalla (cambiaste de usuario o bloqueaste) no se anima, no
    /// refresca ni publica: nadie la ve y Windows ni siquiera le deja leer sus ventanas. Al volver
    /// a pantalla relee la ubicación y el estado actuales (otra sesión pudo mover cosas).
    /// </summary>
    private void EvaluateActivity()
    {
        bool shouldBeActive = !_locked && LocalApps.IsActiveConsoleSession();
        if (shouldBeActive == _active) return;
        _active = shouldBeActive;

        if (_active) _ = ActivateAsync();
        else Deactivate();
    }

    private async Task ActivateAsync()
    {
        try
        {
            AppLog.Write("Sesión en pantalla: se reanuda y se relee ubicación y estado.");
            ApplyStripBounds();        // el área de trabajo pudo cambiar (monitor, resolución, barra)
            ApplySettingsFromDisk();   // aspecto y posiciones que dejó la otra sesión
            if (_settingsWatcher is not null) _settingsWatcher.EnableRaisingEvents = true;

            _lastTicks = _clock.ElapsedTicks; // sin esto el primer tick vería un salto de tiempo enorme
            _loop.Start();
            _refresh.Start();
            _publish.Start();

            await RefreshUsersAsync();  // quién tiene sesión y en qué estado
            await PublishOwnAppsAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Error al reanudar la sesión: {ex.Message}");
        }
    }

    private void Deactivate()
    {
        AppLog.Write("Sesión fuera de pantalla (en segundo plano o bloqueada): se detiene el render.");
        try { PersistPositions(); }
        catch (Exception ex) { AppLog.Write($"No se pudieron guardar las posiciones al salir: {ex.Message}"); }
        _loop.Stop();
        _refresh.Stop();
        _publish.Stop();
        _settingsDebounce.Stop();
        _boundsDebounce.Stop();
        if (_settingsWatcher is not null) _settingsWatcher.EnableRaisingEvents = false;

        InfoPopup.IsOpen = false;
        ConfigPopup.IsOpen = false;
        Effects.Clear();
        if (!_clickThrough)
        {
            InteropNative.SetClickThrough(_hwnd, true);
            _clickThrough = true;
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        // SystemEvents avisa desde otro hilo.
        Dispatcher.InvokeAsync(() =>
        {
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                    _locked = true;
                    break;
                // Al conectar una sesión o desbloquearla se asume desbloqueada (si estuviera
                // bloqueada llegaría un SessionLock); equivocarse hacia "activa" solo cuesta un poco de CPU.
                case SessionSwitchReason.SessionUnlock:
                case SessionSwitchReason.ConsoleConnect:
                case SessionSwitchReason.RemoteConnect:
                case SessionSwitchReason.SessionLogon:
                    _locked = false;
                    break;
            }
            EvaluateActivity();
        });
    }

    // --- Cambios de pantalla / barra de tareas -------------------------------------

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(RequestBoundsRefresh);

    private void OnSystemParameterChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "WorkArea")
            Dispatcher.InvokeAsync(RequestBoundsRefresh);
    }

    /// <summary>
    /// Reajusta la franja al área de trabajo nueva. Fuera de pantalla no se hace nada: al volver,
    /// <see cref="ActivateAsync"/> la recalcula igualmente.
    /// </summary>
    private void RequestBoundsRefresh()
    {
        if (!_active) return;
        _boundsDebounce.Stop();
        _boundsDebounce.Start();
    }

    // --- Ajustes compartidos entre sesiones ----------------------------------------

    /// <summary>Vigila settings.json para reflejar al instante lo que otra sesión cambie.</summary>
    private void StartSettingsWatcher()
    {
        try
        {
            _settingsWatcher = new FileSystemWatcher(_store.SharedDirectory, SettingsStore.SharedFileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
                               | NotifyFilters.Size | NotifyFilters.CreationTime,
                EnableRaisingEvents = false, // se activa al ponerse en pantalla
            };
            _settingsWatcher.Changed += (_, _) => QueueSettingsReload();
            _settingsWatcher.Created += (_, _) => QueueSettingsReload();
            _settingsWatcher.Renamed += (_, _) => QueueSettingsReload();
        }
        catch (Exception ex)
        {
            AppLog.Write($"No se pudo vigilar settings.json (se releerá al volver a pantalla): {ex.Message}");
            _settingsWatcher = null;
        }
    }

    /// <summary>Agrupa ráfagas de avisos (una escritura genera varios) en una sola recarga.</summary>
    private void QueueSettingsReload() =>
        Dispatcher.InvokeAsync(() =>
        {
            if (!_active) return;
            _settingsDebounce.Stop();
            _settingsDebounce.Start();
        });

    /// <summary>Relee settings.json y aplica a la pantalla SOLO lo que cambió.</summary>
    private void ApplySettingsFromDisk()
    {
        var latest = _store.Load();
        var delta = SettingsDelta.Between(_settings, latest);
        _settings = latest;
        ApplyDelta(delta);
    }

    /// <summary>
    /// Cambia un ajuste sobre lo MÁS RECIENTE del disco (no pisa lo que otra sesión haya cambiado
    /// entre medias) y aplica a la pantalla lo que esa fusión trajo de nuevo.
    /// </summary>
    private void UpdateSettings(Action<AppSettings> mutate)
    {
        var before = _settings;
        _settings = _store.Update(mutate);
        ApplyDelta(SettingsDelta.Between(before, _settings));
    }

    /// <summary>
    /// Guarda dónde está cada gato AHORA, para que la sesión a la que se cambia los muestre en el
    /// mismo sitio (también al que camina y al que nunca se movió con el ratón). Si otra sesión ya
    /// movió a un gato desde que lo vimos, a ese no se le pisa la posición.
    /// </summary>
    private void PersistPositions()
    {
        if (_agents.Count == 0) return;

        // Un gato sentado en la caja no está "en el suelo": su X es la de la caja.
        var cats = _agents.Values
            .Where(a => !a.InBox)
            .Select(a => new CatSnapshot(a.Key, a.Color, a.X))
            .ToList();
        var known = _settings.Cats.ToDictionary(kv => kv.Key, kv => kv.Value.X);

        // La caja también: los gatos la empujan. Igual que con los gatos, no se pisa si otra sesión
        // la movió desde la última vez que la vimos.
        double boxX = _world.Box.X;
        double? knownBox = _settings.BoxLeft;

        UpdateSettings(s =>
        {
            CatPositions.Apply(s, cats, known);
            if (s.BoxLeft is null || knownBox is null || Math.Abs(s.BoxLeft.Value - knownBox.Value) <= 0.5)
            {
                s.BoxLeft = boxX;
                s.BoxTop = null;
            }
        });
    }

    private void ApplyDelta(SettingsDelta delta)
    {
        if (!delta.Any) return;

        if (delta.ShowLabels)
            foreach (var sprite in _sprites.Values)
                sprite.ShowLabel(_settings.ShowLabels);

        if (delta.Box && !Box.IsMouseCaptured && _world.Box.OccupantId is null)
            RepositionBox();

        foreach (string key in delta.Cats)
        {
            if (!_settings.Cats.TryGetValue(key, out var pref) || !_agents.TryGetValue(key, out var agent))
                continue; // gato aún no creado: al crearse leerá _settings

            agent.Color = CatPalettes.Normalize(pref.Color ?? agent.Color);
            if (_sprites.TryGetValue(key, out var sprite))
                sprite.Render();

            // La posición solo se aplica si está en el suelo (el motor no mueve a uno que llevas,
            // que está en la caja o en mitad de un salto).
            if (pref.X is double px)
                _world.SetCatX(key, px);
        }
    }

    /// <summary>
    /// Enumera en proceso las apps de la sesión actual y las publica en el servicio. Solo con la
    /// sesión en pantalla: en segundo plano Windows no deja ver las ventanas, y el servicio conserva
    /// la última lista conocida de esa sesión.
    /// </summary>
    private async Task PublishOwnAppsAsync()
    {
        if (!_active) return;
        try
        {
            var apps = await Task.Run(() => LocalApps.Enumerate());
            AppLog.WriteOnChange("publish",
                $"Publicando {apps.Count} apps: {string.Join(", ", apps.Select(a => a.ProcessName))}");
            await _client.PublishAppsAsync(apps);
        }
        catch (Exception ex)
        {
            AppLog.WriteOnChange("publish-error", $"Error publicando apps: {ex.Message}");
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Eventos estáticos: hay que soltarlos o la ventana quedaría viva en memoria.
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;

        _loop.Stop();
        _refresh.Stop();
        _publish.Stop();
        _activity.Stop();
        _settingsDebounce.Stop();
        _boundsDebounce.Stop();
        _settingsWatcher?.Dispose();
        // Cada cambio ya se guardó al hacerlo (UpdateSettings), no hay nada pendiente.
    }

    // --- Tamaño de la ventana (toda la pantalla, fija) --------------------------

    private void ApplyStripBounds()
    {
        // La franja ocupa toda el ÁREA DE TRABAJO (pantalla menos la barra de tareas), fija. Se mide
        // en píxeles reales del monitor principal: si se pasa a otra pantalla con otra escala, la
        // ventana se recoloca ahí y el motor recibe el tamaño en px lógicos de esa pantalla.
        // La ventana baja además, sobre la barra de tareas, lo que los gatos dibujan por debajo de la
        // base (24 filas): la cola que cuelga, algo que se cae... El suelo del motor sigue siendo el
        // borde del área de trabajo.
        double below = _atlas.GridHeight - _atlas.BaseRows;
        if (_hwnd != IntPtr.Zero && DisplayNative.PrimaryWorkArea() is { } work)
        {
            DisplayNative.PlaceWindow(_hwnd, work.Pixels);
            // Si al moverla cambió de monitor (y de escala), WPF la redimensiona a su manera:
            // se vuelve a colocar ya con la escala nueva para que ocupe exactamente el área.
            double scale = DisplayNative.WindowScale(_hwnd);
            int extra = Math.Max(0, Math.Min((int)Math.Ceiling(below * scale), work.Monitor.Bottom - work.Pixels.Bottom));
            var rect = work.Pixels;
            rect.Bottom += extra;
            DisplayNative.PlaceWindow(_hwnd, rect);
            Left = rect.Left / scale;
            Top = rect.Top / scale;
            Width = rect.Width / scale;
            Height = rect.Height / scale;
            _world.Resize(work.Pixels.Width / scale, work.Pixels.Height / scale);
            EmptyHint.Margin = new Thickness(0, 0, 0, 8 + extra / scale);
        }
        else
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left;
            Top = area.Top;
            Width = area.Width;
            Height = area.Height;
            _world.Resize(Width, Height);
        }
        RepositionCats();
    }

    /// <summary>
    /// La ventana pasó a una pantalla con otra escala (o cambiaste la escala de Windows): los gatos
    /// se redibujan nítidos con la escala nueva y la franja se reajusta al área de trabajo.
    /// </summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        AppLog.Write($"Escala de pantalla {oldDpi.DpiScaleX:0.##} -> {newDpi.DpiScaleX:0.##}");
        foreach (var (_, sprite) in _sprites)
            sprite.UpdateScale(newDpi.DpiScaleX); // la nueva: los hijos pueden no haberla recibido aún
        RequestBoundsRefresh();
    }

    /// <summary>Ajusta el tamaño de los gatos y recoloca la caja (el motor ya ajustó el suelo).</summary>
    private void RepositionCats()
    {
        foreach (var (_, sprite) in _sprites)
            sprite.UpdateScale();
        RepositionBox();
    }

    /// <summary>Coloca la caja en su posición guardada, o por defecto en la esquina inferior izquierda.</summary>
    private void RepositionBox()
    {
        Box.Width = BoxW;
        Box.Height = BoxH;
        _world.PlaceBox(_settings.BoxLeft ?? 8); // la caja siempre descansa en el suelo
        RenderBox();
    }

    // --- Bucle de animación -----------------------------------------------------

    private void Loop_Tick(object? sender, EventArgs e)
    {
        long now = _clock.ElapsedTicks;
        double dt = (now - _lastTicks) / (double)Stopwatch.Frequency;
        _lastTicks = now;

        _world.Cursor = CursorInWorld();
        _world.Step(dt); // el motor limita los saltos de tiempo tras una pausa

        foreach (var (id, sprite) in _sprites)
        {
            if (sprite.Agent.Body is not { } cat) continue;
            PlaceSprite(sprite, cat);
            SetZ(sprite, cat.Pose.BehindBox ? -1 : 0); // dentro de la caja: detrás de ella
            sprite.Render();
        }

        RenderBox();
        Effects.Sync(_world.Particles);
        UpdateClickThrough();

        // Cada segundo, volver a ponerse por encima de la barra de tareas (al pulsarla se pone delante
        // y taparía lo que cuelga por debajo de la base). No con un menú abierto: quedaría detrás.
        if (++_topmostTicks >= 30)
        {
            _topmostTicks = 0;
            if (!InfoPopup.IsOpen && !ConfigPopup.IsOpen) DisplayNative.KeepOnTop(_hwnd);
        }
    }

    /// <summary>Dibuja la caja donde la tiene el motor (posición, abierta/cerrada y tambaleo).</summary>
    private void RenderBox()
    {
        var b = _world.Box;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double left = PixelScale.Snap(b.X, scale), top = PixelScale.Snap(b.Y, scale); // casillas sobre píxeles reales
        Canvas.SetLeft(Box, left);
        Canvas.SetTop(Box, top);
        Canvas.SetLeft(BoxFrame, left);
        Canvas.SetTop(BoxFrame, top);
        BoxFrame.Width = Box.Width;
        BoxFrame.Height = Box.Height;
        BoxFrame.StrokeThickness = 1 / scale; // 1 píxel real
        if (_boxShownOpen != b.Open)
        {
            _boxShownOpen = b.Open;
            Box.Source = BoxSprites.Get(b.Open);
        }
        if (_boxTilt.Angle != b.Wobble) _boxTilt.Angle = b.Wobble;
    }

    /// <summary>Cursor en coordenadas de la ventana (DIP), o null si está fuera de ella.</summary>
    private (double X, double Y)? CursorInWorld()
    {
        if (!InteropNative.GetCursorPos(out var p)) return null;
        try
        {
            var local = PointFromScreen(new Point(p.X, p.Y));
            if (local.X < 0 || local.Y < 0 || local.X > Width || local.Y > Height) return null;
            return (local.X, local.Y);
        }
        catch (InvalidOperationException)
        {
            return null; // ventana aún sin mostrar
        }
    }

    /// <summary>
    /// Coloca el gato en píxeles reales enteros: el motor calcula posiciones continuas, pero la
    /// cuadrícula solo se dibuja nítida si cae justo sobre los píxeles de la pantalla.
    /// </summary>
    private static void PlaceSprite(CatSprite sprite, Cat cat)
    {
        Canvas.SetLeft(sprite, PixelScale.Snap(cat.X, sprite.Scale));
        Canvas.SetTop(sprite, PixelScale.Snap(cat.Y, sprite.Scale));
    }

    private static void SetZ(UIElement el, int z)
    {
        if (Panel.GetZIndex(el) != z) Panel.SetZIndex(el, z);
    }

    /// <summary>
    /// Hace la ventana "click-through" salvo cuando el cursor está sobre un gato o la casa.
    /// En modo mover, la ventana captura todo el ratón para poder arrastrarla.
    /// </summary>
    private void UpdateClickThrough()
    {
        bool wantThrough = !_dragging && !CursorOverInteractive();
        if (wantThrough != _clickThrough)
        {
            InteropNative.SetClickThrough(_hwnd, wantThrough);
            _clickThrough = wantThrough;
        }
    }

    private bool CursorOverInteractive()
    {
        if (!InteropNative.GetCursorPos(out var p))
            return false;

        var dpi = VisualTreeHelper.GetDpi(this);

        // La caja: solo su figura visible (el PNG cerrado tiene ~16% de margen transparente arriba).
        var area = SpriteBounds.Opaque(BoxSprites.Grid(_world.Box.Open));
        var boxRect = new Rect(
            area.X * Box.ActualWidth, area.Y * Box.ActualHeight,
            area.Width * Box.ActualWidth, area.Height * Box.ActualHeight);
        if (IsPointOver(Box, boxRect, p, dpi)) return true;

        // Cada gato: la caja de sus píxeles visibles, según su pose y hacia dónde mira.
        foreach (var sprite in _sprites.Values)
            if (IsPointOver(sprite, sprite.GetHitRect(), p, dpi)) return true;
        return false;
    }

    /// <summary>¿Está el cursor (en píxeles de pantalla) dentro de <paramref name="local"/>, rectángulo en coordenadas del elemento?</summary>
    private static bool IsPointOver(FrameworkElement el, Rect local, InteropNative.POINT p, DpiScale dpi)
    {
        if (local.Width <= 0 || local.Height <= 0 || !el.IsVisible) return false;
        Point tl;
        try { tl = el.PointToScreen(local.TopLeft); }
        catch { return false; }
        double w = local.Width * dpi.DpiScaleX;
        double h = local.Height * dpi.DpiScaleY;
        return p.X >= tl.X && p.X <= tl.X + w && p.Y >= tl.Y && p.Y <= tl.Y + h;
    }

    // --- Sincronización de sesiones <-> gatos -----------------------------------

    private async Task RefreshUsersAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            ShowStatus(null);
            IReadOnlyList<UserInfo> users = await _client.GetUsersAsync();
            var liveKeys = users.Select(u => u.UserName).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Quitar gatos de usuarios que ya no existen.
            foreach (string gone in _agents.Keys.Where(k => !liveKeys.Contains(k)).ToList())
            {
                _world.RemoveCat(gone);
                if (_sprites.Remove(gone, out var sprite))
                    Yard.Children.Remove(sprite);
                _agents.Remove(gone);
            }

            double maxX = Math.Max(0, _world.Width - _atlas.GridWidth);
            bool placedNewCat = false; // algún gato recibió una X aleatoria que hay que compartir

            foreach (var u in users)
            {
                if (_agents.TryGetValue(u.UserName, out var agent))
                {
                    agent.IsCurrent = u.IsCurrent;
                    agent.SessionId = u.SessionId;
                    _world.SetMode(agent.Key, agent.Mode); // lanza la transición si cambió la sesión
                }
                else
                {
                    agent = new CatAgent
                    {
                        Key = u.UserName,
                        SessionId = u.SessionId,
                        DisplayName = u.UserName, // solo el nombre de usuario (sin dominio)
                        IsCurrent = u.IsCurrent,
                    };

                    // Preferencias por gato: color (por defecto, distinto por gato) y posición X.
                    _settings.Cats.TryGetValue(u.UserName, out var pref);
                    string defaultColor = CatPalettes.All[_agents.Count % CatPalettes.All.Count].Id;
                    agent.Color = CatPalettes.Normalize(pref?.Color ?? defaultColor);
                    double x;
                    if (pref?.X is double px) x = Math.Clamp(px, 0, maxX);
                    else { x = _rng.NextDouble() * maxX; placedNewCat = true; }

                    agent.Body = _world.AddCat(agent.Key, x, agent.Mode, facingRight: _rng.NextDouble() < 0.5);
                    _agents[u.UserName] = agent;

                    var sprite = new CatSprite(agent, _atlas);
                    sprite.ShowLabel(_settings.ShowLabels);
                    WireSprite(sprite);
                    PlaceSprite(sprite, agent.Body);
                    Yard.Children.Add(sprite);
                    _sprites[u.UserName] = sprite;
                }
            }

            // Sin esto, cada sesión sorteaba su propia X para los gatos nunca movidos y los veías en
            // sitios distintos al cambiar de usuario.
            if (placedNewCat) PersistPositions();

            UpdateEmptyHint(_sprites.Count == 0
                ? "No se detectaron usuarios."
                : null);
        }
        catch (ServiceUnavailableException ex)
        {
            ShowStatus(ex.Message);
            UpdateEmptyHint(ex.Message +
                "\nInstala el servicio: scripts\\install-service.ps1 (como administrador).");
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message);
            UpdateEmptyHint("Error: " + ex.Message);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void UpdateEmptyHint(string? message)
    {
        // Solo se muestra el aviso si no hay gatos en pantalla.
        if (_sprites.Count > 0 || string.IsNullOrEmpty(message))
        {
            EmptyHint.Visibility = Visibility.Collapsed;
        }
        else
        {
            EmptyHintText.Text = message;
            EmptyHint.Visibility = Visibility.Visible;
        }
    }

    private void WireSprite(CatSprite sprite)
    {
        // Clic IZQUIERDO mantenido = cargar/arrastrar; clic izquierdo solo = acariciar (corazón).
        // Clic DERECHO = abrir el menú de configuración del gato.
        sprite.MouseLeftButtonDown += Cat_Down;
        sprite.MouseMove += Cat_Move;
        sprite.MouseLeftButtonUp += Cat_Up;
        sprite.MouseRightButtonUp += (s, e) =>
        {
            OpenInfo((CatSprite)s);
            e.Handled = true;
        };
    }

    private void Cat_Down(object sender, MouseButtonEventArgs e)
    {
        var sprite = (CatSprite)sender;
        _catDrag = sprite;
        _catMoved = false;
        _dragging = true;
        _catGrab = e.GetPosition(sprite);
        sprite.CaptureMouse();
        e.Handled = true;
    }

    private void Cat_Move(object sender, MouseEventArgs e)
    {
        var sprite = (CatSprite)sender;
        if (!sprite.IsMouseCaptured || _catDrag != sprite || sprite.Agent.Body is not { } cat) return;

        var p = e.GetPosition(Yard);
        double x = p.X - _catGrab.X, y = p.Y - _catGrab.Y;
        if (!_catMoved)
        {
            // Solo se "coge" al moverlo de verdad: un clic sin mover es una caricia.
            if (Math.Abs(x - cat.X) < 3 && Math.Abs(y - cat.Y) < 3) return;
            _catMoved = true;
            _world.BeginCarry(cat.Id); // se baja de la caja si estaba en ella; pose "cargado"
        }
        _world.CarryTo(cat.Id, x, y);
        PlaceSprite(sprite, cat);
    }

    private void Cat_Up(object sender, MouseButtonEventArgs e)
    {
        var sprite = (CatSprite)sender;
        if (sprite.IsMouseCaptured) sprite.ReleaseMouseCapture();
        _catDrag = null;
        _dragging = false;
        if (sprite.Agent.Body is not { } cat) return;

        if (_catMoved)
        {
            // Soltarlo: si cae sobre la caja libre se queda en ella (encima o dentro); si no, cae.
            bool onBox = _world.Drop(cat.Id);
            if (!onBox)
            {
                var agent = sprite.Agent;
                double x = cat.X;
                UpdateSettings(s => GetOrAddCat(s, agent).X = x); // cae en vertical: la X ya es la final
            }
        }
        else
        {
            _world.Pet(cat.Id); // clic sin mover: caricia (corazón, y el gato reacciona según su estado)
        }
        e.Handled = true;
    }

    private void OpenInfo(CatSprite sprite)
    {
        if (_openAgent is not null)
        {
            _openAgent.MenuOpen = false;
            _world.SetAttention(_openAgent.Key, false);
        }
        _openAgent = sprite.Agent;
        _openAgent.MenuOpen = true;
        _world.SetAttention(_openAgent.Key, true); // te mira mientras ves su menú (dormido, no se entera)
        InfoPopup.DataContext = sprite.Agent;
        InfoPopup.PlacementTarget = sprite;
        InfoPopup.HorizontalOffset = 0;
        InfoPopup.VerticalOffset = 0;
        if (CatSettingsPanel is not null) CatSettingsPanel.Visibility = Visibility.Collapsed;
        PrepareMenuWindow(InfoPopup);
        InfoPopup.IsOpen = true;
        _ = LoadAppsAsync(sprite.Agent);
    }

    private void Gear_Click(object sender, RoutedEventArgs e)
    {
        CatSettingsPanel.Visibility = CatSettingsPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
    }

    // --- Arrastre de los menús (por el encabezado) ------------------------------

    private void InfoHeader_Down(object sender, MouseButtonEventArgs e) => MenuDragStart(InfoPopup, sender, e);
    private void InfoHeader_Move(object sender, MouseEventArgs e) => MenuDragMove(e);
    private void InfoHeader_Up(object sender, MouseButtonEventArgs e) => MenuDragEnd();

    private void MenuDragStart(System.Windows.Controls.Primitives.Popup popup, object sender, MouseButtonEventArgs e)
    {
        _menuTarget = popup;
        _menuDragEl = sender as UIElement;
        _menuStartH = popup.HorizontalOffset;
        _menuStartV = popup.VerticalOffset;
        InteropNative.GetCursorPos(out _menuStartCursor);
        _menuDragEl?.CaptureMouse();
        e.Handled = true;
    }

    private void MenuDragMove(MouseEventArgs e)
    {
        if (_menuTarget is null || _menuDragEl is null || e.LeftButton != MouseButtonState.Pressed) return;
        if (!InteropNative.GetCursorPos(out var cur)) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        double dx = (cur.X - _menuStartCursor.X) / dpi.DpiScaleX;
        double dy = (cur.Y - _menuStartCursor.Y) / dpi.DpiScaleY;
        _menuTarget.HorizontalOffset = _menuStartH + dx;
        _menuTarget.VerticalOffset = _menuStartV + dy;
    }

    private void MenuDragEnd()
    {
        _menuDragEl?.ReleaseMouseCapture();
        _menuDragEl = null;
        _menuTarget = null;
    }

    // --- Casa / configuración / mover -----------------------------------------

    private void Box_Down(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _boxMoved = false;
        _dragging = true;
        _boxStartLeft = _world.Box.X;
        _boxStartTop = _world.Box.Y;
        _boxGrab = e.GetPosition(Box);
        Box.CaptureMouse();
    }

    private void Box_Move(object sender, MouseEventArgs e)
    {
        if (!Box.IsMouseCaptured) return;
        var p = e.GetPosition(Yard);
        double left = p.X - _boxGrab.X, top = p.Y - _boxGrab.Y;
        if (!_boxMoved)
        {
            if (Math.Abs(left - _boxStartLeft) <= 3 && Math.Abs(top - _boxStartTop) <= 3) return;
            _boxMoved = true;
            _world.BeginBoxDrag();
        }
        _world.BoxDragTo(left, top); // el gato que esté en la caja va con ella
        RenderBox();
    }

    private void Box_Up(object sender, MouseButtonEventArgs e)
    {
        if (Box.IsMouseCaptured) Box.ReleaseMouseCapture();
        _dragging = false;

        if (_boxMoved)
        {
            _world.EndBoxDrag(); // cae por gravedad hasta el suelo
            // Solo guardamos la X; la caja cae por gravedad hasta el fondo.
            double left = _world.Box.X;
            UpdateSettings(s => { s.BoxLeft = left; s.BoxTop = null; });
        }
        else
        {
            // Clic izquierdo (sin arrastrar): solo abrir/cerrar la caja, nada más.
            SetBoxOpen(!_world.Box.Open);
        }
    }

    private void BoxRight_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenConfig(); // el menú se abre con el clic derecho
    }

    private void OpenConfig()
    {
        ShowStatus(null);
        SetBoxOpen(true); // la caja se abre al mostrar el menú
        ConfigPopup.HorizontalOffset = 0;
        ConfigPopup.VerticalOffset = 0;
        PrepareMenuWindow(ConfigPopup);
        ConfigPopup.IsOpen = true;
    }

    /// <summary>Abre o cierra la caja. El gato que esté en ella pasa de encima a dentro (o al revés).</summary>
    private void SetBoxOpen(bool open)
    {
        _world.SetBoxOpen(open);
        RenderBox();
    }

    /// <summary>
    /// Al abrirse un menú: con <c>MenuBlur</c> activo, desenfoca lo de detrás y usa el fondo más
    /// transparente (80%: tinte negro al 20%); si no, o si Windows rechaza el desenfoque, el fondo normal (30%).
    /// Se hace en cada apertura porque Windows crea una ventana nueva cada vez.
    /// </summary>
    private void Popup_Opened(object? sender, EventArgs e)
    {
        if (sender is not System.Windows.Controls.Primitives.Popup { Child: Border panel } popup) return;

        bool blurred = _settings.MenuBlur && BlurHelper.Apply(panel);
        panel.Background = (Brush)FindResource(blurred ? "MenuPanelBlurBrush" : "MenuPanelBrush");

        string region;
        if (!popup.AllowsTransparency)
        {
            // Windows 11: redondea DWM y el desenfoque queda recortado con esquinas suavizadas. El
            // panel usa el mismo radio que DWM para que su tinte encaje en la curva.
            panel.CornerRadius = new CornerRadius(MenuCorners.DwmRadius);
            region = BlurHelper.ApplyDwmRoundedCorners(panel);
        }
        else
        {
            // Respaldo: recorte por región (el desenfoque no lo respeta: puede asomar en las esquinas).
            panel.CornerRadius = (CornerRadius)FindResource("MenuCornerRadius");
            region = blurred ? BlurHelper.RoundCorners(panel, panel.CornerRadius.TopLeft) : "sin recorte";
        }

        AppLog.WriteOnChange("blur", _settings.MenuBlur
            ? $"Desenfoque de los menús: {(blurred ? "aceptado por Windows" : "RECHAZADO por Windows, se usa el fondo normal")}; {region}"
            : "Desenfoque de los menús desactivado (MenuBlur=false)");
    }

    /// <summary>
    /// Decide, ANTES de abrir un menú, qué ventana usa: en Windows 11 con desenfoque, una ventana no
    /// "layered" para que DWM pueda redondearla (ver <see cref="MenuCorners"/>). Solo se puede
    /// cambiar con el menú cerrado.
    /// </summary>
    private void PrepareMenuWindow(System.Windows.Controls.Primitives.Popup popup)
    {
        var mode = MenuCorners.Choose(Environment.OSVersion.Version.Build, _settings.MenuBlur);
        bool layered = mode == MenuCornerMode.Layered;
        if (!popup.IsOpen && popup.AllowsTransparency != layered)
            popup.AllowsTransparency = layered;
    }

    private async Task LoadAppsAsync(CatAgent agent)
    {
        if (!agent.HasSession)
        {
            agent.Apps.Clear();
            agent.Apps.Add(new AppRow { Name = "(sin sesión iniciada)" });
            agent.AppsLoading = false;
            return;
        }

        agent.AppsLoading = true;
        try
        {
            // La sesión actual se enumera EN PROCESO (fiable); las otras, vía el servicio.
            IReadOnlyList<AppInfo> apps = agent.IsCurrent
                ? await Task.Run(() => (IReadOnlyList<AppInfo>)LocalApps.Enumerate(logBreakdown: true))
                : await _client.GetWindowedAppsAsync(agent.SessionId);
            AppLog.Write($"LoadApps agente={agent.DisplayName} sesión={agent.SessionId} " +
                $"current={agent.IsCurrent} → {apps.Count} apps " +
                $"({(agent.IsCurrent ? "en proceso" : "del servicio")})");
            agent.Apps.Clear();
            if (apps.Count == 0)
            {
                agent.Apps.Add(new AppRow { Name = "(sin apps con ventana)" });
            }
            else
            {
                foreach (var a in apps)
                    agent.Apps.Add(new AppRow
                    {
                        Name = a.ProcessName,
                        Cpu = PercentFormat.Format(a.CpuPercent),
                        Ram = PercentFormat.Format(a.RamPercent),
                    });
            }
        }
        catch (Exception ex)
        {
            agent.Apps.Clear();
            agent.Apps.Add(new AppRow { Name = $"(error: {ex.Message})" });
        }
        finally
        {
            agent.AppsLoading = false;
        }
    }

    // --- Cambio de sesión -------------------------------------------------------

    private async void StartLogon_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Se mostrará la pantalla de inicio de sesión de Windows para que inicies esta cuenta. " +
            "Tu sesión actual quedará abierta en segundo plano. ¿Continuar?",
            "Swip", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        try
        {
            InfoPopup.IsOpen = false;
            PersistPositions();
            await _client.StartLogonAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Swip", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Switch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int sessionId }) return;

        try
        {
            InfoPopup.IsOpen = false;
            PersistPositions(); // la otra sesión leerá estas posiciones al ponerse en pantalla
            await _client.SwitchToSessionAsync(sessionId); // cambio directo, sin confirmación
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Swip", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // --- Personalización por gato (color) ---------------------------------------

    /// <summary>Una muestra de color por paleta del catálogo (las retintadas no necesitan arte nuevo).</summary>
    private void BuildColorSwatches()
    {
        ColorSwatches.Children.Clear();
        foreach (var palette in CatPalettes.All)
        {
            var c = palette.Swatch;
            var button = new Button
            {
                Style = (Style)FindResource("Swatch"),
                Tag = palette.Id,
                ToolTip = palette.Name,
                Background = new SolidColorBrush(Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c)),
            };
            button.Click += Color_Click;
            ColorSwatches.Children.Add(button);
        }
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (_openAgent is null || sender is not Button { Tag: string theme }) return;
        _openAgent.Color = CatPalettes.Normalize(theme);
        if (_sprites.TryGetValue(_openAgent.Key, out var sprite)) sprite.Render();
        SaveCatPref(_openAgent);
    }

    /// <summary>Guarda el color del gato SIN tocar el resto de sus datos (p. ej. su posición).</summary>
    private void SaveCatPref(CatAgent agent)
    {
        string color = agent.Color;
        UpdateSettings(s => GetOrAddCat(s, agent).Color = color);
    }

    /// <summary>Preferencias del gato; si no existen aún, se crean con su aspecto actual.</summary>
    private static CatPref GetOrAddCat(AppSettings settings, CatAgent agent)
    {
        if (!settings.Cats.TryGetValue(agent.Key, out var pref))
        {
            pref = new CatPref { Color = agent.Color };
            settings.Cats[agent.Key] = pref;
        }
        return pref;
    }

    // --- Opciones ---------------------------------------------------------------

    private void ToggleLabels_Click(object sender, RoutedEventArgs e)
    {
        bool show = !_settings.ShowLabels;
        UpdateSettings(s => s.ShowLabels = show); // ApplyDelta actualiza las etiquetas en pantalla
    }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        // update.ps1 se instala en la carpeta raíz de Swip (un nivel por encima de \App).
        string scriptPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "update.ps1"));

        if (!File.Exists(scriptPath))
        {
            MessageBox.Show(
                "No se encontró update.ps1. Reinstala con install.ps1 para habilitar la actualización.",
                "Swip", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            ConfigPopup.IsOpen = false;

            // En segundo plano: elevado (el aviso de UAC es inevitable, hay que parar el servicio y
            // escribir en Archivos de programa) pero SIN ventana. -Silent quita las pausas "Pulsa
            // Enter" (ocultas esperarían una tecla para siempre); el resultado queda en
            // %ProgramData%\Swip\update.log y, si falla, el propio script muestra un aviso.
            // Si va bien, se nota porque el gato desaparece unos segundos y vuelve.
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\" -Silent",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: rechazaste el aviso de UAC. No es un fallo: simplemente no se actualiza.
            AppLog.Write("Actualización cancelada en el aviso de UAC.");
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo iniciar la actualización: " + ex.Message,
                "Swip", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Exit_Click(object sender, RoutedEventArgs e)
    {
        // Avisar al servicio ANTES de cerrar: si no, su vigilante relanzaría el gato en ~30 s.
        // Si el servicio no responde, cerramos igualmente (el timeout del cliente es de 2 s).
        try { await _client.QuitSessionAsync(); }
        catch { /* sin servicio no hay vigilante que relance */ }
        Close();
    }

    private void ShowStatus(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            StatusText.Visibility = Visibility.Collapsed;
            StatusText.Text = string.Empty;
        }
        else
        {
            StatusText.Text = message;
            StatusText.Visibility = Visibility.Visible;
        }
    }
}
