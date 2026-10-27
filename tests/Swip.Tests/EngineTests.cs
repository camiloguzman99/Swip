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
        // Los clips que solo se usan como fase de otro movimiento (trotar al acercarse, etc.) también cuentan.
        var phaseClips = new[] { "trot", "walk", "sit_look", "box_peek", "sniff", "carry" };
        var used = Behaviors.All.Select(b => b.Clip).Concat(phaseClips).ToHashSet();
        foreach (var c in ClipLibrary.Default.All)
            Assert.True(used.Contains(c.Id), $"El clip '{c.Id}' no lo usa ningún comportamiento");
    }

    [Fact]
    public void Hay_disparador_para_cada_transicion_en_los_temperamentos_que_toca()
    {
        Assert.NotNull(Behaviors.ForTrigger(Trigger.ToSleep, CatMode.Sleep));
        Assert.NotNull(Behaviors.ForTrigger(Trigger.WakeUp, CatMode.Play));
        Assert.NotNull(Behaviors.ForTrigger(Trigger.WakeUp, CatMode.Walk));
        Assert.NotNull(Behaviors.ForTrigger(Trigger.ToBackground, CatMode.Play));
        Assert.NotNull(Behaviors.ForTrigger(Trigger.ToActive, CatMode.Walk));
        foreach (var m in Enum.GetValues<CatMode>())
        {
            Assert.NotNull(Behaviors.ForTrigger(Trigger.Pet, m));
            Assert.NotNull(Behaviors.ForTrigger(Trigger.Land, m));
            Assert.NotNull(Behaviors.ForTrigger(Trigger.Carry, m));
        }
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
        Assert.Contains("play_yarn", seen);
        Assert.True(seen.Count >= 6, $"poca variedad jugando: {string.Join(", ", seen)}");
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
        for (int seed = 1; seed <= 12 && !AllBoxActions(seen); seed++)
        {
            var w = NewWorld(seed, boxX: 500);
            w.BehaviorStarted += (_, b) => seen.Add(b.Id);
            w.AddCat("w", 300, CatMode.Walk);
            // Alterna caja cerrada/abierta para dar ocasión a subirse y a meterse.
            for (int i = 0; i < 8; i++)
            {
                Run(w, 75);
                w.SetBoxOpen(!w.Box.Open);
            }
        }
        foreach (var id in BoxActions)
            Assert.Contains(id, seen);
    }

    private static readonly string[] BoxActions =
        { "box_push", "box_jump_on", "box_jump_in", "box_exit_top", "box_exit_in", "box_rub", "box_circle",
          "box_peek", "box_sit_watch", "box_ambush", "box_scratch" };

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
        for (int i = 0; i < 40 && !pushed; i++) Run(w, 15);
        Assert.True(pushed, "nunca eligió empujar");
        Run(w, 10);
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

    [Theory]
    [InlineData(CatMode.Sleep, CatMode.Walk, "wake_up")]
    [InlineData(CatMode.Sleep, CatMode.Play, "wake_up")]
    [InlineData(CatMode.Walk, CatMode.Play, "to_sit")]
    [InlineData(CatMode.Play, CatMode.Walk, "to_active")]
    [InlineData(CatMode.Walk, CatMode.Sleep, "to_sleep")]
    [InlineData(CatMode.Play, CatMode.Sleep, "to_sleep")]
    public void Cambiar_de_sesion_lanza_su_transicion(CatMode from, CatMode to, string expected)
    {
        var w = NewWorld();
        var cat = w.AddCat("a", 100, from);
        Run(w, 1);
        w.SetMode("a", to);
        Run(w, 0.1);
        Assert.Equal(expected, cat.Current.Id);
    }

    [Fact]
    public void Acariciar_al_dormido_no_lo_despierta_ni_lo_mueve()
    {
        var w = NewWorld();
        var cat = w.AddCat("s", 300, CatMode.Sleep);
        Run(w, 2);
        w.Pet("s");
        Run(w, 0.1);
        Assert.Equal("sleep_petted", cat.Current.Id);
        Assert.Contains(w.Particles, p => p.Kind == EffectKind.Heart);
        Run(w, 3);
        Assert.Equal(CatMode.Sleep, cat.Mode);
        Assert.Contains(CatMode.Sleep, cat.Current.Modes);
        Assert.Equal(300, cat.X, 6);
    }

    [Fact]
    public void Con_el_menu_abierto_te_mira_y_al_cerrarlo_sigue_con_lo_suyo()
    {
        var w = NewWorld();
        var cat = w.AddCat("w", 300, CatMode.Walk);
        w.SetAttention("w", true);
        Run(w, 8);
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

    [Fact]
    public void Un_gato_activo_invita_a_jugar_al_que_esta_jugando()
    {
        bool invited = false;
        for (int seed = 1; seed <= 15 && !invited; seed++)
        {
            var w = NewWorld(seed, boxX: 1100);
            w.AddCat("w", 300, CatMode.Walk);
            w.AddCat("p", 340, CatMode.Play);
            w.BehaviorStarted += (c, b) => { if (c.Id == "p" && b.Id == "play_invited") invited = true; };
            Run(w, 120);
        }
        Assert.True(invited);
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
        Run(w, 120, () =>
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
        Assert.True(moves > 20, $"apenas se movió ({moves} avances)");
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
                foreach (var skin in Swip.Shared.Art.CatPalettes.Skins)
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
