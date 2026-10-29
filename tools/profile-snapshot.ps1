<#
.SYNOPSIS
    Read-only before/after check of the places of the user profile that LayerPeek or its tests could change (T-095).

.DESCRIPTION
    -Save writes the current state of the watched places into a JSON file outside the repository; a later -Compare
    against that file lists every change and ends with exit code 1 (0 = no change). Typical use around a test run:

        pwsh tools/profile-snapshot.ps1 -Save /tmp/before.json
        dotnet test
        pwsh tools/profile-snapshot.ps1 -Compare /tmp/before.json

    Watched on macOS (docs/architecture.md, "Izolace testů na macOS"):
      - ~/Library/Application Support/LayerPeek and ~/Library/Logs/LayerPeek (every file with its size and SHA-256),
      - ~/Library/LaunchAgents (every file, whoever it belongs to),
      - Login Items (sfltool dumpbtm, only the lines that mention LayerPeek); without the administrator authorization
        that sfltool asks for they are reported as not checked, never as a change,
      - names of the Keychain services that contain "LayerPeek" or "Prusa" (security dump-keychain without -d: the
        attributes only, never a secret; nothing but the service names is kept).
    Watched on Windows:
      - %APPDATA%\LayerPeek and %LOCALAPPDATA%\LayerPeek (files with size and SHA-256),
      - the Startup folder of the user (every file),
      - target names of the Credential Manager entries that contain "LayerPeek" (cmdkey /list, no secrets).

    The script only reads: it creates, changes and deletes nothing but the -Save file. Files that cannot be read (held
    open without sharing) are listed with their size and "unreadable" instead of a hash.

.PARAMETER Save
    File to write the current state to (JSON). Must not be inside the repository.

.PARAMETER Compare
    File written by -Save before; the current state is compared with it.

.EXAMPLE
    pwsh tools/profile-snapshot.ps1 -Save "$env:TMPDIR/layerpeek-profile-before.json"

.EXAMPLE
    pwsh tools/profile-snapshot.ps1 -Compare "$env:TMPDIR/layerpeek-profile-before.json"
#>
#Requires -Version 7.2
[CmdletBinding()]
param(
    # Not mandatory, so that the Pester tests can dot-source the script; the main part requires exactly one of them.
    [string] $Save,
    [string] $Compare
)

Set-StrictMode -Version Latest

$script:FormatVersion = 1
$script:RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

<#
.SYNOPSIS
    The watched places of this OS: name -> folder (files listed recursively) and the probes of the system stores.
#>
function Get-WatchedLocations {
    param([string] $HomeDirectory = [Environment]::GetFolderPath('UserProfile'))
    if ($IsMacOS) {
        $library = Join-Path $HomeDirectory 'Library'
        return [ordered]@{
            'Application Support/LayerPeek' = Join-Path $library 'Application Support' 'LayerPeek'
            'Logs/LayerPeek'                = Join-Path $library 'Logs' 'LayerPeek'
            'LaunchAgents'                  = Join-Path $library 'LaunchAgents'
        }
    }
    if ($IsWindows) {
        return [ordered]@{
            '%APPDATA%\LayerPeek'      = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'LayerPeek'
            '%LOCALAPPDATA%\LayerPeek' = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'LayerPeek'
            'Startup'                  = [Environment]::GetFolderPath('Startup')
        }
    }
    throw 'Only macOS and Windows are supported.'
}

<#
.SYNOPSIS
    Every entry below a folder as sorted lines "relative/path/" (folder) or "relative/path  size  sha256" (file); an
    empty list when the folder does not exist, and the single line "(missing)" for a missing folder so that creating an
    empty folder is a change too.
#>
function Get-FolderState {
    param([Parameter(Mandatory)] [string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return @('(missing)') }
    $root = (Get-Item -LiteralPath $Path -Force).FullName.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $lines = foreach ($item in Get-ChildItem -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue) {
        $relative = $item.FullName.Substring($root.Length).Replace('\', '/')
        if ($item.PSIsContainer) { "$relative/"; continue }
        $hash = try { (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant() } catch { 'unreadable' }
        "$relative  $($item.Length)  $hash"
    }
    @('(exists)') + @($lines | Sort-Object -CaseSensitive)
}

<#
.SYNOPSIS
    Login Items of macOS that mention LayerPeek (sfltool dumpbtm), as one array (also when empty). Returns $null when
    they cannot be read: sfltool then prints only an error, because it asks for an administrator authorization that a
    non-interactive run cannot give.
#>
function Get-MacLoginItems {
    param([scriptblock] $DumpBtm = { & /usr/bin/sfltool dumpbtm 2>$null })
    $output = @(& $DumpBtm)
    if ($output.Count -eq 0) { return $null }
    # The comma keeps an empty list a list: "no LayerPeek item" is a state, unlike "not readable".
    , [string[]] @($output | Where-Object { $_ -match '(?i)layerpeek' } | ForEach-Object { ([string] $_).Trim() } | Sort-Object -Unique -CaseSensitive)
}

<#
.SYNOPSIS
    Names of the Keychain services that contain LayerPeek or Prusa, each with how many items use it. Reads only the
    attributes ("svce"); the secrets are never requested (no -d).
#>
function Get-MacKeychainServices {
    param([scriptblock] $DumpKeychain = { & /usr/bin/security dump-keychain 2>$null })
    $services = foreach ($line in @(& $DumpKeychain)) {
        if ($line -notmatch '^\s*"svce"<blob>="(?<name>[^"]*)"') { continue }
        $name = $Matches.name
        if ($name -match '(?i)layerpeek|prusa') { $name }
    }
    @($services | Group-Object -NoElement -CaseSensitive | Sort-Object Name -CaseSensitive | ForEach-Object { "$($_.Name)  x$($_.Count)" })
}

<#
.SYNOPSIS
    Target names of the Windows Credential Manager entries that contain LayerPeek (cmdkey /list; the labels of its
    output are localized, so only the target names are matched).
#>
function Get-WindowsCredentialTargets {
    param([scriptblock] $ListCredentials = { & cmdkey.exe /list 2>$null })
    $targets = foreach ($line in @(& $ListCredentials)) {
        foreach ($match in [regex]::Matches([string] $line, '(?i)(?:target=)?(LayerPeek\S*)')) { $match.Groups[1].Value }
    }
    @($targets | Sort-Object -Unique -CaseSensitive)
}

<#
.SYNOPSIS
    The state of every watched place: an ordered dictionary name -> sorted lines, or -> $null when it cannot be read.
#>
function Get-ProfileState {
    param(
        [System.Collections.IDictionary] $Locations = (Get-WatchedLocations),
        [scriptblock] $LoginItems = $(if ($IsMacOS) { { Get-MacLoginItems } } else { $null }),
        [scriptblock] $Credentials = $(if ($IsMacOS) { { Get-MacKeychainServices } } elseif ($IsWindows) { { Get-WindowsCredentialTargets } } else { $null })
    )
    $state = [ordered]@{}
    foreach ($name in $Locations.Keys) { $state[$name] = @(Get-FolderState -Path $Locations[$name]) }
    if ($LoginItems) {
        # Assigned in each branch: an 'if' expression would turn an empty list into $null ("not readable").
        $items = & $LoginItems
        if ($null -eq $items) { $state['Login Items'] = $null } else { $state['Login Items'] = [string[]] @($items) }
    }
    if ($Credentials) { $state[$(if ($IsWindows) { 'Credential Manager' } else { 'Keychain services' })] = @(& $Credentials) }
    $state
}

<#
.SYNOPSIS
    Differences between two states as lines "<place>: + line" / "<place>: - line"; places that could not be read in one
    of the states are not compared and come back in NotChecked.
#>
function Compare-ProfileState {
    param([Parameter(Mandatory)] [System.Collections.IDictionary] $Before, [Parameter(Mandatory)] [System.Collections.IDictionary] $After)
    $changes = [System.Collections.Generic.List[string]]::new()
    $notChecked = [System.Collections.Generic.List[string]]::new()
    $names = @(@($Before.Keys) + @($After.Keys) | Select-Object -Unique)
    foreach ($name in $names) {
        # Not 'if' expressions: they would turn an empty list into $null.
        $old = $null
        $new = $null
        if ($Before.Contains($name)) { $old = $Before[$name] }
        if ($After.Contains($name)) { $new = $After[$name] }
        if ($null -eq $old -or $null -eq $new) { $notChecked.Add($name); continue }
        $oldSet = [System.Collections.Generic.HashSet[string]]::new([string[]] @($old), [StringComparer]::Ordinal)
        $newSet = [System.Collections.Generic.HashSet[string]]::new([string[]] @($new), [StringComparer]::Ordinal)
        foreach ($line in @($old)) { if (-not $newSet.Contains($line)) { $changes.Add("${name}: - $line") } }
        foreach ($line in @($new)) { if (-not $oldSet.Contains($line)) { $changes.Add("${name}: + $line") } }
    }
    [pscustomobject]@{ Changes = $changes.ToArray(); NotChecked = $notChecked.ToArray() }
}

function Assert-OutsideRepository {
    param([Parameter(Mandatory)] [string] $Path, [string] $RepoRoot = $script:RepoRoot)
    $full = [System.IO.Path]::GetFullPath($Path)
    $root = $RepoRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if ($full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "The snapshot file must be outside the repository: $full" }
}

function Save-ProfileState {
    param([Parameter(Mandatory)] [string] $Path, [Parameter(Mandatory)] [System.Collections.IDictionary] $State)
    Assert-OutsideRepository -Path $Path
    $document = [ordered]@{ Version = $script:FormatVersion; Created = [DateTime]::UtcNow.ToString('o'); State = $State }
    [System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($Path), ($document | ConvertTo-Json -Depth 5) + "`n", [System.Text.UTF8Encoding]::new($false))
}

function Read-ProfileState {
    param([Parameter(Mandatory)] [string] $Path)
    $document = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable
    if ($document.Version -ne $script:FormatVersion) { throw "Unsupported snapshot format $($document.Version) in $Path." }
    $state = [ordered]@{}
    foreach ($name in $document.State.Keys) {
        $value = $document.State[$name]
        if ($null -eq $value) { $state[$name] = $null } else { $state[$name] = [string[]] @($value) }
    }
    $state
}

<# .SYNOPSIS -Save or -Compare; returns the exit code. #>
function Invoke-Main {
    param(
        [string] $Save,
        [string] $Compare,
        [scriptblock] $ReadState = { Get-ProfileState }
    )
    $state = & $ReadState
    if ($Save) {
        Save-ProfileState -Path $Save -State $state
        $unread = @($state.Keys | Where-Object { $null -eq $state[$_] })
        Write-Host "Profile state saved to $([System.IO.Path]::GetFullPath($Save))."
        foreach ($name in $unread) { Write-Host "Not readable, not checked: $name" -ForegroundColor Yellow }
        return 0
    }
    $result = Compare-ProfileState -Before (Read-ProfileState -Path $Compare) -After $state
    foreach ($name in $result.NotChecked) { Write-Host "Not readable, not checked: $name" -ForegroundColor Yellow }
    if ($result.Changes.Count -eq 0) {
        Write-Host 'No change in the watched places of the user profile.' -ForegroundColor Green
        return 0
    }
    Write-Host "$($result.Changes.Count) change(s) in the user profile:" -ForegroundColor Red
    foreach ($change in $result.Changes) { Write-Host "  $change" }
    return 1
}

# Dot-sourced (the Pester tests): only the functions above.
if ($MyInvocation.InvocationName -ne '.') {
    $ErrorActionPreference = 'Stop'
    if ([string]::IsNullOrEmpty($Save) -eq [string]::IsNullOrEmpty($Compare)) {
        Write-Host 'Pass exactly one of -Save <file> or -Compare <file> (see Get-Help).' -ForegroundColor Red
        exit 2
    }
    exit (Invoke-Main -Save $Save -Compare $Compare)
}
