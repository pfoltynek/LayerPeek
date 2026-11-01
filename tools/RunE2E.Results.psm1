#Requires -Version 7.0
# The summary of tools/run-e2e.ps1 (T-073): passed, skipped and failed tests of each pass (the Debug exe, and with
# -PublishAot the Native AOT exe of the Debug configuration), read from the xUnit.net v2 XML report of the test runner
# (-result-xml), and the kept copies of the results folders of failed runs (T-132). Kept apart from the script so that it
# is unit-tested (tests/LayerPeek.App.E2E.Tests/RunE2EScriptTests.cs, tools/RunE2E.Results.Tests.ps1).

Set-StrictMode -Version Latest

<#
.SYNOPSIS
    The counts of one pass from the xUnit.net v2 XML report of the test runner.

.PARAMETER Path
    The report (-result-xml). Missing or unreadable: $null (the test process ended without a report).

.OUTPUTS
    An object with Total, Passed, Skipped, Failed (failed tests plus errors outside tests, e.g. a failed cleanup of the
    run) and NotRun; or $null.
#>
function Get-TestPassCounts {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    try {
        [xml] $report = Get-Content -LiteralPath $Path -Raw
    }
    catch {
        return $null
    }

    $assemblies = @($report.SelectNodes('/assemblies/assembly'))
    if ($assemblies.Count -eq 0) { return $null }
    $sum = { param([string] $Name) ($assemblies | ForEach-Object { [int] $_.GetAttribute($Name) } | Measure-Object -Sum).Sum }
    return [pscustomobject] @{
        Total   = & $sum 'total'
        Passed  = & $sum 'passed'
        Skipped = & $sum 'skipped'
        Failed  = (& $sum 'failed') + (& $sum 'errors')
        NotRun  = & $sum 'not-run'
    }
}

<#
.SYNOPSIS
    One line of the summary for a pass.

.PARAMETER Name
    The pass, e.g. 'Debug exe'.

.PARAMETER ExitCode
    The exit code of the test runner; $null when the pass did not run.

.PARAMETER Counts
    Get-TestPassCounts of its report, or $null.

.PARAMETER NotRunReason
    Why the pass did not run (with ExitCode $null).
#>
function Format-TestPassSummary {
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] [string] $Name,
        [AllowNull()] [Nullable[int]] $ExitCode,
        [AllowNull()] $Counts,
        [string] $NotRunReason = 'not run'
    )

    if ($null -eq $ExitCode) { return "${Name}: $NotRunReason" }
    $verdict = if ($ExitCode -eq 0) { 'green' } else { "FAILED (exit code $ExitCode)" }
    if ($null -eq $Counts) { return "${Name}: $verdict, no test report (the test process ended without one)" }
    $notRun = if ($Counts.NotRun -gt 0) { ", $($Counts.NotRun) not run" } else { '' }
    return "${Name}: $verdict - $($Counts.Passed) passed, $($Counts.Skipped) skipped, $($Counts.Failed) failed$notRun (of $($Counts.Total))"
}

# Kept copies of failed runs (T-132): the results folder of a pass is emptied by the next run, so the script copies the
# folder of a failed pass to <folder>-failed-<yyyyMMdd-HHmmss> next to it and prunes the older copies. Only folders that
# match these names exactly, lie directly in the temp folder and carry the marker of the end-to-end tests are touched.

# The marker the tests write into their results folder (E2EFixture.ResultsMarkerFileName).
$script:ResultsMarker = '.layerpeek-e2e-results'
# The results folder of a pass: tests/LayerPeek.App.E2E.Tests (Debug pass) or run-e2e.ps1 (<results>-aot).
$script:ResultsNamePattern = '^LayerPeek\.App\.E2E\.Results\.[0-9a-f]{8}(-aot)?$'
$script:FailedSuffixPattern = '-failed-(?<stamp>\d{8}-\d{6})(-(?<n>\d{1,3}))?$'

<#
.SYNOPSIS
    How many kept copies of failed runs stay per results folder by default (the newest ones).
#>
function Get-DefaultFailedResultsToKeep {
    [OutputType([int])]
    param()
    return 10
}

function Get-PathComparison {
    if ($IsWindows) { return [StringComparison]::OrdinalIgnoreCase }
    return [StringComparison]::Ordinal
}

function Test-IsReparsePoint([IO.FileSystemInfo] $Item) {
    return ($Item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
}

# The full path of a results folder of a pass, or an exception when it is anything else.
function Assert-ResultsFolder([string] $Results, [string] $TempRoot) {
    $full = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Results))
    $root = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($TempRoot))
    $parent = [IO.Path]::GetDirectoryName($full)
    $name = [IO.Path]::GetFileName($full)
    if (-not [string]::Equals($parent, $root, (Get-PathComparison))) {
        throw "Refusing '$full': not directly in the temp folder '$root'."
    }
    if ($name -cnotmatch $script:ResultsNamePattern) {
        throw "Refusing '$full': not a results folder of the end-to-end tests (LayerPeek.App.E2E.Results.<id>[-aot])."
    }
    return $full
}

# Whether the folder is an actual folder (no link) with the marker of the end-to-end tests.
function Test-MarkedFolder([IO.DirectoryInfo] $Folder) {
    if (-not $Folder.Exists -or (Test-IsReparsePoint $Folder)) { return $false }
    $marker = [IO.FileInfo]::new((Join-Path $Folder.FullName $script:ResultsMarker))
    return $marker.Exists -and -not (Test-IsReparsePoint $marker)
}

# Deletes a folder with its content; a link inside it is removed itself, never what it points to. The file named
# LastFileName (the marker) goes last, so a copy that cannot be deleted completely (a locked file) keeps its marker and
# the next prune tries again instead of leaving an unmarked folder behind for good.
function Remove-FolderTree([IO.DirectoryInfo] $Folder, [string] $LastFileName) {
    $last = $null
    foreach ($entry in @($Folder.EnumerateFileSystemInfos())) {
        if ($LastFileName -and $entry -is [IO.FileInfo] -and [string]::Equals($entry.Name, $LastFileName, (Get-PathComparison))) {
            $last = $entry
        }
        elseif ($entry -is [IO.DirectoryInfo]) {
            if (Test-IsReparsePoint $entry) { $entry.Delete() } else { Remove-FolderTree $entry }
        }
        else {
            $entry.Attributes = [IO.FileAttributes]::Normal
            $entry.Delete()
        }
    }
    if ($last) {
        $last.Attributes = [IO.FileAttributes]::Normal
        $last.Delete()
    }
    $Folder.Delete()
}

<#
.SYNOPSIS
    The passes whose results folder is kept: every failed pass; when the run failed without a failed pass (e.g. a system
    change was not restored afterwards), every pass.

.PARAMETER Passes
    The passes of the run (Name, ExitCode, Results).

.PARAMETER ExitCode
    The exit code of the whole run.
#>
function Get-PassesToKeep {
    param(
        [AllowEmptyCollection()] [object[]] $Passes = @(),
        [Parameter(Mandatory)] [int] $ExitCode
    )

    if ($ExitCode -eq 0) { return @() }
    $failed = @($Passes | Where-Object { $_.ExitCode -ne 0 })
    if ($failed.Count -gt 0) { return $failed }
    return @($Passes)
}

<#
.SYNOPSIS
    Copies the results folder of a failed pass to <Results>-failed-<yyyyMMdd-HHmmss> next to it and returns the path of the
    copy; $null when there is nothing to keep (no folder, or no marker of the tests in it: the pass ended before it wrote
    anything).

.DESCRIPTION
    A copy, not a move: an app or the watchdog of a killed test process may still write into the folder. Files are read
    with sharing for writers, so open logs are copied too; a file that still cannot be read is skipped with a warning.
    Links inside the folder are not followed (skipped). Refuses anything but a results folder of the tests directly in
    TempRoot.

.PARAMETER Results
    The results folder of the pass (LayerPeek.App.E2E.Results.<id> or ...-aot).

.PARAMETER TempRoot
    The temp folder the results folder must lie in directly (default: the system temp folder).

.PARAMETER Now
    The time stamp of the copy (default: now, local time).
#>
function Save-FailedTestResults {
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] [string] $Results,
        [string] $TempRoot = [IO.Path]::GetTempPath(),
        [datetime] $Now = [datetime]::Now
    )

    $source = Assert-ResultsFolder -Results $Results -TempRoot $TempRoot
    $folder = [IO.DirectoryInfo]::new($source)
    if ((Test-Path -LiteralPath $source) -and (Test-IsReparsePoint $folder)) {
        throw "Refusing '$source': it is a link, not a folder."
    }
    if (-not (Test-MarkedFolder $folder)) { return $null }

    $base = "$source-failed-$($Now.ToString('yyyyMMdd-HHmmss', [Globalization.CultureInfo]::InvariantCulture))"
    $target = $base
    for ($n = 2; (Test-Path -LiteralPath $target); $n++) {
        if ($n -gt 999) { throw "No free name for the copy of '$source'." }
        $target = "$base-$n"
    }

    $null = [IO.Directory]::CreateDirectory($target)
    $pending = [Collections.Generic.Stack[IO.DirectoryInfo]]::new()
    $pending.Push($folder)
    while ($pending.Count -gt 0) {
        $current = $pending.Pop()
        $relative = [IO.Path]::GetRelativePath($source, $current.FullName)
        $destination = if ($relative -eq '.') { $target } else { Join-Path $target $relative }
        $null = [IO.Directory]::CreateDirectory($destination)
        foreach ($entry in $current.EnumerateFileSystemInfos()) {
            if (Test-IsReparsePoint $entry) {
                Write-Warning "Not copied (a link): $($entry.FullName)"
                continue
            }
            if ($entry -is [IO.DirectoryInfo]) {
                $pending.Push($entry)
                continue
            }
            try {
                $in = [IO.FileStream]::new($entry.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
                try {
                    $out = [IO.FileStream]::new((Join-Path $destination $entry.Name), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
                    try { $in.CopyTo($out) } finally { $out.Dispose() }
                }
                finally {
                    $in.Dispose()
                }
            }
            catch {
                Write-Warning "Not copied: $($entry.FullName) ($($_.Exception.Message))"
            }
        }
    }
    return $target
}

<#
.SYNOPSIS
    Deletes the older kept copies of failed runs of one results folder and keeps the newest Keep of them; returns the
    deleted paths.

.DESCRIPTION
    Only folders directly in TempRoot named exactly <Results>-failed-<yyyyMMdd-HHmmss>[-<n>] with the marker of the tests
    are counted and deleted; links, files and every other folder (also the copies of the other pass, e.g. <Results>-aot-...)
    stay. Newest by the time stamp in the name. A copy that cannot be deleted completely is reported as a warning and keeps
    its marker, so the next run tries again.

.PARAMETER Results
    The results folder of the pass (LayerPeek.App.E2E.Results.<id> or ...-aot).

.PARAMETER Keep
    How many copies stay (default: Get-DefaultFailedResultsToKeep).

.PARAMETER TempRoot
    The temp folder the results folder must lie in directly (default: the system temp folder).
#>
function Remove-OldFailedTestResults {
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] [string] $Results,
        [ValidateRange(0, 1000)] [int] $Keep = (Get-DefaultFailedResultsToKeep),
        [string] $TempRoot = [IO.Path]::GetTempPath()
    )

    $source = Assert-ResultsFolder -Results $Results -TempRoot $TempRoot
    $parent = [IO.DirectoryInfo]::new([IO.Path]::GetDirectoryName($source))
    if (-not $parent.Exists) { return @() }
    $pattern = '^' + [regex]::Escape([IO.Path]::GetFileName($source)) + $script:FailedSuffixPattern
    $copies = foreach ($folder in $parent.EnumerateDirectories()) {
        $match = [regex]::Match($folder.Name, $pattern)
        if (-not $match.Success -or -not (Test-MarkedFolder $folder)) { continue }
        [pscustomobject] @{
            Folder = $folder
            Stamp  = $match.Groups['stamp'].Value
            Number = if ($match.Groups['n'].Success) { [int] $match.Groups['n'].Value } else { 1 }
        }
    }
    $old = @($copies | Sort-Object -Property @{ Expression = 'Stamp'; Descending = $true }, @{ Expression = 'Number'; Descending = $true } | Select-Object -Skip $Keep)
    foreach ($copy in $old) {
        # One copy that cannot be deleted (e.g. a file held open by a virus scanner) does not keep the older ones.
        try {
            Remove-FolderTree $copy.Folder -LastFileName $script:ResultsMarker
            $copy.Folder.FullName
        }
        catch {
            Write-Warning "Old kept results not deleted (next run tries again): $($copy.Folder.FullName) ($($_.Exception.Message))"
        }
    }
}

Export-ModuleMember -Function Get-TestPassCounts, Format-TestPassSummary, Get-DefaultFailedResultsToKeep, Get-PassesToKeep, Save-FailedTestResults, Remove-OldFailedTestResults
