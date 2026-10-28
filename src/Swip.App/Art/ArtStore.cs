using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Swip.Shared.Art;
using Swip.Shared.Engine;

namespace Swip.App.Art;

/// <summary>
/// Los retoques que se hacen desde el Visor, en una carpeta COMPARTIDA por todas las sesiones
/// (%ProgramData%\Swip\art; si no se puede escribir ahí, en %AppData%\Swip\art):
///   timing.json                      tiempos de los clips (<see cref="ClipTimingFile"/>)
///   &lt;gato&gt;\&lt;clip&gt;_&lt;cuadro&gt;.png   un cuadro retocado, con la cuadrícula entera (p. ej. 180x130 casillas)
/// Cualquier usuario puede escribir en esa carpeta, así que todo lo que se lee se valida: nombres seguros
/// (<see cref="ArtNames"/>), tamaño de archivo máximo y dimensiones exactas de la cuadrícula; lo demás se ignora.
/// Las escrituras son atómicas (archivo temporal y renombrado).
/// </summary>
public sealed class ArtStore
{
    private const long MaxPngBytes = 512 * 1024;
    private const long MaxTimingBytes = 256 * 1024;
    private const int MaxFramesPerSkin = 2000;   // los cuadros de todos los clips son unos cientos

    public string Directory { get; }

    public ArtStore()
    {
        string shared = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Swip", "art");
        string user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Swip", "art");
        Directory = Writable(shared) ? shared : user;
        try { System.IO.Directory.CreateDirectory(Directory); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static bool Writable(string dir)
    {
        try
        {
            System.IO.Directory.CreateDirectory(dir);
            string probe = Path.Combine(dir, $".probe{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string TimingPath => Path.Combine(Directory, "timing.json");

    private string SkinDir(string skin) => Path.Combine(Directory, CatSkins.Normalize(skin));

    // ---------------------------------------------------------------- tiempos

    public Dictionary<string, ClipTimingEntry> LoadTiming(ClipLibrary library)
    {
        try
        {
            var info = new FileInfo(TimingPath);
            if (!info.Exists || info.Length > MaxTimingBytes) return new();
            return ClipTimingFile.Parse(File.ReadAllText(TimingPath), library);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void SaveTiming(IReadOnlyDictionary<string, ClipTimingEntry> entries)
    {
        System.IO.Directory.CreateDirectory(Directory);
        WriteAtomically(TimingPath, tmp => File.WriteAllText(tmp, ClipTimingFile.Serialize(entries)));
    }

    // ---------------------------------------------------------------- cuadros retocados

    /// <summary>
    /// Huella barata de los cuadros retocados (cuántos hay, cuándo se cambió el último y cuánto ocupan): si no cambia, no
    /// hace falta volver a leer ni decodificar ningún PNG.
    /// </summary>
    public string FramesSignature()
    {
        long count = 0, bytes = 0, newest = 0;
        try
        {
            foreach (string skin in CatSkins.All)
            {
                string dir = SkinDir(skin);
                if (!System.IO.Directory.Exists(dir)) continue;
                foreach (var info in new DirectoryInfo(dir).EnumerateFiles("*.png").Take(MaxFramesPerSkin))
                {
                    count++;
                    bytes += info.Length;
                    newest = Math.Max(newest, info.LastWriteTimeUtc.Ticks);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return $"{count}:{bytes}:{newest}";
    }

    /// <summary>Todos los cuadros retocados de un gato: (clip, cuadro) → píxeles BGRA de width x height.</summary>
    public Dictionary<(string Clip, int Frame), byte[]> LoadFrames(string skin, int width, int height)
    {
        var result = new Dictionary<(string, int), byte[]>();
        string dir = SkinDir(skin);
        if (!System.IO.Directory.Exists(dir)) return result;
        try
        {
            foreach (string path in System.IO.Directory.EnumerateFiles(dir, "*.png").Take(MaxFramesPerSkin))
            {
                if (ArtNames.ParseFrameFile(Path.GetFileName(path)) is not { } key) continue;
                if (ReadPng(path, width, height) is { } pixels) result[key] = pixels;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return result;
    }

    public void SaveFrame(string skin, string clip, int frame, byte[] bgra, int width, int height)
    {
        string? name = ArtNames.FrameFile(clip, frame)
            ?? throw new ArgumentException("Nombre de clip o cuadro no válido.");
        if (bgra.Length != width * height * 4) throw new ArgumentException("Tamaño de imagen no válido.");
        string dir = SkinDir(skin);
        System.IO.Directory.CreateDirectory(dir);
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        WriteAtomically(Path.Combine(dir, name), tmp =>
        {
            using var fs = File.Create(tmp);
            encoder.Save(fs);
        });
    }

    public void DeleteFrame(string skin, string clip, int frame)
    {
        if (ArtNames.FrameFile(clip, frame) is not { } name) return;
        try { File.Delete(Path.Combine(SkinDir(skin), name)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static byte[]? ReadPng(string path, int width, int height)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaxPngBytes) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var decoder = new PngBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count != 1) return null;
            var frame = decoder.Frames[0];
            if (frame.PixelWidth != width || frame.PixelHeight != height) return null;   // solo la cuadrícula entera
            var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[width * height * 4];
            bgra.CopyPixels(pixels, width * 4, 0);
            return pixels;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                      or FileFormatException or InvalidOperationException)
        {
            return null; // archivo roto o que no es un PNG: se ignora
        }
    }

    private static void WriteAtomically(string path, Action<string> write)
    {
        string tmp = $"{path}.{Environment.ProcessId}.tmp";
        try
        {
            write(tmp);
            File.Move(tmp, path, overwrite: true);   // los lectores ven el archivo viejo o el nuevo, nunca a medias
        }
        catch
        {
            try { File.Delete(tmp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }
}
