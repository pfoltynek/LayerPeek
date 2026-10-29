<#
.SYNOPSIS
    Maintainer tool for a new FFmpeg version: downloads the new FFmpeg archive of each platform, verifies it against
    independent sources and prints (or with -Update writes) the pinned values of build/FFmpeg.targets and
    src/LayerPeek.App/Licenses/ThirdPartyLicenses.cs.

.DESCRIPTION
    Only needed when the pinned FFmpeg version is bumped; a normal build never runs it (build/FFmpeg.targets verifies
    the pinned SHA256 itself on every first build). Both platforms are checked by default (-Platform all); every step
    fails the script on any mismatch (exit code 1) and then nothing is written.

    Before any download: the inputs are validated and both platforms must stay on the same FFmpeg release branch
    (major.minor, e.g. 9.0): the new value of one platform is compared with the new or the currently pinned value of the
    other one. The library names of both platforms in build/FFmpeg.targets must have the same major versions
    (avformat-63.dll <-> libavformat.63.dylib ...).

    macOS (-Version, the official source tarball built by build/macos/build-ffmpeg.sh, T-026):
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
      Values: LayerPeekFFmpegMacSourceUrl, LayerPeekFFmpegMacSourceSha256, LayerPeekFFmpegMacSourceFolder and
      MacOSBuildUri.

    Windows (-BtbNTag and -BtbNAsset, the LGPL shared win64 build of BtbN, T-014):
      1. Downloads checksums.sha256 of the release (https://github.com/BtbN/FFmpeg-Builds/releases/download/<tag>/,
         sha256sum format) and takes the line of the asset. A release without that file, or without the asset in it,
         fails the script (no silent skip).
      2. Downloads the zip into the temporary folder, computes its SHA256 and compares it with that line.
      3. Checks the zip: exactly one top folder named like the asset (LayerPeekFFmpegArchiveFolder), LICENSE.txt with the
         LGPL 3 text, and bin/<dll> for every Windows library of build/FFmpeg.targets (avformat-63.dll ...).
      Values: LayerPeekFFmpegUrl, LayerPeekFFmpegSha256, LayerPeekFFmpegArchiveFolder and WindowsBuildUri.

    The temporary folder is deleted at the end. Nothing is installed; only read-only HTTPS GETs. Procedure:
    docs/prerequisites.md ("Nová verze FFmpeg").

.PARAMETER Platform
    macos, windows or all (default): which pinned build to verify and update.

.PARAMETER Version
    macOS: the FFmpeg release, e.g. 9.0.3 (the tarball ffmpeg-<version>.tar.xz on ffmpeg.org).

.PARAMETER BtbNTag
    Windows: the BtbN release tag, e.g. autobuild-2026-10-17-20-51.

.PARAMETER BtbNAsset
    Windows: the asset of that release, e.g. ffmpeg-n9.0.2-28-g669f5ab083-win64-lgpl-shared-9.0.zip (only LGPL shared
    win64 builds of a release branch are accepted).

.PARAMETER Update
    Writes the verified values into build/FFmpeg.targets and ThirdPartyLicenses.cs (otherwise they are only printed).

.EXAMPLE
    pwsh build/update-ffmpeg.ps1 -Version 9.0.3 -BtbNTag autobuild-2026-11-20-12-00 -BtbNAsset ffmpeg-n9.0.3-...-win64-lgpl-shared-9.0.zip

.EXAMPLE
    pwsh build/update-ffmpeg.ps1 -Platform macos -Version 9.0.3 -Update
#>
#Requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateSet('macos', 'windows', 'all')]
    [string] $Platform = 'all',
    # Not mandatory, so that the Pester tests can dot-source the script; the main part checks them.
    [string] $Version,
    [string] $BtbNTag,
    [string] $BtbNAsset,
    [switch] $Update
)

Set-StrictMode -Version Latest

$script:RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$script:ReleasesBaseUrl = 'https://ffmpeg.org/releases/'
$script:HomebrewFormulaUrl = 'https://formulae.brew.sh/api/formula/ffmpeg.json'
$script:SigningKeyUrl = 'https://ffmpeg.org/ffmpeg-devel.asc'
# "FFmpeg release signing key <ffmpeg-devel@ffmpeg.org>", as published on https://ffmpeg.org/download.html.
$script:SigningKeyFingerprint = 'FCF986EA15E6E293A5644F10B4322F04D67658D8'
$script:BtbNDownloadBaseUrl = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/'
# The checksum file BtbN attaches to every autobuild release (sha256sum format, one line per asset).
$script:BtbNChecksumsName = 'checksums.sha256'

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
    The BtbN LGPL shared win64 build of a release: URLs of the zip and of the checksum file, archive folder and release
    branch. Throws on anything else (GPL, static, other targets, the master builds N-..., a malformed tag).
#>
function Get-BtbNBuild {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Tag, [Parameter(Mandatory)] [AllowEmptyString()] [string] $Asset)
    if ($Tag -notmatch '^autobuild-\d{4}-\d{2}-\d{2}-\d{2}-\d{2}$') {
        throw "'$Tag' is not a BtbN release tag (e.g. autobuild-2026-10-17-20-51)."
    }
    if ($Asset -notmatch '^ffmpeg-n(?<major>\d+)\.(?<minor>\d+)(\.\d+)?(-\d+-g[0-9a-f]+)?-win64-lgpl-shared-(?<branch>\d+\.\d+)\.zip$') {
        throw "'$Asset' is not an LGPL shared win64 build of a BtbN release branch (e.g. ffmpeg-n9.0.2-28-g669f5ab083-win64-lgpl-shared-9.0.zip)."
    }
    $branch = "$($Matches.major).$($Matches.minor)"
    if ($Matches.branch -ne $branch) {
        throw "'$Asset' names FFmpeg $branch but the release branch $($Matches.branch)."
    }
    $releaseUrl = "$($script:BtbNDownloadBaseUrl)$Tag/"
    [pscustomobject]@{
        Tag          = $Tag
        Asset        = $Asset
        Branch       = $branch
        Url          = "$releaseUrl$Asset"
        ChecksumsUrl = "$releaseUrl$($script:BtbNChecksumsName)"
        Folder       = $Asset.Substring(0, $Asset.Length - '.zip'.Length)
    }
}

<#
.SYNOPSIS
    The FFmpeg release branch (major.minor) of the builds currently pinned in build/FFmpeg.targets.
#>
function Get-PinnedBranches {
    param([Parameter(Mandatory)] [string] $TargetsText)
    $mac = [regex]::Match($TargetsText, '<LayerPeekFFmpegMacSourceFolder>ffmpeg-(\d+\.\d+)[^<]*</LayerPeekFFmpegMacSourceFolder>')
    $windows = [regex]::Match($TargetsText, '<LayerPeekFFmpegArchiveFolder>ffmpeg-n(\d+\.\d+)[^<]*</LayerPeekFFmpegArchiveFolder>')
    if (-not $mac.Success) { throw 'build/FFmpeg.targets has no LayerPeekFFmpegMacSourceFolder of the form ffmpeg-<version>.' }
    if (-not $windows.Success) { throw 'build/FFmpeg.targets has no LayerPeekFFmpegArchiveFolder of the form ffmpeg-n<version>-...' }
    [pscustomobject]@{ MacOS = $mac.Groups[1].Value; Windows = $windows.Groups[1].Value }
}

<#
.SYNOPSIS
    The library file names of both platforms in build/FFmpeg.targets (the _LayerPeekFFmpegLibrary items). Throws unless
    every macOS library has its Windows counterpart with the same major version (libavformat.63.dylib <->
    avformat-63.dll); Windows may have more (swresample, only a dependency of the BtbN build).
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
    foreach ($name in $mac) {
        if ($name -notmatch '^lib(?<lib>[a-z]+)\.(?<major>\d+)\.dylib$') { throw "Unexpected macOS library name '$name' in build/FFmpeg.targets." }
        $counterpart = "$($Matches.lib)-$($Matches.major).dll"
        if ($windows -notcontains $counterpart) {
            throw "build/FFmpeg.targets lists $name for macOS but not $counterpart for Windows: both platforms must use the same library major versions."
        }
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
    SHA256 of one asset in the text of a BtbN checksums.sha256 (sha256sum format); throws when the asset is missing,
    listed twice or the file is malformed.
#>
function Get-BtbNChecksum {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Text, [Parameter(Mandatory)] [string] $Asset)
    $found = [System.Collections.Generic.List[string]]::new()
    foreach ($line in ($Text -split "`r?`n")) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line -notmatch '^(?<hash>[0-9a-fA-F]{64}) [ *](?<name>[^\\/]+)$') {
            throw "Malformed line in $($script:BtbNChecksumsName): '$line'"
        }
        if ($Matches.name -ceq $Asset) { $found.Add($Matches.hash.ToLowerInvariant()) }
    }
    if ($found.Count -eq 0) { throw "$($script:BtbNChecksumsName) of the release does not list $Asset." }
    if ($found.Count -gt 1) { throw "$($script:BtbNChecksumsName) of the release lists $Asset $($found.Count) times." }
    $found[0]
}

<#
.SYNOPSIS
    Checks the BtbN zip without extracting it: one top folder named Folder, Folder/LICENSE.txt with the LGPL 3 text and
    Folder/bin/<name> for every library. Throws with what is missing (and the FFmpeg DLLs that are there instead).
#>
function Test-BtbNArchive {
    param([Parameter(Mandatory)] [string] $Path, [Parameter(Mandatory)] [string] $Folder, [Parameter(Mandatory)] [string[]] $Libraries)
    Add-Type -AssemblyName System.IO.Compression
    $zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $names = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        $tops = @($names | ForEach-Object { ($_ -split '/')[0] } | Sort-Object -Unique)
        if ($tops.Count -ne 1 -or $tops[0] -cne $Folder) {
            throw "The zip must contain exactly one top folder $Folder (LayerPeekFFmpegArchiveFolder), found: $($tops -join ', ')."
        }
        $license = $zip.Entries | Where-Object { $_.FullName.Replace('\', '/') -ceq "$Folder/LICENSE.txt" } | Select-Object -First 1
        if (-not $license) { throw "The zip has no $Folder/LICENSE.txt." }
        $reader = [System.IO.StreamReader]::new($license.Open())
        try { $licenseText = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($licenseText -notmatch 'GNU LESSER GENERAL PUBLIC LICENSE\s+Version 3') {
            throw "$Folder/LICENSE.txt is not the LGPL 3 text: not an LGPL build."
        }
        $missing = @($Libraries | Where-Object { $names -cnotcontains "$Folder/bin/$_" })
        if ($missing.Count -gt 0) {
            $present = @($names | Where-Object { $_ -match "^$([regex]::Escape($Folder))/bin/(av|sw)[a-z]+-\d+\.dll$" } | ForEach-Object { ($_ -split '/')[-1] } | Sort-Object)
            throw "The zip has no $($missing -join ', ') in $Folder/bin (it has $($present -join ', ')). Other library major versions mean a new FFmpeg branch: the library names change in build/FFmpeg.targets, build/macos/build-ffmpeg.sh and FFmpegRuntime (docs/prerequisites.md)."
        }
    }
    finally {
        $zip.Dispose()
    }
    'Verified'
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
    Text of ThirdPartyLicenses.cs with the given Uri properties (MacOSBuildUri, WindowsBuildUri -> URL) replaced; each
    must occur exactly once (a test keeps them equal to build/FFmpeg.targets).
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

function Invoke-MacOSCheck {
    param($Source, [string] $Work, [scriptblock] $Download, [scriptblock] $GetJson, [AllowNull()] [string] $Gpg, [scriptblock] $InvokeGpg)
    $tarball = Join-Path $Work $Source.FileName
    Write-Host "macOS: downloading $($Source.Url) ..."
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

function Invoke-WindowsCheck {
    param($Build, [string] $Work, [scriptblock] $Download, [string[]] $Libraries)
    $checksums = Join-Path $Work $script:BtbNChecksumsName
    Write-Host "Windows: downloading $($Build.ChecksumsUrl) ..."
    try { & $Download $Build.ChecksumsUrl $checksums }
    catch { throw "The BtbN release $($Build.Tag) publishes no $($script:BtbNChecksumsName) (or it cannot be downloaded: $($_.Exception.Message)). Without it the zip cannot be verified; do not pin it." }
    if (-not (Test-Path -LiteralPath $checksums -PathType Leaf)) { throw "The download of $($Build.ChecksumsUrl) produced no file." }
    $published = Get-BtbNChecksum -Text ([System.IO.File]::ReadAllText($checksums)) -Asset $Build.Asset

    $zip = Join-Path $Work $Build.Asset
    Write-Host "Windows: downloading $($Build.Url) ..."
    & $Download $Build.Url $zip
    if (-not (Test-Path -LiteralPath $zip -PathType Leaf)) { throw "The download of $($Build.Url) produced no file." }
    $sha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "SHA256 of the download:   $sha256"
    Write-Host "SHA256 in BtbN checksums: $published ($($Build.ChecksumsUrl))"
    if ($published -ne $sha256) {
        throw "SHA256 mismatch: the download has $sha256, $($script:BtbNChecksumsName) of the release $published. Do not pin this zip."
    }
    Write-Host 'BtbN checksum:            OK' -ForegroundColor Green
    $null = Test-BtbNArchive -Path $zip -Folder $Build.Folder -Libraries $Libraries
    Write-Host "Archive contents:         OK ($($Build.Folder)/, LICENSE.txt LGPL 3, bin/$($Libraries -join ', bin/'))" -ForegroundColor Green
    # The zip is large (about 80 MB); delete it right away.
    Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
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
        [ValidateSet('macos', 'windows', 'all')] [string] $Platform = 'all',
        [string] $Version,
        [string] $BtbNTag,
        [string] $BtbNAsset,
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
        $mac = $Platform -in 'macos', 'all'
        $windows = $Platform -in 'windows', 'all'
        $targetsPath = Join-Path $RepoRoot 'build' 'FFmpeg.targets'
        $licensesPath = Join-Path $RepoRoot 'src' 'LayerPeek.App' 'Licenses' 'ThirdPartyLicenses.cs'
        $targetsText = [System.IO.File]::ReadAllText($targetsPath)

        # Everything that needs no download first.
        if ($mac -and [string]::IsNullOrEmpty($Version)) { throw 'macOS needs -Version <FFmpeg release>, e.g. -Version 9.0.3.' }
        if ($windows -and ([string]::IsNullOrEmpty($BtbNTag) -or [string]::IsNullOrEmpty($BtbNAsset))) {
            throw 'Windows needs -BtbNTag <autobuild-...> and -BtbNAsset <ffmpeg-n...-win64-lgpl-shared-x.y.zip>.'
        }
        $source = if ($mac) { Get-FFmpegSource -Version $Version } else { $null }
        $build = if ($windows) { Get-BtbNBuild -Tag $BtbNTag -Asset $BtbNAsset } else { $null }
        $libraries = Get-FFmpegLibraries -TargetsText $targetsText
        $pinned = Get-PinnedBranches -TargetsText $targetsText
        $macBranch = if ($source) { $source.Branch } else { $pinned.MacOS }
        $windowsBranch = if ($build) { $build.Branch } else { $pinned.Windows }
        if ($macBranch -ne $windowsBranch) {
            throw "Both platforms must use the same FFmpeg release branch: macOS would be on $macBranch, Windows on $windowsBranch. Bump both together (-Platform all)."
        }

        $values = [ordered]@{}
        $uris = [ordered]@{}
        if ($mac) {
            $sha256 = Invoke-MacOSCheck -Source $source -Work $work -Download $Download -GetJson $GetJson -Gpg $Gpg -InvokeGpg $InvokeGpg
            $values['LayerPeekFFmpegMacSourceUrl'] = $source.Url
            $values['LayerPeekFFmpegMacSourceSha256'] = $sha256
            $values['LayerPeekFFmpegMacSourceFolder'] = $source.Folder
            $uris['MacOSBuildUri'] = $source.Url
        }
        if ($windows) {
            if ($mac) { Write-Host '' }
            $sha256 = Invoke-WindowsCheck -Build $build -Work $work -Download $Download -Libraries $libraries.Windows
            $values['LayerPeekFFmpegUrl'] = $build.Url
            $values['LayerPeekFFmpegSha256'] = $sha256
            $values['LayerPeekFFmpegArchiveFolder'] = $build.Folder
            $uris['WindowsBuildUri'] = $build.Url
        }

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
        Write-Host 'Also check (docs/prerequisites.md, "Nová verze FFmpeg"): the version check in build/macos/build-ffmpeg.sh,'
        Write-Host 'the versions in ThirdPartyLicensesTests and the docs; then dotnet build and dotnet test on both platforms.'
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
    exit (Invoke-Main -Platform $Platform -Version $Version -BtbNTag $BtbNTag -BtbNAsset $BtbNAsset -Update:$Update)
}
