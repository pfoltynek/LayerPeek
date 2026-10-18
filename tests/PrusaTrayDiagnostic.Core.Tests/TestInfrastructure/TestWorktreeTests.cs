using PrusaTrayDiagnostic.Tests.Shared;

namespace PrusaTrayDiagnostic.Core.Tests.TestInfrastructure;

/// <summary>The worktree id that scopes test locks and reserved %TEMP% folders (T-032).</summary>
public sealed class TestWorktreeTests
{
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "PrusaTrayDiagnostic.Core.Tests.IdProbe", "Repo");

    [Fact]
    public void IdIsShortLowercaseHexAndStable()
    {
        string id = TestWorktree.IdOf(Base);

        Assert.Equal(8, id.Length);
        Assert.True(TestWorktree.IsId(id), id);
        Assert.Equal(id, TestWorktree.IdOf(Base));
    }

    [Fact]
    public void IdIgnoresTrailingSeparatorsAndRelativeSegments()
    {
        string id = TestWorktree.IdOf(Base);
        char separator = Path.DirectorySeparatorChar;

        Assert.Equal(id, TestWorktree.IdOf(Base + separator));
        Assert.Equal(id, TestWorktree.IdOf(Path.Combine(Base, "sub", "..")));
        Assert.Equal(id, TestWorktree.IdOf(Path.Combine(Base, ".")));
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(id, TestWorktree.IdOf(Base.Replace('\\', '/')));
        }
    }

    [Fact]
    public void IdIgnoresCaseOnWindowsOnly()
    {
        bool sameId = TestWorktree.IdOf(Base.ToUpperInvariant()) == TestWorktree.IdOf(Base.ToLowerInvariant());

        Assert.Equal(OperatingSystem.IsWindows(), sameId);
    }

    [Fact]
    public void DifferentWorktreesGetDifferentIds()
    {
        Assert.NotEqual(TestWorktree.IdOf(Base), TestWorktree.IdOf(Base + "2"));
        Assert.NotEqual(TestWorktree.IdOf(Base), TestWorktree.IdOf(Path.Combine(Base, "nested")));
    }

    [Theory]
    [InlineData("0123abcd", true)]
    [InlineData("0123ABCD", false)]
    [InlineData("0123abc", false)]
    [InlineData("0123abcde", false)]
    [InlineData("0123abcg", false)]
    [InlineData("Snapshot", false)]
    public void IsIdAcceptsExactlyEightLowercaseHexCharacters(string text, bool expected) =>
        Assert.Equal(expected, TestWorktree.IsId(text));

    [Fact]
    public void CurrentWorktreeIsTheRepositoryRootTheTestsWereBuiltFrom()
    {
        Assert.True(File.Exists(Path.Combine(TestWorktree.Root, "PrusaTrayDiagnostic.slnx")), TestWorktree.Root);
        Assert.Equal(TestWorktree.Normalize(TestWorktree.Root), TestWorktree.Root);
        Assert.Equal(TestWorktree.IdOf(TestWorktree.Root), TestWorktree.Id);
    }

    [Fact]
    public void ReservedFolderOfThisAssemblyCarriesTheWorktreeIdAndMarker()
    {
        Assert.Equal(Path.Combine(Path.GetTempPath(), $"PrusaTrayDiagnostic.Core.Tests.{TestWorktree.Id}"), TestDirectories.Root);
        // Created by the assembly fixture, with the marker naming this worktree.
        Assert.Equal(TestWorktree.Root, File.ReadAllText(TestDirectories.Reserved.MarkerPath));
    }
}
