using System.Runtime.Versioning;

using LayerPeek.App.Platform.Windows;

namespace LayerPeek.App.Tests.Platform.Windows;

/// <summary>
/// Real IShellLinkW / IPropertyStore interop, but only in a temp folder: the Start menu is never touched.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsShortcutStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly WindowsShortcutStore _store = new();

    public WindowsShortcutStoreTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Shell links exist only on Windows.");
        _directory = TestShortcuts.NewDirectory();
    }

    public void Dispose()
    {
        if (_directory is not null)
        {
            TestShortcuts.Delete(_directory);
        }
    }

    private static string ExistingTarget => Environment.ProcessPath!;

    private static string OtherExistingTarget => typeof(WindowsShortcutStoreTests).Assembly.Location;

    /// <summary>
    /// T-017: the Start menu shortcut Velopack created keeps its description and icon when the app adds its AppUserModelID
    /// and toast activator; only those properties change.
    /// </summary>
    [Fact]
    public void AnExistingShortcutKeepsItsOtherPropertiesWhenTheIdentityIsAdded()
    {
        string path = Path.Combine(_directory, "existing.lnk");
        string icon = Path.Combine(Environment.SystemDirectory, "shell32.dll");
        Type shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell is not available.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic link = shell.CreateShortcut(path);
        link.TargetPath = ExistingTarget;
        link.Description = "Made by the installer";
        link.IconLocation = icon + ",3";
        link.Save();
        Guid clsid = new("0B8D1A2C-3E4F-4A5B-9C6D-7E8F9A0B1C2D");

        _store.Write(path, new AppShortcut(ExistingTarget, "LayerPeek.Tests", clsid));

        dynamic reread = shell.CreateShortcut(path);
        Assert.Equal("Made by the installer", (string)reread.Description);
        Assert.Equal(icon + ",3", (string)reread.IconLocation, ignoreCase: true);
        Assert.Equal(new AppShortcut(ExistingTarget, "LayerPeek.Tests", clsid), _store.Read(path));
    }

    /// <summary>A loaded shortcut loses the id and the activator that are left out, and keeps its description.</summary>
    [Fact]
    public void ValuesLeftOutAreClearedOnALoadedShortcut()
    {
        string path = Path.Combine(_directory, "clear.lnk");
        Type shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell is not available.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic link = shell.CreateShortcut(path);
        link.TargetPath = ExistingTarget;
        link.Description = "Made by the installer";
        link.Save();
        _store.Write(path, new AppShortcut(ExistingTarget, "LayerPeek.Tests", new Guid("0B8D1A2C-3E4F-4A5B-9C6D-7E8F9A0B1C2D")));

        _store.Write(path, new AppShortcut(ExistingTarget, null, null));

        Assert.Equal(new AppShortcut(ExistingTarget, null, null), _store.Read(path));
        Assert.Equal("Made by the installer", (string)shell.CreateShortcut(path).Description);
    }

    [Fact]
    public void AnExistingShortcutOfAnotherTargetIsPointedToTheNewOne()
    {
        string path = Path.Combine(_directory, "moved.lnk");
        _store.Write(path, new AppShortcut(OtherExistingTarget, "LayerPeek.Tests"));

        _store.Write(path, new AppShortcut(ExistingTarget, "LayerPeek.Tests"));

        AppShortcut? shortcut = _store.Read(path);
        Assert.Equal(ExistingTarget, shortcut?.TargetPath, ignoreCase: true);
        Assert.Equal("LayerPeek.Tests", shortcut?.AppUserModelId);
    }

    [Fact]
    public void MissingShortcutReadsAsNull()
    {
        Assert.Null(_store.Read(Path.Combine(_directory, "missing.lnk")));
    }

    [Fact]
    public void WrittenShortcutReadsBack()
    {
        string path = Path.Combine(_directory, "LayerPeek.lnk");
        AppShortcut shortcut = new(ExistingTarget, "LayerPeek.Tests");

        _store.Write(path, shortcut);

        Assert.True(File.Exists(path));
        AppShortcut? read = _store.Read(path);
        Assert.NotNull(read);
        Assert.Equal(shortcut.TargetPath, read.TargetPath, ignoreCase: true);
        Assert.Equal(shortcut.AppUserModelId, read.AppUserModelId);
        Assert.Equal(ShortcutAction.UpToDate, AppShortcutRegistration.Decide(read, shortcut));
    }

    [Fact]
    public void ToastActivatorReadsBack()
    {
        string path = Path.Combine(_directory, "activator.lnk");
        AppShortcut shortcut = new(ExistingTarget, "LayerPeek.Tests", new Guid("0B8D1A2C-3E4F-4A5B-9C6D-7E8F9A0B1C2D"));

        _store.Write(path, shortcut);

        AppShortcut? read = _store.Read(path);
        Assert.NotNull(read);
        Assert.Equal(shortcut.ToastActivatorClsid, read.ToastActivatorClsid);
        Assert.Equal("LayerPeek.Tests", read.AppUserModelId);
        Assert.Equal(ShortcutAction.UpToDate, AppShortcutRegistration.Decide(read, shortcut));

        // Rewritten without one, the shortcut has none.
        _store.Write(path, shortcut with { ToastActivatorClsid = null });
        Assert.Null(_store.Read(path)?.ToastActivatorClsid);
    }

    [Fact]
    public void RewritingUpdatesTargetAndId()
    {
        string path = Path.Combine(_directory, "app.lnk");
        _store.Write(path, new AppShortcut(ExistingTarget, "First.Id"));

        _store.Write(path, new AppShortcut(OtherExistingTarget, "Second.Id"));

        AppShortcut? read = _store.Read(path);
        Assert.NotNull(read);
        Assert.Equal(OtherExistingTarget, read.TargetPath, ignoreCase: true);
        Assert.Equal("Second.Id", read.AppUserModelId);
    }

    [Fact]
    public void ShortcutWithoutIdReadsWithNullId()
    {
        string path = Path.Combine(_directory, "plain.lnk");
        _store.Write(path, new AppShortcut(ExistingTarget, "Temp.Id"));
        _store.Write(path, new AppShortcut(ExistingTarget, null));

        AppShortcut? read = _store.Read(path);

        Assert.NotNull(read);
        Assert.Null(read.AppUserModelId);
    }

    [Fact]
    public void CorruptShortcutFailsToRead()
    {
        string path = Path.Combine(_directory, "corrupt.lnk");
        File.WriteAllText(path, "not a shell link");

        Assert.ThrowsAny<Exception>(() => _store.Read(path));
    }

    [Fact]
    public void WorksFromAnStaThread()
    {
        string path = Path.Combine(_directory, "thread.lnk");
        Exception? failure = null;
        AppShortcut? read = null;
        Thread thread = new(() =>
        {
            try
            {
                _store.Write(path, new AppShortcut(ExistingTarget, "Thread.Id"));
                read = _store.Read(path);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.Equal("Thread.Id", read?.AppUserModelId);
    }
}
