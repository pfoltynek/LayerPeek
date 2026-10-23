#Requires -Version 7.0
# The summary of tools/run-e2e.ps1 (T-073): passed, skipped and failed tests of each pass (the Debug exe, and with
# -PublishAot the Native AOT exe of the Debug configuration), read from the xUnit.net v2 XML report of the test runner
# (-result-xml). Kept apart from the script so that it is unit-tested (tests/PrusaTrayDiagnostic.App.E2E.Tests/RunE2EScriptTests.cs).

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

Export-ModuleMember -Function Get-TestPassCounts, Format-TestPassSummary
