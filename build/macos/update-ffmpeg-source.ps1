<#
.SYNOPSIS
    Maintainer tool for a new FFmpeg version on macOS (T-026): downloads the official source tarball, verifies it against
    independent sources and prints (or with -Update writes) the pinned values of build/FFmpeg.targets.

.DESCRIPTION
    Only needed when the pinned FFmpeg version is bumped; a normal build never runs it (build/FFmpeg.targets verifies
    the pinned SHA256 itself on every first build).

    Steps, each one fails the script on any mismatch (exit code 1):
      1. Downloads https://ffmpeg.org/releases/ffmpeg-<version>.tar.xz into a new temporary folder and computes its
         SHA256 (FFmpeg publishes no checksums, only GPG signatures).
      2. Cross-checks the SHA256 against the Homebrew formula (https://formulae.brew.sh/api/formula/ffmpeg.json,
         urls.stable.checksum), a source independent of this download. Only a read-only HTTPS GET; Homebrew itself is not
         needed. The formula must point to exactly the same tarball URL; when Homebrew is on another version, the
         cross-check is impossible and the script fails (try again once Homebrew has the version).
      3. When gpg is on PATH: verifies the detached signature <tarball>.asc with the FFmpeg release signing key
         (https://ffmpeg.org/ffmpeg-devel.asc, fingerprint pinned below) in a temporary GnuPG home folder, so the
         user's keyring is never touched. Without gpg the step is skipped and the script says so.
      4. Prints the values for build/FFmpeg.targets (LayerPeekFFmpegMacSourceUrl, LayerPeekFFmpegMacSourceSha256,
         LayerPeekFFmpegMacSourceFolder) and src/LayerPeek.App/Licenses/ThirdPartyLicenses.cs (MacOSBuildUri);
         -Update writes them into both files.

    The temporary folder is deleted at the end. Nothing is installed. Procedure: docs/prerequisites.md
    ("Nová verze FFmpeg").

.PARAMETER Version
    The FFmpeg release, e.g. 9.0.3 (the tarball ffmpeg-<version>.tar.xz on ffmpeg.org).

.PARAMETER Update
    Writes the verified values into build/FFmpeg.targets and ThirdPartyLicenses.cs (otherwise they are only printed).

.EXAMPLE
    pwsh build/macos/update-ffmpeg-source.ps1 -Version 9.0.3

.EXAMPLE
    pwsh build/macos/update-ffmpeg-source.ps1 -Version 9.0.3 -Update
#>
#Requires -Version 7.2
[CmdletBinding()]
param(
    # Not mandatory, so that the Pester tests can dot-source the script; the main part requires it.
    [string] $Version,
    [switch] $Update
)

Set-StrictMode -Version Latest

$script:RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..' '..'))
$script:ReleasesBaseUrl = 'https://ffmpeg.org/releases/'
$script:HomebrewFormulaUrl = 'https://formulae.brew.sh/api/formula/ffmpeg.json'
$script:SigningKeyUrl = 'https://ffmpeg.org/ffmpeg-devel.asc'
# "FFmpeg release signing key <ffmpeg-devel@ffmpeg.org>", as published on https://ffmpeg.org/download.html.
$script:SigningKeyFingerprint = 'FCF986EA15E6E293A5644F10B4322F04D67658D8'

<#
.SYNOPSIS
    The official source tarball of a release: URL, file name and top folder inside the tarball.
#>
function Get-FFmpegSource {
    param([Parameter(Mandatory)] [string] $Version)
    if ($Version -notmatch '^\d+\.\d+(\.\d+)?$') {
        throw "'$Version' is not an FFmpeg release version (e.g. 9.0.3)."
    }
    [pscustomobject]@{
        Version  = $Version
        Url      = "$($script:ReleasesBaseUrl)ffmpeg-$Version.tar.xz"
        FileName = "ffmpeg-$Version.tar.xz"
        Folder   = "ffmpeg-$Version"
    }
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
    Text of build/FFmpeg.targets with the pinned macOS source values replaced (each property must occur exactly once).
#>
function Update-TargetsText {
    param([Parameter(Mandatory)] [string] $Text, [Parameter(Mandatory)] $Source, [Parameter(Mandatory)] [string] $Sha256)
    $values = [ordered]@{
        LayerPeekFFmpegMacSourceUrl    = $Source.Url
        LayerPeekFFmpegMacSourceSha256 = $Sha256
        LayerPeekFFmpegMacSourceFolder = $Source.Folder
    }
    foreach ($name in $values.Keys) {
        $pattern = "<$name>[^<]*</$name>"
        $count = [regex]::Matches($Text, $pattern).Count
        if ($count -ne 1) { throw "build/FFmpeg.targets must contain <$name> exactly once (found $count)." }
        $Text = [regex]::Replace($Text, $pattern, "<$name>$($values[$name])</$name>")
    }
    $Text
}

<#
.SYNOPSIS
    Text of ThirdPartyLicenses.cs with MacOSBuildUri set to the tarball URL (a test keeps it equal to the targets).
#>
function Update-LicensesText {
    param([Parameter(Mandatory)] [string] $Text, [Parameter(Mandatory)] $Source)
    $pattern = '(public static Uri MacOSBuildUri \{ get; \} = new\(")[^"]*("\);)'
    $count = [regex]::Matches($Text, $pattern).Count
    if ($count -ne 1) { throw "ThirdPartyLicenses.cs must contain MacOSBuildUri exactly once (found $count)." }
    [regex]::Replace($Text, $pattern, { param($m) $m.Groups[1].Value + $Source.Url + $m.Groups[2].Value })
}

function Get-DefaultGpg {
    $command = Get-Command gpg -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { $command.Source } else { $null }
}

<#
.SYNOPSIS
    The whole run; returns the exit code. Download: { param($Url, $Path) }, GetJson: { param($Url) } (parsed object),
    Gpg: path of gpg or $null (skipped), InvokeGpg: see Test-GpgSignature. All replaceable by the tests.
#>
function Invoke-Main {
    param(
        [Parameter(Mandatory)] [string] $Version,
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
        $source = Get-FFmpegSource -Version $Version
        $tarball = Join-Path $work $source.FileName

        Write-Host "Downloading $($source.Url) ..."
        & $Download $source.Url $tarball
        if (-not (Test-Path -LiteralPath $tarball -PathType Leaf)) { throw "The download of $($source.Url) produced no file." }
        $sha256 = (Get-FileHash -LiteralPath $tarball -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-Host "SHA256 of the download:   $sha256"

        $homebrew = Get-HomebrewChecksum -Formula (& $GetJson $script:HomebrewFormulaUrl) -Url $source.Url
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
            $keyFile = Join-Path $work 'ffmpeg-devel.asc'
            & $Download "$($source.Url).asc" $signature
            & $Download $script:SigningKeyUrl $keyFile
            $gpgPath = $Gpg
            $runGpg = if ($InvokeGpg) { $InvokeGpg } else {
                {
                    param([string[]] $Arguments)
                    $output = & $gpgPath @Arguments 2>&1
                    @{ ExitCode = $LASTEXITCODE; Output = @($output | ForEach-Object { [string] $_ }) }
                }.GetNewClosure()
            }
            $gnupgHome = Join-Path $work 'gnupg'
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

        Write-Host ''
        Write-Host 'Pinned values (build/FFmpeg.targets):'
        Write-Host "  <LayerPeekFFmpegMacSourceUrl>$($source.Url)</LayerPeekFFmpegMacSourceUrl>"
        Write-Host "  <LayerPeekFFmpegMacSourceSha256>$sha256</LayerPeekFFmpegMacSourceSha256>"
        Write-Host "  <LayerPeekFFmpegMacSourceFolder>$($source.Folder)</LayerPeekFFmpegMacSourceFolder>"
        Write-Host 'src/LayerPeek.App/Licenses/ThirdPartyLicenses.cs:'
        Write-Host "  MacOSBuildUri = $($source.Url)"

        if ($Update) {
            $targetsPath = Join-Path $RepoRoot 'build' 'FFmpeg.targets'
            $licensesPath = Join-Path $RepoRoot 'src' 'LayerPeek.App' 'Licenses' 'ThirdPartyLicenses.cs'
            # Both texts first, then both writes: a file that does not match leaves neither changed.
            $targets = Update-TargetsText -Text ([System.IO.File]::ReadAllText($targetsPath)) -Source $source -Sha256 $sha256
            $licenses = Update-LicensesText -Text ([System.IO.File]::ReadAllText($licensesPath)) -Source $source
            [System.IO.File]::WriteAllText($targetsPath, $targets)
            [System.IO.File]::WriteAllText($licensesPath, $licenses)
            Write-Host 'Written into build/FFmpeg.targets and ThirdPartyLicenses.cs.' -ForegroundColor Green
        }
        Write-Host ''
        Write-Host 'Also check (docs/prerequisites.md, "Nová verze FFmpeg"): the Windows BtbN build of the same release,'
        Write-Host 'the library versions (avformat-63 ...) and the version check in build/macos/build-ffmpeg.sh, the docs.'
        return 0
    }
    catch {
        Write-Host "update-ffmpeg-source.ps1: $($_.Exception.Message)" -ForegroundColor Red
        return 1
    }
    finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# Dot-sourced (the Pester tests): only the functions above.
if ($MyInvocation.InvocationName -ne '.') {
    $ErrorActionPreference = 'Stop'
    if ([string]::IsNullOrEmpty($Version)) {
        Write-Host 'Pass -Version <FFmpeg release>, e.g. -Version 9.0.3 (see Get-Help).' -ForegroundColor Red
        exit 2
    }
    exit (Invoke-Main -Version $Version -Update:$Update)
}
