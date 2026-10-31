<#
.SYNOPSIS
    Pester 5+ tests of the before/after check of the user profile (tools/profile-snapshot.ps1, T-095).

.DESCRIPTION
    The script is dot-sourced (its main part does not run) and its functions are tested against stand-in folders in a
    temporary folder and fake outputs of sfltool, security and cmdkey: nothing of the real user profile, the Login Items,
    the Keychain or the Credential Manager is read.

    Temporary files live in one folder under $env:LAYERPEEK_PESTER_TESTS_TEMP (set by the xUnit wrapper
    tests/LayerPeek.Scripts.Tests, which also deletes it after a crash) or under the system temp folder.
    AfterAll deletes the folder.

.EXAMPLE
    Invoke-Pester tools/profile-snapshot.Tests.ps1 -Output Detailed
#>
#Requires -Version 7.2

BeforeAll {
    . (Join-Path $PSScriptRoot 'profile-snapshot.ps1')

    $base = if ($env:LAYERPEEK_PESTER_TESTS_TEMP) { $env:LAYERPEEK_PESTER_TESTS_TEMP } else { [System.IO.Path]::GetTempPath() }
    $script:TempRoot = Join-Path $base "layerpeek-ps-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    $null = New-Item -ItemType Directory -Path $script:TempRoot

    function New-File([string] $Path, [string] $Content = 'x') {
        $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path)
        [System.IO.File]::WriteAllText($Path, $Content)
    }

    # A stand-in profile: the data folder exists with a file, the log folder and the launch agents do not.
    function New-StandIn {
        $root = Join-Path $script:TempRoot ([guid]::NewGuid().ToString('N').Substring(0, 8))
        $locations = [ordered]@{
            Data         = Join-Path $root 'Application Support' 'LayerPeek'
            Logs         = Join-Path $root 'Logs' 'LayerPeek'
            LaunchAgents = Join-Path $root 'LaunchAgents'
        }
        New-File (Join-Path $locations.Data 'settings.json') '{"a":1}'
        $locations
    }

    function Read-StandIn([System.Collections.IDictionary] $Locations, [string[]] $Login = @(), [string[]] $Keychain = @()) {
        Get-ProfileState -Locations $Locations -LoginItems { , [string[]] $Login }.GetNewClosure() -LaunchServices { , [string[]] @() } -Credentials { $Keychain }.GetNewClosure()
    }

    function Invoke-Quiet([scriptblock] $Action) {
        $output = & $Action 6>&1
        [pscustomobject]@{ Code = @($output)[-1]; Text = (@($output | Select-Object -SkipLast 1) | Out-String) }
    }
}

AfterAll {
    if ($script:TempRoot -and (Test-Path -LiteralPath $script:TempRoot)) { Remove-Item -LiteralPath $script:TempRoot -Recurse -Force }
}

Describe 'Folder state' {
    It 'tells a missing folder from an empty one' {
        $standIn = New-StandIn
        @(Get-FolderState -Path $standIn.Logs) | Should -BeExactly @('(missing)')
        $null = New-Item -ItemType Directory -Path $standIn.Logs
        @(Get-FolderState -Path $standIn.Logs) | Should -BeExactly @('(exists)')
    }
    It 'lists folders and files with size and SHA-256, sorted, with / as separator' {
        $standIn = New-StandIn
        New-File (Join-Path $standIn.Data 'images' 'a.png') 'png'
        $hash = (Get-FileHash -LiteralPath (Join-Path $standIn.Data 'settings.json') -Algorithm SHA256).Hash.ToLowerInvariant()
        $lines = @(Get-FolderState -Path $standIn.Data)
        $lines[0] | Should -BeExactly '(exists)'
        $lines | Should -Contain 'images/'
        $lines | Should -Contain "settings.json  7  $hash"
        @($lines | Where-Object { $_ -like 'images/a.png  3  *' }).Count | Should -Be 1
    }
    It 'lists a watched file (a Preferences plist) with its size and SHA-256' {
        $standIn = New-StandIn
        $plist = Join-Path (Split-Path -Parent $standIn.Data) 'io.example.app.plist'
        @(Get-FolderState -Path $plist) | Should -BeExactly @('(missing)')
        New-File $plist 'bplist'
        $hash = (Get-FileHash -LiteralPath $plist -Algorithm SHA256).Hash.ToLowerInvariant()
        @(Get-FolderState -Path $plist) | Should -BeExactly @("(file)  6  $hash")
    }
    It 'only reads: the stand-in folder is the same afterwards' {
        $standIn = New-StandIn
        $before = @(Get-ChildItem -LiteralPath $standIn.Data -Recurse -Force | ForEach-Object { "$($_.FullName)|$($_.LastWriteTimeUtc.Ticks)" })
        $null = Read-StandIn $standIn
        @(Get-ChildItem -LiteralPath $standIn.Data -Recurse -Force | ForEach-Object { "$($_.FullName)|$($_.LastWriteTimeUtc.Ticks)" }) | Should -BeExactly $before
        Test-Path -LiteralPath $standIn.Logs | Should -BeFalse
        Test-Path -LiteralPath $standIn.LaunchAgents | Should -BeFalse
    }
}

Describe 'Before/after comparison' {
    It 'reports no change and exit code 0 for the same state' {
        $script:StandIn = New-StandIn
        $file = Join-Path $script:TempRoot "same-$([guid]::NewGuid().ToString('N')).json"
        (Invoke-Quiet { Invoke-Main -Save $file -ReadState { Read-StandIn $script:StandIn } }).Code | Should -Be 0
        $result = Invoke-Quiet { Invoke-Main -Compare $file -ReadState { Read-StandIn $script:StandIn } }
        $result.Code | Should -Be 0
        $result.Text | Should -Match 'No change'
    }
    It 'reports every change with a non-zero exit code: <Name>' -ForEach @(
        @{ Name = 'a new file'; Change = { param($s) New-File (Join-Path $s.Data 'new.txt') }; Expected = 'Data: \+ new\.txt  1  ' }
        @{ Name = 'a changed file'; Change = { param($s) New-File (Join-Path $s.Data 'settings.json') '{"a":2}' }; Expected = 'Data: - settings\.json  7  ' }
        @{ Name = 'a deleted file'; Change = { param($s) Remove-Item -LiteralPath (Join-Path $s.Data 'settings.json') }; Expected = 'Data: - settings\.json' }
        @{ Name = 'a created empty folder'; Change = { param($s) $null = New-Item -ItemType Directory -Path $s.Logs }; Expected = 'Logs: \+ \(exists\)' }
        @{ Name = 'a launch agent'; Change = { param($s) New-File (Join-Path $s.LaunchAgents 'com.example.plist') }; Expected = 'LaunchAgents: \+ com\.example\.plist' }
    ) {
        $script:StandIn = New-StandIn
        $file = Join-Path $script:TempRoot "change-$([guid]::NewGuid().ToString('N')).json"
        $null = Invoke-Quiet { Invoke-Main -Save $file -ReadState { Read-StandIn $script:StandIn } }
        & $Change $script:StandIn
        $result = Invoke-Quiet { Invoke-Main -Compare $file -ReadState { Read-StandIn $script:StandIn } }
        $result.Code | Should -Be 1
        $result.Text | Should -Match $Expected
    }
    It 'reports a new Login Item and a new saved password' {
        $standIn = New-StandIn
        $before = Read-StandIn $standIn
        $after = Read-StandIn $standIn -Login 'URL: file:///Applications/LayerPeek.app/' -Keychain 'LayerPeek/printer-1  x1'
        $changes = (Compare-ProfileState -Before $before -After $after).Changes
        $changes | Should -Contain 'Login Items: + URL: file:///Applications/LayerPeek.app/'
        # The place is named after the OS the test runs on (Credential Manager on Windows, Keychain on macOS).
        $changes | Should -Contain "$(Get-CredentialsPlace): + LayerPeek/printer-1  x1"
    }
    It 'never reports a place that could not be read as a change, but names it' {
        $standIn = New-StandIn
        $before = Get-ProfileState -Locations $standIn -LoginItems { $null } -LaunchServices { , [string[]] @() } -Credentials { @() }
        $after = Read-StandIn $standIn -Login 'LayerPeek'
        $result = Compare-ProfileState -Before $before -After $after
        $result.Changes | Should -BeNullOrEmpty
        $result.NotChecked | Should -BeExactly @('Login Items')
    }
    It 'keeps an empty list apart from "not readable" through the saved file' {
        $standIn = New-StandIn
        $file = Join-Path $script:TempRoot "empty-$([guid]::NewGuid().ToString('N')).json"
        Save-ProfileState -Path $file -State (Get-ProfileState -Locations $standIn -LoginItems { , [string[]] @() } -LaunchServices { , [string[]] @() } -Credentials { @() })
        $read = Read-ProfileState -Path $file
        , $read['Login Items'] | Should -BeOfType [string[]]
        $read['Login Items'].Count | Should -Be 0
        $read[(Get-CredentialsPlace)].Count | Should -Be 0
        (Compare-ProfileState -Before $read -After $read).NotChecked | Should -BeNullOrEmpty
    }
    It 'refuses a snapshot file inside the repository' {
        { Save-ProfileState -Path (Join-Path $script:RepoRoot 'snapshot.json') -State ([ordered]@{}) } | Should -Throw '*outside the repository*'
        Test-Path -LiteralPath (Join-Path $script:RepoRoot 'snapshot.json') | Should -BeFalse
    }
}

Describe 'Places of the bundle identifier (T-019)' {
    It 'reads the bundle identifier of build/macos/Info.plist' {
        Get-BundleIdentifier | Should -Match '^[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+$'
        $plist = Join-Path $script:TempRoot "info-$([guid]::NewGuid().ToString('N')).plist"
        New-File $plist "<plist><dict><key>CFBundleIdentifier</key>`n  <string>io.example.app</string></dict></plist>"
        Get-BundleIdentifier -PlistPath $plist | Should -BeExactly 'io.example.app'
    }
    It 'watches Preferences, Saved Application State, Caches and HTTPStorages of the bundle identifier on macOS' -Skip:(-not $IsMacOS) {
        $locations = Get-WatchedLocations -HomeDirectory '/Users/u' -BundleIdentifier 'io.example.app'
        $locations['Preferences/io.example.app.plist'] | Should -BeExactly '/Users/u/Library/Preferences/io.example.app.plist'
        $locations['Preferences/LayerPeek.plist'] | Should -BeExactly '/Users/u/Library/Preferences/LayerPeek.plist'
        $locations['Saved Application State/io.example.app.savedState'] | Should -BeExactly '/Users/u/Library/Saved Application State/io.example.app.savedState'
        $locations['Caches/io.example.app'] | Should -BeExactly '/Users/u/Library/Caches/io.example.app'
        $locations['HTTPStorages/io.example.app'] | Should -BeExactly '/Users/u/Library/HTTPStorages/io.example.app'
    }
    It 'keeps only the path, identifier and name lines of LaunchServices that mention LayerPeek, without record ids' {
        $dump = @(
            'bundle id:                  5432'
            'path:                       /Volumes/Data/x/artifacts/release/0.2.0/app/osx-arm64/LayerPeek.app (0x1abc)'
            'name:                       LayerPeek'
            'identifier:                 io.github.pfoltynek.LayerPeek (0x8000)'
            'reg date:                   2026-10-29 10:00:00'
            'path:                       /Applications/Safari.app (0x12)'
        )
        $entries = Get-LaunchServicesEntries -Dump { $dump }.GetNewClosure()
        $entries | Should -BeExactly @(
            'identifier: io.github.pfoltynek.LayerPeek'
            'name: LayerPeek'
            'path: /Volumes/Data/x/artifacts/release/0.2.0/app/osx-arm64/LayerPeek.app')
        $none = Get-LaunchServicesEntries -Dump { 'path: /Applications/Safari.app' }
        $null -eq $none | Should -BeFalse
        $none.Count | Should -Be 0
        $null -eq (Get-LaunchServicesEntries -Dump { }) | Should -BeTrue
    }
    It 'reports a new registration of the app with LaunchServices' {
        $standIn = New-StandIn
        $before = Read-StandIn $standIn
        $after = Get-ProfileState -Locations $standIn -LoginItems { , [string[]] @() } -LaunchServices { , [string[]] @('path: /x/LayerPeek.app') } -Credentials { @() }
        (Compare-ProfileState -Before $before -After $after).Changes | Should -Contain 'LaunchServices: + path: /x/LayerPeek.app'
    }
}

Describe 'System stores (fake outputs)' {
    It 'keeps only the LayerPeek and Prusa service names of the Keychain, with their count' {
        $dump = @(
            'keychain: "/Users/u/Library/Keychains/login.keychain-db"'
            '    "acct"<blob>="maker"'
            '    "svce"<blob>="LayerPeek/printer-1"'
            '    "svce"<blob>="LayerPeek/printer-1"'
            '    "svce"<blob>="Prusa Connect"'
            '    "svce"<blob>="com.example.other"'
            '    "desc"<blob>="LayerPeek secret description"'
        )
        @(Get-MacKeychainServices -DumpKeychain { $dump }.GetNewClosure()) | Should -BeExactly @('LayerPeek/printer-1  x2', 'Prusa Connect  x1')
    }
    It 'treats an sfltool run without authorization as not readable and an empty match as an empty list' {
        Get-MacLoginItems -DumpBtm { } | Should -BeNullOrEmpty
        $null -eq (Get-MacLoginItems -DumpBtm { }) | Should -BeTrue
        $none = Get-MacLoginItems -DumpBtm { '========================', ' #1:', '   Name: Other' }
        $null -eq $none | Should -BeFalse
        $none.Count | Should -Be 0
        @(Get-MacLoginItems -DumpBtm { ' #1:', '   URL: file:///Applications/LayerPeek.app/  ' }) | Should -BeExactly @('URL: file:///Applications/LayerPeek.app/')
    }
    It 'reads the LayerPeek target names of cmdkey in any language' {
        $english = @('Currently stored credentials:', '    Target: LegacyGeneric:target=LayerPeek/printer-1', '    Type: Generic', '    Target: LegacyGeneric:target=other')
        $czech = @('Aktuálně uložená pověření:', '    Cíl: LegacyGeneric:target=LayerPeek/printer-2', '    Uživatel: maker')
        @(Get-WindowsCredentialTargets -ListCredentials { $english }.GetNewClosure()) | Should -BeExactly @('LayerPeek/printer-1')
        @(Get-WindowsCredentialTargets -ListCredentials { $czech }.GetNewClosure()) | Should -BeExactly @('LayerPeek/printer-2')
    }
}
