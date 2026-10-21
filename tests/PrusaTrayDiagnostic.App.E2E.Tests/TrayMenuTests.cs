using PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

namespace PrusaTrayDiagnostic.App.E2E.Tests;

/// <summary>
/// The real tray menu stays a manual check (T-044): the icon of a test app sits in the notification area next to (and
/// looks exactly like) the user's own running instance, Windows 11 often hides it in the overflow flyout, and opening it
/// needs a real click into the taskbar, which UI Automation cannot do reliably without moving the user's mouse. The menu
/// contents and commands are covered by the model, presenter and headless tests (T-031, T-042); the windows it opens are
/// covered here through the same commands of the instance channel.
/// </summary>
public sealed class TrayMenuTests
{
    [Fact]
    public void RealNotificationAreaMenuIsCheckedManually()
    {
        E2EFixture.Require();
        Assert.Skip("Manual check (see the class remarks and the notes of T-044): the real notification-area menu is not automated.");
    }
}
