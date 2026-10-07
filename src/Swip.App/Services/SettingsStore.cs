using System.IO;
using Swip.Shared.Settings;

namespace Swip.App.Services;

/// <summary>
/// Ajustes de la app en una ubicación COMPARTIDA (%ProgramData%\Swip) para que todas las sesiones
/// del equipo compartan aspecto y posición. Si no se puede escribir ahí, cae al perfil del usuario
/// (%AppData%). La lectura/escritura segura entre procesos la hace <see cref="SettingsFile"/>.
/// </summary>
public sealed class SettingsStore
{
    private readonly SettingsFile _shared;
    private readonly SettingsFile _user;

    public SettingsStore()
    {
        string shared = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Swip");
        string user = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Swip");
        try { Directory.CreateDirectory(shared); } catch { }
        try { Directory.CreateDirectory(user); } catch { }
        SharedDirectory = shared;
        _shared = new SettingsFile(Path.Combine(shared, "settings.json"));
        _user = new SettingsFile(Path.Combine(user, "settings.json"));
    }

    /// <summary>Carpeta compartida donde vive settings.json (la que se vigila para sincronizar).</summary>
    public string SharedDirectory { get; }

    public const string SharedFileName = "settings.json";

    /// <summary>Lee los ajustes más recientes: los compartidos si existen; si no, los del usuario.</summary>
    public AppSettings Load()
    {
        if (_shared.Exists) return _shared.Load();
        if (_user.Exists) return _user.Load();
        return new AppSettings();
    }

    /// <summary>
    /// Aplica un cambio sobre lo MÁS RECIENTE del disco y lo guarda (no pisa lo que otra sesión
    /// cambiara). Devuelve los ajustes fusionados. <paramref name="mutate"/> debe ser idempotente.
    /// </summary>
    public AppSettings Update(Action<AppSettings> mutate)
    {
        try { return _shared.Update(mutate); }
        catch { /* sin permisos en la carpeta compartida: usar la del usuario */ }

        try { return _user.Update(mutate); }
        catch { /* ni eso: el cambio vale solo en memoria */ }

        var fallback = Load();
        mutate(fallback);
        return fallback;
    }
}
