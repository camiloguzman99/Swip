using System.IO;
using System.Text.Json;
using Swip.App.Models;

namespace Swip.App.Services;

/// <summary>
/// Carga y guarda <see cref="AppSettings"/>. Se guarda en una ubicación COMPARTIDA
/// (%ProgramData%\Swip) para que todas las sesiones del equipo compartan aspecto y posición.
/// Si no se puede escribir ahí, cae al perfil del usuario (%AppData%).
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _sharedPath;
    private readonly string _userPath;

    public SettingsStore()
    {
        string shared = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Swip");
        string user = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Swip");
        try { Directory.CreateDirectory(shared); } catch { }
        try { Directory.CreateDirectory(user); } catch { }
        _sharedPath = Path.Combine(shared, "settings.json");
        _userPath = Path.Combine(user, "settings.json");
    }

    public AppSettings Load()
    {
        // Preferir la configuración compartida; si no, la del usuario.
        foreach (var path in new[] { _sharedPath, _userPath })
        {
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new();
            }
            catch { /* corrupto: probar el siguiente */ }
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        string json = JsonSerializer.Serialize(settings, Options);
        // Intentar la ubicación compartida; si falla (permisos), usar la del usuario.
        try { File.WriteAllText(_sharedPath, json); return; }
        catch { }
        try { File.WriteAllText(_userPath, json); } catch { }
    }
}
