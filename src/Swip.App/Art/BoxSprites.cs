using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Swip.Shared.Art;

namespace Swip.App.Art;

/// <summary>
/// La caja de cartón (cerrada / abierta) es una CUADRÍCULA, igual que los gatos (Assets/box, generada
/// por tools/art/build_atlas.py: 1 casilla = 1 px lógico): cuadro principal de 32x32 que la caja llena
/// entero (su borde de abajo es la base) y un margen de 16 a cada lado y por encima para las solapas y
/// el tambaleo (cuadro de 64x48). El tambaleo se hace con píxeles: la caja gira DENTRO de su cuadro (cada casilla toma el
/// color que le toca; las casillas nunca se inclinan ni se estiran) y se balancea sobre la esquina de
/// su base que queda apoyada.
/// </summary>
public static class BoxSprites
{
    /// <summary>Inclinación máxima del motor (grados).</summary>
    private const double MaxTilt = 12;

    /// <summary>Margen del cuadro alrededor del principal: columnas a cada lado y filas por encima.</summary>
    public const int Side = 16, Top = 16;

    private static BitmapSource? _closedGrid, _openGrid;
    private static int[]? _closedPx, _openPx;
    private static readonly Dictionary<(bool Open, int Degrees, int Prescale), BitmapSource> Cache = new();

    /// <summary>Cuadro de la caja sin inclinar (para medidas y zona clicable).</summary>
    public static BitmapSource Grid(bool open) =>
        open ? _openGrid ??= Load("Assets/box/box_open.png") : _closedGrid ??= Load("Assets/box/box_closed.png");

    /// <summary>Cuadro de dibujo completo (casillas = px lógicos).</summary>
    public static int FrameWidth => Grid(false).PixelWidth;
    public static int FrameHeight => Grid(false).PixelHeight;

    /// <summary>Cuadro principal (la caja para el motor): 32x32.</summary>
    public static Int32Rect Main => new(Side, Top, FrameWidth - 2 * Side, FrameHeight - Top);

    /// <summary>Zona del margen (todo el cuadro), para los marcos: el principal va dentro.</summary>
    public static Int32Rect[] ExtraZones => new[] { new Int32Rect(0, 0, FrameWidth, FrameHeight) };

    // Margen transparente superior de cada estado dentro del principal (fracción de su alto). La
    // colisión usa el borde VISIBLE (la tapa cerrada está más baja que las solapas abiertas).
    public static double TopInsetClosed => TopInset(false);
    public static double TopInsetOpen => TopInset(true);

    private static double TopInset(bool open)
    {
        var area = SpriteBounds.Opaque(Grid(open));
        double topRow = area.Y * FrameHeight;
        return Math.Clamp((topRow - Top) / Main.Height, 0, 1);
    }

    /// <summary>
    /// La caja inclinada <paramref name="degrees"/> grados (positivo = horario, se levanta el lado
    /// izquierdo) en su cuadro, ampliada píxel a píxel por <paramref name="prescale"/> (ver
    /// <see cref="PixelScale"/>).
    /// </summary>
    public static BitmapSource Get(bool open, double degrees, int prescale = 1)
    {
        int deg = (int)Math.Round(Math.Clamp(degrees, -MaxTilt, MaxTilt));
        prescale = Math.Clamp(prescale, 1, 8);
        var key = (open, deg, prescale);
        if (Cache.TryGetValue(key, out var cached)) return cached;

        int[] src = Pixels(open);
        int w = FrameWidth, h = FrameHeight;
        var cells = new int[w * h];

        // Giro en el espacio de las casillas: para cada casilla del cuadro se busca de qué casilla del
        // cuadro sin inclinar viene (giro inverso) y se copia su color tal cual. El pivote es la esquina
        // de la BASE del dibujo (la fila visible más baja; la caja abierta es más ancha arriba por las
        // solapas que abajo).
        var (footLeft, footRight, footRow) = Foot(src, w, h);
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

    private static int[] Pixels(bool open)
    {
        ref int[]? slot = ref open ? ref _openPx : ref _closedPx;
        if (slot is not null) return slot;
        var bgra = new FormatConvertedBitmap(Grid(open), PixelFormats.Bgra32, null, 0);
        var px = new int[bgra.PixelWidth * bgra.PixelHeight];
        bgra.CopyPixels(px, bgra.PixelWidth * 4, 0);
        slot = px;
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
