using System.Net;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Previews;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Secrets;
using LayerPeek.PrusaLink.Api;

namespace LayerPeek.PrusaLink.Tests;

/// <summary>The preview links (<c>file.refs</c> of <c>/api/v1/job</c>) as <see cref="PrintJob.PreviewReference"/>.</summary>
public sealed class PrusaLinkPreviewReferenceTests : IDisposable
{
    private const string StatusPath = "/api/v1/status";
    private const string JobPath = "/api/v1/job";

    private readonly FakePrusaLinkServer _server = new();
    private readonly InMemorySecretStore _secrets = new();
    private readonly HttpClient _httpClient;
    private readonly PrusaLinkClient _client;

    public PrusaLinkPreviewReferenceTests()
    {
        _httpClient = new HttpClient(_server);
        _secrets.SetAsync(SecretKeys.ForPrinter("core-one"), _server.Password, CancellationToken.None).GetAwaiter().GetResult();
        _client = new PrusaLinkClient(
            _httpClient,
            new PrinterDefinition { Id = "core-one", Name = "Core One", ConnectionType = ConnectionType.PrusaLink, Address = "192.168.1.20" },
            _secrets,
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<PrusaLinkClient>.Instance);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _httpClient.Dispose();
        _server.Dispose();
    }

    [Fact]
    public async Task JobCarriesThePreviewLinksOfTheRecordedFixture()
    {
        _server.RouteFixture(StatusPath, "status-printing");
        _server.RouteFixture(JobPath, "job-printing");

        PrintJob job = (await _client.GetSnapshotAsync(Ct)).Job!;

        PrusaLinkPreviewReference reference = Assert.IsType<PrusaLinkPreviewReference>(job.PreviewReference);
        Assert.Equal("thumb/s/usb/BENCHY~1.BGC", reference.Icon);
        Assert.Equal("thumb/l/usb/BENCHY~1.BGC", reference.Thumbnail);
        Assert.Equal(reference.Icon, reference.PathFor(PrintPreviewSize.Small));
        Assert.Equal(reference.Thumbnail, reference.PathFor(PrintPreviewSize.Large));
    }

    [Fact]
    public async Task ReferenceIsReadOncePerJobAndKeptAfterThePrintEnded()
    {
        _server.RouteFixture(StatusPath, "status-printing");
        _server.RouteFixture(JobPath, "job-printing");
        PrintPreviewReference? printing = (await _client.GetSnapshotAsync(Ct)).Job!.PreviewReference;
        PrintPreviewReference? again = (await _client.GetSnapshotAsync(Ct)).Job!.PreviewReference;

        // After the end /api/v1/job answers 204; the last job (with its reference) is still reported.
        _server.RouteFixture(StatusPath, "status-finished");
        _server.Route(JobPath, HttpStatusCode.NoContent);
        PrintPreviewReference? finished = (await _client.GetSnapshotAsync(Ct)).Job!.PreviewReference;
        _server.RouteFixture(StatusPath, "status-stopped");
        PrintPreviewReference? stopped = (await _client.GetSnapshotAsync(Ct)).Job!.PreviewReference;

        Assert.NotNull(printing);
        Assert.Same(printing, again);
        Assert.Same(printing, finished);
        Assert.Same(printing, stopped);
        Assert.Equal(1, _server.Requests.Count(static r => r.PathAndQuery == JobPath && r.Status == HttpStatusCode.OK));
    }

    [Fact]
    public async Task NextJobGetsItsOwnReference()
    {
        _server.RouteFixture(StatusPath, "status-printing");
        _server.RouteFixture(JobPath, "job-printing");
        PrintPreviewReference? first = (await _client.GetSnapshotAsync(Ct)).Job!.PreviewReference;

        _server.RouteFixture(StatusPath, "status-printing-next-job");
        _server.RouteFixture(JobPath, "job-next");
        PrintPreviewReference? next = (await _client.GetSnapshotAsync(Ct)).Job!.PreviewReference;

        Assert.Equal(new PrusaLinkPreviewReference("thumb/s/usb/CUBE~1.BGC", "thumb/l/usb/CUBE~1.BGC"), next);
        Assert.NotEqual(first, next);
    }

    [Fact]
    public async Task JobWithoutLinksHasNoReference()
    {
        _server.RouteFixture(StatusPath, "status-printing");
        _server.Route(JobPath, HttpStatusCode.OK, "{\"id\":42,\"file\":{\"name\":\"BENCHY~1.BGC\",\"display_name\":\"benchy.bgcode\"}}");

        PrintJob job = (await _client.GetSnapshotAsync(Ct)).Job!;

        Assert.Equal("benchy.bgcode", job.FileName);
        Assert.Null(job.PreviewReference);
    }

    [Fact]
    public async Task FailedJobRequestLeavesTheReferenceUnknown()
    {
        _server.RouteFixture(StatusPath, "status-printing");
        _server.Route(JobPath, HttpStatusCode.InternalServerError);

        Assert.Null((await _client.GetSnapshotAsync(Ct)).Job!.PreviewReference);
    }

    [Fact]
    public void OnlyOneUsableLinkIsEnough()
    {
        PrusaLinkPreviewReference? reference = PrusaLinkPreviewReference.FromRefs(new JobFileRefs { Icon = "?", Thumbnail = "/thumb/l/usb/A.BGC" });

        Assert.NotNull(reference);
        Assert.Null(reference.Icon);
        Assert.Equal("thumb/l/usb/A.BGC", reference.Thumbnail);
        Assert.Null(PrusaLinkPreviewReference.FromRefs(new JobFileRefs { Download = "/usb/A.BGC" }));
        Assert.Null(PrusaLinkPreviewReference.FromRefs(null));
    }

    [Theory]
    [InlineData("/thumb/l/usb/BENCHY~1.BGC", "thumb/l/usb/BENCHY~1.BGC")]
    [InlineData("/thumb/s/usb/SUB/DEEP/A.BGC", "thumb/s/usb/SUB/DEEP/A.BGC")]
    [InlineData("/thumb/l/usb/MY FILE~1.BGC", "thumb/l/usb/MY%20FILE~1.BGC")]
    [InlineData("/thumb/l/usb/čaj.bgcode", "thumb/l/usb/%C4%8Daj.bgcode")]
    [InlineData("/thumb/l/usb/100%.BGC", "thumb/l/usb/100%25.BGC")]
    [InlineData("/api/thumbnails/usb/A.BGC.orig.png", "api/thumbnails/usb/A.BGC.orig.png")]
    public void LinksAreEscapedPerSegment(string link, string expected) =>
        Assert.Equal(expected, PrusaLinkPreviewReference.ToRelativePath(link));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("thumb/l/usb/A.BGC")]
    [InlineData("//evil.example/thumb/l/A.BGC")]
    [InlineData("http://evil.example/thumb/l/A.BGC")]
    [InlineData("/thumb/l/../../api/v1/status")]
    [InlineData("/thumb/l/./usb/A.BGC")]
    [InlineData("/thumb/l/usb//A.BGC")]
    [InlineData("/thumb/l/usb/A.BGC?x=1")]
    [InlineData("/thumb/l/usb/A.BGC#top")]
    [InlineData("/thumb/l/usb\\A.BGC")]
    [InlineData("/thumb/l/usb/A\r\nX-Injected: 1")]
    public void UnsafeLinksAreRefused(string? link) =>
        Assert.Null(PrusaLinkPreviewReference.ToRelativePath(link));

    [Fact]
    public void LoneSurrogatesAreRefused()
    {
        // Built in code: attribute data cannot carry invalid UTF-16.
        Assert.Null(PrusaLinkPreviewReference.ToRelativePath("/thumb/l/usb/" + '\uD800' + ".BGC"));
        Assert.Null(PrusaLinkPreviewReference.ToRelativePath("/thumb/l/usb/A" + '\uDC00'));
        Assert.Null(PrusaLinkPreviewReference.ToRelativePath("/thumb/l/usb/A" + '\uD800'));
        Assert.Equal("thumb/l/%F0%9F%A6%9C.BGC", PrusaLinkPreviewReference.ToRelativePath("/thumb/l/\U0001F99C.BGC"));
    }

    [Fact]
    public void OverlongLinkIsRefused() =>
        Assert.Null(PrusaLinkPreviewReference.ToRelativePath("/thumb/l/" + new string('A', 1100)));
}
