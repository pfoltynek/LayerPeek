#Requires -Version 7.0
# Which Windows settings the selected end-to-end tests change, and the warning text of tools/run-e2e.ps1 (T-065, T-066).
# Every test that changes the system carries one trait SystemChanges=<kind> per kind of change
# (tests/PrusaTrayDiagnostic.App.E2E.Tests/Infrastructure/SystemChanges.cs, SystemChangeTraits); the warning lists only the
# kinds of the selected tests. Kept apart from the script so that the selection is unit-tested
# (tests/PrusaTrayDiagnostic.App.E2E.Tests/RunE2EScriptTests.cs).

Set-StrictMode -Version Latest

# The kinds in the order of the warning: what changes, and how to restore it by hand. The names must equal
# SystemChangeTraits.All (checked by a test).
$script:Kinds = [ordered]@{
    Theme         = @{
        Change  = @('light/dark mode of the taskbar and of apps (Settings > Personalization > Colors)')
        Restore = 'Settings > Personalization > Colors'
    }
    DoNotDisturb  = @{
        Change  = @('Do Not Disturb (switched off for notification tests)')
        Restore = 'the clock on the taskbar (or Win+N) > Do not disturb'
    }
    TrayPromotion = @{
        Change  = @('the test tray icon shown directly on the taskbar')
        Restore = 'Settings > Personalization > Taskbar > Other system tray icons'
    }
    DisplayScale  = @{
        Change  = @(
            'display scale of the primary and another monitor (100/125/150 %); windows of all apps are',
            '     rearranged as with a manual change and go back when the scale does')
        Restore = 'Settings > System > Display > Scale'
    }
    MonitorOff    = @{
        Change  = @(
            'a second (not primary) monitor switched off for a moment; Windows moves its windows to the',
            '     primary monitor and back (the tests never move windows of other apps)')
        Restore = 'Win+P > Extend'
    }
    Toasts        = @{
        Change  = @(
            'silent test toasts in the notification center, a temporary Start menu shortcut',
            '     "Prusa Tray Diagnostic E2E" and its toast activator under HKCU\Software\Classes\CLSID',
            '     (a click on a test toast starts the test app) (Windows may keep remembering the app PrusaTrayDiagnostic.E2E',
            '     in its notification database / Settings > Notifications); notifications of Windows and of',
            '     the test app are switched on for the test if they are off')
        Restore = 'Settings > System > Notifications'
    }
    Credentials   = @{
        Change  = @('temporary entries PrusaTrayDiagnostic.E2E/... in Credential Manager')
        Restore = 'Control Panel > Credential Manager > Windows Credentials'
    }
    TestProbe     = @{
        Change  = @('a test value under HKCU\Software\PrusaTrayDiagnostic.E2E.Tests (self-test of the guard, no Windows setting)')
        Restore = 'delete the key HKCU\Software\PrusaTrayDiagnostic.E2E.Tests'
    }
}

<#
.SYNOPSIS
    The names of all known kinds of system changes, in the order of the warning.
#>
function Get-SystemChangeKindNames {
    [OutputType([string[]])]
    param()
    return [string[]] @($script:Kinds.Keys)
}

<#
.SYNOPSIS
    The kinds of system changes the listed tests make, in the order of the warning.

.PARAMETER ListOutput
    The output of the test exe with `-list full/json -trait 'SystemChanges=*'` plus the -class/-method filters (one JSON
    array of the selected tests with their traits; other lines, e.g. a banner, are ignored).

.OUTPUTS
    The kinds (empty when no selected test changes the system). A test with a value the script does not know throws:
    the warning could not say what it changes.
#>
function Get-SelectedSystemChanges {
    [OutputType([string[]])]
    param([AllowEmptyCollection()] [AllowEmptyString()] [string[]] $ListOutput)

    $text = ($ListOutput | Where-Object { $_ } | ForEach-Object { "$_".Trim() }) -join "`n"
    $start = $text.IndexOf('[')
    if ($start -lt 0) {
        if ($text -match '\S' -and $text -notmatch '^xUnit\.net') {
            throw "Unexpected test list (no JSON array): $text"
        }
        return [string[]] @()
    }

    $tests = @($text.Substring($start) | ConvertFrom-Json -Depth 10)
    $found = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $unknown = [Collections.Generic.List[string]]::new()
    foreach ($test in $tests) {
        if (-not $test.PSObject.Properties['Traits'] -or $null -eq $test.Traits) { continue }
        $values = $test.Traits.PSObject.Properties['SystemChanges']
        if (-not $values) { continue }
        foreach ($value in @($values.Value)) {
            if ($script:Kinds.Keys -ccontains "$value") {
                [void] $found.Add("$value")
            }
            else {
                $unknown.Add("$($test.DisplayName): SystemChanges=$value")
            }
        }
    }

    if ($unknown.Count -gt 0) {
        throw "Tests with an unknown kind of system change (add it to tools/RunE2E.SystemChanges.psm1 and SystemChangeTraits): $($unknown -join '; ')"
    }

    return [string[]] @($script:Kinds.Keys | Where-Object { $found.Contains($_) })
}

<#
.SYNOPSIS
    The lines of the warning for the given kinds (only those kinds, in the order of the warning).
#>
function Get-SystemChangesWarningLines {
    [OutputType([string[]])]
    param([Parameter(Mandatory)] [string[]] $Kinds)

    foreach ($kind in $Kinds) {
        if (-not ($script:Kinds.Keys -ccontains $kind)) { throw "Unknown kind of system change '$kind'." }
    }
    $selected = @($script:Kinds.Keys | Where-Object { $Kinds -ccontains $_ })
    if ($selected.Count -eq 0) { throw 'No kind of system change selected; no warning to show.' }

    $lines = [Collections.Generic.List[string]]::new()
    $lines.AddRange([string[]] @(
            '',
            '=====================================================================================',
            '  WARNING: the selected end-to-end tests temporarily CHANGE WINDOWS SETTINGS',
            '=====================================================================================',
            '  They change (each change is restored right after its test, also on failure):'))
    foreach ($kind in $selected) {
        $change = @($script:Kinds[$kind].Change)
        $lines.Add("   - $($change[0])")
        foreach ($more in $change | Select-Object -Skip 1) { $lines.Add($more) }
    }
    $lines.AddRange([string[]] @(
            '  Do not use the mouse or keyboard during the run; windows will move and change size.',
            '',
            '  If anything stays changed after the run:',
            '   - run  tools/run-e2e.ps1 -RestoreOnly',
            '   - by hand:'))
    foreach ($kind in $selected) { $lines.Add("       $($script:Kinds[$kind].Restore)") }
    $lines.AddRange([string[]] @(
            '   - details: docs/troubleshooting.md',
            '====================================================================================='))
    return [string[]] $lines
}

Export-ModuleMember -Function Get-SystemChangeKindNames, Get-SelectedSystemChanges, Get-SystemChangesWarningLines
