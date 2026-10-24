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

    It 'never overwrites an existing target' {
        $p = New-Profile
        $data = Get-MigrationFolders -AppData $p.AppData -LocalAppData $p.LocalAppData | Where-Object Name -EQ 'data'
        New-File (Join-Path $data.Source 'settings.json') 'old'
        New-File (Join-Path $data.Target 'settings.json') 'new'

        Copy-MigrationFolder -Source $data.Source -Target $data.Target | Should -BeExactly 'TargetExists'

        Get-Content -Raw (Join-Path $data.Target 'settings.json') | Should -BeExactly 'new'
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
        # Data migrated, logs not.
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

    It 'reads HKCU without changing it' -Skip:(-not $IsWindows) {
        $registry = New-CurrentUserRegistry

        $registry.Exists('Software') | Should -BeTrue
        $registry.Exists("Software\LayerPeek.MigrationTests.$([guid]::NewGuid().ToString('N'))") | Should -BeFalse
    }
}
