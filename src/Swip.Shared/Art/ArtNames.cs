using System.Text.RegularExpressions;

namespace Swip.Shared.Art;

/// <summary>
/// Nombres de los archivos de arte retocado (%ProgramData%\Swip\art\&lt;gato&gt;\&lt;clip&gt;_&lt;cuadro&gt;.png). La carpeta
/// la puede escribir cualquier usuario, así que los nombres que se usan para construir rutas se validan antes:
/// solo minúsculas, dígitos y guion bajo (nada de separadores ni «..»).
/// </summary>
public static partial class ArtNames
{
    public const int MaxFrame = 999;

    [GeneratedRegex("^[a-z0-9_]{1,48}$")]
    private static partial Regex SafeName();

    public static bool IsSafe(string? name) => name is not null && SafeName().IsMatch(name);

    /// <summary>Nombre del archivo de un cuadro retocado, o null si el clip o el cuadro no son válidos.</summary>
    public static string? FrameFile(string? clip, int frame) =>
        IsSafe(clip) && frame is >= 0 and <= MaxFrame ? $"{clip}_{frame}.png" : null;

    /// <summary>Lo contrario: clip y cuadro de un nombre de archivo, o null si no tiene la forma esperada.</summary>
    public static (string Clip, int Frame)? ParseFrameFile(string fileName)
    {
        if (!fileName.EndsWith(".png", StringComparison.Ordinal)) return null;
        string stem = fileName[..^4];
        int cut = stem.LastIndexOf('_');
        if (cut <= 0 || !int.TryParse(stem[(cut + 1)..], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int frame))
            return null;
        string clip = stem[..cut];
        return IsSafe(clip) && frame <= MaxFrame ? (clip, frame) : null;
    }
}
