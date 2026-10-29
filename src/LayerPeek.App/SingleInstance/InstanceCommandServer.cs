using Microsoft.Extensions.Logging;

namespace LayerPeek.App.SingleInstance;

/// <summary>
/// The running instance's side of the channel (T-043): accepts one client at a time on a background task, reads its
/// command (at most <see cref="InstanceProtocol.MaxMessageLength"/> bytes within <see cref="ReadTimeout"/>) and reports it.
/// A broken or unknown message is logged and the server goes on. Runs until disposed.
/// </summary>
internal sealed partial class InstanceCommandServer : IAsyncDisposable, IDisposable
{
    /// <summary>A client writes its command right after connecting; a silent one is dropped after this.</summary>
    public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Pause after a failed accept, so that a persistent failure does not spin.</summary>
    private static readonly TimeSpan AcceptRetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly IInstanceListener _listener;
    private readonly Action<InstanceCommand> _received;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private int _disposed;

    /// <param name="listener">Already listening; owned (disposed) by the server.</param>
    /// <param name="received">Called on a background thread for each valid command.</param>
    public InstanceCommandServer(IInstanceListener listener, Action<InstanceCommand> received, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(received);
        ArgumentNullException.ThrowIfNull(logger);
        _listener = listener;
        _received = received;
        _logger = logger;
        _loop = Task.Run(() => RunAsync(_stop.Token));
    }

    /// <summary>Stops accepting and removes the channel without waiting for the background task.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stop.Cancel();
        _listener.Dispose();
    }

    /// <summary>Like <see cref="Dispose"/>, then waits until the background task ended (tests).</summary>
    public async ValueTask DisposeAsync()
    {
        Dispose();
        await _loop.ConfigureAwait(false);
        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Stream client;
            try
            {
                client = await _listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (InstancePeerRefusedException ex)
            {
                // Already closed by the listener; no pause, the next client may be the user's own start.
                LogPeerRefused(ex.PeerUserId);
                continue;
            }
            catch (Exception ex)
            {
                LogAcceptFailed(ex);
                try
                {
                    await Task.Delay(AcceptRetryDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
            }

            await using (client.ConfigureAwait(false))
            {
                await HandleAsync(client, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleAsync(Stream client, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[InstanceProtocol.MaxMessageLength + 1];
        // Reading the command and answering it share one limit: a client that stops reading cannot hold the server.
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadTimeout);
        int length = 0;
        try
        {
            while (length < buffer.Length)
            {
                int read = await client.ReadAsync(buffer.AsMemory(length), timeout.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                length += read;
                if (Array.IndexOf(buffer, (byte)'\n', 0, length) >= 0)
                {
                    break;
                }
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            LogReadFailed(ex);
            return;
        }

        // Without a line feed within the limit the whole (too long) text is decoded and rejected.
        int end = Array.IndexOf(buffer, (byte)'\n', 0, length);
        if (!InstanceProtocol.TryDecode(buffer.AsSpan(0, end >= 0 ? end : length), out InstanceCommand command))
        {
            LogUnknownCommand(length);
            return;
        }

        LogCommandReceived(command);
        try
        {
            _received(command);
        }
        catch (Exception ex)
        {
            // Not acknowledged: the client tries again within its budget.
            LogDeliveryFailed(command, ex);
            return;
        }

        try
        {
            await client.WriteAsync(new[] { InstanceProtocol.Acknowledgement }, timeout.Token).ConfigureAwait(false);
            await client.FlushAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // The client is gone or stopped reading; the command was handed over anyway.
            LogAcknowledgeFailed(ex);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Acknowledging the command of another start of the app failed.")]
    private partial void LogAcknowledgeFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused a connection of another user (uid {PeerUserId}) to the instance channel; nothing was read.")]
    private partial void LogPeerRefused(uint? peerUserId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Accepting a connection from another start of the app failed.")]
    private partial void LogAcceptFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading the command of another start of the app failed.")]
    private partial void LogReadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Another start of the app sent an unknown command ({Length} bytes); ignored.")]
    private partial void LogUnknownCommand(int length);

    [LoggerMessage(Level = LogLevel.Information, Message = "Another start of the app sent {Command}.")]
    private partial void LogCommandReceived(InstanceCommand command);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handing over the instance command {Command} failed.")]
    private partial void LogDeliveryFailed(InstanceCommand command, Exception exception);
}
