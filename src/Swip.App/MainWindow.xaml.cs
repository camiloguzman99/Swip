using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Swip.App.Models;
using Swip.App.Services;
using Swip.App.ViewModels;
using Swip.Shared;

namespace Swip.App;

public partial class MainWindow : Window
{
    private readonly ServiceClient _client = new();
    private readonly SettingsStore _store = new();
    private readonly ObservableCollection<SessionViewModel> _sessions = new();
    private readonly DispatcherTimer _refreshTimer = new();
    private AppSettings _settings = new();
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        SessionsList.ItemsSource = _sessions;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings = _store.Load();
        ApplySize(_settings.Width, _settings.Height);
        LockMenuItem.IsChecked = _settings.SizeLocked;

        if (_settings.Left is double l && _settings.Top is double t)
        {
            Left = l;
            Top = t;
        }
        else
        {
            SnapToTaskbar();
        }

        _refreshTimer.Interval = TimeSpan.FromSeconds(Math.Max(3, _settings.RefreshSeconds));
        _refreshTimer.Tick += async (_, _) => { if (MenuPopup.IsOpen) await RefreshAsync(); };
        _refreshTimer.Start();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _settings.Left = Left;
        _settings.Top = Top;
        _store.Save(_settings);
    }

    // --- Interacción con el gato ------------------------------------------------

    private async void Cat_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        double beforeLeft = Left, beforeTop = Top;
        try { DragMove(); } catch { /* DragMove falla si no hay botón presionado */ }

        bool moved = Math.Abs(Left - beforeLeft) > 2 || Math.Abs(Top - beforeTop) > 2;
        if (moved)
        {
            _settings.Left = Left;
            _settings.Top = Top;
            _store.Save(_settings);
            return;
        }

        // Fue un clic, no un arrastre: alternar el menú.
        if (MenuPopup.IsOpen)
        {
            MenuPopup.IsOpen = false;
        }
        else
        {
            MenuPopup.IsOpen = true;
            await RefreshAsync();
        }
    }

    private void Cat_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ContextMenu is not null)
        {
            ContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    // --- Refresco de sesiones y apps -------------------------------------------

    private async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            ShowStatus(null);
            IReadOnlyList<SessionInfo> sessions = await _client.GetSessionsAsync();

            _sessions.Clear();
            foreach (var s in sessions.OrderByDescending(s => s.IsCurrent).ThenBy(s => s.DisplayName))
            {
                var vm = new SessionViewModel
                {
                    SessionId = s.SessionId,
                    DisplayName = s.DisplayName,
                    IsCurrent = s.IsCurrent,
                    StateText = SessionViewModel.DescribeState(s.State, s.IsCurrent),
                };
                _sessions.Add(vm);
            }

            // Para las otras sesiones, cargar las apps con ventana en segundo plano.
            foreach (var vm in _sessions.Where(v => !v.IsCurrent))
                _ = LoadAppsAsync(vm);
        }
        catch (ServiceUnavailableException ex)
        {
            ShowStatus(ex.Message + "\nInstala el servicio con install-service.ps1 (como administrador).");
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task LoadAppsAsync(SessionViewModel vm)
    {
        vm.AppsLoading = true;
        vm.Apps.Clear();
        try
        {
            IReadOnlyList<AppInfo> apps = await _client.GetWindowedAppsAsync(vm.SessionId);
            if (apps.Count == 0)
            {
                vm.Apps.Add("(sin apps con ventana)");
            }
            else
            {
                foreach (var a in apps)
                    vm.Apps.Add($"• {FriendlyName(a)}");
            }
        }
        catch (Exception ex)
        {
            vm.Apps.Add($"(no se pudieron leer: {ex.Message})");
        }
        finally
        {
            vm.AppsLoading = false;
        }
    }

    private static string FriendlyName(AppInfo a) =>
        string.IsNullOrWhiteSpace(a.WindowTitle) ? a.ProcessName : $"{a.ProcessName} — {a.WindowTitle}";

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
            MenuPopup.IsOpen = false;
            await _client.SwitchToSessionAsync(sessionId);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Swip", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // --- Opciones del menú contextual ------------------------------------------

    private void LockSize_Click(object sender, RoutedEventArgs e)
    {
        _settings.SizeLocked = LockMenuItem.IsChecked;
        _store.Save(_settings);
    }

    private void SizeSmall_Click(object sender, RoutedEventArgs e) => SetSize(64);
    private void SizeMedium_Click(object sender, RoutedEventArgs e) => SetSize(96);
    private void SizeLarge_Click(object sender, RoutedEventArgs e) => SetSize(128);

    private void SetSize(double size)
    {
        if (_settings.SizeLocked)
        {
            MessageBox.Show(
                "El tamaño está bloqueado. Desmarca «Bloquear tamaño» para cambiarlo.",
                "Swip", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ApplySize(size, size);
        _settings.Width = size;
        _settings.Height = size;
        _store.Save(_settings);
    }

    private void ApplySize(double width, double height)
    {
        Width = width;
        Height = height;
    }

    private void SnapToTaskbar_Click(object sender, RoutedEventArgs e) => SnapToTaskbar();

    /// <summary>
    /// Coloca el gato en la esquina inferior derecha del área de trabajo, justo sobre la barra
    /// de tareas. Usa el área de trabajo para no quedar tapado por la barra.
    /// </summary>
    private void SnapToTaskbar()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Right - Width - 8;
        Top = work.Bottom - Height - 4;
        _settings.Left = Left;
        _settings.Top = Top;
        _store.Save(_settings);
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
