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
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Random _rng = new();

    private AppSettings _settings = new();
    private IntPtr _hwnd;
    private long _lastTicks;
    private bool _clickThrough = true;
    private CatAgent? _openAgent;
    private bool _refreshing;
    private bool _moveMode;

    private static readonly Brush StripBrush = CreateStripBrush();
    private static Brush CreateStripBrush()
    {
        var b = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xCD, 0x1A));
        b.Freeze();
        return b;
    }

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
        InfoPopup.Closed += (_, _) => _openAgent = null;
    }

    private double HouseSize => Math.Clamp(_settings.CatSize, 48, 96);

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
        House.Source = PixelHouse.Get();
        Yard.MouseLeftButtonDown += Yard_MouseLeftButtonDown;
        ApplyStripBounds();
        ApplyStripBackground();
        UpdateConfigLabels();

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

        await RefreshUsersAsync();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _settings.Left = Left;
        _settings.Top = Top;
        _store.Save(_settings);
    }

    // --- Posición y tamaño de la franja ----------------------------------------

    private void ApplyStripBounds()
    {
        var work = SystemParameters.WorkArea;
        Width = _settings.StripWidth ?? work.Width;
        Height = _settings.StripHeight;

        if (_settings.PositionLocked && _settings.Left is double l && _settings.Top is double t)
        {
            Left = l;
            Top = t;
        }
        else
        {
            Left = work.Left;
            Top = work.Bottom - Height; // justo sobre la barra de tareas
        }

        RepositionCats();
    }

    /// <summary>Recalcula la "línea de suelo" y reubica a los gatos dentro de la franja.</summary>
    private void RepositionCats()
    {
        double baseY = Height - _settings.CatSize;
        double maxX = Math.Max(0, Width - _settings.CatSize);
        foreach (var (_, agent) in _agents)
        {
            agent.BaseY = baseY;
            if (agent.X > maxX) agent.X = maxX;
            if (agent.X < 0) agent.X = 0;
        }
        foreach (var (_, sprite) in _sprites)
            sprite.SetCatSize(_settings.CatSize);

        // La casa vive en la esquina inferior izquierda, sobre el suelo.
        House.Width = HouseSize;
        House.Height = HouseSize;
        Canvas.SetLeft(House, 8);
        Canvas.SetTop(House, Height - HouseSize);
    }

    private void ApplyStripBackground()
    {
        // Fondo visible si el usuario lo activó o mientras se mueve la ventana.
        Yard.Background = (_settings.ShowStripBackground || _moveMode) ? StripBrush : null;
    }

    private void UpdateConfigLabels()
    {
        MoveBtn.Content = _moveMode ? "Terminar de mover" : "Mover ventana";
        VisibleBtn.Content = _settings.ShowStripBackground ? "Ocultar la franja" : "Hacer visible la ventana";
        LabelsBtn.Content = _settings.ShowLabels ? "Ocultar etiquetas" : "Mostrar etiquetas";
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
            agent.Update(dt, Width, Height, _settings.CatSize);
            if (_sprites.TryGetValue(id, out var sprite))
            {
                Canvas.SetLeft(sprite, agent.X);
                Canvas.SetTop(sprite, agent.Y);
                sprite.Render();
            }
        }

        UpdateClickThrough();
    }

    /// <summary>
    /// Hace la ventana "click-through" salvo cuando el cursor está sobre un gato o la casa.
    /// En modo mover, la ventana captura todo el ratón para poder arrastrarla.
    /// </summary>
    private void UpdateClickThrough()
    {
        bool wantThrough = !_moveMode && !CursorOverInteractive();
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
        if (IsPointOver(House, p, dpi)) return true;
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
                if (_sprites.Remove(gone, out var sprite))
                    Yard.Children.Remove(sprite);
                _agents.Remove(gone);
            }

            double maxX = Math.Max(0, Width - _settings.CatSize);
            double baseY = Height - _settings.CatSize;

            foreach (var u in users)
            {
                var state = CatAgent.StateFor(u);
                string stateText = CatAgent.DescribeState(u);

                if (_agents.TryGetValue(u.UserName, out var agent))
                {
                    agent.State = state;
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
                        DisplayName = u.DisplayName,
                        IsCurrent = u.IsCurrent,
                        State = state,
                        StateText = stateText,
                        BaseY = baseY,
                        X = _rng.NextDouble() * maxX,
                        FacingRight = _rng.NextDouble() < 0.5,
                    };
                    agent.Y = baseY;
                    _agents[u.UserName] = agent;

                    var sprite = new CatSprite(agent, _settings.CatSize);
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
        sprite.MouseLeftButtonDown += (_, e) =>
        {
            sprite.Agent.Interact();
            e.Handled = true;
        };
        sprite.MouseRightButtonUp += (_, e) =>
        {
            OpenInfo(sprite);
            e.Handled = true;
        };
    }

    private void OpenInfo(CatSprite sprite)
    {
        _openAgent = sprite.Agent;
        InfoPopup.DataContext = sprite.Agent;
        InfoPopup.PlacementTarget = sprite;
        InfoPopup.IsOpen = true;
        _ = LoadAppsAsync(sprite.Agent);
    }

    // --- Casa / configuración / mover -----------------------------------------

    private void House_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_moveMode)
        {
            ExitMoveMode(); // en modo mover, pulsar la casa termina de mover
            return;
        }
        ShowStatus(null);
        UpdateConfigLabels();
        ConfigPopup.IsOpen = true;
    }

    private void Yard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_moveMode) return;
        try { DragMove(); } catch { /* sin botón presionado */ }
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.PositionLocked = true;
        _store.Save(_settings);
    }

    private void ToggleMove_Click(object sender, RoutedEventArgs e)
    {
        ConfigPopup.IsOpen = false;
        if (_moveMode) ExitMoveMode();
        else EnterMoveMode();
    }

    private void EnterMoveMode()
    {
        _moveMode = true;
        MoveHint.Visibility = Visibility.Visible;
        ApplyStripBackground();
        UpdateConfigLabels();
        InteropNative.SetClickThrough(_hwnd, false);
        _clickThrough = false;
    }

    private void ExitMoveMode()
    {
        _moveMode = false;
        MoveHint.Visibility = Visibility.Collapsed;
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.PositionLocked = true;
        _store.Save(_settings);
        ApplyStripBackground();
        UpdateConfigLabels();
    }

    private void ToggleVisible_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShowStripBackground = !_settings.ShowStripBackground;
        _store.Save(_settings);
        ApplyStripBackground();
        UpdateConfigLabels();
    }

    private async Task LoadAppsAsync(CatAgent agent)
    {
        if (!agent.HasSession)
        {
            agent.Apps.Clear();
            agent.Apps.Add("(sin sesión iniciada)");
            agent.AppsLoading = false;
            return;
        }

        agent.AppsLoading = true;
        try
        {
            IReadOnlyList<AppInfo> apps = await _client.GetWindowedAppsAsync(agent.SessionId);
            agent.Apps.Clear();
            if (apps.Count == 0)
            {
                agent.Apps.Add("(sin apps con ventana)");
            }
            else
            {
                foreach (var a in apps)
                    agent.Apps.Add("• " + (string.IsNullOrWhiteSpace(a.WindowTitle)
                        ? a.ProcessName
                        : $"{a.ProcessName} — {a.WindowTitle}"));
            }
        }
        catch (Exception ex)
        {
            agent.Apps.Clear();
            agent.Apps.Add($"(no se pudieron leer: {ex.Message})");
        }
        finally
        {
            agent.AppsLoading = false;
        }
    }

    // --- Cambio de sesión -------------------------------------------------------

    private async void Switch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int sessionId }) return;

        var confirm = MessageBox.Show(
            "¿Cambiar a esta sesión? La sesión actual quedará abierta en segundo plano.",
            "Swip", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            InfoPopup.IsOpen = false;
            await _client.SwitchToSessionAsync(sessionId);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Swip", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // --- Opciones ---------------------------------------------------------------

    private void CatsBigger_Click(object sender, RoutedEventArgs e) => ChangeCatSize(+16);
    private void CatsSmaller_Click(object sender, RoutedEventArgs e) => ChangeCatSize(-16);

    private void ChangeCatSize(double delta)
    {
        _settings.CatSize = Math.Clamp(_settings.CatSize + delta, 32, 160);
        RepositionCats();
        _store.Save(_settings);
    }

    private void StripTaller_Click(object sender, RoutedEventArgs e) => ChangeStripHeight(+40);
    private void StripShorter_Click(object sender, RoutedEventArgs e) => ChangeStripHeight(-40);

    private void ChangeStripHeight(double delta)
    {
        _settings.StripHeight = Math.Clamp(_settings.StripHeight + delta, 100, 400);
        _store.Save(_settings);
        ApplyStripBounds();
    }

    private void SnapToTaskbar_Click(object sender, RoutedEventArgs e)
    {
        _settings.PositionLocked = false;
        _settings.Left = null;
        _settings.Top = null;
        _settings.StripWidth = null;
        _store.Save(_settings);
        ApplyStripBounds();
    }

    private void ToggleLabels_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShowLabels = !_settings.ShowLabels;
        foreach (var sprite in _sprites.Values)
            sprite.ShowLabel(_settings.ShowLabels);
        _store.Save(_settings);
        UpdateConfigLabels();
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
