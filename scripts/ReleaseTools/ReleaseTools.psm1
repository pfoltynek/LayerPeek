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
        if ($url.ExitCode -ne 0) { continue }
        $value = $url.Output | Select-Object -First 1
        if (-not (Get-GitHubRepository "$value")) {
            # get-url applies url.<base>.insteadOf; when that rewrites a GitHub URL to something else (e.g. a local
            # mirror in the Pester tests), the configured URL still names the GitHub repository.
            $configured = Invoke-Git $RepoRoot @('config', '--get', "remote.$name.url")
            if ($configured.ExitCode -eq 0 -and (Get-GitHubRepository "$($configured.Output | Select-Object -First 1)")) { $value = $configured.Output | Select-Object -First 1 }
        }
        $remotes[$name] = $value
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

function Get-NativeToolchainProblems {
    <#
    .SYNOPSIS
        Checks the Native AOT toolchain of the requested RIDs before a long build (docs/prerequisites.md): Xcode Command
        Line Tools on macOS, the MSVC component of every Windows RID. Returns problem messages. Reads only (xcrun, vswhere).
    #>
    param([Parameter(Mandatory)] [string[]] $Runtimes, [Parameter(Mandatory)] [string] $HostOs)
    $problems = [System.Collections.Generic.List[string]]::new()
    if ($HostOs -eq 'macos') {
        $found = $false
        if (Get-Command xcrun -ErrorAction SilentlyContinue) {
            $null = & xcrun --find clang 2>&1
            $found = $LASTEXITCODE -eq 0
        }
        if (-not $found) { $problems.Add('Xcode Command Line Tools are missing (xcode-select --install). See docs/prerequisites.md.') }
        return $problems.ToArray()
    }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio' 'Installer' 'vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) {
        $problems.Add("Visual Studio Build Tools are missing ($vswhere not found). See docs/prerequisites.md.")
        return $problems.ToArray()
    }
    $components = @{
        'win-x64'   = 'Microsoft.VisualStudio.Component.VC.Tools.x86.x64'
        'win-arm64' = 'Microsoft.VisualStudio.Component.VC.Tools.ARM64'
    }
    foreach ($rid in $Runtimes) {
        $component = $components[$rid]
        $found = @(& $vswhere -products * -requires $component -property installationPath)
        if ($LASTEXITCODE -ne 0 -or -not ($found | Where-Object { $_ })) {
            $problems.Add("The MSVC component $component needed for the $rid AOT publish is not installed. See docs/prerequisites.md.")
        }
    }
    $problems.ToArray()
}

function Get-ReleaseBuildOutput {
    <#
    .SYNOPSIS
        The output of scripts/release-build.ps1 for a version (artifacts/release/<version>), checked for an upload of the
        commit Head: build marker (Get-BuildInfoProblems), checksum file and the SHA256 of every file.
    .OUTPUTS
        DistDirectory, BuildInfo, Files (Name and Sha256 of every file to upload including the checksum file; empty when
        the marker does not fit) and Problems (empty = this output can be uploaded).
    #>
    param([Parameter(Mandatory)] [string] $RepoRoot, [Parameter(Mandatory)] [string] $Version, [Parameter(Mandatory)] [string] $Head)
    $releaseDir = Join-Path $RepoRoot 'artifacts' 'release' $Version
    $distDir = Join-Path $releaseDir 'dist'
    $buildInfoPath = Get-BuildInfoPath $releaseDir
    $buildInfo = if (Test-Path -LiteralPath $buildInfoPath -PathType Leaf) { Get-Content -LiteralPath $buildInfoPath -Raw | ConvertFrom-Json } else { $null }
    $distFiles = if (Test-Path -LiteralPath $distDir) { @(Get-ChildItem -LiteralPath $distDir -File -Force | ForEach-Object Name) } else { @() }
    $problems = [System.Collections.Generic.List[string]]::new()
    $markerProblems = @(Get-BuildInfoProblems -BuildInfo $buildInfo -Version $Version -Head $Head -DistFiles @($distFiles))
    foreach ($problem in $markerProblems) { $problems.Add($problem) }
    $files = @()
    if ($buildInfo -and $markerProblems.Count -eq 0) {
        $sumsPath = Join-Path $distDir $buildInfo.ChecksumFile
        foreach ($problem in @(Get-Sha256SumsProblems $sumsPath)) { $problems.Add($problem) }
        foreach ($file in @($buildInfo.Files)) {
            if ((Get-FileSha256 (Join-Path $distDir $file.Name)) -ne $file.Sha256) { $problems.Add("$($file.Name) changed after the build. Rebuild.") }
        }
        $files = @($buildInfo.Files | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Sha256 = $_.Sha256 } }) +
            [pscustomobject]@{ Name = $buildInfo.ChecksumFile; Sha256 = Get-FileSha256 $sumsPath }
    }
    [pscustomobject]@{ DistDirectory = $distDir; BuildInfo = $buildInfo; Files = $files; Problems = $problems.ToArray() }
}

# ---------------------------------------------------------------------------------------------------------------------
# Release orchestrator (scripts/release.ps1): Gitflow steps 1 and 2 of docs/release.md, then build and draft upload.
# The facts are collected by Get-ReleaseFlowFacts (read-only git, gh and file queries); Get-ReleaseFlowPlan decides
# purely from them which steps are already done (resume) and what blocks the remaining ones.
# ---------------------------------------------------------------------------------------------------------------------

function Get-ReleaseVersionProblems {
    <#
    .SYNOPSIS
        Pure check of the version to release: plain X.Y.Z, greater than every existing release tag (its own tag excluded,
        so that a resumed release passes) and not lower than <Version> on develop. Returns problem messages.
    #>
    param(
        [AllowNull()] [AllowEmptyString()] [string] $Version,
        [AllowNull()] [AllowEmptyCollection()] [string[]] $ExistingTags = @(),
        [AllowNull()] [AllowEmptyString()] [string] $SourceVersion
    )
    $problems = [System.Collections.Generic.List[string]]::new()
    if (-not (Test-ReleaseVersion $Version)) {
        $problems.Add("'$Version' is not a version in the form X.Y.Z (e.g. 0.1.0).")
        return $problems.ToArray()
    }
    $own = ConvertTo-ReleaseTag $Version
    $released = @($ExistingTags | Where-Object { $_ -and $_ -cne $own } | ForEach-Object { Get-VersionFromTag $_ } | Where-Object { $_ } |
        ForEach-Object { [version] $_ } | Sort-Object -Descending)
    if ($released.Count -gt 0 -and [version] $Version -le $released[0]) {
        $problems.Add("Version $Version is not greater than the latest release tag v$($released[0]).")
    }
    if ((Test-ReleaseVersion $SourceVersion) -and [version] $Version -lt [version] $SourceVersion) {
        $problems.Add("Version $Version is lower than <Version> $SourceVersion on develop.")
    }
    $problems.ToArray()
}

function Get-VersionFromPropsText {
    <# .SYNOPSIS The single <Version> value of a Directory.Build.props text; $null when there is not exactly one. #>
    param([AllowNull()] [AllowEmptyString()] [string] $Text)
    $found = [regex]::Matches("$Text", '<Version>\s*(?<v>[^<]*?)\s*</Version>')
    if ($found.Count -ne 1) { return $null }
    $found[0].Groups['v'].Value
}

function Set-SourceVersionText {
    <# .SYNOPSIS Pure: the Directory.Build.props text with its single <Version> set to Version (everything else unchanged). #>
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Text, [Parameter(Mandatory)] [string] $Version)
    if (-not (Test-ReleaseVersion $Version)) { throw "'$Version' is not a version in the form X.Y.Z." }
    $pattern = '(?<open><Version>)\s*[^<]*?\s*(?<close></Version>)'
    $count = [regex]::Matches($Text, $pattern).Count
    if ($count -ne 1) { throw "Expected exactly one <Version> in Directory.Build.props, found $count." }
    [regex]::Replace($Text, $pattern, { param($m) "$($m.Groups['open'].Value)$Version$($m.Groups['close'].Value)" })
}

function ConvertTo-ReleaseChangelog {
    <#
    .SYNOPSIS
        Pure: the CHANGELOG.md text (Keep a Changelog) with the release date for Version.
    .DESCRIPTION
        - A section '## [X.Y.Z]' (with '- unreleased', another placeholder, a date, or an en/em dash instead of '-') gets
          the heading '## [X.Y.Z] - <Date>'. [Unreleased] must be empty then (entries in both are not merged automatically).
        - Without such a section the entries of '## [Unreleased]' move into a new section '## [X.Y.Z] - <Date>' right
          below it, and '## [Unreleased]' stays on top. Sub-headings without entries (an empty '### Fixed' template) and
          a preamble without entries (e.g. a comment) stay under [Unreleased]; only sub-sections with entries move.
        - Link references: '[Unreleased]: <base>/compare/vA.B.C...HEAD' (A.B.C other than X.Y.Z) becomes
          '<base>/compare/vX.Y.Z...HEAD', and '[X.Y.Z]: <base>/compare/vA.B.C...vX.Y.Z' is added below it when missing.
        Every line keeps its own line ending; new lines get the most frequent line ending of the file. Throws when there is
        nothing to release or the file is ambiguous.
    .OUTPUTS
        Text, Change ('Dated', 'Redated', 'Unchanged' or 'Moved'), OldHeading, NewHeading.
    #>
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Text,
        [Parameter(Mandatory)] [string] $Version,
        [Parameter(Mandatory)] [string] $Date
    )
    if (-not (Test-ReleaseVersion $Version)) { throw "'$Version' is not a version in the form X.Y.Z." }
    $parsed = [datetime]::MinValue
    if (-not ($Date -match '^\d{4}-\d{2}-\d{2}$' -and [datetime]::TryParseExact($Date, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$parsed))) {
        throw "Release date '$Date' is not in the form YYYY-MM-DD."
    }

    # One record per line: its text and its own line ending ('' for a last line without one).
    $rec = [System.Collections.Generic.List[object]]::new()
    $position = 0
    while ($position -lt $Text.Length) {
        $next = $Text.IndexOfAny([char[]]"`r`n", $position)
        if ($next -lt 0) { $rec.Add([pscustomobject]@{ T = $Text.Substring($position); E = '' }); break }
        $eol = if ($Text[$next] -eq "`r" -and $next + 1 -lt $Text.Length -and $Text[$next + 1] -eq "`n") { "`r`n" } else { [string]$Text[$next] }
        $rec.Add([pscustomobject]@{ T = $Text.Substring($position, $next - $position); E = $eol })
        $position = $next + $eol.Length
    }
    $endsWithNewline = $rec.Count -gt 0 -and $rec[$rec.Count - 1].E -ne ''
    $dominant = @($rec | Where-Object E | Group-Object E -NoElement | Sort-Object Count -Descending | Select-Object -First 1 | ForEach-Object Name)
    $newline = if ($dominant.Count -gt 0) { $dominant[0] } else { "`n" }
    function Line([string] $T) { [pscustomobject]@{ T = $T; E = $newline } }

    $escaped = [regex]::Escape($Version)
    $dash = '[-–—]'
    $versionHeading = '^##\s+\[' + $escaped + '\](?:\s+' + $dash + '\s+(?<date>.*?))?\s*$'
    $linkReference = '^\[(Unreleased|\d+\.\d+\.\d+)\]:\s'
    $newHeading = "## [$Version] - $Date"

    function Find([string] $Pattern) { @(for ($i = 0; $i -lt $rec.Count; $i++) { if ($rec[$i].T -match $Pattern) { $i } }) }
    function BodyEnd([int] $Start) {
        # The body of a section ends before the next '## ' heading or the link references at the end of the file.
        for ($i = $Start + 1; $i -lt $rec.Count; $i++) { if ($rec[$i].T -match '^##\s' -or $rec[$i].T -match $linkReference) { return $i } }
        $rec.Count
    }
    function IsEntry([string] $T) { -not ([string]::IsNullOrWhiteSpace($T) -or $T.TrimStart() -match '^(#|<!--)' -or $T -match $linkReference) }
    function HasEntries([int] $Start) {
        for ($i = $Start + 1; $i -lt (BodyEnd $Start); $i++) { if (IsEntry $rec[$i].T) { return $true } }
        $false
    }
    function Trimmed([object[]] $Block) {
        $list = [System.Collections.Generic.List[object]]::new()
        foreach ($r in $Block) { $list.Add($r) }
        while ($list.Count -gt 0 -and [string]::IsNullOrWhiteSpace($list[0].T)) { $list.RemoveAt(0) }
        while ($list.Count -gt 0 -and [string]::IsNullOrWhiteSpace($list[$list.Count - 1].T)) { $list.RemoveAt($list.Count - 1) }
        , $list.ToArray()
    }

    $unreleased = @(Find '^##\s+\[Unreleased\]\s*$')
    if ($unreleased.Count -eq 0) { throw "CHANGELOG.md has no '## [Unreleased]' section." }
    if ($unreleased.Count -gt 1) { throw "CHANGELOG.md has $($unreleased.Count) '## [Unreleased]' sections; keep exactly one." }
    $sections = @(Find $versionHeading)
    if ($sections.Count -gt 1) { throw "CHANGELOG.md has $($sections.Count) sections for version $Version; keep exactly one." }
    $unreleasedHasEntries = HasEntries $unreleased[0]

    if ($sections.Count -eq 1) {
        $index = $sections[0]
        if ($unreleasedHasEntries) {
            throw "CHANGELOG.md has entries both under [Unreleased] and under [$Version]. Move them into one section by hand."
        }
        if (-not (HasEntries $index)) { throw "Section [$Version] in CHANGELOG.md is empty. Write the release notes of $Version first." }
        $oldHeading = $rec[$index].T
        $null = $oldHeading -match $versionHeading
        $oldDate = if ($Matches.ContainsKey('date')) { $Matches.date } else { '' }
        $change = if ($oldDate -ceq $Date -and $oldHeading -ceq $newHeading) { 'Unchanged' } elseif ($oldDate -match '^\d{4}-\d{2}-\d{2}$') { 'Redated' } else { 'Dated' }
        $rec[$index].T = $newHeading
    }
    else {
        if (-not $unreleasedHasEntries) {
            throw "CHANGELOG.md has no entries for $($Version): neither a section '## [$Version]' nor entries under '## [Unreleased]'. Write the release notes first."
        }
        $start = $unreleased[0]
        $end = BodyEnd $start
        $oldHeading = $rec[$start].T
        $change = 'Moved'
        # Blocks: a preamble before the first '###' and one block per '###' sub-section.
        $blocks = [System.Collections.Generic.List[object]]::new()
        $current = [System.Collections.Generic.List[object]]::new()
        for ($i = $start + 1; $i -lt $end; $i++) {
            if ($rec[$i].T -match '^###\s' -and $current.Count -gt 0) { $blocks.Add($current.ToArray()); $current = [System.Collections.Generic.List[object]]::new() }
            $current.Add($rec[$i])
        }
        if ($current.Count -gt 0) { $blocks.Add($current.ToArray()) }
        $kept = @(); $moved = @()
        foreach ($block in $blocks) {
            $trimmed = Trimmed $block
            if ($trimmed.Count -eq 0) { continue }
            if (@($trimmed | Where-Object { IsEntry $_.T }).Count -gt 0) { $moved += , $trimmed } else { $kept += , $trimmed }
        }
        $body = [System.Collections.Generic.List[object]]::new()
        $body.Add((Line ''))
        foreach ($block in $kept) { foreach ($r in $block) { $body.Add($r) }; $body.Add((Line '')) }
        $body.Add((Line $newHeading))
        $body.Add((Line ''))
        foreach ($block in $moved) { foreach ($r in $block) { $body.Add($r) }; $body.Add((Line '')) }
        # A blank line separates the section from what follows; at the end of the file there is nothing to separate.
        if ($end -ge $rec.Count) { $body.RemoveAt($body.Count - 1) }
        $rec.RemoveRange($start + 1, $end - $start - 1)
        $rec.InsertRange($start + 1, $body)
    }

    $links = @(Find '^\[Unreleased\]:\s*\S+/compare/v\d+\.\d+\.\d+\.\.\.HEAD\s*$')
    if ($links.Count -eq 1) {
        $null = $rec[$links[0]].T -match '^\[Unreleased\]:\s*(?<base>\S+)/compare/v(?<previous>\d+\.\d+\.\d+)\.\.\.HEAD\s*$'
        $base = $Matches.base
        $previous = $Matches.previous
        if ($previous -cne $Version) {
            $rec[$links[0]].T = "[Unreleased]: $base/compare/v$Version...HEAD"
            if (@(Find ('^\[' + $escaped + '\]:\s')).Count -eq 0) { $rec.Insert($links[0] + 1, (Line "[$Version]: $base/compare/v$previous...v$Version")) }
        }
    }

    # Line endings: every line but the last has one; the last keeps the state of the original file.
    for ($i = 0; $i -lt $rec.Count - 1; $i++) { if ($rec[$i].E -eq '') { $rec[$i].E = $newline } }
    if ($rec.Count -gt 0) {
        $last = $rec[$rec.Count - 1]
        if (-not $endsWithNewline) { $last.E = '' } elseif ($last.E -eq '') { $last.E = $newline }
    }
    $builder = [System.Text.StringBuilder]::new()
    foreach ($r in $rec) { $null = $builder.Append($r.T).Append($r.E) }
    [pscustomobject]@{ Text = $builder.ToString(); Change = $change; OldHeading = $oldHeading; NewHeading = $newHeading }
}

function ConvertFrom-ReleaseTextBytes {
    <# .SYNOPSIS UTF-8 text of bytes and whether they start with a byte order mark (kept when written back). #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [byte[]] $Bytes)
    $bom = $Bytes.Length -ge 3 -and $Bytes[0] -eq 0xEF -and $Bytes[1] -eq 0xBB -and $Bytes[2] -eq 0xBF
    $offset = if ($bom) { 3 } else { 0 }
    [pscustomobject]@{ Text = [System.Text.UTF8Encoding]::new($false, $true).GetString($Bytes, $offset, $Bytes.Length - $offset); Bom = $bom }
}

function Read-ReleaseTextFile {
    <# .SYNOPSIS UTF-8 text of a file and whether it starts with a byte order mark (kept when the file is written back). #>
    param([Parameter(Mandatory)] [string] $Path)
    ConvertFrom-ReleaseTextBytes ([System.IO.File]::ReadAllBytes($Path))
}

function Write-ReleaseTextFile {
    <# .SYNOPSIS Writes UTF-8 text, with a byte order mark only when Bom is set. #>
    param([Parameter(Mandatory)] [string] $Path, [Parameter(Mandatory)] [AllowEmptyString()] [string] $Text, [bool] $Bom = $false)
    [System.IO.File]::WriteAllText($Path, $Text, [System.Text.UTF8Encoding]::new($Bom))
}

function Read-GitBlobText {
    <#
    .SYNOPSIS
        Text of a file in a commit (Spec 'commit:path'), byte-exact from git cat-file blob (not through the decoding of the
        PowerShell pipeline), as ConvertFrom-ReleaseTextBytes. $null when the file does not exist there.
    #>
    param([Parameter(Mandatory)] [string] $RepoRoot, [Parameter(Mandatory)] [string] $Spec)
    $info = [System.Diagnostics.ProcessStartInfo]::new('git')
    foreach ($argument in @('-C', $RepoRoot, 'cat-file', 'blob', $Spec)) { $info.ArgumentList.Add($argument) }
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.UseShellExecute = $false
    $process = [System.Diagnostics.Process]::Start($info)
    try {
        $errorText = $process.StandardError.ReadToEndAsync()
        $buffer = [System.IO.MemoryStream]::new()
        $process.StandardOutput.BaseStream.CopyTo($buffer)
        $process.WaitForExit()
        $null = $errorText.Result
        if ($process.ExitCode -ne 0) { return $null }
        ConvertFrom-ReleaseTextBytes $buffer.ToArray()
    }
    finally { $process.Dispose() }
}

function Invoke-GitChecked {
    <# .SYNOPSIS Runs git and returns its stdout lines; throws with git's error output on a non-zero exit code. #>
    param([Parameter(Mandatory)] [string] $RepoRoot, [Parameter(Mandatory)] [string[]] $Arguments)
    $result = Invoke-Git $RepoRoot $Arguments
    if ($result.ExitCode -ne 0) { throw "git $($Arguments -join ' ') failed (exit code $($result.ExitCode)): $($result.Error) $($result.Output -join ' ')".Trim() }
    $result.Output
}

function Get-ReleaseCommitState {
    <#
    .SYNOPSIS
        Checks the files of a release commit (the tip of release/X.Y.Z, the tagged merge, or the merge before it is tagged)
        byte-exact from git: <Version> of Directory.Build.props and a dated, non-empty CHANGELOG.md section (the upload
        check, Get-ChangelogSection without -AllowUndated). When the section is dated with another day than Date, Redate
        describes the change of ConvertTo-ReleaseChangelog. Reads only; a temporary copy in TempDirectory is deleted.
    .OUTPUTS
        Version, VersionProblem, ChangelogProblem, Date, Redate (OldHeading, NewHeading or $null).
    #>
    param(
        [Parameter(Mandatory)] [string] $RepoRoot,
        [Parameter(Mandatory)] [string] $Commit,
        [Parameter(Mandatory)] [string] $Version,
        [Parameter(Mandatory)] [string] $Date,
        [Parameter(Mandatory)] [string] $TempDirectory
    )
    $props = Read-GitBlobText $RepoRoot "$($Commit):Directory.Build.props"
    $found = if ($props) { Get-VersionFromPropsText $props.Text } else { $null }
    $versionProblem = if ($found -cne $Version) { "its Directory.Build.props has version '$found' instead of $Version" } else { $null }
    $changelogProblem = $null
    $sectionDate = $null
    $redate = $null
    $changelog = Read-GitBlobText $RepoRoot "$($Commit):CHANGELOG.md"
    if (-not $changelog) { $changelogProblem = 'it has no CHANGELOG.md.' }
    else {
        $copy = Join-Path $TempDirectory "check-$([guid]::NewGuid().ToString('N').Substring(0, 8))-CHANGELOG.md"
        try {
            Write-ReleaseTextFile $copy $changelog.Text $changelog.Bom
            $null = Get-ChangelogSection -Path $copy -Version $Version
        }
        catch { $changelogProblem = $_.Exception.Message.Replace($copy, 'CHANGELOG.md') }
        finally { Remove-Item -LiteralPath $copy -Force -ErrorAction SilentlyContinue }
        if (-not $changelogProblem) {
            $heading = [regex]::Match($changelog.Text, '(?m)^##\s+\[' + [regex]::Escape($Version) + '\]\s+-\s+(?<date>\d{4}-\d{2}-\d{2})\s*\r?$')
            $sectionDate = if ($heading.Success) { $heading.Groups['date'].Value } else { $null }
            if ($sectionDate -cne $Date) {
                try {
                    $converted = ConvertTo-ReleaseChangelog -Text $changelog.Text -Version $Version -Date $Date
                    $redate = [pscustomobject]@{ OldHeading = $converted.OldHeading; NewHeading = $converted.NewHeading }
                }
                catch { $changelogProblem = $_.Exception.Message }
            }
        }
    }
    [pscustomobject]@{ Version = $found; VersionProblem = $versionProblem; ChangelogProblem = $changelogProblem; Date = $sectionDate; Redate = $redate }
}

function Test-ReleaseTestToolchainSkip {
    <#
    .SYNOPSIS
        Test-only: true when the Pester tests asked to skip the MSVC / Xcode / vpk check. Needs
        LAYERPEEK_RELEASE_TEST_SKIP_TOOLCHAIN=1 and LAYERPEEK_PESTER_TESTS_TEMP, and the repository must lie inside that
        temp folder, so that variables left behind by an aborted Pester run in an interactive pwsh never affect a real
        repository.
    #>
    param([Parameter(Mandatory)] [string] $RepoRoot)
    $base = $env:LAYERPEEK_PESTER_TESTS_TEMP
    if (-not $base -or $env:LAYERPEEK_RELEASE_TEST_SKIP_TOOLCHAIN -ne '1') { return $false }
    $separators = [char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $root = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd($separators)
    $folder = [System.IO.Path]::GetFullPath($base).TrimEnd($separators)
    $comparison = if ($IsWindows -or $IsMacOS) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $root.StartsWith($folder + [System.IO.Path]::DirectorySeparatorChar, $comparison)
}

function Get-ReleaseFlowFacts {
    <#
    .SYNOPSIS
        Collects everything scripts/release.ps1 decides on. Read-only: one git ls-remote of the release remote, local git
        queries (files of commits byte-exact via git cat-file), gh auth status and gh release view, the build output and
        the toolchain check. CHANGELOG.md checks run on copies inside TempDirectory. Nothing is fetched, pushed or changed.
    .PARAMETER Repository
        owner/name as for release-upload.ps1 -Repo; empty = origin. The git steps use the remote of that repository.
    .NOTES
        Test-only: with LAYERPEEK_RELEASE_TEST_SKIP_TOOLCHAIN=1 the MSVC / Xcode / vpk check is skipped, but only while
        LAYERPEEK_PESTER_TESTS_TEMP is set too (the Pester tests of scripts/ReleaseTools set both); never in a release.
    #>
    param(
        [Parameter(Mandatory)] [string] $RepoRoot,
        [Parameter(Mandatory)] [string] $Version,
        [string] $Repository,
        [Parameter(Mandatory)] [string] $TempDirectory,
        [Parameter(Mandatory)] [string] $Date,
        [string] $HostOs = (Get-HostOs)
    )
    $tag = ConvertTo-ReleaseTag $Version
    $branch = "release/$Version"

    function Commit([string] $Ref) {
        $r = Invoke-Git $RepoRoot @('rev-parse', '--verify', '--quiet', "$Ref^{commit}")
        if ($r.ExitCode -eq 0 -and $r.Output.Count -gt 0) { $r.Output[0].Trim() } else { $null }
    }
    function Present([string] $Sha) { [bool]($Sha -and (Invoke-Git $RepoRoot @('cat-file', '-e', "$Sha^{commit}")).ExitCode -eq 0) }
    function Ancestor([string] $Older, [string] $Newer) {
        if (-not ($Older -and $Newer)) { return $null }
        switch ((Invoke-Git $RepoRoot @('merge-base', '--is-ancestor', $Older, $Newer)).ExitCode) { 0 { $true } 1 { $false } default { $null } }
    }
    function Parents([string] $Sha) {
        if (-not $Sha) { return @() }
        $r = Invoke-Git $RepoRoot @('rev-list', '--parents', '-n', '1', $Sha)
        if ($r.ExitCode -ne 0 -or $r.Output.Count -eq 0) { return @() }
        @(($r.Output[0].Trim() -split '\s+') | Select-Object -Skip 1)
    }
    function Relation([string] $Local, [string] $Remote) {
        # How a local branch relates to its remote counterpart (both present locally).
        if (-not $Local) { return 'missing' }
        if (-not $Remote) { return 'unknown' }
        if ($Local -eq $Remote) { return 'equal' }
        if ((Ancestor $Remote $Local) -eq $true) { return 'ahead' }
        if ((Ancestor $Local $Remote) -eq $true) { return 'behind' }
        'diverged'
    }
    function PropsVersionAt([string] $Commit) {
        if (-not $Commit) { return $null }
        $blob = Read-GitBlobText $RepoRoot "$($Commit):Directory.Build.props"
        if ($blob) { Get-VersionFromPropsText $blob.Text } else { $null }
    }
    function ConfiguredRepository([string] $Name, [string] $Key) {
        $r = Invoke-Git $RepoRoot @('config', '--get', "remote.$Name.$Key")
        if ($r.ExitCode -eq 0 -and $r.Output.Count -gt 0) { Get-GitHubRepository "$($r.Output[0])" } else { $null }
    }

    $remotes = Get-GitRemotes $RepoRoot
    $target = Resolve-ReleaseRemote -Remotes $remotes -Repository $Repository
    $remote = $target.Remote
    $remoteProblem = $target.Problem
    $hasOrigin = $remotes.Contains('origin')
    $originRepository = if ($hasOrigin) { Get-GitHubRepository "$($remotes['origin'])" } else { $null }
    # The repository a push goes to: the push URL after pushInsteadOf / insteadOf; when that is no GitHub URL (a local
    # mirror in the tests), the configured pushurl or url.
    $pushRepository = $null
    if ($remote) {
        $pushUrl = Invoke-Git $RepoRoot @('remote', 'get-url', '--push', $remote)
        if ($pushUrl.ExitCode -eq 0 -and $pushUrl.Output.Count -gt 0) { $pushRepository = Get-GitHubRepository "$($pushUrl.Output[0])" }
        if (-not $pushRepository) { $pushRepository = (ConfiguredRepository $remote 'pushurl') ?? (ConfiguredRepository $remote 'url') }
    }

    $remoteQueried = $false
    $remoteMain = $null; $remoteDevelop = $null; $remoteTag = $null
    $remoteTags = [System.Collections.Generic.List[string]]::new()
    if ($remote) {
        $ls = Invoke-Git $RepoRoot @('ls-remote', $remote)
        if ($ls.ExitCode -ne 0) { $remoteProblem = "git ls-remote $remote failed: $($ls.Error)" }
        else {
            $remoteQueried = $true
            $plainTag = $null
            foreach ($line in $ls.Output) {
                $parts = $line -split '\s+'
                if ($parts.Count -lt 2) { continue }
                switch -CaseSensitive ($parts[1]) {
                    'refs/heads/main' { $remoteMain = $parts[0] }
                    'refs/heads/develop' { $remoteDevelop = $parts[0] }
                    "refs/tags/$tag^{}" { $remoteTag = $parts[0] }
                    "refs/tags/$tag" { $plainTag = $parts[0] }
                }
                if ($parts[1] -match '^refs/tags/(?<name>[^\^]+)$') { $remoteTags.Add($Matches.name) }
            }
            if (-not $remoteTag -and $plainTag) {
                # A lightweight tag points to the commit itself; an annotated tag not yet fetched cannot be peeled here.
                $remoteTag = if (Present $plainTag) { Commit $plainTag } else { $plainTag }
            }
        }
    }

    $statusLines = @(Get-GitStatusLines $RepoRoot)
    $gitVersionText = (Invoke-Git $RepoRoot @('--version')).Output | Select-Object -First 1
    $gitVersion = if ("$gitVersionText" -match '(\d+)\.(\d+)') { [version]"$($Matches[1]).$($Matches[2])" } else { $null }
    $symbolic = Invoke-Git $RepoRoot @('symbolic-ref', '--quiet', '--short', 'HEAD')
    $currentBranch = if ($symbolic.ExitCode -eq 0 -and $symbolic.Output.Count -gt 0) { $symbolic.Output[0].Trim() } else { $null }

    # Branches checked out in other worktrees: their refs must not be moved under them.
    $top = (Invoke-GitChecked $RepoRoot @('rev-parse', '--show-toplevel') | Select-Object -First 1).Trim()
    $otherWorktreeBranches = [System.Collections.Generic.List[string]]::new()
    $worktree = $null
    foreach ($line in @(Invoke-GitChecked $RepoRoot @('worktree', 'list', '--porcelain'))) {
        if ($line -match '^worktree (?<path>.+)$') { $worktree = $Matches.path.Trim() }
        elseif ($line -match '^branch refs/heads/(?<name>.+)$' -and -not [string]::Equals($worktree, $top, [StringComparison]::OrdinalIgnoreCase)) {
            $otherWorktreeBranches.Add($Matches.name.Trim())
        }
    }

    $localMain = Commit 'refs/heads/main'
    $localDevelop = Commit 'refs/heads/develop'
    $localBranch = Commit "refs/heads/$branch"
    $localTag = Commit "refs/tags/$tag"
    $localTags = @(Invoke-GitChecked $RepoRoot @('tag', '--list') | Where-Object { $_ })
    $remoteMainPresent = Present $remoteMain
    $remoteDevelopPresent = Present $remoteDevelop

    $tagParents = @(Parents $localTag)
    $releaseCommit = if ($tagParents.Count -eq 2) { $tagParents[1] } elseif (-not $localTag -and $localBranch) { $localBranch } else { $null }
    $developParents = @(Parents $localDevelop)

    # The release commit is checked whenever it exists (an existing release branch, or the tag), not only on a fresh run.
    $checkCommit = if ($localTag) { $localTag } elseif ($localBranch) { $localBranch } else { $null }
    $releaseCheck = if ($checkCommit) { Get-ReleaseCommitState -RepoRoot $RepoRoot -Commit $checkCommit -Version $Version -Date $Date -TempDirectory $TempDirectory } else { $null }
    $developContainsRelease = Ancestor $releaseCommit $localDevelop
    # Commits of the release branch that develop does not have yet (stabilization fixes): listed, not refused.
    $branchOnlyCommits = if ($releaseCommit -and $localDevelop -and $developContainsRelease -ne $true) {
        @(Invoke-GitChecked $RepoRoot @('log', '--oneline', '--no-decorate', "$localDevelop..$releaseCommit") | Where-Object { $_ })
    }
    else { @() }

    $sourceVersion = PropsVersionAt $localDevelop
    $propsChange = $null
    $changelog = $null
    if (-not $localTag -and -not $remoteTag -and -not $localBranch -and $localDevelop) {
        # Fresh release: the transformation of the develop files (byte-exact from git, as the release commit will use).
        try {
            $props = Read-GitBlobText $RepoRoot "$($localDevelop):Directory.Build.props"
            if (-not $props) { throw 'not found on develop.' }
            $null = Set-SourceVersionText -Text $props.Text -Version $Version
            $propsChange = [pscustomobject]@{ From = (Get-VersionFromPropsText $props.Text); To = $Version; Problem = $null }
        }
        catch { $propsChange = [pscustomobject]@{ From = $null; To = $Version; Problem = "Directory.Build.props: $($_.Exception.Message)" } }
        try {
            $source = Read-GitBlobText $RepoRoot "$($localDevelop):CHANGELOG.md"
            if (-not $source) { throw 'CHANGELOG.md not found on develop.' }
            $converted = ConvertTo-ReleaseChangelog -Text $source.Text -Version $Version -Date $Date
            $copy = Join-Path $TempDirectory 'CHANGELOG.md'
            Write-ReleaseTextFile $copy $converted.Text $source.Bom
            # The same check as the upload: a dated, non-empty section.
            $null = Get-ChangelogSection -Path $copy -Version $Version
            $changelog = [pscustomobject]@{ Change = $converted.Change; OldHeading = $converted.OldHeading; NewHeading = $converted.NewHeading; Problem = $null }
        }
        catch { $changelog = [pscustomobject]@{ Change = $null; OldHeading = $null; NewHeading = $null; Problem = $_.Exception.Message } }
    }

    $ghProblems = @(Get-GhProblems)
    $release = $null
    $releaseError = $null
    if ($ghProblems.Count -eq 0 -and $target.Repository -and -not $target.Problem) {
        try { $release = Get-GitHubRelease -Repository $target.Repository -Tag $tag }
        catch { $releaseError = $_.Exception.Message }
    }

    $build = if ($localTag) { Get-ReleaseBuildOutput -RepoRoot $RepoRoot -Version $Version -Head $localTag } else { $null }
    $buildCurrent = [bool]($build -and $build.Problems.Count -eq 0)
    $uploadPlan = if ($release -and $buildCurrent) { @(Get-UploadPlan -LocalFiles @($build.Files) -RemoteAssets @($release.assets)) } else { @() }
    $checksumFile = Get-ChecksumFileName $HostOs

    $toolchainProblems = [System.Collections.Generic.List[string]]::new()
    $runtimes = @(Resolve-ReleaseRuntimes -HostOs $HostOs)
    $skipToolchain = Test-ReleaseTestToolchainSkip -RepoRoot $RepoRoot
    if (-not $skipToolchain) {
        foreach ($problem in @(Get-NativeToolchainProblems -Runtimes $runtimes -HostOs $HostOs)) { $toolchainProblems.Add($problem) }
        if ($HostOs -eq 'windows') {
            $packagesProps = Join-Path $RepoRoot 'Directory.Packages.props'
            $toolManifest = Join-Path $RepoRoot '.config' 'dotnet-tools.json'
            if ((Test-Path -LiteralPath $packagesProps -PathType Leaf) -and (Test-Path -LiteralPath $toolManifest -PathType Leaf)) {
                foreach ($problem in @(Get-VelopackVersionProblems -PackagesPropsPath $packagesProps -ToolManifestPath $toolManifest)) { $toolchainProblems.Add($problem) }
            }
            else { $toolchainProblems.Add("The Velopack installer step needs $packagesProps and $toolManifest.") }
        }
    }

    [pscustomobject]@{
        Version                      = $Version
        Tag                          = $tag
        Branch                       = $branch
        Date                         = $Date
        HostOs                       = $HostOs
        Runtimes                     = $runtimes
        Repository                   = $target.Repository
        RepositoryArgument           = $Repository
        Remote                       = $remote
        RemoteProblem                = $remoteProblem
        HasOrigin                    = $hasOrigin
        OriginRepository             = $originRepository
        PushRepository               = $pushRepository
        RemoteQueried                = $remoteQueried
        RemoteMain                   = $remoteMain
        RemoteDevelop                = $remoteDevelop
        RemoteTagCommit              = $remoteTag
        RemoteMainPresent            = $remoteMainPresent
        RemoteDevelopPresent         = $remoteDevelopPresent
        CurrentBranch                = $currentBranch
        StatusLines                  = $statusLines
        OtherWorktreeBranches        = $otherWorktreeBranches.ToArray()
        LocalMain                    = $localMain
        LocalDevelop                 = $localDevelop
        LocalBranchCommit            = $localBranch
        LocalTagCommit               = $localTag
        ExistingTags                 = @(@($localTags) + @($remoteTags) | Sort-Object -Unique)
        MainRelation                 = if ($remoteMainPresent) { Relation $localMain $remoteMain } else { 'unknown' }
        DevelopRelation              = if ($remoteDevelopPresent) { Relation $localDevelop $remoteDevelop } else { 'unknown' }
        DevelopFirstParent           = if ($developParents.Count -ge 1) { $developParents[0] } else { $null }
        DevelopSecondParent          = if ($developParents.Count -ge 2) { $developParents[1] } else { $null }
        DevelopFirstParentInRemote   = if ($developParents.Count -ge 1 -and $remoteDevelopPresent) { Ancestor $developParents[0] $remoteDevelop } else { $null }
        TagFirstParent               = if ($tagParents.Count -ge 1) { $tagParents[0] } else { $null }
        TagParentCount               = $tagParents.Count
        TagVersion                   = PropsVersionAt $localTag
        BranchVersion                = PropsVersionAt $localBranch
        ReleaseCommit                = $releaseCommit
        ReleaseCheck                 = $releaseCheck
        BranchOnlyCommits            = $branchOnlyCommits
        DevelopIsAncestorOfRelease   = Ancestor $localDevelop $releaseCommit
        LocalMainContainsTag         = Ancestor $localTag $localMain
        LocalMainIsAncestorOfTag     = if ($localMain) { Ancestor $localMain $localTag } else { $true }
        RemoteMainContainsTag        = if ($remoteMainPresent) { Ancestor $localTag $remoteMain } else { $null }
        LocalDevelopContainsRelease  = $developContainsRelease
        RemoteDevelopContainsRelease = if ($remoteDevelopPresent) { Ancestor $releaseCommit $remoteDevelop } else { $null }
        SourceVersion                = $sourceVersion
        PropsChange                  = $propsChange
        Changelog                    = $changelog
        GhProblems                   = $ghProblems
        Release                      = $release
        ReleaseError                 = $releaseError
        BuildCurrent                 = $buildCurrent
        BuildProblems                = if ($build) { @($build.Problems) } else { @() }
        UploadPlan                   = $uploadPlan
        ChecksumFile                 = $checksumFile
        ToolchainProblems            = $toolchainProblems.ToArray()
        ToolchainSkipped             = [bool]$skipToolchain
        GitVersion                   = $gitVersion
    }
}

function Get-ReleaseFlowPlan {
    <#
    .SYNOPSIS
        Pure decision of scripts/release.ps1 from Get-ReleaseFlowFacts: the steps with Done (detected from the state, so
        that a re-run continues where the last run stopped), every problem that blocks the remaining steps (all at once)
        and notes (information that blocks nothing, e.g. stabilization commits of the release branch).
    .OUTPUTS
        Steps (Id, Title, Detail, Outward, Done), Problems, Notes, Published (the GitHub release is already published).
    #>
    param([Parameter(Mandatory)] $Facts)
    $f = $Facts
    $problems = [System.Collections.Generic.List[string]]::new()
    $notes = [System.Collections.Generic.List[string]]::new()
    function Add([string] $Problem) { if ($Problem -and -not $problems.Contains($Problem)) { $problems.Add($Problem) } }
    function Short([string] $Sha) { if ($Sha) { $Sha.Substring(0, [Math]::Min(7, $Sha.Length)) } else { '-' } }
    $r = $f.Remote ?? 'origin'
    $tag = $f.Tag
    $branch = $f.Branch
    $check = $f.ReleaseCheck

    foreach ($problem in @(Get-ReleaseVersionProblems -Version $f.Version -ExistingTags $f.ExistingTags -SourceVersion $(if ($f.Changelog) { $f.SourceVersion } else { $null }))) { Add $problem }
    if ($f.CurrentBranch -cne 'develop') {
        Add "Run the release from develop (HEAD is $($f.CurrentBranch ?? 'detached')): git switch develop"
    }
    $status = @($f.StatusLines)
    if ($status.Count -gt 0) {
        $shown = ($status | Select-Object -First 5) -join '; '
        $more = if ($status.Count -gt 5) { " (and $($status.Count - 5) more)" } else { '' }
        Add "Working tree is not clean ($($status.Count) changed or untracked files: $shown$more). Commit, stash or remove them first."
    }
    foreach ($name in @('main', $branch)) {
        if (@($f.OtherWorktreeBranches) -ccontains $name) { Add "Branch $name is checked out in another worktree (git worktree list); switch that worktree to another branch first." }
    }
    Add $f.RemoteProblem
    if ($f.RepositoryArgument -and $f.HasOrigin -and $f.OriginRepository -ne $f.Repository) {
        Add "This clone's origin is $($f.OriginRepository ?? 'not a GitHub repository'), not -Repo $($f.RepositoryArgument): main and develop of this clone would get the release merges of another repository. Run it in a separate temporary clone without origin (docs/release.md, trial release)."
    }
    if ($f.Remote -and $f.Repository -and $f.PushRepository -ne $f.Repository) {
        Add "Remote $r fetches from $($f.Repository) but pushes to $($f.PushRepository ?? 'a non-GitHub URL') (git remote get-url --push $r). Fix the push URL first."
    }
    foreach ($problem in @($f.GhProblems)) { Add $problem }
    Add $f.ReleaseError
    if ($f.RemoteQueried) {
        if (-not $f.RemoteMain) { Add "Branch main does not exist on $r." }
        elseif (-not $f.RemoteMainPresent) { Add "$r/main ($(Short $f.RemoteMain)) is not in the local repository. Run: git fetch $r" }
        if (-not $f.RemoteDevelop) { Add "Branch develop does not exist on $r." }
        elseif (-not $f.RemoteDevelopPresent) { Add "$r/develop ($(Short $f.RemoteDevelop)) is not in the local repository. Run: git fetch $r" }
    }
    if (-not $f.LocalDevelop) { Add 'There is no local branch develop.' }
    # The merges are made with git merge-tree --write-tree (git 2.38 or newer).
    $gitTooOld = -not $f.GitVersion -or $f.GitVersion -lt [version]'2.38'

    $tagLocal = $f.LocalTagCommit
    $tagRemote = $f.RemoteTagCommit
    $tagged = [bool]($tagLocal -or $tagRemote)
    if ($tagRemote -and -not $tagLocal) { Add "Tag $tag is on $r but not in the local repository. Run: git fetch $r --tags" }
    if ($tagRemote -and $tagLocal -and $tagRemote -ne $tagLocal) {
        Add "Tag $tag on $r points to $(Short $tagRemote), the local tag to $(Short $tagLocal). Tags are never moved automatically: resolve it by hand (docs/release.md)."
    }
    if ($tagLocal -and $f.TagParentCount -ne 2) {
        Add "Tag $tag ($(Short $tagLocal)) is not a merge commit of $branch into main. Finish this release by hand (docs/release.md)."
    }
    if ($tagLocal -and $f.TagVersion -cne $f.Version) {
        Add "Tag $tag exists, but its Directory.Build.props has version '$($f.TagVersion)'. Finish this release by hand (docs/release.md)."
    }
    if ($tagLocal -and $check) {
        if ($check.ChangelogProblem) { Add "Tag $($tag): $($check.ChangelogProblem) The tag is never changed automatically: finish this release by hand (docs/release.md)." }
        elseif ($check.Redate) { $notes.Add("CHANGELOG.md of $tag is dated $($check.Date), not the release day $($f.Date); the tagged release notes stay as they are.") }
    }

    $release = $f.Release
    $published = [bool]($release -and -not $release.isDraft)
    $assets = if ($release) { @($release.assets) } else { @() }
    $hostSumsUploaded = [bool](@($assets | Where-Object { $_.name -ceq $f.ChecksumFile }).Count -gt 0)
    $pending = @(@($f.UploadPlan) | Where-Object { $_.Action -ne 'Skip' })
    $branchExists = [bool]$f.LocalBranchCommit
    $redate = (-not $tagged -and $branchExists -and $check -and $check.Redate -and -not $check.ChangelogProblem -and -not $check.VersionProblem)

    $done = [ordered]@{
        Branch       = $tagged -or ($branchExists -and -not $redate)
        MergeMain    = [bool]$tagLocal -and $f.LocalMainContainsTag -eq $true
        PushMain     = [bool]$tagLocal -and $tagRemote -eq $tagLocal -and $f.RemoteMainContainsTag -eq $true
        MergeDevelop = $f.LocalDevelopContainsRelease -eq $true
        PushDevelop  = $f.RemoteDevelopContainsRelease -eq $true
        DeleteBranch = $tagged -and -not $branchExists
        Build        = $published -or $f.BuildCurrent
        Upload       = $published -or ([bool]$release -and $f.BuildCurrent -and $pending.Count -eq 0)
    }

    if ($gitTooOld -and ((-not $done.MergeMain -and -not $tagLocal) -or -not $done.MergeDevelop)) {
        Add "git $($f.GitVersion ?? '?') is too old: the release merges need git 2.38 or newer (git merge-tree --write-tree)."
    }
    $mainProblem = switch ($f.MainRelation) {
        'ahead' { "Local main has commits that are not on $r/main. Push or remove them first (the release merge is based on $r/main)." }
        'diverged' { "Local main and $r/main have diverged. Resolve it by hand first." }
        default { $null }
    }
    $developProblem = switch ($f.DevelopRelation) {
        'ahead' { "Local develop has commits that are not on $r/develop. Push them first (git push $r develop) or remove them." }
        'behind' { "Local develop is behind $r/develop. Run: git pull --ff-only" }
        'diverged' { "Local develop and $r/develop have diverged. Resolve it by hand first." }
        default { $null }
    }

    $fixBranch = "Fix it on the branch, or delete the branch (git branch -D $branch) and re-run."
    if (-not $branchExists -and -not $tagged) {
        if ($f.PropsChange) { Add $f.PropsChange.Problem }
        if ($f.Changelog) { Add $f.Changelog.Problem }
        Add $developProblem
    }
    elseif (-not $tagged) {
        # An existing release branch (made by an earlier run or by hand) is checked like a new one.
        if ($check -and $check.VersionProblem) { Add "$branch exists, but $($check.VersionProblem). $fixBranch" }
        if ($check -and $check.ChangelogProblem) { Add "$($branch): $($check.ChangelogProblem) $fixBranch" }
        if ($f.DevelopIsAncestorOfRelease -eq $false) {
            Add "$branch ($(Short $f.LocalBranchCommit)) is not based on the current develop ($(Short $f.LocalDevelop)): develop has commits that are not in the release branch. Merge develop into $branch, or delete the branch (git branch -D $branch) and re-run."
        }
    }
    if (-not $tagged -and $release) {
        Add "A GitHub release for $tag already exists ($($release.url)) although the tag does not. Delete it or choose another version."
    }
    if (-not $done.MergeMain) {
        if ($tagLocal) {
            if ($f.LocalMainIsAncestorOfTag -ne $true) { Add "Local main ($(Short $f.LocalMain)) is not contained in tag $tag; it cannot be moved to the tag without losing commits. Resolve it by hand." }
        }
        else { Add $mainProblem }
    }
    elseif (-not $done.PushMain -and $f.MainRelation -notin 'ahead', 'equal') {
        Add "$r/main has moved since the release merge (local main is $($f.MainRelation)). Nothing is force-pushed: resolve it by hand (docs/release.md)."
    }
    elseif (-not $done.PushMain -and $f.LocalMain -ne $tagLocal) {
        Add "Local main ($(Short $f.LocalMain)) has commits after the release merge of tag $tag ($(Short $tagLocal)). Only the tagged merge would be pushed, so main must be exactly at the tag: move those commits elsewhere (e.g. a branch) and run git branch -f main $tag, then re-run."
    }
    if (-not $done.MergeDevelop) {
        if ($branchExists -or $tagged) { Add $developProblem }
    }
    elseif (-not $done.PushDevelop) {
        $onlyMerge = $f.DevelopRelation -eq 'ahead' -and $f.DevelopFirstParent -eq $f.RemoteDevelop -and $f.DevelopSecondParent -eq $f.ReleaseCommit
        if ($f.DevelopRelation -eq 'diverged' -and $f.DevelopSecondParent -eq $f.ReleaseCommit -and $f.DevelopFirstParentInRemote -eq $true) {
            # Only the merge back is local: dropping it loses nothing that is not on the remote.
            Add "$r/develop has moved since the merge back of $branch (someone pushed to develop). Nothing is force-pushed: drop the local merge (git reset --keep $r/develop; only the merge is lost, the next run makes it again) and re-run."
        }
        elseif ($f.DevelopRelation -eq 'diverged') {
            Add "Local develop and $r/develop have diverged after the merge back of $branch, and local develop has commits that are not on $r/develop. Nothing is force-pushed: resolve it by hand (docs/release.md)."
        }
        elseif (-not $onlyMerge) { Add "Local develop has other commits besides the merge of $branch that are not on $r/develop. Push or remove them by hand first." }
    }
    if ($branchExists -and $f.ReleaseCommit -and $tagLocal -and $f.LocalBranchCommit -ne $f.ReleaseCommit) {
        Add "$branch ($(Short $f.LocalBranchCommit)) has commits that are not part of tag $tag. Merge or delete it by hand."
    }
    if (-not $done.MergeDevelop -and @($f.BranchOnlyCommits).Count -gt 0) {
        $notes.Add("$($branch) has $(@($f.BranchOnlyCommits).Count) commit(s) that develop does not have yet; they go to main and back to develop:")
        foreach ($line in @($f.BranchOnlyCommits)) { $notes.Add("  $line") }
    }
    if (-not $done.Build) {
        foreach ($problem in @($f.ToolchainProblems)) { Add $problem }
        if ($release -and $hostSumsUploaded) {
            Add "The draft $tag already contains $($f.ChecksumFile), but there is no matching local build of the tag (a new build would differ). Check the draft (release-publish.ps1 -DryRun) and finish by hand (docs/release.md)."
        }
    }
    $conflicts = @($pending | Where-Object Action -EQ 'Conflict')
    if (-not $published -and $conflicts.Count -gt 0) {
        Add "The draft $tag already has different files with the names $($conflicts.Name -join ', '). Check them and replace them by hand (release-upload.ps1 -Force)."
    }

    $repoArgument = if ($f.RepositoryArgument) { " -Repo $($f.RepositoryArgument)" } else { '' }
    $base = $f.RemoteMain
    $propsDetail = if ($f.PropsChange -and $f.PropsChange.From -and $f.PropsChange.From -cne $f.Version) { "<Version> $($f.PropsChange.From) -> $($f.Version)" } else { "<Version> $($f.Version) (unchanged)" }
    $changelogDetail = if ($f.Changelog -and $f.Changelog.Change) { "CHANGELOG.md '$($f.Changelog.OldHeading)' -> '$($f.Changelog.NewHeading)' ($($f.Changelog.Change.ToLowerInvariant()))" } else { 'CHANGELOG.md dated' }
    $branchStep = if ($redate) {
        [pscustomobject]@{ Id = 'Branch'; Outward = $false; Title = "Re-date $branch to the release day"
            Detail = "commit 'Release $($f.Version): release date $($f.Date)' on $($branch): CHANGELOG.md '$($check.Redate.OldHeading)' -> '$($check.Redate.NewHeading)' (redated)" }
    }
    else {
        [pscustomobject]@{ Id = 'Branch'; Outward = $false; Title = "Create $branch"
            Detail = "from develop $(Short $f.LocalDevelop), commit 'Release $($f.Version)': $propsDetail, $changelogDetail" }
    }
    $steps = @(
        $branchStep
        [pscustomobject]@{ Id = 'MergeMain'; Outward = $false; Title = "Merge $branch into main and tag $tag"
            Detail = "merge --no-ff into main ($(Short $base)), annotated tag $tag 'Release $($f.Version)' (local only)" }
        [pscustomobject]@{ Id = 'PushMain'; Outward = $true; Title = "Push main and $tag (confirm by typing the version)"
            Detail = "git push --atomic $r <merge>:refs/heads/main refs/tags/$tag" }
        [pscustomobject]@{ Id = 'MergeDevelop'; Outward = $false; Title = "Merge $branch back into develop"
            Detail = "merge --no-ff into develop (local only)" }
        [pscustomobject]@{ Id = 'PushDevelop'; Outward = $true; Title = 'Push develop (confirm y/N)'
            Detail = "git push --atomic $r <merge>:refs/heads/develop" }
        [pscustomobject]@{ Id = 'DeleteBranch'; Outward = $false; Title = "Delete the local branch $branch"
            Detail = "git branch -d $branch" }
        [pscustomobject]@{ Id = 'Build'; Outward = $false; Title = "Build $($f.Runtimes -join ', ') from the tag"
            Detail = "git switch --detach $tag; pwsh scripts/release-build.ps1" }
        [pscustomobject]@{ Id = 'Upload'; Outward = $true; Title = 'Create or fill the draft release (confirm y/N)'
            Detail = "pwsh scripts/release-upload.ps1$repoArgument -DryRun, then pwsh scripts/release-upload.ps1$repoArgument; never published here" }
    )
    foreach ($step in $steps) { $step | Add-Member -NotePropertyName Done -NotePropertyValue ([bool]$done[$step.Id]) }

    [pscustomobject]@{ Steps = $steps; Problems = $problems.ToArray(); Notes = $notes.ToArray(); Published = $published }
}

function Format-ReleaseFlowPlan {
    <# .SYNOPSIS Pure: the lines that describe a plan of Get-ReleaseFlowPlan. #>
    param([Parameter(Mandatory)] $Plan, [Parameter(Mandatory)] $Facts)
    $number = 0
    foreach ($step in $Plan.Steps) {
        $number++
        $state = if ($step.Done) { 'done' } else { 'todo' }
        "  [$state] $number. $($step.Title)"
        if (-not $step.Done) { "           $($step.Detail)" }
    }
    if (-not $Plan.Published -and @($Plan.Steps | Where-Object { $_.Id -in 'Build', 'Upload' -and -not $_.Done }).Count -gt 0) {
        '           then back to develop: git switch develop'
    }
    if ($Plan.Published) { "  Release $($Facts.Tag) is already published on GitHub." }
    if ($Facts.PSObject.Properties['ToolchainSkipped'] -and $Facts.ToolchainSkipped) {
        '  TEST MODE: toolchain check skipped (LAYERPEEK_RELEASE_TEST_SKIP_TOOLCHAIN=1, repository inside LAYERPEEK_PESTER_TESTS_TEMP). Not for a real release.'
    }
    foreach ($note in @($Plan.Notes)) { "  Note: $note" }
}

function Get-ReleaseNextSteps {
    <# .SYNOPSIS Pure: the manual commands after the draft is ready. Publishing always stays a separate manual step. #>
    param([Parameter(Mandatory)] [string] $Version, [string] $Repository)
    $tag = ConvertTo-ReleaseTag $Version
    $repoArgument = if ($Repository) { " -Repo $Repository" } else { '' }
    @(
        "Publishing is a separate manual step. Check the draft $tag on GitHub (text, files), then:"
        "  git switch --detach $tag"
        "  pwsh scripts/release-publish.ps1$repoArgument -DryRun"
        "  pwsh scripts/release-publish.ps1$repoArgument -AllowIncomplete   # Windows only until T-019: no macOS files yet"
        '  git switch develop'
        'With the macOS files (after T-019, docs/release.md step 4) publish without -AllowIncomplete.'
    )
}

function New-ReleaseTreeCommit {
    <#
    .SYNOPSIS
        A commit on top of Base whose tree is Base's tree with Files (path -> Text, Bom) replaced, made without touching
        the working tree or the real index (a separate index in TempDirectory). Moves no branch. Returns the commit.
    #>
    param(
        [Parameter(Mandatory)] [string] $RepoRoot,
        [Parameter(Mandatory)] [string] $Base,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Files,
        [Parameter(Mandatory)] [string] $Message,
        [Parameter(Mandatory)] [string] $TempDirectory
    )
    $savedIndex = $env:GIT_INDEX_FILE
    try {
        $env:GIT_INDEX_FILE = Join-Path $TempDirectory "release-index-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
        $null = Invoke-GitChecked $RepoRoot @('read-tree', $Base)
        foreach ($name in $Files.Keys) {
            $content = Join-Path $TempDirectory "release-$([guid]::NewGuid().ToString('N').Substring(0, 8))-$(Split-Path -Leaf $name)"
            Write-ReleaseTextFile $content $Files[$name].Text $Files[$name].Bom
            # --path applies the attributes (eol) of the real path, as git add would.
            $blob = (Invoke-GitChecked $RepoRoot @('hash-object', '-w', "--path=$name", $content) | Select-Object -First 1).Trim()
            $entry = (Invoke-GitChecked $RepoRoot @('ls-files', '--stage', '--', $name) | Select-Object -First 1)
            $mode = if ($entry) { ($entry -split '\s+')[0] } else { '100644' }
            $null = Invoke-GitChecked $RepoRoot @('update-index', '--cacheinfo', "$mode,$blob,$name")
        }
        $tree = (Invoke-GitChecked $RepoRoot @('write-tree') | Select-Object -First 1).Trim()
    }
    finally {
        $env:GIT_INDEX_FILE = $savedIndex
    }
    (Invoke-GitChecked $RepoRoot @('commit-tree', $tree, '-p', $Base, '-m', $Message) | Select-Object -First 1).Trim()
}

function New-ReleaseBranch {
    <#
    .SYNOPSIS
        Gitflow step 1 without touching the working tree: commits 'Release X.Y.Z' (Directory.Build.props <Version>,
        CHANGELOG.md release date, ConvertTo-ReleaseChangelog) on top of BaseCommit and creates release/X.Y.Z there.
        The files come byte-exact from BaseCommit (git cat-file), not from the working tree. The branch appears only when
        the commit exists, so a failure leaves nothing half done. Returns the commit.
    #>
    param(
        [Parameter(Mandatory)] [string] $RepoRoot,
        [Parameter(Mandatory)] [string] $Version,
        [Parameter(Mandatory)] [string] $Date,
        [Parameter(Mandatory)] [string] $BaseCommit,
        [Parameter(Mandatory)] [string] $TempDirectory
    )
    $props = Read-GitBlobText $RepoRoot "$($BaseCommit):Directory.Build.props"
    $changelog = Read-GitBlobText $RepoRoot "$($BaseCommit):CHANGELOG.md"
    if (-not $props -or -not $changelog) { throw "Directory.Build.props or CHANGELOG.md is missing in $BaseCommit." }
    $files = [ordered]@{
        'Directory.Build.props' = [pscustomobject]@{ Text = (Set-SourceVersionText -Text $props.Text -Version $Version); Bom = $props.Bom }
        'CHANGELOG.md'          = [pscustomobject]@{ Text = (ConvertTo-ReleaseChangelog -Text $changelog.Text -Version $Version -Date $Date).Text; Bom = $changelog.Bom }
    }
    $commit = New-ReleaseTreeCommit -RepoRoot $RepoRoot -Base $BaseCommit -Files $files -Message "Release $Version" -TempDirectory $TempDirectory
    $null = Invoke-GitChecked $RepoRoot @('branch', "release/$Version", $commit)
    $commit
}

function New-ReleaseDateCommit {
    <#
    .SYNOPSIS
        Re-dates an existing release/X.Y.Z (resumed on a later day, before the tag exists): a commit on its tip that only
        gives the CHANGELOG.md section the release day, then the branch moves there (compare-and-swap). Returns the commit.
    #>
    param(
        [Parameter(Mandatory)] [string] $RepoRoot,
        [Parameter(Mandatory)] [string] $Version,
        [Parameter(Mandatory)] [string] $Date,
        [Parameter(Mandatory)] [string] $ExpectedTip,
        [Parameter(Mandatory)] [string] $TempDirectory
    )
    $ref = "refs/heads/release/$Version"
    $current = (Invoke-GitChecked $RepoRoot @('rev-parse', '--verify', "$ref^{commit}") | Select-Object -First 1).Trim()
    if ($current -ne $ExpectedTip) { throw "release/$Version moved to $current after it was checked (expected $ExpectedTip). Nothing was changed; re-run." }
    $tip = $ExpectedTip
    $changelog = Read-GitBlobText $RepoRoot "$($tip):CHANGELOG.md"
    if (-not $changelog) { throw "CHANGELOG.md is missing in $tip." }
    $files = [ordered]@{ 'CHANGELOG.md' = [pscustomobject]@{ Text = (ConvertTo-ReleaseChangelog -Text $changelog.Text -Version $Version -Date $Date).Text; Bom = $changelog.Bom } }
    $commit = New-ReleaseTreeCommit -RepoRoot $RepoRoot -Base $tip -Files $files -Message "Release $($Version): release date $Date" -TempDirectory $TempDirectory
    $null = Invoke-GitChecked $RepoRoot @('update-ref', '-m', "release: date release/$Version", $ref, $commit, $tip)
    $commit
}

function New-ReleaseMergeCommit {
    <#
    .SYNOPSIS
        A --no-ff merge commit of From into Into (parents Into, From) made without a checkout (git merge-tree). Moves no
        branch. Throws on conflicts. Returns the commit.
    #>
    param([Parameter(Mandatory)] [string] $RepoRoot, [Parameter(Mandatory)] [string] $Into, [Parameter(Mandatory)] [string] $From, [Parameter(Mandatory)] [string] $Message)
    $merge = Invoke-Git $RepoRoot @('merge-tree', '--write-tree', '--no-messages', $Into, $From)
    if ($merge.ExitCode -eq 1) { throw "Merging $From into $Into has conflicts. Nothing was changed; merge by hand (docs/release.md)." }
    if ($merge.ExitCode -ne 0 -or $merge.Output.Count -eq 0) { throw "git merge-tree failed (exit code $($merge.ExitCode)): $($merge.Error)" }
    $tree = $merge.Output[0].Trim()
    (Invoke-GitChecked $RepoRoot @('commit-tree', $tree, '-p', $Into, '-p', $From, '-m', $Message) | Select-Object -First 1).Trim()
}

function Invoke-ReleaseMainMerge {
    <#
    .SYNOPSIS
        Gitflow step 2a, local only: merges the release commit into main (based on Base, normally the remote main), checks
        the merged files (Get-ReleaseCommitState: version and dated changelog, which also covers hotfix commits on main),
        creates the annotated tag vX.Y.Z on the merge and moves the local main branch there (compare-and-swap, so a
        concurrent change fails instead of being lost). With ExistingTagCommit (a re-run after the tag was made) only
        main is moved to the tag. Returns the merge commit.
    #>
    param(
        [Parameter(Mandatory)] [string] $RepoRoot,
        [Parameter(Mandatory)] [string] $Version,
        [Parameter(Mandatory)] [string] $Date,
        [Parameter(Mandatory)] [string] $ReleaseCommit,
        [Parameter(Mandatory)] [string] $Base,
        [AllowNull()] [AllowEmptyString()] [string] $LocalMain,
        [Parameter(Mandatory)] [string] $Remote,
        [AllowNull()] [AllowEmptyString()] [string] $ExistingTagCommit,
        [Parameter(Mandatory)] [string] $TempDirectory
    )
    $tag = ConvertTo-ReleaseTag $Version
    if ($ExistingTagCommit) { $merge = $ExistingTagCommit }
    else {
        $merge = New-ReleaseMergeCommit -RepoRoot $RepoRoot -Into $Base -From $ReleaseCommit -Message "Merge branch 'release/$Version'"
        $state = Get-ReleaseCommitState -RepoRoot $RepoRoot -Commit $merge -Version $Version -Date $Date -TempDirectory $TempDirectory
        $reasons = @($state.VersionProblem, $state.ChangelogProblem | Where-Object { $_ })
        if ($reasons.Count -gt 0) {
            throw "The merge of release/$Version into main would not be releasable: $($reasons -join ' '). Nothing was changed (no tag, main not moved)."
        }
        $null = Invoke-GitChecked $RepoRoot @('tag', '-a', $tag, '-m', "Release $Version", $merge)
    }
    if ($LocalMain) {
        $null = Invoke-GitChecked $RepoRoot @('update-ref', '-m', "release: merge release/$Version", 'refs/heads/main', $merge, $LocalMain)
    }
    else {
        $null = Invoke-GitChecked $RepoRoot @('branch', 'main', $merge)
        $null = Invoke-GitChecked $RepoRoot @('config', 'branch.main.remote', $Remote)
        $null = Invoke-GitChecked $RepoRoot @('config', 'branch.main.merge', 'refs/heads/main')
    }
    $merge
}

function Invoke-ReleaseDevelopMerge {
    <#
    .SYNOPSIS
        Gitflow step 2c, local only: merges the release commit into develop. Refuses unless develop is the checked-out
        branch, still at Develop and the working tree is clean; the merge commit is made with git merge-tree, then develop
        is fast-forwarded to it. Returns the merge commit.
    #>
    param([Parameter(Mandatory)] [string] $RepoRoot, [Parameter(Mandatory)] [string] $Version, [Parameter(Mandatory)] [string] $ReleaseCommit, [Parameter(Mandatory)] [string] $Develop)
    $head = Invoke-Git $RepoRoot @('symbolic-ref', '--quiet', 'HEAD')
    $headRef = if ($head.ExitCode -eq 0 -and $head.Output.Count -gt 0) { $head.Output[0].Trim() } else { 'a detached HEAD' }
    if ($headRef -cne 'refs/heads/develop') { throw "The merge back needs develop checked out, but HEAD is $headRef. Nothing was changed." }
    $current = (Invoke-GitChecked $RepoRoot @('rev-parse', 'refs/heads/develop') | Select-Object -First 1).Trim()
    if ($current -ne $Develop) { throw "develop moved to $current during the release (expected $Develop). Nothing was changed; re-run." }
    if (@(Get-GitStatusLines $RepoRoot).Count -gt 0) { throw 'The working tree is not clean; the merge back was not made.' }
    $merge = New-ReleaseMergeCommit -RepoRoot $RepoRoot -Into $Develop -From $ReleaseCommit -Message "Merge branch 'release/$Version' into develop"
    $null = Invoke-GitChecked $RepoRoot @('merge', '--ff-only', '--quiet', $merge)
    $merge
}

function Invoke-ReleasePush {
    <#
    .SYNOPSIS
        git push --atomic of confirmed commits (never forced). Branches: name -> commit SHA, pushed as '<sha>:refs/heads/<name>';
        Tags: name -> commit SHA the local tag must peel to, pushed as 'refs/tags/<name>:refs/tags/<name>'. Right before
        the push every local ref is compared with its confirmed SHA; a ref that moved meanwhile refuses the whole push.
        Prints git's output; throws on failure.
    #>
    param(
        [Parameter(Mandatory)] [string] $RepoRoot,
        [Parameter(Mandatory)] [string] $Remote,
        [System.Collections.IDictionary] $Branches = @{},
        [System.Collections.IDictionary] $Tags = @{}
    )
    $refspecs = [System.Collections.Generic.List[string]]::new()
    $checks = [System.Collections.Generic.List[object]]::new()
    foreach ($name in $Branches.Keys) { $checks.Add([pscustomobject]@{ Ref = "refs/heads/$name"; Sha = $Branches[$name]; Spec = "$($Branches[$name]):refs/heads/$name" }) }
    foreach ($name in $Tags.Keys) { $checks.Add([pscustomobject]@{ Ref = "refs/tags/$name"; Sha = $Tags[$name]; Spec = "refs/tags/$($name):refs/tags/$name" }) }
    if ($checks.Count -eq 0) { throw 'Nothing to push.' }
    foreach ($check in $checks) {
        $local = Invoke-Git $RepoRoot @('rev-parse', '--verify', '--quiet', "$($check.Ref)^{commit}")
        $actual = if ($local.ExitCode -eq 0 -and $local.Output.Count -gt 0) { $local.Output[0].Trim() } else { 'nothing' }
        if ($actual -ne $check.Sha) {
            throw "$($check.Ref) is $actual, but $($check.Sha) was confirmed. It moved after the confirmation: nothing was pushed. Check it and re-run."
        }
        $refspecs.Add($check.Spec)
    }
    $result = Invoke-Git $RepoRoot (@('push', '--atomic', $Remote) + $refspecs.ToArray())
    foreach ($line in @($result.Output) + @($result.Error)) { if ($line) { Write-Host "  $line" } }
    if ($result.ExitCode -ne 0) { throw "git push $Remote $($refspecs -join ' ') failed (exit code $($result.ExitCode)). Nothing is force-pushed; see docs/release.md." }
}

Export-ModuleMember -Function *
