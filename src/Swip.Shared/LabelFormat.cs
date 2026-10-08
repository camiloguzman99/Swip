namespace Swip.Shared;

/// <summary>Cómo se reparte el nombre del usuario en el rótulo que sale encima del gato.</summary>
public static class LabelFormat
{
    /// <summary>
    /// Una palabra, en una línea; dos palabras, una por línea ("Concepto General" →
    /// "Concepto" / "General"). Con tres o más, dos líneas lo más parejas posible. Lo decide esta
    /// función y no el ajuste automático del texto, que no garantiza cortar entre palabras.
    /// </summary>
    public static string Split(string name)
    {
        var words = name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries); // separa por espacios, tabuladores...
        if (words.Length == 0) return string.Empty;
        if (words.Length == 1) return words[0];
        if (words.Length == 2) return words[0] + "\n" + words[1];

        // Tres o más: el corte que deja la línea más larga lo más corta posible.
        int bestCut = 1, bestLongest = int.MaxValue;
        for (int cut = 1; cut < words.Length; cut++)
        {
            int longest = Math.Max(Join(words, 0, cut).Length, Join(words, cut, words.Length).Length);
            if (longest < bestLongest) { bestLongest = longest; bestCut = cut; }
        }
        return Join(words, 0, bestCut) + "\n" + Join(words, bestCut, words.Length);
    }

    private static string Join(string[] words, int from, int to) =>
        string.Join(" ", words, from, to - from);
}
