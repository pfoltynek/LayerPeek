<#
.SYNOPSIS
    Builds the release files of this machine's OS: build + tests, Native AOT publish per RID, AOT smoke test,
    archives, the signed update feed and SHA256 checksums. Uploads nothing (see scripts/release-upload.ps1).

.DESCRIPTION
    Native AOT does not compile across operating systems: Windows RIDs are built on Windows, macOS RIDs on a Mac.
    The version comes from <Version> in the root Directory.Build.props.

    Output (artifacts/ is gitignored), recreated on every run for the version:
        artifacts/release/<version>/publish/<rid>/   Native AOT publish folder
        artifacts/release/<version>/dist/            files to upload: archives, the Velopack installer and update feed (Windows),
                                                     the .dmg and the .tar.gz of LayerPeek.app and the Velopack package and
                                                     update feed (macOS, T-058), the signature of every update feed
                                                     releases.<rid>.json.sig (T-181), the FFmpeg source
                                                     ffmpeg-source-<version>-<os>.tar.xz (T-199) + SHA256SUMS-<os>.txt
        artifacts/release/<version>/app/<rid>/       the signed LayerPeek.app of vpk (macOS, T-019, T-058: portable/)
        artifacts/release/<version>/build-info.json  build marker (version, commit, clean tree, files) that
                                                     release-upload.ps1 checks so that no older build is uploaded
    Build all RIDs of this OS in one run: a new run deletes the previous output of the same version.

    The script may run on any commit (e.g. to try the packaging); only a build of the tagged release commit from a
    clean working tree can be uploaded.

    Update feed signature (T-181, docs/release.md "Podpis feedu aktualizací"): every releases.<rid>.json is signed with the
    offline private key, an encrypted PKCS#8 PEM given by -FeedSigningKey or LAYERPEEK_FEED_SIGNING_KEY. Before the long
    build the script checks that the source of the app (src/LayerPeek.App/Installer/UpdateFeedKeys.cs, Production) trusts
    at least one key, asks for the password, decrypts the key in memory and refuses it unless its public key is in that
    list; it prints the SHA-256 of the public key and then drops the key, its text and the password: nothing secret stays in
    memory during the build. The key medium stays connected until the end: after packaging the script reads the key file
    again from the same path (when it is missing, it asks to connect the medium and retry instead of failing the build),
    asks for the password a second time, refuses the key unless it is the same key as checked at the start (same public
    key), signs the feeds, verifies every signature against the trusted keys and disposes the key and the password. The key
    is never copied, printed or written; build-info.json names only the SHA-256 of the public key. Without a key the build
    fails, unless -SkipFeedSignature makes a trial build that release-upload.ps1 refuses.

    FFmpeg source (T-199, docs/release.md "Zdrojový kód FFmpeg"): before the long build the script fetches the pinned
    FFmpeg source of this OS (build/FFmpeg.targets) into the cache LayerPeek.Build/ffmpeg-source (macOS
    ~/Library/Caches, Windows %LOCALAPPDATA%; downloaded and verified once, never deleted) and fails early when it cannot.
    The packaging step 'ffmpeg-source' packs it with the build script and a README after checking that the published
    FFmpeg libraries are built from it.

    Build record before signing (T-197): after packaging, before the key is read again, the script writes build-info.json
    with FeedSignature.Status 'pending': the version, commit, clean tree, OS, RIDs, every packaged file with its SHA-256
    and the SHA-256 of the public key checked before the build. A successful signing replaces it with the final marker
    (Status 'signed'). When the signing fails (key medium disconnected and 'abort', a non-interactive session, three wrong
    passwords, another key at the path), the record stays and -SignOnly signs that dist/ later without a new build.

.PARAMETER SignOnly
    Signs the finished dist/ of a build whose signing did not finish (T-197), without building anything: only in a clean
    checkout of the tagged release commit (vX.Y.Z of <Version>) that dist/ was built from, and only when every file of
    dist/ is exactly the file of the pending build record (same names, same SHA-256; nothing missing or added). Then the
    same key checks as the signing of the build (encrypted PEM, UpdateFeedKeys.Production not empty and trusting the key
    the build checked, password with three attempts at the prompt, the same public key as checked before the build, every
    signature verified) and the same output: releases.<rid>.json.sig, SHA256SUMS-<os>.txt and build-info.json with
    Status 'signed', which release-upload.ps1 accepts. Refuses anything else (another commit, a dirty tree, a changed,
    missing or added file, no record, an already signed or trial build) and signs nothing; then build again. Takes
    -FeedSigningKey / LAYERPEEK_FEED_SIGNING_KEY and -FeedSigningKeyPassword; the password is asked for once.

.PARAMETER Runtime
    RIDs to build. Default: the default RIDs of this OS (Windows: win-x64, macOS: osx-arm64). RIDs of another OS and
    unknown RIDs are refused; Windows ARM64 (win-arm64) is not supported (T-130).

.PARAMETER SmokeSeconds
    How long the published app must keep running in the AOT smoke test. Default 10.

.PARAMETER FeedSigningKey
    Path of the encrypted private key of the update feed (offline media). Default: the environment variable
    LAYERPEEK_FEED_SIGNING_KEY. The file must be there at the start and again at the end of the build (keep the key medium
    connected). The password is asked for twice (not echoed): before the build and for signing after packaging, unless
    -FeedSigningKeyPassword is given.

.PARAMETER FeedSigningKeyPassword
    The password of -FeedSigningKey as a SecureString, for a run without a prompt (e.g. from another PowerShell script:
    & scripts/release-build.ps1 -FeedSigningKeyPassword (Read-Host -AsSecureString)). Never pass a password as plain text
    on a command line: it ends up in the shell history. Used for both steps (the check before the build and the signing
    after it); the script works on copies and disposes each right after use, the caller disposes the original.

.PARAMETER SkipFeedSignature
    A trial build without the update feed signature (no key needed). build-info.json marks it, and release-upload.ps1
    refuses to upload it.

.EXAMPLE
    pwsh scripts/release-build.ps1 -FeedSigningKey /Volumes/LP-KEY/layerpeek-feed-key.pem
    pwsh scripts/release-build.ps1 -Runtime win-x64 -FeedSigningKey E:\layerpeek-feed-key.pem
    pwsh scripts/release-build.ps1 -SkipFeedSignature
    pwsh scripts/release-build.ps1 -SignOnly -FeedSigningKey /Volumes/LP-KEY/layerpeek-feed-key.pem
#>
#Requires -Version 7.2
[CmdletBinding()]
param(
    [string[]] $Runtime,
    [ValidateRange(1, 600)] [int] $SmokeSeconds = 10,
    [string] $FeedSigningKey,
    [System.Security.SecureString] $FeedSigningKeyPassword,
    [switch] $SkipFeedSignature,
    [switch] $SignOnly
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools' 'ReleaseTools.psm1') -Force

# Physical path: dotnet restore breaks on symlinked paths (Resolve-PhysicalPath).
$repoRoot = Resolve-PhysicalPath (Join-Path $PSScriptRoot '..')
$solution = Join-Path $repoRoot 'LayerPeek.slnx'
$appProject = Join-Path $repoRoot 'src' 'LayerPeek.App' 'LayerPeek.App.csproj'
$smokeScript = Join-Path $PSScriptRoot 'aot-smoke.ps1'

# Packaging steps, run for every RID in this order. Each step gets a context object (Version, Runtime, Os,
# PublishDirectory, OutputDirectory), creates its files directly in OutputDirectory (dist/) and returns their paths.
# Returned files go automatically into the checksum file, the build marker and the upload.
# Installers are further steps (T-017: the Windows installer, T-019/T-058: macOS .app of vpk + .dmg + .tar.gz + feed); the build and
# publish part above them does not change. Os limits a step to RIDs of that OS.
$packagingSteps = @(
    # Windows: the .zip of the publish folder. macOS packs LayerPeek.app instead (step 'app' below, T-019).
    [pscustomobject]@{
        Name   = 'archive'
        Os     = @('windows')
        Invoke = {
            param($Context)
            $archive = Join-Path $Context.OutputDirectory (Get-ArchiveFileName -Version $Context.Version -Runtime $Context.Runtime)
            New-ReleaseArchive -SourceDirectory $Context.PublishDirectory -Destination $archive
        }
    },
    # T-017: the Velopack installer and its update feed (docs/velopack.md point 12). vpk works in its own folder next to
    # dist/ (deleted afterwards); only the expected files are copied into dist/ (Setup.exe renamed per version and RID).
    [pscustomobject]@{
        Name   = 'installer'
        Os     = @('windows')
        Invoke = {
            param($Context)
            $work = Join-Path (Split-Path -Parent $Context.OutputDirectory) "velopack-$($Context.Runtime)"
            if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
            $null = New-Item -ItemType Directory -Path (Join-Path $work 'out')
            try {
                $notes = Join-Path $work 'release-notes.md'
                $text = Get-ChangelogSection -Path (Join-Path $repoRoot 'CHANGELOG.md') -Version $Context.Version -AllowUndated
                [System.IO.File]::WriteAllText($notes, $text + "`n", [System.Text.UTF8Encoding]::new($false))
                $arguments = Get-VelopackPackArguments -Version $Context.Version -Runtime $Context.Runtime -PublishDirectory $Context.PublishDirectory `
                    -OutputDirectory (Join-Path $work 'out') -ReleaseNotesFile $notes -IconFile (Join-Path $repoRoot (Get-VelopackSettings).Icon)
                # A local tool: dotnet finds .config/dotnet-tools.json from the current folder.
                Push-Location $repoRoot
                # Its console output must not become a returned path.
                try { Invoke-Native "vpk pack $($Context.Runtime)" { dotnet @arguments | Out-Host } }
                finally { Pop-Location }
                Copy-VelopackOutput -VelopackDirectory (Join-Path $work 'out') -OutputDirectory $Context.OutputDirectory -Version $Context.Version -Runtime $Context.Runtime
            }
            finally {
                Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    },
    # T-019: LayerPeek.app (Info.plist with the bundle identifier and LSUIElement, icon from the 1024 px master, T-097: the
    # InfoPlist.strings of every app language, checked and linted with plutil), checked for the FFmpeg libraries and for
    # references to Homebrew (T-026). T-058 (decision Q9 a): vpk packs that finished bundle and signs it ad hoc inside out
    # with the hardened runtime and the entitlements of build/macos/LayerPeek.entitlements (T-108), adding the updater
    # UpdateMac and the manifest sq.version; the .app of its Portable.zip is what users get: verified (codesign --verify
    # --deep --strict, hardened runtime, Velopack manifest), smoke-tested as the bundle, then the .dmg (the app and a link to
    # /Applications) and the .tar.gz of it. The .app inside the .nupkg (what an update installs) gets the hardened-runtime
    # check too. dist gets the .dmg, the .tar.gz, the .nupkg and the feed of the osx-arm64 channel; no .pkg, no Portable.zip.
    # The signed app stays in artifacts/release/<version>/app/<rid>/portable/ for local use.
    [pscustomobject]@{
        Name   = 'app'
        Os     = @('macos')
        Invoke = {
            param($Context)
            $mac = Get-MacBundleSettings
            $names = Get-MacReleaseFileNames -Version $Context.Version -Runtime $Context.Runtime
            $work = Join-Path (Split-Path -Parent $Context.OutputDirectory) 'app' $Context.Runtime
            if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
            $bundleFolder = Join-Path $work 'bundle'
            $null = New-Item -ItemType Directory -Path $bundleFolder
            $exact = @{}
            foreach ($pixels in $mac.IconExact.Keys) { $exact[$pixels] = Join-Path $repoRoot $mac.IconExact[$pixels] }
            # T-097: the localized Info.plist values, one InfoPlist.strings per app language, checked before anything is built.
            $plistTemplate = Join-Path $repoRoot $mac.PlistTemplate
            $localizations = Join-Path $repoRoot $mac.Localizations
            $languages = @(Get-AppLanguageCodes ([System.IO.File]::ReadAllText((Join-Path $repoRoot $mac.AppLanguagesSource))))
            $localizationProblems = @(Get-MacLocalizationProblems -Directory $localizations -PlistText (Get-Content -LiteralPath $plistTemplate -Raw) -Languages $languages)
            if ($localizationProblems.Count -gt 0) { throw ($localizationProblems -join ' ') }
            $icon = New-MacIcon -MasterPng (Join-Path $repoRoot $mac.IconMaster) -ExactPngs $exact -Destination (Join-Path $work $mac.IconFile)
            $app = New-MacAppBundle -PublishDirectory $Context.PublishDirectory -Destination $bundleFolder -Version $Context.Version `
                -PlistTemplate $plistTemplate -IconFile $icon -LocalizationDirectory $localizations
            Remove-Item -LiteralPath $icon -Force
            $lintProblems = @(Get-MacPlistLintProblems $app)
            if ($lintProblems.Count -gt 0) { throw ($lintProblems -join ' ') }
            # T-026: the FFmpeg libraries are in the bundle and nothing loads from Homebrew or /usr/local.
            $libraryProblems = @(Get-MacBundleLibraryProblems $app)
            if ($libraryProblems.Count -gt 0) { throw ($libraryProblems -join ' ') }

            # T-058: vpk signs and packs the bundle (its own folder next to dist/, deleted afterwards except the app).
            $velopack = Join-Path $work 'velopack'
            $null = New-Item -ItemType Directory -Path (Join-Path $velopack 'out')
            $notes = Join-Path $velopack 'release-notes.md'
            $text = Get-ChangelogSection -Path (Join-Path $repoRoot 'CHANGELOG.md') -Version $Context.Version -AllowUndated
            [System.IO.File]::WriteAllText($notes, $text + "`n", [System.Text.UTF8Encoding]::new($false))
            $arguments = Get-VelopackMacPackArguments -Version $Context.Version -Runtime $Context.Runtime -AppPath $app `
                -OutputDirectory (Join-Path $velopack 'out') -ReleaseNotesFile $notes -Entitlements (Get-MacEntitlementsPath)
            Write-Host "==> Ad-hoc signing and packing $app (vpk)" -ForegroundColor Cyan
            # A local tool: dotnet finds .config/dotnet-tools.json from the current folder. Its console output must not become
            # a returned path.
            Push-Location $repoRoot
            try { Invoke-Native "vpk pack $($Context.Runtime)" { dotnet @arguments | Out-Host } }
            finally { Pop-Location }
            $feed = @(Copy-VelopackOutput -VelopackDirectory (Join-Path $velopack 'out') -OutputDirectory $Context.OutputDirectory -Version $Context.Version -Runtime $Context.Runtime)
            $portableFolder = Expand-ZipWithDitto -ZipPath (Join-Path $velopack 'out' (Get-VelopackPortableFileName $Context.Runtime)) -Destination (Join-Path $work 'portable')
            $signed = Join-Path $portableFolder $mac.BundleName
            $packageFolder = Expand-ZipWithDitto -ZipPath (Join-Path $Context.OutputDirectory @((Get-VelopackOutputMap -Version $Context.Version -Runtime $Context.Runtime).Keys)[0]) -Destination (Join-Path $velopack 'package')
            Remove-Item -LiteralPath $bundleFolder -Recurse -Force

            $problems = @(Get-MacSignatureProblems $signed) + @(Get-MacPlistLintProblems $signed) + @(Get-MacVelopackBundleProblems -AppPath $signed -Version $Context.Version -Runtime $Context.Runtime)
            if ($problems.Count -gt 0) { throw ($problems -join ' ') }
            Write-Host 'codesign --verify --deep --strict: valid; UpdateMac and sq.version present'
            # T-108: the hardened runtime on every Mach-O file and exactly the entitlements of build/macos/LayerPeek.entitlements,
            # in the app users install and in the app an update installs (the .nupkg).
            $runtimeProblems = @(Get-MacHardenedRuntimeProblems $signed) + @(Get-MacHardenedRuntimeProblems (Join-Path $packageFolder 'lib' 'app'))
            if ($runtimeProblems.Count -gt 0) { throw ($runtimeProblems -join ' ') }
            Write-Host "Hardened runtime on every Mach-O file (Portable.zip and .nupkg), entitlements: $($mac.EntitlementKeys -join ', ')"
            Remove-Item -LiteralPath $velopack -Recurse -Force
            if (Test-RuntimeRunnable -Runtime $Context.Runtime -HostOs $Context.Os -HostArchitecture (Get-HostArchitecture)) {
                # The bundle itself: its Info.plist (bundle identifier, LSUIElement) and its signature are what users start.
                $before = Get-FolderSnapshot $signed
                # Its console output must not become a returned path.
                Invoke-Native "AOT smoke test $($Context.Runtime) (LayerPeek.app)" { Invoke-Pwsh -NoProfile -File $smokeScript -Runtime $Context.Runtime -PublishDir (Join-Path $signed 'Contents' 'MacOS') -Seconds $SmokeSeconds | Out-Host }
                if ((Get-FolderSnapshot $signed) -ne $before -or @(Get-MacSignatureProblems $signed).Count -gt 0) { throw "The smoke run changed $signed; it must not be packaged." }
                $smoke["$($Context.Runtime) LayerPeek.app"] = 'passed'
            }
            $archive = New-ReleaseArchive -SourceDirectory $portableFolder -Destination (Join-Path $Context.OutputDirectory $names.Archive)
            $diskImage = New-MacDiskImage -AppPath $signed -Destination (Join-Path $Context.OutputDirectory $names.DiskImage) -WorkDirectory $work
            $diskImage
            $archive
            $feed
        }
    },
    # T-199 (LGPL, https://ffmpeg.org/legal.html): the exact source of the FFmpeg libraries of this OS with how they were
    # built, verified before the build (Get-FFmpegSourceInputs below). One file per OS: there is one RID per OS, and the
    # FFmpeg build of build/FFmpeg.targets is that of the OS (a second RID of an OS would return the same name twice).
    [pscustomobject]@{
        Name   = 'ffmpeg-source'
        Os     = @('windows', 'macos')
        Invoke = {
            param($Context)
            $work = Join-Path (Split-Path -Parent $Context.OutputDirectory) "ffmpeg-source-$($Context.Runtime)"
            if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
            New-FFmpegSourceArchive -Source $ffmpegSource -PublishDirectory $Context.PublishDirectory -AppVersion $Context.Version -RepoRoot $repoRoot `
                -Destination (Join-Path $Context.OutputDirectory (Get-FFmpegSourceFileName -Version $Context.Version -Os $Context.Os)) -WorkDirectory $work
        }
    }
)

function Invoke-Native {
    param([Parameter(Mandatory)] [string] $Description, [Parameter(Mandatory)] [scriptblock] $Command)
    Write-Host "==> $Description" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Description failed with exit code $LASTEXITCODE." }
}

function Get-FeedSigning {
    # T-181: checks the signing key before the long build (Confirm-FeedSigningKey) and returns only what the signing after
    # packaging needs to read and check the key again: its full path, the SHA-256 of its public key and the trusted
    # (public) keys; $null for -SkipFeedSignature. The key, its text and the password copy are already gone when this
    # returns (decision of question 17).
    if ($SkipFeedSignature) {
        if ($FeedSigningKey -or $FeedSigningKeyPassword) { throw '-SkipFeedSignature builds without the update feed signature; do not pass -FeedSigningKey or -FeedSigningKeyPassword with it.' }
        Write-Host 'Trial build without the update feed signature (-SkipFeedSignature): release-upload.ps1 will refuse it.' -ForegroundColor Yellow
        return $null
    }
    $keyPath = Resolve-FeedSigningKeyPath -Alternative 'for a trial build that cannot be uploaded use -SkipFeedSignature'
    $trusted = @(Get-ProductionFeedKeys)
    $checked = Confirm-FeedSigningKey -KeyPath $keyPath -TrustedKeys $trusted -Password $FeedSigningKeyPassword
    Write-Host "Update feed signing key accepted (public key SHA-256 $($checked.PublicKeySha256), trusted by UpdateFeedKeys.$((Get-FeedSignatureSettings).KeysList)). Keep the key medium connected: the feeds are signed with it at the end of the build, and the password is asked for again then."
    [pscustomobject]@{ KeyPath = $checked.KeyPath; PublicKeySha256 = $checked.PublicKeySha256; TrustedKeys = $trusted }
}

function Resolve-FeedSigningKeyPath {
    # The full path of the signing key from -FeedSigningKey or LAYERPEEK_FEED_SIGNING_KEY: the signing must read the same
    # file whatever the current folder is then. Alternative completes the error message without a key.
    param([Parameter(Mandatory)] [string] $Alternative)
    $variable = (Get-FeedSignatureSettings).KeyVariable
    $keyPath = if ($FeedSigningKey) { $FeedSigningKey } else { [Environment]::GetEnvironmentVariable($variable) }
    if (-not $keyPath) {
        throw "No update feed signing key. Connect the key medium and pass -FeedSigningKey <path> or set $variable (docs/release.md, 'Podpis feedu aktualizací'); $Alternative."
    }
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($keyPath)
}

function Get-ProductionFeedKeys {
    # The public keys the app of this checkout trusts (UpdateFeedKeys.Production, read from its source).
    Get-TrustedFeedKeys ([System.IO.File]::ReadAllText((Join-Path $repoRoot (Get-FeedSignatureSettings).KeysSource)))
}

function Write-BuildInfo {
    param([Parameter(Mandatory)] [System.Collections.IDictionary] $BuildInfo, [Parameter(Mandatory)] [string] $ReleaseDirectory)
    $BuildInfo | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Get-BuildInfoPath $ReleaseDirectory) -Encoding utf8NoBOM
}

function Add-FeedSignatureFiles {
    # T-181: signs the update feed of every RID of the build with the key read and decrypted again (the same key as checked
    # before the build, Signing.PublicKeySha256), verified against the keys of the app; then the key is gone. Adds the .sig
    # files to the build record and marks it signed. A .sig written before a failure is deleted again, so that the pending
    # record still matches dist/ and -SignOnly can sign it (T-197).
    param(
        [Parameter(Mandatory)] [System.Collections.IDictionary] $BuildInfo,
        [Parameter(Mandatory)] [string] $DistDirectory,
        [Parameter(Mandatory)] $Signing,
        [Parameter(Mandatory)] [string] $Purpose
    )
    $signatures = @($BuildInfo.Runtimes | ForEach-Object { Join-Path $DistDirectory (Get-UpdateFeedSignatureFileName $_) })
    $new = @($signatures | Where-Object { -not (Test-Path -LiteralPath $_) })
    try {
        $null = Invoke-FeedSigning -KeyPath $Signing.KeyPath -ExpectedPublicKeySha256 $Signing.PublicKeySha256 -TrustedKeys $Signing.TrustedKeys `
            -DistDirectory $DistDirectory -Runtimes $BuildInfo.Runtimes -Password $FeedSigningKeyPassword -Purpose $Purpose
    }
    catch {
        foreach ($path in $new) { Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue }
        throw
    }
    $BuildInfo.Files = @($BuildInfo.Files) + @(foreach ($rid in $BuildInfo.Runtimes) {
            $name = Get-UpdateFeedSignatureFileName $rid
            Write-Host "$name verified with the keys of UpdateFeedKeys.$((Get-FeedSignatureSettings).KeysList)"
            [pscustomobject]@{ Name = $name; Runtime = $rid; Step = 'feed-signature'; Sha256 = Get-FileSha256 (Join-Path $DistDirectory $name) }
        })
    $BuildInfo.FeedSignature = [ordered]@{ Status = 'signed'; PublicKeySha256 = $Signing.PublicKeySha256 }
}

function Complete-BuildOutput {
    # The checksum file over every file of the build (the .sig files too), verified, and the final build-info.json (it
    # replaces the pending record of T-197); then the summary.
    param([Parameter(Mandatory)] [System.Collections.IDictionary] $BuildInfo, [Parameter(Mandatory)] [string] $ReleaseDirectory)
    $distDir = Join-Path $ReleaseDirectory 'dist'
    $null = Write-Sha256Sums -Directory $distDir -FileName $BuildInfo.ChecksumFile -Files @($BuildInfo.Files | ForEach-Object Name)
    $sumsProblems = @(Get-Sha256SumsProblems (Join-Path $distDir $BuildInfo.ChecksumFile))
    if ($sumsProblems.Count -gt 0) { throw "Checksum verification failed: $($sumsProblems -join ' ')" }

    # Re-checked at the end: a change during the build also makes it unreleasable.
    $BuildInfo.Dirty = $BuildInfo.Dirty -or @(Get-GitStatusLines $repoRoot).Count -gt 0 -or (Get-GitHead $repoRoot) -ne $BuildInfo.Commit
    Write-BuildInfo $BuildInfo $ReleaseDirectory

    Write-Host ''
    Write-Host "Release files in $($distDir):" -ForegroundColor Green
    foreach ($name in @($BuildInfo.Files | ForEach-Object Name) + $BuildInfo.ChecksumFile) {
        $length = (Get-Item -LiteralPath (Join-Path $distDir $name)).Length
        Write-Host ("  {0,-48} {1,10:N1} MB" -f $name, ($length / 1MB))
    }
    foreach ($entry in @($BuildInfo.Smoke.GetEnumerator())) { Write-Host "  AOT smoke test $($entry.Key): $($entry.Value)" }
    if ($BuildInfo.Dirty) { Write-Host 'Built from a dirty working tree: release-upload.ps1 will refuse this output.' -ForegroundColor Yellow }
    if ($BuildInfo.FeedSignature.Status -ceq 'skipped') { Write-Host 'Built with -SkipFeedSignature: the update feed is not signed and release-upload.ps1 will refuse this output.' -ForegroundColor Yellow }
    Write-Host 'Next step: pwsh scripts/release-upload.ps1 -DryRun' -ForegroundColor Green
}

function Invoke-SignOnly {
    # T-197: signs the dist/ of a build whose signing did not finish. Nothing is signed unless the checkout is the clean,
    # tagged commit of the build and dist/ is exactly the packaged files of the pending record (Get-SignOnlyProblems); the
    # key checks are those of the signing at the end of the build (Invoke-FeedSigning), with the key the build checked.
    $hostOs = Get-HostOs
    $version = Get-SourceVersion (Join-Path $repoRoot 'Directory.Build.props')
    $releaseDir = Join-Path $repoRoot 'artifacts' 'release' $version
    $distDir = Join-Path $releaseDir 'dist'
    Write-Host "Signing the finished build $version ($hostOs) in $distDir without building it again (-SignOnly)"

    $state = Get-ReleaseGitState -RepoRoot $repoRoot -Tag (ConvertTo-ReleaseTag $version) -LocalOnly
    $buildInfoPath = Get-BuildInfoPath $releaseDir
    $record = $null
    if (Test-Path -LiteralPath $buildInfoPath -PathType Leaf) {
        try { $record = Get-Content -LiteralPath $buildInfoPath -Raw | ConvertFrom-Json }
        catch { throw "$buildInfoPath is no build record (not JSON). Nothing was signed; build again with the signing key (docs/release.md)." }
    }
    # @(): 'if' as an expression would turn an empty list into $null.
    $distFiles = @(if (Test-Path -LiteralPath $distDir -PathType Container) {
            Get-ChildItem -LiteralPath $distDir -File -Force | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Sha256 = Get-FileSha256 $_.FullName } }
        })
    $problems = @(Get-ReleaseGitProblems -State $state -Version $version) +
        @(Get-SignOnlyProblems -BuildInfo $record -Version $version -Head $state.Head -Os $hostOs -DistFiles $distFiles)
    if ($problems.Count -gt 0) { throw ($problems -join ' ') }
    Write-Host "dist/ matches the build record of commit $($state.Head) (tag $(ConvertTo-ReleaseTag $version), clean tree): $(@($record.Files).Count) files, SHA-256 unchanged."

    # The key checks before the password, as before the build: the list is not empty and trusts the key the build checked.
    $keyPath = Resolve-FeedSigningKeyPath -Alternative 'without a key nothing can be signed'
    $trusted = @(Get-ProductionFeedKeys)
    $publicKeySha256 = $record.FeedSignature.PublicKeySha256
    $trustProblems = @(Get-FeedSigningTrustProblems -TrustedKeys $trusted -PublicKeySha256 $publicKeySha256)
    if ($trustProblems.Count -gt 0) { throw ($trustProblems -join ' ') }

    # The rest of the record as the build wrote it (ConvertFrom-Json reads the time as a DateTime, the smoke results as an object).
    $created = if ($record.PSObject.Properties['CreatedUtc'] -and $record.CreatedUtc -is [datetime]) { $record.CreatedUtc.ToUniversalTime().ToString('o') }
    elseif ($record.PSObject.Properties['CreatedUtc']) { "$($record.CreatedUtc)" }
    else { $null }
    $smoke = [ordered]@{}
    if ($record.PSObject.Properties['Smoke'] -and $record.Smoke) { foreach ($property in $record.Smoke.PSObject.Properties) { $smoke[$property.Name] = $property.Value } }
    $buildInfo = [ordered]@{
        Version       = $record.Version
        Commit        = $record.Commit
        Dirty         = $false
        Os            = $record.Os
        Runtimes      = @($record.Runtimes)
        ChecksumFile  = $record.ChecksumFile
        Files         = @($record.Files | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Runtime = $_.Runtime; Step = $_.Step; Sha256 = $_.Sha256 } })
        FeedSignature = [ordered]@{ Status = 'pending'; PublicKeySha256 = $publicKeySha256 }
        Smoke         = $smoke
        CreatedUtc    = $created
    }
    Write-Host "==> Signing the update feeds with the key checked before the build (public key SHA-256 $publicKeySha256)" -ForegroundColor Cyan
    Add-FeedSignatureFiles -BuildInfo $buildInfo -DistDirectory $distDir -Purpose 'sign the update feeds of the finished build' `
        -Signing ([pscustomobject]@{ KeyPath = $keyPath; PublicKeySha256 = $publicKeySha256; TrustedKeys = $trusted })
    Complete-BuildOutput -BuildInfo $buildInfo -ReleaseDirectory $releaseDir
}

function Get-FolderSnapshot {
    # Relative path, size and write time of every file: proves that the smoke run left the publish folder unchanged.
    param([Parameter(Mandatory)] [string] $Path)
    (Get-ChildItem -LiteralPath $Path -Recurse -Force -File |
        ForEach-Object { "$([System.IO.Path]::GetRelativePath($Path, $_.FullName))|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)" } |
        Sort-Object) -join "`n"
}

$feedSigning = $null
try {
    if ($SignOnly) {
        $conflicts = @('Runtime', 'SmokeSeconds', 'SkipFeedSignature' | Where-Object { $PSBoundParameters.ContainsKey($_) })
        if ($conflicts.Count -gt 0) { throw "-SignOnly signs the finished build as it is; do not pass $(($conflicts | ForEach-Object { "-$_" }) -join ', ') with it." }
        Invoke-SignOnly
        exit 0
    }

    $hostOs = Get-HostOs
    $hostArchitecture = Get-HostArchitecture
    $config = Get-ReleaseRuntimeConfig
    $runtimes = @(Resolve-ReleaseRuntimes -Requested $Runtime -HostOs $hostOs)
    $version = Get-SourceVersion (Join-Path $repoRoot 'Directory.Build.props')
    $head = Get-GitHead $repoRoot
    $dirty = @(Get-GitStatusLines $repoRoot).Count -gt 0

    Write-Host "Release build $version ($hostOs, $($runtimes -join ', ')) of commit $head"
    if ($dirty) {
        Write-Host 'The working tree has uncommitted or untracked files: this build cannot be uploaded.' -ForegroundColor Yellow
    }
    foreach ($rid in $runtimes) {
        if (-not $config[$rid].Uploadable) {
            Write-Host "$rid is built for local verification only and cannot be uploaded: $($config[$rid].BlockedReason)." -ForegroundColor Yellow
        }
    }

    # T-181: the signing key is checked (and its password asked for) before the long build, not after it.
    $feedSigning = Get-FeedSigning

    # T-199: the pinned FFmpeg source of this OS (cached, verified) before the long build, for the 'ffmpeg-source' step.
    $ffmpegSource = if ($hostOs -in 'windows', 'macos') { Get-FFmpegSourceInputs -RepoRoot $repoRoot -Os $hostOs } else { $null }

    # Fails before the long build when the AOT toolchain of a requested RID is missing (shared with scripts/release.ps1).
    $toolchainProblems = @(Get-NativeToolchainProblems -Runtimes $runtimes -HostOs $hostOs)
    if ($toolchainProblems.Count -gt 0) { throw ($toolchainProblems -join ' ') }
    # The Velopack steps (T-017 Windows installer, T-058 macOS app): vpk pinned in .config/dotnet-tools.json, the same version
    # as the NuGet package.
    if ($hostOs -in 'windows', 'macos') {
        $velopackProblems = @(Get-VelopackVersionProblems -PackagesPropsPath (Join-Path $repoRoot 'Directory.Packages.props') -ToolManifestPath (Join-Path $repoRoot '.config' 'dotnet-tools.json'))
        if ($velopackProblems.Count -gt 0) { throw ($velopackProblems -join ' ') }
        Push-Location $repoRoot
        try { Invoke-Native 'dotnet tool restore (vpk)' { dotnet tool restore } }
        finally { Pop-Location }
    }

    $releaseDir = Join-Path $repoRoot 'artifacts' 'release' $version
    $publishRoot = Join-Path $releaseDir 'publish'
    $distDir = Join-Path $releaseDir 'dist'
    if (Test-Path -LiteralPath $releaseDir) {
        Write-Host "Deleting the previous output in $releaseDir"
        Remove-Item -LiteralPath $releaseDir -Recurse -Force
    }
    $null = New-Item -ItemType Directory -Path $publishRoot, $distDir

    # -warnaserror: any MSBuild warning (including trim/AOT warnings of the ILC step) stops the release.
    Invoke-Native 'dotnet build (Release)' { dotnet build $solution -c Release -warnaserror }
    Invoke-Native 'dotnet test (Release)' { dotnet test --solution $solution -c Release --no-build }

    $smoke = [ordered]@{}
    foreach ($rid in $runtimes) {
        $publishDir = Join-Path $publishRoot $rid
        Invoke-Native "dotnet publish $rid (Native AOT)" { dotnet publish $appProject -c Release -r $rid -o $publishDir -warnaserror }

        $exe = Join-Path $publishDir ($hostOs -eq 'windows' ? 'LayerPeek.exe' : 'LayerPeek')
        if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Published executable not found: $exe" }
        if ($hostOs -eq 'windows') {
            # ProductVersion is the informational version "X.Y.Z+<commit>"; it must come from Directory.Build.props.
            $productVersion = ((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion -split '\+')[0]
            if ($productVersion -ne $version) { throw "$exe has product version '$productVersion', expected $version." }
            Write-Host "Product version of the exe: $productVersion"
        }

        if (Test-RuntimeRunnable -Runtime $rid -HostOs $hostOs -HostArchitecture $hostArchitecture) {
            $before = Get-FolderSnapshot $publishDir
            Invoke-Native "AOT smoke test $rid" { Invoke-Pwsh -NoProfile -File $smokeScript -Runtime $rid -PublishDir $publishDir -Seconds $SmokeSeconds }
            if ((Get-FolderSnapshot $publishDir) -ne $before) { throw "The smoke run changed the publish folder $publishDir; it must not be packaged." }
            $smoke[$rid] = 'passed'
        }
        else {
            $smoke[$rid] = "not verified by running (this machine is $hostOs-$hostArchitecture)"
        }
    }

    $files = [System.Collections.Generic.List[object]]::new()
    foreach ($rid in $runtimes) {
        $context = [pscustomobject]@{
            Version          = $version
            Runtime          = $rid
            Os               = $hostOs
            PublishDirectory = Join-Path $publishRoot $rid
            OutputDirectory  = $distDir
        }
        foreach ($step in @($packagingSteps | Where-Object { $_.Os -contains $hostOs })) {
            Write-Host "==> Packaging $rid ($($step.Name))" -ForegroundColor Cyan
            foreach ($path in @(& $step.Invoke $context)) {
                $item = Get-Item -LiteralPath $path
                if ($item.DirectoryName -ne (Get-Item -LiteralPath $distDir).FullName) { throw "Packaging step '$($step.Name)' created $path outside $distDir." }
                if ($files | Where-Object { $_.Name -ceq $item.Name }) { throw "Packaging step '$($step.Name)' returned $($item.Name) twice." }
                $files.Add([pscustomobject]@{ Name = $item.Name; Runtime = $rid; Step = $step.Name; Sha256 = Get-FileSha256 $item.FullName })
            }
        }
    }

    $buildInfo = [ordered]@{
        Version       = $version
        Commit        = $head
        Dirty         = $dirty -or @(Get-GitStatusLines $repoRoot).Count -gt 0 -or (Get-GitHead $repoRoot) -ne $head
        Os            = $hostOs
        Runtimes      = $runtimes
        ChecksumFile  = Get-ChecksumFileName $hostOs
        Files         = $files.ToArray()
        FeedSignature = $feedSigning ? [ordered]@{ Status = 'pending'; PublicKeySha256 = $feedSigning.PublicKeySha256 } : [ordered]@{ Status = 'skipped'; PublicKeySha256 = $null }
        Smoke         = $smoke
        CreatedUtc    = [DateTime]::UtcNow.ToString('o')
    }
    if ($feedSigning) {
        # T-197: the record of the packaged files before the key is needed again; release-upload.ps1 refuses it (pending),
        # and when the signing below fails, release-build.ps1 -SignOnly signs exactly these files later.
        Write-BuildInfo $buildInfo $releaseDir
        Write-Host '==> Signing the update feeds (key medium and password needed again)' -ForegroundColor Cyan
        Add-FeedSignatureFiles -BuildInfo $buildInfo -DistDirectory $distDir -Signing $feedSigning -Purpose 'second time: sign the update feeds'
    }
    Complete-BuildOutput -BuildInfo $buildInfo -ReleaseDirectory $releaseDir
    exit 0
}
catch {
    Write-Host "Release build FAILED: $_" -ForegroundColor Red
    exit 1
}
