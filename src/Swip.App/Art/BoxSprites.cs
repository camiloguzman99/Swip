using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Swip.App.Art;

/// <summary>
/// La caja de cartón (cerrada / abierta) es una CUADRÍCULA, igual que los gatos (Assets/box, generada
/// por tools/art/build_atlas.py: 1 casilla = 1 px lógico). Se dibuja como cuadrados vectoriales, no
/// como imagen: cuando se tambalea gira la cuadrícula entera y cada casilla sigue siendo un cuadrado
/// (inclinado, con el borde suavizado), sin remuestrear el dibujo ni deformarlo.
/// </summary>
public static class BoxSprites
{
    private static BitmapSource? _closedGrid, _openGrid;
    private static DrawingImage? _closed, _open;

    /// <summary>Cuadrícula de la caja (para medidas y zona clicable).</summary>
    public static BitmapSource Grid(bool open) =>
        open ? _openGrid ??= Load("Assets/box/box_open.png") : _closedGrid ??= Load("Assets/box/box_closed.png");

    /// <summary>Proporciones de la caja: casillas de ancho y de alto.</summary>
    public static double AspectW => Grid(false).PixelWidth;
    public static double AspectH => Grid(false).PixelHeight;

    // Margen transparente superior de cada estado (fracción del alto). La colisión debe usar el borde
    // VISIBLE (la tapa cerrada está más baja que las solapas abiertas) o el gato flota sobre la caja.
    public static double TopInsetClosed => SpriteBounds.Opaque(Grid(false)).Y;
    public static double TopInsetOpen => SpriteBounds.Opaque(Grid(true)).Y;

    /// <summary>Dibujo de la caja: un cuadrado por casilla.</summary>
    public static ImageSource Get(bool open) =>
        open ? _open ??= Cells(Grid(true)) : _closed ??= Cells(Grid(false));

    /// <summary>
    /// Convierte la cuadrícula en cuadrados vectoriales. Las casillas de un mismo color se juntan en
    /// una sola figura por fila (sin rendijas al girar) y cada color es una única geometría.
    /// </summary>
    private static DrawingImage Cells(BitmapSource grid)
    {
        var bgra = new FormatConvertedBitmap(grid, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        bgra.CopyPixels(px, stride, 0);

        var runs = new Dictionary<uint, List<Rect>>();
        for (int y = 0; y < h; y++)
        {
            int x = 0;
            while (x < w)
            {
                int i = y * stride + x * 4;
                if (px[i + 3] == 0) { x++; continue; }
                uint color = (uint)(px[i + 2] << 16 | px[i + 1] << 8 | px[i]);
                int start = x;
                while (x < w && px[y * stride + x * 4 + 3] != 0
                       && (uint)(px[y * stride + x * 4 + 2] << 16 | px[y * stride + x * 4 + 1] << 8 | px[y * stride + x * 4]) == color)
                    x++;
                // Un pelín más grande (0.03 casillas) hacia la derecha y abajo, sin salirse de la
                // cuadrícula: las figuras vecinas se solapan y al girar no asoma el fondo entre ellas.
                if (!runs.TryGetValue(color, out var list)) runs[color] = list = new List<Rect>();
                list.Add(new Rect(start, y, Math.Min(x - start + 0.03, w - start), Math.Min(1.03, h - y)));
            }
        }

        var group = new DrawingGroup();
        // Rectángulo transparente del tamaño de la cuadrícula: fija los límites del dibujo, para que
        // los márgenes transparentes cuenten y la caja no se estire al ajustarse a la imagen.
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, w, h))));
        foreach (var (color, rects) in runs)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                foreach (var r in rects)
                {
                    ctx.BeginFigure(r.TopLeft, isFilled: true, isClosed: true);
                    ctx.PolyLineTo(new[] { r.TopRight, r.BottomRight, r.BottomLeft }, isStroked: false, isSmoothJoin: false);
                }
            }
            geometry.Freeze();
            var brush = new SolidColorBrush(Color.FromRgb((byte)(color >> 16), (byte)(color >> 8), (byte)color));
            brush.Freeze();
            group.Children.Add(new GeometryDrawing(brush, null, geometry));
        }
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
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
