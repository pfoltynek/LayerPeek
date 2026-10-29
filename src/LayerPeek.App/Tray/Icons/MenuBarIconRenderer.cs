using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace LayerPeek.App.Tray.Icons;

/// <summary>
/// Draws a <see cref="MenuBarIconAppearance"/> with the Avalonia drawing API (no reflection, no SVG library; Native AOT
/// safe): the geometry of <c>docs/design/macos-tray-icons.md</c> on an 18 × 18 pt grid, rendered at @2x into one
/// 36 × 36 px bitmap. Requires the Avalonia platform to be initialized; call on the UI thread.
/// </summary>
/// <remarks>
/// <c>Avalonia.Native</c> (12.1, <c>trayicon.mm</c>) hands the icon to the status item as one image and sizes it to
/// <c>floor(menu font × 4/3)</c> = 18 pt, keeping the aspect ratio: 36 px fill it 1 : 1 on Retina (@2x) displays. Every
/// edge of the hotend and the layers lies on whole points (even pixels), so the 2 : 1 reduction macOS does on @1x
/// displays stays sharp; only the diagonals and dots of the symbols are anti-aliased (decision 2026-10-29: one bitmap,
/// a multi-resolution image only if a real Mac shows it blurred).
/// </remarks>
internal static class MenuBarIconRenderer
{
    /// <summary>Side of the icon in points.</summary>
    public const int PointSize = 18;

    /// <summary>Pixels per point of the bitmap (@2x).</summary>
    public const int Scale = 2;

    /// <summary>Side of the bitmap in pixels.</summary>
    public const int PixelSize = PointSize * Scale;

    /// <summary>Length of one layer in points; the three layers together are 48 pt long.</summary>
    public const int LayerLength = 16;

    /// <summary>Heatsink fin (pt).</summary>
    public static readonly Rect Fin = new(2, 1, 4, 1);

    /// <summary>Heater block (pt).</summary>
    public static readonly Rect Block = new(1, 3, 6, 3);

    /// <summary>The three layers (pt), bottom (filled first) to top.</summary>
    public static readonly IReadOnlyList<Rect> Layers = [new(1, 15, LayerLength, 2), new(1, 12, LayerLength, 2), new(1, 9, LayerLength, 2)];

    /// <summary>Box of the state symbol (pt), top right; kept clear of the hotend (2 pt) and the top layer (1 pt).</summary>
    public static readonly Rect SymbolBox = new(9, 1, 8, 7);

    private static readonly Point SlashStart = new(1.5, 1);
    private static readonly Point SlashEnd = new(16.5, 17);

    /// <summary>The icon as a <see cref="WindowIcon"/> (one 36 px bitmap).</summary>
    public static WindowIcon RenderIcon(MenuBarIconAppearance appearance)
    {
        // The icon keeps its own copy of the pixels, so the bitmap is released right away.
        using RenderTargetBitmap bitmap = RenderBitmap(appearance);
        return new WindowIcon(bitmap);
    }

    /// <summary>The 36 × 36 px bitmap; the caller disposes it.</summary>
    public static RenderTargetBitmap RenderBitmap(MenuBarIconAppearance appearance)
    {
        RenderTargetBitmap bitmap = new(new PixelSize(PixelSize, PixelSize), new Vector(96, 96));
        try
        {
            using DrawingContext context = bitmap.CreateDrawingContext();
            using (context.PushTransform(Matrix.CreateScale(Scale, Scale)))
            {
                Draw(context, appearance);
            }
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

        return bitmap;
    }

    /// <summary>
    /// How many whole points of each layer (bottom first) are filled: <c>floor(48 × fraction)</c>, so the layers are full
    /// only at 100 % and every 5 % step changes at least 2 pt.
    /// </summary>
    public static int[] FilledPoints(double fraction)
    {
        int remaining = (int)Math.Floor((Layers.Count * LayerLength * Math.Clamp(fraction, 0, 1)) + 1e-9);
        int[] filled = new int[Layers.Count];
        for (int i = 0; i < filled.Length; i++)
        {
            filled[i] = Math.Min(remaining, LayerLength);
            remaining -= filled[i];
        }

        return filled;
    }

    private static void Draw(DrawingContext context, MenuBarIconAppearance appearance)
    {
        if (appearance.Symbol != MenuBarSymbol.Slash)
        {
            DrawMotif(context, appearance);
            DrawSymbol(context, appearance.Symbol);
            return;
        }

        // A gap of 3.6 pt is cut out of the motif around the 1.5 pt slash, so that the slash reads as separate.
        Geometry gap = new LineGeometry(SlashStart, SlashEnd).GetWidenedGeometry(new Pen(Brushes.Black, 3.6, lineCap: PenLineCap.Round));
        CombinedGeometry clearance = new(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, PointSize, PointSize)), gap);
        using (context.PushGeometryClip(clearance))
        {
            DrawMotif(context, appearance);
        }

        context.DrawLine(new Pen(Ink(MenuBarIconAppearance.Opaque), 1.5, lineCap: PenLineCap.Round), SlashStart, SlashEnd);
    }

    private static void DrawMotif(DrawingContext context, MenuBarIconAppearance appearance)
    {
        SolidColorBrush ink = Ink(appearance.Ink);
        context.DrawRectangle(ink, null, Fin);
        context.DrawRectangle(ink, null, Block);
        context.DrawGeometry(ink, null, Polygon((3, 6), (5, 6), (4.5, 7), (3.5, 7)));

        // Never overlapping: the track covers only the unfilled part, otherwise the alphas would add up.
        SolidColorBrush track = Ink(appearance.Track);
        int[] filled = FilledPoints(appearance.FilledFraction);
        for (int i = 0; i < Layers.Count; i++)
        {
            Rect layer = Layers[i];
            if (filled[i] > 0)
            {
                context.DrawRectangle(ink, null, layer.WithWidth(filled[i]));
            }

            if (filled[i] < LayerLength)
            {
                context.DrawRectangle(track, null, new Rect(layer.X + filled[i], layer.Y, LayerLength - filled[i], layer.Height));
            }
        }
    }

    private static void DrawSymbol(DrawingContext context, MenuBarSymbol symbol)
    {
        SolidColorBrush ink = Ink(MenuBarIconAppearance.Opaque);
        switch (symbol)
        {
            case MenuBarSymbol.Busy:
                foreach (double x in (double[])[10, 13, 16])
                {
                    context.DrawEllipse(ink, null, new Point(x, 5), 1.2, 1.2);
                }

                break;
            case MenuBarSymbol.Printing:
                context.DrawGeometry(ink, null, Polygon((10.5, 1), (16.5, 4.5), (10.5, 8)));
                break;
            case MenuBarSymbol.Paused:
                context.DrawRectangle(ink, null, new Rect(10, 1, 2, 7));
                context.DrawRectangle(ink, null, new Rect(14, 1, 2, 7));
                break;
            case MenuBarSymbol.Finished:
                StreamGeometry check = new();
                using (StreamGeometryContext figure = check.Open())
                {
                    figure.BeginFigure(new Point(9.9, 4.6), isFilled: false);
                    figure.LineTo(new Point(12.2, 6.9));
                    figure.LineTo(new Point(16.2, 2.1));
                    figure.EndFigure(isClosed: false);
                }

                context.DrawGeometry(null, new Pen(ink, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), check);
                break;
            case MenuBarSymbol.Attention:
                context.DrawRectangle(ink, null, new Rect(12, 1, 2, 4.5), 0.4, 0.4);
                context.DrawRectangle(ink, null, new Rect(12, 6, 2, 2), 0.4, 0.4);
                break;
            case MenuBarSymbol.Error:
                Pen cross = new(ink, 1.7, lineCap: PenLineCap.Round);
                context.DrawLine(cross, new Point(10.3, 1.8), new Point(15.7, 7.2));
                context.DrawLine(cross, new Point(10.3, 7.2), new Point(15.7, 1.8));
                break;
        }
    }

    /// <summary>Black with the given alpha: a template image carries no colour of its own.</summary>
    private static SolidColorBrush Ink(byte alpha) => new(Color.FromArgb(alpha, 0, 0, 0));

    private static StreamGeometry Polygon(params ReadOnlySpan<(double X, double Y)> points)
    {
        StreamGeometry geometry = new();
        using StreamGeometryContext figure = geometry.Open();
        figure.BeginFigure(new Point(points[0].X, points[0].Y), isFilled: true);
        foreach ((double x, double y) in points[1..])
        {
            figure.LineTo(new Point(x, y));
        }

        figure.EndFigure(isClosed: true);
        return geometry;
    }
}
