using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;

using PrusaTrayDiagnostic.Tests.Shared;

namespace PrusaTrayDiagnostic.App.UiTests.Infrastructure;

/// <summary>
/// Compares a window rendered by Skia with a reference PNG committed in <c>Snapshots/</c>. Deterministic: render scaling
/// 1.0, no keyboard focus (no caret, no focus adorner), no pointer over the window, the bundled Inter font only, and the
/// frame must be stable over further render ticks (no running animation). Set <c>PTD_UPDATE_SNAPSHOTS=1</c> to (re)write
/// the references instead of comparing. On a mismatch the actual frame and a diff image are written to
/// <see cref="FailureDirectory"/> (swept at the start of the next run) and the path is in the assertion message.
/// UI thread only.
/// </summary>
public static class SnapshotAssert
{
    public const string UpdateVariable = "PTD_UPDATE_SNAPSHOTS";

    /// <summary>A pixel differs when one of its channels differs by more than this (anti-aliasing noise).</summary>
    private const int ChannelTolerance = 16;

    /// <summary>
    /// The images match when at most this many pixels differ (beyond <see cref="ChannelTolerance"/>). Rendering is
    /// bit-exact between runs on one machine; the slack only absorbs isolated edge pixels on another machine. Kept far
    /// below a single changed glyph (a one-digit change of the light grey address placeholder alone is about 20 pixels),
    /// so that a changed text, a 1 px shift or a changed colour always fails.
    /// </summary>
    internal const int MaxDifferentPixels = 10;

    /// <summary>The verdict of <see cref="Matches"/> for a count of pixels beyond the channel tolerance.</summary>
    internal static bool IsWithinTolerance(int differentPixels) => differentPixels <= MaxDifferentPixels;

    /// <summary>
    /// The reservation of this assembly in %TEMP% for this worktree (the un-hashed folder of older runs is swept too).
    /// Runs in the same worktree are serialized by its lock (<see cref="HeadlessSession"/>).
    /// </summary>
    internal static ReservedTempDirectory Reserved { get; } =
        ReservedTempDirectory.ForCurrentWorktree("PrusaTrayDiagnostic.App.UiTests", legacyDirectoryName: "PrusaTrayDiagnostic.App.UiTests.Snapshots");

    /// <summary>Reserved folder for the images of failed comparisons; nothing else is written there (besides its marker).</summary>
    public static string FailureDirectory => Reserved.Path;

    /// <summary>The <c>Snapshots</c> folder of the test project in the source tree.</summary>
    public static string ReferenceDirectory { get; } =
        typeof(SnapshotAssert).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(static attribute => attribute.Key == "SnapshotDirectory").Value
        ?? throw new InvalidOperationException("The SnapshotDirectory assembly metadata is missing.");

    public static bool IsUpdating => Environment.GetEnvironmentVariable(UpdateVariable) == "1";

    /// <summary>Asserts that <paramref name="window"/> looks like the reference image <paramref name="name"/>.png.</summary>
    public static void Matches(Window window, string name)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        window.SetRenderScaling(1.0);
        window.FocusManager?.Focus(null);
        window.Settle();
        AssertOnlyBundledFont(window);

        Image actual = Capture(window);
        // A second frame after more render ticks must be identical: otherwise an animation or a caret is running.
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(10);
        Image later = Capture(window);
        if (CountDifferentPixels(actual, later, channelTolerance: 0, diff: null) != 0)
        {
            Assert.Fail($"Snapshot '{name}': the rendered frame is not stable (animation, caret or pending layout).");
        }

        string reference = Path.Combine(ReferenceDirectory, name + ".png");
        if (IsUpdating)
        {
            Directory.CreateDirectory(ReferenceDirectory);
            actual.Save(reference);
            return;
        }

        if (!File.Exists(reference))
        {
            Assert.Fail($"Snapshot '{name}': no reference image '{reference}'. Create it with {UpdateVariable}=1 dotnet test --project tests/PrusaTrayDiagnostic.App.UiTests and review it.");
        }

        Image expected = Image.Load(reference);
        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            string actualPath = WriteFailure(name, actual, diff: null);
            Assert.Fail($"Snapshot '{name}': size {actual.Width}×{actual.Height} differs from the reference {expected.Width}×{expected.Height}. Actual image: '{actualPath}'.");
        }

        byte[] diff = new byte[actual.Pixels.Length];
        int different = CountDifferentPixels(expected, actual, ChannelTolerance, diff);
        if (!IsWithinTolerance(different))
        {
            string actualPath = WriteFailure(name, actual, new Image(actual.Width, actual.Height, diff));
            Assert.Fail(
                $"Snapshot '{name}': {different} pixels differ from '{reference}' (allowed {MaxDifferentPixels}). Actual and diff images: '{actualPath}' and its .diff.png in '{FailureDirectory}'. "
                + $"If the change is intended, update the references with {UpdateVariable}=1.");
        }
    }

    /// <summary>
    /// How many pixels of two rendered windows differ by more than the channel tolerance, i.e. what <see cref="Matches"/>
    /// compares with <see cref="MaxDifferentPixels"/>. For the tests of the comparison itself.
    /// </summary>
    internal static int CountDifferentPixels(Window expected, Window actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        foreach (Window window in new[] { expected, actual })
        {
            window.SetRenderScaling(1.0);
            window.FocusManager?.Focus(null);
            window.Settle();
        }

        return CountDifferentPixels(Capture(expected), Capture(actual), ChannelTolerance, diff: null);
    }

    /// <summary>Start of a run (under the lock): removes the images of the previous run's failures and other leftovers.</summary>
    internal static async Task SweepFailureOutputAsync()
    {
        await Reserved.SweepAsync().ConfigureAwait(false);
        await Reserved.CreateAsync().ConfigureAwait(false);
    }

    /// <summary>End of a run: the folder stays only when it holds images of failures.</summary>
    internal static void RemoveEmptyFailureOutput()
    {
        if (Reserved.HoldsOnlyMarker())
        {
            Directory.Delete(FailureDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Every character the window shows must be in the bundled Inter font: anything else would be drawn with a font
    /// installed on the machine and make the snapshot machine-dependent.
    /// </summary>
    private static void AssertOnlyBundledFont(Window window)
    {
        Assert.Equal("Inter", FontManager.Current.DefaultFontFamily.Name);
        if (!FontManager.Current.TryGetGlyphTypeface(new Typeface(HeadlessTestApp.FontFamilyName), out GlyphTypeface? inter))
        {
            Assert.Fail("The bundled Inter font is not available.");
        }

        StringBuilder shown = new();
        foreach (Visual visual in window.GetSelfAndVisualDescendants())
        {
            if (visual is TextBlock { IsEffectivelyVisible: true } block)
            {
                shown.Append(block.Text);
            }
            else if (visual is TextBox { IsEffectivelyVisible: true } box)
            {
                shown.Append(box.PasswordChar == default ? box.Text : new string(box.PasswordChar, box.Text?.Length ?? 0));
                shown.Append(box.PlaceholderText);
            }
        }

        List<string> missing = [];
        foreach (Rune rune in shown.ToString().EnumerateRunes())
        {
            if (!Rune.IsWhiteSpace(rune) && !inter.CharacterToGlyphMap.TryGetGlyph(rune.Value, out _))
            {
                missing.Add($"U+{rune.Value:X4}");
            }
        }

        Assert.True(missing.Count == 0, $"Characters without a glyph in Inter (would use a system font): {string.Join(", ", missing.Distinct())}.");
    }

    private static Image Capture(Window window)
    {
        using WriteableBitmap frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("The window rendered no frame.");
        return Image.From(frame);
    }

    private static int CountDifferentPixels(Image expected, Image actual, int channelTolerance, byte[]? diff)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            return expected.Width * expected.Height;
        }

        int different = 0;
        for (int i = 0; i < actual.Pixels.Length; i += 4)
        {
            bool differs = false;
            for (int channel = 0; channel < 4; channel++)
            {
                differs |= Math.Abs(expected.Pixels[i + channel] - actual.Pixels[i + channel]) > channelTolerance;
            }

            if (differs)
            {
                different++;
            }

            if (diff is not null)
            {
                // Differences in red, the rest as a faint grey copy of the expected image.
                byte grey = (byte)(192 + (expected.Pixels[i] + expected.Pixels[i + 1] + expected.Pixels[i + 2]) / 12);
                diff[i] = differs ? (byte)0 : grey;
                diff[i + 1] = differs ? (byte)0 : grey;
                diff[i + 2] = differs ? (byte)255 : grey;
                diff[i + 3] = 255;
            }
        }

        return different;
    }

    private static string WriteFailure(string name, Image actual, Image? diff)
    {
        Directory.CreateDirectory(FailureDirectory);
        string actualPath = Path.Combine(FailureDirectory, name + ".actual.png");
        actual.Save(actualPath);
        diff?.Save(Path.Combine(FailureDirectory, name + ".diff.png"));
        return actualPath;
    }

    /// <summary>Pixels as BGRA, 4 bytes per pixel, no row padding.</summary>
    private sealed class Image(int width, int height, byte[] pixels)
    {
        public int Width { get; } = width;

        public int Height { get; } = height;

        public byte[] Pixels { get; } = pixels;

        public static Image Load(string path)
        {
            using FileStream stream = File.OpenRead(path);
            using WriteableBitmap bitmap = WriteableBitmap.Decode(stream);
            return From(bitmap);
        }

        public static Image From(WriteableBitmap bitmap)
        {
            using ILockedFramebuffer buffer = bitmap.Lock();
            int width = buffer.Size.Width;
            int height = buffer.Size.Height;
            byte[] pixels = new byte[width * height * 4];
            for (int row = 0; row < height; row++)
            {
                Marshal.Copy(buffer.Address + (row * buffer.RowBytes), pixels, row * width * 4, width * 4);
            }

            if (buffer.Format == PixelFormat.Rgba8888)
            {
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
                }
            }
            else if (buffer.Format != PixelFormat.Bgra8888)
            {
                throw new NotSupportedException($"Pixel format {buffer.Format} is not supported by the snapshot comparison.");
            }

            return new Image(width, height, pixels);
        }

        public void Save(string path)
        {
            using WriteableBitmap bitmap = new(new PixelSize(Width, Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using (ILockedFramebuffer buffer = bitmap.Lock())
            {
                for (int row = 0; row < Height; row++)
                {
                    Marshal.Copy(Pixels, row * Width * 4, buffer.Address + (row * buffer.RowBytes), Width * 4);
                }
            }

            bitmap.Save(path, new PngBitmapEncoderOptions());
        }
    }
}
