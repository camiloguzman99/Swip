using Swip.Shared;

namespace Swip.Tests;

public class AppsCacheTests
{
    private static AppInfo App(string name) => new() { ProcessName = name };

    private sealed class Clock
    {
        public DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        public void Advance(TimeSpan t) => Now += t;
    }

    [Fact]
    public void Sesion_sin_publicaciones_es_Missing()
    {
        var cache = new AppsCache();
        Assert.Equal(CacheLookup.Missing, cache.TryGet(4, sessionIsActive: false, out var apps));
        Assert.Empty(apps);
    }

    [Fact]
    public void Devuelve_lo_publicado()
    {
        var cache = new AppsCache();
        cache.Publish(1, new[] { App("Edge"), App("Outlook") }, sessionIsActive: true);

        Assert.Equal(CacheLookup.Hit, cache.TryGet(1, true, out var apps));
        Assert.Equal(2, apps.Count);
    }

    // El caso real de los logs: la sesión 4, en segundo plano, publicaba siempre 0 apps.
    [Fact]
    public void Lista_vacia_desde_segundo_plano_no_pisa_la_ultima_conocida()
    {
        var cache = new AppsCache();
        cache.Publish(4, new[] { App("Edge"), App("Claude") }, sessionIsActive: true);   // cuando estaba en pantalla

        bool kept = cache.Publish(4, Array.Empty<AppInfo>(), sessionIsActive: false);    // ya en 2º plano

        Assert.True(kept);
        cache.TryGet(4, false, out var apps);
        Assert.Equal(2, apps.Count);
    }

    [Fact]
    public void Lista_vacia_de_la_sesion_en_pantalla_es_real_y_se_guarda()
    {
        var cache = new AppsCache();
        cache.Publish(1, new[] { App("Edge") }, sessionIsActive: true);

        bool kept = cache.Publish(1, Array.Empty<AppInfo>(), sessionIsActive: true);     // cerró todo

        Assert.False(kept);
        cache.TryGet(1, true, out var apps);
        Assert.Empty(apps);
    }

    [Fact]
    public void Segundo_plano_con_apps_nuevas_si_actualiza()
    {
        var cache = new AppsCache();
        cache.Publish(4, new[] { App("Edge") }, true);

        cache.Publish(4, new[] { App("Edge"), App("Word") }, sessionIsActive: false);

        cache.TryGet(4, false, out var apps);
        Assert.Equal(2, apps.Count);
    }

    [Fact]
    public void La_sesion_en_pantalla_caduca_si_su_gato_deja_de_publicar()
    {
        var clock = new Clock();
        var cache = new AppsCache(TimeSpan.FromSeconds(60), () => clock.Now);
        cache.Publish(1, new[] { App("Edge") }, true);

        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(CacheLookup.Hit, cache.TryGet(1, true, out _));

        clock.Advance(TimeSpan.FromSeconds(40));
        Assert.Equal(CacheLookup.Stale, cache.TryGet(1, true, out var apps));
        Assert.Empty(apps);
    }

    [Fact]
    public void Una_sesion_en_segundo_plano_no_caduca_aunque_pasen_horas()
    {
        var clock = new Clock();
        var cache = new AppsCache(TimeSpan.FromSeconds(60), () => clock.Now);
        cache.Publish(4, new[] { App("Edge") }, true);

        clock.Advance(TimeSpan.FromHours(5));

        Assert.Equal(CacheLookup.Hit, cache.TryGet(4, sessionIsActive: false, out var apps));
        Assert.Single(apps);
    }

    [Fact]
    public void Prune_olvida_las_sesiones_que_ya_no_existen()
    {
        var cache = new AppsCache();
        cache.Publish(1, new[] { App("a") }, true);
        cache.Publish(4, new[] { App("b") }, false);

        cache.Prune(new HashSet<int> { 1 });

        Assert.Equal(1, cache.Count);
        Assert.Equal(CacheLookup.Missing, cache.TryGet(4, false, out _));
    }
}
