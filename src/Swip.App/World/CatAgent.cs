using System.Collections.ObjectModel;
using System.ComponentModel;
using Swip.Shared.Art;
using Swip.Shared.Engine;

namespace Swip.App.World;

/// <summary>
/// Un gato = una cuenta de usuario. Reúne lo que muestra su menú (sesión, apps, aspecto) y apunta
/// a su cuerpo en el motor (<see cref="Body"/>), que es quien decide cómo se mueve.
/// </summary>
public sealed class CatAgent : INotifyPropertyChanged
{
    // --- Identidad / info de usuario (para el menú) ----------------------------
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;

    private int _sessionId = -1;
    public int SessionId
    {
        get => _sessionId;
        set { _sessionId = value; Raise(nameof(SessionId)); Raise(nameof(HasSession)); Raise(nameof(CanSwitch)); Raise(nameof(ShowNoSession)); }
    }

    public bool HasSession => _sessionId >= 0;

    private bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set { _isCurrent = value; Raise(nameof(IsCurrent)); Raise(nameof(CanSwitch)); Raise(nameof(ShowNoSession)); }
    }

    public bool CanSwitch => HasSession && !IsCurrent;
    public bool ShowNoSession => !HasSession && !IsCurrent;

    private bool _appsLoading;
    public bool AppsLoading
    {
        get => _appsLoading;
        set { _appsLoading = value; Raise(nameof(AppsLoading)); }
    }

    public ObservableCollection<AppRow> Apps { get; } = new();

    /// <summary>True mientras su menú está abierto.</summary>
    public bool MenuOpen { get; set; }

    // --- Apariencia ------------------------------------------------------------
    /// <summary>Su dibujo, fijo (ver Swip.Shared.Art.CatSkins): "orange" o "gray".</summary>
    public string Skin { get; set; } = CatSkins.Orange;

    // --- Motor -----------------------------------------------------------------
    /// <summary>Su cuerpo en el motor de movimientos.</summary>
    public Cat? Body { get; set; }

    /// <summary>Temperamento según la sesión: cerrada = dormir, en pantalla = caminar, en segundo plano = jugar.</summary>
    public CatMode Mode =>
        IsCurrent ? CatMode.Walk
        : HasSession ? CatMode.Play
        : CatMode.Sleep;

    public double X => Body?.X ?? 0;

    /// <summary>True si está en la caja (encima o dentro).</summary>
    public bool InBox => Body?.Place is CatPlace.OnBox or CatPlace.InBox;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>Una fila de la lista de apps: nombre, CPU% y RAM% (solo valores).</summary>
public sealed class AppRow
{
    public string Name { get; init; } = string.Empty;
    public string Cpu { get; init; } = string.Empty;
    public string Ram { get; init; } = string.Empty;
}
