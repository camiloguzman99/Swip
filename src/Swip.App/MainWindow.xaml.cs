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
using Swip.App.Models;
using Swip.App.Native;
using Swip.App.Services;
using Swip.App.World;
using Swip.Shared;

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
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Random _rng = new();

    private AppSettings _settings = new();
    private IntPtr _hwnd;
    private long _lastTicks;
    private bool _clickThrough = true;
    private CatAgent? _openAgent;
    private bool _refreshing;
    private bool _dragging; // mientras se arrastra caja/gato/menú: fuerza captura del ratón

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

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings = _store.Load();
        SetBoxOpen(false);
        ApplyStripBounds();

        _loop.Interval = TimeSpan.FromMilliseconds(33); // ~30 fps
        _loop.Tick += Loop_Tick;
        _lastTicks = _clock.ElapsedTicks;
        _loop.Start();

        _refresh.Interval = TimeSpan.FromSeconds(Math.Max(3, _settings.RefreshSeconds));
        _refresh.Tick += async (_, _) =>
        {
            await RefreshUsersAsync();
            if (_openAgent is not null) await LoadAppsAsync(_openAgent);
        };
        _refresh.Start();

        // Publicar al servicio las apps de NUESTRA propia sesión, para que el gato de la otra
        // sesión pueda mostrarlas (el servicio en sesión 0 no puede enumerarlas por sí mismo).
        _publish.Interval = TimeSpan.FromSeconds(8);
        _publish.Tick += async (_, _) => await PublishOwnAppsAsync();
        _publish.Start();
        _ = PublishOwnAppsAsync();

        await RefreshUsersAsync();
    }

    /// <summary>
    /// Enumera en proceso las apps de la sesión actual y las publica en el servicio. Así el gato
    /// de la otra sesión las puede leer sin que el servicio tenga que enumerar un escritorio ajeno.
    /// </summary>
    private async Task PublishOwnAppsAsync()
    {
        int ownSession = Process.GetCurrentProcess().SessionId;
        try
        {
            bool active = LocalApps.IsActiveConsoleSession();
            var apps = await Task.Run(() => LocalApps.Enumerate());
            AppLog.Write($"Publicando {apps.Count} apps de la sesión {ownSession} (activa={active}): " +
                string.Join(", ", apps.Select(a => a.ProcessName)));
            await _client.PublishAppsAsync(ownSession, apps, active);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Error publicando apps de la sesión {ownSession}: {ex.Message}");
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _store.Save(_settings);
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
        if (IsPointOver(Box, p, dpi)) return true;
        foreach (var sprite in _sprites.Values)
            if (IsPointOver(sprite, p, dpi)) return true;
        return false;
    }

    private static bool IsPointOver(FrameworkElement el, InteropNative.POINT p, DpiScale dpi)
    {
        if (el.ActualWidth <= 0 || !el.IsVisible) return false;
        Point tl;
        try { tl = el.PointToScreen(new Point(0, 0)); }
        catch { return false; }
        double w = el.ActualWidth * dpi.DpiScaleX;
        double h = el.ActualHeight * dpi.DpiScaleY;
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

            foreach (var u in users)
            {
                string stateText = CatAgent.DescribeState(u);

                if (_agents.TryGetValue(u.UserName, out var agent))
                {
                    agent.StateText = stateText;
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
                        StateText = stateText,
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
                // Guardar la posición horizontal (cae por gravedad hasta abajo).
                var pref = _settings.Cats.TryGetValue(sprite.Agent.Key, out var p) ? p : new CatPref();
                pref.Color = sprite.Agent.Color;
                pref.Fat = sprite.Agent.FatLevel;
                pref.X = sprite.Agent.X;
                _settings.Cats[sprite.Agent.Key] = pref;
                _store.Save(_settings);
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
            _settings.BoxLeft = Canvas.GetLeft(Box);
            _settings.BoxTop = null;
            _store.Save(_settings);
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

        double catW = sprite.SpriteWidth > 0 ? sprite.SpriteWidth : CatPx;
        agent.X = Math.Clamp(boxLeft + BoxW / 2 - catW / 2, 0, Math.Max(0, Width - catW));

        if (_boxOpen)
        {
            // Dentro de la caja: base al 50% del alto y por detrás de la caja (asoma por arriba).
            agent.Y = boxTop + BoxH * 0.5 - CatPx;
            SetZ(sprite, -1);
        }
        else
        {
            // Encima de la caja cerrada, por delante.
            agent.Y = boxTop - CatPx;
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

        double catW = sprite.SpriteWidth > 0 ? sprite.SpriteWidth : CatPx;
        double cx = sprite.Agent.X, cy = sprite.Agent.Y;
        bool overlapX = cx + catW > boxLeft && cx < boxLeft + BoxW;
        bool overlapY = cy + CatPx > boxTop - CatPx * 0.5 && cy < boxTop + BoxH;
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

    /// <summary>Aplica fondo negro translúcido con desenfoque (acrílico) al abrir un menú.</summary>
    private void Popup_Opened(object? sender, EventArgs e)
    {
        if (sender is System.Windows.Controls.Primitives.Popup { Child: Visual child })
            AcrylicHelper.Apply(child);
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
                ? await Task.Run(() => (IReadOnlyList<AppInfo>)LocalApps.Enumerate())
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
                        Cpu = $"{a.CpuPercent:0}%",
                        Ram = $"{a.RamPercent:0}%",
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

    private void SaveCatPref(CatAgent agent)
    {
        _settings.Cats[agent.Key] = new CatPref { Color = agent.Color, Fat = agent.FatLevel };
        _store.Save(_settings);
    }

    // --- Opciones ---------------------------------------------------------------

    private void ToggleLabels_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShowLabels = !_settings.ShowLabels;
        foreach (var sprite in _sprites.Values)
            sprite.ShowLabel(_settings.ShowLabels);
        _store.Save(_settings);
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
            // Se lanza elevado (UAC); el script detiene el gato, actualiza y lo relanza.
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Normal,
            };
            Process.Start(psi);
            ConfigPopup.IsOpen = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo iniciar la actualización: " + ex.Message,
                "Swip", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

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
