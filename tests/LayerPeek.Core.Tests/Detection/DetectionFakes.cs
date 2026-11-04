using System.Collections.Concurrent;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Detection;
using LayerPeek.Core.Notifications;

namespace LayerPeek.Core.Tests.Detection;

/// <summary>
/// A detector driven by the test (T-153: the infrastructure is tested without a real detector): its score and warm-up
/// come from <see cref="DetectorScript"/>; it records what it was given. Scores only from inside the region: the mean
/// brightness of the region is recorded, and <see cref="DetectorScript.ScoreFromRegion"/> scores it.
/// </summary>
internal sealed class ScriptedDetector(DetectorScript script) : IPrintFailureDetector
{
    private int _samples;
    private int _resets;

    public int Samples => Volatile.Read(ref _samples);

    public int Resets => Volatile.Read(ref _resets);

    public DetectionPixelRegion LastRegion { get; private set; }

    public (int Width, int Height) LastSize { get; private set; }

    public PrintFailureContext LastContext { get; private set; }

    public double LastRegionMean { get; private set; }

    public void Reset(in PrintFailureContext context)
    {
        Interlocked.Increment(ref _resets);
        if (script.ThrowOnReset)
        {
            throw new InvalidOperationException("Scripted reset failure.");
        }
    }

    public DetectionResult Analyze(in DetectionSample sample)
    {
        if (script.ThrowOnAnalyze)
        {
            throw new InvalidOperationException("Scripted failure.");
        }

        long sum = 0;
        for (int y = sample.Region.Y; y < sample.Region.Bottom; y++)
        {
            foreach (byte value in sample.Row(y).Slice(sample.Region.X, sample.Region.Width))
            {
                sum += value;
            }
        }

        LastRegionMean = (double)sum / sample.Region.Area;
        LastRegion = sample.Region;
        LastSize = (sample.Width, sample.Height);
        LastContext = sample.Context;
        Interlocked.Increment(ref _samples);
        float score = script.ScoreFromRegion ? (float)(LastRegionMean / 255) : script.Score;
        return new DetectionResult(script.WarmedUp, score);
    }
}

/// <summary>What the scripted detectors of a test return; changed by the test while they run.</summary>
internal sealed class DetectorScript
{
    private readonly ConcurrentQueue<ScriptedDetector> _created = new();

    public bool WarmedUp { get; set; } = true;

    public bool ThrowOnAnalyze { get; set; }

    public bool ThrowOnReset { get; set; }

    public bool ScoreFromRegion { get; set; }

    private float _score;

    public float Score
    {
        get => Volatile.Read(ref _score);
        set => Volatile.Write(ref _score, value);
    }

    /// <summary>Every detector created so far, oldest first.</summary>
    public ScriptedDetector[] Created => [.. _created];

    public ScriptedDetector Latest => Created[^1];

    /// <summary>Thresholds 0.5 (sensitivity 0) – 0.1 (100): 0.3 at the default sensitivity.</summary>
    public PrintFailureDetectorRegistration Registration(string id = "scripted") =>
        new(id, DetectionFrameSpec.Default, new DetectionThresholds(0.5f, 0.1f), () =>
        {
            ScriptedDetector detector = new(this);
            _created.Enqueue(detector);
            return detector;
        });
}

/// <summary>Records every notification of a print failure.</summary>
internal sealed class AlarmNotifications : INotificationService
{
    public ConcurrentQueue<PrintFailureNotification> Shown { get; } = new();

    public void Show(PrintNotification notification)
    {
    }

    public void ShowPrintFailure(PrintFailureNotification notification) => Shown.Enqueue(notification);
}

/// <summary>A JPEG "encoder" that writes a recognizable stand-in (SOI, width, height, EOI) and counts its calls.</summary>
internal sealed class FakeJpegEncoder : ICameraImageEncoder
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public bool Fail { get; set; }

    public byte[] EncodeJpeg(ReadOnlySpan<byte> pixels, int width, int height, int stride, int quality)
    {
        Interlocked.Increment(ref _calls);
        if (Fail)
        {
            throw new InvalidOperationException("Scripted encoder failure.");
        }

        return [0xFF, 0xD8, (byte)width, (byte)height, pixels[0], 0xFF, 0xD9];
    }
}

/// <summary>The recordings folder of a test: a temporary root, and the rotation requests counted.</summary>
internal sealed class FakeRecordingsFolder(string root) : ICameraRecordingsFolder
{
    private int _rotations;

    public string RecordingsFolder { get; } = root;

    public int Rotations => Volatile.Read(ref _rotations);

    public void RequestRotation() => Interlocked.Increment(ref _rotations);
}
