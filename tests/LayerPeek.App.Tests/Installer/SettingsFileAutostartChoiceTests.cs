using LayerPeek.App.Installer;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tests.Installer;

/// <summary>
/// T-201: the install hook reads the start after logon the user chose from the settings file of the data folder; a folder
/// of the test assembly stands in for <c>%APPDATA%\LayerPeek</c>.
/// </summary>
public sealed class SettingsFileAutostartChoiceTests : IDisposable
{
    private readonly string _directory = TestDirectories.NewDirectoryPath();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => TestDirectories.Delete(_directory);

    [Fact]
    public void WithoutSettingsThereIsNoChoice()
    {
        Assert.Null(new SettingsFileAutostartChoice(() => _directory).Read());
        Assert.False(Directory.Exists(_directory));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheSavedChoiceIsRead(bool start)
    {
        using (JsonSettingsStore store = new(_directory))
        {
            await store.SaveAsync(new AppSettings { StartAtLogin = start }, Ct);
        }

        Assert.Equal(start, new SettingsFileAutostartChoice(() => _directory).Read());
    }

    /// <summary>The folder is resolved when reading (a folder that cannot be resolved fails the read, not the hook's setup).</summary>
    [Fact]
    public async Task TheFolderIsResolvedWhenReading()
    {
        int resolved = 0;
        string? folder = null;
        SettingsFileAutostartChoice choice = new(() =>
        {
            resolved++;
            return folder ?? throw new InvalidOperationException("no folder yet");
        });

        Assert.Equal(0, resolved);
        Assert.Throws<InvalidOperationException>(() => choice.Read());

        using (JsonSettingsStore store = new(_directory))
        {
            await store.SaveAsync(new AppSettings { StartAtLogin = false }, Ct);
        }

        folder = _directory;
        Assert.False(choice.Read());
        Assert.Equal(2, resolved);
    }

    [Fact]
    public void ACorruptedFileThrows()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, JsonSettingsStore.FileName), "{ damaged");

        Assert.ThrowsAny<Exception>(() => new SettingsFileAutostartChoice(() => _directory).Read());
    }
}
