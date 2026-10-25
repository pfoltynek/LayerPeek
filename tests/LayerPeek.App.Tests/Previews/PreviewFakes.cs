using Avalonia;
using Avalonia.Media;

using LayerPeek.App.Previews;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Previews;
using LayerPeek.Core.Printers;
using LayerPeek.Tests.Shared;

namespace LayerPeek.App.Tests.Previews;

/// <summary>A preview reference of the tests (the real one belongs to the PrusaLink client).</summary>
internal sealed record TestPreviewReference(string Path) : PrintPreviewReference;

/// <summary>
/// A preview source the test controls: records every request; answers with <see cref="Answer"/> (default: the preview of
/// <see cref="Previews"/> for the job's reference, or none), or leaves the request pending while <see cref="Hold"/> is set.
/// </summary>
internal sealed class FakePreviewSource : IPrintPreviewSource
{
    private readonly Lock _lock = new();
    private readonly List<(PrinterDefinition Printer, PrintJob Job, PrintPreviewSize Size)> _requests = [];
    private readonly List<TaskCompletionSource<PrintPreview?>> _held = [];

    /// <summary>The preview per reference path; a missing path = no preview (like HTTP 404).</summary>
    public Dictionary<string, PrintPreview?> Previews { get; } = new(StringComparer.Ordinal);

    /// <summary>Requests stay pending until <see cref="Release"/>.</summary>
    public bool Hold { get; set; }

    public IReadOnlyList<(PrinterDefinition Printer, PrintJob Job, PrintPreviewSize Size)> Requests
    {
        get
        {
            lock (_lock)
            {
                return [.. _requests];
            }
        }
    }

    public int RequestCount
    {
        get
        {
            lock (_lock)
            {
                return _requests.Count;
            }
        }
    }

    public static PrintPreview Png(int width = 380, int height = 285, byte red = 0xFA, byte green = 0x68, byte blue = 0x31) =>
        PrintPreview.FromPng(TestPng.Create(width, height, red, green, blue))!;

    public Task<PrintPreview?> GetAsync(PrinterDefinition printer, PrintJob job, PrintPreviewSize size, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _requests.Add((printer, job, size));
            if (Hold)
            {
                TaskCompletionSource<PrintPreview?> held = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _held.Add(held);
                return held.Task.WaitAsync(cancellationToken);
            }

            return Task.FromResult(Answer(job));
        }
    }

    /// <summary>Answers the held requests (with what <see cref="Previews"/> holds now) and stops holding.</summary>
    public void Release()
    {
        TaskCompletionSource<PrintPreview?>[] held;
        List<PrintJob> jobs;
        lock (_lock)
        {
            Hold = false;
            held = [.. _held];
            jobs = [.. _requests.Skip(_requests.Count - held.Length).Select(static request => request.Job)];
            _held.Clear();
        }

        for (int i = 0; i < held.Length; i++)
        {
            held[i].TrySetResult(Answer(jobs[i]));
        }
    }

    private PrintPreview? Answer(PrintJob job) =>
        job.PreviewReference is TestPreviewReference { Path: var path } && Previews.TryGetValue(path, out PrintPreview? preview) ? preview : null;
}

/// <summary>
/// A decoder that records what it decodes and returns <see cref="FakeImage"/>s; throws for previews in
/// <see cref="Corrupt"/>; blocks while <see cref="Gate"/> is not set.
/// </summary>
internal sealed class FakeThumbnailDecoder : IPrintThumbnailDecoder
{
    private readonly Lock _lock = new();
    private readonly List<FakeImage> _images = [];
    private readonly List<(PrintPreview Preview, int MaxSide)> _decoded = [];

    /// <summary>Set by default; reset it to keep decoding busy (proves the caller is not blocked).</summary>
    public ManualResetEventSlim Gate { get; } = new(initialState: true);

    public HashSet<PrintPreview> Corrupt { get; } = [];

    public IReadOnlyList<FakeImage> Images
    {
        get
        {
            lock (_lock)
            {
                return [.. _images];
            }
        }
    }

    public IReadOnlyList<(PrintPreview Preview, int MaxSide)> Decoded
    {
        get
        {
            lock (_lock)
            {
                return [.. _decoded];
            }
        }
    }

    public IImage Decode(PrintPreview preview, int maxSide)
    {
        Assert.True(Gate.Wait(TestTimeouts.Wait), "The decoder gate was never opened.");
        lock (_lock)
        {
            _decoded.Add((preview, maxSide));
            if (Corrupt.Contains(preview))
            {
                throw new InvalidOperationException("Corrupt PNG.");
            }

            FakeImage image = new(preview, maxSide);
            _images.Add(image);
            return image;
        }
    }
}

/// <summary>A decoded thumbnail of the tests: remembers what it was decoded from and whether it was disposed.</summary>
internal sealed class FakeImage(PrintPreview preview, int maxSide) : IImage, IDisposable
{
    private int _disposed;

    public PrintPreview Preview { get; } = preview;

    public int MaxSide { get; } = maxSide;

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public int DisposeCount => Volatile.Read(ref _disposed);

    public Size Size => new(MaxSide, MaxSide);

    public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
    {
    }

    public void Dispose() => Interlocked.Increment(ref _disposed);
}
