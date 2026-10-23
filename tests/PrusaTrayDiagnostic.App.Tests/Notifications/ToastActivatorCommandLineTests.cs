using PrusaTrayDiagnostic.App.Notifications;

namespace PrusaTrayDiagnostic.App.Tests.Notifications;

/// <summary>
/// The command line Windows starts the app with for a click on a toast while the app is not running (T-068): the
/// <c>LocalServer32</c> value of the COM toast activator, and how a start recognizes it. In Debug builds the test app's
/// command line carries its test mode, so that a start by Windows is never a production start.
/// </summary>
public sealed class ToastActivatorCommandLineTests
{
    [Theory]
    [InlineData("-ToastActivated")]
    [InlineData("-toastactivated")]
    [InlineData("-Embedding")]
    [InlineData("/Embedding")]
    [InlineData("-embedding")]
    public void StartsByWindowsForAToastAreRecognized(string argument)
    {
        Assert.True(ToastActivatorCommandLine.IsActivationStart([argument]));
        Assert.True(ToastActivatorCommandLine.IsActivationStart(["--other", argument]));
    }

    [Theory]
    [InlineData]
    [InlineData("--ptd-e2e-run=C:\\x")]
    [InlineData("ToastActivated")]
    [InlineData("-ToastActivatedX")]
    [InlineData("-Embed")]
    public void OtherStartsAreNormalStarts(params string[] arguments)
    {
        Assert.False(ToastActivatorCommandLine.IsActivationStart(arguments));
    }

    [Fact]
    public void ServerCommandLineQuotesTheExecutableAndMarksTheStart()
    {
        Assert.Equal(
            "\"C:\\Program Files\\Prusa\\PrusaTrayDiagnostic.exe\" -ToastActivated",
            ToastActivatorCommandLine.For(@"C:\Program Files\Prusa\PrusaTrayDiagnostic.exe", []));
        Assert.Equal(
            "\"C:\\Apps\\PrusaTrayDiagnostic.exe\" -ToastActivated plain \"with space\"",
            ToastActivatorCommandLine.For(@"C:\Apps\PrusaTrayDiagnostic.exe", ["plain", "with space"]));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("a\tb", "\"a\tb\"")]
    [InlineData("C:\\dir\\", "C:\\dir\\")]
    [InlineData("C:\\my dir\\", "\"C:\\my dir\\\\\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("a\\\"b", "\"a\\\\\\\"b\"")]
    public void ArgumentsAreQuotedTheWayWindowsSplitsThem(string argument, string quoted)
    {
        Assert.Equal(quoted, ToastActivatorCommandLine.Quote(argument));
    }

    [Fact]
    public void EmptyExecutableIsRejected()
    {
        Assert.Throws<ArgumentException>(() => ToastActivatorCommandLine.For(" ", []));
        Assert.Throws<ArgumentNullException>(() => ToastActivatorCommandLine.For(@"C:\a.exe", null!));
        Assert.Throws<ArgumentNullException>(() => ToastActivatorCommandLine.IsActivationStart(null!));
    }

#if DEBUG
    private static readonly Dictionary<string, string?> TestVariables = new()
    {
        ["PRUSATRAYDIAGNOSTIC_TEST_DATA_DIR"] = @"C:\Temp\PrusaTrayDiagnostic.E2E\run 1\data",
        ["PRUSATRAYDIAGNOSTIC_INSTANCE_SUFFIX"] = "e2e-0123456789ab",
        ["PRUSATRAYDIAGNOSTIC_TEST_TOASTS"] = "1",
        ["PRUSATRAYDIAGNOSTIC_TEST_CREDENTIAL_PREFIX"] = null,
        ["PRUSATRAYDIAGNOSTIC_TEST_START_DELAY_MS"] = "",
        ["PRUSATRAYDIAGNOSTIC_INSTANCE_FORWARD_ONLY"] = "1",
        ["PATH"] = @"C:\Windows",
    };

    [Fact]
    public void TheTestAppCarriesItsTestModeAndItsOwnArgumentsToAStartByWindows()
    {
        List<string> arguments = ToastActivatorCommandLine.TestServerArguments(
            ["--ptd-e2e-run=C:\\Temp\\run 1", "-ToastActivated", "-Embedding", "--ptd-e2e-env=PATH=C:\\evil"],
            name => TestVariables.GetValueOrDefault(name));

        // Only the switches of the test mode (never the forward-only switch of the two-process test, never other variables),
        // and the process's own arguments without those of an earlier start by Windows.
        Assert.Equal(
            [
                @"--ptd-e2e-env=PRUSATRAYDIAGNOSTIC_TEST_DATA_DIR=C:\Temp\PrusaTrayDiagnostic.E2E\run 1\data",
                "--ptd-e2e-env=PRUSATRAYDIAGNOSTIC_INSTANCE_SUFFIX=e2e-0123456789ab",
                "--ptd-e2e-env=PRUSATRAYDIAGNOSTIC_TEST_TOASTS=1",
                @"--ptd-e2e-run=C:\Temp\run 1",
            ],
            arguments);
    }

    [Fact]
    public void TheTestModeOfTheCommandLineIsAppliedToTheProcess()
    {
        List<string> arguments = ToastActivatorCommandLine.TestServerArguments([], name => TestVariables.GetValueOrDefault(name));
        Dictionary<string, string> applied = [];

        Assert.True(ToastActivatorCommandLine.HasTestEnvironment(["-ToastActivated", .. arguments]));
        Assert.True(ToastActivatorCommandLine.TryApplyTestEnvironment(["-ToastActivated", .. arguments, "-Embedding"], (name, value) => applied[name] = value));

        Assert.Equal(@"C:\Temp\PrusaTrayDiagnostic.E2E\run 1\data", applied["PRUSATRAYDIAGNOSTIC_TEST_DATA_DIR"]);
        Assert.Equal("e2e-0123456789ab", applied["PRUSATRAYDIAGNOSTIC_INSTANCE_SUFFIX"]);
        Assert.Equal("1", applied["PRUSATRAYDIAGNOSTIC_TEST_TOASTS"]);
        Assert.Equal(3, applied.Count);
    }

    [Theory]
    [InlineData("--ptd-e2e-env=PATH=C:\\evil")]
    [InlineData("--ptd-e2e-env=PRUSATRAYDIAGNOSTIC_INSTANCE_FORWARD_ONLY=1")]
    [InlineData("--ptd-e2e-env=PRUSATRAYDIAGNOSTIC_TEST_DATA_DIR")]
    [InlineData("--ptd-e2e-env==x")]
    [InlineData("--ptd-e2e-env=")]
    public void AnyOtherVariableRefusesTheWholeStart(string argument)
    {
        Dictionary<string, string> applied = [];

        Assert.True(ToastActivatorCommandLine.HasTestEnvironment([argument]));
        Assert.False(ToastActivatorCommandLine.TryApplyTestEnvironment(
            ["--ptd-e2e-env=PRUSATRAYDIAGNOSTIC_TEST_DATA_DIR=C:\\d", argument],
            (name, value) => applied[name] = value));
        Assert.Empty(applied);
    }

    [Fact]
    public void WithoutTheTestModeTheCommandLineAppliesNothing()
    {
        Assert.False(ToastActivatorCommandLine.HasTestEnvironment(["-ToastActivated", "-Embedding"]));
        Assert.True(ToastActivatorCommandLine.TryApplyTestEnvironment(["-ToastActivated"], static (_, _) => Assert.Fail("Nothing to apply.")));
    }

    [Fact]
    public void TheServerCommandLineOfTheTestAppSplitsBackIntoItsArguments()
    {
        List<string> arguments = ToastActivatorCommandLine.TestServerArguments(["--ptd-e2e-run=C:\\Temp\\run 1"], name => TestVariables.GetValueOrDefault(name));

        string commandLine = ToastActivatorCommandLine.For(@"C:\src\bin\Debug\PrusaTrayDiagnostic.exe", arguments);

        Assert.Equal(
            "\"C:\\src\\bin\\Debug\\PrusaTrayDiagnostic.exe\" -ToastActivated "
            + "\"--ptd-e2e-env=PRUSATRAYDIAGNOSTIC_TEST_DATA_DIR=C:\\Temp\\PrusaTrayDiagnostic.E2E\\run 1\\data\" "
            + "--ptd-e2e-env=PRUSATRAYDIAGNOSTIC_INSTANCE_SUFFIX=e2e-0123456789ab "
            + "--ptd-e2e-env=PRUSATRAYDIAGNOSTIC_TEST_TOASTS=1 "
            + "\"--ptd-e2e-run=C:\\Temp\\run 1\"",
            commandLine);
    }
#endif
}
