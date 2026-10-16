using System.Net;

using Microsoft.Extensions.Logging.Abstractions;

using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Secrets;

namespace PrusaTrayDiagnostic.PrusaLink.Tests;

/// <summary>
/// Replays responses recorded from real printers by tools/record-prusalink-fixtures.ps1
/// (Fixtures/recorded/&lt;printer&gt;/&lt;state&gt;/{status,job,version,info}.json; job.json is absent when the printer answered 204).
/// </summary>
public class RecordedFixturesTests
{
    private const string NoRecordings = "(none)";

    private static readonly string RecordedDirectory = Path.Combine(Fixtures.Directory, "recorded");

    public static TheoryData<string> Recordings
    {
        get
        {
            TheoryData<string> data = [];
            if (Directory.Exists(RecordedDirectory))
            {
                foreach (string statusFile in Directory.GetFiles(RecordedDirectory, "status.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                {
                    data.Add(Path.GetRelativePath(RecordedDirectory, Path.GetDirectoryName(statusFile)!));
                }
            }

            if (data.Count == 0)
            {
                data.Add(NoRecordings);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Recordings))]
    public async Task RecordedResponsesAreUnderstood(string recording)
    {
        Assert.SkipWhen(recording == NoRecordings, "No responses recorded yet (run tools/record-prusalink-fixtures.ps1).");
        string directory = Path.Combine(RecordedDirectory, recording);
        FakePrusaLinkServer server = new();
        await RouteAsync(server, "/api/v1/status", Path.Combine(directory, "status.json"));
        await RouteAsync(server, "/api/v1/job", Path.Combine(directory, "job.json"));
        await RouteAsync(server, "/api/version", Path.Combine(directory, "version.json"));
        await RouteAsync(server, "/api/v1/info", Path.Combine(directory, "info.json"));
        InMemorySecretStore secrets = new();
        await secrets.SetAsync(SecretKeys.ForPrinter("recorded"), server.Password, TestContext.Current.CancellationToken);
        using HttpClient httpClient = new(server);
        PrusaLinkClient client = new(
            httpClient,
            new PrinterDefinition { Id = "recorded", Name = "recorded", ConnectionType = ConnectionType.PrusaLink, Address = "printer.local" },
            secrets,
            TimeProvider.System,
            NullLogger<PrusaLinkClient>.Instance);

        PrinterSnapshot snapshot = await client.GetSnapshotAsync(TestContext.Current.CancellationToken);
        Assert.True(snapshot.State.IsReachable(), $"state {snapshot.State} ({snapshot.StatusMessage})");
        Assert.NotNull(snapshot.Telemetry);
        if (snapshot.State.HasActiveJob())
        {
            Assert.NotNull(snapshot.Job);
        }

        if (File.Exists(Path.Combine(directory, "version.json")))
        {
            PrusaLinkInfoResult info = await client.GetInfoAsync(TestContext.Current.CancellationToken);
            Assert.Equal(PrinterState.Idle, info.State);
            Assert.NotNull(info.Info!.ApiVersion);
        }
    }

    private static async Task RouteAsync(FakePrusaLinkServer server, string path, string file)
    {
        if (File.Exists(file))
        {
            server.Route(path, HttpStatusCode.OK, await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
        }
        else
        {
            server.Route(path, HttpStatusCode.NoContent);
        }
    }
}
