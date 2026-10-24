using LayerPeek.App.E2E.Tests.Infrastructure;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// The decisions of the installer change (T-017) without installing anything: which installation the restore may
/// uninstall (never a real one of the user) and how an old journal entry is treated. Runs in every <c>dotnet test</c>.
/// </summary>
public sealed class InstallerKindTests
{
    private static readonly DateTime Began = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AnInstallationOfTheTestVersionMadeAfterTheChangeBeganIsTheTests()
    {
        Assert.Null(InstallerKind.InstallationConflict("0.1.0", Began, "0.1.0", Began.AddMinutes(1), Began.AddMinutes(1)));
    }

    [Fact]
    public void AnInstallationOfAnotherVersionIsLeftAlone()
    {
        string? conflict = InstallerKind.InstallationConflict("0.1.0", Began, "0.2.0", Began.AddMinutes(1), Began.AddMinutes(1));

        Assert.NotNull(conflict);
        Assert.Contains("0.2.0", conflict, StringComparison.Ordinal);
        Assert.Contains("left alone", conflict, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInstallationMadeBeforeTheChangeBeganIsLeftAlone()
    {
        Assert.NotNull(InstallerKind.InstallationConflict("0.1.0", Began, "0.1.0", Began.AddHours(-1), Began.AddHours(-1)));
    }

    [Fact]
    public void ASlightlyEarlierCreationTimeStillCounts()
    {
        // The file system may round creation times.
        Assert.Null(InstallerKind.InstallationConflict("0.1.0", Began, "0.1.0", Began.AddSeconds(-2), null));
    }

    /// <summary>An interrupted uninstall: <c>current</c> is gone, a locked <c>Update.exe</c> made by the test is left.</summary>
    [Fact]
    public void AHalfDeletedTestInstallationIsTheTests()
    {
        Assert.Null(InstallerKind.InstallationConflict("0.1.0", Began, installedVersion: null, currentCreatedUtc: null, updateExeCreatedUtc: Began.AddMinutes(1)));
    }

    [Fact]
    public void AHalfDeletedInstallationFromBeforeTheChangeIsLeftAlone()
    {
        Assert.NotNull(InstallerKind.InstallationConflict("0.1.0", Began, installedVersion: null, currentCreatedUtc: null, updateExeCreatedUtc: Began.AddDays(-3)));
        Assert.NotNull(InstallerKind.InstallationConflict("0.1.0", Began, installedVersion: null, currentCreatedUtc: null, updateExeCreatedUtc: null));
    }

    [Theory]
    [InlineData("logs|12")]
    [InlineData("logs")]
    [InlineData("logs|x|ABC")]
    public void AJournalEntryOfAnOlderFormatNeverCountsAsRestored(string entry)
    {
        Assert.False(InstallerKind.StartsWithOriginal(entry));
    }
}
