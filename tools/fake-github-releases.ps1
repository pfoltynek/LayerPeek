<#
.SYNOPSIS
    A stand-in for the GitHub REST release list on 127.0.0.1, for the real-UI update check of tools/macos-verify.ps1
    (check mac.update-check): the Debug app in the test mode asks it instead of GitHub (LAYERPEEK_TEST_UPDATE_SOURCE).

.DESCRIPTION
    Answers GET /repos/pfoltynek/LayerPeek/releases with one release -Version (no draft, no pre-release) that has the
    checksum files of both OSes, which the app requires before it offers a release (GitHubReleaseChecker, T-113); any
    other request gets 404. Each answer waits -DelayMilliseconds first, so that the app's "Checking…" stays visible.
    Prints "Address: http://127.0.0.1:<port>/" once it listens, appends one line per request to -RequestLog (UTC time
    with milliseconds, request line, status) and ends when -ParentProcessId ends (a killed verification run leaves no
    server behind). One request at a time is plenty for one app.

.EXAMPLE
    pwsh -NoProfile -File tools/fake-github-releases.ps1 -Version 9.9.0 -DelayMilliseconds 1500 -RequestLog /tmp/requests.txt
#>
#Requires -Version 7.2
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')] [string] $Version = '9.9.0',
    [ValidateRange(0, 30000)] [int] $DelayMilliseconds = 1500,
    [string] $RequestLog,
    [int] $ParentProcessId
)

$ErrorActionPreference = 'Stop'
$releasesPath = '/repos/pfoltynek/LayerPeek/releases'
$release = [ordered]@{
    tag_name   = "v$Version"
    html_url   = "https://github.com/pfoltynek/LayerPeek/releases/tag/v$Version"
    draft      = $false
    prerelease = $false
    assets     = @(
        [ordered]@{ name = 'SHA256SUMS-macos.txt'; state = 'uploaded' }
        [ordered]@{ name = 'SHA256SUMS-windows.txt'; state = 'uploaded' }
        [ordered]@{ name = "LayerPeek-$Version-osx-arm64.zip"; state = 'uploaded' }
        [ordered]@{ name = "LayerPeek-$Version-win-x64.zip"; state = 'uploaded' }
    )
}
$body = [System.Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject @($release) -Depth 5 -Compress))
$notFound = [System.Text.Encoding]::UTF8.GetBytes('{"message":"Not Found"}')

function Test-ParentAlive {
    if ($ParentProcessId -le 0) { return $true }
    $parent = Get-Process -Id $ParentProcessId -ErrorAction SilentlyContinue
    $null -ne $parent -and -not $parent.HasExited
}

function Read-RequestLine([System.Net.Sockets.NetworkStream] $Stream) {
    # The request line and the headers up to the empty line; a GET has no body.
    $buffer = [byte[]]::new(4096)
    $head = [System.Text.StringBuilder]::new()
    while ($head.Length -lt 65536 -and -not $head.ToString().Contains("`r`n`r`n")) {
        $read = $Stream.Read($buffer, 0, $buffer.Length)
        if ($read -le 0) { break }
        $null = $head.Append([System.Text.Encoding]::ASCII.GetString($buffer, 0, $read))
    }
    ($head.ToString() -split "`r`n", 2)[0]
}

$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start()
try {
    [Console]::Out.WriteLine("Address: http://127.0.0.1:$($listener.LocalEndpoint.Port)/")
    [Console]::Out.Flush()
    while (Test-ParentAlive) {
        if (-not $listener.Pending()) { Start-Sleep -Milliseconds 100; continue }
        $client = $listener.AcceptTcpClient()
        try {
            $client.ReceiveTimeout = 10000
            $client.SendTimeout = 10000
            $stream = $client.GetStream()
            $requestLine = Read-RequestLine $stream
            $parts = $requestLine -split ' '
            $known = $parts.Count -gt 1 -and $parts[0] -ceq 'GET' -and ($parts[1] -split '\?', 2)[0] -ceq $releasesPath
            $content = if ($known) { $body } else { $notFound }
            $status = if ($known) { '200 OK' } else { '404 Not Found' }
            if ($RequestLog) { Add-Content -LiteralPath $RequestLog -Value "$([DateTimeOffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffZ')) $requestLine -> $status" }
            if ($known -and $DelayMilliseconds -gt 0) { Start-Sleep -Milliseconds $DelayMilliseconds }
            $header = [System.Text.Encoding]::ASCII.GetBytes("HTTP/1.1 $status`r`nContent-Type: application/json; charset=utf-8`r`nContent-Length: $($content.Length)`r`nConnection: close`r`n`r`n")
            $stream.Write($header, 0, $header.Length)
            $stream.Write($content, 0, $content.Length)
            $stream.Flush()
        }
        catch [System.IO.IOException], [System.Net.Sockets.SocketException] {
            # The client went away; the next one is served.
        }
        finally { $client.Dispose() }
    }
}
finally { $listener.Stop() }
