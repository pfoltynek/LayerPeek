using System.Net;
using System.Text.Json;

using LayerPeek.Core.Previews;
using LayerPeek.Simulator.Printers;

namespace LayerPeek.Simulator.Tests;

/// <summary>G-code previews of the simulated printer (<c>/thumb/s</c>, <c>/thumb/l</c>, T-086) over real loopback HTTP.</summary>
public sealed class SimulatedPreviewTests
{
    private const string Large = "thumb/l/usb/BENCHY~1.BGC";
    private const string Small = "thumb/s/usb/BENCHY~1.BGC";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly SimulationClock _clock = new();

    [Theory]
    [InlineData(Large)]
    [InlineData(Small)]
    public async Task PreviewsNeedTheLogin(string path)
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock);
        using HttpClient client = TestPrinters.CreateHttpClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(printer.BaseUri, path), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Digest", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        Assert.Equal(0, printer.PreviewRequestCount);
    }

    [Theory]
    [InlineData(Large, 380, 285)]
    [InlineData(Small, 16, 16)]
    public async Task PreviewsArePngsOfTheFirmwareSizes(string path, int width, int height)
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock);
        using HttpClient client = TestPrinters.CreateHttpClient();

        using HttpResponseMessage response = await TestPrinters.GetWithDigestAsync(client, new Uri(printer.BaseUri, path));
        byte[] body = await response.Content.ReadAsByteArrayAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Matches("^\"[0-9]+\"$", response.Headers.ETag?.ToString());
        Assert.True(PrintPreview.TryReadSize(body, out int actualWidth, out int actualHeight));
        Assert.Equal((width, height), (actualWidth, actualHeight));
        Assert.Equal(1, printer.PreviewRequestCount);
    }

    [Fact]
    public async Task LinksOfTheJobPointToServedPreviewsOfEveryFile()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock);
        using HttpClient client = TestPrinters.CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestPrinters.Password);
        HashSet<string> distinct = new(StringComparer.Ordinal);

        foreach (SimulatedFile file in PrinterSimulation.AllFiles)
        {
            SimulatedJob job = new(42, 10, 1000, 100, file);
            using JsonDocument json = JsonDocument.Parse(PrusaLinkJson.Job(job, "PRINTING"));
            JsonElement refs = json.RootElement.GetProperty("file").GetProperty("refs");
            foreach (string link in new[] { refs.GetProperty("icon").GetString()!, refs.GetProperty("thumbnail").GetString()! })
            {
                byte[] png = await client.GetByteArrayAsync(new Uri(printer.BaseUri, link.TrimStart('/')), Ct);
                Assert.NotNull(PrintPreview.FromPng(png));
                distinct.Add(Convert.ToBase64String(png));
            }
        }

        // Every file and size has an image of its own.
        Assert.Equal(PrinterSimulation.AllFiles.Count * 2, distinct.Count);
    }

    [Fact]
    public async Task MatchingETagIsAnsweredWithNotModified()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock);
        using HttpClient client = TestPrinters.CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestPrinters.Password);
        Uri uri = new(printer.BaseUri, Large);
        using HttpResponseMessage first = await client.GetAsync(uri, Ct);
        string etag = first.Headers.ETag!.ToString();

        using HttpRequestMessage conditional = new(HttpMethod.Get, uri);
        conditional.Headers.TryAddWithoutValidation("If-None-Match", etag);
        using HttpResponseMessage notModified = await client.SendAsync(conditional, Ct);
        using HttpRequestMessage otherTag = new(HttpMethod.Get, uri);
        otherTag.Headers.TryAddWithoutValidation("If-None-Match", "\"1\"");
        using HttpResponseMessage modified = await client.SendAsync(otherTag, Ct);

        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
        Assert.Equal(etag, notModified.Headers.ETag?.ToString());
        Assert.Empty(await notModified.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal(HttpStatusCode.OK, modified.StatusCode);
    }

    [Fact]
    public async Task NoPreviewModeAnswersLikeTheFirmwareForAFileWithoutPreview()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock);
        using HttpClient client = TestPrinters.CreateHttpClient();

        using HttpResponseMessage off = await client.PostAsync(new Uri(printer.BaseUri, "_simulator/previews/off"), null, Ct);
        string stateOff = await off.Content.ReadAsStringAsync(Ct);
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(printer.BaseUri, Large));
        request.Headers.Add("X-Api-Key", TestPrinters.Password);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        using HttpResponseMessage missing = await client.SendAsync(request, Ct);
        string error = await missing.Content.ReadAsStringAsync(Ct);
        using HttpResponseMessage on = await client.PostAsync(new Uri(printer.BaseUri, "_simulator/previews/on"), null, Ct);
        using HttpRequestMessage again = new(HttpMethod.Get, new Uri(printer.BaseUri, Large));
        again.Headers.Add("X-Api-Key", TestPrinters.Password);
        using HttpResponseMessage served = await client.SendAsync(again, Ct);

        Assert.Contains("\"previews\":false", stateOff, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("{\"title\": \"404: Not Found\",\"message\":\"File doesn't contain preview\"}", error);
        Assert.Equal(HttpStatusCode.OK, on.StatusCode);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.True(printer.PreviewsEnabled);
    }

    [Fact]
    public async Task PreviewsCanBeDisabledFromTheStart()
    {
        await using SimulatedPrinter printer = await SimulatedPrinter.StartAsync(
            new SimulatedPrinterOptions { Port = 0, Password = TestPrinters.Password, TimeProvider = _clock, Previews = false },
            Ct);
        using HttpClient client = TestPrinters.CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestPrinters.Password);

        using HttpResponseMessage response = await client.GetAsync(new Uri(printer.BaseUri, Small), Ct);

        Assert.False(printer.PreviewsEnabled);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("thumb/x/usb/BENCHY~1.BGC")]
    [InlineData("thumb/l/usb/MISSING.BGC")]
    [InlineData("thumb/l/BENCHY~1.BGC")]
    public async Task UnknownSizeOrFileIsNotFound(string path)
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock);
        using HttpClient client = TestPrinters.CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestPrinters.Password);

        using HttpResponseMessage response = await client.GetAsync(new Uri(printer.BaseUri, path), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OtherMethodsAreNotAllowed()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock);
        using HttpClient client = TestPrinters.CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestPrinters.Password);

        using HttpResponseMessage response = await client.DeleteAsync(new Uri(printer.BaseUri, Large), Ct);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public void PreviewsAreGeneratedOnceAndETagsAreStable()
    {
        SimulatedFile benchy = PrinterSimulation.AllFiles[0];

        Assert.Same(PreviewImages.For(benchy, large: true), PreviewImages.For(benchy, large: true));
        Assert.Equal(PreviewImages.ETag(benchy), PreviewImages.ETag(benchy with { DisplayName = "renamed" }));
        Assert.NotEqual(PreviewImages.ETag(benchy), PreviewImages.ETag(PrinterSimulation.AllFiles[1]));
    }
}
