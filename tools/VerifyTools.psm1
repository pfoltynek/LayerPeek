<#
.SYNOPSIS
    Shared engine of the user-run verification scripts tools/macos-verify.ps1 and tools/windows-verify.ps1 (T-105).

.DESCRIPTION
    A check has an id, the task criteria it closes, a kind and an action:
      - Auto        runs without asking anything (also in -Unattended);
      - Permission  needs something only the user may allow (a macOS privacy permission, sudo, a real display sleep, an
                    installation into the user profile); asked separately before it runs, never run in -Unattended;
      - Manual      needs a human judgement (y/n) or a human action (log out and in, a real camera); never run in
                    -Unattended.
    Results: PASS (checked automatically), FAIL, SKIP (always with the reason) and MANUAL (the user confirmed it with
    "y"; a "n" is a FAIL with the question in the detail). The report (Markdown and JSON) and the evidence of every check
    go to artifacts/verify/<timestamp>/.

    Everything that talks to the user goes through the context (Prompt, Write), so that the self-test and the Pester
    tests run the engine with stand-ins and never wait for input. The pure helpers (parsers of tool output, placement
    rules, the test Info.plist) are here too, so that they are covered by tools/VerifyTools.Tests.ps1.
#>
#Requires -Version 7.2
Set-StrictMode -Version Latest

$script:Kinds = @('Auto', 'Permission', 'Manual')
$script:Statuses = @('PASS', 'FAIL', 'SKIP', 'MANUAL')
$script:SkipMarker = 'LayerPeek.VerifySkip'
$script:ReportFormat = 1

# ---------------------------------------------------------------------------------------------------------------------
# Checks, selection, running
# ---------------------------------------------------------------------------------------------------------------------

function New-VerifyCheck {
    <#
    .SYNOPSIS A check definition. -Closes lists the task criteria it closes, each starting with the task id ("T-021 kritérium 1 ...").
    .PARAMETER Permission What the user is asked to allow before a Permission check runs (shown in the question and the report).
    .PARAMETER Final Runs after the cleanup of the run (the before/after comparison of the profile).
    .PARAMETER Needs Names of the preconditions the check needs (T-208, e.g. Session, Accessibility). The script checks
        them once at the start of an unattended run; a check whose need is not met ends with SKIP and the reason, unrun.
    #>
    param(
        [Parameter(Mandatory)] [string] $Id,
        [Parameter(Mandatory)] [string] $Title,
        [Parameter(Mandatory)] [string[]] $Closes,
        [Parameter(Mandatory)] [ValidateSet('Auto', 'Permission', 'Manual')] [string] $Kind,
        [string] $Permission,
        [switch] $Final,
        [string[]] $Needs = @(),
        [Parameter(Mandatory)] [scriptblock] $Action
    )
    if ($Id -cnotmatch '^[a-z]+(\.[a-z0-9]+(-[a-z0-9]+)*)+$') { throw "Invalid check id '$Id' (lower case, e.g. mac.keychain)." }
    if ($Kind -eq 'Permission' -and [string]::IsNullOrWhiteSpace($Permission)) { throw "The Permission check $Id must say what it needs (-Permission)." }
    foreach ($criterion in $Closes) {
        if ($criterion -cnotmatch '^T-\d{3}\b') { throw "The check $Id closes '$criterion', which does not start with a task id (T-XXX)." }
    }
    foreach ($need in $Needs) {
        if ($need -cnotmatch '^[A-Z][A-Za-z]+$') { throw "The check $Id needs '$need', which is not a precondition name (e.g. Session)." }
    }
    [pscustomobject]@{
        PSTypeName = 'LayerPeek.VerifyCheck'
        Id         = $Id
        Title      = $Title
        Closes     = [string[]] $Closes
        Kind       = $Kind
        Permission = $Permission
        Final      = [bool] $Final
        Needs      = [string[]] @($Needs)
        Action     = $Action
    }
}

function Get-VerifyTaskIds {
    <# .SYNOPSIS The task ids (T-XXX) a check closes. #>
    param([Parameter(Mandatory)] $Check)
    @($Check.Closes | ForEach-Object { if ($_ -match '^(T-\d{3})') { $Matches[1] } } | Select-Object -Unique)
}

function Test-VerifyCheckDefinitions {
    <#
    .SYNOPSIS
        Problems of a check list (empty = fine): duplicate ids, and task ids without a task file under -TasksRoot (except
        -AllowMissingTasks: tasks of other branches that a check waits for, e.g. T-102).
    #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Checks, [string] $TasksRoot, [string[]] $AllowMissingTasks = @())
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $known = $null
    if ($TasksRoot) {
        $known = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($file in Get-ChildItem -LiteralPath $TasksRoot -Recurse -File -Filter 'T-*.md') {
            if ($file.Name -match '^(T-\d{3})-') { $null = $known.Add($Matches[1]) }
        }
    }
    foreach ($check in $Checks) {
        if (-not $seen.Add($check.Id)) { "The check id $($check.Id) is used twice." }
        if ($known) {
            foreach ($task in Get-VerifyTaskIds $check) {
                if (-not $known.Contains($task) -and $AllowMissingTasks -notcontains $task) { "The check $($check.Id) closes $task, which has no task file in $TasksRoot." }
            }
        }
    }
}

function Split-VerifyIds {
    <# .SYNOPSIS 'a,b' and @('a', 'b') alike ('pwsh -File' passes a list as one string). #>
    param([AllowNull()] [AllowEmptyCollection()] [string[]] $Ids)
    @($Ids | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

function Select-VerifyChecks {
    <#
    .SYNOPSIS
        The checks to run, in definition order: all, or those matching -Only (ids or wildcards, e.g. mac.camera*), minus
        those matching -Skip. A pattern that matches no check throws, so that a typo never silently runs nothing.
    #>
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Checks,
        [string[]] $Only,
        [string[]] $Skip
    )
    $onlyPatterns = @(Split-VerifyIds $Only)
    $skipPatterns = @(Split-VerifyIds $Skip)
    foreach ($pattern in @($onlyPatterns) + @($skipPatterns)) {
        if (-not ($Checks | Where-Object { $_.Id -like $pattern })) {
            throw "No check matches '$pattern'. Run the script with -ListChecks for the ids."
        }
    }
    @($Checks | Where-Object {
            $id = $_.Id
            ($onlyPatterns.Count -eq 0 -or ($onlyPatterns | Where-Object { $id -like $_ })) -and -not ($skipPatterns | Where-Object { $id -like $_ })
        })
}

function Format-VerifyCheckList {
    <# .SYNOPSIS The lines of -ListChecks: id, kind, the criteria it closes and the title. #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Checks)
    foreach ($check in $Checks) {
        $kind = switch ($check.Kind) { 'Auto' { 'auto' } 'Permission' { 'permission' } 'Manual' { 'manual' } }
        "$($check.Id)  [$kind]  $($check.Title)"
        foreach ($criterion in $check.Closes) { "    closes: $criterion" }
        if ($check.Permission) { "    needs:  $($check.Permission)" }
        if (@($check.Needs).Count -gt 0) { "    requires: $($check.Needs -join ', ')" }
    }
}

# ---------------------------------------------------------------------------------------------------------------------
# Presets and preconditions (T-208)
# ---------------------------------------------------------------------------------------------------------------------

function Test-VerifyPreset {
    <#
    .SYNOPSIS
        Problems of a preset (empty = fine): an id twice, an id without a check, a Manual check (a preset runs without a
        human) and a check without the criteria it closes.
    #>
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Checks,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Ids
    )
    if ($Ids.Count -eq 0) { "The preset $Name lists no check." }
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($id in $Ids) {
        if (-not $seen.Add($id)) { "The preset $Name lists $id twice."; continue }
        $check = $Checks | Where-Object { $_.Id -ceq $id } | Select-Object -First 1
        if (-not $check) { "The preset $Name lists $id, which is no check."; continue }
        if ($check.Kind -notin 'Auto', 'Permission') { "The preset $Name lists the $($check.Kind) check $id; a preset runs only Auto and Permission checks." }
        if (@($check.Closes | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count -eq 0) { "The preset $Name lists $id, which closes no criterion." }
    }
}

function Select-VerifyPreset {
    <#
    .SYNOPSIS
        The checks of a preset in definition order (the order of the script, e.g. builds first), minus those matching
        -Skip. An unknown id or a -Skip pattern that matches no check of the preset throws.
    #>
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Checks,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Ids,
        [string[]] $Skip
    )
    $problems = @(Test-VerifyPreset -Checks $Checks -Name $Name -Ids $Ids)
    if ($problems.Count -gt 0) { throw ($problems -join ' ') }
    $inPreset = @($Checks | Where-Object { $Ids -ccontains $_.Id })
    @(Select-VerifyChecks -Checks $inPreset -Skip $Skip)
}

function Get-VerifyNeeds {
    <# .SYNOPSIS The distinct precondition names the checks need, sorted. #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Checks)
    @($Checks | ForEach-Object { @($_.Needs) } | Where-Object { $_ } | Sort-Object -Unique -CaseSensitive)
}

function Format-VerifyUnmetNeeds {
    <#
    .SYNOPSIS
        The lines printed once at the start of a run: every precondition that is not met, its reason and the checks it
        ends with SKIP. Nothing when all are met.
    .PARAMETER Unmet Precondition name -> reason.
    #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Checks, [Parameter(Mandatory)] [System.Collections.IDictionary] $Unmet)
    foreach ($need in @($Unmet.Keys | Sort-Object -CaseSensitive)) {
        $affected = @($Checks | Where-Object { @($_.Needs) -ccontains $need } | ForEach-Object Id)
        if ($affected.Count -eq 0) { continue }
        "Not met: $need - $($Unmet[$need])"
        "    SKIP: $($affected -join ', ')"
    }
}

function Get-VerifyUnmetReason {
    <# .SYNOPSIS The SKIP reason of a check with an unmet precondition, or $null when all its needs are met. #>
    param([Parameter(Mandatory)] $Check, [AllowNull()] [System.Collections.IDictionary] $Unmet)
    if (-not $Unmet) { return $null }
    $reasons = @(@($Check.Needs) | Where-Object { $Unmet.Contains($_) } | ForEach-Object { "$_ ($($Unmet[$_]))" })
    if ($reasons.Count -eq 0) { return $null }
    "precondition not met at the start of the run: $($reasons -join '; ')."
}

function New-VerifyResult {
    <# .SYNOPSIS The result an action returns. MANUAL = confirmed by the user (with -Answer). #>
    param(
        [Parameter(Mandatory)] [ValidateSet('PASS', 'FAIL', 'SKIP', 'MANUAL')] [string] $Status,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Detail
    )
    [pscustomobject]@{ PSTypeName = 'LayerPeek.VerifyResult'; Status = $Status; Detail = $Detail }
}

function Skip-VerifyCheck {
    <# .SYNOPSIS Ends the running check with SKIP and the reason (e.g. a missing prerequisite). #>
    param([Parameter(Mandatory)] [string] $Reason)
    $exception = [System.InvalidOperationException]::new($Reason)
    $exception.Data[$script:SkipMarker] = $true
    throw $exception
}

function New-VerifyContext {
    <#
    .SYNOPSIS
        What a check action gets: the mode, the run folder, the prompt and the writer (stand-ins in tests), a store for the
        fixtures the checks share (a built test app, a running simulator) and the cleanup list of the run.
    .PARAMETER Prompt { param($Question) ... } returns the answer text; default: Read-Host.
    .PARAMETER Writer { param($Text, $Color) ... }; default: Write-Host.
    .PARAMETER Allow Ids or wildcards of the Permission checks the user allowed in advance (T-140): they run without the
        question, also in -Unattended.
    #>
    param(
        [Parameter(Mandatory)] [string] $RunFolder,
        [switch] $Unattended,
        [string[]] $Allow = @(),
        [scriptblock] $Prompt = { param($Question) Read-Host $Question },
        [scriptblock] $Writer = { param($Text, $Color) if ($Color) { Write-Host $Text -ForegroundColor $Color } else { Write-Host $Text } }
    )
    [pscustomobject]@{
        PSTypeName = 'LayerPeek.VerifyContext'
        Unattended = [bool] $Unattended
        Allow      = [string[]] @($Allow | Where-Object { $_ })
        RunFolder  = $RunFolder
        Prompt     = $Prompt
        Writer     = $Writer
        Shared     = @{}
        # T-208: precondition name -> reason, filled once at the start of an unattended run.
        Unmet      = [ordered]@{}
        Cleanup    = [System.Collections.Generic.List[object]]::new()
        CheckId    = $null
        Log        = [System.Collections.Generic.List[string]]::new()
    }
}

function Write-VerifyStep {
    <# .SYNOPSIS An instruction or progress line of the running check (console and the check's log). #>
    param([Parameter(Mandatory)] $Context, [Parameter(Mandatory)] [AllowEmptyString()] [string] $Text, [string] $Color)
    $Context.Log.Add($Text)
    & $Context.Writer $Text $Color
}

function Read-VerifyAnswer {
    <# .SYNOPSIS Asks a y/n question; only "y" / "yes" (any case) is yes. Never asks in -Unattended (throws). #>
    param([Parameter(Mandatory)] $Context, [Parameter(Mandatory)] [string] $Question)
    if ($Context.Unattended) { throw 'Read-VerifyAnswer must not be called in -Unattended.' }
    $answer = [string] (& $Context.Prompt "$Question [y/N]")
    $yes = $answer.Trim() -imatch '^(y|yes)$'
    $Context.Log.Add("? $Question -> $(if ($yes) { 'yes' } else { 'no' })")
    $yes
}

function Confirm-VerifyStep {
    <#
    .SYNOPSIS
        A human judgement inside a check: in -Unattended the check ends with SKIP; otherwise the y/n answer (with the
        question kept for the report).
    #>
    param([Parameter(Mandatory)] $Context, [Parameter(Mandatory)] [string] $Question)
    if ($Context.Unattended) { Skip-VerifyCheck "needs a human answer ('$Question'); run the script without -Unattended." }
    Read-VerifyAnswer -Context $Context -Question $Question
}

function Read-VerifyText {
    <# .SYNOPSIS A free-text answer (e.g. the exact text of a system dialog); in -Unattended the check ends with SKIP. #>
    param([Parameter(Mandatory)] $Context, [Parameter(Mandatory)] [string] $Question)
    if ($Context.Unattended) { Skip-VerifyCheck "needs a human answer ('$Question'); run the script without -Unattended." }
    $answer = ([string] (& $Context.Prompt $Question)).Trim()
    $Context.Log.Add("? $Question -> $answer")
    $answer
}

function Get-VerifyEvidenceFolder {
    <# .SYNOPSIS evidence/<check id> of the run (created). #>
    param([Parameter(Mandatory)] $Context)
    $folder = Join-Path $Context.RunFolder 'evidence' ($Context.CheckId ?? 'run')
    $null = New-Item -ItemType Directory -Force -Path $folder
    $folder
}

function Save-VerifyEvidence {
    <# .SYNOPSIS Writes lines (-Content) or copies a file (-Path) into the evidence folder of the running check; returns the path. #>
    param(
        [Parameter(Mandatory)] $Context,
        [Parameter(Mandatory)] [string] $Name,
        [AllowNull()] [AllowEmptyCollection()] [object[]] $Content,
        [string] $Path
    )
    $target = Join-Path (Get-VerifyEvidenceFolder $Context) $Name
    if ($Path) {
        if (Test-Path -LiteralPath $Path -PathType Container) { Copy-Item -LiteralPath $Path -Destination $target -Recurse -Force }
        elseif (Test-Path -LiteralPath $Path) { Copy-Item -LiteralPath $Path -Destination $target -Force }
        else { [System.IO.File]::WriteAllText($target, "(not found: $Path)`n") }
    }
    else {
        $text = (@($Content) | ForEach-Object { "$_" }) -join "`n"
        [System.IO.File]::WriteAllText($target, $text + "`n", [System.Text.UTF8Encoding]::new($false))
    }
    $target
}

function Add-VerifyCleanup {
    <#
    .SYNOPSIS
        Registers a cleanup step of the run, run in reverse order by Invoke-VerifyCleanup as '& $Action $State'. The state
        is passed instead of a closure: a GetNewClosure() script block cannot call the functions of the calling script.
    #>
    param([Parameter(Mandatory)] $Context, [Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [scriptblock] $Action, $State, [switch] $RunLast)
    $step = [pscustomobject]@{ Name = $Name; Action = $Action; State = $State }
    # -RunLast: shared fixtures (a test bundle, a simulator) outlive the steps registered after them.
    if ($RunLast) { $Context.Cleanup.Insert(0, $step) } else { $Context.Cleanup.Add($step) }
}

function Invoke-VerifyCleanup {
    <# .SYNOPSIS Runs the registered cleanup steps, last first; every step runs even when one fails. Returns the problems. #>
    param([Parameter(Mandatory)] $Context)
    $problems = [System.Collections.Generic.List[string]]::new()
    for ($i = $Context.Cleanup.Count - 1; $i -ge 0; $i--) {
        $step = $Context.Cleanup[$i]
        try { $null = & $step.Action $step.State }
        catch { $problems.Add("$($step.Name): $($_.Exception.Message)") }
    }
    $Context.Cleanup.Clear()
    $problems.ToArray()
}

function Invoke-VerifyCheck {
    <#
    .SYNOPSIS
        Runs one check and returns its report entry. Permission checks are asked first (never run in -Unattended), Manual
        checks are skipped in -Unattended; an exception is a FAIL (its message is the detail, the script stack trace goes
        to the evidence), Skip-VerifyCheck a SKIP.
    #>
    param([Parameter(Mandatory)] $Check, [Parameter(Mandatory)] $Context)
    $Context.CheckId = $Check.Id
    $Context.Log.Clear()
    $started = [DateTime]::UtcNow
    $result = $null
    & $Context.Writer '' $null
    & $Context.Writer "== $($Check.Id): $($Check.Title)" 'Cyan'
    $allowed = @($Context.Allow | Where-Object { $Check.Id -like $_ }).Count -gt 0
    $unmet = Get-VerifyUnmetReason -Check $Check -Unmet $Context.Unmet
    if ($unmet) { $result = New-VerifyResult SKIP $unmet }
    elseif ($Check.Kind -eq 'Permission' -and $allowed) {
        & $Context.Writer "   Allowed in advance (-Allow): $($Check.Permission)." $null
    }
    elseif ($Check.Kind -eq 'Permission') {
        if ($Context.Unattended) { $result = New-VerifyResult SKIP "needs a permission or a system change ($($Check.Permission)); never asked in -Unattended." }
        elseif (-not (Read-VerifyAnswer -Context $Context -Question "This check needs: $($Check.Permission). Run it now?")) {
            $result = New-VerifyResult SKIP "declined by the user (needs: $($Check.Permission))."
        }
    }
    elseif ($Check.Kind -eq 'Manual' -and $Context.Unattended) {
        $result = New-VerifyResult SKIP 'needs a human step or judgement; never run in -Unattended.'
    }
    if (-not $result) {
        try {
            $output = @(& $Check.Action $Context | Where-Object { $null -ne $_ })
            $returned = @($output | Where-Object { $_.PSObject.TypeNames -contains 'LayerPeek.VerifyResult' })
            $other = @($output | Where-Object { $_.PSObject.TypeNames -notcontains 'LayerPeek.VerifyResult' })
            if ($other.Count -gt 0) { $null = Save-VerifyEvidence -Context $Context -Name 'output.txt' -Content $other }
            $result = if ($returned.Count -gt 0) { $returned[-1] } else { New-VerifyResult FAIL 'The check returned no result (a bug of the check).' }
        }
        catch {
            $exception = $_.Exception
            if ($exception.Data.Contains($script:SkipMarker)) { $result = New-VerifyResult SKIP $exception.Message }
            else {
                $result = New-VerifyResult FAIL "error: $($exception.Message)"
                $null = Save-VerifyEvidence -Context $Context -Name 'error.txt' -Content @($_.ToString(), $_.ScriptStackTrace)
            }
        }
    }
    if ($Context.Log.Count -gt 0) { $null = Save-VerifyEvidence -Context $Context -Name 'steps.txt' -Content $Context.Log }
    $evidenceFolder = Join-Path $Context.RunFolder 'evidence' $Check.Id
    $evidence = @(if (Test-Path -LiteralPath $evidenceFolder) {
            Get-ChildItem -LiteralPath $evidenceFolder -Recurse -File | ForEach-Object { [System.IO.Path]::GetRelativePath($Context.RunFolder, $_.FullName).Replace('\', '/') } | Sort-Object -CaseSensitive
        })
    $color = switch ($result.Status) { 'PASS' { 'Green' } 'MANUAL' { 'Green' } 'SKIP' { 'Yellow' } default { 'Red' } }
    & $Context.Writer "   $($result.Status): $($result.Detail)" $color
    [pscustomobject]@{
        Id       = $Check.Id
        Title    = $Check.Title
        Kind     = $Check.Kind
        Closes   = [string[]] $Check.Closes
        Status   = $result.Status
        Detail   = $result.Detail
        Seconds  = [math]::Round(([DateTime]::UtcNow - $started).TotalSeconds, 1)
        Evidence = [string[]] $evidence
    }
}

function Invoke-VerifyRun {
    <#
    .SYNOPSIS
        Runs the selected checks (the Final ones after the cleanup) and returns their report entries. The cleanup always
        runs, also after Ctrl+C in a check; its problems are a FAIL entry 'cleanup'.
    #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Checks, [Parameter(Mandatory)] $Context)
    $results = [System.Collections.Generic.List[object]]::new()
    try {
        foreach ($check in @($Checks | Where-Object { -not $_.Final })) { $results.Add((Invoke-VerifyCheck -Check $check -Context $Context)) }
    }
    finally {
        $problems = @(Invoke-VerifyCleanup -Context $Context)
        if ($problems.Count -gt 0) {
            & $Context.Writer "Cleanup problems: $($problems -join '; ')" 'Red'
            $results.Add([pscustomobject]@{ Id = 'cleanup'; Title = 'Cleanup of the run'; Kind = 'Auto'; Closes = [string[]] @(); Status = 'FAIL'; Detail = ($problems -join '; '); Seconds = 0; Evidence = [string[]] @() })
        }
    }
    foreach ($check in @($Checks | Where-Object { $_.Final })) { $results.Add((Invoke-VerifyCheck -Check $check -Context $Context)) }
    , $results.ToArray()
}

function Get-VerifyExitCode {
    <# .SYNOPSIS 1 when any result is a FAIL, otherwise 0 (SKIP and MANUAL do not fail the run). #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Results)
    if ($Results | Where-Object { $_.Status -eq 'FAIL' }) { 1 } else { 0 }
}

# ---------------------------------------------------------------------------------------------------------------------
# Run folder, run info, report
# ---------------------------------------------------------------------------------------------------------------------

function New-VerifyRunFolder {
    <# .SYNOPSIS <Root>/<yyyyMMdd-HHmmss> (with -2, -3 ... when it exists), created. #>
    param([Parameter(Mandatory)] [string] $Root, [DateTime] $Now = [DateTime]::Now)
    $name = $Now.ToString('yyyyMMdd-HHmmss', [System.Globalization.CultureInfo]::InvariantCulture)
    $folder = Join-Path $Root $name
    $n = 2
    while (Test-Path -LiteralPath $folder) { $folder = Join-Path $Root "$name-$n"; $n++ }
    $null = New-Item -ItemType Directory -Force -Path $folder
    (Resolve-Path -LiteralPath $folder).ProviderPath
}

function Get-VerifyRunInfo {
    <# .SYNOPSIS What the report says about the run: commit, branch, local changes, app version, OS, mode, selection. #>
    param(
        [Parameter(Mandatory)] [string] $RepoRoot,
        [Parameter(Mandatory)] [string] $Script,
        [switch] $Unattended,
        [string[]] $Selected = @(),
        # T-140: the Permission checks allowed in advance; only then the report gets an Allowed field.
        [string[]] $Allow = @(),
        # T-129: the stand-in file of a test run (LAYERPEEK_VERIFY_STANDINS); only then the report gets a StandIns field.
        [string] $StandIns,
        # T-208: the preset of the run (-Preset), the state of its preconditions (name -> 'met' or the reason) and the start.
        [string] $Preset,
        [System.Collections.IDictionary] $Preconditions,
        [DateTimeOffset] $Started = [DateTimeOffset]::Now,
        [scriptblock] $Git = { param($Arguments) $output = & git -C $RepoRoot @Arguments 2>$null; if ($LASTEXITCODE -eq 0) { $output } else { $null } }
    )
    $commit = & $Git @('rev-parse', 'HEAD')
    $branch = & $Git @('rev-parse', '--abbrev-ref', 'HEAD')
    $status = & $Git @('status', '--porcelain')
    $version = $null
    $props = Join-Path $RepoRoot 'Directory.Build.props'
    if (Test-Path -LiteralPath $props) {
        [xml] $xml = Get-Content -LiteralPath $props -Raw
        $node = $xml.SelectSingleNode('/Project/PropertyGroup/Version')
        if ($node) { $version = $node.InnerText.Trim() }
    }
    $machine = $null
    $os = if ($IsMacOS) {
        $machine = "$((& /usr/sbin/sysctl -n hw.model 2>$null) -join '')".Trim()
        "macOS $((& /usr/bin/sw_vers -productVersion 2>$null) -join '') $((& /usr/bin/sw_vers -buildVersion 2>$null) -join '') ($([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture))"
    }
    else { "$([System.Runtime.InteropServices.RuntimeInformation]::OSDescription) ($([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture))" }
    $info = [ordered]@{
        Script     = $Script
        Started    = $Started.ToString('o')
        Commit     = if ($commit) { "$commit".Trim() } else { 'unknown' }
        Branch     = if ($branch) { "$branch".Trim() } else { 'unknown' }
        Dirty      = [bool] (@($status | Where-Object { $_ }).Count -gt 0)
        AppVersion = $version ?? 'unknown'
        Os         = $os
        Machine    = if ($machine) { $machine } else { 'unknown' }
        PowerShell = $PSVersionTable.PSVersion.ToString()
        Mode       = if ($Unattended) { 'unattended' } else { 'interactive' }
        Preset     = if ($Preset) { $Preset } else { $null }
        Selected   = [string[]] $Selected
    }
    if ($Preconditions -and $Preconditions.Count -gt 0) { $info.Preconditions = $Preconditions }
    if ($Allow.Count -gt 0) { $info.Allowed = [string[]] $Allow }
    if ($StandIns) { $info.StandIns = $StandIns }
    $info
}

function Resolve-VerifyStandInsPath {
    <#
    .SYNOPSIS
        T-129: the full path of the stand-in file of a test run when it lies inside one of -AllowedRoots (the temp folders of
        the tests), otherwise $null: a stand-in file elsewhere is never loaded. A relative path is relative to -BasePath.
    #>
    param([Parameter(Mandatory)] [string] $Path, [Parameter(Mandatory)] [AllowEmptyCollection()] [AllowEmptyString()] [string[]] $AllowedRoots, [Parameter(Mandatory)] [string] $BasePath)
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $full = [System.IO.Path]::GetFullPath($Path, $BasePath)
    foreach ($root in @($AllowedRoots | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
        $prefix = [System.IO.Path]::TrimEndingDirectorySeparator([System.IO.Path]::GetFullPath($root, $BasePath)) + [System.IO.Path]::DirectorySeparatorChar
        if ($full.StartsWith($prefix, $comparison)) { return $full }
    }
    $null
}

function Get-VerifySummary {
    <# .SYNOPSIS Counts per status. #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Results)
    $summary = [ordered]@{}
    foreach ($status in $script:Statuses) { $summary[$status] = @($Results | Where-Object { $_.Status -eq $status }).Count }
    $summary
}

function Format-VerifySummary {
    <# .SYNOPSIS The console summary at the end of a run. #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Results)
    $summary = Get-VerifySummary $Results
    'Summary: ' + (($summary.Keys | ForEach-Object { "$_ $($summary[$_])" }) -join ', ')
    foreach ($result in $Results) { "  {0,-6} {1}  {2}" -f $result.Status, $result.Id, $result.Detail }
}

function ConvertTo-VerifyMarkdown {
    <# .SYNOPSIS The Markdown report (pure). #>
    param([Parameter(Mandatory)] [System.Collections.IDictionary] $Info, [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Results)
    $summary = Get-VerifySummary $Results
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# LayerPeek verification report ($($Info.Script))")
    $lines.Add('')
    $lines.Add("- Started: $($Info.Started)")
    if ($Info.Contains('Seconds')) { $lines.Add("- Duration: $(Format-VerifyDuration $Info.Seconds)") }
    $lines.Add("- Commit: ``$($Info.Commit)`` on ``$($Info.Branch)``$(if ($Info.Dirty) { ' (with local changes)' })")
    $lines.Add("- Clean tree: $(if ($Info.Dirty) { 'no' } else { 'yes' })")
    $lines.Add("- App version: $($Info.AppVersion)")
    $lines.Add("- OS: $($Info.Os), PowerShell $($Info.PowerShell)")
    if ($Info.Contains('Machine')) { $lines.Add("- Machine: $($Info.Machine)") }
    $lines.Add("- Mode: $($Info.Mode)")
    if ($Info.Contains('Preset')) { $lines.Add("- Preset: $(if ($Info.Preset) { $Info.Preset } else { 'none' })") }
    if ($Info.Contains('Preconditions')) {
        $lines.Add('- Preconditions: ' + (($Info.Preconditions.Keys | ForEach-Object { "$_ $($Info.Preconditions[$_])" }) -join '; '))
    }
    if ($Info.Contains('StandIns') -and $Info.StandIns) { $lines.Add("- **Stand-ins:** ``$($Info.StandIns)`` (test run with stand-in checks, not a real verification)") }
    $lines.Add("- Result: " + (($summary.Keys | ForEach-Object { "$_ $($summary[$_])" }) -join ', '))
    $lines.Add('')
    $lines.Add('| Check | Kind | Result | Seconds | Closes | Detail |')
    $lines.Add('|---|---|---|---|---|---|')
    foreach ($result in $Results) {
        $detail = ($result.Detail -replace '\|', '\|' -replace "`r?`n", ' ')
        $closes = (@($result.Closes) -join '<br>') -replace '\|', '\|'
        $lines.Add("| ``$($result.Id)`` | $($result.Kind) | **$($result.Status)** | $($result.Seconds) | $closes | $detail |")
    }
    $lines.Add('')
    $lines.Add('## Evidence')
    foreach ($result in $Results) {
        if (@($result.Evidence).Count -eq 0) { continue }
        $lines.Add('')
        $lines.Add("### $($result.Id)")
        foreach ($file in $result.Evidence) {
            $link = ($file -split '/' | ForEach-Object { [uri]::EscapeDataString($_) }) -join '/'
            if ($file -match '\.png$') { $lines.Add("- ![$file]($link)") } else { $lines.Add("- [$file]($link)") }
        }
    }
    $lines.Add('')
    $lines.ToArray()
}

function Format-VerifyDuration {
    <# .SYNOPSIS '12 min 5 s' (or '42 s') of a number of seconds. #>
    param([Parameter(Mandatory)] [double] $Seconds)
    $total = [int] [math]::Round($Seconds)
    if ($total -lt 60) { return "$total s" }
    "$([math]::Floor($total / 60)) min $($total % 60) s"
}

function Hide-VerifyHomePath {
    <#
    .SYNOPSIS
        T-208: the text with the user's home folder written as ~ (the report names no profile path), also inside arrays
        and dictionaries; other values unchanged. /Users/me2 is not /Users/me.
    #>
    param([AllowNull()] $Value, [string] $HomePath = [Environment]::GetFolderPath('UserProfile'))
    if ($null -eq $Value -or [string]::IsNullOrEmpty($HomePath)) { return , $Value }
    $trimmed = [System.IO.Path]::TrimEndingDirectorySeparator($HomePath)
    if ($trimmed.Length -lt 2) { return , $Value }
    if ($Value -is [string]) {
        $options = if ($IsWindows) { 'IgnoreCase' } else { 'None' }
        return [regex]::Replace($Value, [regex]::Escape($trimmed) + '(?=[/\\]|$|[^\w.-])', '~', $options)
    }
    if ($Value -is [System.Collections.IDictionary]) {
        $copy = [ordered]@{}
        foreach ($key in $Value.Keys) { $copy[$key] = Hide-VerifyHomePath -Value $Value[$key] -HomePath $HomePath }
        return $copy
    }
    if ($Value -is [System.Collections.IEnumerable]) { return , [object[]] @($Value | ForEach-Object { Hide-VerifyHomePath -Value $_ -HomePath $HomePath }) }
    , $Value
}

function Set-VerifyLatestLink {
    <#
    .SYNOPSIS
        T-208: <Root>/latest as a relative symbolic link to the run folder (replaces an older link; a real folder named
        latest is never touched). Returns the link path, or $null when the link could not be made (e.g. Windows without
        the privilege for symbolic links).
    #>
    param([Parameter(Mandatory)] [string] $Root, [Parameter(Mandatory)] [string] $RunFolder)
    $link = Join-Path $Root 'latest'
    $existing = Get-Item -LiteralPath $link -Force -ErrorAction SilentlyContinue
    if ($existing -and -not $existing.LinkType) { return $null }
    try {
        if ($existing) { $existing.Delete() }
        $null = New-Item -ItemType SymbolicLink -Path $link -Target (Split-Path -Leaf $RunFolder) -ErrorAction Stop
        $link
    }
    catch { Write-Verbose "No latest link: $($_.Exception.Message)"; $null }
}

function Write-VerifyReport {
    <# .SYNOPSIS report.md and report.json in -Folder, the home folder written as ~ (T-208); returns both paths. #>
    param([Parameter(Mandatory)] [string] $Folder, [Parameter(Mandatory)] [System.Collections.IDictionary] $Info, [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Results)
    $Info = Hide-VerifyHomePath $Info
    $Results = @($Results | ForEach-Object { $r = $_.PSObject.Copy(); $r.Title = Hide-VerifyHomePath $r.Title; $r.Detail = Hide-VerifyHomePath $r.Detail; $r })
    $utf8 = [System.Text.UTF8Encoding]::new($false)
    $markdown = Join-Path $Folder 'report.md'
    $json = Join-Path $Folder 'report.json'
    [System.IO.File]::WriteAllText($markdown, ((ConvertTo-VerifyMarkdown -Info $Info -Results $Results) -join "`n"), $utf8)
    $document = [ordered]@{
        Format  = $script:ReportFormat
        Run     = $Info
        Summary = Get-VerifySummary $Results
        Results = @($Results | ForEach-Object { [ordered]@{ Id = $_.Id; Title = $_.Title; Kind = $_.Kind; Closes = @($_.Closes); Status = $_.Status; Detail = $_.Detail; Seconds = $_.Seconds; Evidence = @($_.Evidence) } })
    }
    [System.IO.File]::WriteAllText($json, ($document | ConvertTo-Json -Depth 6) + "`n", $utf8)
    [pscustomobject]@{ Markdown = $markdown; Json = $json }
}

# ---------------------------------------------------------------------------------------------------------------------
# Running tools
# ---------------------------------------------------------------------------------------------------------------------

function Invoke-VerifyTool {
    <#
    .SYNOPSIS
        Runs a command with extra environment variables (restored afterwards), keeps its output (stdout and stderr) as
        evidence <Label>.txt of the running check and returns ExitCode and Output. -Show also prints the output while it
        runs (long runs such as dotnet test).
    #>
    param(
        [Parameter(Mandatory)] $Context,
        [Parameter(Mandatory)] [string] $Label,
        [Parameter(Mandatory)] [string] $FilePath,
        [string[]] $ArgumentList = @(),
        [hashtable] $Environment = @{},
        [switch] $Show
    )
    $saved = @{}
    foreach ($name in $Environment.Keys) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $Environment[$name])
    }
    $lines = [System.Collections.Generic.List[string]]::new()
    try {
        & $FilePath @ArgumentList 2>&1 | ForEach-Object {
            $line = "$_"
            $lines.Add($line)
            if ($Show) { & $Context.Writer "   | $line" $null }
        }
        $exitCode = $LASTEXITCODE
    }
    finally {
        foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    }
    $header = @("> $FilePath $($ArgumentList -join ' ')") + @($Environment.Keys | Sort-Object | ForEach-Object { "> env $_=$($Environment[$_])" }) + @("> exit code $exitCode", '')
    $null = Save-VerifyEvidence -Context $Context -Name "$Label.txt" -Content (@($header) + @($lines))
    [pscustomobject]@{ ExitCode = $exitCode; Output = [string[]] $lines.ToArray() }
}

function Wait-VerifyCondition {
    <# .SYNOPSIS Polls -Condition until it is true; $false after -TimeoutSeconds. #>
    param([Parameter(Mandatory)] [scriptblock] $Condition, [double] $TimeoutSeconds = 30, [int] $IntervalMilliseconds = 250)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ($true) {
        if (& $Condition) { return $true }
        if ([DateTime]::UtcNow -ge $deadline) { return $false }
        Start-Sleep -Milliseconds $IntervalMilliseconds
    }
}

# ---------------------------------------------------------------------------------------------------------------------
# Parsers of tool output (pure)
# ---------------------------------------------------------------------------------------------------------------------

function Get-VerifyTestTotals {
    <#
    .SYNOPSIS
        Totals of a 'dotnet test' run (Microsoft.Testing.Platform) from its output, language independent: the sum of the
        per-assembly lines "<assembly> ... [+passed/xfailed/?skipped]". $null when there is none (the build failed).
        Each assembly counts once (T-208): a solution run prints its line again in the closing summary.
    #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [AllowEmptyString()] [string[]] $Output)
    $perAssembly = [ordered]@{}
    $index = 0
    foreach ($line in $Output) {
        $index++
        foreach ($match in [regex]::Matches($line, '\[\+(?<p>\d+)/x(?<f>\d+)/\?(?<s>\d+)\]')) {
            # "<path> (<tfm>|<arch>)" names the assembly; a line without it counts on its own.
            $key = if ($line -match '^\s*(?<key>\S.*?\([^)]*\))\s') { $Matches.key } else { "line $index" }
            $perAssembly[$key] = [int[]] @($match.Groups['p'].Value, $match.Groups['f'].Value, $match.Groups['s'].Value)
        }
    }
    if ($perAssembly.Count -eq 0) { return $null }
    $passed = 0; $failed = 0; $skipped = 0
    foreach ($counts in $perAssembly.Values) { $passed += $counts[0]; $failed += $counts[1]; $skipped += $counts[2] }
    [pscustomobject]@{ Total = $passed + $failed + $skipped; Passed = $passed; Failed = $failed; Skipped = $skipped; Assemblies = $perAssembly.Count }
}

function Get-VerifyRunE2ETotals {
    <#
    .SYNOPSIS
        Totals of tools/run-e2e.ps1 from its summary lines "<pass>: green - 12 passed, 3 skipped, 0 failed (of 15)"
        (Format-TestPassSummary), summed over the passes; $null when there is none.
    #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [AllowEmptyString()] [string[]] $Output)
    $passed = 0; $failed = 0; $skipped = 0; $total = 0; $passes = 0
    foreach ($line in $Output) {
        if ($line -match ': .* - (?<p>\d+) passed, (?<s>\d+) skipped, (?<f>\d+) failed.*\(of (?<t>\d+)\)\s*$') {
            $passed += [int] $Matches.p; $skipped += [int] $Matches.s; $failed += [int] $Matches.f; $total += [int] $Matches.t; $passes++
        }
    }
    if ($passes -eq 0) { return $null }
    [pscustomobject]@{ Total = $total; Passed = $passed; Failed = $failed; Skipped = $skipped; Assemblies = $passes }
}

function Get-VerifySkippedTests {
    <#
    .SYNOPSIS
        The skipped tests of an English 'dotnet test' output (DOTNET_CLI_UI_LANGUAGE=en): lines "skipped <name>" with the
        reason on the next indented line.
    #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [AllowEmptyString()] [string[]] $Output)
    for ($i = 0; $i -lt $Output.Count; $i++) {
        if ($Output[$i] -match '^\s*skipped (?<name>\S.*)$') {
            $name = $Matches.name.Trim()
            $reason = if ($i + 1 -lt $Output.Count -and $Output[$i + 1] -match '^\s{2,}(?<r>\S.*)$') { $Matches.r } else { '' }
            [pscustomobject]@{ Name = $name; Reason = $reason }
        }
    }
}

function Get-VerifyBuildWarnings {
    <# .SYNOPSIS The distinct MSBuild warning lines (": warning XX1234:") of a build output. #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [AllowEmptyString()] [string[]] $Output)
    @($Output | Where-Object { $_ -match ':\s+warning\s+[A-Za-z]+\d+\s*:' } | ForEach-Object { $_.Trim() } | Select-Object -Unique)
}

function Get-VerifyNotificationStatus {
    <# .SYNOPSIS 'permission=<p> delivered=<n>' of the status mode of the test bundle (T-020); $null when not found. #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [AllowEmptyString()] [string[]] $Output)
    foreach ($line in $Output) {
        if ($line -match '^permission=(?<p>\w+) delivered=(?<d>\d+)$') { return [pscustomobject]@{ Permission = $Matches.p; Delivered = [int] $Matches.d } }
    }
    $null
}

function ConvertFrom-VerifyNotificationList {
    <#
    .SYNOPSIS
        The JSON of the list mode of the test bundle (--layerpeek-e2e-notification-list, T-212): an object with
        Notifications and Categories (always arrays); $null when the output is no list of that bundle.
    #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [AllowEmptyString()] [string[]] $Output, [Parameter(Mandatory)] [string] $BundleIdentifier)
    try { $json = ($Output -join "`n") | ConvertFrom-Json -Depth 10 -ErrorAction Stop } catch { return $null }
    if (-not $json -or $json.bundleIdentifier -cne $BundleIdentifier) { return $null }
    [pscustomobject]@{ Notifications = @($json.notifications | Where-Object { $_ }); Categories = @($json.categories | Where-Object { $_ }) }
}

function Get-VerifyNotificationProblems {
    <#
    .SYNOPSIS
        What a delivered notification of the list mode (T-212) does differently from -Expected: Title (exact), BodyPrefix,
        BodyLine (one line of the body, exact), HasSound, Attachments (count), AttachmentType (exact), LaunchArgument (exact),
        ActionArgumentPrefix, and Category with ActionTitle: the category must be registered (-Categories) with a button of
        that title that does not bring the app to the front (UNNotificationActionOptionForeground, 4). Empty: as expected.
    #>
    param($Notification, [Parameter(Mandatory)] [System.Collections.IDictionary] $Expected, [object[]] $Categories = @())
    if (-not $Notification) { return @('no such notification was delivered') }
    $problems = [System.Collections.Generic.List[string]]::new()
    $lines = @("$($Notification.body)" -split "`n")
    if ($Expected.Contains('Title') -and "$($Notification.title)" -cne $Expected.Title) { $problems.Add("title '$($Notification.title)', expected '$($Expected.Title)'") }
    if ($Expected.Contains('BodyPrefix') -and -not "$($Notification.body)".StartsWith($Expected.BodyPrefix, [StringComparison]::Ordinal)) { $problems.Add("text '$($Notification.body -replace "`n", ' / ')' does not start with '$($Expected.BodyPrefix)'") }
    if ($Expected.Contains('BodyLine') -and $lines -cnotcontains $Expected.BodyLine) { $problems.Add("text '$($Notification.body -replace "`n", ' / ')' has no line '$($Expected.BodyLine)'") }
    if ($Expected.Contains('HasSound') -and [bool] $Notification.hasSound -ne [bool] $Expected.HasSound) { $problems.Add("sound $(if ($Notification.hasSound) { 'on' } else { 'off' }), expected $(if ($Expected.HasSound) { 'on' } else { 'off' })") }
    $attachments = @($Notification.attachments | Where-Object { $_ })
    if ($Expected.Contains('Attachments') -and $attachments.Count -ne $Expected.Attachments) { $problems.Add("$($attachments.Count) attachment(s), expected $($Expected.Attachments)") }
    if ($Expected.Contains('AttachmentType')) {
        foreach ($attachment in $attachments) { if ("$($attachment.type)" -cne $Expected.AttachmentType) { $problems.Add("attachment of type '$($attachment.type)', expected '$($Expected.AttachmentType)'") } }
    }
    if ($Expected.Contains('LaunchArgument') -and "$($Notification.launchArgument)" -cne $Expected.LaunchArgument) { $problems.Add("click argument '$($Notification.launchArgument)', expected '$($Expected.LaunchArgument)'") }
    if ($Expected.Contains('ActionArgumentPrefix') -and -not "$($Notification.actionArgument)".StartsWith($Expected.ActionArgumentPrefix, [StringComparison]::Ordinal)) { $problems.Add("button argument '$($Notification.actionArgument)' does not start with '$($Expected.ActionArgumentPrefix)'") }
    if ($Expected.Contains('Category')) {
        if ("$($Notification.categoryIdentifier)" -cne $Expected.Category) { $problems.Add("category '$($Notification.categoryIdentifier)', expected '$($Expected.Category)'") }
        $category = @($Categories | Where-Object { $_ -and "$($_.identifier)" -ceq $Expected.Category }) | Select-Object -First 1
        if (-not $category) { $problems.Add("the category '$($Expected.Category)' is not registered") }
        elseif ($Expected.Contains('ActionTitle')) {
            $action = @($category.actions | Where-Object { $_ -and "$($_.title)" -ceq $Expected.ActionTitle }) | Select-Object -First 1
            if (-not $action) { $problems.Add("the category '$($Expected.Category)' has no button '$($Expected.ActionTitle)' (buttons: $(@($category.actions | ForEach-Object { "'$($_.title)'" }) -join ', '))") }
            elseif (([long] $action.options -band 4) -ne 0) { $problems.Add("the button '$($Expected.ActionTitle)' brings the app to the front (options $($action.options))") }
        }
    }
    $problems.ToArray()
}

function Get-VerifyFrontAsn {
    <# .SYNOPSIS The ASN of 'lsappinfo front' (e.g. ASN:0x0-0x1d01d:); $null when none. #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [AllowEmptyString()] [string[]] $Output)
    foreach ($line in $Output) { if ($line -match '(ASN:0x[0-9a-fA-F]+-0x[0-9a-fA-F]+:)') { return $Matches[1] } }
    $null
}

function Get-VerifyLsappinfoPid {
    <# .SYNOPSIS The pid of 'lsappinfo info -only pid <asn>' ("pid"=1234 or pid = 1234); $null when none. #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [AllowEmptyString()] [string[]] $Output)
    foreach ($line in $Output) { if ($line -match '"?pid"?\s*=\s*(?<pid>\d+)') { return [int] $Matches.pid } }
    $null
}

function Get-VerifySpctlAssessment {
    <#
    .SYNOPSIS
        The verdict of 'spctl --assess -vv' (exit code and output): Accepted, the verdict word ('accepted', 'rejected') and
        the 'source=' and 'origin=' values when printed.
    #>
    param([Parameter(Mandatory)] [int] $ExitCode, [Parameter(Mandatory)] [AllowEmptyCollection()] [AllowEmptyString()] [string[]] $Output)
    $verdict = $null; $source = $null; $origin = $null
    foreach ($line in $Output) {
        if ($line -match ':\s*(?<v>accepted|rejected)\b') { $verdict = $Matches.v }
        elseif ($line -match '^\s*source=(?<s>.+)$') { $source = $Matches.s.Trim() }
        elseif ($line -match '^\s*origin=(?<o>.+)$') { $origin = $Matches.o.Trim() }
    }
    [pscustomobject]@{ Accepted = ($ExitCode -eq 0 -and $verdict -ne 'rejected'); Verdict = $verdict; Source = $source; Origin = $origin }
}

function Get-VerifyCredentialTargets {
    <# .SYNOPSIS The targets of 'cmdkey /list' that contain LayerPeek (labels are localized: only the target values are matched). #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [AllowEmptyString()] [string[]] $Output)
    @($Output | ForEach-Object { foreach ($m in [regex]::Matches([string] $_, '(?i)target=(LayerPeek\S*)')) { $m.Groups[1].Value } } | Sort-Object -Unique -CaseSensitive)
}

# ---------------------------------------------------------------------------------------------------------------------
# macOS test bundle, instance channel, window placement (pure parts)
# ---------------------------------------------------------------------------------------------------------------------

function ConvertTo-VerifyTestInfoPlist {
    <#
    .SYNOPSIS
        The Info.plist of the test bundle "LayerPeek E2E.app" from the production template (pure; the same rule as
        MacNotificationsE2ETests.TestBundle.TestInfoPlist): identifier + .E2E, version 0.0.0, name "LayerPeek E2E", no icon.
    #>
    param([Parameter(Mandatory)] [string] $ProductionText, [Parameter(Mandatory)] [string] $ProductionIdentifier)
    $text = $ProductionText.Replace("<string>$ProductionIdentifier</string>", "<string>$ProductionIdentifier.E2E</string>").Replace('@VERSION@', '0.0.0')
    $text = [regex]::Replace($text, '(<key>CFBundle(Display)?Name</key>\s*<string>)LayerPeek(</string>)', '${1}LayerPeek E2E${3}')
    [regex]::Replace($text, '\s*<key>CFBundleIconFile</key>\s*<string>[^<]*</string>', '')
}

function Get-VerifyInstanceSocketPath {
    <#
    .SYNOPSIS
        Where the test app's instance socket is (the rule of InstanceTransports.UnixPaths, T-053): instance-<suffix>.sock in
        the data folder, or (path longer than 103 bytes) in the private folder LayerPeek-<uid> of the temp folder.
    #>
    param([Parameter(Mandatory)] [string] $DataDirectory, [Parameter(Mandatory)] [string] $Suffix, [Parameter(Mandatory)] [string] $TempDirectory, [Parameter(Mandatory)] [int] $UserId)
    $socket = Join-Path $DataDirectory "instance-$Suffix.sock"
    if ([System.Text.Encoding]::UTF8.GetByteCount($socket) -le 103) { return $socket }
    Join-Path $TempDirectory "LayerPeek-$UserId" "instance-$Suffix.sock"
}

function Send-VerifyInstanceCommand {
    <#
    .SYNOPSIS
        Sends one command of the instance channel (T-043/T-044 wire format: an ASCII word and a line feed) to a Unix socket
        and waits for the acknowledgement (a line feed). $true when it was acknowledged.
    #>
    param([Parameter(Mandatory)] [string] $SocketPath, [Parameter(Mandatory)] [string] $Command, [int] $TimeoutMilliseconds = 5000)
    if ($Command -cnotmatch '^[a-z]+(:[A-Za-z0-9_-]+)?$') { throw "Not an instance command: $Command" }
    $socket = [System.Net.Sockets.Socket]::new([System.Net.Sockets.AddressFamily]::Unix, [System.Net.Sockets.SocketType]::Stream, [System.Net.Sockets.ProtocolType]::Unspecified)
    try {
        $socket.ReceiveTimeout = $TimeoutMilliseconds
        $socket.SendTimeout = $TimeoutMilliseconds
        $socket.Connect([System.Net.Sockets.UnixDomainSocketEndPoint]::new($SocketPath))
        $null = $socket.Send([System.Text.Encoding]::ASCII.GetBytes("$Command`n"))
        $buffer = [byte[]]::new(1)
        $read = $socket.Receive($buffer)
        $read -eq 1 -and $buffer[0] -eq 10
    }
    # No server, a refused connection, a timeout, or a path a Unix socket cannot have (longer than 104 bytes on macOS).
    catch [System.Net.Sockets.SocketException], [System.ArgumentException], [System.Management.Automation.MethodInvocationException] { $false }
    finally { $socket.Dispose() }
}

function Get-VerifyCornerProblems {
    <#
    .SYNOPSIS
        Problems of a camera window's frame against its corner rule (T-035/T-038; empty = fine): the whole frame inside the
        usable area (never under the menu bar or the Dock) and -Margin points from both edges of the corner (centred for
        Center), within -Tolerance. Rectangles are X, Y, Width, Height in points with the origin top left (CGWindowList).
    #>
    param(
        [Parameter(Mandatory)] $Window,
        [Parameter(Mandatory)] $Area,
        [Parameter(Mandatory)] [ValidateSet('TopLeft', 'TopRight', 'BottomLeft', 'BottomRight', 'Center')] [string] $Corner,
        [double] $Margin = 16,
        [double] $Tolerance = 2
    )
    $left = $Window.X - $Area.X
    $top = $Window.Y - $Area.Y
    $right = ($Area.X + $Area.Width) - ($Window.X + $Window.Width)
    $bottom = ($Area.Y + $Area.Height) - ($Window.Y + $Window.Height)
    $t = $Tolerance
    if ($left -lt -$t -or $top -lt -$t -or $right -lt -$t -or $bottom -lt -$t) {
        "the window ($($Window.X), $($Window.Y), $($Window.Width) x $($Window.Height)) is not inside the usable area ($($Area.X), $($Area.Y), $($Area.Width) x $($Area.Height))"
    }
    $expect = {
        param($Name, $Actual, $Wanted)
        if ([math]::Abs($Actual - $Wanted) -gt $t) { "$Name distance is $([math]::Round($Actual, 1)) pt, expected $Wanted pt" }
    }
    switch ($Corner) {
        'TopLeft' { & $expect 'left' $left $Margin; & $expect 'top' $top $Margin }
        'TopRight' { & $expect 'right' $right $Margin; & $expect 'top' $top $Margin }
        'BottomLeft' { & $expect 'left' $left $Margin; & $expect 'bottom' $bottom $Margin }
        'BottomRight' { & $expect 'right' $right $Margin; & $expect 'bottom' $bottom $Margin }
        'Center' {
            if ([math]::Abs($left - $right) -gt 2 * $t + 1) { "not centred horizontally (left $([math]::Round($left, 1)) pt, right $([math]::Round($right, 1)) pt)" }
            if ([math]::Abs($top - $bottom) -gt 2 * $t + 1) { "not centred vertically (top $([math]::Round($top, 1)) pt, bottom $([math]::Round($bottom, 1)) pt)" }
        }
    }
}

function Get-VerifyMainWindow {
    <#
    .SYNOPSIS
        The biggest normal window (layer 0, on screen) of the helper's window list for a process, or $null. Menu bar items
        are layer 25 (kCGStatusWindowLevel) and are never returned here.
    #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Windows, [int] $MinimumSize = 50)
    @($Windows | Where-Object { $_.layer -eq 0 -and $_.onscreen -and $_.width -ge $MinimumSize -and $_.height -ge $MinimumSize } |
            Sort-Object { $_.width * $_.height } -Descending) | Select-Object -First 1
}

function Test-VerifyMenuBarItem {
    <# .SYNOPSIS Whether the helper's window list of a process has a status item (layer 25) on screen in the menu bar. #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Windows)
    [bool] ($Windows | Where-Object { $_.layer -eq 25 -and $_.width -gt 0 -and $_.height -gt 0 })
}

function ConvertTo-VerifyJsonArray {
    <#
    .SYNOPSIS
        The items as a JSON array, also for none or one item (ConvertTo-Json on the pipeline writes nothing for an empty
        array and a single object for one item, which made the evidence files invalid or ambiguous).
    #>
    param([AllowNull()] [AllowEmptyCollection()] [object[]] $Items, [int] $Depth = 4)
    ConvertTo-Json -InputObject @($Items | Where-Object { $null -ne $_ }) -Depth $Depth
}

function Get-VerifySessionLimitation {
    <#
    .SYNOPSIS
        Why window checks cannot be judged in this login session, or $null. Input: the JSON object of
        'macos-verify-helper session' (locked, onConsole, known). With a locked screen (e.g. an agent or SSH session at
        night) no app can come to the front, the menu bar shows no status items and no window enters a full-screen Space.
    #>
    param([AllowNull()] $Session)
    if (-not $Session -or -not $Session.known) { return $null }
    if ($Session.locked) { return 'the screen is locked (CGSSessionScreenIsLocked): no app comes to the front, the menu bar shows no status items and no window enters full screen' }
    if (-not $Session.onConsole) { return 'the login session is not on the console (fast user switching or a remote session): windows are not shown' }
    $null
}

function Resolve-VerifySessionResult {
    <#
    .SYNOPSIS
        A FAIL of a check that needs an unlocked session on the console becomes a SKIP with the reason when the session had
        such a limitation before or after the check; the FAIL detail stays in the SKIP text, nothing is dropped. PASS, SKIP
        and MANUAL are returned unchanged, as is a FAIL in an unlimited session.
    #>
    param([Parameter(Mandatory)] $Result, [AllowEmptyCollection()] [AllowNull()] [string[]] $Limitations)
    $reason = @($Limitations | Where-Object { $_ }) | Select-Object -First 1
    if ($Result.Status -ne 'FAIL' -or -not $reason) { return $Result }
    New-VerifyResult SKIP "cannot be judged in this session: $reason. Run it again in your own unlocked session. Failed here: $($Result.Detail)"
}

function Get-VerifyMenuBarOwners {
    <#
    .SYNOPSIS
        "owner (pid) x count" for the status item windows (layer 25) of all processes in the helper's 'menubar' list, for
        the FAIL text of mac.menubar: shows whether this macOS version draws the status items in another process.
    #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Windows)
    @($Windows | Where-Object { $_.layer -eq 25 } | Group-Object { "$($_.owner) ($($_.pid))" } | Sort-Object Name | ForEach-Object { "$($_.Name) x $($_.Count)" })
}

function Get-VerifyTopWindow {
    <#
    .SYNOPSIS
        The window a click at a point reaches, from the helper's 'windowsat' list (front to back): the first one of a
        normal or floating level (0 to 24); overlays above the menu bar (e.g. display tools, the cursor) let clicks through,
        and so does a window of the Dock larger than 200 x 200 pt (the Dock level 20 covering a whole display, seen with a
        Screen Sharing display 2026-11-15: real clicks reached the windows under it; the Dock itself is narrower).
    #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Windows)
    @($Windows | Where-Object { $_.layer -ge 0 -and $_.layer -lt 25 -and -not ($_.owner -ceq 'Dock' -and $_.width -gt 200 -and $_.height -gt 200) }) | Select-Object -First 1
}

function ConvertFrom-VerifyAxWatch {
    <# .SYNOPSIS The JSON lines of 'macos-verify-helper axwatch' as objects (ms, found, text, frame); other lines (errors) are skipped. #>
    param([AllowNull()] [AllowEmptyCollection()] [string[]] $Lines)
    foreach ($line in @($Lines)) {
        if (-not "$line".TrimStart().StartsWith('{')) { continue }
        try { "$line" | ConvertFrom-Json } catch { Write-Verbose "Not a watch line: $line" }
    }
}

function Get-VerifyUpdateCheckVerdict {
    <#
    .SYNOPSIS
        Judges one press of "Check now" (mac.update-check) from the timeline of SettingsUpdateCheckResult
        (ConvertFrom-VerifyAxWatch; the first entry is the state before the press) and the number of requests the stand-in
        GitHub API got meanwhile. Passed when a visible text containing -Expected appeared after the press and is still
        there at the end; when it was shown already before the press, only a showing after "Checking…" counts.
        Returns Passed, Detail (a sentence for the report), Initial, Checking, Shown and Final (timeline entries or $null).
    #>
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Timeline,
        [int] $Requests,
        [Parameter(Mandatory)] [string] $Expected,
        [string] $Checking = 'Checking'
    )
    $describe = {
        param($Entry)
        if (-not $Entry) { 'nothing' } elseif (-not $Entry.found) { 'no element (hidden)' } elseif (-not "$($Entry.text)") { 'an empty text' } else { "'$($Entry.text)'" }
    }
    $isShown = { param($Entry) $Entry -and $Entry.found -and "$($Entry.text)".Contains($Expected) -and $Entry.frame.width -gt 0 -and $Entry.frame.height -gt 0 }
    $requestText = "$Requests request(s) to the stand-in GitHub API"
    if ($Timeline.Count -eq 0) {
        return [pscustomobject]@{ Passed = $false; Detail = "the Accessibility watch recorded nothing ($requestText)."; Initial = $null; Checking = $null; Shown = $null; Final = $null }
    }
    $initial = $Timeline[0]
    $later = @($Timeline | Select-Object -Skip 1)
    $checkingEntry = @($later | Where-Object { "$($_.text)".StartsWith($Checking) }) | Select-Object -First 1
    $candidates = if (-not (& $isShown $initial)) { $later } elseif ($checkingEntry) { @($later | Where-Object { $_.ms -gt $checkingEntry.ms }) } else { @() }
    $shown = @($candidates | Where-Object { & $isShown $_ }) | Select-Object -First 1
    $final = $Timeline[-1]
    $texts = (@($Timeline | ForEach-Object { "$($_.ms) ms: $(& $describe $_)" })) -join ' -> '
    $checkingText = if ($checkingEntry) { "'$($checkingEntry.text)' at $($checkingEntry.ms) ms" } else { "no '$Checking…' seen" }
    $passed = [bool] ($shown -and (& $isShown $final))
    $detail = if ($passed) { "result '$($shown.text)' shown $($shown.ms) ms after the watch started ($checkingText, $requestText)" }
    elseif ($shown) { "the result was shown at $($shown.ms) ms but then changed to $(& $describe $final) ($requestText; timeline: $texts)" }
    elseif ($Requests -eq 0 -and -not $checkingEntry) { "the press ran no check: no request reached the stand-in GitHub API, no '$Checking…', SettingsUpdateCheckResult stayed $(& $describe $initial) (timeline: $texts)" }
    else { "the check ran ($requestText, $checkingText) but SettingsUpdateCheckResult never showed '$Expected' (timeline: $texts)" }
    [pscustomobject]@{ Passed = $passed; Detail = $detail; Initial = $initial; Checking = $checkingEntry; Shown = $shown; Final = $final }
}

# ---------------------------------------------------------------------------------------------------------------------
# The status item menu (T-209, mac.menu-items)
# ---------------------------------------------------------------------------------------------------------------------

function Get-VerifyResxStrings {
    <# .SYNOPSIS The values of a .resx file by name (ordinal), e.g. src/LayerPeek.App/Resources/Strings.resx. #>
    param([Parameter(Mandatory)] [string] $Path)
    $table = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    $xml = [xml] (Get-Content -LiteralPath $Path -Raw)
    foreach ($data in $xml.root.data) { $table[$data.name] = [string] $data.value }
    $table
}

function ConvertFrom-VerifyAccessText {
    <#
    .SYNOPSIS
        The text a menu item shows for a resource text with an access key: the first single "_" (the marker) removed and
        "__" shown as "_", like Avalonia gives it to NSMenu (NativeMenuText.DisplayTextOf in the app).
    #>
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Text)
    $marker = -1
    for ($i = 0; $i -lt $Text.Length - 1; $i++) {
        if ($Text[$i] -ne '_') { continue }
        if ($Text[$i + 1] -ne '_') { $marker = $i; break }
        $i++
    }
    $shown = if ($marker -ge 0) { $Text.Remove($marker, 1) } else { $Text }
    $shown.Replace('__', '_').Replace([string] [char] 0x2060, '')
}

function Get-VerifyMenuEntries {
    <# .SYNOPSIS The items of the helper's 'menu' JSON (or of a submenu) without the separators. #>
    param([AllowNull()] [AllowEmptyCollection()] [object[]] $Items)
    @($Items | Where-Object { $_ -and -not $_.separator })
}

function Get-VerifyMenuHelps {
    <#
    .SYNOPSIS
        "path: disabled, help 'text'" for every disabled item of the helper's 'menu' JSON, submenus included: whether the
        app's tooltip of a disabled item (its reason) reaches NSMenu as AXHelp (the open question of T-146 / T-148 / T-161).
    #>
    param([AllowNull()] [AllowEmptyCollection()] [object[]] $Items, [string] $Path = '')
    foreach ($item in Get-VerifyMenuEntries $Items) {
        $here = if ($Path) { "$Path>$($item.title)" } else { $item.title }
        if (-not $item.enabled) { "${here}: disabled, help $(if ($item.help) { "'$($item.help)'" } else { 'none' })" }
        if ($item.submenu) { Get-VerifyMenuHelps -Items $item.submenu -Path $here }
    }
}

function Get-VerifyMenuHeaderPattern {
    <#
    .SYNOPSIS
        The regular expression of the printer item "Name: State" (with " – 42%" when -Progress), built from the resource
        formats MenuPrinter, ToolTipStateWithProgress and ToolTipProgress (texts: Get-VerifyResxStrings).
    #>
    param([Parameter(Mandatory)] $Texts, [Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [string] $State, [switch] $Progress)
    $percent = [char] 0xE000
    $plain = [regex]::Escape(($Texts['MenuPrinter'] -f $Name, $State))
    if (-not $Progress) { return "^$plain$" }
    $withProgress = $Texts['MenuPrinter'] -f $Name, ($Texts['ToolTipStateWithProgress'] -f $State, ($Texts['ToolTipProgress'] -f $percent))
    "^($plain|$([regex]::Escape($withProgress).Replace([string] $percent, '\d+')))$"
}

function Get-VerifyMenuProblems {
    <#
    .SYNOPSIS
        What is wrong with the menu of the test app with one printer in one state (empty = fine), from the helper's 'menu'
        JSON: the printer item "Name: State" with its submenu (T-042: "Open PrusaLink" first; with -Camera the camera and
        recording items of T-014 / T-146 / T-161, "Play last …" disabled without a file), then "Diagnostics…" and
        "Settings…" (enabled), "User manual", "About" and "Quit" as the last item. Texts: Get-VerifyResxStrings.
    #>
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Items,
        [Parameter(Mandatory)] $Texts,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $State,
        [switch] $Progress,
        [switch] $Camera
    )
    $text = { param($Key) ConvertFrom-VerifyAccessText $Texts[$Key] }
    $entries = @(Get-VerifyMenuEntries $Items)
    if ($entries.Count -eq 0) { return @('the menu has no items') }
    $problems = [System.Collections.Generic.List[string]]::new()
    $printer = $entries[0]
    $pattern = Get-VerifyMenuHeaderPattern -Texts $Texts -Name $Name -State $State -Progress:$Progress
    if ($printer.title -cnotmatch $pattern) { $problems.Add("the printer item is '$($printer.title)', expected '$(($Texts['MenuPrinter'] -f $Name, $State))'$(if ($Progress) { ' (with or without progress)' })") }
    $sub = @(Get-VerifyMenuEntries $printer.submenu)
    if ($sub.Count -eq 0) { $problems.Add("the printer item '$($printer.title)' has no submenu items") }
    else {
        if ($sub[0].title -cne (& $text 'MenuOpenPrusaLink') -or -not $sub[0].enabled) { $problems.Add("the first submenu item is '$($sub[0].title)' (enabled: $($sub[0].enabled)), expected '$(& $text 'MenuOpenPrusaLink')' enabled") }
        $titles = @($sub.title)
        if ($Camera) {
            $cameraTitles = @((& $text 'MenuShowCamera'), (& $text 'MenuHideCamera'))
            if (-not ($sub | Where-Object { $cameraTitles -ccontains $_.title -and $_.enabled })) { $problems.Add("no enabled '$($cameraTitles[0])' in the submenu ($($titles -join ', '))") }
            foreach ($key in 'MenuStartRecording', 'MenuStartTimelapse', 'MenuPlayLastRecording', 'MenuPlayLastTimelapse', 'MenuOpenRecordingsFolder') {
                if ($titles -cnotcontains (& $text $key)) { $problems.Add("no '$(& $text $key)' in the submenu ($($titles -join ', '))") }
            }
            foreach ($key in 'MenuPlayLastRecording', 'MenuPlayLastTimelapse') {
                if ($sub | Where-Object { $_.title -ceq (& $text $key) -and $_.enabled }) { $problems.Add("'$(& $text $key)' is enabled without any file") }
            }
            if (-not @($printer.submenu | Where-Object { $_.separator })) { $problems.Add('no separator before the recording items in the submenu') }
        }
        elseif ($sub.Count -ne 1) { $problems.Add("a printer without a camera has submenu items $($titles -join ', '), expected only '$(& $text 'MenuOpenPrusaLink')'") }
    }
    $fixed = @($entries | Select-Object -Skip 1 | ForEach-Object title)
    $expected = @('MenuDiagnostics', 'MenuSettings', 'MenuManual', 'MenuAbout', 'MenuQuit' | ForEach-Object { & $text $_ })
    if (($fixed -join "`n") -cne ($expected -join "`n")) { $problems.Add("the items after the printer are '$($fixed -join "', '")', expected '$($expected -join "', '")'") }
    foreach ($key in 'MenuDiagnostics', 'MenuSettings', 'MenuQuit') {
        if ($entries | Where-Object { $_.title -ceq (& $text $key) -and -not $_.enabled }) { $problems.Add("'$(& $text $key)' is disabled") }
    }
    $problems.ToArray()
}

function Get-VerifyToolTipProblem {
    <#
    .SYNOPSIS
        Why the tooltip of the status item (AXHelp from the helper's 'statusitems') does not show the printer in the state,
        or $null: "[E2E] Name" on the first line, the state (with " – 42%" when -Progress) at the start of the second.
    #>
    param([AllowNull()] [AllowEmptyString()] [string] $ToolTip, [Parameter(Mandatory)] $Texts, [Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [string] $State, [switch] $Progress)
    $lines = @("$ToolTip" -split "`n")
    $percent = [char] 0xE000
    $stateText = [regex]::Escape($State)
    if ($Progress) { $stateText = "($stateText|$([regex]::Escape(($Texts['ToolTipStateWithProgress'] -f $State, ($Texts['ToolTipProgress'] -f $percent))).Replace([string] $percent, '\d+')))" }
    if ($lines.Count -lt 2 -or $lines[0] -cne "[E2E] $Name" -or $lines[1] -cnotmatch "^$stateText$") {
        return "the tooltip is '$($ToolTip -replace "`n", ' | ')', expected '[E2E] $Name | $State$(if ($Progress) { ' (– N%)' })'"
    }
    $null
}

function ConvertFrom-VerifyRecordingFileName {
    <#
    .SYNOPSIS
        The parts of the name of a camera recording (T-145 / T-148, CameraRecordingFolders in Core), or $null for any other
        name: Stamp (yyyy-MM-dd_HH-mm-ss), Title ('' without one), Kind ('print', 'dashcam' or 'timelapse'), Trigger of a
        dashcam save (error, attention, stopped, manual, failure; '' otherwise) and Segment (1, or N of a "-N" suffix: the
        file after the Nth camera outage).
    #>
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Name)
    if ($Name -cnotmatch '^(?<stamp>\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2})(?:_(?<rest>.+?))?(?:-(?<segment>[2-9]|[1-9]\d+))?\.mp4$') { return $null }
    # Optional groups that did not match are missing from $Matches (strict mode): the indexer gives $null.
    $stamp = $Matches['stamp']
    $rest = [string] $Matches['rest']
    $segment = if ($Matches['segment']) { [int] $Matches['segment'] } else { 1 }
    $kind = 'print'
    $trigger = ''
    $title = $rest
    if ($rest -cmatch '^(?:(?<title>.+)_)?dashcam-(?<trigger>error|attention|stopped|manual|failure)$') { $kind = 'dashcam'; $trigger = $Matches['trigger']; $title = [string] $Matches['title'] }
    elseif ($rest -cmatch '^(?:(?<title>.+?)_)?(?:recording_)?timelapse$') { $kind = 'timelapse'; $title = [string] $Matches['title'] }
    [pscustomobject]@{ Stamp = $stamp; Title = $title; Kind = $kind; Trigger = $trigger; Segment = $segment }
}

function Get-VerifyDecodeProblems {
    <#
    .SYNOPSIS
        What is wrong with a recording by the JSON of the helper's 'decode' (T-210) and its exit code (empty = fine): the
        helper's own problems, codec avc1, five seeks without an error, and optionally the decoded frames (-Frames) and the
        duration (-MinSeconds, -MaxSeconds).
    #>
    param([AllowNull()] $Report, [int] $ExitCode, [int] $Frames = -1, [double] $MinSeconds = -1, [double] $MaxSeconds = -1)
    if (-not $Report) { return @("the helper printed no JSON (exit code $ExitCode)") }
    $problems = [System.Collections.Generic.List[string]]::new()
    foreach ($problem in @($Report.problems)) { if ($problem) { $problems.Add([string] $problem) } }
    if ($ExitCode -ne 0 -and $problems.Count -eq 0) { $problems.Add("exit code $ExitCode without a problem") }
    if ($ExitCode -eq 0 -and $problems.Count -gt 0) { $problems.Add('exit code 0 despite the problems') }
    if ($Report.PSObject.Properties['codec'] -and $Report.codec -cne 'avc1') { $problems.Add("codec '$($Report.codec)', expected avc1") }
    $seeks = @($Report.seeks | Where-Object { $_ })
    if ($problems.Count -eq 0 -and $seeks.Count -ne 5) { $problems.Add("$($seeks.Count) seek(s), expected 5 (0, 25, 50, 75 % and the end)") }
    if ($Frames -ge 0 -and $Report.decodedFrames -ne $Frames) { $problems.Add("$($Report.decodedFrames) decoded frame(s), expected $Frames") }
    $duration = [double] $Report.duration
    if ($MinSeconds -ge 0 -and $duration -lt $MinSeconds) { $problems.Add("duration $duration s, expected at least $MinSeconds s") }
    if ($MaxSeconds -ge 0 -and $duration -gt $MaxSeconds) { $problems.Add("duration $duration s, expected at most $MaxSeconds s") }
    $problems.ToArray()
}

function Get-VerifyMdatCut {
    <#
    .SYNOPSIS
        Where to cut an MP4 file so that a sample is incomplete (T-210: a damaged recording for 'decode'): the middle of the
        middle 'mdat' box among the top-level boxes; $null when there is none.
    #>
    param([Parameter(Mandatory)] [byte[]] $Bytes)
    $boxes = [System.Collections.Generic.List[object]]::new()
    $offset = [long] 0
    while ($offset + 8 -le $Bytes.Length) {
        $size = ([long] $Bytes[$offset] -shl 24) -bor ([long] $Bytes[$offset + 1] -shl 16) -bor ([long] $Bytes[$offset + 2] -shl 8) -bor [long] $Bytes[$offset + 3]
        $type = [System.Text.Encoding]::ASCII.GetString($Bytes, [int] $offset + 4, 4)
        if ($size -eq 1 -and $offset + 16 -le $Bytes.Length) {
            $size = [long] 0
            for ($i = 8; $i -lt 16; $i++) { $size = ($size -shl 8) -bor [long] $Bytes[$offset + $i] }
        }
        elseif ($size -eq 0) { $size = $Bytes.Length - $offset }
        if ($size -lt 8) { break }
        $boxes.Add([pscustomobject]@{ Type = $type; Offset = $offset; Size = $size })
        $offset += $size
    }
    $mdats = @($boxes | Where-Object Type -CEQ 'mdat')
    if ($mdats.Count -eq 0) { return $null }
    $middle = $mdats[[int] [math]::Floor($mdats.Count / 2)]
    [long] ($middle.Offset + [math]::Floor($middle.Size / 2))
}

function Get-VerifyRecordingEvents {
    <#
    .SYNOPSIS
        The recording events in the app's log, in order (T-210): Writes (a file was started, File = its name), Ended (a
        recording ended, Files = its file count), DashcamSaved (Files), Failed (a recording or dashcam could not start or
        failed, Text = the line), TimelapseStarted (T-211, File = its name, Mode = Manual or Automatic) and TimelapseFailed
        (a timelapse could not start or stopped with an error). PrinterId of each.
    #>
    param([AllowNull()] [AllowEmptyString()] [string] $Log)
    foreach ($line in @("$Log" -split "`n")) {
        if ($line -cmatch "Recording of printer '(?<id>[^']+)' writes '(?<file>[^']+)'") { [pscustomobject]@{ Kind = 'Writes'; PrinterId = $Matches.id; File = $Matches.file; Files = 0; Text = $line.Trim() } }
        elseif ($line -cmatch "Recording of printer '(?<id>[^']+)' ended \((?<n>\d+) file\(s\)\)") { [pscustomobject]@{ Kind = 'Ended'; PrinterId = $Matches.id; File = ''; Files = [int] $Matches.n; Text = $line.Trim() } }
        elseif ($line -cmatch "Dashcam of printer '(?<id>[^']+)' saved [\d.,]+ s into (?<n>\d+) file\(s\)") { [pscustomobject]@{ Kind = 'DashcamSaved'; PrinterId = $Matches.id; File = ''; Files = [int] $Matches.n; Text = $line.Trim() } }
        elseif ($line -cmatch "(?:Recording|Dashcam) of printer '(?<id>[^']+)' (?:could not start|could not save|failed)") { [pscustomobject]@{ Kind = 'Failed'; PrinterId = $Matches.id; File = ''; Files = 0; Text = $line.Trim() } }
        elseif ($line -cmatch "Timelapse of printer '(?<id>[^']+)' started \((?<mode>\w+)\) into '(?<file>[^']+)'") { [pscustomobject]@{ Kind = 'TimelapseStarted'; PrinterId = $Matches.id; File = $Matches.file; Files = 0; Mode = $Matches.mode; Text = $line.Trim() } }
        elseif ($line -cmatch "(?:Timelapse of printer '(?<id>[^']+)' could not start|Stopping the timelapse of printer '(?<id>[^']+)': )") { [pscustomobject]@{ Kind = 'TimelapseFailed'; PrinterId = $Matches.id; File = ''; Files = 0; Text = $line.Trim() } }
    }
}

function Get-VerifyTimelapseFrameRange {
    <#
    .SYNOPSIS
        How many frames a timelapse by time (T-160) may have (T-211): each span in which frames were taken (from the start
        or a resume after a pause to the pause or the stop, -Spans in seconds) gives one frame at once and one every
        -IntervalSeconds. -SlackSeconds per span covers what the script cannot time exactly: the poll that notices a state,
        the start of the timelapse and the key frame each frame waits for. Min (at least one per span) and Max.
    #>
    param([Parameter(Mandatory)] [double[]] $Spans, [Parameter(Mandatory)] [ValidateRange(1, 3600)] [int] $IntervalSeconds, [ValidateRange(0, 600)] [double] $SlackSeconds = 0)
    $min = 0
    $max = 0
    foreach ($span in $Spans) {
        $min += 1 + [math]::Floor([math]::Max(0, $span - $SlackSeconds) / $IntervalSeconds)
        $max += 1 + [math]::Floor(($span + $SlackSeconds) / $IntervalSeconds)
    }
    [pscustomobject]@{ Min = [int] $min; Max = [int] $max }
}

function Get-VerifyLayerTimelapseFrames {
    <#
    .SYNOPSIS
        The frames of a timelapse by layer (T-159) of a whole print of -Layers layers that rise evenly (the simulator's
        --layers, T-211): one at the start, one at every -EveryNthLayer-th of the Layers - 1 layer changes and one of the
        finished print. A pause adds none.
    #>
    param([Parameter(Mandatory)] [ValidateRange(1, 1000)] [int] $Layers, [ValidateRange(1, 100)] [int] $EveryNthLayer = 1)
    [int] (2 + [math]::Floor(($Layers - 1) / $EveryNthLayer))
}

# ---------------------------------------------------------------------------------------------------------------------
# Windows, the Dock and the camera image (T-215, mac.dock-windows, mac.mini-after-fullscreen, mac.camera-live,
# mac.camera-next-attempt; pure)
# ---------------------------------------------------------------------------------------------------------------------

function Format-VerifyBounds {
    <# .SYNOPSIS "320 x 180 pt at 951, 619" of a window or frame (x, y, width, height in points). #>
    param([AllowNull()] $Bounds)
    if (-not $Bounds) { return 'no window' }
    '{0:0.#} x {1:0.#} pt at {2:0.#}, {3:0.#}' -f [double] $Bounds.width, [double] $Bounds.height, [double] $Bounds.x, [double] $Bounds.y
}

function Get-VerifyBoundsProblem {
    <#
    .SYNOPSIS
        How the bounds of a window (x, y, width, height in points) differ from the expected ones by more than -Tolerance
        points, or $null when they match (-SizeOnly: the size only).
    #>
    param([AllowNull()] $Actual, [Parameter(Mandatory)] $Expected, [double] $Tolerance = 1, [switch] $SizeOnly)
    if (-not $Actual) { return 'no window' }
    $names = if ($SizeOnly) { 'width', 'height' } else { 'x', 'y', 'width', 'height' }
    $off = @($names | Where-Object { [math]::Abs([double] $Actual.$_ - [double] $Expected.$_) -gt $Tolerance })
    if ($off.Count -eq 0) { return $null }
    "$(Format-VerifyBounds $Actual), expected $(Format-VerifyBounds $Expected) (off: $($off -join ', '); tolerance $Tolerance pt)"
}

function Get-VerifyWindowCorner {
    <#
    .SYNOPSIS
        The corner of an area (a screen's usable area) a window is nearest to and its distances from the four edges
        (Corner TopLeft / TopRight / BottomLeft / BottomRight, Left, Top, Right, Bottom in points; origin top left).
    #>
    param([Parameter(Mandatory)] $Window, [Parameter(Mandatory)] $Area)
    $left = [double] $Window.x - [double] $Area.x
    $top = [double] $Window.y - [double] $Area.y
    $right = ([double] $Area.x + [double] $Area.width) - ([double] $Window.x + [double] $Window.width)
    $bottom = ([double] $Area.y + [double] $Area.height) - ([double] $Window.y + [double] $Window.height)
    [pscustomobject]@{
        Corner = "$(if ($top -le $bottom) { 'Top' } else { 'Bottom' })$(if ($left -le $right) { 'Left' } else { 'Right' })"
        Left   = [math]::Round($left, 1)
        Top    = [math]::Round($top, 1)
        Right  = [math]::Round($right, 1)
        Bottom = [math]::Round($bottom, 1)
    }
}

function Get-VerifyScreenOf {
    <# .SYNOPSIS The screen of the helper's 'screens' list that contains the centre of a window, or $null. #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Screens, [AllowNull()] $Window)
    if (-not $Window) { return $null }
    $x = [double] $Window.x + [double] $Window.width / 2
    $y = [double] $Window.y + [double] $Window.height / 2
    @($Screens | Where-Object { $x -ge $_.frame.x -and $x -lt $_.frame.x + $_.frame.width -and $y -ge $_.frame.y -and $y -lt $_.frame.y + $_.frame.height }) | Select-Object -First 1
}

function Get-VerifyDockMenuProblems {
    <#
    .SYNOPSIS
        The windows (their titles) the Dock menu of the app does not list (the helper's 'dock <app> menu' items), as
        problems; empty when every one is there.
    #>
    param([AllowEmptyCollection()] [object[]] $MenuItems = @(), [Parameter(Mandatory)] [string[]] $WindowTitles)
    $titles = @($MenuItems | Where-Object { $_ } | ForEach-Object { "$($_.title)" })
    foreach ($title in $WindowTitles) {
        if ($titles -cnotcontains $title) { "the Dock menu does not list the window '$title' (its items: $(@($titles | Where-Object { $_ } | ForEach-Object { "'$_'" }) -join ', '))" }
    }
}

function Get-VerifyWindowOrder {
    <#
    .SYNOPSIS
        Whether window -Number is above or below window -Other in the helper's 'windowsat' list (front to back): 'above',
        'below', or 'unknown' when one of them is not at that point.
    #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Windows, [int] $Number, [int] $Other)
    $numbers = @($Windows | ForEach-Object { [int] $_.number })
    $mine = [array]::IndexOf($numbers, $Number)
    $theirs = [array]::IndexOf($numbers, $Other)
    if ($mine -lt 0 -or $theirs -lt 0) { return 'unknown' }
    if ($mine -lt $theirs) { 'above' } else { 'below' }
}

function Get-VerifyRtspPeers {
    <#
    .SYNOPSIS
        The TCP connections ("127.0.0.1:52345->127.0.0.1:8554") of lsof lines (Get-RtspConnections): the same set before
        and after shows that the stream was never opened again (T-184).
    #>
    param([AllowNull()] [AllowEmptyCollection()] [string[]] $Lines)
    @(foreach ($line in @($Lines)) { if ("$line" -match '(\S+->\S+)') { $Matches[1] } }) | Sort-Object -Unique
}

function Get-VerifyLiveImageVerdict {
    <#
    .SYNOPSIS
        Whether two captures of the camera image 1 s apart differ (the helper's 'regiondiff' JSON over the image area):
        Live when at least -MinShare of the pixels differ by more than 8 levels (slightShare) or the mean difference is
        at least -MinMean. A frozen image drawn again gives 0 for both. Live and Detail.
    #>
    param([AllowNull()] $Diff, [double] $MinShare = 0.002, [double] $MinMean = 0.3)
    if (-not $Diff) { return [pscustomobject]@{ Live = $false; Detail = 'the captures could not be compared' } }
    $share = [double] $Diff.slightShare
    $mean = [double] $Diff.mean
    $detail = '{0:0.###} % of {1} pixels changed by more than 8 levels, mean difference {2:0.###}' -f ($share * 100), $Diff.pixels, $mean
    [pscustomobject]@{ Live = ($share -ge $MinShare -or $mean -ge $MinMean); Detail = $detail }
}

function Get-VerifyBlackFrames {
    <#
    .SYNOPSIS
        The captures of the helper's 'lumawatch' (JSON lines: ms, file, mean, deviation or error) whose camera image is
        black: a mean luminance below the larger of -Floor and -Share times the baseline (the median of the first
        -BaselineCount captures without error, or -Baseline). Returns Baseline, Threshold, Frames (the captures with a
        value), Errors (their count) and Black (the black ones).
    #>
    param([AllowEmptyCollection()] [object[]] $Frames = @(), [double] $Baseline = -1, [int] $BaselineCount = 5, [double] $Floor = 8, [double] $Share = 0.25)
    $failed = { param($Frame) $Frame.PSObject.Properties['error'] -and $Frame.error }
    $valid = @($Frames | Where-Object { $_ -and -not (& $failed $_) -and $_.PSObject.Properties['mean'] -and $null -ne $_.mean })
    if ($Baseline -lt 0) {
        $first = @($valid | Select-Object -First $BaselineCount | ForEach-Object { [double] $_.mean } | Sort-Object)
        $Baseline = if ($first.Count -gt 0) { $first[[math]::Floor(($first.Count - 1) / 2)] } else { 0 }
    }
    $threshold = [math]::Max($Floor, $Baseline * $Share)
    [pscustomobject]@{
        Baseline  = [math]::Round($Baseline, 1)
        Threshold = [math]::Round($threshold, 1)
        Frames    = $valid.Count
        Errors    = @($Frames | Where-Object { $_ -and (& $failed $_) }).Count
        Black     = @($valid | Where-Object { [double] $_.mean -lt $threshold })
    }
}

function ConvertFrom-VerifyNextAttemptText {
    <#
    .SYNOPSIS
        The seconds of "Next attempt in about 5 s" / "… 1 min" (T-188): -SecondsFormat and -MinutesFormat are the resource
        texts with "{0}" (CameraNextAttemptSeconds, CameraNextAttemptMinutes); minutes count as 60 s each. $null for any
        other text.
    #>
    param([AllowNull()] [AllowEmptyString()] [string] $Text, [Parameter(Mandatory)] [string] $SecondsFormat, [Parameter(Mandatory)] [string] $MinutesFormat)
    if (-not $Text) { return $null }
    foreach ($entry in @(@($SecondsFormat, 1), @($MinutesFormat, 60))) {
        $parts = $entry[0] -split '\{0\}', 2
        $pattern = '^' + (($parts | ForEach-Object { [regex]::Escape($_) -replace '\\ ', '[\s  ]' }) -join '(\d+)') + '$'
        if ($Text.Trim() -cmatch $pattern) { return [int] $Matches[1] * $entry[1] }
    }
    $null
}

function Get-VerifyNextAttemptPauses {
    <#
    .SYNOPSIS
        The pauses before the next camera attempts from the timeline of the helper's 'axwatch' on CameraNextAttempt
        (ConvertFrom-VerifyAxWatch): a pause starts when the shown seconds grow (or first appear) and goes on while they
        fall. Each pause: Start (seconds first shown), StartMs, LastMs, Values (ms, seconds, Gap: the line was hidden or
        showed another text since the previous value). Entries without the line (mini preview, minimized, a new attempt)
        only mark a gap; after a gap, seconds much lower than the time since the last value explains start a new pause.
    #>
    param([AllowEmptyCollection()] [object[]] $Timeline = @(), [Parameter(Mandatory)] [string] $SecondsFormat, [Parameter(Mandatory)] [string] $MinutesFormat)
    $pauses = [System.Collections.Generic.List[object]]::new()
    $current = $null
    $gap = $false
    foreach ($entry in @($Timeline | Where-Object { $_ })) {
        $seconds = if ($entry.found) { ConvertFrom-VerifyNextAttemptText -Text "$($entry.text)" -SecondsFormat $SecondsFormat -MinutesFormat $MinutesFormat } else { $null }
        if ($null -eq $seconds) { $gap = $true; continue }
        # A new pause: the seconds grow, or after a gap they are much lower than the time since the last value explains
        # (a new attempt in between, e.g. after the window was restored, starts again with 5 s).
        $last = if ($current) { $current.Values[-1] }
        if (-not $current -or $seconds -gt $last.Seconds -or ($gap -and $seconds -lt $last.Seconds - ([int] $entry.ms - $last.Ms) / 1000 - 2)) {
            $current = [pscustomobject]@{ Start = $seconds; StartMs = [int] $entry.ms; LastMs = [int] $entry.ms; Values = [System.Collections.Generic.List[object]]::new() }
            $pauses.Add($current)
            $gap = $false
        }
        $current.Values.Add([pscustomobject]@{ Ms = [int] $entry.ms; Seconds = $seconds; Gap = $gap })
        $current.LastMs = [int] $entry.ms
        $gap = $false
    }
    $pauses.ToArray()
}

function Get-VerifyCountdownProblems {
    <#
    .SYNOPSIS
        Problems of the camera's next attempt pauses (Get-VerifyNextAttemptPauses) against -Expected (seconds of the first
        pauses, e.g. 5, 10, 20, 40, 60): each one starts with its value (or a second less: the first text may come a moment
        late); under a minute the text falls by 1 s each second (two values without a gap between them: exactly 1 less,
        600 to 1400 ms apart); every expected pause but the last lasts its full time (no mode switch shortens it: from its
        first to its last value at least its start less 2 s).
    #>
    param([AllowEmptyCollection()] [object[]] $Pauses = @(), [Parameter(Mandatory)] [int[]] $Expected)
    $shown = @($Pauses | ForEach-Object Start)
    if ($Pauses.Count -lt $Expected.Count) { "$($Pauses.Count) pause(s) seen (starting with $($shown -join ', ') s), expected at least $($Expected.Count) ($($Expected -join ', ') s)" }
    for ($i = 0; $i -lt [math]::Min($Pauses.Count, $Expected.Count); $i++) {
        $pause = $Pauses[$i]
        $want = $Expected[$i]
        if ($pause.Start -gt $want -or $pause.Start -lt $want - 1) { "pause $($i + 1) started with $($pause.Start) s, expected $want s" }
        $values = @($pause.Values)
        for ($j = 1; $j -lt $values.Count; $j++) {
            $previous = $values[$j - 1]
            $value = $values[$j]
            if ($value.Gap -or $previous.Seconds -ge 60) { continue }
            $step = $previous.Seconds - $value.Seconds
            $elapsed = $value.Ms - $previous.Ms
            if ($step -ne 1 -or $elapsed -lt 600 -or $elapsed -gt 1400) { "pause $($i + 1) ($want s): $($previous.Seconds) s then $($value.Seconds) s after $elapsed ms (expected 1 s less after about 1000 ms)" }
        }
        $last = $values[-1].Seconds
        if ($i -lt $Expected.Count - 1 -and $i + 1 -lt $Pauses.Count -and ($last -gt 2 -or ($pause.LastMs - $pause.StartMs) -lt ($want - 2) * 1000)) {
            "pause $($i + 1) ($want s) ended early: last shown $last s, $([math]::Round(($pause.LastMs - $pause.StartMs) / 1000, 1)) s after its start"
        }
    }
}

# ---------------------------------------------------------------------------------------------------------------------
# Small checks (T-216, mac.camera-autoclose, mac.camera-pin-ax, mac.detection-region, mac.appkit-language, mac.open-fake,
# mac.hw-decoding-switch, mac.keys-czech-layout; pure)
# ---------------------------------------------------------------------------------------------------------------------

function ConvertFrom-VerifyFormattedText {
    <#
    .SYNOPSIS
        The whole numbers a text shows in the places {0}, {1}, ... of a .NET format string (a resource text), in the order
        of the placeholders, e.g. the accessible name "Bed region: 15 % from the left, ..." of the detection region editor;
        $null when the text does not have that form. Any white space of the format (also U+00A0) matches any white space.
    #>
    param([Parameter(Mandatory)] [string] $Format, [AllowNull()] [AllowEmptyString()] [string] $Text)
    if (-not $Text) { return $null }
    $pattern = [System.Text.StringBuilder]::new('^')
    $order = [System.Collections.Generic.List[int]]::new()
    foreach ($part in [regex]::Split($Format, '(\{\d+\})')) {
        if ($part -match '^\{(?<n>\d+)\}$') { $order.Add([int] $Matches.n); $null = $pattern.Append("(?<p$($Matches.n)>-?\d+)") }
        else { $null = $pattern.Append(([regex]::Escape($part) -replace '(\\ |\s)+', '\s+')) }
    }
    $null = $pattern.Append('$')
    $match = [regex]::Match($Text.Trim(), $pattern.ToString())
    if (-not $match.Success) { return $null }
    foreach ($n in ($order | Sort-Object -Unique)) { [int] $match.Groups["p$n"].Value }
}

function Get-VerifyRegionProblem {
    <#
    .SYNOPSIS
        How a bed region in whole percent (x, y, width, height) differs from the expected one by more than -Tolerance
        percentage points, or $null. "x 15 (expected 5)" for each value that is off.
    #>
    param([AllowNull()] [int[]] $Actual, [Parameter(Mandatory)] [double[]] $Expected, [double] $Tolerance = 0)
    if (-not $Actual -or $Actual.Count -ne 4) { return 'no region' }
    $names = 'x', 'y', 'width', 'height'
    $off = for ($i = 0; $i -lt 4; $i++) { if ([math]::Abs($Actual[$i] - $Expected[$i]) -gt $Tolerance) { "$($names[$i]) $($Actual[$i]) (expected $([math]::Round($Expected[$i], 1)))" } }
    if ($off) { "region $($Actual -join ' / ') %: $(@($off) -join ', ')" }
}

function ConvertTo-VerifyRegionPercent {
    <# .SYNOPSIS A region of settings.json (x, y, width, height as fractions 0-1) in whole percent, as the editor names it; $null without one. #>
    param([AllowNull()] $Region)
    if (-not $Region) { return $null }
    foreach ($name in 'x', 'y', 'width', 'height') { [int] [math]::Round([double] $Region.$name * 100, [MidpointRounding]::AwayFromZero) }
}

function Get-VerifyDecodingPaths {
    <#
    .SYNOPSIS
        The decoding paths of the app's log lines "Camera video decoding: <path>." (T-203), in order: e.g. videotoolbox,
        "software (the option is off)".
    #>
    param([AllowNull()] [AllowEmptyString()] [string] $Log)
    if (-not $Log) { return @() }
    @([regex]::Matches($Log, 'Camera video decoding: (?<path>[^\r\n]+?)\.?\s*(\r?\n|$)') | ForEach-Object { $_.Groups['path'].Value })
}

# The titles AppKit gives the buttons of an open panel and the standard items of a text context menu, per language of
# LayerPeek (macOS 15 to 27; several where macOS uses more than one, e.g. "Einsetzen" / "Einfügen").
$script:AppKitTitles = [ordered]@{
    en = @('Cancel', 'Open', 'Choose', 'Cut', 'Copy', 'Paste', 'Select All', 'Delete', 'Undo', 'Redo')
    cs = @('Zrušit', 'Otevřít', 'Vybrat', 'Vyjmout', 'Kopírovat', 'Vložit', 'Vybrat vše', 'Smazat', 'Odvolat', 'Znovu')
    de = @('Abbrechen', 'Öffnen', 'Auswählen', 'Ausschneiden', 'Kopieren', 'Einsetzen', 'Einfügen', 'Alles auswählen', 'Löschen', 'Widerrufen', 'Wiederholen')
    es = @('Cancelar', 'Abrir', 'Seleccionar', 'Elegir', 'Cortar', 'Copiar', 'Pegar', 'Seleccionar todo', 'Eliminar', 'Deshacer', 'Rehacer')
    fr = @('Annuler', 'Ouvrir', 'Choisir', 'Couper', 'Copier', 'Coller', 'Tout sélectionner', 'Supprimer', 'Rétablir')
    it = @('Annulla', 'Apri', 'Scegli', 'Taglia', 'Copia', 'Incolla', 'Seleziona tutto', 'Elimina', 'Ripristina')
    pl = @('Anuluj', 'Otwórz', 'Wybierz', 'Wytnij', 'Kopiuj', 'Wklej', 'Zaznacz wszystko', 'Usuń', 'Cofnij', 'Powtórz')
    sk = @('Zrušiť', 'Otvoriť', 'Vybrať', 'Vystrihnúť', 'Kopírovať', 'Vložiť', 'Vybrať všetko', 'Vymazať', 'Odvolať', 'Znova')
}

function Get-VerifyTitleLanguages {
    <#
    .SYNOPSIS
        The languages of LayerPeek (en, cs, de, es, fr, it, pl, sk) whose AppKit titles contain the titles read (T-097
        question 5: in which language AppKit shows its panel and menu), the best first: Language and Matches (how many of
        the titles). An empty list when no title is known. "Annulla" alone is it, "Cancel" en; titles without a trailing
        ellipsis and access key ("_", the app's own menus of T-242) are compared exactly.
    #>
    param([AllowEmptyCollection()] [AllowNull()] [string[]] $Titles = @())
    $clean = @($Titles | Where-Object { $_ } | ForEach-Object { ($_ -replace '(…|\.\.\.)$', '' -replace '_', '').Trim() } | Select-Object -Unique)
    $found = foreach ($language in $script:AppKitTitles.Keys) {
        $count = @($clean | Where-Object { $script:AppKitTitles[$language] -ccontains $_ }).Count
        if ($count -gt 0) { [pscustomobject]@{ Language = $language; Matches = $count } }
    }
    @($found | Sort-Object -Property @{ Expression = 'Matches'; Descending = $true }, @{ Expression = 'Language'; Descending = $false })
}

function Get-VerifyTextMenuProblem {
    <#
    .SYNOPSIS
        Why the items of the context menu of a text field are not all in LayerPeek's language (T-242: the app's own menu
        follows the app language, not the system's); $null when they are. Titles as for Get-VerifyTitleLanguages.
    #>
    param([AllowEmptyCollection()] [AllowNull()] [string[]] $Titles = @(), [Parameter(Mandatory)] [string] $Language)
    $read = @($Titles | Where-Object { $_ } | Select-Object -Unique)
    if ($read.Count -eq 0) { return 'no items' }
    $best = @(Get-VerifyTitleLanguages -Titles $read) | Select-Object -First 1
    if (-not $best) { return "items '$($read -join "', '")' are in no LayerPeek language" }
    $ours = @(Get-VerifyTitleLanguages -Titles $read) | Where-Object Language -EQ $Language | Select-Object -First 1
    $matched = if ($ours) { $ours.Matches } else { 0 }
    if ($matched -lt $read.Count) { return "items '$($read -join "', '")' are $($best.Language), expected all $Language ($matched of $($read.Count))" }
    $null
}

function Get-VerifyOtherLanguage {
    <#
    .SYNOPSIS
        A LayerPeek language other than the system's (the first entry of AppleLanguages, e.g. cs-CZ): de, or fr when the
        system is German (T-097 question 5).
    #>
    param([AllowEmptyCollection()] [AllowNull()] [string[]] $SystemLanguages = @())
    $first = "$(@($SystemLanguages | Where-Object { $_ }) | Select-Object -First 1)".ToLowerInvariant()
    if ($first -eq 'de' -or $first.StartsWith('de-')) { 'fr' } else { 'de' }
}

function Get-VerifyDefaultBrowser {
    <#
    .SYNOPSIS
        The bundle identifier of the default browser from the LaunchServices handlers (the JSON of
        ~/Library/Preferences/com.apple.LaunchServices/com.apple.launchservices.secure.plist): the role handler of the
        https scheme, else of http; com.apple.safari when there is none (the system default).
    #>
    param([AllowNull()] $Handlers)
    if (-not $Handlers -or -not $Handlers.PSObject.Properties['LSHandlers']) { return 'com.apple.safari' }
    foreach ($scheme in 'https', 'http') {
        $entry = @($Handlers.LSHandlers | Where-Object { $_ -and $_.PSObject.Properties['LSHandlerURLScheme'] -and $_.LSHandlerURLScheme -ceq $scheme }) | Select-Object -First 1
        if ($entry -and $entry.PSObject.Properties['LSHandlerRoleAll'] -and $entry.LSHandlerRoleAll) { return ([string] $entry.LSHandlerRoleAll).ToLowerInvariant() }
    }
    'com.apple.safari'
}

function Get-VerifyProxyAnswers {
    <#
    .SYNOPSIS
        The answers of the app's web proxy in its detailed log ("Web proxy: GET / answered 200 (printer 'verify')."; the
        path without the access token), in order: Method, Path, Status, Printer.
    #>
    param([AllowNull()] [AllowEmptyString()] [string] $Log)
    if (-not $Log) { return @() }
    @([regex]::Matches($Log, "Web proxy: (?<method>[A-Z]+) (?<path>\S*) answered (?<status>\d{3}) \(printer '(?<printer>[^']*)'\)") | ForEach-Object {
            [pscustomobject]@{ Method = $_.Groups['method'].Value; Path = $_.Groups['path'].Value; Status = [int] $_.Groups['status'].Value; Printer = $_.Groups['printer'].Value }
        })
}

function Get-VerifyTabMoveProblem {
    <#
    .SYNOPSIS
        Whether the selected tab moved by -Step (+1 = Cmd-Shift-], -1 = Cmd-Shift-[, wrapping round) from -Before to
        -After in the tab list; $null when it did, else what happened.
    #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Tabs, [AllowEmptyString()] [string] $Before, [AllowEmptyString()] [string] $After, [int] $Step = 1)
    if (-not $Before -or -not $After -or $Tabs.Count -eq 0) { return 'the selected tab could not be read' }
    $index = [array]::IndexOf($Tabs, $Before)
    if ($index -lt 0) { return "the tab $Before is not one of $($Tabs -join ', ')" }
    $expected = $Tabs[(($index + $Step) % $Tabs.Count + $Tabs.Count) % $Tabs.Count]
    if ($After -cne $expected) { "went from $Before to $After, expected $expected" }
}

# ---------------------------------------------------------------------------------------------------------------------
# First run with an unlocked screen (2026-11-15): tabs, menu titles, the test mode's URL launcher
# ---------------------------------------------------------------------------------------------------------------------

function ConvertTo-VerifyMenuTitle {
    <#
    .SYNOPSIS
        A menu title for comparing: no-break spaces (U+00A0, U+202F, U+2007) as plain spaces. NSMenu hands a title such
        as "Save the last 2<U+00A0>min" of the resources to the Accessibility API with a plain space; the menus Avalonia
        draws itself (the context menu of the camera window) keep the no-break space.
    #>
    param([AllowNull()] [AllowEmptyString()] [string] $Title)
    "$Title".Replace([string] [char] 0x00A0, ' ').Replace([string] [char] 0x202F, ' ').Replace([string] [char] 0x2007, ' ')
}

function Test-VerifyAxTabSelected {
    <#
    .SYNOPSIS
        Whether a tab item of the helper's Accessibility tree is the selected one: a value of 1 / True, or the selection
        pipe of the Fluent theme (PART_SelectedPipe) among its children (hidden, so not in the tree, in the other tabs).
    #>
    param([AllowNull()] $Node)
    if (-not $Node) { return $false }
    if ("$($Node.value)" -in '1', 'True') { return $true }
    foreach ($child in @($Node.children)) {
        if (-not $child) { continue }
        if ($child.identifier -ceq 'PART_SelectedPipe' -or (Test-VerifyAxTabSelected ([pscustomobject]@{ value = ''; children = $child.children }))) { return $true }
    }
    $false
}

function Get-VerifyAxTabItems {
    <#
    .SYNOPSIS
        The tab items of a TabControl of the helper's Accessibility tree (AXRadioButton / AXTab with an identifier), in
        order, without those of a nested TabControl in the content of the selected tab (PART_SelectedContentHost): the
        sub-tabs of a printer are no main tabs (first run with an unlocked screen 2026-11-15).
    #>
    param([AllowNull()] $Group)
    if (-not $Group -or -not $Group.PSObject.Properties['children']) { return }
    foreach ($child in @($Group.children)) {
        if (-not $child -or $child.identifier -ceq 'PART_SelectedContentHost') { continue }
        if ($child.identifier -and $child.role -in 'AXRadioButton', 'AXTab') { $child; continue }
        Get-VerifyAxTabItems $child
    }
}

function Format-VerifyInvariantNumber {
    <# .SYNOPSIS A number as a command line argument: invariant culture (12.5, never 12,5), no exponent for the usual values. #>
    param([Parameter(Mandatory)] [double] $Value)
    $Value.ToString('0.############', [cultureinfo]::InvariantCulture)
}

function Test-VerifyUrlsRecorded {
    <#
    .SYNOPSIS
        Whether the log of a Debug test app shows that its addresses went to opened-urls.txt instead of the browser
        (RecordingUrlLauncher, T-067): then no browser opens and no request reaches the web proxy.
    #>
    param([AllowNull()] [AllowEmptyString()] [string] $Log)
    "$Log" -match 'RecordingUrlLauncher: Test mode: an address on .+ was recorded instead of opening the browser'
}

function Get-VerifyOpenFakeEnvironment {
    <#
    .SYNOPSIS
        The environment of the test app of mac.open-fake: the PATH with the fake open first, and the Debug switch that opens
        the links through the real launcher (/usr/bin/open, T-238) instead of recording them in opened-urls.txt. An older
        build ignores the switch and still records them (Test-VerifyUrlsRecorded, the SKIP of the check).
    #>
    param([Parameter(Mandatory)] [string] $Path)
    @{ PATH = $Path; LAYERPEEK_TEST_REAL_URL_LAUNCHER = '1' }
}

# ---------------------------------------------------------------------------------------------------------------------
# Self-test (stand-in checks, stand-in prompt)
# ---------------------------------------------------------------------------------------------------------------------

function Get-VerifyFileJobEvents {
    <#
    .SYNOPSIS
        The results of the file jobs in the app's log, in order (T-213): Exported (the JPEG export of a timelapse, T-165:
        Frames, Name = the folder), NotExported (Name = the timelapse, Problem, e.g. NotATimelapse or Cancelled), Made (a
        timelapse from a recording, T-164: Frames, Files, Name = the recording) and NotMade (Name, Problem).
    #>
    param([AllowNull()] [AllowEmptyString()] [string] $Log)
    foreach ($line in @("$Log" -split "`n")) {
        if ($line -cmatch "Exported (?<n>\d+) timelapse frames as JPEG into '(?<name>[^']+)'") { [pscustomobject]@{ Kind = 'Exported'; Name = $Matches.name; Frames = [int] $Matches.n; Files = 0; Problem = '' } }
        elseif ($line -cmatch "The JPEG export of '(?<name>[^']+)' ended without a folder \((?<problem>\w+)\)") { [pscustomobject]@{ Kind = 'NotExported'; Name = $Matches.name; Frames = 0; Files = 0; Problem = $Matches.problem } }
        elseif ($line -cmatch "Made a timelapse of (?<n>\d+) frames in (?<files>\d+) file\(s\) from the recording '(?<name>[^']+)'") { [pscustomobject]@{ Kind = 'Made'; Name = $Matches.name; Frames = [int] $Matches.n; Files = [int] $Matches.files; Problem = '' } }
        elseif ($line -cmatch "The timelapse from the recording '(?<name>[^']+)' ended without a file \((?<problem>\w+)\)") { [pscustomobject]@{ Kind = 'NotMade'; Name = $Matches.name; Frames = 0; Files = 0; Problem = $Matches.problem } }
    }
}

function Get-VerifyPanelProblems {
    <#
    .SYNOPSIS
        What an open panel of the helper's 'panel' JSON (T-213) does differently from -Expected: Sheet ($true: attached to
        a window, $false: a free-standing window), Title (the window title or one of its texts, the message), Folder (the
        folder it shows: AXDocument, else the name in its path pop-up), Front (its app active and the panel its key window).
        Paths are compared without /private and a trailing separator. Empty: as expected.
    #>
    param($Panel, [Parameter(Mandatory)] [System.Collections.IDictionary] $Expected)
    if (-not $Panel) { return @('no open panel') }
    $problems = [System.Collections.Generic.List[string]]::new()
    $plain = { param([string] $Path) if ($Path) { [System.IO.Path]::TrimEndingDirectorySeparator(($Path -replace '^/private/', '/')) } else { '' } }
    if ($Expected.Contains('Sheet') -and [bool] $Panel.sheet -ne [bool] $Expected.Sheet) {
        $problems.Add("a $(if ($Panel.sheet) { 'sheet' } else { 'free-standing window' }) ($($Panel.role) $($Panel.subrole)), expected a $(if ($Expected.Sheet) { 'sheet' } else { 'free-standing window' })")
    }
    if ($Expected.Contains('Title') -and "$($Panel.title)" -cne $Expected.Title -and @($Panel.texts) -cnotcontains $Expected.Title) {
        $problems.Add("title '$($Panel.title)' and texts $(@($Panel.texts | ForEach-Object { "'$_'" }) -join ', ') without '$($Expected.Title)'")
    }
    if ($Expected.Contains('Folder')) {
        $folder = & $plain $Panel.folder
        $wanted = & $plain $Expected.Folder
        $shown = if ($folder) { $folder -ceq $wanted } else { "$($Panel.folderTitle)" -ceq (Split-Path -Leaf $wanted) }
        if (-not $shown) { $problems.Add("shows the folder '$(if ($folder) { $folder } else { $Panel.folderTitle })', expected '$wanted'") }
    }
    if ($Expected.Contains('Front') -and $Expected.Front) {
        if (-not $Panel.frontmost) { $problems.Add('its app is not in front') }
        if (-not $Panel.focused) { $problems.Add('it is not the key window of its app') }
    }
    $problems.ToArray()
}

function Get-VerifyPanelItemProblems {
    <#
    .SYNOPSIS
        Whether the items of an open panel (the helper's 'panel' JSON, T-213) can be chosen as expected: every name of
        -Allowed enabled, every name of -Refused disabled. Returns Problems, and Unknown: the names AX shows without an
        enabled state (or not at all), which the check cannot judge.
    #>
    param([AllowEmptyCollection()] [object[]] $Items = @(), [string[]] $Allowed = @(), [string[]] $Refused = @())
    $problems = [System.Collections.Generic.List[string]]::new()
    $unknown = [System.Collections.Generic.List[string]]::new()
    foreach ($name in @($Allowed) + @($Refused)) {
        $item = @($Items | Where-Object { $_ -and "$($_.name)" -ceq $name }) | Select-Object -First 1
        if (-not $item -or $null -eq $item.enabled) { $unknown.Add($name); continue }
        $expected = $Allowed -ccontains $name
        if ([bool] $item.enabled -ne $expected) { $problems.Add("'$name' is $(if ($item.enabled) { 'enabled' } else { 'disabled' }), expected $(if ($expected) { 'enabled' } else { 'disabled' })") }
    }
    [pscustomobject]@{ Problems = $problems.ToArray(); Unknown = $unknown.ToArray() }
}

function Get-VerifyMenuPercent {
    <#
    .SYNOPSIS
        The percentage of the first item of the helper's 'menu' JSON (submenus included) whose title is -Format (a resource
        text with "{0}" for the percentage and maybe an access key, e.g. "Cancel JPEG _export ({0}%)"); $null when none
        (T-213).
    #>
    param([AllowEmptyCollection()] [object[]] $Items = @(), [Parameter(Mandatory)] [string] $Format)
    $parts = (ConvertFrom-VerifyAccessText $Format) -split '\{0\}', 2
    $pattern = "^$([regex]::Escape($parts[0]))(\d{1,3})$([regex]::Escape($parts[1]))$"
    foreach ($item in @($Items | Where-Object { $_ })) {
        if ("$($item.title)" -cmatch $pattern) { return [int] $Matches[1] }
        if ($item.submenu) {
            $found = Get-VerifyMenuPercent -Items @($item.submenu) -Format $Format
            if ($null -ne $found) { return $found }
        }
    }
    $null
}

function Invoke-VerifySelfTest {
    <#
    .SYNOPSIS
        Runs the engine with stand-in checks and a stand-in prompt, in both modes, into a temporary folder that is deleted
        afterwards, and returns the problems (empty = the engine works). Touches nothing outside -TempRoot.
    #>
    param([Parameter(Mandatory)] [string] $TempRoot)
    $problems = [System.Collections.Generic.List[string]]::new()
    $root = Join-Path $TempRoot "layerpeek-verify-selftest-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    $null = New-Item -ItemType Directory -Path $root
    try {
        $cleaned = [System.Collections.Generic.List[string]]::new()
        $checks = @(
            New-VerifyCheck -Id 'self.pass' -Title 'passes' -Closes 'T-105 self-test' -Kind Auto -Action { param($c) $null = Save-VerifyEvidence -Context $c -Name 'e.txt' -Content 'evidence'; New-VerifyResult PASS 'ok' }
            New-VerifyCheck -Id 'self.fail' -Title 'fails' -Closes 'T-105 self-test' -Kind Auto -Action { New-VerifyResult FAIL 'bad' }
            New-VerifyCheck -Id 'self.throw' -Title 'throws' -Closes 'T-105 self-test' -Kind Auto -Action { throw 'boom' }
            New-VerifyCheck -Id 'self.skip' -Title 'skips' -Closes 'T-105 self-test' -Kind Auto -Action { Skip-VerifyCheck 'not here' }
            New-VerifyCheck -Id 'self.noresult' -Title 'returns nothing' -Closes 'T-105 self-test' -Kind Auto -Action { 'just text' }
            New-VerifyCheck -Id 'self.ask' -Title 'auto with a question' -Closes 'T-105 self-test' -Kind Auto -Action { param($c) if (Confirm-VerifyStep $c 'ok?') { New-VerifyResult MANUAL 'confirmed' } else { New-VerifyResult FAIL 'user: no' } }
            New-VerifyCheck -Id 'self.permission' -Title 'permission' -Closes 'T-105 self-test' -Kind Permission -Permission 'a stand-in permission' -Action { param($c) Add-VerifyCleanup $c 'stand-in' { param($list) $list.Add('x') } -State $cleaned; New-VerifyResult PASS 'ran' }
            New-VerifyCheck -Id 'self.manual' -Title 'manual' -Closes 'T-105 self-test' -Kind Manual -Action { param($c) if (Confirm-VerifyStep $c 'seen?') { New-VerifyResult MANUAL 'seen' } else { New-VerifyResult FAIL 'not seen' } }
            New-VerifyCheck -Id 'self.needs' -Title 'unmet precondition' -Closes 'T-208 self-test' -Kind Auto -Needs 'Standin' -Action { New-VerifyResult FAIL 'ran despite the unmet precondition' }
            New-VerifyCheck -Id 'self.final' -Title 'final' -Closes 'T-105 self-test' -Kind Auto -Final -Action { param($c) New-VerifyResult PASS "cleaned $($cleaned.Count)" }
        )
        $expected = @{
            Interactive = [ordered]@{ 'self.pass' = 'PASS'; 'self.fail' = 'FAIL'; 'self.throw' = 'FAIL'; 'self.skip' = 'SKIP'; 'self.noresult' = 'FAIL'; 'self.ask' = 'MANUAL'; 'self.permission' = 'PASS'; 'self.manual' = 'FAIL'; 'self.needs' = 'SKIP'; 'self.final' = 'PASS' }
            Unattended  = [ordered]@{ 'self.pass' = 'PASS'; 'self.fail' = 'FAIL'; 'self.throw' = 'FAIL'; 'self.skip' = 'SKIP'; 'self.noresult' = 'FAIL'; 'self.ask' = 'SKIP'; 'self.permission' = 'SKIP'; 'self.manual' = 'SKIP'; 'self.needs' = 'SKIP'; 'self.final' = 'PASS' }
        }
        foreach ($mode in 'Interactive', 'Unattended') {
            # Stand-in answers: y (self.ask), y (permission), n (manual). Unattended must never ask.
            $answers = [System.Collections.Generic.Queue[string]]::new([string[]] @('y', 'y', 'n'))
            $asked = [System.Collections.Generic.List[string]]::new()
            $prompt = { param($Question) $asked.Add($Question); if ($answers.Count -gt 0) { $answers.Dequeue() } else { 'n' } }.GetNewClosure()
            $written = [System.Collections.Generic.List[string]]::new()
            $writer = { param($Text, $Color) $written.Add([string] $Text) }.GetNewClosure()
            $cleaned.Clear()
            $folder = New-VerifyRunFolder -Root (Join-Path $root $mode)
            $context = New-VerifyContext -RunFolder $folder -Unattended:($mode -eq 'Unattended') -Prompt $prompt -Writer $writer
            $context.Unmet['Standin'] = 'a stand-in precondition'
            $results = Invoke-VerifyRun -Checks $checks -Context $context
            foreach ($id in $expected[$mode].Keys) {
                $result = $results | Where-Object Id -EQ $id
                if (-not $result) { $problems.Add("${mode}: no result for $id"); continue }
                if ($result.Status -ne $expected[$mode][$id]) { $problems.Add("${mode}: $id is $($result.Status) ($($result.Detail)), expected $($expected[$mode][$id])") }
                if ($result.Status -eq 'SKIP' -and -not $result.Detail) { $problems.Add("${mode}: $id is skipped without a reason") }
            }
            if ($mode -eq 'Unattended' -and $asked.Count -gt 0) { $problems.Add("Unattended asked: $($asked -join ' | ')") }
            if ($mode -eq 'Interactive' -and $asked.Count -ne 3) { $problems.Add("Interactive asked $($asked.Count) questions, expected 3") }
            $finalDetail = ($results | Where-Object Id -EQ 'self.final').Detail
            $wantCleaned = if ($mode -eq 'Interactive') { 'cleaned 1' } else { 'cleaned 0' }
            if ($finalDetail -ne $wantCleaned) { $problems.Add("${mode}: the final check ran before the cleanup ($finalDetail)") }
            if ((Get-VerifyExitCode $results) -ne 1) { $problems.Add("${mode}: the exit code is not 1 with failures") }
            $info = Get-VerifyRunInfo -RepoRoot $root -Script 'self-test' -Unattended:($mode -eq 'Unattended') -Git { param($Arguments) $null }
            $report = Write-VerifyReport -Folder $folder -Info $info -Results $results
            $json = Get-Content -LiteralPath $report.Json -Raw | ConvertFrom-Json
            if (@($json.Results).Count -ne $checks.Count) { $problems.Add("${mode}: the JSON report has $(@($json.Results).Count) results") }
            if ($json.Run.Mode -ne $mode.ToLowerInvariant()) { $problems.Add("${mode}: the JSON report says mode $($json.Run.Mode)") }
            $markdown = Get-Content -LiteralPath $report.Markdown -Raw
            if ($markdown -notmatch [regex]::Escape('evidence/self.pass/e.txt')) { $problems.Add("${mode}: the Markdown report does not link the evidence") }
            if (-not (Test-Path -LiteralPath (Join-Path $folder 'evidence' 'self.throw' 'error.txt'))) { $problems.Add("${mode}: no error evidence of a throwing check") }
        }
        if ((Get-VerifyExitCode @((New-VerifyResult PASS 'a'), (New-VerifyResult SKIP 'b'), (New-VerifyResult MANUAL 'c'))) -ne 0) { $problems.Add('PASS/SKIP/MANUAL give a non-zero exit code') }
        $selected = @(Select-VerifyChecks -Checks $checks -Only 'self.p*,self.manual' -Skip 'self.pass')
        if (($selected.Id -join ',') -ne 'self.permission,self.manual') { $problems.Add("-Only/-Skip selected $($selected.Id -join ',')") }
        try { $null = Select-VerifyChecks -Checks $checks -Only 'self.nothing'; $problems.Add('-Only with an unknown id did not throw') } catch { }
    }
    finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
    $problems.ToArray()
}

Export-ModuleMember -Function *
