#Requires -Version 7.0
<#
.SYNOPSIS
    Records PrusaLink API responses of a real printer as test fixtures.

.DESCRIPTION
    Downloads /api/v1/status and /api/v1/job (plus /api/version and /api/v1/info) from the printer and stores them
    in tests/PrusaTrayDiagnostic.PrusaLink.Tests/Fixtures/recorded/<Printer>/<State>/. The password is asked for
    interactively (hidden) and is neither stored nor printed. The serial number is replaced by a placeholder.

    Run it once per printer state you want covered, e.g. idle, printing, paused, attention (filament runout / MMU),
    finished, stopped.

.EXAMPLE
    ./tools/record-prusalink-fixtures.ps1 -Address 192.168.1.20 -Printer core-one -State printing
#>
param(
    [Parameter(Mandatory)] [string] $Address,
    [Parameter(Mandatory)] [ValidatePattern('^[a-z0-9-]+$')] [string] $Printer,
    [Parameter(Mandatory)] [ValidatePattern('^[a-z0-9-]+$')] [string] $State
)

$ErrorActionPreference = 'Stop'

$trimmed = $Address.Trim()
$baseUri = $(if ($trimmed -match '://') { $trimmed } else { "http://$trimmed" }).TrimEnd('/')
$password = Read-Host -AsSecureString -Prompt "PrusaLink password for user 'maker' (shown on the printer: Settings > Network > PrusaLink)"
$credential = [pscredential]::new('maker', $password)
$target = Join-Path $PSScriptRoot "../tests/PrusaTrayDiagnostic.PrusaLink.Tests/Fixtures/recorded/$Printer/$State"
New-Item -ItemType Directory -Force -Path $target | Out-Null

function Save-Endpoint([string] $path, [string] $fileName) {
    # Digest authentication is negotiated by the .NET handler behind Invoke-WebRequest.
    $response = Invoke-WebRequest -Uri "$baseUri$path" -Credential $credential -AllowUnencryptedAuthentication `
        -Headers @{ Accept = 'application/json' } -SkipHttpErrorCheck
    $file = Join-Path $target $fileName
    if ($response.StatusCode -eq 204) {
        Remove-Item -Path $file -ErrorAction SilentlyContinue
        Write-Host "$path -> 204 No Content (no file)"
        return
    }

    if ($response.StatusCode -ne 200) {
        throw "$path -> HTTP $($response.StatusCode). Check the address and password."
    }

    $content = $response.Content -replace '"serial"\s*:\s*"[^"]*"', '"serial":"SN-ANONYMIZED"'
    Set-Content -Path $file -Value $content -NoNewline -Encoding utf8NoBOM
    Write-Host "$path -> $file"
}

Save-Endpoint '/api/v1/status' 'status.json'
Save-Endpoint '/api/v1/job' 'job.json'
Save-Endpoint '/api/version' 'version.json'
Save-Endpoint '/api/v1/info' 'info.json'
Write-Host "Done. Review the files before committing them."
