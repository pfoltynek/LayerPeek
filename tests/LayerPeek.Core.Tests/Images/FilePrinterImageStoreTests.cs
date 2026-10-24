using LayerPeek.Core.Images;
using LayerPeek.Core.Settings;

namespace LayerPeek.Core.Tests.Images;

public sealed class FilePrinterImageStoreTests : IDisposable
{
    private readonly string _settingsDirectory = TestDirectories.NewDirectoryPath();
    private readonly FilePrinterImageStore _store;

    public FilePrinterImageStoreTests()
    {
        _store = new FilePrinterImageStore(_settingsDirectory);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => TestDirectories.Delete(_settingsDirectory);

    [Fact]
    public void ImagesLiveNextToTheSettingsFile()
    {
        Assert.Equal(Path.Combine(Path.GetFullPath(_settingsDirectory), "printer-images"), _store.DirectoryPath);
        Assert.Equal(Path.Combine(_store.DirectoryPath, "mk4.png"), _store.GetFilePath("MK4"));
        Assert.Equal(
            Path.Combine(AppDataPaths.GetDefaultDirectory(), FilePrinterImageStore.DirectoryName),
            FilePrinterImageStore.CreateDefault().DirectoryPath);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../../escape")]
    [InlineData("..\\..\\escape")]
    [InlineData("/absolute")]
    public void PathsNeverLeaveTheImageFolder(string id)
    {
        string path = _store.GetFilePath(id);

        Assert.Equal(_store.DirectoryPath, Path.GetDirectoryName(Path.GetFullPath(path)));
    }

    [Fact]
    public async Task SavedImageIsReadBack()
    {
        byte[] png = ImageTestData.Png(1000);

        await _store.SaveAsync("mk4", png, Ct);

        Assert.Equal(png, await _store.ReadAsync("mk4", Ct));
        Assert.Equal(png, await _store.ReadAsync("Mk4", Ct));
        Assert.Equal([_store.GetFilePath("mk4")], Directory.GetFiles(_store.DirectoryPath));
    }

    [Fact]
    public async Task SaveReplacesThePreviousImageAndLeavesNoTemporaryFile()
    {
        await _store.SaveAsync("mk4", ImageTestData.Png(100), Ct);
        byte[] second = ImageTestData.Png(200);

        await _store.SaveAsync("mk4", second, Ct);

        Assert.Equal(second, await _store.ReadAsync("mk4", Ct));
        Assert.Single(Directory.GetFiles(_store.DirectoryPath));
    }

    public static TheoryData<byte[]> NotStorable =>
    [
        Array.Empty<byte>(),
        ImageTestData.Jpeg(),
        "plain text"u8.ToArray(),
        ImageTestData.Png(PrinterImageFormats.MaxStoredBytes + 1),
    ];

    [Theory]
    [MemberData(nameof(NotStorable))]
    public async Task SaveRejectsAnythingButAStorablePng(byte[] content)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _store.SaveAsync("mk4", content, Ct));

        Assert.False(Directory.Exists(_store.DirectoryPath));
    }

    [Fact]
    public async Task SaveWhenCancelledKeepsThePreviousImage()
    {
        byte[] original = ImageTestData.Png(100);
        await _store.SaveAsync("mk4", original, Ct);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _store.SaveAsync("mk4", ImageTestData.Png(200), new CancellationToken(canceled: true)));

        Assert.Equal(original, await _store.ReadAsync("mk4", Ct));
        Assert.Single(Directory.GetFiles(_store.DirectoryPath));
    }

    [Fact]
    public async Task SaveWhenReplaceFailsThrowsAndRemovesTemporaryFile()
    {
        // A directory in place of the image makes the final replace step fail.
        Directory.CreateDirectory(_store.GetFilePath("mk4"));

        await Assert.ThrowsAnyAsync<Exception>(() => _store.SaveAsync("mk4", ImageTestData.Png(), Ct));

        Assert.Empty(Directory.GetFiles(_store.DirectoryPath));
    }

    [Fact]
    public async Task ReadOfMissingImageReturnsNull()
    {
        Assert.Null(await _store.ReadAsync("mk4", Ct));

        Directory.CreateDirectory(_store.DirectoryPath);
        Assert.Null(await _store.ReadAsync("mk4", Ct));
    }

    public static TheoryData<byte[]> Foreign =>
    [
        Array.Empty<byte>(),
        ImageTestData.Jpeg(),
        "not an image"u8.ToArray(),
        ImageTestData.Png(PrinterImageFormats.MaxStoredBytes + 1),
    ];

    [Theory]
    [MemberData(nameof(Foreign))]
    public async Task ReadIgnoresForeignOrDamagedFiles(byte[] content)
    {
        Directory.CreateDirectory(_store.DirectoryPath);
        await File.WriteAllBytesAsync(_store.GetFilePath("mk4"), content, Ct);

        Assert.Null(await _store.ReadAsync("mk4", Ct));
    }

    [Fact]
    public async Task DeleteRemovesOnlyThatImage()
    {
        await _store.SaveAsync("mk4", ImageTestData.Png(), Ct);
        await _store.SaveAsync("xl", ImageTestData.Png(), Ct);

        Assert.True(await _store.DeleteAsync("MK4", Ct));

        Assert.Null(await _store.ReadAsync("mk4", Ct));
        Assert.NotNull(await _store.ReadAsync("xl", Ct));
    }

    [Fact]
    public async Task DeleteOfMissingImageReturnsFalse()
    {
        Assert.False(await _store.DeleteAsync("mk4", Ct));

        Directory.CreateDirectory(_store.DirectoryPath);
        Assert.False(await _store.DeleteAsync("mk4", Ct));
    }

    [Fact]
    public async Task RemoveOrphansKeepsImagesOfConfiguredPrinters()
    {
        await _store.SaveAsync("mk4", ImageTestData.Png(), Ct);
        await _store.SaveAsync("removed", ImageTestData.Png(), Ct);
        await _store.SaveAsync("tiskárna", ImageTestData.Png(), Ct);
        string leftoverTemp = Path.Combine(_store.DirectoryPath, "mk4.png.0123456789abcdef0123456789abcdef.tmp");
        string unrelated = Path.Combine(_store.DirectoryPath, "readme.txt");
        string subfolder = Path.Combine(_store.DirectoryPath, "keep.png");
        await File.WriteAllBytesAsync(leftoverTemp, [1, 2, 3], Ct);
        await File.WriteAllTextAsync(unrelated, "x", Ct);
        Directory.CreateDirectory(subfolder);

        int deleted = await _store.RemoveOrphansAsync(["MK4", "TISKÁRNA", "never-had-an-image"], Ct);

        Assert.Equal(2, deleted);
        Assert.Equal(
            new[] { _store.GetFilePath("mk4"), _store.GetFilePath("tiskárna"), unrelated }.Order(StringComparer.Ordinal),
            Directory.GetFiles(_store.DirectoryPath).Order(StringComparer.Ordinal));
        Assert.True(Directory.Exists(subfolder));
    }

    [Fact]
    public async Task RemoveOrphansWithoutFolderDoesNothing()
    {
        Assert.Equal(0, await _store.RemoveOrphansAsync(["mk4"], Ct));
        Assert.False(Directory.Exists(_store.DirectoryPath));
    }

    [Fact]
    public async Task RemoveOrphansWithNoPrintersDeletesEveryImage()
    {
        await _store.SaveAsync("a", ImageTestData.Png(), Ct);
        await _store.SaveAsync("b", ImageTestData.Png(), Ct);

        Assert.Equal(2, await _store.RemoveOrphansAsync([], Ct));
        Assert.Empty(Directory.GetFiles(_store.DirectoryPath));
    }

    [Fact]
    public async Task RemoveOrphansSkipsFilesThatCannotBeDeleted()
    {
        await _store.SaveAsync("open", ImageTestData.Png(), Ct);
        await _store.SaveAsync("closed", ImageTestData.Png(), Ct);
        int deleted;
        using (new FileStream(_store.GetFilePath("open"), FileMode.Open, FileAccess.Read, OperatingSystem.IsWindows() ? FileShare.None : FileShare.Read))
        {
            deleted = await _store.RemoveOrphansAsync([], Ct);
        }

        if (OperatingSystem.IsWindows())
        {
            // Windows refuses to delete an open file; the sweep goes on with the others.
            Assert.Equal(1, deleted);
            Assert.True(File.Exists(_store.GetFilePath("open")));
            await _store.RemoveOrphansAsync([], Ct);
        }

        Assert.False(File.Exists(_store.GetFilePath("closed")));
    }
}
