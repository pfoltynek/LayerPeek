#Requires -Version 7.2
<#
.SYNOPSIS
    Shared helpers of the release scripts (T-055): version, tag, changelog, runtime selection, git state checks,
    build marker, archives, checksums and the GitHub upload plan.

.DESCRIPTION
    Pure functions (no git, gh or dotnet calls) take plain values and return values or problem lists, so that the
    Pester tests (scripts/ReleaseTools/ReleaseTools.Tests.ps1) can check them. Functions that query git or gh
    only collect facts; the decisions are made by the pure functions.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Single configuration point of the released runtimes.
#   Os          host OS that can build it (Native AOT does not compile across operating systems)
#   Default     built when the build script gets no -Runtime
#   Uploadable  its files may be uploaded to GitHub
# win-arm64 stays off until T-057 ships the ARM64 FFmpeg libraries (camera). Enabling it (Default and Uploadable)
# is a deliberate change here after T-057 is done.
$script:ReleaseRuntimes = [ordered]@{
    'win-x64'   = [pscustomobject]@{ Os = 'windows'; Default = $true; Uploadable = $true; BlockedReason = $null }
    'win-arm64' = [pscustomobject]@{ Os = 'windows'; Default = $false; Uploadable = $false; BlockedReason = 'win-arm64 is not released until T-057 (FFmpeg for ARM64) is done' }
    'osx-arm64' = [pscustomobject]@{ Os = 'macos'; Default = $true; Uploadable = $true; BlockedReason = $null }
}

# Name of the build marker written next to dist/ by the build script and checked by the upload script.
$script:BuildInfoFileName = 'build-info.json'

function Get-ReleaseRuntimeConfig {
    <# .SYNOPSIS Returns the runtime configuration (ordered dictionary RID -> settings). #>
    $script:ReleaseRuntimes
}

function Get-HostOs {
    <# .SYNOPSIS 'windows' or 'macos'; throws on other operating systems. #>
    if ($IsWindows) { return 'windows' }
    if ($IsMacOS) { return 'macos' }
    throw 'Releases are built only on Windows or macOS.'
}

function Get-HostArchitecture {
    <# .SYNOPSIS 'x64' or 'arm64' of the operating system (not of the PowerShell process). #>
    [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
}

function Get-RuntimeOs {
    <# .SYNOPSIS Operating system of a configured RID ('windows' / 'macos'). #>
    param([Parameter(Mandatory)] [string] $Runtime, $Config = $script:ReleaseRuntimes)
    if (-not $Config.Contains($Runtime)) { throw "Unknown runtime '$Runtime'. Released runtimes: $($Config.Keys -join ', ')." }
    $Config[$Runtime].Os
}

function Resolve-ReleaseRuntimes {
    <#
    .SYNOPSIS
        Validates the requested RIDs for this host. Without a request returns the default RIDs of the host OS.
        Throws for unknown RIDs and RIDs of another OS.
    #>
    param(
        [string[]] $Requested,
        [Parameter(Mandatory)] [string] $HostOs,
        $Config = $script:ReleaseRuntimes
    )
    # 'pwsh -File' passes 'a,b' as one string: accept comma-separated lists too.
    $requestedRids = @($Requested | ForEach-Object { $_ -split ',' } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($requestedRids.Count -eq 0) {
        $defaults = @($Config.Keys | Where-Object { $Config[$_].Os -eq $HostOs -and $Config[$_].Default })
        if ($defaults.Count -eq 0) { throw "No default runtime is configured for $HostOs." }
        return $defaults
    }
    $result = [System.Collections.Generic.List[string]]::new()
    foreach ($rid in $requestedRids) {
        $key = $rid.Trim().ToLowerInvariant()
        if (-not $Config.Contains($key)) {
            throw "Unknown runtime '$rid'. Released runtimes: $($Config.Keys -join ', ')."
        }
        $ridOs = $Config[$key].Os
        if ($ridOs -ne $HostOs) {
            throw "Runtime '$key' cannot be built on $($HostOs): Native AOT does not compile across operating systems. Build it on $ridOs."
        }
        if (-not $result.Contains($key)) { $result.Add($key) }
    }
    $result.ToArray()
}

function Test-RuntimeRunnable {
    <# .SYNOPSIS True when a RID matches this machine's OS and architecture, i.e. its AOT binary can be smoke-tested here. #>
    param([Parameter(Mandatory)] [string] $Runtime, [Parameter(Mandatory)] [string] $HostOs, [Parameter(Mandatory)] [string] $HostArchitecture)
    $prefix = if ($HostOs -eq 'windows') { 'win' } else { 'osx' }
    $Runtime -eq "$prefix-$HostArchitecture"
}

function Test-ReleaseVersion {
    <# .SYNOPSIS True for a plain X.Y.Z version (no leading zeros, no suffix). #>
    param([AllowNull()] [AllowEmptyString()] [string] $Version)
    [bool]($Version -match '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')
}

function Get-SourceVersion {
    <# .SYNOPSIS Reads the single <Version> from the root Directory.Build.props. #>
    param([Parameter(Mandatory)] [string] $PropsPath)
    if (-not (Test-Path -LiteralPath $PropsPath -PathType Leaf)) { throw "Version source not found: $PropsPath" }
    [xml] $xml = Get-Content -LiteralPath $PropsPath -Raw
    $nodes = @($xml.SelectNodes('/Project/PropertyGroup/Version'))
    if ($nodes.Count -ne 1) { throw "Expected exactly one <Version> in $PropsPath, found $($nodes.Count)." }
    $version = $nodes[0].InnerText.Trim()
    if (-not (Test-ReleaseVersion $version)) { throw "<Version> '$version' in $PropsPath is not in the form X.Y.Z." }
    $version
}

function ConvertTo-ReleaseTag {
    <# .SYNOPSIS 'X.Y.Z' -> 'vX.Y.Z'. #>
    param([Parameter(Mandatory)] [string] $Version)
    "v$Version"
}

function Get-VersionFromTag {
    <# .SYNOPSIS 'vX.Y.Z' -> 'X.Y.Z'; $null for anything else. #>
    param([AllowNull()] [AllowEmptyString()] [string] $Tag)
    if ($Tag -match '^v(?<version>\d+\.\d+\.\d+)$' -and (Test-ReleaseVersion $Matches.version)) { return $Matches.version }
    $null
}

function Test-PrereleaseVersion {
    <# .SYNOPSIS Versions 0.x are marked as pre-releases on GitHub (decided 2026-10-21). #>
    param([Parameter(Mandatory)] [string] $Version)
    $Version.StartsWith('0.', [StringComparison]::Ordinal)
}

function Get-ChangelogSection {
    <#
    .SYNOPSIS
        Returns the release notes of one version from a Keep a Changelog file.
    .DESCRIPTION
        The section starts with '## [X.Y.Z] - YYYY-MM-DD' and ends before the next '## ' heading. Link reference
        definitions of versions at the end of the file ('[X.Y.Z]: https://...') are not part of the notes.
        Throws when the section is missing, duplicated, has no valid release date or no content.
        -AllowUndated accepts a section without a date (e.g. '- unreleased'): the build of any commit packs the notes
        into the Velopack package (T-017); only the upload insists on the date.
    #>
    param([Parameter(Mandatory)] [string] $Path, [Parameter(Mandatory)] [string] $Version, [switch] $AllowUndated)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Changelog not found: $Path. Release notes are taken from it." }
    $lines = @(Get-Content -LiteralPath $Path -Encoding utf8)
    $heading = '^##\s+\[' + [regex]::Escape($Version) + '\](?:\s+-\s+(?<date>.*?))?\s*$'
    $starts = @(for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match $heading) { $i } })
    if ($starts.Count -eq 0) {
        throw "$Path has no section '## [$Version] - YYYY-MM-DD'. Write the release notes of $Version first."
    }
    if ($starts.Count -gt 1) { throw "$Path has $($starts.Count) sections for version $Version; keep exactly one." }

    $null = $lines[$starts[0]] -match $heading
    $date = if ($Matches.ContainsKey('date')) { $Matches.date } else { '' }
    $parsed = [datetime]::MinValue
    if (-not $AllowUndated -and -not ($date -match '^\d{4}-\d{2}-\d{2}$' -and [datetime]::TryParseExact($date, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$parsed))) {
        throw "Section [$Version] in $Path has no release date: expected '## [$Version] - YYYY-MM-DD', found '$($lines[$starts[0]])'. Replace a placeholder such as 'unreleased' with the release date."
    }

    $body = [System.Collections.Generic.List[string]]::new()
    for ($i = $starts[0] + 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^##\s') { break }
        $body.Add($lines[$i])
    }
    # Drop trailing link reference definitions of versions and blank lines around the content.
    while ($body.Count -gt 0 -and ([string]::IsNullOrWhiteSpace($body[$body.Count - 1]) -or $body[$body.Count - 1] -match '^\[(Unreleased|\d+\.\d+\.\d+)\]:\s')) {
        $body.RemoveAt($body.Count - 1)
    }
    while ($body.Count -gt 0 -and [string]::IsNullOrWhiteSpace($body[0])) { $body.RemoveAt(0) }

    # Sub-headings and HTML comments alone are not release notes.
    if (-not ($body | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_.TrimStart() -notmatch '^(#|<!--)' })) {
        throw "Section [$Version] in $Path is empty. Write the release notes of $Version first."
    }
    $body -join "`n"
}

function Invoke-Git {
    <# .SYNOPSIS Runs git in a repository and returns exit code, stdout lines and stderr text (never throws). #>
    param([Parameter(Mandatory)] [string] $RepoRoot, [Parameter(Mandatory)] [string[]] $Arguments)
    $all = & git -C $RepoRoot @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    [pscustomobject]@{
        ExitCode = $exitCode
        Output   = @($all | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] } | ForEach-Object { "$_" })
        Error    = (@($all | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] } | ForEach-Object { "$_" }) -join ' ').Trim()
    }
}

function Get-GitHead {
    <# .SYNOPSIS Full hash of HEAD. #>
    param([Parameter(Mandatory)] [string] $RepoRoot)
    $result = Invoke-Git $RepoRoot @('rev-parse', 'HEAD')
    if ($result.ExitCode -ne 0) { throw "git rev-parse HEAD failed: $($result.Error)" }
    $result.Output[0].Trim()
}

function Get-GitStatusLines {
    <# .SYNOPSIS 'git status --porcelain' lines including untracked files (empty = clean tree). #>
    param([Parameter(Mandatory)] [string] $RepoRoot)
    $result = Invoke-Git $RepoRoot @('status', '--porcelain', '--untracked-files=all')
    if ($result.ExitCode -ne 0) { throw "git status failed: $($result.Error)" }
    @($result.Output | Where-Object { $_ })
}

function Get-ReleaseGitState {
    <#
    .SYNOPSIS
        Collects the git facts the release checks need. Reads origin with a single git ls-remote and changes nothing,
        neither on origin nor in the local repository (no fetch). Remote failures are returned in RemoteErrors.
    #>
    param(
        [Parameter(Mandatory)] [string] $RepoRoot,
        [Parameter(Mandatory)] [string] $Tag,
        [string] $Remote = 'origin',
        [string] $MainBranch = 'main'
    )
    $head = Get-GitHead $RepoRoot
    $status = @(Get-GitStatusLines $RepoRoot)
    $tags = Invoke-Git $RepoRoot @('tag', '--points-at', 'HEAD')
    if ($tags.ExitCode -ne 0) { throw "git tag --points-at HEAD failed: $($tags.Error)" }

    $remoteErrors = [System.Collections.Generic.List[string]]::new()
    $remoteTagCommit = $null
    $remoteMain = $null
    $onMain = $null
    # One read-only query of the remote; nothing is fetched, so no local ref changes. Without a remote (it could not be
    # resolved; the caller reports why) only the local facts are collected.
    $lsRemote = if ($Remote) { Invoke-Git $RepoRoot @('ls-remote', $Remote, "refs/heads/$MainBranch", "refs/tags/$Tag", "refs/tags/$Tag^{}") } else { $null }
    if (-not $Remote) {
        $remoteErrors.Add('The tag and the main branch were not checked: there is no remote to check them on.')
    }
    elseif ($lsRemote.ExitCode -ne 0) {
        $remoteErrors.Add("git ls-remote $Remote failed: $($lsRemote.Error)")
    }
    else {
        # An annotated tag is listed twice: the tag object and the peeled commit (^{}); the peeled one wins.
        $plain = $null
        foreach ($line in $lsRemote.Output) {
            $parts = $line -split '\s+'
            if ($parts.Count -lt 2) { continue }
            if ($parts[1] -eq "refs/tags/$Tag^{}") { $remoteTagCommit = $parts[0] }
            elseif ($parts[1] -eq "refs/tags/$Tag") { $plain = $parts[0] }
            elseif ($parts[1] -eq "refs/heads/$MainBranch") { $remoteMain = $parts[0] }
        }
        if (-not $remoteTagCommit) { $remoteTagCommit = $plain }

        if (-not $remoteMain) {
            $remoteErrors.Add("Branch $MainBranch does not exist on $Remote.")
        }
        elseif ((Invoke-Git $RepoRoot @('cat-file', '-e', "$remoteMain^{commit}")).ExitCode -ne 0) {
            $remoteErrors.Add("$Remote/$MainBranch ($remoteMain) is not in the local repository. Run: git fetch $Remote")
        }
        else {
            $ancestor = Invoke-Git $RepoRoot @('merge-base', '--is-ancestor', 'HEAD', $remoteMain)
            $onMain = switch ($ancestor.ExitCode) {
                0 { $true }
                1 { $false }
                default { $remoteErrors.Add("git merge-base failed: $($ancestor.Error)"); $null }
            }
        }
    }

    [pscustomobject]@{
        Head            = $head
        StatusLines     = $status
        HeadTags        = @($tags.Output | Where-Object { $_ })
        RemoteTagCommit = $remoteTagCommit
        OnMain          = $onMain
        Remote          = $Remote
        MainBranch      = $MainBranch
        RemoteErrors    = $remoteErrors.ToArray()
        RemoteQueried   = [bool]($Remote -and $lsRemote.ExitCode -eq 0)
    }
}

function Get-ReleaseGitProblems {
    <#
    .SYNOPSIS
        Pure check of the git facts from Get-ReleaseGitState against the source version. Returns problem messages
        (empty = releasable): dirty tree, HEAD without the tag, tag not matching the version, tag missing on origin
        or pointing elsewhere, HEAD not contained in origin/main.
    #>
    param([Parameter(Mandatory)] $State, [Parameter(Mandatory)] [string] $Version)
    $problems = [System.Collections.Generic.List[string]]::new()
    $tag = ConvertTo-ReleaseTag $Version

    $status = @($State.StatusLines)
    if ($status.Count -gt 0) {
        $shown = ($status | Select-Object -First 5) -join '; '
        $more = if ($status.Count -gt 5) { " (and $($status.Count - 5) more)" } else { '' }
        $problems.Add("Working tree is not clean ($($status.Count) changed or untracked files: $shown$more). Release only from a clean checkout of the tagged commit.")
    }

    $headTags = @($State.HeadTags)
    if ($headTags -notcontains $tag) {
        $versionTags = @($headTags | Where-Object { Get-VersionFromTag $_ })
        if ($versionTags.Count -gt 0) {
            $problems.Add("HEAD is tagged $($versionTags -join ', ') but the version in Directory.Build.props is $Version (expected tag $tag).")
        }
        else {
            $problems.Add("HEAD has no tag $tag. Tag the release commit on $($State.MainBranch) (git tag -a $tag) and push the tag.")
        }
    }

    foreach ($remoteError in @($State.RemoteErrors)) { $problems.Add($remoteError) }

    # The tag is judged whenever the remote answered (other remote problems, e.g. a missing main branch, do not hide it).
    if ($State.RemoteQueried) {
        if (-not $State.RemoteTagCommit) {
            $problems.Add("Tag $tag is not on $($State.Remote). Push it first: git push $($State.Remote) $tag")
        }
        elseif ($State.RemoteTagCommit -ne $State.Head) {
            $problems.Add("Tag $tag on $($State.Remote) points to $($State.RemoteTagCommit), but HEAD is $($State.Head).")
        }
    }

    if ($State.OnMain -eq $false) {
        $problems.Add("HEAD $($State.Head) is not contained in $($State.Remote)/$($State.MainBranch). Releases are made only from $($State.MainBranch).")
    }
    $problems.ToArray()
}

function Get-FileSha256 {
    <# .SYNOPSIS Lower-case SHA256 of a file. #>
    param([Parameter(Mandatory)] [string] $Path)
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-ChecksumFileName {
    <# .SYNOPSIS One checksum file per OS so that the Windows and macOS runs never overwrite each other. #>
    param([Parameter(Mandatory)] [string] $Os)
    "SHA256SUMS-$Os.txt"
}

function Write-Sha256Sums {
    <# .SYNOPSIS Writes '<sha256>  <file name>' lines (sha256sum format, sorted, LF, UTF-8 without BOM). #>
    param([Parameter(Mandatory)] [string] $Directory, [Parameter(Mandatory)] [string] $FileName, [Parameter(Mandatory)] [string[]] $Files)
    $lines = foreach ($name in ($Files | Sort-Object -CaseSensitive)) {
        if ($name -match '[\\/]' -or $name -eq $FileName) { throw "Invalid checksum entry '$name'." }
        "$(Get-FileSha256 (Join-Path $Directory $name))  $name"
    }
    $path = Join-Path $Directory $FileName
    [System.IO.File]::WriteAllText($path, (($lines -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))
    $path
}

function Read-Sha256Sums {
    <# .SYNOPSIS Parses a sha256sum file into an ordered dictionary file name -> hash; throws on malformed lines. #>
    param([Parameter(Mandatory)] [string] $Path)
    $result = [ordered]@{}
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line -notmatch '^(?<hash>[0-9a-f]{64}) [ *](?<name>[^\\/]+)$') { throw "Malformed line in $($Path): '$line'" }
        if ($result.Contains($Matches.name)) { throw "Duplicate entry '$($Matches.name)' in $Path" }
        $result[$Matches.name] = $Matches.hash
    }
    $result
}

function Get-Sha256SumsProblems {
    <# .SYNOPSIS Verifies every entry of a checksum file against the files next to it; returns problem messages. #>
    param([Parameter(Mandatory)] [string] $Path)
    $problems = [System.Collections.Generic.List[string]]::new()
    $directory = Split-Path -Parent $Path
    $entries = Read-Sha256Sums $Path
    foreach ($name in $entries.Keys) {
        $file = Join-Path $directory $name
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { $problems.Add("$name is listed in $(Split-Path -Leaf $Path) but missing."); continue }
        if ((Get-FileSha256 $file) -ne $entries[$name]) { $problems.Add("$name does not match its SHA256 in $(Split-Path -Leaf $Path).") }
    }
    $problems.ToArray()
}

function Get-ArchiveFileName {
    <# .SYNOPSIS LayerPeek-<version>-<rid>.zip (Windows) / .tar.gz (macOS). #>
    param([Parameter(Mandatory)] [string] $Version, [Parameter(Mandatory)] [string] $Runtime, $Config = $script:ReleaseRuntimes)
    $extension = if ((Get-RuntimeOs $Runtime $Config) -eq 'windows') { 'zip' } else { 'tar.gz' }
    "LayerPeek-$Version-$Runtime.$extension"
}

function Get-TarCommand {
    <#
    .SYNOPSIS
        bsdtar of the OS, never a GNU tar that may come first in PATH: on Windows System32\tar.exe (Git for Windows' GNU
        tar reads 'C:' in a path as a remote host), on macOS /usr/bin/tar (a Homebrew gnubin tar does not know
        --no-mac-metadata).
    #>
    $tar = if ($IsWindows) { Join-Path $env:SystemRoot 'System32' 'tar.exe' } elseif ($IsMacOS) { '/usr/bin/tar' } else { $null }
    if ($tar -and (Test-Path -LiteralPath $tar -PathType Leaf)) { return $tar }
    'tar'
}

function Test-ArchiveExcluded {
    <#
    .SYNOPSIS
        True for debug symbols, which are never released (decided 2026-10-21): *.pdb files and everything inside a
        *.dSYM bundle. RelativePath uses / or \ as separator.
    #>
    param([Parameter(Mandatory)] [string] $RelativePath)
    $segments = @($RelativePath -split '[\\/]' | Where-Object { $_ })
    [bool](($segments | Where-Object { $_ -like '*.dSYM' }) -or ($segments.Count -gt 0 -and $segments[-1] -like '*.pdb'))
}

function Get-ArchiveFiles {
    <# .SYNOPSIS Relative paths ('/'-separated, sorted) of the files of a publish folder that go into the archive. #>
    param([Parameter(Mandatory)] [string] $SourceDirectory)
    $root = (Resolve-Path -LiteralPath $SourceDirectory).ProviderPath
    @(Get-ChildItem -LiteralPath $root -Recurse -Force -File |
        ForEach-Object { [System.IO.Path]::GetRelativePath($root, $_.FullName) -replace '\\', '/' } |
        Where-Object { -not (Test-ArchiveExcluded $_) } |
        Sort-Object -CaseSensitive)
}

function New-ReleaseArchive {
    <#
    .SYNOPSIS
        Archives the content of a publish folder (without the folder itself) except debug symbols
        (Test-ArchiveExcluded): .zip or .tar.gz by file name.
    #>
    param([Parameter(Mandatory)] [string] $SourceDirectory, [Parameter(Mandatory)] [string] $Destination)
    if (Test-Path -LiteralPath $Destination) { throw "Archive already exists: $Destination" }
    $root = (Resolve-Path -LiteralPath $SourceDirectory).ProviderPath
    $files = @(Get-ArchiveFiles $root)
    if ($files.Count -eq 0) { throw "Nothing to archive in $SourceDirectory" }
    if ($Destination.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase)) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [System.IO.Compression.ZipFile]::Open($Destination, [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($file in $files) {
                $null = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $root $file), $file, [System.IO.Compression.CompressionLevel]::Optimal)
            }
        }
        finally { $zip.Dispose() }
    }
    elseif ($Destination.EndsWith('.tar.gz', [StringComparison]::OrdinalIgnoreCase)) {
        # tar keeps the Unix permissions (the executable bit of the macOS binary). The file list goes through a
        # temporary file (-T), so that exactly the files of Get-ArchiveFiles are archived.
        $list = Join-Path ([System.IO.Path]::GetTempPath()) "layerpeek-tar-list-$([guid]::NewGuid().ToString('N')).txt"
        try {
            [System.IO.File]::WriteAllText($list, (($files | ForEach-Object { "./$_" }) -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))
            # macOS bsdtar stores extended attributes by default (--mac-metadata, AppleDouble '._*' entries such as the
            # quarantine flag); the release archive must hold only the files themselves.
            $macArguments = if ($IsMacOS) { @('--no-mac-metadata') } else { @() }
            $savedCopyFile = $env:COPYFILE_DISABLE
            if ($IsMacOS) { $env:COPYFILE_DISABLE = '1' }
            try {
                & (Get-TarCommand) @macArguments -czf $Destination -C $root -T $list
                if ($LASTEXITCODE -ne 0) { throw "tar failed with exit code $LASTEXITCODE." }
            }
            finally { if ($IsMacOS) { $env:COPYFILE_DISABLE = $savedCopyFile } }
        }
        finally { Remove-Item -LiteralPath $list -Force -ErrorAction SilentlyContinue }
    }
    else {
        throw "Unsupported archive type: $Destination"
    }
    $Destination
}

function Get-BuildInfoPath {
    <# .SYNOPSIS Path of the build marker in a release output folder. #>
    param([Parameter(Mandatory)] [string] $ReleaseDirectory)
    Join-Path $ReleaseDirectory $script:BuildInfoFileName
}

function Get-BuildInfoProblems {
    <#
    .SYNOPSIS
        Pure check of a build marker before upload: same version and commit as HEAD, built from a clean tree, only
        uploadable runtimes, and the files in dist exactly the files listed in the marker. Returns problem messages.
    #>
    param(
        $BuildInfo,
        [Parameter(Mandatory)] [string] $Version,
        [Parameter(Mandatory)] [string] $Head,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $DistFiles,
        $Config = $script:ReleaseRuntimes
    )
    $problems = [System.Collections.Generic.List[string]]::new()
    if ($null -eq $BuildInfo) {
        $problems.Add("No build output for version $Version. Run scripts/release-build.ps1 on the tagged commit first.")
        return $problems.ToArray()
    }
    if ($BuildInfo.Version -ne $Version) { $problems.Add("Build output is version $($BuildInfo.Version), the source is $Version. Rebuild.") }
    if ($BuildInfo.Commit -ne $Head) { $problems.Add("Build output comes from commit $($BuildInfo.Commit), HEAD is $Head. Rebuild on HEAD so that no older build is uploaded.") }
    if ($BuildInfo.Dirty) { $problems.Add('Build output was made from a working tree with uncommitted or untracked files. Rebuild from a clean checkout.') }
    foreach ($rid in @($BuildInfo.Runtimes)) {
        if (-not $Config.Contains($rid)) { $problems.Add("Build output contains unknown runtime '$rid'."); continue }
        if (-not $Config[$rid].Uploadable) { $problems.Add("Build output contains $rid, which must not be uploaded: $($Config[$rid].BlockedReason). Rebuild without it.") }
    }
    $expected = @($BuildInfo.Files | ForEach-Object { $_.Name }) + @($BuildInfo.ChecksumFile)
    $missing = @($expected | Where-Object { $DistFiles -cnotcontains $_ })
    $extra = @($DistFiles | Where-Object { $expected -cnotcontains $_ })
    if ($missing.Count -gt 0) { $problems.Add("Files listed in the build marker are missing in dist: $($missing -join ', ').") }
    if ($extra.Count -gt 0) { $problems.Add("dist contains files not produced by the build: $($extra -join ', '). Rebuild instead of adding files by hand.") }
    $problems.ToArray()
}

function Get-UploadPlan {
    <#
    .SYNOPSIS
        Pure decision per local file against the assets already in the release:
        Upload (new), Skip (same name and SHA256 already uploaded), Replace (different content, -Force),
        Conflict (different content without -Force).
    .PARAMETER LocalFiles
        Objects with Name and Sha256.
    .PARAMETER RemoteAssets
        Objects with name and digest ('sha256:<hex>' as returned by gh release view --json assets; may be empty).
    #>
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $LocalFiles,
        [AllowEmptyCollection()] [object[]] $RemoteAssets = @(),
        [switch] $Force
    )
    foreach ($file in $LocalFiles) {
        $asset = @($RemoteAssets | Where-Object { $_.name -ceq $file.Name }) | Select-Object -First 1
        $action = if ($null -eq $asset) { 'Upload' }
        elseif ($asset.PSObject.Properties['digest'] -and $asset.digest -eq "sha256:$($file.Sha256)") { 'Skip' }
        elseif ($Force) { 'Replace' }
        else { 'Conflict' }
        [pscustomobject]@{ Name = $file.Name; Sha256 = $file.Sha256; Action = $action }
    }
}

function Get-GitHubRepository {
    <# .SYNOPSIS 'owner/repo' parsed from a GitHub remote URL (https or ssh); $null for other hosts. #>
    param([Parameter(Mandatory)] [string] $RemoteUrl)
    if ($RemoteUrl -match '^(?:https://(?:[^@/]+@)?github\.com/|git@github\.com:|ssh://git@github\.com/)(?<repo>[^/\s]+/[^/\s]+?)(?:\.git)?/?$') { return $Matches.repo }
    $null
}

function Get-GhProblems {
    <# .SYNOPSIS Checks that the GitHub CLI exists and is logged in; never prints gh's output (it describes the token). #>
    $problems = [System.Collections.Generic.List[string]]::new()
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        $problems.Add('GitHub CLI (gh) is not installed. See docs/prerequisites.md (winget install --id GitHub.cli / brew install gh).')
        return $problems.ToArray()
    }
    $null = & gh auth status --hostname github.com 2>&1
    if ($LASTEXITCODE -ne 0) { $problems.Add('GitHub CLI is not logged in to github.com. Run: gh auth login') }
    $problems.ToArray()
}

function Get-GitHubRelease {
    <#
    .SYNOPSIS
        Release of a tag (drafts included) as returned by gh release view --json, or $null when it does not exist.
        Throws on any other gh failure (network, permissions).
    #>
    param([Parameter(Mandatory)] [string] $Repository, [Parameter(Mandatory)] [string] $Tag)
    $all = & gh release view $Tag --repo $Repository --json 'tagName,name,isDraft,isPrerelease,url,assets' 2>&1
    $exitCode = $LASTEXITCODE
    $stdout = @($all | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] } | ForEach-Object { "$_" }) -join "`n"
    $stderr = (@($all | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] } | ForEach-Object { "$_" }) -join ' ').Trim()
    if ($exitCode -ne 0) {
        if ($stderr -match 'release not found') { return $null }
        throw "gh release view $Tag failed (exit code $exitCode): $stderr"
    }
    $stdout | ConvertFrom-Json
}

function Get-PublishProblems {
    <#
    .SYNOPSIS
        Pure check before a draft release is published: it exists, is a draft, has the checksum file of every
        expected OS, every file listed in the checksum files is uploaded with the listed SHA256, no other file is
        uploaded and no file of a runtime that must not be uploaded (win-arm64 until T-057) is there.
    .PARAMETER Release
        Release as returned by Get-GitHubRelease ($null = no release).
    .PARAMETER Checksums
        Dictionary checksum file name -> (dictionary file name -> sha256) of the downloaded checksum files.
    #>
    param(
        $Release,
        [Parameter(Mandatory)] [string] $Tag,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $ExpectedOs,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Checksums,
        $Config = $script:ReleaseRuntimes
    )
    $problems = [System.Collections.Generic.List[string]]::new()
    if ($null -eq $Release) {
        $problems.Add("There is no release for $Tag. Upload the build first (scripts/release-upload.ps1).")
        return $problems.ToArray()
    }
    if (-not $Release.isDraft) { $problems.Add("Release $Tag is already published.") }
    $assets = @($Release.assets)
    if ($assets.Count -eq 0) { $problems.Add("Release $Tag has no files.") }
    foreach ($os in $ExpectedOs) {
        $sums = Get-ChecksumFileName $os
        if (-not (@($assets | Where-Object { $_.name -ceq $sums }))) { $problems.Add("Release $Tag has no $($sums): the $os build is not uploaded yet.") }
    }
    $listed = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($sumsName in $Checksums.Keys) {
        $null = $listed.Add($sumsName)
        $entries = $Checksums[$sumsName]
        foreach ($name in $entries.Keys) {
            $null = $listed.Add($name)
            $asset = @($assets | Where-Object { $_.name -ceq $name }) | Select-Object -First 1
            if ($null -eq $asset) { $problems.Add("$name is listed in $sumsName but not uploaded."); continue }
            $digest = if ($asset.PSObject.Properties['digest']) { $asset.digest } else { $null }
            if (-not $digest) { $problems.Add("GitHub reports no SHA256 digest for $name; it cannot be verified.") }
            elseif ($digest -ne "sha256:$($entries[$name])") { $problems.Add("Uploaded $name does not match its SHA256 in $sumsName.") }
        }
    }
    foreach ($asset in $assets) {
        if (-not $listed.Contains($asset.name)) { $problems.Add("$($asset.name) is uploaded but not listed in any checksum file.") }
        foreach ($rid in $Config.Keys) {
            if (-not $Config[$rid].Uploadable -and (Test-RuntimeFileName -Name $asset.name -Runtime $rid)) {
                $problems.Add("$($asset.name) belongs to $rid, which must not be released: $($Config[$rid].BlockedReason).")
            }
        }
    }
    $problems.ToArray()
}

function Test-RuntimeFileName {
    <#
    .SYNOPSIS
        True when a release file belongs to the RID: the RID as a name part separated by '-' or '.', as in the archive
        (LayerPeek-0.1.0-win-arm64.zip) and in every Velopack file (releases.win-arm64.json, RELEASES-win-arm64,
        LayerPeek-0.1.0-win-arm64-full.nupkg, LayerPeek-0.1.0-win-arm64-Setup.exe).
    #>
    param([Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [string] $Runtime)
    [bool]($Name -match "(^|[-.])$([regex]::Escape($Runtime))([-.]|$)")
}

# Velopack packaging (T-017, docs/velopack.md point 12). Fixed by the user: package id, title and AppUserModelID
# 'LayerPeek', Start menu shortcut only, one channel per RID, no deltas yet, no Portable.zip (the archive step makes the .zip).
$script:Velopack = [pscustomobject]@{
    PackId    = 'LayerPeek'
    Title     = 'LayerPeek'
    Aumid     = 'LayerPeek'
    Authors   = 'Petr Foltýnek'
    MainExe   = 'LayerPeek.exe'
    Shortcuts = 'StartMenuRoot'
    Icon      = 'src/LayerPeek.App/Assets/AppIcon/app-icon.ico'
}

function Get-VelopackSettings {
    <# .SYNOPSIS The fixed values of the Velopack packaging step. #>
    $script:Velopack
}

function Get-VelopackSetupFileName {
    <# .SYNOPSIS The installer in dist: LayerPeek-<version>-<rid>-Setup.exe (named like the archive of the RID). #>
    param([Parameter(Mandatory)] [string] $Version, [Parameter(Mandatory)] [string] $Runtime)
    "LayerPeek-$Version-$Runtime-Setup.exe"
}

function Get-VelopackOutputMap {
    <#
    .SYNOPSIS
        The files vpk pack writes for one RID (channel = RID) and their names in dist: the feed files and the package keep
        Velopack's names (the update client looks them up by these names), only Setup.exe is renamed. Ordered
        dictionary vpk name -> dist name.
    #>
    param([Parameter(Mandatory)] [string] $Version, [Parameter(Mandatory)] [string] $Runtime)
    $id = $script:Velopack.PackId
    [ordered]@{
        "$id-$Version-$Runtime-full.nupkg" = "$id-$Version-$Runtime-full.nupkg"
        "releases.$Runtime.json"           = "releases.$Runtime.json"
        "assets.$Runtime.json"             = "assets.$Runtime.json"
        "RELEASES-$Runtime"                = "RELEASES-$Runtime"
        "$id-$Runtime-Setup.exe"           = Get-VelopackSetupFileName -Version $Version -Runtime $Runtime
    }
}

function Get-VelopackPackArguments {
    <#
    .SYNOPSIS
        The arguments of 'dotnet vpk' for one RID (pure): Start menu shortcut with the AUMID LayerPeek, channel and runtime
        = RID, release notes from a file, no deltas, no Portable.zip, no update check of vpk itself.
    #>
    param(
        [Parameter(Mandatory)] [string] $Version,
        [Parameter(Mandatory)] [string] $Runtime,
        [Parameter(Mandatory)] [string] $PublishDirectory,
        [Parameter(Mandatory)] [string] $OutputDirectory,
        [Parameter(Mandatory)] [string] $ReleaseNotesFile,
        [Parameter(Mandatory)] [string] $IconFile
    )
    $v = $script:Velopack
    @(
        'vpk', '--skip-updates', 'pack',
        '--packId', $v.PackId,
        '--packVersion', $Version,
        '--packDir', $PublishDirectory,
        '--mainExe', $v.MainExe,
        '--packTitle', $v.Title,
        '--packAuthors', $v.Authors,
        '--aumid', $v.Aumid,
        '--shortcuts', $v.Shortcuts,
        '--icon', $IconFile,
        '--channel', $Runtime,
        '-r', $Runtime,
        '--releaseNotes', $ReleaseNotesFile,
        '--delta', 'None',
        '--noPortable',
        '--outputDir', $OutputDirectory
    )
}

function Get-VelopackVersionProblems {
    <#
    .SYNOPSIS
        The NuGet package Velopack (Directory.Packages.props) and the vpk tool (.config/dotnet-tools.json) must have the
        same version (vpk warns or fails otherwise, docs/velopack.md). Returns problem messages.
    #>
    param([Parameter(Mandatory)] [string] $PackagesPropsPath, [Parameter(Mandatory)] [string] $ToolManifestPath)
    $problems = [System.Collections.Generic.List[string]]::new()
    [xml] $props = Get-Content -LiteralPath $PackagesPropsPath -Raw
    $package = @($props.SelectNodes("//PackageVersion[@Include='Velopack']"))
    $packageVersion = if ($package.Count -eq 1) { $package[0].GetAttribute('Version') } else { $null }
    if (-not $packageVersion) { $problems.Add("$PackagesPropsPath has no single PackageVersion of Velopack.") }
    $manifest = Get-Content -LiteralPath $ToolManifestPath -Raw | ConvertFrom-Json -AsHashtable
    $toolVersion = if ($manifest.ContainsKey('tools') -and $manifest.tools.ContainsKey('vpk')) { $manifest.tools.vpk.version } else { $null }
    if (-not $toolVersion) { $problems.Add("$ToolManifestPath does not pin the vpk tool.") }
    if ($packageVersion -and $toolVersion -and $packageVersion -ne $toolVersion) {
        $problems.Add("Velopack $packageVersion (Directory.Packages.props) and vpk $toolVersion (.config/dotnet-tools.json) differ; use the same version.")
    }
    $problems.ToArray()
}

function Copy-VelopackOutput {
    <#
    .SYNOPSIS
        Checks that vpk wrote exactly the expected files (no Portable.zip, no delta) and copies them into dist under their
        dist names. Returns the dist paths. Throws on missing or unexpected files, or when a dist file already exists.
    #>
    param(
        [Parameter(Mandatory)] [string] $VelopackDirectory,
        [Parameter(Mandatory)] [string] $OutputDirectory,
        [Parameter(Mandatory)] [string] $Version,
        [Parameter(Mandatory)] [string] $Runtime
    )
    $map = Get-VelopackOutputMap -Version $Version -Runtime $Runtime
    $found = @(Get-ChildItem -LiteralPath $VelopackDirectory -File -Force | ForEach-Object Name)
    $missing = @($map.Keys | Where-Object { $found -cnotcontains $_ })
    $extra = @($found | Where-Object { -not $map.Contains($_) })
    if ($missing.Count -gt 0) { throw "vpk did not create $($missing -join ', ') in $VelopackDirectory." }
    if ($extra.Count -gt 0) { throw "vpk created unexpected files in $($VelopackDirectory): $($extra -join ', ')." }
    foreach ($name in $map.Keys) {
        $destination = Join-Path $OutputDirectory $map[$name]
        if (Test-Path -LiteralPath $destination) { throw "Release file already exists: $destination" }
        Copy-Item -LiteralPath (Join-Path $VelopackDirectory $name) -Destination $destination
        $destination
    }
}

function Get-GitRemotes {
    <# .SYNOPSIS Ordered dictionary remote name -> fetch URL of a repository. #>
    param([Parameter(Mandatory)] [string] $RepoRoot)
    $names = Invoke-Git $RepoRoot @('remote')
    if ($names.ExitCode -ne 0) { throw "git remote failed: $($names.Error)" }
    $remotes = [ordered]@{}
    foreach ($name in @($names.Output | Where-Object { $_ })) {
        $url = Invoke-Git $RepoRoot @('remote', 'get-url', $name)
        if ($url.ExitCode -eq 0) { $remotes[$name] = $url.Output | Select-Object -First 1 }
    }
    $remotes
}

function Resolve-ReleaseRemote {
    <#
    .SYNOPSIS
        Pure choice of the git remote whose tag and main branch are checked, and of the GitHub repository the release
        goes to. Without -Repository: remote 'origin' and its repository. With -Repository owner/name (e.g. a trial
        repository): the single remote pointing to that GitHub repository, so that the git checks and the release always
        concern the same repository. Returns Remote, Repository and Problem ($null = OK).
    #>
    param([Parameter(Mandatory)] [System.Collections.IDictionary] $Remotes, [string] $Repository)
    function Result($remote, $repo, $problem) { [pscustomobject]@{ Remote = $remote; Repository = $repo; Problem = $problem } }
    if (-not $Repository) {
        if (-not $Remotes.Contains('origin')) { return Result $null $null 'Remote origin is not configured.' }
        $repo = Get-GitHubRepository $Remotes['origin']
        if (-not $repo) { return Result $null $null 'Remote origin is not a GitHub repository.' }
        return Result 'origin' $repo $null
    }
    if ($Repository -notmatch '^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$') { return Result $null $null "-Repo '$Repository' is not in the form owner/name." }
    $matching = @($Remotes.Keys | Where-Object { (Get-GitHubRepository $Remotes[$_]) -eq $Repository })
    if ($matching.Count -eq 0) {
        return Result $null $Repository "No git remote points to github.com/$Repository. Add one, e.g.: git remote add trial https://github.com/$Repository.git"
    }
    if ($matching.Count -gt 1) {
        return Result $null $Repository "Several git remotes point to github.com/$($Repository): $($matching -join ', '). Keep one."
    }
    Result $matching[0] (Get-GitHubRepository $Remotes[$matching[0]]) $null
}

function Get-ReleasePreflight {
    <#
    .SYNOPSIS
        Checks shared by release-upload.ps1 and release-publish.ps1: version source, git state of HEAD (clean, tagged,
        tag on the remote, contained in its main branch), GitHub CLI and the GitHub repository. The remote is origin,
        or with -Repository the remote of that repository (Resolve-ReleaseRemote). Reads only.
        Returns Version, Tag, Repository, Remote, GitState and Problems (empty = OK).
    #>
    param([Parameter(Mandatory)] [string] $RepoRoot, [string] $Repository)
    $problems = [System.Collections.Generic.List[string]]::new()
    $version = Get-SourceVersion (Join-Path $RepoRoot 'Directory.Build.props')
    $tag = ConvertTo-ReleaseTag $version

    $target = Resolve-ReleaseRemote -Remotes (Get-GitRemotes $RepoRoot) -Repository $Repository
    if ($target.Problem) { $problems.Add($target.Problem) }
    $state = Get-ReleaseGitState -RepoRoot $RepoRoot -Tag $tag -Remote $target.Remote
    foreach ($problem in @(Get-ReleaseGitProblems -State $state -Version $version)) { $problems.Add($problem) }
    foreach ($problem in @(Get-GhProblems)) { $problems.Add($problem) }

    [pscustomobject]@{
        Version    = $version
        Tag        = $tag
        Repository = $target.Repository
        Remote     = $target.Remote
        GitState   = $state
        Problems   = $problems
    }
}

function Write-Problems {
    <# .SYNOPSIS Prints all problems and a refusal line; returns nothing. #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Problems, [Parameter(Mandatory)] [string] $Action)
    Write-Host "$Action REFUSED ($($Problems.Count) problem(s)):" -ForegroundColor Red
    foreach ($problem in $Problems) { Write-Host "  - $problem" -ForegroundColor Red }
}

Export-ModuleMember -Function *
