using LayerPeek.Simulator;

if (!SimulatorOptions.TryParse(args, out SimulatorOptions options, out string? error))
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine("Use --help for the options.");
    return 2;
}

if (options.ShowHelp)
{
    Console.WriteLine(SimulatorOptions.Usage);
    return 0;
}

using CancellationTokenSource stop = new();
Console.CancelKeyPress += (_, e) =>
{
    // Shut down cleanly instead of being killed: the ports are released and clients see the connections close.
    e.Cancel = true;
    stop.Cancel();
};

SimulatorHost host;
try
{
    host = await SimulatorHost.StartAsync(options, stop.Token);
}
catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"The simulator could not start: {ex.Message}");
    return 1;
}
catch (OperationCanceledException)
{
    return 1;
}

await using (host)
{
    Console.WriteLine("PrusaSimulator is running. Enter in the app's settings (Printer: connection PrusaLink):");
    Console.WriteLine();
    for (int i = 0; i < host.Printers.Count; i++)
    {
        var printer = host.Printers[i];
        Console.WriteLine($"  Printer {i + 1}: {printer.Model.ProductName} (model code {printer.Model.Code})");
        Console.WriteLine($"    Address:   {printer.BaseUri}");
        Console.WriteLine($"    User name: {printer.UserName}");
        Console.WriteLine($"    Password:  {printer.Password}");
        if (host.Camera is { } camera)
        {
            Console.WriteLine($"    Camera:    {camera.StreamUri}");
        }
    }

    if (host.Printers.Count > 0 && host.Printers[0].ControlLoginRequired)
    {
        Console.WriteLine("Listening beyond this computer: the /_simulator/ control endpoints need the PrusaLink login.");
    }

    Console.WriteLine();
    ConsoleCommands.WriteState(host, Console.Out);
    Console.WriteLine();
    Console.WriteLine("Type 'help' for commands, Ctrl+C to stop.");

    Task stopped = Task.Delay(Timeout.Infinite, stop.Token).ContinueWith(static _ => { }, TaskScheduler.Default);
    // Console input cannot be cancelled: the reader runs on its own and is abandoned at shutdown.
    _ = Task.Run(async () =>
    {
        while (!stop.IsCancellationRequested)
        {
            string? line = await Console.In.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                // No console input (redirected and closed): keep running until Ctrl+C.
                return;
            }

            if (!ConsoleCommands.Execute(host, line, Console.Out))
            {
                await stop.CancelAsync().ConfigureAwait(false);
            }
        }
    });

    await stopped;
    Console.WriteLine("Stopping...");
}

Console.WriteLine("Stopped.");
return 0;
