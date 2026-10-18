using System.Globalization;

using PrusaTrayDiagnostic.App.Localization;
using PrusaTrayDiagnostic.App.Notifications;
using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.App.Tests.Notifications;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.Core.Events;
using PrusaTrayDiagnostic.Core.Notifications;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Localization;

/// <summary>
/// Toasts (T-010) follow the language switch of T-029 like the tray does: the texts of a toast are formatted when it is
/// built, in the language active then, not in a language captured when the service was created. Switches the
/// process-wide language, so it runs alone (see <see cref="GlobalLanguageGroup"/>) and always restores the system language.
/// </summary>
[Collection(nameof(GlobalLanguageGroup))]
public sealed class ToastLanguageSwitchTests : IDisposable
{
    private readonly CultureInfo? _defaultThreadUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
    private readonly FakeToastPlatform _platform = new();
    private readonly ToastNotificationService _service;

    public ToastLanguageSwitchTests()
    {
        // A known system language for new threads (the toast thread), so that "follow the system" is predictable.
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        _service = new ToastNotificationService(_platform, static _ => { }, UiLanguage.CurrentTextCulture, new ListLogger<ToastNotificationService>());
    }

    public void Dispose()
    {
        _service.Dispose();
        _platform.Session.Release();
        // Never leave another language behind for the tests that run next.
        Strings.Culture = null;
        CultureInfo.DefaultThreadCurrentUICulture = _defaultThreadUiCulture;
    }

    [Fact]
    public async Task SwitchingLanguageChangesTheTextOfTheNextToast()
    {
        UiLanguage language = new();
        _service.Show(Notification(PrintEventKind.PrintFinished));
        await _platform.Session.WaitForShownAsync(1);

        language.Apply("de");
        _service.Show(Notification(PrintEventKind.PrintFinished));
        await _platform.Session.WaitForShownAsync(2);
        language.Apply("sk");
        _service.Show(Notification(PrintEventKind.PrintFinished));

        IReadOnlyList<string> shown = await _platform.Session.WaitForShownAsync(3);
        Assert.Contains("Print finished", shown[0], StringComparison.Ordinal);
        Assert.Contains("Druck abgeschlossen", shown[1], StringComparison.Ordinal);
        Assert.Contains("Tlač sa dokončila", shown[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SwitchingBackToTheSystemLanguageUsesTheSystemLanguageAgain()
    {
        UiLanguage language = new();
        language.Apply("fr");
        _service.Show(Notification(PrintEventKind.PrintPaused));
        await _platform.Session.WaitForShownAsync(1);

        language.Apply(null);
        _service.Show(Notification(PrintEventKind.PrintPaused));

        IReadOnlyList<string> shown = await _platform.Session.WaitForShownAsync(2);
        Assert.Contains("Impression en pause", shown[0], StringComparison.Ordinal);
        // The toast thread is not left in the previous language.
        Assert.Contains("Print paused", shown[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task CurrentTextCultureIsTheChosenLanguageOrTheSystemLanguage()
    {
        UiLanguage language = new();
        CultureInfo system = await Task.Run(UiLanguage.CurrentTextCulture, TestContext.Current.CancellationToken);
        Assert.Equal(CultureInfo.CurrentUICulture, UiLanguage.CurrentTextCulture());

        language.Apply("it");
        Assert.Equal("it", UiLanguage.CurrentTextCulture().Name);
        Assert.Equal("it", (await Task.Run(UiLanguage.CurrentTextCulture, TestContext.Current.CancellationToken)).Name);

        language.Apply(null);
        Assert.Equal(system, await Task.Run(UiLanguage.CurrentTextCulture, TestContext.Current.CancellationToken));
    }

    private static PrintNotification Notification(PrintEventKind kind) => new()
    {
        Printer = Definition(),
        Kind = kind,
        Timestamp = Now,
    };
}
