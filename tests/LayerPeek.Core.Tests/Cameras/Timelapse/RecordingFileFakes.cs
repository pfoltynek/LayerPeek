using System.Runtime.CompilerServices;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;

namespace LayerPeek.Core.Tests.Cameras.Timelapse;

/// <summary>
/// The packets of one recording file for <see cref="ScriptedRecordings"/> (T-164): a regular stream of
/// <see cref="FramesPerSecond"/> with a key frame every <see cref="KeyFrameEvery"/> frames, timestamps of the 90 kHz clock
/// from <see cref="FirstDts"/>. Key frame <i>k</i> of the file carries the byte <see cref="FirstFill"/> + <i>k</i>, every
/// other packet 0xFF, so a test sees which key frames were written.
/// </summary>
internal sealed class RecordingScript
{
    public static readonly CameraStreamInfo DefaultInfo = new(CameraVideoCodec.H264, 1280, 720, CameraTimeBase.Rtp90kHz, new byte[] { 1, 0x42, 0xC0, 0x1F });

    public CameraStreamInfo Info { get; init; } = DefaultInfo;

    public int Frames { get; init; } = 25 * 60;

    public int FramesPerSecond { get; init; } = 25;

    public int KeyFrameEvery { get; init; } = 50;

    public long FirstDts { get; init; }

    public byte FirstFill { get; init; }

    public int PacketLength { get; init; } = 16;

    /// <summary>The read fails with a <see cref="CameraFeedException"/> after this many packets (a damaged end); <see langword="null"/>: never.</summary>
    public int? FailAfterPackets { get; init; }

    /// <summary>Packets carry no timestamps.</summary>
    public bool NoTimestamps { get; init; }

    /// <summary>Awaited before this packet (0-based) is yielded, so that a test can act meanwhile.</summary>
    public (int Packet, Task Gate)? Hold { get; init; }

    /// <summary>
    /// Called with the allocated bytes of the reading thread (where the consumer's inline continuations run too) before
    /// packet <see cref="MeasureFrom"/> and after the last packet.
    /// </summary>
    public Action<long, long>? Allocations { get; init; }

    public int MeasureFrom { get; init; }

    public long TicksPerFrame => 90_000L / FramesPerSecond;
}

/// <summary>
/// The demuxer of recording files for the timelapse from a recording (T-164): plays the <see cref="RecordingScript"/> of the
/// file's path (packets only, synchronously, from one reused buffer like the FFmpeg feed); a file without a script fails to
/// open like garbage.
/// </summary>
internal sealed class ScriptedRecordings : ICameraFeedFactory
{
    private readonly Dictionary<string, RecordingScript> _scripts = new(StringComparer.Ordinal);
    private readonly List<string> _opened = [];

    /// <summary>Thrown by <see cref="Create"/>; <see langword="null"/>: none.</summary>
    public Exception? CreateFailure { get; set; }

    /// <summary>The files read, in order.</summary>
    public IReadOnlyList<string> Opened
    {
        get
        {
            lock (_opened)
            {
                return [.. _opened];
            }
        }
    }

    /// <summary>Writes <paramref name="path"/> (some bytes, for its size) and plays <paramref name="script"/> for it.</summary>
    public string Add(string path, RecordingScript script, int fileBytes = 1000)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[fileBytes]);
        _scripts[path] = script;
        return path;
    }

    public ICameraFeed Create(Uri streamUri)
    {
        if (CreateFailure is { } failure)
        {
            throw failure;
        }

        Assert.True(streamUri.IsFile);
        string path = streamUri.LocalPath;
        lock (_opened)
        {
            _opened.Add(path);
        }

        return new Feed(_scripts.GetValueOrDefault(path));
    }

    private sealed class Feed(RecordingScript? script) : ICameraFeed
    {
        public IAsyncEnumerable<CameraFrame> ReadFramesAsync(CancellationToken cancellationToken) => throw new CameraFeedException("No decoder in this test.");

        public async IAsyncEnumerable<CameraStreamItem> ReadAsync(CameraStreamDemand demand, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Assert.False(demand.Frames);
            Assert.True(demand.Packets);
            if (script is null)
            {
                throw new CameraFeedException("Opening the camera stream failed: Invalid data found when processing input.");
            }

            yield return CameraStreamItem.Started(script.Info);
            byte[] data = new byte[script.PacketLength];
            long before = 0;
            for (int n = 0; n < script.Frames; n++)
            {
                if (n == script.MeasureFrom)
                {
                    before = GC.GetAllocatedBytesForCurrentThread();
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (script.FailAfterPackets is { } fail && n >= fail)
                {
                    throw new CameraFeedException("Reading the camera stream failed: Invalid data found when processing input.");
                }

                if (script.Hold is { } hold && hold.Packet == n)
                {
                    await hold.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                bool key = n % script.KeyFrameEvery == 0;
                Array.Fill(data, key ? (byte)(script.FirstFill + (n / script.KeyFrameEvery)) : (byte)0xFF);
                long? dts = script.NoTimestamps ? null : script.FirstDts + (n * script.TicksPerFrame);
                yield return CameraStreamItem.FromPacket(new CameraPacket(data, dts, dts, script.TicksPerFrame, key, CameraTimeBase.Rtp90kHz));
            }

            script.Allocations?.Invoke(before, GC.GetAllocatedBytesForCurrentThread());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
