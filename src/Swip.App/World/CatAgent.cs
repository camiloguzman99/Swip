using System.Collections.ObjectModel;
using System.ComponentModel;
using Swip.App.Art;
using Swip.Shared;

namespace Swip.App.World;

/// <summary>
/// Un gato = una cuenta de usuario. Reúne la información del menú y el estado de
/// simulación (acción, animación, posición y gravedad) que la ventana actualiza cada tick.
/// </summary>
public sealed class CatAgent : INotifyPropertyChanged
{
    private static readonly Random Rng = new();

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

    private string _stateText = string.Empty;
    public string StateText
    {
        get => _stateText;
        set { _stateText = value; Raise(nameof(StateText)); }
    }

    private bool _appsLoading;
    public bool AppsLoading
    {
        get => _appsLoading;
        set { _appsLoading = value; Raise(nameof(AppsLoading)); }
    }

    public ObservableCollection<AppRow> Apps { get; } = new();

    /// <summary>True mientras su menú está abierto: el gato se pone a "jugar".</summary>
    public bool MenuOpen { get; set; }

    // --- Apariencia ------------------------------------------------------------
    public string Color { get; set; } = "orange";
    public int FatLevel { get; set; }

    // --- Simulación ------------------------------------------------------------
    public double X { get; set; }
    public double Y { get; set; }
    public double Vx { get; set; }
    public double Vy { get; set; }
    public bool FacingRight { get; set; } = true;
    public int FrameIndex { get; private set; }

    /// <summary>True mientras el usuario arrastra el gato (lo lleva el ratón; sin gravedad).</summary>
    public bool Dragging { get; set; }

    /// <summary>Caricia: muestra un corazón y pone al gato a "jugar" un momento.</summary>
    public void Pet() => _petTimer = 1.1;
    public bool IsPetting => _petTimer > 0;

    private double _animTimer;
    private double _wander;
    private double _petTimer;
    private double _walkPhase;

    /// <summary>
    /// Acción actual según el estado de la sesión (o Carry si se está arrastrando):
    /// durmiendo (sin sesión), caminando (sesión activa), jugando (en espera).
    /// </summary>
    public CatAction Action =>
        Dragging ? CatAction.Carry
        : (IsPetting || MenuOpen) ? CatAction.Play
        : IsCurrent ? CatAction.Walk
        : HasSession ? CatAction.Play
        : CatAction.Sleep;

    public void Update(double dt, double viewportWidth, double viewportHeight, double catSize)
    {
        if (_petTimer > 0) _petTimer -= dt;
        CatAction act = Action;

        // Animación de 2 frames, a ritmo distinto por acción.
        double interval = act switch
        {
            CatAction.Walk => 0.26,
            CatAction.Play => 0.5,
            CatAction.Sleep => 0.9,
            CatAction.Carry => 0.22,
            _ => 0.5,
        };
        _animTimer += dt;
        if (_animTimer >= interval)
        {
            _animTimer = 0;
            FrameIndex ^= 1;
        }

        if (Dragging)
            return; // la posición la fija el ratón

        double floor = Math.Max(0, viewportHeight - catSize);

        // Gravedad: si está por encima del suelo, cae.
        if (Y < floor - 0.5)
        {
            Vy += 2200 * dt;
            Y += Vy * dt;
            if (Y >= floor) { Y = floor; Vy = 0; }
            return; // mientras cae no camina
        }
        Y = floor;
        Vy = 0;

        // Solo el gato "caminando" (sesión activa) merodea, despacio y con un leve trote.
        if (act == CatAction.Walk)
        {
            _wander -= dt;
            if (_wander <= 0)
            {
                if (Rng.NextDouble() < 0.25)
                {
                    Vx = 0;
                    _wander = 1.0 + Rng.NextDouble() * 1.8;
                }
                else
                {
                    double speed = 10 + Rng.NextDouble() * 12; // más lento (antes 20-56)
                    Vx = Rng.NextDouble() < 0.5 ? -speed : speed;
                    FacingRight = Vx > 0;
                    _wander = 1.6 + Rng.NextDouble() * 2.8;
                }
            }

            X += Vx * dt;
            double maxX = Math.Max(0, viewportWidth - catSize);
            if (X <= 0) { X = 0; Vx = Math.Abs(Vx); FacingRight = true; }
            else if (X >= maxX) { X = maxX; Vx = -Math.Abs(Vx); FacingRight = false; }

            // Trote: pequeño bote vertical mientras avanza (da sensación de caminar).
            if (Vx != 0)
            {
                _walkPhase += dt * 11;
                Y = floor - Math.Abs(Math.Sin(_walkPhase)) * (catSize * 0.06);
            }
        }
        else
        {
            Vx = 0;
        }
    }

    public static string DescribeState(UserInfo u)
    {
        if (!u.HasSession) return "Sesión cerrada (durmiendo)";
        if (u.IsCurrent) return "Sesión activa (en pantalla)";
        return u.State switch
        {
            SessionConnectionState.Active => "Activa",
            SessionConnectionState.Connected => "Conectada",
            SessionConnectionState.Disconnected => "En espera (en el otro escritorio)",
            SessionConnectionState.Idle => "Inactiva",
            _ => u.State.ToString(),
        };
    }

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
