<#
.SYNOPSIS
    User-run verification of the open macOS acceptance criteria (T-105): automatic checks, checks that need a permission
    (each asked separately) and guided manual steps, with a report and evidence under artifacts/verify/<timestamp>/.

.DESCRIPTION
    Run it in your own Terminal session on the Mac (not in an agent sandbox): there the login Keychain is unlocked, the
    privacy permissions (TCC) are yours to give, the display can sleep and sudo works. Every check says which task
    criterion it closes (-ListChecks). The checks use the Debug app in the isolated test mode as the test bundle
    "LayerPeek E2E.app" (identifier io.github.pfoltynek.LayerPeek.E2E, built in artifacts/verify-bundle/ and removed at
    the end) and the PrusaSimulator on 127.0.0.1; the production app, its profile and its permissions are never used by an
    automatic check. Only the guided manual checks ask you to use /Applications/LayerPeek.app; they offer to move your
    LayerPeek profile aside and restore it afterwards (the journal of scripts/aot-smoke.ps1, so that
    'pwsh scripts/aot-smoke.ps1 -RestoreOnly' finishes an interrupted restore).

    Before the first check the script saves the state of the watched profile places (tools/profile-snapshot.ps1) and
    compares it after the cleanup (check mac.profile-unchanged). The cleanup at the end removes what the run created:
    the test apps, the notifications of the test bundle (--layerpeek-e2e-clear-toasts), the stand-in Login Items of the
    test data folders (--layerpeek-e2e-clear-autostart), the LaunchServices registration, Preferences and caches of the
    test bundle, Keychain items with the test prefix LayerPeek.Tests and the temporary folders. Known leftovers that no
    tool can remove: the entry "LayerPeek E2E" in System Settings > Notifications and (after mac.keys) Terminal in
    Privacy & Security > Accessibility (switch it off there yourself).

    Results: PASS (checked automatically), FAIL, SKIP (with the reason), MANUAL (you confirmed it with y). Exit code 1
    when any check failed, 2 when the script could not run. The checks that need a visible desktop (mac.menubar,
    mac.settings-front, mac.camera) report SKIP with the reason and the failure text instead of FAIL when the screen
    was locked (or the session not on the console) before or after them: nothing can be judged there.

.PARAMETER Unattended
    Never asks anything and never requests a permission: Permission and Manual checks are SKIP with the reason, Auto
    checks that would need an answer too. For test runs (also inside dotnet test).

.PARAMETER Only
    Check ids or wildcards to run (comma-separated or an array), e.g. -Only mac.keychain,mac.camera*.

.PARAMETER Skip
    Check ids or wildcards not to run.

.PARAMETER ListChecks
    Lists the checks (id, kind, the criteria they close) and exits.

.PARAMETER SelfTest
    Runs the engine with stand-in checks and a stand-in prompt in a temporary folder and validates the check list; touches
    nothing of the system or the profile. Works on any OS.

.PARAMETER OutputRoot
    Folder of the run folders; default artifacts/verify in the repository.

.EXAMPLE
    pwsh tools/macos-verify.ps1 -ListChecks
    pwsh tools/macos-verify.ps1                              # everything, interactive
    pwsh tools/macos-verify.ps1 -Unattended                  # only what runs without you
    pwsh tools/macos-verify.ps1 -Only mac.keychain,mac.notifications
#>
#Requires -Version 7.2
[CmdletBinding()]
param(
    [switch] $Unattended,
    [string[]] $Only,
    [string[]] $Skip,
    [switch] $ListChecks,
    [switch] $SelfTest,
    [string] $OutputRoot
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'VerifyTools.psm1') -Force
Import-Module (Join-Path $PSScriptRoot '..' 'scripts' 'ReleaseTools' 'ReleaseTools.psm1') -Force

$script:Repo = if ($IsMacOS) { Resolve-PhysicalPath (Join-Path $PSScriptRoot '..') } else { [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')) }
$script:Lsregister = '/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister'
$script:BundleId = Get-MacBundleIdentifier (Join-Path $script:Repo 'build' 'macos' 'Info.plist')
$script:TestBundleId = "$($script:BundleId).E2E"
$script:DotnetEnv = @{ DOTNET_CLI_UI_LANGUAGE = 'en'; DOTNET_NOLOGO = '1'; DOTNET_CLI_TELEMETRY_OPTOUT = '1' }
$script:PrinterId = 'verify'
$script:Corners = @('TopLeft', 'TopRight', 'BottomLeft', 'BottomRight', 'Center')

# ---------------------------------------------------------------------------------------------------------------------
# Fixtures shared by the checks (built once per run, cleaned up at the end)
# ---------------------------------------------------------------------------------------------------------------------

function New-ShortId { [guid]::NewGuid().ToString('N').Substring(0, 8) }

function Get-AppVersion { Get-SourceVersion -PropsPath (Join-Path $script:Repo 'Directory.Build.props') }

function Get-ReleaseApp {
    <# .SYNOPSIS The release bundle of scripts/release-build.ps1 for this version, or $null. #>
    $app = Join-Path $script:Repo 'artifacts' 'release' (Get-AppVersion) 'app' 'osx-arm64' 'LayerPeek.app'
    if (Test-Path -LiteralPath $app) { $app } else { $null }
}

function Get-Helper($Context) {
    if (-not $Context.Shared.ContainsKey('Helper')) {
        $folder = Join-Path $Context.RunFolder 'tmp'
        $null = New-Item -ItemType Directory -Force -Path $folder
        $binary = Join-Path $folder 'macos-verify-helper'
        $result = Invoke-VerifyTool -Context $Context -Label 'swiftc-helper' -FilePath '/usr/bin/xcrun' -ArgumentList @('swiftc', '-O', '-o', $binary, (Join-Path $PSScriptRoot 'macos-verify-helper.swift'))
        if ($result.ExitCode -ne 0) { throw "The helper did not compile (Xcode Command Line Tools installed? xcode-select --install): $($result.Output -join ' ')" }
        $Context.Shared.Helper = $binary
    }
    $Context.Shared.Helper
}

function Invoke-Helper($Context, [string[]] $Arguments, [switch] $AllowFailure) {
    $output = @(& (Get-Helper $Context) @Arguments 2>&1 | ForEach-Object { "$_" })
    if ($LASTEXITCODE -ne 0 -and -not $AllowFailure) { throw "macos-verify-helper $($Arguments -join ' ') failed: $($output -join ' ')" }
    $output
}

function Get-Screens($Context) { @((Invoke-Helper $Context @('screens')) -join "`n" | ConvertFrom-Json) }

function Get-ProcessWindows($Context, [int] $ProcessId) { @((Invoke-Helper $Context @('windows', "$ProcessId")) -join "`n" | ConvertFrom-Json) }

function Get-SessionLimitation($Context) {
    <# .SYNOPSIS Why window checks cannot be judged in this session (locked screen, not on the console), or $null. #>
    try { Get-VerifySessionLimitation ((Invoke-Helper $Context @('session')) -join "`n" | ConvertFrom-Json) } catch { $null }
}

function Invoke-SessionCheck($Context, [scriptblock] $Body) {
    <#
    .SYNOPSIS
        Runs a check that needs an unlocked session on the console (front app, status item, full-screen Space). Its FAIL
        becomes a SKIP with the reason (and the FAIL text) when the screen was locked before or after; see
        Resolve-VerifySessionResult.
    #>
    $before = Get-SessionLimitation $Context
    $result = @(& $Body | Where-Object { $_.PSObject.TypeNames -contains 'LayerPeek.VerifyResult' })[-1]
    if (-not $result) { $result = New-VerifyResult FAIL 'The check returned no result (a bug of the check).' }
    Resolve-VerifySessionResult -Result $result -Limitations @($before, (Get-SessionLimitation $Context))
}

function Test-Accessibility($Context) { (Invoke-Helper $Context @('trusted')) -contains 'true' }

function Get-DotnetRoot {
    # "Microsoft.NETCore.App 11.0.0 [<root>/shared/Microsoft.NETCore.App]"
    foreach ($line in @(& dotnet --list-runtimes 2>$null)) {
        if ($line -match '^Microsoft\.NETCore\.App \S+ \[(?<path>.+)\]$') { return Split-Path -Parent (Split-Path -Parent $Matches.path) }
    }
    throw 'dotnet --list-runtimes lists no Microsoft.NETCore.App.'
}

function Get-DebugApp($Context) {
    if (-not $Context.Shared.ContainsKey('DebugApp')) {
        $result = Invoke-VerifyTool -Context $Context -Label 'build-debug-app' -FilePath 'dotnet' -ArgumentList @('build', (Join-Path $script:Repo 'src' 'LayerPeek.App' 'LayerPeek.App.csproj'), '-c', 'Debug') -Environment $script:DotnetEnv
        if ($result.ExitCode -ne 0) { throw 'dotnet build of the Debug app failed (see build-debug-app.txt).' }
        $Context.Shared.DebugApp = Join-Path $script:Repo 'src' 'LayerPeek.App' 'bin' 'Debug' 'net11.0'
    }
    $Context.Shared.DebugApp
}

function Get-SimulatorDll($Context) {
    if (-not $Context.Shared.ContainsKey('SimulatorDll')) {
        $result = Invoke-VerifyTool -Context $Context -Label 'build-simulator' -FilePath 'dotnet' -ArgumentList @('build', (Join-Path $script:Repo 'tools' 'PrusaSimulator' 'PrusaSimulator.csproj'), '-c', 'Debug') -Environment $script:DotnetEnv
        if ($result.ExitCode -ne 0) { throw 'dotnet build of the PrusaSimulator failed.' }
        $Context.Shared.SimulatorDll = Join-Path $script:Repo 'tools' 'PrusaSimulator' 'bin' 'Debug' 'net11.0' 'PrusaSimulator.dll'
    }
    $Context.Shared.SimulatorDll
}

function Get-RegisteredTestBundles {
    <# .SYNOPSIS The paths LaunchServices knows with the test bundle identifier (the rule of MacNotificationsE2ETests.RegisteredPaths). #>
    $paths = [System.Collections.Generic.List[string]]::new()
    $path = $null
    foreach ($raw in @(& $script:Lsregister -dump 2>$null)) {
        $line = "$raw".Trim()
        if ($line.StartsWith('----')) { $path = $null }
        elseif ($line.StartsWith('path:')) { $path = ($line.Substring(5).Trim() -replace '\s+\(0x[0-9a-fA-F]+\)$', '') }
        elseif ($path -and $line -match '^identifier:\s*(?<id>\S+)$' -and $Matches.id -ceq $script:TestBundleId -and -not $paths.Contains($path)) { $paths.Add($path) }
    }
    $paths.ToArray()
}

function Get-TestBundle($Context) {
    <# .SYNOPSIS "LayerPeek E2E.app" of the Debug app (as MacNotificationsE2ETests builds it), signed ad hoc and registered. #>
    if ($Context.Shared.ContainsKey('Bundle')) { Register-TestBundle $Context.Shared.Bundle; return $Context.Shared.Bundle }
    $source = Get-DebugApp $Context
    $folder = Join-Path $script:Repo 'artifacts' 'verify-bundle' (Split-Path -Leaf $Context.RunFolder)
    $app = Join-Path $folder 'LayerPeek E2E.app'
    $macOS = Join-Path $app 'Contents' 'MacOS'
    $bundle = [pscustomobject]@{ Path = $app; Executable = Join-Path $macOS 'LayerPeek'; Folder = $folder }
    Add-VerifyCleanup $Context 'test bundle' { param($b) Remove-TestBundle $b } -State $bundle -RunLast
    $null = New-Item -ItemType Directory -Force -Path $macOS
    $root = (Resolve-Path -LiteralPath $source).ProviderPath
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
        $relative = [System.IO.Path]::GetRelativePath($root, $file.FullName)
        # The native libraries of other platforms are not needed (and not worth signing).
        if ($relative.StartsWith('runtimes/') -and -not $relative.StartsWith('runtimes/osx')) { continue }
        $target = Join-Path $macOS $relative
        $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target)
        [System.IO.File]::Copy($file.FullName, $target)
    }
    $plist = ConvertTo-VerifyTestInfoPlist -ProductionText (Get-Content -LiteralPath (Join-Path $script:Repo 'build' 'macos' 'Info.plist') -Raw) -ProductionIdentifier $script:BundleId
    [System.IO.File]::WriteAllText((Join-Path $app 'Contents' 'Info.plist'), $plist, [System.Text.UTF8Encoding]::new($false))
    $identifier = (& /usr/bin/plutil -extract CFBundleIdentifier raw (Join-Path $app 'Contents' 'Info.plist')) -join ''
    if ($identifier -cne $script:TestBundleId) { throw "The test bundle got the identifier '$identifier'; refusing to run it." }
    $files = @(Get-ChildItem -LiteralPath $macOS -Recurse -File | Where-Object FullName -NE $bundle.Executable | ForEach-Object FullName)
    for ($i = 0; $i -lt $files.Count; $i += 40) {
        $chunk = @($files[$i..([math]::Min($i + 39, $files.Count - 1))])
        $null = & /usr/bin/codesign --force --sign - --timestamp=none @chunk 2>&1
        if ($LASTEXITCODE -ne 0) { throw 'codesign of the test bundle files failed.' }
    }
    $null = & /usr/bin/codesign --force --sign - --timestamp=none $app 2>&1
    $null = & /usr/bin/codesign --verify --strict $app 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'The signature of the test bundle does not verify.' }
    $Context.Shared.Bundle = $bundle
    Register-TestBundle $bundle
    $bundle
}

function Register-TestBundle($Bundle) {
    # Another registration of the test identifier (the E2E tests, a crashed run) makes the notification center refuse this one.
    foreach ($stale in Get-RegisteredTestBundles) { if ($stale -cne $Bundle.Path) { $null = & $script:Lsregister -u $stale 2>&1 } }
    $null = & $script:Lsregister -f $Bundle.Path 2>&1
}

function Get-BundleProcessIds($Bundle) {
    $ids = foreach ($line in @(& /bin/ps -axww -o 'pid=,command=' 2>$null)) {
        if ("$line" -match '^\s*(?<pid>\d+)\s+(?<command>.+)$') {
            $command = $Matches.command
            if ($command.StartsWith($Bundle.Executable) -or $command.StartsWith("/private$($Bundle.Executable)")) { [int] $Matches.pid }
        }
    }
    @($ids)
}

function Invoke-BundleTool($Bundle, [string[]] $Arguments, [hashtable] $Environment = @{}) {
    $saved = @{}
    $all = @{ DOTNET_ROOT = (Get-DotnetRoot) } + $Environment
    foreach ($name in $all.Keys) { $saved[$name] = [Environment]::GetEnvironmentVariable($name); [Environment]::SetEnvironmentVariable($name, $all[$name]) }
    try { $output = @(& $Bundle.Executable @Arguments 2>&1 | ForEach-Object { "$_" }); $code = $LASTEXITCODE }
    finally { foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) } }
    [pscustomobject]@{ ExitCode = $code; Output = $output }
}

function Get-TestNotificationStatus($Context) {
    $bundle = Get-TestBundle $Context
    $result = Invoke-BundleTool $bundle @('--layerpeek-e2e-notification-status')
    $status = Get-VerifyNotificationStatus $result.Output
    if (-not $status) { throw "The status mode of the test bundle answered: $($result.Output -join ' ')" }
    $status
}

function Remove-TestBundle($Bundle) {
    foreach ($id in Get-BundleProcessIds $Bundle) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $Bundle.Executable) { $null = Invoke-BundleTool $Bundle @('--layerpeek-e2e-clear-toasts') }
    $null = & $script:Lsregister -u $Bundle.Path 2>&1
    $null = & /usr/bin/defaults read $script:TestBundleId 2>&1
    if ($LASTEXITCODE -eq 0) { $null = & /usr/bin/defaults delete $script:TestBundleId 2>&1 }
    $library = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Library'
    # These places belong to the test identifier only ('defaults delete' leaves an empty plist behind).
    foreach ($place in @("Preferences/$($script:TestBundleId).plist", "Saved Application State/$($script:TestBundleId).savedState", "Caches/$($script:TestBundleId)", "HTTPStorages/$($script:TestBundleId)")) {
        $path = Join-Path $library $place
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
    if (Test-Path -LiteralPath $Bundle.Folder) { Remove-Item -LiteralPath $Bundle.Folder -Recurse -Force }
    $parent = Split-Path -Parent $Bundle.Folder
    if ((Test-Path -LiteralPath $parent) -and -not (Get-ChildItem -LiteralPath $parent -Force)) { Remove-Item -LiteralPath $parent -Force }
}

function Get-Simulator($Context) {
    <# .SYNOPSIS The PrusaSimulator (one printer, camera) on 127.0.0.1 with free ports, started once per run. #>
    if ($Context.Shared.ContainsKey('Simulator')) { return $Context.Shared.Simulator }
    $dll = Get-SimulatorDll $Context
    $folder = Join-Path $Context.RunFolder 'tmp' "simulator-$(New-ShortId)"
    $null = New-Item -ItemType Directory -Force -Path $folder
    $out = Join-Path $folder 'stdout.txt'
    $start = [System.Diagnostics.ProcessStartInfo]::new('/bin/sh')
    foreach ($argument in @('-c', 'exec dotnet "$0" "$@" >"$LPV_OUT" 2>&1 </dev/null', $dll, '--port', '0', '--camera-port', '0', '--scenario', 'printing')) { $start.ArgumentList.Add($argument) }
    $start.Environment['LPV_OUT'] = $out
    $start.UseShellExecute = $false
    $process = [System.Diagnostics.Process]::Start($start)
    Add-VerifyCleanup $Context 'simulator' { param($p) if (-not $p.HasExited) { $p.Kill($true); $p.WaitForExit() } } -State $process -RunLast
    $ready = Wait-VerifyCondition -TimeoutSeconds 60 -Condition { (Test-Path -LiteralPath $out) -and (Select-String -LiteralPath $out -Pattern 'Camera:' -Quiet) }
    if (-not $ready) { throw "The simulator did not start: $(Get-Content -LiteralPath $out -Raw -ErrorAction SilentlyContinue)" }
    $text = Get-Content -LiteralPath $out -Raw
    $simulator = [pscustomobject]@{
        BaseUri  = ([regex]::Match($text, 'Address:\s+(\S+)')).Groups[1].Value
        User     = ([regex]::Match($text, 'User name:\s+(\S+)')).Groups[1].Value
        Password = ([regex]::Match($text, 'Password:\s+(\S+)')).Groups[1].Value
        Camera   = ([regex]::Match($text, 'Camera:\s+(\S+)')).Groups[1].Value
        Output   = $out
    }
    $simulator | Add-Member -NotePropertyName Address -NotePropertyValue (([uri] $simulator.BaseUri).Authority)
    $simulator | Add-Member -NotePropertyName CameraPort -NotePropertyValue (([uri] $simulator.Camera).Port)
    $Context.Shared.Simulator = $simulator
    $simulator
}

function Set-SimulatorScenario($Simulator, [string] $Scenario) {
    $null = Invoke-RestMethod -Method Post -Uri "$($Simulator.BaseUri.TrimEnd('/'))/_simulator/scenario/$Scenario" -TimeoutSec 10
}

function New-TestData {
    <#
    .SYNOPSIS
        A test data folder (settings.json, test-secrets.txt) of the isolated test mode and its instance suffix. -Camera adds
        the camera of the printer, -Window its camera.window section. Removed at the end (after the autostart stand-ins).
    #>
    param($Context, $Simulator, [string] $Camera, [System.Collections.IDictionary] $Window, [switch] $SharedTemp, [switch] $Notifications)
    $base = if ($SharedTemp) { '/tmp' } else { [System.IO.Path]::GetTempPath() }
    $data = Join-Path $base "lpv-$(New-ShortId)"
    $null = New-Item -ItemType Directory -Force -Path $data
    $data = (Resolve-Path -LiteralPath $data).ProviderPath
    $suffix = "v$(New-ShortId)"
    $settings = [ordered]@{
        version                = 1
        pollingIntervalSeconds = 5
        language               = 'en'
        notifications          = [ordered]@{ enabled = [bool] $Notifications; remainingTimeMinutes = 0; sound = 'Off' }
        printers               = @()
    }
    if ($Simulator) {
        $printer = [ordered]@{ id = $script:PrinterId; name = "LP verify $(New-ShortId)"; connectionType = 'PrusaLink'; address = $Simulator.Address; userName = $Simulator.User }
        if ($Camera) {
            $printer.camera = [ordered]@{ enabled = $true; streamUrl = $Camera }
            if ($Window) { $printer.camera.window = $Window }
        }
        $settings.printers = @($printer)
        [System.IO.File]::WriteAllText((Join-Path $data 'test-secrets.txt'), "printer/$($script:PrinterId)`t$($Simulator.Password)`n")
    }
    [System.IO.File]::WriteAllText((Join-Path $data 'settings.json'), ($settings | ConvertTo-Json -Depth 8))
    $testData = [pscustomobject]@{
        Context = $Context
        Data   = $data
        Suffix = $suffix
        Socket = Get-VerifyInstanceSocketPath -DataDirectory $data -Suffix $suffix -TempDirectory ([System.IO.Path]::GetTempPath()) -UserId ([int] (& /usr/bin/id -u))
        Logs   = Join-Path $data 'logs'
    }
    Add-VerifyCleanup $Context "test data $data" {
        param($s)
        # The stand-in Login Item of the test mode (T-019), then the folder.
        if ($s.Shared.ContainsKey('Bundle') -and (Test-Path -LiteralPath $s.Shared.Bundle.Executable)) { $null = Invoke-BundleTool $s.Shared.Bundle @('--layerpeek-e2e-clear-autostart') @{ LAYERPEEK_TEST_DATA_DIR = $s.Data; LAYERPEEK_INSTANCE_SUFFIX = $s.Suffix } }
        if (Test-Path -LiteralPath $s.Data) { Remove-Item -LiteralPath $s.Data -Recurse -Force }
    } -State ([pscustomobject]@{ Shared = $Context.Shared; Data = $data; Suffix = $suffix })
    $testData
}

function Read-TestLog($TestData) {
    if (-not (Test-Path -LiteralPath $TestData.Logs)) { return '' }
    (Get-ChildItem -LiteralPath $TestData.Logs -File | Sort-Object Name | ForEach-Object {
            $stream = [System.IO.FileStream]::new($_.FullName, 'Open', 'Read', 'ReadWrite, Delete')
            try { [System.IO.StreamReader]::new($stream).ReadToEnd() } finally { $stream.Dispose() }
        }) -join "`n"
}

function Start-TestApp {
    <#
    .SYNOPSIS
        Starts the test bundle's executable in the test mode as a child of this script (its responsible process is the
        terminal: no Local Network question for 127.0.0.1 or the LAN) and waits for its instance socket.
    #>
    param($Context, $TestData, [hashtable] $Environment = @{}, [switch] $NoWait)
    $bundle = Get-TestBundle $Context
    $start = [System.Diagnostics.ProcessStartInfo]::new('/bin/sh')
    foreach ($argument in @('-c', 'exec "$0" >>"$LPV_OUT" 2>&1 </dev/null', $bundle.Executable)) { $start.ArgumentList.Add($argument) }
    foreach ($name in @($start.Environment.Keys | Where-Object { $_ -like 'LAYERPEEK_*' })) { $null = $start.Environment.Remove($name) }
    $start.Environment['DOTNET_ROOT'] = Get-DotnetRoot
    $start.Environment['LAYERPEEK_TEST_DATA_DIR'] = $TestData.Data
    $start.Environment['LAYERPEEK_INSTANCE_SUFFIX'] = $TestData.Suffix
    $start.Environment['LPV_OUT'] = Join-Path $TestData.Data 'stdout.txt'
    foreach ($name in $Environment.Keys) { $start.Environment[$name] = $Environment[$name] }
    $start.UseShellExecute = $false
    $process = [System.Diagnostics.Process]::Start($start)
    Add-VerifyCleanup $Context "test app $($process.Id)" { param($p) if (-not $p.HasExited) { $p.Kill(); $p.WaitForExit() } } -State $process
    if (-not $NoWait) {
        if (-not (Wait-VerifyCondition -TimeoutSeconds 30 -Condition { $process.HasExited -or (Test-Path -LiteralPath $TestData.Socket) })) { throw "The test app did not open its instance channel within 30 s. Log:`n$(Read-TestLog $TestData)" }
        if ($process.HasExited) { throw "The test app ended with $($process.ExitCode). Log:`n$(Read-TestLog $TestData)" }
        Start-Sleep -Seconds 2
        if ((Read-TestLog $TestData) -match 'No active display') {
            # T-096: the app waits for a display before its UI starts; nothing of the UI can be checked meanwhile.
            $null = Stop-TestApp $TestData $process
            Skip-VerifyCheck 'every display is asleep (the app waits for one before its UI starts): wake the display and run the script under caffeinate -d -u.'
        }
    }
    $process
}

function Start-TestAppViaOpen {
    <# .SYNOPSIS Starts the test bundle through LaunchServices (open -n), as macOS requires for notifications; returns the pid. #>
    param($Context, $TestData, [hashtable] $Environment = @{})
    $bundle = Get-TestBundle $Context
    $before = @(Get-BundleProcessIds $bundle)
    $arguments = @('-n', '-F', '-g', '--stdout', (Join-Path $TestData.Data 'stdout.txt'), '--stderr', (Join-Path $TestData.Data 'stderr.txt'))
    $all = [ordered]@{ LAYERPEEK_TEST_DATA_DIR = $TestData.Data; LAYERPEEK_INSTANCE_SUFFIX = $TestData.Suffix; DOTNET_ROOT = (Get-DotnetRoot) }
    foreach ($name in $Environment.Keys) { $all[$name] = $Environment[$name] }
    foreach ($name in $all.Keys) { $arguments += @('--env', "$name=$($all[$name])") }
    & /usr/bin/open @arguments $bundle.Path
    if ($LASTEXITCODE -ne 0) { throw 'open of the test bundle failed.' }
    $found = 0
    $null = Wait-VerifyCondition -TimeoutSeconds 20 -Condition { $script:candidate = @(Get-BundleProcessIds $bundle | Where-Object { $before -notcontains $_ }); $script:candidate.Count -gt 0 }
    if ($script:candidate.Count -gt 0) { $found = $script:candidate[0] }
    if (-not $found) { throw 'The test app did not start through open.' }
    Add-VerifyCleanup $Context "test app $found" { param($id) Stop-Process -Id $id -Force -ErrorAction SilentlyContinue } -State $found
    $null = Wait-VerifyCondition -TimeoutSeconds 30 -Condition { Test-Path -LiteralPath $TestData.Socket }
    Start-Sleep -Seconds 2
    if ((Read-TestLog $TestData) -match 'No active display') {
        Skip-VerifyCheck 'every display is asleep (the app waits for one before its UI starts): wake the display and run the script under caffeinate -d -u.'
    }
    $found
}

function Send-TestCommand($TestData, [string] $Command) {
    if (-not (Send-VerifyInstanceCommand -SocketPath $TestData.Socket -Command $Command)) { throw "The test app did not acknowledge the command '$Command'." }
}

function Stop-TestApp($TestData, $Process) {
    <#
    .SYNOPSIS
        Quits through the test command (like the menu item Quit); kills after 20 s. Keeps the app's log as evidence of the
        running check. Returns the exit code.
    #>
    $code = if ($Process.HasExited) { $Process.ExitCode }
    else {
        $null = Send-VerifyInstanceCommand -SocketPath $TestData.Socket -Command 'quit'
        if ($Process.WaitForExit(20000)) { $Process.ExitCode } else { $Process.Kill(); $Process.WaitForExit(); -1 }
    }
    $null = Save-VerifyEvidence -Context $TestData.Context -Name "app-$($TestData.Suffix).log" -Content (Read-TestLog $TestData)
    $code
}

function Get-FrontProcessId {
    $asn = Get-VerifyFrontAsn @(& /usr/bin/lsappinfo front 2>$null)
    if (-not $asn) { return $null }
    Get-VerifyLsappinfoPid @(& /usr/bin/lsappinfo info -only pid $asn 2>$null)
}

function Get-FrontAppText {
    <# .SYNOPSIS "name (pid)" of the front app for a FAIL text, e.g. "loginwindow (179)" with a locked screen. #>
    $frontId = Get-FrontProcessId
    if (-not $frontId) { return 'none' }
    $name = (Get-Process -Id $frontId -ErrorAction SilentlyContinue).ProcessName
    "$(if ($name) { $name } else { '?' }) ($frontId)"
}

function Invoke-FinderToFront {
    <#
    .SYNOPSIS
        Brings Finder (always running) to the front as "the other app"; asks again every 3 s for up to 12 s, because a
        single 'open -a Finder' is sometimes lost while another app is activating. Returns whether Finder is in front.
    #>
    for ($attempt = 0; $attempt -lt 4; $attempt++) {
        & /usr/bin/open -a Finder
        $finder = @(Get-Process -Name 'Finder' -ErrorAction SilentlyContinue | ForEach-Object Id)
        if (Wait-VerifyCondition -TimeoutSeconds 3 -Condition { $finder -contains (Get-FrontProcessId) }) { return $true }
    }
    $false
}

function Get-CameraWindow($Context, [int] $ProcessId) {
    # The normal window is layer 0; the mini preview (always on top) a floating level; menu bar items (25) never count.
    @(Get-ProcessWindows $Context $ProcessId | Where-Object { $_.onscreen -and $_.layer -ge 0 -and $_.layer -lt 25 -and $_.width -ge 80 -and $_.height -ge 45 } |
            Sort-Object { $_.width * $_.height } -Descending) | Select-Object -First 1
}

function Test-FullScreen($Window, $Screens) {
    [bool] ($Screens | Where-Object { [math]::Abs($_.frame.x - $Window.x) -le 2 -and [math]::Abs($_.frame.y - $Window.y) -le 2 -and [math]::Abs($_.frame.width - $Window.width) -le 2 -and [math]::Abs($_.frame.height - $Window.height) -le 2 })
}

function Get-RtspConnections([int] $ProcessId, [int] $Port) {
    @(& /usr/sbin/lsof -a -n -P -p $ProcessId "-iTCP:$Port" -sTCP:ESTABLISHED 2>$null | Select-Object -Skip 1)
}

function Invoke-CameraModes {
    <#
    .SYNOPSIS
        For each camera window mode: a test app with the camera (-Camera, default the simulator's), the window opened by the
        test command, its frame and the RTSP connection; with -Look the user is asked whether the image and the overlay are
        visible. Returns the problems and the answers.
    #>
    param($Context, [string] $Camera, [switch] $Look)
    $simulator = Get-Simulator $Context
    Set-SimulatorScenario $simulator 'printing'
    $cameraUrl = if ($Camera) { $Camera } else { $simulator.Camera }
    $port = ([uri] $cameraUrl).Port
    if ($port -le 0) { $port = 554 }
    $screens = Get-Screens $Context
    $problems = [System.Collections.Generic.List[string]]::new()
    $notes = [System.Collections.Generic.List[string]]::new()
    foreach ($mode in 'Normal', 'Mini', 'FullScreen') {
        $data = New-TestData -Context $Context -Simulator $simulator -Camera $cameraUrl -Window ([ordered]@{ rememberLastPlacement = $false; startMode = $mode })
        $app = Start-TestApp -Context $Context -TestData $data
        try {
            Send-TestCommand $data "camera:$($script:PrinterId)"
            $script:w = $null
            $opened = Wait-VerifyCondition -TimeoutSeconds 20 -Condition { $script:w = Get-CameraWindow $Context $app.Id; $script:w -and ($mode -ne 'FullScreen' -or (Test-FullScreen $script:w $screens)) }
            $window = $script:w
            $connected = Wait-VerifyCondition -TimeoutSeconds 20 -Condition { (Get-RtspConnections $app.Id $port).Count -gt 0 }
            Start-Sleep -Seconds 5
            $still = (Get-RtspConnections $app.Id $port).Count -gt 0
            $null = Save-VerifyEvidence -Context $Context -Name "$($mode.ToLowerInvariant())-windows.json" -Content (ConvertTo-VerifyJsonArray (Get-ProcessWindows $Context $app.Id))
            $null = Save-VerifyEvidence -Context $Context -Name "$($mode.ToLowerInvariant())-rtsp.txt" -Content (Get-RtspConnections $app.Id $port)
            if (-not $opened) {
                $seen = if ($window) { " (largest window $([math]::Round($window.width)) x $([math]::Round($window.height)) pt at $([math]::Round($window.x)),$([math]::Round($window.y)), layer $($window.layer))" } else { '' }
                $problems.Add("${mode}: no camera window$(if ($mode -eq 'FullScreen') { ' covering a whole screen' })$seen")
            }
            if (-not $connected -or -not $still) { $problems.Add("${mode}: no lasting RTSP connection to port $port") }
            $errors = @((Read-TestLog $data) -split "`n" | Where-Object { $_ -match ' (ERR|CRT) ' -and $_ -match '(?i)camera' })
            if ($errors.Count -gt 0) { $problems.Add("${mode}: camera errors in the log: $($errors[0])") }
            if ($window) { $notes.Add("${mode}: $([math]::Round($window.width)) x $([math]::Round($window.height)) pt, layer $($window.layer)") }
            if ($Look) {
                if (-not (Read-VerifyAnswer -Context $Context -Question "Camera window in mode $($mode): do you see a moving camera image with the print status overlay?")) { $problems.Add("${mode}: the user saw no moving image or no overlay") }
            }
        }
        finally {
            $null = Stop-TestApp $data $app
            $null = Save-VerifyEvidence -Context $Context -Name "$($mode.ToLowerInvariant())-app.log" -Content (Read-TestLog $data)
        }
    }
    [pscustomobject]@{ Problems = $problems.ToArray(); Notes = $notes.ToArray() }
}

function Invoke-DotnetTest($Context, [string] $Label, [string[]] $Arguments, [hashtable] $Environment = @{}) {
    $result = Invoke-VerifyTool -Context $Context -Label $Label -FilePath 'dotnet' -ArgumentList (@('test') + $Arguments) -Environment ($script:DotnetEnv + $Environment) -Show
    $totals = Get-VerifyTestTotals $result.Output
    [pscustomobject]@{ ExitCode = $result.ExitCode; Totals = $totals; Skipped = @(Get-VerifySkippedTests $result.Output); Output = $result.Output }
}

function Format-Totals($Totals) { "total $($Totals.Total), passed $($Totals.Passed), failed $($Totals.Failed), skipped $($Totals.Skipped)" }

function Get-KeychainTestServices {
    @(& /usr/bin/security dump-keychain 2>$null | ForEach-Object { if ("$_" -match '^\s*"svce"<blob>="(?<name>LayerPeek\.Tests[^"]*)"') { $Matches.name } } | Select-Object -Unique)
}

function Use-ProductionProfile {
    <#
    .SYNOPSIS
        Runs -Action with the LayerPeek profile moved aside when the user wants that (the journal of scripts/aot-smoke.ps1:
        data, logs, Preferences, saved state, caches); restored afterwards. Otherwise the action uses the real profile.
    #>
    param($Context, [scriptblock] $Action)
    if (Get-Process -Name 'LayerPeek' -ErrorAction SilentlyContinue) {
        Write-VerifyStep $Context 'LayerPeek is running: quit it (menu > Quit) before this check.' 'Yellow'
        if (-not (Wait-VerifyCondition -TimeoutSeconds 120 -Condition { -not (Get-Process -Name 'LayerPeek' -ErrorAction SilentlyContinue) })) { Skip-VerifyCheck 'LayerPeek kept running.' }
    }
    $backup = Read-VerifyAnswer -Context $Context -Question 'Run this step with an empty LayerPeek profile (your settings, logs and preferences are moved aside and restored afterwards)?'
    if (-not $backup) {
        $Context.Shared.ProductionProfileUsed = @($Context.Shared.ProductionProfileUsed) + $Context.CheckId
        return & $Action
    }
    $smokeProfile = Get-SmokeProfile
    Backup-SmokeProfile -SmokeProfile $smokeProfile
    try { & $Action }
    finally {
        if (Get-Process -Name 'LayerPeek' -ErrorAction SilentlyContinue) {
            Write-VerifyStep $Context 'Quit LayerPeek (menu > Quit) so that the profile can be restored.' 'Yellow'
            $null = Wait-VerifyCondition -TimeoutSeconds 300 -Condition { -not (Get-Process -Name 'LayerPeek' -ErrorAction SilentlyContinue) }
        }
        $null = Restore-SmokeProfile -SmokeProfile $smokeProfile
        Write-VerifyStep $Context 'Your LayerPeek profile is restored.'
    }
}

function Save-ProfileSnapshot([string] $Path, [switch] $LoginItems) {
    # The functions of tools/profile-snapshot.ps1 (dot-sourced here, in this function's scope only).
    . (Join-Path $PSScriptRoot 'profile-snapshot.ps1')
    $state = if ($LoginItems) { Get-ProfileState } else { Get-ProfileState -LoginItems { $null } }
    Save-ProfileState -Path $Path -State $state
}

function Compare-ProfileSnapshot([string] $Path, [switch] $LoginItems) {
    . (Join-Path $PSScriptRoot 'profile-snapshot.ps1')
    $state = if ($LoginItems) { Get-ProfileState } else { Get-ProfileState -LoginItems { $null } }
    Compare-ProfileState -Before (Read-ProfileState -Path $Path) -After $state
}

# ---------------------------------------------------------------------------------------------------------------------
# The checks
# ---------------------------------------------------------------------------------------------------------------------

$checks = @(
    New-VerifyCheck -Id 'mac.build' -Kind Auto -Title 'dotnet build Debug and Release without warnings' `
        -Closes 'T-018 kritérium 4 (build na macOS)', 'T-053 kritérium 6 (část: build)' -Action {
        param($c)
        $problems = [System.Collections.Generic.List[string]]::new()
        foreach ($configuration in 'Debug', 'Release') {
            $result = Invoke-VerifyTool -Context $c -Label "build-$($configuration.ToLowerInvariant())" -FilePath 'dotnet' -ArgumentList @('build', (Join-Path $script:Repo 'LayerPeek.slnx'), '-c', $configuration) -Environment $script:DotnetEnv
            $warnings = @(Get-VerifyBuildWarnings $result.Output)
            if ($result.ExitCode -ne 0) { $problems.Add("dotnet build -c $configuration failed (exit code $($result.ExitCode))") }
            elseif ($warnings.Count -gt 0) { $problems.Add("dotnet build -c $configuration has $($warnings.Count) warning(s): $($warnings[0])") }
        }
        if ($problems.Count -gt 0) { return New-VerifyResult FAIL ($problems -join '; ') }
        New-VerifyResult PASS 'Debug and Release: 0 warnings, 0 errors.'
    }

    New-VerifyCheck -Id 'mac.test' -Kind Auto -Title 'full dotnet test without failures' `
        -Closes 'T-018 kritérium 4 (testy na macOS)', 'T-095 (zelené testy na macOS, kontrola)' -Action {
        param($c)
        $run = Invoke-DotnetTest $c 'dotnet-test' @('--solution', (Join-Path $script:Repo 'LayerPeek.slnx'))
        if (-not $run.Totals) { return New-VerifyResult FAIL "no test results (build failed?), exit code $($run.ExitCode)." }
        $null = Save-VerifyEvidence -Context $c -Name 'skipped.txt' -Content ($run.Skipped | ForEach-Object { "$($_.Name)`n    $($_.Reason)" })
        if ($run.Totals.Failed -gt 0 -or $run.ExitCode -ne 0) { return New-VerifyResult FAIL "$(Format-Totals $run.Totals); exit code $($run.ExitCode)." }
        New-VerifyResult PASS "$(Format-Totals $run.Totals) (skipped tests with reasons in skipped.txt)"
    }

    New-VerifyCheck -Id 'mac.aot-smoke' -Kind Auto -Title 'Native AOT publish osx-arm64 without warnings and smoke start (scripts/aot-smoke.ps1)' `
        -Closes 'T-053 kritérium 6 (lokální Native AOT publish osx-arm64 bez varování)' -Action {
        param($c)
        $pwsh = @(Get-PwshCommand)
        $arguments = @($pwsh | Select-Object -Skip 1) + @('-NoProfile', '-File', (Join-Path $script:Repo 'scripts' 'aot-smoke.ps1'), '-Runtime', 'osx-arm64')
        $result = Invoke-VerifyTool -Context $c -Label 'aot-smoke' -FilePath $pwsh[0] -ArgumentList $arguments -Environment $script:DotnetEnv -Show
        $warnings = @(Get-VerifyBuildWarnings $result.Output)
        if ($result.ExitCode -ne 0) { return New-VerifyResult FAIL "aot-smoke.ps1 failed (exit code $($result.ExitCode)); see aot-smoke.txt." }
        if ($warnings.Count -gt 0) { return New-VerifyResult FAIL "the AOT publish has $($warnings.Count) warning(s): $($warnings[0])" }
        New-VerifyResult PASS 'published without warnings; the AOT app ran without a Dock icon; profile restored by aot-smoke.ps1.'
    }

    New-VerifyCheck -Id 'mac.keychain' -Kind Auto -Title 'Keychain integration tests on the unlocked login Keychain (no skipped test)' `
        -Closes 'T-021 kritéria 1–5 (round-trip, přepis, velikost písmen, izolace, úklid)', 'T-021 kritérium 8 (zprávy bez tajného údaje)', 'T-021 kritérium 7 (s mac.profile-unchanged)' -Action {
        param($c)
        $run = Invoke-DotnetTest $c 'dotnet-test-keychain' @('--project', (Join-Path $script:Repo 'tests' 'LayerPeek.App.Tests'), '--', '--filter-class', '*KeychainSecretStoreTests')
        if (-not $run.Totals) { return New-VerifyResult FAIL "no test results (build failed?), exit code $($run.ExitCode)." }
        $left = @(Get-KeychainTestServices)
        if ($run.Totals.Failed -gt 0) { return New-VerifyResult FAIL (Format-Totals $run.Totals) }
        if ($run.Totals.Skipped -gt 0) {
            $reason = ($run.Skipped | Select-Object -First 1).Reason
            # Stays a FAIL (a skipped write test never counts); the session note only says why the Keychain may be locked.
            $session = Get-SessionLimitation $c
            $note = if ($session) { " This session has a limitation too ($session): an agent, SSH or screen-locked session cannot unlock the Keychain; run the check in your own Terminal." } else { '' }
            return New-VerifyResult FAIL "$($run.Totals.Skipped) Keychain test(s) skipped (a skipped write test does not count): $reason$note"
        }
        if ($left.Count -gt 0) { return New-VerifyResult FAIL "Keychain items with the test prefix were left: $($left -join ', ')" }
        New-VerifyResult PASS "$(Format-Totals $run.Totals); no LayerPeek.Tests item left in the Keychain."
    }

    New-VerifyCheck -Id 'mac.dock' -Kind Auto -Title 'the app runs without a Dock icon (LSUIElement, LaunchServices type UIElement)' `
        -Closes 'T-019 kritérium 7 (bez ikony v Docku)' -Action {
        param($c)
        $data = New-TestData -Context $c
        $app = Start-TestApp -Context $c -TestData $data
        try {
            $null = Wait-VerifyCondition -TimeoutSeconds 20 -Condition { $null -ne (Get-LaunchServicesAppType -ListOutput @(& /usr/bin/lsappinfo list 2>$null) -ProcessId $app.Id) }
            $list = @(& /usr/bin/lsappinfo list 2>$null)
            $type = Get-LaunchServicesAppType -ListOutput $list -ProcessId $app.Id
            $null = Save-VerifyEvidence -Context $c -Name 'lsappinfo.txt' -Content @($list | Select-String -SimpleMatch "pid = $($app.Id)" -Context 0, 3 | ForEach-Object { $_.ToString() })
            if ($type -cne 'UIElement') { return New-VerifyResult FAIL "LaunchServices type '$type', expected UIElement (no Dock icon, not in Cmd-Tab)." }
            New-VerifyResult PASS 'LaunchServices type UIElement: no Dock icon and not in the Cmd-Tab switcher.'
        }
        finally { $null = Stop-TestApp $data $app }
    }

    New-VerifyCheck -Id 'mac.menubar' -Kind Auto -Title 'status item in the menu bar; Quit ends the app and removes it' `
        -Closes 'T-019 kritérium 7 (ikona jen v menu baru)', 'T-018 kritérium 3 (část: „Ukončit“ ukončí aplikaci)' -Action {
        param($c)
        Invoke-SessionCheck $c {
            $data = New-TestData -Context $c
            $app = Start-TestApp -Context $c -TestData $data
            $shown = Wait-VerifyCondition -TimeoutSeconds 20 -Condition { Test-VerifyMenuBarItem @(Get-ProcessWindows $c $app.Id) }
            $null = Save-VerifyEvidence -Context $c -Name 'windows.json' -Content (ConvertTo-VerifyJsonArray (Get-ProcessWindows $c $app.Id))
            # Every process's menu bar level windows: shows who owns the status items when the app's own are missing.
            $menuBar = @((Invoke-Helper $c @('menubar')) -join "`n" | ConvertFrom-Json)
            $null = Save-VerifyEvidence -Context $c -Name 'menubar-windows.json' -Content (ConvertTo-VerifyJsonArray $menuBar)
            $code = Stop-TestApp $data $app
            if (-not $shown) {
                $owners = @(Get-VerifyMenuBarOwners $menuBar)
                $others = if ($owners.Count -gt 0) { "status item windows (layer 25) of: $($owners -join ', ')" } else { 'no process has a status item window (layer 25)' }
                return New-VerifyResult FAIL "no status item (window layer 25) of the app (pid $($app.Id)) in the menu bar; $others (menubar-windows.json)."
            }
            if ($code -ne 0) { return New-VerifyResult FAIL "Quit ended the app with exit code $code." }
            New-VerifyResult PASS 'status item shown in the menu bar; Quit (the same command as the menu item) ended the app with 0 and the item is gone.'
        }
    }

    New-VerifyCheck -Id 'mac.settings-front' -Kind Auto -Title 'Settings, About, Diagnostics and a later start come to the front with focus over another app' `
        -Closes 'T-019 kritérium 8 (okno Nastavení v popředí s fokusem)' -Action {
        param($c)
        Invoke-SessionCheck $c {
            $simulator = Get-Simulator $c
            $data = New-TestData -Context $c -Simulator $simulator
            $app = Start-TestApp -Context $c -TestData $data
            $notes = [System.Collections.Generic.List[string]]::new()
            try {
                foreach ($command in 'settings', 'about', 'diagnostics', 'activate') {
                    if (-not (Invoke-FinderToFront)) { return New-VerifyResult FAIL "Finder did not come to the front (needed as the other app); front app: $(Get-FrontAppText)." }
                    Send-TestCommand $data $command
                    $front = Wait-VerifyCondition -TimeoutSeconds 10 -Condition { (Get-FrontProcessId) -eq $app.Id -and (Get-VerifyMainWindow @(Get-ProcessWindows $c $app.Id)) }
                    $null = Save-VerifyEvidence -Context $c -Name "$command-windows.json" -Content (ConvertTo-VerifyJsonArray (Get-ProcessWindows $c $app.Id))
                    if (-not $front) { return New-VerifyResult FAIL "'$command': the app is not the front app with a window (front app $(Get-FrontAppText), app pid $($app.Id))." }
                    $notes.Add($command)
                }
            }
            finally { $null = Stop-TestApp $data $app }
            New-VerifyResult PASS "front app with an open window over Finder after: $($notes -join ', ') (the active app receives the key events)."
        }
    }

    New-VerifyCheck -Id 'mac.plist-localnetwork' -Kind Auto -Title 'NSLocalNetworkUsageDescription in every app language as macOS picks it (NSBundle)' `
        -Closes 'T-097 kritérium 3 (podklad: text dialogu v každém jazyce; dialog sám viz mac.local-network)' -Action {
        param($c)
        $settings = Get-MacBundleSettings
        $languages = @(Get-AppLanguageCodes -SourceText (Get-Content -LiteralPath (Join-Path $script:Repo $settings.AppLanguagesSource) -Raw))
        $localizations = Join-Path $script:Repo $settings.Localizations
        $app = Get-ReleaseApp
        $source = 'the release bundle'
        if (-not $app) {
            # A bundle with only the Info.plist and the localizations, built like New-MacAppBundle does.
            $source = 'a temporary bundle from build/macos'
            $folder = Join-Path $c.RunFolder 'tmp' "plist-$(New-ShortId)"
            $app = Join-Path $folder 'LayerPeek.app'
            $null = New-Item -ItemType Directory -Force -Path (Join-Path $app 'Contents' 'Resources')
            $plist = ConvertTo-MacInfoPlist -TemplateText (Get-Content -LiteralPath (Join-Path $script:Repo $settings.PlistTemplate) -Raw) -Version (Get-AppVersion)
            [System.IO.File]::WriteAllText((Join-Path $app 'Contents' 'Info.plist'), $plist)
            foreach ($file in Get-MacLocalizationFiles $localizations) {
                $target = Join-Path $app 'Contents' 'Resources' $file
                $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target)
                Copy-Item -LiteralPath (Join-Path $localizations $file) -Destination $target
            }
        }
        $problems = [System.Collections.Generic.List[string]]::new()
        foreach ($problem in @(Get-MacLocalizationProblems -Directory $localizations -PlistText (Get-Content -LiteralPath (Join-Path $app 'Contents' 'Info.plist') -Raw) -Languages $languages)) { $problems.Add($problem) }
        $json = (Invoke-Helper $c (@('localized', $app, 'NSLocalNetworkUsageDescription') + $languages)) -join "`n"
        $null = Save-VerifyEvidence -Context $c -Name 'localized.json' -Content $json
        $resolved = $json | ConvertFrom-Json -AsHashtable
        $english = $resolved['en'].value
        foreach ($language in $languages) {
            $entry = $resolved[$language]
            $expected = (ConvertFrom-StringsText ([System.IO.File]::ReadAllText((Join-Path $localizations "$language.lproj" 'InfoPlist.strings'))))['NSLocalNetworkUsageDescription']
            if ($entry.localization -cne $language) { $problems.Add("${language}: macOS picks the localization '$($entry.localization)'") }
            elseif ([string]::IsNullOrWhiteSpace($entry.value)) { $problems.Add("${language}: no text") }
            elseif ($entry.value -cne $expected) { $problems.Add("${language}: the bundle text differs from build/macos/Localizations") }
            elseif ($language -ne 'en' -and $entry.value -ceq $english) { $problems.Add("${language}: the text is the English one") }
        }
        if ($problems.Count -gt 0) { return New-VerifyResult FAIL ($problems -join '; ') }
        New-VerifyResult PASS "$($languages.Count) languages ($($languages -join ', ')) in $source each resolve to their own translated text (localized.json)."
    }

    New-VerifyCheck -Id 'mac.gatekeeper' -Kind Auto -Title 'Gatekeeper rejects a quarantined copy of the .dmg app (spctl), as docs/release.md describes' `
        -Closes 'T-019 kritérium 12 (část: posouzení Gatekeeperem; dialog viz mac.gatekeeper-dialog)' -Action {
        param($c)
        $version = Get-AppVersion
        $names = Get-MacReleaseFileNames -Version $version -Runtime 'osx-arm64'
        $dmg = Join-Path $script:Repo 'artifacts' 'release' $version 'dist' $names.DiskImage
        if (-not (Test-Path -LiteralPath $dmg)) { Skip-VerifyCheck "no $($names.DiskImage) in artifacts/release/$version/dist; build it first: caffeinate -d -u -i pwsh scripts/release-build.ps1" }
        $temp = Join-Path ([System.IO.Path]::GetTempPath()) "layerpeek-verify-gk-$(New-ShortId)"
        $mount = Join-Path $temp 'mnt'
        $copy = Join-Path $temp 'copy'
        $null = New-Item -ItemType Directory -Force -Path $mount, $copy
        try {
            $attach = Invoke-VerifyTool -Context $c -Label 'hdiutil-attach' -FilePath '/usr/bin/hdiutil' -ArgumentList @('attach', '-nobrowse', '-readonly', '-noautoopen', '-mountpoint', $mount, $dmg)
            if ($attach.ExitCode -ne 0) { return New-VerifyResult FAIL 'hdiutil attach of the .dmg failed.' }
            try { & /usr/bin/ditto (Join-Path $mount 'LayerPeek.app') (Join-Path $copy 'LayerPeek.app') }
            finally { $null = & /usr/bin/hdiutil detach $mount -quiet 2>&1; if ($LASTEXITCODE -ne 0) { $null = & /usr/bin/hdiutil detach $mount -force -quiet 2>&1 } }
            $app = Join-Path $copy 'LayerPeek.app'
            $dmgCopy = Join-Path $copy $names.DiskImage
            Copy-Item -LiteralPath $dmg -Destination $dmgCopy
            # What a browser download sets (flags 0083: downloaded, not yet approved), on the copies in the temp folder only.
            $quarantine = '0083;{0:x8};Safari;{1}' -f [DateTimeOffset]::UtcNow.ToUnixTimeSeconds(), [guid]::NewGuid().ToString().ToUpperInvariant()
            foreach ($item in $app, $dmgCopy) { & /usr/bin/xattr -w com.apple.quarantine $quarantine $item }
            $null = Invoke-VerifyTool -Context $c -Label 'xattr' -FilePath '/usr/bin/xattr' -ArgumentList @('-p', 'com.apple.quarantine', $app)
            $null = Invoke-VerifyTool -Context $c -Label 'codesign' -FilePath '/usr/bin/codesign' -ArgumentList @('-dv', '--verbose=2', $app)
            $assessApp = Invoke-VerifyTool -Context $c -Label 'spctl-app' -FilePath '/usr/sbin/spctl' -ArgumentList @('--assess', '-vv', '--type', 'execute', $app)
            $assessDmg = Invoke-VerifyTool -Context $c -Label 'spctl-dmg' -FilePath '/usr/sbin/spctl' -ArgumentList @('--assess', '-vv', '--type', 'open', '--context', 'context:primary-signature', $dmgCopy)
            $verdict = Get-VerifySpctlAssessment -ExitCode $assessApp.ExitCode -Output $assessApp.Output
            $dmgVerdict = Get-VerifySpctlAssessment -ExitCode $assessDmg.ExitCode -Output $assessDmg.Output
            if ($verdict.Accepted) { return New-VerifyResult FAIL "Gatekeeper accepts the ad-hoc signed app ($($verdict.Source)); docs/release.md describes a rejection." }
            New-VerifyResult PASS "app: $($verdict.Verdict ?? 'rejected') (source=$($verdict.Source)); .dmg: $($dmgVerdict.Verdict ?? "exit code $($assessDmg.ExitCode)") (source=$($dmgVerdict.Source)). Expected for an ad-hoc signed, not notarized app: 'Done' / 'Move to Bin', then Privacy & Security > Open Anyway."
        }
        finally { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue }
    }

    New-VerifyCheck -Id 'mac.notifications' -Kind Auto -Title 'notification E2E of the test bundle (delivery, removal on quit and by the cleanup mode)' `
        -Closes 'T-020 kritérium 1 (doručení ze sestavené .app, test bundle)', 'T-020 kritérium 5 (odstranění při ukončení i úklidem po pádu)' -Action {
        param($c)
        $status = Get-TestNotificationStatus $c
        Write-VerifyStep $c "Notifications of 'LayerPeek E2E': $($status.Permission)."
        if ($status.Permission -ne 'Allowed') {
            if ($c.Unattended) { Skip-VerifyCheck "the notifications of 'LayerPeek E2E' are $($status.Permission); macOS would ask: run 'pwsh tools/macos-verify.ps1 -Only mac.notifications' interactively once." }
            if ($status.Permission -eq 'Denied') {
                Write-VerifyStep $c "Allow them: System Settings > Notifications > LayerPeek E2E > Allow notifications (opening System Settings)." 'Yellow'
                & /usr/bin/open 'x-apple.systempreferences:com.apple.Notifications-Settings.extension'
                if (-not (Read-VerifyAnswer -Context $c -Question "Have you allowed the notifications of 'LayerPeek E2E'?")) { Skip-VerifyCheck "notifications of 'LayerPeek E2E' denied; allow them and run -Only mac.notifications again." }
            }
            elseif (-not (Read-VerifyAnswer -Context $c -Question "macOS will ask whether 'LayerPeek E2E' may send notifications (a notification with Options > Allow). Run the test and allow it?")) {
                Skip-VerifyCheck 'declined: macOS was not asked.'
            }
        }
        $run = Invoke-DotnetTest $c 'dotnet-test-notifications' @('--project', (Join-Path $script:Repo 'tests' 'LayerPeek.App.Tests'), '--', '--filter-class', '*MacNotificationsE2ETests') @{ LAYERPEEK_E2E = '1' }
        if (-not $run.Totals) { return New-VerifyResult FAIL "no test results, exit code $($run.ExitCode)." }
        if ($run.Totals.Failed -gt 0) { return New-VerifyResult FAIL (Format-Totals $run.Totals) }
        if ($run.Totals.Skipped -gt 0) { return New-VerifyResult FAIL "the E2E test was skipped: $(($run.Skipped | Select-Object -First 1).Reason)" }
        New-VerifyResult PASS "$(Format-Totals $run.Totals): delivered, removed on quit and by --layerpeek-e2e-clear-toasts after a kill."
    }

    New-VerifyCheck -Id 'mac.display-wait' -Kind Auto -Title 'E2E of the start without an active display (simulated, MacDisplayWaitE2ETests)' `
        -Closes 'T-096 kritérium 1 (podklad: simulované čekání)', 'T-099 kritérium 1 (podklad: tray se připojí ke sledování)' -Action {
        param($c)
        $run = Invoke-DotnetTest $c 'dotnet-test-display-wait' @('--project', (Join-Path $script:Repo 'tests' 'LayerPeek.App.Tests'), '--', '--filter-class', '*MacDisplayWaitE2ETests') @{ LAYERPEEK_E2E = '1' }
        if (-not $run.Totals) { return New-VerifyResult FAIL "no test results, exit code $($run.ExitCode)." }
        if ($run.Totals.Failed -gt 0 -or $run.Totals.Skipped -gt 0) { return New-VerifyResult FAIL "$(Format-Totals $run.Totals) (run with an awake display, e.g. caffeinate -d -u)." }
        New-VerifyResult PASS (Format-Totals $run.Totals)
    }

    New-VerifyCheck -Id 'mac.camera' -Kind Auto -Title 'camera window in normal, mini and full-screen mode with a lasting RTSP stream (simulator)' `
        -Closes 'T-026 kritérium 1 (automatická část: všechny režimy se simulátorem)' -Action {
        param($c)
        Invoke-SessionCheck $c {
            $result = Invoke-CameraModes -Context $c
            if ($result.Problems.Count -gt 0) { return New-VerifyResult FAIL ($result.Problems -join '; ') }
            New-VerifyResult PASS "all modes open and stream: $($result.Notes -join '; ')"
        }
    }

    New-VerifyCheck -Id 'mac.camera-placement' -Kind Auto -Title 'camera window corners and centre on every connected monitor (normal and mini, 16 pt from the usable area)' `
        -Closes 'T-038 kritérium 1 (automatická část: J2 rohy a střed, D1 zvolený monitor)' -Action {
        param($c)
        $simulator = Get-Simulator $c
        $screens = @(Get-Screens $c)
        $null = Save-VerifyEvidence -Context $c -Name 'screens.json' -Content (ConvertTo-VerifyJsonArray $screens)
        $rows = [System.Collections.Generic.List[object]]::new()
        $problems = [System.Collections.Generic.List[string]]::new()
        foreach ($screen in $screens) {
            foreach ($mode in 'Normal', 'Mini') {
                foreach ($corner in $script:Corners) {
                    $window = [ordered]@{
                        rememberLastPlacement = $false
                        startMode             = $mode
                        monitor               = [ordered]@{ displayName = $screen.name; width = [int] ($screen.frame.width * $screen.scale); height = [int] ($screen.frame.height * $screen.scale); devicePath = $screen.uuid }
                        normal                = [ordered]@{ width = 480; height = 270; corner = $corner }
                        mini                  = [ordered]@{ width = 320; height = 180; corner = $corner }
                    }
                    $data = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -Window $window
                    $app = Start-TestApp -Context $c -TestData $data
                    try {
                        Send-TestCommand $data "camera:$($script:PrinterId)"
                        $null = Wait-VerifyCondition -TimeoutSeconds 15 -Condition { $null -ne (Get-CameraWindow $c $app.Id) }
                        Start-Sleep -Milliseconds 1500
                        $frame = Get-CameraWindow $c $app.Id
                        $found = if ($frame) { @(Get-VerifyCornerProblems -Window ([pscustomobject]@{ X = $frame.x; Y = $frame.y; Width = $frame.width; Height = $frame.height }) -Area ([pscustomobject]@{ X = $screen.visible.x; Y = $screen.visible.y; Width = $screen.visible.width; Height = $screen.visible.height }) -Corner $corner) } else { @('no window') }
                        foreach ($problem in $found) { $problems.Add("$($screen.name) $mode ${corner}: $problem") }
                        $rows.Add([ordered]@{ Screen = $screen.name; Mode = $mode; Corner = $corner; Window = $frame; Problems = $found })
                    }
                    finally { $null = Stop-TestApp $data $app }
                }
            }
        }
        $null = Save-VerifyEvidence -Context $c -Name 'placements.json' -Content (ConvertTo-VerifyJsonArray $rows -Depth 6)
        if ($problems.Count -gt 0) { return New-VerifyResult FAIL "$($problems.Count) of $($rows.Count) placements wrong: $($problems[0])" }
        New-VerifyResult PASS "$($rows.Count) placements on $($screens.Count) monitor(s) ($(($screens.name) -join ', ')) are 16 pt from the usable area, never under the menu bar or the Dock."
    }

    New-VerifyCheck -Id 'mac.icons' -Kind Manual -Title 'menu bar icons on light and dark menu bars, app icon next to system app icons (contact sheet)' `
        -Closes 'T-018 kritérium 1 (čitelnost ve světlém i tmavém režimu)', 'T-098 kritérium 2 (velikost vedle systémových aplikací)' -Action {
        param($c)
        $sheet = Join-Path (Get-VerifyEvidenceFolder $c) 'icons.png'
        $appIcon = Get-ReleaseApp
        if (-not $appIcon) { $appIcon = Join-Path $script:Repo 'src' 'LayerPeek.App' 'Assets' 'AppIcon' 'app-icon-macos-1024.png' }
        $system = @('/System/Library/CoreServices/Finder.app', '/System/Applications/Calculator.app', '/System/Applications/System Settings.app', '/System/Applications/App Store.app') | Where-Object { Test-Path -LiteralPath $_ }
        $null = Invoke-Helper $c (@('icons', $sheet, (Join-Path $script:Repo 'tests' 'LayerPeek.App.UiTests' 'Snapshots' 'macOS' 'MenuBarIcons'), $appIcon) + $system)
        & /usr/bin/open $sheet
        $answers = @(
            Confirm-VerifyStep $c 'In icons.png (opened in Preview): are all menu bar icons readable on the light and the dark bar, also at @1x?'
            Confirm-VerifyStep $c 'Is the LayerPeek app icon about as big as the system app icons at every size?'
        )
        if ($answers -contains $false) { return New-VerifyResult FAIL 'the user answered no (see steps.txt).' }
        New-VerifyResult MANUAL "confirmed on the contact sheet icons.png ($(if (Get-ReleaseApp) { 'icon of the release bundle' } else { 'master PNG; build the release bundle for the .icns' }))."
    }

    New-VerifyCheck -Id 'mac.menu' -Kind Manual -Title 'menu bar icon, tooltip and menu follow the printer states; Quit from the real menu' `
        -Closes 'T-018 kritérium 1 (stavy ikony)', 'T-018 kritérium 3 (menu se zobrazí, aktualizuje, „Ukončit“)', 'T-116 kritérium 3 (obrázky tiskáren v menu)' -Action {
        param($c)
        $simulator = Get-Simulator $c
        $data = New-TestData -Context $c -Simulator $simulator
        Set-SimulatorScenario $simulator 'idle'
        $app = Start-TestApp -Context $c -TestData $data
        Write-VerifyStep $c 'The test app shows its own icon in the menu bar (tooltip starts with [E2E]). For each state: look at the icon, hover for the tooltip, open the menu.'
        foreach ($scenario in 'idle', 'printing', 'paused', 'finished', 'error', 'attention', 'offline') {
            Set-SimulatorScenario $simulator $scenario
            Start-Sleep -Seconds 7
            $null = Read-VerifyText -Context $c -Question "State '$scenario' is shown now. Press Enter for the next state"
        }
        $states = Confirm-VerifyStep $c 'Did the icon, the tooltip and the menu items follow every state (also after switching System Settings > Appearance between Light and Dark)?'
        $printerIcons = Confirm-VerifyStep $c 'Does the printer item show its coloured printer picture, sharp (also on an @1x display), readable in Light and Dark and on the highlighted item, without shifting the other items or its submenu?'
        Write-VerifyStep $c 'Now click the [E2E] menu bar icon and choose Quit.' 'Yellow'
        $quit = $app.WaitForExit(120000)
        if (-not $quit) { $null = Stop-TestApp $data $app }
        if (-not $states) { return New-VerifyResult FAIL 'the user saw a state that the icon or the menu did not follow.' }
        if (-not $printerIcons) { return New-VerifyResult FAIL 'the user saw a printer picture in the menu that was blurred, unreadable or misaligned (T-116).' }
        if (-not $quit -or $app.ExitCode -ne 0) { return New-VerifyResult FAIL 'Quit from the menu did not end the app with 0 within 2 minutes.' }
        New-VerifyResult MANUAL 'states and printer pictures confirmed by the user; Quit from the real menu ended the app with 0.'
    }

    New-VerifyCheck -Id 'mac.camera-look' -Kind Manual -Title 'you see the moving camera image and the overlay in every window mode (simulator)' `
        -Closes 'T-026 kritérium 1 (obraz a překryv ve všech režimech, simulátor)' -Action {
        param($c)
        $result = Invoke-CameraModes -Context $c -Look
        if ($result.Problems.Count -gt 0) { return New-VerifyResult FAIL ($result.Problems -join '; ') }
        New-VerifyResult MANUAL "image and overlay confirmed in every mode: $($result.Notes -join '; ')"
    }

    New-VerifyCheck -Id 'mac.camera-buddy3d' -Kind Manual -Title 'a real Buddy3D camera in every window mode' `
        -Closes 'T-026 kritérium 1 (skutečná kamera Buddy3D)' -Action {
        param($c)
        $url = Read-VerifyText -Context $c -Question 'RTSP address of your Buddy3D camera without a user name or password (e.g. rtsp://192.168.1.50/live), or empty to skip'
        if (-not $url) { Skip-VerifyCheck 'no camera address given.' }
        if ($url -notmatch '^rtsp://[^/@\s]+(/\S*)?$') { return New-VerifyResult FAIL "'$url' is not an rtsp:// address without credentials." }
        $result = Invoke-CameraModes -Context $c -Camera $url -Look
        if ($result.Problems.Count -gt 0) { return New-VerifyResult FAIL ($result.Problems -join '; ') }
        New-VerifyResult MANUAL "Buddy3D stream confirmed in every mode: $($result.Notes -join '; ')"
    }

    New-VerifyCheck -Id 'mac.keys' -Kind Permission -Permission 'Accessibility for Terminal (System Settings > Privacy & Security > Accessibility), to post real key events' `
        -Title 'keyboard shortcuts as real key events: Ctrl-Cmd-F, Esc, Cmd-., Cmd-W, Cmd-Shift-]' `
        -Closes 'T-100 kritérium 4 (část: zkratky jako skutečné události klávesnice)' -Action {
        param($c)
        if (-not (Test-Accessibility $c)) {
            Write-VerifyStep $c 'Add your terminal app in System Settings > Privacy & Security > Accessibility and switch it on (opening it now). Remove it again after the run.' 'Yellow'
            & /usr/bin/open 'x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility'
            $null = Read-VerifyAnswer -Context $c -Question 'Is Accessibility switched on for the terminal?'
            if (-not (Test-Accessibility $c)) { Skip-VerifyCheck 'no Accessibility permission for the terminal (it may need a restart of the terminal).' }
        }
        $simulator = Get-Simulator $c
        $data = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -Window ([ordered]@{ rememberLastPlacement = $false; startMode = 'Normal' })
        $app = Start-TestApp -Context $c -TestData $data
        $screens = Get-Screens $c
        $problems = [System.Collections.Generic.List[string]]::new()
        $key = { param($Combo) $null = Invoke-Helper $c @('key', $Combo); Start-Sleep -Milliseconds 400 }
        $frontWith = { param($Command) Send-TestCommand $data $Command; Wait-VerifyCondition -TimeoutSeconds 10 -Condition { (Get-FrontProcessId) -eq $app.Id } }
        $isFull = { $w = Get-CameraWindow $c $app.Id; $w -and (Test-FullScreen $w $screens) }
        $noWindow = { -not (Get-CameraWindow $c $app.Id) }
        try {
            if (-not (& $frontWith "camera:$($script:PrinterId)")) { $problems.Add('the camera window did not come to the front') }
            & $key 'ctrl+cmd+f'
            if (-not (Wait-VerifyCondition -TimeoutSeconds 6 -Condition $isFull)) { $problems.Add('Ctrl-Cmd-F did not enter full screen') }
            & $key 'escape'
            if (-not (Wait-VerifyCondition -TimeoutSeconds 6 -Condition { -not (& $isFull) })) { $problems.Add('Esc did not leave full screen') }
            & $key 'ctrl+cmd+f'
            $null = Wait-VerifyCondition -TimeoutSeconds 6 -Condition $isFull
            & $key 'cmd+period'
            if (-not (Wait-VerifyCondition -TimeoutSeconds 6 -Condition { -not (& $isFull) })) { $problems.Add('Cmd-. did not leave full screen') }
            & $key 'cmd+w'
            if (-not (Wait-VerifyCondition -TimeoutSeconds 6 -Condition $noWindow)) { $problems.Add('Cmd-W did not close the camera window') }
            if ($app.HasExited) { $problems.Add('Cmd-W ended the app') }
            $settingsFile = Join-Path $data.Data 'settings.json'
            $hash = (Get-FileHash -LiteralPath $settingsFile).Hash
            foreach ($closer in 'cmd+w', 'cmd+period') {
                if (-not (& $frontWith 'settings')) { $problems.Add('Settings did not come to the front'); continue }
                if ($closer -eq 'cmd+w') {
                    & $key 'cmd+shift+]'
                    & $key 'cmd+shift+]'
                    if (-not $c.Unattended -and -not (Read-VerifyAnswer -Context $c -Question 'Settings: did the main tab move twice to the right (Cmd-Shift-])?')) { $problems.Add('Cmd-Shift-] did not switch the tabs (user)') }
                }
                & $key $closer
                if (-not (Wait-VerifyCondition -TimeoutSeconds 6 -Condition { -not (Get-VerifyMainWindow @(Get-ProcessWindows $c $app.Id)) })) { $problems.Add("$closer did not close Settings") }
            }
            if ((Get-FileHash -LiteralPath $settingsFile).Hash -ne $hash) { $problems.Add('closing Settings with a shortcut changed settings.json') }
            foreach ($window in 'about', 'diagnostics') {
                if (-not (& $frontWith $window)) { $problems.Add("$window did not come to the front"); continue }
                & $key 'cmd+w'
                if (-not (Wait-VerifyCondition -TimeoutSeconds 6 -Condition { -not (Get-VerifyMainWindow @(Get-ProcessWindows $c $app.Id)) })) { $problems.Add("Cmd-W did not close $window") }
            }
        }
        finally {
            $null = Stop-TestApp $data $app
            $null = Save-VerifyEvidence -Context $c -Name 'app.log' -Content (Read-TestLog $data)
        }
        if ($problems.Count -gt 0) { return New-VerifyResult FAIL ($problems -join '; ') }
        New-VerifyResult PASS 'Ctrl-Cmd-F, Esc, Cmd-. and Cmd-W in the camera window; Cmd-Shift-], Cmd-W and Cmd-. in Settings (no change saved); Cmd-W in About and Diagnostics. Remove the terminal from Accessibility again.'
    }

    New-VerifyCheck -Id 'mac.notification-click' -Kind Manual -Title 'a click on a delivered notification of the test bundle opens the printer web' `
        -Closes 'T-020 kritérium 2 (kliknutí otevře web tiskárny; aplikace běží)' -Action {
        param($c)
        $status = Get-TestNotificationStatus $c
        if ($status.Permission -ne 'Allowed') { Skip-VerifyCheck "the notifications of 'LayerPeek E2E' are $($status.Permission); run mac.notifications first." }
        $simulator = Get-Simulator $c
        Set-SimulatorScenario $simulator 'printing'
        $data = New-TestData -Context $c -Simulator $simulator -Notifications
        $null = Start-TestAppViaOpen -Context $c -TestData $data -Environment @{ LAYERPEEK_TEST_TOASTS = '1' }
        Start-Sleep -Seconds 12
        Set-SimulatorScenario $simulator 'paused'
        if (-not (Wait-VerifyCondition -TimeoutSeconds 45 -Condition { (Get-TestNotificationStatus $c).Delivered -gt 0 })) { return New-VerifyResult FAIL 'no notification of the test bundle was delivered.' }
        Write-VerifyStep $c "Click the 'LayerPeek E2E' notification now (the banner, or in Notification Centre). Waiting up to 2 minutes." 'Yellow'
        $urls = Join-Path $data.Data 'opened-urls.txt'
        $clicked = Wait-VerifyCondition -TimeoutSeconds 120 -Condition { (Test-Path -LiteralPath $urls) -and (Get-Content -LiteralPath $urls | Where-Object { $_ }) }
        $null = Save-VerifyEvidence -Context $c -Name 'opened-urls.txt' -Path $urls
        $null = Save-VerifyEvidence -Context $c -Name 'app.log' -Content (Read-TestLog $data)
        $null = Send-VerifyInstanceCommand -SocketPath $data.Socket -Command 'quit'
        if (-not $clicked) { return New-VerifyResult FAIL 'no address was opened within 2 minutes of the notification.' }
        New-VerifyResult PASS "the click opened $((Get-Content -LiteralPath $urls | Select-Object -First 1)) (recorded by the test mode instead of a browser)."
    }

    New-VerifyCheck -Id 'mac.socket-other-user' -Kind Permission -Permission 'sudo (your administrator password) to connect to the instance socket as the user nobody' `
        -Title 'a connection of another user to the instance socket is refused' `
        -Closes 'T-053 kritérium 1 (spojení od jiného UID se odmítne, skutečný test)' -Action {
        param($c)
        $data = New-TestData -Context $c -SharedTemp
        $app = Start-TestApp -Context $c -TestData $data
        try {
            if (-not (Send-VerifyInstanceCommand -SocketPath $data.Socket -Command 'about')) { return New-VerifyResult FAIL 'the own user could not use the instance channel (control).' }
            # Reachable for another user: the folder and the socket open to everyone, as an attacker would need.
            & /bin/chmod 755 $data.Data
            & /bin/chmod 777 $data.Socket
            Write-VerifyStep $c 'sudo asks for your password (only for: sudo -u nobody nc -U <socket>).' 'Yellow'
            & /usr/bin/sudo -v
            if ($LASTEXITCODE -ne 0) { Skip-VerifyCheck 'sudo was not authorized.' }
            $attempt = Invoke-VerifyTool -Context $c -Label 'nc-as-nobody' -FilePath '/bin/sh' -ArgumentList @('-c', "printf 'activate\n' | /usr/bin/sudo -n -u nobody /usr/bin/nc -U -w 3 '$($data.Socket)'; echo `"nc exit code `$?`"")
            & /usr/bin/sudo -k
            Start-Sleep -Seconds 2
            $log = Read-TestLog $data
            $null = Save-VerifyEvidence -Context $c -Name 'app.log' -Content $log
            if ($log -notmatch 'Refused a connection of another user \(uid \d+\)') { return New-VerifyResult FAIL "the app did not log a refused connection; nc printed: $($attempt.Output -join ' ')" }
            if ($log -match 'The instance command Activate reached') { return New-VerifyResult FAIL 'the command of the other user reached the app.' }
            New-VerifyResult PASS "refused: '$((($log -split "`n") | Where-Object { $_ -match 'Refused a connection' } | Select-Object -First 1).Trim())'"
        }
        finally { $null = Stop-TestApp $data $app }
    }

    New-VerifyCheck -Id 'mac.display-sleep' -Kind Permission -Permission 'putting the display to sleep (pmset displaysleepnow) and waking it (caffeinate -u); you may have to unlock the screen' `
        -Title 'real start while every display sleeps: monitoring runs, the menu bar item appears after wake-up' `
        -Closes 'T-096 kritérium 1 (skutečný start s uspaným displejem)', 'T-099 kritérium 1 (ikona po probuzení se stavem)', 'T-099 kritérium 3 (skutečný start se zhasnutým displejem)' -Action {
        param($c)
        $simulator = Get-Simulator $c
        Set-SimulatorScenario $simulator 'printing'
        $data = New-TestData -Context $c -Simulator $simulator
        $null = Get-TestBundle $c
        Write-VerifyStep $c 'The display goes to sleep in 5 s and the script wakes it after about 40 s. Do not touch mouse or keyboard meanwhile; unlock the screen afterwards if asked.' 'Yellow'
        Start-Sleep -Seconds 5
        & /usr/bin/pmset displaysleepnow
        Start-Sleep -Seconds 5
        $app = Start-TestApp -Context $c -TestData $data -NoWait
        try {
            $waiting = Wait-VerifyCondition -TimeoutSeconds 30 -Condition { (Read-TestLog $data) -match 'No active display' }
            $monitoring = Wait-VerifyCondition -TimeoutSeconds 30 -Condition { (Read-TestLog $data) -match 'Starting printer monitoring and notifications while waiting for an active display\.' }
            Set-SimulatorScenario $simulator 'paused'
            Start-Sleep -Seconds 20
            & /usr/bin/caffeinate -u -t 2
            Write-VerifyStep $c 'Display woken. Unlock the screen if it is locked.' 'Yellow'
            $ui = Wait-VerifyCondition -TimeoutSeconds 180 -Condition { (Read-TestLog $data) -match 'starting the UI\.' }
            $item = Wait-VerifyCondition -TimeoutSeconds 30 -Condition { Test-VerifyMenuBarItem @(Get-ProcessWindows $c $app.Id) }
            $log = Read-TestLog $data
            $null = Save-VerifyEvidence -Context $c -Name 'app.log' -Content $log
            $problems = @(
                if (-not $waiting) { "no 'No active display' in the log (did the display sleep?)" }
                if (-not $monitoring) { 'monitoring did not start while waiting' }
                if (-not $ui) { 'the UI did not start after the wake-up' }
                if ($log -notmatch 'The menu bar item attaches to the monitoring that already runs\.') { 'the menu bar item did not attach to the running monitoring' }
                if (-not $item) { 'no status item in the menu bar after the wake-up' }
                if ($app.HasExited) { "the app ended with $($app.ExitCode)" }
            )
            if ($problems.Count -gt 0) { return New-VerifyResult FAIL ($problems -join '; ') }
            if (-not (Read-VerifyAnswer -Context $c -Question 'Does the [E2E] menu bar icon show the paused printer (not "Starting...")?')) { return New-VerifyResult FAIL 'the icon did not show the state of the running monitoring (user).' }
            New-VerifyResult MANUAL 'waited without a display with monitoring running; after the wake-up the menu bar item attached and showed the current state.'
        }
        finally { $null = Stop-TestApp $data $app }
    }

    New-VerifyCheck -Id 'mac.gatekeeper-dialog' -Kind Manual -Title 'the Gatekeeper dialogs of the downloaded .dmg (guided, session 1 of docs/macos-manual-testing.md)' `
        -Closes 'T-019 kritérium 12 (dialog Gatekeeperu, texty do T-019)', 'T-026 kritérium 3 (podklad: aplikace z .dmg se spustí)' -Action {
        param($c)
        Write-VerifyStep $c 'Follow docs/macos-manual-testing.md, session 1, steps 2-4: download the .dmg with Safari, start LayerPeek from the disk image, then install it to Applications and start it from Finder (Open Anyway).'
        $null = Read-VerifyText -Context $c -Question 'Press Enter when done'
        $quarantine = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Downloads' "LayerPeek-$(Get-AppVersion)-osx-arm64.dmg"
        if (Test-Path -LiteralPath $quarantine) { $null = Invoke-VerifyTool -Context $c -Label 'xattr-download' -FilePath '/usr/bin/xattr' -ArgumentList @('-p', 'com.apple.quarantine', $quarantine) }
        $title = Read-VerifyText -Context $c -Question 'Exact text of the first Gatekeeper dialog (copy it; empty if none appeared)'
        $buttons = Read-VerifyText -Context $c -Question 'Its buttons'
        $null = Save-VerifyEvidence -Context $c -Name 'dialog.txt' -Content @("Dialog: $title", "Buttons: $buttons")
        $ok = @(
            Confirm-VerifyStep $c 'Did "Open Anyway" in Privacy & Security start LayerPeek, and does the next start open without asking?'
            Confirm-VerifyStep $c 'From the .dmg window: is "Open at login" in Settings > Startup and updates disabled with a reason?'
        )
        if ($ok -contains $false) { return New-VerifyResult FAIL 'the user answered no (steps.txt).' }
        New-VerifyResult MANUAL "dialog '$title' [$buttons]; Open Anyway worked (dialog.txt: copy into T-019 and compare with docs/release.md)."
    }

    New-VerifyCheck -Id 'mac.login-item' -Kind Manual -Title 'Open at login after a real log out and log in (/Applications/LayerPeek.app, two runs)' `
        -Closes 'T-019 kritérium 9 (autostart po přihlášení)' -Action {
        param($c)
        $installed = '/Applications/LayerPeek.app'
        if (-not (Test-Path -LiteralPath $installed)) { Skip-VerifyCheck 'LayerPeek is not in /Applications (install it from the .dmg first, mac.gatekeeper-dialog).' }
        if (-not (Read-VerifyAnswer -Context $c -Question 'Have you switched on "Open at login" in LayerPeek (Settings > Startup and updates), saved, and logged out and in since then?')) {
            Write-VerifyStep $c 'Do now: docs/macos-manual-testing.md session 7, steps 1-2 (switch on, Save, log out, log in). Then run: pwsh tools/macos-verify.ps1 -Only mac.login-item' 'Yellow'
            Skip-VerifyCheck 'pending: switch on Open at login, log out and in, then run -Only mac.login-item again.'
        }
        $ps = Invoke-VerifyTool -Context $c -Label 'ps' -FilePath '/bin/ps' -ArgumentList @('-axww', '-o', 'pid=,lstart=,command=')
        $null = Invoke-VerifyTool -Context $c -Label 'who' -FilePath '/usr/bin/who'
        $running = @($ps.Output | Where-Object { $_ -match [regex]::Escape("$installed/Contents/MacOS/LayerPeek") })
        if ($running.Count -eq 0) { return New-VerifyResult FAIL 'LayerPeek from /Applications is not running after the login.' }
        $ok = @(
            Confirm-VerifyStep $c 'Did LayerPeek start by itself at the login (menu bar icon), and is the start time in ps.txt right after your login (who.txt)?'
            Confirm-VerifyStep $c 'Session 7 step 3: switched off in System Settings > Login Items, does LayerPeek Settings show the hint and the button that opens Login Items?'
            Confirm-VerifyStep $c 'Session 7 step 4: switched off in LayerPeek and saved, is the item gone from Login Items?'
        )
        if ($ok -contains $false) { return New-VerifyResult FAIL 'the user answered no (steps.txt).' }
        New-VerifyResult MANUAL "started at login: $($running[0].Trim())"
    }

    New-VerifyCheck -Id 'mac.local-network' -Kind Manual -Title 'the Local Network dialog in the system language, Deny and Allow (real printer in the LAN, /Applications app)' `
        -Closes 'T-097 kritérium 3 (dialog v jazyce systému)', 'T-097 kritérium 4 (odepření bez pádu, srozumitelná hláška)' -Action {
        param($c)
        if (-not (Test-Path -LiteralPath '/Applications/LayerPeek.app')) { Skip-VerifyCheck 'LayerPeek is not in /Applications.' }
        Use-ProductionProfile $c {
            Write-VerifyStep $c 'Follow docs/macos-manual-testing.md, session 4 (start LayerPeek from Finder, add your printer in the LAN). The dialog appears with the first connection to the LAN.'
            $text = Read-VerifyText -Context $c -Question 'Exact text of the Local Network dialog and the system language (empty if none appeared)'
            $null = Save-VerifyEvidence -Context $c -Name 'dialog.txt' -Content $text
            $ok = @(
                Confirm-VerifyStep $c 'Is the dialog text the LayerPeek text in the system language?'
                Confirm-VerifyStep $c 'After "Don''t Allow": no crash, Diagnostics / Test connection show the Local Network hint?'
                Confirm-VerifyStep $c 'Allowed again in Privacy & Security > Local Network: does the connection come back by itself?'
            )
            if ($ok -contains $false) { New-VerifyResult FAIL 'the user answered no (steps.txt).' }
            else { New-VerifyResult MANUAL "dialog: $text" }
        }
    }

    New-VerifyCheck -Id 'mac.notifications-production' -Kind Manual -Title 'notifications of /Applications/LayerPeek.app, and the hint when they are switched off' `
        -Closes 'T-020 kritérium 1 (ze sestavené .app)', 'T-020 kritérium 3 (odepřené oprávnění: hláška a tlačítko v Nastavení)' -Action {
        param($c)
        if (-not (Test-Path -LiteralPath '/Applications/LayerPeek.app')) { Skip-VerifyCheck 'LayerPeek is not in /Applications.' }
        $simulator = Get-Simulator $c
        Set-SimulatorScenario $simulator 'printing'
        Use-ProductionProfile $c {
            Write-VerifyStep $c "Start /Applications/LayerPeek.app from Finder and add the simulator printer: address $($simulator.Address), user $($simulator.User), password $($simulator.Password) (test values of the simulator, not a secret)."
            $null = Read-VerifyText -Context $c -Question 'Press Enter when the printer shows as printing'
            Set-SimulatorScenario $simulator 'paused'
            Write-VerifyStep $c 'The simulator printer is paused now: macOS asks about notifications the first time; allow them.'
            $ok = @(
                Confirm-VerifyStep $c 'Did a notification with the printer name and the LayerPeek icon appear?'
                Confirm-VerifyStep $c 'Switched off in System Settings > Notifications > LayerPeek: does Settings > General show the red hint, and does its button open that page? Switched on again, does the hint go away?'
                Confirm-VerifyStep $c 'Have you removed the simulator printer in Settings again (its password leaves the Keychain)?'
            )
            if ($ok -contains $false) { New-VerifyResult FAIL 'the user answered no (steps.txt).' }
            else { New-VerifyResult MANUAL 'notification of the installed app and the hint confirmed.' }
        }
    }

    New-VerifyCheck -Id 'mac.keyboard-physical' -Kind Manual -Title 'shortcuts on the built-in and an external (Logitech) keyboard, also with the Czech layout' `
        -Closes 'T-100 kritérium 4 (ověřeno uživatelem včetně Logitech klávesnice)' -Action {
        param($c)
        $simulator = Get-Simulator $c
        $data = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -Window ([ordered]@{ rememberLastPlacement = $false; startMode = 'Normal' })
        $app = Start-TestApp -Context $c -TestData $data
        try {
            Send-TestCommand $data "camera:$($script:PrinterId)"
            Write-VerifyStep $c 'The test app runs with a camera window (tray icon [E2E]). Follow docs/macos-manual-testing.md session 6 step 3 on the built-in keyboard, then on the external keyboard, then with the Czech layout.'
            $ok = @(
                Confirm-VerifyStep $c 'Built-in keyboard: do Ctrl-Cmd-F, Esc, Cmd-., Cmd-W, Cmd-Shift-] / [ and Cmd-Opt-arrows work as described?'
                Confirm-VerifyStep $c 'External (Logitech) keyboard: the same?'
                Confirm-VerifyStep $c 'Czech layout: do Cmd-Shift-] / [ (keys right of P) switch the tabs and nothing else happens?'
            )
            $notes = Read-VerifyText -Context $c -Question 'Notes (e.g. what fn+F11 did), or empty'
            $null = Save-VerifyEvidence -Context $c -Name 'notes.txt' -Content $notes
            if ($ok -contains $false) { return New-VerifyResult FAIL "the user answered no (steps.txt). $notes" }
            New-VerifyResult MANUAL "confirmed on both keyboards and the Czech layout. $notes"
        }
        finally { $null = Stop-TestApp $data $app }
    }

    New-VerifyCheck -Id 'mac.profile-unchanged' -Kind Auto -Final -Title 'the watched places of the user profile are unchanged after the run (tools/profile-snapshot.ps1)' `
        -Closes 'T-021 kritérium 7 (kontrola před/po)', 'T-095 (izolace testů na macOS, kontrola)' -Action {
        param($c)
        $before = $c.Shared.ProfileBefore
        if (-not $before -or -not (Test-Path -LiteralPath $before)) { return New-VerifyResult FAIL 'no snapshot from the start of the run.' }
        $comparison = Compare-ProfileSnapshot -Path $before -LoginItems:($c.Shared.LoginItems -eq $true)
        $null = Save-VerifyEvidence -Context $c -Name 'profile-before.json' -Path $before
        $null = Save-VerifyEvidence -Context $c -Name 'profile-changes.txt' -Content (@($comparison.Changes) + @($comparison.NotChecked | ForEach-Object { "not checked: $_" }))
        Remove-Item -LiteralPath $before -Force
        if ($comparison.Changes.Count -eq 0) { return New-VerifyResult PASS "no change$(if ($comparison.NotChecked.Count) { " (not checked: $($comparison.NotChecked -join ', '))" })." }
        $used = @($c.Shared.ProductionProfileUsed | Where-Object { $_ })
        if ($used.Count -gt 0) { Skip-VerifyCheck "$($comparison.Changes.Count) change(s), expected: the production app ran with your profile in $($used -join ', ') (profile-changes.txt)." }
        New-VerifyResult FAIL "$($comparison.Changes.Count) change(s): $($comparison.Changes[0]) (profile-changes.txt)"
    }
)

# ---------------------------------------------------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------------------------------------------------

if ($ListChecks) {
    Format-VerifyCheckList -Checks $checks | Write-Output
    exit 0
}

if ($SelfTest) {
    $problems = @(Invoke-VerifySelfTest -TempRoot ([System.IO.Path]::GetTempPath())) + @(Test-VerifyCheckDefinitions -Checks $checks -TasksRoot (Join-Path $script:Repo 'tasks'))
    foreach ($check in $checks) { if ($check.Id -notlike 'mac.*') { $problems += "The check $($check.Id) is not a mac.* check." } }
    if ($problems.Count -gt 0) {
        Write-Output 'SELF-TEST FAILED:'
        $problems | ForEach-Object { Write-Output "  $_" }
        exit 1
    }
    Write-Output "SELF-TEST PASSED: engine, both modes and $($checks.Count) check definitions."
    exit 0
}

if (-not $IsMacOS) {
    Write-Host 'tools/macos-verify.ps1 runs on macOS only (use tools/windows-verify.ps1 on Windows).' -ForegroundColor Red
    exit 2
}

try { $selected = @(Select-VerifyChecks -Checks $checks -Only $Only -Skip $Skip) }
catch { Write-Host $_.Exception.Message -ForegroundColor Red; exit 2 }
if ($selected.Count -eq 0) { Write-Host 'No check selected.' -ForegroundColor Red; exit 2 }
if ($PWD.ProviderPath -ne (Resolve-PhysicalPath $PWD.ProviderPath)) {
    Write-Host 'Note: the terminal is in the repository through a symbolic link; the script uses the physical path for dotnet.' -ForegroundColor Yellow
}

$root = if ($OutputRoot) { $OutputRoot } else { Join-Path $script:Repo 'artifacts' 'verify' }
$runFolder = New-VerifyRunFolder -Root $root
$context = New-VerifyContext -RunFolder $runFolder -Unattended:$Unattended
$context.Shared.ProductionProfileUsed = @()
Write-Host "LayerPeek macOS verification ($($context.Unattended ? 'unattended' : 'interactive')), $($selected.Count) check(s); report in $runFolder"

# Leftovers of an interrupted run: test bundles of earlier runs (and their LaunchServices registrations).
$bundleRoot = Join-Path $script:Repo 'artifacts' 'verify-bundle'
if (Test-Path -LiteralPath $bundleRoot) {
    foreach ($old in Get-ChildItem -LiteralPath $bundleRoot -Directory) {
        $app = Join-Path $old.FullName 'LayerPeek E2E.app'
        Remove-TestBundle ([pscustomobject]@{ Path = $app; Executable = Join-Path $app 'Contents' 'MacOS' 'LayerPeek'; Folder = $old.FullName })
    }
}
# The Keychain items of the test prefix (never the production service LayerPeek), also after a crashed test run.
Add-VerifyCleanup $context 'Keychain test items' {
    foreach ($service in Get-KeychainTestServices) {
        for ($i = 0; $i -lt 50; $i++) { $null = & /usr/bin/security delete-generic-password -s $service 2>&1; if ($LASTEXITCODE -ne 0) { break } }
    }
}
if ($selected | Where-Object Id -EQ 'mac.profile-unchanged') {
    $context.Shared.LoginItems = (-not $Unattended) -and (Read-VerifyAnswer -Context $context -Question 'Include the Login Items in the before/after check of the profile (macOS asks for an administrator authorization for sfltool)?')
    $context.Shared.ProfileBefore = Join-Path ([System.IO.Path]::GetTempPath()) "layerpeek-verify-profile-$(Split-Path -Leaf $runFolder).json"
    Save-ProfileSnapshot -Path $context.Shared.ProfileBefore -LoginItems:$context.Shared.LoginItems
}

$results = Invoke-VerifyRun -Checks $selected -Context $context
$info = Get-VerifyRunInfo -RepoRoot $script:Repo -Script 'tools/macos-verify.ps1' -Unattended:$Unattended -Selected @($selected.Id)
$report = Write-VerifyReport -Folder $runFolder -Info $info -Results $results
Write-Host ''
Format-VerifySummary -Results $results | ForEach-Object { Write-Host $_ }
Write-Host "Report: $($report.Markdown)"
Write-Host 'Known leftovers no tool can remove: "LayerPeek E2E" in System Settings > Notifications; the terminal in Privacy & Security > Accessibility if you allowed it for mac.keys.'
exit (Get-VerifyExitCode -Results $results)
