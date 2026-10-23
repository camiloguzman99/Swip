namespace Swip.Shared.Art;

/// <summary>
/// Una paleta de gato. Se dibuja una sola "piel" (los dibujos naranja o gris) y cada paleta la
/// retiñe al cargar: así hay colores nuevos sin redibujar nada. Las paletas sin rampas usan la
/// piel tal cual (el arte original).
/// </summary>
/// <param name="Skin">Hoja de sprites de la que parte ("orange" o "gray").</param>
/// <param name="FurRamp">Colores del pelaje, de oscuro a claro (0xRRGGBB); null = sin retocar.</param>
/// <param name="BellyRamp">Colores de la barriga/hocico, de oscuro a claro; null = sin retocar.</param>
/// <param name="Swatch">Color de la muestra del menú (0xRRGGBB).</param>
public sealed record CatPalette(string Id, string Name, string Skin, uint[]? FurRamp, uint[]? BellyRamp, uint Swatch)
{
    public bool Recolors => FurRamp is not null || BellyRamp is not null;
}

public static class CatPalettes
{
    public static readonly IReadOnlyList<CatPalette> All = new CatPalette[]
    {
        new("orange", "Naranja", "orange", null, null, 0xF2913B),
        new("gray", "Atigrado", "gray", null, null, 0x8B8178),
        new("black", "Negro", "orange",
            new uint[] { 0x121016, 0x1F1C25, 0x302C38, 0x46414F }, new uint[] { 0x3A3542, 0x4F4958, 0x655F70 }, 0x2A2731),
        new("white", "Blanco", "orange",
            new uint[] { 0xA9A39C, 0xCCC6BF, 0xE6E2DC, 0xF8F6F2 }, new uint[] { 0xEDE9E3, 0xF7F4EF, 0xFFFFFF }, 0xF1EEE9),
        new("cream", "Crema", "orange",
            new uint[] { 0xA87745, 0xCB9A63, 0xE4BC86, 0xF4D9AC }, new uint[] { 0xF1E2C9, 0xF8EEDC, 0xFFF9EF }, 0xE8C48F),
        new("chocolate", "Chocolate", "orange",
            new uint[] { 0x3A2114, 0x573320, 0x75472C, 0x925E3F }, new uint[] { 0xAE876A, 0xC9A586, 0xDEC3A6 }, 0x6E4329),
    };

    /// <summary>Colores de piel válidos (las paletas que no retiñen).</summary>
    public static IEnumerable<string> Skins => All.Where(p => !p.Recolors).Select(p => p.Skin).Distinct();

    public static CatPalette Get(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    /// <summary>Id válido (cualquier valor desconocido o antiguo pasa a naranja).</summary>
    public static string Normalize(string? id) => Get(id).Id;
}

/// <summary>Qué parte del dibujo es un píxel, a efectos de retintado.</summary>
public enum PixelRole { Other, Fur, Belly }

/// <summary>
/// Retintado por rampas. Calibrado sobre la piel naranja: pelaje = naranja saturado; barriga =
/// crema claro poco saturado; todo lo demás (contorno oscuro, ojos, nariz rosa, juguete) se deja.
/// </summary>
public static class Recolor
{
    // Umbrales (tono en grados, saturación y brillo 0..1).
    public const double FurMaxHue = 45, FurMinSat = 0.42, FurMinValue = 0.30;
    public const double BellyMinHue = 15, BellyMaxHue = 60, BellyMinSat = 0.06, BellyMinValue = 0.72;

    public static PixelRole Classify(byte r, byte g, byte b)
    {
        var (h, s, v) = ToHsv(r, g, b);
        bool orangeHue = h <= FurMaxHue || h >= 355;
        if (orangeHue && s >= FurMinSat && v >= FurMinValue) return PixelRole.Fur;
        if (h >= BellyMinHue && h <= BellyMaxHue && s >= BellyMinSat && s < FurMinSat && v >= BellyMinValue)
            return PixelRole.Belly;
        return PixelRole.Other;
    }

    /// <summary>Retiñe en el sitio un búfer BGRA (4 bytes por píxel). Conserva el alfa.</summary>
    public static void Apply(Span<byte> bgra, CatPalette palette)
    {
        if (!palette.Recolors) return;
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            if (bgra[i + 3] == 0) continue;
            byte b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
            var role = Classify(r, g, b);
            uint[]? ramp = role switch
            {
                PixelRole.Fur => palette.FurRamp,
                PixelRole.Belly => palette.BellyRamp,
                _ => null,
            };
            if (ramp is null) continue;

            var (_, _, v) = ToHsv(r, g, b);
            double t = role == PixelRole.Fur
                ? (v - FurMinValue) / (1 - FurMinValue)
                : (v - BellyMinValue) / (1 - BellyMinValue);
            uint c = Sample(ramp, Math.Clamp(t, 0, 1));
            bgra[i + 2] = (byte)(c >> 16);
            bgra[i + 1] = (byte)(c >> 8);
            bgra[i] = (byte)c;
        }
    }

    /// <summary>Color de la rampa en la posición t (0 = oscuro, 1 = claro), interpolado.</summary>
    public static uint Sample(uint[] ramp, double t)
    {
        if (ramp.Length == 1) return ramp[0];
        double pos = t * (ramp.Length - 1);
        int i = Math.Min((int)pos, ramp.Length - 2);
        double f = pos - i;
        uint a = ramp[i], z = ramp[i + 1];
        byte Lerp(int shift) => (byte)Math.Round(((a >> shift) & 0xFF) * (1 - f) + ((z >> shift) & 0xFF) * f);
        return ((uint)Lerp(16) << 16) | ((uint)Lerp(8) << 8) | Lerp(0);
    }

    public static (double H, double S, double V) ToHsv(byte r, byte g, byte b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf)), min = Math.Min(rf, Math.Min(gf, bf));
        double d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == rf) h = 60 * (((gf - bf) / d) % 6);
            else if (max == gf) h = 60 * ((bf - rf) / d + 2);
            else h = 60 * ((rf - gf) / d + 4);
        }
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }
}
