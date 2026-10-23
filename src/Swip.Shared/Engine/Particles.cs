namespace Swip.Shared.Engine;

/// <summary>Una partícula de efecto (zzz, corazón, polvo...). La app solo la dibuja.</summary>
public sealed class Particle
{
    public EffectKind Kind { get; internal set; }
    public double X { get; internal set; }
    public double Y { get; internal set; }
    public double Vx { get; internal set; }
    public double Vy { get; internal set; }
    public double Age { get; internal set; }
    public double Life { get; internal set; }
    /// <summary>Tamaño base en píxeles.</summary>
    public double Size { get; internal set; }

    /// <summary>Gato al que sigue (la mosca vuela alrededor de su cabeza).</summary>
    public string? FollowCatId { get; internal set; }
    internal double Phase;

    /// <summary>Opacidad 0..1: entra suave y se desvanece al final.</summary>
    public double Alpha
    {
        get
        {
            double t = Life <= 0 ? 1 : Age / Life;
            double fadeIn = Math.Min(1, Age / 0.12);
            return Math.Clamp(fadeIn * (1 - t * t), 0, 1);
        }
    }
}

/// <summary>Sistema de partículas con tope (para que nunca crezca sin límite).</summary>
public sealed class ParticleSystem
{
    public const int MaxParticles = 160;

    private readonly List<Particle> _items = new();
    private readonly Random _rng;

    public ParticleSystem(Random rng) => _rng = rng;

    public IReadOnlyList<Particle> Items => _items;

    public void Emit(EffectKind kind, double x, double y, bool facingRight, int count = 1,
        string? followCatId = null, double life = 0)
    {
        for (int i = 0; i < count; i++)
        {
            if (_items.Count >= MaxParticles) _items.RemoveAt(0); // el más viejo deja sitio
            double dir = facingRight ? 1 : -1;
            var p = new Particle { Kind = kind, X = x, Y = y, Phase = _rng.NextDouble() * Math.Tau };
            switch (kind)
            {
                case EffectKind.Zzz:
                    p.Vx = 7 * dir; p.Vy = -11; p.Life = 2.2; p.Size = 9 + _rng.NextDouble() * 3;
                    break;
                case EffectKind.Heart:
                    p.Vx = (_rng.NextDouble() - 0.5) * 8; p.Vy = -20; p.Life = 1.1; p.Size = 12;
                    break;
                case EffectKind.Dust:
                    p.Vx = (_rng.NextDouble() * 2 - 1) * 26; p.Vy = -6 - _rng.NextDouble() * 8;
                    p.Life = 0.45 + _rng.NextDouble() * 0.2; p.Size = 3 + _rng.NextDouble() * 3;
                    break;
                case EffectKind.Exclaim:
                    p.Vy = -6; p.Life = 0.9; p.Size = 14;
                    break;
                case EffectKind.Sparkle:
                    p.X += (_rng.NextDouble() - 0.5) * 18; p.Y += (_rng.NextDouble() - 0.5) * 10;
                    p.Vy = -8; p.Life = 0.8; p.Size = 9;
                    break;
                case EffectKind.Fly:
                    p.Life = life > 0 ? life : 4; p.Size = 3; p.FollowCatId = followCatId;
                    break;
                case EffectKind.Note:
                    p.Vx = 6 * dir; p.Vy = -14; p.Life = 1.4; p.Size = 11;
                    break;
                case EffectKind.Scratch:
                    p.Y += (_rng.NextDouble() - 0.5) * 8; p.Life = 0.5; p.Size = 8;
                    break;
            }
            _items.Add(p);
        }
    }

    /// <param name="headOf">Posición de la cabeza de un gato (para la mosca), o null si ya no existe.</param>
    public void Step(double dt, Func<string, (double X, double Y)?> headOf)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var p = _items[i];
            p.Age += dt;
            if (p.Age >= p.Life) { _items.RemoveAt(i); continue; }

            switch (p.Kind)
            {
                case EffectKind.Fly when p.FollowCatId is not null:
                    var head = headOf(p.FollowCatId);
                    if (head is null) { _items.RemoveAt(i); continue; }
                    double a = p.Phase + p.Age * 3.1;
                    p.X = head.Value.X + Math.Cos(a) * 16 + Math.Sin(a * 2.3) * 4;
                    p.Y = head.Value.Y - 10 + Math.Sin(a * 1.7) * 9;
                    break;
                case EffectKind.Zzz:
                    p.X += (p.Vx + Math.Sin(p.Phase + p.Age * 3) * 6) * dt;
                    p.Y += p.Vy * dt;
                    break;
                case EffectKind.Dust:
                    p.Vx *= Math.Exp(-4 * dt);
                    p.X += p.Vx * dt;
                    p.Y += p.Vy * dt;
                    p.Size += 6 * dt;
                    break;
                default:
                    p.X += p.Vx * dt;
                    p.Y += p.Vy * dt;
                    break;
            }
        }
    }

    public void Clear() => _items.Clear();
}
