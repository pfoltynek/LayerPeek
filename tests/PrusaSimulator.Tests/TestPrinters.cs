using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Time.Testing;

using PrusaTrayDiagnostic.Simulator.Printers;

namespace PrusaTrayDiagnostic.Simulator.Tests;

/// <summary>Simulated printers on ephemeral loopback ports and a plain HTTP client for them.</summary>
internal static partial class TestPrinters
{
    public const string Password = "Test-Pa55";

    public static readonly SimulationTiming Timing = SimulationTiming.Default with
    {
        HeatUp = TimeSpan.FromSeconds(90),
        Print = TimeSpan.FromMinutes(30),
    };

    public static Task<SimulatedPrinter> StartAsync(
        TimeProvider clock,
        PrinterScenario scenario = PrinterScenario.Printing,
        SimulatedModel? model = null,
        OfflineMode offlineMode = OfflineMode.Drop,
        string userName = SimulatedPrinterOptions.DefaultUserName) =>
        SimulatedPrinter.StartAsync(
            new SimulatedPrinterOptions
            {
                Model = model ?? SimulatedModel.CoreOne,
                Scenario = scenario,
                Port = 0,
                UserName = userName,
                Password = Password,
                Timing = Timing,
                OfflineMode = offlineMode,
                TimeProvider = clock,
            },
            TestContext.Current.CancellationToken);

    public static HttpClient CreateHttpClient(TimeSpan? timeout = null) =>
        new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false })
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(10),
        };

    /// <summary>GET with the firmware's Digest login (computed here, independently of the simulator and of the app).</summary>
    public static async Task<HttpResponseMessage> GetWithDigestAsync(HttpClient client, Uri uri, string password = Password, string userName = "maker")
    {
        using HttpResponseMessage challenge = await client.GetAsync(uri, TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, challenge.StatusCode);
        string nonce = Nonce(challenge);
        using HttpRequestMessage request = new(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Authorization", DigestHeader(userName, password, nonce, "GET", uri.PathAndQuery));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static string DigestHeader(string userName, string password, string nonce, string method, string uri)
    {
        string response = Md5($"{Md5($"{userName}:Printer API:{password}")}:{nonce}:{Md5($"{method}:{uri}")}");
        return $"Digest username=\"{userName}\", realm=\"Printer API\", nonce=\"{nonce}\", uri=\"{uri}\", response=\"{response}\"";
    }

    public static string Nonce(HttpResponseMessage challenge)
    {
        AuthenticationHeaderValue digest = Assert.Single(challenge.Headers.WwwAuthenticate);
        Assert.Equal("Digest", digest.Scheme);
        return NonceRegex().Match(digest.Parameter!).Groups[1].Value;
    }

    /// <summary>Property paths of a JSON document (e.g. "printer.temp_bed"), for comparing shapes with the fixtures.</summary>
    public static SortedSet<string> Shape(string json)
    {
        SortedSet<string> paths = new(StringComparer.Ordinal);
        using JsonDocument document = JsonDocument.Parse(json);
        Collect(document.RootElement, "", paths);
        return paths;
    }

    public static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "firmware", name + ".json"));

    private static void Collect(JsonElement element, string prefix, SortedSet<string> paths)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            string path = prefix + property.Name;
            paths.Add(path);
            Collect(property.Value, path + ".", paths);
        }
    }

#pragma warning disable CA5351 // The printer firmware mandates MD5 digest authentication.
    private static string Md5(string value) => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(value)));
#pragma warning restore CA5351

    [GeneratedRegex("nonce=\"([^\"]+)\"")]
    private static partial Regex NonceRegex();
}

/// <summary>A clock that starts at a fixed, realistic date.</summary>
internal sealed class SimulationClock() : FakeTimeProvider(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));
