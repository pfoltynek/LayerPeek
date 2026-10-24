using System.Net;
using System.Net.Http.Headers;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Previews;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Secrets;
using LayerPeek.PrusaLink.Tests.WebProxy;
using LayerPeek.Tests.Shared;

namespace LayerPeek.PrusaLink.Tests;

public sealed class PrusaLinkPreviewFetcherTests : IDisposable
{
    private const string Address = "192.168.1.20";
    private const string LargePath = "/thumb/l/usb/BENCHY~1.BGC";
    private const string SmallPath = "/thumb/s/usb/BENCHY~1.BGC";

    private static readonly PrusaLinkPreviewReference Benchy = new("thumb/s/usb/BENCHY~1.BGC", "thumb/l/usb/BENCHY~1.BGC");

    private readonly FakePrusaLinkServer _server = new();
    private readonly InMemorySecretStore _secrets = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly CapturingLoggerFactory _logs = new();
    private readonly PrusaLinkPreviewFetcher _fetcher;

    public PrusaLinkPreviewFetcherTests()
    {
        _secrets.SetAsync(SecretKeys.ForPrinter("core-one"), _server.Password, CancellationToken.None).GetAwaiter().GetResult();
        _fetcher = CreateFetcher();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _server.Dispose();
        _logs.Dispose();
    }

    [Fact]
    public async Task LargePreviewIsDownloadedWithDigestLogin()
    {
        byte[] png = TestPng.Create(380, 285);
        _server.Handler = (request, _) => Task.FromResult(Png(png, etag: "\"3216549870\""));

        PrintPreviewFetchResult result = await _fetcher.FetchAsync(Printer(), Benchy, PrintPreviewSize.Large, null, Ct);

        Assert.Equal(PrintPreviewFetchStatus.Downloaded, result.Status);
        Assert.Equal(380, result.Preview!.Width);
        Assert.Equal(285, result.Preview.Height);
        Assert.Equal(png, result.Preview.Png.ToArray());
        Assert.Equal("\"3216549870\"", result.ETag);
        Assert.Collection(
            _server.Requests,
            static first => Assert.Equal((LargePath, HttpStatusCode.Unauthorized), (first.PathAndQuery, first.Status)),
            static second =>
            {
                Assert.Equal(LargePath, second.PathAndQuery);
                Assert.StartsWith("Digest ", second.Authorization, StringComparison.Ordinal);
                Assert.Equal("image/png", second.Accept);
                Assert.DoesNotContain("If-None-Match", second.AllHeaders, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task SmallPreviewUsesTheIconLink()
    {
        _server.Handler = (_, _) => Task.FromResult(Png(TestPng.Create(16, 16)));

        PrintPreviewFetchResult result = await _fetcher.FetchAsync(Printer(), Benchy, PrintPreviewSize.Small, null, Ct);

        Assert.Equal(PrintPreviewFetchStatus.Downloaded, result.Status);
        Assert.Equal(16, result.Preview!.Width);
        Assert.Null(result.ETag);
        Assert.Equal(SmallPath, _server.Requests.Last().PathAndQuery);
    }

    [Fact]
    public async Task FileWithoutPreviewIsNotAvailableAndNotLogged()
    {
        _server.Handler = static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{\"title\": \"404: Not Found\",\"message\":\"File doesn't contain preview\"}"),
        });

        PrintPreviewFetchResult result = await _fetcher.FetchAsync(Printer(), Benchy, PrintPreviewSize.Small, null, Ct);

        Assert.Equal(PrintPreviewFetchStatus.NotAvailable, result.Status);
        Assert.Empty(_logs.Entries);
    }

    [Fact]
    public async Task ETagIsSentAndNotModifiedIsReported()
    {
        string? ifNoneMatch = null;
        _server.Handler = (request, _) =>
        {
            ifNoneMatch = request.Headers.IfNoneMatch.ToString();
            HttpResponseMessage response = new(HttpStatusCode.NotModified);
            response.Headers.ETag = new EntityTagHeaderValue("\"42\"");
            return Task.FromResult(response);
        };

        PrintPreviewFetchResult result = await _fetcher.FetchAsync(Printer(), Benchy, PrintPreviewSize.Large, "\"42\"", Ct);

        Assert.Equal("\"42\"", ifNoneMatch);
        Assert.Equal(PrintPreviewFetchStatus.NotModified, result.Status);
        Assert.Null(result.Preview);
        Assert.Equal("\"42\"", result.ETag);
    }

    [Fact]
    public async Task NotModifiedWithoutAnETagSentIsAFailure()
    {
        _server.Handler = static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));

        PrintPreviewFetchResult result = await _fetcher.FetchAsync(Printer(), Benchy, PrintPreviewSize.Large, null, Ct);

        Assert.Equal(PrintPreviewFetchStatus.Failed, result.Status);
    }

    [Fact]
    public async Task DeclaredLengthOverTheLimitIsRefusedWithoutReadingTheBody()
    {
        GeneratedStream body = new(2 * 1024 * 1024);
        _server.Handler = (_, _) =>
        {
            HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new StreamContent(body) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            response.Content.Headers.ContentLength = body.Length;
            return Task.FromResult(response);
        };

        PrintPreviewFetchResult result = await _fetcher.FetchAsync(Printer(), Benchy, PrintPreviewSize.Large, null, Ct);

        Assert.Equal(PrintPreviewFetchStatus.NotAvailable, result.Status);
        Assert.Equal(0, body.Position);
    }

    [Fact]
    public async Task EndlessBodyIsAbandonedAtTheLimitWhileStreaming()
    {
        // No Content-Length (chunked, as the firmware sends previews): only counting while reading can stop it.
        GeneratedStream body = new(long.MaxValue / 2);
        _server.Handler = (_, _) =>
        {
            HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new StreamContent(body) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return Task.FromResult(response);
        };
        PrusaLinkPreviewFetcher fetcher = CreateFetcher(new PrusaLinkPreviewOptions { MaxBytes = 64 * 1024 });

        PrintPreviewFetchResult result = await fetcher.FetchAsync(Printer(), Benchy, PrintPreviewSize.Large, null, Ct);

        Assert.Equal(PrintPreviewFetchStatus.NotAvailable, result.Status);
        Assert.Equal((64 * 1024) + 1, body.Position);
    }

    [Fact]
    public async Task PreviewExactlyAtTheLimitIsAccepted()
    {
        byte[] png = TestPng.Create(16, 16);
        byte[] padded = new byte[2048];
        png.CopyTo(padded, 0);
        _server.Handler = (_, _) => Task.FromResult(Png(padded, chunked: true));
        PrusaLinkPreviewFetcher fetcher = CreateFetcher(new PrusaLinkPreviewOptions { MaxBytes = 2048 });

        PrintPreviewFetchResult result = await fetcher.FetchAsync(Printer(), Benchy, PrintPreviewSize.Large, null, Ct);

        Assert.Equal(PrintPreviewFetchStatus.Downloaded, result.Status);
        Assert.Equal(2048, result.Preview!.Png.Length);
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("image/qoi")]
    [InlineData(null)]
    public async Task OtherContentTypeIsRefused(string? mediaType)
    {
        _server.Handler = (_, _) =>
        {
            HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new ByteArrayContent(TestPng.Create(16, 16)) };
            response.Content.Headers.ContentType = mediaType is null ? null : new MediaTypeHeaderValue(mediaType);
            return Task.FromResult(response);
        };

        PrintPreviewFetchResult result = await _fetcher.FetchAsync(Printer(), Benchy, PrintPreviewSize.Large, null, Ct);

        Assert.Equal(PrintPreviewFetchStatus.NotAvailable, result.Status);
    }

    [Fact]
    public async Task BodyThatIsNotAPngIsRefused()
    {
        _server.Handler = static (_, _) => Task.FromResult(Png("<html><body>proxy error</body></html>"u8.ToArray()));

        PrintPreviewFetchResult result = await _fetcher.FetchAsync(Printer(), Benchy, PrintPreviewSize.Large, null, Ct);

        Assert.Equal(PrintPreviewFetchStatus.NotAvailable, result.Status);
    }

    [Fact]
    public async Task TimeoutIsATransientFailure()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.Handler = async (_, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Png(TestPng.Create(16, 16));
        };

        Task<PrintPreviewFetchResult> fetch = _fetcher.FetchAsync(Printer(), Benchy, PrintPreviewSize.Large, null, Ct);
        await started.Task.WaitAsync(Ct);
        _time.Advance(PrusaLinkPreviewOptions.Default.Timeout);
        PrintPreviewFetchResult result = await fetch.WaitAsync(Ct);

        Assert.Equal(PrintPreviewFetchStatus.Failed, result.Status);
        Assert.Contains(_logs.Entries, static e => e.Contains("Timeout", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TimeoutAlsoCoversASlowBody()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource never = new();
        _server.Handler = (_, _) =>
        {
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                // Sends 100 bytes, then stalls.
                Content = new StreamContent(new GeneratedStream(1000, gateAt: 100, gate: never.Task)),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            started.TrySetResult();
            return Task.FromResult(response);
        };

        Task<PrintPreviewFetchResult> fetch = _fetcher.FetchAsync(Printer(), Benchy, PrintPreviewSize.Large, null, Ct);
        await started.Task.WaitAsync(Ct);
        _time.Advance(PrusaLinkPreviewOptions.Default.Timeout);

        Assert.Equal(PrintPreviewFetchStatus.Failed, (await fetch.WaitAsync(Ct)).Status);
    }

    [Fact]
    public async Task CallerCancellationIsThrown()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.Handler = async (_, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Png(TestPng.Create(16, 16));
        };
        using CancellationTokenSource cancellation = new();

        Task<PrintPreviewFetchResult> fetch = _fetcher.FetchAsync(Printer(), Benchy, PrintPreviewSize.Large, null, cancellation.Token);
        await started.Task.WaitAsync(Ct);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetch);
    }

    [Fact]
    public async Task UnreachablePrinterIsATransientFailureWithoutAddressInTheLog()
    {
        _server.Override = static (_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError, "No connection could be made (192.168.1.20:80)");

        PrintPreviewFetchResult result = await _fetcher.FetchAsync(Printer(), Benchy, PrintPreviewSize.Large, null, Ct);

        Assert.Equal(PrintPreviewFetchStatus.Failed, result.Status);
        string log = Assert.Single(_logs.Entries);
        Assert.StartsWith("Debug: ", log, StringComparison.Ordinal);
        Assert.Contains("ConnectionError", log, StringComparison.Ordinal);
        AssertNoSecretsLogged();
    }

    [Fact]
    public async Task DroppedConnectionIsATransientFailure()
    {
        _server.Override = static (_, _) => throw new IOException("connection reset by 192.168.1.20");

        Assert.Equal(PrintPreviewFetchStatus.Failed, (await _fetcher.FetchAsync(Printer(), Benchy, PrintPreviewSize.Large, null, Ct)).Status);
        AssertNoSecretsLogged();
    }

    [Fact]
    public async Task WrongPasswordIsATransientFailureWithoutSecretsInTheLog()
    {
        await _secrets.SetAsync(SecretKeys.ForPrinter("core-one"), "wrong-password", Ct);
        _server.Handler = static (_, _) => Task.FromResult(Png(TestPng.Create(16, 16)));

        PrintPreviewFetchResult result = await _fetcher.FetchAsync(Printer(), Benchy, PrintPreviewSize.Large, null, Ct);

        Assert.Equal(PrintPreviewFetchStatus.Failed, result.Status);
        Assert.Contains(_logs.Entries, static e => e.Contains("HTTP 401", StringComparison.Ordinal));
        AssertNoSecretsLogged("wrong-password");
    }

    [Fact]
    public async Task MissingPasswordIsATransientFailure()
    {
        await _secrets.DeleteAsync(SecretKeys.ForPrinter("core-one"), Ct);

        PrintPreviewFetchResult result = await _fetcher.FetchAsync(Printer(), Benchy, PrintPreviewSize.Large, null, Ct);

        Assert.Equal(PrintPreviewFetchStatus.Failed, result.Status);
        Assert.Contains(_logs.Entries, static e => e.Contains(nameof(PrusaLinkFailure.PasswordMissing), StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnreadablePasswordIsATransientFailure()
    {
        PrusaLinkPreviewFetcher fetcher = new(new FakeHttpClientFactory(_server), new ThrowingSecretStore(), _time, _logs.CreateLogger<PrusaLinkPreviewFetcher>());

        PrintPreviewFetchResult result = await fetcher.FetchAsync(Printer(), Benchy, PrintPreviewSize.Large, null, Ct);

        Assert.Equal(PrintPreviewFetchStatus.Failed, result.Status);
        AssertNoSecretsLogged();
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task OtherErrorsAreTransientFailures(HttpStatusCode status)
    {
        _server.Handler = (_, _) => Task.FromResult(new HttpResponseMessage(status));

        Assert.Equal(PrintPreviewFetchStatus.Failed, (await _fetcher.FetchAsync(Printer(), Benchy, PrintPreviewSize.Large, null, Ct)).Status);
        AssertNoSecretsLogged();
    }

    [Theory]
    [InlineData("/thumb/l/usb/MY FILE~1.BGC", "/thumb/l/usb/MY%20FILE~1.BGC")]
    [InlineData("/thumb/l/usb/ŽLUŤOUČ~1.BGC", "/thumb/l/usb/%C5%BDLU%C5%A4OU%C4%8C~1.BGC")]
    [InlineData("/thumb/l/usb/A%20B&C+D.BGC", "/thumb/l/usb/A%2520B%26C%2BD.BGC")]
    public async Task FileNamesAreEscapedAndSignedAsSent(string link, string expectedTarget)
    {
        PrusaLinkPreviewReference reference = PrusaLinkPreviewReference.FromRefs(new Api.JobFileRefs { Thumbnail = link })!;
        _server.Handler = static (_, _) => Task.FromResult(Png(TestPng.Create(16, 16)));

        PrintPreviewFetchResult result = await _fetcher.FetchAsync(Printer(), reference, PrintPreviewSize.Large, null, Ct);

        // The fake server checks the digest against the target actually sent.
        Assert.Equal(PrintPreviewFetchStatus.Downloaded, result.Status);
        Assert.Equal(expectedTarget, _server.Requests.Last().PathAndQuery);
    }

    [Fact]
    public async Task LinksResolveBelowAReverseProxyPrefix()
    {
        _server.Handler = static (_, _) => Task.FromResult(Png(TestPng.Create(16, 16)));

        PrintPreviewFetchResult result = await _fetcher.FetchAsync(Printer("http://proxy.local/printers/core-one"), Benchy, PrintPreviewSize.Large, null, Ct);

        Assert.Equal(PrintPreviewFetchStatus.Downloaded, result.Status);
        Assert.Equal("/printers/core-one" + LargePath, _server.Requests.Last().PathAndQuery);
    }

    [Fact]
    public async Task SchemeLookingFirstSegmentStaysOnThePrinter()
    {
        PrusaLinkPreviewReference reference = PrusaLinkPreviewReference.FromRefs(new Api.JobFileRefs { Thumbnail = "/http:/evil.example/A.BGC" })!;
        _server.Handler = static (_, _) => Task.FromResult(Png(TestPng.Create(16, 16)));

        await _fetcher.FetchAsync(Printer(), reference, PrintPreviewSize.Large, null, Ct);

        Assert.All(_server.Requests, static r => Assert.Equal("/http%3A/evil.example/A.BGC", r.PathAndQuery));
        Assert.NotEmpty(_server.Requests);
    }

    [Theory]
    [InlineData("http://evil.example/thumb/l/A.BGC")]
    [InlineData("https://192.168.1.20/thumb/l/A.BGC")]
    [InlineData("//evil.example/thumb/l/A.BGC")]
    [InlineData("/thumb/l/A.BGC")]
    [InlineData("../../thumb/l/A.BGC")]
    [InlineData("thumb/l/A.BGC?x=1")]
    public async Task ReferenceThatWouldLeaveThePrinterIsNotFetched(string path)
    {
        // References are validated when created; the fetcher must still never follow one off the printer (or above the
        // reverse-proxy prefix), whoever constructed it.
        PrusaLinkPreviewReference reference = new(null, path);

        PrintPreviewFetchResult result = await _fetcher.FetchAsync(Printer("http://192.168.1.20/printers/core-one"), reference, PrintPreviewSize.Large, null, Ct);

        Assert.Equal(PrintPreviewFetchStatus.NotAvailable, result.Status);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task ResponsesAreDisposedWhateverTheOutcome()
    {
        List<TrackingContent> contents = [];
        Queue<Func<HttpResponseMessage>> answers = new([
            () => Respond(HttpStatusCode.OK, TestPng.Create(16, 16), "image/png"),
            () => Respond(HttpStatusCode.OK, TestPng.Create(16, 16), "image/png", declaredLength: 10 * 1024 * 1024),
            () => Respond(HttpStatusCode.OK, TestPng.Create(16, 16), "text/html"),
            () => Respond(HttpStatusCode.OK, "nope"u8.ToArray(), "image/png"),
            () => Respond(HttpStatusCode.NotFound, [], null),
            () => Respond(HttpStatusCode.NotModified, [], null),
            () => Respond(HttpStatusCode.InternalServerError, [], null),
        ]);
        int count = answers.Count;
        _server.Handler = (_, _) => Task.FromResult(answers.Dequeue()());

        for (int i = 0; i < count; i++)
        {
            await _fetcher.FetchAsync(Printer(), Benchy, PrintPreviewSize.Large, "\"1\"", Ct);
        }

        Assert.Equal(count, contents.Count);
        Assert.All(contents, static c => Assert.True(c.Disposed));

        HttpResponseMessage Respond(HttpStatusCode status, byte[] body, string? mediaType, long? declaredLength = null)
        {
            TrackingContent content = new(body, declaredLength);
            contents.Add(content);
            if (mediaType is not null)
            {
                content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            }

            return new HttpResponseMessage(status) { Content = content };
        }
    }

    [Fact]
    public async Task ForeignPrintersAndReferencesAreNotAvailableWithoutRequests()
    {
        PrinterDefinition connect = new() { Id = "core-one", Name = "Core One", ConnectionType = ConnectionType.PrusaConnect, Address = "c0ffee" };

        PrintPreviewFetchResult connectPrinter = await _fetcher.FetchAsync(connect, Benchy, PrintPreviewSize.Large, null, Ct);
        PrintPreviewFetchResult foreignReference = await _fetcher.FetchAsync(Printer(), new ForeignReference(), PrintPreviewSize.Large, null, Ct);
        PrintPreviewFetchResult missingLink = await _fetcher.FetchAsync(Printer(), Benchy with { Icon = null }, PrintPreviewSize.Small, null, Ct);
        PrintPreviewFetchResult badAddress = await _fetcher.FetchAsync(Printer("ftp://printer"), Benchy, PrintPreviewSize.Large, null, Ct);

        Assert.All([connectPrinter, foreignReference, missingLink, badAddress], static r => Assert.Equal(PrintPreviewFetchStatus.NotAvailable, r.Status));
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task ConcurrentFetchesOfOneFetcherDoNotInterfere()
    {
        _server.Handler = static (request, _) => Task.FromResult(Png(TestPng.Create(request.RequestUri!.AbsolutePath.Contains("/s/", StringComparison.Ordinal) ? 16 : 300, 16)));

        PrintPreviewFetchResult[] results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            _fetcher.FetchAsync(Printer(), Benchy, i % 2 == 0 ? PrintPreviewSize.Small : PrintPreviewSize.Large, null, Ct)));

        for (int i = 0; i < results.Length; i++)
        {
            Assert.Equal(i % 2 == 0 ? 16 : 300, results[i].Preview!.Width);
        }
    }

    [Fact]
    public async Task ArgumentsAreValidated()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _fetcher.FetchAsync(null!, Benchy, PrintPreviewSize.Large, null, Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _fetcher.FetchAsync(Printer(), null!, PrintPreviewSize.Large, null, Ct));
        Assert.Throws<ArgumentOutOfRangeException>(static () => new PrusaLinkPreviewOptions { MaxBytes = 10 });
        Assert.Throws<ArgumentOutOfRangeException>(static () => new PrusaLinkPreviewOptions { Timeout = TimeSpan.Zero });
    }

    private PrusaLinkPreviewFetcher CreateFetcher(PrusaLinkPreviewOptions? options = null) =>
        new(new FakeHttpClientFactory(_server), _secrets, _time, _logs.CreateLogger<PrusaLinkPreviewFetcher>(), options);

    private void AssertNoSecretsLogged(string? password = null)
    {
        string all = _logs.AllText;
        Assert.DoesNotContain(Address, all, StringComparison.Ordinal);
        Assert.DoesNotContain(_server.Password, all, StringComparison.Ordinal);
        Assert.DoesNotContain("BENCHY", all, StringComparison.Ordinal);
        Assert.DoesNotContain("thumb", all, StringComparison.Ordinal);
        if (password is not null)
        {
            Assert.DoesNotContain(password, all, StringComparison.Ordinal);
        }
    }

    private static PrinterDefinition Printer(string address = Address) => new()
    {
        Id = "core-one",
        Name = "Core One",
        ConnectionType = ConnectionType.PrusaLink,
        Address = address,
    };

    private static HttpResponseMessage Png(byte[] body, string? etag = null, bool chunked = false)
    {
        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = chunked ? new StreamContent(new MemoryStream(body)) : new ByteArrayContent(body),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        if (chunked)
        {
            response.Content.Headers.ContentLength = null;
        }

        if (etag is not null)
        {
            response.Headers.ETag = new EntityTagHeaderValue(etag);
        }

        return response;
    }

    private sealed record ForeignReference : PrintPreviewReference;

    /// <summary>A body that records whether it was disposed (with the response).</summary>
    private sealed class TrackingContent : ByteArrayContent
    {
        public TrackingContent(byte[] body, long? declaredLength)
            : base(body)
        {
            if (declaredLength is not null)
            {
                Headers.ContentLength = declaredLength;
            }
        }

        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class ThrowingSecretStore : ISecretStore
    {
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken) => throw new SecretStoreException("keychain locked");

        public Task SetAsync(string key, string secret, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}

/// <summary>Hands out clients over one in-memory handler (which stays alive), as the factory does with its pooled handler.</summary>
internal sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public List<string> Names { get; } = [];

    public HttpClient CreateClient(string name)
    {
        lock (Names)
        {
            Names.Add(name);
        }

        return new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
    }
}
