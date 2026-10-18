using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Web;

namespace PrusaTrayDiagnostic.App.Tests.Tray.Menu;

/// <summary>Records what the app asks the web gateway (T-028) and answers as the test says; starts no proxy.</summary>
internal sealed class FakeWebGateway : IPrinterWebGateway
{
    /// <summary>A proxy address like the real gateway returns; the last segment stands for the secret token.</summary>
    public const string Token = "Zm9vYmFyU2VjcmV0VG9rZW4xMjM0NTY";

    public static readonly Uri ProxyAddress = new($"http://127.0.0.1:50123/{Token}/");

    private readonly Lock _lock = new();
    private readonly List<PrinterDefinition> _opened = [];
    private readonly List<string> _stopped = [];
    private readonly List<string>? _log;

    public FakeWebGateway(List<string>? log = null) => _log = log;

    /// <summary>Like the real gateway without a stored password: the plain address of the menu entry is opened.</summary>
    public static FakeWebGateway WithoutPassword() =>
        new() { Open = static (_, _) => Task.FromResult(PrinterWebAccess.Plain(null, PrinterWebFallback.PasswordMissing)) };

    /// <summary>The answer of <see cref="OpenAsync"/>; default: the proxy address.</summary>
    public Func<PrinterDefinition, CancellationToken, Task<PrinterWebAccess>> Open { get; set; } =
        static (_, _) => Task.FromResult(PrinterWebAccess.Proxied(ProxyAddress));

    /// <summary>When set, <see cref="StopAsync"/> fails with it.</summary>
    public Exception? StopFailure { get; set; }

    /// <summary>When set, <see cref="StopAsync"/> waits for it (or for its token).</summary>
    public TaskCompletionSource? StopGate { get; set; }

    public IReadOnlyList<PrinterDefinition> Opened
    {
        get
        {
            lock (_lock)
            {
                return [.. _opened];
            }
        }
    }

    public IReadOnlyList<string> Stopped
    {
        get
        {
            lock (_lock)
            {
                return [.. _stopped];
            }
        }
    }

    public int StopAllCount { get; private set; }

    public int DisposeCount { get; private set; }

    public Task<PrinterWebAccess> OpenAsync(PrinterDefinition printer, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _opened.Add(printer);
        }

        return Open(printer, cancellationToken);
    }

    public async Task StopAsync(string printerId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _stopped.Add(printerId);
            _log?.Add("stop web " + printerId);
        }

        if (StopFailure is not null)
        {
            throw StopFailure;
        }

        if (StopGate is not null)
        {
            await StopGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task StopAllAsync(CancellationToken cancellationToken)
    {
        StopAllCount++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }
}
