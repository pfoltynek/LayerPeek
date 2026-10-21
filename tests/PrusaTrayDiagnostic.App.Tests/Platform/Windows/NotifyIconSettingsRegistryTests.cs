using System.Runtime.Versioning;

using Microsoft.Win32;

using PrusaTrayDiagnostic.App.Platform.Windows;

namespace PrusaTrayDiagnostic.App.Tests.Platform.Windows;

/// <summary>
/// The real registry access, under a test key of its own (<see cref="TestRegistry"/>), never under Explorer's
/// <c>Control Panel\NotifyIconSettings</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NotifyIconSettingsRegistryTests : IDisposable
{
    private readonly string _subKey = TestRegistry.NewSubKey();

    public NotifyIconSettingsRegistryTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows registry");
    }

    public void Dispose() => TestRegistry.Delete(_subKey);

    [Fact]
    public void MissingParentKeyReadsAsNoEntries()
    {
        Assert.Empty(new NotifyIconSettingsRegistry(_subKey).ReadEntries());
    }

    [Fact]
    public void EntriesAreReadWithExpandedPaths()
    {
        CreateEntry("1", @"{F38BF404-1D43-42F2-9305-67DE0B28FC23}\explorer.exe", promoted: null);
        CreateEntry("2", @"C:\Apps\PrusaTrayDiagnostic.exe", promoted: 0);
        CreateEntry("3", path: null, promoted: 1);

        NotifyIconEntry[] entries = [.. new NotifyIconSettingsRegistry(_subKey).ReadEntries().OrderBy(e => e.KeyName, StringComparer.Ordinal)];

        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.Equal(
            [
                new NotifyIconEntry("1", Path.Combine(windows, "explorer.exe"), HasPromotion: false),
                new NotifyIconEntry("2", @"C:\Apps\PrusaTrayDiagnostic.exe", HasPromotion: true),
                new NotifyIconEntry("3", null, HasPromotion: true),
            ],
            entries);
    }

    [Theory]
    [InlineData(@"{F38BF404-1D43-42F2-9305-67DE0B28FC23}\explorer.exe", @"C:\Windows\explorer.exe")]
    [InlineData(@"{f38bf404-1d43-42f2-9305-67de0b28fc23}\System32\x.exe", @"C:\Windows\System32\x.exe")]
    [InlineData(@"C:\Apps\PrusaTrayDiagnostic.exe", @"C:\Apps\PrusaTrayDiagnostic.exe")]
    [InlineData(@"{00000000-0000-0000-0000-000000000001}\a.exe", @"{00000000-0000-0000-0000-000000000001}\a.exe")]
    [InlineData(@"{not-a-guid}\a.exe", @"{not-a-guid}\a.exe")]
    [InlineData("{F38BF404-1D43-42F2-9305-67DE0B28FC23}", "{F38BF404-1D43-42F2-9305-67DE0B28FC23}")]
    [InlineData("", "")]
    public void KnownFolderPrefixIsExpanded(string stored, string expected)
    {
        Guid windows = new("F38BF404-1D43-42F2-9305-67DE0B28FC23");

        string expanded = NotifyIconSettingsRegistry.ExpandKnownFolder(stored, id => id == windows ? @"C:\Windows" : null);

        Assert.Equal(expected, expanded);
    }

    [Fact]
    public void PromoteWritesDwordOne()
    {
        CreateEntry("1", @"C:\Apps\PrusaTrayDiagnostic.exe", promoted: null);

        new NotifyIconSettingsRegistry(_subKey).Promote("1");

        using RegistryKey key = Registry.CurrentUser.OpenSubKey(_subKey + @"\1")!;
        Assert.Equal(RegistryValueKind.DWord, key.GetValueKind("IsPromoted"));
        Assert.Equal(1, key.GetValue("IsPromoted"));
    }

    [Fact]
    public void PromoteOfAVanishedEntryDoesNothing()
    {
        CreateEntry("1", @"C:\Apps\PrusaTrayDiagnostic.exe", promoted: null);

        new NotifyIconSettingsRegistry(_subKey).Promote("2");

        Assert.Null(Registry.CurrentUser.OpenSubKey(_subKey + @"\2"));
    }

    [Fact]
    public void ClearPromotionDeletesOnlyThatValue()
    {
        CreateEntry("1", @"C:\Apps\PrusaTrayDiagnostic.exe", promoted: 1);
        CreateEntry("2", @"C:\Other\Other.exe", promoted: 1);
        CreateEntry("3", @"C:\Apps\PrusaTrayDiagnostic.exe", promoted: null);
        NotifyIconSettingsRegistry settings = new(_subKey);

        settings.ClearPromotion("1");
        settings.ClearPromotion("3");
        settings.ClearPromotion("missing");

        Assert.Equal(
            [
                new NotifyIconEntry("1", @"C:\Apps\PrusaTrayDiagnostic.exe", HasPromotion: false),
                new NotifyIconEntry("2", @"C:\Other\Other.exe", HasPromotion: true),
                new NotifyIconEntry("3", @"C:\Apps\PrusaTrayDiagnostic.exe", HasPromotion: false),
            ],
            settings.ReadEntries().OrderBy(e => e.KeyName, StringComparer.Ordinal));
        Assert.Null(Registry.CurrentUser.OpenSubKey(_subKey + @"\missing"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"1\..")]
    [InlineData(@"..\x")]
    public void KeyNamesWithSeparatorsAreRejected(string keyName)
    {
        NotifyIconSettingsRegistry settings = new(_subKey);

        Assert.ThrowsAny<ArgumentException>(() => settings.Promote(keyName));
        Assert.ThrowsAny<ArgumentException>(() => settings.ClearPromotion(keyName));
    }

    private void CreateEntry(string name, string? path, int? promoted)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(_subKey + @"\" + name);
        if (path is not null)
        {
            key.SetValue("ExecutablePath", path, RegistryValueKind.String);
        }

        if (promoted is { } value)
        {
            key.SetValue("IsPromoted", value, RegistryValueKind.DWord);
        }
    }
}
