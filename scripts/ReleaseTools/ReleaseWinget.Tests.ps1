<#
.SYNOPSIS
    Pester 5+ tests of the winget submission (T-059): the manifest templates in build/winget, the pure decisions of
    ReleaseTools.psm1 and Invoke-WingetRelease / scripts/release-winget.ps1 against fake gh, wingetcreate and winget.

.DESCRIPTION
    No network: gh, wingetcreate and winget are fakes first on PATH (a shim that starts one pwsh implementation). They
    log every call (arguments and a SHA256 of WINGET_CREATE_GITHUB_TOKEN as the tool saw it); the fake wingetcreate also
    prints the token it got on stdout and stderr, so that the tests prove the output is redacted. The real tools are
    never called. Every run checks that the token never appears in the output, in the logged arguments or in the
    environment after the call.

    Temporary files live in one folder under $env:LAYERPEEK_PESTER_TESTS_TEMP (set by the xUnit wrapper
    tests/LayerPeek.Scripts.Tests, which also deletes it after a crash) or under the system temp folder.
    AfterAll deletes the folder and restores the environment.

.EXAMPLE
    Invoke-Pester scripts/ReleaseTools/ReleaseWinget.Tests.ps1 -Output Detailed
#>
#Requires -Version 7.2

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1') -Force

    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).ProviderPath
    $base = if ($env:LAYERPEEK_PESTER_TESTS_TEMP) { $env:LAYERPEEK_PESTER_TESTS_TEMP } else { [System.IO.Path]::GetTempPath() }
    $script:TempRoot = Join-Path $base "layerpeek-wg-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    $null = New-Item -ItemType Directory -Path $script:TempRoot

    # One implementation of the three fake tools; the first argument is the tool name.
    $fakeDir = Join-Path $script:TempRoot 'bin'
    $implDir = Join-Path $script:TempRoot 'impl'
    $null = New-Item -ItemType Directory -Path $fakeDir, $implDir
    $impl = Join-Path $implDir 'fake.ps1'
    [System.IO.File]::WriteAllText($impl, @'
$tool = $args[0]
$a = @($args | Select-Object -Skip 1)
$token = $env:WINGET_CREATE_GITHUB_TOKEN
$tokenHash = if ($token) { [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($token))) } else { 'none' }
Add-Content -LiteralPath $env:LAYERPEEK_FAKE_LOG -Value "$tool $($a -join ' ') | token=$tokenHash"
switch ($tool) {
    'gh' {
        if ($a[0] -eq 'auth' -and $a[1] -eq 'status') { exit 0 }
        if ($a[0] -eq 'auth' -and $a[1] -eq 'token') {
            if ($env:LAYERPEEK_FAKE_GH_TOKEN) { [Console]::Out.WriteLine($env:LAYERPEEK_FAKE_GH_TOKEN); exit 0 }
            [Console]::Error.WriteLine('no oauth token found for github.com'); exit 1
        }
        if ($a[0] -eq 'release' -and $a[1] -eq 'view') {
            if ($env:LAYERPEEK_FAKE_RELEASE -and (Test-Path -LiteralPath $env:LAYERPEEK_FAKE_RELEASE)) { [Console]::Out.Write([System.IO.File]::ReadAllText($env:LAYERPEEK_FAKE_RELEASE)); exit 0 }
            [Console]::Error.WriteLine('release not found'); exit 1
        }
        if ($a[0] -eq 'api') {
            switch ($env:LAYERPEEK_FAKE_PACKAGE) {
                'exists' { exit 0 }
                'missing' { [Console]::Error.WriteLine('gh: Not Found (HTTP 404)'); exit 1 }
                default { [Console]::Error.WriteLine('error connecting to api.github.com'); exit 1 }
            }
        }
        if ($a[0] -eq 'pr' -and $a[1] -eq 'list') { [Console]::Out.WriteLine($(if ($env:LAYERPEEK_FAKE_PRS) { $env:LAYERPEEK_FAKE_PRS } else { '[]' })); exit 0 }
        [Console]::Error.WriteLine("fake gh: unsupported $($a -join ' ')"); exit 99
    }
    'wingetcreate' {
        if ($a[0] -eq 'submit') {
            $folder = $a[-1]
            foreach ($file in @(Get-ChildItem -LiteralPath $folder -File | Sort-Object Name)) {
                Add-Content -LiteralPath $env:LAYERPEEK_FAKE_LOG -Value "  file $($file.Name)"
            }
            $sha = Select-String -LiteralPath (Join-Path $folder '3DPrint.LayerPeek.installer.yaml') -Pattern 'InstallerSha256: (\S+)'
            Add-Content -LiteralPath $env:LAYERPEEK_FAKE_LOG -Value "  sha $($sha.Matches[0].Groups[1].Value)"
        }
        [Console]::Out.WriteLine("fake wingetcreate: using the token $token")
        [Console]::Error.WriteLine("fake wingetcreate (stderr): token=$token")
        exit $(if ($env:LAYERPEEK_FAKE_WINGETCREATE_EXIT) { [int]$env:LAYERPEEK_FAKE_WINGETCREATE_EXIT } else { 0 })
    }
    'winget' {
        [Console]::Out.WriteLine('Manifest validation succeeded.')
        exit $(if ($env:LAYERPEEK_FAKE_WINGET_EXIT) { [int]$env:LAYERPEEK_FAKE_WINGET_EXIT } else { 0 })
    }
}
'@)
    $pwshCommand = (Get-PwshCommand | ForEach-Object { "`"$_`"" }) -join ' '
    foreach ($tool in 'gh', 'wingetcreate', 'winget') {
        if ($IsWindows) {
            $shim = "@echo off`r`n$pwshCommand -NoProfile -NonInteractive -File `"$impl`" $tool %*`r`nexit /b %ERRORLEVEL%`r`n"
            [System.IO.File]::WriteAllText((Join-Path $fakeDir "$tool.cmd"), $shim)
        }
        else {
            $path = Join-Path $fakeDir $tool
            [System.IO.File]::WriteAllText($path, "#!/bin/sh`nexec $pwshCommand -NoProfile -NonInteractive -File `"$impl`" $tool `"`$@`"`n")
            & chmod +x $path
        }
    }

    $script:Token = 'gho_FakeTokenForTheWingetTests0123456789'
    $script:Log = Join-Path $script:TempRoot 'calls.log'
    $script:ReleaseFile = Join-Path $script:TempRoot 'release.json'
    $script:Environment = @{
        LAYERPEEK_FAKE_LOG             = $script:Log
        LAYERPEEK_FAKE_GH_TOKEN        = $script:Token
        LAYERPEEK_FAKE_RELEASE         = $script:ReleaseFile
        LAYERPEEK_FAKE_PACKAGE         = 'exists'
        LAYERPEEK_FAKE_PRS             = $null
        LAYERPEEK_FAKE_WINGETCREATE_EXIT = $null
        LAYERPEEK_FAKE_WINGET_EXIT     = $null
        # An earlier value must be restored after the call (and must not reach the tool: the gh token replaces it).
        WINGET_CREATE_GITHUB_TOKEN     = 'value-before-the-test'
        GH_TOKEN                       = $null
        PATH                           = "$fakeDir$([System.IO.Path]::PathSeparator)$env:PATH"
    }
    $script:SavedEnvironment = @{}
    foreach ($name in $script:Environment.Keys) {
        $script:SavedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $script:Environment[$name])
    }

    $script:Sha256 = 'a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90'
    function New-FakeRelease {
        param([string] $Version = '1.0.0', [switch] $Draft, [switch] $NoSetup, [string] $State = 'uploaded', [string] $Digest = "sha256:$script:Sha256")
        # pscustomobject, as ConvertFrom-Json returns it for gh release view.
        $assets = @([pscustomobject]@{ name = "LayerPeek-$Version-osx-arm64.dmg"; state = 'uploaded'; digest = "sha256:$('0' * 64)" })
        if (-not $NoSetup) { $assets += [pscustomobject]@{ name = "LayerPeek-$Version-win-x64-Setup.exe"; state = $State; digest = $Digest } }
        [pscustomobject]@{
            tagName = "v$Version"; name = "LayerPeek $Version"; isDraft = [bool]$Draft; isPrerelease = $false
            url = "https://example.invalid/pfoltynek/LayerPeek/releases/tag/v$Version"; publishedAt = '2026-11-12T21:30:00Z'; assets = $assets
        }
    }
    function Set-FakeRelease($Release) { [System.IO.File]::WriteAllText($script:ReleaseFile, ($Release | ConvertTo-Json -Depth 5)) }
    function Reset-Fakes {
        Set-FakeRelease (New-FakeRelease)
        if (Test-Path -LiteralPath $script:Log) { Remove-Item -LiteralPath $script:Log }
        $env:LAYERPEEK_FAKE_PACKAGE = 'exists'
        $env:LAYERPEEK_FAKE_PRS = $null
        $env:LAYERPEEK_FAKE_GH_TOKEN = $script:Token
        $env:LAYERPEEK_FAKE_WINGETCREATE_EXIT = $null
        $env:LAYERPEEK_FAKE_WINGET_EXIT = $null
        $env:WINGET_CREATE_GITHUB_TOKEN = 'value-before-the-test'
    }
    function Get-Calls { if (Test-Path -LiteralPath $script:Log) { @(Get-Content -LiteralPath $script:Log | Where-Object { $_ }) } else { @() } }
    function Get-TokenHash([string] $Value) { [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($Value))) }

    function Invoke-Step {
        # Invoke-WingetRelease with its output (all streams) as text; Result is the returned bool.
        param([hashtable] $Parameters, [string] $Answer = 'y')
        $answerBlock = [scriptblock]::Create("'$Answer'")
        $records = @(Invoke-WingetRelease -RepoRoot $script:RepoRoot -Version '1.0.0' -HostOs 'windows' -Confirm $answerBlock @Parameters *>&1)
        $results = @($records | Where-Object { $_ -is [bool] })
        [pscustomobject]@{
            Result = if ($results.Count -gt 0) { $results[-1] } else { $null }
            Output = (@($records | Where-Object { $_ -isnot [bool] } | ForEach-Object { "$_" }) -join "`n")
        }
    }
    function Assert-NoTokenLeak([string] $Output) {
        $Output | Should -Not -Match ([regex]::Escape($script:Token))
        (Get-Calls) -join "`n" | Should -Not -Match ([regex]::Escape($script:Token))
        $env:WINGET_CREATE_GITHUB_TOKEN | Should -Be 'value-before-the-test'
    }

    function Get-TopLevelValues([string] $Text) {
        # The top-level "Key: value" pairs of a manifest (enough for these flat files; lists and blocks are skipped).
        $values = [ordered]@{}
        foreach ($line in ($Text -split "`n")) { if ($line -match '^([A-Za-z]+): (.+)$') { $values[$Matches[1]] = $Matches[2] } }
        $values
    }
    $script:Templates = Read-WingetTemplates -Directory (Join-Path $script:RepoRoot 'build' 'winget')
    $script:Manifests = ConvertTo-WingetManifests -Templates $script:Templates -Version '1.0.0' -ReleaseDate '2026-11-12' -InstallerSha256 $script:Sha256
}

AfterAll {
    foreach ($name in $script:SavedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $script:SavedEnvironment[$name]) }
    if ($script:TempRoot -and (Test-Path -LiteralPath $script:TempRoot)) { Remove-Item -LiteralPath $script:TempRoot -Recurse -Force }
}

Describe 'Manifest templates (build/winget)' {
    It 'are exactly the three files of a multi-file manifest, UTF-8 without BOM, with the 1.28.0 schema header' {
        @($script:Templates.Keys | Sort-Object) | Should -BeExactly @('3DPrint.LayerPeek.installer.yaml', '3DPrint.LayerPeek.locale.en-US.yaml', '3DPrint.LayerPeek.yaml')
        $types = @{ '3DPrint.LayerPeek.yaml' = 'version'; '3DPrint.LayerPeek.installer.yaml' = 'installer'; '3DPrint.LayerPeek.locale.en-US.yaml' = 'defaultLocale' }
        foreach ($name in $types.Keys) {
            $bytes = [System.IO.File]::ReadAllBytes((Join-Path $script:RepoRoot 'build' 'winget' $name))
            ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) | Should -BeFalse -Because "$name has no BOM"
            $script:Manifests[$name] | Should -Match "(?m)^# yaml-language-server: \`$schema=https://aka\.ms/winget-manifest\.$($types[$name])\.1\.28\.0\.schema\.json$"
            $values = Get-TopLevelValues $script:Manifests[$name]
            $values.ManifestType | Should -BeExactly $types[$name]
            $values.ManifestVersion | Should -BeExactly '1.28.0'
            $values.PackageIdentifier | Should -BeExactly '3DPrint.LayerPeek'
            $values.PackageVersion | Should -BeExactly '1.0.0'
            $script:Manifests[$name] | Should -Not -Match '@[A-Z0-9_]+@'
        }
    }
    It 'match the Velopack installer: publisher, name and ARP key as Velopack writes them into Apps & features' {
        $velopack = Get-VelopackSettings
        $locale = Get-TopLevelValues $script:Manifests['3DPrint.LayerPeek.locale.en-US.yaml']
        $installer = Get-TopLevelValues $script:Manifests['3DPrint.LayerPeek.installer.yaml']
        # Velopack 1.2.161 writes Publisher = --packAuthors, DisplayName = --packTitle, key = --packId (registry.rs).
        $locale.Publisher | Should -BeExactly $velopack.Authors
        $locale.Publisher | Should -BeExactly 'Petr Foltýnek'
        $locale.PackageName | Should -BeExactly $velopack.Title
        $installer.ProductCode | Should -BeExactly $velopack.PackId
    }
    It 'describe a silent per-user x64 exe installer from the version-specific release URL' {
        $text = $script:Manifests['3DPrint.LayerPeek.installer.yaml']
        $values = Get-TopLevelValues $text
        $values.InstallerType | Should -BeExactly 'exe'
        $values.Scope | Should -BeExactly 'user'
        $values.ReleaseDate | Should -BeExactly '2026-11-12'
        $text | Should -Match '(?m)^  Silent: --silent$'
        $text | Should -Match '(?m)^  SilentWithProgress: --silent$'
        $text | Should -Match '(?m)^  Log: --log "<LOGPATH>"$'
        @([regex]::Matches($text, '(?m)^- Architecture: (\S+)$') | ForEach-Object { $_.Groups[1].Value }) | Should -BeExactly @('x64')
        $text | Should -Match "(?m)^  InstallerUrl: $([regex]::Escape((Get-WingetUrls -Version '1.0.0').Installer))$"
        (Get-WingetUrls -Version '1.0.0').Installer | Should -BeExactly 'https://github.com/pfoltynek/LayerPeek/releases/download/v1.0.0/LayerPeek-1.0.0-win-x64-Setup.exe'
        $text | Should -Match "(?m)^  InstallerSha256: $($script:Sha256.ToUpperInvariant())$"
    }
    It 'have the license, moniker and URLs decided for the package' {
        $text = $script:Manifests['3DPrint.LayerPeek.locale.en-US.yaml']
        $values = Get-TopLevelValues $text
        $values.PackageLocale | Should -BeExactly (Get-TopLevelValues $script:Manifests['3DPrint.LayerPeek.yaml']).DefaultLocale
        $values.PackageLocale | Should -BeExactly 'en-US'
        $values.License | Should -BeExactly 'GPL-3.0-only'
        $values.LicenseUrl | Should -BeExactly 'https://github.com/pfoltynek/LayerPeek/blob/v1.0.0/LICENSE'
        Test-Path -LiteralPath (Join-Path $script:RepoRoot 'LICENSE') | Should -BeTrue
        $values.ReleaseNotesUrl | Should -BeExactly (Get-WingetUrls -Version '1.0.0').ReleaseNotes
        $values.Moniker | Should -BeExactly 'layerpeek'
        $values.ShortDescription.Length | Should -BeLessOrEqual 256
        $tags = @([regex]::Matches(($text -split '(?m)^Tags:')[1], '(?m)^- (\S+)$') | ForEach-Object { $_.Groups[1].Value })
        $tags.Count | Should -BeGreaterThan 0
        $tags.Count | Should -BeLessOrEqual 16
        foreach ($tag in $tags) { $tag | Should -MatchExactly '^[a-z0-9-]+$' }
        $text | Should -Match 'not affiliated'
    }
    It 'refuse a malformed value, a missing template or an unknown placeholder' {
        { ConvertTo-WingetManifests -Templates $script:Templates -Version '1.0' -ReleaseDate '2026-11-12' -InstallerSha256 $script:Sha256 } | Should -Throw '*X.Y.Z*'
        { ConvertTo-WingetManifests -Templates $script:Templates -Version '1.0.0' -ReleaseDate '12.11.2026' -InstallerSha256 $script:Sha256 } | Should -Throw '*YYYY-MM-DD*'
        { ConvertTo-WingetManifests -Templates $script:Templates -Version '1.0.0' -ReleaseDate '2026-11-12' -InstallerSha256 'abc' } | Should -Throw '*SHA256*'
        $partial = @{} + $script:Templates; $partial.Remove('3DPrint.LayerPeek.yaml')
        { ConvertTo-WingetManifests -Templates $partial -Version '1.0.0' -ReleaseDate '2026-11-12' -InstallerSha256 $script:Sha256 } | Should -Throw '*missing: 3DPrint.LayerPeek.yaml*'
        $unknown = @{} + $script:Templates; $unknown['3DPrint.LayerPeek.yaml'] += "Extra: @OTHER@`n"
        { ConvertTo-WingetManifests -Templates $unknown -Version '1.0.0' -ReleaseDate '2026-11-12' -InstallerSha256 $script:Sha256 } | Should -Throw '*@OTHER@*'
    }
    It 'go into the winget-pkgs folder of the identifier' {
        Get-WingetManifestRelativePath | Should -BeExactly 'manifests/3/3DPrint/LayerPeek'
        Get-WingetManifestRelativePath -Version '1.0.0' | Should -BeExactly 'manifests/3/3DPrint/LayerPeek/1.0.0'
    }
}

Describe 'Decisions (pure)' {
    It 'submits stable versions of pfoltynek/LayerPeek on Windows only' {
        @(Get-WingetPreflightProblems -Version '1.0.0' -Repository 'pfoltynek/LayerPeek' -HostOs 'windows') | Should -BeNullOrEmpty
        @(Get-WingetPreflightProblems -Version '1.0.0' -HostOs 'windows') | Should -BeNullOrEmpty
        @(Get-WingetPreflightProblems -Version '0.6.0' -HostOs 'windows') | Should -Match 'pre-release \(0\.x\)'
        @(Get-WingetPreflightProblems -Version '1.0' -HostOs 'windows') | Should -Match 'not a version'
        @(Get-WingetPreflightProblems -Version '1.0.0' -Repository 'pfoltynek/LayerPeek-release-trial' -HostOs 'windows') | Should -Match 'trial repository never goes to winget'
        $mac = @(Get-WingetPreflightProblems -Version '1.0.0' -HostOs 'macos')
        $mac.Count | Should -Be 1
        $mac[0] | Should -Match 'runs on Windows only .*pwsh scripts/release-winget\.ps1 -Version 1\.0\.0'
        @(Get-WingetPreflightProblems -Version '1.0.0' -HostOs 'macos' -AnyOs) | Should -BeNullOrEmpty
    }
    It 'needs a published release with the completely uploaded installer and its SHA256' {
        @(Get-WingetReleaseProblems -Release $null -Version '1.0.0') | Should -Match 'no release v1\.0\.0'
        @(Get-WingetReleaseProblems -Release (New-FakeRelease -Draft) -Version '1.0.0') | Should -Match 'still a draft'
        @(Get-WingetReleaseProblems -Release (New-FakeRelease -NoSetup) -Version '1.0.0') | Should -Match 'no LayerPeek-1\.0\.0-win-x64-Setup\.exe yet.*-AddToPublished'
        @(Get-WingetReleaseProblems -Release (New-FakeRelease -State 'starter') -Version '1.0.0') | Should -Match 'not completely uploaded'
        @(Get-WingetReleaseProblems -Release (New-FakeRelease -Digest '') -Version '1.0.0') | Should -Match 'no SHA256'
        @(Get-WingetReleaseProblems -Release (New-FakeRelease) -Version '1.0.0') | Should -BeNullOrEmpty
        Get-WingetInstallerSha256 -Release (New-FakeRelease) -Version '1.0.0' | Should -BeExactly $script:Sha256.ToUpperInvariant()
    }
    It 'takes the release date from publishedAt (UTC), or the fallback' {
        Get-WingetReleaseDate -Release (New-FakeRelease) -Fallback '2000-01-01' | Should -BeExactly '2026-11-12'
        $late = New-FakeRelease; $late.publishedAt = [datetime]::new(2026, 11, 12, 23, 30, 0, [DateTimeKind]::Utc)
        Get-WingetReleaseDate -Release $late -Fallback '2000-01-01' | Should -BeExactly '2026-11-12'
        $none = New-FakeRelease; $none.publishedAt = $null
        Get-WingetReleaseDate -Release $none -Fallback '2000-01-01' | Should -BeExactly '2000-01-01'
    }
    It 'refuses a second pull request for the version, and a later version while the first one is still open' {
        $open = @([pscustomobject]@{ title = 'New package: 3DPrint.LayerPeek version 1.0.0'; url = 'https://example.invalid/pr/1' })
        @(Get-WingetPullRequestProblems -PullRequests $open -Version '1.0.0' -Mode 'new') | Should -Match 'already exists: https://example\.invalid/pr/1'
        @(Get-WingetPullRequestProblems -PullRequests $open -Version '1.0.1' -Mode 'new') | Should -Match 'still open.*Wait until it is merged'
        @(Get-WingetPullRequestProblems -PullRequests $open -Version '1.0.1' -Mode 'update') | Should -BeNullOrEmpty
        @(Get-WingetPullRequestProblems -PullRequests $open -Version '11.0.0' -Mode 'update') | Should -BeNullOrEmpty
        $other = @([pscustomobject]@{ title = 'New package: 3DPrint.LayerPeekPro version 1.0.0'; url = 'x' })
        @(Get-WingetPullRequestProblems -PullRequests $other -Version '1.0.0' -Mode 'new') | Should -BeNullOrEmpty
        @(Get-WingetPullRequestProblems -PullRequests @() -Version '1.0.0' -Mode 'new') | Should -BeNullOrEmpty
    }
    It 'builds the wingetcreate arguments without a token' {
        $update = Get-WingetCreateArguments -Mode 'update' -Version '1.0.1' -ReleaseDate '2026-12-01'
        $update | Should -BeExactly @(
            'update', '3DPrint.LayerPeek', '--version', '1.0.1',
            '--urls', 'https://github.com/pfoltynek/LayerPeek/releases/download/v1.0.1/LayerPeek-1.0.1-win-x64-Setup.exe',
            '--release-notes-url', 'https://github.com/pfoltynek/LayerPeek/releases/tag/v1.0.1',
            '--release-date', '2026-12-01', '--prtitle', 'Update: 3DPrint.LayerPeek to 1.0.1', '--submit')
        Get-WingetCreateArguments -Mode 'new' -Version '1.0.0' -ManifestDirectory 'C:\m' | Should -BeExactly @('submit', '--prtitle', 'New package: 3DPrint.LayerPeek version 1.0.0', 'C:\m')
        @(Get-WingetCreateArguments -Mode 'update' -Version '1.0.1' -ReleaseDate '2026-12-01') -join ' ' | Should -Not -Match '--token|-t '
        { Get-WingetCreateArguments -Mode 'new' -Version '1.0.0' } | Should -Throw
        { Get-WingetCreateArguments -Mode 'update' -Version '1.0.1' -ReleaseDate 'today' } | Should -Throw
    }
    It 'accepts classic and OAuth tokens and refuses a missing or fine-grained one without naming it' {
        @(Get-WingetTokenProblems -Token 'gho_abc') | Should -BeNullOrEmpty
        @(Get-WingetTokenProblems -Token 'ghp_abc') | Should -BeNullOrEmpty
        @(Get-WingetTokenProblems -Token '') | Should -Match 'returned no token'
        $fine = 'github_pat_11ABCDEFG_secretpart'
        $problems = @(Get-WingetTokenProblems -Token $fine)
        $problems | Should -Match 'fine-grained'
        $problems -join ' ' | Should -Not -Match 'secretpart'
        @(Get-WingetTokenProblems -Token "gho_a b") | Should -Match 'white space'
    }
    It 'redacts the token in tool output' {
        Protect-WingetOutput -Line 'x gho_secret y gho_secret' -Secret 'gho_secret' | Should -BeExactly 'x *** y ***'
        Protect-WingetOutput -Line 'nothing' -Secret '' | Should -BeExactly 'nothing'
    }
    It 'offers the submission only on Windows for a stable version whose release has the installer' {
        $setup = @('LayerPeek-1.0.0-win-x64-Setup.exe', 'SHA256SUMS-windows.txt')
        (Get-WingetOffer -Version '1.0.0' -Repository 'pfoltynek/LayerPeek' -HostOs 'windows' -AssetNames $setup).Ask | Should -BeTrue
        $pre = Get-WingetOffer -Version '0.6.0' -HostOs 'windows' -AssetNames @('LayerPeek-0.6.0-win-x64-Setup.exe')
        $pre.Ask | Should -BeFalse
        $pre.Lines | Should -Match 'pre-release, it is not submitted'
        $trial = Get-WingetOffer -Version '1.0.0' -Repository 'test-owner/two' -HostOs 'windows' -AssetNames $setup
        $trial.Ask | Should -BeFalse
        $trial.Lines | Should -Match 'nothing is submitted'
        $macOnly = Get-WingetOffer -Version '1.0.0' -HostOs 'macos' -AssetNames @('LayerPeek-1.0.0-osx-arm64.dmg')
        $macOnly.Ask | Should -BeFalse
        $macOnly.Lines | Should -Match 'no LayerPeek-1\.0\.0-win-x64-Setup\.exe yet.*-AddToPublished'
        $onMac = Get-WingetOffer -Version '1.0.0' -HostOs 'macos' -AssetNames $setup
        $onMac.Ask | Should -BeFalse
        $onMac.Lines | Should -Match 'on a Windows machine.*pwsh scripts/release-winget\.ps1 -Version 1\.0\.0'
    }
}

Describe 'Invoke-WingetRelease against fake gh, wingetcreate and winget' {
    BeforeEach { Reset-Fakes }

    It 'updates an existing package with wingetcreate update, the token only in the environment and redacted' {
        $run = Invoke-Step @{}
        $run.Result | Should -BeTrue -Because $run.Output
        $calls = Get-Calls
        $create = @($calls | Where-Object { $_ -like 'wingetcreate *' })
        $create.Count | Should -Be 1
        $create[0] | Should -BeLike 'wingetcreate update 3DPrint.LayerPeek --version 1.0.0 --urls https://github.com/pfoltynek/LayerPeek/releases/download/v1.0.0/LayerPeek-1.0.0-win-x64-Setup.exe *--release-date 2026-11-12 *--submit | token=*'
        # The tool got exactly the token of gh through WINGET_CREATE_GITHUB_TOKEN (not the earlier value).
        $create[0] | Should -BeLike "* | token=$(Get-TokenHash $script:Token)"
        # gh was asked for the token after the confirmation and never had it in its environment (only the earlier value).
        @($calls | Where-Object { $_ -like 'gh auth token*' }).Count | Should -Be 1
        @($calls | Where-Object { $_ -like 'gh *' -and $_ -notlike "* | token=$(Get-TokenHash 'value-before-the-test')" }) | Should -BeNullOrEmpty
        $run.Output | Should -Match 'fake wingetcreate: using the token \*\*\*'
        $run.Output | Should -Match 'fake wingetcreate \(stderr\): token=\*\*\*'
        $run.Output | Should -Match 'update of the package in microsoft/winget-pkgs'
        @($calls | Where-Object { $_ -like 'winget *' }) | Should -BeNullOrEmpty
        Assert-NoTokenLeak $run.Output
    }
    It 'submits the first version from the templates after winget validate, and deletes the temporary manifests' {
        $env:LAYERPEEK_FAKE_PACKAGE = 'missing'
        $run = Invoke-Step @{}
        $run.Result | Should -BeTrue -Because $run.Output
        $calls = Get-Calls
        $validate = @($calls | Where-Object { $_ -like 'winget validate --manifest *' })
        $validate.Count | Should -Be 1
        $validate[0] | Should -BeLike "* | token=$(Get-TokenHash 'value-before-the-test')"
        $submit = @($calls | Where-Object { $_ -like 'wingetcreate submit *' })
        $submit.Count | Should -Be 1
        $submit[0] | Should -BeLike "wingetcreate submit --prtitle New package: 3DPrint.LayerPeek version 1.0.0 *$([System.IO.Path]::DirectorySeparatorChar)3DPrint.LayerPeek$([System.IO.Path]::DirectorySeparatorChar)1.0.0 | token=$(Get-TokenHash $script:Token)"
        $calls | Should -Contain '  file 3DPrint.LayerPeek.installer.yaml'
        $calls | Should -Contain '  file 3DPrint.LayerPeek.locale.en-US.yaml'
        $calls | Should -Contain '  file 3DPrint.LayerPeek.yaml'
        $calls | Should -Contain "  sha $($script:Sha256.ToUpperInvariant())"
        $folder = ($submit[0] -replace '^wingetcreate submit --prtitle New package: 3DPrint\.LayerPeek version 1\.0\.0 ', '') -replace ' \| token=.*$', ''
        Test-Path -LiteralPath $folder | Should -BeFalse
        Assert-NoTokenLeak $run.Output
    }
    It 'does not read the token or submit anything without the confirmation' {
        $run = Invoke-Step @{} -Answer 'n'
        $run.Result | Should -BeFalse
        $run.Output | Should -Match 'not confirmed, nothing was submitted'
        @(Get-Calls | Where-Object { $_ -like 'gh auth token*' -or $_ -like 'wingetcreate *' -or $_ -like 'winget *' }) | Should -BeNullOrEmpty
    }
    It 'prints the plan with -DryRun and reads no token' {
        $run = Invoke-Step @{ DryRun = $true }
        $run.Result | Should -BeTrue
        $run.Output | Should -Match 'wingetcreate update 3DPrint\.LayerPeek --version 1\.0\.0'
        $run.Output | Should -Match 'Dry run: nothing was submitted'
        @(Get-Calls | Where-Object { $_ -like 'gh auth token*' -or $_ -like 'wingetcreate *' }) | Should -BeNullOrEmpty
    }
    It 'refuses a fine-grained token of gh without showing it' {
        $env:LAYERPEEK_FAKE_GH_TOKEN = 'github_pat_11FAKE_finegrainedsecret'
        $run = Invoke-Step @{}
        $run.Result | Should -BeFalse
        $run.Output | Should -Match 'fine-grained personal access token'
        $run.Output | Should -Not -Match 'finegrainedsecret'
        @(Get-Calls | Where-Object { $_ -like 'wingetcreate *' }) | Should -BeNullOrEmpty
        $env:WINGET_CREATE_GITHUB_TOKEN | Should -Be 'value-before-the-test'
    }
    It 'refuses when gh has no token' {
        $env:LAYERPEEK_FAKE_GH_TOKEN = $null
        $run = Invoke-Step @{}
        $run.Result | Should -BeFalse
        $run.Output | Should -Match 'returned no token'
        @(Get-Calls | Where-Object { $_ -like 'wingetcreate *' }) | Should -BeNullOrEmpty
    }
    It 'refuses an open pull request for the same version before asking' {
        $env:LAYERPEEK_FAKE_PRS = '[{"number":1,"title":"Update: 3DPrint.LayerPeek to 1.0.0","url":"https://example.invalid/pr/1"}]'
        $run = Invoke-Step @{} -Answer 'unexpected'
        $run.Result | Should -BeFalse
        $run.Output | Should -Match 'open pull request for 3DPrint\.LayerPeek 1\.0\.0 already exists'
        @(Get-Calls | Where-Object { $_ -like 'gh auth token*' -or $_ -like 'wingetcreate *' }) | Should -BeNullOrEmpty
    }
    It 'stops when GitHub cannot tell whether the package exists (never a second new-package pull request)' {
        $env:LAYERPEEK_FAKE_PACKAGE = 'offline'
        { Invoke-WingetRelease -RepoRoot $script:RepoRoot -Version '1.0.0' -HostOs 'windows' -Confirm { 'y' } 6>$null } | Should -Throw '*gh api repos/microsoft/winget-pkgs/contents/manifests/3/3DPrint/LayerPeek failed*'
        @(Get-Calls | Where-Object { $_ -like 'wingetcreate *' }) | Should -BeNullOrEmpty
    }
    It 'reports a failed wingetcreate and a failed winget validate' {
        $env:LAYERPEEK_FAKE_WINGETCREATE_EXIT = '3'
        $failed = Invoke-Step @{}
        $failed.Result | Should -BeFalse
        $failed.Output | Should -Match 'wingetcreate failed with exit code 3'
        Assert-NoTokenLeak $failed.Output

        Reset-Fakes
        $env:LAYERPEEK_FAKE_PACKAGE = 'missing'
        $env:LAYERPEEK_FAKE_WINGET_EXIT = '1'
        $invalid = Invoke-Step @{}
        $invalid.Result | Should -BeFalse
        $invalid.Output | Should -Match 'winget validate failed \(exit code 1\); nothing was submitted'
        @(Get-Calls | Where-Object { $_ -like 'wingetcreate *' }) | Should -BeNullOrEmpty
    }
    It 'refuses a release without the installer, a draft and a pre-release' {
        Set-FakeRelease (New-FakeRelease -NoSetup)
        (Invoke-Step @{}).Output | Should -Match 'no LayerPeek-1\.0\.0-win-x64-Setup\.exe yet'
        Set-FakeRelease (New-FakeRelease -Draft)
        (Invoke-Step @{}).Output | Should -Match 'still a draft'
        $pre = @(Invoke-WingetRelease -RepoRoot $script:RepoRoot -Version '0.6.0' -HostOs 'windows' -Confirm { 'y' } *>&1)
        $pre[-1] | Should -BeFalse
        @(Get-Calls | Where-Object { $_ -like 'wingetcreate *' -or $_ -like 'gh auth token*' }) | Should -BeNullOrEmpty
    }
    It 'refuses to submit on macOS, but writes the manifests for review there with -OutputDirectory' {
        $mac = @(Invoke-WingetRelease -RepoRoot $script:RepoRoot -Version '1.0.0' -HostOs 'macos' -Confirm { throw 'no question expected' } *>&1)
        $mac[-1] | Should -BeFalse
        (@($mac | Where-Object { $_ -isnot [bool] }) -join "`n") | Should -Match 'runs on Windows only'
        @(Get-Calls) | Should -BeNullOrEmpty

        $out = Join-Path $script:TempRoot 'review'
        $records = @(Invoke-WingetRelease -RepoRoot $script:RepoRoot -Version '1.0.0' -HostOs 'macos' -OutputDirectory $out -Confirm { throw 'no question expected' } *>&1)
        $records[-1] | Should -BeTrue
        @(Get-ChildItem -LiteralPath $out -File | ForEach-Object Name | Sort-Object) | Should -BeExactly @('3DPrint.LayerPeek.installer.yaml', '3DPrint.LayerPeek.locale.en-US.yaml', '3DPrint.LayerPeek.yaml')
        [System.IO.File]::ReadAllText((Join-Path $out '3DPrint.LayerPeek.installer.yaml')) | Should -Match "InstallerSha256: $($script:Sha256.ToUpperInvariant())"
        (@($records | Where-Object { $_ -isnot [bool] }) -join "`n") | Should -Match 'winget validate --manifest'
        @(Get-Calls | Where-Object { $_ -like 'gh auth token*' -or $_ -like 'wingetcreate *' -or $_ -like 'winget *' -or $_ -like 'gh api*' }) | Should -BeNullOrEmpty
        # Never into a folder that already has files.
        { Invoke-WingetRelease -RepoRoot $script:RepoRoot -Version '1.0.0' -HostOs 'macos' -OutputDirectory $out *>$null } | Should -Throw '*is not empty*'
    }
}

Describe 'The offer after publishing (Invoke-WingetReleaseOffer)' {
    BeforeEach { Reset-Fakes }

    It 'only prints a note for a pre-release, without asking or calling anything' {
        $text = @(Invoke-WingetReleaseOffer -RepoRoot $script:RepoRoot -Version '0.6.0' -HostOs 'windows' -AssetNames @('LayerPeek-0.6.0-win-x64-Setup.exe') -Confirm { throw 'no question expected' } *>&1) -join "`n"
        $text | Should -Match 'pre-release, it is not submitted'
        @(Get-Calls) | Should -BeNullOrEmpty
    }
    It 'asks on Windows and does nothing when the answer is no' {
        $script:Asked = $null
        $text = @(Invoke-WingetReleaseOffer -RepoRoot $script:RepoRoot -Version '1.0.0' -Repository 'pfoltynek/LayerPeek' -HostOs 'windows' -AssetNames @('LayerPeek-1.0.0-win-x64-Setup.exe') -Confirm { param($q) $script:Asked = $q; 'n' } *>&1) -join "`n"
        $script:Asked | Should -Match 'Submit LayerPeek 1\.0\.0 to winget'
        $text | Should -Match 'not submitted\. Later on Windows: pwsh scripts/release-winget\.ps1 -Version 1\.0\.0'
        @(Get-Calls) | Should -BeNullOrEmpty
    }
    It 'runs the submission after yes, and never throws when it fails' {
        $script:Questions = [System.Collections.Generic.List[string]]::new()
        $text = @(Invoke-WingetReleaseOffer -RepoRoot $script:RepoRoot -Version '1.0.0' -HostOs 'windows' -AssetNames @('LayerPeek-1.0.0-win-x64-Setup.exe') -Confirm { param($q) $script:Questions.Add($q); 'y' } *>&1) -join "`n"
        @(Get-Calls | Where-Object { $_ -like 'wingetcreate update *' }).Count | Should -Be 1
        # Asked twice: whether to submit at all, then once more after the plan was printed.
        $script:Questions.Count | Should -Be 2
        $script:Questions[1] | Should -Match 'Open the pull request in microsoft/winget-pkgs for 3DPrint\.LayerPeek 1\.0\.0'
        $text.IndexOf('wingetcreate update 3DPrint.LayerPeek', [StringComparison]::Ordinal) | Should -BeGreaterThan -1
        Assert-NoTokenLeak $text

        Reset-Fakes
        $env:LAYERPEEK_FAKE_PACKAGE = 'offline'
        $failed = @(Invoke-WingetReleaseOffer -RepoRoot $script:RepoRoot -Version '1.0.0' -HostOs 'windows' -AssetNames @('LayerPeek-1.0.0-win-x64-Setup.exe') -Confirm { 'y' } *>&1) -join "`n"
        $failed | Should -Match 'the submission FAILED'
        $failed | Should -Match 'Re-run it on Windows: pwsh scripts/release-winget\.ps1 -Version 1\.0\.0'
    }
}

Describe 'scripts/release-winget.ps1' {
    BeforeEach { Reset-Fakes }

    It 'refuses to run off Windows with a clear message and calls nothing' -Skip:$IsWindows {
        $output = Invoke-Pwsh -NoProfile -NonInteractive -File (Join-Path $script:RepoRoot 'scripts' 'release-winget.ps1') -Version '1.0.0' *>&1
        $LASTEXITCODE | Should -Be 1
        ($output -join "`n") | Should -Match 'runs on Windows only \(wingetcreate and winget are Windows tools\)'
        @(Get-Calls) | Should -BeNullOrEmpty
    }
    It 'writes the manifests of a published release with -OutputDirectory on any OS' {
        $out = Join-Path $script:TempRoot 'script-review'
        $output = Invoke-Pwsh -NoProfile -NonInteractive -File (Join-Path $script:RepoRoot 'scripts' 'release-winget.ps1') -Version '1.0.0' -OutputDirectory $out *>&1
        $LASTEXITCODE | Should -Be 0 -Because ($output -join "`n")
        Test-Path -LiteralPath (Join-Path $out '3DPrint.LayerPeek.locale.en-US.yaml') | Should -BeTrue
        @(Get-Calls | Where-Object { $_ -like 'gh auth token*' -or $_ -like 'wingetcreate *' }) | Should -BeNullOrEmpty
    }
}
