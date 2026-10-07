namespace Swip.Shared;

/// <summary>
/// Registro de diagnóstico con tamaño acotado: al llegar al máximo el archivo pasa a "<c>.1</c>"
/// (sustituyendo al anterior), así que ocupa como mucho ~2× el límite. Seguro entre hilos y
/// "best effort": un fallo al escribir el log nunca rompe la app.
/// </summary>
public sealed class SwipLog
{
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _last = new();

    public SwipLog(string path, long maxBytes = 256 * 1024)
    {
        _path = path;
        _maxBytes = maxBytes;
    }

    public void Write(string message)
    {
        lock (_gate)
        {
            try
            {
                string? dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                RotateIfNeeded();
                File.AppendAllText(_path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch { /* el log es best-effort */ }
        }
    }

    /// <summary>
    /// Escribe solo si el mensaje cambió desde la última vez con la misma <paramref name="key"/>.
    /// Evita llenar el log con líneas idénticas de tareas periódicas. Devuelve si escribió.
    /// </summary>
    public bool WriteOnChange(string key, string message)
    {
        lock (_gate)
        {
            if (_last.TryGetValue(key, out var previous) && previous == message) return false;
            _last[key] = message;
        }
        Write(message);
        return true;
    }

    private void RotateIfNeeded()
    {
        var info = new FileInfo(_path);
        if (info.Exists && info.Length >= _maxBytes)
            File.Move(_path, _path + ".1", overwrite: true);
    }
}
