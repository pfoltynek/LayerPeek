using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace PrusaTrayDiagnostic.App.Tray.Icons;

/// <summary>
/// Draws a <see cref="TrayIconAppearance"/> with the Avalonia drawing API (no reflection, Native AOT safe).
/// Requires the Avalonia platform to be initialized; call on the UI thread.
/// </summary>
internal static class TrayIconRenderer
{
    /// <summary>
    /// Bitmap size in pixels. The shell scales it to the notification area size (16 px at 100 %, 32 px at 200 %).
    /// </summary>
    public const int Size = 32;

    private const double Center = Size / 2.0;
    private static readonly Point CenterPoint = new(Center, Center);
    private static readonly Color TrackColor = Color.FromArgb(0x80, 0x9E, 0x9E, 0x9E);

    public static WindowIcon Render(TrayIconAppearance appearance)
    {
        // The icon keeps its own copy of the pixels, so the bitmap is released right away.
        using RenderTargetBitmap bitmap = new(new PixelSize(Size, Size), new Vector(96, 96));
        using (DrawingContext context = bitmap.CreateDrawingContext())
        {
            Draw(context, appearance);
        }

        return new WindowIcon(bitmap);
    }

    private static void Draw(DrawingContext context, TrayIconAppearance appearance)
    {
        SolidColorBrush fill = new(appearance.Fill);
        SolidColorBrush outline = new(appearance.Outline);

        switch (appearance.Shape)
        {
            case TrayIconShape.ProgressRing:
                DrawProgressRing(context, fill, outline, appearance);
                break;
            case TrayIconShape.CrossedRing:
                DrawCrossedRing(context, fill, outline);
                break;
            default:
                context.DrawEllipse(fill, new Pen(outline, 2), CenterPoint, 12, 12);
                break;
        }
    }

    private static void DrawProgressRing(DrawingContext context, IBrush fill, IBrush outline, TrayIconAppearance appearance)
    {
        const double radius = 11.5;
        const double thickness = 6;

        // Track: the full ring in a neutral semi-transparent grey, so that the remaining part of the print stays
        // visible and the colour of the state (blue / yellow) comes only from the arc and the centre dot.
        context.DrawEllipse(null, new Pen(new SolidColorBrush(TrackColor), thickness), CenterPoint, radius, radius);

        double sweep = Math.Clamp(appearance.ProgressSweepDegrees, 0, 360);
        if (sweep >= 360)
        {
            context.DrawEllipse(null, new Pen(fill, thickness), CenterPoint, radius, radius);
        }
        else if (sweep > 0)
        {
            context.DrawGeometry(null, new Pen(fill, thickness), CreateArc(radius, sweep));
        }

        context.DrawEllipse(fill, new Pen(outline, 1.5), CenterPoint, 5, 5);
    }

    private static void DrawCrossedRing(DrawingContext context, IBrush fill, IBrush outline)
    {
        const double radius = 11;
        Point from = new(7, 25);
        Point to = new(25, 7);

        // Dark under-stroke then light stroke: readable on both light and dark taskbars.
        Pen under = new(outline, 5, lineCap: PenLineCap.Round);
        Pen over = new(fill, 3, lineCap: PenLineCap.Round);
        context.DrawEllipse(null, under, CenterPoint, radius, radius);
        context.DrawLine(under, from, to);
        context.DrawEllipse(null, over, CenterPoint, radius, radius);
        context.DrawLine(over, from, to);
    }

    /// <summary>Clockwise arc starting at 12 o'clock.</summary>
    private static StreamGeometry CreateArc(double radius, double sweepDegrees)
    {
        double angle = sweepDegrees * Math.PI / 180;
        Point start = new(Center, Center - radius);
        Point end = new(Center + (radius * Math.Sin(angle)), Center - (radius * Math.Cos(angle)));

        StreamGeometry geometry = new();
        using (StreamGeometryContext figure = geometry.Open())
        {
            figure.BeginFigure(start, isFilled: false);
            figure.ArcTo(end, new Size(radius, radius), rotationAngle: 0, isLargeArc: sweepDegrees > 180, SweepDirection.Clockwise);
            figure.EndFigure(isClosed: false);
        }

        return geometry;
    }
}
