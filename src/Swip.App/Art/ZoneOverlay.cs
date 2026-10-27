using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Swip.App.Art;

/// <summary>
/// Marcos del cuadro de dibujo, siempre visibles para comprobar que nada se deforma: las zonas extra
/// (laterales, abajo o arriba) en azul, el cuadro principal en verde y la base donde se apoya en
/// magenta (el borde de abajo del principal). Es una imagen del tamaño de la cuadrícula ampliada por un
/// factor entero, así cada línea mide exactamente 1 píxel real.
/// </summary>
public static class ZoneOverlay
{
    private static readonly uint Blue = 0xE00096FF, Green = 0xF028C846, Magenta = 0xFFFF3CDC;
    private static readonly uint CellLine = 0x5A808080;   // cuadrícula interior: gris translúcido
    private static readonly Dictionary<(string Key, int Factor), BitmapSource> Cache = new();

    /// <summary>Factor de ampliación de los marcos para una escala de pantalla (líneas de 1 px real).</summary>
    public static int FactorFor(double scale) => Math.Max(1, (int)Math.Round(scale));

    /// <param name="key">Identifica el cuadro (para la caché).</param>
    /// <param name="width">Ancho del cuadro en casillas.</param>
    /// <param name="height">Alto del cuadro en casillas.</param>
    /// <param name="extra">Zonas extra (azul).</param>
    /// <param name="main">Cuadro principal (verde); su borde de abajo es la base (magenta).</param>
    /// <param name="cellLines">
    /// Dibujar también la cuadrícula interior (una línea entre cada casilla, dentro de las zonas). Solo
    /// tiene sentido ampliado (el Visor): a tamaño real cada casilla es un píxel y las líneas lo taparían.
    /// </param>
    public static BitmapSource Get(string key, int width, int height, Int32Rect[] extra, Int32Rect main, int factor,
        bool cellLines = false)
    {
        factor = Math.Clamp(factor, 1, 8);
        key = cellLines ? key + "+casillas" : key;
        if (Cache.TryGetValue((key, factor), out var cached)) return cached;

        int w = width * factor, h = height * factor;
        var px = new uint[w * h];
        void Set(int x, int y, uint c)
        {
            if (x >= 0 && x < w && y >= 0 && y < h) px[y * w + x] = c;
        }
        // Un borde en la casilla B se dibuja en el píxel B*factor (el último, en el borde del cuadro):
        // así dos zonas vecinas comparten UNA sola línea, como en el esquema.
        int Col(int b) => Math.Min(b * factor, w - 1);
        int Row(int b) => Math.Min(b * factor, h - 1);
        void Outline(Int32Rect r, uint c)
        {
            int x0 = Col(r.X), y0 = Row(r.Y), x1 = Col(r.X + r.Width), y1 = Row(r.Y + r.Height);
            for (int x = x0; x <= x1; x++) { Set(x, y0, c); Set(x, y1, c); }
            for (int y = y0; y <= y1; y++) { Set(x0, y, c); Set(x1, y, c); }
        }

        if (cellLines)
        {
            var zones = extra.Append(main).ToArray();
            bool Inside(int cx, int cy) =>
                zones.Any(z => cx >= z.X && cx < z.X + z.Width && cy >= z.Y && cy < z.Y + z.Height);
            // Los cuatro bordes de cada casilla de dibujo (las vecinas comparten el suyo).
            for (int cy = 0; cy < height; cy++)
                for (int cx = 0; cx < width; cx++)
                {
                    if (!Inside(cx, cy)) continue;
                    int x0 = Col(cx), x1 = Col(cx + 1), y0 = Row(cy), y1 = Row(cy + 1);
                    for (int x = x0; x <= x1; x++) { Set(x, y0, CellLine); Set(x, y1, CellLine); }
                    for (int y = y0; y <= y1; y++) { Set(x0, y, CellLine); Set(x1, y, CellLine); }
                }
        }

        foreach (var zone in extra) Outline(zone, Blue);
        Outline(main, Green);   // encima del azul en los bordes compartidos
        int baseY = Row(main.Y + main.Height);
        for (int x = Col(main.X); x <= Col(main.X + main.Width); x++) Set(x, baseY, Magenta);

        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        bitmap.Freeze();
        Cache[(key, factor)] = bitmap;
        return bitmap;
    }
}
