#Requires -Version 7.0
# Which Windows settings the selected end-to-end tests change, and the warning text of tools/run-e2e.ps1 (T-065, T-066).
# Every test that changes the system carries one trait SystemChanges=<kind> per kind of change
# (tests/LayerPeek.App.E2E.Tests/Infrastructure/SystemChanges.cs, SystemChangeTraits); the warning lists only the
# kinds of the selected tests. Kept apart from the script so that the selection is unit-tested
# (tests/LayerPeek.App.E2E.Tests/RunE2EScriptTests.cs).

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
            '     "LayerPeek E2E" and its toast activator under HKCU\Software\Classes\CLSID',
            '     (a click on a test toast starts the test app) (Windows may keep remembering the app LayerPeek.E2E',
            '     in its notification database / Settings > Notifications); notifications of Windows and of',
            '     the test app are switched on for the test if they are off')
        Restore = 'Settings > System > Notifications'
    }
    Credentials   = @{
        Change  = @('temporary entries LayerPeek.E2E/... in Credential Manager')
        Restore = 'Control Panel > Credential Manager > Windows Credentials'
    }
    Installer     = @{
        Change  = @(
            'the Velopack installer of the release build: an interactive start cancelled at its overwrite dialog, then a',
            '     silent install into %LOCALAPPDATA%\LayerPeek and an uninstall (T-017). The logs in %LOCALAPPDATA%\LayerPeek',
            '     are copied away and back (the install replaces that folder). It replaces the Start menu shortcut',
            '     "LayerPeek", the toast activator of LayerPeek under HKCU\Software\Classes\CLSID and the notification',
            '     settings of LayerPeek; the test adds a temporary autostart shortcut Startup\LayerPeek.lnk (to a file',
            '     that does not exist), a temporary entry under HKCU\Control Panel\NotifyIconSettings and one silent test',
            '     toast of LayerPeek (restored from the journal; the toast goes with the uninstall, which clears ALL',
            '     notifications of LayerPeek from the notification center; they cannot be restored). Velopack keeps its',
            '     log in %LOCALAPPDATA%\velopack')
        Restore = 'Settings > Apps > Installed apps > LayerPeek > Uninstall, then run LayerPeek once (it recreates its shortcut)'
    }
    TestProbe     = @{
        Change  = @('a test value under HKCU\Software\LayerPeek.E2E.Tests (self-test of the guard, no Windows setting)')
        Restore = 'delete the key HKCU\Software\LayerPeek.E2E.Tests'
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

<#
.SYNOPSIS
    Checks the restore journal of the app's former name (T-074) under -LocalAppData.
.DESCRIPTION
    Before the rename to LayerPeek the journal lived in <LocalAppData>\PrusaTrayDiagnostic.E2E.Restore, and its entries
    name the test app, shortcut, credentials and registry keys of the former name, which this code no longer knows.
    Returns nothing when there is no such folder; deletes the folder when it holds no pending change (*.json) and
    returns nothing; otherwise leaves it alone and returns the lines that tell how to restore it with the last commit
    of the former name.
#>
function Resolve-LegacyRestoreJournal {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $LocalAppData
    )

    $legacy = Join-Path $LocalAppData 'PrusaTrayDiagnostic.E2E.Restore'
    if (-not (Test-Path -LiteralPath $legacy -PathType Container)) { return }

    $pending = @(Get-ChildItem -LiteralPath $legacy -Filter '*.json' -File)
    if ($pending.Count -eq 0) {
        Remove-Item -LiteralPath $legacy -Recurse -Force
        return
    }

    return [string[]] @(
        "System changes of an E2E run from before the rename to LayerPeek (T-074) are pending: $($pending.Count) change(s) in",
        "  $legacy",
        'This code cannot restore them (they name the test app of the former name). Restore them with the last commit',
        'before the rename (fc2bb11), then run this script again:',
        '  git worktree add <empty temp folder> fc2bb11',
        '  pwsh <that folder>/tools/run-e2e.ps1 -RestoreOnly',
        '  git worktree remove <that folder>',
        'By hand instead: docs/troubleshooting.md (section on an interrupted E2E run), then delete the folder above.')
}

Export-ModuleMember -Function Get-SystemChangeKindNames, Get-SelectedSystemChanges, Get-SystemChangesWarningLines, Resolve-LegacyRestoreJournal
