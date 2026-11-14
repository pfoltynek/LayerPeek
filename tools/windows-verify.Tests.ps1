<#
.SYNOPSIS
    Pester 5+ tests of the main part of tools/windows-verify.ps1 (T-129, the T-105 criterion "the whole interactive run"):
    the real script in a child pwsh, interactive answers through standard input (the real Read-Host prompt), an
    interruption in the middle of a run and -RestoreOnly, the console summary and the report.

.DESCRIPTION
    Nothing real changes on the system: the script gets a stand-in file through LAYERPEEK_VERIFY_STANDINS (dot-sourced by
    the script before its main part) that replaces the checks with stand-ins and points the install paths and the
    Credential Manager into a sandbox folder. The stand-in checks still use the script's own install backup and
    restore (Backup-Install, Restore-Install), so the restore logic of -RestoreOnly is the real one.

    Windows only (the script refuses to run its checks elsewhere), except the -ListChecks test of the seam. Temporary files live in one folder under
    $env:LAYERPEEK_PESTER_TESTS_TEMP (set by the xUnit wrapper tests/LayerPeek.Scripts.Tests) or under the system temp
    folder; AfterAll deletes it.

.EXAMPLE
    Invoke-Pester tools/windows-verify.Tests.ps1 -Output Detailed
#>
#Requires -Version 7.2

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..' 'scripts' 'ReleaseTools' 'ReleaseTools.psm1') -Force
    # Hide-VerifyHomePath: what the report writes for a path.
    Import-Module (Join-Path $PSScriptRoot 'VerifyTools.psm1') -Force

    $base = if ($env:LAYERPEEK_PESTER_TESTS_TEMP) { $env:LAYERPEEK_PESTER_TESTS_TEMP } else { [System.IO.Path]::GetTempPath() }
    $script:TempRoot = Join-Path $base "layerpeek-wv-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    $null = New-Item -ItemType Directory -Path $script:TempRoot
    $script:Script = Join-Path $PSScriptRoot 'windows-verify.ps1'

    # Dot-sourced by windows-verify.ps1 ($PSScriptRoot = the sandbox). Single-quoted: nothing is expanded here.
    $script:StandIns = @'
$script:Sandbox = $PSScriptRoot

function Get-InstallPaths {
    $local = Join-Path $script:Sandbox 'local'
    $roaming = Join-Path $script:Sandbox 'roaming'
    [pscustomobject]@{
        InstallFolder = Join-Path $local 'LayerPeek'
        InstalledExe  = Join-Path $local 'LayerPeek' 'current' 'LayerPeek.exe'
        UpdateExe     = Join-Path $local 'LayerPeek' 'Update.exe'
        Data          = Join-Path $roaming 'LayerPeek'
        StartMenu     = Join-Path $script:Sandbox 'programs' 'LayerPeek.lnk'
        Startup       = Join-Path $script:Sandbox 'startup' 'LayerPeek.lnk'
        Journal       = Join-Path $roaming 'LayerPeek.verify-install-journal.json'
        State         = Join-Path $roaming 'LayerPeek.verify-install-state.json'
    }
}

function Get-CredentialTargets {
    $file = Join-Path $script:Sandbox 'credentials.txt'
    @(if (Test-Path -LiteralPath $file) { Get-Content -LiteralPath $file | Where-Object { $_ } })
}

function Remove-CredentialTarget([string] $Target) {
    $left = @(Get-CredentialTargets | Where-Object { $_ -cne $Target })
    [System.IO.File]::WriteAllLines((Join-Path $script:Sandbox 'credentials.txt'), [string[]] $left)
}

function Write-Ran([string] $Id) { [System.IO.File]::AppendAllText((Join-Path $script:Sandbox 'ran.txt'), "$Id`n") }

$checks = @(
    New-VerifyCheck -Id 'win.fake-auto' -Kind Auto -Title 'stand-in auto' -Closes 'T-129 stand-in' -Action {
        param($c)
        Write-Ran 'win.fake-auto'
        $null = Save-VerifyEvidence -Context $c -Name 'auto.txt' -Content 'stand-in evidence'
        New-VerifyResult PASS 'stand-in auto ran'
    }
    New-VerifyCheck -Id 'win.fake-permission' -Kind Permission -Permission 'a stand-in permission' -Title 'stand-in permission' -Closes 'T-129 stand-in' -Action {
        Write-Ran 'win.fake-permission'
        New-VerifyResult PASS 'stand-in permission ran'
    }
    New-VerifyCheck -Id 'win.fake-declined' -Kind Permission -Permission 'a stand-in to decline' -Title 'stand-in declined' -Closes 'T-129 stand-in' -Action {
        Write-Ran 'win.fake-declined'
        New-VerifyResult PASS 'must not run'
    }
    New-VerifyCheck -Id 'win.fake-install' -Kind Permission -Permission 'a stand-in install into the sandbox' -Title 'stand-in install' -Closes 'T-129 stand-in' -Action {
        Write-Ran 'win.fake-install'
        $paths = Get-InstallPaths
        Backup-Install
        # What Setup.exe and the installed app would leave: the app, its data, a credential and the shortcuts.
        $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $paths.InstalledExe), $paths.Data
        [System.IO.File]::WriteAllText($paths.InstalledExe, 'installed')
        [System.IO.File]::WriteAllText((Join-Path $paths.Data 'settings.json'), '{"installed":true}')
        [System.IO.File]::WriteAllText($paths.StartMenu, 'installed start menu shortcut')
        [System.IO.File]::WriteAllText($paths.Startup, 'installed startup shortcut')
        [System.IO.File]::AppendAllText((Join-Path $script:Sandbox 'credentials.txt'), "LayerPeek/printer/new`n")
        New-VerifyResult PASS 'stand-in installed'
    }
    New-VerifyCheck -Id 'win.fake-manual' -Kind Manual -Title 'stand-in manual' -Closes 'T-129 stand-in' -Action {
        param($c)
        Write-Ran 'win.fake-manual'
        if (Confirm-VerifyStep $c 'Did the stand-in toast appear?') { return New-VerifyResult MANUAL 'stand-in toast seen' }
        New-VerifyResult FAIL 'no stand-in toast (user).'
    }
    New-VerifyCheck -Id 'win.fake-interrupt' -Kind Auto -Title 'stand-in interruption (the window is closed)' -Closes 'T-129 stand-in' -Action {
        Write-Ran 'win.fake-interrupt'
        # A hard end of the process: no finally block, no cleanup, no report.
        Stop-Process -Id $PID -Force
    }
    New-VerifyCheck -Id 'win.fake-uninstall' -Kind Permission -Permission 'removing the stand-in install' -Title 'stand-in uninstall' -Closes 'T-129 stand-in' -Action {
        param($c)
        Write-Ran 'win.fake-uninstall'
        $notes = @(Restore-Install)
        $null = Save-VerifyEvidence -Context $c -Name 'restore.txt' -Content $notes
        New-VerifyResult PASS "stand-in uninstalled; $($notes -join '; ')."
    }
)
'@

    function New-Sandbox {
        $sandbox = Join-Path $script:TempRoot ([guid]::NewGuid().ToString('N').Substring(0, 8))
        # Separate path segments: the -ListChecks test also runs on macOS, where a backslash is part of a file name.
        foreach ($folder in @(@('roaming', 'LayerPeek'), @('local', 'LayerPeek', 'logs'), @('programs'), @('startup'), @('out'))) { $null = New-Item -ItemType Directory -Force -Path (Join-Path $sandbox @folder) }
        # The "user profile" of before: data, logs, a Start menu shortcut, no Startup shortcut, one credential.
        [System.IO.File]::WriteAllText((Join-Path $sandbox 'roaming' 'LayerPeek' 'settings.json'), '{"user":true}')
        [System.IO.File]::WriteAllText((Join-Path $sandbox 'local' 'LayerPeek' 'logs' 'app.log'), 'user log')
        [System.IO.File]::WriteAllText((Join-Path $sandbox 'programs' 'LayerPeek.lnk'), 'user start menu shortcut')
        [System.IO.File]::WriteAllText((Join-Path $sandbox 'credentials.txt'), "LayerPeek/printer/existing`n")
        [System.IO.File]::WriteAllText((Join-Path $sandbox 'standins.ps1'), $script:StandIns)
        $sandbox
    }

    function Get-ProfileSnapshot([string] $Sandbox) {
        # Every file of the stand-in profile with its content; the credentials as a sorted list.
        $lines = foreach ($folder in 'roaming', 'local', 'programs', 'startup') {
            Get-ChildItem -LiteralPath (Join-Path $Sandbox $folder) -Recurse -Force -File | Sort-Object FullName | ForEach-Object {
                "$([System.IO.Path]::GetRelativePath($Sandbox, $_.FullName))=$([System.IO.File]::ReadAllText($_.FullName))"
            }
        }
        $credentials = @(Get-Content -LiteralPath (Join-Path $Sandbox 'credentials.txt') | Where-Object { $_ } | Sort-Object)
        (@($lines) + @("credentials=$($credentials -join ',')")) -join "`n"
    }

    function Get-Ran([string] $Sandbox) {
        $file = Join-Path $Sandbox 'ran.txt'
        @(if (Test-Path -LiteralPath $file) { Get-Content -LiteralPath $file | Where-Object { $_ } })
    }

    function Invoke-Verify([string] $Sandbox, [string[]] $Arguments, [string[]] $Answers = @(), [string] $StandInsPath) {
        <# windows-verify.ps1 in a child pwsh with the stand-ins; the answers go to its standard input (Read-Host). #>
        $command = @(Get-PwshCommand)
        $prefix = @($command | Select-Object -Skip 1)
        $saved = $env:LAYERPEEK_VERIFY_STANDINS
        $env:LAYERPEEK_VERIFY_STANDINS = if ($StandInsPath) { $StandInsPath } else { Join-Path $Sandbox 'standins.ps1' }
        try {
            # Always a redirected standard input: an empty line when no answer is given, so that nothing waits for a key.
            $stdin = if ($Answers.Count -gt 0) { $Answers } else { @('') }
            $output = @($stdin | & $command[0] @prefix -NoProfile -File $script:Script @Arguments -OutputRoot (Join-Path $Sandbox 'out') 2>&1 | ForEach-Object { "$_" })
            $exitCode = $LASTEXITCODE
        }
        finally { $env:LAYERPEEK_VERIFY_STANDINS = $saved }
        $runs = @(Get-ChildItem -LiteralPath (Join-Path $Sandbox 'out') -Directory -ErrorAction SilentlyContinue | Sort-Object Name)
        [pscustomobject]@{ ExitCode = $exitCode; Output = $output; Text = $output -join "`n"; RunFolder = $(if ($runs) { $runs[-1].FullName } else { $null }) }
    }

    function Read-Report([string] $RunFolder) {
        [pscustomobject]@{
            Markdown = Get-Content -LiteralPath (Join-Path $RunFolder 'report.md') -Raw
            Json     = Get-Content -LiteralPath (Join-Path $RunFolder 'report.json') -Raw | ConvertFrom-Json
        }
    }
}

AfterAll {
    if ($script:TempRoot -and (Test-Path -LiteralPath $script:TempRoot)) { Remove-Item -LiteralPath $script:TempRoot -Recurse -Force }
}

Describe 'windows-verify.ps1 stand-in seam' {
    It 'replaces the checks only when LAYERPEEK_VERIFY_STANDINS is set (any OS: -ListChecks runs before the OS check)' {
        $sandbox = New-Sandbox
        $standIn = Invoke-Verify $sandbox @('-ListChecks')
        $standIn.ExitCode | Should -Be 0 -Because $standIn.Text
        $standIn.Text | Should -Match 'STAND-INS: '
        $standIn.Text | Should -Match 'win\.fake-interrupt  \[auto\]'
        $standIn.Text | Should -Not -Match 'win\.build  \[auto\]'

        $saved = $env:LAYERPEEK_VERIFY_STANDINS
        $env:LAYERPEEK_VERIFY_STANDINS = $null
        try {
            $command = @(Get-PwshCommand)
            $real = @(& $command[0] @($command | Select-Object -Skip 1) -NoProfile -NonInteractive -File $script:Script -ListChecks 2>&1 | ForEach-Object { "$_" }) -join "`n"
        }
        finally { $env:LAYERPEEK_VERIFY_STANDINS = $saved }
        $real | Should -Not -Match 'STAND-INS|win\.fake-'
        $real | Should -Match 'win\.build  \[auto\]'
        $real | Should -Match 'win\.uninstall  \[permission\]'
    }

    It 'refuses a stand-in file outside the temp folders of the tests with exit code 2 and runs nothing (<Case>)' -ForEach @(
        @{ Case = 'in the repository'; Path = { Join-Path $PSScriptRoot 'standins.ps1' } }
        @{ Case = 'out of the temp folder through ..'; Path = { param($s) Join-Path $s '..' '..' '..' '..' '..' '..' '..' '..' '..' '..' 'standins.ps1' } }
    ) {
        $sandbox = New-Sandbox
        $path = & $Path $sandbox
        foreach ($arguments in @('-ListChecks'), @('-Unattended', '-Only', 'win.fake-auto')) {
            $run = Invoke-Verify $sandbox $arguments -StandInsPath $path
            $run.ExitCode | Should -Be 2 -Because $run.Text
            $run.Text | Should -Match 'is not inside the temp folder of the tests'
            $run.Text | Should -Match 'no check was run'
            $run.Text | Should -Not -Match 'STAND-INS: |win\.build|Summary:'
        }
        Get-Ran $sandbox | Should -HaveCount 0
        @(Get-ChildItem -LiteralPath (Join-Path $sandbox 'out') -Force) | Should -HaveCount 0
    }
}

Describe 'windows-verify.ps1 interactive run (stand-in checks)' -Skip:(-not $IsWindows) {
    It 'asks before Permission and Manual checks, runs only what was allowed and reports every answer' {
        $sandbox = New-Sandbox
        $before = Get-ProfileSnapshot $sandbox
        # Questions in check order: permission y, declined n, install y, manual n, uninstall y.
        $run = Invoke-Verify $sandbox @('-Skip', 'win.fake-interrupt') @('y', 'n', 'y', 'n', 'y')

        $run.ExitCode | Should -Be 1 -Because $run.Text
        $run.Text | Should -Match 'STAND-INS: '
        $run.Text | Should -Match 'LayerPeek Windows verification \(interactive\), 6 check\(s\)'
        Get-Ran $sandbox | Should -Be @('win.fake-auto', 'win.fake-permission', 'win.fake-install', 'win.fake-manual', 'win.fake-uninstall')
        $run.Text | Should -Match ([regex]::Escape('Summary: PASS 4, FAIL 1, SKIP 1, MANUAL 0'))
        $run.Text | Should -Match 'SKIP\s+win\.fake-declined\s+declined by the user \(needs: a stand-in to decline\)'
        $run.Text | Should -Match 'FAIL\s+win\.fake-manual\s+no stand-in toast \(user\)'
        $run.Text | Should -Not -Match 'The verification install stays'

        $report = Read-Report $run.RunFolder
        $report.Json.Run.Mode | Should -Be 'interactive'
        $report.Json.Run.Script | Should -Be 'tools/windows-verify.ps1'
        # The report says that this was a test run with stand-ins.
        # With the home folder as ~ like every path of the report (T-208); the sandbox lies in %TEMP%, inside the profile.
        $standIns = Hide-VerifyHomePath ([System.IO.Path]::GetFullPath((Join-Path $sandbox 'standins.ps1')))
        $report.Json.Run.StandIns | Should -Be $standIns
        $report.Markdown | Should -Match ([regex]::Escape("- **Stand-ins:** ``$standIns`` (test run with stand-in checks, not a real verification)"))
        @($report.Json.Run.Selected) | Should -Be @('win.fake-auto', 'win.fake-permission', 'win.fake-declined', 'win.fake-install', 'win.fake-manual', 'win.fake-uninstall')
        @($report.Json.Results | ForEach-Object { "$($_.Id)=$($_.Status)" }) | Should -Be @('win.fake-auto=PASS', 'win.fake-permission=PASS', 'win.fake-declined=SKIP', 'win.fake-install=PASS', 'win.fake-manual=FAIL', 'win.fake-uninstall=PASS')
        $report.Json.Summary.PASS | Should -Be 4
        $report.Json.Summary.FAIL | Should -Be 1
        $report.Markdown | Should -Match '- Mode: interactive'
        $report.Markdown | Should -Match ([regex]::Escape('- Result: PASS 4, FAIL 1, SKIP 1, MANUAL 0'))
        $report.Markdown | Should -Match ([regex]::Escape('| `win.fake-declined` | Permission | **SKIP** | ') + '[\d.,]+' + [regex]::Escape(' | T-129 stand-in | declined by the user (needs: a stand-in to decline). |'))
        $report.Markdown | Should -Match ([regex]::Escape('[evidence/win.fake-auto/auto.txt](evidence/win.fake-auto/auto.txt)'))
        # The answers are kept with the questions.
        Get-Content -LiteralPath (Join-Path $run.RunFolder 'evidence' 'win.fake-manual' 'steps.txt') | Should -Contain '? Did the stand-in toast appear? -> no'
        Get-Content -LiteralPath (Join-Path $run.RunFolder 'evidence' 'win.fake-uninstall' 'restore.txt') | Should -Contain 'removed the credential LayerPeek/printer/new'
        # The install was undone inside the run: the profile is as before.
        Get-ProfileSnapshot $sandbox | Should -Be $before
    }

    It 'confirms a Manual check with y as MANUAL and ends with exit code 0' {
        $sandbox = New-Sandbox
        $run = Invoke-Verify $sandbox @('-Only', 'win.fake-auto,win.fake-manual') @('Y')

        $run.ExitCode | Should -Be 0 -Because $run.Text
        $run.Text | Should -Match ([regex]::Escape('Summary: PASS 1, FAIL 0, SKIP 0, MANUAL 1'))
        # The Seconds column (T-208) between result and criterion.
        (Read-Report $run.RunFolder).Markdown | Should -Match ([regex]::Escape('| `win.fake-manual` | Manual | **MANUAL** | ') + '[\d.,]+' + [regex]::Escape(' | T-129 stand-in | stand-in toast seen |'))
    }

    It 'never asks and never runs Permission or Manual checks with -Unattended' {
        $sandbox = New-Sandbox
        $before = Get-ProfileSnapshot $sandbox
        $run = Invoke-Verify $sandbox @('-Unattended', '-Skip', 'win.fake-interrupt') @('y', 'y', 'y', 'y', 'y')

        $run.ExitCode | Should -Be 0 -Because $run.Text
        Get-Ran $sandbox | Should -Be @('win.fake-auto')
        $run.Text | Should -Match ([regex]::Escape('Summary: PASS 1, FAIL 0, SKIP 5, MANUAL 0'))
        (Read-Report $run.RunFolder).Json.Run.Mode | Should -Be 'unattended'
        Get-ProfileSnapshot $sandbox | Should -Be $before
    }

    It 'keeps the install between runs and says so, until the uninstall run restores the profile' {
        $sandbox = New-Sandbox
        $before = Get-ProfileSnapshot $sandbox
        $install = Invoke-Verify $sandbox @('-Only', 'win.fake-install') @('y')
        $install.ExitCode | Should -Be 0 -Because $install.Text
        $install.Text | Should -Match 'The verification install stays \(with your profile moved aside\) until: pwsh tools/windows-verify.ps1 -Only win.uninstall'
        Test-Path -LiteralPath (Join-Path $sandbox 'roaming' 'LayerPeek.verify-install-state.json') | Should -BeTrue

        $uninstall = Invoke-Verify $sandbox @('-Only', 'win.fake-uninstall') @('y')
        $uninstall.ExitCode | Should -Be 0 -Because $uninstall.Text
        $uninstall.Text | Should -Match 'The verification install was removed and your profile restored\.'
        Get-ProfileSnapshot $sandbox | Should -Be $before
    }

    It 'finishes the restore with -RestoreOnly after a run interrupted in the middle' {
        $sandbox = New-Sandbox
        $before = Get-ProfileSnapshot $sandbox
        $run = Invoke-Verify $sandbox @('-Only', 'win.fake-install,win.fake-interrupt,win.fake-uninstall') @('y', 'y')

        $run.ExitCode | Should -Not -Be 0 -Because $run.Text
        # The uninstall after the interruption never ran (nor was it asked).
        Get-Ran $sandbox | Should -Be @('win.fake-install', 'win.fake-interrupt')
        $run.RunFolder | Should -Not -BeNullOrEmpty
        Test-Path -LiteralPath (Join-Path $run.RunFolder 'report.md') | Should -BeFalse
        # Left behind by the interruption: the install state, the journal and the user's folders moved aside.
        Test-Path -LiteralPath (Join-Path $sandbox 'roaming' 'LayerPeek.verify-install-state.json') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $sandbox 'roaming' 'LayerPeek.verify-install-journal.json') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $sandbox 'roaming' 'LayerPeek.aot-smoke-backup' 'settings.json') | Should -BeTrue
        Get-ProfileSnapshot $sandbox | Should -Not -Be $before

        $restore = Invoke-Verify $sandbox @('-RestoreOnly')
        $restore.ExitCode | Should -Be 0 -Because $restore.Text
        $restore.Text | Should -Match 'removed the credential LayerPeek/printer/new'
        $restore.Text | Should -Match ([regex]::Escape('restored %APPDATA%\LayerPeek and %LOCALAPPDATA%\LayerPeek'))
        Get-ProfileSnapshot $sandbox | Should -Be $before
        @(Get-ChildItem -LiteralPath (Join-Path $sandbox 'roaming') -Force).Name | Should -Be @('LayerPeek')

        $again = Invoke-Verify $sandbox @('-RestoreOnly')
        $again.ExitCode | Should -Be 0
        $again.Text | Should -Match 'Nothing to restore\.'
    }
}
