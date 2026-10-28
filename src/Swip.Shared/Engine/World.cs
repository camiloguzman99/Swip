namespace Swip.Shared.Engine;

/// <summary>
/// Medidas del mundo (en píxeles independientes del dispositivo). <c>CellSize</c> son los px lógicos que mide
/// cada casilla del dibujo: el avance por fotograma de los clips y las filas de aplastamiento de la caja están
/// en casillas y se pasan a px con él.
/// </summary>
public sealed record WorldConfig(
    double CatWidth,
    double CatHeight,
    double BoxWidth,
    double BoxHeight,
    double BoxTopInsetClosed,
    double BoxTopInsetOpen,
    double Gravity = 2200,
    double CellSize = 1);

/// <summary>
/// El motor de movimientos de Swip. Todo lo que se mueve vive aquí: física de gatos y caja,
/// cerebro de cada gato (qué comportamiento hace y cuándo cambia), movimientos (caminar, saltar,
/// empujar...), tambaleo de la caja, sombra y partículas. El gato se desplaza por fotogramas: solo
/// avanza cuando cambia el fotograma del clip, lo que ese fotograma tenga asignado (ClipDef.Advance).
/// La app solo traduce la entrada del usuario en llamadas y dibuja <see cref="Cat.Pose"/>,
/// <see cref="Box"/> y <see cref="Particles"/>. No depende de WPF, así que se prueba entero.
/// </summary>
public sealed class SwipWorld
{
    // Las velocidades de desplazamiento salen de los clips (avance por fotograma × fps), no de aquí.
    public const double JumpTime = 0.55;
    public const double HopHeight = 14;

    private WorldConfig _config;
    private readonly ClipLibrary _clips;
    private readonly Random _rng;
    private readonly Dictionary<string, Cat> _cats = new(StringComparer.OrdinalIgnoreCase);
    private readonly ParticleSystem _particles;

    public SwipWorld(WorldConfig config, ClipLibrary? clips = null, int? seed = null)
    {
        _config = config;
        _clips = clips ?? ClipLibrary.Default;
        _rng = seed is null ? new Random() : new Random(seed.Value);
        _particles = new ParticleSystem(_rng);
        Box = new BoxBody(config.BoxWidth, config.BoxHeight, config.BoxTopInsetClosed, config.BoxTopInsetOpen, config.CellSize);
    }

    public double Width { get; private set; }
    public double Height { get; private set; }
    public BoxBody Box { get; }
    public IReadOnlyCollection<Cat> Cats => _cats.Values;
    public IReadOnlyList<Particle> Particles => _particles.Items;
    public ClipLibrary Clips => _clips;

    /// <summary>Posición del cursor en coordenadas del mundo, o null si está fuera.</summary>
    public (double X, double Y)? Cursor { get; set; }

    /// <summary>
    /// Altura de la cabeza dentro del dibujo (fracción 0..1) para un clip. La app la saca de los
    /// metadatos del atlas; sin ella se usa una aproximación por clip.
    /// </summary>
    public Func<string, double>? HeadFraction { get; set; }

    /// <summary>Se dispara al empezar cada comportamiento (registro y pruebas).</summary>
    public event Action<Cat, Behavior>? BehaviorStarted;

    public double Floor => Height;
    private double CatFloorY => Height - _config.CatHeight;
    private double BoxFloorY => Height - Box.Height;

    public Cat? Find(string id) => _cats.TryGetValue(id, out var c) ? c : null;

    // ======================================================================================
    // API para la app
    // ======================================================================================

    public void Resize(double width, double height)
    {
        Width = width;
        Height = height;
        Box.X = Math.Clamp(Box.X, 0, Math.Max(0, Width - Box.Width));
        Box.Y = Math.Min(Box.Y, BoxFloorY);
        foreach (var cat in _cats.Values)
        {
            cat.X = Math.Clamp(cat.X, 0, MaxCatX);
            if (cat.Place == CatPlace.Ground) cat.Y = CatFloorY;
        }
    }

    /// <summary>
    /// Cambia el tamaño de las piezas (px lógicos por casilla) con el mundo en marcha: escala los gatos y la caja,
    /// los vuelve a apoyar en el suelo y ajusta el avance de los clips y el aplastamiento. Los gatos que van en la
    /// caja se recolocan solos en el siguiente paso.
    /// </summary>
    public void SetCellSize(double cellSize)
    {
        if (cellSize <= 0 || Math.Abs(cellSize - _config.CellSize) < 1e-9) return;
        double f = cellSize / _config.CellSize;
        bool boxOnFloor = Box.Y >= BoxFloorY - 0.5;
        _config = _config with
        {
            CatWidth = _config.CatWidth * f, CatHeight = _config.CatHeight * f,
            BoxWidth = _config.BoxWidth * f, BoxHeight = _config.BoxHeight * f, CellSize = cellSize,
        };
        Box.Width *= f;
        Box.Height *= f;
        Box._cellSize = cellSize;
        foreach (var cat in _cats.Values)
        {
            cat.Width *= f;
            cat.Height *= f;
        }
        Resize(Width, Height);
        if (boxOnFloor) Box.Y = BoxFloorY;
    }

    public Cat AddCat(string id, double x, CatMode mode, bool facingRight = true)
    {
        var cat = new Cat(id, _config.CatWidth, _config.CatHeight)
        {
            X = Math.Clamp(x, 0, MaxCatX),
            Y = CatFloorY,
            FacingRight = facingRight,
            Mode = mode,
            ModeInitialized = true,
        };
        _cats[id] = cat;
        StartRandom(cat);
        UpdatePose(cat, 0);
        return cat;
    }

    public void RemoveCat(string id)
    {
        if (Box.OccupantId == id) Box.OccupantId = null;
        _cats.Remove(id);
    }

    /// <summary>Cambia el temperamento (estado de la sesión). Lanza la transición que toque.</summary>
    public void SetMode(string id, CatMode mode)
    {
        if (Find(id) is not { } cat || cat.Mode == mode) return;
        CatMode old = cat.Mode;
        cat.Mode = mode;
        cat.Pending = mode == CatMode.Sleep ? Trigger.ToSleep
            : old == CatMode.Sleep ? Trigger.WakeUp
            : mode == CatMode.Play ? Trigger.ToBackground
            : Trigger.ToActive;
    }

    public void Pet(string id)
    {
        if (Find(id) is { Place: not CatPlace.Carried } cat) cat.Pending = Trigger.Pet;
    }

    /// <summary>El menú del gato se abrió/cerró: quiere atención (dormido, no se entera).</summary>
    public void SetAttention(string id, bool on)
    {
        if (Find(id) is { } cat) cat.Attention = on;
    }

    public void BeginCarry(string id)
    {
        if (Find(id) is not { } cat) return;
        if (Box.OccupantId == id) Box.OccupantId = null;
        cat.Place = CatPlace.Carried;
        cat.Vy = 0;
        Start(cat, Behaviors.Get("carry"));
    }

    public void CarryTo(string id, double x, double y)
    {
        if (Find(id) is not { Place: CatPlace.Carried } cat) return;
        cat.X = Math.Clamp(x, 0, MaxCatX);
        cat.Y = Math.Clamp(y, 0, CatFloorY);
    }

    /// <summary>Suelta al gato: si cae sobre la caja libre se queda en ella; si no, cae por gravedad.</summary>
    /// <returns>True si quedó en la caja.</returns>
    public bool Drop(string id)
    {
        if (Find(id) is not { Place: CatPlace.Carried } cat) return false;
        if (Box.OccupantId is null && OverlapsBox(cat))
        {
            AttachToBox(cat, Box.Open ? CatPlace.InBox : CatPlace.OnBox);
            cat.Pending = Trigger.Land;
            return true;
        }
        cat.Place = CatPlace.Air;
        cat.Vy = 0;
        StartRandom(cat); // la pose de caída la decide el planificador al aterrizar
        return false;
    }

    /// <summary>
    /// Coloca a un gato en una X (p. ej. la que dejó la otra sesión). Solo si está en el suelo:
    /// a uno que llevas, que está en la caja o en mitad de un salto no se le mueve.
    /// </summary>
    public void SetCatX(string id, double x)
    {
        if (Find(id) is { Place: CatPlace.Ground } cat && !IsCommitted(cat))
            cat.X = Math.Clamp(x, 0, MaxCatX);
    }

    /// <summary>Gordo o delgado: cuánto aplasta la caja al subirse a ella.</summary>
    public void SetCatHeavy(string id, bool heavy)
    {
        if (Find(id) is { } cat) cat.Heavy = heavy;
    }

    public void SetBoxOpen(bool open)
    {
        Box.Open = open;
        if (Box.OccupantId is { } id && Find(id) is { } cat && cat.Place is CatPlace.OnBox or CatPlace.InBox)
            cat.Place = open ? CatPlace.InBox : CatPlace.OnBox;
    }

    public void PlaceBox(double x, double? y = null)
    {
        Box.X = Math.Clamp(x, 0, Math.Max(0, Width - Box.Width));
        Box.Y = y is double yy ? Math.Clamp(yy, 0, BoxFloorY) : BoxFloorY;
        Box.Vx = Box.Vy = 0;
    }

    public void BeginBoxDrag() { Box.Dragging = true; Box.Vx = Box.Vy = 0; }

    public void BoxDragTo(double x, double y)
    {
        Box.X = Math.Clamp(x, 0, Math.Max(0, Width - Box.Width));
        Box.Y = Math.Clamp(y, 0, BoxFloorY);
    }

    public void EndBoxDrag() => Box.Dragging = false;

    // ======================================================================================
    // Paso de simulación
    // ======================================================================================

    public void Step(double dt)
    {
        if (dt <= 0) return;
        if (dt > 0.1) dt = 0.1; // tras una pausa, no dar saltos enormes

        StepBox(dt);
        foreach (var cat in _cats.Values)
        {
            StepBrain(cat, dt);
            StepMotion(cat, dt);
            StepPhysics(cat, dt);
            EmitEffects(cat);
            UpdatePose(cat, dt);
        }
        _particles.Step(dt, HeadOf);
    }

    private void StepBox(double dt)
    {
        var b = Box;
        if (!b.Dragging)
        {
            // Gravedad.
            if (b.Y < BoxFloorY - 0.5)
            {
                b.Vy += _config.Gravity * dt;
                b.Y += b.Vy * dt;
                if (b.Y >= BoxFloorY)
                {
                    b.Y = BoxFloorY;
                    b.Vy = 0;
                    b.Shake(60);
                    EmitDustAt(b.CenterX, b.Bottom, 3);
                }
            }
            else
            {
                b.Y = BoxFloorY;
                b.Vy = 0;
            }

            // Deslizamiento con fricción (empujones).
            if (Math.Abs(b.Vx) > 0.01)
            {
                b.X += b.Vx * dt;
                b.Vx *= Math.Exp(-5 * dt);
                if (Math.Abs(b.Vx) < 1) b.Vx = 0;
                double maxX = Math.Max(0, Width - b.Width);
                if (b.X <= 0) { b.X = 0; b.Vx = 0; }
                else if (b.X >= maxX) { b.X = maxX; b.Vx = 0; }
            }
        }

        // Tambaleo: muelle amortiguado hacia 0 grados (leve: como mucho MaxWobble).
        b.WobbleVelocity += (-120 * b.Wobble - 9 * b.WobbleVelocity) * dt;
        b.Wobble += b.WobbleVelocity * dt;
        if (Math.Abs(b.Wobble) < 0.02 && Math.Abs(b.WobbleVelocity) < 0.2) { b.Wobble = 0; b.WobbleVelocity = 0; }
        b.Wobble = Math.Clamp(b.Wobble, -BoxBody.MaxWobble, BoxBody.MaxWobble);

        // Solapas: un fotograma cada LidFrameTime hacia abierta o cerrada.
        b.Lid = StepToward(b.Lid, b.Open ? BoxBody.LidFrames - 1 : 0, ref b.LidClock, LidFrameTime, dt);

        // Aplastamiento: según el peso del gato que está encima (con la caja cerrada); al aterrizar,
        // una fila más un instante (rebote). Fila a fila, como fotogramas.
        int squash = 0;
        if (!b.Open && b.OccupantId is { } id && Find(id) is { Place: CatPlace.OnBox } rider)
        {
            squash = rider.Heavy ? BoxBody.HeavySquash : BoxBody.LightSquash;
            if (b.SquashBounce > 0) squash = Math.Min(BoxBody.MaxSquash, squash + 1);
        }
        if (b.SquashBounce > 0) b.SquashBounce -= dt;
        b.Squash = StepToward(b.Squash, b.Lid == 0 ? squash : 0, ref b.SquashClock, SquashFrameTime, dt);
    }

    /// <summary>Segundos por fotograma de las solapas y del aplastamiento.</summary>
    private const double LidFrameTime = 1 / 18.0, SquashFrameTime = 1 / 20.0;

    /// <summary>Avanza un valor entero de uno en uno hacia el objetivo, un paso cada <paramref name="frameTime"/>.</summary>
    private static int StepToward(int value, int target, ref double clock, double frameTime, double dt)
    {
        if (value == target) { clock = 0; return value; }
        clock += dt;
        while (clock >= frameTime && value != target)
        {
            clock -= frameTime;
            value += Math.Sign(target - value);
        }
        return value;
    }

    // ======================================================================================
    // Cerebro: qué hace cada gato
    // ======================================================================================

    private void StepBrain(Cat cat, double dt)
    {
        cat.BehaviorTime += dt;
        cat.PhaseTime += dt;
        if (cat.Place == CatPlace.Carried) return; // lo lleva el ratón

        bool committed = IsCommitted(cat);

        // 1) Disparadores (transiciones, caricias, aterrizajes, invitaciones). Esperan a terminar un salto.
        if (cat.Pending is { } trigger && !committed && cat.Place != CatPlace.Air)
        {
            cat.Pending = null;
            if (Behaviors.ForTrigger(trigger, cat.Mode) is { } b)
            {
                Start(cat, b);
                return;
            }
        }

        // 2) Atención (menú abierto): mientras dure, te mira. Dormido no se entera.
        if (cat.Attention && cat.Mode != CatMode.Sleep && !committed && cat.Place != CatPlace.Air
            && cat.Current.Trigger is Trigger.Random)
        {
            Start(cat, Behaviors.Get("attention"));
            return;
        }
        if (cat.Current.Trigger == Trigger.Attention && (!cat.Attention || cat.Mode == CatMode.Sleep))
        {
            StartRandom(cat);
            return;
        }

        // 3) Fin del comportamiento, o ya no se cumplen sus condiciones (p. ej. abriste la caja).
        bool expired = cat.Finished || cat.BehaviorTime >= cat.BehaviorDuration;
        bool invalid = cat.Current.Trigger == Trigger.Random && !committed && cat.Place != CatPlace.Air
                       && !Allowed(cat, cat.Current);
        bool wrongMode = !cat.Current.Modes.Contains(cat.Mode) && !committed;
        if (expired || invalid || wrongMode)
            StartRandom(cat);
    }

    /// <summary>True en mitad de un salto: no se interrumpe (se terminaría en el aire).</summary>
    private static bool IsCommitted(Cat cat) =>
        cat.Current.Motion is Motion.JumpOntoBox or Motion.JumpIntoBox or Motion.JumpOffBox && cat.Phase == 1;

    private void StartRandom(Cat cat)
    {
        var candidates = Behaviors.All
            .Where(b => b.Trigger == Trigger.Random && b.Modes.Contains(cat.Mode) && Allowed(cat, b))
            .ToList();

        if (candidates.Count == 0)
        {
            // Siempre hay algo que hacer: la acción base del temperamento (o sentarse en la caja).
            candidates.Add(Fallback(cat));
        }

        double total = 0;
        var weights = new double[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
        {
            double w = candidates[i].Weight;
            if (candidates.Count > 1 && ReferenceEquals(candidates[i], cat.Last)) w *= 0.15; // no repetir seguido
            weights[i] = w;
            total += w;
        }

        double roll = _rng.NextDouble() * total;
        int pick = candidates.Count - 1;
        for (int i = 0; i < candidates.Count; i++)
        {
            roll -= weights[i];
            if (roll <= 0) { pick = i; break; }
        }
        Start(cat, candidates[pick]);
    }

    private Behavior Fallback(Cat cat) => cat.Mode switch
    {
        CatMode.Sleep => Behaviors.Get(cat.Place == CatPlace.InBox ? "sleep_in_box" : "sleep_curl"),
        CatMode.Play => Behaviors.Get(cat.Place == CatPlace.InBox ? "box_peek" : "play_yarn"),
        _ => Behaviors.Get(cat.Place switch
        {
            CatPlace.InBox => "box_peek",
            CatPlace.OnBox => "box_sit_watch",
            _ => "walk",
        }),
    };

    /// <summary>
    /// Cierre del comportamiento que termina (o se interrumpe): lo que debe pasar justo al acabar.
    /// Va aquí y no en la pose porque el cerebro cambia de comportamiento antes de calcularla.
    /// </summary>
    private void EndOf(Cat cat)
    {
        var b = cat.Current;
        bool nearEnd = cat.BehaviorTime >= cat.BehaviorDuration * 0.5;
        switch (b.Motion)
        {
            case Motion.TurnAround when nearEnd:
                cat.FacingRight = !cat.FacingRight;
                break;
            case Motion.Hop when cat.BehaviorTime >= cat.BehaviorDuration * 0.9:
                EmitDustAt(cat.CenterX, cat.Bottom, 3);
                break;
            case Motion.PushBox:
                // Al soltarla, la caja sigue un poco por inercia (la frena la fricción).
                break;
        }
    }

    private void Start(Cat cat, Behavior b)
    {
        EndOf(cat);
        cat.Last = cat.Current;
        cat.Current = b;
        cat.BehaviorTime = 0;
        cat.PhaseTime = 0;
        cat.Phase = 0;
        cat.Finished = false;
        cat.ClipOverride = null;
        cat.PartnerId = null;
        cat.Speed = 0;
        cat.BehaviorDuration = b.MaxTime > 0
            ? b.MinTime + _rng.NextDouble() * (b.MaxTime - b.MinTime)
            : ClipLength(b.Clip);
        cat.NextEmit = b.EffectList.Select(e => e.Interval > 0 ? e.Interval * 0.3 : double.MaxValue).ToArray();
        cat.Emitted = new bool[b.EffectList.Count];
        BeginMotion(cat, b);
        BehaviorStarted?.Invoke(cat, b);
    }

    private double ClipLength(string clip) => _clips.Contains(clip) ? _clips[clip].Length : 1;

    // ---------------- condiciones ----------------

    public bool Allowed(Cat cat, Behavior b)
    {
        var state = Conditions(cat);
        return (state & b.Needs) == b.Needs && (state & b.Forbids) == 0;
    }

    public Cond Conditions(Cat cat)
    {
        Cond c = Cond.None;
        if (cat.Place == CatPlace.Ground) c |= Cond.OnGround;
        if (cat.Place == CatPlace.OnBox) c |= Cond.OnBox;
        if (cat.Place == CatPlace.InBox) c |= Cond.InBox;
        if (Box.Open) c |= Cond.BoxOpen; else c |= Cond.BoxClosed;
        if (Box.OccupantId is null) c |= Cond.BoxFree;
        bool boxGrounded = !Box.Dragging && Box.Y >= BoxFloorY - 0.5 && Math.Abs(Box.Vx) < 1;
        if (boxGrounded) c |= Cond.BoxGrounded;
        if (cat.Place == CatPlace.Ground && boxGrounded
            && Math.Abs(cat.CenterX - Box.CenterX) < Box.Width / 2 + cat.Width / 2 + 10)
            c |= Cond.NearBox;

        if (Cursor is { } cur)
        {
            double dx = Math.Abs(cur.X - cat.CenterX);
            double dy = cur.Y - cat.Y;
            if (dx < 140 && dy > -200 && dy < cat.Height + 20) c |= Cond.CursorNear;
            if (dx < 40 && dy > -30 && dy < cat.Height + 10) c |= Cond.CursorVeryNear;
        }

        foreach (var other in _cats.Values)
        {
            if (ReferenceEquals(other, cat) || other.Place != CatPlace.Ground) continue;
            if (Math.Abs(other.CenterX - cat.CenterX) > 90) continue;
            c |= Cond.CatNear;
            if (other.Mode == CatMode.Sleep) c |= Cond.CatNearSleeping;
            if (other.Mode == CatMode.Play) c |= Cond.CatNearPlaying;
        }
        return c;
    }

    // ======================================================================================
    // Movimientos
    // ======================================================================================

    private double MaxCatX => Math.Max(0, Width - _config.CatWidth);

    private void BeginMotion(Cat cat, Behavior b)
    {
        switch (b.Motion)
        {
            case Motion.Walk or Motion.Trot or Motion.Run:
                if (_rng.NextDouble() < 0.3) cat.FacingRight = !cat.FacingRight;
                cat.Direction = cat.FacingRight ? 1 : -1;
                break;

            case Motion.ApproachCat or Motion.FaceCat:
                cat.PartnerId = NearestCat(cat, b.Motion == Motion.FaceCat ? CatMode.Sleep : null)?.Id;
                break;

            case Motion.PushBox:
                // Empuja desde el lado en el que está, hacia el otro.
                cat.Direction = cat.CenterX < Box.CenterX ? 1 : -1;
                break;

            case Motion.JumpOffBox:
                StartJumpOff(cat);
                break;

            case Motion.CircleBox:
                cat.TargetX = cat.CenterX < Box.CenterX
                    ? Box.Right + 10
                    : Box.X - cat.Width - 10;
                cat.TargetX = Math.Clamp(cat.TargetX, 0, MaxCatX);
                break;
        }

        if (b.Id == "cats_invite" && NearestCat(cat, CatMode.Play) is { } partner)
        {
            partner.Pending = Trigger.Invited;
            cat.FacingRight = partner.CenterX > cat.CenterX;
        }
    }

    private void StepMotion(Cat cat, double dt)
    {
        cat.Speed = 0;
        var b = cat.Current;
        switch (b.Motion)
        {
            case Motion.Walk or Motion.Trot or Motion.Run:
                Locomote(cat);
                break;
            case Motion.Spin:
                cat.FacingRight = ((int)(cat.BehaviorTime / 0.14)) % 2 == 0;
                break;
            case Motion.FaceCursor:
                if (Cursor is { } c) cat.FacingRight = c.X > cat.CenterX;
                break;
            case Motion.FollowCursor:
                FollowCursor(cat, dt);
                break;
            case Motion.ApproachCat:
                ApproachCat(cat, dt);
                break;
            case Motion.FaceCat:
                if (Find(cat.PartnerId ?? "") is { } p) cat.FacingRight = p.CenterX > cat.CenterX;
                break;
            case Motion.ScratchBox:
                cat.FacingRight = Box.CenterX > cat.CenterX;
                if ((int)(cat.BehaviorTime / 0.35) != (int)((cat.BehaviorTime - dt) / 0.35)) Box.Shake(25);
                break;
            case Motion.RubBox:
                RubBox(cat, dt);
                break;
            case Motion.PushBox:
                PushBox(cat);
                break;
            case Motion.JumpOntoBox:
                JumpToBox(cat, dt, CatPlace.OnBox);
                break;
            case Motion.JumpIntoBox:
                JumpToBox(cat, dt, CatPlace.InBox);
                break;
            case Motion.JumpOffBox:
                JumpArc(cat, dt);
                break;
            case Motion.CircleBox:
                cat.ClipOverride = null;
                if (MoveToward(cat, cat.TargetX)) cat.Finished = true;
                break;
            case Motion.Ambush:
                double pop = ClipLength(b.Clip);
                if (cat.Phase == 0 && cat.BehaviorTime >= cat.BehaviorDuration - pop)
                {
                    cat.Phase = 1;
                    Emit(cat, EffectKind.Exclaim, Anchor.Head);
                    Box.Shake(70);
                }
                cat.ClipOverride = cat.Phase == 0 ? "box_peek" : null;
                break;
        }
    }

    /// <summary>
    /// Casillas que debe avanzar el gato en este paso: las del fotograma (o fotogramas) que acaban de
    /// mostrarse del clip actual. Entre dos cambios de fotograma devuelve 0, así que el gato no se
    /// desliza: el desplazamiento va siempre unido al dibujo de las patas.
    /// </summary>
    private double FrameStep(Cat cat)
    {
        string clipId = CurrentClip(cat);
        if (!_clips.Contains(clipId)) return 0;
        var clip = _clips[clipId];
        int tick = clip.TickAt(cat.ClipOverride is null ? cat.BehaviorTime : cat.PhaseTime);
        cat.Speed = clip.Speed * _config.CellSize;
        // Clip nuevo, clip reiniciado o un salto grande de tiempo (pausa): se toma como punto de
        // partida sin avanzar, para no dar un tirón.
        if (cat.StepClip != clipId || tick < cat.StepTick || tick - cat.StepTick > 2)
        {
            cat.StepClip = clipId;
            cat.StepTick = tick;
            return 0;
        }
        int cells = 0;
        for (int t = cat.StepTick + 1; t <= tick; t++)
            cells += clip.AdvanceOf(clip.FrameOfTick(t));
        cat.StepTick = tick;
        return cells * _config.CellSize;
    }

    private void Locomote(Cat cat)
    {
        if (cat.Place != CatPlace.Ground) return;
        cat.X += cat.Direction * FrameStep(cat);
        if (cat.X <= 0) { cat.X = 0; cat.Direction = 1; }
        else if (cat.X >= MaxCatX) { cat.X = MaxCatX; cat.Direction = -1; }
        cat.FacingRight = cat.Direction > 0;
    }

    /// <summary>Camina hacia una X (al ritmo de los fotogramas del clip); devuelve true al llegar.</summary>
    private bool MoveToward(Cat cat, double targetX)
    {
        if (cat.Place != CatPlace.Ground) return true;
        double dx = targetX - cat.X;
        if (Math.Abs(dx) <= 1.5) return true;
        double step = Math.Min(Math.Abs(dx), FrameStep(cat));
        cat.X = Math.Clamp(cat.X + Math.Sign(dx) * step, 0, MaxCatX);
        cat.FacingRight = dx > 0;
        return Math.Abs(targetX - cat.X) <= 1.5;
    }

    private void FollowCursor(Cat cat, double dt)
    {
        if (Cursor is not { } c) { cat.Finished = true; return; }
        double target = Math.Clamp(c.X - cat.Width / 2, 0, MaxCatX);
        bool arrived = Math.Abs(c.X - cat.CenterX) < 24 || MoveToward(cat, target);
        if (arrived) cat.FacingRight = c.X > cat.CenterX;
        cat.ClipOverride = arrived ? "sit_look" : null;
    }

    private void ApproachCat(Cat cat, double dt)
    {
        if (Find(cat.PartnerId ?? "") is not { } partner) { cat.Finished = true; return; }
        double gap = cat.Width + 8;
        double target = partner.CenterX > cat.CenterX ? partner.X - gap : partner.X + gap;
        bool arrived = Math.Abs(partner.CenterX - cat.CenterX) <= gap + 2
                       || MoveToward(cat, Math.Clamp(target, 0, MaxCatX));
        cat.ClipOverride = arrived ? null : "walk";
        if (arrived) cat.FacingRight = partner.CenterX > cat.CenterX;
    }

    private double SideStandX(Cat cat, bool leftSide) =>
        leftSide ? Box.X - cat.Width + 6 : Box.Right - 6;

    private void RubBox(Cat cat, double dt)
    {
        bool left = cat.CenterX < Box.CenterX;
        cat.FacingRight = left;
        if (cat.Mode != CatMode.Walk) return; // jugando: se frota solo si ya está al lado
        bool arrived = MoveToward(cat, Math.Clamp(SideStandX(cat, left), 0, MaxCatX));
        cat.ClipOverride = arrived ? null : "walk";
        cat.FacingRight = left;
    }

    private void PushBox(Cat cat)
    {
        bool fromLeft = cat.Direction > 0;
        double contactX = SideStandX(cat, fromLeft);
        if (cat.Phase == 0)
        {
            cat.ClipOverride = "trot";
            if (MoveToward(cat, Math.Clamp(contactX, 0, MaxCatX)))
            {
                cat.Phase = 1;
                cat.PhaseTime = 0;
            }
            if (cat.BehaviorTime > 5) cat.Finished = true; // no llega (caja contra la pared)
            return;
        }

        // Empujando: en cada fotograma del clip la caja avanza lo que ese fotograma tenga asignado y
        // el gato la sigue pegado a ella (los dos se mueven a la vez, al ritmo de las patas).
        cat.ClipOverride = null;
        cat.FacingRight = fromLeft;
        double maxBoxX = Math.Max(0, Width - Box.Width);
        double step = FrameStep(cat);
        if (step > 0)
        {
            Box.Vx = 0;
            Box.X = Math.Clamp(Box.X + cat.Direction * step, 0, maxBoxX);
            EmitDustAt(fromLeft ? Box.Right : Box.X, Box.Bottom, 1);
        }
        cat.X = Math.Clamp(SideStandX(cat, fromLeft), 0, MaxCatX);

        bool atWall = fromLeft ? Box.X >= maxBoxX - 0.5 : Box.X <= 0.5;
        if (atWall || cat.PhaseTime > 2.5) cat.Finished = true;
    }

    private (double X, double Y) BoxAnchor(Cat cat, CatPlace place)
    {
        double x = Math.Clamp(Box.CenterX - cat.Width / 2, 0, MaxCatX);
        double visibleTop = Box.VisibleTop;
        double visibleH = Box.Bottom - visibleTop;
        double y = place == CatPlace.InBox
            ? visibleTop + visibleH * 0.5 - cat.Height  // dentro: base al 50% del alto visible
            : visibleTop - cat.Height;                  // encima de la tapa
        return (x, y);
    }

    private void JumpToBox(Cat cat, double dt, CatPlace target)
    {
        if (cat.Phase == 0)
        {
            // Acercarse a un lado de la caja, a una distancia de salto.
            if (Box.OccupantId is not null || Box.Dragging) { cat.Finished = true; return; }
            bool left = cat.CenterX < Box.CenterX;
            double standX = left ? Box.X - cat.Width - 4 : Box.Right + 4;
            standX = Math.Clamp(standX, 0, MaxCatX);
            cat.ClipOverride = "trot";
            bool arrived = MoveToward(cat, standX) || cat.BehaviorTime > 6;
            if (!arrived) return;

            cat.FacingRight = left;
            cat.Phase = 1;
            cat.PhaseTime = 0;
            cat.StartX = cat.X;
            cat.StartY = cat.Y;
            cat.JumpTargetPlace = target;
            cat.Place = CatPlace.Air;
            cat.ClipOverride = null;
            EmitDustAt(cat.CenterX, cat.Bottom, 3);
            return;
        }
        JumpArc(cat, dt);
    }

    private void StartJumpOff(Cat cat)
    {
        if (Box.OccupantId == cat.Id) Box.OccupantId = null;
        int dir = cat.FacingRight ? 1 : -1;
        double tx = dir > 0 ? Box.Right + 4 : Box.X - cat.Width - 4;
        // Si ese lado no cabe (caja contra la pared), por el otro.
        if (tx < 0 || tx > MaxCatX)
        {
            dir = -dir;
            tx = dir > 0 ? Box.Right + 4 : Box.X - cat.Width - 4;
        }
        cat.FacingRight = dir > 0;
        cat.Phase = 1;
        cat.PhaseTime = 0;
        cat.StartX = cat.X;
        cat.StartY = cat.Y;
        cat.TargetX = Math.Clamp(tx, 0, MaxCatX);
        cat.TargetY = CatFloorY;
        cat.JumpTargetPlace = CatPlace.Ground;
        cat.Place = CatPlace.Air;
    }

    /// <summary>Vuelo parabólico del salto (sin gravedad: la trayectoria está calculada).</summary>
    private void JumpArc(Cat cat, double dt)
    {
        if (cat.Phase != 1) return;
        if (cat.JumpTargetPlace != CatPlace.Ground)
            (cat.TargetX, cat.TargetY) = BoxAnchor(cat, cat.JumpTargetPlace); // la caja podría moverse

        double u = Math.Min(1, cat.PhaseTime / JumpTime);
        double peak = 26 + Math.Max(0, cat.StartY - cat.TargetY) * 0.4;
        cat.X = cat.StartX + (cat.TargetX - cat.StartX) * u;
        cat.Y = cat.StartY + (cat.TargetY - cat.StartY) * u - peak * 4 * u * (1 - u);
        if (u < 1) return;

        // Aterrizaje.
        cat.Phase = 2;
        if (cat.JumpTargetPlace == CatPlace.Ground)
        {
            cat.Place = CatPlace.Ground;
            cat.Y = CatFloorY;
            EmitDustAt(cat.CenterX, cat.Bottom, 4);
        }
        else if (Box.OccupantId is null)
        {
            AttachToBox(cat, cat.JumpTargetPlace);
            Box.Shake(cat.JumpTargetPlace == CatPlace.InBox ? 90 : 60);
        }
        else
        {
            // Otro gato se le adelantó: cae al suelo.
            cat.Place = CatPlace.Air;
            cat.Vy = 0;
        }
        cat.Finished = true;
    }

    private void AttachToBox(Cat cat, CatPlace place)
    {
        Box.OccupantId = cat.Id;
        if (place == CatPlace.OnBox) Box.SquashBounce = 0.12; // al caer encima, se hunde una fila más un instante
        cat.Place = place;
        cat.Vy = 0;
        (cat.X, cat.Y) = BoxAnchor(cat, place);
    }

    // ======================================================================================
    // Física
    // ======================================================================================

    private void StepPhysics(Cat cat, double dt)
    {
        switch (cat.Place)
        {
            case CatPlace.Ground:
                cat.Y = CatFloorY;
                cat.X = Math.Clamp(cat.X, 0, MaxCatX);
                break;

            case CatPlace.OnBox or CatPlace.InBox:
                if (Box.OccupantId != cat.Id) { cat.Place = CatPlace.Air; break; }
                (cat.X, cat.Y) = BoxAnchor(cat, cat.Place); // va con la caja si la mueves o la empujan
                break;

            case CatPlace.Air when !IsCommitted(cat):
                // Caída libre (lo soltaste).
                cat.Vy += _config.Gravity * dt;
                cat.Y += cat.Vy * dt;
                if (cat.Y >= CatFloorY)
                {
                    cat.Y = CatFloorY;
                    cat.Vy = 0;
                    cat.Place = CatPlace.Ground;
                    cat.Pending = Trigger.Land;
                }
                break;
        }
    }

    // ======================================================================================
    // Efectos
    // ======================================================================================

    private void EmitEffects(Cat cat)
    {
        var effects = cat.Current.EffectList;
        for (int i = 0; i < effects.Count && i < cat.NextEmit.Length; i++)
        {
            var e = effects[i];
            if (e.Interval > 0)
            {
                if (cat.BehaviorTime >= cat.NextEmit[i])
                {
                    cat.NextEmit[i] += e.Interval;
                    if (e.Kind == EffectKind.Dust && cat.Speed <= 0) continue; // sin moverse no levanta polvo
                    Emit(cat, e.Kind, e.Anchor, e.Count);
                }
            }
            else if (!cat.Emitted[i] && cat.BehaviorTime >= e.At)
            {
                cat.Emitted[i] = true;
                Emit(cat, e.Kind, e.Anchor, e.Count);
            }
        }
    }

    private void Emit(Cat cat, EffectKind kind, Anchor anchor, int count = 1)
    {
        var (x, y) = AnchorPoint(cat, anchor);
        double life = kind == EffectKind.Fly ? Math.Max(1, cat.BehaviorDuration - cat.BehaviorTime) : 0;
        _particles.Emit(kind, x, y, cat.FacingRight, count, kind == EffectKind.Fly ? cat.Id : null, life);
    }

    private void EmitDustAt(double x, double y, int count) => _particles.Emit(EffectKind.Dust, x, y, true, count);

    private (double X, double Y) AnchorPoint(Cat cat, Anchor anchor)
    {
        double dir = cat.FacingRight ? 1 : -1;
        double headY = cat.Y + cat.Height * HeadFractionOf(CurrentClip(cat)) + cat.Pose.OffsetY;
        return anchor switch
        {
            Anchor.Head => (cat.CenterX + dir * cat.Width * 0.15, headY),
            Anchor.Feet => (cat.CenterX, cat.Bottom),
            Anchor.Front => (cat.CenterX + dir * cat.Width * 0.55, headY + cat.Height * 0.15),
            Anchor.BoxSide => (cat.CenterX < Box.CenterX ? Box.X : Box.Right, Box.Y + Box.Height * 0.55),
            _ => (cat.CenterX, cat.Y),
        };
    }

    private (double X, double Y)? HeadOf(string id) =>
        Find(id) is { } cat ? AnchorPoint(cat, Anchor.Head) : null;

    private double HeadFractionOf(string clip)
    {
        if (HeadFraction is { } f) return f(clip);
        // Aproximación sin metadatos: el gato dormido es más bajo.
        return clip.StartsWith("sleep", StringComparison.Ordinal) ? 0.6 : 0.3;
    }

    private Cat? NearestCat(Cat cat, CatMode? mode)
    {
        Cat? best = null;
        double bestDist = double.MaxValue;
        foreach (var other in _cats.Values)
        {
            if (ReferenceEquals(other, cat) || other.Place != CatPlace.Ground) continue;
            if (mode is { } m && other.Mode != m) continue;
            double d = Math.Abs(other.CenterX - cat.CenterX);
            if (d < bestDist) { bestDist = d; best = other; }
        }
        return best;
    }

    // ======================================================================================
    // Pose: lo que se dibuja
    // ======================================================================================

    private string CurrentClip(Cat cat) => cat.ClipOverride ?? cat.Current.Clip;

    private void UpdatePose(Cat cat, double dt)
    {
        // Cayendo tras soltarlo: cuelga como cuando lo llevas.
        bool falling = cat.Place == CatPlace.Air && !IsCommitted(cat);
        string clipId = falling ? "carry" : CurrentClip(cat);
        var clip = _clips.Contains(clipId) ? _clips[clipId] : null;
        var b = cat.Current;
        double t = cat.BehaviorTime;

        int frame;
        if (b.Motion is Motion.JumpOntoBox or Motion.JumpIntoBox or Motion.JumpOffBox && cat.Phase >= 1 && clip is not null)
        {
            // El salto sigue la trayectoria: agacharse, volar, aterrizar.
            double u = Math.Clamp(cat.PhaseTime / JumpTime, 0, 1);
            frame = Math.Min(clip.FrameCount - 1, (int)(u * clip.FrameCount));
        }
        else
        {
            frame = clip?.FrameAt(cat.ClipOverride is null ? t : cat.PhaseTime) ?? 0;
        }

        // El dibujo no se deforma: solo desplazamientos (saltos en el sitio, temblor, escondido).
        double ox = 0, oy = 0;
        switch (b.Motion)
        {
            case Motion.Hop:
                double u = clip is null ? 1 : Math.Clamp(t / clip.Length, 0, 1);
                oy -= HopHeight * 4 * u * (1 - u);
                break;
            case Motion.Jitter:
                ox += 0.7 * Math.Sin(t * 45);
                break;
            case Motion.Ambush when cat.Phase == 0:
                oy += cat.Height * 0.3; // escondido: solo asoman las orejas
                break;
        }

        double surface = cat.Place switch
        {
            CatPlace.OnBox => Box.VisibleTop,
            CatPlace.InBox => double.NaN,
            _ => Floor,
        };
        double drop = double.IsNaN(surface) || cat.Place == CatPlace.Carried
            ? -1
            : Math.Max(0, surface - (cat.Bottom + oy));

        cat.Pose = new CatPose(clipId, frame, cat.FacingRight, ox, oy, drop,
            BehindBox: cat.Place == CatPlace.InBox);
    }

    private bool OverlapsBox(Cat cat)
    {
        double visibleTop = Box.VisibleTop;
        bool overlapX = cat.CenterX > Box.X && cat.CenterX < Box.Right;
        bool overlapY = cat.Bottom > visibleTop - cat.Height * 0.5 && cat.Y < Box.Bottom;
        return overlapX && overlapY;
    }
}
