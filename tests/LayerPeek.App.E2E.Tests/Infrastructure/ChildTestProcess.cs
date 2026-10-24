using System.Diagnostics;
using System.Text;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// This test assembly started again as a child process running one test method (the crash and guard self-tests of
/// T-065), with <see cref="E2EFixture.ChildVariable"/>=1 so its fixtures stay out of the parent's locks and folders.
/// Disposing kills it if it still runs.
/// </summary>
internal sealed class ChildTestProcess : IDisposable
{
    private readonly StringBuilder _output = new();

    private ChildTestProcess(Process process)
    {
        Process = process;
        process.OutputDataReceived += (_, e) => Append(e.Data);
        process.ErrorDataReceived += (_, e) => Append(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    public Process Process { get; }

    /// <summary>What the child wrote so far (xUnit's progress and summary).</summary>
    public string Output
    {
        get
        {
            lock (_output)
            {
                return _output.ToString();
            }
        }
    }

    /// <summary>
    /// Starts the test method <paramref name="method"/> (full name) in a child process; <paramref name="environment"/>
    /// sets (value) or removes (<see langword="null"/>) variables of the inherited environment.
    /// </summary>
    public static ChildTestProcess Start(string method, IReadOnlyDictionary<string, string?> environment)
    {
        string exe = Path.ChangeExtension(typeof(ChildTestProcess).Assembly.Location, ".exe");
        ProcessStartInfo startInfo = new(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in new[] { "-method", method, "-noColor", "-noLogo" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment[E2EFixture.RunVariable] = "1";
        startInfo.Environment[E2EFixture.ChildVariable] = "1";
        startInfo.Environment.Remove(E2EFixture.RestoreVariable);
        foreach ((string name, string? value) in environment)
        {
            if (value is null)
            {
                startInfo.Environment.Remove(name);
            }
            else
            {
                startInfo.Environment[name] = value;
            }
        }

        return new ChildTestProcess(Process.Start(startInfo) ?? throw new InvalidOperationException($"'{exe}' did not start."));
    }

    /// <summary>Waits for the end; fails with the output when it takes longer than <paramref name="timeout"/>.</summary>
    public async Task WaitForExitAsync(TimeSpan timeout)
    {
        try
        {
            await Process.WaitForExitAsync().WaitAsync(timeout);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"The child test process did not end within {timeout.TotalSeconds:0} s:{Environment.NewLine}{Output}", ex);
        }

        // The asynchronous readers get the rest of the output.
        Process.WaitForExit();
    }

    public void Dispose()
    {
        try
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
                Process.WaitForExit(10_000);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }

        Process.Dispose();
    }

    private void Append(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_output)
        {
            _output.AppendLine(line);
        }
    }
}
