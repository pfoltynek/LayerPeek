<#
.SYNOPSIS
    Maintainer tool for a new FFmpeg version: downloads the new official FFmpeg source tarball, verifies it against
    independent sources and prints (or with -Update writes) the pinned values of build/FFmpeg.targets and
    src/LayerPeek.App/Licenses/ThirdPartyLicenses.cs.

.DESCRIPTION
    Only needed when the pinned FFmpeg version is bumped; a normal build never runs it (build/FFmpeg.targets verifies
    the pinned SHA256 itself on every first build). Every step fails the script on any mismatch (exit code 1) and then
    nothing is written.

    Both platforms build the same tarball (macOS: build/macos/build-ffmpeg.sh, T-026; Windows:
    build/windows/build-ffmpeg.sh, T-202; before it the prebuilt BtbN release), so there is one set of values. Before any
    download the inputs are validated and the library names of both platforms in build/FFmpeg.targets must be the same
    libraries with the same major versions (avformat-63.dll <-> libavformat.63.dylib ...).

    -Version (the official source tarball):
      1. Downloads https://ffmpeg.org/releases/ffmpeg-<version>.tar.xz into a new temporary folder and computes its
         SHA256 (FFmpeg publishes no checksums, only GPG signatures).
      2. Cross-checks the SHA256 against the Homebrew formula (https://formulae.brew.sh/api/formula/ffmpeg.json,
         urls.stable.checksum), a source independent of this download. Only a read-only HTTPS GET; Homebrew itself is not
         needed. The formula must point to exactly the same tarball URL; when Homebrew is on another version, the
         cross-check is impossible and the script fails (try again once Homebrew has the version; no override, user
         decision 2026-10-29).
      3. When gpg is on PATH: verifies the detached signature <tarball>.asc with the FFmpeg release signing key
         (https://ffmpeg.org/ffmpeg-devel.asc, fingerprint pinned below) in a temporary GnuPG home folder, so the
         user's keyring is never touched. Without gpg the step is skipped and the script says so.
      Values: LayerPeekFFmpegSourceUrl, LayerPeekFFmpegSourceSha256, LayerPeekFFmpegSourceFolder (build/FFmpeg.targets)
      and SourceTarballUri (ThirdPartyLicenses.cs).

    The temporary folder is deleted at the end. Nothing is installed; only read-only HTTPS GETs. Procedure:
    docs/prerequisites.md ("Nová verze FFmpeg").

.PARAMETER Version
    The FFmpeg release, e.g. 9.0.3 (the tarball ffmpeg-<version>.tar.xz on ffmpeg.org).

.PARAMETER Update
    Writes the verified values into build/FFmpeg.targets and ThirdPartyLicenses.cs (otherwise they are only printed).

.EXAMPLE
    pwsh build/update-ffmpeg.ps1 -Version 9.0.3

.EXAMPLE
    pwsh build/update-ffmpeg.ps1 -Version 9.0.3 -Update
#>
#Requires -Version 7.2
[CmdletBinding()]
param(
    # Not mandatory, so that the Pester tests can dot-source the script; the main part checks it.
    [string] $Version,
    [switch] $Update
)

Set-StrictMode -Version Latest

$script:RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$script:ReleasesBaseUrl = 'https://ffmpeg.org/releases/'
$script:HomebrewFormulaUrl = 'https://formulae.brew.sh/api/formula/ffmpeg.json'
$script:SigningKeyUrl = 'https://ffmpeg.org/ffmpeg-devel.asc'
# "FFmpeg release signing key <ffmpeg-devel@ffmpeg.org>", as published on https://ffmpeg.org/download.html.
$script:SigningKeyFingerprint = 'FCF986EA15E6E293A5644F10B4322F04D67658D8'

<#
.SYNOPSIS
    The official source tarball of a release: URL, file name, top folder inside the tarball and release branch.
#>
function Get-FFmpegSource {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Version)
    if ($Version -notmatch '^(\d+)\.(\d+)(\.\d+)?$') {
        throw "'$Version' is not an FFmpeg release version (e.g. 9.0.3)."
    }
    [pscustomobject]@{
        Version  = $Version
        Branch   = "$($Matches[1]).$($Matches[2])"
        Url      = "$($script:ReleasesBaseUrl)ffmpeg-$Version.tar.xz"
        FileName = "ffmpeg-$Version.tar.xz"
        Folder   = "ffmpeg-$Version"
    }
}

<#
.SYNOPSIS
    The library file names of both platforms in build/FFmpeg.targets (the _LayerPeekFFmpegLibrary items). Throws unless
    both platforms list the same libraries with the same major versions (libavformat.63.dylib <-> avformat-63.dll),
    since T-202 one for one: both build the same tarball.
#>
function Get-FFmpegLibraries {
    param([Parameter(Mandatory)] [string] $TargetsText)
    $lists = @([regex]::Matches($TargetsText, '<_LayerPeekFFmpegLibrary Include="([^"]+)"') | ForEach-Object { , @($_.Groups[1].Value -split ';' | Where-Object { $_ }) })
    $windows = @($lists | Where-Object { $_[0] -like '*.dll' })
    $mac = @($lists | Where-Object { $_[0] -like '*.dylib' })
    if ($windows.Count -ne 1 -or $mac.Count -ne 1) {
        throw "build/FFmpeg.targets must list the libraries in exactly one Windows and one macOS _LayerPeekFFmpegLibrary item (found $($windows.Count) and $($mac.Count))."
    }
    $windows = @($windows[0])
    $mac = @($mac[0])
    foreach ($name in $windows) {
        if ($name -notmatch '^[a-z]+-\d+\.dll$') { throw "Unexpected Windows library name '$name' in build/FFmpeg.targets." }
    }
    $counterparts = foreach ($name in $mac) {
        if ($name -notmatch '^lib(?<lib>[a-z]+)\.(?<major>\d+)\.dylib$') { throw "Unexpected macOS library name '$name' in build/FFmpeg.targets." }
        $counterpart = "$($Matches.lib)-$($Matches.major).dll"
        if ($windows -notcontains $counterpart) {
            throw "build/FFmpeg.targets lists $name for macOS but not $counterpart for Windows: both platforms must use the same library major versions."
        }
        $counterpart
    }
    $extra = @($windows | Where-Object { $counterparts -notcontains $_ })
    if ($extra.Count -gt 0) {
        throw "build/FFmpeg.targets lists $($extra -join ', ') for Windows but not for macOS: both platforms build the same libraries."
    }
    [pscustomobject]@{ Windows = $windows; MacOS = $mac }
}

<#
.SYNOPSIS
    SHA256 of the tarball in the Homebrew formula JSON (already parsed) for exactly this URL; throws when the formula is
    on another tarball or has no usable checksum.
#>
function Get-HomebrewChecksum {
    param([Parameter(Mandatory)] $Formula, [Parameter(Mandatory)] [string] $Url)
    $stable = $null
    if ($Formula.PSObject.Properties['urls'] -and $Formula.urls.PSObject.Properties['stable']) { $stable = $Formula.urls.stable }
    if ($null -eq $stable -or -not $stable.PSObject.Properties['url']) {
        throw 'The Homebrew formula has no urls.stable.url.'
    }
    if ($stable.url -ne $Url) {
        $homebrewVersion = if ($Formula.PSObject.Properties['versions']) { $Formula.versions.stable } else { '?' }
        throw "Homebrew's formula is on another tarball ($($stable.url), version $homebrewVersion), so the SHA256 of $Url cannot be cross-checked. Try again once Homebrew has this version."
    }
    $checksum = if ($stable.PSObject.Properties['checksum']) { [string] $stable.checksum } else { '' }
    if ($checksum -notmatch '^[0-9a-fA-F]{64}$') {
        throw "The Homebrew formula has no SHA256 for $Url (urls.stable.checksum: '$checksum')."
    }
    $checksum.ToLowerInvariant()
}

<#
.SYNOPSIS
    Verifies a detached signature with gpg in its own home folder. Returns Verified (good signature by the pinned key)
    or throws. InvokeGpg: { param([string[]] $Arguments) } returning @{ ExitCode; Output } (stand-in in the tests).
#>
function Test-GpgSignature {
    param(
        [Parameter(Mandatory)] [scriptblock] $InvokeGpg,
        [Parameter(Mandatory)] [string] $GnuPgHome,
        [Parameter(Mandatory)] [string] $KeyFile,
        [Parameter(Mandatory)] [string] $Signature,
        [Parameter(Mandatory)] [string] $Tarball,
        [string] $Fingerprint = $script:SigningKeyFingerprint
    )
    $null = New-Item -ItemType Directory -Force -Path $GnuPgHome
    $common = @('--homedir', $GnuPgHome, '--batch', '--status-fd', '1')
    $import = & $InvokeGpg ($common + @('--import', $KeyFile))
    if ($import.ExitCode -ne 0) {
        throw "gpg could not import the FFmpeg signing key from $KeyFile (exit code $($import.ExitCode)): $($import.Output -join ' ')"
    }
    $verify = & $InvokeGpg ($common + @('--verify', $Signature, $Tarball))
    $lines = @($verify.Output | ForEach-Object { [string] $_ })
    $valid = @($lines | Where-Object { $_ -match '^\[GNUPG:\] VALIDSIG ([0-9A-F]{40}) ' } | ForEach-Object { $Matches[1] })
    $good = @($lines | Where-Object { $_ -match '^\[GNUPG:\] GOODSIG ' })
    $bad = @($lines | Where-Object { $_ -match '^\[GNUPG:\] (BADSIG|ERRSIG|EXPSIG|EXPKEYSIG|REVKEYSIG|NO_PUBKEY) ' })
    if ($verify.ExitCode -ne 0 -or $bad.Count -gt 0 -or $good.Count -eq 0 -or $valid.Count -eq 0) {
        throw "The GPG signature $Signature does not verify (exit code $($verify.ExitCode)): $($lines -join ' ')"
    }
    # VALIDSIG <signing key fingerprint> ... <primary key fingerprint>: the pinned key may be the primary key or the subkey.
    $primary = @($lines | Where-Object { $_ -match '^\[GNUPG:\] VALIDSIG ' } | ForEach-Object { ($_ -split ' ')[-1] })
    if (($valid + $primary) -notcontains $Fingerprint) {
        throw "The signature was made by key $($valid -join ', '), not by the FFmpeg release signing key $Fingerprint."
    }
    'Verified'
}

<#
.SYNOPSIS
    Text of build/FFmpeg.targets with the given properties (name -> value) replaced; each must occur exactly once.
#>
function Update-TargetsText {
    param([Parameter(Mandatory)] [string] $Text, [Parameter(Mandatory)] [System.Collections.IDictionary] $Values)
    foreach ($name in $Values.Keys) {
        $pattern = "<$name>[^<]*</$name>"
        $count = [regex]::Matches($Text, $pattern).Count
        if ($count -ne 1) { throw "build/FFmpeg.targets must contain <$name> exactly once (found $count)." }
        $value = $Values[$name]
        $Text = [regex]::Replace($Text, $pattern, { param($m) "<$name>$value</$name>" }.GetNewClosure())
    }
    $Text
}

<#
.SYNOPSIS
    Text of ThirdPartyLicenses.cs with the given Uri properties (SourceTarballUri -> URL) replaced; each must occur
    exactly once (a test keeps it equal to build/FFmpeg.targets).
#>
function Update-LicensesText {
    param([Parameter(Mandatory)] [string] $Text, [Parameter(Mandatory)] [System.Collections.IDictionary] $Uris)
    foreach ($name in $Uris.Keys) {
        $pattern = "(public static Uri $name \{ get; \} =\s*new\(`")[^`"]*(`"\);)"
        $count = [regex]::Matches($Text, $pattern).Count
        if ($count -ne 1) { throw "ThirdPartyLicenses.cs must contain $name exactly once (found $count)." }
        $url = $Uris[$name]
        $Text = [regex]::Replace($Text, $pattern, { param($m) $m.Groups[1].Value + $url + $m.Groups[2].Value }.GetNewClosure())
    }
    $Text
}

function Get-DefaultGpg {
    $command = Get-Command gpg -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { $command.Source } else { $null }
}

function Invoke-SourceCheck {
    param($Source, [string] $Work, [scriptblock] $Download, [scriptblock] $GetJson, [AllowNull()] [string] $Gpg, [scriptblock] $InvokeGpg)
    $tarball = Join-Path $Work $Source.FileName
    Write-Host "Downloading $($Source.Url) ..."
    & $Download $Source.Url $tarball
    if (-not (Test-Path -LiteralPath $tarball -PathType Leaf)) { throw "The download of $($Source.Url) produced no file." }
    $sha256 = (Get-FileHash -LiteralPath $tarball -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "SHA256 of the download:   $sha256"

    $homebrew = Get-HomebrewChecksum -Formula (& $GetJson $script:HomebrewFormulaUrl) -Url $Source.Url
    Write-Host "SHA256 in Homebrew:       $homebrew ($($script:HomebrewFormulaUrl))"
    if ($homebrew -ne $sha256) {
        throw "SHA256 mismatch: the download has $sha256, the Homebrew formula $homebrew for the same URL. Do not pin this tarball."
    }
    Write-Host 'Homebrew cross-check:     OK' -ForegroundColor Green

    if ([string]::IsNullOrEmpty($Gpg)) {
        Write-Host 'GPG signature:            SKIPPED (gpg is not installed; the Homebrew cross-check above still applies)' -ForegroundColor Yellow
    }
    else {
        $signature = "$tarball.asc"
        $keyFile = Join-Path $Work 'ffmpeg-devel.asc'
        & $Download "$($Source.Url).asc" $signature
        & $Download $script:SigningKeyUrl $keyFile
        $gpgPath = $Gpg
        $runGpg = if ($InvokeGpg) { $InvokeGpg } else {
            {
                param([string[]] $Arguments)
                $output = & $gpgPath @Arguments 2>&1
                @{ ExitCode = $LASTEXITCODE; Output = @($output | ForEach-Object { [string] $_ }) }
            }.GetNewClosure()
        }
        $gnupgHome = Join-Path $Work 'gnupg'
        try {
            $null = Test-GpgSignature -InvokeGpg $runGpg -GnuPgHome $gnupgHome -KeyFile $keyFile -Signature $signature -Tarball $tarball
        }
        finally {
            # The temporary home may have started an agent or keyboxd; stop them before the folder is deleted.
            if (-not $InvokeGpg) {
                $gpgconf = Join-Path (Split-Path -Parent $Gpg) 'gpgconf'
                if (Test-Path -LiteralPath $gpgconf) { & $gpgconf --homedir $gnupgHome --kill all 2>&1 | Out-Null }
            }
        }
        Write-Host "GPG signature:            OK (FFmpeg release signing key $($script:SigningKeyFingerprint))" -ForegroundColor Green
    }
    $sha256
}

<#
.SYNOPSIS
    The whole run; returns the exit code. Download: { param($Url, $Path) } (throws on HTTP errors), GetJson:
    { param($Url) } (parsed object), Gpg: path of gpg or $null (skipped), InvokeGpg: see Test-GpgSignature. All
    replaceable by the tests.
#>
function Invoke-Main {
    param(
        [string] $Version,
        [switch] $Update,
        [string] $RepoRoot = $script:RepoRoot,
        [scriptblock] $Download = { param($Url, $Path) Invoke-WebRequest -Uri $Url -OutFile $Path -UseBasicParsing -MaximumRetryCount 3 -RetryIntervalSec 2 },
        [scriptblock] $GetJson = { param($Url) Invoke-RestMethod -Uri $Url -UseBasicParsing -MaximumRetryCount 3 -RetryIntervalSec 2 },
        [AllowNull()] [string] $Gpg = (Get-DefaultGpg),
        [scriptblock] $InvokeGpg = $null,
        [string] $TempRoot = [System.IO.Path]::GetTempPath()
    )
    $work = Join-Path $TempRoot "layerpeek-ffmpeg-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    $null = New-Item -ItemType Directory -Path $work
    try {
        $targetsPath = Join-Path $RepoRoot 'build' 'FFmpeg.targets'
        $licensesPath = Join-Path $RepoRoot 'src' 'LayerPeek.App' 'Licenses' 'ThirdPartyLicenses.cs'
        $targetsText = [System.IO.File]::ReadAllText($targetsPath)

        # Everything that needs no download first.
        if ([string]::IsNullOrEmpty($Version)) { throw 'Needs -Version <FFmpeg release>, e.g. -Version 9.0.3.' }
        $source = Get-FFmpegSource -Version $Version
        $null = Get-FFmpegLibraries -TargetsText $targetsText

        $sha256 = Invoke-SourceCheck -Source $source -Work $work -Download $Download -GetJson $GetJson -Gpg $Gpg -InvokeGpg $InvokeGpg
        $values = [ordered]@{
            LayerPeekFFmpegSourceUrl    = $source.Url
            LayerPeekFFmpegSourceSha256 = $sha256
            LayerPeekFFmpegSourceFolder = $source.Folder
        }
        $uris = [ordered]@{ SourceTarballUri = $source.Url }

        Write-Host ''
        Write-Host 'Pinned values (build/FFmpeg.targets):'
        foreach ($name in $values.Keys) { Write-Host "  <$name>$($values[$name])</$name>" }
        Write-Host 'src/LayerPeek.App/Licenses/ThirdPartyLicenses.cs:'
        foreach ($name in $uris.Keys) { Write-Host "  $name = $($uris[$name])" }

        # Both texts first, then both writes: a file that does not match leaves neither changed.
        $targets = Update-TargetsText -Text $targetsText -Values $values
        $licenses = Update-LicensesText -Text ([System.IO.File]::ReadAllText($licensesPath)) -Uris $uris
        if ($Update) {
            [System.IO.File]::WriteAllText($targetsPath, $targets)
            [System.IO.File]::WriteAllText($licensesPath, $licenses)
            Write-Host 'Written into build/FFmpeg.targets and ThirdPartyLicenses.cs.' -ForegroundColor Green
        }
        elseif ($targets -ceq $targetsText) {
            Write-Host 'These are the values already pinned in build/FFmpeg.targets.' -ForegroundColor Green
        }
        Write-Host ''
        Write-Host 'Also check (docs/prerequisites.md, "Nová verze FFmpeg"): the version check in build/macos/build-ffmpeg.sh and'
        Write-Host 'build/windows/build-ffmpeg.sh, the versions in ThirdPartyLicensesTests and the docs; then dotnet build and'
        Write-Host 'dotnet test on both platforms.'
        return 0
    }
    catch {
        Write-Host "update-ffmpeg.ps1: $($_.Exception.Message)" -ForegroundColor Red
        return 1
    }
    finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# Dot-sourced (the Pester tests): only the functions above.
if ($MyInvocation.InvocationName -ne '.') {
    $ErrorActionPreference = 'Stop'
    exit (Invoke-Main -Version $Version -Update:$Update)
}
