using System.Security.Cryptography;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Cameras.Timelapse;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// T-165: the JPEG export reads a real timelapse (fragmented MP4 written by the recording writer of T-158 from the fixture's
/// key frames) through FFmpeg's MP4 demuxer; the encoder is a stand-in that records size and pixels of every frame (the real
/// JPEG encoder of the App is tested with Avalonia in the UI tests). Also a truncated and a garbage file, and cancelling.
/// </summary>
public sealed class FFmpegTimelapseJpegExportTests : IDisposable
{
    private readonly string _folder = TestDirectories.CreateDirectory();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => TestDirectories.Delete(_folder);

    [FFmpegFact]
    public async Task EveryFrameOfATimelapseIsExportedInOrderAndInItsSize()
    {
        // The fixture's two key frames alternately: the order of the exported frames is visible in their pixels.
        string timelapse = await WriteTimelapseAsync("2026-11-04_14-05-09_Benchy_timelapse.mp4", [0, 1, 0, 0, 1, 1, 0]);
        PixelDigests encoders = new();

        CameraTimelapseExportResult result = await ExportAsync(timelapse, encoders);

        Assert.True(result.Succeeded, result.Problem.ToString());
        Assert.Equal(7, result.Frames);
        Assert.Equal(Path.Combine(_folder, "2026-11-04_14-05-09_Benchy_timelapse_jpg"), result.OutputFolder);
        Assert.Equal([.. Enumerable.Range(1, 7).Select(static n => $"{n:0000}.jpg")], Directory.GetFiles(result.OutputFolder!).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        (int Width, int Height, string Digest)[] frames = [.. Enumerable.Range(1, 7).Select(n => PixelDigests.Read(Path.Combine(result.OutputFolder!, $"{n:0000}.jpg")))];
        Assert.All(frames, static f => Assert.Equal((TestFixtures.Buddy3DWidth, TestFixtures.Buddy3DHeight), (f.Width, f.Height)));
        string a = frames[0].Digest;
        string b = frames[1].Digest;
        Assert.NotEqual(a, b);
        Assert.Equal([a, b, a, a, b, b, a], frames.Select(static f => f.Digest));
        Assert.Equal(["2026-11-04_14-05-09_Benchy_timelapse.mp4", "2026-11-04_14-05-09_Benchy_timelapse_jpg"], Directory.GetFileSystemEntries(_folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [FFmpegFact]
    public async Task ATimelapseCutOffByACrashExportsTheFramesBeforeTheCut()
    {
        string complete = await WriteTimelapseAsync("2026-11-04_14-05-09_timelapse.mp4", [0, 1, 0, 1, 0, 1, 0, 1]);
        string truncated = Path.Combine(_folder, "2026-11-04_15-00-00_timelapse.mp4");
        byte[] bytes = await File.ReadAllBytesAsync(complete, Ct);
        await File.WriteAllBytesAsync(truncated, bytes[..(int)(bytes.Length * 0.6)], Ct);

        CameraTimelapseExportResult result = await ExportAsync(truncated, new PixelDigests());

        // Never a crash: the frames of the fragments before the cut (measured: 4 of 8 at 60 %).
        TestContext.Current.TestOutputHelper?.WriteLine($"{result.Problem}, {result.Frames} frames of 8.");
        Assert.True(result.Succeeded, result.Problem.ToString());
        Assert.InRange(result.Frames, 1, 7);
        Assert.Equal(result.Frames, Directory.GetFiles(result.OutputFolder!).Length);
        Assert.DoesNotContain(Directory.GetDirectories(_folder), static d => Path.GetFileName(d).StartsWith('.'));
    }

    [FFmpegFact]
    public async Task AFileThatIsNoVideoFailsWithoutLeftovers()
    {
        string garbage = Path.Combine(_folder, "2026-11-04_14-05-09_timelapse.mp4");
        byte[] noise = new byte[64 * 1024];
        new Random(165).NextBytes(noise);
        await File.WriteAllBytesAsync(garbage, noise, Ct);

        CameraTimelapseExportResult result = await ExportAsync(garbage, new PixelDigests());

        Assert.Equal(CameraTimelapseExportProblem.Unreadable, result.Problem);
        Assert.Equal([garbage], Directory.GetFileSystemEntries(_folder));
    }

    [FFmpegFact]
    public async Task AnEmptyFileFailsWithoutLeftovers()
    {
        string empty = Path.Combine(_folder, "2026-11-04_14-05-09_timelapse.mp4");
        await File.WriteAllBytesAsync(empty, [], Ct);

        CameraTimelapseExportResult result = await ExportAsync(empty, new PixelDigests());

        Assert.Equal(CameraTimelapseExportProblem.Unreadable, result.Problem);
        Assert.Equal([empty], Directory.GetFileSystemEntries(_folder));
    }

    [FFmpegFact]
    public async Task CancellingDuringTheDecodingLeavesNothing()
    {
        string timelapse = await WriteTimelapseAsync("2026-11-04_14-05-09_timelapse.mp4", [0, 1, 0, 1, 0, 1]);
        TaskCompletionSource second = new(TaskCreationOptions.RunContinuationsAsynchronously);
        PixelDigests encoders = new() { OnFrame = n => { if (n == 2) { second.TrySetResult(); } } };
        await using CameraTimelapseJpegExporter exporter = new(new FFmpegCameraFeedFactory(), encoders, NullLogger<CameraTimelapseJpegExporter>.Instance);
        TaskCompletionSource<CameraTimelapseExportResult> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        exporter.Completed += (_, r) => completed.TrySetResult(r);
        // The encoder blocks the decoder thread at frame 2 until the test cancelled.
        encoders.Block = (2, second.Task.ContinueWith(_ => exporter.Cancel(), Ct, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default));

        Assert.True(exporter.Start(timelapse));
        CameraTimelapseExportResult result = await completed.Task.WaitAsync(TestFixtures.Prompt, Ct);

        Assert.Equal(CameraTimelapseExportProblem.Cancelled, result.Problem);
        Assert.Equal([timelapse], Directory.GetFileSystemEntries(_folder));
    }

    private static async Task<CameraTimelapseExportResult> ExportAsync(string timelapse, ICameraJpegSequenceEncoderFactory encoders)
    {
        await using CameraTimelapseJpegExporter exporter = new(new FFmpegCameraFeedFactory(), encoders, NullLogger<CameraTimelapseJpegExporter>.Instance);
        TaskCompletionSource<CameraTimelapseExportResult> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        exporter.Completed += (_, result) => completed.TrySetResult(result);
        Assert.True(exporter.Start(timelapse, "mk4"));
        return await completed.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
    }

    /// <summary>A timelapse of the fixture's key frames in the given order, as the timelapse recorder writes it (T-158).</summary>
    private async Task<string> WriteTimelapseAsync(string name, int[] keyFrameOrder)
    {
        List<byte[]> keyFrames = [];
        await using (FFmpegCameraFeed feed = TestFixtures.CreateFeed(TestFixtures.Buddy3D))
        {
            await foreach (CameraStreamItem item in feed.ReadAsync(new CameraStreamDemand(frames: false, packets: true), Ct))
            {
                if (item.Kind == CameraStreamItemKind.Packet && item.Packet.IsKeyFrame)
                {
                    keyFrames.Add(item.Packet.Data.ToArray());
                }
            }
        }

        string path = Path.Combine(_folder, name);
        CameraStreamInfo info = new(CameraVideoCodec.H264, 0, 0, CameraTimeBase.Rtp90kHz, ReadOnlyMemory<byte>.Empty);
        using ICameraRecordingWriter writer = new FFmpegCameraRecordingWriterFactory().Create(path, info, keyFrames[keyFrameOrder[0]]);
        for (int n = 0; n < keyFrameOrder.Length; n++)
        {
            writer.Write(keyFrames[keyFrameOrder[n]], n * 3000L, n * 3000L, 3000, isKeyFrame: true);
        }

        writer.Complete();
        return path;
    }

    /// <summary>Writes width, height and a digest of the BGRA pixels instead of a JPEG.</summary>
    private sealed class PixelDigests : ICameraJpegSequenceEncoderFactory
    {
        public Action<int>? OnFrame { get; set; }

        /// <summary>Waited for (on the decoder thread) at that frame, after <see cref="OnFrame"/>; <see langword="null"/>: none.</summary>
        public (int Frame, Task Gate)? Block { get; set; }

        public ICameraJpegSequenceEncoder Create(int quality) => new Encoder(this);

        public static (int Width, int Height, string Digest) Read(string path)
        {
            string[] parts = File.ReadAllText(path).Split(' ');
            return (int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), parts[2]);
        }

        private sealed class Encoder(PixelDigests owner) : ICameraJpegSequenceEncoder
        {
            private int _frames;

            public void Encode(in CameraFrame frame, Stream destination)
            {
                _frames++;
                owner.OnFrame?.Invoke(_frames);
                if (owner.Block is { } block && block.Frame == _frames)
                {
                    Assert.True(block.Gate.Wait(TestFixtures.Prompt));
                }

                using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                ReadOnlySpan<byte> pixels = frame.Pixels.Span;
                for (int row = 0; row < frame.Height; row++)
                {
                    hash.AppendData(pixels.Slice(row * frame.Stride, frame.Width * CameraFrame.BytesPerPixel));
                }

                destination.Write(System.Text.Encoding.ASCII.GetBytes($"{frame.Width} {frame.Height} {Convert.ToHexString(hash.GetHashAndReset())}"));
            }

            public void Dispose()
            {
            }
        }
    }
}
