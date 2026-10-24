<#
.SYNOPSIS
    Pester 5+ tests of the one-time migration from the former name (tools/migrate-from-prusatraydiagnostic.ps1, T-074).

.DESCRIPTION
    The script is dot-sourced (its main part does not run) and its functions are tested against made-up per-user
    folders in a temporary folder, a fake credential store and a fake registry: nothing of the real user profile, the
    Credential Manager or the registry is read or changed.

    Temporary files live in one folder under $env:LAYERPEEK_PESTER_TESTS_TEMP (set by the xUnit wrapper
    tests/LayerPeek.Scripts.Tests, which also deletes it after a crash) or under the system temp folder.
    AfterAll deletes the folder.

.EXAMPLE
    Invoke-Pester tools/migrate-from-prusatraydiagnostic.Tests.ps1 -Output Detailed
#>
#Requires -Version 7.2

BeforeAll {
    . (Join-Path $PSScriptRoot 'migrate-from-prusatraydiagnostic.ps1')
    # Invoke-Main without these parameters would run against the real profile, Credential Manager and registry.
    foreach ($parameter in 'AppData', 'LocalAppData', 'Programs', 'Store', 'Registry', 'GetRunningApps', 'ReadConfirmation') {
        if (-not (Get-Command Invoke-Main).Parameters.ContainsKey($parameter)) {
            throw "Invoke-Main has no parameter -$parameter; the tests refuse to run (they would touch the real profile)."
        }
    }

    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).ProviderPath
    $base = if ($env:LAYERPEEK_PESTER_TESTS_TEMP) { $env:LAYERPEEK_PESTER_TESTS_TEMP } else { [System.IO.Path]::GetTempPath() }
    $script:TempRoot = Join-Path $base "layerpeek-mt-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    $null = New-Item -ItemType Directory -Path $script:TempRoot

    $script:Secret = 'S3cret-never-printed!'

    # A fresh fake profile: AppData, LocalAppData and the Start menu Programs folder.
    function New-Profile {
        $root = Join-Path $script:TempRoot ([guid]::NewGuid().ToString('N').Substring(0, 8))
        $fake = [pscustomobject]@{
            AppData      = Join-Path $root 'Roaming'
            LocalAppData = Join-Path $root 'Local'
            Programs     = Join-Path $root 'Programs'
        }
        foreach ($folder in $fake.AppData, $fake.LocalAppData, $fake.Programs) { $null = New-Item -ItemType Directory -Path $folder }
        return $fake
    }

    function New-File([string] $Path, [string] $Content = 'x') {
        $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path)
        [System.IO.File]::WriteAllText($Path, $Content)
    }

    # Credential Manager stand-in: target -> secret, filter like CredEnumerate ("prefix*", case-insensitive).
    function New-FakeStore([hashtable] $Entries) {
        $store = [pscustomobject]@{ Entries = $Entries }
        $store | Add-Member -MemberType ScriptMethod -Name List -Value {
            param([string] $Filter)
            $prefix = $Filter.TrimEnd('*')
            @($this.Entries.Keys | Where-Object { $_.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) })
        }
        $store | Add-Member -MemberType ScriptMethod -Name Exists -Value { param([string] $Target) $this.Entries.ContainsKey($Target) }
        $store | Add-Member -MemberType ScriptMethod -Name Copy -Value { param([string] $Source, [string] $Target) $this.Entries[$Target] = $this.Entries[$Source] }
        $store | Add-Member -MemberType ScriptMethod -Name Delete -Value { param([string] $Target) $this.Entries.Remove($Target) }
        return $store
    }

    # HKCU stand-in: a set of existing sub-keys (with all their parents implied).
    function New-FakeRegistry([string[]] $Keys) {
        $registry = [pscustomobject]@{ Keys = [System.Collections.Generic.HashSet[string]]::new([string[]] $Keys, [StringComparer]::OrdinalIgnoreCase) }
        $registry | Add-Member -MemberType ScriptMethod -Name Exists -Value { param([string] $Key) $this.Keys.Contains($Key) }
        $registry | Add-Member -MemberType ScriptMethod -Name Remove -Value { param([string] $Key) $null = $this.Keys.Remove($Key) }
        return $registry
    }

    $script:ActivatorKey = 'Software\Classes\CLSID\{E8389DCA-851B-498C-8082-69B1AB263C14}'
    $script:NotificationKey = 'Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\PrusaTrayDiagnostic'
    $script:TestNotificationKey = 'Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\PrusaTrayDiagnostic.E2E'

    # Everything an old development build left behind, next to test leftovers and unrelated items that must stay.
    function New-OldInstallation {
        $p = New-Profile
        New-File (Join-Path $p.AppData 'PrusaTrayDiagnostic/settings.json') '{"old":1}'
        New-File (Join-Path $p.AppData 'PrusaTrayDiagnostic/printer-images/p1.png') 'png'
        New-File (Join-Path $p.LocalAppData 'PrusaTrayDiagnostic/logs/PrusaTrayDiagnostic.log') 'log'
        New-File (Join-Path $p.LocalAppData 'PrusaTrayDiagnostic.Build/ffmpeg/x.dll')
        New-File (Join-Path $p.Programs 'Prusa Tray Diagnostic.lnk')
        New-File (Join-Path $p.Programs 'Prusa Tray Diagnostic E2E.lnk')
        New-File (Join-Path $p.Programs 'Other.lnk')
        $p | Add-Member -NotePropertyName Store -NotePropertyValue (New-FakeStore @{
                'PrusaTrayDiagnostic/printer/password' = $script:Secret
                'PrusaTrayDiagnostic.E2E/wt/1/x'       = 'test'
                'Other/x'                              = 'other'
            })
        $p | Add-Member -NotePropertyName Registry -NotePropertyValue (New-FakeRegistry @(
                $script:ActivatorKey, $script:NotificationKey, $script:TestNotificationKey, 'Software\Other'))
        return $p
    }

    # Every file (with content), folder, credential and registry key of a profile, to compare before and after.
    function Get-Snapshot([object] $Profile) {
        $root = Split-Path -Parent $Profile.AppData
        foreach ($entry in Get-ChildItem -LiteralPath $root -Recurse -Force | Sort-Object FullName) {
            $relative = [System.IO.Path]::GetRelativePath($root, $entry.FullName)
            if ($entry.PSIsContainer) { "dir|$relative" } else { "file|$relative|$([System.IO.File]::ReadAllText($entry.FullName))" }
        }
        foreach ($target in $Profile.Store.Entries.Keys | Sort-Object) { "cred|$target|$($Profile.Store.Entries[$target])" }
        foreach ($key in $Profile.Registry.Keys | Sort-Object) { "reg|$key" }
    }

    # Invoke-Main on a profile: -Answer is what the user types at the removal question ($null = no console).
    function Invoke-Migration {
        param(
            [object] $Profile,
            [AllowNull()] [string] $Answer,
            [switch] $RemoveOld,
            [switch] $WhatIf,
            [scriptblock] $Running = { @() }
        )

        $asked = @{ Count = 0 }
        $reader = { $asked.Count++; $Answer }.GetNewClosure()
        $output = Invoke-Main -RemoveOld:$RemoveOld -WhatIf:$WhatIf -AppData $Profile.AppData -LocalAppData $Profile.LocalAppData `
            -Programs $Profile.Programs -Store $Profile.Store -Registry $Profile.Registry -GetRunningApps $Running -ReadConfirmation $reader *>&1 |
            Out-String
        [pscustomobject]@{ Output = $output; Asked = $asked.Count }
    }
}

AfterAll {
    if ($script:TempRoot -and (Test-Path -LiteralPath $script:TempRoot)) {
        Remove-Item -LiteralPath $script:TempRoot -Recurse -Force
    }
}

Describe 'Names' {
    It 'uses the exact production identities of the former name' {
        $script:OldCredentialPrefix | Should -BeExactly 'PrusaTrayDiagnostic/'
        $script:OldShortcutFileName | Should -BeExactly 'Prusa Tray Diagnostic.lnk'
        $script:OldToastActivatorKey | Should -BeExactly $script:ActivatorKey
        $script:OldNotificationSettingsKey | Should -BeExactly $script:NotificationKey
    }

    It 'migrates to the names the app uses now' {
        $source = Join-Path $script:RepoRoot 'src'
        Get-Content -Raw (Join-Path $source 'LayerPeek.Core/Settings/AppDataPaths.cs') |
            Should -Match ([regex]::Escape("AppFolderName = `"$script:NewName`""))
        Get-Content -Raw (Join-Path $source 'LayerPeek.App/Platform/Windows/WindowsCredentialSecretStore.cs') |
            Should -Match ([regex]::Escape("DefaultTargetPrefix = `"$($script:NewCredentialPrefix.TrimEnd('/'))`""))
        Get-Content -Raw (Join-Path $source 'LayerPeek.App/Logging/LogFile.cs') |
            Should -Match ([regex]::Escape("FileName = `"$script:NewName.log`""))
    }

    It 'renames the log files of the old app and keeps other names' -TestCases @(
        @{ Name = 'PrusaTrayDiagnostic.log'; Expected = 'LayerPeek.log' }
        @{ Name = 'PrusaTrayDiagnostic.3.log'; Expected = 'LayerPeek.3.log' }
        @{ Name = 'PrusaTrayDiagnostic.rotating.log'; Expected = 'LayerPeek.rotating.log' }
        @{ Name = 'notes.txt'; Expected = 'notes.txt' }
        @{ Name = 'PrusaTrayDiagnosticX.log'; Expected = 'PrusaTrayDiagnosticX.log' }
    ) {
        Get-MigratedLogFileName -Name $Name | Should -BeExactly $Expected
    }
}

Describe 'Copy-MigrationFolder' {
    It 'copies the data folder with its sub-folders once' {
        $p = New-Profile
        $folders = Get-MigrationFolders -AppData $p.AppData -LocalAppData $p.LocalAppData
        $data = $folders | Where-Object Name -EQ 'data'
        New-File (Join-Path $data.Source 'settings.json') '{"a":1}'
        New-File (Join-Path $data.Source 'printer-images/p1.png') 'png'

        Copy-MigrationFolder -Source $data.Source -Target $data.Target | Should -BeExactly 'Copied'

        Get-Content -Raw (Join-Path $p.AppData 'LayerPeek/settings.json') | Should -BeExactly '{"a":1}'
        Get-Content -Raw (Join-Path $p.AppData 'LayerPeek/printer-images/p1.png') | Should -BeExactly 'png'
        Test-Path -LiteralPath $data.Source | Should -BeTrue
        @(Get-ChildItem -LiteralPath $p.AppData -Force -Filter '.migrating-*') | Should -HaveCount 0
    }

    It 'marks the copied old folder and reports it as copied on the next run' {
        $p = New-Profile
        $data = Get-MigrationFolders -AppData $p.AppData -LocalAppData $p.LocalAppData | Where-Object Name -EQ 'data'
        New-File (Join-Path $data.Source 'settings.json')

        Copy-MigrationFolder -Source $data.Source -Target $data.Target | Should -BeExactly 'Copied'
        Copy-MigrationFolder -Source $data.Source -Target $data.Target | Should -BeExactly 'AlreadyCopied'

        Test-Path -LiteralPath (Join-Path $data.Source '.migrated-to-LayerPeek') -PathType Leaf | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $data.Target '.migrated-to-LayerPeek') | Should -BeFalse
    }

    It 'never overwrites an existing target and does not mark the old folder' {
        $p = New-Profile
        $data = Get-MigrationFolders -AppData $p.AppData -LocalAppData $p.LocalAppData | Where-Object Name -EQ 'data'
        New-File (Join-Path $data.Source 'settings.json') 'old'
        New-File (Join-Path $data.Target 'settings.json') 'new'

        Copy-MigrationFolder -Source $data.Source -Target $data.Target | Should -BeExactly 'TargetExists'

        Get-Content -Raw (Join-Path $data.Target 'settings.json') | Should -BeExactly 'new'
        Test-Path -LiteralPath (Join-Path $data.Source '.migrated-to-LayerPeek') | Should -BeFalse
    }

    It 'leaves no target and no marker when a file cannot be read' -Skip:(-not $IsWindows) {
        $p = New-Profile
        $data = Get-MigrationFolders -AppData $p.AppData -LocalAppData $p.LocalAppData | Where-Object Name -EQ 'data'
        New-File (Join-Path $data.Source 'settings.json')
        New-File (Join-Path $data.Source 'printer-images/locked.png')
        # The caller's default: a failed file copy is not terminating by itself.
        $ErrorActionPreference = 'Continue'
        $lock = [System.IO.File]::Open((Join-Path $data.Source 'printer-images/locked.png'), 'Open', 'Read', 'None')
        try {
            { Copy-MigrationFolder -Source $data.Source -Target $data.Target 2>$null } | Should -Throw
        }
        finally {
            $lock.Dispose()
        }

        Test-Path -LiteralPath $data.Target | Should -BeFalse
        @(Get-ChildItem -LiteralPath $p.AppData -Force -Filter '.migrating-*') | Should -HaveCount 0
        Test-Path -LiteralPath (Join-Path $data.Source '.migrated-to-LayerPeek') | Should -BeFalse
    }

    It 'does nothing without a source' {
        $p = New-Profile
        $data = Get-MigrationFolders -AppData $p.AppData -LocalAppData $p.LocalAppData | Where-Object Name -EQ 'data'

        Copy-MigrationFolder -Source $data.Source -Target $data.Target | Should -BeExactly 'NoSource'
        Test-Path -LiteralPath $data.Target | Should -BeFalse
    }

    It 'copies the logs under the new log names' {
        $p = New-Profile
        $logs = Get-MigrationFolders -AppData $p.AppData -LocalAppData $p.LocalAppData | Where-Object Name -EQ 'logs'
        New-File (Join-Path $logs.Source 'PrusaTrayDiagnostic.log') 'current'
        New-File (Join-Path $logs.Source 'PrusaTrayDiagnostic.1.log') 'older'

        Copy-MigrationFolder -Source $logs.Source -Target $logs.Target -RenameLogs:$logs.RenameLogs | Should -BeExactly 'Copied'

        $logs.Target | Should -BeExactly (Join-Path $p.LocalAppData 'LayerPeek' 'logs')
        @(Get-ChildItem -LiteralPath $logs.Target -Name | Sort-Object) | Should -Be @('LayerPeek.1.log', 'LayerPeek.log')
        Get-Content -Raw (Join-Path $logs.Target 'LayerPeek.log') | Should -BeExactly 'current'
    }

    It 'changes nothing with -WhatIf' {
        $p = New-Profile
        $data = Get-MigrationFolders -AppData $p.AppData -LocalAppData $p.LocalAppData | Where-Object Name -EQ 'data'
        New-File (Join-Path $data.Source 'settings.json')

        Copy-MigrationFolder -Source $data.Source -Target $data.Target -WhatIf | Should -BeExactly 'WouldCopy'

        Test-Path -LiteralPath $data.Target | Should -BeFalse
    }
}

Describe 'Copy-MigrationCredentials' {
    It 'copies only the production prefix and keeps existing new entries' {
        $store = New-FakeStore @{
            'PrusaTrayDiagnostic/printer-a/password' = $script:Secret
            'PrusaTrayDiagnostic/printer-b/password' = 'old-b'
            'LayerPeek/printer-b/password'           = 'new-b'
            'PrusaTrayDiagnostic.E2E/wt/1/x'         = 'test'
            'PrusaTrayDiagnosticOther/x'             = 'other'
            'PrusaTrayDiagnostic/'                   = 'empty key'
        }

        $results = @(Copy-MigrationCredentials -Store $store)

        $results | ForEach-Object Old | Should -Be @('PrusaTrayDiagnostic/printer-a/password', 'PrusaTrayDiagnostic/printer-b/password')
        ($results | Where-Object Old -EQ 'PrusaTrayDiagnostic/printer-a/password').Status | Should -BeExactly 'Copied'
        ($results | Where-Object Old -EQ 'PrusaTrayDiagnostic/printer-b/password').Status | Should -BeExactly 'TargetExists'
        $store.Entries['LayerPeek/printer-a/password'] | Should -BeExactly $script:Secret
        $store.Entries['LayerPeek/printer-b/password'] | Should -BeExactly 'new-b'
        $store.Entries.Keys | Where-Object { $_ -like 'LayerPeek.E2E*' -or $_ -like 'LayerPeekOther*' } | Should -BeNullOrEmpty
        $store.Entries.Count | Should -Be 7
    }

    It 'changes nothing with -WhatIf' {
        $store = New-FakeStore @{ 'PrusaTrayDiagnostic/a' = 'x' }

        (Copy-MigrationCredentials -Store $store -WhatIf).Status | Should -BeExactly 'WouldCopy'

        $store.Entries.Count | Should -Be 1
    }
}

Describe 'Get-LegacyItems and Remove-LegacyItems' {
    It 'lists every leftover and removes only what exists under the new name' {
        $p = New-Profile
        New-File (Join-Path $p.AppData 'PrusaTrayDiagnostic/settings.json')
        New-File (Join-Path $p.LocalAppData 'PrusaTrayDiagnostic/logs/PrusaTrayDiagnostic.log')
        New-File (Join-Path $p.LocalAppData 'PrusaTrayDiagnostic.Build/ffmpeg/x.dll')
        New-File (Join-Path $p.Programs 'Prusa Tray Diagnostic.lnk')
        New-File (Join-Path $p.Programs 'Prusa Tray Diagnostic E2E.lnk')
        New-File (Join-Path $p.Programs 'LayerPeek.lnk')
        # Data migrated by the script (its marker), logs not.
        New-File (Join-Path $p.AppData 'PrusaTrayDiagnostic/.migrated-to-LayerPeek')
        New-File (Join-Path $p.AppData 'LayerPeek/settings.json')
        $store = New-FakeStore @{
            'PrusaTrayDiagnostic/a' = 'a'
            'LayerPeek/a'           = 'a'
            'PrusaTrayDiagnostic/b' = 'b'
            'PrusaTrayDiagnostic.E2E/x' = 'test'
        }
        $registry = New-FakeRegistry @($script:ActivatorKey, $script:NotificationKey, 'Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\PrusaTrayDiagnostic.E2E')

        $items = @(Get-LegacyItems -AppData $p.AppData -LocalAppData $p.LocalAppData -Programs $p.Programs -Store $store -Registry $registry)

        $items | ForEach-Object { "$($_.Kind)|$($_.Path)|$($_.Ready)" } | Should -Be @(
            "Folder|$(Join-Path $p.AppData 'PrusaTrayDiagnostic')|True"
            "Folder|$(Join-Path $p.LocalAppData 'PrusaTrayDiagnostic')|False"
            "Folder|$(Join-Path $p.LocalAppData 'PrusaTrayDiagnostic.Build')|True"
            'Credential|PrusaTrayDiagnostic/a|True'
            'Credential|PrusaTrayDiagnostic/b|False'
            "Shortcut|$(Join-Path $p.Programs 'Prusa Tray Diagnostic.lnk')|True"
            "RegistryKey|HKCU\$script:ActivatorKey|True"
            "RegistryKey|HKCU\$script:NotificationKey|True"
        )

        $removed = @(Remove-LegacyItems -Items $items -Store $store -Registry $registry)

        $removed | Should -HaveCount 6
        Test-Path -LiteralPath (Join-Path $p.AppData 'PrusaTrayDiagnostic') | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $p.LocalAppData 'PrusaTrayDiagnostic/logs/PrusaTrayDiagnostic.log') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $p.LocalAppData 'PrusaTrayDiagnostic.Build') | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $p.Programs 'Prusa Tray Diagnostic.lnk') | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $p.Programs 'Prusa Tray Diagnostic E2E.lnk') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $p.Programs 'LayerPeek.lnk') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $p.AppData 'LayerPeek/settings.json') | Should -BeTrue
        @($store.Entries.Keys | Sort-Object) | Should -Be @('LayerPeek/a', 'PrusaTrayDiagnostic.E2E/x', 'PrusaTrayDiagnostic/b')
        @($registry.Keys) | Should -Be @('Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\PrusaTrayDiagnostic.E2E')
    }

    It 'keeps old folders whose LayerPeek counterpart existed before the migration' {
        $p = New-Profile
        New-File (Join-Path $p.AppData 'PrusaTrayDiagnostic/settings.json')
        New-File (Join-Path $p.LocalAppData 'PrusaTrayDiagnostic/logs/PrusaTrayDiagnostic.log')
        # A first start of LayerPeek before the migration: its own settings and log, the old ones never copied.
        New-File (Join-Path $p.AppData 'LayerPeek/settings.json')
        New-File (Join-Path $p.LocalAppData 'LayerPeek/logs/LayerPeek.log')
        $store = New-FakeStore @{}
        $registry = New-FakeRegistry @()

        $items = @(Get-LegacyItems -AppData $p.AppData -LocalAppData $p.LocalAppData -Programs $p.Programs -Store $store -Registry $registry)

        $items | ForEach-Object { "$($_.Kind)|$($_.Path)|$($_.Ready)" } | Should -Be @(
            "Folder|$(Join-Path $p.AppData 'PrusaTrayDiagnostic')|False"
            "Folder|$(Join-Path $p.LocalAppData 'PrusaTrayDiagnostic')|False"
        )
        $items | ForEach-Object { $_.Reason | Should -Match 'existed before the migration' }
        @(Remove-LegacyItems -Items $items -Store $store -Registry $registry) | Should -HaveCount 0
        Test-Path -LiteralPath (Join-Path $p.AppData 'PrusaTrayDiagnostic/settings.json') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $p.LocalAppData 'PrusaTrayDiagnostic/logs/PrusaTrayDiagnostic.log') | Should -BeTrue
    }

    It 'removes the old log folder once the script copied its logs' {
        $p = New-Profile
        New-File (Join-Path $p.LocalAppData 'PrusaTrayDiagnostic/logs/PrusaTrayDiagnostic.log')
        $logs = Get-MigrationFolders -AppData $p.AppData -LocalAppData $p.LocalAppData | Where-Object Name -EQ 'logs'
        $null = Copy-MigrationFolder -Source $logs.Source -Target $logs.Target -RenameLogs
        $store = New-FakeStore @{}
        $registry = New-FakeRegistry @()

        $items = @(Get-LegacyItems -AppData $p.AppData -LocalAppData $p.LocalAppData -Programs $p.Programs -Store $store -Registry $registry)

        $items | ForEach-Object { "$($_.Kind)|$($_.Path)|$($_.Ready)" } | Should -Be @("Folder|$(Join-Path $p.LocalAppData 'PrusaTrayDiagnostic')|True")
    }

    It 'removes nothing with -WhatIf' {
        $p = New-Profile
        New-File (Join-Path $p.Programs 'Prusa Tray Diagnostic.lnk')
        $registry = New-FakeRegistry @($script:ActivatorKey)
        $store = New-FakeStore @{}
        $items = @(Get-LegacyItems -AppData $p.AppData -LocalAppData $p.LocalAppData -Programs $p.Programs -Store $store -Registry $registry)

        @(Remove-LegacyItems -Items $items -Store $store -Registry $registry -WhatIf) | Should -HaveCount 0

        Test-Path -LiteralPath (Join-Path $p.Programs 'Prusa Tray Diagnostic.lnk') | Should -BeTrue
        $registry.Exists($script:ActivatorKey) | Should -BeTrue
    }

    It 'finds nothing in a clean profile' {
        $p = New-Profile

        @(Get-LegacyItems -AppData $p.AppData -LocalAppData $p.LocalAppData -Programs $p.Programs -Store (New-FakeStore @{}) -Registry (New-FakeRegistry @())) |
            Should -HaveCount 0
    }
}

Describe 'Invoke-Main' {
    It 'copies everything and keeps the old data unless REMOVE is typed exactly (<Label>)' -TestCases @(
        @{ Label = 'no console'; Answer = $null }
        @{ Label = 'empty'; Answer = '' }
        @{ Label = 'lower case'; Answer = 'remove' }
        @{ Label = 'trailing space'; Answer = 'REMOVE ' }
        @{ Label = 'yes'; Answer = 'yes' }
    ) {
        $p = New-OldInstallation

        $run = Invoke-Migration -Profile $p -Answer $Answer

        $run.Asked | Should -Be 1
        $run.Output | Should -Match 'The old data were kept'
        Get-Content -Raw (Join-Path $p.AppData 'LayerPeek/settings.json') | Should -BeExactly '{"old":1}'
        Get-Content -Raw (Join-Path $p.LocalAppData 'LayerPeek/logs/LayerPeek.log') | Should -BeExactly 'log'
        $p.Store.Entries['LayerPeek/printer/password'] | Should -BeExactly $script:Secret
        Test-Path -LiteralPath (Join-Path $p.AppData 'PrusaTrayDiagnostic/settings.json') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $p.LocalAppData 'PrusaTrayDiagnostic/logs/PrusaTrayDiagnostic.log') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $p.LocalAppData 'PrusaTrayDiagnostic.Build') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $p.Programs 'Prusa Tray Diagnostic.lnk') | Should -BeTrue
        $p.Store.Entries.ContainsKey('PrusaTrayDiagnostic/printer/password') | Should -BeTrue
        @($p.Registry.Keys) | Should -HaveCount 4
    }

    It 'removes exactly the migrated leftovers after REMOVE' {
        $p = New-OldInstallation

        $run = Invoke-Migration -Profile $p -Answer 'REMOVE'

        $run.Asked | Should -Be 1
        $run.Output | Should -Match 'Removed 7 item\(s\)'
        $run.Output | Should -Not -Match ([regex]::Escape($script:Secret))
        @(Get-ChildItem -LiteralPath $p.AppData -Name -Force) | Should -Be @('LayerPeek')
        @(Get-ChildItem -LiteralPath $p.LocalAppData -Name -Force) | Should -Be @('LayerPeek')
        @(Get-ChildItem -LiteralPath $p.Programs -Name -Force | Sort-Object) | Should -Be @('Other.lnk', 'Prusa Tray Diagnostic E2E.lnk')
        Get-Content -Raw (Join-Path $p.AppData 'LayerPeek/printer-images/p1.png') | Should -BeExactly 'png'
        @($p.Store.Entries.Keys | Sort-Object) | Should -Be @('LayerPeek/printer/password', 'Other/x', 'PrusaTrayDiagnostic.E2E/wt/1/x')
        $p.Store.Entries['LayerPeek/printer/password'] | Should -BeExactly $script:Secret
        @($p.Registry.Keys | Sort-Object) | Should -Be @('Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\PrusaTrayDiagnostic.E2E', 'Software\Other')
    }

    It 'changes nothing and asks nothing with -WhatIf' {
        $p = New-OldInstallation
        $before = @(Get-Snapshot $p)

        $run = Invoke-Migration -Profile $p -Answer 'REMOVE' -WhatIf

        $run.Asked | Should -Be 0
        @(Get-Snapshot $p) | Should -Be $before
    }

    It 'changes nothing and asks nothing with -RemoveOld -WhatIf after a migration' {
        $p = New-OldInstallation
        $null = Invoke-Migration -Profile $p -Answer 'no'
        $before = @(Get-Snapshot $p)

        $run = Invoke-Migration -Profile $p -Answer 'REMOVE' -RemoveOld -WhatIf

        $run.Asked | Should -Be 0
        @(Get-Snapshot $p) | Should -Be $before
    }

    It 'refuses while either app runs, before any change' {
        $p = New-OldInstallation
        $before = @(Get-Snapshot $p)

        { Invoke-Migration -Profile $p -Answer 'REMOVE' -Running { [pscustomobject]@{ ProcessName = 'LayerPeek'; Id = 42 } } } |
            Should -Throw '*LayerPeek (42)*'

        @(Get-Snapshot $p) | Should -Be $before
    }

    It 'stops before the removal question when a credential cannot be copied' {
        $p = New-OldInstallation
        $p.Store | Add-Member -MemberType ScriptMethod -Name Copy -Value { param($Source, $Target) throw 'CredWrite failed' } -Force
        $asked = @{ Count = 0 }

        {
            Invoke-Main -AppData $p.AppData -LocalAppData $p.LocalAppData -Programs $p.Programs -Store $p.Store -Registry $p.Registry `
                -GetRunningApps { @() } -ReadConfirmation { $asked.Count++; 'REMOVE' }.GetNewClosure() 6>$null
        } | Should -Throw '*CredWrite failed*'

        $asked.Count | Should -Be 0
        Test-Path -LiteralPath (Join-Path $p.AppData 'PrusaTrayDiagnostic/settings.json') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $p.Programs 'Prusa Tray Diagnostic.lnk') | Should -BeTrue
        $p.Store.Entries.ContainsKey('PrusaTrayDiagnostic/printer/password') | Should -BeTrue
        @($p.Registry.Keys) | Should -HaveCount 4
    }

    It 'runs again without harm' {
        $p = New-OldInstallation
        $null = Invoke-Migration -Profile $p -Answer 'no'
        $migrated = @(Get-Snapshot $p)

        $again = Invoke-Migration -Profile $p -Answer 'no'

        $again.Output | Should -Match 'AlreadyCopied'
        @(Get-Snapshot $p) | Should -Be $migrated

        $remove = Invoke-Migration -Profile $p -Answer 'REMOVE' -RemoveOld
        $remove.Output | Should -Match 'Removed 7 item\(s\)'

        $last = Invoke-Migration -Profile $p -Answer 'REMOVE'
        $last.Asked | Should -Be 0
        $last.Output | Should -Match 'Nothing of the old name is left'
    }

    It 'with -RemoveOld before a migration removes no data, only what needs no copy' {
        $p = New-OldInstallation

        $run = Invoke-Migration -Profile $p -Answer 'REMOVE' -RemoveOld

        $run.Output | Should -Match 'Removed 4 item\(s\)'
        Test-Path -LiteralPath (Join-Path $p.AppData 'LayerPeek') | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $p.AppData 'PrusaTrayDiagnostic/settings.json') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $p.LocalAppData 'PrusaTrayDiagnostic/logs/PrusaTrayDiagnostic.log') | Should -BeTrue
        $p.Store.Entries.ContainsKey('PrusaTrayDiagnostic/printer/password') | Should -BeTrue
        $p.Store.Entries.ContainsKey('LayerPeek/printer/password') | Should -BeFalse
    }
}

Describe 'Secrets' {
    It 'never shows a password in any output' {
        $p = New-Profile
        $store = New-FakeStore @{ 'PrusaTrayDiagnostic/printer/password' = $script:Secret }
        $registry = New-FakeRegistry @()

        $output = & {
            Copy-MigrationCredentials -Store $store -Verbose | Format-Table -AutoSize | Out-String
            $items = @(Get-LegacyItems -AppData $p.AppData -LocalAppData $p.LocalAppData -Programs $p.Programs -Store $store -Registry $registry)
            $items | ForEach-Object { Format-LegacyItem -Item $_ }
            $items | Format-List | Out-String
            Remove-LegacyItems -Items $items -Store $store -Registry $registry -Verbose | Out-String
        } *>&1 | Out-String

        $output | Should -Not -BeNullOrEmpty
        $output | Should -Not -Match ([regex]::Escape($script:Secret))
        $store.Entries.ContainsKey('PrusaTrayDiagnostic/printer/password') | Should -BeFalse
        $store.Entries['LayerPeek/printer/password'] | Should -BeExactly $script:Secret
    }
}

Describe 'Windows helpers' {
    It 'compiles the Credential Manager helper and lists without changing anything' -Skip:(-not $IsWindows) {
        # Read-only: a filter no credential can match; nothing is written, copied or deleted.
        $store = New-CredentialManagerStore

        @($store.List("LayerPeek.MigrationTests.$([guid]::NewGuid().ToString('N'))/*")) | Should -HaveCount 0
        $store.Exists("LayerPeek.MigrationTests.$([guid]::NewGuid().ToString('N'))/x") | Should -BeFalse
    }

    It 'copies a real credential inside Windows with its user name and persistence' -Skip:(-not $IsWindows) {
        # Session credentials under a prefix of this test only: never a credential of the app or of the E2E tests, and
        # Windows drops them at logoff even if this test cannot delete them (a crash).
        if (-not ('LayerPeekMigrationTests.CredentialFixture' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LayerPeekMigrationTests
{
    public static class CredentialFixture
    {
        public const int PersistSession = 1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string Comment;
            public long LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref Credential credential, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr buffer);

        public static void WriteSession(string target, string userName, byte[] blob)
        {
            IntPtr buffer = Marshal.AllocCoTaskMem(blob.Length);
            try
            {
                Marshal.Copy(blob, 0, buffer, blob.Length);
                Credential credential = new Credential { Type = 1, TargetName = target, UserName = userName, CredentialBlobSize = blob.Length, CredentialBlob = buffer, Persist = PersistSession };
                if (!CredWrite(ref credential, 0)) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
            }
            finally
            {
                Marshal.FreeCoTaskMem(buffer);
            }
        }

        // User name, persistence and the secret bytes as Base64 (test secrets only).
        public static string Describe(string target)
        {
            IntPtr pointer;
            if (!CredRead(target, 1, 0, out pointer)) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
            try
            {
                Credential credential = Marshal.PtrToStructure<Credential>(pointer);
                byte[] blob = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
                return credential.UserName + "|" + credential.Persist + "|" + Convert.ToBase64String(blob);
            }
            finally
            {
                CredFree(pointer);
            }
        }

        public static void DeleteIfExists(string target)
        {
            CredDelete(target, 1, 0);
        }
    }
}
'@
        }
        $prefix = "LayerPeek.MigrationTests.$([guid]::NewGuid().ToString('N'))"
        $source = "$prefix/printer/password"
        $target = "$prefix.New/printer/password"
        $sibling = "$prefix.E2E/printer/password"
        try {
            [LayerPeekMigrationTests.CredentialFixture]::WriteSession($source, 'maker', [System.Text.Encoding]::UTF8.GetBytes($script:Secret))
            [LayerPeekMigrationTests.CredentialFixture]::WriteSession($sibling, 'test', [byte[]] @(1, 2, 3))
            $store = New-CredentialManagerStore

            # The filter of CredEnumerate keeps the slash: a sibling prefix like the test one is never listed.
            @($store.List("$prefix/*")) | Should -Be @($source)
            $store.Exists($target) | Should -BeFalse
            $store.Copy($source, $target)

            $store.Exists($target) | Should -BeTrue
            [LayerPeekMigrationTests.CredentialFixture]::Describe($target) |
                Should -BeExactly ([LayerPeekMigrationTests.CredentialFixture]::Describe($source))
            [LayerPeekMigrationTests.CredentialFixture]::Describe($target) | Should -BeLike 'maker|1|*'
            $store.Delete($source)
            $store.Exists($source) | Should -BeFalse
            $store.Exists($sibling) | Should -BeTrue
        }
        finally {
            foreach ($credential in $source, $target, $sibling) { [LayerPeekMigrationTests.CredentialFixture]::DeleteIfExists($credential) }
        }
    }

    It 'reads HKCU without changing it' -Skip:(-not $IsWindows) {
        $registry = New-CurrentUserRegistry

        $registry.Exists('Software') | Should -BeTrue
        $registry.Exists("Software\LayerPeek.MigrationTests.$([guid]::NewGuid().ToString('N'))") | Should -BeFalse
    }
}
