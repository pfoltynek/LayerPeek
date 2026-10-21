using System.Collections.Concurrent;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PrusaTrayDiagnostic.Core.Monitoring;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Secrets;
using PrusaTrayDiagnostic.PrusaLink;
using PrusaTrayDiagnostic.Simulator.Printers;

namespace PrusaTrayDiagnostic.Simulator.Tests;

/// <summary>
/// The app's PrusaLink client, registered exactly as the app does (<see cref="PrusaLinkServiceCollectionExtensions.AddPrusaLink"/>),
/// and the Core monitor against simulated printers: what the settings test, monitoring, Diagnostics and the camera
/// overlay get from a simulated printer.
/// </summary>
public sealed class AppClientAgainstSimulatorTests : IAsyncDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly SimulationClock _clock = new();
    private readonly InMemorySecretStore _secrets = new();
    private readonly ServiceProvider _services;

    public AppClientAgainstSimulatorTests()
    {
        ServiceCollection services = new();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ISecretStore>(_secrets);
        services.AddPrusaLink();
        _services = services.BuildServiceProvider();
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    public static TheoryData<string, string, PrinterModel> Models => new()
    {
        { "coreone", "7.1.0", PrinterModel.CoreOne },
        { "mk4s", "1.4.1", PrinterModel.Mk4S },
        { "xl", "3.1.0", PrinterModel.Xl },
        { "mini", "2.1.0", PrinterModel.Mini },
    };

    [Theory]
    [MemberData(nameof(Models))]
    public async Task ConnectionTestReportsTheSimulatedModel(string key, string code, PrinterModel model)
    {
        Assert.True(SimulatedModel.TryParse(key, out SimulatedModel simulated));
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock, model: simulated);
        PrusaLinkClient client = await CreateClientAsync(printer);

        PrusaLinkInfoResult info = await client.GetInfoAsync(Ct);
        PrinterModelDetection detection = await client.DetectModelAsync(Ct);

        Assert.Equal(PrusaLinkFailure.None, info.Failure);
        Assert.Equal(code, info.Info!.PrinterModel);
        Assert.Equal(simulated.Hostname, info.Info.Hostname);
        Assert.Equal(SimulatedModel.Firmware, info.Info.Firmware);
        Assert.Equal(0.4, info.Info.NozzleDiameter);
        Assert.Equal(PrinterModelDetectionStatus.Detected, detection.Status);
        Assert.Equal(model, PrinterModels.FromCode(detection.Code!.Value));
    }

    [Fact]
    public async Task SnapshotOfAPrintHasJobFileProgressAndTelemetry()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock);
        PrusaLinkClient client = await CreateClientAsync(printer);
        _clock.Advance(TimeSpan.FromSeconds(90) + TimeSpan.FromMinutes(15));

        PrinterSnapshot snapshot = await client.GetSnapshotAsync(Ct);

        Assert.Equal(PrinterState.Printing, snapshot.State);
        Assert.Null(snapshot.StatusMessage);
        Assert.Equal(42, snapshot.Job!.Id);
        Assert.Equal("benchy_0.4n_0.2mm_PLA.bgcode", snapshot.Job.FileName);
        Assert.Equal(50, snapshot.Job.ProgressPercent, 1);
        Assert.Equal(TimeSpan.FromSeconds(900), snapshot.Job.Remaining);
        Assert.Equal(TimeSpan.FromSeconds(990), snapshot.Job.Elapsed);
        Assert.Equal(215, snapshot.Telemetry!.Nozzle!.Value.Target);
        Assert.InRange(snapshot.Telemetry.Nozzle.Value.Actual, 214, 216);
        Assert.Equal(60, snapshot.Telemetry.Bed!.Value.Target);
        Assert.Equal(4900, snapshot.Telemetry.PrintFanRpm);
        Assert.Equal(7200, snapshot.Telemetry.HotendFanRpm);
    }

    [Fact]
    public async Task ScenarioTransitionsAreSeenByTheClient()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock);
        PrusaLinkClient client = await CreateClientAsync(printer);
        List<PrinterSnapshot> snapshots = [];

        snapshots.Add(await client.GetSnapshotAsync(Ct)); // heating
        _clock.Advance(TimeSpan.FromMinutes(10));
        snapshots.Add(await client.GetSnapshotAsync(Ct)); // printing
        printer.Simulation.SetScenario(PrinterScenario.Paused);
        snapshots.Add(await client.GetSnapshotAsync(Ct));
        printer.Simulation.SetScenario(PrinterScenario.Attention);
        snapshots.Add(await client.GetSnapshotAsync(Ct));
        printer.Simulation.SetScenario(PrinterScenario.Printing);
        _clock.Advance(TimeSpan.FromMinutes(22));
        snapshots.Add(await client.GetSnapshotAsync(Ct)); // finished
        printer.Simulation.SetScenario(PrinterScenario.Error);
        snapshots.Add(await client.GetSnapshotAsync(Ct));
        printer.Simulation.SetScenario(PrinterScenario.Idle);
        snapshots.Add(await client.GetSnapshotAsync(Ct));

        Assert.Equal(
            [PrinterState.Printing, PrinterState.Printing, PrinterState.Paused, PrinterState.Attention, PrinterState.Finished, PrinterState.Error, PrinterState.Idle],
            snapshots.Select(static s => s.State));
        Assert.Null(snapshots[0].Job!.Remaining);
        Assert.Equal(snapshots[1].Job!.ProgressPercent, snapshots[2].Job!.ProgressPercent);
        // The client reports the finished job as complete (it remembers the last job).
        Assert.Equal(100, snapshots[4].Job!.ProgressPercent);
        Assert.Equal(TimeSpan.Zero, snapshots[4].Job!.Remaining);
        Assert.Null(snapshots[5].Job);
        Assert.Null(snapshots[6].Job);
    }

    [Fact]
    public async Task WrongPasswordIsUnauthorized()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock);
        PrusaLinkClient client = await CreateClientAsync(printer, password: "wrong");

        PrinterSnapshot snapshot = await client.GetSnapshotAsync(Ct);
        PrusaLinkInfoResult info = await client.GetInfoAsync(Ct);

        Assert.Equal(PrinterState.Unauthorized, snapshot.State);
        Assert.Equal(PrusaLinkFailure.PasswordRejected, info.Failure);
    }

    [Fact]
    public async Task CustomUserNameWorks()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock, userName: "operator");
        PrusaLinkClient client = await CreateClientAsync(printer, userName: "operator");

        Assert.Equal(PrinterState.Printing, (await client.GetSnapshotAsync(Ct)).State);
    }

    [Fact]
    public async Task ExpiredNonceIsRenewedWithoutAFailure()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock);
        PrusaLinkClient client = await CreateClientAsync(printer);
        Assert.Equal(PrinterState.Printing, (await client.GetSnapshotAsync(Ct)).State);
        int reads = _secrets.ReadCount;

        _clock.Advance(DigestVerifier.NonceLifetime + TimeSpan.FromSeconds(10));
        PrinterSnapshot snapshot = await client.GetSnapshotAsync(Ct);

        Assert.Equal(PrinterState.Printing, snapshot.State);
        // stale=true: the client keeps the password and only takes the new nonce.
        Assert.Equal(reads, _secrets.ReadCount);
    }

    [Fact]
    public async Task OfflinePrinterIsReportedOfflineAndRecovers()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock);
        PrusaLinkClient client = await CreateClientAsync(printer);
        Assert.Equal(PrinterState.Printing, (await client.GetSnapshotAsync(Ct)).State);

        printer.Simulation.SetOffline(true);
        PrinterSnapshot offline = await client.GetSnapshotAsync(Ct);
        PrusaLinkInfoResult offlineInfo = await client.GetInfoAsync(Ct);
        printer.Simulation.SetOffline(false);
        PrinterSnapshot recovered = await client.GetSnapshotAsync(Ct);

        Assert.Equal(PrinterState.Offline, offline.State);
        Assert.Equal(PrusaLinkFailure.NotReachable, offlineInfo.Failure);
        Assert.Equal(PrinterState.Printing, recovered.State);
    }

    [Fact]
    public async Task HangingOfflinePrinterRunsIntoTheClientTimeout()
    {
        await using SimulatedPrinter printer = await TestPrinters.StartAsync(_clock, offlineMode: OfflineMode.Hang);
        PrusaLinkClient client = await CreateClientAsync(printer);
        printer.Simulation.SetOffline(true);

        PrusaLinkInfoResult info = await client.GetInfoAsync(Ct);

        Assert.Equal(PrinterState.Offline, info.State);
        Assert.Equal(PrusaLinkFailure.Timeout, info.Failure);
    }

    [Fact]
    public async Task MonitorFollowsSeveralPrintersAndAnOutage()
    {
        await using SimulatedPrinter coreOne = await TestPrinters.StartAsync(_clock, PrinterScenario.Printing, SimulatedModel.CoreOne);
        await using SimulatedPrinter mini = await TestPrinters.StartAsync(_clock, PrinterScenario.Idle, SimulatedModel.Mini);
        PrinterDefinition first = await DefineAsync(coreOne, "sim-1");
        PrinterDefinition second = await DefineAsync(mini, "sim-2");
        // The monitor runs on the real clock (short polling interval); the printers on the fake one.
        PrinterMonitor monitor = new(
            _services.GetRequiredService<PrusaLinkClientFactory>(),
            TimeProvider.System,
            NullLogger<PrinterMonitor>.Instance,
            new PrinterMonitorOptions { MaxBackoff = TimeSpan.Zero });
        ConcurrentQueue<PrinterModelDetected> models = new();
        ConcurrentQueue<PrinterSnapshot> snapshots = new();
        monitor.ModelDetected += (_, e) => models.Enqueue(e);
        monitor.SnapshotUpdated += (_, e) => snapshots.Enqueue(e);
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        Task run = monitor.RunAsync([first, second], TimeSpan.FromMilliseconds(50), stop.Token);
        try
        {
            await WaitUntilAsync(() => State(monitor, "sim-1") == PrinterState.Printing && State(monitor, "sim-2") == PrinterState.Idle);
            mini.Simulation.SetOffline(true);
            await WaitUntilAsync(() => State(monitor, "sim-2") == PrinterState.Offline);
            mini.Simulation.SetOffline(false);
            await WaitUntilAsync(() => State(monitor, "sim-2") == PrinterState.Idle);
            await WaitUntilAsync(() => models.Count == 2);
        }
        finally
        {
            await stop.CancelAsync();
            await run;
        }

        Assert.Equal(PrinterState.Printing, State(monitor, "sim-1"));
        Assert.Equal(PrinterModel.CoreOne, Assert.Single(models, static m => m.PrinterId == "sim-1").Model);
        Assert.Equal(PrinterModel.Mini, Assert.Single(models, static m => m.PrinterId == "sim-2").Model);
        Assert.Contains(snapshots, static s => s.PrinterId == "sim-1" && s.Job?.FileName == "benchy_0.4n_0.2mm_PLA.bgcode");
    }

    private static PrinterState? State(PrinterMonitor monitor, string id) =>
        monitor.TryGetSnapshot(id, out PrinterSnapshot? snapshot) ? snapshot.State : null;

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    private async Task<PrinterDefinition> DefineAsync(SimulatedPrinter printer, string id, string? password = null, string? userName = null)
    {
        PrinterDefinition definition = new()
        {
            Id = id,
            Name = "Simulator " + printer.Model.ProductName,
            ConnectionType = ConnectionType.PrusaLink,
            Address = printer.BaseUri.ToString(),
            UserName = userName ?? PrinterUserName.Default,
        };
        await _secrets.SetAsync(SecretKeys.ForPrinter(id), password ?? TestPrinters.Password, Ct);
        return definition;
    }

    private async Task<PrusaLinkClient> CreateClientAsync(SimulatedPrinter printer, string? password = null, string? userName = null) =>
        _services.GetRequiredService<PrusaLinkClientFactory>().Create(await DefineAsync(printer, "sim", password, userName));

    private sealed class InMemorySecretStore : ISecretStore
    {
        private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.OrdinalIgnoreCase);
        private int _readCount;

        public int ReadCount => Volatile.Read(ref _readCount);

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _readCount);
            return Task.FromResult(_secrets.TryGetValue(key, out string? secret) ? secret : null);
        }

        public Task SetAsync(string key, string secret, CancellationToken cancellationToken)
        {
            _secrets[key] = secret;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken) => Task.FromResult(_secrets.TryRemove(key, out _));
    }
}
