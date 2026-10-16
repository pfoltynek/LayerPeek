using System.Runtime.Versioning;

using PrusaTrayDiagnostic.App.Platform.Windows;

namespace PrusaTrayDiagnostic.App.Tests.Platform.Windows;

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

    [Fact]
    public void MissingShortcutReadsAsNull()
    {
        Assert.Null(_store.Read(Path.Combine(_directory, "missing.lnk")));
    }

    [Fact]
    public void WrittenShortcutReadsBack()
    {
        string path = Path.Combine(_directory, "Prusa Tray Diagnostic.lnk");
        AppShortcut shortcut = new(ExistingTarget, "PrusaTrayDiagnostic.Tests");

        _store.Write(path, shortcut);

        Assert.True(File.Exists(path));
        AppShortcut? read = _store.Read(path);
        Assert.NotNull(read);
        Assert.Equal(shortcut.TargetPath, read.TargetPath, ignoreCase: true);
        Assert.Equal(shortcut.AppUserModelId, read.AppUserModelId);
        Assert.Equal(ShortcutAction.UpToDate, AppShortcutRegistration.Decide(read, shortcut));
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
