using PrusaTrayDiagnostic.App.Platform.Windows;

namespace PrusaTrayDiagnostic.App.Tests.Platform.Windows;

/// <summary>A managed stand-in for the WinRT toast whose unsubscribe fails like a closed object (RO_E_CLOSED...).</summary>
internal sealed class FakeToastNotification(Exception removeFailure) : IToastNotification
{
    public int RemoveCount { get; private set; }

    public int RemoveFailedCount { get; private set; }

    public void AddActivated(nint handler, out long token) => token = 42;

    public void RemoveActivated(long token)
    {
        RemoveCount++;
        throw removeFailure;
    }

    public void AddFailed(nint handler, out long token) => token = 43;

    public void RemoveFailed(long token)
    {
        RemoveFailedCount++;
        throw removeFailure;
    }

    public void GetIids(out uint iidCount, out nint iids) => throw new NotSupportedException();

    public void GetRuntimeClassName(out nint className) => throw new NotSupportedException();

    public void GetTrustLevel(out int trustLevel) => throw new NotSupportedException();

    public void GetContent(out nint value) => throw new NotSupportedException();

    public void PutExpirationTime(nint value) => throw new NotSupportedException();

    public void GetExpirationTime(out nint value) => throw new NotSupportedException();

    public void AddDismissed(nint handler, out long token) => throw new NotSupportedException();

    public void RemoveDismissed(long token) => throw new NotSupportedException();
}
