namespace LayerPeek.App.Platform.MacOS;

/// <summary>The state of the app's Login Item (values of <c>SMAppServiceStatus</c>).</summary>
internal enum LoginItemStatus
{
    /// <summary>Not registered: the app does not open at login.</summary>
    NotRegistered = 0,

    /// <summary>Registered and allowed: the app opens at login.</summary>
    Enabled = 1,

    /// <summary>Registered, but switched off by the user in System Settings &gt; General &gt; Login Items.</summary>
    RequiresApproval = 2,

    /// <summary>macOS does not know the item (also: the app is not a bundle macOS can register).</summary>
    NotFound = 3,
}

/// <summary>
/// The Login Item of the running app (T-019): the real one of <c>SMAppService.mainAppService</c>
/// (<see cref="SMAppServiceLoginItem"/>), or in the test mode a stand-in file in the test data folder
/// (<see cref="StandInLoginItem"/>), so that a test never changes the user's Login Items. Calls may block (XPC): call them
/// off the UI thread.
/// </summary>
internal interface ILoginItemService
{
    LoginItemStatus Status();

    /// <exception cref="LoginItemException">macOS refused it.</exception>
    void Register();

    /// <exception cref="LoginItemException">macOS refused it.</exception>
    void Unregister();

    /// <summary>
    /// Opens System Settings &gt; General &gt; Login Items, where the user allows an item in the state
    /// <see cref="LoginItemStatus.RequiresApproval"/> (user decision 2026-10-29). The stand-in of the test mode opens nothing.
    /// </summary>
    /// <exception cref="LoginItemException">macOS could not open it.</exception>
    void OpenSystemSettings();
}

/// <summary>macOS refused to change the Login Item; the message carries the code and text of its <c>NSError</c>.</summary>
internal sealed class LoginItemException : Exception
{
    public LoginItemException()
    {
    }

    public LoginItemException(string message)
        : base(message)
    {
    }

    public LoginItemException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The <c>NSError</c> code (<c>kSMError…</c>), 0 when macOS gave none.</summary>
    public long Code { get; init; }
}
