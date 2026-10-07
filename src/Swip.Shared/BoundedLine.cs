using System.Text;

namespace Swip.Shared;

/// <summary>
/// Lee una línea con un tope de caracteres. <see cref="TextReader.ReadLineAsync()"/> no tiene
/// límite, así que un cliente podía mandar una "línea" interminable y agotar la memoria del servicio.
/// </summary>
public static class BoundedLine
{
    /// <returns>La línea (sin salto), o null si se llegó al final sin ningún carácter.</returns>
    /// <exception cref="InvalidDataException">La línea supera <paramref name="maxChars"/>.</exception>
    public static async Task<string?> ReadAsync(TextReader reader, int maxChars, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var one = new char[1];
        while (true)
        {
            int n = await reader.ReadAsync(one.AsMemory(), ct);
            if (n == 0)
                return sb.Length > 0 ? sb.ToString() : null; // fin del flujo
            if (one[0] == '\n')
            {
                if (sb.Length > 0 && sb[^1] == '\r') sb.Length--;
                return sb.ToString();
            }
            if (sb.Length >= maxChars)
                throw new InvalidDataException($"La línea supera el máximo de {maxChars} caracteres.");
            sb.Append(one[0]);
        }
    }
}
