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
    #>
    param(
        [Parameter(Mandatory)] [string] $Id,
        [Parameter(Mandatory)] [string] $Title,
        [Parameter(Mandatory)] [string[]] $Closes,
        [Parameter(Mandatory)] [ValidateSet('Auto', 'Permission', 'Manual')] [string] $Kind,
        [string] $Permission,
        [switch] $Final,
        [Parameter(Mandatory)] [scriptblock] $Action
    )
    if ($Id -cnotmatch '^[a-z]+(\.[a-z0-9]+(-[a-z0-9]+)*)+$') { throw "Invalid check id '$Id' (lower case, e.g. mac.keychain)." }
    if ($Kind -eq 'Permission' -and [string]::IsNullOrWhiteSpace($Permission)) { throw "The Permission check $Id must say what it needs (-Permission)." }
    foreach ($criterion in $Closes) {
        if ($criterion -cnotmatch '^T-\d{3}\b') { throw "The check $Id closes '$criterion', which does not start with a task id (T-XXX)." }
    }
    [pscustomobject]@{
        PSTypeName = 'LayerPeek.VerifyCheck'
        Id         = $Id
        Title      = $Title
        Closes     = [string[]] $Closes
        Kind       = $Kind
        Permission = $Permission
        Final      = [bool] $Final
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
    }
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
    #>
    param(
        [Parameter(Mandatory)] [string] $RunFolder,
        [switch] $Unattended,
        [scriptblock] $Prompt = { param($Question) Read-Host $Question },
        [scriptblock] $Writer = { param($Text, $Color) if ($Color) { Write-Host $Text -ForegroundColor $Color } else { Write-Host $Text } }
    )
    [pscustomobject]@{
        PSTypeName = 'LayerPeek.VerifyContext'
        Unattended = [bool] $Unattended
        RunFolder  = $RunFolder
        Prompt     = $Prompt
        Writer     = $Writer
        Shared     = @{}
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
    if ($Check.Kind -eq 'Permission') {
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
    $os = if ($IsMacOS) { "macOS $((& /usr/bin/sw_vers -productVersion 2>$null) -join '') ($([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture))" }
    else { "$([System.Runtime.InteropServices.RuntimeInformation]::OSDescription) ($([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture))" }
    [ordered]@{
        Script     = $Script
        Started    = [DateTimeOffset]::Now.ToString('o')
        Commit     = if ($commit) { "$commit".Trim() } else { 'unknown' }
        Branch     = if ($branch) { "$branch".Trim() } else { 'unknown' }
        Dirty      = [bool] (@($status | Where-Object { $_ }).Count -gt 0)
        AppVersion = $version ?? 'unknown'
        Os         = $os
        PowerShell = $PSVersionTable.PSVersion.ToString()
        Mode       = if ($Unattended) { 'unattended' } else { 'interactive' }
        Selected   = [string[]] $Selected
    }
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
    $lines.Add("- Commit: ``$($Info.Commit)`` on ``$($Info.Branch)``$(if ($Info.Dirty) { ' (with local changes)' })")
    $lines.Add("- App version: $($Info.AppVersion)")
    $lines.Add("- OS: $($Info.Os), PowerShell $($Info.PowerShell)")
    $lines.Add("- Mode: $($Info.Mode)")
    $lines.Add("- Result: " + (($summary.Keys | ForEach-Object { "$_ $($summary[$_])" }) -join ', '))
    $lines.Add('')
    $lines.Add('| Check | Kind | Result | Closes | Detail |')
    $lines.Add('|---|---|---|---|---|')
    foreach ($result in $Results) {
        $detail = ($result.Detail -replace '\|', '\|' -replace "`r?`n", ' ')
        $closes = (@($result.Closes) -join '<br>') -replace '\|', '\|'
        $lines.Add("| ``$($result.Id)`` | $($result.Kind) | **$($result.Status)** | $closes | $detail |")
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

function Write-VerifyReport {
    <# .SYNOPSIS report.md and report.json in -Folder; returns both paths. #>
    param([Parameter(Mandatory)] [string] $Folder, [Parameter(Mandatory)] [System.Collections.IDictionary] $Info, [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Results)
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
    #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [AllowEmptyString()] [string[]] $Output)
    $passed = 0; $failed = 0; $skipped = 0; $assemblies = 0
    foreach ($line in $Output) {
        foreach ($match in [regex]::Matches($line, '\[\+(?<p>\d+)/x(?<f>\d+)/\?(?<s>\d+)\]')) {
            $passed += [int] $match.Groups['p'].Value
            $failed += [int] $match.Groups['f'].Value
            $skipped += [int] $match.Groups['s'].Value
            $assemblies++
        }
    }
    if ($assemblies -eq 0) { return $null }
    [pscustomobject]@{ Total = $passed + $failed + $skipped; Passed = $passed; Failed = $failed; Skipped = $skipped; Assemblies = $assemblies }
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

# ---------------------------------------------------------------------------------------------------------------------
# Self-test (stand-in checks, stand-in prompt)
# ---------------------------------------------------------------------------------------------------------------------

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
            New-VerifyCheck -Id 'self.final' -Title 'final' -Closes 'T-105 self-test' -Kind Auto -Final -Action { param($c) New-VerifyResult PASS "cleaned $($cleaned.Count)" }
        )
        $expected = @{
            Interactive = [ordered]@{ 'self.pass' = 'PASS'; 'self.fail' = 'FAIL'; 'self.throw' = 'FAIL'; 'self.skip' = 'SKIP'; 'self.noresult' = 'FAIL'; 'self.ask' = 'MANUAL'; 'self.permission' = 'PASS'; 'self.manual' = 'FAIL'; 'self.final' = 'PASS' }
            Unattended  = [ordered]@{ 'self.pass' = 'PASS'; 'self.fail' = 'FAIL'; 'self.throw' = 'FAIL'; 'self.skip' = 'SKIP'; 'self.noresult' = 'FAIL'; 'self.ask' = 'SKIP'; 'self.permission' = 'SKIP'; 'self.manual' = 'SKIP'; 'self.final' = 'PASS' }
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
