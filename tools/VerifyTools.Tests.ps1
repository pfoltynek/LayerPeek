<#
.SYNOPSIS
    Pester 5+ tests of the verification engine (tools/VerifyTools.psm1) and of the two verification scripts
    tools/macos-verify.ps1 and tools/windows-verify.ps1 (T-105).

.DESCRIPTION
    The engine runs with stand-in checks, a stand-in prompt and a stand-in writer: nothing waits for input. The parsers
    get recorded tool output. The scripts are only started with -ListChecks, with -SelfTest and with an unknown check id,
    which never touch the system or the user profile.

    Temporary files live in one folder under $env:LAYERPEEK_PESTER_TESTS_TEMP (set by the xUnit wrapper
    tests/LayerPeek.Scripts.Tests) or under the system temp folder; AfterAll deletes it.

.EXAMPLE
    Invoke-Pester tools/VerifyTools.Tests.ps1 -Output Detailed
#>
#Requires -Version 7.2

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot 'VerifyTools.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..' 'scripts' 'ReleaseTools' 'ReleaseTools.psm1') -Force

    $base = if ($env:LAYERPEEK_PESTER_TESTS_TEMP) { $env:LAYERPEEK_PESTER_TESTS_TEMP } else { [System.IO.Path]::GetTempPath() }
    $script:TempRoot = Join-Path $base "layerpeek-vt-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    $null = New-Item -ItemType Directory -Path $script:TempRoot

    function New-TestContext([string[]] $Answers = @(), [switch] $Unattended) {
        $queue = [System.Collections.Generic.Queue[string]]::new([string[]] $Answers)
        $asked = [System.Collections.Generic.List[string]]::new()
        $folder = New-VerifyRunFolder -Root (Join-Path $script:TempRoot ([guid]::NewGuid().ToString('N').Substring(0, 8)))
        $context = New-VerifyContext -RunFolder $folder -Unattended:$Unattended `
            -Prompt { param($Question) $asked.Add($Question); if ($queue.Count) { $queue.Dequeue() } else { 'n' } }.GetNewClosure() `
            -Writer { param($Text, $Color) }
        $context | Add-Member -NotePropertyName Asked -NotePropertyValue $asked
        $context
    }

    function Invoke-Script([string] $Name, [string[]] $Arguments) {
        $command = @(Get-PwshCommand)
        $prefix = @($command | Select-Object -Skip 1)
        $output = @(& $command[0] @prefix -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot $Name) @Arguments 2>&1 | ForEach-Object { "$_" })
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output; Text = $output -join "`n" }
    }
}

AfterAll {
    if ($script:TempRoot -and (Test-Path -LiteralPath $script:TempRoot)) { Remove-Item -LiteralPath $script:TempRoot -Recurse -Force }
}

Describe 'Check definitions' {
    It 'refuses an invalid id, a permission check without its permission and a criterion without a task id' {
        { New-VerifyCheck -Id 'Mac.Bad' -Title 't' -Closes 'T-001 x' -Kind Auto -Action { } } | Should -Throw '*Invalid check id*'
        { New-VerifyCheck -Id 'mac.p' -Title 't' -Closes 'T-001 x' -Kind Permission -Action { } } | Should -Throw '*-Permission*'
        { New-VerifyCheck -Id 'mac.c' -Title 't' -Closes 'criterion 1' -Kind Auto -Action { } } | Should -Throw '*task id*'
    }

    It 'finds duplicate ids and unknown tasks, and allows the listed missing ones' {
        $tasks = Join-Path $script:TempRoot 'tasks'
        $null = New-Item -ItemType Directory -Force -Path (Join-Path $tasks 'done')
        [System.IO.File]::WriteAllText((Join-Path $tasks 'done' 'T-001-a.md'), 'x')
        $checks = @(
            New-VerifyCheck -Id 'x.a' -Title 't' -Closes 'T-001 a' -Kind Auto -Action { }
            New-VerifyCheck -Id 'x.a' -Title 't' -Closes 'T-002 b', 'T-003 c' -Kind Auto -Action { }
        )
        $problems = @(Test-VerifyCheckDefinitions -Checks $checks -TasksRoot $tasks -AllowMissingTasks 'T-003')
        $problems | Should -HaveCount 2
        $problems[0] | Should -BeLike '*x.a is used twice*'
        $problems[1] | Should -BeLike '*closes T-002*'
    }

    It 'selects by id and wildcard in definition order, skips, and refuses an unknown pattern' {
        $checks = 'a.one', 'a.two', 'b.one' | ForEach-Object { New-VerifyCheck -Id $_ -Title $_ -Closes 'T-001 x' -Kind Auto -Action { } }
        (Select-VerifyChecks -Checks $checks).Id | Should -Be @('a.one', 'a.two', 'b.one')
        (Select-VerifyChecks -Checks $checks -Only 'b.one,a.*').Id | Should -Be @('a.one', 'a.two', 'b.one')
        (Select-VerifyChecks -Checks $checks -Only 'a.*' -Skip 'a.two').Id | Should -Be @('a.one')
        { Select-VerifyChecks -Checks $checks -Only 'c.one' } | Should -Throw "*No check matches 'c.one'*"
        { Select-VerifyChecks -Checks $checks -Skip 'nope' } | Should -Throw '*nope*'
    }

    It 'lists every check with its kind and criteria' {
        $check = New-VerifyCheck -Id 'a.one' -Title 'title' -Closes 'T-001 kritérium 1', 'T-002 kritérium 3' -Kind Permission -Permission 'sudo' -Action { }
        $lines = @(Format-VerifyCheckList -Checks @($check))
        $lines[0] | Should -Be 'a.one  [permission]  title'
        $lines | Should -Contain '    closes: T-002 kritérium 3'
        $lines | Should -Contain '    needs:  sudo'
    }
}

Describe 'Running checks' {
    It 'never runs or asks a Permission or Manual check in the unattended mode' {
        $ran = [System.Collections.Generic.List[string]]::new()
        $context = New-TestContext -Unattended
        foreach ($kind in 'Permission', 'Manual') {
            $check = New-VerifyCheck -Id "k.$($kind.ToLowerInvariant())" -Title 't' -Closes 'T-001 x' -Kind $kind -Permission 'p' -Action { $ran.Add('x'); New-VerifyResult PASS 'ran' }
            $result = Invoke-VerifyCheck -Check $check -Context $context
            $result.Status | Should -Be 'SKIP'
            $result.Detail | Should -Not -BeNullOrEmpty
        }
        $ran | Should -HaveCount 0
        $context.Asked | Should -HaveCount 0
    }

    It 'asks before a Permission check and skips it when declined' {
        $context = New-TestContext -Answers 'n', 'y'
        $check = New-VerifyCheck -Id 'k.p' -Title 't' -Closes 'T-001 x' -Kind Permission -Permission 'the stand-in' -Action { New-VerifyResult PASS 'ran' }
        (Invoke-VerifyCheck -Check $check -Context $context).Status | Should -Be 'SKIP'
        (Invoke-VerifyCheck -Check $check -Context $context).Status | Should -Be 'PASS'
        $context.Asked[0] | Should -BeLike '*the stand-in*'
    }

    It 'turns an exception into a FAIL with evidence and Skip-VerifyCheck into a SKIP' {
        $context = New-TestContext
        $throwing = New-VerifyCheck -Id 'k.t' -Title 't' -Closes 'T-001 x' -Kind Auto -Action { throw 'boom' }
        $result = Invoke-VerifyCheck -Check $throwing -Context $context
        $result.Status | Should -Be 'FAIL'
        $result.Detail | Should -BeLike '*boom*'
        $result.Evidence | Should -Contain 'evidence/k.t/error.txt'
        $skipping = New-VerifyCheck -Id 'k.s' -Title 't' -Closes 'T-001 x' -Kind Auto -Action { Skip-VerifyCheck 'no .dmg' }
        $result = Invoke-VerifyCheck -Check $skipping -Context $context
        $result.Status | Should -Be 'SKIP'
        $result.Detail | Should -Be 'no .dmg'
    }

    It 'skips an Auto check that needs an answer in the unattended mode' {
        $check = New-VerifyCheck -Id 'k.q' -Title 't' -Closes 'T-001 x' -Kind Auto -Action { param($c) if (Confirm-VerifyStep $c 'seen?') { New-VerifyResult MANUAL 'y' } else { New-VerifyResult FAIL 'n' } }
        (Invoke-VerifyCheck -Check $check -Context (New-TestContext -Unattended)).Status | Should -Be 'SKIP'
        (Invoke-VerifyCheck -Check $check -Context (New-TestContext -Answers 'yes')).Status | Should -Be 'MANUAL'
        (Invoke-VerifyCheck -Check $check -Context (New-TestContext -Answers 'no')).Status | Should -Be 'FAIL'
    }

    It 'fails a check without a result and keeps its other output as evidence' {
        $context = New-TestContext
        $result = Invoke-VerifyCheck -Check (New-VerifyCheck -Id 'k.n' -Title 't' -Closes 'T-001 x' -Kind Auto -Action { 'text' }) -Context $context
        $result.Status | Should -Be 'FAIL'
        Get-Content -LiteralPath (Join-Path $context.RunFolder 'evidence' 'k.n' 'output.txt') | Should -Be 'text'
    }

    It 'runs the cleanup (last first, with its state) before the final checks, also when a step fails' {
        $order = [System.Collections.Generic.List[string]]::new()
        $context = New-TestContext
        $checks = @(
            New-VerifyCheck -Id 'r.final' -Title 't' -Closes 'T-001 x' -Kind Auto -Final -Action { $order.Add('final'); New-VerifyResult PASS 'f' }
            New-VerifyCheck -Id 'r.one' -Title 't' -Closes 'T-001 x' -Kind Auto -Action {
                param($c)
                Add-VerifyCleanup $c 'first' { param($s) $order.Add("cleanup $s") } -State 'A'
                Add-VerifyCleanup $c 'broken' { throw 'cannot' }
                Add-VerifyCleanup $c 'last' { param($s) $order.Add("cleanup $s") } -State 'B'
                $order.Add('one')
                New-VerifyResult PASS 'o'
            }
        )
        $results = Invoke-VerifyRun -Checks $checks -Context $context
        $order | Should -Be @('one', 'cleanup B', 'cleanup A', 'final')
        ($results | Where-Object Id -EQ 'cleanup').Detail | Should -BeLike 'broken: cannot*'
        $results.Id | Should -Be @('r.one', 'cleanup', 'r.final')
        Get-VerifyExitCode $results | Should -Be 1
    }

    It 'has exit code 0 without a FAIL' {
        Get-VerifyExitCode @((New-VerifyResult PASS 'a'), (New-VerifyResult SKIP 'b'), (New-VerifyResult MANUAL 'c')) | Should -Be 0
        Get-VerifyExitCode @() | Should -Be 0
    }

    It 'restores the environment after a tool run and keeps its output as evidence' {
        $context = New-TestContext
        $context.CheckId = 'k.tool'
        [Environment]::SetEnvironmentVariable('LAYERPEEK_VT_PROBE', 'before')
        try {
            $pwsh = @(Get-PwshCommand)
            $result = Invoke-VerifyTool -Context $context -Label 'probe' -FilePath $pwsh[0] -ArgumentList (@($pwsh | Select-Object -Skip 1) + @('-NoProfile', '-Command', 'Write-Output $env:LAYERPEEK_VT_PROBE; exit 3')) -Environment @{ LAYERPEEK_VT_PROBE = 'inside' }
            $result.ExitCode | Should -Be 3
            $result.Output | Should -Contain 'inside'
            $env:LAYERPEEK_VT_PROBE | Should -Be 'before'
            Get-Content -LiteralPath (Join-Path $context.RunFolder 'evidence' 'k.tool' 'probe.txt') | Should -Contain '> exit code 3'
        }
        finally { [Environment]::SetEnvironmentVariable('LAYERPEEK_VT_PROBE', $null) }
    }
}

Describe 'Report' {
    It 'writes Markdown and JSON with the run info, every result and links to the evidence' {
        $context = New-TestContext
        $check = New-VerifyCheck -Id 'rep.one' -Title 'one' -Closes 'T-021 kritérium 1' -Kind Auto -Action { param($c) $null = Save-VerifyEvidence -Context $c -Name 'shot one.png' -Content 'x'; New-VerifyResult PASS 'a | b' }
        $results = @(Invoke-VerifyCheck -Check $check -Context $context)
        $info = Get-VerifyRunInfo -RepoRoot (Join-Path $PSScriptRoot '..') -Script 'tools/x.ps1' -Unattended -Selected 'rep.one' -Git { param($Arguments) switch ($Arguments[0]) { 'rev-parse' { if ($Arguments[1] -eq 'HEAD') { 'abc123' } else { 'feature/x' } } 'status' { ' M file' } } }
        $info.Commit | Should -Be 'abc123'
        $info.Dirty | Should -BeTrue
        $info.AppVersion | Should -Match '^\d+\.\d+\.\d+$'
        $report = Write-VerifyReport -Folder $context.RunFolder -Info $info -Results $results
        $json = Get-Content -LiteralPath $report.Json -Raw | ConvertFrom-Json
        $json.Run.Mode | Should -Be 'unattended'
        $json.Summary.PASS | Should -Be 1
        $json.Results[0].Closes | Should -Be @('T-021 kritérium 1')
        $markdown = Get-Content -LiteralPath $report.Markdown -Raw
        $markdown | Should -Match 'abc123'
        $markdown | Should -Match ([regex]::Escape('a \| b'))
        $markdown | Should -Match ([regex]::Escape('![evidence/rep.one/shot one.png](evidence/rep.one/shot%20one.png)'))
    }
}

Describe 'Parsers' {
    It 'sums the per-assembly totals of dotnet test in any UI language' {
        $czech = @('x.dll (net11.0|arm64) úspěch [+14/x0/?11] (4s 477ms)', 'Souhrn testovacího běhu: Úspěšné!', '  celkem: 25')
        $totals = Get-VerifyTestTotals $czech
        $totals.Total | Should -Be 25
        $totals.Skipped | Should -Be 11
        $english = @('a.dll (net11.0|arm64) passed [+3/x0/?0] (1s)', 'b.dll (net11.0|arm64) failed [+5/x2/?1] (2s)')
        $totals = Get-VerifyTestTotals $english
        $totals.Passed | Should -Be 8
        $totals.Failed | Should -Be 2
        $totals.Assemblies | Should -Be 2
        Get-VerifyTestTotals @('error CS1002: ; expected') | Should -BeNullOrEmpty
    }

    It 'sums the passes of tools/run-e2e.ps1' {
        $output = @('Debug exe: green - 12 passed, 3 skipped, 0 failed (of 15)', 'Native AOT exe: FAILED (exit code 1) - 10 passed, 1 skipped, 2 failed, 1 not run (of 14)')
        $totals = Get-VerifyRunE2ETotals $output
        $totals.Passed | Should -Be 22
        $totals.Failed | Should -Be 2
        $totals.Total | Should -Be 29
        Get-VerifyRunE2ETotals @('Debug exe: not run') | Should -BeNullOrEmpty
    }

    It 'reads the skipped tests and their reasons' {
        $output = @('skipped LayerPeek.App.Tests.KeychainSecretStoreTests.RoundTrip', '  The default (login) Keychain is locked.', '  from x.dll', 'passed Other')
        $skipped = @(Get-VerifySkippedTests $output)
        $skipped | Should -HaveCount 1
        $skipped[0].Name | Should -Be 'LayerPeek.App.Tests.KeychainSecretStoreTests.RoundTrip'
        $skipped[0].Reason | Should -Be 'The default (login) Keychain is locked.'
    }

    It 'finds MSBuild warnings once each' {
        $output = @('/a/b.cs(1,2): warning CS8602: Dereference [x.csproj]', '/a/b.cs(1,2): warning CS8602: Dereference [x.csproj]', '    0 Warning(s)', 'Build succeeded.')
        @(Get-VerifyBuildWarnings $output) | Should -HaveCount 1
        @(Get-VerifyBuildWarnings @('    0 Warning(s)')) | Should -HaveCount 0
    }

    It 'reads the notification status, lsappinfo, spctl and cmdkey output' {
        (Get-VerifyNotificationStatus @('noise', 'permission=Allowed delivered=2')).Delivered | Should -Be 2
        Get-VerifyNotificationStatus @('permission=') | Should -BeNullOrEmpty
        Get-VerifyFrontAsn @('ASN:0x0-0x1d01d:') | Should -Be 'ASN:0x0-0x1d01d:'
        Get-VerifyLsappinfoPid @('"pid"=1234') | Should -Be 1234
        $rejected = Get-VerifySpctlAssessment -ExitCode 3 -Output @('/tmp/LayerPeek.app: rejected', 'source=no usable signature')
        $rejected.Accepted | Should -BeFalse
        $rejected.Verdict | Should -Be 'rejected'
        $rejected.Source | Should -Be 'no usable signature'
        (Get-VerifySpctlAssessment -ExitCode 0 -Output @('/x.app: accepted', 'source=Notarized Developer ID')).Accepted | Should -BeTrue
        Get-VerifyCredentialTargets @('    Target: LegacyGeneric:target=LayerPeek/printer/abc', '    Ziel: LegacyGeneric:target=Other', 'Target: LegacyGeneric:target=LayerPeek/printer/abc') | Should -Be @('LayerPeek/printer/abc')
    }
}

Describe 'macOS helpers (pure)' {
    It 'gives the test bundle its own identity without an icon' {
        $template = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..' 'build' 'macos' 'Info.plist') -Raw
        $id = Get-MacBundleIdentifier (Join-Path $PSScriptRoot '..' 'build' 'macos' 'Info.plist')
        $plist = ConvertTo-VerifyTestInfoPlist -ProductionText $template -ProductionIdentifier $id
        $entries = Get-PlistEntries -Text $plist
        $entries['CFBundleIdentifier'].InnerText | Should -Be "$id.E2E"
        $entries['CFBundleName'].InnerText | Should -Be 'LayerPeek E2E'
        $entries.Contains('CFBundleIconFile') | Should -BeFalse
        $entries['LSUIElement'].Name | Should -Be 'true'
        $plist | Should -Not -Match '@VERSION@'
    }

    It 'puts the instance socket into the data folder, or into the private temp folder when the path is too long' {
        Get-VerifyInstanceSocketPath -DataDirectory '/tmp/lpv-1' -Suffix 'v1' -TempDirectory '/var/t' -UserId 501 | Should -Be (Join-Path '/tmp/lpv-1' 'instance-v1.sock')
        $long = '/tmp/' + ('d' * 100)
        Get-VerifyInstanceSocketPath -DataDirectory $long -Suffix 'v1' -TempDirectory '/var/t' -UserId 501 | Should -Be (Join-Path '/var/t' 'LayerPeek-501' 'instance-v1.sock')
    }

    It 'refuses a malformed instance command and reports an unreachable socket' {
        { Send-VerifyInstanceCommand -SocketPath (Join-Path $script:TempRoot 'none.sock') -Command 'bad command' } | Should -Throw '*Not an instance command*'
        Send-VerifyInstanceCommand -SocketPath (Join-Path $script:TempRoot 'none.sock') -Command 'quit' -TimeoutMilliseconds 500 | Should -BeFalse
    }

    It 'checks the corner placement 16 pt from the usable area' {
        $area = [pscustomobject]@{ X = 0; Y = 30; Width = 1728; Height = 963 }
        $win = { param($x, $y) [pscustomobject]@{ X = $x; Y = $y; Width = 480; Height = 300 } }
        @(Get-VerifyCornerProblems -Window (& $win 16 46) -Area $area -Corner TopLeft) | Should -HaveCount 0
        @(Get-VerifyCornerProblems -Window (& $win (1728 - 16 - 480) 46) -Area $area -Corner TopRight) | Should -HaveCount 0
        @(Get-VerifyCornerProblems -Window (& $win 16 (993 - 16 - 300)) -Area $area -Corner BottomLeft) | Should -HaveCount 0
        @(Get-VerifyCornerProblems -Window (& $win (1728 - 16 - 480) (993 - 16 - 300)) -Area $area -Corner BottomRight) | Should -HaveCount 0
        @(Get-VerifyCornerProblems -Window (& $win 624 361.5) -Area $area -Corner Center) | Should -HaveCount 0
        # Under the menu bar: outside the usable area and the wrong distance.
        $problems = @(Get-VerifyCornerProblems -Window (& $win 16 10) -Area $area -Corner TopLeft)
        $problems | Should -HaveCount 2
        $problems[0] | Should -BeLike '*not inside the usable area*'
        @(Get-VerifyCornerProblems -Window (& $win 40 46) -Area $area -Corner TopLeft) | Should -BeLike 'left distance is 40*'
    }

    It 'tells the menu bar item from normal windows' {
        $windows = @(
            [pscustomobject]@{ layer = 25; onscreen = $true; width = 22; height = 24 }
            [pscustomobject]@{ layer = 0; onscreen = $true; width = 600; height = 400 }
            [pscustomobject]@{ layer = 0; onscreen = $false; width = 900; height = 700 }
        )
        Test-VerifyMenuBarItem $windows | Should -BeTrue
        (Get-VerifyMainWindow $windows).width | Should -Be 600
        Test-VerifyMenuBarItem @($windows[1]) | Should -BeFalse
        Get-VerifyMainWindow @($windows[0]) | Should -BeNullOrEmpty
    }
}

Describe 'Self-test of the engine' {
    It 'passes and leaves nothing behind' {
        $root = Join-Path $script:TempRoot 'self'
        $null = New-Item -ItemType Directory -Path $root
        @(Invoke-VerifySelfTest -TempRoot $root) | Should -HaveCount 0
        @(Get-ChildItem -LiteralPath $root -Force) | Should -HaveCount 0
    }
}

Describe 'The verification scripts' {
    It '<Name> -Unattended -ListChecks lists checks of its OS with the criteria they close' -ForEach @(
        @{ Name = 'macos-verify.ps1'; Prefix = 'mac.' }
        @{ Name = 'windows-verify.ps1'; Prefix = 'win.' }
    ) {
        $run = Invoke-Script $Name @('-Unattended', '-ListChecks')
        $run.ExitCode | Should -Be 0 -Because $run.Text
        $ids = @($run.Output | Where-Object { $_ -match '^\S+  \[(auto|permission|manual)\]' } | ForEach-Object { ($_ -split '  ')[0] })
        $ids.Count | Should -BeGreaterThan 5
        $ids | ForEach-Object { $_ | Should -BeLike "$Prefix*" }
        @($run.Output | Where-Object { $_ -match '^    closes: T-\d{3}' }).Count | Should -BeGreaterOrEqual $ids.Count
    }

    It '<Name> -Unattended -SelfTest passes' -ForEach @(
        @{ Name = 'macos-verify.ps1' }
        @{ Name = 'windows-verify.ps1' }
    ) {
        $run = Invoke-Script $Name @('-Unattended', '-SelfTest')
        $run.ExitCode | Should -Be 0 -Because $run.Text
        $run.Text | Should -Match 'SELF-TEST PASSED'
    }

    It 'macos-verify.ps1 refuses an unknown check id before it creates anything' -Skip:(-not $IsMacOS) {
        $out = Join-Path $script:TempRoot 'out-mac'
        $run = Invoke-Script 'macos-verify.ps1' @('-Unattended', '-Only', 'mac.nothing', '-OutputRoot', $out)
        $run.ExitCode | Should -Be 2
        $run.Text | Should -Match "No check matches 'mac.nothing'"
        Test-Path -LiteralPath $out | Should -BeFalse
    }

    It 'windows-verify.ps1 refuses an unknown check id before it creates anything' -Skip:(-not $IsWindows) {
        $out = Join-Path $script:TempRoot 'out-win'
        $run = Invoke-Script 'windows-verify.ps1' @('-Unattended', '-Only', 'win.nothing', '-OutputRoot', $out)
        $run.ExitCode | Should -Be 2
        Test-Path -LiteralPath $out | Should -BeFalse
    }

    It 'each script refuses to run its checks on the other OS' {
        $other = if ($IsWindows) { 'macos-verify.ps1' } else { 'windows-verify.ps1' }
        $run = Invoke-Script $other @('-Unattended', '-OutputRoot', (Join-Path $script:TempRoot 'out-other'))
        $run.ExitCode | Should -Be 2
        $run.Text | Should -Match 'runs on (macOS|Windows) only'
    }
}
