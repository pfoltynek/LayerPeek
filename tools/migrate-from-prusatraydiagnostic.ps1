<#
.SYNOPSIS
    One-time migration of a developer machine from the app's former name PrusaTrayDiagnostic to LayerPeek (T-074).

.DESCRIPTION
    The app was renamed before its first release, so the app itself migrates nothing. This script moves what the old
    development builds left on a Windows machine:

      1. copies %APPDATA%\PrusaTrayDiagnostic (settings, camera windows, printer images) to %APPDATA%\LayerPeek,
         only when %APPDATA%\LayerPeek does not exist yet;
      2. copies %LOCALAPPDATA%\PrusaTrayDiagnostic\logs to %LOCALAPPDATA%\LayerPeek\logs, only when that folder does not
         exist yet; the log files are renamed (PrusaTrayDiagnostic.log -> LayerPeek.log, PrusaTrayDiagnostic.1.log ->
         LayerPeek.1.log, ...), so the new app continues the same rotation;
         a copied old folder gets the marker file .migrated-to-LayerPeek, and only such a folder is ever removed (a
         LayerPeek folder that existed before, e.g. from a first start of LayerPeek, never makes the old data removable);
      3. copies every Credential Manager entry PrusaTrayDiagnostic/<key> to LayerPeek/<key> (production prefix only,
         never the test prefixes; an existing new entry is kept). The secret is copied inside Windows (CredRead ->
         CredWrite) and never reaches PowerShell, the console or a file.

    Removing the old data is a separate step that is never done without an explicit confirmation (typing REMOVE):
    asked at the end of a migration run in an interactive console, or with -RemoveOld (also asks). It removes
      - %APPDATA%\PrusaTrayDiagnostic and %LOCALAPPDATA%\PrusaTrayDiagnostic (only when this script copied them and the
        LayerPeek counterpart exists),
      - %LOCALAPPDATA%\PrusaTrayDiagnostic.Build (the FFmpeg download cache of the old builds),
      - the old Credential Manager entries (only those whose LayerPeek/<key> exists),
      - the old Start menu shortcut "Prusa Tray Diagnostic.lnk",
      - the old toast activator HKCU\Software\Classes\CLSID\{E8389DCA-851B-498C-8082-69B1AB263C14},
      - the old notification settings HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\PrusaTrayDiagnostic.
    The new app creates its own shortcut, activator and notification settings at its first start.

    Neither app may run meanwhile (the script refuses). -WhatIf shows every step and changes nothing.
    Leftovers of the end-to-end tests of the old name are not touched (see docs/troubleshooting.md).

.PARAMETER RemoveOld
    Only remove the old data (after a migration run and a check that LayerPeek works); asks for confirmation.

.EXAMPLE
    pwsh tools/migrate-from-prusatraydiagnostic.ps1 -WhatIf

.EXAMPLE
    pwsh tools/migrate-from-prusatraydiagnostic.ps1

.EXAMPLE
    pwsh tools/migrate-from-prusatraydiagnostic.ps1 -RemoveOld
#>
#Requires -Version 7.2
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Low')]
param(
    [switch] $RemoveOld
)

Set-StrictMode -Version Latest

# The identities of the former name, exactly as the code before T-074 used them (production only).
$script:OldName = 'PrusaTrayDiagnostic'
$script:NewName = 'LayerPeek'
$script:OldCredentialPrefix = 'PrusaTrayDiagnostic/'
$script:NewCredentialPrefix = 'LayerPeek/'
$script:OldShortcutFileName = 'Prusa Tray Diagnostic.lnk'
$script:OldToastActivatorKey = 'Software\Classes\CLSID\{E8389DCA-851B-498C-8082-69B1AB263C14}'
$script:OldNotificationSettingsKey = 'Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\PrusaTrayDiagnostic'
# Written into an old folder once this script has copied it; only a folder with it may be removed.
$script:MigratedMarkerName = '.migrated-to-LayerPeek'
$script:KindLabels = @{ Folder = 'folder'; Credential = 'credential'; Shortcut = 'shortcut'; RegistryKey = 'registry key' }

<#
.SYNOPSIS
    The new name of a log file of the old app: PrusaTrayDiagnostic.log -> LayerPeek.log, PrusaTrayDiagnostic.<n>.log ->
    LayerPeek.<n>.log, PrusaTrayDiagnostic.rotating.log -> LayerPeek.rotating.log; any other name stays.
#>
function Get-MigratedLogFileName {
    param([Parameter(Mandatory)] [string] $Name)

    if ($Name.StartsWith("$script:OldName.", [StringComparison]::OrdinalIgnoreCase)) {
        return $script:NewName + $Name.Substring($script:OldName.Length)
    }
    return $Name
}

<#
.SYNOPSIS
    The folders to copy: the data folder and the log folder, old and new paths.
#>
function Get-MigrationFolders {
    param(
        [Parameter(Mandatory)] [string] $AppData,
        [Parameter(Mandatory)] [string] $LocalAppData
    )

    @(
        [pscustomobject]@{
            Name       = 'data'
            Source     = Join-Path $AppData $script:OldName
            Target     = Join-Path $AppData $script:NewName
            RenameLogs = $false
        },
        [pscustomobject]@{
            Name       = 'logs'
            Source     = Join-Path $LocalAppData $script:OldName 'logs'
            Target     = Join-Path $LocalAppData $script:NewName 'logs'
            RenameLogs = $true
        }
    )
}

<#
.SYNOPSIS
    Copies -Source to -Target when the source exists and the target does not. Returns NoSource, Copied, AlreadyCopied
    (by an earlier run), TargetExists (the target existed before and was left alone) or WouldCopy (-WhatIf).
.DESCRIPTION
    The copy is made in a sibling folder .migrating-<random> first and renamed to -Target at the end, so an interrupted
    copy never leaves a half-filled target that a later run would take for done. Only then the marker file
    .migrated-to-LayerPeek is written into -Source (Test-MigratedFolder).
#>
function Copy-MigrationFolder {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Low')]
    param(
        [Parameter(Mandatory)] [string] $Source,
        [Parameter(Mandatory)] [string] $Target,
        [switch] $RenameLogs
    )

    # One file that cannot be copied must stop the whole copy, whatever the caller's preference.
    $ErrorActionPreference = 'Stop'
    if (-not (Test-Path -LiteralPath $Source -PathType Container)) { return 'NoSource' }
    if (Test-Path -LiteralPath $Target) {
        return $(if (Test-MigratedFolder -Source $Source -Target $Target) { 'AlreadyCopied' } else { 'TargetExists' })
    }
    if (-not $PSCmdlet.ShouldProcess($Target, "Copy from $Source")) { return 'WouldCopy' }

    $parent = Split-Path -Parent $Target
    $null = New-Item -ItemType Directory -Force -Path $parent
    $staging = Join-Path $parent ".migrating-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    try {
        Copy-Item -LiteralPath $Source -Destination $staging -Recurse
        if ($RenameLogs) {
            foreach ($file in @(Get-ChildItem -LiteralPath $staging -File)) {
                $name = Get-MigratedLogFileName -Name $file.Name
                if ($name -ne $file.Name) { Rename-Item -LiteralPath $file.FullName -NewName $name }
            }
        }
        Move-Item -LiteralPath $staging -Destination $Target
    }
    finally {
        if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    }
    [System.IO.File]::WriteAllText((Join-Path $Source $script:MigratedMarkerName), "Copied to $Target by tools/migrate-from-prusatraydiagnostic.ps1 (T-074).`n")
    return 'Copied'
}

<#
.SYNOPSIS
    True when this script copied -Source to -Target: the marker file is in -Source and -Target exists.
#>
function Test-MigratedFolder {
    param(
        [Parameter(Mandatory)] [string] $Source,
        [Parameter(Mandatory)] [string] $Target
    )

    (Test-Path -LiteralPath (Join-Path $Source $script:MigratedMarkerName) -PathType Leaf) -and
        (Test-Path -LiteralPath $Target -PathType Container)
}

<#
.SYNOPSIS
    Why an old folder that was not copied by this script stays (-Shown is the new folder as the user knows it).
#>
function Get-NotMigratedReason {
    param(
        [Parameter(Mandatory)] [string] $Target,
        [Parameter(Mandatory)] [string] $Shown
    )

    if (Test-Path -LiteralPath $Target) {
        return "$Shown existed before the migration, the old data were not copied (docs/troubleshooting.md)"
    }
    return "$Shown does not exist (not migrated)"
}

<#
.SYNOPSIS
    Copies every credential PrusaTrayDiagnostic/<key> of -Store to LayerPeek/<key>; one result per old entry.
.DESCRIPTION
    -Store has the methods List(filter), Exists(target), Copy(source, target) and Delete(target) (the Windows
    Credential Manager, or a fake in the tests). Only targets that start exactly with the production prefix are taken.
#>
function Copy-MigrationCredentials {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Low')]
    param([Parameter(Mandatory)] [object] $Store)

    foreach ($old in @(Get-OldCredentialTargets -Store $Store)) {
        $new = $script:NewCredentialPrefix + $old.Substring($script:OldCredentialPrefix.Length)
        $status = if ($Store.Exists($new)) {
            'TargetExists'
        }
        elseif ($PSCmdlet.ShouldProcess($new, "Copy credential from $old")) {
            $Store.Copy($old, $new)
            'Copied'
        }
        else {
            'WouldCopy'
        }
        [pscustomobject]@{ Old = $old; New = $new; Status = $status }
    }
}

<#
.SYNOPSIS
    The credential targets of the old production prefix (never PrusaTrayDiagnostic.E2E/... of the tests).
#>
function Get-OldCredentialTargets {
    param([Parameter(Mandatory)] [object] $Store)

    @($Store.List("$($script:OldCredentialPrefix)*")) |
        Where-Object { $_.StartsWith($script:OldCredentialPrefix, [StringComparison]::Ordinal) -and $_.Length -gt $script:OldCredentialPrefix.Length } |
        Sort-Object
}

<#
.SYNOPSIS
    What the removal step would remove: one item per leftover of the old name, Ready when it may go (its data exist
    under the new name), otherwise with the reason it stays.
#>
function Get-LegacyItems {
    param(
        [Parameter(Mandatory)] [string] $AppData,
        [Parameter(Mandatory)] [string] $LocalAppData,
        [Parameter(Mandatory)] [string] $Programs,
        [Parameter(Mandatory)] [object] $Store,
        [Parameter(Mandatory)] [object] $Registry
    )

    $items = [System.Collections.Generic.List[object]]::new()
    function Add([string] $Kind, [string] $Path, [bool] $Ready, [string] $Reason) {
        $items.Add([pscustomobject]@{ Kind = $Kind; Path = $Path; Ready = $Ready; Reason = $Reason })
    }

    $folders = @(Get-MigrationFolders -AppData $AppData -LocalAppData $LocalAppData)
    $data = $folders | Where-Object Name -EQ 'data'
    if (Test-Path -LiteralPath $data.Source) {
        $ready = Test-MigratedFolder -Source $data.Source -Target $data.Target
        Add 'Folder' $data.Source $ready $(if ($ready) { '' } else { Get-NotMigratedReason -Target $data.Target -Shown "%APPDATA%\$script:NewName" })
    }

    # The old builds kept nothing but their logs under %LOCALAPPDATA%\PrusaTrayDiagnostic.
    $oldLocal = Join-Path $LocalAppData $script:OldName
    if (Test-Path -LiteralPath $oldLocal) {
        $logs = $folders | Where-Object Name -EQ 'logs'
        $ready = -not (Test-Path -LiteralPath $logs.Source) -or (Test-MigratedFolder -Source $logs.Source -Target $logs.Target)
        Add 'Folder' $oldLocal $ready $(if ($ready) { '' } else { Get-NotMigratedReason -Target $logs.Target -Shown "%LOCALAPPDATA%\$script:NewName\logs" })
    }

    $oldBuild = Join-Path $LocalAppData "$script:OldName.Build"
    if (Test-Path -LiteralPath $oldBuild) { Add 'Folder' $oldBuild $true '' }

    foreach ($old in @(Get-OldCredentialTargets -Store $Store)) {
        $new = $script:NewCredentialPrefix + $old.Substring($script:OldCredentialPrefix.Length)
        $ready = [bool] $Store.Exists($new)
        Add 'Credential' $old $ready $(if ($ready) { '' } else { "$new does not exist (not migrated)" })
    }

    $shortcut = Join-Path $Programs $script:OldShortcutFileName
    if (Test-Path -LiteralPath $shortcut -PathType Leaf) { Add 'Shortcut' $shortcut $true '' }

    foreach ($key in $script:OldToastActivatorKey, $script:OldNotificationSettingsKey) {
        if ($Registry.Exists($key)) { Add 'RegistryKey' "HKCU\$key" $true '' }
    }

    return $items.ToArray()
}

<#
.SYNOPSIS
    Removes the Ready items of Get-LegacyItems; items that are not ready stay. Returns the removed items.
#>
function Remove-LegacyItems {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Low')]
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Items,
        [Parameter(Mandatory)] [object] $Store,
        [Parameter(Mandatory)] [object] $Registry
    )

    $ErrorActionPreference = 'Stop'
    foreach ($item in $Items) {
        if (-not $item.Ready) { continue }
        if (-not $PSCmdlet.ShouldProcess($item.Path, "Remove $($script:KindLabels[$item.Kind])")) { continue }
        switch ($item.Kind) {
            'Folder' { Remove-Item -LiteralPath $item.Path -Recurse -Force }
            'Shortcut' { Remove-Item -LiteralPath $item.Path -Force }
            'Credential' { $Store.Delete($item.Path) }
            'RegistryKey' { $Registry.Remove($item.Path.Substring('HKCU\'.Length)) }
            default { throw "Unknown kind of item: $($item.Kind)" }
        }
        $item
    }
}

<#
.SYNOPSIS
    The text of an item of Get-LegacyItems for the console (paths and credential names only, never a secret).
#>
function Format-LegacyItem {
    param([Parameter(Mandatory)] [object] $Item)

    $line = "  {0,-11} {1}" -f $Item.Kind, $Item.Path
    if (-not $Item.Ready) { $line += "   (stays: $($Item.Reason))" }
    return $line
}

<#
.SYNOPSIS
    The Windows Credential Manager of the current user (generic credentials) as a store for the functions above.
    Copy reads and writes the secret blob inside native code: it never becomes a PowerShell string.
#>
function New-CredentialManagerStore {
    if (-not ('LayerPeekMigration.CredentialManager' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LayerPeekMigration
{
    public static class CredentialManager
    {
        private const int CredTypeGeneric = 1;
        private const int ErrorNotFound = 1168;

        [StructLayout(LayoutKind.Sequential)]
        private struct Credential
        {
            public int Flags;
            public int Type;
            public IntPtr TargetName;
            public IntPtr Comment;
            public int LastWrittenLow;
            public int LastWrittenHigh;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            public IntPtr UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredEnumerateW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredEnumerate(string filter, int flags, out int count, out IntPtr credentials);

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref Credential credential, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr buffer);

        public static string[] List(string filter)
        {
            int count;
            IntPtr list;
            if (!CredEnumerate(filter, 0, out count, out list))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == ErrorNotFound) { return new string[0]; }
                throw new Win32Exception(error);
            }
            try
            {
                List<string> targets = new List<string>();
                for (int i = 0; i < count; i++)
                {
                    Credential credential = Marshal.PtrToStructure<Credential>(Marshal.ReadIntPtr(list, i * IntPtr.Size));
                    if (credential.Type == CredTypeGeneric) { targets.Add(Marshal.PtrToStringUni(credential.TargetName)); }
                }
                return targets.ToArray();
            }
            finally
            {
                CredFree(list);
            }
        }

        public static bool Exists(string target)
        {
            IntPtr credential;
            if (CredRead(target, CredTypeGeneric, 0, out credential)) { CredFree(credential); return true; }
            int error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound) { return false; }
            throw new Win32Exception(error);
        }

        public static void Copy(string source, string target)
        {
            IntPtr pointer;
            if (!CredRead(source, CredTypeGeneric, 0, out pointer)) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
            IntPtr targetName = Marshal.StringToCoTaskMemUni(target);
            try
            {
                Credential old = Marshal.PtrToStructure<Credential>(pointer);
                Credential copy = new Credential
                {
                    Type = CredTypeGeneric,
                    TargetName = targetName,
                    CredentialBlobSize = old.CredentialBlobSize,
                    CredentialBlob = old.CredentialBlob,
                    Persist = old.Persist,
                    UserName = old.UserName,
                };
                if (!CredWrite(ref copy, 0)) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
            }
            finally
            {
                Marshal.FreeCoTaskMem(targetName);
                CredFree(pointer);
            }
        }

        public static void Delete(string target)
        {
            if (!CredDelete(target, CredTypeGeneric, 0))
            {
                int error = Marshal.GetLastWin32Error();
                if (error != ErrorNotFound) { throw new Win32Exception(error); }
            }
        }
    }
}
'@
    }

    $store = [pscustomobject]@{ Name = 'Windows Credential Manager' }
    $store | Add-Member -MemberType ScriptMethod -Name List -Value { param([string] $Filter) [LayerPeekMigration.CredentialManager]::List($Filter) }
    $store | Add-Member -MemberType ScriptMethod -Name Exists -Value { param([string] $Target) [LayerPeekMigration.CredentialManager]::Exists($Target) }
    $store | Add-Member -MemberType ScriptMethod -Name Copy -Value { param([string] $Source, [string] $Target) [LayerPeekMigration.CredentialManager]::Copy($Source, $Target) }
    $store | Add-Member -MemberType ScriptMethod -Name Delete -Value { param([string] $Target) [LayerPeekMigration.CredentialManager]::Delete($Target) }
    return $store
}

<#
.SYNOPSIS
    HKEY_CURRENT_USER as a registry for the functions above (sub-key paths relative to HKCU).
#>
function New-CurrentUserRegistry {
    $registry = [pscustomobject]@{ Name = 'HKCU' }
    $registry | Add-Member -MemberType ScriptMethod -Name Exists -Value {
        param([string] $Key)
        $opened = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($Key)
        if ($null -eq $opened) { return $false }
        $opened.Dispose()
        return $true
    }
    $registry | Add-Member -MemberType ScriptMethod -Name Remove -Value {
        param([string] $Key)
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($Key, $false)
    }
    return $registry
}

<#
.SYNOPSIS
    What the user types at the removal question; $null without an interactive console (then nothing is removed).
#>
function Read-RemovalAnswer {
    if (-not [Environment]::UserInteractive -or [Console]::IsInputRedirected) {
        Write-Host 'No interactive console: nothing removed. Run the script again with -RemoveOld in a console.' -ForegroundColor Yellow
        return $null
    }
    return Read-Host 'Type REMOVE to delete the old data listed above (anything else keeps it)'
}

<#
.SYNOPSIS
    The whole run. The parameters after -RemoveOld exist for the tests only; the script passes none of them, so a run
    uses the real per-user folders, the Windows Credential Manager, HKCU, the running processes and the console.
#>
function Invoke-Main {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Low')]
    param(
        [switch] $RemoveOld,
        [string] $AppData = [Environment]::GetFolderPath('ApplicationData'),
        [string] $LocalAppData = [Environment]::GetFolderPath('LocalApplicationData'),
        [string] $Programs = [Environment]::GetFolderPath('Programs'),
        [object] $Store,
        [object] $Registry,
        [scriptblock] $GetRunningApps = { Get-Process -Name $script:OldName, $script:NewName -ErrorAction SilentlyContinue },
        [scriptblock] $ReadConfirmation = { Read-RemovalAnswer }
    )

    $ErrorActionPreference = 'Stop'
    $running = @(& $GetRunningApps)
    if ($running.Count -gt 0) {
        throw "Quit $(($running | ForEach-Object { "$($_.ProcessName) ($($_.Id))" }) -join ', ') first: its files and credentials must not change meanwhile."
    }

    foreach ($folder in $AppData, $LocalAppData, $Programs) {
        if (-not [IO.Path]::IsPathFullyQualified($folder)) { throw 'A per-user folder of Windows is not available.' }
    }
    if (-not $Store) { $Store = New-CredentialManagerStore }
    if (-not $Registry) { $Registry = New-CurrentUserRegistry }

    if (-not $RemoveOld) {
        Write-Host "Migrating $script:OldName -> $script:NewName$(if ($WhatIfPreference) { ' (what if: nothing changes)' })" -ForegroundColor Cyan
        foreach ($folder in Get-MigrationFolders -AppData $appData -LocalAppData $localAppData) {
            $status = Copy-MigrationFolder -Source $folder.Source -Target $folder.Target -RenameLogs:$folder.RenameLogs
            Write-Host ("  {0,-5} {1,-13} {2} -> {3}" -f $folder.Name, $status, $folder.Source, $folder.Target)
            if ($status -eq 'TargetExists') {
                Write-Host "        not copied: $($folder.Target) existed before (e.g. LayerPeek started already); the old folder stays, see docs/troubleshooting.md" -ForegroundColor Yellow
            }
        }
        foreach ($result in @(Copy-MigrationCredentials -Store $store)) {
            Write-Host ("  cred  {0,-13} {1} -> {2}" -f $result.Status, $result.Old, $result.New)
        }
    }

    $items = @(Get-LegacyItems -AppData $appData -LocalAppData $localAppData -Programs $programs -Store $store -Registry $registry)
    if ($items.Count -eq 0) {
        Write-Host 'Nothing of the old name is left.' -ForegroundColor Green
        return
    }

    Write-Host ''
    Write-Host 'Left over from the old name:' -ForegroundColor Cyan
    foreach ($item in $items) { Write-Host (Format-LegacyItem -Item $item) }
    $ready = @($items | Where-Object Ready)
    if ($ready.Count -eq 0) { return }

    if ($WhatIfPreference) {
        $null = Remove-LegacyItems -Items $ready -Store $store -Registry $registry
        return
    }
    if (-not $RemoveOld) {
        Write-Host ''
        Write-Host "Check that $script:NewName starts with your printers before removing the old data (or run again later with -RemoveOld)."
    }
    # Exactly REMOVE, case-sensitive and without spaces; anything else (or no console) keeps everything.
    if ((& $ReadConfirmation) -cne 'REMOVE') {
        Write-Host 'The old data were kept.'
        return
    }
    $removed = @(Remove-LegacyItems -Items $ready -Store $store -Registry $registry)
    Write-Host "Removed $($removed.Count) item(s)." -ForegroundColor Green
}

# Dot-sourced (the Pester tests): only the functions above.
if ($MyInvocation.InvocationName -ne '.') {
    $ErrorActionPreference = 'Stop'
    if (-not $IsWindows) { throw 'The migration is for Windows only (the old builds never ran elsewhere).' }
    Invoke-Main -RemoveOld:$RemoveOld -WhatIf:$WhatIfPreference
}
