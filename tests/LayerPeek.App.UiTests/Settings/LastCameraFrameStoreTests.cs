using Avalonia.Media;
using Avalonia.Media.Imaging;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Settings;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.UiTests.Settings;

/// <summary>
/// The last camera frame of the bed region editor (T-154): one small JPEG per printer in the cache folder, named by a hash
/// of the id, read back as a bitmap, deleted with the printer; a broken file is no frame. Needs Avalonia's imaging.
/// </summary>
public sealed class LastCameraFrameStoreTests(HeadlessSession session) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "LayerPeek.LastCameraFrameStoreTests." + Guid.NewGuid().ToString("N"), "last-frames");

    public void Dispose()
    {
        string root = Path.GetDirectoryName(_folder)!;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public Task AFrameIsStoredSmallAndReadBack() =>
        session.RunAsync(async _ =>
        {
            LayerPeek.App.Tests.Tray.ListLogger<LastCameraFrameStore> logger = new();
            LastCameraFrameStore store = new(_folder, logger);
            CameraImage image = TestCameraImages.Quadrants(1920, 1080, 0xFFE05A1E, 0xFF2E7D32, 0xFF1565C0, 0xFFF9A825);

            store.Save("Core-One", image);
            await store.Pending;

            string path = store.PathOf("core-one");
            Assert.True(File.Exists(path), string.Join(" | ", logger.Entries.Select(static e => e.Exception?.ToString())));
            Assert.Equal(".jpg", Path.GetExtension(path));
            // The id never appears on disk, and ids differing only in case are one printer.
            Assert.DoesNotContain("core", Path.GetFileName(path), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(store.PathOf("Core-One"), path);
            Assert.Empty(Directory.GetFiles(_folder, "*.partial"));
            IImage? loaded = await store.LoadAsync("CORE-ONE", CancellationToken.None);
            Bitmap bitmap = Assert.IsAssignableFrom<Bitmap>(loaded);
            Assert.Equal(new Avalonia.PixelSize(LastCameraFrameStore.MaxWidth, LastCameraFrameStore.MaxHeight), bitmap.PixelSize);
            bitmap.Dispose();

            store.Delete("core-one");
            await store.Pending;
            Assert.False(File.Exists(path));
            Assert.Null(await store.LoadAsync("core-one", CancellationToken.None));
        });

    [Fact]
    public Task ASmallFrameKeepsItsSizeAndABrokenFileIsNoFrame() =>
        session.RunAsync(async _ =>
        {
            LastCameraFrameStore store = new(_folder, NullLogger<LastCameraFrameStore>.Instance);

            store.Save("mk4", TestCameraImages.Solid(320, 240, 0xFF2E7D32));
            await store.Pending;
            using (Bitmap small = Assert.IsAssignableFrom<Bitmap>(await store.LoadAsync("mk4", CancellationToken.None)))
            {
                Assert.Equal(new Avalonia.PixelSize(320, 240), small.PixelSize);
            }

            await File.WriteAllBytesAsync(store.PathOf("xl"), [1, 2, 3], TestContext.Current.CancellationToken);
            Assert.Null(await store.LoadAsync("xl", CancellationToken.None));
        });

    [Fact]
    public void TheStoredSizeKeepsTheAspectRatioWithinTheBounds()
    {
        Assert.Equal(new Avalonia.PixelSize(640, 360), LastCameraFrameStore.StoredSize(1280, 720));
        Assert.Equal(new Avalonia.PixelSize(480, 360), LastCameraFrameStore.StoredSize(640, 480));
        Assert.Equal(new Avalonia.PixelSize(100, 50), LastCameraFrameStore.StoredSize(100, 50));
        Assert.Equal(new Avalonia.PixelSize(640, 274), LastCameraFrameStore.StoredSize(2560, 1097));
    }

    [Fact]
    public void DownscalingAveragesTheCoveredPixels()
    {
        // 4 × 2 → 2 × 1: each target pixel is the mean of a 2 × 2 block.
        byte[] source =
        [
            0, 0, 0, 255,   100, 100, 100, 255,   10, 20, 30, 255,   10, 20, 30, 255,
            200, 200, 200, 255,   100, 100, 100, 255,   30, 40, 50, 255,   30, 40, 50, 255,
        ];
        byte[] target = new byte[2 * 1 * 4];

        LastCameraFrameStore.Downscale(source, 4, 2, target, 2, 1);

        Assert.Equal([100, 100, 100, 255, 20, 30, 40, 255], target);
    }
}
