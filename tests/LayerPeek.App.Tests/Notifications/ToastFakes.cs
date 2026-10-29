using System.Diagnostics;

using LayerPeek.App.Notifications;
using LayerPeek.App.Platform.Windows;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.Notifications;

internal static class Waiting
{
    public static async Task ForAsync(Func<bool> condition)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(elapsed.Elapsed < TestTimeouts.Wait, "Condition not met in time.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}

/// <summary>Records what the toast service would show; nothing reaches Windows.</summary>
internal sealed class FakeToastPlatform : IToastPlatform
{
    private int _openCount;

    public FakeToastSession Session { get; } = new();

    public Exception? OpenFailure { get; set; }

    public int OpenCount => Volatile.Read(ref _openCount);

    public int OpenThreadId { get; private set; }

    public Action<string?>? Activated { get; private set; }

    public IToastSession Open(Action<string?> activated)
    {
        Interlocked.Increment(ref _openCount);
        OpenThreadId = Environment.CurrentManagedThreadId;
        if (OpenFailure is not null)
        {
            throw OpenFailure;
        }

        Activated = activated;
        return Session;
    }
}

internal sealed class FakeToastSession : IToastSession
{
    private readonly Lock _lock = new();
    private readonly List<string> _shown = [];
    private readonly List<string?> _tags = [];
    private readonly ManualResetEventSlim _gate = new(initialState: true);
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _disposed;

    public Exception? FailNext { get; set; }

    /// <summary>What <see cref="Setting"/> reports (T-101); Windows shows the toasts by default.</summary>
    public ToastNotifierSetting CurrentSetting { get; set; } = ToastNotifierSetting.Enabled;

    /// <summary>Thrown by <see cref="Setting"/> when set.</summary>
    public Exception? SettingFailure { get; set; }

    /// <summary>The thread <see cref="Setting"/> was last read on.</summary>
    public int SettingThreadId { get; private set; }

    /// <summary>The tags of <see cref="Shown"/>, in the same order.</summary>
    public IReadOnlyList<string?> ShownTags
    {
        get
        {
            lock (_lock)
            {
                return [.. _tags];
            }
        }
    }

    public ToastNotifierSetting Setting
    {
        get
        {
            SettingThreadId = Environment.CurrentManagedThreadId;
            return SettingFailure is { } failure ? throw failure : CurrentSetting;
        }
    }

    public int ShowThreadId { get; private set; }

    public int DisposeThreadId { get; private set; }

    public bool Disposed => _disposed;

    public IReadOnlyList<string> Shown
    {
        get
        {
            lock (_lock)
            {
                return [.. _shown];
            }
        }
    }

    public void Block() => _gate.Reset();

    public void Release() => _gate.Set();

    public Task WaitUntilShowEnteredAsync() => _entered.Task.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

    public async Task<IReadOnlyList<string>> WaitForShownAsync(int count)
    {
        await Waiting.ForAsync(() => Shown.Count >= count);
        return Shown;
    }

    public void Show(string toastXml, string? tag)
    {
        ShowThreadId = Environment.CurrentManagedThreadId;
        _entered.TrySetResult();
        _gate.Wait(TestTimeouts.Wait);
        if (FailNext is { } failure)
        {
            FailNext = null;
            throw failure;
        }

        lock (_lock)
        {
            _shown.Add(toastXml);
            _tags.Add(tag);
        }
    }

    public void Dispose()
    {
        DisposeThreadId = Environment.CurrentManagedThreadId;
        _disposed = true;
    }
}
