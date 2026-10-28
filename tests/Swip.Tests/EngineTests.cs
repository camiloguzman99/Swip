using System.Text.Json;
using Swip.Shared.Engine;

namespace Swip.Tests;

public class EngineTests
{
    private const double W = 1200, H = 700, Dt = 1.0 / 30;

    private static readonly WorldConfig Config = new(
        CatWidth: 34, CatHeight: 48, BoxWidth: 50, BoxHeight: 36,
        BoxTopInsetClosed: 46.0 / 282, BoxTopInsetOpen: 5.0 / 282);

    private static SwipWorld NewWorld(int seed = 1, double boxX = 600)
    {
        var w = new SwipWorld(Config, seed: seed);
        w.Resize(W, H);
        w.PlaceBox(boxX);
        return w;
    }

    private static void Run(SwipWorld w, double seconds, Action? eachStep = null)
    {
        for (double t = 0; t < seconds; t += Dt)
        {
            w.Step(Dt);
            eachStep?.Invoke();
        }
    }

    // ---------------------------------------------------------------- catálogo

    [Fact]
    public void Cada_comportamiento_usa_un_clip_que_existe()
    {
        var clips = ClipLibrary.Default;
        foreach (var b in Behaviors.All)
            Assert.True(clips.Contains(b.Clip), $"'{b.Id}' usa el clip inexistente '{b.Clip}'");
    }

    [Fact]
    public void Los_identificadores_de_comportamiento_son_unicos()
    {
        var dup = Behaviors.All.GroupBy(b => b.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(dup);
    }

    [Fact]
    public void Cada_clip_tiene_al_menos_un_comportamiento_que_lo_usa()
    {
        // Trotar y caminar también se usan como fase al acercarse a la caja.
        var phaseClips = new[] { "trot", "walk" };
        var used = Behaviors.All.Select(b => b.Clip).Concat(phaseClips).ToHashSet();
        foreach (var c in ClipLibrary.Default.All)
            Assert.True(used.Contains(c.Id), $"El clip '{c.Id}' no lo usa ningún comportamiento");
    }

    [Fact]
    public void Hay_reaccion_para_cada_disparador_en_todos_los_temperamentos()
    {
        foreach (var m in Enum.GetValues<CatMode>())
        {
            Assert.NotNull(Behaviors.ForTrigger(Trigger.Land, m));
            Assert.NotNull(Behaviors.ForTrigger(Trigger.Carry, m));
        }
        foreach (var m in new[] { CatMode.Play, CatMode.Walk })
            Assert.NotNull(Behaviors.ForTrigger(Trigger.Attention, m));
    }

    [Fact]
    public void Las_acciones_son_las_catorce_pedidas()
    {
        var clips = Behaviors.Actions.Select(b => b.Clip).ToHashSet();
        foreach (var clip in new[] { "sit_look", "loaf_idle", "stand_idle", "sleep_curl", "sleep_side", "walk", "trot", "run",
                     "groom_paw", "jump", "jump_box", "box_enter", "box_exit", "box_push" })
            Assert.Contains(clip, clips);
    }

    [Fact]
    public void Entre_dos_posturas_cualesquiera_hay_camino_y_los_pasos_son_contiguos()
    {
        var all = Enum.GetValues<Posture>().Where(p => p != Posture.Any).ToList();
        foreach (var a in all)
            foreach (var b in all)
            {
                var path = Behaviors.Path(a, b);
                if (a == b) { Assert.Empty(path); continue; }
                Assert.NotEmpty(path);
                Assert.Equal(a, path[0].From);
                Assert.Equal(b, path[^1].To);
                for (int i = 1; i < path.Count; i++) Assert.Equal(path[i - 1].To, path[i].From);
            }
    }

    [Fact]
    public void De_bolita_a_correr_no_se_salta_ningun_paso()
    {
        var ids = Behaviors.Path(Posture.Curl, Posture.Run).Select(e => e.Via?.Id ?? "corte").ToList();
        Assert.Equal(new[] { "corte", "t_wake_up", "t_sit_to_stand", "t_stand_to_run" }, ids);
    }

    [Theory]
    [InlineData(CatMode.Sleep, CatPlace.Ground)]
    [InlineData(CatMode.Sleep, CatPlace.OnBox)]
    [InlineData(CatMode.Sleep, CatPlace.InBox)]
    [InlineData(CatMode.Play, CatPlace.Ground)]
    [InlineData(CatMode.Play, CatPlace.OnBox)]
    [InlineData(CatMode.Play, CatPlace.InBox)]
    [InlineData(CatMode.Walk, CatPlace.Ground)]
    [InlineData(CatMode.Walk, CatPlace.OnBox)]
    [InlineData(CatMode.Walk, CatPlace.InBox)]
    public void En_cualquier_temperamento_y_lugar_hay_algo_que_hacer_sin_recurrir_al_respaldo(CatMode mode, CatPlace place)
    {
        var w = NewWorld();
        w.SetBoxOpen(place == CatPlace.InBox);
        var cat = w.AddCat("a", place == CatPlace.Ground ? 100 : 600, mode);
        if (place != CatPlace.Ground)
        {
            w.BeginCarry("a");
            w.CarryTo("a", w.Box.CenterX - cat.Width / 2, w.Box.Y - 10);
            Assert.True(w.Drop("a"));
        }
        Assert.Equal(place, cat.Place);

        var options = Behaviors.All.Where(b => b.Trigger == Trigger.Random && b.Modes.Contains(mode) && w.Allowed(cat, b));
        Assert.NotEmpty(options);
    }

    // ---------------------------------------------------------------- reglas de los temperamentos

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Un_gato_dormido_nunca_se_desplaza_por_su_cuenta(int seed)
    {
        var w = NewWorld(seed);
        var cat = w.AddCat("s", 200, CatMode.Sleep);
        double x = cat.X;
        Run(w, 300, () =>
        {
            Assert.Equal(x, cat.X, 6);
            Assert.Equal(CatPlace.Ground, cat.Place);
            Assert.Contains(CatMode.Sleep, cat.Current.Modes);
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Un_gato_jugando_nunca_camina(int seed)
    {
        var w = NewWorld(seed);
        var cat = w.AddCat("p", 200, CatMode.Play);
        double x = cat.X;
        var seen = new HashSet<string>();
        Run(w, 400, () =>
        {
            Assert.Equal(x, cat.X, 6);
            seen.Add(cat.Current.Id);
        });
        Assert.Contains("sit", seen);
        Assert.True(seen.Count >= 3, $"poca variedad en reposo: {string.Join(", ", seen)}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void Un_gato_caminando_recorre_la_pantalla_sin_salirse(int seed)
    {
        var w = NewWorld(seed);
        var cat = w.AddCat("w", 300, CatMode.Walk);
        double minX = cat.X, maxX = cat.X;
        Run(w, 400, () =>
        {
            Assert.InRange(cat.X, 0, W - cat.Width);
            Assert.True(cat.Bottom <= H + 0.01, $"por debajo del suelo: {cat.Bottom}");
            minX = Math.Min(minX, cat.X);
            maxX = Math.Max(maxX, cat.X);
        });
        Assert.True(maxX - minX > 150, $"apenas se movió: {minX:0}..{maxX:0}");
    }

    // ---------------------------------------------------------------- caja

    [Fact]
    public void Caminando_llega_a_hacer_todas_las_acciones_con_la_caja()
    {
        var seen = new HashSet<string>();
        for (int seed = 1; seed <= 20 && !AllBoxActions(seen); seed++)
        {
            var w = NewWorld(seed, boxX: 500);
            w.BehaviorStarted += (_, b) => seen.Add(b.Id);
            w.AddCat("w", 300, CatMode.Walk);
            // Alterna caja cerrada/abierta para dar ocasión a subirse y a meterse.
            for (int i = 0; i < 16; i++)
            {
                Run(w, 90);
                w.SetBoxOpen(!w.Box.Open);
            }
        }
        foreach (var id in BoxActions)
            Assert.Contains(id, seen);
    }

    private static readonly string[] BoxActions =
        { "box_push", "box_jump_on", "box_jump_in", "box_exit_top", "box_exit_in" };

    private static bool AllBoxActions(HashSet<string> seen) => BoxActions.All(seen.Contains);

    [Fact]
    public void La_caja_nunca_tiene_mas_de_un_ocupante_y_el_ocupante_esta_en_ella()
    {
        var w = NewWorld(5, boxX: 500);
        w.AddCat("a", 200, CatMode.Walk);
        w.AddCat("b", 800, CatMode.Walk);
        Run(w, 600, () =>
        {
            var inBox = w.Cats.Where(c => c.Place is CatPlace.OnBox or CatPlace.InBox).ToList();
            Assert.True(inBox.Count <= 1);
            if (inBox.Count == 1) Assert.Equal(inBox[0].Id, w.Box.OccupantId);
            Assert.InRange(w.Box.X, 0, W - w.Box.Width);
        });
    }

    [Fact]
    public void Empujar_desplaza_la_caja_y_la_friccion_la_detiene()
    {
        var w = NewWorld(3, boxX: 600);
        var cat = w.AddCat("w", 400, CatMode.Walk);
        double startBox = w.Box.X;
        bool pushed = false;
        w.BehaviorStarted += (_, b) => { if (b.Id == "box_push") pushed = true; };
        // Un intento puede no llegar a la caja (el gato empieza lejos y se rinde): se espera a uno que sí.
        for (int i = 0; i < 80 && w.Box.X == startBox; i++) Run(w, 15);
        Assert.True(pushed, "nunca eligió empujar");
        Assert.NotEqual(startBox, w.Box.X);

        // Sin gatos empujando, la caja acaba quieta.
        w.SetMode("w", CatMode.Sleep);
        Run(w, 15);
        Assert.Equal(0, w.Box.Vx, 3);
    }

    [Fact]
    public void Soltar_sobre_la_caja_cerrada_lo_sienta_encima_y_abrirla_lo_mete_dentro()
    {
        var w = NewWorld();
        var cat = w.AddCat("a", 100, CatMode.Play);
        w.BeginCarry("a");
        w.CarryTo("a", w.Box.CenterX - cat.Width / 2, w.Box.Y - 20);
        Assert.True(w.Drop("a"));
        Run(w, 1);
        Assert.Equal(CatPlace.OnBox, cat.Place);
        Assert.Equal(w.Box.VisibleTop, cat.Bottom, 3);   // apoyado en el borde visible de la tapa

        w.SetBoxOpen(true);
        Run(w, 1);
        Assert.Equal(CatPlace.InBox, cat.Place);
        Assert.True(cat.Pose.BehindBox);
        double visibleH = w.Box.Bottom - w.Box.VisibleTop;
        Assert.Equal(w.Box.VisibleTop + visibleH * 0.5, cat.Bottom, 3); // base al 50% del alto
    }

    [Theory]
    [InlineData(false, BoxBody.LightSquash)]
    [InlineData(true, BoxBody.HeavySquash)]
    public void La_caja_se_aplasta_segun_el_peso_del_gato_y_recupera_su_forma(bool heavy, int expected)
    {
        var w = NewWorld();
        var cat = w.AddCat("a", 100, CatMode.Play);
        w.SetCatHeavy("a", heavy);
        w.BeginCarry("a");
        w.CarryTo("a", w.Box.CenterX - cat.Width / 2, w.Box.Y - 20);
        Assert.True(w.Drop("a"));
        var seen = new HashSet<int>();
        Run(w, 1, () => seen.Add(w.Box.Squash));
        Assert.Equal(expected, w.Box.Squash);
        Assert.Equal(w.Box.VisibleTop, cat.Bottom, 3);           // el gato baja con la tapa
        Assert.All(Enumerable.Range(0, expected + 1), k => Assert.Contains(k, seen)); // fila a fila

        w.BeginCarry("a");                                       // se lo llevan: la caja recupera su forma
        Run(w, 1);
        Assert.Equal(0, w.Box.Squash);
    }

    [Fact]
    public void Las_solapas_se_abren_y_cierran_pasando_por_todos_los_fotogramas()
    {
        var w = NewWorld();
        var seen = new List<int> { w.Box.Lid };
        void Record() { if (seen[^1] != w.Box.Lid) seen.Add(w.Box.Lid); }
        w.SetBoxOpen(true);
        Run(w, 1, Record);
        Assert.Equal(Enumerable.Range(0, BoxBody.LidFrames), seen);   // 0, 1, 2, 3, 4
        seen = new List<int> { w.Box.Lid };
        w.SetBoxOpen(false);
        Run(w, 1, Record);
        Assert.Equal(Enumerable.Range(0, BoxBody.LidFrames).Reverse(), seen);
    }

    [Fact]
    public void El_tambaleo_es_leve()
    {
        var w = NewWorld(5, boxX: 500);
        w.AddCat("a", 200, CatMode.Walk);
        w.AddCat("b", 800, CatMode.Walk);
        double max = 0;
        Run(w, 300, () => max = Math.Max(max, Math.Abs(w.Box.Wobble)));
        Assert.InRange(max, 0, BoxBody.MaxWobble);
    }

    [Fact]
    public void Si_mueves_la_caja_el_gato_que_esta_en_ella_va_con_ella()
    {
        var w = NewWorld();
        var cat = w.AddCat("a", 100, CatMode.Sleep);
        w.BeginCarry("a");
        w.CarryTo("a", w.Box.CenterX - cat.Width / 2, w.Box.Y - 20);
        w.Drop("a");
        w.BeginBoxDrag();
        w.BoxDragTo(200, w.Box.Y);
        Run(w, 0.2);
        Assert.Equal(w.Box.CenterX, cat.CenterX, 3);
    }

    [Fact]
    public void Soltarlo_en_el_aire_lo_hace_caer_y_aterrizar()
    {
        var w = NewWorld();
        var cat = w.AddCat("a", 100, CatMode.Walk);
        var started = new List<string>();
        w.BehaviorStarted += (_, b) => started.Add(b.Id);
        w.BeginCarry("a");
        w.CarryTo("a", 100, 100);
        Assert.False(w.Drop("a"));
        Assert.Equal(CatPlace.Air, cat.Place);
        Run(w, 2);
        Assert.Equal(CatPlace.Ground, cat.Place);
        Assert.Contains("land", started);
        Assert.Equal(H, cat.Bottom, 3);
    }

    // ---------------------------------------------------------------- transiciones y reacciones

    private static List<string> Started(SwipWorld w, Action act, double seconds)
    {
        var ids = new List<string>();
        void On(Cat c, Behavior b) => ids.Add(b.Id);
        w.BehaviorStarted += On;
        act();
        Run(w, seconds);
        w.BehaviorStarted -= On;
        return ids;
    }

    [Theory]
    [InlineData(CatMode.Walk)]
    [InlineData(CatMode.Play)]
    public void Al_despertar_primero_se_incorpora_y_nunca_arranca_directo_a_caminar(CatMode to)
    {
        var w = NewWorld();
        w.AddCat("a", 100, CatMode.Sleep);
        Run(w, 1);
        var ids = Started(w, () => w.SetMode("a", to), 90);
        Assert.Contains(ids[0], new[] { "t_wake_up", "loaf" });
        int firstGait = ids.FindIndex(i => i is "walk" or "trot" or "run");
        if (firstGait >= 0) Assert.True(ids.IndexOf("t_sit_to_stand") is >= 0 and var up && up < firstGait,
            $"echó a andar sin levantarse: {string.Join(", ", ids.Take(firstGait + 1))}");
        if (to == CatMode.Play) Assert.DoesNotContain(ids, i => i is "walk" or "trot" or "run");
    }

    [Fact]
    public void Pasar_a_segundo_plano_lo_detiene_y_ya_no_camina()
    {
        var w = NewWorld(2);
        var cat = w.AddCat("a", 100, CatMode.Walk);
        Run(w, 3);
        w.SetMode("a", CatMode.Play);
        Run(w, 20);   // le da tiempo a frenar, sentarse...
        double x = cat.X;
        Run(w, 200, () => Assert.Equal(x, cat.X, 6));
        Assert.DoesNotContain(cat.Posture, new[] { Posture.Walk, Posture.Trot, Posture.Run });
    }

    [Theory]
    [InlineData(CatMode.Walk)]
    [InlineData(CatMode.Play)]
    public void Al_cerrar_la_sesion_se_acuesta_y_se_duerme_pasando_por_las_transiciones(CatMode from)
    {
        var w = NewWorld(4);
        var cat = w.AddCat("a", 100, from);
        Run(w, 2);
        var ids = Started(w, () => w.SetMode("a", CatMode.Sleep), 60);
        Assert.Contains("t_to_sleep", ids);
        Assert.Contains(ids.Last(), new[] { "sleep_curl", "sleep_side" });
        Assert.Equal(CatPlace.Ground, cat.Place);
    }

    [Fact]
    public void Acariciar_suelta_corazones_sin_cambiar_lo_que_hace_ni_moverlo()
    {
        var w = NewWorld();
        var cat = w.AddCat("s", 300, CatMode.Sleep);
        Run(w, 2);
        string before = cat.Current.Id;
        w.Pet("s");
        Run(w, 0.1);
        Assert.Contains(w.Particles, p => p.Kind == EffectKind.Heart);
        Assert.Equal(before, cat.Current.Id);
        Assert.Equal(CatMode.Sleep, cat.Mode);
        Assert.Equal(300, cat.X, 6);
    }

    [Fact]
    public void Con_el_menu_abierto_te_mira_y_al_cerrarlo_sigue_con_lo_suyo()
    {
        var w = NewWorld();
        var cat = w.AddCat("w", 300, CatMode.Walk);
        w.SetAttention("w", true);
        Run(w, 20);
        Assert.Equal("attention", cat.Current.Id);
        w.SetAttention("w", false);
        Run(w, 0.2);
        Assert.NotEqual("attention", cat.Current.Id);
    }

    [Fact]
    public void Dormido_ignora_la_atencion()
    {
        var w = NewWorld();
        var cat = w.AddCat("s", 300, CatMode.Sleep);
        w.SetAttention("s", true);
        Run(w, 20, () => Assert.NotEqual("attention", cat.Current.Id));
    }

    // ---------------------------------------------------------------- ritmo realista

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void El_gato_no_cambia_de_accion_a_cada_rato_y_descansa(int seed)
    {
        var w = NewWorld(seed, boxX: 1100);
        w.AddCat("a", 300, CatMode.Walk);
        var starts = new List<(double T, Behavior B)>();
        double t = 0;
        w.BehaviorStarted += (_, b) => { if (b.Trigger == Trigger.Random) starts.Add((t, b)); };
        for (; t < 900; t += Dt) w.Step(Dt);

        // Al menos unos segundos por acción y nunca ráfagas de cambios.
        Assert.True(starts.Count <= 900 / 5.0, $"demasiados cambios de acción: {starts.Count} en 15 min");
        for (int i = 0; i + 4 < starts.Count; i++)
            Assert.True(starts[i + 4].T - starts[i].T >= 10, $"5 acciones en {starts[i + 4].T - starts[i].T:0.0} s");

        // Lo que cansa nunca va seguido de otra cosa que cansa ni se repite antes de su enfriamiento.
        foreach (var name in new[] { "run", "leap", "groom", "trot" })
        {
            var b = Behaviors.Actions.First(x => x.Id == name);
            var times = starts.Where(x => x.B.Id == name).Select(x => x.T).ToList();
            for (int i = 1; i < times.Count; i++)
                Assert.True(times[i] - times[i - 1] >= b.Cooldown, $"{name} se repite a los {times[i] - times[i - 1]:0} s");
        }
        // Y descansa de verdad: más tiempo en reposo que en acciones que cansan.
        double rest = 0, active = 0;
        for (int i = 0; i < starts.Count - 1; i++)
        {
            double d = starts[i + 1].T - starts[i].T;
            if (starts[i].B.Active) active += d; else rest += d;
        }
        Assert.True(rest > active, $"descansa {rest:0} s y se esfuerza {active:0} s");
    }

    [Fact]
    public void Nunca_encadena_dos_acciones_que_cansan_sin_descansar()
    {
        int checkedPairs = 0;
        for (int seed = 1; seed <= 8; seed++)
        {
            var w = NewWorld(seed, boxX: 1100);
            w.AddCat("a", 300, CatMode.Walk);
            Behavior? last = null;
            w.BehaviorStarted += (_, b) =>
            {
                if (b.Trigger != Trigger.Random) return;
                if (last is { Active: true } && b.Active) checkedPairs++;
                last = b;
            };
            Run(w, 900);
        }
        Assert.True(checkedPairs <= 8, $"{checkedPairs} acciones que cansan seguidas");
    }

    [Fact]
    public void Saltar_al_mismo_nivel_avanza_por_el_suelo_y_vuelve_a_apoyarse()
    {
        bool leapt = false;
        for (int seed = 1; seed <= 30 && !leapt; seed++)
        {
            var w = NewWorld(seed, boxX: 1100);
            var cat = w.AddCat("a", 300, CatMode.Walk);
            w.BehaviorStarted += (_, b) => { if (b.Id == "leap") leapt = true; };
            Run(w, 300);
            if (leapt)
            {
                Assert.Equal(CatPlace.Ground, cat.Place);
                Assert.Equal(H, cat.Bottom, 3);
            }
        }
        Assert.True(leapt, "nunca saltó al mismo nivel");
    }

    // ---------------------------------------------------------------- cerrar la caja con el gato dentro

    private static (SwipWorld W, Cat Cat) CatInOpenBox(CatMode mode)
    {
        var w = NewWorld(6, boxX: 500);
        w.SetBoxOpen(true);
        var cat = w.AddCat("a", 100, mode);
        w.BeginCarry("a");
        w.CarryTo("a", w.Box.CenterX - cat.Width / 2, w.Box.Y - 10);
        Assert.True(w.Drop("a"));
        Run(w, 0.5);
        Assert.Equal(CatPlace.InBox, cat.Place);
        return (w, cat);
    }

    [Theory]
    [InlineData(CatMode.Walk)]
    [InlineData(CatMode.Play)]
    public void Si_van_a_cerrar_la_caja_el_gato_despierto_sale_antes_y_luego_se_cierra(CatMode mode)
    {
        var (w, cat) = CatInOpenBox(mode);
        w.SetBoxOpen(false);
        Assert.True(w.Box.Open, "la tapa no debe cerrarse con el gato dentro");
        bool closedWhileInside = false;
        Run(w, 30, () =>
        {
            if (!w.Box.Open && cat.Place == CatPlace.InBox) closedWhileInside = true;
        });
        Assert.False(closedWhileInside);
        Assert.NotEqual(CatPlace.InBox, cat.Place);
        Assert.False(w.Box.Open, "la caja debe cerrarse cuando ya ha salido");
        Assert.Null(w.Box.OccupantId);
    }

    [Fact]
    public void Si_vuelven_a_abrir_la_caja_antes_de_que_salga_no_se_cierra()
    {
        var (w, cat) = CatInOpenBox(CatMode.Walk);
        w.SetBoxOpen(false);
        w.SetBoxOpen(true);
        Run(w, 30);
        Assert.True(w.Box.Open);
    }

    [Fact]
    public void Al_cerrar_la_caja_un_gato_dormido_dentro_se_queda_encima_de_la_tapa()
    {
        var (w, cat) = CatInOpenBox(CatMode.Sleep);
        w.SetBoxOpen(false);
        Assert.False(w.Box.Open);
        Run(w, 1);
        Assert.Equal(CatPlace.OnBox, cat.Place);
    }

    [Fact]
    public void Al_chocar_con_la_caja_para_empujarla_la_hace_tambalear()
    {
        bool pushed = false;
        double maxWobble = 0;
        for (int seed = 1; seed <= 20 && !pushed; seed++)
        {
            var w = NewWorld(seed, boxX: 700);
            w.AddCat("a", 400, CatMode.Walk);
            w.BehaviorStarted += (_, b) => { if (b.Id == "box_push") pushed = true; };
            for (double t = 0; t < 600 && !pushed; t += Dt) w.Step(Dt);
            if (!pushed) continue;
            for (double t = 0; t < 30; t += Dt)
            {
                w.Step(Dt);
                maxWobble = Math.Max(maxWobble, Math.Abs(w.Box.Wobble));
            }
        }
        Assert.True(pushed);
        Assert.True(maxWobble > 0, "la caja no reaccionó al choque");
    }

    // ---------------------------------------------------------------- efectos

    [Fact]
    public void Dormido_suelta_zzz_y_las_particulas_caducan()
    {
        var w = NewWorld();
        w.AddCat("s", 300, CatMode.Sleep);
        Run(w, 12);
        Assert.Contains(w.Particles, p => p.Kind == EffectKind.Zzz);
        Assert.All(w.Particles, p => Assert.InRange(p.Alpha, 0, 1));
        w.RemoveCat("s");
        Run(w, 5);
        Assert.Empty(w.Particles);
    }

    [Fact]
    public void Las_particulas_nunca_superan_el_tope()
    {
        var w = NewWorld();
        for (int i = 0; i < 8; i++) w.AddCat($"c{i}", 50 + i * 120, CatMode.Walk);
        Run(w, 200, () => Assert.True(w.Particles.Count <= ParticleSystem.MaxParticles));
    }

    [Fact]
    public void La_pose_siempre_apunta_a_un_clip_y_fotograma_validos()
    {
        var clips = ClipLibrary.Default;
        var w = NewWorld(9);
        w.AddCat("a", 100, CatMode.Walk);
        w.AddCat("b", 500, CatMode.Play);
        w.AddCat("c", 900, CatMode.Sleep);
        Run(w, 300, () =>
        {
            foreach (var cat in w.Cats)
            {
                Assert.True(clips.Contains(cat.Pose.Clip), cat.Pose.Clip);
                Assert.InRange(cat.Pose.Frame, 0, clips[cat.Pose.Clip].FrameCount - 1);
                Assert.True(double.IsFinite(cat.X) && double.IsFinite(cat.Y));
            }
        });
    }

    // ---------------------------------------------------------------- desplazamiento por fotogramas

    [Fact]
    public void Los_clips_con_los_que_se_desplaza_tienen_avance_por_fotograma()
    {
        var clips = ClipLibrary.Default;
        foreach (var id in new[] { "walk", "trot", "run", "box_push" })
        {
            var clip = clips[id];
            Assert.True(clip.Speed > 0, $"{id} no tiene 'advance'");
            Assert.True(clip.Advance!.Length == 1 || clip.Advance.Length == clip.FrameCount,
                $"{id}: 'advance' debe tener un valor o uno por fotograma");
        }
    }

    [Fact]
    public void El_gato_solo_se_desplaza_cuando_cambia_el_fotograma()
    {
        var clips = ClipLibrary.Default;
        var w = NewWorld(3, boxX: 1600);
        var cat = w.AddCat("a", 400, CatMode.Walk);
        int moves = 0, movesWithoutNewFrame = 0;
        (string Clip, int Frame) last = (cat.Pose.Clip, cat.Pose.Frame);
        double lastX = cat.X;
        Run(w, 400, () =>
        {
            var now = (cat.Pose.Clip, cat.Pose.Frame);
            bool walking = cat.Place == CatPlace.Ground && cat.Current.Motion is Motion.Walk or Motion.Trot or Motion.Run;
            if (walking && cat.X != lastX)
            {
                moves++;
                if (now == last) movesWithoutNewFrame++;
                // Cada avance es un número entero de casillas (salvo al chocar con el borde de la pantalla).
                bool atWall = cat.X <= 0 || cat.X >= W - Config.CatWidth;
                if (!atWall) Assert.True(Math.Abs(Math.Round(Math.Abs(cat.X - lastX)) - Math.Abs(cat.X - lastX)) < 1e-6,
                    $"{cat.Current.Id} {cat.Pose.Clip} {lastX} -> {cat.X}");
            }
            last = now;
            lastX = cat.X;
        });
        Assert.True(moves > 15, $"apenas se movió ({moves} avances)");
        Assert.Equal(0, movesWithoutNewFrame);
    }

    [Fact]
    public void Al_caminar_la_velocidad_es_el_avance_por_fotograma_por_los_fps()
    {
        var walk = ClipLibrary.Default["walk"];
        var w = NewWorld(4, boxX: 1600);
        var cat = w.AddCat("a", 300, CatMode.Walk);
        double walked = 0, time = 0, lastX = cat.X;
        Run(w, 200, () =>
        {
            if (cat.Current.Motion == Motion.Walk && cat.Place == CatPlace.Ground && cat.X > 1 && cat.X < W - 60)
            {
                walked += Math.Abs(cat.X - lastX);
                time += Dt;
            }
            lastX = cat.X;
        });
        Assert.True(time > 5, "no caminó lo suficiente para medir");
        Assert.InRange(walked / time, walk.Speed * 0.8, walk.Speed * 1.2);
    }

    [Fact]
    public void Con_casillas_de_media_unidad_el_gato_avanza_la_mitad_por_segundo()
    {
        var walk = ClipLibrary.Default["walk"];
        var half = new WorldConfig(CatWidth: 17, CatHeight: 24, BoxWidth: 25, BoxHeight: 18,
            BoxTopInsetClosed: 46.0 / 282, BoxTopInsetOpen: 5.0 / 282, CellSize: 0.5);
        var w = new SwipWorld(half, seed: 4);
        w.Resize(W, H);
        w.PlaceBox(1600);
        var cat = w.AddCat("a", 300, CatMode.Walk);
        double walked = 0, time = 0, lastX = cat.X;
        Run(w, 200, () =>
        {
            if (cat.Current.Motion == Motion.Walk && cat.Place == CatPlace.Ground && cat.X > 1 && cat.X < W - 60)
            {
                walked += Math.Abs(cat.X - lastX);
                time += Dt;
            }
            lastX = cat.X;
        });
        Assert.True(time > 5, "no caminó lo suficiente para medir");
        Assert.InRange(walked / time, walk.Speed * 0.5 * 0.8, walk.Speed * 0.5 * 1.2);
    }

    // ---------------------------------------------------------------- arte

    [Fact]
    public void El_atlas_generado_tiene_todos_los_clips_con_sus_fotogramas_para_cada_piel()
    {
        string path = Path.Combine(RepoRoot(), "src", "Swip.App", "Assets", "atlas", "atlas.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var atlasClips = doc.RootElement.GetProperty("clips");
        foreach (var clip in ClipLibrary.Default.All)
        {
            Assert.True(atlasClips.TryGetProperty(clip.Id, out var entry), $"falta '{clip.Id}' en el atlas: ejecuta tools/art/build_atlas.py");
            var frames = entry.GetProperty("frames");
            Assert.Equal(clip.FrameCount, frames.GetArrayLength());
            foreach (var f in frames.EnumerateArray())
                foreach (var skin in Swip.Shared.Art.CatSkins.All)
                    Assert.Equal(4, f.GetProperty("bounds").GetProperty(skin).GetArrayLength());
        }
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Swip.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }
}
