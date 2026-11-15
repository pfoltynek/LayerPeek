<#
.SYNOPSIS
    Pester 5+ tests of the maintainer tool for a new FFmpeg version (build/update-ffmpeg.ps1: the official source tarball
    both platforms build, T-026 and T-202).

.DESCRIPTION
    The script is dot-sourced (its main part does not run). Downloads, the Homebrew formula and gpg are stand-ins: no
    network, no gpg, nothing of the real repository is changed (-Update writes into copies in a temporary folder).

    Temporary files live in one folder under $env:LAYERPEEK_PESTER_TESTS_TEMP (set by the xUnit wrapper
    tests/LayerPeek.Scripts.Tests, which also deletes it after a crash) or under the system temp folder.
    AfterAll deletes the folder.

.EXAMPLE
    Invoke-Pester build/update-ffmpeg.Tests.ps1 -Output Detailed
#>
#Requires -Version 7.2

BeforeAll {
    . (Join-Path $PSScriptRoot 'update-ffmpeg.ps1')

    $base = if ($env:LAYERPEEK_PESTER_TESTS_TEMP) { $env:LAYERPEEK_PESTER_TESTS_TEMP } else { [System.IO.Path]::GetTempPath() }
    $script:TempRoot = Join-Path $base "layerpeek-ps-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    $null = New-Item -ItemType Directory -Path $script:TempRoot

    $script:Content = 'stand-in tarball'
    $script:ContentSha256 = (Get-FileHash -InputStream ([System.IO.MemoryStream]::new([System.Text.Encoding]::UTF8.GetBytes($script:Content))) -Algorithm SHA256).Hash.ToLowerInvariant()
    $script:Fpr = 'FCF986EA15E6E293A5644F10B4322F04D67658D8'
    $script:Dlls = @('avformat-63.dll', 'avcodec-63.dll', 'avutil-61.dll', 'swscale-10.dll')
    $script:Dylibs = @('libavformat.63.dylib', 'libavcodec.63.dylib', 'libavutil.61.dylib', 'libswscale.10.dylib')

    function New-Formula([string] $Url, [string] $Checksum, [string] $Version = '9.0.3') {
        [pscustomobject]@{ versions = [pscustomobject]@{ stable = $Version }; urls = [pscustomobject]@{ stable = [pscustomobject]@{ url = $Url; checksum = $Checksum } } }
    }

    # Download stand-in: every URL gets the stand-in tarball text. Records the URLs.
    function New-Download {
        $log = [System.Collections.Generic.List[string]]::new()
        $content = $script:Content
        $block = {
            param($Url, $Path)
            $log.Add($Url)
            [System.IO.File]::WriteAllText($Path, $content)
        }.GetNewClosure()
        [pscustomobject]@{ Block = $block; Log = $log }
    }

    function New-Repo([string] $Sha256 = '8c3850283eb25fa026482078a04051e0be17347b09ef81a0849bec15a96e002e', [string] $Version = '9.0.2') {
        $root = Join-Path $script:TempRoot ([guid]::NewGuid().ToString('N').Substring(0, 8))
        $null = New-Item -ItemType Directory -Force -Path (Join-Path $root 'build'), (Join-Path $root 'src' 'LayerPeek.App' 'Licenses')
        $targets = @"
<Project>
  <PropertyGroup>
    <LayerPeekFFmpegSourceUrl>https://ffmpeg.org/releases/ffmpeg-$Version.tar.xz</LayerPeekFFmpegSourceUrl>
    <LayerPeekFFmpegSourceSha256>$Sha256</LayerPeekFFmpegSourceSha256>
    <LayerPeekFFmpegSourceFolder>ffmpeg-$Version</LayerPeekFFmpegSourceFolder>
  </PropertyGroup>
  <ItemGroup Condition="'`$(_LayerPeekFFmpegPlatform)' == 'windows'">
    <_LayerPeekFFmpegLibrary Include="avformat-63.dll;avcodec-63.dll;avutil-61.dll;swscale-10.dll" />
  </ItemGroup>
  <ItemGroup Condition="'`$(_LayerPeekFFmpegPlatform)' == 'macos'">
    <_LayerPeekFFmpegLibrary Include="libavformat.63.dylib;libavcodec.63.dylib;libavutil.61.dylib;libswscale.10.dylib" />
  </ItemGroup>
</Project>
"@
        $licenses = @"
        public static Uri SourceUri { get; } = new("https://ffmpeg.org/download.html");
        public static Uri SourceTarballUri { get; } = new("https://ffmpeg.org/releases/ffmpeg-$Version.tar.xz");
"@
        [System.IO.File]::WriteAllText((Join-Path $root 'build' 'FFmpeg.targets'), $targets)
        [System.IO.File]::WriteAllText((Join-Path $root 'src' 'LayerPeek.App' 'Licenses' 'ThirdPartyLicenses.cs'), $licenses)
        $root
    }

    function Get-RepoFiles([string] $Root) {
        [pscustomobject]@{
            Targets  = [System.IO.File]::ReadAllText((Join-Path $Root 'build' 'FFmpeg.targets'))
            Licenses = [System.IO.File]::ReadAllText((Join-Path $Root 'src' 'LayerPeek.App' 'Licenses' 'ThirdPartyLicenses.cs'))
        }
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

    function Get-Formula {
        $formula = New-Formula 'https://ffmpeg.org/releases/ffmpeg-9.0.3.tar.xz' $script:ContentSha256
        { param($Url) $formula }.GetNewClosure()
    }
}

AfterAll {
    if ($script:TempRoot -and (Test-Path -LiteralPath $script:TempRoot)) { Remove-Item -LiteralPath $script:TempRoot -Recurse -Force }
}

Describe 'Source tarball' {
    It 'derives the official URL, folder and release branch from the version' {
        $source = Get-FFmpegSource -Version '9.0.3'
        $source.Url | Should -BeExactly 'https://ffmpeg.org/releases/ffmpeg-9.0.3.tar.xz'
        $source.FileName | Should -BeExactly 'ffmpeg-9.0.3.tar.xz'
        $source.Folder | Should -BeExactly 'ffmpeg-9.0.3'
        $source.Branch | Should -BeExactly '9.0'
    }

    It 'rejects something that is not a release version' -TestCases @(
        @{ V = '9.0.3/../x' }, @{ V = 'n9.0.3' }, @{ V = '9' }, @{ V = '' }
    ) {
        { Get-FFmpegSource -Version $V } | Should -Throw
    }
}

Describe 'Libraries in build/FFmpeg.targets' {
    It 'reads the library names of both platforms' {
        $libraries = Get-FFmpegLibraries -TargetsText (Get-RepoFiles (New-Repo)).Targets
        $libraries.Windows | Should -BeExactly $script:Dlls
        $libraries.MacOS | Should -BeExactly $script:Dylibs
    }

    It 'fails when the platforms use different library major versions' {
        $text = (Get-RepoFiles (New-Repo)).Targets.Replace('libavcodec.63.dylib', 'libavcodec.64.dylib')
        { Get-FFmpegLibraries -TargetsText $text } | Should -Throw -ExpectedMessage '*libavcodec.64.dylib for macOS but not avcodec-64.dll*'
    }

    It 'fails when Windows lists a library macOS does not build (T-202: the same set on both)' {
        $text = (Get-RepoFiles (New-Repo)).Targets.Replace('swscale-10.dll"', 'swscale-10.dll;swresample-7.dll"')
        { Get-FFmpegLibraries -TargetsText $text } | Should -Throw -ExpectedMessage '*swresample-7.dll for Windows but not for macOS*'
    }

    It 'fails without exactly one library list per platform' {
        { Get-FFmpegLibraries -TargetsText '<Project />' } | Should -Throw -ExpectedMessage '*found 0 and 0*'
    }

    It 'accepts the real build/FFmpeg.targets' {
        $targets = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'FFmpeg.targets'))
        (Get-FFmpegLibraries -TargetsText $targets).Windows | Should -BeExactly $script:Dlls
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
    It 'replaces the given properties and nothing else' {
        $text = (Get-RepoFiles (New-Repo)).Targets
        $new = Update-TargetsText -Text $text -Values ([ordered]@{ LayerPeekFFmpegSourceSha256 = ('b' * 64) })
        $new | Should -Match "<LayerPeekFFmpegSourceSha256>$('b' * 64)</LayerPeekFFmpegSourceSha256>"
        $new | Should -Match '<LayerPeekFFmpegSourceFolder>ffmpeg-9\.0\.2</LayerPeekFFmpegSourceFolder>'
    }

    It 'writes a value with regex substitution characters literally' {
        Update-TargetsText -Text '<A>x</A>' -Values @{ A = 'p$1q$$' } | Should -BeExactly '<A>p$1q$$</A>'
    }

    It 'fails when a property is missing or duplicated' {
        { Update-TargetsText -Text '<Project />' -Values @{ LayerPeekFFmpegSourceUrl = 'x' } } | Should -Throw -ExpectedMessage '*exactly once (found 0)*'
        $twice = '<LayerPeekFFmpegSourceUrl>a</LayerPeekFFmpegSourceUrl><LayerPeekFFmpegSourceUrl>b</LayerPeekFFmpegSourceUrl>'
        { Update-TargetsText -Text $twice -Values @{ LayerPeekFFmpegSourceUrl = 'x' } } | Should -Throw -ExpectedMessage '*exactly once (found 2)*'
    }

    It 'replaces SourceTarballUri only' {
        $text = (Get-RepoFiles (New-Repo)).Licenses
        $new = Update-LicensesText -Text $text -Uris ([ordered]@{ SourceTarballUri = 'https://ffmpeg.org/releases/ffmpeg-9.0.3.tar.xz' })
        $new | Should -Match 'SourceTarballUri \{ get; \} = new\("https://ffmpeg\.org/releases/ffmpeg-9\.0\.3\.tar\.xz"\);'
        $new | Should -Match 'SourceUri \{ get; \} = new\("https://ffmpeg\.org/download\.html"\);'
    }

    It 'fails when a Uri property is missing' {
        { Update-LicensesText -Text 'class X {}' -Uris @{ SourceTarballUri = 'x' } } | Should -Throw -ExpectedMessage '*SourceTarballUri exactly once (found 0)*'
    }

    It 'matches the real build/FFmpeg.targets and ThirdPartyLicenses.cs (without writing them)' {
        $repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
        $targets = [System.IO.File]::ReadAllText((Join-Path $repo 'build' 'FFmpeg.targets'))
        $licenses = [System.IO.File]::ReadAllText((Join-Path $repo 'src' 'LayerPeek.App' 'Licenses' 'ThirdPartyLicenses.cs'))
        $version = [regex]::Match($targets, '<LayerPeekFFmpegSourceUrl>https://ffmpeg\.org/releases/ffmpeg-([0-9.]+)\.tar\.xz<').Groups[1].Value
        $sha = [regex]::Match($targets, '<LayerPeekFFmpegSourceSha256>([0-9a-f]{64})<').Groups[1].Value
        $version | Should -Not -BeNullOrEmpty
        $source = Get-FFmpegSource $version
        # No BtbN pins any more (T-202).
        $targets | Should -Not -Match '<LayerPeekFFmpeg(Url|Sha256|ArchiveFolder|WindowsSource|WindowsScripts|MacSource)|BtbN'
        # Re-pinning the same values gives the files back unchanged.
        $values = [ordered]@{ LayerPeekFFmpegSourceUrl = $source.Url; LayerPeekFFmpegSourceSha256 = $sha; LayerPeekFFmpegSourceFolder = $source.Folder }
        Update-TargetsText -Text $targets -Values $values | Should -BeExactly $targets
        Update-LicensesText -Text $licenses -Uris ([ordered]@{ SourceTarballUri = $source.Url }) | Should -BeExactly $licenses
    }
}

Describe 'Whole run' {
    It 'checks the tarball, skips gpg without it and changes nothing without -Update' {
        $root = New-Repo
        $before = Get-RepoFiles $root
        $download = New-Download
        $r = Invoke-Quiet { Invoke-Main -Version '9.0.3' -RepoRoot $root -Download $download.Block -GetJson (Get-Formula) -Gpg $null -TempRoot $script:TempRoot }
        $r.Code | Should -Be 0
        $r.Text | Should -Match 'Homebrew cross-check:\s+OK'
        $r.Text | Should -Match 'GPG signature:\s+SKIPPED'
        $r.Text | Should -Match "<LayerPeekFFmpegSourceSha256>$($script:ContentSha256)</LayerPeekFFmpegSourceSha256>"
        $r.Text | Should -Match 'SourceTarballUri = https://ffmpeg\.org/releases/ffmpeg-9\.0\.3\.tar\.xz'
        @($download.Log) | Should -BeExactly @('https://ffmpeg.org/releases/ffmpeg-9.0.3.tar.xz')
        $after = Get-RepoFiles $root
        $after.Targets | Should -BeExactly $before.Targets
        $after.Licenses | Should -BeExactly $before.Licenses
        @(Get-ChildItem -LiteralPath $script:TempRoot -Filter 'layerpeek-ffmpeg-*') | Should -BeNullOrEmpty
    }

    It 'writes the values with -Update' {
        $root = New-Repo
        $r = Invoke-Quiet { Invoke-Main -Version '9.0.3' -Update -RepoRoot $root -Download (New-Download).Block -GetJson (Get-Formula) -Gpg $null -TempRoot $script:TempRoot }
        $r.Code | Should -Be 0
        $after = Get-RepoFiles $root
        $after.Targets | Should -Match '<LayerPeekFFmpegSourceUrl>https://ffmpeg\.org/releases/ffmpeg-9\.0\.3\.tar\.xz</LayerPeekFFmpegSourceUrl>'
        $after.Targets | Should -Match "<LayerPeekFFmpegSourceSha256>$($script:ContentSha256)</LayerPeekFFmpegSourceSha256>"
        $after.Targets | Should -Match '<LayerPeekFFmpegSourceFolder>ffmpeg-9\.0\.3</LayerPeekFFmpegSourceFolder>'
        $after.Licenses | Should -Match 'SourceTarballUri \{ get; \} = new\("https://ffmpeg\.org/releases/ffmpeg-9\.0\.3\.tar\.xz"\);'
    }

    It 'says so when the values are the ones already pinned (and not otherwise)' {
        $root = New-Repo -Sha256 $script:ContentSha256 -Version '9.0.3'
        $r = Invoke-Quiet { Invoke-Main -Version '9.0.3' -RepoRoot $root -Download (New-Download).Block -GetJson (Get-Formula) -Gpg $null -TempRoot $script:TempRoot }
        $r.Code | Should -Be 0
        $r.Text | Should -Match 'already pinned'

        $r = Invoke-Quiet { Invoke-Main -Version '9.0.3' -RepoRoot (New-Repo) -Download (New-Download).Block -GetJson (Get-Formula) -Gpg $null -TempRoot $script:TempRoot }
        $r.Code | Should -Be 0
        $r.Text | Should -Not -Match 'already pinned'
    }

    It 'fails before any download when the version is missing or invalid' -TestCases @(
        @{ V = $null; M = 'Needs -Version' },
        @{ V = 'latest'; M = 'not an FFmpeg release version' }
    ) {
        $download = New-Download
        $r = Invoke-Quiet { Invoke-Main -Version $V -RepoRoot (New-Repo) -Download $download.Block -GetJson { throw 'no' } -Gpg $null -TempRoot $script:TempRoot }
        $r.Code | Should -Be 1
        $r.Text | Should -Match $M
        $download.Log.Count | Should -Be 0
    }

    It 'fails before any download when the libraries of the platforms differ' {
        $root = New-Repo
        $targetsPath = Join-Path $root 'build' 'FFmpeg.targets'
        [System.IO.File]::WriteAllText($targetsPath, [System.IO.File]::ReadAllText($targetsPath).Replace('swscale-10.dll"', 'swscale-10.dll;swresample-7.dll"'))
        $download = New-Download
        $r = Invoke-Quiet { Invoke-Main -Version '9.0.3' -RepoRoot $root -Download $download.Block -GetJson (Get-Formula) -Gpg $null -TempRoot $script:TempRoot }
        $r.Code | Should -Be 1
        $r.Text | Should -Match 'swresample-7\.dll for Windows but not for macOS'
        $download.Log.Count | Should -Be 0
    }

    It 'fails on a SHA256 mismatch with Homebrew and writes nothing even with -Update' {
        $root = New-Repo
        $before = Get-RepoFiles $root
        $formula = New-Formula 'https://ffmpeg.org/releases/ffmpeg-9.0.3.tar.xz' ('c' * 64)
        $getJson = { param($Url) $formula }.GetNewClosure()
        $r = Invoke-Quiet { Invoke-Main -Version '9.0.3' -Update -RepoRoot $root -Download (New-Download).Block -GetJson $getJson -Gpg $null -TempRoot $script:TempRoot }
        $r.Code | Should -Be 1
        $r.Text | Should -Match 'SHA256 mismatch'
        (Get-RepoFiles $root).Targets | Should -BeExactly $before.Targets
        (Get-RepoFiles $root).Licenses | Should -BeExactly $before.Licenses
        @(Get-ChildItem -LiteralPath $script:TempRoot -Filter 'layerpeek-ffmpeg-*') | Should -BeNullOrEmpty
    }

    It 'fails when Homebrew is on another version' {
        $formula = New-Formula 'https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz' $script:ContentSha256 '9.0.2'
        $getJson = { param($Url) $formula }.GetNewClosure()
        $r = Invoke-Quiet { Invoke-Main -Version '9.0.3' -RepoRoot (New-Repo) -Download (New-Download).Block -GetJson $getJson -Gpg $null -TempRoot $script:TempRoot }
        $r.Code | Should -Be 1
        $r.Text | Should -Match 'cannot be cross-checked'
    }

    It 'verifies the signature with gpg when available and writes the values with -Update' {
        $root = New-Repo
        $download = New-Download
        $gpg = New-Gpg (Get-GoodLines)
        $r = Invoke-Quiet { Invoke-Main -Version '9.0.3' -Update -RepoRoot $root -Download $download.Block -GetJson (Get-Formula) -Gpg '/stand-in/gpg' -InvokeGpg $gpg.Block -TempRoot $script:TempRoot }
        $r.Code | Should -Be 0
        $r.Text | Should -Match 'GPG signature:\s+OK'
        @($download.Log) | Should -BeExactly @(
            'https://ffmpeg.org/releases/ffmpeg-9.0.3.tar.xz',
            'https://ffmpeg.org/releases/ffmpeg-9.0.3.tar.xz.asc',
            'https://ffmpeg.org/ffmpeg-devel.asc')
        $gpg.Calls.Count | Should -Be 2
        $after = Get-RepoFiles $root
        $after.Targets | Should -Match "<LayerPeekFFmpegSourceSha256>$($script:ContentSha256)</LayerPeekFFmpegSourceSha256>"
        $after.Licenses | Should -Match 'ffmpeg-9\.0\.3\.tar\.xz'
        @(Get-ChildItem -LiteralPath $script:TempRoot -Filter 'layerpeek-ffmpeg-*') | Should -BeNullOrEmpty
    }

    It 'fails on a bad signature and writes nothing' {
        $root = New-Repo
        $before = Get-RepoFiles $root
        $gpg = New-Gpg @('[GNUPG:] BADSIG B4322F04D67658D8 FFmpeg') 1
        $r = Invoke-Quiet { Invoke-Main -Version '9.0.3' -Update -RepoRoot $root -Download (New-Download).Block -GetJson (Get-Formula) -Gpg '/stand-in/gpg' -InvokeGpg $gpg.Block -TempRoot $script:TempRoot }
        $r.Code | Should -Be 1
        $r.Text | Should -Match 'does not verify'
        (Get-RepoFiles $root).Targets | Should -BeExactly $before.Targets
    }
}
