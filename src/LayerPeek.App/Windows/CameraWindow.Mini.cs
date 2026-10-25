using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using LayerPeek.App.Cameras;
using LayerPeek.App.Cameras.Mini;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Windows;

/// <summary>
/// The mini preview with its info strip below the image (T-089): the window is the image (its height from the width and the
/// aspect ratio of the stream) plus the strip. Placing, remembering and correcting the height go through the pure functions
/// of <see cref="CameraMiniGeometry"/> (grow away from the nearest work area edge, stay on the work area).
/// </summary>
/// <remarks>
/// <para>
/// The height is computed again from the width after the user resized the window with its grips, when the first frame
/// tells the aspect ratio of the stream (or it changes) and when the strip changes its height (T-090). Placing the mini
/// preview (opening, switching to it, "Apply") uses its remembered or default <em>image</em> size and adds the strip.
/// </para>
/// <para>
/// Resizing: on Windows <c>BeginResizeDrag</c> runs the system's modal size loop and returns when the user releases the
/// grip, so the height is corrected right after it (nothing changes while dragging, so there is no flicker). Elsewhere
/// (macOS, not verified) the correction follows once the size has not changed for <see cref="ResizeSettleTime"/> after a
/// grip was pressed. A correction never causes another one (it only sets the size it computed; the computation is
/// idempotent, and a size that is already right is not set again).
/// </para>
/// </remarks>
public sealed partial class CameraWindow
{
    /// <summary>Without a modal size loop (not Windows): the resize counts as finished after this long without a size change.</summary>
    private static readonly TimeSpan ResizeSettleTime = TimeSpan.FromMilliseconds(300);

    private CameraMiniStripViewModel? _miniStrip;
    private bool _correctingMini;
    private DispatcherTimer? _resizeSettle;

    /// <summary>The window in the mini preview is at least this wide; the other modes at least <see cref="CameraWindowSize.MinWidth"/>.</summary>
    private static double MinWidthOf(CameraWindowMode mode) =>
        mode == CameraWindowMode.Mini ? CameraMiniGeometry.MinWidth : CameraWindowSize.MinWidth;

    /// <summary>The minimum size of <paramref name="mode"/> (before the window is placed in it).</summary>
    private void ApplyMinimumSize(CameraWindowMode mode) => MinWidth = MinWidthOf(mode);

    private void AttachMini(CameraViewModel viewModel)
    {
        _miniStrip = viewModel.MiniStrip;
        _miniStrip.PropertyChanged += OnMiniStripPropertyChanged;
    }

    private void DetachMini()
    {
        _resizeSettle?.Stop();
        if (_miniStrip is not null)
        {
            _miniStrip.PropertyChanged -= OnMiniStripPropertyChanged;
            _miniStrip = null;
        }
    }

    private void OnMiniStripPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CameraMiniStripViewModel.StripHeight))
        {
            CorrectMiniSize();
        }
    }

    /// <summary>The view model changed something the mini preview follows (the aspect ratio of the stream).</summary>
    private void OnMiniViewModelPropertyChanged(string? propertyName)
    {
        if (propertyName == nameof(CameraViewModel.VideoAspectRatio))
        {
            CorrectMiniSize();
        }
    }

    /// <summary>
    /// Places the mini preview for its remembered or default image placement <paramref name="image"/> (position in pixels,
    /// size in DIP): the strip is added by the growth rule, on the work area of the monitor the image is on.
    /// </summary>
    private void PlaceMini(CameraWindowPlacement image)
    {
        if (!image.IsValid || _viewModel is not { } viewModel)
        {
            return;
        }

        (double scaling, PixelBounds? area) = MonitorOf(new PixelBounds(image.X, image.Y, 1, 1));
        SetBounds(CameraMiniGeometry.FromImage(image, scaling, area, viewModel.VideoAspectRatio, viewModel.MiniStrip.StripHeight));
    }

    /// <summary>Where the mini preview is, as the image placement to remember (the inverse of <see cref="PlaceMini"/>).</summary>
    private CameraWindowPlacement MiniPlacementNow(CameraViewModel viewModel)
    {
        double strip = viewModel.MiniStrip.StripHeight;
        PixelBounds window = CurrentBounds();
        (_, PixelBounds? area) = MonitorOf(window);
        CameraWindowPlacement image = CameraMiniGeometry.ToImage(window, DesktopScaling, area, strip);
        // The size exactly as the window has it (DIP), not converted back from pixels.
        return image with { Width = ClientSize.Width, Height = Math.Max(ClientSize.Height - strip, 1) };
    }

    /// <summary>
    /// The height of the shown mini preview from its width again (aspect ratio of the stream + strip), growing away from the
    /// nearest work area edge and staying on the work area. Does nothing in the other modes, while a mode is applied, while
    /// it corrects already, or when the window is already right.
    /// </summary>
    private void CorrectMiniSize()
    {
        if (!_opened
            || _correctingMini
            || _applyingMode
            || WindowState != WindowState.Normal
            || _appliedMode != CameraWindowMode.Mini
            || _viewModel is not { IsMini: true, IsResettingPlacement: false } viewModel)
        {
            return;
        }

        double scaling = DesktopScaling;
        PixelBounds current = CurrentBounds();
        (_, PixelBounds? area) = MonitorOf(current);
        CameraWindowPlacement target = CameraMiniGeometry.Correct(current, ClientSize.Width, scaling, area, viewModel.VideoAspectRatio, viewModel.MiniStrip.StripHeight);
        // Compared in pixels: the window has whole pixels (190.5 DIP at 100 % is 191 px), so a right window is not set again.
        if (target.X == current.X && target.Y == current.Y && Pixels(target.Width) == current.Width && Pixels(target.Height) == current.Height)
        {
            return;
        }

        _correctingMini = true;
        try
        {
            SetBounds(target);
        }
        finally
        {
            _correctingMini = false;
        }

        int Pixels(double dip) => Math.Max((int)Math.Round(dip * scaling, MidpointRounding.AwayFromZero), 1);
    }

    /// <summary>The window at <paramref name="window"/> (position in pixels, client size in DIP).</summary>
    private void SetBounds(CameraWindowPlacement window)
    {
        Width = window.Width;
        Height = window.Height;
        Position = new PixelPoint(window.X, window.Y);
    }

    /// <summary>
    /// The scaling and the work area of the monitor <paramref name="window"/> is on; without monitors the window's own
    /// scaling and no work area (the window is then not moved).
    /// </summary>
    private (double Scaling, PixelBounds? WorkArea) MonitorOf(PixelBounds window)
    {
        if (CameraWindowPlanner.ScreenOf(window, ScreenCatalog.GetScreens()) is { } screen)
        {
            return (screen.Scaling, screen.UsableArea);
        }

        return (_opened ? DesktopScaling : 1.0, null);
    }

    /// <summary>
    /// A grip of the mini preview was pressed: the system resizes the window; afterwards its height follows its width
    /// (T-089 Q1, variant a).
    /// </summary>
    private void BeginMiniResize(WindowEdge edge, PointerPressedEventArgs e)
    {
        BeginResizeDrag(edge, e);
        if (OperatingSystem.IsWindows())
        {
            // The modal size loop has ended: the user released the grip.
            Dispatcher.UIThread.Post(CorrectMiniSize, DispatcherPriority.Background);
            return;
        }

        _resizeSettle ??= new DispatcherTimer(ResizeSettleTime, DispatcherPriority.Background, OnResizeSettled);
        _resizeSettle.Stop();
        _resizeSettle.Start();
    }

    /// <summary>The client size changed: while a resize without a modal loop runs, its end is a moment later.</summary>
    private void OnMiniClientSizeChanged()
    {
        if (_resizeSettle is { IsEnabled: true } settle)
        {
            settle.Stop();
            settle.Start();
        }
    }

    private void OnResizeSettled(object? sender, EventArgs e)
    {
        _resizeSettle?.Stop();
        CorrectMiniSize();
    }
}
