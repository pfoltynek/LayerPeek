namespace LayerPeek.App.Platform;

/// <summary>
/// What the OS shell needs to know about an auxiliary window (T-043), by its native handle (HWND on Windows). Windows:
/// taskbar properties (only "Close window" in its jump list) and flashing the taskbar button. Other OSes: nothing yet
/// (macOS: T-018). Calls never throw; UI thread.
/// </summary>
internal interface IWindowShell
{
    /// <summary>The window was created and is not shown yet.</summary>
    void Prepare(nint handle);

    /// <summary>The window closed; what <see cref="Prepare"/> set is removed.</summary>
    void Release(nint handle);

    /// <summary>The window was asked to come to the front; draws attention to it if the OS refused that.</summary>
    void RequestAttention(nint handle);
}

/// <summary>An OS without any of it.</summary>
internal sealed class NoWindowShell : IWindowShell
{
    public static readonly NoWindowShell Instance = new();

    public void Prepare(nint handle)
    {
    }

    public void Release(nint handle)
    {
    }

    public void RequestAttention(nint handle)
    {
    }
}
