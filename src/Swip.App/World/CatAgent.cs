using System.Collections.ObjectModel;
using System.ComponentModel;
using Swip.App.Art;
using Swip.Shared;

namespace Swip.App.World;

/// <summary>Estado visible del gato.</summary>
public enum CatState
{
    /// <summary>Sesión activa/conectada: el gato merodea por el viewport.</summary>
    Active,

    /// <summary>Sesión cerrada (desconectada en segundo plano): el gato duerme.</summary>
    Sleeping,
}

/// <summary>
/// Un gato = una sesión. Reúne la información mostrada en el menú (clic derecho) y el estado
/// de movimiento/animación que la ventana actualiza en cada tick del bucle.
/// </summary>
public sealed class CatAgent : INotifyPropertyChanged
{
    private static readonly Random Rng = new();

    // --- Identidad / info de sesión (para el menú) -----------------------------
    public int SessionId { get; init; }
    public string DisplayName { get; init; } = string.Empty;

    private bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set { _isCurrent = value; Raise(nameof(IsCurrent)); Raise(nameof(CanSwitch)); }
    }

    public bool CanSwitch => !IsCurrent;

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

    public ObservableCollection<string> Apps { get; } = new();

    // --- Estado de simulación (lo lee/escribe el bucle de la ventana) ----------
    public CatState State { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double BaseY { get; set; }
    public double Vx { get; set; }
    public bool FacingRight { get; set; } = true;
    public CatFrame Frame { get; private set; } = CatFrame.SitA;

    private double _frameTimer;
    private double _wanderTimer;
    private double _reactionTimer;
    private double _bobPhase;

    /// <summary>Clic izquierdo: el gato reacciona (se pone feliz y da un saltito).</summary>
    public void Interact()
    {
        _reactionTimer = 1.2;
        _bobPhase = 0;
    }

    /// <summary>True mientras el gato muestra la reacción al clic izquierdo.</summary>
    public bool IsReacting => _reactionTimer > 0;

    /// <summary>Desplazamiento vertical del saltito durante la reacción (en píxeles de pantalla).</summary>
    public double HopOffset { get; private set; }

    /// <summary>
    /// Avanza la simulación del gato. <paramref name="dt"/> en segundos; el viewport en DIP;
    /// <paramref name="catSize"/> es el tamaño en pantalla del gato.
    /// </summary>
    public void Update(double dt, double viewportWidth, double viewportHeight, double catSize)
    {
        _bobPhase += dt;

        if (_reactionTimer > 0)
        {
            _reactionTimer -= dt;
            Frame = CatFrame.Happy;
            // Saltito: medio seno a lo largo de la reacción.
            double p = Math.Clamp(1 - _reactionTimer / 1.2, 0, 1);
            HopOffset = -Math.Sin(p * Math.PI) * (catSize * 0.35);
            Y = BaseY + HopOffset;
            return;
        }
        HopOffset = 0;

        if (State == CatState.Sleeping)
        {
            AnimateFrame(dt, 0.7, CatFrame.SleepA, CatFrame.SleepB);
            Y = BaseY;
            return;
        }

        // Activo: merodea horizontalmente con pausas ocasionales.
        AnimateFrame(dt, 0.22, CatFrame.SitA, CatFrame.SitB);

        _wanderTimer -= dt;
        if (_wanderTimer <= 0)
        {
            // 1 de cada 3 veces se queda quieto un momento; si no, elige rumbo y velocidad.
            if (Rng.NextDouble() < 0.33)
            {
                Vx = 0;
                _wanderTimer = 0.8 + Rng.NextDouble() * 1.6;
            }
            else
            {
                double speed = 18 + Rng.NextDouble() * 34; // DIP/seg
                Vx = Rng.NextDouble() < 0.5 ? -speed : speed;
                FacingRight = Vx > 0;
                _wanderTimer = 1.2 + Rng.NextDouble() * 2.5;
            }
        }

        X += Vx * dt;
        double maxX = Math.Max(0, viewportWidth - catSize);
        if (X <= 0) { X = 0; Vx = Math.Abs(Vx); FacingRight = true; }
        else if (X >= maxX) { X = maxX; Vx = -Math.Abs(Vx); FacingRight = false; }

        // Bamboleo vertical suave al moverse.
        double amp = Vx != 0 ? catSize * 0.04 : catSize * 0.015;
        Y = BaseY + Math.Sin(_bobPhase * 6) * amp;
    }

    private void AnimateFrame(double dt, double interval, CatFrame a, CatFrame b)
    {
        _frameTimer += dt;
        if (_frameTimer >= interval)
        {
            _frameTimer = 0;
            Frame = Frame == a ? b : a;
        }
        else if (Frame != a && Frame != b)
        {
            Frame = a;
        }
    }

    public static CatState StateFor(SessionInfo s)
    {
        if (s.IsCurrent) return CatState.Active;
        return s.State switch
        {
            SessionConnectionState.Active => CatState.Active,
            SessionConnectionState.Connected => CatState.Active,
            _ => CatState.Sleeping,
        };
    }

    public static string DescribeState(SessionInfo s)
    {
        if (s.IsCurrent) return "En esta pantalla ahora";
        return s.State switch
        {
            SessionConnectionState.Active => "Activa",
            SessionConnectionState.Connected => "Conectada",
            SessionConnectionState.Disconnected => "Abierta en el otro escritorio (dormida)",
            SessionConnectionState.Idle => "Inactiva (dormida)",
            _ => s.State + " (dormida)",
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
