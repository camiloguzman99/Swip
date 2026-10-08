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

    // Arrastre de la caja
    private bool _boxMoved;
    private double _boxStartLeft, _boxStartTop;
    private Point _boxGrab;
    private double _boxVy;
    private bool _boxOpen;              // si la caja está abierta ahora mismo
    private CatAgent? _catOnBox;        // gato sentado en la caja (como máximo uno)

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
            if (_openAgent is not null) _openAgent.MenuOpen = false;
            _openAgent = null;
        };
        // Al cerrar la configuración, la caja se vuelve a cerrar.
        ConfigPopup.Closed += (_, _) => SetBoxOpen(false);

        _settingsDebounce.Interval = TimeSpan.FromMilliseconds(300);
        _settingsDebounce.Tick += (_, _) => { _settingsDebounce.Stop(); ApplySettingsFromDisk(); };
        _boundsDebounce.Interval = TimeSpan.FromMilliseconds(400);
        _boundsDebounce.Tick += (_, _) => { _boundsDebounce.Stop(); if (_active) ApplyStripBounds(); };

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
    private const double CatPx = 48;   // alto del gato en pantalla
    private const double BoxH = 36;    // alto de la caja (reducido 40%)
    private double BoxW => BoxH * (BoxSprites.AspectW / BoxSprites.AspectH);

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
            .Where(a => !a.OnBox)
            .Select(a => new CatSnapshot(a.Key, a.Color, a.FatLevel, a.X))
            .ToList();
        var known = _settings.Cats.ToDictionary(kv => kv.Key, kv => kv.Value.X);

        UpdateSettings(s => CatPositions.Apply(s, cats, known));
    }

    private void ApplyDelta(SettingsDelta delta)
    {
        if (!delta.Any) return;

        if (delta.ShowLabels)
            foreach (var sprite in _sprites.Values)
                sprite.ShowLabel(_settings.ShowLabels);

        if (delta.Box && !Box.IsMouseCaptured)
            RepositionBox();

        foreach (string key in delta.Cats)
        {
            if (!_settings.Cats.TryGetValue(key, out var pref) || !_agents.TryGetValue(key, out var agent))
                continue; // gato aún no creado: al crearse leerá _settings

            agent.Color = CatSprites.Normalize(pref.Color ?? agent.Color);
            agent.FatLevel = pref.Fat;
            if (_sprites.TryGetValue(key, out var sprite))
            {
                sprite.ApplyFat();
                sprite.Render();
            }

            // La posición solo se aplica si no lo estamos sujetando ni está sentado en la caja.
            if (pref.X is double px && !agent.Dragging && !agent.OnBox)
                agent.X = Math.Clamp(px, 0, Math.Max(0, Width - CatPx));
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
        // La franja ocupa toda el ÁREA DE TRABAJO (pantalla menos la barra de tareas), fija.
        var work = SystemParameters.WorkArea;
        Left = work.Left;
        Top = work.Top;
        Width = work.Width;
        Height = work.Height;
        RepositionCats();
    }

    /// <summary>Recalcula la "línea de suelo" y reubica a los gatos dentro de la franja.</summary>
    private void RepositionCats()
    {
        double baseY = Height - CatPx;
        double maxX = Math.Max(0, Width - CatPx);
        foreach (var (_, agent) in _agents)
        {
            agent.Y = baseY;
            if (agent.X > maxX) agent.X = maxX;
            if (agent.X < 0) agent.X = 0;
        }
        foreach (var (_, sprite) in _sprites)
            sprite.SetCatSize(CatPx);

        RepositionBox();
    }

    /// <summary>Coloca la caja en su posición guardada, o por defecto en la esquina inferior izquierda.</summary>
    private void RepositionBox()
    {
        Box.Width = BoxW;
        Box.Height = BoxH;
        double left = _settings.BoxLeft ?? 8;
        double top = _settings.BoxTop ?? (Height - BoxH);
        left = Math.Clamp(left, 0, Math.Max(0, Width - BoxW));
        top = Math.Clamp(top, 0, Math.Max(0, Height - BoxH));
        Canvas.SetLeft(Box, left);
        Canvas.SetTop(Box, top);
    }

    // --- Bucle de animación -----------------------------------------------------

    private void Loop_Tick(object? sender, EventArgs e)
    {
        long now = _clock.ElapsedTicks;
        double dt = (now - _lastTicks) / (double)Stopwatch.Frequency;
        _lastTicks = now;
        if (dt > 0.1) dt = 0.1; // evita saltos tras pausas

        foreach (var (id, agent) in _agents)
        {
            agent.Update(dt, Width, Height, CatPx);
            if (_sprites.TryGetValue(id, out var sprite))
            {
                if (agent.OnBox) PlaceOnBox(agent, sprite);
                Canvas.SetLeft(sprite, agent.X);
                Canvas.SetTop(sprite, agent.Y);
                sprite.Render();
            }
        }

        UpdateBoxGravity(dt);
        UpdateClickThrough();
    }

    /// <summary>La caja también cae por gravedad hasta el fondo cuando no se está arrastrando.</summary>
    private void UpdateBoxGravity(double dt)
    {
        if (Box.IsMouseCaptured) return;
        double floor = Math.Max(0, Height - BoxH);
        double by = Canvas.GetTop(Box);
        if (double.IsNaN(by)) { Canvas.SetTop(Box, floor); return; }

        if (by < floor - 0.5)
        {
            _boxVy += 2200 * dt;
            by += _boxVy * dt;
            if (by >= floor) { by = floor; _boxVy = 0; }
            Canvas.SetTop(Box, by);
        }
        else if (by != floor)
        {
            _boxVy = 0;
            Canvas.SetTop(Box, floor);
        }
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
        var area = SpriteBounds.Opaque(Box.Source);
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
                if (_catOnBox is not null && _catOnBox.Key == gone) _catOnBox = null;
                if (_sprites.Remove(gone, out var sprite))
                    Yard.Children.Remove(sprite);
                _agents.Remove(gone);
            }

            double maxX = Math.Max(0, Width - CatPx);
            double baseY = Height - CatPx;
            bool placedNewCat = false; // algún gato recibió una X aleatoria que hay que compartir

            foreach (var u in users)
            {
                if (_agents.TryGetValue(u.UserName, out var agent))
                {
                    agent.IsCurrent = u.IsCurrent;
                    agent.SessionId = u.SessionId;
                }
                else
                {
                    agent = new CatAgent
                    {
                        Key = u.UserName,
                        SessionId = u.SessionId,
                        DisplayName = u.UserName, // solo el nombre de usuario (sin dominio)
                        IsCurrent = u.IsCurrent,
                        Y = baseY,
                        X = _rng.NextDouble() * maxX,
                        FacingRight = _rng.NextDouble() < 0.5,
                    };

                    // Preferencias por gato: color (por defecto, distinto por gato), gordura y posición X.
                    _settings.Cats.TryGetValue(u.UserName, out var pref);
                    string defaultColor = CatSprites.Colors[_agents.Count % CatSprites.Colors.Length];
                    agent.Color = CatSprites.Normalize(pref?.Color ?? defaultColor);
                    agent.FatLevel = pref?.Fat ?? 0;
                    if (pref?.X is double px) agent.X = Math.Clamp(px, 0, maxX);
                    else placedNewCat = true;

                    _agents[u.UserName] = agent;

                    var sprite = new CatSprite(agent, CatPx);
                    sprite.ShowLabel(_settings.ShowLabels);
                    WireSprite(sprite);
                    Canvas.SetLeft(sprite, agent.X);
                    Canvas.SetTop(sprite, agent.Y);
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
        if (sprite.Agent.OnBox) DetachFromBox(sprite.Agent); // al cogerlo, se baja de la caja
        sprite.Agent.Dragging = true; // acción "cargado"
        _catGrab = e.GetPosition(sprite);
        sprite.CaptureMouse();
        e.Handled = true;
    }

    private void Cat_Move(object sender, MouseEventArgs e)
    {
        var sprite = (CatSprite)sender;
        if (!sprite.IsMouseCaptured || _catDrag != sprite) return;

        var p = e.GetPosition(Yard);
        double x = Math.Clamp(p.X - _catGrab.X, 0, Math.Max(0, Width - CatPx));
        double y = Math.Clamp(p.Y - _catGrab.Y, 0, Math.Max(0, Height));
        sprite.Agent.X = x;
        sprite.Agent.Y = y;
        sprite.Agent.Vy = 0;
        Canvas.SetLeft(sprite, x);
        Canvas.SetTop(sprite, y);
        _catMoved = true;
    }

    private void Cat_Up(object sender, MouseButtonEventArgs e)
    {
        var sprite = (CatSprite)sender;
        if (sprite.IsMouseCaptured) sprite.ReleaseMouseCapture();
        sprite.Agent.Dragging = false; // al soltar, la gravedad lo hace caer al suelo
        _catDrag = null;
        _dragging = false;

        if (_catMoved)
        {
            if (DroppedOnBox(sprite))
            {
                // Se soltó sobre la caja: se sienta en ella.
                AttachToBox(sprite.Agent);
            }
            else
            {
                // Guardar solo la posición horizontal (cae por gravedad hasta abajo).
                var agent = sprite.Agent;
                double x = agent.X;
                UpdateSettings(s => GetOrAddCat(s, agent).X = x);
            }
        }
        else
        {
            sprite.Agent.Pet(); // clic izquierdo sin mover: acariciar (corazón)
        }
        e.Handled = true;
    }

    private void OpenInfo(CatSprite sprite)
    {
        if (_openAgent is not null) _openAgent.MenuOpen = false;
        _openAgent = sprite.Agent;
        _openAgent.MenuOpen = true; // el gato se pone a jugar mientras ves su menú
        InfoPopup.DataContext = sprite.Agent;
        InfoPopup.PlacementTarget = sprite;
        InfoPopup.HorizontalOffset = 0;
        InfoPopup.VerticalOffset = 0;
        if (CatSettingsPanel is not null) CatSettingsPanel.Visibility = Visibility.Collapsed;
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
        _boxStartLeft = Canvas.GetLeft(Box);
        _boxStartTop = Canvas.GetTop(Box);
        _boxGrab = e.GetPosition(Box);
        Box.CaptureMouse();
    }

    private void Box_Move(object sender, MouseEventArgs e)
    {
        if (!Box.IsMouseCaptured) return;
        var p = e.GetPosition(Yard);
        double left = Math.Clamp(p.X - _boxGrab.X, 0, Math.Max(0, Width - BoxW));
        double top = Math.Clamp(p.Y - _boxGrab.Y, 0, Math.Max(0, Height - BoxH));
        Canvas.SetLeft(Box, left);
        Canvas.SetTop(Box, top);
        if (Math.Abs(left - _boxStartLeft) > 3 || Math.Abs(top - _boxStartTop) > 3)
            _boxMoved = true;
    }

    private void Box_Up(object sender, MouseButtonEventArgs e)
    {
        if (Box.IsMouseCaptured) Box.ReleaseMouseCapture();
        _dragging = false;

        if (_boxMoved)
        {
            // Solo guardamos la X; la caja cae por gravedad hasta el fondo.
            double left = Canvas.GetLeft(Box);
            UpdateSettings(s => { s.BoxLeft = left; s.BoxTop = null; });
        }
        else
        {
            // Clic izquierdo (sin arrastrar): solo abrir/cerrar la caja, nada más.
            SetBoxOpen(!_boxOpen);
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
        ConfigPopup.IsOpen = true;
    }

    /// <summary>Abre o cierra la caja (sprite + estado), manteniéndolos sincronizados.</summary>
    private void SetBoxOpen(bool open)
    {
        _boxOpen = open;
        Box.Source = BoxSprites.Get(open);
    }

    // --- Colisión: sentar un gato en la caja ------------------------------------

    /// <summary>
    /// Fija la posición del gato que está sentado en la caja: encima de la caja cerrada, o dentro
    /// de la caja abierta (detrás, con una base al 50% del alto para que asome por arriba).
    /// </summary>
    private void PlaceOnBox(CatAgent agent, CatSprite sprite)
    {
        double boxLeft = Canvas.GetLeft(Box);
        double boxTop = Canvas.GetTop(Box);
        if (double.IsNaN(boxLeft)) boxLeft = 0;
        if (double.IsNaN(boxTop)) boxTop = Math.Max(0, Height - BoxH);

        // Se centra con el ancho REAL del control (la etiqueta puede hacerlo más ancho que la imagen,
        // que va centrada dentro), no con el ancho de la imagen.
        double catW = sprite.ActualWidth > 0 ? sprite.ActualWidth : CatPx;
        agent.X = Math.Clamp(boxLeft + BoxW / 2 - catW / 2, 0, Math.Max(0, Width - catW));

        // Borde superior VISIBLE de la caja en su estado actual (los PNG tienen distinto margen).
        double visibleTop = boxTop + BoxH * BoxSprites.TopInset(_boxOpen);
        double visibleH = boxTop + BoxH - visibleTop;

        if (_boxOpen)
        {
            // Dentro de la caja: base al 50% del alto visible y por detrás (asoma por arriba).
            agent.Y = visibleTop + visibleH * 0.5 - CatPx;
            SetZ(sprite, -1);
        }
        else
        {
            // Encima de la caja cerrada, apoyado en su borde visible, por delante.
            agent.Y = visibleTop - CatPx;
            SetZ(sprite, 0);
        }
    }

    private static void SetZ(UIElement el, int z)
    {
        if (Panel.GetZIndex(el) != z) Panel.SetZIndex(el, z);
    }

    /// <summary>¿El gato se soltó encima de la caja? (solapamiento de rectángulos).</summary>
    private bool DroppedOnBox(CatSprite sprite)
    {
        double boxLeft = Canvas.GetLeft(Box);
        double boxTop = Canvas.GetTop(Box);
        if (double.IsNaN(boxLeft) || double.IsNaN(boxTop)) return false;

        double catW = sprite.ActualWidth > 0 ? sprite.ActualWidth : CatPx;
        double cx = sprite.Agent.X + catW / 2, cy = sprite.Agent.Y; // cx = centro del gato
        double visibleTop = boxTop + BoxH * BoxSprites.TopInset(_boxOpen);
        bool overlapX = cx > boxLeft && cx < boxLeft + BoxW;
        bool overlapY = cy + CatPx > visibleTop - CatPx * 0.5 && cy < boxTop + BoxH;
        return overlapX && overlapY;
    }

    /// <summary>Sienta al gato en la caja (solo uno a la vez: el anterior se baja).</summary>
    private void AttachToBox(CatAgent agent)
    {
        if (_catOnBox is not null && _catOnBox != agent)
            DetachFromBox(_catOnBox);
        _catOnBox = agent;
        agent.OnBox = true; // la posición se aplica en el siguiente tick (PlaceOnBox)
    }

    private void DetachFromBox(CatAgent agent)
    {
        agent.OnBox = false;
        if (_catOnBox == agent) _catOnBox = null;
        if (_sprites.TryGetValue(agent.Key, out var sprite)) SetZ(sprite, 0);
    }

    /// <summary>
    /// Al abrirse un menú: con <c>MenuBlur</c> activo, desenfoca lo de detrás y usa el fondo más
    /// transparente (80%: tinte negro al 20%); si no, o si Windows rechaza el desenfoque, el fondo normal (30%).
    /// Se hace en cada apertura porque Windows crea una ventana nueva cada vez.
    /// </summary>
    private void Popup_Opened(object? sender, EventArgs e)
    {
        if (sender is not System.Windows.Controls.Primitives.Popup { Child: Border panel }) return;

        bool blurred = _settings.MenuBlur && BlurHelper.Apply(panel);
        panel.Background = (Brush)FindResource(blurred ? "MenuPanelBlurBrush" : "MenuPanelBrush");

        // El desenfoque cubre toda la ventana (un rectángulo) y el panel solo su forma redondeada:
        // se recorta la ventana a esa forma (y se mantiene al cambiar de tamaño).
        string region = blurred ? BlurHelper.RoundCorners(panel, panel.CornerRadius.TopLeft) : "sin recorte";

        AppLog.WriteOnChange("blur", _settings.MenuBlur
            ? $"Desenfoque de los menús: {(blurred ? "aceptado por Windows" : "RECHAZADO por Windows, se usa el fondo normal")}; {region}"
            : "Desenfoque de los menús desactivado (MenuBlur=false)");
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

    // --- Personalización por gato (color / gordura) -----------------------------

    private void Fatten_Click(object sender, RoutedEventArgs e) => ChangeFat(+1);
    private void Slim_Click(object sender, RoutedEventArgs e) => ChangeFat(-1);

    private void ChangeFat(int delta)
    {
        if (_openAgent is null) return;
        _openAgent.FatLevel = Math.Clamp(_openAgent.FatLevel + delta, 0, 6);
        if (_sprites.TryGetValue(_openAgent.Key, out var sprite)) sprite.ApplyFat();
        SaveCatPref(_openAgent);
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (_openAgent is null || sender is not Button { Tag: string theme }) return;
        _openAgent.Color = CatSprites.Normalize(theme);
        if (_sprites.TryGetValue(_openAgent.Key, out var sprite)) sprite.Render();
        SaveCatPref(_openAgent);
    }

    /// <summary>Guarda color y gordura del gato SIN tocar el resto de sus datos (p. ej. su posición).</summary>
    private void SaveCatPref(CatAgent agent)
    {
        string color = agent.Color;
        int fat = agent.FatLevel;
        UpdateSettings(s =>
        {
            var pref = GetOrAddCat(s, agent);
            pref.Color = color;
            pref.Fat = fat;
        });
    }

    /// <summary>Preferencias del gato; si no existen aún, se crean con su aspecto actual.</summary>
    private static CatPref GetOrAddCat(AppSettings settings, CatAgent agent)
    {
        if (!settings.Cats.TryGetValue(agent.Key, out var pref))
        {
            pref = new CatPref { Color = agent.Color, Fat = agent.FatLevel };
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
