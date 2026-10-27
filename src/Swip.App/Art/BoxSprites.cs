using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Swip.Shared.Art;
using Swip.Shared.Engine;

namespace Swip.App.Art;

/// <summary>
/// La caja de cartón, dibujada casilla a casilla (tools/art/box_art.py; Assets/box, 1 casilla = 1 px
/// lógico). Cuadro de 64x48: principal de 32x32 que la caja llena (su borde de abajo es la base) y un
/// margen de 16 a cada lado y por encima para las solapas, el aplastamiento y el tambaleo.
/// Fotogramas: <c>lid_0..lid_6</c> (abrir las cuatro solapas) y <c>squash_1..squash_4</c> (aplastada
/// por el peso), cada uno con su capa delantera y la trasera (la solapa trasera: se dibuja DETRÁS del
/// gato que está dentro). El tambaleo se hace con píxeles: la caja gira dentro de su cuadro (cada casilla toma el
/// color que le toca; las casillas nunca se inclinan ni se estiran) sobre la esquina de su base que
/// queda apoyada.
/// </summary>
public static class BoxSprites
{
    /// <summary>Margen del cuadro alrededor del principal: columnas a cada lado y filas por encima.</summary>
    public const int Side = 16, Top = 16;

    private static readonly Dictionary<string, BitmapSource> Grids = new();
    private static readonly Dictionary<string, int[]> Pixels = new();
    private static readonly Dictionary<(string Frame, bool Back, int Degrees, int Prescale), BitmapSource> Cache = new();

    /// <summary>Nombre del fotograma para el estado de la caja: solapas en movimiento o aplastada.</summary>
    public static string FrameName(int lid, int squash) =>
        lid > 0 ? $"lid_{lid}" : squash > 0 ? $"squash_{Math.Min(squash, BoxBody.MaxSquash)}" : "lid_0";

    /// <summary>Cuadro sin inclinar de un fotograma (para medidas y zona clicable).</summary>
    public static BitmapSource Grid(string frame, bool back = false)
    {
        string key = back ? frame + "_back" : frame;
        if (!Grids.TryGetValue(key, out var bmp)) Grids[key] = bmp = Load($"Assets/box/box_{key}.png");
        return bmp;
    }

    /// <summary>Cuadro de dibujo completo (casillas = px lógicos).</summary>
    public static int FrameWidth => Grid("lid_0").PixelWidth;
    public static int FrameHeight => Grid("lid_0").PixelHeight;

    /// <summary>Cuadro principal (la caja para el motor): 32x32.</summary>
    public static Int32Rect Main => new(Side, Top, FrameWidth - 2 * Side, FrameHeight - Top);

    /// <summary>Zona del margen (todo el cuadro), para los marcos: el principal va dentro.</summary>
    public static Int32Rect[] ExtraZones => new[] { new Int32Rect(0, 0, FrameWidth, FrameHeight) };

    // Margen superior de la caja dentro del principal (fracción de su alto), cerrada y abierta: la caja
    // llena el principal, así que es 0; el aplastamiento lo suma el motor (BoxBody.VisibleTop).
    public static double TopInsetClosed => TopInset("lid_0");
    public static double TopInsetOpen => TopInset($"lid_{BoxBody.LidFrames - 1}");

    private static double TopInset(string frame)
    {
        // Solo cuenta el cuerpo, no las solapas: la fila visible más alta DENTRO del principal.
        var px = PixelsOf(frame, false);
        int w = FrameWidth;
        for (int y = Top; y < FrameHeight; y++)
            for (int x = Side; x < w - Side; x++)
                if ((px[y * w + x] >>> 24) != 0) return (y - Top) / (double)Main.Height;
        return 0;
    }

    /// <summary>
    /// Una capa de un fotograma inclinada <paramref name="degrees"/> grados (positivo = horario, se
    /// levanta el lado izquierdo), ampliada píxel a píxel por <paramref name="prescale"/> (ver
    /// <see cref="PixelScale"/>). Las dos capas usan el mismo pivote: la esquina de la base de la delantera.
    /// </summary>
    public static BitmapSource Get(string frame, bool back, double degrees, int prescale = 1)
    {
        int deg = (int)Math.Round(Math.Clamp(degrees, -BoxBody.MaxWobble, BoxBody.MaxWobble));
        prescale = Math.Clamp(prescale, 1, 8);
        var key = (frame, back, deg, prescale);
        if (Cache.TryGetValue(key, out var cached)) return cached;

        int[] src = PixelsOf(frame, back);
        int w = FrameWidth, h = FrameHeight;
        var cells = new int[w * h];

        // Giro en el espacio de las casillas: para cada casilla del cuadro se busca de qué casilla del
        // cuadro sin inclinar viene (giro inverso) y se copia su color tal cual.
        var (footLeft, footRight, footRow) = Foot(PixelsOf(frame, false), w, h);
        double a = deg * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
        double pivotX = deg >= 0 ? footRight : footLeft, pivotY = footRow;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double dx = x + 0.5 - pivotX, dy = y + 0.5 - pivotY;
                double sx = dx * cos + dy * sin + pivotX;   // giro inverso
                double sy = -dx * sin + dy * cos + pivotY;
                int ix = (int)Math.Floor(sx), iy = (int)Math.Floor(sy);
                if (ix >= 0 && ix < w && iy >= 0 && iy < h)
                    cells[y * w + x] = src[iy * w + ix];
            }
        }

        // Ampliación entera (casilla = bloque de prescale x prescale píxeles).
        int pw = w * prescale, ph = h * prescale;
        var output = new int[pw * ph];
        for (int y = 0; y < ph; y++)
            for (int x = 0; x < pw; x++)
                output[y * pw + x] = cells[(y / prescale) * w + x / prescale];
        var bitmap = BitmapSource.Create(pw, ph, 96, 96, PixelFormats.Bgra32, null, output, pw * 4);
        bitmap.Freeze();
        Cache[key] = bitmap;
        return bitmap;
    }

    /// <summary>Extremos izquierdo y derecho (bordes de casilla) de la fila visible más baja, y su borde inferior.</summary>
    private static (double Left, double Right, double Bottom) Foot(int[] px, int w, int h)
    {
        for (int y = h - 1; y >= 0; y--)
        {
            int first = -1, last = -1;
            for (int x = 0; x < w; x++)
            {
                if ((px[y * w + x] >>> 24) == 0) continue;
                if (first < 0) first = x;
                last = x;
            }
            if (first >= 0) return (first, last + 1, y + 1);
        }
        return (0, w, h);
    }

    private static int[] PixelsOf(string frame, bool back)
    {
        string key = back ? frame + "_back" : frame;
        if (Pixels.TryGetValue(key, out var px)) return px;
        var bgra = new FormatConvertedBitmap(Grid(frame, back), PixelFormats.Bgra32, null, 0);
        px = new int[bgra.PixelWidth * bgra.PixelHeight];
        bgra.CopyPixels(px, bgra.PixelWidth * 4, 0);
        Pixels[key] = px;
        return px;
    }

    private static BitmapImage Load(string relativePath)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.UriSource = new Uri($"pack://application:,,,/{relativePath}", UriKind.Absolute);
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }
}
