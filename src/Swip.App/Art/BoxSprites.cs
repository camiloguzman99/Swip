using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Swip.Shared.Art;

namespace Swip.App.Art;

/// <summary>
/// La caja de cartón (cerrada / abierta) es una CUADRÍCULA, igual que los gatos (Assets/box, generada
/// por tools/art/build_atlas.py: 1 casilla = 1 px lógico). El tambaleo también se hace con píxeles:
/// la caja se gira DENTRO de su cuadrícula (cada casilla toma el color que le toca, las casillas nunca
/// se inclinan ni se estiran) y se balancea sobre la esquina de abajo que queda apoyada.
/// El cuadro de dibujo tiene margen alrededor para que quepa la caja inclinada.
/// </summary>
public static class BoxSprites
{
    /// <summary>Inclinación máxima del motor (grados): define el margen del cuadro de dibujo.</summary>
    private const double MaxTilt = 12;

    private static BitmapSource? _closedGrid, _openGrid;
    private static int[]? _closedPx, _openPx;
    private static readonly Dictionary<(bool Open, int Degrees, int Prescale), BitmapSource> Cache = new();

    /// <summary>Cuadrícula de la caja recta (para medidas y zona clicable).</summary>
    public static BitmapSource Grid(bool open) =>
        open ? _openGrid ??= Load("Assets/box/box_open.png") : _closedGrid ??= Load("Assets/box/box_closed.png");

    /// <summary>Casillas de la caja (ancho y alto de la caja recta).</summary>
    public static int Columns => Grid(false).PixelWidth;
    public static int Rows => Grid(false).PixelHeight;

    /// <summary>Margen del cuadro de dibujo a cada lado y por encima, para la caja inclinada.</summary>
    public static int PadX => (int)Math.Ceiling(Rows * Math.Sin(MaxTilt * Math.PI / 180));
    public static int PadTop => (int)Math.Ceiling(Columns * Math.Sin(MaxTilt * Math.PI / 180));

    /// <summary>Cuadro de dibujo completo de la caja (casillas = px lógicos).</summary>
    public static int FrameWidth => Columns + 2 * PadX;
    public static int FrameHeight => Rows + PadTop;

    // Margen transparente superior de cada estado (fracción del alto). La colisión debe usar el borde
    // VISIBLE (la tapa cerrada está más baja que las solapas abiertas) o el gato flota sobre la caja.
    public static double TopInsetClosed => SpriteBounds.Opaque(Grid(false)).Y;
    public static double TopInsetOpen => SpriteBounds.Opaque(Grid(true)).Y;

    /// <summary>
    /// La caja inclinada <paramref name="degrees"/> grados (positivo = horario, se levanta el lado
    /// izquierdo), dibujada en su cuadro de <see cref="FrameWidth"/>x<see cref="FrameHeight"/> casillas
    /// y ampliada píxel a píxel por <paramref name="prescale"/> (ver <see cref="PixelScale"/>).
    /// </summary>
    public static BitmapSource Get(bool open, double degrees, int prescale = 1)
    {
        int deg = (int)Math.Round(Math.Clamp(degrees, -MaxTilt, MaxTilt));
        prescale = Math.Clamp(prescale, 1, 8);
        var key = (open, deg, prescale);
        if (Cache.TryGetValue(key, out var cached)) return cached;

        int[] src = Pixels(open);
        int cols = Columns, rows = Rows, w = FrameWidth, h = FrameHeight;
        var cells = new int[w * h];

        // Giro en el espacio de las casillas: para cada casilla del cuadro se busca de qué casilla de la
        // caja recta viene (giro inverso) y se copia su color tal cual. Pivote: la esquina de abajo que
        // se queda en el suelo (la derecha si se levanta la izquierda, y al revés).
        // El pivote es la esquina de la BASE del dibujo: la fila de abajo, de su primera a su última
        // casilla visible (los dibujos tienen margen transparente, y la caja abierta es más ancha
        // arriba por las solapas que abajo).
        var (footLeft, footRight, footRow) = Foot(src, cols, rows);
        double a = deg * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
        double pivotX = deg >= 0 ? footRight : footLeft;   // en coordenadas de la caja recta
        double pivotY = footRow;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // Centro de la casilla del cuadro, en coordenadas de la caja recta respecto al pivote.
                double dx = x + 0.5 - PadX - pivotX, dy = y + 0.5 - PadTop - pivotY;
                double sx = dx * cos + dy * sin + pivotX;   // giro inverso
                double sy = -dx * sin + dy * cos + pivotY;
                int ix = (int)Math.Floor(sx), iy = (int)Math.Floor(sy);
                if (ix >= 0 && ix < cols && iy >= 0 && iy < rows)
                    cells[y * w + x] = src[iy * cols + ix];
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
    private static (double Left, double Right, double Bottom) Foot(int[] px, int cols, int rows)
    {
        for (int y = rows - 1; y >= 0; y--)
        {
            int first = -1, last = -1;
            for (int x = 0; x < cols; x++)
            {
                if ((px[y * cols + x] >>> 24) == 0) continue;
                if (first < 0) first = x;
                last = x;
            }
            if (first >= 0) return (first, last + 1, y + 1);
        }
        return (0, cols, rows);
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
