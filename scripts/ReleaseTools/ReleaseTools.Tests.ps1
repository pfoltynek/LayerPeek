#Requires -Version 7.0
<#
.SYNOPSIS
    Pester 5+ tests of the release helpers (scripts/ReleaseTools/ReleaseTools.psm1).

.DESCRIPTION
    Covers the decisions of the release scripts: version source, tag, CHANGELOG.md section, runtime selection, remote
    selection (-Repo), git state checks (dirty tree, missing tag, tag not matching the version, tag missing or different
    on the remote, commit not on main), build marker, checksums, archives (without debug symbols), upload plan and
    publish checks. The git checks run against throw-away repositories: a bare "origin" and clones in a temporary
    folder, with an isolated git configuration. No network, no GitHub, gh is never called.

    Temporary files live in one folder under $env:PTD_RELEASE_TESTS_TEMP (set by the xUnit wrapper
    tests/PrusaTrayDiagnostic.Scripts.Tests, which also deletes it after a crash) or under the system temp folder.
    AfterAll deletes the folder and restores the environment.

.EXAMPLE
    Invoke-Pester scripts/ReleaseTools/ReleaseTools.Tests.ps1 -Output Detailed
#>

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1') -Force

    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).ProviderPath
    $base = if ($env:PTD_RELEASE_TESTS_TEMP) { $env:PTD_RELEASE_TESTS_TEMP } else { [System.IO.Path]::GetTempPath() }
    $script:TempRoot = Join-Path $base "prusatray-release-tests-$([guid]::NewGuid().ToString('N'))"
    $null = New-Item -ItemType Directory -Path $script:TempRoot

    # Git environment isolated from the user's configuration (hooks, signing, default branch); restored in AfterAll.
    $script:GitEnvironment = @{
        GIT_CONFIG_NOSYSTEM = '1'
        GIT_CONFIG_GLOBAL   = (Join-Path $script:TempRoot 'gitconfig')
        GIT_AUTHOR_NAME     = 'Release Tests'
        GIT_AUTHOR_EMAIL    = 'release-tests@example.invalid'
        GIT_COMMITTER_NAME  = 'Release Tests'
        GIT_COMMITTER_EMAIL = 'release-tests@example.invalid'
        GIT_TERMINAL_PROMPT = '0'
    }
    $script:SavedEnvironment = @{}
    foreach ($name in $script:GitEnvironment.Keys) {
        $script:SavedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $script:GitEnvironment[$name])
    }
    [System.IO.File]::WriteAllText($script:GitEnvironment.GIT_CONFIG_GLOBAL, '')

    function New-TempFile {
        param([Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [AllowEmptyString()] [string] $Content)
        $path = Join-Path $script:TempRoot $Name
        $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path)
        [System.IO.File]::WriteAllText($path, $Content, [System.Text.UTF8Encoding]::new($false))
        $path
    }

    function G {
        # git in a test repository; throws on failure. Avoid '-a'/'-m' style flags: PowerShell binds them to parameters.
        param([Parameter(Mandatory)] [string] $Repo, [Parameter(ValueFromRemainingArguments)] [string[]] $Arguments)
        $output = & git -C $Repo @Arguments 2>&1
        if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed: $output" }
        @($output | ForEach-Object { "$_" })
    }

    function Assert-Problems {
        # Exactly one problem matches each regex pattern, and there are no other problems.
        param([AllowNull()] [AllowEmptyCollection()] [string[]] $Problems, [string[]] $Patterns = @())
        $list = @($Problems | Where-Object { $_ })
        foreach ($pattern in $Patterns) {
            if (@($list | Where-Object { $_ -match $pattern }).Count -ne 1) { throw "Expected one problem matching '$pattern', got: $($list -join ' / ')" }
        }
        $unexpected = @($list | Where-Object { $p = $_; -not ($Patterns | Where-Object { $p -match $_ }) })
        if ($unexpected.Count -gt 0) { throw "Unexpected problems: $($unexpected -join ' / ')" }
    }

    function Assert-ThrowsMatching {
        param([Parameter(Mandatory)] [scriptblock] $Body, [Parameter(Mandatory)] [string] $Pattern)
        $message = $null
        try { & $Body } catch { $message = "$_" }
        if ($null -eq $message) { throw "Did not throw (expected '$Pattern')." }
        if ($message -notmatch $Pattern) { throw "Threw '$message', expected a message matching '$Pattern'." }
    }

    function Copy-With([object] $Source, [hashtable] $Changes) {
        $copy = $Source.PSObject.Copy()
        foreach ($key in $Changes.Keys) { $copy.$key = $Changes[$key] }
        $copy
    }
}

AfterAll {
    foreach ($name in $script:SavedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $script:SavedEnvironment[$name]) }
    if ($script:TempRoot -and (Test-Path -LiteralPath $script:TempRoot)) { Remove-Item -LiteralPath $script:TempRoot -Recurse -Force }
}

Describe 'Version and tag' {
    It 'reads <Version> from Directory.Build.props' {
        $props = New-TempFile 'v1/Directory.Build.props' '<Project><PropertyGroup><Version>0.1.0</Version><Nullable>enable</Nullable></PropertyGroup></Project>'
        Get-SourceVersion $props | Should -BeExactly '0.1.0'
    }
    It 'reads a valid version from the real Directory.Build.props' {
        Test-ReleaseVersion (Get-SourceVersion (Join-Path $script:RepoRoot 'Directory.Build.props')) | Should -BeTrue
    }
    It 'refuses a missing <Version>' {
        $props = New-TempFile 'v2/Directory.Build.props' '<Project><PropertyGroup><Nullable>enable</Nullable></PropertyGroup></Project>'
        Assert-ThrowsMatching { Get-SourceVersion $props } 'exactly one <Version>.*found 0'
    }
    It 'refuses two <Version> elements' {
        $props = New-TempFile 'v3/Directory.Build.props' '<Project><PropertyGroup><Version>0.1.0</Version></PropertyGroup><PropertyGroup><Version>0.2.0</Version></PropertyGroup></Project>'
        Assert-ThrowsMatching { Get-SourceVersion $props } 'found 2'
    }
    It 'refuses the version <_>, which is not X.Y.Z' -ForEach '1.0', '1.0.0-beta', '01.0.0', 'v1.0.0', '1.0.0.0' {
        $props = New-TempFile "v4/$_/Directory.Build.props" "<Project><PropertyGroup><Version>$_</Version></PropertyGroup></Project>"
        Assert-ThrowsMatching { Get-SourceVersion $props } 'not in the form X.Y.Z'
    }
    It 'converts between tag and version' {
        ConvertTo-ReleaseTag '0.1.0' | Should -BeExactly 'v0.1.0'
        Get-VersionFromTag 'v1.12.3' | Should -BeExactly '1.12.3'
    }
    It 'does not read a version from the tag <_>' -ForEach '0.1.0', 'v0.1', 'v0.1.0-rc1', 'release-0.1.0', '' {
        Get-VersionFromTag $_ | Should -BeNullOrEmpty
    }
    It 'marks 0.x as a pre-release and 1.x not' {
        Test-PrereleaseVersion '0.1.0' | Should -BeTrue
        Test-PrereleaseVersion '0.12.3' | Should -BeTrue
        Test-PrereleaseVersion '1.0.0' | Should -BeFalse
        Test-PrereleaseVersion '10.0.0' | Should -BeFalse
    }
}

Describe 'Runtimes' {
    It 'builds win-x64 on Windows and osx-arm64 on macOS by default (win-arm64 off until T-057)' {
        Resolve-ReleaseRuntimes -HostOs windows | Should -BeExactly @('win-x64')
        Resolve-ReleaseRuntimes -HostOs macos | Should -BeExactly @('osx-arm64')
    }
    It 'validates, normalizes and de-duplicates explicit runtimes' {
        Resolve-ReleaseRuntimes -Requested 'win-arm64', 'WIN-X64', 'win-arm64' -HostOs windows | Should -BeExactly @('win-arm64', 'win-x64')
    }
    It 'accepts a comma-separated list (pwsh -File passes it as one string)' {
        Resolve-ReleaseRuntimes -Requested 'win-x64, win-arm64' -HostOs windows | Should -BeExactly @('win-x64', 'win-arm64')
    }
    It 'refuses a runtime of the other OS with a clear message' {
        Assert-ThrowsMatching { Resolve-ReleaseRuntimes -Requested 'osx-arm64' -HostOs windows } "'osx-arm64' cannot be built on windows: Native AOT does not compile across operating systems"
        Assert-ThrowsMatching { Resolve-ReleaseRuntimes -Requested 'win-x64' -HostOs macos } 'cannot be built on macos'
    }
    It 'refuses unknown runtimes (osx-x64 is not released)' {
        Assert-ThrowsMatching { Resolve-ReleaseRuntimes -Requested 'osx-x64' -HostOs macos } "Unknown runtime 'osx-x64'"
        Assert-ThrowsMatching { Resolve-ReleaseRuntimes -Requested 'linux-x64' -HostOs windows } 'Unknown runtime'
    }
    It 'never uploads win-arm64 until T-057' {
        $config = Get-ReleaseRuntimeConfig
        $config['win-arm64'].Uploadable | Should -BeFalse
        $config['win-arm64'].Default | Should -BeFalse
        $config['win-x64'].Uploadable | Should -BeTrue
        $config['osx-arm64'].Uploadable | Should -BeTrue
    }
    It 'smoke-tests only the RID of this OS and architecture' {
        Test-RuntimeRunnable -Runtime win-x64 -HostOs windows -HostArchitecture x64 | Should -BeTrue
        Test-RuntimeRunnable -Runtime win-arm64 -HostOs windows -HostArchitecture x64 | Should -BeFalse
        Test-RuntimeRunnable -Runtime osx-arm64 -HostOs macos -HostArchitecture arm64 | Should -BeTrue
        Test-RuntimeRunnable -Runtime win-x64 -HostOs macos -HostArchitecture x64 | Should -BeFalse
    }
    It 'names the archives and checksum files' {
        Get-ArchiveFileName -Version 0.1.0 -Runtime win-x64 | Should -BeExactly 'PrusaTrayDiagnostic-0.1.0-win-x64.zip'
        Get-ArchiveFileName -Version 0.1.0 -Runtime osx-arm64 | Should -BeExactly 'PrusaTrayDiagnostic-0.1.0-osx-arm64.tar.gz'
        Get-ChecksumFileName windows | Should -BeExactly 'SHA256SUMS-windows.txt'
    }
}

Describe 'CHANGELOG.md' {
    BeforeAll {
        $script:Changelog = @'
# Changelog

## [Unreleased]

- not yet

## [0.2.0] - 2026-11-02

### Added

- Second thing.

## [0.1.0] - 2026-10-21

### Added

- First release.

[Unreleased]: https://example.invalid/compare/v0.2.0...HEAD
[0.2.0]: https://example.invalid/compare/v0.1.0...v0.2.0
'@
    }
    It 'extracts the section of a version' {
        $path = New-TempFile 'c1/CHANGELOG.md' $script:Changelog
        Get-ChangelogSection $path '0.2.0' | Should -BeExactly "### Added`n`n- Second thing."
    }
    It 'leaves the link reference definitions out of the last section' {
        $path = New-TempFile 'c2/CHANGELOG.md' $script:Changelog
        Get-ChangelogSection $path '0.1.0' | Should -BeExactly "### Added`n`n- First release."
    }
    It 'refuses a missing section or file' {
        $path = New-TempFile 'c3/CHANGELOG.md' $script:Changelog
        Assert-ThrowsMatching { Get-ChangelogSection $path '0.3.0' } "no section '## \[0.3.0\] - YYYY-MM-DD'"
        Assert-ThrowsMatching { Get-ChangelogSection (Join-Path $script:TempRoot 'missing.md') '0.1.0' } 'Changelog not found'
    }
    It 'refuses the heading <_> without a valid release date' -ForEach '## [0.1.0]', '## [0.1.0] - unreleased', '## [0.1.0] - Unreleased', '## [0.1.0] - RRRR-MM-DD', '## [0.1.0] - 2026-13-01' {
        $path = New-TempFile "c4/$([guid]::NewGuid().ToString('N')).md" "$_`n`n- Text.`n"
        Assert-ThrowsMatching { Get-ChangelogSection $path '0.1.0' } 'has no release date'
    }
    It 'refuses an empty section (sub-headings and comments only)' {
        $path = New-TempFile 'c5/CHANGELOG.md' "## [0.1.0] - 2026-10-21`n`n<!-- todo -->`n### Added`n`n## [0.0.9] - 2026-10-01`n`n- x`n"
        Assert-ThrowsMatching { Get-ChangelogSection $path '0.1.0' } 'is empty'
    }
    It 'refuses a duplicated section' {
        $path = New-TempFile 'c6/CHANGELOG.md' "## [0.1.0] - 2026-10-21`n- a`n## [0.1.0] - 2026-10-22`n- b`n"
        Assert-ThrowsMatching { Get-ChangelogSection $path '0.1.0' } '2 sections'
    }
    It 'does not treat the dots of the version as regex wildcards' {
        $path = New-TempFile 'c7/CHANGELOG.md' "## [0x1x0] - 2026-10-21`n- a`n"
        Assert-ThrowsMatching { Get-ChangelogSection $path '0.1.0' } 'no section'
    }
    It 'refuses the placeholder of the real CHANGELOG.md and reads it once dated' {
        $real = Join-Path $script:RepoRoot 'CHANGELOG.md'
        $version = Get-SourceVersion (Join-Path $script:RepoRoot 'Directory.Build.props')
        $content = [System.IO.File]::ReadAllText($real)
        $escaped = [regex]::Escape($version)
        $content | Should -Match "(?m)^## \[$escaped\] - "
        if ($content -match "(?m)^## \[$escaped\] - unreleased\s*$") {
            Assert-ThrowsMatching { Get-ChangelogSection $real $version } 'has no release date.*unreleased'
        }
        $dated = $content -replace "(?m)^## \[$escaped\] - .*$", "## [$version] - 2026-10-21"
        $notes = Get-ChangelogSection (New-TempFile 'c8/CHANGELOG.md' $dated) $version
        $notes | Should -Not -BeNullOrEmpty
        $notes | Should -Not -Match '(?m)^\[(Unreleased|\d+\.\d+\.\d+)\]:'
    }
}

Describe 'Git state checks (pure)' {
    BeforeAll {
        $script:Head = 'a' * 40
        $script:GoodState = [pscustomobject]@{
            Head = $script:Head; StatusLines = @(); HeadTags = @('v0.1.0'); RemoteTagCommit = $script:Head; OnMain = $true
            Remote = 'origin'; MainBranch = 'main'; RemoteErrors = @()
        }
    }
    It 'accepts a clean, tagged, pushed commit on main' {
        Assert-Problems (Get-ReleaseGitProblems -State $script:GoodState -Version '0.1.0')
    }
    It 'refuses a dirty tree (untracked files included)' {
        Assert-Problems (Get-ReleaseGitProblems -State (Copy-With $script:GoodState @{ StatusLines = @('?? new.txt', ' M a.cs') }) -Version '0.1.0') 'not clean \(2 changed or untracked files: \?\? new.txt'
    }
    It 'refuses a commit without the tag' {
        Assert-Problems (Get-ReleaseGitProblems -State (Copy-With $script:GoodState @{ HeadTags = @('some-other-tag') }) -Version '0.1.0') 'HEAD has no tag v0.1.0'
    }
    It 'refuses a tag not matching the version' {
        Assert-Problems (Get-ReleaseGitProblems -State (Copy-With $script:GoodState @{ HeadTags = @('v0.2.0') }) -Version '0.1.0') 'HEAD is tagged v0.2.0 but the version .* is 0.1.0'
    }
    It 'refuses a tag missing on the remote' {
        Assert-Problems (Get-ReleaseGitProblems -State (Copy-With $script:GoodState @{ RemoteTagCommit = $null }) -Version '0.1.0') 'not on origin.*git push origin v0.1.0'
    }
    It 'refuses a tag pointing elsewhere on the remote' {
        Assert-Problems (Get-ReleaseGitProblems -State (Copy-With $script:GoodState @{ RemoteTagCommit = 'b' * 40 }) -Version '0.1.0') 'on origin points to b{40}, but HEAD is a{40}'
    }
    It 'refuses a commit not on main' {
        Assert-Problems (Get-ReleaseGitProblems -State (Copy-With $script:GoodState @{ OnMain = $false }) -Version '0.1.0') 'not contained in origin/main'
    }
    It 'reports an unreachable remote instead of a missing tag' {
        Assert-Problems (Get-ReleaseGitProblems -State (Copy-With $script:GoodState @{ RemoteTagCommit = $null; OnMain = $null; RemoteErrors = @('git ls-remote origin failed: x') }) -Version '0.1.0') 'ls-remote origin failed'
    }
}

Describe 'Remote selection (-Repo)' {
    BeforeAll {
        $script:Remotes = [ordered]@{
            origin = 'https://github.com/pfoltynek/PrusaTrayDiagnostic.git'
            trial  = 'git@github.com:pfoltynek/PrusaTrayDiagnostic-release-trial.git'
            mirror = 'https://example.com/pfoltynek/PrusaTrayDiagnostic-release-trial.git'
        }
    }
    It 'uses origin and its repository by default' {
        $target = Resolve-ReleaseRemote -Remotes $script:Remotes
        $target.Remote | Should -BeExactly 'origin'
        $target.Repository | Should -BeExactly 'pfoltynek/PrusaTrayDiagnostic'
        $target.Problem | Should -BeNullOrEmpty
    }
    It 'uses the remote of the -Repo repository, so that checks and release concern the same repository' {
        $target = Resolve-ReleaseRemote -Remotes $script:Remotes -Repository 'pfoltynek/PrusaTrayDiagnostic-release-trial'
        $target.Remote | Should -BeExactly 'trial'
        $target.Repository | Should -BeExactly 'pfoltynek/PrusaTrayDiagnostic-release-trial'
        $target.Problem | Should -BeNullOrEmpty
    }
    It 'refuses a -Repo without a git remote, with the command to add one' {
        $target = Resolve-ReleaseRemote -Remotes $script:Remotes -Repository 'someone/else'
        $target.Remote | Should -BeNullOrEmpty
        $target.Problem | Should -Match 'No git remote points to github.com/someone/else.*git remote add trial https://github.com/someone/else.git'
    }
    It 'refuses a -Repo with two git remotes' {
        $remotes = [ordered]@{ a = 'https://github.com/o/r.git'; b = 'git@github.com:o/r.git' }
        (Resolve-ReleaseRemote -Remotes $remotes -Repository 'o/r').Problem | Should -Match 'Several git remotes .*: a, b'
    }
    It 'refuses a malformed -Repo and a missing or non-GitHub origin' {
        (Resolve-ReleaseRemote -Remotes $script:Remotes -Repository 'not a repo').Problem | Should -Match 'not in the form owner/name'
        (Resolve-ReleaseRemote -Remotes ([ordered]@{ trial = 'https://github.com/o/r.git' })).Problem | Should -Match 'origin is not configured'
        (Resolve-ReleaseRemote -Remotes ([ordered]@{ origin = 'https://example.com/o/r.git' })).Problem | Should -Match 'origin is not a GitHub repository'
    }
    It 'parses GitHub remote URLs' {
        Get-GitHubRepository 'https://github.com/pfoltynek/PrusaTrayDiagnostic.git' | Should -BeExactly 'pfoltynek/PrusaTrayDiagnostic'
        Get-GitHubRepository 'git@github.com:owner/repo.git' | Should -BeExactly 'owner/repo'
        Get-GitHubRepository 'https://github.com/owner/repo' | Should -BeExactly 'owner/repo'
        Get-GitHubRepository 'https://example.com/owner/repo.git' | Should -BeNullOrEmpty
    }
}

Describe 'Git state checks (throw-away repositories)' {
    BeforeAll {
        $script:Origin = Join-Path $script:TempRoot 'origin.git'
        $script:Work = Join-Path $script:TempRoot 'work'
        $null = & git init --quiet --bare --initial-branch=main $script:Origin 2>&1
        if ($LASTEXITCODE -ne 0) { throw 'git init --bare failed' }
        $null = & git clone --quiet $script:Origin $script:Work 2>&1
        if ($LASTEXITCODE -ne 0) { throw 'git clone failed' }
        $null = G $script:Work checkout --quiet -b main
        [System.IO.File]::WriteAllText((Join-Path $script:Work 'a.txt'), 'one')
        $null = G $script:Work add a.txt
        $null = G $script:Work commit --quiet --message one
        $null = G $script:Work push --quiet origin main
        $script:First = G $script:Work rev-parse HEAD | Select-Object -First 1

        function State([string] $Tag = 'v0.1.0', [string] $Remote = 'origin') { Get-ReleaseGitState -RepoRoot $script:Work -Tag $Tag -Remote $Remote }
    }

    It 'refuses an untagged commit on main: no tag, not on origin' {
        Assert-Problems (Get-ReleaseGitProblems -State (State) -Version '0.1.0') 'HEAD has no tag v0.1.0', 'not on origin'
    }
    It 'refuses a local annotated tag until it is pushed' {
        $null = G $script:Work tag --annotate --message 'Release 0.1.0' v0.1.0
        Assert-Problems (Get-ReleaseGitProblems -State (State) -Version '0.1.0') 'not on origin'
    }
    It 'accepts a pushed annotated tag on main (the peeled commit is compared)' {
        $null = G $script:Work push --quiet origin v0.1.0
        $state = State
        $state.RemoteTagCommit | Should -BeExactly $script:First
        $state.OnMain | Should -BeTrue
        Assert-Problems (Get-ReleaseGitProblems -State $state -Version '0.1.0')
    }
    It 'refuses a version bumped without a new tag' {
        Assert-Problems (Get-ReleaseGitProblems -State (State 'v0.2.0') -Version '0.2.0') 'HEAD is tagged v0.1.0 but the version .* is 0.2.0', 'v0.2.0 is not on origin'
    }
    It 'refuses an untracked file' {
        [System.IO.File]::WriteAllText((Join-Path $script:Work 'untracked.txt'), 'x')
        try { Assert-Problems (Get-ReleaseGitProblems -State (State) -Version '0.1.0') 'not clean \(1 .*untracked.txt' }
        finally { Remove-Item -LiteralPath (Join-Path $script:Work 'untracked.txt') -Force }
    }
    It 'refuses a modified tracked file' {
        [System.IO.File]::WriteAllText((Join-Path $script:Work 'a.txt'), 'changed')
        try { Assert-Problems (Get-ReleaseGitProblems -State (State) -Version '0.1.0') 'not clean' }
        finally { $null = G $script:Work checkout --quiet -- a.txt }
    }
    It 'refuses a tag moved on origin' {
        $null = G $script:Work checkout --quiet -b other
        [System.IO.File]::WriteAllText((Join-Path $script:Work 'b.txt'), 'two')
        $null = G $script:Work add b.txt
        $null = G $script:Work commit --quiet --message two
        $second = G $script:Work rev-parse HEAD | Select-Object -First 1
        $null = G $script:Work push --quiet --force origin "$($second):refs/tags/v0.1.0"
        $null = G $script:Work checkout --quiet main
        try { Assert-Problems (Get-ReleaseGitProblems -State (State) -Version '0.1.0') "points to $second, but HEAD is $($script:First)" }
        finally { $null = G $script:Work push --quiet --force origin 'refs/tags/v0.1.0:refs/tags/v0.1.0' }
        Assert-Problems (Get-ReleaseGitProblems -State (State) -Version '0.1.0')
    }
    It 'refuses a tagged and pushed commit outside main' {
        $null = G $script:Work checkout --quiet other
        try {
            $null = G $script:Work tag v0.3.0
            $null = G $script:Work push --quiet origin v0.3.0
            $state = State 'v0.3.0'
            $state.OnMain | Should -BeFalse
            Assert-Problems (Get-ReleaseGitProblems -State $state -Version '0.3.0') 'not contained in origin/main'
        }
        finally { $null = G $script:Work checkout --quiet main }
    }
    It 'refuses with a fetch hint when origin/main has commits not fetched yet, and changes no refs' {
        $other = Join-Path $script:TempRoot 'other'
        $null = & git clone --quiet $script:Origin $other 2>&1
        if ($LASTEXITCODE -ne 0) { throw 'git clone failed' }
        [System.IO.File]::WriteAllText((Join-Path $other 'c.txt'), 'three')
        $null = G $other add c.txt
        $null = G $other commit --quiet --message three
        $null = G $other push --quiet origin main
        $newMain = G $other rev-parse HEAD | Select-Object -First 1
        $refsBefore = (G $script:Work show-ref) -join "`n"
        Assert-Problems (Get-ReleaseGitProblems -State (State) -Version '0.1.0') "origin/main \($newMain\) is not in the local repository. Run: git fetch origin"
        (G $script:Work show-ref) -join "`n" | Should -BeExactly $refsBefore
        $null = G $script:Work fetch --quiet origin
        Assert-Problems (Get-ReleaseGitProblems -State (State) -Version '0.1.0')
    }
    It 'checks the tag and main of another remote (trial repository) independently of origin' {
        $trial = Join-Path $script:TempRoot 'trial.git'
        $null = & git init --quiet --bare --initial-branch=main $trial 2>&1
        if ($LASTEXITCODE -ne 0) { throw 'git init --bare failed' }
        $null = G $script:Work remote add trial $trial
        try {
            Assert-Problems (Get-ReleaseGitProblems -State (State -Remote trial) -Version '0.1.0') 'Branch main does not exist on trial'
            $null = G $script:Work push --quiet trial main v0.1.0
            Assert-Problems (Get-ReleaseGitProblems -State (State -Remote trial) -Version '0.1.0')
        }
        finally { $null = G $script:Work remote remove trial }
    }
    It 'checks only local facts when no remote could be resolved' {
        Assert-Problems (Get-ReleaseGitProblems -State (State -Remote '') -Version '0.1.0') 'not checked: there is no remote'
    }
    It 'reports an unreachable origin as a remote error' {
        $null = G $script:Work remote set-url origin (Join-Path $script:TempRoot 'does-not-exist.git')
        try { Assert-Problems (Get-ReleaseGitProblems -State (State) -Version '0.1.0') 'ls-remote origin failed' }
        finally { $null = G $script:Work remote set-url origin $script:Origin }
    }
}

Describe 'Archives and checksums' {
    BeforeAll {
        $script:Publish = Join-Path $script:TempRoot 'publish'
        $null = New-Item -ItemType Directory -Path (Join-Path $script:Publish 'sub'), (Join-Path $script:Publish 'App.dSYM' 'Contents')
        [System.IO.File]::WriteAllText((Join-Path $script:Publish 'app.exe'), 'binary')
        [System.IO.File]::WriteAllText((Join-Path $script:Publish 'app.pdb'), 'symbols')
        [System.IO.File]::WriteAllText((Join-Path $script:Publish 'sub' 'lib.dll'), 'library')
        [System.IO.File]::WriteAllText((Join-Path $script:Publish 'sub' 'lib.PDB'), 'symbols')
        [System.IO.File]::WriteAllText((Join-Path $script:Publish 'App.dSYM' 'Contents' 'Info.plist'), 'symbols')
        $script:Dist = Join-Path $script:TempRoot 'dist'
        $null = New-Item -ItemType Directory -Path $script:Dist
    }
    It 'treats <_> as a debug symbol' -ForEach 'a.pdb', 'sub/b.PDB', 'App.dSYM/Contents/Info.plist', 'x\\App.dSYM\\y' {
        Test-ArchiveExcluded $_ | Should -BeTrue
    }
    It 'does not treat <_> as a debug symbol' -ForEach 'app.exe', 'pdb/readme.txt', 'a.pdb.txt', 'dSYM.txt' {
        Test-ArchiveExcluded $_ | Should -BeFalse
    }
    It 'zips the publish folder content without the folder itself and without debug symbols' {
        $zip = New-ReleaseArchive -SourceDirectory $script:Publish -Destination (Join-Path $script:Dist 'a-win-x64.zip')
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
        try { @($archive.Entries | ForEach-Object FullName | Sort-Object) | Should -BeExactly @('app.exe', 'sub/lib.dll') }
        finally { $archive.Dispose() }
        Assert-ThrowsMatching { New-ReleaseArchive -SourceDirectory $script:Publish -Destination $zip } 'already exists'
    }
    It 'tars the publish folder content without the folder itself and without debug symbols' {
        $tgz = New-ReleaseArchive -SourceDirectory $script:Publish -Destination (Join-Path $script:Dist 'a-osx-arm64.tar.gz')
        $entries = @(& (Get-TarCommand) -tzf $tgz | Where-Object { $_ -and -not $_.EndsWith('/') } | ForEach-Object { $_ -replace '^\./', '' } | Sort-Object)
        $entries | Should -BeExactly @('app.exe', 'sub/lib.dll')
    }
    It 'refuses to archive a folder with nothing but debug symbols' {
        $symbolsOnly = Join-Path $script:TempRoot 'symbols-only'
        $null = New-Item -ItemType Directory -Path $symbolsOnly
        [System.IO.File]::WriteAllText((Join-Path $symbolsOnly 'a.pdb'), 'x')
        Assert-ThrowsMatching { New-ReleaseArchive -SourceDirectory $symbolsOnly -Destination (Join-Path $script:TempRoot 'empty.zip') } 'Nothing to archive'
    }
    It 'writes SHA256SUMS in sha256sum format matching Get-FileHash and detects tampering' {
        $sums = Write-Sha256Sums -Directory $script:Dist -FileName 'SHA256SUMS-windows.txt' -Files 'a-win-x64.zip', 'a-osx-arm64.tar.gz'
        [System.IO.File]::ReadAllText($sums) | Should -Not -Match "`r"
        $entries = Read-Sha256Sums $sums
        @($entries.Keys) | Should -BeExactly @('a-osx-arm64.tar.gz', 'a-win-x64.zip')
        $entries['a-win-x64.zip'] | Should -BeExactly (Get-FileHash -LiteralPath (Join-Path $script:Dist 'a-win-x64.zip') -Algorithm SHA256).Hash.ToLowerInvariant()
        Assert-Problems (Get-Sha256SumsProblems $sums)
        [System.IO.File]::AppendAllText((Join-Path $script:Dist 'a-win-x64.zip'), 'x')
        Assert-Problems (Get-Sha256SumsProblems $sums) 'a-win-x64.zip does not match'
        Remove-Item -LiteralPath (Join-Path $script:Dist 'a-osx-arm64.tar.gz')
        Assert-Problems (Get-Sha256SumsProblems $sums) 'a-win-x64.zip does not match', 'a-osx-arm64.tar.gz is listed .* but missing'
    }
    It 'refuses malformed checksum files' {
        Assert-ThrowsMatching { Read-Sha256Sums (New-TempFile 's1.txt' "abc  file.zip`n") } 'Malformed'
        Assert-ThrowsMatching { Read-Sha256Sums (New-TempFile 's2.txt' "$('0' * 64)  ../file.zip`n") } 'Malformed'
        Assert-ThrowsMatching { Read-Sha256Sums (New-TempFile 's3.txt' "$('0' * 64)  f.zip`n$('1' * 64)  f.zip`n") } 'Duplicate'
    }
}

Describe 'Build marker' {
    BeforeAll {
        $script:Head = 'a' * 40
        $script:Info = [pscustomobject]@{
            Version = '0.1.0'; Commit = $script:Head; Dirty = $false; Runtimes = @('win-x64'); ChecksumFile = 'SHA256SUMS-windows.txt'
            Files = @([pscustomobject]@{ Name = 'P-0.1.0-win-x64.zip'; Runtime = 'win-x64'; Sha256 = '0' * 64 })
        }
        $script:DistNames = @('P-0.1.0-win-x64.zip', 'SHA256SUMS-windows.txt')
        function Problems($Info, [string[]] $Dist = $script:DistNames) { Get-BuildInfoProblems -BuildInfo $Info -Version '0.1.0' -Head $script:Head -DistFiles $Dist }
    }
    It 'accepts a build of HEAD from a clean tree' {
        Assert-Problems (Problems $script:Info)
    }
    It 'refuses missing build output' {
        Assert-Problems (Get-BuildInfoProblems -BuildInfo $null -Version '0.1.0' -Head $script:Head -DistFiles @()) 'No build output'
    }
    It 'refuses a build of another commit or version or from a dirty tree' {
        Assert-Problems (Problems (Copy-With $script:Info @{ Commit = 'c' * 40 })) 'from commit c{40}, HEAD is a{40}'
        Assert-Problems (Problems (Copy-With $script:Info @{ Version = '0.0.9' })) 'version 0.0.9'
        Assert-Problems (Problems (Copy-With $script:Info @{ Dirty = $true })) 'uncommitted or untracked'
    }
    It 'refuses a build containing win-arm64 until T-057' {
        Assert-Problems (Problems (Copy-With $script:Info @{ Runtimes = @('win-x64', 'win-arm64') })) 'contains win-arm64.*T-057'
    }
    It 'refuses files added to or missing in dist' {
        Assert-Problems (Problems $script:Info ($script:DistNames + 'old.zip')) 'not produced by the build: old.zip'
        Assert-Problems (Problems $script:Info @('SHA256SUMS-windows.txt')) 'missing in dist: P-0.1.0-win-x64.zip'
    }
}

Describe 'Upload plan' {
    BeforeAll {
        $script:Local = @(
            [pscustomobject]@{ Name = 'new.zip'; Sha256 = '1' * 64 },
            [pscustomobject]@{ Name = 'same.zip'; Sha256 = '2' * 64 },
            [pscustomobject]@{ Name = 'changed.zip'; Sha256 = '3' * 64 },
            [pscustomobject]@{ Name = 'nodigest.zip'; Sha256 = '4' * 64 }
        )
        $script:Remote = @(
            [pscustomobject]@{ name = 'same.zip'; digest = "sha256:$('2' * 64)" },
            [pscustomobject]@{ name = 'changed.zip'; digest = "sha256:$('9' * 64)" },
            [pscustomobject]@{ name = 'nodigest.zip'; digest = $null },
            [pscustomobject]@{ name = 'SHA256SUMS-macos.txt'; digest = "sha256:$('5' * 64)" }
        )
        function Summary($Plan) { @($Plan | ForEach-Object { "$($_.Name)=$($_.Action)" }) }
    }
    It 'uploads new files, skips identical ones and reports different ones as conflicts' {
        Summary (Get-UploadPlan -LocalFiles $script:Local -RemoteAssets $script:Remote) | Should -BeExactly @('new.zip=Upload', 'same.zip=Skip', 'changed.zip=Conflict', 'nodigest.zip=Conflict')
    }
    It 'replaces only different files with -Force' {
        Summary (Get-UploadPlan -LocalFiles $script:Local -RemoteAssets $script:Remote -Force) | Should -BeExactly @('new.zip=Upload', 'same.zip=Skip', 'changed.zip=Replace', 'nodigest.zip=Replace')
    }
    It 'uploads everything into a new release' {
        @(Get-UploadPlan -LocalFiles $script:Local -RemoteAssets @() | ForEach-Object Action) | Should -BeExactly @('Upload', 'Upload', 'Upload', 'Upload')
    }
}

Describe 'Publish checks' {
    BeforeAll {
        $script:WinSums = [ordered]@{ 'P-0.1.0-win-x64.zip' = '1' * 64 }
        $script:MacSums = [ordered]@{ 'P-0.1.0-osx-arm64.tar.gz' = '2' * 64 }
        $script:Complete = @(
            [pscustomobject]@{ name = 'P-0.1.0-win-x64.zip'; digest = "sha256:$('1' * 64)" },
            [pscustomobject]@{ name = 'SHA256SUMS-windows.txt'; digest = "sha256:$('7' * 64)" },
            [pscustomobject]@{ name = 'P-0.1.0-osx-arm64.tar.gz'; digest = "sha256:$('2' * 64)" },
            [pscustomobject]@{ name = 'SHA256SUMS-macos.txt'; digest = "sha256:$('8' * 64)" }
        )
        $script:BothSums = [ordered]@{ 'SHA256SUMS-windows.txt' = $script:WinSums; 'SHA256SUMS-macos.txt' = $script:MacSums }
        function Release([object[]] $Assets, [bool] $Draft = $true) { [pscustomobject]@{ isDraft = $Draft; assets = $Assets; url = 'https://example.invalid' } }
    }
    It 'accepts a complete draft' {
        Assert-Problems (Get-PublishProblems -Release (Release $script:Complete) -Tag v0.1.0 -ExpectedOs windows, macos -Checksums $script:BothSums)
    }
    It 'refuses a missing or already published release' {
        Assert-Problems (Get-PublishProblems -Release $null -Tag v0.1.0 -ExpectedOs windows -Checksums @{}) 'no release for v0.1.0'
        Assert-Problems (Get-PublishProblems -Release (Release $script:Complete $false) -Tag v0.1.0 -ExpectedOs windows, macos -Checksums $script:BothSums) 'already published'
    }
    It 'refuses a draft without the macOS files unless incomplete releases are allowed' {
        $windowsOnly = @($script:Complete | Select-Object -First 2)
        $sums = [ordered]@{ 'SHA256SUMS-windows.txt' = $script:WinSums }
        Assert-Problems (Get-PublishProblems -Release (Release $windowsOnly) -Tag v0.1.0 -ExpectedOs windows, macos -Checksums $sums) 'no SHA256SUMS-macos.txt'
        Assert-Problems (Get-PublishProblems -Release (Release $windowsOnly) -Tag v0.1.0 -ExpectedOs @() -Checksums $sums)
    }
    It 'refuses missing, different, unlisted and win-arm64 files' {
        $assets = @($script:Complete | Where-Object name -NE 'P-0.1.0-osx-arm64.tar.gz') + @(
            [pscustomobject]@{ name = 'stray.txt'; digest = "sha256:$('3' * 64)" },
            [pscustomobject]@{ name = 'P-0.1.0-win-arm64.zip'; digest = "sha256:$('4' * 64)" }
        )
        $assets[0] = [pscustomobject]@{ name = 'P-0.1.0-win-x64.zip'; digest = "sha256:$('6' * 64)" }
        Assert-Problems (Get-PublishProblems -Release (Release $assets) -Tag v0.1.0 -ExpectedOs windows, macos -Checksums $script:BothSums) `
            'P-0.1.0-osx-arm64.tar.gz is listed .* but not uploaded', 'Uploaded P-0.1.0-win-x64.zip does not match', 'stray.txt is uploaded but not listed', 'P-0.1.0-win-arm64.zip is uploaded but not listed', 'P-0.1.0-win-arm64.zip belongs to win-arm64'
    }
}
