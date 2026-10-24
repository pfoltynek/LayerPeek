using LayerPeek.Core.Images;

namespace LayerPeek.Core.Tests.Images;

public sealed class PrinterImageImporterTests : IDisposable
{
    private readonly string _directory = TestDirectories.NewDirectoryPath();
    private readonly FakeImageProcessor _processor = new();
    private readonly PrinterImageImporter _importer;

    public PrinterImageImporterTests()
    {
        Directory.CreateDirectory(_directory);
        _importer = new PrinterImageImporter(_processor);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => TestDirectories.Delete(_directory);

    [Fact]
    public async Task PngIsConvertedToAtMost512Pixels()
    {
        byte[] result = ImageTestData.Png(300);
        _processor.Result = () => result;

        PrinterImageImportResult import = await _importer.ImportFileAsync(await WriteAsync("photo.png", ImageTestData.Png(1000)), Ct);

        Assert.Equal(PrinterImageImportStatus.Imported, import.Status);
        Assert.Same(result, import.Png);
        Assert.Equal([(1000, PrinterImageFormat.Png, 512)], _processor.Calls);
    }

    [Fact]
    public async Task JpegIsAccepted()
    {
        PrinterImageImportResult import = await _importer.ImportFileAsync(await WriteAsync("photo.jpg", ImageTestData.Jpeg(1000)), Ct);

        Assert.Equal(PrinterImageImportStatus.Imported, import.Status);
        Assert.Equal(PrinterImageFormat.Jpeg, Assert.Single(_processor.Calls).Format);
    }

    [Fact]
    public async Task FormatIsTakenFromTheContentNotTheName()
    {
        PrinterImageImportResult import = await _importer.ImportFileAsync(await WriteAsync("photo.png", ImageTestData.Jpeg()), Ct);

        Assert.Equal(PrinterImageImportStatus.Imported, import.Status);
        Assert.Equal(PrinterImageFormat.Jpeg, Assert.Single(_processor.Calls).Format);
    }

    [Theory]
    [InlineData("image.png")]
    [InlineData("image.gif")]
    public async Task OtherContentIsUnsupported(string name)
    {
        PrinterImageImportResult import = await _importer.ImportFileAsync(await WriteAsync(name, "GIF89a-not-a-png"u8.ToArray()), Ct);

        Assert.Equal(new PrinterImageImportResult(PrinterImageImportStatus.UnsupportedFormat), import);
        Assert.Empty(_processor.Calls);
    }

    [Fact]
    public async Task EmptyFileIsUnsupported()
    {
        PrinterImageImportResult import = await _importer.ImportFileAsync(await WriteAsync("empty.png", []), Ct);

        Assert.Equal(PrinterImageImportStatus.UnsupportedFormat, import.Status);
        Assert.Empty(_processor.Calls);
    }

    [Fact]
    public async Task FileOfExactly5MegabytesIsAccepted()
    {
        PrinterImageImportResult import = await _importer.ImportFileAsync(await WriteAsync("max.png", ImageTestData.Png(PrinterImageFormats.MaxSourceBytes)), Ct);

        Assert.Equal(PrinterImageImportStatus.Imported, import.Status);
        Assert.Equal(PrinterImageFormats.MaxSourceBytes, Assert.Single(_processor.Calls).Length);
    }

    [Fact]
    public async Task LargerFileIsRejectedBeforeDecoding()
    {
        PrinterImageImportResult import = await _importer.ImportFileAsync(await WriteAsync("big.png", ImageTestData.Png(PrinterImageFormats.MaxSourceBytes + 1)), Ct);

        Assert.Equal(new PrinterImageImportResult(PrinterImageImportStatus.TooLarge), import);
        Assert.Empty(_processor.Calls);
    }

    [Fact]
    public async Task LargeNonSeekableStreamIsRejectedWithoutReadingItAll()
    {
        using NonSeekableStream stream = new(ImageTestData.Png(PrinterImageFormats.MaxSourceBytes * 3));

        PrinterImageImportResult import = await _importer.ImportAsync(stream, Ct);

        Assert.Equal(PrinterImageImportStatus.TooLarge, import.Status);
        Assert.True(stream.BytesRead <= PrinterImageFormats.MaxSourceBytes + 81920);
        Assert.Empty(_processor.Calls);
    }

    [Fact]
    public async Task NonSeekableStreamWithinTheLimitIsImported()
    {
        using NonSeekableStream stream = new(ImageTestData.Jpeg(200_000));

        PrinterImageImportResult import = await _importer.ImportAsync(stream, Ct);

        Assert.Equal(PrinterImageImportStatus.Imported, import.Status);
        Assert.Equal(200_000, Assert.Single(_processor.Calls).Length);
    }

    [Fact]
    public async Task UndecodableImageIsReported()
    {
        _processor.Throws = new PrinterImageDecodeException();

        PrinterImageImportResult import = await _importer.ImportFileAsync(await WriteAsync("broken.png", ImageTestData.Png()), Ct);

        Assert.Equal(new PrinterImageImportResult(PrinterImageImportStatus.Undecodable), import);
    }

    public static TheoryData<byte[]> BadResults =>
    [
        Array.Empty<byte>(),
        ImageTestData.Jpeg(),
        ImageTestData.Png(PrinterImageFormats.MaxStoredBytes + 1),
    ];

    [Theory]
    [MemberData(nameof(BadResults))]
    public async Task UnstorableProcessorResultIsRejected(byte[] result)
    {
        _processor.Result = () => result;

        PrinterImageImportResult import = await _importer.ImportFileAsync(await WriteAsync("photo.png", ImageTestData.Png()), Ct);

        Assert.Equal(new PrinterImageImportResult(PrinterImageImportStatus.Undecodable), import);
    }

    [Fact]
    public async Task MissingFileIsReported()
    {
        Assert.Equal(PrinterImageImportStatus.NotFound, (await _importer.ImportFileAsync(Path.Combine(_directory, "missing.png"), Ct)).Status);
        Assert.Equal(PrinterImageImportStatus.NotFound, (await _importer.ImportFileAsync(Path.Combine(_directory, "no-dir", "missing.png"), Ct)).Status);
    }

    [Fact]
    public async Task UnreadableFileIsReported()
    {
        // A folder cannot be opened as a file.
        string folder = Path.Combine(_directory, "folder.png");
        Directory.CreateDirectory(folder);

        Assert.Equal(PrinterImageImportStatus.Unreadable, (await _importer.ImportFileAsync(folder, Ct)).Status);
    }

    [Fact]
    public async Task ReadErrorIsReported()
    {
        using FailingStream stream = new();

        Assert.Equal(PrinterImageImportStatus.Unreadable, (await _importer.ImportAsync(stream, Ct)).Status);
    }

    [Fact]
    public async Task CancellationIsPropagated()
    {
        string path = await WriteAsync("photo.png", ImageTestData.Png());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _importer.ImportFileAsync(path, new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task ImportWritesNothing()
    {
        string path = await WriteAsync("photo.png", ImageTestData.Png());

        await _importer.ImportFileAsync(path, Ct);

        Assert.Equal([path], Directory.GetFileSystemEntries(_directory));
    }

    private async Task<string> WriteAsync(string name, byte[] content)
    {
        string path = Path.Combine(_directory, name);
        await File.WriteAllBytesAsync(path, content, Ct);
        return path;
    }

    private sealed class NonSeekableStream(byte[] content) : MemoryStream(content)
    {
        public long BytesRead { get; private set; }

        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += read;
            return read;
        }
    }

    private sealed class FailingStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("device not ready"));
    }
}
