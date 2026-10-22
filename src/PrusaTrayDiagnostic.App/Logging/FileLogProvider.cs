using System.Text;

using Microsoft.Extensions.Logging;

namespace PrusaTrayDiagnostic.App.Logging;

/// <summary>
/// Writes the log into <see cref="LogFile"/> (T-024). Callers only format their record and put it into a bounded queue;
/// one background thread writes the queue in batches, so logging never blocks polling or the UI on the disk. A full queue
/// drops new records and the log then says how many were dropped. <see cref="Flush"/> waits for everything logged so far
/// (quitting, T-046 crash handlers). After <see cref="Dispose"/> late records are dropped silently, never thrown.
/// </summary>
internal sealed class FileLogProvider : ILoggerProvider
{
    public const int DefaultQueueCapacity = 10_000;

    /// <summary>How long <see cref="Dispose"/> waits for the queue to be written.</summary>
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Encoding buffer of the writer, enough for the batches of normal operation.</summary>
    private const int BufferSize = 64 * 1024;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly LogFile _file;
    private readonly LogLevelSwitch _level;
    private readonly TimeProvider _time;
    private readonly int _capacity;
    private readonly int _processId = Environment.ProcessId;
    private readonly Thread _writer;

    // Guards the queue and the counters below; the writer and Flush wait on it (Monitor.Wait).
    private readonly object _gate = new();
    private List<string> _queue = [];
    private long _accepted;
    private long _written;
    private long _dropped;
    // Written under the gate; read without it by the loggers to skip formatting once disposed.
    private volatile bool _closed;

    /// <param name="file">The file to write; disposed with the provider.</param>
    /// <param name="level">Minimum level, read on every call (T-047 switches it while the app runs).</param>
    /// <param name="time">Clock of the time stamps (tests); the system clock by default.</param>
    /// <param name="queueCapacity">Records waiting for the writer at most; more are dropped.</param>
    public FileLogProvider(LogFile file, LogLevelSwitch level, TimeProvider? time = null, int queueCapacity = DefaultQueueCapacity)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(level);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(queueCapacity);
        _file = file;
        _level = level;
        _time = time ?? TimeProvider.System;
        _capacity = queueCapacity;
        // A background thread never keeps the process alive; it waits on a monitor, not on the thread pool, so it still
        // writes when the pool is starved (crash handlers, T-046).
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "Log file writer" };
        _writer.Start();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    /// <summary>Starts rotating the files: call in the primary instance only (T-043).</summary>
    public void EnableRotation() => _file.RotationEnabled = true;

    /// <summary>
    /// Waits until every record logged before this call is in the file (or lost: a failed write is not retried), at most
    /// <paramref name="timeout"/> (<see cref="Timeout.InfiniteTimeSpan"/>: no limit). Never throws.
    /// </summary>
    /// <returns><see langword="false"/> when the time ran out (e.g. a hanging disk).</returns>
    public bool Flush(TimeSpan timeout)
    {
        long deadline = timeout == Timeout.InfiniteTimeSpan ? long.MaxValue : Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        lock (_gate)
        {
            long target = _accepted;
            while (_written < target)
            {
                long remaining = deadline - Environment.TickCount64;
                if (remaining <= 0 || !_writer.IsAlive)
                {
                    return false;
                }

                Monitor.Wait(_gate, (int)Math.Min(remaining, int.MaxValue));
            }
        }

        return true;
    }

    /// <summary>Writes what is queued (at most a few seconds) and stops the writer; later records are dropped.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            Monitor.PulseAll(_gate);
        }

        // The writer drains the queue before it ends; a hanging disk does not hold up quitting.
        if (_writer.Join(DisposeTimeout))
        {
            _file.Dispose();
        }
    }

    private void Enqueue(string record)
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            if (_queue.Count >= _capacity)
            {
                _dropped++;
                return;
            }

            _queue.Add(record);
            _accepted++;
            if (_queue.Count == 1)
            {
                // The writer waits only while the queue is empty.
                Monitor.PulseAll(_gate);
            }
        }
    }

    private void WriteLoop()
    {
        List<string> batch = [];
        byte[] buffer = new byte[BufferSize];
        // Records lost so far and not yet reported in the file: dropped by a full queue, or in a batch that failed to write.
        long dropped = 0;
        long unwritten = 0;
        while (true)
        {
            lock (_gate)
            {
                while (_queue.Count == 0 && !_closed)
                {
                    Monitor.Wait(_gate);
                }

                if (_queue.Count == 0)
                {
                    // Closed and everything written.
                    return;
                }

                // The callers go on filling the other list meanwhile.
                (batch, _queue) = (_queue, batch);
                dropped += _dropped;
                _dropped = 0;
            }

            int records = batch.Count;
            if (TryWrite(batch, dropped, unwritten, ref buffer))
            {
                dropped = 0;
                unwritten = 0;
            }
            else
            {
                // Reported with the next batch that gets through.
                unwritten += records;
            }

            batch.Clear();
            lock (_gate)
            {
                // Written or lost: either way Flush no longer waits for them.
                _written += records;
                Monitor.PulseAll(_gate);
            }
        }
    }

    private void AddLossReport(List<string> batch, long count, string reason)
    {
        if (count > 0)
        {
            batch.Add(Format(LogLevel.Warning, typeof(FileLogProvider).FullName!, $"Log messages lost: {count} ({reason}).", null));
        }
    }

    /// <summary>
    /// Encodes the batch, after it the reports of records lost so far, and appends it in one write. Never throws: the log
    /// is best effort, and a full disk, a removed drive, missing rights or a huge record must never break the app (an
    /// exception here would end the process).
    /// </summary>
    private bool TryWrite(List<string> batch, long dropped, long unwritten, ref byte[] buffer)
    {
        try
        {
            AddLossReport(batch, dropped, "the log queue was full");
            AddLossReport(batch, unwritten, "writing the log file failed");
            long byteCount = 0;
            foreach (string record in batch)
            {
                byteCount += Utf8.GetByteCount(record);
            }

            if (buffer.Length < byteCount || buffer.Length > BufferSize && byteCount <= BufferSize)
            {
                // Grown for a burst, back to the usual size afterwards.
                buffer = new byte[Math.Max(checked((int)byteCount), BufferSize)];
            }

            int length = 0;
            foreach (string record in batch)
            {
                length += Utf8.GetBytes(record, buffer.AsSpan(length));
            }

            _file.Append(buffer.AsSpan(0, length));
            return true;
        }
        catch (Exception)
        {
            // No other place to report it; the next batch that gets through says how many records were lost.
            return false;
        }
    }

    private string Format(LogLevel level, string category, string message, Exception? exception) =>
        LogLineFormatter.Format(_time.GetLocalNow(), level, _processId, category, message, exception);

    private sealed class FileLogger(FileLogProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => provider._level.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!provider._level.IsEnabled(logLevel) || provider._closed)
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(formatter);
            // Formatted here, on the caller's thread: the time stamp is the time of the call, and the state is not kept.
            provider.Enqueue(provider.Format(logLevel, category, formatter(state, exception), exception));
        }
    }
}
