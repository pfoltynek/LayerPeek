using LayerPeek.App.Notifications;

namespace LayerPeek.App.Tests.Notifications;

/// <summary>A notification center in memory (T-020): nothing reaches macOS, no permission is ever asked.</summary>
internal sealed class FakeUserNotificationCenter(string bundleIdentifier = "io.github.pfoltynek.LayerPeek.E2E") : IUserNotificationCenter
{
    private readonly Lock _lock = new();
    private readonly List<UserNotificationRequest> _posted = [];
    private int _authorizationRequests;
    private int _removals;

    public string BundleIdentifier { get; } = bundleIdentifier;

    /// <summary>The answer of <see cref="RequestAuthorizationAsync"/> (changeable between notifications).</summary>
    public bool Allowed { get; set; } = true;

    /// <summary>Thrown by <see cref="RequestAuthorizationAsync"/>.</summary>
    public Exception? AuthorizationFailure { get; set; }

    /// <summary>When set, <see cref="RequestAuthorizationAsync"/> waits for it (the user has not answered yet).</summary>
    public TaskCompletionSource? AuthorizationGate { get; set; }

    public NotificationPermission Permission { get; set; } = NotificationPermission.Allowed;

    public Exception? PermissionFailure { get; set; }

    /// <summary>Thrown by the next <see cref="Post"/>.</summary>
    public Exception? FailNextPost { get; set; }

    /// <summary>Delivered notifications that a removal does not take away (removing is asynchronous in macOS).</summary>
    public int StubbornDelivered { get; set; }

    public Exception? CountFailure { get; set; }

    public int AuthorizationRequests => Volatile.Read(ref _authorizationRequests);

    public int Removals => Volatile.Read(ref _removals);

    public IReadOnlyList<UserNotificationRequest> Posted
    {
        get
        {
            lock (_lock)
            {
                return [.. _posted];
            }
        }
    }

    /// <summary>Delivered notifications (all posted ones until removed).</summary>
    public int Delivered { get; private set; }

    public async Task<bool> RequestAuthorizationAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _authorizationRequests);
        if (AuthorizationGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        return AuthorizationFailure is { } failure ? throw failure : Allowed;
    }

    public Task<NotificationPermission> GetPermissionAsync(CancellationToken cancellationToken) =>
        PermissionFailure is { } failure ? Task.FromException<NotificationPermission>(failure) : Task.FromResult(Permission);

    public void Post(UserNotificationRequest request)
    {
        if (FailNextPost is { } failure)
        {
            FailNextPost = null;
            throw failure;
        }

        lock (_lock)
        {
            _posted.Add(request);
            Delivered++;
        }
    }

    public void RemoveAll()
    {
        Interlocked.Increment(ref _removals);
        lock (_lock)
        {
            if (StubbornDelivered > 0)
            {
                StubbornDelivered--;
                return;
            }

            Delivered = 0;
        }
    }

    public Task<int> CountDeliveredAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return CountFailure is { } failure ? Task.FromException<int>(failure) : Task.FromResult(Delivered);
        }
    }
}
