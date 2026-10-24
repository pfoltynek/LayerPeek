using Microsoft.Playwright;

using LayerPeek.E2E.Tests.Infrastructure;

namespace LayerPeek.E2E.Tests;

/// <summary>The test infrastructure itself: a missing Edge skips, a closed browser leaves no process and no file.</summary>
public sealed class BrowserLifecycleTests(EdgeFixture edge)
{
    [Fact]
    public async Task MissingBrowserIsASkipReasonNotADownload()
    {
        if (edge.SkipReason is not null)
        {
            Assert.Skip(edge.SkipReason);
        }

        string missing = Path.Combine(EdgeFixture.Root, "missing-" + Guid.NewGuid().ToString("N"), "msedge.exe");

        (IBrowser? browser, string? skipReason) = await EdgeLauncher.LaunchAsync(edge.Playwright.Chromium, new BrowserTypeLaunchOptions { ExecutablePath = missing });

        Assert.Null(browser);
        Assert.NotNull(skipReason);
        Assert.Contains("is not installed", skipReason, StringComparison.Ordinal);
        Assert.False(File.Exists(missing));
    }

    [Fact]
    public void ChannelNotInstalledIsRecognizedAsMissingBrowser()
    {
        // The driver's message for Channel = "msedge" without Edge (Playwright 1.63, coreBundle.js _createChromiumChannel).
        PlaywrightException notFound = new("""
            Chromium distribution 'msedge' is not found at C:\Users\someone\AppData\Local\Microsoft\Edge\Application\msedge.exe
            Run "pwsh bin/Debug/netX/playwright.ps1 install msedge"
            """);
        PlaywrightException crashed = new("Browser closed. Target page, context or browser has been closed");

        Assert.True(EdgeLauncher.IsBrowserMissing(notFound));
        Assert.False(EdgeLauncher.IsBrowserMissing(crashed));
    }

    [Fact]
    public void OnlyTheBrowserProfileSwitchMarksAnOwnProcess()
    {
        string marker = EdgeFixture.ProcessMarker;
        string profile = marker + "tmp" + Path.DirectorySeparatorChar + "playwright_chromiumdev_profile-AbC123";

        // Every Edge process of a run carries the profile, quoted (renderer, utility) or not (crashpad handler).
        Assert.True(OwnProcesses.IsOwnCommandLine($"msedge.exe --type=renderer --user-data-dir=\"{profile}\" --lang=en-US", marker));
        Assert.True(OwnProcesses.IsOwnCommandLine($"msedge.exe --type=crashpad-handler --user-data-dir={profile} /prefetch:4", marker));
        Assert.True(OwnProcesses.IsOwnCommandLine($"\"--user-data-dir={profile}\" --headless", marker));
        Assert.True(OwnProcesses.IsOwnCommandLine($"--user-data-dir={profile}", marker));
        // Paths and switch names are case-insensitive on Windows only.
        Assert.Equal(OperatingSystem.IsWindows(), OwnProcesses.IsOwnCommandLine($"msedge.exe --USER-DATA-DIR={profile.ToUpperInvariant()}", marker));
        // The switch only counts as an argument of its own and with the path right after it (one optional quote).
        Assert.False(OwnProcesses.IsOwnCommandLine($"tool.exe --x--user-data-dir={profile}", marker));
        Assert.False(OwnProcesses.IsOwnCommandLine($"tool.exe --user-data-dir=\"\"{profile}", marker));
        Assert.False(OwnProcesses.IsOwnCommandLine($"tool.exe --user-data-dir= {profile}", marker));
        // Sibling directories whose name starts like the reserved one (the marker ends with a separator).
        Assert.False(OwnProcesses.IsOwnCommandLine($"msedge.exe --user-data-dir=\"{EdgeFixture.Root}2{Path.DirectorySeparatorChar}p\"", marker));
        Assert.False(OwnProcesses.IsOwnCommandLine($"msedge.exe --user-data-dir={EdgeFixture.Root}", marker));
        // The user's own Edge opening a file that lies there, or a shell deleting the directory, is not ours.
        Assert.False(OwnProcesses.IsOwnCommandLine($"msedge.exe \"{profile}{Path.DirectorySeparatorChar}page.html\"", marker));
        Assert.False(OwnProcesses.IsOwnCommandLine($"cmd.exe /c rmdir /s /q \"{marker}\"", marker));
        // The user's Edge with its own profile, also one that merely mentions the path elsewhere.
        Assert.False(OwnProcesses.IsOwnCommandLine($"msedge.exe --user-data-dir=\"C:\\Users\\someone\\Edge\" --load-extension=\"{marker}x\"", marker));
        // A sibling directory with the same prefix.
        Assert.False(OwnProcesses.IsOwnCommandLine($"msedge.exe --user-data-dir=\"{EdgeFixture.Root}.Other{Path.DirectorySeparatorChar}p\"", marker));
    }

    [Fact(Timeout = 120_000)]
    public async Task DisposedRigReleasesThePrinterAndProxyPorts()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        PrinterRig rig = await PrinterRig.StartAsync(cancellationToken);
        int printerPort;
        int proxyPort;
        try
        {
            printerPort = rig.Printer.BaseAddress.Port;
            proxyPort = (await rig.OpenProxyAsync(cancellationToken)).Port;
            Assert.True(IsListening(printerPort));
            Assert.True(IsListening(proxyPort));
        }
        finally
        {
            await rig.DisposeAsync();
        }

        Assert.False(IsListening(printerPort));
        Assert.False(IsListening(proxyPort));
    }

    [Fact]
    public void SharedBrowserRunsUnderTheReservedDirectory()
    {
        if (edge.SkipReason is not null)
        {
            Assert.Skip(edge.SkipReason);
        }

        // Otherwise the leak check of the fixture (processes outliving the run) would find nothing by construction.
        List<System.Diagnostics.Process> running = OwnProcesses.Find(EdgeFixture.ProcessMarker);
        try
        {
            Assert.Contains(running, static p => p.ProcessName.Contains("edge", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            running.ForEach(static p => p.Dispose());
        }
    }

    [Fact]
    public async Task ClosedBrowserLeavesNoProcessAndNoProfile()
    {
        if (edge.SkipReason is not null)
        {
            Assert.Skip(edge.SkipReason);
        }

        string profile = Path.Combine(EdgeFixture.Root, "profile-" + Guid.NewGuid().ToString("N"));
        // Unique: the browser processes carry it in --user-data-dir.
        string marker = profile;
        (IBrowserContext? context, string? skipReason) = await EdgeLauncher.LaunchPersistentAsync(edge.Playwright.Chromium, profile);
        Assert.Null(skipReason);
        try
        {
            IPage page = await context!.NewPageAsync();
            await page.SetContentAsync("<p id=alive>alive</p>");
            Assert.Equal("alive", await page.Locator("#alive").TextContentAsync());
            // The detection of own processes works: the running browser is found by its profile directory.
            List<System.Diagnostics.Process> running = OwnProcesses.Find(marker);
            Assert.NotEmpty(running);
            running.ForEach(static p => p.Dispose());
        }
        finally
        {
            await context!.CloseAsync();
        }

        Assert.Empty(await OwnProcesses.WaitForExitOrKillAsync(marker, TimeSpan.FromSeconds(15)));
        Directory.Delete(profile, recursive: true);
        Assert.False(Directory.Exists(profile));
    }

    /// <summary>OS listener table (a refused connect to loopback takes seconds on Windows).</summary>
    private static bool IsListening(int port) =>
        System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e => e.Port == port);
}
