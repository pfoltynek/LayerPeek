<#
.SYNOPSIS
    Pester 5+ tests of the maintainer tool for a new FFmpeg version on macOS (build/macos/update-ffmpeg-source.ps1, T-026).

.DESCRIPTION
    The script is dot-sourced (its main part does not run). Downloads, the Homebrew formula and gpg are stand-ins: no
    network, no gpg, nothing of the real repository is changed (-Update writes into copies in a temporary folder).

    Temporary files live in one folder under $env:LAYERPEEK_PESTER_TESTS_TEMP (set by the xUnit wrapper
    tests/LayerPeek.Scripts.Tests, which also deletes it after a crash) or under the system temp folder.
    AfterAll deletes the folder.

.EXAMPLE
    Invoke-Pester build/macos/update-ffmpeg-source.Tests.ps1 -Output Detailed
#>
#Requires -Version 7.2

BeforeAll {
    . (Join-Path $PSScriptRoot 'update-ffmpeg-source.ps1')

    $base = if ($env:LAYERPEEK_PESTER_TESTS_TEMP) { $env:LAYERPEEK_PESTER_TESTS_TEMP } else { [System.IO.Path]::GetTempPath() }
    $script:TempRoot = Join-Path $base "layerpeek-ps-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    $null = New-Item -ItemType Directory -Path $script:TempRoot

    $script:Content = 'stand-in tarball'
    $script:ContentSha256 = (Get-FileHash -InputStream ([System.IO.MemoryStream]::new([System.Text.Encoding]::UTF8.GetBytes($script:Content))) -Algorithm SHA256).Hash.ToLowerInvariant()
    $script:Fpr = 'FCF986EA15E6E293A5644F10B4322F04D67658D8'

    function New-Formula([string] $Url, [string] $Checksum, [string] $Version = '9.0.3') {
        [pscustomobject]@{ versions = [pscustomobject]@{ stable = $Version }; urls = [pscustomobject]@{ stable = [pscustomobject]@{ url = $Url; checksum = $Checksum } } }
    }

    # Download stand-in: writes the stand-in tarball (and dummy .asc / key files) and records the URLs.
    function New-Download {
        $log = [System.Collections.Generic.List[string]]::new()
        $content = $script:Content
        $block = { param($Url, $Path) $log.Add($Url); [System.IO.File]::WriteAllText($Path, $content) }.GetNewClosure()
        [pscustomobject]@{ Block = $block; Log = $log }
    }

    function New-Repo {
        $root = Join-Path $script:TempRoot ([guid]::NewGuid().ToString('N').Substring(0, 8))
        $null = New-Item -ItemType Directory -Force -Path (Join-Path $root 'build'), (Join-Path $root 'src' 'LayerPeek.App' 'Licenses')
        $targets = @'
<Project>
  <PropertyGroup>
    <LayerPeekFFmpegUrl>https://example.invalid/win.zip</LayerPeekFFmpegUrl>
    <LayerPeekFFmpegMacSourceUrl>https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz</LayerPeekFFmpegMacSourceUrl>
    <LayerPeekFFmpegMacSourceSha256>8c3850283eb25fa026482078a04051e0be17347b09ef81a0849bec15a96e002e</LayerPeekFFmpegMacSourceSha256>
    <LayerPeekFFmpegMacSourceFolder>ffmpeg-9.0.2</LayerPeekFFmpegMacSourceFolder>
  </PropertyGroup>
</Project>
'@
        $licenses = @'
        public static Uri MacOSBuildUri { get; } = new("https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz");
        public static Uri WindowsBuildUri { get; } = new("https://example.invalid/win.zip");
'@
        [System.IO.File]::WriteAllText((Join-Path $root 'build' 'FFmpeg.targets'), $targets)
        [System.IO.File]::WriteAllText((Join-Path $root 'src' 'LayerPeek.App' 'Licenses' 'ThirdPartyLicenses.cs'), $licenses)
        $root
    }

    function Invoke-Quiet([scriptblock] $Action) {
        $output = & $Action 6>&1
        [pscustomobject]@{ Code = @($output)[-1]; Text = (@($output | Select-Object -SkipLast 1) | Out-String) }
    }

    # gpg stand-in: import succeeds, verify answers with the given status lines and exit code; records the calls.
    function New-Gpg([string[]] $VerifyLines, [int] $VerifyExit = 0, [int] $ImportExit = 0) {
        $calls = [System.Collections.Generic.List[string]]::new()
        $block = {
            param([string[]] $Arguments)
            $calls.Add($Arguments -join ' ')
            if ($Arguments -contains '--import') { return @{ ExitCode = $ImportExit; Output = @('[GNUPG:] IMPORT_OK 1') } }
            @{ ExitCode = $VerifyExit; Output = $VerifyLines }
        }.GetNewClosure()
        [pscustomobject]@{ Block = $block; Calls = $calls }
    }

    function Get-GoodLines([string] $Signer = $script:Fpr, [string] $Primary = $script:Fpr) {
        @("[GNUPG:] GOODSIG B4322F04D67658D8 FFmpeg release signing key <ffmpeg-devel@ffmpeg.org>",
          "[GNUPG:] VALIDSIG $Signer 2026-10-01 1790000000 0 4 0 1 10 00 $Primary")
    }
}

AfterAll {
    if ($script:TempRoot -and (Test-Path -LiteralPath $script:TempRoot)) { Remove-Item -LiteralPath $script:TempRoot -Recurse -Force }
}

Describe 'Source tarball' {
    It 'derives the official URL and folder from the version' {
        $source = Get-FFmpegSource -Version '9.0.3'
        $source.Url | Should -BeExactly 'https://ffmpeg.org/releases/ffmpeg-9.0.3.tar.xz'
        $source.FileName | Should -BeExactly 'ffmpeg-9.0.3.tar.xz'
        $source.Folder | Should -BeExactly 'ffmpeg-9.0.3'
    }

    It 'rejects something that is not a release version' -TestCases @(
        @{ V = '9.0.3/../x' }, @{ V = 'n9.0.3' }, @{ V = '9' }, @{ V = '' }
    ) {
        { Get-FFmpegSource -Version $V } | Should -Throw
    }
}

Describe 'Homebrew cross-check' {
    It 'returns the checksum of exactly the same URL in lower case' {
        $formula = New-Formula 'https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz' '8C3850283EB25FA026482078A04051E0BE17347B09EF81A0849BEC15A96E002E'
        Get-HomebrewChecksum -Formula $formula -Url 'https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz' |
            Should -BeExactly '8c3850283eb25fa026482078a04051e0be17347b09ef81a0849bec15a96e002e'
    }

    It 'fails when Homebrew is on another tarball' {
        $formula = New-Formula 'https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz' ('a' * 64) '9.0.2'
        { Get-HomebrewChecksum -Formula $formula -Url 'https://ffmpeg.org/releases/ffmpeg-9.0.3.tar.xz' } |
            Should -Throw -ExpectedMessage '*another tarball*version 9.0.2*'
    }

    It 'fails without a usable checksum' -TestCases @(@{ C = '' }, @{ C = 'abc' }, @{ C = ('g' * 64) }) {
        $formula = New-Formula 'https://ffmpeg.org/releases/ffmpeg-9.0.3.tar.xz' $C
        { Get-HomebrewChecksum -Formula $formula -Url 'https://ffmpeg.org/releases/ffmpeg-9.0.3.tar.xz' } | Should -Throw -ExpectedMessage '*no SHA256*'
    }

    It 'fails when the formula has no stable URL' {
        { Get-HomebrewChecksum -Formula ([pscustomobject]@{ urls = [pscustomobject]@{} }) -Url 'x' } | Should -Throw -ExpectedMessage '*urls.stable.url*'
    }
}

Describe 'GPG signature' {
    BeforeAll {
        $script:Home1 = Join-Path $script:TempRoot 'gnupg-sig'
    }

    It 'accepts a good signature of the pinned key and uses its own home folder' {
        $gpg = New-Gpg (Get-GoodLines)
        Test-GpgSignature -InvokeGpg $gpg.Block -GnuPgHome $script:Home1 -KeyFile 'k.asc' -Signature 't.asc' -Tarball 't' | Should -BeExactly 'Verified'
        $gpg.Calls.Count | Should -Be 2
        $gpg.Calls | ForEach-Object { $_ | Should -BeLike "--homedir $($script:Home1) --batch*" }
        $gpg.Calls[1] | Should -BeLike '*--verify t.asc t'
    }

    It 'accepts a signing subkey of the pinned primary key' {
        $gpg = New-Gpg (Get-GoodLines -Signer ('1' * 40))
        Test-GpgSignature -InvokeGpg $gpg.Block -GnuPgHome $script:Home1 -KeyFile 'k' -Signature 's' -Tarball 't' | Should -BeExactly 'Verified'
    }

    It 'rejects a good signature of another key' {
        $gpg = New-Gpg (Get-GoodLines -Signer ('1' * 40) -Primary ('2' * 40))
        { Test-GpgSignature -InvokeGpg $gpg.Block -GnuPgHome $script:Home1 -KeyFile 'k' -Signature 's' -Tarball 't' } | Should -Throw -ExpectedMessage '*not by the FFmpeg release signing key*'
    }

    It 'rejects a bad signature' {
        $gpg = New-Gpg @('[GNUPG:] BADSIG B4322F04D67658D8 FFmpeg release signing key') 1
        { Test-GpgSignature -InvokeGpg $gpg.Block -GnuPgHome $script:Home1 -KeyFile 'k' -Signature 's' -Tarball 't' } | Should -Throw -ExpectedMessage '*does not verify*'
    }

    It 'rejects a failed gpg run even with good status lines' {
        $gpg = New-Gpg (Get-GoodLines) 2
        { Test-GpgSignature -InvokeGpg $gpg.Block -GnuPgHome $script:Home1 -KeyFile 'k' -Signature 's' -Tarball 't' } | Should -Throw -ExpectedMessage '*does not verify*'
    }

    It 'fails when the key cannot be imported' {
        $gpg = New-Gpg (Get-GoodLines) 0 2
        { Test-GpgSignature -InvokeGpg $gpg.Block -GnuPgHome $script:Home1 -KeyFile 'k' -Signature 's' -Tarball 't' } | Should -Throw -ExpectedMessage '*could not import*'
    }
}

Describe 'Pinned values in the files' {
    It 'replaces the three macOS properties and nothing else' {
        $root = New-Repo
        $text = [System.IO.File]::ReadAllText((Join-Path $root 'build' 'FFmpeg.targets'))
        $new = Update-TargetsText -Text $text -Source (Get-FFmpegSource '9.0.3') -Sha256 ('b' * 64)
        $new | Should -Match '<LayerPeekFFmpegMacSourceUrl>https://ffmpeg\.org/releases/ffmpeg-9\.0\.3\.tar\.xz</LayerPeekFFmpegMacSourceUrl>'
        $new | Should -Match "<LayerPeekFFmpegMacSourceSha256>$('b' * 64)</LayerPeekFFmpegMacSourceSha256>"
        $new | Should -Match '<LayerPeekFFmpegMacSourceFolder>ffmpeg-9\.0\.3</LayerPeekFFmpegMacSourceFolder>'
        $new | Should -Match '<LayerPeekFFmpegUrl>https://example\.invalid/win\.zip</LayerPeekFFmpegUrl>'
    }

    It 'fails when a property is missing or duplicated' {
        { Update-TargetsText -Text '<Project />' -Source (Get-FFmpegSource '9.0.3') -Sha256 ('b' * 64) } | Should -Throw -ExpectedMessage '*exactly once (found 0)*'
        $twice = '<LayerPeekFFmpegMacSourceUrl>a</LayerPeekFFmpegMacSourceUrl><LayerPeekFFmpegMacSourceUrl>b</LayerPeekFFmpegMacSourceUrl>'
        { Update-TargetsText -Text $twice -Source (Get-FFmpegSource '9.0.3') -Sha256 ('b' * 64) } | Should -Throw -ExpectedMessage '*exactly once (found 2)*'
    }

    It 'replaces MacOSBuildUri only' {
        $root = New-Repo
        $text = [System.IO.File]::ReadAllText((Join-Path $root 'src' 'LayerPeek.App' 'Licenses' 'ThirdPartyLicenses.cs'))
        $new = Update-LicensesText -Text $text -Source (Get-FFmpegSource '9.0.3')
        $new | Should -Match 'MacOSBuildUri \{ get; \} = new\("https://ffmpeg\.org/releases/ffmpeg-9\.0\.3\.tar\.xz"\);'
        $new | Should -Match 'WindowsBuildUri \{ get; \} = new\("https://example\.invalid/win\.zip"\);'
    }

    It 'matches the real build/FFmpeg.targets and ThirdPartyLicenses.cs (without writing them)' {
        $repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..' '..'))
        $targets = [System.IO.File]::ReadAllText((Join-Path $repo 'build' 'FFmpeg.targets'))
        $licenses = [System.IO.File]::ReadAllText((Join-Path $repo 'src' 'LayerPeek.App' 'Licenses' 'ThirdPartyLicenses.cs'))
        $pinned = [regex]::Match($targets, '<LayerPeekFFmpegMacSourceUrl>https://ffmpeg\.org/releases/ffmpeg-([0-9.]+)\.tar\.xz<').Groups[1].Value
        $pinned | Should -Not -BeNullOrEmpty
        $sha = [regex]::Match($targets, '<LayerPeekFFmpegMacSourceSha256>([0-9a-f]{64})<').Groups[1].Value
        # Re-pinning the same values gives the files back unchanged.
        Update-TargetsText -Text $targets -Source (Get-FFmpegSource $pinned) -Sha256 $sha | Should -BeExactly $targets
        Update-LicensesText -Text $licenses -Source (Get-FFmpegSource $pinned) | Should -BeExactly $licenses
    }
}

Describe 'Whole run' {
    It 'passes when the download matches Homebrew, skips gpg without it and changes nothing without -Update' {
        $root = New-Repo
        $before = [System.IO.File]::ReadAllText((Join-Path $root 'build' 'FFmpeg.targets'))
        $download = New-Download
        $formula = New-Formula 'https://ffmpeg.org/releases/ffmpeg-9.0.3.tar.xz' $script:ContentSha256
        $getJson = { param($Url) $formula }.GetNewClosure()
        $r = Invoke-Quiet { Invoke-Main -Version '9.0.3' -RepoRoot $root -Download $download.Block -GetJson $getJson -Gpg $null -TempRoot $script:TempRoot }
        $r.Code | Should -Be 0
        $r.Text | Should -Match 'Homebrew cross-check:\s+OK'
        $r.Text | Should -Match 'GPG signature:\s+SKIPPED'
        $r.Text | Should -Match "<LayerPeekFFmpegMacSourceSha256>$($script:ContentSha256)</LayerPeekFFmpegMacSourceSha256>"
        @($download.Log) | Should -BeExactly @('https://ffmpeg.org/releases/ffmpeg-9.0.3.tar.xz')
        [System.IO.File]::ReadAllText((Join-Path $root 'build' 'FFmpeg.targets')) | Should -BeExactly $before
        @(Get-ChildItem -LiteralPath $script:TempRoot -Filter 'layerpeek-ffmpeg-*') | Should -BeNullOrEmpty
    }

    It 'fails on a SHA256 mismatch with Homebrew and writes nothing even with -Update' {
        $root = New-Repo
        $before = [System.IO.File]::ReadAllText((Join-Path $root 'build' 'FFmpeg.targets'))
        $download = New-Download
        $formula = New-Formula 'https://ffmpeg.org/releases/ffmpeg-9.0.3.tar.xz' ('c' * 64)
        $getJson = { param($Url) $formula }.GetNewClosure()
        $r = Invoke-Quiet { Invoke-Main -Version '9.0.3' -Update -RepoRoot $root -Download $download.Block -GetJson $getJson -Gpg $null -TempRoot $script:TempRoot }
        $r.Code | Should -Be 1
        $r.Text | Should -Match 'SHA256 mismatch'
        [System.IO.File]::ReadAllText((Join-Path $root 'build' 'FFmpeg.targets')) | Should -BeExactly $before
        @(Get-ChildItem -LiteralPath $script:TempRoot -Filter 'layerpeek-ffmpeg-*') | Should -BeNullOrEmpty
    }

    It 'fails when Homebrew is on another version' {
        $root = New-Repo
        $download = New-Download
        $formula = New-Formula 'https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz' $script:ContentSha256 '9.0.2'
        $getJson = { param($Url) $formula }.GetNewClosure()
        $r = Invoke-Quiet { Invoke-Main -Version '9.0.3' -RepoRoot $root -Download $download.Block -GetJson $getJson -Gpg $null -TempRoot $script:TempRoot }
        $r.Code | Should -Be 1
        $r.Text | Should -Match 'cannot be cross-checked'
    }

    It 'verifies the signature with gpg when available and writes the values with -Update' {
        $root = New-Repo
        $download = New-Download
        $gpg = New-Gpg (Get-GoodLines)
        $formula = New-Formula 'https://ffmpeg.org/releases/ffmpeg-9.0.3.tar.xz' $script:ContentSha256
        $getJson = { param($Url) $formula }.GetNewClosure()
        $r = Invoke-Quiet { Invoke-Main -Version '9.0.3' -Update -RepoRoot $root -Download $download.Block -GetJson $getJson -Gpg '/stand-in/gpg' -InvokeGpg $gpg.Block -TempRoot $script:TempRoot }
        $r.Code | Should -Be 0
        $r.Text | Should -Match 'GPG signature:\s+OK'
        @($download.Log) | Should -BeExactly @(
            'https://ffmpeg.org/releases/ffmpeg-9.0.3.tar.xz',
            'https://ffmpeg.org/releases/ffmpeg-9.0.3.tar.xz.asc',
            'https://ffmpeg.org/ffmpeg-devel.asc')
        $gpg.Calls.Count | Should -Be 2
        $targets = [System.IO.File]::ReadAllText((Join-Path $root 'build' 'FFmpeg.targets'))
        $targets | Should -Match "<LayerPeekFFmpegMacSourceSha256>$($script:ContentSha256)</LayerPeekFFmpegMacSourceSha256>"
        $targets | Should -Match '<LayerPeekFFmpegMacSourceFolder>ffmpeg-9\.0\.3</LayerPeekFFmpegMacSourceFolder>'
        [System.IO.File]::ReadAllText((Join-Path $root 'src' 'LayerPeek.App' 'Licenses' 'ThirdPartyLicenses.cs')) | Should -Match 'ffmpeg-9\.0\.3\.tar\.xz'
        @(Get-ChildItem -LiteralPath $script:TempRoot -Filter 'layerpeek-ffmpeg-*') | Should -BeNullOrEmpty
    }

    It 'fails on a bad signature and writes nothing' {
        $root = New-Repo
        $before = [System.IO.File]::ReadAllText((Join-Path $root 'build' 'FFmpeg.targets'))
        $download = New-Download
        $gpg = New-Gpg @('[GNUPG:] BADSIG B4322F04D67658D8 FFmpeg') 1
        $formula = New-Formula 'https://ffmpeg.org/releases/ffmpeg-9.0.3.tar.xz' $script:ContentSha256
        $getJson = { param($Url) $formula }.GetNewClosure()
        $r = Invoke-Quiet { Invoke-Main -Version '9.0.3' -Update -RepoRoot $root -Download $download.Block -GetJson $getJson -Gpg '/stand-in/gpg' -InvokeGpg $gpg.Block -TempRoot $script:TempRoot }
        $r.Code | Should -Be 1
        $r.Text | Should -Match 'does not verify'
        [System.IO.File]::ReadAllText((Join-Path $root 'build' 'FFmpeg.targets')) | Should -BeExactly $before
    }

    It 'fails on an invalid version before downloading anything' {
        $download = New-Download
        $r = Invoke-Quiet { Invoke-Main -Version 'latest' -RepoRoot (New-Repo) -Download $download.Block -GetJson { throw 'no' } -Gpg $null -TempRoot $script:TempRoot }
        $r.Code | Should -Be 1
        $download.Log.Count | Should -Be 0
    }
}
