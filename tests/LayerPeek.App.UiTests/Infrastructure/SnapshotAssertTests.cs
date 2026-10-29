using System.Runtime.InteropServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace LayerPeek.App.UiTests.Infrastructure;

/// <summary>The snapshot comparison itself: its tolerance must not let small but real changes of the UI pass.</summary>
public sealed class SnapshotAssertTests(HeadlessSession session)
{
    [Fact]
    public Task IdenticalWindowsRenderIdentically() =>
        session.RunAsync(static scope =>
        {
            Window expected = Show(scope, placeholder: "192.168.1.20", margin: 16);
            Window actual = Show(scope, placeholder: "192.168.1.20", margin: 16);

            int different = SnapshotAssert.CountDifferentPixels(expected, actual);
            Assert.Equal(0, different);
            Assert.True(SnapshotAssert.IsWithinTolerance(different));
            return Task.CompletedTask;
        });

    [Fact]
    public Task OneChangedDigitOfALightPlaceholderExceedsTheTolerance() =>
        session.RunAsync(static scope =>
        {
            // The faintest text of the settings window: the grey placeholder of the address box.
            Window expected = Show(scope, placeholder: "192.168.1.20", margin: 16);
            Window actual = Show(scope, placeholder: "192.168.1.29", margin: 16);

            int different = SnapshotAssert.CountDifferentPixels(expected, actual);
            Assert.False(SnapshotAssert.IsWithinTolerance(different), $"A changed character must fail the snapshot comparison ({different} pixels differ).");
            return Task.CompletedTask;
        });

    [Fact]
    public Task OnePixelShiftExceedsTheTolerance() =>
        session.RunAsync(static scope =>
        {
            Window expected = Show(scope, placeholder: "192.168.1.20", margin: 16);
            Window actual = Show(scope, placeholder: "192.168.1.20", margin: 17);

            int different = SnapshotAssert.CountDifferentPixels(expected, actual);
            Assert.False(SnapshotAssert.IsWithinTolerance(different), $"A 1 px shift must fail the snapshot comparison ({different} pixels differ).");
            return Task.CompletedTask;
        });

    // ---- Reference set per OS (T-095) ----

    [Fact]
    public void WindowsKeepsItsReferencesInTheRootAndMacOsHasItsOwnFolder()
    {
        string root = Path.Combine("repo", "Snapshots");

        Assert.Equal(root, ReferenceSet.For(windows: true, macOS: false).DirectoryIn(root));
        Assert.Equal(Path.Combine(root, "macOS"), ReferenceSet.For(windows: false, macOS: true).DirectoryIn(root));
        // Any other OS has no references yet: its own, missing folder, never the set of another OS.
        Assert.Equal(Path.Combine(root, "linux"), ReferenceSet.For(windows: false, macOS: false).DirectoryIn(root));
    }

    [Fact]
    public void TheCurrentSetIsTheOneOfThisOs()
    {
        ReferenceSet expected = OperatingSystem.IsWindows() ? ReferenceSet.Windows : OperatingSystem.IsMacOS() ? ReferenceSet.MacOS : ReferenceSet.Linux;

        Assert.Equal(expected, ReferenceSet.Current);
        Assert.Equal(expected.DirectoryIn(SnapshotAssert.ReferenceDirectory), SnapshotAssert.CurrentReferenceDirectory);
    }

    [Fact]
    public Task UpdatingWritesOnlyTheSetOfTheCurrentOs() =>
        session.RunAsync(static _ =>
        {
            string root = UiTestDirectories.NewDirectoryPath();
            try
            {
                // An existing reference of every set, the current one included, all with the same old content.
                byte[] old = [1, 2, 3];
                Dictionary<ReferenceSet, string> references = [];
                foreach (ReferenceSet set in new[] { ReferenceSet.Windows, ReferenceSet.MacOS, ReferenceSet.Linux })
                {
                    string path = Path.Combine(set.DirectoryIn(root), "TrayIcons", "idle-16.png");
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllBytes(path, old);
                    references[set] = path;
                }

                using WriteableBitmap icon = SolidBitmap(16, 16);
                SnapshotAssert.Matches(icon, "TrayIcons/idle-16", root, updating: true);

                foreach ((ReferenceSet set, string path) in references)
                {
                    if (set == ReferenceSet.Current)
                    {
                        Assert.NotEqual(old, File.ReadAllBytes(path));
                        // The written reference is what the comparison then accepts.
                        SnapshotAssert.Matches(icon, "TrayIcons/idle-16", root, updating: false);
                    }
                    else
                    {
                        Assert.Equal(old, File.ReadAllBytes(path));
                    }
                }

                // Nothing else was written: one file per set, as before.
                Assert.Equal(3, Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count());
            }
            finally
            {
                UiTestDirectories.Delete(root);
            }

            return Task.CompletedTask;
        });

    [Fact]
    public Task AMissingReferenceOfThisOsFailsEvenWhenAnotherOsHasOne() =>
        session.RunAsync(static _ =>
        {
            string root = UiTestDirectories.NewDirectoryPath();
            try
            {
                using WriteableBitmap icon = SolidBitmap(16, 16);
                ReferenceSet other = ReferenceSet.Current == ReferenceSet.Windows ? ReferenceSet.MacOS : ReferenceSet.Windows;
                string otherReference = Path.Combine(other.DirectoryIn(root), "idle-16.png");
                Directory.CreateDirectory(Path.GetDirectoryName(otherReference)!);
                File.WriteAllBytes(otherReference, [1, 2, 3]);

                Exception failure = Assert.ThrowsAny<Exception>(() => SnapshotAssert.Matches(icon, "idle-16", root, updating: false));

                Assert.Contains($"no {ReferenceSet.Current.Name} reference image", failure.Message, StringComparison.Ordinal);
                Assert.Contains(Path.Combine(ReferenceSet.Current.DirectoryIn(root), "idle-16.png"), failure.Message, StringComparison.Ordinal);
                Assert.Contains(SnapshotAssert.UpdateVariable, failure.Message, StringComparison.Ordinal);
            }
            finally
            {
                UiTestDirectories.Delete(root);
            }

            return Task.CompletedTask;
        });

    private static WriteableBitmap SolidBitmap(int width, int height)
    {
        WriteableBitmap bitmap = new(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using ILockedFramebuffer buffer = bitmap.Lock();
        byte[] row = new byte[width * 4];
        for (int i = 0; i < row.Length; i += 4)
        {
            (row[i], row[i + 1], row[i + 2], row[i + 3]) = (0x31, 0x68, 0xFA, 0xFF);
        }

        for (int y = 0; y < height; y++)
        {
            Marshal.Copy(row, 0, buffer.Address + (y * buffer.RowBytes), row.Length);
        }

        return bitmap;
    }

    private static Window Show(UiTestScope scope, string placeholder, double margin)
    {
        Window window = scope.Track(new Window
        {
            Width = 320,
            Height = 120,
            Content = new TextBox
            {
                PlaceholderText = placeholder,
                Margin = new Avalonia.Thickness(16, margin, 16, 0),
                VerticalAlignment = VerticalAlignment.Top,
            },
        });
        window.Show();
        return window;
    }
}
