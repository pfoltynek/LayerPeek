#Requires -Version 7.0
<#
.SYNOPSIS
    Self-check of the release helpers (scripts/ReleaseTools/ReleaseTools.psm1) without a test framework.

.DESCRIPTION
    Checks the decisions of the release scripts: version source, tag, CHANGELOG.md section, runtime selection,
    git state checks (dirty tree, missing tag, tag not matching the version, tag missing or different on origin,
    commit not on main), build marker, checksums, archives, upload plan and publish checks.
    The git checks run against throw-away repositories: a bare "origin" and a clone in a temporary folder, with an
    isolated git configuration. No network and no GitHub access; gh is never called. The temporary folder is always
    deleted and the environment restored, also after a failure.
    Exit code 0 = all checks passed.

.EXAMPLE
    pwsh scripts/ReleaseTools/Test-ReleaseTools.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1') -Force

$script:failures = [System.Collections.Generic.List[string]]::new()
$script:passed = 0

function Test {
    param([Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [scriptblock] $Body)
    try {
        & $Body
        $script:passed++
        Write-Host "  ok    $Name"
    }
    catch {
        $script:failures.Add("$Name`: $_")
        Write-Host "  FAIL  $Name`: $_" -ForegroundColor Red
    }
}

function Assert-Equal {
    param($Expected, $Actual, [string] $Message = '')
    $e = @($Expected) -join '|'
    $a = @($Actual) -join '|'
    if ($e -cne $a) { throw "Expected '$e' but was '$a'. $Message" }
}

function Assert-True {
    param([bool] $Condition, [string] $Message = 'condition is false')
    if (-not $Condition) { throw $Message }
}

function Assert-Throws {
    param([Parameter(Mandatory)] [scriptblock] $Body, [Parameter(Mandatory)] [string] $Pattern)
    try { & $Body }
    catch {
        if ("$_" -notmatch $Pattern) { throw "Threw '$_', expected a message matching '$Pattern'." }
        return
    }
    throw "Did not throw (expected '$Pattern')."
}

function Assert-Problems {
    # Exactly one problem matches each pattern, and there are no other problems.
    param([AllowEmptyCollection()] [string[]] $Problems, [string[]] $Patterns = @())
    $list = @($Problems | Where-Object { $_ })
    foreach ($pattern in $Patterns) {
        if (@($list | Where-Object { $_ -match $pattern }).Count -ne 1) { throw "Expected one problem matching '$pattern', got: $($list -join ' / ')" }
    }
    $unexpected = @($list | Where-Object { $p = $_; -not ($Patterns | Where-Object { $p -match $_ }) })
    if ($unexpected.Count -gt 0) { throw "Unexpected problems: $($unexpected -join ' / ')" }
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "prusatray-release-selftest-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $tempRoot
# Git environment isolated from the user's configuration (hooks, signing, default branch); restored at the end.
$gitEnvironment = @{
    GIT_CONFIG_NOSYSTEM = '1'
    GIT_CONFIG_GLOBAL   = (Join-Path $tempRoot 'gitconfig')
    GIT_AUTHOR_NAME     = 'Release Self-Test'
    GIT_AUTHOR_EMAIL    = 'selftest@example.invalid'
    GIT_COMMITTER_NAME  = 'Release Self-Test'
    GIT_COMMITTER_EMAIL = 'selftest@example.invalid'
    GIT_TERMINAL_PROMPT = '0'
}
$savedEnvironment = @{}
foreach ($name in $gitEnvironment.Keys) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }

function New-TempFile {
    param([Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [AllowEmptyString()] [string] $Content)
    $path = Join-Path $tempRoot $Name
    $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path)
    [System.IO.File]::WriteAllText($path, $Content, [System.Text.UTF8Encoding]::new($false))
    $path
}

function G {
    # git in a test repository; throws on failure.
    param([Parameter(Mandatory)] [string] $Repo, [Parameter(ValueFromRemainingArguments)] [string[]] $Arguments)
    $output = & git -C $Repo @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed: $output" }
    @($output | ForEach-Object { "$_" })
}

try {
    foreach ($name in $gitEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $gitEnvironment[$name]) }
    [System.IO.File]::WriteAllText($gitEnvironment.GIT_CONFIG_GLOBAL, '')

    Write-Host 'Version and tag'
    Test 'reads <Version> from Directory.Build.props' {
        $props = New-TempFile 'v1/Directory.Build.props' '<Project><PropertyGroup><Version>0.1.0</Version><Nullable>enable</Nullable></PropertyGroup></Project>'
        Assert-Equal '0.1.0' (Get-SourceVersion $props)
    }
    Test 'reads the version of the real Directory.Build.props' {
        Assert-True (Test-ReleaseVersion (Get-SourceVersion (Join-Path $PSScriptRoot '..' '..' 'Directory.Build.props')))
    }
    Test 'refuses a missing <Version>' {
        $props = New-TempFile 'v2/Directory.Build.props' '<Project><PropertyGroup><Nullable>enable</Nullable></PropertyGroup></Project>'
        Assert-Throws { Get-SourceVersion $props } 'exactly one <Version>.*found 0'
    }
    Test 'refuses two <Version> elements' {
        $props = New-TempFile 'v3/Directory.Build.props' '<Project><PropertyGroup><Version>0.1.0</Version></PropertyGroup><PropertyGroup><Version>0.2.0</Version></PropertyGroup></Project>'
        Assert-Throws { Get-SourceVersion $props } 'found 2'
    }
    Test 'refuses a version that is not X.Y.Z' {
        foreach ($bad in '1.0', '1.0.0-beta', '01.0.0', 'v1.0.0', '1.0.0.0') {
            $props = New-TempFile "v4/$bad/Directory.Build.props" "<Project><PropertyGroup><Version>$bad</Version></PropertyGroup></Project>"
            Assert-Throws { Get-SourceVersion $props } 'not in the form X.Y.Z'
        }
    }
    Test 'tag <-> version' {
        Assert-Equal 'v0.1.0' (ConvertTo-ReleaseTag '0.1.0')
        Assert-Equal '1.12.3' (Get-VersionFromTag 'v1.12.3')
        foreach ($bad in '0.1.0', 'v0.1', 'v0.1.0-rc1', 'release-0.1.0', '', $null) { Assert-Equal $null (Get-VersionFromTag $bad) "tag '$bad'" }
    }
    Test '0.x is a pre-release, 1.x is not' {
        Assert-True (Test-PrereleaseVersion '0.1.0')
        Assert-True (Test-PrereleaseVersion '0.12.3')
        Assert-True (-not (Test-PrereleaseVersion '1.0.0'))
        Assert-True (-not (Test-PrereleaseVersion '10.0.0'))
    }

    Write-Host 'Runtimes'
    Test 'default runtimes per OS (win-arm64 off until T-057)' {
        Assert-Equal 'win-x64' (Resolve-ReleaseRuntimes -HostOs windows)
        Assert-Equal 'osx-arm64' (Resolve-ReleaseRuntimes -HostOs macos)
    }
    Test 'explicit runtimes are validated, normalized and de-duplicated' {
        Assert-Equal 'win-arm64|win-x64' (Resolve-ReleaseRuntimes -Requested 'win-arm64', 'WIN-X64', 'win-arm64' -HostOs windows)
        Assert-Equal 'win-x64|win-arm64' (Resolve-ReleaseRuntimes -Requested 'win-x64, win-arm64' -HostOs windows)
    }
    Test 'a runtime of the other OS is refused with a clear message' {
        Assert-Throws { Resolve-ReleaseRuntimes -Requested 'osx-arm64' -HostOs windows } "'osx-arm64' cannot be built on windows: Native AOT does not compile across operating systems"
        Assert-Throws { Resolve-ReleaseRuntimes -Requested 'win-x64' -HostOs macos } 'cannot be built on macos'
    }
    Test 'unknown runtimes are refused (osx-x64 is not released)' {
        Assert-Throws { Resolve-ReleaseRuntimes -Requested 'osx-x64' -HostOs macos } "Unknown runtime 'osx-x64'"
        Assert-Throws { Resolve-ReleaseRuntimes -Requested 'linux-x64' -HostOs windows } 'Unknown runtime'
    }
    Test 'win-arm64 must not be uploaded until T-057' {
        $config = Get-ReleaseRuntimeConfig
        Assert-True (-not $config['win-arm64'].Uploadable)
        Assert-True (-not $config['win-arm64'].Default)
        Assert-True ($config['win-x64'].Uploadable -and $config['osx-arm64'].Uploadable)
    }
    Test 'only the RID of this OS and architecture is smoke-tested' {
        Assert-True (Test-RuntimeRunnable -Runtime win-x64 -HostOs windows -HostArchitecture x64)
        Assert-True (-not (Test-RuntimeRunnable -Runtime win-arm64 -HostOs windows -HostArchitecture x64))
        Assert-True (Test-RuntimeRunnable -Runtime osx-arm64 -HostOs macos -HostArchitecture arm64)
        Assert-True (-not (Test-RuntimeRunnable -Runtime win-x64 -HostOs macos -HostArchitecture x64))
    }
    Test 'archive names' {
        Assert-Equal 'PrusaTrayDiagnostic-0.1.0-win-x64.zip' (Get-ArchiveFileName -Version 0.1.0 -Runtime win-x64)
        Assert-Equal 'PrusaTrayDiagnostic-0.1.0-osx-arm64.tar.gz' (Get-ArchiveFileName -Version 0.1.0 -Runtime osx-arm64)
        Assert-Equal 'SHA256SUMS-windows.txt' (Get-ChecksumFileName windows)
    }

    Write-Host 'CHANGELOG.md'
    $changelog = @'
# Changelog

## [Unreleased]

- not yet

## [0.2.0] - 2026-11-02

### Přidáno

- Druhá věc.

## [0.1.0] - 2026-10-21

### Přidáno

- První vydání.

[Unreleased]: https://example.invalid/compare/v0.2.0...HEAD
[0.2.0]: https://example.invalid/compare/v0.1.0...v0.2.0
'@
    Test 'extracts the section of a version' {
        $path = New-TempFile 'c1/CHANGELOG.md' $changelog
        Assert-Equal "### Přidáno`n`n- Druhá věc." (Get-ChangelogSection $path '0.2.0')
    }
    Test 'the last section excludes the link reference definitions' {
        $path = New-TempFile 'c2/CHANGELOG.md' $changelog
        Assert-Equal "### Přidáno`n`n- První vydání." (Get-ChangelogSection $path '0.1.0')
    }
    Test 'refuses a missing section' {
        $path = New-TempFile 'c3/CHANGELOG.md' $changelog
        Assert-Throws { Get-ChangelogSection $path '0.3.0' } "no section '## \[0.3.0\] - YYYY-MM-DD'"
        Assert-Throws { Get-ChangelogSection (Join-Path $tempRoot 'missing.md') '0.1.0' } 'Changelog not found'
    }
    Test 'refuses a section without a valid date' {
        foreach ($heading in '## [0.1.0]', '## [0.1.0] - unreleased', '## [0.1.0] - Unreleased', '## [0.1.0] - RRRR-MM-DD', '## [0.1.0] - 2026-13-01') {
            $path = New-TempFile "c4/$([guid]::NewGuid().ToString('N')).md" "$heading`n`n- Text.`n"
            Assert-Throws { Get-ChangelogSection $path '0.1.0' } 'has no release date'
        }
    }
    Test 'refuses an empty section (sub-headings and comments only)' {
        $path = New-TempFile 'c5/CHANGELOG.md' "## [0.1.0] - 2026-10-21`n`n<!-- todo -->`n### Přidáno`n`n## [0.0.9] - 2026-10-01`n`n- x`n"
        Assert-Throws { Get-ChangelogSection $path '0.1.0' } 'is empty'
    }
    Test 'refuses a duplicated section' {
        $path = New-TempFile 'c6/CHANGELOG.md' "## [0.1.0] - 2026-10-21`n- a`n## [0.1.0] - 2026-10-22`n- b`n"
        Assert-Throws { Get-ChangelogSection $path '0.1.0' } '2 sections'
    }
    Test 'the real CHANGELOG.md: a placeholder date is refused, a dated section yields the notes without link definitions' {
        $real = Join-Path $PSScriptRoot '..' '..' 'CHANGELOG.md'
        $version = Get-SourceVersion (Join-Path $PSScriptRoot '..' '..' 'Directory.Build.props')
        $content = [System.IO.File]::ReadAllText($real)
        $heading = "## [$version] - "
        Assert-True ($content.Contains($heading)) "CHANGELOG.md has no section for $version"
        if ($content -match "(?m)^## \[$([regex]::Escape($version))\] - unreleased\s*$") {
            Assert-Throws { Get-ChangelogSection $real $version } 'has no release date.*unreleased'
        }
        $dated = $content -replace "(?m)^## \[$([regex]::Escape($version))\] - .*$", "## [$version] - 2026-10-21"
        $notes = Get-ChangelogSection (New-TempFile 'c8/CHANGELOG.md' $dated) $version
        Assert-True ($notes.Length -gt 0 -and $notes -notmatch '(?m)^\[(Unreleased|\d+\.\d+\.\d+)\]:') 'notes must not contain link reference definitions'
    }
    Test 'the version dots are not regex wildcards' {
        $path = New-TempFile 'c7/CHANGELOG.md' "## [0x1x0] - 2026-10-21`n- a`n"
        Assert-Throws { Get-ChangelogSection $path '0.1.0' } 'no section'
    }

    Write-Host 'Git state checks (pure)'
    $head = 'a' * 40
    $goodState = [pscustomobject]@{
        Head = $head; StatusLines = @(); HeadTags = @('v0.1.0'); RemoteTagCommit = $head; OnMain = $true
        Remote = 'origin'; MainBranch = 'main'; RemoteErrors = @()
    }
    function With([hashtable] $Changes) { $s = $goodState.PSObject.Copy(); foreach ($k in $Changes.Keys) { $s.$k = $Changes[$k] }; $s }
    Test 'a clean, tagged, pushed commit on main is releasable' {
        Assert-Problems (Get-ReleaseGitProblems -State $goodState -Version '0.1.0')
    }
    Test 'dirty tree is refused (untracked files included)' {
        Assert-Problems (Get-ReleaseGitProblems -State (With @{ StatusLines = @('?? new.txt', ' M a.cs') }) -Version '0.1.0') 'not clean \(2 changed or untracked files: \?\? new.txt'
    }
    Test 'commit without the tag is refused' {
        Assert-Problems (Get-ReleaseGitProblems -State (With @{ HeadTags = @('some-other-tag') }) -Version '0.1.0') 'HEAD has no tag v0.1.0'
    }
    Test 'tag not matching the version is refused' {
        Assert-Problems (Get-ReleaseGitProblems -State (With @{ HeadTags = @('v0.2.0') }) -Version '0.1.0') 'HEAD is tagged v0.2.0 but the version .* is 0.1.0'
    }
    Test 'tag missing on origin is refused' {
        Assert-Problems (Get-ReleaseGitProblems -State (With @{ RemoteTagCommit = $null }) -Version '0.1.0') 'not on origin.*git push origin v0.1.0'
    }
    Test 'tag on origin pointing elsewhere is refused' {
        Assert-Problems (Get-ReleaseGitProblems -State (With @{ RemoteTagCommit = 'b' * 40 }) -Version '0.1.0') 'on origin points to b{40}, but HEAD is a{40}'
    }
    Test 'commit not on main is refused' {
        Assert-Problems (Get-ReleaseGitProblems -State (With @{ OnMain = $false }) -Version '0.1.0') 'not contained in origin/main'
    }
    Test 'unreachable origin is reported, not mistaken for a missing tag' {
        Assert-Problems (Get-ReleaseGitProblems -State (With @{ RemoteTagCommit = $null; OnMain = $null; RemoteErrors = @('git ls-remote origin failed: x') }) -Version '0.1.0') 'ls-remote origin failed'
    }

    Write-Host 'Git state checks (throw-away repositories)'
    $origin = Join-Path $tempRoot 'origin.git'
    $work = Join-Path $tempRoot 'work'
    $null = & git init --quiet --bare --initial-branch=main $origin 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'git init --bare failed' }
    $null = & git clone --quiet $origin $work 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'git clone failed' }
    $null = G $work checkout --quiet -b main
    [System.IO.File]::WriteAllText((Join-Path $work 'a.txt'), 'one')
    $null = G $work add a.txt
    $null = G $work commit --quiet -m one
    $null = G $work push --quiet origin main
    $first = G $work rev-parse HEAD | Select-Object -First 1
    function State { Get-ReleaseGitState -RepoRoot $work -Tag 'v0.1.0' }

    Test 'untagged commit on main: no tag, not on origin' {
        Assert-Problems (Get-ReleaseGitProblems -State (State) -Version '0.1.0') 'HEAD has no tag v0.1.0', 'not on origin'
    }
    Test 'local annotated tag not pushed: refused until pushed' {
        $null = G $work tag --annotate --message 'Release 0.1.0' v0.1.0
        Assert-Problems (Get-ReleaseGitProblems -State (State) -Version '0.1.0') 'not on origin'
    }
    Test 'pushed annotated tag on main: releasable (peeled commit compared)' {
        $null = G $work push --quiet origin v0.1.0
        $state = State
        Assert-Equal $first $state.RemoteTagCommit
        Assert-Equal $true $state.OnMain
        Assert-Problems (Get-ReleaseGitProblems -State $state -Version '0.1.0')
    }
    Test 'version bumped without a new tag: refused' {
        Assert-Problems (Get-ReleaseGitProblems -State (Get-ReleaseGitState -RepoRoot $work -Tag 'v0.2.0') -Version '0.2.0') 'HEAD is tagged v0.1.0 but the version .* is 0.2.0', 'v0.2.0 is not on origin'
    }
    Test 'untracked file: refused' {
        [System.IO.File]::WriteAllText((Join-Path $work 'untracked.txt'), 'x')
        try { Assert-Problems (Get-ReleaseGitProblems -State (State) -Version '0.1.0') 'not clean \(1 .*untracked.txt' }
        finally { Remove-Item -LiteralPath (Join-Path $work 'untracked.txt') -Force }
    }
    Test 'modified tracked file: refused' {
        [System.IO.File]::WriteAllText((Join-Path $work 'a.txt'), 'changed')
        try { Assert-Problems (Get-ReleaseGitProblems -State (State) -Version '0.1.0') 'not clean' }
        finally { $null = G $work checkout --quiet -- a.txt }
    }
    Test 'tag moved on origin: refused' {
        $null = G $work checkout --quiet -b other
        [System.IO.File]::WriteAllText((Join-Path $work 'b.txt'), 'two')
        $null = G $work add b.txt
        $null = G $work commit --quiet -m two
        $second = G $work rev-parse HEAD | Select-Object -First 1
        $null = G $work push --quiet --force origin "$($second):refs/tags/v0.1.0"
        $null = G $work checkout --quiet main
        Assert-Problems (Get-ReleaseGitProblems -State (State) -Version '0.1.0') "points to $second, but HEAD is $first"
        $null = G $work push --quiet --force origin "refs/tags/v0.1.0:refs/tags/v0.1.0"
        Assert-Problems (Get-ReleaseGitProblems -State (State) -Version '0.1.0')
    }
    Test 'tagged and pushed commit outside main: refused' {
        $null = G $work checkout --quiet other
        $null = G $work tag v0.3.0
        $null = G $work push --quiet origin v0.3.0
        try {
            $state = Get-ReleaseGitState -RepoRoot $work -Tag 'v0.3.0'
            Assert-Equal $false $state.OnMain
            Assert-Problems (Get-ReleaseGitProblems -State $state -Version '0.3.0') 'not contained in origin/main'
        }
        finally { $null = G $work checkout --quiet main }
    }
    Test 'origin/main with commits not fetched yet: refused with a fetch hint, nothing fetched' {
        $other = Join-Path $tempRoot 'other'
        $null = & git clone --quiet $origin $other 2>&1
        if ($LASTEXITCODE -ne 0) { throw 'git clone failed' }
        [System.IO.File]::WriteAllText((Join-Path $other 'c.txt'), 'three')
        $null = G $other add c.txt
        $null = G $other commit --quiet -m three
        $null = G $other push --quiet origin main
        $newMain = G $other rev-parse HEAD | Select-Object -First 1
        $refsBefore = (G $work show-ref) -join "`n"
        Assert-Problems (Get-ReleaseGitProblems -State (State) -Version '0.1.0') "origin/main \($newMain\) is not in the local repository. Run: git fetch origin"
        Assert-Equal $refsBefore ((G $work show-ref) -join "`n") 'the check must not change local refs'
        $null = G $work fetch --quiet origin
        Assert-Problems (Get-ReleaseGitProblems -State (State) -Version '0.1.0')
    }
    Test 'unreachable origin: reported as a remote error' {
        $null = G $work remote set-url origin (Join-Path $tempRoot 'does-not-exist.git')
        try {
            $state = State
            Assert-Problems (Get-ReleaseGitProblems -State $state -Version '0.1.0') 'ls-remote origin failed'
        }
        finally { $null = G $work remote set-url origin $origin }
    }

    Write-Host 'Checksums and archives'
    $publish = Join-Path $tempRoot 'publish'
    $null = New-Item -ItemType Directory -Path (Join-Path $publish 'sub')
    [System.IO.File]::WriteAllText((Join-Path $publish 'app.exe'), 'binary')
    [System.IO.File]::WriteAllText((Join-Path $publish 'sub' 'lib.dll'), 'library')
    $dist = Join-Path $tempRoot 'dist'
    $null = New-Item -ItemType Directory -Path $dist
    Test 'zip contains the publish folder content without the folder itself' {
        $zip = New-ReleaseArchive -SourceDirectory $publish -Destination (Join-Path $dist 'a-win-x64.zip')
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
        try { Assert-Equal 'app.exe|sub/lib.dll' (@($archive.Entries | Where-Object { $_.Name } | ForEach-Object { $_.FullName -replace '\\', '/' } | Sort-Object)) }
        finally { $archive.Dispose() }
        Assert-Throws { New-ReleaseArchive -SourceDirectory $publish -Destination $zip } 'already exists'
    }
    Test 'tar.gz contains the publish folder content without the folder itself' {
        $tgz = New-ReleaseArchive -SourceDirectory $publish -Destination (Join-Path $dist 'a-osx-arm64.tar.gz')
        $entries = @(& (Get-TarCommand) -tzf $tgz | Where-Object { $_ -and -not $_.EndsWith('/') } | ForEach-Object { $_ -replace '^\./', '' } | Sort-Object)
        Assert-Equal 'app.exe|sub/lib.dll' $entries
    }
    Test 'SHA256SUMS in sha256sum format matches Get-FileHash and detects tampering' {
        $sums = Write-Sha256Sums -Directory $dist -FileName 'SHA256SUMS-windows.txt' -Files 'a-win-x64.zip', 'a-osx-arm64.tar.gz'
        $lines = [System.IO.File]::ReadAllText($sums)
        Assert-True (-not $lines.Contains("`r")) 'checksum file must use LF'
        $entries = Read-Sha256Sums $sums
        Assert-Equal 'a-osx-arm64.tar.gz|a-win-x64.zip' @($entries.Keys)
        Assert-Equal (Get-FileHash -LiteralPath (Join-Path $dist 'a-win-x64.zip') -Algorithm SHA256).Hash.ToLowerInvariant() $entries['a-win-x64.zip']
        Assert-Problems (Get-Sha256SumsProblems $sums)
        [System.IO.File]::AppendAllText((Join-Path $dist 'a-win-x64.zip'), 'x')
        Assert-Problems (Get-Sha256SumsProblems $sums) 'a-win-x64.zip does not match'
        Remove-Item -LiteralPath (Join-Path $dist 'a-osx-arm64.tar.gz')
        Assert-Problems (Get-Sha256SumsProblems $sums) 'a-win-x64.zip does not match', 'a-osx-arm64.tar.gz is listed .* but missing'
    }
    Test 'malformed checksum files are refused' {
        Assert-Throws { Read-Sha256Sums (New-TempFile 's1.txt' "abc  file.zip`n") } 'Malformed'
        Assert-Throws { Read-Sha256Sums (New-TempFile 's2.txt' "$('0' * 64)  ../file.zip`n") } 'Malformed'
        Assert-Throws { Read-Sha256Sums (New-TempFile 's3.txt' "$('0' * 64)  f.zip`n$('1' * 64)  f.zip`n") } 'Duplicate'
    }

    Write-Host 'Build marker'
    $info = [pscustomobject]@{
        Version = '0.1.0'; Commit = $head; Dirty = $false; Runtimes = @('win-x64'); ChecksumFile = 'SHA256SUMS-windows.txt'
        Files = @([pscustomobject]@{ Name = 'P-0.1.0-win-x64.zip'; Runtime = 'win-x64'; Sha256 = '0' * 64 })
    }
    $distNames = @('P-0.1.0-win-x64.zip', 'SHA256SUMS-windows.txt')
    function WithInfo([hashtable] $Changes) { $s = $info.PSObject.Copy(); foreach ($k in $Changes.Keys) { $s.$k = $Changes[$k] }; $s }
    Test 'a build of HEAD from a clean tree can be uploaded' {
        Assert-Problems (Get-BuildInfoProblems -BuildInfo $info -Version '0.1.0' -Head $head -DistFiles $distNames)
    }
    Test 'missing build output is refused' {
        Assert-Problems (Get-BuildInfoProblems -BuildInfo $null -Version '0.1.0' -Head $head -DistFiles @()) 'No build output'
    }
    Test 'a build of another commit, version or a dirty tree is refused' {
        Assert-Problems (Get-BuildInfoProblems -BuildInfo (WithInfo @{ Commit = 'c' * 40 }) -Version '0.1.0' -Head $head -DistFiles $distNames) 'from commit c{40}, HEAD is a{40}'
        Assert-Problems (Get-BuildInfoProblems -BuildInfo (WithInfo @{ Version = '0.0.9' }) -Version '0.1.0' -Head $head -DistFiles $distNames) 'version 0.0.9'
        Assert-Problems (Get-BuildInfoProblems -BuildInfo (WithInfo @{ Dirty = $true }) -Version '0.1.0' -Head $head -DistFiles $distNames) 'uncommitted or untracked'
    }
    Test 'a build containing win-arm64 is refused until T-057' {
        Assert-Problems (Get-BuildInfoProblems -BuildInfo (WithInfo @{ Runtimes = @('win-x64', 'win-arm64') }) -Version '0.1.0' -Head $head -DistFiles $distNames) 'contains win-arm64.*T-057'
    }
    Test 'files added to or missing in dist are refused' {
        Assert-Problems (Get-BuildInfoProblems -BuildInfo $info -Version '0.1.0' -Head $head -DistFiles ($distNames + 'old.zip')) 'not produced by the build: old.zip'
        Assert-Problems (Get-BuildInfoProblems -BuildInfo $info -Version '0.1.0' -Head $head -DistFiles @('SHA256SUMS-windows.txt')) 'missing in dist: P-0.1.0-win-x64.zip'
    }

    Write-Host 'Upload plan and publish checks'
    $local = @(
        [pscustomobject]@{ Name = 'new.zip'; Sha256 = '1' * 64 },
        [pscustomobject]@{ Name = 'same.zip'; Sha256 = '2' * 64 },
        [pscustomobject]@{ Name = 'changed.zip'; Sha256 = '3' * 64 },
        [pscustomobject]@{ Name = 'nodigest.zip'; Sha256 = '4' * 64 }
    )
    $remote = @(
        [pscustomobject]@{ name = 'same.zip'; digest = "sha256:$('2' * 64)" },
        [pscustomobject]@{ name = 'changed.zip'; digest = "sha256:$('9' * 64)" },
        [pscustomobject]@{ name = 'nodigest.zip'; digest = $null },
        [pscustomobject]@{ name = 'SHA256SUMS-macos.txt'; digest = "sha256:$('5' * 64)" }
    )
    Test 'upload plan: new files upload, identical skip, different conflict' {
        $plan = @(Get-UploadPlan -LocalFiles $local -RemoteAssets $remote)
        Assert-Equal 'new.zip=Upload|same.zip=Skip|changed.zip=Conflict|nodigest.zip=Conflict' @($plan | ForEach-Object { "$($_.Name)=$($_.Action)" })
    }
    Test 'upload plan with -Force replaces different files only' {
        $plan = @(Get-UploadPlan -LocalFiles $local -RemoteAssets $remote -Force)
        Assert-Equal 'new.zip=Upload|same.zip=Skip|changed.zip=Replace|nodigest.zip=Replace' @($plan | ForEach-Object { "$($_.Name)=$($_.Action)" })
    }
    Test 'upload plan for a new release uploads everything' {
        Assert-Equal 'Upload|Upload|Upload|Upload' @(Get-UploadPlan -LocalFiles $local -RemoteAssets @() | ForEach-Object Action)
    }

    $winSums = [ordered]@{ 'P-0.1.0-win-x64.zip' = '1' * 64 }
    $macSums = [ordered]@{ 'P-0.1.0-osx-arm64.tar.gz' = '2' * 64 }
    $completeAssets = @(
        [pscustomobject]@{ name = 'P-0.1.0-win-x64.zip'; digest = "sha256:$('1' * 64)" },
        [pscustomobject]@{ name = 'SHA256SUMS-windows.txt'; digest = "sha256:$('7' * 64)" },
        [pscustomobject]@{ name = 'P-0.1.0-osx-arm64.tar.gz'; digest = "sha256:$('2' * 64)" },
        [pscustomobject]@{ name = 'SHA256SUMS-macos.txt'; digest = "sha256:$('8' * 64)" }
    )
    $bothSums = [ordered]@{ 'SHA256SUMS-windows.txt' = $winSums; 'SHA256SUMS-macos.txt' = $macSums }
    function Release([object[]] $Assets, [bool] $Draft = $true) { [pscustomobject]@{ isDraft = $Draft; assets = $Assets; url = 'https://example.invalid' } }
    Test 'a complete draft can be published' {
        Assert-Problems (Get-PublishProblems -Release (Release $completeAssets) -Tag v0.1.0 -ExpectedOs windows, macos -Checksums $bothSums)
    }
    Test 'publishing refuses a missing or already published release' {
        Assert-Problems (Get-PublishProblems -Release $null -Tag v0.1.0 -ExpectedOs windows -Checksums @{}) 'no release for v0.1.0'
        Assert-Problems (Get-PublishProblems -Release (Release $completeAssets $false) -Tag v0.1.0 -ExpectedOs windows, macos -Checksums $bothSums) 'already published'
    }
    Test 'publishing refuses a draft without the macOS files unless incomplete is allowed' {
        $windowsOnly = @($completeAssets | Select-Object -First 2)
        $sums = [ordered]@{ 'SHA256SUMS-windows.txt' = $winSums }
        Assert-Problems (Get-PublishProblems -Release (Release $windowsOnly) -Tag v0.1.0 -ExpectedOs windows, macos -Checksums $sums) 'no SHA256SUMS-macos.txt'
        Assert-Problems (Get-PublishProblems -Release (Release $windowsOnly) -Tag v0.1.0 -ExpectedOs @() -Checksums $sums)
    }
    Test 'publishing refuses missing, different, unlisted and win-arm64 files' {
        $assets = @($completeAssets | Where-Object name -NE 'P-0.1.0-osx-arm64.tar.gz') + @(
            [pscustomobject]@{ name = 'stray.txt'; digest = "sha256:$('3' * 64)" },
            [pscustomobject]@{ name = 'P-0.1.0-win-arm64.zip'; digest = "sha256:$('4' * 64)" }
        )
        $assets[0] = [pscustomobject]@{ name = 'P-0.1.0-win-x64.zip'; digest = "sha256:$('6' * 64)" }
        Assert-Problems (Get-PublishProblems -Release (Release $assets) -Tag v0.1.0 -ExpectedOs windows, macos -Checksums $bothSums) `
            'P-0.1.0-osx-arm64.tar.gz is listed .* but not uploaded', 'Uploaded P-0.1.0-win-x64.zip does not match', 'stray.txt is uploaded but not listed', 'P-0.1.0-win-arm64.zip is uploaded but not listed', 'P-0.1.0-win-arm64.zip belongs to win-arm64'
    }

    Write-Host 'GitHub repository of origin'
    Test 'parses GitHub remote URLs' {
        Assert-Equal 'pfoltynek/PrusaTrayDiagnostic' (Get-GitHubRepository 'https://github.com/pfoltynek/PrusaTrayDiagnostic.git')
        Assert-Equal 'owner/repo' (Get-GitHubRepository 'git@github.com:owner/repo.git')
        Assert-Equal 'owner/repo' (Get-GitHubRepository 'https://github.com/owner/repo')
        Assert-Equal $null (Get-GitHubRepository 'https://example.com/owner/repo.git')
    }
}
finally {
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $tempRoot) { Write-Host "WARNING: could not delete $tempRoot" -ForegroundColor Yellow }
}

Write-Host ''
if ($script:failures.Count -gt 0) {
    Write-Host "Release self-check FAILED: $($script:failures.Count) failed, $script:passed passed." -ForegroundColor Red
    exit 1
}
Write-Host "Release self-check PASSED: $script:passed checks." -ForegroundColor Green
exit 0
