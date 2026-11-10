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
                                                     releases.<rid>.json.sig (T-181) + SHA256SUMS-<os>.txt
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
    list; then the key is disposed and the key medium may be disconnected. After packaging the key is decrypted once more
    from the text read at the start, signs the feeds, every signature is verified against the trusted keys, and the key and
    the password are disposed. The key is never copied, printed or written; build-info.json names only the SHA-256 of the
    public key. Without a key the build fails, unless -SkipFeedSignature makes a trial build that release-upload.ps1 refuses.

.PARAMETER Runtime
    RIDs to build. Default: the default RIDs of this OS (Windows: win-x64, macOS: osx-arm64). RIDs of another OS and
    unknown RIDs are refused; Windows ARM64 (win-arm64) is not supported (T-130).

.PARAMETER SmokeSeconds
    How long the published app must keep running in the AOT smoke test. Default 10.

.PARAMETER FeedSigningKey
    Path of the encrypted private key of the update feed (offline media). Default: the environment variable
    LAYERPEEK_FEED_SIGNING_KEY. The password is asked for (not echoed) unless -FeedSigningKeyPassword is given.

.PARAMETER FeedSigningKeyPassword
    The password of -FeedSigningKey as a SecureString, for a run without a prompt (e.g. from another PowerShell script:
    & scripts/release-build.ps1 -FeedSigningKeyPassword (Read-Host -AsSecureString)). Never pass a password as plain text
    on a command line: it ends up in the shell history. The script disposes its own copy after signing.

.PARAMETER SkipFeedSignature
    A trial build without the update feed signature (no key needed). build-info.json marks it, and release-upload.ps1
    refuses to upload it.

.EXAMPLE
    pwsh scripts/release-build.ps1 -FeedSigningKey /Volumes/LP-KEY/layerpeek-feed-key.pem
    pwsh scripts/release-build.ps1 -Runtime win-x64 -FeedSigningKey E:\layerpeek-feed-key.pem
    pwsh scripts/release-build.ps1 -SkipFeedSignature
#>
#Requires -Version 7.2
[CmdletBinding()]
param(
    [string[]] $Runtime,
    [ValidateRange(1, 600)] [int] $SmokeSeconds = 10,
    [string] $FeedSigningKey,
    [System.Security.SecureString] $FeedSigningKeyPassword,
    [switch] $SkipFeedSignature
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
    }
)

function Invoke-Native {
    param([Parameter(Mandatory)] [string] $Description, [Parameter(Mandatory)] [scriptblock] $Command)
    Write-Host "==> $Description" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Description failed with exit code $LASTEXITCODE." }
}

function Get-FeedSigning {
    # T-181: checks the signing key before the long build. Returns the encrypted key text, the password (a copy owned by
    # this script) and the trusted keys, or $null for -SkipFeedSignature. The key itself is disposed again right here.
    if ($SkipFeedSignature) {
        if ($FeedSigningKey -or $FeedSigningKeyPassword) { throw '-SkipFeedSignature builds without the update feed signature; do not pass -FeedSigningKey or -FeedSigningKeyPassword with it.' }
        Write-Host 'Trial build without the update feed signature (-SkipFeedSignature): release-upload.ps1 will refuse it.' -ForegroundColor Yellow
        return $null
    }
    $settings = Get-FeedSignatureSettings
    $keyPath = if ($FeedSigningKey) { $FeedSigningKey } else { [Environment]::GetEnvironmentVariable($settings.KeyVariable) }
    if (-not $keyPath) {
        throw "No update feed signing key. Connect the key medium and pass -FeedSigningKey <path> or set $($settings.KeyVariable) (docs/release.md, 'Podpis feedu aktualizací'); for a trial build that cannot be uploaded use -SkipFeedSignature."
    }
    $encrypted = Read-FeedSigningKeyFile $keyPath
    # An empty list is refused before the password is asked for: no key could be accepted.
    $trusted = @(Get-TrustedFeedKeys ([System.IO.File]::ReadAllText((Join-Path $repoRoot $settings.KeysSource))))
    $trustProblems = @(Get-FeedSigningTrustProblems -TrustedKeys $trusted)
    if ($trustProblems.Count -gt 0) { throw ($trustProblems -join ' ') }
    $password = if ($FeedSigningKeyPassword) { $FeedSigningKeyPassword.Copy() } else { Read-FeedSigningKeyPassword $keyPath }
    try {
        $key = Import-FeedSigningKey -EncryptedPem $encrypted -Password $password
        try { $publicKey = $key.ExportSubjectPublicKeyInfo() }
        finally { $key.Dispose() }
        $trustProblems = @(Get-FeedSigningTrustProblems -TrustedKeys $trusted -PublicKey $publicKey)
        if ($trustProblems.Count -gt 0) { throw ($trustProblems -join ' ') }
    }
    catch {
        $password.Dispose()
        throw
    }
    $fingerprint = Get-FeedKeyFingerprint $publicKey
    Write-Host "Update feed signing key accepted (public key SHA-256 $fingerprint, trusted by UpdateFeedKeys.$($settings.KeysList)). The key medium may be disconnected now."
    [pscustomobject]@{ EncryptedPem = $encrypted; Password = $password; TrustedKeys = $trusted; PublicKeySha256 = $fingerprint }
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

    # T-181: the signature of every update feed, verified against the keys of the app; then the key is gone.
    if ($feedSigning) {
        Write-Host '==> Signing the update feeds' -ForegroundColor Cyan
        $key = Import-FeedSigningKey -EncryptedPem $feedSigning.EncryptedPem -Password $feedSigning.Password
        try { $null = Add-UpdateFeedSignatures -Key $key -DistDirectory $distDir -Runtimes $runtimes -TrustedKeys $feedSigning.TrustedKeys }
        finally {
            $key.Dispose()
            $feedSigning.Password.Dispose()
        }
        foreach ($rid in $runtimes) {
            $name = Get-UpdateFeedSignatureFileName $rid
            $files.Add([pscustomobject]@{ Name = $name; Runtime = $rid; Step = 'feed-signature'; Sha256 = Get-FileSha256 (Join-Path $distDir $name) })
            Write-Host "$name verified with the keys of UpdateFeedKeys.$((Get-FeedSignatureSettings).KeysList)"
        }
        $feedSignature = [ordered]@{ Status = 'signed'; PublicKeySha256 = $feedSigning.PublicKeySha256 }
    }
    else {
        $feedSignature = [ordered]@{ Status = 'skipped'; PublicKeySha256 = $null }
    }

    $checksumFile = Get-ChecksumFileName $hostOs
    $null = Write-Sha256Sums -Directory $distDir -FileName $checksumFile -Files @($files | ForEach-Object Name)
    $sumsProblems = @(Get-Sha256SumsProblems (Join-Path $distDir $checksumFile))
    if ($sumsProblems.Count -gt 0) { throw "Checksum verification failed: $($sumsProblems -join ' ')" }

    # Re-checked at the end: a change during the build also makes it unreleasable.
    $dirty = $dirty -or @(Get-GitStatusLines $repoRoot).Count -gt 0 -or (Get-GitHead $repoRoot) -ne $head
    $buildInfo = [ordered]@{
        Version       = $version
        Commit        = $head
        Dirty         = $dirty
        Os            = $hostOs
        Runtimes      = $runtimes
        ChecksumFile  = $checksumFile
        Files         = $files.ToArray()
        FeedSignature = $feedSignature
        Smoke         = $smoke
        CreatedUtc    = [DateTime]::UtcNow.ToString('o')
    }
    $buildInfo | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Get-BuildInfoPath $releaseDir) -Encoding utf8NoBOM

    Write-Host ''
    Write-Host "Release files in $($distDir):" -ForegroundColor Green
    foreach ($file in @($files) + [pscustomobject]@{ Name = $checksumFile }) {
        $length = (Get-Item -LiteralPath (Join-Path $distDir $file.Name)).Length
        Write-Host ("  {0,-48} {1,10:N1} MB" -f $file.Name, ($length / 1MB))
    }
    foreach ($rid in $smoke.Keys) { Write-Host "  AOT smoke test ${rid}: $($smoke[$rid])" }
    if ($dirty) { Write-Host 'Built from a dirty working tree: release-upload.ps1 will refuse this output.' -ForegroundColor Yellow }
    if (-not $feedSigning) { Write-Host 'Built with -SkipFeedSignature: the update feed is not signed and release-upload.ps1 will refuse this output.' -ForegroundColor Yellow }
    Write-Host 'Next step: pwsh scripts/release-upload.ps1 -DryRun' -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "Release build FAILED: $_" -ForegroundColor Red
    exit 1
}
finally {
    # The password of the signing key never outlives the run (already disposed after signing; again on failure).
    if ($feedSigning) { $feedSigning.Password.Dispose() }
}
