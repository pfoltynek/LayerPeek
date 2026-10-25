using System.Net;
using System.Text;

using LayerPeek.PrusaLink.WebProxy;
using LayerPeek.Tests.Allocation;

namespace LayerPeek.PrusaLink.Tests.WebProxy;

/// <summary>Nonce handling and login failures: the browser never sees a 401 or a login challenge.</summary>
public class WebProxyDigestTests
{
    private const string UploadPath = "api/v1/files/usb/part.gcode";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OneChallengeServesTheWholeSeries()
    {
        await using ProxyTestContext context = new();
        Assert.Equal(0, context.Secrets.ReadCount);
        Uri proxy = await context.OpenAsync();

        for (int i = 0; i < 5; i++)
        {
            using HttpResponseMessage response = await context.Browser.GetAsync(new Uri(proxy, "api/v1/status"), Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // The login check before opening answered the only challenge; the password was read once, at that challenge.
        Assert.Equal(1, context.Printer.UnauthorizedCount);
        Assert.Equal(1, context.Secrets.ReadCount);
    }

    [Fact]
    public async Task StaleNonceRepeatsASmallRequestOnce()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        List<string> bodies = [];
        context.Printer.Handler = async (request, ct) =>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.Created);
        };
        context.Printer.ExpireNonce();
        int before = context.Printer.Requests.Count;

        using HttpResponseMessage response = await context.Browser.PutAsync(new Uri(proxy, UploadPath), new StringContent("G28\nG1 X10"), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        RecordedRequest[] puts = [.. context.Printer.Requests.Skip(before)];
        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Continue], puts.Select(static r => r.Status));
        Assert.All(puts, static r => Assert.Equal("PUT", r.Method));
        Assert.Equal(["G28\nG1 X10"], bodies);
        // Stale nonce: the password was not read again.
        Assert.Equal(1, context.Secrets.ReadCount);
    }

    [Fact]
    public async Task OldNonceIsRenewedBeforeAStreamedUpload()
    {
        await using ProxyTestContext context = new(new PrusaLinkWebProxyOptions { MaxBufferedBodySize = 1024 });
        Uri proxy = await context.OpenAsync();
        long received = 0;
        context.Printer.Handler = async (request, ct) =>
        {
            if (request.Content is null)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fixtures.Read("version")) };
            }

            received = (await request.Content.ReadAsByteArrayAsync(ct)).Length;
            return new HttpResponseMessage(HttpStatusCode.Created);
        };
        context.Time.Advance(TimeSpan.FromSeconds(241));
        context.Printer.ExpireNonce();
        int before = context.Printer.Requests.Count;

        using HttpResponseMessage response = await context.Browser.PutAsync(new Uri(proxy, UploadPath), new ByteArrayContent(new byte[100_000]), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(100_000, received);
        RecordedRequest[] sent = [.. context.Printer.Requests.Skip(before)];
        Assert.Equal(
            ["GET /api/version Unauthorized", "GET /api/version Continue", "PUT /api/v1/files/usb/part.gcode Continue"],
            sent.Select(static r => $"{r.Method} {r.PathAndQuery} {r.Status}"));
    }

    [Fact]
    public async Task FreshNonceNeedsNoRenewalBeforeAStreamedUpload()
    {
        await using ProxyTestContext context = new(new PrusaLinkWebProxyOptions { MaxBufferedBodySize = 1024 });
        Uri proxy = await context.OpenAsync();
        context.Printer.Handler = static async (request, ct) =>
        {
            await request.Content!.ReadAsByteArrayAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.Created);
        };
        context.Time.Advance(TimeSpan.FromSeconds(200));
        int before = context.Printer.Requests.Count;

        using HttpResponseMessage response = await context.Browser.PutAsync(new Uri(proxy, UploadPath), new ByteArrayContent(new byte[10_000]), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(["PUT"], context.Printer.Requests.Skip(before).Select(static r => r.Method));
    }

    [Fact]
    public async Task StreamedUploadRejectedAsStaleAnswersForbiddenAndRenewsTheNonce()
    {
        await using ProxyTestContext context = new(new PrusaLinkWebProxyOptions { MaxBufferedBodySize = 1024 });
        Uri proxy = await context.OpenAsync();
        // The printer forgot the nonce earlier than the proxy expects (e.g. rebooted).
        context.Printer.ExpireNonce();

        using HttpResponseMessage response = await context.Browser.PutAsync(new Uri(proxy, UploadPath), new ByteArrayContent(new byte[10_000]), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("WWW-Authenticate"));
        Assert.Contains("expired", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        int before = context.Printer.Requests.Count;
        using HttpResponseMessage next = await context.Browser.GetAsync(new Uri(proxy, "api/v1/status"), Ct);
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        Assert.Equal([HttpStatusCode.OK], context.Printer.Requests.Skip(before).Select(static r => r.Status));
    }

    [Fact]
    public async Task PasswordRejectedDuringTheSessionAnswersForbiddenJsonNeverUnauthorized()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        context.Printer.Password = "changed-on-printer";

        using HttpResponseMessage response = await context.Browser.GetAsync(new Uri(proxy, "api/v1/status"), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("WWW-Authenticate"));
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        string body = await response.Content.ReadAsStringAsync(Ct);
        Assert.StartsWith("{\"title\":\"403: Forbidden\",\"message\":\"", body, StringComparison.Ordinal);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        // A rejected password is read again (it may have been corrected in the settings meanwhile).
        Assert.Equal(2, context.Secrets.ReadCount);
    }

    [Fact]
    public async Task PasswordRejectedDuringAStreamedUploadAnswersForbidden()
    {
        await using ProxyTestContext context = new(new PrusaLinkWebProxyOptions { MaxBufferedBodySize = 1024 });
        Uri proxy = await context.OpenAsync();
        context.Printer.Password = "changed-on-printer";

        using HttpResponseMessage response = await context.Browser.PutAsync(new Uri(proxy, UploadPath), new ByteArrayContent(new byte[10_000]), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("WWW-Authenticate"));
        Assert.Contains("rejected", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PasswordRemovedDuringTheSessionAnswersForbidden()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        context.Printer.Password = "changed-on-printer";
        await context.Secrets.DeleteAsync(Core.Secrets.SecretKeys.ForPrinter("core-one"), Ct);

        using HttpResponseMessage response = await context.Browser.GetAsync(new Uri(proxy, "api/v1/status"), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("No password", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsupportedChallengeDuringTheSessionAnswersForbidden()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        context.Printer.ChallengeScheme = "ApiKey";

        using HttpResponseMessage response = await context.Browser.GetAsync(new Uri(proxy, "api/v1/status"), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("WWW-Authenticate"));
        Assert.Contains("authentication method", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnreadablePasswordDuringTheSessionAnswersForbidden()
    {
        ToggleSecretStore secrets = new();
        await using ProxyTestContext context = new(secrets: secrets);
        await secrets.SetAsync(Core.Secrets.SecretKeys.ForPrinter("core-one"), context.Printer.Password, Ct);
        Uri proxy = await context.OpenAsync();
        context.Printer.Password = "changed-on-printer";
        secrets.Fail = true;

        using HttpResponseMessage response = await context.Browser.GetAsync(new Uri(proxy, "api/v1/status"), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("could not be read", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    private sealed class ToggleSecretStore : Core.Secrets.ISecretStore
    {
        private readonly InMemorySecretStore _inner = new();

        public bool Fail { get; set; }

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken) =>
            Fail ? throw new Core.Secrets.SecretStoreException("The credential store is locked.") : _inner.GetAsync(key, cancellationToken);

        public Task SetAsync(string key, string secret, CancellationToken cancellationToken) => _inner.SetAsync(key, secret, cancellationToken);

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken) => _inner.DeleteAsync(key, cancellationToken);
    }
}

/// <summary>
/// Bodies of hundreds of MB pass through block by block in both directions. The allocation checks count every thread
/// of the process (the proxy forwards on server threads), so the class runs alone, see <see cref="AllocationSensitive"/>.
/// </summary>
[Collection(nameof(AllocationSensitive))]
public class WebProxyStreamingTests
{
    private const long Size = 256L * 1024 * 1024;
    private const long Gate = 32L * 1024 * 1024;
    private const long FirstPart = 1024 * 1024;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LargeUploadIsStreamedWithItsHeaders()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        VerifyingSink sink = new(FirstPart);
        long? contentLength = null;
        string? overwrite = null;
        string? printAfterUpload = null;
        context.Printer.Handler = async (request, ct) =>
        {
            contentLength = request.Content!.Headers.ContentLength;
            overwrite = request.Headers.GetValues("Overwrite").Single();
            printAfterUpload = request.Headers.GetValues("Print-After-Upload").Single();
            // Read block by block through the content's own serialization (no buffering on the printer side either).
            await request.Content.CopyToAsync(sink, ct);
            return new HttpResponseMessage(HttpStatusCode.Created);
        };
        // The browser sends only the first 32 MB until the printer has received 1 MB: a proxy that buffered the body
        // would never forward anything and the upload would hang (and time out).
        using HttpRequestMessage request = new(HttpMethod.Put, new Uri(proxy, "api/v1/files/usb/big.gcode"))
        {
            Content = new StreamContent(new GeneratedStream(Size, Gate, sink.Reached), 64 * 1024),
        };
        request.Content.Headers.ContentLength = Size;
        request.Headers.TryAddWithoutValidation("Overwrite", "?1");
        request.Headers.TryAddWithoutValidation("Print-After-Upload", "?1");
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        long allocatedBefore = GC.GetTotalAllocatedBytes();

        using HttpResponseMessage response = await context.Browser.SendAsync(request, timeout.Token);

        long allocated = GC.GetTotalAllocatedBytes() - allocatedBefore;
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(Size, sink.Received);
        Assert.True(sink.ContentMatches);
        Assert.Equal(Size, contentLength);
        Assert.Equal("?1", overwrite);
        Assert.Equal("?1", printAfterUpload);
        Assert.True(allocated < 64L * 1024 * 1024, $"{allocated / 1024 / 1024} MB allocated for a {Size / 1024 / 1024} MB upload.");
    }

    [Fact]
    public async Task LargeDownloadIsStreamed()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        VerifyingSink sink = new(FirstPart);
        context.Printer.Handler = (_, _) =>
        {
            HttpResponseMessage answer = new(HttpStatusCode.OK) { Content = new StreamContent(new GeneratedStream(Size, Gate, sink.Reached), 64 * 1024) };
            answer.Content.Headers.ContentLength = Size;
            return Task.FromResult(answer);
        };
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        long allocatedBefore = GC.GetTotalAllocatedBytes();

        using HttpResponseMessage response = await context.Browser.GetAsync(new Uri(proxy, "api/v1/files/usb/big.gcode"), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        Stream body = await response.Content.ReadAsStreamAsync(timeout.Token);
        await body.CopyToAsync(sink, timeout.Token);

        long allocated = GC.GetTotalAllocatedBytes() - allocatedBefore;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Size, response.Content.Headers.ContentLength);
        Assert.Equal(Size, sink.Received);
        Assert.True(sink.ContentMatches);
        Assert.True(allocated < 64L * 1024 * 1024, $"{allocated / 1024 / 1024} MB allocated for a {Size / 1024 / 1024} MB download.");
    }

    [Fact]
    public async Task ChunkedUploadIsStreamed()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        VerifyingSink sink = new(FirstPart);
        context.Printer.Handler = async (request, ct) =>
        {
            Assert.Null(request.Content!.Headers.ContentLength);
            await request.Content.CopyToAsync(sink, ct);
            return new HttpResponseMessage(HttpStatusCode.Created);
        };
        const long ChunkedSize = 40L * 1024 * 1024;
        using HttpRequestMessage request = new(HttpMethod.Put, new Uri(proxy, "api/v1/files/usb/chunked.gcode"))
        {
            Content = new StreamContent(new GeneratedStream(ChunkedSize, Gate, sink.Reached), 64 * 1024),
        };
        request.Headers.TransferEncodingChunked = true;
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));

        using HttpResponseMessage response = await context.Browser.SendAsync(request, timeout.Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(ChunkedSize, sink.Received);
        Assert.True(sink.ContentMatches);
    }

    [Fact]
    public async Task SmallBodyArrivesWithItsContentHeaders()
    {
        await using ProxyTestContext context = new();
        Uri proxy = await context.OpenAsync();
        string? contentType = null;
        long? contentLength = null;
        context.Printer.Handler = async (request, ct) =>
        {
            contentType = request.Content!.Headers.ContentType?.ToString();
            contentLength = request.Content.Headers.ContentLength;
            await request.Content.ReadAsByteArrayAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        };

        using HttpResponseMessage response = await context.Browser.PostAsync(new Uri(proxy, "api/v1/job/1/pause"), new StringContent("{}", Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", contentType);
        Assert.Equal(2, contentLength);
    }
}
