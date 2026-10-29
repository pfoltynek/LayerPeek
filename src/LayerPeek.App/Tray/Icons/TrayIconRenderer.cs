using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace LayerPeek.App.Tray.Icons;

/// <summary>
/// Draws a <see cref="TrayIconAppearance"/> with the Avalonia drawing API (no reflection, no SVG library; Native AOT safe).
/// Requires the Avalonia platform to be initialized; call on the UI thread.
/// </summary>
/// <remarks>
/// Size selection on Windows (Avalonia 12.1, <c>Avalonia.Win32</c>): the notification area asks the icon for
/// 16 px × the scaling of the taskbar monitor, rounded up (16 / 20 / 24 / 32 px at 100 / 125 / 150 / 200 %). An icon
/// made from a single bitmap is handed to the shell as is and scaled by Windows; an icon made from <c>.ico</c> data is
/// searched for the closest entry, which is then used unscaled. <see cref="RenderIcon"/> therefore packs one image per
/// <see cref="TrayIconLayout.Sizes"/> into an <c>.ico</c>, largest first, so that ties (28 px at 175 %) pick the larger one.
/// </remarks>
internal static class TrayIconRenderer
{
    /// <summary>Size of the single bitmap of <see cref="RenderIcon"/> where multi-size icons are not used (neither Windows nor macOS).</summary>
    public const int SingleSize = 32;

    /// <param name="multiSize">One image per notification-area size (Windows); otherwise one 32 px bitmap.</param>
    public static WindowIcon RenderIcon(TrayIconAppearance appearance, bool multiSize)
    {
        if (!multiSize)
        {
            // The icon keeps its own copy of the pixels, so the bitmap is released right away.
            using RenderTargetBitmap bitmap = RenderBitmap(appearance, SingleSize);
            return new WindowIcon(bitmap);
        }

        using MemoryStream ico = new(RenderIco(appearance));
        return new WindowIcon(ico);
    }

    /// <summary>The <c>.ico</c> data with one PNG image per <see cref="TrayIconLayout.Sizes"/>, largest first.</summary>
    public static byte[] RenderIco(TrayIconAppearance appearance)
    {
        List<(int Size, byte[] Png)> images = [];
        foreach (int size in TrayIconLayout.Sizes.OrderDescending())
        {
            using RenderTargetBitmap bitmap = RenderBitmap(appearance, size);
            using MemoryStream png = new();
            bitmap.Save(png, new PngBitmapEncoderOptions());
            images.Add((size, png.ToArray()));
        }

        return IconFile.FromPngImages(images);
    }

    /// <summary>The icon at one of <see cref="TrayIconLayout.Sizes"/>; the caller disposes the bitmap.</summary>
    public static RenderTargetBitmap RenderBitmap(TrayIconAppearance appearance, int size)
    {
        TrayIconLayout layout = TrayIconLayout.For(size);
        RenderTargetBitmap bitmap = new(new PixelSize(size, size), new Vector(96, 96));
        try
        {
            using DrawingContext context = bitmap.CreateDrawingContext();
            Draw(context, layout, appearance);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

        return bitmap;
    }

    private static void Draw(DrawingContext context, TrayIconLayout layout, TrayIconAppearance appearance)
    {
        SolidColorBrush hotend = new(appearance.Hotend);
        foreach (PixelRect part in (PixelRect[])[layout.Fin, layout.Block, layout.Tip])
        {
            Fill(context, hotend, part);
        }

        SolidColorBrush track = new(appearance.Track);
        SolidColorBrush fill = new(appearance.Fill);
        int[] filled = layout.FilledPixels(appearance.FilledFraction);
        for (int i = 0; i < layout.Layers.Count; i++)
        {
            PixelRect layer = layout.Layers[i];
            if (filled[i] < layer.Width)
            {
                Fill(context, track, layer);
            }

            if (filled[i] > 0)
            {
                Fill(context, fill, layer.WithWidth(filled[i]));
            }
        }

        if (appearance.Badge == TrayBadge.None)
        {
            return;
        }

        if (layout.PixelBadge is { } corner)
        {
            DrawPixelBadge(context, corner, appearance.Badge, appearance.BadgeColors);
        }
        else
        {
            DrawBadge(context, layout.BadgeCentre, layout.BadgeScale, appearance.Badge, appearance.BadgeColors);
        }
    }

    /// <summary>Disc with outline and symbol, on the 32-unit design scaled by <paramref name="k"/>.</summary>
    private static void DrawBadge(DrawingContext context, Point centre, double k, TrayBadge badge, TrayBadgeColors colors)
    {
        context.DrawEllipse(new SolidColorBrush(colors.Fill), new Pen(new SolidColorBrush(colors.Outline), 1.5 * k), centre, 6.25 * k, 6.25 * k);

        SolidColorBrush symbol = new(colors.Symbol);
        Pen stroke = new(symbol, 2 * k, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        Point At(double x, double y) => new(centre.X + (x * k), centre.Y + (y * k));
        // Bar symbols snap to whole pixels: at 20 and 24 px their 2-unit width would otherwise be a blurred 1.25 or 1.5 px.
        Rect Box(double x, double y, double width, double height) => Snap(new Rect(At(x, y), new Size(width * k, height * k)));
        double radius = k >= 1 ? 0.6 * k : 0;

        switch (badge)
        {
            case TrayBadge.Busy:
                foreach (double x in (double[])[-3, 0, 3])
                {
                    context.DrawEllipse(symbol, null, At(x, 0), 1.1 * k, 1.1 * k);
                }

                break;
            case TrayBadge.Paused:
                context.DrawRectangle(symbol, null, Box(-3, -3, 2, 6));
                context.DrawRectangle(symbol, null, Box(1, -3, 2, 6));
                break;
            case TrayBadge.Finished:
                StreamGeometry check = new();
                using (StreamGeometryContext figure = check.Open())
                {
                    figure.BeginFigure(At(-3, 0.2), isFilled: false);
                    figure.LineTo(At(-0.8, 2.4));
                    figure.LineTo(At(3.2, -2.4));
                    figure.EndFigure(isClosed: false);
                }

                context.DrawGeometry(null, stroke, check);
                break;
            case TrayBadge.Attention:
                context.DrawRectangle(symbol, null, Box(-1, -4, 2, 5), radius, radius);
                context.DrawRectangle(symbol, null, Box(-1, 2, 2, 2), radius, radius);
                break;
            case TrayBadge.Error:
                context.DrawLine(stroke, At(-2.4, -2.4), At(2.4, 2.4));
                context.DrawLine(stroke, At(-2.4, 2.4), At(2.4, -2.4));
                break;
            case TrayBadge.Offline:
                context.DrawRectangle(symbol, null, Box(-3, -1, 6, 2), radius, radius);
                break;
        }
    }

    /// <summary>7 × 7 px octagon badge for 16 px: outline, fill and a symbol on whole pixels (designer's pixel art).</summary>
    private static void DrawPixelBadge(DrawingContext context, PixelPoint corner, TrayBadge badge, TrayBadgeColors colors)
    {
        int bx = corner.X;
        int by = corner.Y;
        SolidColorBrush outline = new(colors.Outline);
        Fill(context, outline, new(bx + 2, by, 3, 1));
        Fill(context, outline, new(bx + 2, by + 6, 3, 1));
        Fill(context, outline, new(bx, by + 2, 1, 3));
        Fill(context, outline, new(bx + 6, by + 2, 1, 3));
        Dots(context, outline, (bx + 1, by + 1), (bx + 5, by + 1), (bx + 1, by + 5), (bx + 5, by + 5));

        SolidColorBrush fill = new(colors.Fill);
        Fill(context, fill, new(bx + 2, by + 1, 3, 1));
        Fill(context, fill, new(bx + 1, by + 2, 5, 3));
        Fill(context, fill, new(bx + 2, by + 5, 3, 1));

        SolidColorBrush symbol = new(colors.Symbol);
        int cx = bx + 3;
        int cy = by + 3;
        switch (badge)
        {
            case TrayBadge.Busy:
                Dots(context, symbol, (cx - 2, cy), (cx, cy), (cx + 2, cy));
                break;
            case TrayBadge.Paused:
                Fill(context, symbol, new(cx - 1, cy - 1, 1, 3));
                Fill(context, symbol, new(cx + 1, cy - 1, 1, 3));
                break;
            case TrayBadge.Finished:
                Dots(context, symbol, (cx - 2, cy), (cx - 1, cy + 1), (cx, cy), (cx + 1, cy - 1), (cx + 2, cy - 2));
                break;
            case TrayBadge.Attention:
                Fill(context, symbol, new(cx, cy - 2, 1, 3));
                Fill(context, symbol, new(cx, cy + 2, 1, 1));
                break;
            case TrayBadge.Error:
                Dots(context, symbol, (cx - 1, cy - 1), (cx + 1, cy - 1), (cx, cy), (cx - 1, cy + 1), (cx + 1, cy + 1));
                break;
            case TrayBadge.Offline:
                Fill(context, symbol, new(cx - 2, cy, 5, 1));
                break;
        }
    }

    private static void Dots(DrawingContext context, IBrush brush, params ReadOnlySpan<(int X, int Y)> pixels)
    {
        foreach ((int x, int y) in pixels)
        {
            Fill(context, brush, new(x, y, 1, 1));
        }
    }

    private static void Fill(DrawingContext context, IBrush brush, PixelRect rect) =>
        context.DrawRectangle(brush, null, new Rect(rect.X, rect.Y, rect.Width, rect.Height));

    /// <summary>Rounds the edges to whole pixels, keeping at least 1 px in each direction.</summary>
    private static Rect Snap(Rect rect)
    {
        double left = Math.Round(rect.X, MidpointRounding.AwayFromZero);
        double top = Math.Round(rect.Y, MidpointRounding.AwayFromZero);
        double right = Math.Max(left + 1, Math.Round(rect.Right, MidpointRounding.AwayFromZero));
        double bottom = Math.Max(top + 1, Math.Round(rect.Bottom, MidpointRounding.AwayFromZero));
        return new Rect(left, top, right - left, bottom - top);
    }
}
