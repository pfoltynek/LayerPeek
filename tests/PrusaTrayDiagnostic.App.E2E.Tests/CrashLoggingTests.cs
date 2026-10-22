using PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

namespace PrusaTrayDiagnostic.App.E2E.Tests;

/// <summary>
/// Deliberate crashes of the real app (T-046, Debug-only test command <c>crash:&lt;kind&gt;</c>): the unhandled exception
/// is in the log with its stack trace before the process ends (also against the Native AOT exe with
/// <c>tools/run-e2e.ps1 -PublishAot</c>), the app crashes as before, and an unobserved task exception is logged while the
/// app goes on.
/// </summary>
public sealed class CrashLoggingTests(E2EFixture fixture)
{
    private const string Category = "PrusaTrayDiagnostic.App.UnhandledExceptions";

    [Fact]
    public async Task CrashOnTheUiThreadEndsTheAppWithItsStackTraceInTheLog()
    {
        E2EFixture.Require();
        await using AppInstance app = await AppInstance.StartAsync(fixture, Name("crash-ui"), TestSettings.Json("en", []));

        DateTime crashed = DateTime.Now;
        await app.SendAsync("crash:ui");

        await ExitedAsync(app, crashed);
        string[] record = CrashRecord(app, "ui", "Unhandled exception on the UI thread.", " CRT ");
        Assert.Contains("System.InvalidOperationException: E2E test crash on the UI thread.", record[1], StringComparison.Ordinal);
        Assert.Contains(record, static line => line.Contains("TestCrash.ThrowOnUiThread", StringComparison.Ordinal));
        // Logged once: the process handler that then ends the app sees the same exception.
        Assert.Single(app.ReadLog().Split('\n'), static line => line.Contains($"] {Category}: ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CrashOnABackgroundThreadEndsTheAppWithItsStackTraceInTheLog()
    {
        E2EFixture.Require();
        await using AppInstance app = await AppInstance.StartAsync(fixture, Name("crash-background"), TestSettings.Json("en", []));

        DateTime crashed = DateTime.Now;
        await app.SendAsync("crash:background");

        await ExitedAsync(app, crashed);
        string[] record = CrashRecord(app, "background", "Unhandled exception (the process ends: True).", " CRT ");
        Assert.Contains("System.InvalidOperationException: E2E test crash on a background thread.", record[1], StringComparison.Ordinal);
        Assert.Contains(record, static line => line.Contains("TestCrash.ThrowOnBackgroundThread", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnobservedTaskExceptionIsLoggedAndTheAppGoesOn()
    {
        E2EFixture.Require();
        await using AppInstance app = await AppInstance.StartAsync(fixture, Name("crash-task"), TestSettings.Json("en", []));

        await app.SendAsync("crash:task");

        const string Message = "A task failed and nobody observed its exception; the app goes on.";
        await Wait.UntilAsync(() => app.ReadLog().Contains(Message, StringComparison.Ordinal), TimeSpan.FromSeconds(20), "the unobserved task in the log");
        string[] record = CrashRecord(app, "task", Message, " ERR ");
        Assert.Contains(record, static line => line.Contains("E2E test exception of an unobserved task.", StringComparison.Ordinal));
        Assert.Contains(record, static line => line.Contains("TestCrash.ThrowInUnobservedTask", StringComparison.Ordinal));
        // No change of behaviour: the app still runs and still takes commands.
        Assert.False(app.Process.HasExited);
        await app.OpenAsync("about", AppWindowKind.Other);
    }

    private static string Name(string test) => E2EFixture.IsNativeAot ? test + "-aot" : test;

    private static async Task ExitedAsync(AppInstance app, DateTime crashed)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        await app.Process.WaitForExitAsync(timeout.Token);
        await CrashArtifacts.RemoveAsync(app.Process.Id, crashed);
        // A crash, not a normal end.
        Assert.NotEqual(0, app.Process.ExitCode);
    }

    /// <summary>The record with <paramref name="message"/> and its indented exception lines; saved to the results.</summary>
    private static string[] CrashRecord(AppInstance app, string kind, string message, string level)
    {
        string[] lines = app.ReadLog().Split('\n');
        string header = $"[pid {app.Process.Id}] {Category}: {message}";
        int start = Array.FindIndex(lines, line => line.EndsWith(header, StringComparison.Ordinal));
        Assert.True(start >= 0, $"No '{header}' in the log.");
        Assert.Contains(level, lines[start], StringComparison.Ordinal);
        string[] record = [lines[start], .. lines.Skip(start + 1).TakeWhile(static line => line.StartsWith("    ", StringComparison.Ordinal))];
        Screenshots.SaveText(string.Join('\n', record), $"crash-{kind}{(E2EFixture.IsNativeAot ? "-aot" : string.Empty)}");
        Assert.True(record.Length > 2, "The exception has no stack trace.");
        Assert.Contains(record, static line => line.TrimStart().StartsWith("at ", StringComparison.Ordinal));
        return record;
    }
}
