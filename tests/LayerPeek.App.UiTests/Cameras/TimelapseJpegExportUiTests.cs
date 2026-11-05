using System.Diagnostics;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Cameras.Recording;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.UiTests.Printers;
using LayerPeek.Camera.FFmpeg;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Cameras.Timelapse;
using LayerPeek.Core.Images;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// T-165: the JPEG encoder of the export (Avalonia / Skia, variant A1) on the headless platform, and the whole export of a real
/// timelapse: FFmpeg reads the MP4 written from the recorded Buddy3D key frames, Avalonia writes the JPEG files.
/// </summary>
public sealed class TimelapseJpegExportUiTests(HeadlessSession session) : IDisposable
{
    private const uint Red = 0xFFFF0000;
    private const uint Blue = 0xFF0000FF;

    private readonly string _folder = Directory.CreateTempSubdirectory("LayerPeek.TimelapseJpegExportUiTests.").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>BGRA of one color, rows <paramref name="stride"/> bytes apart.</summary>
    private static byte[] Solid(int width, int height, int stride, uint argb)
    {
        byte[] pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * stride) + (x * 4);
                (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = ((byte)argb, (byte)(argb >> 8), (byte)(argb >> 16), 255);
            }
        }

        return pixels;
    }

    private static (int Width, int Height, uint Center) Decode(byte[] jpeg)
    {
        Assert.Equal(PrinterImageFormat.Jpeg, PrinterImageFormats.Detect(jpeg));
        (int w, int h, uint[] pixels) = UiTestImages.Read(jpeg);
        return (w, h, pixels[(h / 2 * w) + (w / 2)]);
    }

    [Fact]
    public Task OneEncoderWritesEveryFrameOfASequenceInItsSizeAlsoWhenTheSizeOrTheRowsChange() =>
        session.RunAsync(async _ =>
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            List<byte[]> jpegs = await Task.Run(() =>
            {
                using ICameraJpegSequenceEncoder encoder = AvaloniaJpegSequenceEncoderFactory.Instance.Create(CameraTimelapseJpegExporter.DefaultQuality);
                List<byte[]> result = [];
                foreach ((int width, int height, int stride, uint color) in ((int, int, int, uint)[])[(64, 36, 256, Red), (64, 36, 256, Blue), (32, 18, (32 * 4) + 64, Red), (64, 36, 256, Blue)])
                {
                    using MemoryStream jpeg = new();
                    encoder.Encode(new CameraFrame(width, height, stride, Solid(width, height, stride, color)), jpeg);
                    result.Add(jpeg.ToArray());
                }

                return result;
            }, ct);

            Assert.Collection(
                jpegs,
                static j => Assert.True(Decode(j) is (64, 36, var c) && UiTestImages.IsNear(c, Red, 24)),
                static j => Assert.True(Decode(j) is (64, 36, var c) && UiTestImages.IsNear(c, Blue, 24)),
                static j => Assert.True(Decode(j) is (32, 18, var c) && UiTestImages.IsNear(c, Red, 24)),
                static j => Assert.True(Decode(j) is (64, 36, var c) && UiTestImages.IsNear(c, Blue, 24)));
            // The same frame gives the same file: nothing of the previous frame stays in the reused bitmap.
            Assert.Equal(jpegs[1], jpegs[3]);
        });

    [Fact]
    public void InvalidQualitiesAndADisposedEncoderAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(static () => AvaloniaJpegSequenceEncoderFactory.Instance.Create(0));
        Assert.Throws<ArgumentOutOfRangeException>(static () => AvaloniaJpegSequenceEncoderFactory.Instance.Create(101));
        ICameraJpegSequenceEncoder encoder = AvaloniaJpegSequenceEncoderFactory.Instance.Create(85);
        encoder.Dispose();
        Assert.Throws<ObjectDisposedException>(() => encoder.Encode(new CameraFrame(1, 1, 4, new byte[4]), Stream.Null));
    }

    [Fact]
    public Task ATimelapseBecomesOneJpegPerFrameInOrderAndInTheCameraSize() =>
        session.RunAsync(async _ =>
        {
            Assert.SkipUnless(File.Exists(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "avformat-63.dll" : "libavformat.63.dylib")), "The FFmpeg libraries are not next to the tests.");
            CancellationToken ct = TestContext.Current.CancellationToken;
            int[] order = [0, 1, 0, 1, 1, 0, 0, 1, 0, 1, 0, 1];
            string timelapse = await WriteTimelapseAsync(Path.Combine(_folder, "2026-11-04_14-05-09_Benchy_timelapse.mp4"), order, ct);
            await using CameraTimelapseJpegExporter exporter = new(new FFmpegCameraFeedFactory(), AvaloniaJpegSequenceEncoderFactory.Instance, NullLogger<CameraTimelapseJpegExporter>.Instance);
            TaskCompletionSource<CameraTimelapseExportResult> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            exporter.Completed += (_, result) => completed.TrySetResult(result);
            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            Stopwatch watch = Stopwatch.StartNew();

            Assert.True(exporter.Start(timelapse, "mk4"));
            CameraTimelapseExportResult result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(60), ct);
            watch.Stop();
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

            Assert.True(result.Succeeded, result.Problem.ToString());
            Assert.Equal(order.Length, result.Frames);
            string[] files = [.. Directory.GetFiles(result.OutputFolder!).Order(StringComparer.Ordinal)];
            Assert.Equal([.. Enumerable.Range(1, order.Length).Select(static n => $"{n:0000}.jpg")], files.Select(Path.GetFileName));
            byte[][] jpegs = [.. files.Select(File.ReadAllBytes)];
            Assert.All(jpegs, static jpeg => Assert.Equal((1280, 720), (UiTestImages.Read(jpeg).Width, UiTestImages.Read(jpeg).Height)));
            // The same key frame gives the same file, so the order of the files is the order of the timelapse.
            Assert.NotEqual(jpegs[0], jpegs[1]);
            for (int n = 0; n < order.Length; n++)
            {
                Assert.Equal(jpegs[order[n]], jpegs[n]);
            }

            long size = jpegs.Sum(static j => (long)j.Length) / jpegs.Length;
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"{order.Length} frames 1280×720 in {watch.ElapsedMilliseconds} ms ({watch.Elapsed.TotalMilliseconds / order.Length:F1} ms per frame incl. counting and opening), {size / 1024} kB per JPEG, {allocated / 1024} kB managed allocations in total.");
        });

    /// <summary>A timelapse of the recorded clip's key frames in the given order, as the timelapse recorder writes it (T-158).</summary>
    private static async Task<string> WriteTimelapseAsync(string path, int[] order, CancellationToken ct)
    {
        Uri clip = new(Path.Combine(AppContext.BaseDirectory, "Fixtures", "buddy3d.h264"));
        List<byte[]> keyFrames = [];
        ICameraFeed feed = new FFmpegCameraFeedFactory().Create(clip);
        await using (feed.ConfigureAwait(false))
        {
            await foreach (CameraStreamItem item in feed.ReadAsync(new CameraStreamDemand(frames: false, packets: true), ct).ConfigureAwait(false))
            {
                if (item.Kind == CameraStreamItemKind.Packet && item.Packet.IsKeyFrame)
                {
                    keyFrames.Add(item.Packet.Data.ToArray());
                }
            }
        }

        CameraStreamInfo info = new(CameraVideoCodec.H264, 0, 0, CameraTimeBase.Rtp90kHz, ReadOnlyMemory<byte>.Empty);
        using ICameraRecordingWriter writer = new FFmpegCameraRecordingWriterFactory().Create(path, info, keyFrames[order[0]]);
        for (int n = 0; n < order.Length; n++)
        {
            writer.Write(keyFrames[order[n]], n * 3000L, n * 3000L, 3000, isKeyFrame: true);
        }

        writer.Complete();
        return path;
    }
}
