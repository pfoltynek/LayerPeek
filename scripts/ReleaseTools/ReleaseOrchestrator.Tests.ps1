<#
.SYNOPSIS
    Pester 5+ tests of the release orchestrator (scripts/release.ps1 and its functions in ReleaseTools.psm1).

.DESCRIPTION
    Covers the version check, the Directory.Build.props and CHANGELOG.md transformation (pure functions), the plan with
    its resume detection and refusals (pure, from crafted facts), the Gitflow steps against throw-away repositories (a
    bare "origin" reached through a github.com URL rewritten with url.<base>.insteadOf, an isolated git configuration)
    and release.ps1 itself in -DryRun mode and up to its first confirmation. No network: gh is a fake on PATH that
    only answers "gh auth status" and "release not found" and logs every call; the real gh is never called.

    Temporary files live in one folder under $env:LAYERPEEK_PESTER_TESTS_TEMP (set by the xUnit wrapper
    tests/LayerPeek.Scripts.Tests, which also deletes it after a crash) or under the system temp folder.
    AfterAll deletes the folder and restores the environment.

.EXAMPLE
    Invoke-Pester scripts/ReleaseTools/ReleaseOrchestrator.Tests.ps1 -Output Detailed
#>
#Requires -Version 7.2

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1') -Force

    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).ProviderPath
    $base = if ($env:LAYERPEEK_PESTER_TESTS_TEMP) { $env:LAYERPEEK_PESTER_TESTS_TEMP } else { [System.IO.Path]::GetTempPath() }
    # Short name: git repositories live below it (Windows path length limit).
    $script:TempRoot = Join-Path $base "layerpeek-ro-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    $null = New-Item -ItemType Directory -Path $script:TempRoot

    # A fake gh first on PATH (the real gh is never called). It logs every call. "auth status" succeeds; "release view"
    # reports "release not found" unless LAYERPEEK_FAKE_GH_STATE is set and a draft was created there by
    # "release create" (then it returns that draft as JSON, assets with sha256 digests and state 'uploaded'); "release
    # upload" adds files, "release download" copies the uploaded files matching --pattern into --dir, "release edit"
    # sets --draft / --prerelease (T-113: publish, then add the other OS). Uploaded files are kept next to the state
    # file (<state>.files). Everything else fails. The shim on PATH (gh.cmd / gh) logs the call and answers the read-only cases itself; only
    # with LAYERPEEK_FAKE_GH_STATE it starts the pwsh implementation (outside PATH, so that "& gh" starts a process with
    # a real stderr, as the real gh), which keeps the number of pwsh starts low.
    $fakeGhDir = Join-Path $script:TempRoot 'gh'
    $fakeGhImpl = Join-Path $script:TempRoot 'gh-impl'
    $null = New-Item -ItemType Directory -Path $fakeGhDir, $fakeGhImpl
    $script:FakeGhLog = Join-Path $script:TempRoot 'gh-calls.log'
    $impl = Join-Path $fakeGhImpl 'gh.ps1'
    [System.IO.File]::WriteAllText($impl, @'
$a = @($args)
function Opt([string] $Name) { $i = [array]::IndexOf($a, $Name); if ($i -ge 0) { $a[$i + 1] } else { $null } }
if ($a[0] -eq 'auth') { exit 0 }
if ($a[0] -ne 'release') { [Console]::Error.WriteLine("fake gh: unsupported $($a -join ' ')"); exit 99 }
$tag = $a[2]; $repo = Opt '--repo'
$state = if ($env:LAYERPEEK_FAKE_GH_STATE) { Join-Path $env:LAYERPEEK_FAKE_GH_STATE "$($repo -replace '/', '_')_$tag.json" } else { $null }
function Files { $skip = @('--repo', '--title', '--notes-file', '--pattern', '--dir'); $out = @(); for ($i = 3; $i -lt $a.Count; $i++) { if ($skip -contains $a[$i]) { $i++; continue }; if ($a[$i] -like '--*') { continue }; $out += $a[$i] }; $out }
function Asset([string] $Path) { [ordered]@{ name = (Split-Path -Leaf $Path); state = 'uploaded'; digest = "sha256:$((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant())" } }
function Keep([string] $Path) { $dir = "$state.files"; if (-not (Test-Path -LiteralPath $dir)) { $null = New-Item -ItemType Directory -Path $dir }; Copy-Item -LiteralPath $Path -Destination $dir -Force }
switch ($a[1]) {
    'view' {
        if ($state -and (Test-Path -LiteralPath $state)) { Get-Content -LiteralPath $state -Raw; exit 0 }
        [Console]::Error.WriteLine('release not found'); exit 1
    }
    'create' {
        if (-not $state -or (Test-Path -LiteralPath $state)) { [Console]::Error.WriteLine('fake gh: create refused'); exit 98 }
        $release = [ordered]@{ tagName = $tag; name = (Opt '--title'); isDraft = $true; isPrerelease = ($a -contains '--prerelease')
            url = "https://example.invalid/$repo/releases/tag/$tag"; body = [System.IO.File]::ReadAllText((Opt '--notes-file'))
            assets = @(Files | ForEach-Object { Keep $_; Asset $_ }) }
        $release | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $state; exit 0
    }
    'upload' {
        if (-not $state -or -not (Test-Path -LiteralPath $state)) { [Console]::Error.WriteLine('fake gh: no release'); exit 97 }
        $release = Get-Content -LiteralPath $state -Raw | ConvertFrom-Json -AsHashtable
        foreach ($path in Files) { Keep $path; $release.assets = @($release.assets | Where-Object { $_.name -cne (Split-Path -Leaf $path) }) + (Asset $path) }
        $release | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $state; exit 0
    }
    'download' {
        if (-not $state -or -not (Test-Path -LiteralPath $state)) { [Console]::Error.WriteLine('release not found'); exit 1 }
        $dir = Opt '--dir'; $pattern = Opt '--pattern'
        foreach ($file in @(Get-ChildItem -LiteralPath "$state.files" -File | Where-Object { $_.Name -like $pattern })) { Copy-Item -LiteralPath $file.FullName -Destination $dir }
        exit 0
    }
    'edit' {
        if (-not $state -or -not (Test-Path -LiteralPath $state)) { [Console]::Error.WriteLine('release not found'); exit 1 }
        $release = Get-Content -LiteralPath $state -Raw | ConvertFrom-Json -AsHashtable
        foreach ($arg in $a) {
            if ($arg -match '^--draft=(true|false)$') { $release.isDraft = $Matches[1] -eq 'true' }
            if ($arg -match '^--prerelease=(true|false)$') { $release.isPrerelease = $Matches[1] -eq 'true' }
        }
        $release | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $state; exit 0
    }
    default { [Console]::Error.WriteLine("fake gh: unsupported $($a -join ' ')"); exit 99 }
}
'@)
    # Quoted command line of the running PowerShell (an installed pwsh, or dotnet + pwsh.dll of the dotnet tool).
    $pwshCommand = (Get-PwshCommand | ForEach-Object { "`"$_`"" }) -join ' '
    if ($IsWindows) {
        $shim = @(
            '@echo off'
            'echo %*>>"%LAYERPEEK_FAKE_GH_LOG%"'
            'if "%~1"=="auth" exit /b 0'
            'if defined LAYERPEEK_FAKE_GH_STATE goto impl'
            'if "%~1"=="release" if "%~2"=="view" ('
            '  echo release not found 1>&2'
            '  exit /b 1'
            ')'
            'echo fake gh: unsupported %* 1>&2'
            'exit /b 99'
            ':impl'
            "$pwshCommand -NoProfile -NonInteractive -File `"$impl`" %*"
            'exit /b %ERRORLEVEL%'
        ) -join "`r`n"
        [System.IO.File]::WriteAllText((Join-Path $fakeGhDir 'gh.cmd'), $shim + "`r`n")
    }
    else {
        $path = Join-Path $fakeGhDir 'gh'
        $shim = @(
            '#!/bin/sh'
            'echo "$*" >> "$LAYERPEEK_FAKE_GH_LOG"'
            'if [ "$1" = auth ]; then exit 0; fi'
            'if [ -z "$LAYERPEEK_FAKE_GH_STATE" ]; then'
            '  if [ "$1" = release ] && [ "$2" = view ]; then echo "release not found" >&2; exit 1; fi'
            '  echo "fake gh: unsupported $*" >&2; exit 99'
            'fi'
            "exec $pwshCommand -NoProfile -NonInteractive -File `"$impl`" `"`$@`""
        ) -join "`n"
        [System.IO.File]::WriteAllText($path, $shim + "`n")
        & chmod +x $path
    }

    # Git environment isolated from the user's configuration (hooks, signing, default branch); restored in AfterAll.
    # LAYERPEEK_RELEASE_TEST_SKIP_TOOLCHAIN (test-only, honoured only together with LAYERPEEK_PESTER_TESTS_TEMP): the
    # release.ps1 tests do not depend on MSVC / Xcode / vpk of this machine.
    $script:GitEnvironment = @{
        GIT_CONFIG_NOSYSTEM                  = '1'
        GIT_CONFIG_GLOBAL                    = (Join-Path $script:TempRoot 'gitconfig')
        GIT_AUTHOR_NAME                      = 'Release Tests'
        GIT_AUTHOR_EMAIL                     = 'release-tests@example.invalid'
        GIT_COMMITTER_NAME                   = 'Release Tests'
        GIT_COMMITTER_EMAIL                  = 'release-tests@example.invalid'
        GIT_TERMINAL_PROMPT                  = '0'
        LAYERPEEK_FAKE_GH_LOG                = $script:FakeGhLog
        LAYERPEEK_FAKE_GH_STATE              = $null
        # The unique test folder, not $base (%TEMP%): an aborted run must not leave the toolchain skip applying to
        # other repositories in %TEMP%, such as the trial clone from docs/release.md.
        LAYERPEEK_PESTER_TESTS_TEMP          = $script:TempRoot
        LAYERPEEK_RELEASE_TEST_SKIP_TOOLCHAIN = '1'
        PATH                                 = "$fakeGhDir$([System.IO.Path]::PathSeparator)$env:PATH"
    }
    $script:SavedEnvironment = @{}
    foreach ($name in $script:GitEnvironment.Keys) {
        $script:SavedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $script:GitEnvironment[$name])
    }
    [System.IO.File]::WriteAllText($script:GitEnvironment.GIT_CONFIG_GLOBAL, '')

    function Use-GhLog([string] $Name) {
        # One log of gh calls per Describe block, so that each block checks only its own calls.
        $script:FakeGhLog = Join-Path $script:TempRoot "gh-$Name.log"
        $env:LAYERPEEK_FAKE_GH_LOG = $script:FakeGhLog
        $script:FakeGhLog
    }
    function Get-GhCall { if (Test-Path -LiteralPath $script:FakeGhLog) { @(Get-Content -LiteralPath $script:FakeGhLog | Where-Object { $_ }) } else { @() } }

    function G {
        # git in a test repository; throws on failure.
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

    # The toolchain of this machine (MSVC / Xcode) is not part of what the git tests check.
    $script:ToolchainPattern = 'MSVC|Visual Studio Build Tools|Xcode'
    function Select-NonToolchain([string[]] $Problems) { @($Problems | Where-Object { $_ -and $_ -notmatch $script:ToolchainPattern }) }

    function States($Plan) { @($Plan.Steps | ForEach-Object { "$($_.Id)=$(if ($_.Done) { 'done' } else { 'todo' })" }) }

    $script:SeedChangelog = @'
# Changelog

## [Unreleased]

### Added

- Second thing.

## [0.1.0] - 2026-10-21

### Added

- First release.

[Unreleased]: https://github.com/test-owner/LayerPeek/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/test-owner/LayerPeek/releases/tag/v0.1.0

'@ -replace '\r\n', "`n"
    $script:SeedProps = "<Project>`n  <!-- Copyright © Petr Foltýnek -->`n  <PropertyGroup>`n    <Version>0.1.0</Version>`n  </PropertyGroup>`n</Project>`n"

    function New-ReleaseTestRepository {
        <#
        A bare origin with main (v0.1.0, annotated) and develop (one more changelog entry), reachable as
        https://github.com/test-owner/<Name>.git, and a clone on develop. -WithScripts adds the release scripts, so that
        scripts/release.ps1 of the clone works on the clone.
        #>
        param([Parameter(Mandatory)] [string] $Name, [switch] $WithScripts, [string] $BuildScript = "Write-Host 'simulated build failure'`nexit 1`n")
        $root = Join-Path $script:TempRoot $Name
        $origin = Join-Path $root 'o.git'
        $seed = Join-Path $root 's'
        $work = Join-Path $root 'w'
        $url = "https://github.com/test-owner/$Name.git"
        $null = & git init --quiet --bare --initial-branch=main $origin 2>&1
        if ($LASTEXITCODE -ne 0) { throw 'git init --bare failed' }
        $null = & git config --global "url.$($origin -replace '\\', '/').insteadOf" $url 2>&1
        $null = & git init --quiet --initial-branch=main $seed 2>&1
        [System.IO.File]::WriteAllText((Join-Path $seed '.gitattributes'), "* text=auto eol=lf`n")
        [System.IO.File]::WriteAllText((Join-Path $seed '.gitignore'), "artifacts/`n")
        [System.IO.File]::WriteAllText((Join-Path $seed 'Directory.Build.props'), $script:SeedProps, [System.Text.UTF8Encoding]::new($false))
        $released = $script:SeedChangelog -replace '(?s)## \[Unreleased\]\n\n### Added\n\n- Second thing.\n\n', "## [Unreleased]`n`n"
        [System.IO.File]::WriteAllText((Join-Path $seed 'CHANGELOG.md'), $released)
        # The Velopack version check of the build needs both files (as in the real repository).
        Copy-Item -LiteralPath (Join-Path $script:RepoRoot 'Directory.Packages.props') -Destination $seed
        $null = New-Item -ItemType Directory -Path (Join-Path $seed '.config')
        Copy-Item -LiteralPath (Join-Path $script:RepoRoot '.config' 'dotnet-tools.json') -Destination (Join-Path $seed '.config')
        if ($WithScripts) {
            $null = New-Item -ItemType Directory -Path (Join-Path $seed 'scripts' 'ReleaseTools')
            Copy-Item -LiteralPath (Join-Path $script:RepoRoot 'scripts' 'release.ps1') -Destination (Join-Path $seed 'scripts')
            Copy-Item -LiteralPath (Join-Path $script:RepoRoot 'scripts' 'ReleaseTools' 'ReleaseTools.psm1') -Destination (Join-Path $seed 'scripts' 'ReleaseTools')
            # The real upload script (it talks to the fake gh); a stand-in build (no dotnet, no AOT): by default one
            # that fails, as a real build failing after the tag was pushed.
            Copy-Item -LiteralPath (Join-Path $script:RepoRoot 'scripts' 'release-upload.ps1') -Destination (Join-Path $seed 'scripts')
            Copy-Item -LiteralPath (Join-Path $script:RepoRoot 'scripts' 'release-publish.ps1') -Destination (Join-Path $seed 'scripts')
            [System.IO.File]::WriteAllText((Join-Path $seed 'scripts' 'release-build.ps1'), $BuildScript)
        }
        $null = G $seed add --all
        $null = G $seed commit --quiet --message 'Release 0.1.0'
        $null = G $seed tag --annotate --message 'Release 0.1.0' v0.1.0
        $null = G $seed switch --quiet --create develop
        [System.IO.File]::WriteAllText((Join-Path $seed 'CHANGELOG.md'), $script:SeedChangelog)
        $null = G $seed commit --quiet --all --message 'Second thing'
        $null = G $seed push --quiet $origin main develop v0.1.0
        $null = & git clone --quiet --branch develop $url $work 2>&1
        if ($LASTEXITCODE -ne 0) { throw 'git clone failed' }
        [pscustomobject]@{ Origin = $origin; Work = $work; Url = $url; Repository = "test-owner/$Name" }
    }

    function Get-AllRefs([string] $Repo) { (G $Repo for-each-ref --format='%(refname) %(objectname)') -join "`n" }
}

AfterAll {
    foreach ($name in $script:SavedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $script:SavedEnvironment[$name]) }
    if ($script:TempRoot -and (Test-Path -LiteralPath $script:TempRoot)) { Remove-Item -LiteralPath $script:TempRoot -Recurse -Force }
}

Describe 'Version to release (pure)' {
    It 'accepts the first version without any tag' {
        Assert-Problems (Get-ReleaseVersionProblems -Version '0.1.0' -ExistingTags @() -SourceVersion '0.1.0')
    }
    It 'accepts a version greater than every release tag (numeric, 0.10.0 > 0.9.0)' {
        Assert-Problems (Get-ReleaseVersionProblems -Version '0.10.0' -ExistingTags 'v0.9.0', 'v0.2.1', 'not-a-version' -SourceVersion '0.9.0')
    }
    It 'refuses a version not greater than the latest tag (<_>)' -ForEach '0.2.0', '0.1.9' {
        Assert-Problems (Get-ReleaseVersionProblems -Version $_ -ExistingTags 'v0.1.0', 'v0.2.0', 'v0.2.1') 'not greater than the latest release tag v0.2.1'
    }
    It 'ignores its own tag, so that a resumed release passes' {
        Assert-Problems (Get-ReleaseVersionProblems -Version '0.2.0' -ExistingTags 'v0.1.0', 'v0.2.0')
    }
    It 'refuses a version lower than Version on develop' {
        Assert-Problems (Get-ReleaseVersionProblems -Version '0.1.0' -SourceVersion '0.2.0') 'lower than <Version> 0.2.0 on develop'
    }
    It 'refuses the malformed version <_>' -ForEach '1.0', 'v1.0.0', '1.0.0-rc1', '01.0.0', '' {
        Assert-Problems (Get-ReleaseVersionProblems -Version $_) 'not a version in the form X.Y.Z'
    }
}

Describe 'Test-only toolchain switch' {
    It 'skips the toolchain check only for a repository inside LAYERPEEK_PESTER_TESTS_TEMP' {
        $inside = Join-Path $env:LAYERPEEK_PESTER_TESTS_TEMP 'some-repo'
        Test-ReleaseTestToolchainSkip -RepoRoot $inside | Should -BeTrue
        # A real repository (this one) is never affected, even with both variables left behind by an aborted run.
        Test-ReleaseTestToolchainSkip -RepoRoot $script:RepoRoot | Should -BeFalse
        Test-ReleaseTestToolchainSkip -RepoRoot $env:LAYERPEEK_PESTER_TESTS_TEMP | Should -BeFalse
        Test-ReleaseTestToolchainSkip -RepoRoot "$($env:LAYERPEEK_PESTER_TESTS_TEMP.TrimEnd('\', '/'))-other" | Should -BeFalse
    }
    It 'needs both variables' {
        $saved = $env:LAYERPEEK_RELEASE_TEST_SKIP_TOOLCHAIN
        try {
            $env:LAYERPEEK_RELEASE_TEST_SKIP_TOOLCHAIN = $null
            Test-ReleaseTestToolchainSkip -RepoRoot (Join-Path $env:LAYERPEEK_PESTER_TESTS_TEMP 'some-repo') | Should -BeFalse
        }
        finally { $env:LAYERPEEK_RELEASE_TEST_SKIP_TOOLCHAIN = $saved }
    }
}

Describe 'Directory.Build.props version (pure)' {
    It 'sets the single Version and keeps everything else' {
        $text = Set-SourceVersionText -Text $script:SeedProps -Version '0.2.0'
        $text | Should -BeExactly ($script:SeedProps -replace '0\.1\.0', '0.2.0')
        Get-VersionFromPropsText $text | Should -BeExactly '0.2.0'
    }
    It 'refuses a props text without or with two Version elements' {
        Assert-ThrowsMatching { Set-SourceVersionText -Text '<Project />' -Version '0.2.0' } 'found 0'
        Assert-ThrowsMatching { Set-SourceVersionText -Text '<Version>1.0.0</Version><Version>1.0.0</Version>' -Version '0.2.0' } 'found 2'
        Get-VersionFromPropsText '<Project />' | Should -BeNullOrEmpty
    }
    It 'refuses a malformed target version' {
        Assert-ThrowsMatching { Set-SourceVersionText -Text $script:SeedProps -Version '0.2' } 'not a version'
    }
}

Describe 'CHANGELOG.md release date (pure)' {
    BeforeAll {
        $script:Placeholder = @'
# Changelog

## [Unreleased]

## [0.2.0] - unreleased

### Added

- Second thing.

## [0.1.0] - 2026-10-21

- First release.

[Unreleased]: https://github.com/o/r/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/o/r/releases/tag/v0.1.0
'@ -replace '\r\n', "`n"
    }
    It 'dates the placeholder section and leaves the empty Unreleased section on top' {
        $result = ConvertTo-ReleaseChangelog -Text $script:Placeholder -Version '0.2.0' -Date '2026-11-02'
        $result.Change | Should -BeExactly 'Dated'
        $result.OldHeading | Should -BeExactly '## [0.2.0] - unreleased'
        $result.Text | Should -Match '(?m)^## \[Unreleased\]\n\n## \[0\.2\.0\] - 2026-11-02\n\n### Added\n\n- Second thing\.$'
        $result.Text | Should -Match '(?m)^\[Unreleased\]: https://github.com/o/r/compare/v0\.2\.0\.\.\.HEAD\n\[0\.2\.0\]: https://github.com/o/r/compare/v0\.1\.0\.\.\.v0\.2\.0\n\[0\.1\.0\]: '
    }
    It 'moves the Unreleased entries into a new dated section and keeps an empty Unreleased section' {
        $result = ConvertTo-ReleaseChangelog -Text $script:SeedChangelog -Version '0.2.0' -Date '2026-11-02'
        $result.Change | Should -BeExactly 'Moved'
        $result.Text | Should -BeExactly (@'
# Changelog

## [Unreleased]

## [0.2.0] - 2026-11-02

### Added

- Second thing.

## [0.1.0] - 2026-10-21

### Added

- First release.

[Unreleased]: https://github.com/test-owner/LayerPeek/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/test-owner/LayerPeek/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/test-owner/LayerPeek/releases/tag/v0.1.0

'@ -replace '\r\n', "`n")
    }
    It 'produces a section the upload accepts (dated, not empty, no link references)' {
        $path = Join-Path $script:TempRoot 'cl-ok.md'
        [System.IO.File]::WriteAllText($path, (ConvertTo-ReleaseChangelog -Text $script:SeedChangelog -Version '0.2.0' -Date '2026-11-02').Text)
        Get-ChangelogSection -Path $path -Version '0.2.0' | Should -BeExactly "### Added`n`n- Second thing."
    }
    It 'converts the real CHANGELOG.md for its version into a section the upload accepts' {
        $version = Get-SourceVersion (Join-Path $script:RepoRoot 'Directory.Build.props')
        $text = (Read-ReleaseTextFile (Join-Path $script:RepoRoot 'CHANGELOG.md')).Text
        $tags = @(& git -C $script:RepoRoot tag --list 'v*')
        if ($tags -contains "v$version") { Set-ItResult -Skipped -Because "v$version is already released" ; return }
        $path = Join-Path $script:TempRoot 'cl-real.md'
        [System.IO.File]::WriteAllText($path, (ConvertTo-ReleaseChangelog -Text $text -Version $version -Date '2026-11-02').Text)
        Get-ChangelogSection -Path $path -Version $version | Should -Not -BeNullOrEmpty
    }
    It 'keeps CRLF line endings' {
        $result = ConvertTo-ReleaseChangelog -Text ($script:SeedChangelog -replace "`n", "`r`n") -Version '0.2.0' -Date '2026-11-02'
        $result.Text | Should -Not -Match '(?<!\r)\n'
        $result.Text | Should -Match "## \[0\.2\.0\] - 2026-11-02`r`n"
    }
    It 'keeps the line ending of every line in a file with mixed line endings (new lines get the dominant one)' {
        $mixed = "# Changelog`r`n`r`n## [Unreleased]`n`n### Added`r`n`r`n- Second thing.`r`n`r`n## [0.1.0] - 2026-10-21`r`n`r`n- First.`n"
        $text = (ConvertTo-ReleaseChangelog -Text $mixed -Version '0.2.0' -Date '2026-11-02').Text
        # Unreleased heading keeps its LF; the moved lines keep their CRLF; the inserted heading and blank lines get CRLF
        # (the most frequent ending); the last line keeps its LF.
        $text | Should -BeExactly "# Changelog`r`n`r`n## [Unreleased]`n`r`n## [0.2.0] - 2026-11-02`r`n`r`n### Added`r`n`r`n- Second thing.`r`n`r`n## [0.1.0] - 2026-10-21`r`n`r`n- First.`n"
    }
    It 'keeps a missing final line ending missing' {
        (ConvertTo-ReleaseChangelog -Text "## [Unreleased]`n`n- x" -Version '0.2.0' -Date '2026-11-02').Text | Should -BeExactly "## [Unreleased]`n`n## [0.2.0] - 2026-11-02`n`n- x"
        (ConvertTo-ReleaseChangelog -Text "## [Unreleased]`n`n## [0.2.0] - unreleased`n`n- x" -Version '0.2.0' -Date '2026-11-02').Text | Should -BeExactly "## [Unreleased]`n`n## [0.2.0] - 2026-11-02`n`n- x"
    }
    It 'leaves empty template sub-headings and an entry-less preamble under Unreleased and moves only filled ones' {
        $template = "## [Unreleased]`n`n<!-- Add entries below. -->`n`n### Added`n`n- New.`n`n### Fixed`n`n### Security`n`n## [0.1.0] - 2026-10-21`n`n- First.`n"
        (ConvertTo-ReleaseChangelog -Text $template -Version '0.2.0' -Date '2026-11-02').Text |
            Should -BeExactly "## [Unreleased]`n`n<!-- Add entries below. -->`n`n### Fixed`n`n### Security`n`n## [0.2.0] - 2026-11-02`n`n### Added`n`n- New.`n`n## [0.1.0] - 2026-10-21`n`n- First.`n"
    }
    It 'updates the Unreleased compare link even when the version link already exists' {
        $text = $script:Placeholder.Replace('[0.1.0]: https://github.com/o/r/releases/tag/v0.1.0', "[0.2.0]: https://github.com/o/r/compare/v0.1.0...v0.2.0`n[0.1.0]: https://github.com/o/r/releases/tag/v0.1.0")
        $result = (ConvertTo-ReleaseChangelog -Text $text -Version '0.2.0' -Date '2026-11-02').Text
        $result | Should -Match '(?m)^\[Unreleased\]: https://github.com/o/r/compare/v0\.2\.0\.\.\.HEAD$'
        @([regex]::Matches($result, '(?m)^\[0\.2\.0\]:')).Count | Should -Be 1
    }
    It 'treats a heading with an en or em dash as the same version section (<_>)' -ForEach ([string][char]0x2013), ([string][char]0x2014) {
        $text = $script:Placeholder.Replace('## [0.2.0] - unreleased', "## [0.2.0] $_ unreleased")
        $result = ConvertTo-ReleaseChangelog -Text $text -Version '0.2.0' -Date '2026-11-02'
        $result.Change | Should -BeExactly 'Dated'
        $result.Text | Should -Match '(?m)^## \[0\.2\.0\] - 2026-11-02$'
        @([regex]::Matches($result.Text, '(?m)^## \[0\.2\.0\]')).Count | Should -Be 1
    }
    It 'reports a re-dated and an unchanged section' {
        $dated = $script:Placeholder -replace '0\.2\.0\] - unreleased', '0.2.0] - 2026-10-30'
        (ConvertTo-ReleaseChangelog -Text $dated -Version '0.2.0' -Date '2026-11-02').Change | Should -BeExactly 'Redated'
        (ConvertTo-ReleaseChangelog -Text $dated -Version '0.2.0' -Date '2026-10-30').Change | Should -BeExactly 'Unchanged'
    }
    It 'does not add a link reference when there is no compare link to extend' {
        $text = "## [Unreleased]`n`n- x`n"
        (ConvertTo-ReleaseChangelog -Text $text -Version '0.2.0' -Date '2026-11-02').Text | Should -BeExactly "## [Unreleased]`n`n## [0.2.0] - 2026-11-02`n`n- x`n"
    }
    It 'refuses a changelog with nothing to release' {
        Assert-ThrowsMatching { ConvertTo-ReleaseChangelog -Text "## [Unreleased]`n`n### Added`n`n## [0.1.0] - 2026-10-21`n- a`n" -Version '0.2.0' -Date '2026-11-02' } 'no entries for 0\.2\.0'
    }
    It 'refuses entries both under Unreleased and under the version' {
        $both = $script:Placeholder -replace '## \[Unreleased\]\n', "## [Unreleased]`n`n- Also new.`n"
        Assert-ThrowsMatching { ConvertTo-ReleaseChangelog -Text $both -Version '0.2.0' -Date '2026-11-02' } 'both under \[Unreleased\] and under \[0\.2\.0\]'
    }
    It 'refuses an empty version section, a missing or doubled Unreleased section and a doubled version section' {
        Assert-ThrowsMatching { ConvertTo-ReleaseChangelog -Text "## [Unreleased]`n`n## [0.2.0] - unreleased`n`n### Added`n" -Version '0.2.0' -Date '2026-11-02' } 'is empty'
        Assert-ThrowsMatching { ConvertTo-ReleaseChangelog -Text "## [0.2.0] - unreleased`n- a`n" -Version '0.2.0' -Date '2026-11-02' } "no '## \[Unreleased\]'"
        Assert-ThrowsMatching { ConvertTo-ReleaseChangelog -Text "## [Unreleased]`n## [Unreleased]`n- a`n" -Version '0.2.0' -Date '2026-11-02' } '2 .*Unreleased'
        Assert-ThrowsMatching { ConvertTo-ReleaseChangelog -Text "## [Unreleased]`n## [0.2.0]`n- a`n## [0.2.0]`n- b`n" -Version '0.2.0' -Date '2026-11-02' } '2 sections for version 0\.2\.0'
    }
    It 'refuses the release date <_>' -ForEach '2026-13-01', '02.11.2026', '' {
        $date = $_
        Assert-ThrowsMatching { ConvertTo-ReleaseChangelog -Text $script:SeedChangelog -Version '0.2.0' -Date $date } 'not in the form YYYY-MM-DD|empty string'
    }
}

Describe 'Plan and resume detection (pure)' {
    BeforeAll {
        $a = 'a' * 40; $m = 'b' * 40; $d = 'd' * 40; $c = 'c' * 40; $t = 'e' * 40; $dm = 'f' * 40
        $script:Ids = @{ Main = $m; Develop = $d; Release = $c; Tag = $t; DevelopMerge = $dm; Other = $a }
        # The check of a valid release commit (version, dated changelog of the release day).
        $script:GoodCheck = [pscustomobject]@{ Version = '0.2.0'; VersionProblem = $null; ChangelogProblem = $null; Date = '2026-11-02'; Redate = $null }
        # A fresh release that may start: on develop, clean, everything fetched, nothing of 0.2.0 exists yet.
        $script:Fresh = [pscustomobject]@{
            Version = '0.2.0'; Tag = 'v0.2.0'; Branch = 'release/0.2.0'; Date = '2026-11-02'; HostOs = 'windows'; Runtimes = @('win-x64')
            Repository = 'o/r'; RepositoryArgument = $null; Remote = 'origin'; RemoteProblem = $null; RemoteQueried = $true
            RemoteMain = $m; RemoteDevelop = $d; RemoteTagCommit = $null; RemoteMainPresent = $true; RemoteDevelopPresent = $true
            CurrentBranch = 'develop'; StatusLines = @(); OtherWorktreeBranches = @()
            LocalMain = $m; LocalDevelop = $d; LocalBranchCommit = $null; LocalTagCommit = $null; ExistingTags = @('v0.1.0')
            MainRelation = 'equal'; DevelopRelation = 'equal'; DevelopFirstParent = $a; DevelopSecondParent = $null
            TagFirstParent = $null; TagParentCount = 0; TagVersion = $null; BranchVersion = $null; ReleaseCommit = $null
            LocalMainContainsTag = $null; LocalMainIsAncestorOfTag = $null; RemoteMainContainsTag = $null
            LocalDevelopContainsRelease = $null; RemoteDevelopContainsRelease = $null
            SourceVersion = '0.1.0'
            PropsChange = [pscustomobject]@{ From = '0.1.0'; To = '0.2.0'; Problem = $null }
            Changelog = [pscustomobject]@{ Change = 'Moved'; OldHeading = '## [Unreleased]'; NewHeading = '## [0.2.0] - 2026-11-02'; Problem = $null }
            GhProblems = @(); Release = $null; ReleaseError = $null; BuildCurrent = $false; BuildProblems = @('No build output')
            UploadPlan = @(); ChecksumFile = 'SHA256SUMS-windows.txt'; ToolchainProblems = @(); GitVersion = [version]'2.56'
            ReleaseCheck = $null; BranchOnlyCommits = @(); DevelopIsAncestorOfRelease = $null; HasOrigin = $true; OriginRepository = 'o/r'; PushRepository = 'o/r'
            DevelopFirstParentInRemote = $null; ToolchainSkipped = $false
        }
        # Everything of Gitflow done: tag and merges pushed, branch deleted.
        $script:GitDone = Copy-With $script:Fresh @{
            RemoteTagCommit = $t; LocalTagCommit = $t; LocalMain = $t; RemoteMain = $t; LocalDevelop = $dm; RemoteDevelop = $dm
            TagFirstParent = $m; TagParentCount = 2; TagVersion = '0.2.0'; ReleaseCommit = $c; ExistingTags = @('v0.1.0', 'v0.2.0')
            LocalMainContainsTag = $true; LocalMainIsAncestorOfTag = $true; RemoteMainContainsTag = $true
            LocalDevelopContainsRelease = $true; RemoteDevelopContainsRelease = $true; DevelopFirstParent = $d; DevelopSecondParent = $c
            PropsChange = $null; Changelog = $null; SourceVersion = '0.2.0'; ReleaseCheck = $script:GoodCheck
        }
        function Release([bool] $Draft = $true, [object[]] $Assets = @()) { [pscustomobject]@{ isDraft = $Draft; url = 'https://example.invalid/r'; assets = $Assets } }
        function Plan($Facts) { Get-ReleaseFlowPlan -Facts $Facts }
    }

    It 'plans every step of a fresh release without problems' {
        $plan = Plan $script:Fresh
        Assert-Problems $plan.Problems
        States $plan | Should -BeExactly @('Branch=todo', 'MergeMain=todo', 'PushMain=todo', 'MergeDevelop=todo', 'PushDevelop=todo', 'DeleteBranch=todo', 'Build=todo', 'Upload=todo')
        @($plan.Steps | Where-Object Outward | ForEach-Object Id) | Should -BeExactly @('PushMain', 'PushDevelop', 'Upload')
        $plan.Published | Should -BeFalse
    }
    It 'reports every failed precondition of a fresh release at once' {
        $facts = Copy-With $script:Fresh @{
            CurrentBranch = 'feature/x'; StatusLines = @('?? new.txt'); DevelopRelation = 'ahead'; MainRelation = 'diverged'
            ExistingTags = @('v0.1.0', 'v0.3.0'); GhProblems = @('GitHub CLI is not logged in'); ToolchainProblems = @('The MSVC component x is not installed.')
            Changelog = [pscustomobject]@{ Change = $null; OldHeading = $null; NewHeading = $null; Problem = 'CHANGELOG.md has no entries for 0.2.0' }
        }
        Assert-Problems (Plan $facts).Problems 'from develop \(HEAD is feature/x\)', 'not clean \(1 .*new\.txt', 'develop has commits that are not on origin/develop',
            'main and origin/main have diverged', 'not greater than the latest release tag v0\.3\.0', 'not logged in', 'MSVC', 'no entries for 0\.2\.0'
    }
    It 'asks for git fetch when the remote branches are not in the local repository' {
        $facts = Copy-With $script:Fresh @{ RemoteMainPresent = $false; RemoteDevelopPresent = $false; MainRelation = 'unknown'; DevelopRelation = 'unknown' }
        Assert-Problems (Plan $facts).Problems 'origin/main \(b{7}\) is not in the local repository\. Run: git fetch origin', 'origin/develop \(d{7}\) .*git fetch origin'
    }
    It 'accepts a local main behind the remote main (the merge is based on the remote main)' {
        Assert-Problems (Plan (Copy-With $script:Fresh @{ MainRelation = 'behind' })).Problems
        Assert-Problems (Plan (Copy-With $script:Fresh @{ MainRelation = 'missing'; LocalMain = $null })).Problems
    }
    It 'refuses a local develop behind the remote develop' {
        Assert-Problems (Plan (Copy-With $script:Fresh @{ DevelopRelation = 'behind' })).Problems 'behind origin/develop\. Run: git pull --ff-only'
    }
    It 'refuses a version lower than develop, a GitHub release without tag and branches checked out elsewhere' {
        $facts = Copy-With $script:Fresh @{ SourceVersion = '0.3.0'; Release = (Release); OtherWorktreeBranches = @('main') }
        Assert-Problems (Plan $facts).Problems 'lower than <Version> 0\.3\.0', 'GitHub release for v0\.2\.0 already exists .* although the tag does not', 'main is checked out in another worktree'
    }
    It 'needs git 2.38 or newer only while a merge is still to be made' {
        Assert-Problems (Plan (Copy-With $script:Fresh @{ GitVersion = [version]'2.37' })).Problems 'git 2\.37 is too old: .*2\.38'
        Assert-Problems (Plan (Copy-With $script:GitDone @{ GitVersion = [version]'2.37' })).Problems
    }
    It 'reports an unresolved or unreachable remote' {
        $facts = Copy-With $script:Fresh @{ RemoteProblem = 'git ls-remote origin failed: x'; RemoteQueried = $false }
        Assert-Problems (Plan $facts).Problems 'ls-remote origin failed'
    }

    It 'continues after the release branch was created' {
        $facts = Copy-With $script:Fresh @{ LocalBranchCommit = $script:Ids.Release; BranchVersion = '0.2.0'; ReleaseCommit = $script:Ids.Release; PropsChange = $null; Changelog = $null; ReleaseCheck = $script:GoodCheck; DevelopIsAncestorOfRelease = $true }
        $plan = Plan $facts
        Assert-Problems $plan.Problems
        States $plan | Should -BeExactly @('Branch=done', 'MergeMain=todo', 'PushMain=todo', 'MergeDevelop=todo', 'PushDevelop=todo', 'DeleteBranch=todo', 'Build=todo', 'Upload=todo')
    }
    It 'checks an existing release branch like a new one: version, dated changelog, based on develop' {
        $branch = Copy-With $script:Fresh @{ LocalBranchCommit = $script:Ids.Release; ReleaseCommit = $script:Ids.Release; PropsChange = $null; Changelog = $null; DevelopIsAncestorOfRelease = $true }
        $bad = [pscustomobject]@{ Version = '0.1.0'; VersionProblem = "its Directory.Build.props has version '0.1.0' instead of 0.2.0"; ChangelogProblem = "Section [0.2.0] in CHANGELOG.md has no release date: expected '## [0.2.0] - YYYY-MM-DD', found '## [0.2.0] - unreleased'."; Date = $null; Redate = $null }
        Assert-Problems (Plan (Copy-With $branch @{ ReleaseCheck = $bad })).Problems "release/0\.2\.0 exists, but its Directory.Build.props has version '0\.1\.0'.*git branch -D release/0\.2\.0", 'release/0\.2\.0: Section \[0\.2\.0\] in CHANGELOG\.md has no release date'
        Assert-Problems (Plan (Copy-With $branch @{ ReleaseCheck = $script:GoodCheck; DevelopIsAncestorOfRelease = $false })).Problems 'release/0\.2\.0 \(c{7}\) is not based on the current develop \(d{7}\)'
    }
    It 're-dates an existing release branch dated with an earlier day (before the tag exists)' {
        $old = [pscustomobject]@{ Version = '0.2.0'; VersionProblem = $null; ChangelogProblem = $null; Date = '2026-10-30'; Redate = [pscustomobject]@{ OldHeading = '## [0.2.0] - 2026-10-30'; NewHeading = '## [0.2.0] - 2026-11-02' } }
        $facts = Copy-With $script:Fresh @{ LocalBranchCommit = $script:Ids.Release; ReleaseCommit = $script:Ids.Release; PropsChange = $null; Changelog = $null; ReleaseCheck = $old; DevelopIsAncestorOfRelease = $true }
        $plan = Plan $facts
        Assert-Problems $plan.Problems
        States $plan | Select-Object -First 1 | Should -BeExactly 'Branch=todo'
        (Format-ReleaseFlowPlan -Plan $plan -Facts $facts) -join "`n" | Should -Match "\[todo\] 1\. Re-date release/0\.2\.0 to the release day\n.*'## \[0\.2\.0\] - 2026-10-30' -> '## \[0\.2\.0\] - 2026-11-02' \(redated\)"
    }
    It 'leaves the date of an existing tag and only notes it' {
        $old = Copy-With $script:GoodCheck @{ Date = '2026-10-30'; Redate = [pscustomobject]@{ OldHeading = '## [0.2.0] - 2026-10-30'; NewHeading = '## [0.2.0] - 2026-11-02' } }
        $plan = Plan (Copy-With $script:GitDone @{ ReleaseCheck = $old })
        Assert-Problems $plan.Problems
        $plan.Notes -join "`n" | Should -Match 'CHANGELOG\.md of v0\.2\.0 is dated 2026-10-30, not the release day 2026-11-02; the tagged release notes stay'
        Assert-Problems (Plan (Copy-With $script:GitDone @{ ReleaseCheck = (Copy-With $script:GoodCheck @{ ChangelogProblem = 'Section [0.2.0] in CHANGELOG.md is empty.' }) })).Problems 'Tag v0\.2\.0: Section .* is empty\. The tag is never changed automatically'
    }
    It 'lists the commits of the release branch that develop does not have (stabilization), without refusing' {
        $facts = Copy-With $script:Fresh @{ LocalBranchCommit = $script:Ids.Release; ReleaseCommit = $script:Ids.Release; PropsChange = $null; Changelog = $null; ReleaseCheck = $script:GoodCheck; DevelopIsAncestorOfRelease = $true; BranchOnlyCommits = @('c1 Fix the tray icon', 'c2 Release 0.2.0') }
        $plan = Plan $facts
        Assert-Problems $plan.Problems
        $text = (Format-ReleaseFlowPlan -Plan $plan -Facts $facts) -join "`n"
        $text | Should -Match 'Note: release/0\.2\.0 has 2 commit\(s\) that develop does not have yet'
        $text | Should -Match 'Note:   c1 Fix the tray icon'
    }
    It 'compares owner/name case-insensitively, as GitHub and Resolve-ReleaseRemote do' {
        Assert-Problems (Plan (Copy-With $script:Fresh @{ PushRepository = 'O/R' })).Problems
        Assert-Problems (Plan (Copy-With $script:Fresh @{ RepositoryArgument = 'o/trial'; Repository = 'o/Trial'; Remote = 'trial'; PushRepository = 'O/TRIAL'; OriginRepository = 'o/trial' })).Problems
    }
    It 'gives the reset advice only when the merge back is the only local commit, with git reset --keep' {
        $moved = Copy-With $script:GitDone @{ RemoteDevelop = $script:Ids.Other; DevelopRelation = 'diverged'; RemoteDevelopContainsRelease = $false; DevelopFirstParentInRemote = $true }
        Assert-Problems (Plan $moved).Problems 'git reset --keep origin/develop; only the merge is lost'
        Assert-Problems (Plan (Copy-With $moved @{ DevelopFirstParentInRemote = $false })).Problems 'Local develop and origin/develop have diverged after the merge back .*resolve it by hand'
        (Plan $moved).Problems -join ' ' | Should -Not -Match 'reset --hard'
    }
    It 'refuses the push of main when local main has commits after the tagged merge' {
        $facts = Copy-With $script:GitDone @{ RemoteTagCommit = $null; RemoteMain = $script:Ids.Main; RemoteMainContainsTag = $false; MainRelation = 'ahead'; LocalMain = $script:Ids.Other }
        Assert-Problems (Plan $facts).Problems 'Local main \(a{7}\) has commits after the release merge of tag v0\.2\.0 \(e{7}\).*git branch -f main v0\.2\.0'
    }
    It 'prints the test mode of the toolchain check' {
        $text = (Format-ReleaseFlowPlan -Plan (Plan $script:Fresh) -Facts (Copy-With $script:Fresh @{ ToolchainSkipped = $true })) -join "`n"
        $text | Should -Match 'TEST MODE: toolchain check skipped'
        (Format-ReleaseFlowPlan -Plan (Plan $script:Fresh) -Facts $script:Fresh) -join "`n" | Should -Not -Match 'TEST MODE'
    }
    It 'refuses -Repo in a clone whose origin is another repository, and a push URL of another repository' {
        Assert-Problems (Plan (Copy-With $script:Fresh @{ RepositoryArgument = 'o/trial'; Repository = 'o/trial'; Remote = 'trial'; PushRepository = 'o/trial' })).Problems "origin is o/r, not -Repo o/trial.*separate temporary clone without origin"
        Assert-Problems (Plan (Copy-With $script:Fresh @{ RepositoryArgument = 'o/trial'; Repository = 'o/trial'; Remote = 'trial'; PushRepository = 'o/trial'; HasOrigin = $false; OriginRepository = $null })).Problems
        Assert-Problems (Plan (Copy-With $script:Fresh @{ PushRepository = 'x/elsewhere' })).Problems 'Remote origin fetches from o/r but pushes to x/elsewhere'
        Assert-Problems (Plan (Copy-With $script:Fresh @{ PushRepository = $null })).Problems 'pushes to a non-GitHub URL'
    }
    It 'continues with the push after the local merge and tag (interrupted or not confirmed)' {
        $facts = Copy-With $script:GitDone @{
            RemoteTagCommit = $null; RemoteMain = $script:Ids.Main; RemoteMainContainsTag = $false; MainRelation = 'ahead'
            LocalDevelop = $script:Ids.Develop; RemoteDevelop = $script:Ids.Develop; DevelopRelation = 'equal'; DevelopFirstParent = $script:Ids.Other; DevelopSecondParent = $null
            LocalDevelopContainsRelease = $false; RemoteDevelopContainsRelease = $false; LocalBranchCommit = $script:Ids.Release
        }
        $plan = Plan $facts
        Assert-Problems $plan.Problems
        States $plan | Should -BeExactly @('Branch=done', 'MergeMain=done', 'PushMain=todo', 'MergeDevelop=todo', 'PushDevelop=todo', 'DeleteBranch=todo', 'Build=todo', 'Upload=todo')
    }
    It 'moves only main to an existing local tag (tag made, main not moved yet)' {
        $facts = Copy-With $script:GitDone @{
            RemoteTagCommit = $null; RemoteMain = $script:Ids.Main; LocalMain = $script:Ids.Main; MainRelation = 'equal'; LocalMainContainsTag = $false
            RemoteMainContainsTag = $false; LocalBranchCommit = $script:Ids.Release; LocalDevelopContainsRelease = $false; RemoteDevelopContainsRelease = $false
            LocalDevelop = $script:Ids.Develop; RemoteDevelop = $script:Ids.Develop; DevelopRelation = 'equal'
        }
        $plan = Plan $facts
        Assert-Problems $plan.Problems
        @($plan.Steps | Where-Object { -not $_.Done } | ForEach-Object Id)[0] | Should -BeExactly 'MergeMain'
        Assert-Problems (Plan (Copy-With $facts @{ LocalMainIsAncestorOfTag = $false })).Problems 'Local main .* is not contained in tag v0\.2\.0'
    }
    It 'refuses to push main when the remote main moved after the release merge' {
        $facts = Copy-With $script:GitDone @{ RemoteTagCommit = $null; RemoteMainContainsTag = $false; MainRelation = 'diverged'; RemoteMain = $script:Ids.Other }
        Assert-Problems (Plan $facts).Problems 'origin/main has moved since the release merge .*Nothing is force-pushed'
    }
    It 'continues with the develop push after the merge back' {
        $facts = Copy-With $script:GitDone @{ RemoteDevelop = $script:Ids.Develop; DevelopRelation = 'ahead'; RemoteDevelopContainsRelease = $false; LocalBranchCommit = $script:Ids.Release }
        $plan = Plan $facts
        Assert-Problems $plan.Problems
        @($plan.Steps | Where-Object { -not $_.Done } | ForEach-Object Id) | Should -BeExactly @('PushDevelop', 'DeleteBranch', 'Build', 'Upload')
    }
    It 'refuses to push develop with other unpushed commits besides the merge' {
        $facts = Copy-With $script:GitDone @{ RemoteDevelop = $script:Ids.Develop; DevelopRelation = 'ahead'; RemoteDevelopContainsRelease = $false; DevelopFirstParent = $script:Ids.DevelopMerge; DevelopSecondParent = $null }
        Assert-Problems (Plan $facts).Problems 'develop has other commits besides the merge of release/0\.2\.0'
    }
    It 'explains how to recover when origin/develop moved after the merge back' {
        $facts = Copy-With $script:GitDone @{ RemoteDevelop = $script:Ids.Other; DevelopRelation = 'diverged'; RemoteDevelopContainsRelease = $false; DevelopFirstParentInRemote = $true }
        Assert-Problems (Plan $facts).Problems 'origin/develop has moved since the merge back of release/0\.2\.0 .*git reset --keep origin/develop.*re-run'
    }
    It 'refuses a tag that is only on the remote, a moved tag and a tag that is not a release merge' {
        Assert-Problems (Plan (Copy-With $script:GitDone @{ LocalTagCommit = $null })).Problems 'on origin but not in the local repository\. Run: git fetch origin --tags'
        Assert-Problems (Plan (Copy-With $script:GitDone @{ RemoteTagCommit = $script:Ids.Other })).Problems 'on origin points to a{7}, the local tag to e{7}\. Tags are never moved automatically'
        Assert-Problems (Plan (Copy-With $script:GitDone @{ TagParentCount = 1; TagVersion = '0.1.0' })).Problems 'is not a merge commit of release/0\.2\.0', "props has version '0\.1\.0'"
    }
    It 'refuses to delete a release branch with commits outside the tag' {
        Assert-Problems (Plan (Copy-With $script:GitDone @{ LocalBranchCommit = $script:Ids.Other })).Problems 'release/0\.2\.0 \(a{7}\) has commits that are not part of tag v0\.2\.0'
    }
    It 'builds and uploads after Gitflow is done, and checks the toolchain only while the build is pending' {
        $plan = Plan (Copy-With $script:GitDone @{ ToolchainProblems = @('The MSVC component x is not installed.') })
        @($plan.Steps | Where-Object { -not $_.Done } | ForEach-Object Id) | Should -BeExactly @('Build', 'Upload')
        Assert-Problems $plan.Problems 'MSVC'
        $built = Plan (Copy-With $script:GitDone @{ BuildCurrent = $true; ToolchainProblems = @('The MSVC component x is not installed.') })
        Assert-Problems $built.Problems
        @($built.Steps | Where-Object { -not $_.Done } | ForEach-Object Id) | Should -BeExactly @('Upload')
    }
    It 'finishes when the draft holds every file of the local build' {
        $facts = Copy-With $script:GitDone @{ BuildCurrent = $true; Release = (Release); UploadPlan = @([pscustomobject]@{ Name = 'a.zip'; Action = 'Skip' }) }
        @((Plan $facts).Steps | Where-Object { -not $_.Done }) | Should -BeNullOrEmpty
        $partial = Copy-With $facts @{ UploadPlan = @([pscustomobject]@{ Name = 'a.zip'; Action = 'Skip' }, [pscustomobject]@{ Name = 'b.zip'; Action = 'Upload' }) }
        @((Plan $partial).Steps | Where-Object { -not $_.Done } | ForEach-Object Id) | Should -BeExactly @('Upload')
    }
    It 'refuses files in the draft with different content' {
        $facts = Copy-With $script:GitDone @{ BuildCurrent = $true; Release = (Release); UploadPlan = @([pscustomobject]@{ Name = 'a.zip'; Action = 'Conflict' }) }
        Assert-Problems (Plan $facts).Problems 'already has different files with the names a\.zip.*-Force'
    }
    It 'does not rebuild into a draft that already has the files of this OS' {
        $facts = Copy-With $script:GitDone @{ Release = (Release -Assets @([pscustomobject]@{ name = 'SHA256SUMS-windows.txt'; digest = 'sha256:x' })) }
        Assert-Problems (Plan $facts).Problems 'already contains SHA256SUMS-windows\.txt, but there is no matching local build'
        Assert-Problems (Plan (Copy-With $facts @{ Release = (Release -Assets @([pscustomobject]@{ name = 'SHA256SUMS-macos.txt'; digest = 'sha256:x' })) })).Problems
    }
    It 'needs neither build nor upload for a published release' {
        $plan = Plan (Copy-With $script:GitDone @{ Release = (Release $false); ToolchainProblems = @('The MSVC component x is not installed.') })
        $plan.Published | Should -BeTrue
        Assert-Problems $plan.Problems
        @($plan.Steps | Where-Object { -not $_.Done }) | Should -BeNullOrEmpty
        (Format-ReleaseFlowPlan -Plan $plan -Facts $script:GitDone) -join "`n" | Should -Match 'already published'
    }
    It 'prints the overwritten date of an already dated version section' {
        $redated = [pscustomobject]@{ Change = 'Redated'; OldHeading = '## [0.2.0] - 2026-10-30'; NewHeading = '## [0.2.0] - 2026-11-02'; Problem = $null }
        $facts = Copy-With $script:Fresh @{ Changelog = $redated }
        (Format-ReleaseFlowPlan -Plan (Plan $facts) -Facts $facts) -join "`n" | Should -Match "CHANGELOG\.md '## \[0\.2\.0\] - 2026-10-30' -> '## \[0\.2\.0\] - 2026-11-02' \(redated\)"
    }
    It 'describes the plan with the remote, the tag and -Repo' {
        $lines = Format-ReleaseFlowPlan -Plan (Plan (Copy-With $script:Fresh @{ RepositoryArgument = 'o/trial'; Remote = 'trial' })) -Facts $script:Fresh
        $text = $lines -join "`n"
        $text | Should -Match "\[todo\] 1\. Create release/0\.2\.0\n.*<Version> 0\.1\.0 -> 0\.2\.0, CHANGELOG\.md '## \[Unreleased\]' -> '## \[0\.2\.0\] - 2026-11-02' \(moved\)"
        $text | Should -Match 'git push --atomic trial <merge>:refs/heads/main refs/tags/v0.2.0'
        $text | Should -Match 'git push --atomic trial <merge>:refs/heads/develop'
        $text | Should -Match 'release-upload\.ps1 -Repo o/trial -DryRun'
        $text | Should -Match 'then back to develop: git switch develop'
    }
    It 'names the OS that went first and how the other one follows (T-113, <HostOs>)' -ForEach @(
        @{ HostOs = 'macos'; This = 'macOS'; Other = 'Windows' }
        @{ HostOs = 'windows'; This = 'Windows'; Other = 'macOS' }
    ) {
        $text = (Get-ReleaseNextSteps -Version '0.2.0' -HostOs $HostOs) -join "`n"
        $text | Should -Match "release-publish\.ps1 -AllowIncomplete   # only the $This files so far"
        $text | Should -Match "The $Other files follow on a $Other machine"
        $text | Should -Match 'git fetch origin --tags; git switch --detach v0\.2\.0; pwsh scripts/release-build\.ps1'
        $text | Should -Match 'pwsh scripts/release-upload\.ps1 -AddToPublished'
        $text | Should -Not -Match 'Windows only|T-019'
    }
    It 'tells how to add this OS to a release that the other OS published without it (T-113)' {
        $facts = Copy-With $script:GitDone @{ ChecksumFile = 'SHA256SUMS-windows.txt'; Release = (Release $false -Assets @([pscustomobject]@{ name = 'SHA256SUMS-macos.txt'; digest = 'sha256:x' })) }
        $plan = Plan $facts
        Assert-Problems $plan.Problems
        $text = (Format-ReleaseFlowPlan -Plan $plan -Facts $facts) -join "`n"
        $text | Should -Match 'already published'
        $text | Should -Match 'It has no SHA256SUMS-windows\.txt yet'
        $text | Should -Match 'pwsh scripts/release-upload\.ps1 -AddToPublished'
        $with = Copy-With $facts @{ ChecksumFile = 'SHA256SUMS-macos.txt' }
        (Format-ReleaseFlowPlan -Plan (Plan $with) -Facts $with) -join "`n" | Should -Not -Match 'AddToPublished'
    }
    It 'prints the manual publish commands with -AllowIncomplete and never publishes itself' {
        $text = (Get-ReleaseNextSteps -Version '0.2.0' -Repository 'o/trial' -HostOs 'windows' -Remote 'trial') -join "`n"
        $text | Should -Match 'git switch --detach v0\.2\.0'
        $text | Should -Match 'release-publish\.ps1 -Repo o/trial -DryRun'
        $text | Should -Match 'release-publish\.ps1 -Repo o/trial -AllowIncomplete'
        $text | Should -Not -Match 'T-019'
        $script = [System.IO.File]::ReadAllText((Join-Path $script:RepoRoot 'scripts' 'release.ps1'))
        $script | Should -Not -Match "release-publish\.ps1'"
        $script | Should -Not -Match 'gh release (edit|create|upload|delete)'
        $script | Should -Not -Match '--force|push -f|tag -d|--delete'
    }
}

Describe 'Gitflow steps and resume detection (throw-away repositories)' {
    BeforeAll {
        $null = Use-GhLog 'gf'
        $script:R = New-ReleaseTestRepository -Name 'gf'
        $script:Work = $script:R.Work
        $script:ToolTemp = Join-Path $script:TempRoot 'gf-tmp'
        $null = New-Item -ItemType Directory -Path $script:ToolTemp
        function Facts([string] $Version = '0.2.0', [string] $Repository) { Get-ReleaseFlowFacts -RepoRoot $script:Work -Version $Version -Repository $Repository -TempDirectory $script:ToolTemp -Date '2026-11-02' }
        function Get-RemoteRef([string] $Ref) { $r = Invoke-Git $script:R.Origin @('rev-parse', '--verify', '--quiet', $Ref); if ($r.ExitCode -eq 0) { $r.Output[0].Trim() } else { $null } }
        function Get-LocalRef([string] $Ref) { $r = Invoke-Git $script:Work @('rev-parse', '--verify', '--quiet', $Ref); if ($r.ExitCode -eq 0) { $r.Output[0].Trim() } else { $null } }
    }

    It 'detects a fresh release of the GitHub repository behind the rewritten origin URL' {
        $facts = Facts
        $facts.Repository | Should -BeExactly $script:R.Repository
        $facts.PushRepository | Should -BeExactly $script:R.Repository
        $facts.Remote | Should -BeExactly 'origin'
        $facts.Changelog.Change | Should -BeExactly 'Moved'
        $facts.SourceVersion | Should -BeExactly '0.1.0'
        $plan = Get-ReleaseFlowPlan -Facts $facts
        Assert-Problems (Select-NonToolchain $plan.Problems)
        States $plan | Should -BeExactly @('Branch=todo', 'MergeMain=todo', 'PushMain=todo', 'MergeDevelop=todo', 'PushDevelop=todo', 'DeleteBranch=todo', 'Build=todo', 'Upload=todo')
    }
    It 'refuses a push URL of another repository' {
        $null = G $script:Work config remote.origin.pushurl 'https://github.com/test-owner/elsewhere.git'
        try { Assert-Problems (Select-NonToolchain (Get-ReleaseFlowPlan -Facts (Facts)).Problems) 'Remote origin fetches from test-owner/gf but pushes to test-owner/elsewhere' }
        finally { $null = G $script:Work config --unset remote.origin.pushurl }
    }
    It 'refuses -Repo of a trial repository in a clone whose origin is the main repository' {
        $trial = Join-Path $script:TempRoot 'gf-trial.git'
        $null = & git init --quiet --bare --initial-branch=main $trial 2>&1
        $null = & git config --global "url.$($trial -replace '\\', '/').insteadOf" 'https://github.com/test-owner/gf-trial.git' 2>&1
        $null = G $script:Work remote add trial 'https://github.com/test-owner/gf-trial.git'
        try {
            $facts = Facts -Repository 'test-owner/gf-trial'
            $facts.Remote | Should -BeExactly 'trial'
            @((Get-ReleaseFlowPlan -Facts $facts).Problems | Where-Object { $_ -match 'origin is test-owner/gf, not -Repo test-owner/gf-trial.*separate temporary clone without origin' }).Count | Should -Be 1
        }
        finally { $null = G $script:Work remote remove trial }
    }
    It 'refuses a version not greater than the existing tag v0.1.0 and an untidy checkout' {
        Assert-Problems (Select-NonToolchain (Get-ReleaseFlowPlan -Facts (Facts '0.0.9')).Problems) 'not greater than the latest release tag v0\.1\.0', 'lower than <Version> 0\.1\.0 on develop'
        [System.IO.File]::WriteAllText((Join-Path $script:Work 'stray.txt'), 'x')
        # Also creates the local main (tracking origin/main) that the compare-and-swap below uses.
        $null = G $script:Work switch --quiet main
        try { Assert-Problems (Select-NonToolchain (Get-ReleaseFlowPlan -Facts (Facts)).Problems) 'from develop \(HEAD is main\)', 'not clean \(1 .*stray\.txt' }
        finally {
            $null = G $script:Work switch --quiet develop
            Remove-Item -LiteralPath (Join-Path $script:Work 'stray.txt')
        }
    }
    It 'asks for a fetch when origin has commits that are not fetched yet, and changes no refs' {
        $other = Join-Path $script:TempRoot 'gf-other'
        $null = & git clone --quiet --branch develop $script:R.Url $other 2>&1
        [System.IO.File]::WriteAllText((Join-Path $other 'x.txt'), 'x')
        $null = G $other add x.txt
        $null = G $other commit --quiet --message x
        $null = G $other push --quiet origin develop
        $refs = Get-AllRefs $script:Work
        try { Assert-Problems (Select-NonToolchain (Get-ReleaseFlowPlan -Facts (Facts)).Problems) 'origin/develop \(\w{7}\) is not in the local repository\. Run: git fetch origin' }
        finally { $null = G $other push --quiet --force origin 'HEAD~1:refs/heads/develop' }
        Get-AllRefs $script:Work | Should -BeExactly $refs
        Assert-Problems (Select-NonToolchain (Get-ReleaseFlowPlan -Facts (Facts)).Problems)
    }

    It 'step 1 creates release/0.2.0 from the develop commit (not the working tree) without touching the working tree' {
        $develop = G $script:Work rev-parse develop | Select-Object -First 1
        $index = G $script:Work ls-files --stage
        # A changed working-tree file must not get into the release commit: its files come from the develop commit.
        [System.IO.File]::AppendAllText((Join-Path $script:Work 'CHANGELOG.md'), "`nnot committed`n")
        try { $commit = New-ReleaseBranch -RepoRoot $script:Work -Version '0.2.0' -Date '2026-11-02' -BaseCommit $develop -TempDirectory $script:ToolTemp }
        finally { $null = G $script:Work checkout --quiet -- CHANGELOG.md }
        G $script:Work rev-parse 'release/0.2.0' | Select-Object -First 1 | Should -BeExactly $commit
        G $script:Work rev-parse "$commit^" | Select-Object -First 1 | Should -BeExactly $develop
        G $script:Work log -1 --format=%s $commit | Should -BeExactly 'Release 0.2.0'
        @(G $script:Work diff --name-only $develop $commit) | Should -BeExactly @('CHANGELOG.md', 'Directory.Build.props')
        (Read-GitBlobText $script:Work "$($commit):CHANGELOG.md").Text | Should -Not -Match 'not committed'
        (Read-GitBlobText $script:Work "$($commit):Directory.Build.props").Text | Should -BeExactly ($script:SeedProps -replace '0\.1\.0', '0.2.0')
        @(G $script:Work status --porcelain --untracked-files=all) | Should -BeNullOrEmpty
        G $script:Work ls-files --stage | Should -BeExactly $index
        G $script:Work symbolic-ref --short HEAD | Should -BeExactly 'develop'

        $facts = Facts
        $facts.ReleaseCheck.Date | Should -BeExactly '2026-11-02'
        $plan = Get-ReleaseFlowPlan -Facts $facts
        Assert-Problems (Select-NonToolchain $plan.Problems)
        States $plan | Should -BeExactly @('Branch=done', 'MergeMain=todo', 'PushMain=todo', 'MergeDevelop=todo', 'PushDevelop=todo', 'DeleteBranch=todo', 'Build=todo', 'Upload=todo')
    }
    It 'step 2a: when moving main fails after the tag was made, main stays and a re-run only moves main' {
        $facts = Facts
        $facts.LocalMain | Should -Not -BeNullOrEmpty
        $originMain = Get-RemoteRef main
        # A stale expected value makes the compare-and-swap of main fail, as a concurrent change of main would.
        Assert-ThrowsMatching { Invoke-ReleaseMainMerge -RepoRoot $script:Work -Version '0.2.0' -Date '2026-11-02' -ReleaseCommit $facts.ReleaseCommit -Base $facts.RemoteMain `
                -LocalMain ('0' * 39 + '1') -Remote origin -ExistingTagCommit $null -TempDirectory $script:ToolTemp } 'update-ref'
        G $script:Work cat-file -t v0.2.0 | Should -BeExactly 'tag'
        Get-LocalRef main | Should -BeExactly $facts.LocalMain

        $resumed = Facts
        $plan = Get-ReleaseFlowPlan -Facts $resumed
        Assert-Problems (Select-NonToolchain $plan.Problems)
        (States $plan)[0..1] | Should -BeExactly @('Branch=done', 'MergeMain=todo')
        $merge = Invoke-ReleaseMainMerge -RepoRoot $script:Work -Version '0.2.0' -Date '2026-11-02' -ReleaseCommit $resumed.ReleaseCommit -Base $resumed.RemoteMain `
            -LocalMain $resumed.LocalMain -Remote origin -ExistingTagCommit $resumed.LocalTagCommit -TempDirectory $script:ToolTemp
        $merge | Should -BeExactly $resumed.LocalTagCommit
        @((G $script:Work rev-list --parents -n 1 $merge) -split ' ') | Select-Object -Skip 1 | Should -BeExactly @($facts.RemoteMain, $facts.ReleaseCommit)
        Get-LocalRef main | Should -BeExactly $merge
        Get-RemoteRef main | Should -BeExactly $originMain
        Get-RemoteRef v0.2.0 | Should -BeNullOrEmpty
        @(G $script:Work status --porcelain --untracked-files=all) | Should -BeNullOrEmpty
        States (Get-ReleaseFlowPlan -Facts (Facts)) | Should -BeExactly @('Branch=done', 'MergeMain=done', 'PushMain=todo', 'MergeDevelop=todo', 'PushDevelop=todo', 'DeleteBranch=todo', 'Build=todo', 'Upload=todo')
    }
    It 'refuses to push a ref that moved after the confirmation' {
        Assert-ThrowsMatching { Invoke-ReleasePush -RepoRoot $script:Work -Remote origin -Branches @{ main = ('a' * 40) } 6>$null } 'refs/heads/main is \w{40}, but a{40} was confirmed.*nothing was pushed'
        Assert-ThrowsMatching { Invoke-ReleasePush -RepoRoot $script:Work -Remote origin -Tags @{ 'v0.2.0' = ('a' * 40) } 6>$null } 'refs/tags/v0\.2\.0 is \w{40}, but a{40} was confirmed'
        Get-RemoteRef v0.2.0 | Should -BeNullOrEmpty
    }
    It 'rejects the push when origin/main moved between the merge and the push, and pushes nothing (atomic)' {
        $merge = Get-LocalRef main
        $other = Join-Path $script:TempRoot 'gf-main'
        $null = & git clone --quiet --branch main $script:R.Url $other 2>&1
        [System.IO.File]::WriteAllText((Join-Path $other 'hotfix.txt'), 'x')
        $null = G $other add hotfix.txt
        $null = G $other commit --quiet --message hotfix
        $null = G $other push --quiet origin main
        try {
            Assert-ThrowsMatching { Invoke-ReleasePush -RepoRoot $script:Work -Remote origin -Branches @{ main = $merge } -Tags @{ 'v0.2.0' = $merge } 6>$null } 'git push origin .* failed.*Nothing is force-pushed'
            Get-RemoteRef v0.2.0 | Should -BeNullOrEmpty
            $null = G $script:Work fetch --quiet --no-tags origin
            Assert-Problems (Select-NonToolchain (Get-ReleaseFlowPlan -Facts (Facts)).Problems) 'origin/main has moved since the release merge \(local main is diverged\)\. Nothing is force-pushed'
        }
        finally {
            $null = G $other push --quiet --force origin 'HEAD~1:refs/heads/main'
            $null = G $script:Work fetch --quiet --no-tags origin
        }
    }
    It 'step 2b pushes main and the tag atomically, by SHA' {
        $merge = Get-LocalRef main
        Invoke-ReleasePush -RepoRoot $script:Work -Remote origin -Branches @{ main = $merge } -Tags @{ 'v0.2.0' = $merge } 6>$null
        Get-RemoteRef main | Should -BeExactly $merge
        Get-RemoteRef 'v0.2.0^{commit}' | Should -BeExactly $merge
        G $script:R.Origin cat-file -t v0.2.0 | Should -BeExactly 'tag'
        States (Get-ReleaseFlowPlan -Facts (Facts)) | Should -BeExactly @('Branch=done', 'MergeMain=done', 'PushMain=done', 'MergeDevelop=todo', 'PushDevelop=todo', 'DeleteBranch=todo', 'Build=todo', 'Upload=todo')
    }
    It 'refuses the merge back unless develop is checked out and still where it was' {
        $facts = Facts
        $null = G $script:Work switch --quiet --detach develop
        try { Assert-ThrowsMatching { Invoke-ReleaseDevelopMerge -RepoRoot $script:Work -Version '0.2.0' -ReleaseCommit $facts.ReleaseCommit -Develop $facts.LocalDevelop } 'needs develop checked out, but HEAD is a detached HEAD' }
        finally { $null = G $script:Work switch --quiet develop }
        Assert-ThrowsMatching { Invoke-ReleaseDevelopMerge -RepoRoot $script:Work -Version '0.2.0' -ReleaseCommit $facts.ReleaseCommit -Develop ('a' * 40) } 'develop moved to \w{40} during the release'
        Get-LocalRef develop | Should -BeExactly $facts.LocalDevelop
    }
    It 'step 2c merges back into develop and updates the checked-out files' {
        $facts = Facts
        $merge = Invoke-ReleaseDevelopMerge -RepoRoot $script:Work -Version '0.2.0' -ReleaseCommit $facts.ReleaseCommit -Develop $facts.LocalDevelop
        G $script:Work rev-parse develop | Should -BeExactly $merge
        G $script:Work log -1 --format=%s develop | Should -BeExactly "Merge branch 'release/0.2.0' into develop"
        # The worktree follows, byte for byte (UTF-8 without BOM, LF, the non-ASCII comment kept).
        [System.IO.File]::ReadAllText((Join-Path $script:Work 'Directory.Build.props')) | Should -BeExactly ($script:SeedProps -replace '0\.1\.0', '0.2.0')
        [System.IO.File]::ReadAllBytes((Join-Path $script:Work 'Directory.Build.props'))[0] | Should -Be ([byte][char]'<')
        [System.IO.File]::ReadAllText((Join-Path $script:Work 'CHANGELOG.md')) | Should -Match '(?m)^## \[Unreleased\]\n\n## \[0\.2\.0\] - 2026-11-02$'
        @(G $script:Work status --porcelain --untracked-files=all) | Should -BeNullOrEmpty

        $plan = Get-ReleaseFlowPlan -Facts (Facts)
        Assert-Problems (Select-NonToolchain $plan.Problems)
        @($plan.Steps | Where-Object { -not $_.Done } | ForEach-Object Id) | Should -BeExactly @('PushDevelop', 'DeleteBranch', 'Build', 'Upload')
    }
    It 'refuses to push develop while it has another unpushed commit' {
        [System.IO.File]::WriteAllText((Join-Path $script:Work 'late.txt'), 'x')
        $null = G $script:Work add late.txt
        $null = G $script:Work commit --quiet --message late
        try { Assert-Problems (Select-NonToolchain (Get-ReleaseFlowPlan -Facts (Facts)).Problems) 'develop has other commits besides the merge' }
        finally { $null = G $script:Work reset --quiet --hard 'HEAD~1' }
    }
    It 'step 2d and 2e push develop and delete the local release branch' {
        Invoke-ReleasePush -RepoRoot $script:Work -Remote origin -Branches @{ develop = (Get-LocalRef develop) } 6>$null
        Get-RemoteRef develop | Should -BeExactly (Get-LocalRef develop)
        @((Get-ReleaseFlowPlan -Facts (Facts)).Steps | Where-Object { -not $_.Done } | ForEach-Object Id) | Should -BeExactly @('DeleteBranch', 'Build', 'Upload')
        $null = G $script:Work branch --delete release/0.2.0
        $plan = Get-ReleaseFlowPlan -Facts (Facts)
        Assert-Problems (Select-NonToolchain $plan.Problems)
        @($plan.Steps | Where-Object { -not $_.Done } | ForEach-Object Id) | Should -BeExactly @('Build', 'Upload')
    }
    It 'detects a current build of the tag and leaves only the upload' {
        $tagCommit = G $script:Work rev-parse 'v0.2.0^{commit}' | Select-Object -First 1
        $dist = Join-Path $script:Work 'artifacts' 'release' '0.2.0' 'dist'
        $null = New-Item -ItemType Directory -Path $dist
        [System.IO.File]::WriteAllText((Join-Path $dist 'LayerPeek-0.2.0-win-x64.zip'), 'zip')
        $null = Write-Sha256Sums -Directory $dist -FileName 'SHA256SUMS-windows.txt' -Files 'LayerPeek-0.2.0-win-x64.zip'
        $info = [ordered]@{
            Version = '0.2.0'; Commit = $tagCommit; Dirty = $false; Os = 'windows'; Runtimes = @('win-x64'); ChecksumFile = 'SHA256SUMS-windows.txt'
            Files = @([ordered]@{ Name = 'LayerPeek-0.2.0-win-x64.zip'; Runtime = 'win-x64'; Step = 'archive'; Sha256 = Get-FileSha256 (Join-Path $dist 'LayerPeek-0.2.0-win-x64.zip') })
        }
        $info | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $script:Work 'artifacts' 'release' '0.2.0' 'build-info.json') -Encoding utf8NoBOM
        $facts = Facts
        $facts.BuildCurrent | Should -BeTrue
        $plan = Get-ReleaseFlowPlan -Facts $facts
        Assert-Problems $plan.Problems
        @($plan.Steps | Where-Object { -not $_.Done } | ForEach-Object Id) | Should -BeExactly @('Upload')
        [System.IO.File]::AppendAllText((Join-Path $dist 'LayerPeek-0.2.0-win-x64.zip'), 'changed')
        (Facts).BuildCurrent | Should -BeFalse
    }
    It 'calls gh only for auth status and release view' {
        $calls = @(Get-Content -LiteralPath $script:FakeGhLog | Where-Object { $_ })
        $calls.Count | Should -BeGreaterThan 0
        @($calls | Where-Object { $_ -notmatch '^(auth status|release view v0\.(2\.0|0\.9) --repo test-owner/gf(-trial)? --json )' }) | Should -BeNullOrEmpty
    }
    It 'refuses a conflicting merge without changing anything' {
        $repo = Join-Path $script:TempRoot 'gf-conflict'
        $null = & git init --quiet --initial-branch=main $repo 2>&1
        [System.IO.File]::WriteAllText((Join-Path $repo 'a.txt'), "one`n")
        $null = G $repo add a.txt
        $null = G $repo commit --quiet --message one
        $null = G $repo branch side
        [System.IO.File]::WriteAllText((Join-Path $repo 'a.txt'), "main`n")
        $null = G $repo commit --quiet --all --message main
        $null = G $repo switch --quiet side
        [System.IO.File]::WriteAllText((Join-Path $repo 'a.txt'), "side`n")
        $null = G $repo commit --quiet --all --message side
        $refs = Get-AllRefs $repo
        Assert-ThrowsMatching { New-ReleaseMergeCommit -RepoRoot $repo -Into main -From side -Message m } 'has conflicts\. Nothing was changed'
        Get-AllRefs $repo | Should -BeExactly $refs
        @(G $repo status --porcelain --untracked-files=all) | Should -BeNullOrEmpty
    }
}

Describe 'scripts/release.ps1 (throw-away clone)' {
    BeforeAll {
        $null = Use-GhLog 'rs'
        $script:S = New-ReleaseTestRepository -Name 'rs' -WithScripts
        $script:Script = Join-Path $script:S.Work 'scripts' 'release.ps1'
        function Invoke-Release([string[]] $Arguments) {
            $output = Invoke-Pwsh -NoProfile -NonInteractive -File $script:Script @Arguments 2>&1 | Out-String
            [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
        }
        function Invoke-ReleaseAnswering([string[]] $Answers, [string[]] $Arguments) {
            # The answers go to Read-Host through stdin, one line per confirmation.
            $output = $Answers | Invoke-Pwsh -NoProfile -File $script:Script @Arguments 2>&1 | Out-String
            [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
        }
        function Snapshot { "$(Get-AllRefs $script:S.Work)`n--`n$(Get-AllRefs $script:S.Origin)`n--`n$((G $script:S.Work status --porcelain --untracked-files=all) -join "`n")" }
        function Get-TempLeftover { @(Get-ChildItem -LiteralPath ([System.IO.Path]::GetTempPath()) -Filter 'layerpeek-release-*' -Force -ErrorAction SilentlyContinue) }
        $script:LeftoversBefore = @(Get-TempLeftover).Count
    }

    It 'prints the full plan in a dry run and changes nothing' {
        $before = Snapshot
        $result = Invoke-Release @('-Version', '0.2.0', '-DryRun')
        $result.ExitCode | Should -Be 0 -Because $result.Output
        $result.Output | Should -Match "Release 0\.2\.0 \(dry run\) of test-owner/rs \(git remote origin\)"
        $result.Output | Should -Match "\[todo\] 1\. Create release/0\.2\.0"
        $result.Output | Should -Match "\[todo\] 8\. Create or fill the draft release"
        $result.Output | Should -Match 'Dry run: nothing was changed'
        $result.Output | Should -Match 'TEST MODE: toolchain check skipped'
        $result.Output | Should -Match 'release-publish\.ps1 -AllowIncomplete'
        Snapshot | Should -BeExactly $before
        @(Get-TempLeftover).Count | Should -Be $script:LeftoversBefore
    }
    It 'refuses a malformed version' {
        $result = Invoke-Release @('-Version', '0.2', '-DryRun')
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match "'0\.2' is not a version in the form X\.Y\.Z"
    }
    It 'lists the problems of a dirty tree together with the plan and exits with 1' {
        [System.IO.File]::WriteAllText((Join-Path $script:S.Work 'stray.txt'), 'x')
        try {
            $result = Invoke-Release @('-Version', '0.2.0', '-DryRun')
            $result.ExitCode | Should -Be 1
            $result.Output | Should -Match '\[todo\] 1\. Create release/0\.2\.0'
            $result.Output | Should -Match 'Release REFUSED'
            $result.Output | Should -Match 'Working tree is not clean \(1 .*stray\.txt'
        }
        finally { Remove-Item -LiteralPath (Join-Path $script:S.Work 'stray.txt') }
    }
    It 'stops before the first push when it cannot be confirmed, and a re-run resumes there' {
        $originBefore = Get-AllRefs $script:S.Origin
        $result = Invoke-Release @('-Version', '0.2.0')
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'Stopped before: Push main and v0\.2\.0 .*not confirmed'
        # Local steps done, nothing pushed, still on develop with a clean tree, no temp files left.
        Get-AllRefs $script:S.Origin | Should -BeExactly $originBefore
        G $script:S.Work cat-file -t v0.2.0 | Should -BeExactly 'tag'
        G $script:S.Work symbolic-ref --short HEAD | Should -BeExactly 'develop'
        @(G $script:S.Work status --porcelain --untracked-files=all) | Should -BeNullOrEmpty
        @(Get-TempLeftover).Count | Should -Be $script:LeftoversBefore

        $again = Invoke-Release @('-Version', '0.2.0', '-DryRun')
        $again.ExitCode | Should -Be 0 -Because $again.Output
        $again.Output | Should -Match '\[done\] 1\. Create release/0\.2\.0'
        $again.Output | Should -Match '\[done\] 2\. Merge release/0\.2\.0 into main'
        $again.Output | Should -Match '\[todo\] 3\. Push main and v0\.2\.0'
    }
    It 'does not push main and the tag when the typed version is wrong' {
        $originBefore = Get-AllRefs $script:S.Origin
        $result = Invoke-ReleaseAnswering @('0.2') @('-Version', '0.2.0')
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'Stopped before: Push main and v0\.2\.0'
        Get-AllRefs $script:S.Origin | Should -BeExactly $originBefore
    }
    It 'pushes after confirmation, returns to develop when the build fails, and a re-run continues with the build' {
        $result = Invoke-ReleaseAnswering @('0.2.0', 'y') @('-Version', '0.2.0')
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'simulated build failure'
        $result.Output | Should -Match 'Release FAILED: scripts/release-build\.ps1 failed'
        $result.Output | Should -Match 're-run: pwsh scripts/release\.ps1 -Version 0\.2\.0'
        # Gitflow is complete on the remote: tag on main, release merged into develop, local release branch deleted.
        $tagCommit = G $script:S.Work rev-parse 'v0.2.0^{commit}' | Select-Object -First 1
        G $script:S.Origin rev-parse 'v0.2.0^{commit}' | Should -BeExactly $tagCommit
        G $script:S.Origin rev-parse main | Should -BeExactly $tagCommit
        G $script:S.Origin rev-parse develop | Should -BeExactly (G $script:S.Work rev-parse develop | Select-Object -First 1)
        G $script:S.Origin log -1 --format=%s develop | Should -BeExactly "Merge branch 'release/0.2.0' into develop"
        @(G $script:S.Work branch --list 'release/*') | Should -BeNullOrEmpty
        # Back on develop with a clean tree, nothing left in the temp folder.
        G $script:S.Work symbolic-ref --short HEAD | Should -BeExactly 'develop'
        @(G $script:S.Work status --porcelain --untracked-files=all) | Should -BeNullOrEmpty
        @(Get-TempLeftover).Count | Should -Be $script:LeftoversBefore

        $again = Invoke-Release @('-Version', '0.2.0', '-DryRun')
        $again.ExitCode | Should -Be 0 -Because $again.Output
        foreach ($n in 1..6) { $again.Output | Should -Match "\[done\] $n\. " }
        $again.Output | Should -Match '\[todo\] 7\. Build'
        $again.Output | Should -Match '\[todo\] 8\. Create or fill the draft release'
    }
    It 'fetches origin in a real run but not in a dry run' {
        # Someone pushes to develop on origin after the last fetch of the clone.
        $other = Join-Path $script:TempRoot 'rs-other'
        $null = & git clone --quiet --branch develop $script:S.Url $other 2>&1
        [System.IO.File]::WriteAllText((Join-Path $other 'later.txt'), 'x')
        $null = G $other add later.txt
        $null = G $other commit --quiet --message later
        $null = G $other push --quiet origin develop
        $pushed = G $other rev-parse HEAD | Select-Object -First 1
        $develop = G $script:S.Work rev-parse develop | Select-Object -First 1

        $before = Get-AllRefs $script:S.Work
        # 0.3.0: a fresh release, whose checks compare develop with origin/develop.
        $dry = Invoke-Release @('-Version', '0.3.0', '-DryRun')
        $dry.ExitCode | Should -Be 1
        $dry.Output | Should -Not -Match '==> git fetch'
        $dry.Output | Should -Match 'origin/develop \(\w{7}\) is not in the local repository\. Run: git fetch origin'
        Get-AllRefs $script:S.Work | Should -BeExactly $before

        $real = Invoke-Release @('-Version', '0.3.0')
        $real.ExitCode | Should -Be 1
        $real.Output | Should -Match '==> git fetch --no-tags origin'
        $real.Output | Should -Match 'Local develop is behind origin/develop\. Run: git pull --ff-only'
        $real.Output | Should -Not -Match 'is not in the local repository'
        # Only the remote-tracking ref moved; local develop and tags stay.
        G $script:S.Work rev-parse refs/remotes/origin/develop | Should -BeExactly $pushed
        G $script:S.Work rev-parse develop | Should -BeExactly $develop
        G $script:S.Work symbolic-ref --short HEAD | Should -BeExactly 'develop'
    }
    It 'never calls gh for anything but reading' {
        @(Get-Content -LiteralPath $script:FakeGhLog | Where-Object { $_ -and $_ -notmatch '^(auth status|release view )' }) | Should -BeNullOrEmpty
    }
}

Describe 'Existing release branch and the check before the tag (throw-away repositories)' {
    BeforeAll {
        $null = Use-GhLog 'rb'
        $script:B = New-ReleaseTestRepository -Name 'rb'
        $script:BW = $script:B.Work
        $script:BTemp = Join-Path $script:TempRoot 'rb-tmp'
        $null = New-Item -ItemType Directory -Path $script:BTemp
        function BFacts { Get-ReleaseFlowFacts -RepoRoot $script:BW -Version '0.2.0' -TempDirectory $script:BTemp -Date '2026-11-02' }
        function New-HandBranch([string] $Heading, [string] $PropsVersion = '0.2.0') {
            # A release branch made by hand from develop: Directory.Build.props and a CHANGELOG.md section as given.
            $null = G $script:BW switch --quiet --create release/0.2.0
            [System.IO.File]::WriteAllText((Join-Path $script:BW 'Directory.Build.props'), ($script:SeedProps -replace '0\.1\.0', $PropsVersion))
            [System.IO.File]::WriteAllText((Join-Path $script:BW 'CHANGELOG.md'), $script:SeedChangelog.Replace("## [Unreleased]`n`n### Added", "## [Unreleased]`n`n$Heading`n`n### Added"))
            $null = G $script:BW commit --quiet --all --message 'Release 0.2.0 by hand'
            $null = G $script:BW switch --quiet develop
            G $script:BW rev-parse release/0.2.0 | Select-Object -First 1
        }
        function Remove-ReleaseBranch { $null = G $script:BW branch --quiet --delete --force release/0.2.0 }
    }

    It 'refuses a hand-made branch with an undated changelog, and the merge before the tag refuses it too' {
        $tip = New-HandBranch '## [0.2.0] - unreleased'
        try {
            $facts = BFacts
            $plan = Get-ReleaseFlowPlan -Facts $facts
            Assert-Problems (Select-NonToolchain $plan.Problems) 'release/0\.2\.0: Section \[0\.2\.0\] in CHANGELOG\.md has no release date.*git branch -D release/0\.2\.0'
            # Without the plan check, the merge itself would still refuse to tag it: nothing is changed.
            $refs = Get-AllRefs $script:BW
            Assert-ThrowsMatching { Invoke-ReleaseMainMerge -RepoRoot $script:BW -Version '0.2.0' -Date '2026-11-02' -ReleaseCommit $tip -Base $facts.RemoteMain `
                    -LocalMain $facts.LocalMain -Remote origin -ExistingTagCommit $null -TempDirectory $script:BTemp } 'would not be releasable: Section \[0\.2\.0\] .*no release date.*Nothing was changed'
            Get-AllRefs $script:BW | Should -BeExactly $refs
        }
        finally { Remove-ReleaseBranch }
    }
    It 'refuses a hand-made branch with another version' {
        $null = New-HandBranch '## [0.2.0] - 2026-11-02' -PropsVersion '0.1.0'
        try { Assert-Problems (Select-NonToolchain (Get-ReleaseFlowPlan -Facts (BFacts)).Problems) "release/0\.2\.0 exists, but its Directory\.Build\.props has version '0\.1\.0' instead of 0\.2\.0" }
        finally { Remove-ReleaseBranch }
    }
    It 're-dates a branch dated with an earlier day before the tag exists, and lists its commits' {
        $tip = New-HandBranch '## [0.2.0] - 2026-10-30'
        try {
            $facts = BFacts
            $plan = Get-ReleaseFlowPlan -Facts $facts
            Assert-Problems (Select-NonToolchain $plan.Problems)
            (States $plan)[0] | Should -BeExactly 'Branch=todo'
            $text = (Format-ReleaseFlowPlan -Plan $plan -Facts $facts) -join "`n"
            $text | Should -Match "Re-date release/0\.2\.0 to the release day\n.*'## \[0\.2\.0\] - 2026-10-30' -> '## \[0\.2\.0\] - 2026-11-02' \(redated\)"
            $text | Should -Match 'Note: release/0\.2\.0 has 1 commit\(s\) that develop does not have yet'
            $text | Should -Match 'Note:   \w+ Release 0\.2\.0 by hand'

            # The tip must still be the checked one: a branch moved meanwhile is refused and stays as it is.
            Assert-ThrowsMatching { New-ReleaseDateCommit -RepoRoot $script:BW -Version '0.2.0' -Date '2026-11-02' -ExpectedTip ('a' * 40) -TempDirectory $script:BTemp } 'moved to \w{40} after it was checked.*Nothing was changed'
            G $script:BW rev-parse release/0.2.0 | Should -BeExactly $tip
            $dated = New-ReleaseDateCommit -RepoRoot $script:BW -Version '0.2.0' -Date '2026-11-02' -ExpectedTip $facts.LocalBranchCommit -TempDirectory $script:BTemp
            G $script:BW rev-parse release/0.2.0 | Should -BeExactly $dated
            G $script:BW rev-parse "$dated^" | Should -BeExactly $tip
            G $script:BW log -1 --format=%s $dated | Should -BeExactly 'Release 0.2.0: release date 2026-11-02'
            @(G $script:BW diff --name-only $tip $dated) | Should -BeExactly @('CHANGELOG.md')
            (Read-GitBlobText $script:BW "$($dated):CHANGELOG.md").Text | Should -Match '(?m)^## \[0\.2\.0\] - 2026-11-02$'
            @(G $script:BW status --porcelain --untracked-files=all) | Should -BeNullOrEmpty
            (States (Get-ReleaseFlowPlan -Facts (BFacts)))[0] | Should -BeExactly 'Branch=done'
        }
        finally { Remove-ReleaseBranch }
    }
    It 'accepts stabilization commits on the release branch and lists them' {
        $develop = G $script:BW rev-parse develop | Select-Object -First 1
        $null = New-ReleaseBranch -RepoRoot $script:BW -Version '0.2.0' -Date '2026-11-02' -BaseCommit $develop -TempDirectory $script:BTemp
        $null = G $script:BW switch --quiet release/0.2.0
        [System.IO.File]::WriteAllText((Join-Path $script:BW 'fix.txt'), 'fix')
        $null = G $script:BW add fix.txt
        $null = G $script:BW commit --quiet --message 'Fix the tray icon'
        $null = G $script:BW switch --quiet develop
        try {
            $plan = Get-ReleaseFlowPlan -Facts (BFacts)
            Assert-Problems (Select-NonToolchain $plan.Problems)
            $plan.Notes -join "`n" | Should -Match 'has 2 commit\(s\)'
            $plan.Notes -join "`n" | Should -Match 'Fix the tray icon'
        }
        finally { Remove-ReleaseBranch }
    }
    It 'refuses a stale release branch that develop has moved past' {
        $develop = G $script:BW rev-parse develop | Select-Object -First 1
        $null = New-ReleaseBranch -RepoRoot $script:BW -Version '0.2.0' -Date '2026-11-02' -BaseCommit $develop -TempDirectory $script:BTemp
        [System.IO.File]::WriteAllText((Join-Path $script:BW 'later.txt'), 'later')
        $null = G $script:BW add later.txt
        $null = G $script:BW commit --quiet --message 'Later on develop'
        $null = G $script:BW push --quiet origin develop
        try { Assert-Problems (Select-NonToolchain (Get-ReleaseFlowPlan -Facts (BFacts)).Problems) 'release/0\.2\.0 \(\w{7}\) is not based on the current develop \(\w{7}\).*Merge develop into release/0\.2\.0' }
        finally { Remove-ReleaseBranch }
    }
    It 'reads gh only' {
        @(Get-GhCall | Where-Object { $_ -notmatch '^(auth status|release view )' }) | Should -BeNullOrEmpty
    }
}

Describe 'scripts/release.ps1 end to end (stand-in build, fake GitHub)' {
    BeforeAll {
        $null = Use-GhLog 'ok'
        $script:GhState = Join-Path $script:TempRoot 'gh-state'
        $null = New-Item -ItemType Directory -Path $script:GhState
        $env:LAYERPEEK_FAKE_GH_STATE = $script:GhState
        # A stand-in build that writes valid output for the checked-out commit (one archive, its checksum file and the
        # build marker), as release-build.ps1 would, without dotnet.
        $build = @'
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools' 'ReleaseTools.psm1') -Force
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).ProviderPath
$version = Get-SourceVersion (Join-Path $root 'Directory.Build.props')
$os = Get-HostOs
$rid = @(Resolve-ReleaseRuntimes -HostOs $os)[0]
$releaseDir = Join-Path $root 'artifacts' 'release' $version
if (Test-Path -LiteralPath $releaseDir) { Remove-Item -LiteralPath $releaseDir -Recurse -Force }
$dist = Join-Path $releaseDir 'dist'
$null = New-Item -ItemType Directory -Path $dist
$name = Get-ArchiveFileName -Version $version -Runtime $rid
[System.IO.File]::WriteAllText((Join-Path $dist $name), "stand-in $version")
$sums = Get-ChecksumFileName $os
$null = Write-Sha256Sums -Directory $dist -FileName $sums -Files $name
[ordered]@{ Version = $version; Commit = (Get-GitHead $root); Dirty = $false; Os = $os; Runtimes = @($rid); ChecksumFile = $sums
    Files = @([ordered]@{ Name = $name; Runtime = $rid; Step = 'archive'; Sha256 = (Get-FileSha256 (Join-Path $dist $name)) }) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Get-BuildInfoPath $releaseDir) -Encoding utf8NoBOM
Write-Host 'stand-in build done'
exit 0
'@
        $script:E = New-ReleaseTestRepository -Name 'ok' -WithScripts -BuildScript $build
        $script:EScript = Join-Path $script:E.Work 'scripts' 'release.ps1'
        function Invoke-E([string[]] $Arguments, [string[]] $Answers) {
            $output = if ($Answers) { $Answers | Invoke-Pwsh -NoProfile -File $script:EScript @Arguments 2>&1 | Out-String }
            else { Invoke-Pwsh -NoProfile -NonInteractive -File $script:EScript @Arguments 2>&1 | Out-String }
            [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
        }
    }
    AfterAll { $env:LAYERPEEK_FAKE_GH_STATE = $null }

    It 'releases up to the draft after the three confirmations, ends on develop, and a re-run finds every step done' {
        $result = Invoke-E @('-Version', '0.2.0') @('0.2.0', 'y', 'y')
        $result.ExitCode | Should -Be 0 -Because $result.Output
        $result.Output | Should -Match 'stand-in build done'
        $result.Output | Should -Match 'Draft release v0\.2\.0 is ready: https://example\.invalid/test-owner/ok/releases/tag/v0\.2\.0'
        $result.Output | Should -Match 'release-publish\.ps1 -AllowIncomplete'
        # T-113: the next steps name the OS of this machine (either OS can go first) and how the other one follows.
        $this, $other = if ($IsWindows) { 'Windows', 'macOS' } else { 'macOS', 'Windows' }
        $result.Output | Should -Match "only the $this files so far"
        $result.Output | Should -Match "The $other files follow on a $other machine"
        $result.Output | Should -Match 'release-upload\.ps1 -AddToPublished'
        G $script:E.Work symbolic-ref --short HEAD | Should -BeExactly 'develop'
        @(G $script:E.Work status --porcelain --untracked-files=all) | Should -BeNullOrEmpty

        # GitHub (fake): a draft pre-release with the archive and the checksum file of this OS.
        $draft = Get-Content -LiteralPath (Join-Path $script:GhState 'test-owner_ok_v0.2.0.json') -Raw | ConvertFrom-Json
        $draft.isDraft | Should -BeTrue
        $draft.isPrerelease | Should -BeTrue
        @($draft.assets.name | Sort-Object) | Should -BeExactly @(@("LayerPeek-0.2.0-$(@(Resolve-ReleaseRuntimes -HostOs (Get-HostOs))[0]).$(if ($IsWindows) { 'zip' } else { 'tar.gz' })", (Get-ChecksumFileName (Get-HostOs))) | Sort-Object)
        $calls = Get-GhCall
        @($calls | Where-Object { $_ -match '^release create v0\.2\.0 --repo test-owner/ok --draft --verify-tag' }).Count | Should -Be 1
        @($calls | Where-Object { $_ -match '^release (edit|delete)' }) | Should -BeNullOrEmpty
        # origin: main and the tag on the release merge, develop with the merge back.
        $tag = G $script:E.Work rev-parse 'v0.2.0^{commit}' | Select-Object -First 1
        G $script:E.Origin rev-parse main | Should -BeExactly $tag
        G $script:E.Origin log -1 --format=%s develop | Should -BeExactly "Merge branch 'release/0.2.0' into develop"

        $dry = Invoke-E @('-Version', '0.2.0', '-DryRun')
        $dry.ExitCode | Should -Be 0 -Because $dry.Output
        foreach ($n in 1..8) { $dry.Output | Should -Match "\[done\] $n\. " }
        $dry.Output | Should -Not -Match '\[todo\]'

        $originRefs = Get-AllRefs $script:E.Origin
        $callCount = @(Get-GhCall).Count
        $again = Invoke-E @('-Version', '0.2.0')
        $again.ExitCode | Should -Be 0 -Because $again.Output
        $again.Output | Should -Match 'Draft release v0\.2\.0 is ready'
        Get-AllRefs $script:E.Origin | Should -BeExactly $originRefs
        @(Get-GhCall | Select-Object -Skip $callCount | Where-Object { $_ -notmatch '^(auth status|release view )' }) | Should -BeNullOrEmpty
        G $script:E.Work symbolic-ref --short HEAD | Should -BeExactly 'develop'
    }
}

Describe 'scripts/release-upload.ps1 -AddToPublished: release on two machines (stand-in builds, fake GitHub, T-113)' {
    BeforeAll {
        $null = Use-GhLog 'two'
        $script:TwoState = Join-Path $script:TempRoot 'gh-state-two'
        $null = New-Item -ItemType Directory -Path $script:TwoState
        $env:LAYERPEEK_FAKE_GH_STATE = $script:TwoState
        # The released commit v0.1.0 of the test repository: tagged, on origin/main, clean, dated changelog section.
        $script:T = New-ReleaseTestRepository -Name 'two' -WithScripts
        $null = G $script:T.Work switch --quiet --detach v0.1.0
        $script:TwoRelease = Join-Path $script:TwoState 'test-owner_two_v0.1.0.json'

        function Write-StandInBuild([string] $Os, [string] $Content = 'stand-in') {
            # The output release-build.ps1 would write for HEAD on that OS (the other OS too: the marker names its OS),
            # with the archive, the Velopack files and the checksum file.
            $root = $script:T.Work
            $rid = if ($Os -eq 'windows') { 'win-x64' } else { 'osx-arm64' }
            $releaseDir = Join-Path $root 'artifacts' 'release' '0.1.0'
            if (Test-Path -LiteralPath $releaseDir) { Remove-Item -LiteralPath $releaseDir -Recurse -Force }
            $dist = Join-Path $releaseDir 'dist'
            $null = New-Item -ItemType Directory -Path $dist
            $names = @(Get-ArchiveFileName -Version '0.1.0' -Runtime $rid) + @((Get-VelopackOutputMap -Version '0.1.0' -Runtime $rid).Values)
            if ($Os -eq 'macos') { $names += (Get-MacReleaseFileNames -Version '0.1.0' -Runtime $rid).DiskImage }
            $names = @($names | Sort-Object -Unique)
            foreach ($name in $names) { [System.IO.File]::WriteAllText((Join-Path $dist $name), "$Content $name") }
            $sums = Get-ChecksumFileName $Os
            $null = Write-Sha256Sums -Directory $dist -FileName $sums -Files $names
            [ordered]@{ Version = '0.1.0'; Commit = (Get-GitHead $root); Dirty = $false; Os = $Os; Runtimes = @($rid); ChecksumFile = $sums
                Files = @($names | ForEach-Object { [ordered]@{ Name = $_; Runtime = $rid; Step = 'stand-in'; Sha256 = (Get-FileSha256 (Join-Path $dist $_)) } }) } |
                ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Get-BuildInfoPath $releaseDir) -Encoding utf8NoBOM
            $names + $sums
        }
        function Invoke-T([string] $Name, [string[]] $Arguments = @()) {
            $output = Invoke-Pwsh -NoProfile -NonInteractive -File (Join-Path $script:T.Work 'scripts' $Name) @Arguments 2>&1 | Out-String
            [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
        }
        function Get-TwoRelease { Get-Content -LiteralPath $script:TwoRelease -Raw | ConvertFrom-Json }
        function Set-TwoRelease([scriptblock] $Edit) {
            $release = Get-Content -LiteralPath $script:TwoRelease -Raw | ConvertFrom-Json -AsHashtable
            & $Edit $release
            $release | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $script:TwoRelease
        }
        function Get-NewGhCall([int] $Skip) { @(Get-GhCall | Select-Object -Skip $Skip) }
    }
    AfterAll { $env:LAYERPEEK_FAKE_GH_STATE = $null }

    It 'refuses -AddToPublished before anything is uploaded (no release yet)' {
        $null = Write-StandInBuild 'macos'
        $result = Invoke-T 'release-upload.ps1' @('-AddToPublished')
        $result.ExitCode | Should -Be 1 -Because $result.Output
        $result.Output | Should -Match 'There is no release for v0\.1\.0\. -AddToPublished only adds files to a published release'
        Test-Path -LiteralPath $script:TwoRelease | Should -BeFalse
    }
    It 'first machine: uploads its OS into a new draft, refuses -AddToPublished into the draft and publishes with -AllowIncomplete' {
        $macFiles = Write-StandInBuild 'macos'
        $upload = Invoke-T 'release-upload.ps1'
        $upload.ExitCode | Should -Be 0 -Because $upload.Output
        $upload.Output | Should -Match 'add the other OS later with -AddToPublished'

        $draft = Invoke-T 'release-upload.ps1' @('-AddToPublished')
        $draft.ExitCode | Should -Be 1 -Because $draft.Output
        $draft.Output | Should -Match 'Release v0\.1\.0 is still a draft\. Upload into the draft without -AddToPublished'

        $complete = Invoke-T 'release-publish.ps1'
        $complete.ExitCode | Should -Be 1 -Because $complete.Output
        $complete.Output | Should -Match 'has no SHA256SUMS-windows\.txt'
        $publish = Invoke-T 'release-publish.ps1' @('-AllowIncomplete')
        $publish.ExitCode | Should -Be 0 -Because $publish.Output
        $release = Get-TwoRelease
        $release.isDraft | Should -BeFalse
        $release.isPrerelease | Should -BeTrue
        @($release.assets.name | Sort-Object) | Should -BeExactly @($macFiles | Sort-Object)
        $script:PublishedBody = $release.body
        $script:PublishedBody | Should -Match 'First release'
    }
    It 'second machine: without -AddToPublished the upload into the published release is refused as before, with a hint' {
        $null = Write-StandInBuild 'windows'
        $before = [System.IO.File]::ReadAllText($script:TwoRelease)
        $calls = @(Get-GhCall).Count
        $result = Invoke-T 'release-upload.ps1'
        $result.ExitCode | Should -Be 1 -Because $result.Output
        $result.Output | Should -Match 'Release v0\.1\.0 is already published \(https://example\.invalid/test-owner/two/releases/tag/v0\.1\.0\)\. Files are uploaded only into a draft release\.'
        $result.Output | Should -Match 'pwsh scripts/release-upload\.ps1 -AddToPublished'
        [System.IO.File]::ReadAllText($script:TwoRelease) | Should -BeExactly $before
        @(Get-NewGhCall $calls | Where-Object { $_ -notmatch '^(auth status|release view )' }) | Should -BeNullOrEmpty
    }
    It 'refuses -AddToPublished together with -Force without asking GitHub' {
        $calls = @(Get-GhCall).Count
        $result = Invoke-T 'release-upload.ps1' @('-AddToPublished', '-Force')
        $result.ExitCode | Should -Be 1 -Because $result.Output
        $result.Output | Should -Match 'cannot be combined with -Force'
        @(Get-NewGhCall $calls) | Should -BeNullOrEmpty
    }
    It 'keeps every other check: a dirty tree is refused with -AddToPublished too' {
        $extra = Join-Path $script:T.Work 'extra.txt'
        [System.IO.File]::WriteAllText($extra, 'x')
        try {
            $calls = @(Get-GhCall).Count
            $result = Invoke-T 'release-upload.ps1' @('-AddToPublished')
            $result.ExitCode | Should -Be 1 -Because $result.Output
            $result.Output | Should -Match 'not clean'
            @(Get-NewGhCall $calls | Where-Object { $_ -match '^release upload' }) | Should -BeNullOrEmpty
        }
        finally { Remove-Item -LiteralPath $extra }
    }
    It 'dry run: lists the files of the missing OS and changes nothing' {
        $before = [System.IO.File]::ReadAllText($script:TwoRelease)
        $calls = @(Get-GhCall).Count
        $result = Invoke-T 'release-upload.ps1' @('-AddToPublished', '-DryRun')
        $result.ExitCode | Should -Be 0 -Because $result.Output
        $result.Output | Should -Match 'Would add the windows files to the published release v0\.1\.0'
        $result.Output | Should -Match 'SHA256SUMS-windows\.txt\s+upload'
        $result.Output | Should -Match 'Dry run: nothing was changed on GitHub'
        [System.IO.File]::ReadAllText($script:TwoRelease) | Should -BeExactly $before
        @(Get-NewGhCall $calls | Where-Object { $_ -notmatch '^(auth status|release view )' }) | Should -BeNullOrEmpty
    }
    It 'adds the missing OS: packages first, checksum file and feed last, text and flags unchanged, and checks the whole release' {
        $winFiles = Write-StandInBuild 'windows'
        $macNames = @((Get-TwoRelease).assets.name)
        $calls = @(Get-GhCall).Count
        $result = Invoke-T 'release-upload.ps1' @('-AddToPublished')
        $result.ExitCode | Should -Be 0 -Because $result.Output
        $result.Output | Should -Match 'The windows files are added to the published release v0\.1\.0; the release is complete'

        $release = Get-TwoRelease
        $release.isDraft | Should -BeFalse
        $release.isPrerelease | Should -BeTrue
        $release.name | Should -BeExactly 'LayerPeek 0.1.0'
        $release.body | Should -BeExactly $script:PublishedBody
        @($release.assets.name | Sort-Object) | Should -BeExactly @(@($macNames) + @($winFiles) | Sort-Object)

        $new = @(Get-NewGhCall $calls | Where-Object { $_ -notmatch '^(auth status|release view |release download )' })
        $new.Count | Should -Be 2 -Because ($new -join ' / ')
        $new[0] | Should -Match '^release upload v0\.1\.0 --repo test-owner/two '
        $new[0] | Should -Match 'LayerPeek-0\.1\.0-win-x64\.zip'
        $new[0] | Should -Match 'LayerPeek-0\.1\.0-win-x64-full\.nupkg'
        $new[0] | Should -Not -Match 'SHA256SUMS|releases\.win-x64\.json|assets\.win-x64\.json|RELEASES-win-x64'
        $new[1] | Should -Match '^release upload v0\.1\.0 --repo test-owner/two '
        foreach ($marker in 'SHA256SUMS-windows.txt', 'releases.win-x64.json', 'assets.win-x64.json', 'RELEASES-win-x64') { $new[1] | Should -Match ([regex]::Escape($marker)) }
        $new[1] | Should -Not -Match 'win-x64\.zip|full\.nupkg|Setup\.exe'
        @(Get-GhCall | Where-Object { $_ -match '--clobber' }) | Should -BeNullOrEmpty
    }
    It 'refuses the OS whose checksum file is already in the release and changes nothing' {
        $before = [System.IO.File]::ReadAllText($script:TwoRelease)
        $calls = @(Get-GhCall).Count
        $result = Invoke-T 'release-upload.ps1' @('-AddToPublished')
        $result.ExitCode | Should -Be 1 -Because $result.Output
        $result.Output | Should -Match 'Published release v0\.1\.0 already contains SHA256SUMS-windows\.txt'
        [System.IO.File]::ReadAllText($script:TwoRelease) | Should -BeExactly $before
        @(Get-NewGhCall $calls | Where-Object { $_ -match '^release upload' }) | Should -BeNullOrEmpty

        # The first OS as well: its checksum file is there from the start.
        $null = Write-StandInBuild 'macos'
        $mac = Invoke-T 'release-upload.ps1' @('-AddToPublished')
        $mac.ExitCode | Should -Be 1 -Because $mac.Output
        $mac.Output | Should -Match 'already contains SHA256SUMS-macos\.txt'
        [System.IO.File]::ReadAllText($script:TwoRelease) | Should -BeExactly $before
    }
    It 'resumes an interrupted upload: identical files are skipped, only the missing ones are uploaded' {
        $null = Write-StandInBuild 'windows'
        Set-TwoRelease { param($r) $r.assets = @($r.assets | Where-Object { $_.name -notin 'SHA256SUMS-windows.txt', 'releases.win-x64.json' }) }
        $calls = @(Get-GhCall).Count
        $result = Invoke-T 'release-upload.ps1' @('-AddToPublished')
        $result.ExitCode | Should -Be 0 -Because $result.Output
        $result.Output | Should -Match 'LayerPeek-0\.1\.0-win-x64\.zip\s+skip, identical file already uploaded'
        $new = @(Get-NewGhCall $calls | Where-Object { $_ -match '^release upload' })
        $new.Count | Should -Be 1 -Because ($new -join ' / ')
        $new[0] | Should -Match 'SHA256SUMS-windows\.txt'
        $new[0] | Should -Match 'releases\.win-x64\.json'
        $new[0] | Should -Not -Match 'win-x64\.zip|assets\.win-x64\.json'
    }
    It 'never overwrites a file with other content or an unfinished upload, and says what to do' -ForEach @(
        @{ Case = 'other content'; Change = { param($a) $a.digest = "sha256:$('0' * 64)" }; Pattern = 'LayerPeek-0\.1\.0-win-x64\.zip is in release v0\.1\.0 with different content.*Nothing is overwritten' }
        @{ Case = 'unfinished'; Change = { param($a) $a.state = 'starter'; $a.Remove('digest') }; Pattern = 'LayerPeek-0\.1\.0-win-x64\.zip is in release v0\.1\.0, but its upload did not finish \(state starter, no SHA256\)\. Nothing is overwritten' }
    ) {
        $null = Write-StandInBuild 'windows'
        $assetChange = $Change
        Set-TwoRelease { param($r)
            $r.assets = @($r.assets | Where-Object { $_.name -cne 'SHA256SUMS-windows.txt' })
            foreach ($asset in @($r.assets | Where-Object { $_.name -ceq 'LayerPeek-0.1.0-win-x64.zip' })) { & $assetChange $asset }
        }
        $before = [System.IO.File]::ReadAllText($script:TwoRelease)
        $calls = @(Get-GhCall).Count
        $result = Invoke-T 'release-upload.ps1' @('-AddToPublished')
        $result.ExitCode | Should -Be 1 -Because $result.Output
        $result.Output | Should -Match $Pattern
        [System.IO.File]::ReadAllText($script:TwoRelease) | Should -BeExactly $before
        @(Get-NewGhCall $calls | Where-Object { $_ -match '^release upload' }) | Should -BeNullOrEmpty
    }
}

Describe 'Script help of release.ps1' {
    It 'Get-Help reads the synopsis and the parameters' {
        $help = Get-Help (Join-Path $script:RepoRoot 'scripts' 'release.ps1')
        "$($help.Synopsis)" | Should -Match 'DRAFT'
        @($help.parameters.parameter | ForEach-Object Name) | Should -Contain 'DryRun'
    }
}
