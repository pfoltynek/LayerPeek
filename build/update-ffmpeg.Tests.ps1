<#
.SYNOPSIS
    Pester 5+ tests of the maintainer tool for a new FFmpeg version (build/update-ffmpeg.ps1, macOS T-026 and Windows).

.DESCRIPTION
    The script is dot-sourced (its main part does not run). Downloads, the Homebrew formula, the BtbN checksum file and
    zip, and gpg are stand-ins: no network, no gpg, nothing of the real repository is changed (-Update writes into
    copies in a temporary folder).

    Temporary files live in one folder under $env:LAYERPEEK_PESTER_TESTS_TEMP (set by the xUnit wrapper
    tests/LayerPeek.Scripts.Tests, which also deletes it after a crash) or under the system temp folder.
    AfterAll deletes the folder.

.EXAMPLE
    Invoke-Pester build/update-ffmpeg.Tests.ps1 -Output Detailed
#>
#Requires -Version 7.2

BeforeAll {
    . (Join-Path $PSScriptRoot 'update-ffmpeg.ps1')
    Add-Type -AssemblyName System.IO.Compression

    $base = if ($env:LAYERPEEK_PESTER_TESTS_TEMP) { $env:LAYERPEEK_PESTER_TESTS_TEMP } else { [System.IO.Path]::GetTempPath() }
    $script:TempRoot = Join-Path $base "layerpeek-ps-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    $null = New-Item -ItemType Directory -Path $script:TempRoot

    $script:Content = 'stand-in tarball'
    $script:ContentSha256 = (Get-FileHash -InputStream ([System.IO.MemoryStream]::new([System.Text.Encoding]::UTF8.GetBytes($script:Content))) -Algorithm SHA256).Hash.ToLowerInvariant()
    $script:Fpr = 'FCF986EA15E6E293A5644F10B4322F04D67658D8'
    $script:Tag = 'autobuild-2026-11-20-12-00'
    $script:Asset = 'ffmpeg-n9.0.3-4-gabcdef0123-win64-lgpl-shared-9.0.zip'
    $script:Folder = 'ffmpeg-n9.0.3-4-gabcdef0123-win64-lgpl-shared-9.0'
    $script:ZipUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/$($script:Tag)/$($script:Asset)"
    $script:ChecksumsUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/$($script:Tag)/checksums.sha256"
    $script:Dlls = @('avformat-63.dll', 'avcodec-63.dll', 'avutil-61.dll', 'swscale-10.dll', 'swresample-7.dll')
    $script:Lgpl = "                   GNU LESSER GENERAL PUBLIC LICENSE`n                       Version 3, 29 June 2007`n"

    # A small stand-in of the BtbN zip: entries as given (name -> text).
    function New-Zip([System.Collections.IDictionary] $Entries) {
        $path = Join-Path $script:TempRoot "zip-$([guid]::NewGuid().ToString('N').Substring(0, 8)).zip"
        $zip = [System.IO.Compression.ZipFile]::Open($path, [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($name in $Entries.Keys) {
                $writer = [System.IO.StreamWriter]::new($zip.CreateEntry($name).Open())
                try { $writer.Write([string] $Entries[$name]) } finally { $writer.Dispose() }
            }
        }
        finally { $zip.Dispose() }
        $path
    }

    function Get-GoodEntries([string] $Folder = $script:Folder, [string[]] $Dlls = $script:Dlls, [string] $License = $script:Lgpl) {
        $entries = [ordered]@{ "$Folder/LICENSE.txt" = $License; "$Folder/bin/ffmpeg.exe" = 'exe'; "$Folder/include/libavutil/avutil.h" = 'h' }
        foreach ($dll in $Dlls) { $entries["$Folder/bin/$dll"] = 'dll' }
        $entries
    }

    function New-Formula([string] $Url, [string] $Checksum, [string] $Version = '9.0.3') {
        [pscustomobject]@{ versions = [pscustomobject]@{ stable = $Version }; urls = [pscustomobject]@{ stable = [pscustomobject]@{ url = $Url; checksum = $Checksum } } }
    }

    <#
      Download stand-in: the BtbN zip URL gets a copy of $Zip, the checksum URL $Checksums (or an HTTP 404 when $null),
      everything else the stand-in tarball text. Records the URLs.
    #>
    function New-Download([string] $Zip, $Checksums) {
        $log = [System.Collections.Generic.List[string]]::new()
        $content = $script:Content
        $zipUrl = $script:ZipUrl
        $checksumsUrl = $script:ChecksumsUrl
        $block = {
            param($Url, $Path)
            $log.Add($Url)
            if ($Url -eq $zipUrl) { Copy-Item -LiteralPath $Zip -Destination $Path; return }
            if ($Url -eq $checksumsUrl) {
                if ($null -eq $Checksums) { throw 'Response status code does not indicate success: 404 (Not Found).' }
                [System.IO.File]::WriteAllText($Path, $Checksums); return
            }
            [System.IO.File]::WriteAllText($Path, $content)
        }.GetNewClosure()
        [pscustomobject]@{ Block = $block; Log = $log }
    }

    function Get-ChecksumsText([string] $Sha256, [string] $Asset = $script:Asset) {
        "$('1' * 64)  ffmpeg-n9.0.3-4-gabcdef0123-win64-gpl-shared-9.0.zip`n$Sha256  $Asset`n$('2' * 64)  ffmpeg-N-127617-g94cba3150c-win64-lgpl-shared.zip`n"
    }

    function New-Repo {
        $root = Join-Path $script:TempRoot ([guid]::NewGuid().ToString('N').Substring(0, 8))
        $null = New-Item -ItemType Directory -Force -Path (Join-Path $root 'build'), (Join-Path $root 'src' 'LayerPeek.App' 'Licenses')
        $targets = @'
<Project>
  <PropertyGroup>
    <LayerPeekFFmpegUrl>https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-10-17-20-51/ffmpeg-n9.0.2-28-g669f5ab083-win64-lgpl-shared-9.0.zip</LayerPeekFFmpegUrl>
    <LayerPeekFFmpegSha256>00ab3bc8fe02554bd0fffac9968bba498d1ab7e899a4871b85fbba828a50bfcc</LayerPeekFFmpegSha256>
    <LayerPeekFFmpegArchiveFolder>ffmpeg-n9.0.2-28-g669f5ab083-win64-lgpl-shared-9.0</LayerPeekFFmpegArchiveFolder>
    <LayerPeekFFmpegMacSourceUrl>https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz</LayerPeekFFmpegMacSourceUrl>
    <LayerPeekFFmpegMacSourceSha256>8c3850283eb25fa026482078a04051e0be17347b09ef81a0849bec15a96e002e</LayerPeekFFmpegMacSourceSha256>
    <LayerPeekFFmpegMacSourceFolder>ffmpeg-9.0.2</LayerPeekFFmpegMacSourceFolder>
  </PropertyGroup>
  <ItemGroup Condition="'$(_LayerPeekFFmpegPlatform)' == 'windows'">
    <_LayerPeekFFmpegLibrary Include="avformat-63.dll;avcodec-63.dll;avutil-61.dll;swscale-10.dll;swresample-7.dll" />
  </ItemGroup>
  <ItemGroup Condition="'$(_LayerPeekFFmpegPlatform)' == 'macos'">
    <_LayerPeekFFmpegLibrary Include="libavformat.63.dylib;libavcodec.63.dylib;libavutil.61.dylib;libswscale.10.dylib" />
  </ItemGroup>
</Project>
'@
        $licenses = @'
        public static Uri WindowsBuildUri { get; } =
            new("https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-10-17-20-51/ffmpeg-n9.0.2-28-g669f5ab083-win64-lgpl-shared-9.0.zip");
        public static Uri WindowsBuildScriptsUri { get; } = new("https://github.com/BtbN/FFmpeg-Builds");
        public static Uri MacOSBuildUri { get; } = new("https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz");
'@
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

    function Get-MacFormula {
        $formula = New-Formula 'https://ffmpeg.org/releases/ffmpeg-9.0.3.tar.xz' $script:ContentSha256
        { param($Url) $formula }.GetNewClosure()
    }
}

AfterAll {
    if ($script:TempRoot -and (Test-Path -LiteralPath $script:TempRoot)) { Remove-Item -LiteralPath $script:TempRoot -Recurse -Force }
}

Describe 'Source tarball (macOS)' {
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

Describe 'BtbN build (Windows)' {
    It 'derives the URLs, archive folder and release branch from the tag and asset' {
        $build = Get-BtbNBuild -Tag 'autobuild-2026-10-17-20-51' -Asset 'ffmpeg-n9.0.2-28-g669f5ab083-win64-lgpl-shared-9.0.zip'
        $build.Url | Should -BeExactly 'https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-10-17-20-51/ffmpeg-n9.0.2-28-g669f5ab083-win64-lgpl-shared-9.0.zip'
        $build.ChecksumsUrl | Should -BeExactly 'https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-10-17-20-51/checksums.sha256'
        $build.Folder | Should -BeExactly 'ffmpeg-n9.0.2-28-g669f5ab083-win64-lgpl-shared-9.0'
        $build.Branch | Should -BeExactly '9.0'
    }

    It 'accepts a release build without commits on top' {
        (Get-BtbNBuild -Tag 'autobuild-2026-10-17-20-51' -Asset 'ffmpeg-n9.1-win64-lgpl-shared-9.1.zip').Branch | Should -BeExactly '9.1'
    }

    It 'rejects a malformed tag' -TestCases @(@{ T = 'latest' }, @{ T = 'autobuild-2026-10-17' }, @{ T = 'autobuild-2026-10-17-20-51/../x' }, @{ T = '' }) {
        { Get-BtbNBuild -Tag $T -Asset 'ffmpeg-n9.0.2-28-g669f5ab083-win64-lgpl-shared-9.0.zip' } | Should -Throw -ExpectedMessage '*not a BtbN release tag*'
    }

    It 'rejects anything but an LGPL shared win64 build of a release branch' -TestCases @(
        @{ A = 'ffmpeg-n9.0.2-28-g669f5ab083-win64-gpl-shared-9.0.zip' },
        @{ A = 'ffmpeg-n9.0.2-28-g669f5ab083-win64-lgpl-9.0.zip' },
        @{ A = 'ffmpeg-n9.0.2-28-g669f5ab083-winarm64-lgpl-shared-9.0.zip' },
        @{ A = 'ffmpeg-n9.0.2-28-g669f5ab083-linux64-lgpl-shared-9.0.tar.xz' },
        @{ A = 'ffmpeg-N-127617-g94cba3150c-win64-lgpl-shared.zip' },
        @{ A = '../ffmpeg-n9.0.2-win64-lgpl-shared-9.0.zip' },
        @{ A = '' }
    ) {
        { Get-BtbNBuild -Tag 'autobuild-2026-10-17-20-51' -Asset $A } | Should -Throw -ExpectedMessage '*not an LGPL shared win64 build*'
    }

    It 'rejects an asset whose version is not on its release branch' {
        { Get-BtbNBuild -Tag 'autobuild-2026-10-17-20-51' -Asset 'ffmpeg-n9.0.2-win64-lgpl-shared-8.1.zip' } | Should -Throw -ExpectedMessage '*release branch 8.1*'
    }
}

Describe 'Libraries and branches in build/FFmpeg.targets' {
    It 'reads the library names of both platforms' {
        $libraries = Get-FFmpegLibraries -TargetsText (Get-RepoFiles (New-Repo)).Targets
        $libraries.Windows | Should -BeExactly $script:Dlls
        $libraries.MacOS | Should -BeExactly @('libavformat.63.dylib', 'libavcodec.63.dylib', 'libavutil.61.dylib', 'libswscale.10.dylib')
    }

    It 'fails when the platforms use different library major versions' {
        $text = (Get-RepoFiles (New-Repo)).Targets.Replace('libavcodec.63.dylib', 'libavcodec.64.dylib')
        { Get-FFmpegLibraries -TargetsText $text } | Should -Throw -ExpectedMessage '*libavcodec.64.dylib for macOS but not avcodec-64.dll*'
    }

    It 'fails without exactly one library list per platform' {
        { Get-FFmpegLibraries -TargetsText '<Project />' } | Should -Throw -ExpectedMessage '*found 0 and 0*'
    }

    It 'reads the pinned release branches' {
        $branches = Get-PinnedBranches -TargetsText (Get-RepoFiles (New-Repo)).Targets
        $branches.MacOS | Should -BeExactly '9.0'
        $branches.Windows | Should -BeExactly '9.0'
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

Describe 'BtbN checksum file' {
    It 'returns the line of exactly this asset in lower case' {
        $text = Get-ChecksumsText ('AB' * 32)
        Get-BtbNChecksum -Text $text -Asset $script:Asset | Should -BeExactly ('ab' * 32)
    }

    It 'accepts CRLF line ends and the binary marker' {
        Get-BtbNChecksum -Text "$('c' * 64) *$($script:Asset)`r`n" -Asset $script:Asset | Should -BeExactly ('c' * 64)
    }

    It 'fails when the asset is not listed' {
        { Get-BtbNChecksum -Text (Get-ChecksumsText ('a' * 64) 'other.zip') -Asset $script:Asset } | Should -Throw -ExpectedMessage '*does not list*'
        { Get-BtbNChecksum -Text '' -Asset $script:Asset } | Should -Throw -ExpectedMessage '*does not list*'
    }

    It 'fails when the asset is listed twice' {
        $text = "$('a' * 64)  $($script:Asset)`n$('b' * 64)  $($script:Asset)`n"
        { Get-BtbNChecksum -Text $text -Asset $script:Asset } | Should -Throw -ExpectedMessage '*2 times*'
    }

    It 'fails on a malformed line' {
        { Get-BtbNChecksum -Text "abc  $($script:Asset)`n" -Asset $script:Asset } | Should -Throw -ExpectedMessage '*Malformed line*'
    }
}

Describe 'BtbN zip contents' {
    It 'accepts the folder, the LGPL 3 licence and all libraries' {
        Test-BtbNArchive -Path (New-Zip (Get-GoodEntries)) -Folder $script:Folder -Libraries $script:Dlls | Should -BeExactly 'Verified'
    }

    It 'fails on another top folder' {
        $zip = New-Zip (Get-GoodEntries -Folder 'ffmpeg-n9.0.3-4-gabcdef0123-win64-lgpl-shared-9.0-x')
        { Test-BtbNArchive -Path $zip -Folder $script:Folder -Libraries $script:Dlls } | Should -Throw -ExpectedMessage '*exactly one top folder*'
    }

    It 'fails on a second top folder' {
        $entries = Get-GoodEntries
        $entries['extra/readme.txt'] = 'x'
        { Test-BtbNArchive -Path (New-Zip $entries) -Folder $script:Folder -Libraries $script:Dlls } | Should -Throw -ExpectedMessage '*exactly one top folder*extra*'
    }

    It 'fails without LICENSE.txt' {
        $entries = Get-GoodEntries
        $entries.Remove("$($script:Folder)/LICENSE.txt")
        { Test-BtbNArchive -Path (New-Zip $entries) -Folder $script:Folder -Libraries $script:Dlls } | Should -Throw -ExpectedMessage '*no*LICENSE.txt*'
    }

    It 'fails when the licence is not LGPL 3' {
        $zip = New-Zip (Get-GoodEntries -License "                    GNU GENERAL PUBLIC LICENSE`n                       Version 3, 29 June 2007`n")
        { Test-BtbNArchive -Path $zip -Folder $script:Folder -Libraries $script:Dlls } | Should -Throw -ExpectedMessage '*not the LGPL 3 text*'
    }

    It 'fails on other library major versions and names what is there' {
        $zip = New-Zip (Get-GoodEntries -Dlls @('avformat-64.dll', 'avcodec-64.dll', 'avutil-61.dll', 'swscale-10.dll', 'swresample-7.dll'))
        { Test-BtbNArchive -Path $zip -Folder $script:Folder -Libraries $script:Dlls } |
            Should -Throw -ExpectedMessage '*no avformat-63.dll, avcodec-63.dll in*it has avcodec-64.dll, avformat-64.dll*'
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
        $new = Update-TargetsText -Text $text -Values ([ordered]@{ LayerPeekFFmpegMacSourceSha256 = ('b' * 64); LayerPeekFFmpegArchiveFolder = $script:Folder })
        $new | Should -Match "<LayerPeekFFmpegMacSourceSha256>$('b' * 64)</LayerPeekFFmpegMacSourceSha256>"
        $new | Should -Match "<LayerPeekFFmpegArchiveFolder>$([regex]::Escape($script:Folder))</LayerPeekFFmpegArchiveFolder>"
        $new | Should -Match '<LayerPeekFFmpegMacSourceFolder>ffmpeg-9\.0\.2</LayerPeekFFmpegMacSourceFolder>'
        $new | Should -Match '<LayerPeekFFmpegSha256>00ab3bc8'
    }

    It 'writes a value with regex substitution characters literally' {
        Update-TargetsText -Text '<A>x</A>' -Values @{ A = 'p$1q$$' } | Should -BeExactly '<A>p$1q$$</A>'
    }

    It 'fails when a property is missing or duplicated' {
        { Update-TargetsText -Text '<Project />' -Values @{ LayerPeekFFmpegUrl = 'x' } } | Should -Throw -ExpectedMessage '*exactly once (found 0)*'
        $twice = '<LayerPeekFFmpegUrl>a</LayerPeekFFmpegUrl><LayerPeekFFmpegUrl>b</LayerPeekFFmpegUrl>'
        { Update-TargetsText -Text $twice -Values @{ LayerPeekFFmpegUrl = 'x' } } | Should -Throw -ExpectedMessage '*exactly once (found 2)*'
    }

    It 'replaces MacOSBuildUri and the two-line WindowsBuildUri only' {
        $text = (Get-RepoFiles (New-Repo)).Licenses
        $new = Update-LicensesText -Text $text -Uris ([ordered]@{ MacOSBuildUri = 'https://ffmpeg.org/releases/ffmpeg-9.0.3.tar.xz'; WindowsBuildUri = $script:ZipUrl })
        $new | Should -Match 'MacOSBuildUri \{ get; \} = new\("https://ffmpeg\.org/releases/ffmpeg-9\.0\.3\.tar\.xz"\);'
        $new | Should -Match "WindowsBuildUri \{ get; \} =\s+new\(`"$([regex]::Escape($script:ZipUrl))`"\);"
        $new | Should -Match 'WindowsBuildScriptsUri \{ get; \} = new\("https://github\.com/BtbN/FFmpeg-Builds"\);'
    }

    It 'fails when a Uri property is missing' {
        { Update-LicensesText -Text 'class X {}' -Uris @{ WindowsBuildUri = 'x' } } | Should -Throw -ExpectedMessage '*WindowsBuildUri exactly once (found 0)*'
    }

    It 'matches the real build/FFmpeg.targets and ThirdPartyLicenses.cs (without writing them)' {
        $repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
        $targets = [System.IO.File]::ReadAllText((Join-Path $repo 'build' 'FFmpeg.targets'))
        $licenses = [System.IO.File]::ReadAllText((Join-Path $repo 'src' 'LayerPeek.App' 'Licenses' 'ThirdPartyLicenses.cs'))
        $macVersion = [regex]::Match($targets, '<LayerPeekFFmpegMacSourceUrl>https://ffmpeg\.org/releases/ffmpeg-([0-9.]+)\.tar\.xz<').Groups[1].Value
        $macSha = [regex]::Match($targets, '<LayerPeekFFmpegMacSourceSha256>([0-9a-f]{64})<').Groups[1].Value
        $windows = [regex]::Match($targets, '<LayerPeekFFmpegUrl>https://github\.com/BtbN/FFmpeg-Builds/releases/download/([^/<]+)/([^/<]+)</LayerPeekFFmpegUrl>')
        $windowsSha = [regex]::Match($targets, '<LayerPeekFFmpegSha256>([0-9a-f]{64})<').Groups[1].Value
        $macVersion | Should -Not -BeNullOrEmpty
        $windows.Success | Should -BeTrue
        $source = Get-FFmpegSource $macVersion
        $build = Get-BtbNBuild -Tag $windows.Groups[1].Value -Asset $windows.Groups[2].Value
        # The pinned builds pass the checks that need no download.
        $source.Branch | Should -BeExactly $build.Branch
        $null = Get-FFmpegLibraries -TargetsText $targets
        # Re-pinning the same values gives the files back unchanged.
        $values = [ordered]@{
            LayerPeekFFmpegMacSourceUrl = $source.Url; LayerPeekFFmpegMacSourceSha256 = $macSha; LayerPeekFFmpegMacSourceFolder = $source.Folder
            LayerPeekFFmpegUrl = $build.Url; LayerPeekFFmpegSha256 = $windowsSha; LayerPeekFFmpegArchiveFolder = $build.Folder
        }
        Update-TargetsText -Text $targets -Values $values | Should -BeExactly $targets
        Update-LicensesText -Text $licenses -Uris ([ordered]@{ MacOSBuildUri = $source.Url; WindowsBuildUri = $build.Url }) | Should -BeExactly $licenses
    }
}

Describe 'Whole run' {
    It 'checks both platforms, skips gpg without it and changes nothing without -Update' {
        $root = New-Repo
        $before = Get-RepoFiles $root
        $zip = New-Zip (Get-GoodEntries)
        $zipSha = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        $download = New-Download $zip (Get-ChecksumsText $zipSha)
        $r = Invoke-Quiet { Invoke-Main -Version '9.0.3' -BtbNTag $script:Tag -BtbNAsset $script:Asset -RepoRoot $root -Download $download.Block -GetJson (Get-MacFormula) -Gpg $null -TempRoot $script:TempRoot }
        $r.Code | Should -Be 0
        $r.Text | Should -Match 'Homebrew cross-check:\s+OK'
        $r.Text | Should -Match 'GPG signature:\s+SKIPPED'
        $r.Text | Should -Match 'BtbN checksum:\s+OK'
        $r.Text | Should -Match 'Archive contents:\s+OK'
        $r.Text | Should -Match "<LayerPeekFFmpegMacSourceSha256>$($script:ContentSha256)</LayerPeekFFmpegMacSourceSha256>"
        $r.Text | Should -Match "<LayerPeekFFmpegSha256>$zipSha</LayerPeekFFmpegSha256>"
        $r.Text | Should -Match "<LayerPeekFFmpegArchiveFolder>$([regex]::Escape($script:Folder))</LayerPeekFFmpegArchiveFolder>"
        $r.Text | Should -Match "WindowsBuildUri = $([regex]::Escape($script:ZipUrl))"
        @($download.Log) | Should -BeExactly @('https://ffmpeg.org/releases/ffmpeg-9.0.3.tar.xz', $script:ChecksumsUrl, $script:ZipUrl)
        $after = Get-RepoFiles $root
        $after.Targets | Should -BeExactly $before.Targets
        $after.Licenses | Should -BeExactly $before.Licenses
        @(Get-ChildItem -LiteralPath $script:TempRoot -Filter 'layerpeek-ffmpeg-*') | Should -BeNullOrEmpty
    }

    It 'writes the values of both platforms with -Update' {
        $root = New-Repo
        $zip = New-Zip (Get-GoodEntries)
        $zipSha = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        $download = New-Download $zip (Get-ChecksumsText $zipSha)
        $r = Invoke-Quiet { Invoke-Main -Platform all -Version '9.0.3' -BtbNTag $script:Tag -BtbNAsset $script:Asset -Update -RepoRoot $root -Download $download.Block -GetJson (Get-MacFormula) -Gpg $null -TempRoot $script:TempRoot }
        $r.Code | Should -Be 0
        $after = Get-RepoFiles $root
        $after.Targets | Should -Match '<LayerPeekFFmpegMacSourceFolder>ffmpeg-9\.0\.3</LayerPeekFFmpegMacSourceFolder>'
        $after.Targets | Should -Match "<LayerPeekFFmpegUrl>$([regex]::Escape($script:ZipUrl))</LayerPeekFFmpegUrl>"
        $after.Targets | Should -Match "<LayerPeekFFmpegSha256>$zipSha</LayerPeekFFmpegSha256>"
        $after.Targets | Should -Match "<LayerPeekFFmpegArchiveFolder>$([regex]::Escape($script:Folder))</LayerPeekFFmpegArchiveFolder>"
        $after.Licenses | Should -Match ([regex]::Escape("new(`"$($script:ZipUrl)`")"))
        $after.Licenses | Should -Match 'ffmpeg-9\.0\.3\.tar\.xz'
    }

    It 'checks and writes only Windows with -Platform windows' {
        $root = New-Repo
        $before = Get-RepoFiles $root
        $zip = New-Zip (Get-GoodEntries)
        $zipSha = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        $download = New-Download $zip (Get-ChecksumsText $zipSha)
        $r = Invoke-Quiet { Invoke-Main -Platform windows -BtbNTag $script:Tag -BtbNAsset $script:Asset -Update -RepoRoot $root -Download $download.Block -GetJson { throw 'no Homebrew' } -Gpg $null -TempRoot $script:TempRoot }
        $r.Code | Should -Be 0
        @($download.Log) | Should -BeExactly @($script:ChecksumsUrl, $script:ZipUrl)
        $after = Get-RepoFiles $root
        $after.Targets | Should -Match "<LayerPeekFFmpegSha256>$zipSha</LayerPeekFFmpegSha256>"
        $after.Targets | Should -Match '<LayerPeekFFmpegMacSourceUrl>https://ffmpeg\.org/releases/ffmpeg-9\.0\.2\.tar\.xz</LayerPeekFFmpegMacSourceUrl>'
        $after.Licenses | Should -Match 'MacOSBuildUri \{ get; \} = new\("https://ffmpeg\.org/releases/ffmpeg-9\.0\.2\.tar\.xz"\);'
        $after.Licenses | Should -Not -BeExactly $before.Licenses
    }

    It 'says so when the values are the ones already pinned (and not otherwise)' {
        $root = New-Repo
        $pinnedTag = 'autobuild-2026-10-17-20-51'
        $pinnedAsset = 'ffmpeg-n9.0.2-28-g669f5ab083-win64-lgpl-shared-9.0.zip'
        $zip = New-Zip (Get-GoodEntries -Folder 'ffmpeg-n9.0.2-28-g669f5ab083-win64-lgpl-shared-9.0')
        $zipSha = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        $targetsPath = Join-Path $root 'build' 'FFmpeg.targets'
        [System.IO.File]::WriteAllText($targetsPath, [System.IO.File]::ReadAllText($targetsPath).Replace('00ab3bc8fe02554bd0fffac9968bba498d1ab7e899a4871b85fbba828a50bfcc', $zipSha))
        $pinnedUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/$pinnedTag/$pinnedAsset"
        $checksums = "$zipSha  $pinnedAsset`n"
        $download = { param($Url, $Path) if ($Url -eq $pinnedUrl) { Copy-Item -LiteralPath $zip -Destination $Path } else { [System.IO.File]::WriteAllText($Path, $checksums) } }.GetNewClosure()

        $r = Invoke-Quiet { Invoke-Main -Platform windows -BtbNTag $pinnedTag -BtbNAsset $pinnedAsset -RepoRoot $root -Download $download -Gpg $null -TempRoot $script:TempRoot }
        $r.Code | Should -Be 0
        $r.Text | Should -Match 'already pinned'

        $other = New-Download (New-Zip (Get-GoodEntries)) ''
        $r = Invoke-Quiet { Invoke-Main -Platform macos -Version '9.0.3' -RepoRoot $root -Download $other.Block -GetJson (Get-MacFormula) -Gpg $null -TempRoot $script:TempRoot }
        $r.Code | Should -Be 0
        $r.Text | Should -Not -Match 'already pinned'
    }

    It 'fails closed when the BtbN release has no checksum file and writes nothing' {
        $root = New-Repo
        $before = Get-RepoFiles $root
        $download = New-Download (New-Zip (Get-GoodEntries)) $null
        $r = Invoke-Quiet { Invoke-Main -Version '9.0.3' -BtbNTag $script:Tag -BtbNAsset $script:Asset -Update -RepoRoot $root -Download $download.Block -GetJson (Get-MacFormula) -Gpg $null -TempRoot $script:TempRoot }
        $r.Code | Should -Be 1
        $r.Text | Should -Match 'publishes no checksums\.sha256'
        @($download.Log) | Should -Not -Contain $script:ZipUrl
        (Get-RepoFiles $root).Targets | Should -BeExactly $before.Targets
        (Get-RepoFiles $root).Licenses | Should -BeExactly $before.Licenses
        @(Get-ChildItem -LiteralPath $script:TempRoot -Filter 'layerpeek-ffmpeg-*') | Should -BeNullOrEmpty
    }

    It 'fails on a SHA256 mismatch with the BtbN checksum file and writes nothing even for macOS' {
        $root = New-Repo
        $before = Get-RepoFiles $root
        $download = New-Download (New-Zip (Get-GoodEntries)) (Get-ChecksumsText ('d' * 64))
        $r = Invoke-Quiet { Invoke-Main -Version '9.0.3' -BtbNTag $script:Tag -BtbNAsset $script:Asset -Update -RepoRoot $root -Download $download.Block -GetJson (Get-MacFormula) -Gpg $null -TempRoot $script:TempRoot }
        $r.Code | Should -Be 1
        $r.Text | Should -Match 'SHA256 mismatch'
        (Get-RepoFiles $root).Targets | Should -BeExactly $before.Targets
        (Get-RepoFiles $root).Licenses | Should -BeExactly $before.Licenses
    }

    It 'fails when the zip lacks a library and writes nothing' {
        $root = New-Repo
        $before = Get-RepoFiles $root
        $zip = New-Zip (Get-GoodEntries -Dlls @('avformat-63.dll', 'avcodec-63.dll', 'avutil-61.dll', 'swscale-10.dll'))
        $zipSha = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        $download = New-Download $zip (Get-ChecksumsText $zipSha)
        $r = Invoke-Quiet { Invoke-Main -Platform windows -BtbNTag $script:Tag -BtbNAsset $script:Asset -Update -RepoRoot $root -Download $download.Block -Gpg $null -TempRoot $script:TempRoot }
        $r.Code | Should -Be 1
        $r.Text | Should -Match 'no swresample-7\.dll'
        (Get-RepoFiles $root).Targets | Should -BeExactly $before.Targets
    }

    It 'fails before any download when the platforms would be on different release branches' -TestCases @(
        @{ P = 'macos'; V = '9.1'; A = $null },
        @{ P = 'windows'; V = $null; A = 'ffmpeg-n9.1-win64-lgpl-shared-9.1.zip' },
        @{ P = 'all'; V = '9.0.3'; A = 'ffmpeg-n9.1-win64-lgpl-shared-9.1.zip' }
    ) {
        $download = New-Download (New-Zip (Get-GoodEntries)) ''
        $r = Invoke-Quiet { Invoke-Main -Platform $P -Version $V -BtbNTag $script:Tag -BtbNAsset $A -RepoRoot (New-Repo) -Download $download.Block -GetJson { throw 'no' } -Gpg $null -TempRoot $script:TempRoot }
        $r.Code | Should -Be 1
        $r.Text | Should -Match 'same FFmpeg release branch'
        $download.Log.Count | Should -Be 0
    }

    It 'fails before any download when an input is missing or invalid' -TestCases @(
        @{ P = 'all'; V = '9.0.3'; T = $null; A = $null; M = 'Windows needs -BtbNTag' },
        @{ P = 'all'; V = $null; T = 'autobuild-2026-11-20-12-00'; A = 'ffmpeg-n9.0.3-win64-lgpl-shared-9.0.zip'; M = 'macOS needs -Version' },
        @{ P = 'macos'; V = 'latest'; T = $null; A = $null; M = 'not an FFmpeg release version' },
        @{ P = 'windows'; V = $null; T = 'autobuild-2026-11-20-12-00'; A = 'ffmpeg-n9.0.3-win64-gpl-shared-9.0.zip'; M = 'not an LGPL shared win64 build' }
    ) {
        $download = New-Download (New-Zip (Get-GoodEntries)) ''
        $r = Invoke-Quiet { Invoke-Main -Platform $P -Version $V -BtbNTag $T -BtbNAsset $A -RepoRoot (New-Repo) -Download $download.Block -GetJson { throw 'no' } -Gpg $null -TempRoot $script:TempRoot }
        $r.Code | Should -Be 1
        $r.Text | Should -Match $M
        $download.Log.Count | Should -Be 0
    }

    It 'fails on a SHA256 mismatch with Homebrew and writes nothing even with -Update' {
        $root = New-Repo
        $before = Get-RepoFiles $root
        $download = New-Download (New-Zip (Get-GoodEntries)) ''
        $formula = New-Formula 'https://ffmpeg.org/releases/ffmpeg-9.0.3.tar.xz' ('c' * 64)
        $getJson = { param($Url) $formula }.GetNewClosure()
        $r = Invoke-Quiet { Invoke-Main -Platform macos -Version '9.0.3' -Update -RepoRoot $root -Download $download.Block -GetJson $getJson -Gpg $null -TempRoot $script:TempRoot }
        $r.Code | Should -Be 1
        $r.Text | Should -Match 'SHA256 mismatch'
        (Get-RepoFiles $root).Targets | Should -BeExactly $before.Targets
        @(Get-ChildItem -LiteralPath $script:TempRoot -Filter 'layerpeek-ffmpeg-*') | Should -BeNullOrEmpty
    }

    It 'fails when Homebrew is on another version' {
        $download = New-Download (New-Zip (Get-GoodEntries)) ''
        $formula = New-Formula 'https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz' $script:ContentSha256 '9.0.2'
        $getJson = { param($Url) $formula }.GetNewClosure()
        $r = Invoke-Quiet { Invoke-Main -Platform macos -Version '9.0.3' -RepoRoot (New-Repo) -Download $download.Block -GetJson $getJson -Gpg $null -TempRoot $script:TempRoot }
        $r.Code | Should -Be 1
        $r.Text | Should -Match 'cannot be cross-checked'
    }

    It 'verifies the macOS signature with gpg when available and writes the values with -Update' {
        $root = New-Repo
        $download = New-Download (New-Zip (Get-GoodEntries)) ''
        $gpg = New-Gpg (Get-GoodLines)
        $r = Invoke-Quiet { Invoke-Main -Platform macos -Version '9.0.3' -Update -RepoRoot $root -Download $download.Block -GetJson (Get-MacFormula) -Gpg '/stand-in/gpg' -InvokeGpg $gpg.Block -TempRoot $script:TempRoot }
        $r.Code | Should -Be 0
        $r.Text | Should -Match 'GPG signature:\s+OK'
        @($download.Log) | Should -BeExactly @(
            'https://ffmpeg.org/releases/ffmpeg-9.0.3.tar.xz',
            'https://ffmpeg.org/releases/ffmpeg-9.0.3.tar.xz.asc',
            'https://ffmpeg.org/ffmpeg-devel.asc')
        $gpg.Calls.Count | Should -Be 2
        $after = Get-RepoFiles $root
        $after.Targets | Should -Match "<LayerPeekFFmpegMacSourceSha256>$($script:ContentSha256)</LayerPeekFFmpegMacSourceSha256>"
        $after.Targets | Should -Match '<LayerPeekFFmpegMacSourceFolder>ffmpeg-9\.0\.3</LayerPeekFFmpegMacSourceFolder>'
        $after.Targets | Should -Match '<LayerPeekFFmpegSha256>00ab3bc8'
        $after.Licenses | Should -Match 'ffmpeg-9\.0\.3\.tar\.xz'
        $after.Licenses | Should -Match 'autobuild-2026-10-17-20-51'
        @(Get-ChildItem -LiteralPath $script:TempRoot -Filter 'layerpeek-ffmpeg-*') | Should -BeNullOrEmpty
    }

    It 'fails on a bad signature and writes nothing' {
        $root = New-Repo
        $before = Get-RepoFiles $root
        $download = New-Download (New-Zip (Get-GoodEntries)) ''
        $gpg = New-Gpg @('[GNUPG:] BADSIG B4322F04D67658D8 FFmpeg') 1
        $r = Invoke-Quiet { Invoke-Main -Platform macos -Version '9.0.3' -Update -RepoRoot $root -Download $download.Block -GetJson (Get-MacFormula) -Gpg '/stand-in/gpg' -InvokeGpg $gpg.Block -TempRoot $script:TempRoot }
        $r.Code | Should -Be 1
        $r.Text | Should -Match 'does not verify'
        (Get-RepoFiles $root).Targets | Should -BeExactly $before.Targets
    }
}
