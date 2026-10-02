using System.Collections.ObjectModel;
using System.ComponentModel;
using Swip.Shared;

namespace Swip.App.ViewModels;

/// <summary>Una sesión tal como se muestra en el menú del gato.</summary>
public sealed class SessionViewModel : INotifyPropertyChanged
{
    private string _stateText = string.Empty;
    private bool _appsLoading;

    public int SessionId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public bool IsCurrent { get; init; }

    /// <summary>Texto legible del estado, por ejemplo "Abierta en el otro escritorio".</summary>
    public string StateText
    {
        get => _stateText;
        set { _stateText = value; OnChanged(nameof(StateText)); }
    }

    /// <summary>True mientras se cargan las apps de esta sesión.</summary>
    public bool AppsLoading
    {
        get => _appsLoading;
        set { _appsLoading = value; OnChanged(nameof(AppsLoading)); }
    }

    /// <summary>Se puede cambiar a esta sesión (es decir, no es la actual).</summary>
    public bool CanSwitch => !IsCurrent;

    /// <summary>Apps con ventana abiertas en esta sesión (solo se puebla para la otra sesión).</summary>
    public ObservableCollection<string> Apps { get; } = new();

    public static string DescribeState(SessionConnectionState state, bool isCurrent) => isCurrent
        ? "En esta pantalla ahora"
        : state switch
        {
            SessionConnectionState.Active => "Activa",
            SessionConnectionState.Connected => "Conectada",
            SessionConnectionState.Disconnected => "Abierta en el otro escritorio",
            SessionConnectionState.Idle => "Inactiva",
            _ => state.ToString(),
        };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
