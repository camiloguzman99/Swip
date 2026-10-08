namespace Swip.Shared;

/// <summary>
/// Limpia el nombre que Windows da a una app (la "descripción" de su .exe) para mostrarlo. Algunas
/// apps empaquetadas la dejan con un sufijo técnico: WhatsApp se describe como "WhatsApp.Root".
/// </summary>
public static class AppNames
{
    // Sufijos técnicos que nunca forman parte del nombre que ve el usuario.
    private static readonly string[] TechnicalSuffixes = { ".Root", ".exe" };

    public static string Clean(string name)
    {
        string result = name.Trim();

        // Repetido por si hubiera más de uno ("App.Root.exe"); no deja nunca el nombre vacío.
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (string suffix in TechnicalSuffixes)
            {
                if (result.Length > suffix.Length
                    && result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    result = result[..^suffix.Length].TrimEnd();
                    changed = true;
                }
            }
        }
        return result;
    }
}
