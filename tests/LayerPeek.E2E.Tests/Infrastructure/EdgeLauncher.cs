using Microsoft.Playwright;

namespace LayerPeek.E2E.Tests.Infrastructure;

/// <summary>Launches the installed Edge headless; a missing Edge is a skip reason, never a download.</summary>
internal static class EdgeLauncher
{
    /// <summary>The first start of Edge on a cold machine takes up to ~20 s.</summary>
    private const float LaunchTimeoutMs = 60_000;

    /// <summary>
    /// Every host name except 127.0.0.1 fails to resolve and no system proxy is used, so the browser cannot reach
    /// anything outside this machine (also no background traffic of Edge itself).
    /// </summary>
    private static readonly string[] LoopbackOnlyArgs = ["--host-resolver-rules=MAP * ~NOTFOUND , EXCLUDE 127.0.0.1", "--no-proxy-server"];

    public static async Task<(IBrowser? Browser, string? SkipReason)> LaunchAsync(IBrowserType chromium, BrowserTypeLaunchOptions options)
    {
        Configure(options);
        try
        {
            return (await chromium.LaunchAsync(options), null);
        }
        catch (PlaywrightException ex) when (IsBrowserMissing(ex))
        {
            return (null, SkipReason(options.Channel, ex));
        }
    }

    /// <summary>A browser with its own profile directory (one context), for tests of the browser's own lifecycle.</summary>
    public static async Task<(IBrowserContext? Context, string? SkipReason)> LaunchPersistentAsync(IBrowserType chromium, string userDataDir)
    {
        BrowserTypeLaunchPersistentContextOptions options = new()
        {
            Channel = EdgeFixture.Channel,
            Headless = true,
            Timeout = LaunchTimeoutMs,
            Args = LoopbackOnlyArgs,
        };
        try
        {
            return (await chromium.LaunchPersistentContextAsync(userDataDir, options), null);
        }
        catch (PlaywrightException ex) when (IsBrowserMissing(ex))
        {
            return (null, SkipReason(options.Channel, ex));
        }
    }

    /// <summary>
    /// The driver's errors for a channel that is not installed ("Chromium distribution 'msedge' is not found at ...",
    /// "... is not supported on darwin") or an executable path that does not exist. Any other launch error fails the test.
    /// </summary>
    internal static bool IsBrowserMissing(PlaywrightException exception) =>
        (exception.Message.Contains("Chromium distribution '", StringComparison.Ordinal)
            && (exception.Message.Contains("' is not found", StringComparison.Ordinal) || exception.Message.Contains("' is not supported on", StringComparison.Ordinal)))
        || exception.Message.Contains("executable doesn't exist", StringComparison.OrdinalIgnoreCase);

    private static void Configure(BrowserTypeLaunchOptions options)
    {
        if (options.ExecutablePath is null)
        {
            options.Channel ??= EdgeFixture.Channel;
        }

        options.Headless = true;
        options.Timeout = LaunchTimeoutMs;
        options.Args = [.. options.Args ?? [], .. LoopbackOnlyArgs];
    }

    private static string SkipReason(string? channel, PlaywrightException exception)
    {
        // The driver suggests "playwright install"; the tests deliberately never download a browser.
        string firstLine = exception.Message.Split('\n', 2)[0].Trim();
        return $"Microsoft Edge (Playwright channel '{channel}') is not installed, browser tests are skipped. Install Edge to run them. ({firstLine})";
    }
}
