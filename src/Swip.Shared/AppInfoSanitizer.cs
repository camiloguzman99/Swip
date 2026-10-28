namespace Swip.Shared;

/// <summary>
/// Limpia la lista de apps que publica el gato de una sesión antes de guardarla: cualquier proceso del usuario puede
/// hablar con el pipe del servicio, así que nombres, títulos y porcentajes se acotan (otro gato los muestra en su
/// menú y el servicio los guarda en memoria).
/// </summary>
public static class AppInfoSanitizer
{
    public const int MaxApps = 200, MaxNameChars = 128, MaxTitleChars = 256;

    public static List<AppInfo> Clean(IEnumerable<AppInfo?>? apps)
    {
        var result = new List<AppInfo>();
        foreach (var app in apps ?? Enumerable.Empty<AppInfo?>())
        {
            if (app is null || string.IsNullOrWhiteSpace(app.ProcessName)) continue;
            result.Add(new AppInfo
            {
                ProcessName = Trim(app.ProcessName, MaxNameChars)!,
                WindowTitle = Trim(app.WindowTitle, MaxTitleChars),
                CpuPercent = Percent(app.CpuPercent),
                RamPercent = Percent(app.RamPercent),
            });
            if (result.Count == MaxApps) break;
        }
        return result;
    }

    private static string? Trim(string? text, int max)
    {
        if (text is null) return null;
        string clean = new(text.Where(c => !char.IsControl(c)).ToArray());   // sin saltos de línea ni caracteres de control
        return clean.Length <= max ? clean : clean[..max];
    }

    private static double Percent(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;
}
