using System.Runtime.InteropServices;

using PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

namespace PrusaTrayDiagnostic.App.E2E.Tests;

/// <summary>
/// Unit tests of <see cref="Wait"/> (T-073): a UI Automation call that times out while the app's UI thread is busy is
/// asked again instead of failing the test (the cause of the unstable save dialog test of T-064). They run in every
/// <c>dotnet test</c> and touch nothing of the system.
/// </summary>
public sealed class WaitTests
{
    /// <summary>UIA_E_TIMEOUT (the same value as COR_E_TIMEOUT).</summary>
    private const int UiaETimeout = unchecked((int)0x80131505);

    /// <summary>UIA_E_ELEMENTNOTAVAILABLE.</summary>
    private const int UiaEElementNotAvailable = unchecked((int)0x80040201);

    [Fact]
    public async Task UiaTimeoutInTheConditionIsAskedAgain()
    {
        int calls = 0;

        await Wait.UntilAsync(
            () => ++calls switch
            {
                // What the COM interop makes of the HRESULT, and what FlaUI makes of it.
                1 => throw Marshal.GetExceptionForHR(UiaETimeout)!,
                2 => throw new TimeoutException("UIA timeout"),
                _ => true,
            },
            TimeSpan.FromSeconds(10),
            "the condition");

        Assert.Equal(3, calls);
        Assert.True(Wait.IsUiaTimeout(Marshal.GetExceptionForHR(UiaETimeout)!));
    }

    [Fact]
    public async Task UiaTimeoutUntilTheDeadlineIsNamedInTheTimeout()
    {
        WaitTimeoutException timeout = await Assert.ThrowsAsync<WaitTimeoutException>(() => Wait.UntilAsync(
            static bool () => throw new TimeoutException("UIA timeout"),
            TimeSpan.FromMilliseconds(300),
            "the busy window"));

        Assert.Contains("the busy window", timeout.Message, StringComparison.Ordinal);
        Assert.Contains("UI Automation call timed out", timeout.Message, StringComparison.Ordinal);
        // An outer wait ends on it instead of waiting on.
        Assert.False(Wait.IsUiaTimeout(timeout));
    }

    [Fact]
    public async Task TimeoutOfANestedWaitEndsTheWait()
    {
        int calls = 0;

        await Assert.ThrowsAsync<WaitTimeoutException>(() => Wait.UntilAsync(
            async () =>
            {
                calls++;
                await Wait.UntilAsync(static () => false, TimeSpan.FromMilliseconds(100), "something nested");
                return true;
            },
            TimeSpan.FromSeconds(10),
            "the condition"));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task OtherComFailuresEndTheWait()
    {
        // A real failure of the check, not a busy moment.
        Exception failure = Marshal.GetExceptionForHR(UiaEElementNotAvailable)!;
        Assert.IsType<COMException>(failure);

        Exception thrown = await Assert.ThrowsAnyAsync<Exception>(() => Wait.UntilAsync(bool () => throw failure, TimeSpan.FromSeconds(10), "the condition"));

        Assert.Same(failure, thrown);
    }
}
