using System.Text.Json;

namespace Swip.Shared.Settings;

/// <summary>
/// Un archivo de ajustes que VARIOS procesos (una sesión de Windows cada uno) leen y escriben a la
/// vez. Antes cada gato cargaba el archivo al arrancar y lo guardaba entero, de modo que el último
/// en guardar pisaba los cambios del otro. Ahora toda escritura es "leer lo último → aplicar solo
/// el cambio → escribir", dentro de un bloqueo entre procesos y con escritura atómica.
/// </summary>
public sealed class SettingsFile
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;
    private readonly string _lockPath;

    /// <param name="lockTimeout">Cuánto esperar el bloqueo antes de seguir sin él (mejor eso que perder el cambio).</param>
    public SettingsFile(string path, TimeSpan? lockTimeout = null)
    {
        _path = path;
        _lockPath = path + ".lock";
        LockTimeout = lockTimeout ?? TimeSpan.FromSeconds(3);
    }

    public string Path => _path;
    public TimeSpan LockTimeout { get; }
    public bool Exists => File.Exists(_path);

    /// <summary>Lee los ajustes. Si el archivo falta o está corrupto devuelve los valores por defecto.</summary>
    public AppSettings Load()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!File.Exists(_path)) return new AppSettings();
                // FileShare.Delete: otro proceso puede estar reemplazando el archivo (escritura atómica).
                using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs);
                return JsonSerializer.Deserialize<AppSettings>(reader.ReadToEnd(), Options)
                       ?? new AppSettings();
            }
            catch (IOException) { Thread.Sleep(20 * (attempt + 1)); } // en uso un instante: reintentar
            catch (JsonException) { return new AppSettings(); }
            catch (UnauthorizedAccessException) { return new AppSettings(); }
        }
        return new AppSettings();
    }

    /// <summary>
    /// Aplica <paramref name="mutate"/> sobre los ajustes MÁS RECIENTES del disco y los guarda.
    /// Devuelve el resultado fusionado (incluye los cambios que otras sesiones hicieran antes).
    /// Lanza excepción si no se puede escribir (p. ej. sin permisos en la carpeta).
    /// </summary>
    public AppSettings Update(Action<AppSettings> mutate)
    {
        string? dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var gate = AcquireLock();
        var current = Load();
        mutate(current);
        WriteAtomically(current);
        return current;
    }

    private void WriteAtomically(AppSettings settings)
    {
        string json = JsonSerializer.Serialize(settings, Options);
        string tmp = $"{_path}.{Environment.ProcessId}.tmp";
        try
        {
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true); // los lectores ven el archivo viejo o el nuevo, nunca a medias
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* best effort */ }
            throw;
        }
    }

    /// <summary>
    /// Bloqueo entre procesos con un archivo abierto sin compartir (FileShare.None): no necesita
    /// permisos especiales de objetos globales, solo escribir en la misma carpeta.
    /// </summary>
    private IDisposable AcquireLock()
    {
        var deadline = DateTime.UtcNow + LockTimeout;
        while (true)
        {
            try
            {
                return new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(15); // otro proceso está escribiendo
            }
            catch (IOException) { return NoLock.Instance; }                 // se agotó la espera: seguir sin bloqueo
            catch (UnauthorizedAccessException) { return NoLock.Instance; } // lock creado por otro usuario sin acceso
        }
    }

    private sealed class NoLock : IDisposable
    {
        public static readonly NoLock Instance = new();
        public void Dispose() { }
    }
}
