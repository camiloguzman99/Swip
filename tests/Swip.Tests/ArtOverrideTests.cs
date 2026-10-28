using Swip.Shared.Art;
using Swip.Shared.Engine;
using Swip.Shared.Settings;

namespace Swip.Tests;

public class ArtOverrideTests
{
    private static ClipLibrary Library() => ClipLibrary.Parse("""
        {"clips":[
          {"id":"walk","fps":8,"loop":true,"advance":[4,4],"status":"final","description":"x","reference":[{},{}]},
          {"id":"to_sit","fps":5,"loop":false,"status":"final","description":"y","reference":[{},{},{},{},{}]}
        ]}
        """);

    [Fact]
    public void Un_retoque_cambia_los_fps_y_se_puede_deshacer()
    {
        var lib = Library();
        lib.Override("to_sit", 10, null);
        Assert.Equal(10, lib["to_sit"].Fps);
        Assert.Equal(0.5, lib["to_sit"].Length);
        lib.Reset("to_sit");
        Assert.Equal(5, lib["to_sit"].Fps);
    }

    [Fact]
    public void Los_retoques_se_leen_validados()
    {
        var lib = Library();
        var parsed = ClipTimingFile.Parse("""
            {"clips":{
              "walk":{"fps":9999,"advance":[1000,-1000,3]},
              "to_sit":{"fps":0},
              "no_existe":{"fps":10},
              "walk2":null
            }}
            """, lib);
        Assert.Equal(2, parsed.Count);                       // el clip inexistente y el nulo se ignoran
        Assert.Equal(ClipTimingFile.MaxFps, parsed["walk"].Fps);
        Assert.Equal(new[] { 64, -64, 3 }, parsed["walk"].Advance);
        Assert.Equal(ClipTimingFile.MinFps, parsed["to_sit"].Fps);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no es json")]
    [InlineData("{\"clips\": 5}")]
    public void Un_archivo_de_tiempos_roto_no_cambia_nada(string json)
    {
        Assert.Empty(ClipTimingFile.Parse(json, Library()));
    }

    [Fact]
    public void Aplicar_devuelve_a_su_valor_los_clips_que_ya_no_estan_retocados()
    {
        var lib = Library();
        ClipTimingFile.Apply(lib, new Dictionary<string, ClipTimingEntry> { ["walk"] = new(12, new[] { 2 }) });
        Assert.Equal(12, lib["walk"].Fps);
        Assert.Equal(new[] { 2 }, lib["walk"].Advance);
        ClipTimingFile.Apply(lib, new Dictionary<string, ClipTimingEntry>());
        Assert.Equal(8, lib["walk"].Fps);
        Assert.Equal(new[] { 4, 4 }, lib["walk"].Advance);
    }

    [Fact]
    public void Serializar_y_leer_da_lo_mismo()
    {
        var lib = Library();
        var entries = new Dictionary<string, ClipTimingEntry> { ["to_sit"] = new(7.5, null), ["walk"] = new(9, new[] { 1, 2 }) };
        var back = ClipTimingFile.Parse(ClipTimingFile.Serialize(entries), lib);
        Assert.Equal(7.5, back["to_sit"].Fps);
        Assert.Equal(new[] { 1, 2 }, back["walk"].Advance);
    }

    [Fact]
    public void El_tamano_de_casilla_queda_entre_los_limites_y_los_NaN_vuelven_al_valor_por_defecto()
    {
        Assert.Equal(ArtScale.Max, ArtScale.Clamp(99));
        Assert.Equal(ArtScale.Min, ArtScale.Clamp(0));
        Assert.Equal(ArtScale.Default, ArtScale.Clamp(double.NaN));
        Assert.Equal(0.75, ArtScale.Clamp(0.75));
    }

    [Fact]
    public void Cambiar_el_tamano_escala_gatos_y_caja_y_los_vuelve_a_apoyar_en_el_suelo()
    {
        var cfg = new WorldConfig(50, 50, 33, 33, 0.1, 0.05, CellSize: 0.5);
        var w = new SwipWorld(cfg, seed: 2);
        w.Resize(1200, 700);
        w.PlaceBox(600);
        var cat = w.AddCat("a", 200, CatMode.Sleep);
        w.SetCellSize(0.25);
        Assert.Equal(25, cat.Width, 6);
        Assert.Equal(25, cat.Height, 6);
        Assert.Equal(16.5, w.Box.Width, 6);
        Assert.Equal(700, cat.Bottom, 6);                      // sigue apoyado en el suelo
        Assert.Equal(700, w.Box.Bottom, 6);
        w.SetCellSize(0.5);
        Assert.Equal(50, cat.Width, 6);
        Assert.Equal(700, cat.Bottom, 6);
    }

    [Fact]
    public void El_cambio_de_tamano_y_de_arte_de_otra_sesion_se_detecta()
    {
        var before = new AppSettings();
        var after = new AppSettings { CellSize = 0.75, ArtRevision = 3 };
        var delta = SettingsDelta.Between(before, after);
        Assert.True(delta.Size);
        Assert.True(delta.Art);
        Assert.False(SettingsDelta.Between(before, new AppSettings()).Any);
    }
}

public class ArtNamesTests
{
    [Theory]
    [InlineData("walk", 3, "walk_3.png")]
    [InlineData("to_sit", 0, "to_sit_0.png")]
    public void El_nombre_del_archivo_de_un_cuadro(string clip, int frame, string expected) =>
        Assert.Equal(expected, ArtNames.FrameFile(clip, frame));

    [Theory]
    [InlineData("..\\evil", 0)]
    [InlineData("../evil", 0)]
    [InlineData("a/b", 1)]
    [InlineData("WALK", 1)]
    [InlineData("", 1)]
    [InlineData("walk", -1)]
    [InlineData("walk", 100000)]
    public void Los_nombres_que_salen_de_la_carpeta_se_rechazan(string clip, int frame) =>
        Assert.Null(ArtNames.FrameFile(clip, frame));

    [Fact]
    public void Se_lee_el_clip_y_el_cuadro_de_un_nombre_de_archivo()
    {
        Assert.Equal(("to_sit", 4), ArtNames.ParseFrameFile("to_sit_4.png"));
        Assert.Null(ArtNames.ParseFrameFile("to_sit.png"));
        Assert.Null(ArtNames.ParseFrameFile("x_y.png"));
        Assert.Null(ArtNames.ParseFrameFile("..\\x_1.png"));
        Assert.Null(ArtNames.ParseFrameFile("walk_1.jpg"));
    }
}

public class TransitionGraphTests
{
    private static TransitionGraph Graph() => new(
        new[]
        {
            new TransitionEdge("sit_to_stand", "sit", "stand", false),
            new TransitionEdge("to_sit", "stand", "sit", false),
            new TransitionEdge("walk_start_stop", "stand", "walk", false),
            new TransitionEdge("walk_to_run", "walk", "run", false),
            new TransitionEdge("walk_to_run", "run", "walk", true),
            new TransitionEdge("walk_start_stop", "walk", "stand", false),
        },
        new[] { ("sit", "groom"), ("groom", "sit") });

    [Fact]
    public void Una_transicion_directa_es_un_solo_clip()
    {
        var path = Graph().FindPath("sit", "stand")!;
        Assert.Equal(new[] { "sit_to_stand" }, path.Select(e => e.Clip));
    }

    [Fact]
    public void Las_transiciones_se_encadenan_hasta_la_accion_elegida()
    {
        var path = Graph().FindPath("sit", "run")!;
        Assert.Equal(new[] { "sit_to_stand", "walk_start_stop", "walk_to_run" }, path.Select(e => e.Clip));
    }

    [Fact]
    public void La_inversa_de_una_transicion_se_marca_para_reproducirla_al_reves()
    {
        var path = Graph().FindPath("run", "walk")!;
        Assert.Single(path);
        Assert.True(path[0].Reverse);
    }

    [Fact]
    public void Un_corte_directo_no_anade_ningun_clip()
    {
        Assert.Empty(Graph().FindPath("sit", "groom")!);
        Assert.Equal(new[] { "sit_to_stand" }, Graph().FindPath("groom", "stand")!.Select(e => e.Clip));
    }

    [Fact]
    public void Sin_camino_devuelve_null_y_en_la_misma_accion_una_lista_vacia()
    {
        Assert.Null(Graph().FindPath("sit", "trot"));
        Assert.Empty(Graph().FindPath("walk", "walk")!);
    }
}
