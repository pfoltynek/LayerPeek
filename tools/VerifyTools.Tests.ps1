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

    function New-TestContext([string[]] $Answers = @(), [switch] $Unattended, [string[]] $Allow = @()) {
        $queue = [System.Collections.Generic.Queue[string]]::new([string[]] $Answers)
        $asked = [System.Collections.Generic.List[string]]::new()
        $folder = New-VerifyRunFolder -Root (Join-Path $script:TempRoot ([guid]::NewGuid().ToString('N').Substring(0, 8)))
        $context = New-VerifyContext -RunFolder $folder -Unattended:$Unattended -Allow $Allow `
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

    It 'runs a Permission check allowed in advance without asking, also unattended, and nothing else (T-140)' {
        $permission = New-VerifyCheck -Id 'mac.keys' -Title 't' -Closes 'T-001 x' -Kind Permission -Permission 'p' -Action { New-VerifyResult PASS 'ran' }
        $other = New-VerifyCheck -Id 'mac.sudo' -Title 't' -Closes 'T-001 x' -Kind Permission -Permission 'p' -Action { New-VerifyResult PASS 'ran' }
        $manual = New-VerifyCheck -Id 'mac.look' -Title 't' -Closes 'T-001 x' -Kind Manual -Action { New-VerifyResult MANUAL 'ran' }
        foreach ($context in (New-TestContext -Unattended -Allow 'mac.k*', 'mac.look'), (New-TestContext -Allow 'mac.keys')) {
            (Invoke-VerifyCheck -Check $permission -Context $context).Status | Should -Be 'PASS'
            $context.Asked | Should -HaveCount 0
        }
        $context = New-TestContext -Unattended -Allow 'mac.k*', 'mac.look'
        (Invoke-VerifyCheck -Check $other -Context $context).Status | Should -Be 'SKIP'
        (Invoke-VerifyCheck -Check $manual -Context $context).Status | Should -Be 'SKIP'
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
        # A real run has no stand-ins: no field, no line.
        $info.Contains('StandIns') | Should -BeFalse
        $json.Run.PSObject.Properties['StandIns'] | Should -BeNullOrEmpty
        $markdown | Should -Not -Match 'Stand-ins'
    }

    It 'names the stand-in file of a test run in both reports' {
        $context = New-TestContext
        $results = @(Invoke-VerifyCheck -Check (New-VerifyCheck -Id 'rep.two' -Title 'two' -Closes 'T-129 x' -Kind Auto -Action { New-VerifyResult PASS 'ok' }) -Context $context)
        $standIns = Join-Path $script:TempRoot 'standins.ps1'
        $info = Get-VerifyRunInfo -RepoRoot (Join-Path $PSScriptRoot '..') -Script 'tools/x.ps1' -StandIns $standIns -Git { param($Arguments) $null }
        $report = Write-VerifyReport -Folder $context.RunFolder -Info $info -Results $results
        (Get-Content -LiteralPath $report.Json -Raw | ConvertFrom-Json).Run.StandIns | Should -Be $standIns
        Get-Content -LiteralPath $report.Markdown -Raw | Should -Match ([regex]::Escape("- **Stand-ins:** ``$standIns`` (test run with stand-in checks, not a real verification)"))
    }

    It 'loads a stand-in file only from inside an allowed root' {
        $root = Join-Path $script:TempRoot 'allowed'
        $inside = Join-Path $root 'sub' 'standins.ps1'
        Resolve-VerifyStandInsPath -Path $inside -AllowedRoots @($null, '', $root) -BasePath $script:TempRoot | Should -Be $inside
        Resolve-VerifyStandInsPath -Path (Join-Path 'allowed' 'standins.ps1') -AllowedRoots $root -BasePath $script:TempRoot | Should -Be (Join-Path $root 'standins.ps1')
        Resolve-VerifyStandInsPath -Path $inside -AllowedRoots "$root$([System.IO.Path]::DirectorySeparatorChar)" -BasePath $script:TempRoot | Should -Be $inside
        # A sibling folder with the same prefix, a way out through .., no root at all.
        Resolve-VerifyStandInsPath -Path (Join-Path "$root-2" 'standins.ps1') -AllowedRoots $root -BasePath $script:TempRoot | Should -BeNullOrEmpty
        Resolve-VerifyStandInsPath -Path (Join-Path $root '..' 'standins.ps1') -AllowedRoots $root -BasePath $script:TempRoot | Should -BeNullOrEmpty
        Resolve-VerifyStandInsPath -Path $inside -AllowedRoots @() -BasePath $script:TempRoot | Should -BeNullOrEmpty
    }
}

Describe 'Presets and preconditions (T-208)' {
    BeforeAll {
        $script:PresetChecks = @(
            New-VerifyCheck -Id 'pre.one' -Title 'one' -Closes 'T-208 a' -Kind Auto -Action { New-VerifyResult PASS 'one' }
            New-VerifyCheck -Id 'pre.ask' -Title 'ask' -Closes 'T-208 b' -Kind Permission -Permission 'stand-in' -Needs Session -Action { New-VerifyResult PASS 'ask' }
            New-VerifyCheck -Id 'pre.human' -Title 'human' -Closes 'T-208 c' -Kind Manual -Action { New-VerifyResult MANUAL 'human' }
            New-VerifyCheck -Id 'pre.two' -Title 'two' -Closes 'T-208 d' -Kind Auto -Needs Session, Accessibility -Action { New-VerifyResult PASS 'two' }
        )
    }

    It 'validates a preset: known ids, no Manual check, no id twice, criteria filled' {
        @(Test-VerifyPreset -Checks $script:PresetChecks -Name 'ok' -Ids 'pre.two', 'pre.one', 'pre.ask') | Should -HaveCount 0
        $problems = @(Test-VerifyPreset -Checks $script:PresetChecks -Name 'bad' -Ids 'pre.one', 'pre.one', 'pre.human', 'pre.nothing')
        $problems | Should -HaveCount 3
        $problems -join "`n" | Should -Match 'lists pre.one twice'
        $problems -join "`n" | Should -Match 'the Manual check pre.human'
        $problems -join "`n" | Should -Match 'pre.nothing, which is no check'
        @(Test-VerifyPreset -Checks $script:PresetChecks -Name 'empty' -Ids @()) | Should -Match 'lists no check'
        $noCriteria = New-VerifyCheck -Id 'pre.bare' -Title 'bare' -Closes 'T-208 x' -Kind Auto -Action { }
        $noCriteria.Closes = [string[]] @()
        @(Test-VerifyPreset -Checks @($noCriteria) -Name 'bare' -Ids 'pre.bare') | Should -Match 'closes no criterion'
    }

    It 'selects a preset in definition order, honours -Skip and refuses a broken preset' {
        (@(Select-VerifyPreset -Checks $script:PresetChecks -Name 'p' -Ids 'pre.two', 'pre.one').Id -join ',') | Should -Be 'pre.one,pre.two'
        (@(Select-VerifyPreset -Checks $script:PresetChecks -Name 'p' -Ids 'pre.two', 'pre.one' -Skip 'pre.one').Id -join ',') | Should -Be 'pre.two'
        { Select-VerifyPreset -Checks $script:PresetChecks -Name 'p' -Ids 'pre.human' } | Should -Throw '*Manual check pre.human*'
        { Select-VerifyPreset -Checks $script:PresetChecks -Name 'p' -Ids 'pre.one' -Skip 'pre.two' } | Should -Throw "*No check matches 'pre.two'*"
    }

    It 'refuses a precondition name that is not a word starting upper case' {
        { New-VerifyCheck -Id 'pre.x' -Title 'x' -Closes 'T-208 x' -Kind Auto -Needs 'screen locked' -Action { } } | Should -Throw '*not a precondition name*'
        (Get-VerifyNeeds $script:PresetChecks) -join ',' | Should -Be 'Accessibility,Session'
    }

    It 'skips a check whose precondition is not met with the reason, without running or asking, also an allowed Permission check' {
        $context = New-TestContext -Answers 'y', 'y' -Allow 'pre.ask'
        $context.Unmet['Session'] = 'the screen is locked'
        $ran = [System.Collections.Generic.List[string]]::new()
        $check = New-VerifyCheck -Id 'pre.run' -Title 'run' -Closes 'T-208 e' -Kind Auto -Needs Session -Action { $ran.Add('ran'); New-VerifyResult FAIL 'ran' }.GetNewClosure()
        $results = Invoke-VerifyRun -Checks @($script:PresetChecks[1], $check, $script:PresetChecks[0]) -Context $context
        ($results | ForEach-Object Status) -join ',' | Should -Be 'SKIP,SKIP,PASS'
        $results[0].Detail | Should -Be 'precondition not met at the start of the run: Session (the screen is locked).'
        $ran | Should -HaveCount 0
        $context.Asked | Should -HaveCount 0
    }

    It 'names every unmet precondition once with the checks it skips, and none without checks' {
        $lines = @(Format-VerifyUnmetNeeds -Checks $script:PresetChecks -Unmet ([ordered]@{ Session = 'locked'; Accessibility = 'no permission'; Unused = 'nothing needs it' }))
        $lines | Should -Be @(
            'Not met: Accessibility - no permission'
            '    SKIP: pre.two'
            'Not met: Session - locked'
            '    SKIP: pre.ask, pre.two'
        )
        @(Format-VerifyUnmetNeeds -Checks $script:PresetChecks -Unmet @{}) | Should -HaveCount 0
        Get-VerifyUnmetReason -Check $script:PresetChecks[0] -Unmet ([ordered]@{ Session = 'locked' }) | Should -BeNullOrEmpty
    }
}

Describe 'Report of a preset run (T-208)' {
    It 'writes report.json in its documented shape with the run header' {
        $context = New-TestContext -Unattended
        $context.Unmet['Session'] = 'the screen is locked'
        $checks = @(
            New-VerifyCheck -Id 'rep.pass' -Title 'passes' -Closes 'T-208 a', 'T-127 b' -Kind Auto -Action { New-VerifyResult PASS 'fine' }
            New-VerifyCheck -Id 'rep.locked' -Title 'locked' -Closes 'T-208 c' -Kind Auto -Needs Session -Action { New-VerifyResult PASS 'never' }
        )
        $started = [DateTimeOffset]::new(2026, 11, 13, 20, 0, 0, [TimeSpan]::FromHours(1))
        $results = Invoke-VerifyRun -Checks $checks -Context $context
        $git = { param($Arguments) switch ($Arguments[0]) { 'rev-parse' { if ($Arguments[1] -eq 'HEAD') { '0123abc' } else { 'feature/T-208' } } 'status' { @() } } }
        $info = Get-VerifyRunInfo -RepoRoot (Join-Path $PSScriptRoot '..') -Script 'tools/macos-verify.ps1' -Unattended -Selected $checks.Id -Allow 'rep.none' -Preset 'release' `
            -Preconditions ([ordered]@{ Awake = 'met'; Session = 'not met: the screen is locked' }) -Started $started -Git $git
        $info.Seconds = 1830
        $report = Write-VerifyReport -Folder $context.RunFolder -Info $info -Results $results
        $json = Get-Content -LiteralPath $report.Json -Raw | ConvertFrom-Json

        @($json.PSObject.Properties.Name) | Should -Be @('Format', 'Run', 'Summary', 'Results')
        $json.Format | Should -Be 1
        foreach ($field in 'Script', 'Started', 'Commit', 'Branch', 'Dirty', 'AppVersion', 'Os', 'Machine', 'PowerShell', 'Mode', 'Preset', 'Selected', 'Allowed', 'Preconditions', 'Seconds') {
            $json.Run.PSObject.Properties.Name | Should -Contain $field
        }
        $json.Run.Commit | Should -Be '0123abc'
        $json.Run.Dirty | Should -BeFalse
        $json.Run.Preset | Should -Be 'release'
        $json.Run.Mode | Should -Be 'unattended'
        $json.Run.Seconds | Should -Be 1830
        $json.Run.Preconditions.Session | Should -Be 'not met: the screen is locked'
        ([DateTimeOffset]::Parse($json.Run.Started)) | Should -Be $started
        if ($IsMacOS) {
            $json.Run.Os | Should -Match '^macOS \d+(\.\d+)* \S+ \('
            $json.Run.Machine | Should -Not -Be 'unknown'
        }
        @($json.Summary.PSObject.Properties.Name) | Should -Be @('PASS', 'FAIL', 'SKIP', 'MANUAL')
        $json.Summary.SKIP | Should -Be 1
        $json.Results | Should -HaveCount 2
        foreach ($entry in $json.Results) { @($entry.PSObject.Properties.Name) | Should -Be @('Id', 'Title', 'Kind', 'Closes', 'Status', 'Detail', 'Seconds', 'Evidence') }
        $json.Results[0].Closes | Should -Be @('T-208 a', 'T-127 b')
        $json.Results[1].Status | Should -Be 'SKIP'
        $json.Results[1].Detail | Should -Match 'Session \(the screen is locked\)'
        $json.Results[1].Seconds | Should -BeOfType [double]

        $markdown = Get-Content -LiteralPath $report.Markdown -Raw
        foreach ($line in '- Duration: 30 min 30 s', '- Commit: `0123abc` on `feature/T-208`', '- Clean tree: yes', '- Preset: release', '- Mode: unattended', '- Preconditions: Awake met; Session not met: the screen is locked') {
            $markdown | Should -Match ([regex]::Escape($line))
        }
        $markdown | Should -Match '- Machine: '
        $markdown | Should -Match ([regex]::Escape('| `rep.locked` | Auto | **SKIP** |'))
    }

    It 'says "Clean tree: no" with local changes and "Preset: none" without a preset' {
        $context = New-TestContext
        $results = @(Invoke-VerifyCheck -Check (New-VerifyCheck -Id 'rep.x' -Title 'x' -Closes 'T-208 x' -Kind Auto -Action { New-VerifyResult PASS 'x' }) -Context $context)
        $info = Get-VerifyRunInfo -RepoRoot (Join-Path $PSScriptRoot '..') -Script 'tools/x.ps1' -Git { param($Arguments) if ($Arguments[0] -eq 'status') { '?? new.txt' } }
        $report = Write-VerifyReport -Folder $context.RunFolder -Info $info -Results $results
        $markdown = Get-Content -LiteralPath $report.Markdown -Raw
        $markdown | Should -Match '- Clean tree: no'
        $markdown | Should -Match '- Preset: none'
        (Get-Content -LiteralPath $report.Json -Raw | ConvertFrom-Json).Run.Preset | Should -BeNullOrEmpty
    }

    It 'writes the home folder as ~ in the report, but not a longer folder name' {
        $homePath = [Environment]::GetFolderPath('UserProfile')
        $sep = [System.IO.Path]::DirectorySeparatorChar
        Hide-VerifyHomePath "at $homePath${sep}Movies and $homePath" | Should -Be "at ~${sep}Movies and ~"
        Hide-VerifyHomePath "${homePath}2${sep}x" | Should -Be "${homePath}2${sep}x"
        Hide-VerifyHomePath "/Users/me/a, '/Users/me'" -HomePath '/Users/me/' | Should -Be "~/a, '~'"
        $nested = Hide-VerifyHomePath ([ordered]@{ A = @('/Users/me/x', 3); B = [ordered]@{ C = '/Users/me' }; D = $true }) -HomePath '/Users/me'
        $nested.A | Should -Be @('~/x', 3)
        $nested.B.C | Should -Be '~'
        $nested.D | Should -BeTrue

        $context = New-TestContext
        $results = @(Invoke-VerifyCheck -Check (New-VerifyCheck -Id 'rep.home' -Title "in $homePath" -Closes 'T-208 x' -Kind Auto -Action { New-VerifyResult PASS "file $([System.IO.Path]::Combine([Environment]::GetFolderPath('UserProfile'), 'Movies', 'a.mp4'))" }) -Context $context)
        $info = Get-VerifyRunInfo -RepoRoot (Join-Path $PSScriptRoot '..') -Script 'tools/x.ps1' -StandIns (Join-Path $homePath 'standins.ps1') -Git { param($Arguments) $null }
        $report = Write-VerifyReport -Folder $context.RunFolder -Info $info -Results $results
        foreach ($file in $report.Markdown, $report.Json) {
            $text = Get-Content -LiteralPath $file -Raw
            $text | Should -Not -Match ([regex]::Escape($homePath) + '(?=[/\\])')
            $text | Should -Match ([regex]::Escape("~${sep}Movies"))
        }
        $results[0].Detail | Should -Match ([regex]::Escape($homePath)) -Because 'the results of the run stay unchanged'
    }

    It 'points latest at the last run folder and never replaces a real folder' -Skip:$IsWindows {
        $root = Join-Path $script:TempRoot 'latest-root'
        $first = New-VerifyRunFolder -Root $root -Now ([DateTime]::new(2026, 11, 13, 20, 0, 0))
        $second = New-VerifyRunFolder -Root $root -Now ([DateTime]::new(2026, 11, 13, 21, 0, 0))
        $link = Set-VerifyLatestLink -Root $root -RunFolder $first
        $link | Should -Be (Join-Path $root 'latest')
        (Get-Item -LiteralPath $link).Target | Should -Be (Split-Path -Leaf $first)
        Set-VerifyLatestLink -Root $root -RunFolder $second | Should -Be $link
        (Get-Item -LiteralPath $link).Target | Should -Be (Split-Path -Leaf $second)
        Test-Path -LiteralPath $first | Should -BeTrue -Because 'replacing the link keeps the older run'
        Test-Path -LiteralPath (Join-Path $link 'report.md') | Should -BeFalse
        $null = New-Item -ItemType File -Path (Join-Path $second 'report.md')
        Test-Path -LiteralPath (Join-Path $link 'report.md') | Should -BeTrue

        $other = Join-Path $script:TempRoot 'latest-real'
        $null = New-Item -ItemType Directory -Path (Join-Path $other 'latest')
        Set-VerifyLatestLink -Root $other -RunFolder (Join-Path $other 'run') | Should -BeNullOrEmpty
        (Get-Item -LiteralPath (Join-Path $other 'latest')).LinkType | Should -BeNullOrEmpty
    }

    It 'formats a duration' {
        Format-VerifyDuration 42.4 | Should -Be '42 s'
        Format-VerifyDuration 1805 | Should -Be '30 min 5 s'
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

    It 'counts an assembly once although the summary of a solution run repeats its line (T-208)' {
        $solution = @(
            '/r/a/bin/A.Tests.dll (net11.0|arm64) failed with 1 error(s) [+3342/x1/?8] (42s)'
            '/r/b/bin/B.Tests.dll (net11.0|arm64) passed [+10/x0/?2] (3s)'
            ''
            'Test run summary: Failed!'
            '  /r/b/bin/B.Tests.dll (net11.0|arm64) passed [+10/x0/?2] (3s)'
            '  /r/a/bin/A.Tests.dll (net11.0|arm64) failed with 1 error(s) [+3342/x1/?8] (42s)'
            '  total: 3363'
        )
        $totals = Get-VerifyTestTotals $solution
        $totals.Total | Should -Be 3363
        $totals.Failed | Should -Be 1
        $totals.Assemblies | Should -Be 2
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

    It 'writes evidence as a JSON array also for none or one item' {
        (ConvertTo-VerifyJsonArray @() -replace '\s', '') | Should -Be '[]'
        (ConvertTo-VerifyJsonArray $null -replace '\s', '') | Should -Be '[]'
        $one = ConvertTo-VerifyJsonArray @([pscustomobject]@{ layer = 0; width = 960 })
        ($one -replace '\s', '') | Should -Be '[{"layer":0,"width":960}]'
        $list = [System.Collections.Generic.List[object]]::new()
        $list.Add([pscustomobject]@{ a = 1 }); $list.Add([pscustomobject]@{ a = 2 })
        ((ConvertTo-VerifyJsonArray $list) -replace '\s', '') | Should -Be '[{"a":1},{"a":2}]'
    }

    It 'tells a locked screen or a session off the console from a normal session' {
        Get-VerifySessionLimitation ([pscustomobject]@{ known = $true; locked = $false; onConsole = $true }) | Should -BeNullOrEmpty
        Get-VerifySessionLimitation ([pscustomobject]@{ known = $true; locked = $true; onConsole = $true }) | Should -BeLike '*screen is locked*'
        Get-VerifySessionLimitation ([pscustomobject]@{ known = $true; locked = $false; onConsole = $false }) | Should -BeLike '*not on the console*'
        # Unknown session (no dictionary, helper failed): no limitation claimed, the check stays a FAIL.
        Get-VerifySessionLimitation ([pscustomobject]@{ known = $false; locked = $false; onConsole = $false }) | Should -BeNullOrEmpty
        Get-VerifySessionLimitation $null | Should -BeNullOrEmpty
        $parsed = '{ "known" : true, "locked" : true, "onConsole" : true }' | ConvertFrom-Json
        Get-VerifySessionLimitation $parsed | Should -Not -BeNullOrEmpty
    }

    It 'turns only a FAIL in a limited session into a SKIP that keeps the failure text' {
        $fail = New-VerifyResult FAIL 'no status item'
        $skipped = Resolve-VerifySessionResult -Result $fail -Limitations @($null, 'the screen is locked')
        $skipped.Status | Should -Be 'SKIP'
        $skipped.Detail | Should -BeLike '*the screen is locked*'
        $skipped.Detail | Should -BeLike '*Failed here: no status item'
        (Resolve-VerifySessionResult -Result $fail -Limitations @($null, $null)).Status | Should -Be 'FAIL'
        (Resolve-VerifySessionResult -Result $fail -Limitations @()).Status | Should -Be 'FAIL'
        (Resolve-VerifySessionResult -Result (New-VerifyResult PASS 'ok') -Limitations @('the screen is locked')).Status | Should -Be 'PASS'
    }

    It 'names the owners of the status item windows of the menu bar' {
        $menuBar = @(
            [pscustomobject]@{ layer = 25; owner = 'ControlCenter'; pid = 527 }
            [pscustomobject]@{ layer = 25; owner = 'ControlCenter'; pid = 527 }
            [pscustomobject]@{ layer = 25; owner = 'BetterDisplay'; pid = 2359 }
            [pscustomobject]@{ layer = 24; owner = 'MenuBarAgent'; pid = 480 }
        )
        Get-VerifyMenuBarOwners $menuBar | Should -Be @('BetterDisplay (2359) x 1', 'ControlCenter (527) x 2')
        @(Get-VerifyMenuBarOwners @($menuBar[3])) | Should -HaveCount 0
    }

    It 'finds the window a click reaches, past the overlays above the menu bar' {
        $windows = '[{"pid":991,"owner":"BetterDisplay","layer":2147483629},{"pid":42,"owner":"LayerPeek","layer":0,"number":7},{"pid":1,"owner":"Finder","layer":0}]' | ConvertFrom-Json
        (Get-VerifyTopWindow $windows).pid | Should -Be 42
        Get-VerifyTopWindow @($windows[0]) | Should -BeNullOrEmpty
    }

    Context 'the result of "Check now" (mac.update-check)' {
        BeforeAll {
            $script:Expected = 'Version 9.9.0 is available'
            function New-Watch([string[]] $Lines) { @(ConvertFrom-VerifyAxWatch $Lines) }
            $script:Hidden = '{"ms":0,"found":false,"text":"","frame":{}}'
            $script:Checking = '{"ms":420,"found":true,"text":"Checking…","frame":{"x":300,"y":400,"width":70,"height":16}}'
            $script:Shown = '{"ms":1990,"found":true,"text":"Version 9.9.0 is available; the tray menu offers it.","frame":{"x":300,"y":400,"width":290,"height":16}}'
        }

        It 'reads the watch lines and skips anything else' {
            $timeline = New-Watch @($script:Hidden, 'macos-verify-helper: something', '', $script:Checking)
            $timeline | Should -HaveCount 2
            $timeline[1].text | Should -Be 'Checking…'
            $timeline[1].frame.width | Should -Be 70
        }

        It 'passes when the text appears after the press and stays' {
            $verdict = Get-VerifyUpdateCheckVerdict -Timeline (New-Watch @($script:Hidden, $script:Checking, $script:Shown)) -Requests 1 -Expected $script:Expected
            $verdict.Passed | Should -BeTrue
            $verdict.Shown.ms | Should -Be 1990
            $verdict.Detail | Should -BeLike "*shown 1990 ms*'Checking…' at 420 ms*1 request(s)*"
        }

        It 'says that a press ran no check (no request, no "Checking…")' {
            $verdict = Get-VerifyUpdateCheckVerdict -Timeline (New-Watch @($script:Hidden)) -Requests 0 -Expected $script:Expected
            $verdict.Passed | Should -BeFalse
            $verdict.Detail | Should -BeLike 'the press ran no check*stayed no element (hidden)*'
        }

        It 'says that the check ran but its result was never shown' {
            $verdict = Get-VerifyUpdateCheckVerdict -Timeline (New-Watch @($script:Hidden, $script:Checking, '{"ms":1990,"found":false,"text":"","frame":{}}')) -Requests 1 -Expected $script:Expected
            $verdict.Passed | Should -BeFalse
            $verdict.Detail | Should -BeLike "the check ran (1 request(s)*never showed 'Version 9.9.0 is available'*"
            # A text without a visible frame is not shown either.
            $zero = '{"ms":1990,"found":true,"text":"Version 9.9.0 is available; the tray menu offers it.","frame":{"x":0,"y":0,"width":0,"height":0}}'
            (Get-VerifyUpdateCheckVerdict -Timeline (New-Watch @($script:Hidden, $script:Checking, $zero)) -Requests 1 -Expected $script:Expected).Passed | Should -BeFalse
        }

        It 'fails a result that vanishes again' {
            $verdict = Get-VerifyUpdateCheckVerdict -Timeline (New-Watch @($script:Hidden, $script:Checking, $script:Shown, '{"ms":2400,"found":false,"text":"","frame":{}}')) -Requests 1 -Expected $script:Expected
            $verdict.Passed | Should -BeFalse
            $verdict.Detail | Should -BeLike 'the result was shown at 1990 ms but then changed to no element (hidden)*'
        }

        It 'counts a result shown before the press only when it is shown again after "Checking…"' {
            $before = $script:Shown -replace '"ms":1990', '"ms":0'
            (Get-VerifyUpdateCheckVerdict -Timeline (New-Watch @($before)) -Requests 0 -Expected $script:Expected).Passed | Should -BeFalse
            $again = $script:Shown -replace '"ms":1990', '"ms":2100'
            (Get-VerifyUpdateCheckVerdict -Timeline (New-Watch @($before, $script:Checking, $again)) -Requests 1 -Expected $script:Expected).Passed | Should -BeTrue
        }

        It 'fails an empty watch' {
            (Get-VerifyUpdateCheckVerdict -Timeline @() -Requests 0 -Expected $script:Expected).Detail | Should -BeLike 'the Accessibility watch recorded nothing*'
        }
    }

    Context 'the menu of the status item (mac.menu-items, T-209)' {
        BeforeAll {
            $script:Texts = Get-VerifyResxStrings (Join-Path $PSScriptRoot '..' 'src' 'LayerPeek.App' 'Resources' 'Strings.resx')
            $script:Name = 'LP verify 1a2b3c4d'
            function New-MenuItem([string] $Title, [bool] $Enabled = $true, [string] $Help = '', $Submenu = $null) {
                [pscustomobject]@{ title = $Title; enabled = $Enabled; checked = $false; mark = ''; shortcut = ''; help = $Help; separator = $false; submenu = $Submenu }
            }
            $script:Separator = [pscustomobject]@{ title = ''; enabled = $false; checked = $false; mark = ''; shortcut = ''; help = ''; separator = $true; submenu = $null }
            # The menu as the helper prints it for one printer with a camera (titles as NSMenu shows them).
            function New-Menu([string] $Header, [switch] $NoCamera, [string[]] $Fixed = @('Diagnostics…', 'Settings…', 'User manual', 'About', 'Quit')) {
                $sub = @(New-MenuItem 'Open PrusaLink')
                if (-not $NoCamera) {
                    $sub += @(
                        (New-MenuItem 'Show camera'), $script:Separator, (New-MenuItem 'Start recording'),
                        (New-MenuItem 'Start timelapse (experimental)' $false 'The camera is not connected.'),
                        (New-MenuItem 'Play last recording' $false 'No recording yet'), (New-MenuItem 'Play last timelapse' $false ''),
                        (New-MenuItem 'Open recordings folder'))
                }
                $items = @((New-MenuItem $Header $true '' $sub), $script:Separator)
                foreach ($title in $Fixed) { $items += New-MenuItem $title; if ($title -eq 'Settings…') { $items += $script:Separator } }
                # Through JSON, like the check reads the helper's output.
                @((ConvertTo-Json -InputObject ([pscustomobject]@{ items = $items }) -Depth 10 | ConvertFrom-Json -Depth 10).items)
            }
        }

        It 'reads the resources and shows their menu texts like NSMenu' {
            $script:Texts['MenuQuit'] | Should -BeExactly '_Quit'
            ConvertFrom-VerifyAccessText $script:Texts['MenuQuit'] | Should -BeExactly 'Quit'
            ConvertFrom-VerifyAccessText 'Add _printer…' | Should -BeExactly 'Add printer…'
            ConvertFrom-VerifyAccessText "MK4_$([char] 0x2060)__lab" | Should -BeExactly 'MK4_lab'
            ConvertFrom-VerifyAccessText 'a__b_c' | Should -BeExactly 'a_bc'
            ConvertFrom-VerifyAccessText 'plain' | Should -BeExactly 'plain'
            ConvertFrom-VerifyAccessText '' | Should -BeExactly ''
        }

        It 'accepts the menu of every state, with progress only where it may be shown' {
            @(Get-VerifyMenuProblems -Items (New-Menu "$($script:Name): Ready") -Texts $script:Texts -Name $script:Name -State 'Ready' -Camera) | Should -HaveCount 0
            @(Get-VerifyMenuProblems -Items (New-Menu "$($script:Name): Printing – 42%") -Texts $script:Texts -Name $script:Name -State 'Printing' -Progress -Camera) | Should -HaveCount 0
            @(Get-VerifyMenuProblems -Items (New-Menu "$($script:Name): Printing") -Texts $script:Texts -Name $script:Name -State 'Printing' -Progress -Camera) | Should -HaveCount 0
            $problems = @(Get-VerifyMenuProblems -Items (New-Menu "$($script:Name): Ready – 42%") -Texts $script:Texts -Name $script:Name -State 'Ready' -Camera)
            $problems | Should -HaveCount 1
            $problems[0] | Should -BeLike "the printer item is '*Ready – 42%', expected '$($script:Name): Ready'"
            @(Get-VerifyMenuProblems -Items (New-Menu "$($script:Name): Error") -Texts $script:Texts -Name $script:Name -State 'Offline' -Camera) | Should -HaveCount 1
            @(Get-VerifyMenuProblems -Items (New-Menu "$($script:Name): Ready" -NoCamera) -Texts $script:Texts -Name $script:Name -State 'Ready') | Should -HaveCount 0
        }

        It 'names missing camera items, an enabled "Play last …" and a wrong order of the fixed items' {
            $problems = @(Get-VerifyMenuProblems -Items (New-Menu "$($script:Name): Ready" -NoCamera) -Texts $script:Texts -Name $script:Name -State 'Ready' -Camera)
            $problems | Should -Contain "no enabled 'Show camera' in the submenu (Open PrusaLink)"
            $problems | Should -Contain "no 'Start recording' in the submenu (Open PrusaLink)"
            $problems | Should -Contain 'no separator before the recording items in the submenu'
            $menu = New-Menu "$($script:Name): Ready"
            ($menu[0].submenu | Where-Object title -EQ 'Play last recording').enabled = $true
            @(Get-VerifyMenuProblems -Items $menu -Texts $script:Texts -Name $script:Name -State 'Ready' -Camera) | Should -Be @("'Play last recording' is enabled without any file")
            $moved = New-Menu "$($script:Name): Ready" -Fixed 'Diagnostics…', 'Settings…', 'User manual', 'Quit', 'About'
            @(Get-VerifyMenuProblems -Items $moved -Texts $script:Texts -Name $script:Name -State 'Ready' -Camera) | Should -BeLike "the items after the printer are 'Diagnostics…', 'Settings…', 'User manual', 'Quit', 'About', expected*'About', 'Quit'"
            $disabled = New-Menu "$($script:Name): Ready"
            ($disabled | Where-Object title -EQ 'Settings…').enabled = $false
            @(Get-VerifyMenuProblems -Items $disabled -Texts $script:Texts -Name $script:Name -State 'Ready' -Camera) | Should -Be @("'Settings…' is disabled")
            @(Get-VerifyMenuProblems -Items @() -Texts $script:Texts -Name $script:Name -State 'Ready') | Should -Be @('the menu has no items')
        }

        It 'lists the help (AXHelp) of every disabled item with its path' {
            $helps = @(Get-VerifyMenuHelps -Items (New-Menu "$($script:Name): Ready"))
            $helps | Should -Be @(
                "$($script:Name): Ready>Start timelapse (experimental): disabled, help 'The camera is not connected.'"
                "$($script:Name): Ready>Play last recording: disabled, help 'No recording yet'"
                "$($script:Name): Ready>Play last timelapse: disabled, help none"
            )
        }

        It 'checks the tooltip of the status item: [E2E] name, then the state' {
            Get-VerifyToolTipProblem -ToolTip "[E2E] $($script:Name)`nReady" -Texts $script:Texts -Name $script:Name -State 'Ready' | Should -BeNullOrEmpty
            Get-VerifyToolTipProblem -ToolTip "[E2E] $($script:Name)`nPrinting – 7%`n28 min remaining`nbox.bgcode" -Texts $script:Texts -Name $script:Name -State 'Printing' -Progress | Should -BeNullOrEmpty
            Get-VerifyToolTipProblem -ToolTip "[E2E] $($script:Name)`nPrinting – 7%" -Texts $script:Texts -Name $script:Name -State 'Printing' | Should -BeLike "the tooltip is '* | Printing – 7%', expected '* | Printing'"
            Get-VerifyToolTipProblem -ToolTip "$($script:Name)`nReady" -Texts $script:Texts -Name $script:Name -State 'Ready' | Should -Not -BeNullOrEmpty
            Get-VerifyToolTipProblem -ToolTip $null -Texts $script:Texts -Name $script:Name -State 'Ready' | Should -Not -BeNullOrEmpty
        }

        It 'puts mac.menu-items into the release preset with its preconditions' {
            $run = Invoke-Script 'macos-verify.ps1' @('-ListChecks', '-Preset', 'release')
            $run.ExitCode | Should -Be 0 -Because $run.Text
            $run.Output[0] | Should -BeLike '*allowed in advance:*mac.menu-items*'
            $line = @($run.Output | Where-Object { $_ -like 'mac.menu-items  *' })
            $line | Should -HaveCount 1
            $line[0] | Should -BeLike '*`[permission`]*'
        }
    }
}

Describe 'The stand-in GitHub API (tools/fake-github-releases.ps1)' {
    BeforeAll {
        function Start-ChildPwsh([string[]] $Arguments, [string] $Output) {
            # The same PowerShell as this run, also the dotnet tool ('dotnet pwsh.dll').
            $command = @(Get-PwshCommand)
            $all = @($command | Select-Object -Skip 1) + @('-NoProfile', '-NonInteractive') + $Arguments
            if ($Output) { Start-Process -FilePath $command[0] -ArgumentList $all -PassThru -NoNewWindow -RedirectStandardOutput $Output }
            else { Start-Process -FilePath $command[0] -ArgumentList $all -PassThru -NoNewWindow }
        }
    }

    It 'lists one release with the checksum files of both OSes, late by the delay, logs the requests and answers 404 otherwise' {
        $log = Join-Path $script:TempRoot 'github-requests.txt'
        $out = Join-Path $script:TempRoot 'github-out.txt'
        $server = Start-ChildPwsh @('-File', (Join-Path $PSScriptRoot 'fake-github-releases.ps1'), '-Version', '9.9.0', '-DelayMilliseconds', '300', '-RequestLog', $log, '-ParentProcessId', "$PID") $out
        try {
            $deadline = [DateTime]::UtcNow.AddSeconds(30)
            while ([DateTime]::UtcNow -lt $deadline -and -not ((Test-Path -LiteralPath $out) -and (Select-String -LiteralPath $out -Pattern '^Address: ' -Quiet))) { Start-Sleep -Milliseconds 100 }
            $base = ([regex]::Match((Get-Content -LiteralPath $out -Raw), 'Address: (\S+)')).Groups[1].Value
            $base | Should -Match '^http://127\.0\.0\.1:\d+/$'
            $watch = [System.Diagnostics.Stopwatch]::StartNew()
            $releases = @(Invoke-RestMethod -Uri "${base}repos/pfoltynek/LayerPeek/releases?per_page=30" -TimeoutSec 10)
            $watch.ElapsedMilliseconds | Should -BeGreaterOrEqual 250
            $releases | Should -HaveCount 1
            $releases[0].tag_name | Should -Be 'v9.9.0'
            $releases[0].draft | Should -BeFalse
            $releases[0].prerelease | Should -BeFalse
            $releases[0].html_url | Should -Be 'https://github.com/pfoltynek/LayerPeek/releases/tag/v9.9.0'
            @($releases[0].assets | Where-Object state -EQ 'uploaded' | ForEach-Object name) | Should -Contain 'SHA256SUMS-macos.txt'
            @($releases[0].assets | Where-Object state -EQ 'uploaded' | ForEach-Object name) | Should -Contain 'SHA256SUMS-windows.txt'
            { Invoke-RestMethod -Uri "${base}other" -TimeoutSec 10 } | Should -Throw '*404*'
            $requests = @(Get-Content -LiteralPath $log)
            $requests | Should -HaveCount 2
            $requests[0] | Should -Match 'GET /repos/pfoltynek/LayerPeek/releases\?per_page=30 HTTP/1.1 -> 200 OK$'
            $requests[1] | Should -Match 'GET /other HTTP/1.1 -> 404 Not Found$'
        }
        finally { if (-not $server.HasExited) { $server.Kill($true); $server.WaitForExit() } }
    }

    It 'ends when its parent process ends' {
        $out = Join-Path $script:TempRoot 'github-parent.txt'
        $parent = Start-ChildPwsh @('-Command', 'Start-Sleep -Seconds 6')
        $server = Start-ChildPwsh @('-File', (Join-Path $PSScriptRoot 'fake-github-releases.ps1'), '-ParentProcessId', "$($parent.Id)") $out
        try {
            $deadline = [DateTime]::UtcNow.AddSeconds(30)
            while ([DateTime]::UtcNow -lt $deadline -and -not ((Test-Path -LiteralPath $out) -and (Select-String -LiteralPath $out -Pattern '^Address: ' -Quiet))) { Start-Sleep -Milliseconds 100 }
            Get-Content -LiteralPath $out -Raw | Should -Match 'Address: http://127\.0\.0\.1:\d+/'
            $server.HasExited | Should -BeFalse
            $parent.WaitForExit(30000) | Should -BeTrue
            $server.WaitForExit(30000) | Should -BeTrue
        }
        finally { foreach ($process in $server, $parent) { if (-not $process.HasExited) { $process.Kill($true) } } }
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

    It 'macos-verify.ps1 -ListChecks -Preset release lists only Auto and Permission checks with their criteria (T-208)' {
        $run = Invoke-Script 'macos-verify.ps1' @('-ListChecks', '-Preset', 'release')
        $run.ExitCode | Should -Be 0 -Because $run.Text
        $run.Output[0] | Should -Match '^Preset release: \d+ checks, unattended; allowed in advance: mac\.'
        $listed = @($run.Output | Where-Object { $_ -match '^\S+  \[(auto|permission|manual)\]' })
        $listed.Count | Should -BeGreaterThan 10
        $listed | ForEach-Object { $_ | Should -Match '^mac\.\S+  \[(auto|permission)\]' }
        @($run.Output | Where-Object { $_ -match '^    closes: T-\d{3}' }).Count | Should -BeGreaterOrEqual $listed.Count
        $all = Invoke-Script 'macos-verify.ps1' @('-ListChecks')
        @($all.Output | Where-Object { $_ -match '^\S+  \[' }).Count | Should -BeGreaterThan $listed.Count
    }

    It 'macos-verify.ps1 refuses an unknown preset and -Preset with -Only before it creates anything' {
        $out = Join-Path $script:TempRoot 'out-preset'
        $unknown = Invoke-Script 'macos-verify.ps1' @('-Preset', 'nothing', '-OutputRoot', $out)
        $unknown.ExitCode | Should -Be 2
        $unknown.Text | Should -Match "Unknown preset 'nothing' \(presets: release\)"
        $both = Invoke-Script 'macos-verify.ps1' @('-Preset', 'release', '-Only', 'mac.dock', '-OutputRoot', $out)
        $both.ExitCode | Should -Be 2
        $both.Text | Should -Match '-Preset and -Only do not go together'
        Test-Path -LiteralPath $out | Should -BeFalse
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
