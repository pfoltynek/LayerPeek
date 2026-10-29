using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Platform.MacOS;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// "Open at Login" on macOS (T-019): the Login Item counts as on only when macOS opens the app at login; switching changes
/// it only when needed; an item the user switched off in System Settings cannot be switched on by the app. In memory only:
/// nothing here touches SMAppService or the user's Login Items.
/// </summary>
public sealed class LoginItemAutostartTests
{
    private readonly FakeLoginItem _item = new();

    private LoginItemAutostart Autostart() => new(_item, NullLogger.Instance);

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public void OnOnlyWhenMacOSOpensTheAppAtLogin(int status, bool enabled)
    {
        _item.Current = (LoginItemStatus)status;

        Assert.Equal(enabled, Autostart().IsEnabled());
    }

    [Fact]
    public void AnUnreadableStatusCountsAsOff()
    {
        _item.StatusFailure = new LoginItemException("XPC failed");

        Assert.False(Autostart().IsEnabled());
    }

    [Fact]
    public void SwitchingOnRegistersOnce()
    {
        LoginItemAutostart autostart = Autostart();

        autostart.SetEnabled(true);
        autostart.SetEnabled(true);

        Assert.Equal(LoginItemStatus.Enabled, _item.Current);
        Assert.Equal(["register"], _item.Calls);
    }

    [Fact]
    public void SwitchingOffUnregistersOnlyARegisteredItem()
    {
        LoginItemAutostart autostart = Autostart();
        autostart.SetEnabled(false);
        Assert.Empty(_item.Calls);

        _item.Current = LoginItemStatus.Enabled;
        autostart.SetEnabled(false);

        Assert.Equal(LoginItemStatus.NotRegistered, _item.Current);
        Assert.Equal(["unregister"], _item.Calls);
    }

    /// <summary>The user switched the item off in System Settings: only they can allow it again, the app reports a failure.</summary>
    [Fact]
    public void AnItemTheUserSwitchedOffCannotBeSwitchedOnByTheApp()
    {
        _item.Current = LoginItemStatus.RequiresApproval;

        Assert.Throws<LoginItemException>(() => Autostart().SetEnabled(true));
        Assert.Empty(_item.Calls);
    }

    [Fact]
    public void AnItemTheUserSwitchedOffIsRemovedBySwitchingOff()
    {
        _item.Current = LoginItemStatus.RequiresApproval;

        Autostart().SetEnabled(false);

        Assert.Equal(["unregister"], _item.Calls);
    }

    [Fact]
    public void ARegistrationMacOSDoesNotAllowIsAFailure()
    {
        _item.AfterRegister = LoginItemStatus.RequiresApproval;

        Assert.Throws<LoginItemException>(() => Autostart().SetEnabled(true));
        Assert.Equal(["register"], _item.Calls);
    }

    [Fact]
    public void ARefusalOfMacOSReachesTheCaller()
    {
        _item.ChangeFailure = new LoginItemException("denied") { Code = 11 };

        LoginItemException failure = Assert.Throws<LoginItemException>(() => Autostart().SetEnabled(true));
        Assert.Equal(11, failure.Code);
    }

    private sealed class FakeLoginItem : ILoginItemService
    {
        public LoginItemStatus Current { get; set; } = LoginItemStatus.NotRegistered;

        public LoginItemStatus AfterRegister { get; set; } = LoginItemStatus.Enabled;

        public Exception? StatusFailure { get; set; }

        public Exception? ChangeFailure { get; set; }

        public List<string> Calls { get; } = [];

        public LoginItemStatus Status() => StatusFailure is { } failure ? throw failure : Current;

        public void Register()
        {
            Calls.Add("register");
            if (ChangeFailure is { } failure)
            {
                throw failure;
            }

            Current = AfterRegister;
        }

        public void Unregister()
        {
            Calls.Add("unregister");
            if (ChangeFailure is { } failure)
            {
                throw failure;
            }

            Current = LoginItemStatus.NotRegistered;
        }
    }
}
