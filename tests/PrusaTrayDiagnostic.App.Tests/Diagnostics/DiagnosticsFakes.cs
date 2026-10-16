using PrusaTrayDiagnostic.App.Diagnostics;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.PrusaLink;

namespace PrusaTrayDiagnostic.App.Tests.Diagnostics;

/// <summary>Records info requests; the test completes each one when it wants.</summary>
internal sealed class FakePrinterInfoSource : IPrinterInfoSource
{
    private readonly Lock _lock = new();
    private readonly List<InfoRequest> _requests = [];

    /// <summary>Thrown synchronously by <see cref="GetInfoAsync"/> instead of returning a task.</summary>
    public Exception? ThrowOnRequest { get; set; }

    public IReadOnlyList<InfoRequest> Requests
    {
        get
        {
            lock (_lock)
            {
                return [.. _requests];
            }
        }
    }

    public Task<PrusaLinkInfoResult?> GetInfoAsync(PrinterDefinition printer, CancellationToken cancellationToken)
    {
        if (ThrowOnRequest is not null)
        {
            throw ThrowOnRequest;
        }

        InfoRequest request = new(printer, cancellationToken);
        lock (_lock)
        {
            _requests.Add(request);
        }

        return request.Completion.Task;
    }

    /// <summary>Ends every request still pending, so that nothing keeps waiting after a test.</summary>
    public void CancelPending()
    {
        foreach (InfoRequest request in Requests)
        {
            request.Completion.TrySetCanceled();
        }
    }
}

internal sealed class InfoRequest(PrinterDefinition printer, CancellationToken token)
{
    public PrinterDefinition Printer { get; } = printer;

    public CancellationToken Token { get; } = token;

    /// <summary>Continuations run inline, so a completed request posts its result to the dispatcher immediately.</summary>
    public TaskCompletionSource<PrusaLinkInfoResult?> Completion { get; } = new();
}

/// <summary>Fixed "now" in a fixed UTC+2 zone, so that displayed times do not depend on the machine.</summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");

    public DateTimeOffset Now { get; set; } = now;

    public override TimeZoneInfo LocalTimeZone => Zone;

    public override DateTimeOffset GetUtcNow() => Now;
}
