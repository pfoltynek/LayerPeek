using Microsoft.Extensions.Logging;

using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Previews;

/// <summary>
/// In-memory cache of G-code previews in front of an <see cref="IPrintPreviewFetcher"/> (T-086). Nothing is written to
/// disk. Rules:
/// <list type="bullet">
/// <item>Each printer holds the previews of one job (<see cref="PrintJob.Id"/> + <see cref="PrintJob.PreviewReference"/>),
/// at most one per <see cref="PrintPreviewSize"/>. A preview is fetched once per job and size, lazily on the first
/// <see cref="GetAsync"/>; concurrent requests share one fetch.</item>
/// <item>The preview stays while the snapshots report the same job, i.e. also after the print finished or was stopped,
/// and while the printer is idle or offline without a job. It is released when another job appears
/// (<see cref="Observe"/> or <see cref="GetAsync"/>). When that job prints the same file, the old preview is kept only
/// as the validator of a conditional fetch (ETag); an unchanged file then costs no download.</item>
/// <item>A missing preview is remembered for the job; a transient failure for <see cref="PrintPreviewCacheOptions.RetryDelay"/>.</item>
/// <item>A printer that was removed or whose address or connection type changed is dropped
/// (<see cref="RetainOnly"/>, <see cref="Remove"/>, or on the next <see cref="GetAsync"/>); at most
/// <see cref="PrintPreviewCacheOptions.MaxPrinters"/> printers are kept (least recently requested dropped first).</item>
/// </list>
/// Dropping a printer cancels its running fetch. Thread-safe.
/// </summary>
public sealed partial class PrintPreviewCache : IPrintPreviewSource, IDisposable
{
    private static readonly Task<PrintPreview?> NoPreview = Task.FromResult<PrintPreview?>(null);

    private readonly IPrintPreviewFetcher _fetcher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PrintPreviewCache> _logger;
    private readonly PrintPreviewCacheOptions _options;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private long _useCounter;
    private bool _disposed;

    public PrintPreviewCache(IPrintPreviewFetcher fetcher, TimeProvider timeProvider, ILogger<PrintPreviewCache> logger, PrintPreviewCacheOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(fetcher);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _fetcher = fetcher;
        _timeProvider = timeProvider;
        _logger = logger;
        _options = options ?? PrintPreviewCacheOptions.Default;
    }

    /// <summary>Printers that currently hold an entry (a job with fetched, pending or remembered previews).</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    /// <inheritdoc/>
    public Task<PrintPreview?> GetAsync(PrinterDefinition printer, PrintJob job, PrintPreviewSize size, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(printer);
        ArgumentNullException.ThrowIfNull(job);
        if (!Enum.IsDefined(size))
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Unknown preview size.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<PrintPreview?>(cancellationToken);
        }

        if (job.PreviewReference is not { } reference)
        {
            return NoPreview;
        }

        Task<PrintPreview?> result;
        Fetch? fetch = null;
        List<Entry>? released = null;
        lock (_lock)
        {
            if (_disposed)
            {
                return NoPreview;
            }

            Entry entry = GetOrReplace(printer, job.Id, reference, ref released);
            entry.LastUse = ++_useCounter;
            Slot slot = entry.Slots[(int)size];
            if (slot.Result is null || (slot.FailedAt is { } failedAt && _timeProvider.GetElapsedTime(failedAt) >= _options.RetryDelay))
            {
                TaskCompletionSource<PrintPreview?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                slot.Result = completion.Task;
                slot.FailedAt = null;
                entry.FetchesInFlight++;
                fetch = new Fetch(entry, slot, completion, printer, reference, size, slot.ValidatorETag);
            }

            result = slot.Result;
        }

        Release(released);
        if (fetch is not null)
        {
            // Task.Run: a fetcher that does synchronous work first must neither block nor run on the caller's (UI) thread.
            _ = Task.Run(() => RunFetchAsync(fetch), CancellationToken.None);
        }

        // The completed task itself is returned for a cache hit (WaitAsync returns it unchanged), so a hit allocates nothing.
        return result.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Tells the cache about a new snapshot (e.g. from <see cref="Monitoring.IPrinterMonitor.SnapshotUpdated"/>), so that
    /// the preview of a finished job is released as soon as the next job starts, even when nobody asks for the new
    /// preview. Snapshots without a job keep the preview. Cheap and allocation-free when nothing changes.
    /// </summary>
    public void Observe(PrinterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Job is not { } job)
        {
            return;
        }

        List<Entry>? released = null;
        lock (_lock)
        {
            // A job without a reference may be the same job whose details the client could not read this time.
            if (!_entries.TryGetValue(snapshot.PrinterId, out Entry? entry)
                || entry.IsJob(job.Id, job.PreviewReference)
                || (job.PreviewReference is null && job.Id == entry.JobId))
            {
                return;
            }

            Detach(entry, ref released);
            if (job.PreviewReference is { } reference && reference.Equals(entry.Reference))
            {
                // The same file again: keep the old previews only as validators of the first fetch of the new job.
                Entry next = new(entry.PrinterId, entry.ConnectionType, entry.Address, job.Id, reference);
                next.InheritValidators(entry);
                next.LastUse = entry.LastUse;
                _entries.Add(next.PrinterId, next);
            }
        }

        Release(released);
    }

    /// <summary>
    /// Drops every printer that is not in <paramref name="printers"/> or whose address or connection type changed,
    /// e.g. after the settings were saved.
    /// </summary>
    public void RetainOnly(IEnumerable<PrinterDefinition> printers)
    {
        ArgumentNullException.ThrowIfNull(printers);
        Dictionary<string, PrinterDefinition> keep = new(StringComparer.OrdinalIgnoreCase);
        foreach (PrinterDefinition printer in printers)
        {
            ArgumentNullException.ThrowIfNull(printer, nameof(printers));
            keep[printer.Id] = printer;
        }

        List<Entry>? released = null;
        lock (_lock)
        {
            foreach (Entry entry in _entries.Values.ToArray())
            {
                if (!keep.TryGetValue(entry.PrinterId, out PrinterDefinition? printer) || !entry.IsFor(printer))
                {
                    Detach(entry, ref released);
                }
            }
        }

        Release(released);
    }

    /// <summary>Drops the previews of one printer (e.g. removed from the settings).</summary>
    public void Remove(string printerId)
    {
        ArgumentNullException.ThrowIfNull(printerId);
        List<Entry>? released = null;
        lock (_lock)
        {
            if (_entries.TryGetValue(printerId, out Entry? entry))
            {
                Detach(entry, ref released);
            }
        }

        Release(released);
    }

    /// <summary>Drops everything.</summary>
    public void Clear()
    {
        List<Entry>? released = null;
        lock (_lock)
        {
            DetachAll(ref released);
        }

        Release(released);
    }

    /// <summary>Drops everything and cancels running fetches; later requests return no preview.</summary>
    public void Dispose()
    {
        List<Entry>? released = null;
        lock (_lock)
        {
            _disposed = true;
            DetachAll(ref released);
        }

        Release(released);
    }

    private Entry GetOrReplace(PrinterDefinition printer, long? jobId, PrintPreviewReference reference, ref List<Entry>? released)
    {
        Entry? previous = null;
        if (_entries.TryGetValue(printer.Id, out Entry? existing))
        {
            if (existing.IsFor(printer) && existing.IsJob(jobId, reference))
            {
                return existing;
            }

            Detach(existing, ref released);
            previous = existing.IsFor(printer) && existing.Reference.Equals(reference) ? existing : null;
        }

        Entry entry = new(printer.Id, printer.ConnectionType, printer.Address, jobId, reference);
        if (previous is not null)
        {
            entry.InheritValidators(previous);
        }

        _entries.Add(entry.PrinterId, entry);
        while (_entries.Count > _options.MaxPrinters)
        {
            Entry? oldest = null;
            foreach (Entry candidate in _entries.Values)
            {
                if (candidate != entry && (oldest is null || candidate.LastUse < oldest.LastUse))
                {
                    oldest = candidate;
                }
            }

            Detach(oldest!, ref released);
        }

        return entry;
    }

    private void DetachAll(ref List<Entry>? released)
    {
        foreach (Entry entry in _entries.Values.ToArray())
        {
            Detach(entry, ref released);
        }
    }

    /// <summary>Removes the entry under the lock; <see cref="Release"/> cancels its fetches after the lock is left.</summary>
    private void Detach(Entry entry, ref List<Entry>? released)
    {
        _entries.Remove(entry.PrinterId);
        entry.Released = true;
        (released ??= []).Add(entry);
    }

    /// <summary>Cancels the fetches of detached entries (outside the lock: cancellation runs callbacks synchronously).</summary>
    private void Release(List<Entry>? released)
    {
        if (released is null)
        {
            return;
        }

        foreach (Entry entry in released)
        {
            try
            {
                entry.Cancellation.Cancel();
            }
            catch (AggregateException ex)
            {
                LogCancellationFailed(ex, entry.PrinterId);
            }

            bool dispose;
            lock (_lock)
            {
                entry.CancelIssued = true;
                dispose = entry.FetchesInFlight == 0;
            }

            if (dispose)
            {
                entry.Cancellation.Dispose();
            }
        }
    }

    private async Task RunFetchAsync(Fetch fetch)
    {
        Entry entry = fetch.Entry;
        PrintPreviewFetchResult result = PrintPreviewFetchResult.Failed;
        try
        {
            result = await _fetcher.FetchAsync(fetch.Printer, fetch.Reference, fetch.Size, fetch.ETag, entry.Cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (entry.Cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // The fetcher broke its contract (failures are results); the preview is just missing.
            LogFetcherFailed(ex, entry.PrinterId);
        }
        finally
        {
            // Also when logging failed: the slot must leave "pending" and the waiters must be released.
            Complete(fetch, result);
        }
    }

    private void Complete(Fetch fetch, PrintPreviewFetchResult result)
    {
        Entry entry = fetch.Entry;
        PrintPreview? preview = null;
        bool dispose;
        lock (_lock)
        {
            if (!entry.Released)
            {
                preview = Apply(fetch.Slot, result);
            }

            entry.FetchesInFlight--;
            dispose = entry.CancelIssued && entry.FetchesInFlight == 0;
        }

        if (dispose)
        {
            entry.Cancellation.Dispose();
        }

        fetch.Completion.TrySetResult(preview);
    }

    /// <summary>Stores the outcome in the slot (under the lock) and returns the preview to hand out.</summary>
    private PrintPreview? Apply(Slot slot, PrintPreviewFetchResult result)
    {
        switch (result.Status)
        {
            case PrintPreviewFetchStatus.Downloaded when result.Preview is { } downloaded:
                slot.SetCurrent(downloaded, result.ETag);
                return downloaded;
            case PrintPreviewFetchStatus.NotModified when slot.Validator is { } unchanged:
                slot.SetCurrent(unchanged, result.ETag ?? slot.ValidatorETag);
                return unchanged;
            case PrintPreviewFetchStatus.NotAvailable:
                slot.SetCurrent(null, null);
                return null;
            default:
                // Failed, or a NotModified without anything to keep: try again after the delay (the validator stays).
                slot.FailedAt = _timeProvider.GetTimestamp();
                return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Fetching the G-code preview of printer '{PrinterId}' failed unexpectedly.")]
    private partial void LogFetcherFailed(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cancelling the G-code preview fetch of printer '{PrinterId}' failed.")]
    private partial void LogCancellationFailed(Exception exception, string printerId);

    private sealed record Fetch(
        Entry Entry,
        Slot Slot,
        TaskCompletionSource<PrintPreview?> Completion,
        PrinterDefinition Printer,
        PrintPreviewReference Reference,
        PrintPreviewSize Size,
        string? ETag);

    /// <summary>The previews of one job of one printer. Mutable state is guarded by the cache lock.</summary>
    private sealed class Entry(string printerId, ConnectionType connectionType, string address, long? jobId, PrintPreviewReference reference)
    {
        public string PrinterId { get; } = printerId;

        public ConnectionType ConnectionType { get; } = connectionType;

        public string Address { get; } = address;

        public long? JobId { get; } = jobId;

        public PrintPreviewReference Reference { get; } = reference;

        public Slot[] Slots { get; } = [new(), new()];

        /// <summary>Cancelled when the entry is dropped; disposed once that happened and no fetch uses it any more.</summary>
        public CancellationTokenSource Cancellation { get; } = new();

        public long LastUse { get; set; }

        public int FetchesInFlight { get; set; }

        public bool Released { get; set; }

        public bool CancelIssued { get; set; }

        public bool IsFor(PrinterDefinition printer) =>
            printer.ConnectionType == ConnectionType && string.Equals(printer.Address, Address, StringComparison.Ordinal);

        public bool IsJob(long? jobId, PrintPreviewReference? reference) => jobId == JobId && Reference.Equals(reference);

        public void InheritValidators(Entry previous)
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                Slot old = previous.Slots[i];
                (Slots[i].Validator, Slots[i].ValidatorETag) = old.Current is not null && old.ETag is not null
                    ? (old.Current, old.ETag)
                    : (old.Validator, old.ValidatorETag);
            }
        }
    }

    private sealed class Slot
    {
        /// <summary>The fetch of this job and size: pending, or completed with the preview (or null).</summary>
        public Task<PrintPreview?>? Result { get; set; }

        /// <summary>Timestamp of the last transient failure; null when the result is final or pending.</summary>
        public long? FailedAt { get; set; }

        public PrintPreview? Current { get; private set; }

        public string? ETag { get; private set; }

        /// <summary>Preview of the same file from the previous job, sent as If-None-Match and kept on "not modified".</summary>
        public PrintPreview? Validator { get; set; }

        public string? ValidatorETag { get; set; }

        public void SetCurrent(PrintPreview? preview, string? etag)
        {
            Current = preview;
            ETag = preview is null ? null : etag;
            Validator = null;
            ValidatorETag = null;
            FailedAt = null;
        }
    }
}
