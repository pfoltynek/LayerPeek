<#
.SYNOPSIS
    Pester 5+ tests of the FFmpeg build of the camera libraries on Windows: which build build/FFmpeg.targets selects for
    which runtime identifier and host (win-x64, win-arm64 cross build with llvm-mingw, T-225), the toolchain errors before
    any download, the arguments of build/windows/build-ffmpeg.sh and the ARM64 libraries in the cache.

.DESCRIPTION
    The selection tests only evaluate the project (dotnet msbuild -getProperty) or run its toolchain check against a
    missing MSYS2 and an empty cache folder: nothing is downloaded or compiled. The host architecture is overridden with
    the global property _LayerPeekFFmpegHostArchitecture to cover the Windows ARM64 host on an x64 machine. The check of
    the ARM64 libraries reads the cache of an earlier win-arm64 build (dotnet publish src/LayerPeek.App -r win-arm64) and
    is skipped without one.

    Temporary files live in one folder under $env:LAYERPEEK_PESTER_TESTS_TEMP (set by the xUnit wrapper
    tests/LayerPeek.Scripts.Tests) or the system temp folder; AfterAll deletes it.

.EXAMPLE
    Invoke-Pester build/FFmpeg.Tests.ps1 -Output Detailed
#>
#Requires -Version 7.2

BeforeDiscovery {
    # Evaluated before BeforeAll: the -Skip of the argument tests needs it.
    $msys2 = if ($env:LayerPeekMsys2Root) { $env:LayerPeekMsys2Root } else { 'C:\msys64' }
    $HasMsys2Bash = $IsWindows -and (Test-Path -LiteralPath (Join-Path $msys2 'usr' 'bin' 'bash.exe') -PathType Leaf)
}

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..' 'scripts' 'ReleaseTools' 'ReleaseTools.psm1') -Force

    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).ProviderPath
    $script:Project = Join-Path $script:RepoRoot 'src' 'LayerPeek.Camera.FFmpeg' 'LayerPeek.Camera.FFmpeg.csproj'
    $script:Targets = [System.IO.File]::ReadAllText((Join-Path $script:RepoRoot 'build' 'FFmpeg.targets'))
    $script:BuildScriptPath = Join-Path $script:RepoRoot 'build' 'windows' 'build-ffmpeg.sh'
    $script:BuildScript = [System.IO.File]::ReadAllText($script:BuildScriptPath)
    $base = if ($env:LAYERPEEK_PESTER_TESTS_TEMP) { $env:LAYERPEEK_PESTER_TESTS_TEMP } else { [System.IO.Path]::GetTempPath() }
    $script:TempRoot = Join-Path $base "layerpeek-ff-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    $null = New-Item -ItemType Directory -Path $script:TempRoot
    # A cache root of its own: the evaluations never point at the real cache of the machine.
    $script:CacheRoot = Join-Path $script:TempRoot 'cache'

    $msys2 = if ($env:LayerPeekMsys2Root) { $env:LayerPeekMsys2Root } else { 'C:\msys64' }
    # Join-Path resolves the drive of 'C:\msys64', which does not exist on macOS and Linux (the tests needing it skip there).
    $script:Bash = [System.IO.Path]::Combine($msys2, 'usr', 'bin', 'bash.exe')

    # The properties of the library project for the given global properties (evaluation only).
    function Get-FFmpegBuildProperties {
        param([string[]] $Arguments = @(), [string] $CacheRoot = $script:CacheRoot)
        $names = '_LayerPeekFFmpegRid', '_LayerPeekFFmpegPlatform', 'LayerPeekFFmpegEnabled', 'LayerPeekFFmpegCacheDir', '_LayerPeekFFmpegArm64HostWithoutRid', '_LayerPeekLlvmMingwDir'
        $output = (& dotnet msbuild $script:Project -nologo -nr:false "-p:LayerPeekFFmpegCacheRoot=$CacheRoot" @($names | ForEach-Object { "-getProperty:$_" }) @Arguments 2>&1) -join "`n"
        if ($LASTEXITCODE -ne 0) { throw "dotnet msbuild failed: $output" }
        ($output | ConvertFrom-Json).Properties
    }

    # Runs a target of the library project; returns ExitCode and Output (nothing is restored or compiled).
    function Invoke-FFmpegTarget {
        param([Parameter(Mandatory)] [string] $Target, [string[]] $Arguments = @())
        $output = (& dotnet msbuild $script:Project -nologo -nr:false "-t:$Target" "-p:LayerPeekFFmpegCacheRoot=$script:CacheRoot" @Arguments 2>&1) -join "`n"
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    }

    # The DLL names of the import table of a PE image (like FFmpegWindowsBuildTests.ReadImports).
    function Get-PeImportNames {
        param([Parameter(Mandatory)] [string] $Path)
        $bytes = [System.IO.File]::ReadAllBytes($Path)
        $stream = [System.IO.MemoryStream]::new($bytes)
        try { $headers = [System.Reflection.PortableExecutable.PEHeaders]::new($stream) }
        finally { $stream.Dispose() }
        $offset = {
            param([int] $Rva)
            foreach ($section in $headers.SectionHeaders) {
                if ($Rva -ge $section.VirtualAddress -and $Rva -lt $section.VirtualAddress + [Math]::Max($section.VirtualSize, $section.SizeOfRawData)) {
                    return $Rva - $section.VirtualAddress + $section.PointerToRawData
                }
            }
            throw "RVA $Rva of $Path is in no section."
        }
        # IMAGE_IMPORT_DESCRIPTOR: 20 bytes, the name RVA at offset 12; the table ends with a zeroed descriptor.
        $descriptor = & $offset $headers.PEHeader.ImportTableDirectory.RelativeVirtualAddress
        while (($nameRva = [BitConverter]::ToInt32($bytes, $descriptor + 12)) -ne 0) {
            $start = & $offset $nameRva
            $end = [Array]::IndexOf($bytes, [byte] 0, $start)
            [System.Text.Encoding]::ASCII.GetString($bytes, $start, $end - $start)
            $descriptor += 20
        }
    }

    # The value of a variable inside one case of build-ffmpeg.sh ("win-x64)" ... ";;").
    function Get-ScriptCaseValue {
        param([Parameter(Mandatory)] [string] $Rid, [Parameter(Mandatory)] [string] $Name)
        $case = [regex]::Match($script:BuildScript, "(?ms)^    $([regex]::Escape($Rid))\)\r?\n(.*?)^        ;;")
        if (-not $case.Success) { throw "build-ffmpeg.sh has no case $Rid." }
        $value = [regex]::Match($case.Groups[1].Value, "(?ms)^        $Name=(""[^""]*""|\([^)]*\)|\S*)")
        if (-not $value.Success) { throw "The case $Rid of build-ffmpeg.sh sets no $Name." }
        $value.Groups[1].Value.Trim('"')
    }
}

AfterAll {
    Remove-Item -LiteralPath $script:TempRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Describe 'Which FFmpeg build build/FFmpeg.targets selects (T-225)' -Skip:(-not $IsWindows) {
    It 'builds win-x64 without a runtime identifier on an x64 host (dotnet build / dotnet test need no llvm-mingw)' {
        $properties = Get-FFmpegBuildProperties '-p:_LayerPeekFFmpegHostArchitecture=X64'
        $properties._LayerPeekFFmpegRid | Should -BeExactly 'win-x64'
        $properties._LayerPeekFFmpegPlatform | Should -BeExactly 'windows'
        $properties.LayerPeekFFmpegEnabled | Should -BeExactly 'true'
        $properties.LayerPeekFFmpegCacheDir | Should -BeLike "$script:CacheRoot\win-x64-*\"
        $properties._LayerPeekFFmpegArm64HostWithoutRid | Should -BeNullOrEmpty
    }

    It 'builds <Rid> for the runtime identifier of the app on an <HostArch> host' -ForEach @(
        @{ Rid = 'win-x64'; HostArch = 'X64' }
        @{ Rid = 'win-arm64'; HostArch = 'X64' }
        @{ Rid = 'win-x64'; HostArch = 'Arm64' }
        @{ Rid = 'win-arm64'; HostArch = 'Arm64' }
    ) {
        $properties = Get-FFmpegBuildProperties "-p:_LayerPeekFFmpegHostArchitecture=$HostArch", "-p:LayerPeekAppRuntimeIdentifier=$Rid"
        $properties._LayerPeekFFmpegRid | Should -BeExactly $Rid
        $properties._LayerPeekFFmpegPlatform | Should -BeExactly 'windows'
        $properties.LayerPeekFFmpegEnabled | Should -BeExactly 'true'
        $properties.LayerPeekFFmpegCacheDir | Should -BeLike "$script:CacheRoot\$Rid-*\"
        $properties._LayerPeekFFmpegArm64HostWithoutRid | Should -BeNullOrEmpty
    }

    It 'takes the own RuntimeIdentifier of the project when no app passes one, and the app RID first' {
        (Get-FFmpegBuildProperties '-p:RuntimeIdentifier=win-arm64')._LayerPeekFFmpegRid | Should -BeExactly 'win-arm64'
        (Get-FFmpegBuildProperties '-p:RuntimeIdentifier=win-x64', '-p:LayerPeekAppRuntimeIdentifier=win-arm64')._LayerPeekFFmpegRid | Should -BeExactly 'win-arm64'
    }

    It 'switches FFmpeg off on a Windows ARM64 host without a runtime identifier (not supported, no x64 libraries)' {
        $properties = Get-FFmpegBuildProperties '-p:_LayerPeekFFmpegHostArchitecture=Arm64'
        $properties._LayerPeekFFmpegRid | Should -BeNullOrEmpty
        $properties._LayerPeekFFmpegPlatform | Should -BeNullOrEmpty
        $properties.LayerPeekFFmpegEnabled | Should -BeExactly 'false'
        $properties._LayerPeekFFmpegArm64HostWithoutRid | Should -BeExactly 'true'
    }

    It 'says so in a message, not a warning (warnings fail the build)' {
        $run = Invoke-FFmpegTarget 'LayerPeekFFmpegArm64HostNotice' '-p:_LayerPeekFFmpegHostArchitecture=Arm64'
        $run.ExitCode | Should -Be 0
        $run.Output | Should -Match 'LayerPeek\.Camera\.FFmpeg: no FFmpeg camera libraries\. On a Windows ARM64 machine a build without a runtime identifier is not supported'
        $run.Output | Should -Match ([regex]::Escape('dotnet publish src/LayerPeek.App -c Release -r win-arm64'))
        $run.Output | Should -Not -Match 'warning'
        # An x64 host says nothing.
        (Invoke-FFmpegTarget 'LayerPeekFFmpegArm64HostNotice' '-p:_LayerPeekFFmpegHostArchitecture=X64').Output | Should -Not -Match 'no FFmpeg camera libraries'
    }

    It 'provides nothing for another runtime identifier (<_>)' -ForEach 'win-x86', 'linux-x64', 'osx-arm64' {
        $properties = Get-FFmpegBuildProperties "-p:LayerPeekAppRuntimeIdentifier=$_"
        $properties._LayerPeekFFmpegPlatform | Should -BeNullOrEmpty
        $properties.LayerPeekFFmpegEnabled | Should -BeExactly 'false'
    }

    It 'keeps -p:LayerPeekFFmpegEnabled=false for win-arm64' {
        (Get-FFmpegBuildProperties '-p:LayerPeekAppRuntimeIdentifier=win-arm64', '-p:LayerPeekFFmpegEnabled=false').LayerPeekFFmpegEnabled | Should -BeExactly 'false'
    }

    It 'keys the win-arm64 cache by the llvm-mingw release too, the win-x64 cache not' {
        $other = '-p:LayerPeekLlvmMingwSha256=' + ('0' * 64)
        $arm = (Get-FFmpegBuildProperties '-p:LayerPeekAppRuntimeIdentifier=win-arm64').LayerPeekFFmpegCacheDir
        (Get-FFmpegBuildProperties '-p:LayerPeekAppRuntimeIdentifier=win-arm64', $other).LayerPeekFFmpegCacheDir | Should -Not -BeExactly $arm
        $x64 = (Get-FFmpegBuildProperties '-p:LayerPeekAppRuntimeIdentifier=win-x64').LayerPeekFFmpegCacheDir
        (Get-FFmpegBuildProperties '-p:LayerPeekAppRuntimeIdentifier=win-x64', $other).LayerPeekFFmpegCacheDir | Should -BeExactly $x64
        # The hash part differs between the architectures as well (not only the prefix).
        ($arm -replace '^.*\\win-arm64-', '') | Should -Not -BeExactly ($x64 -replace '^.*\\win-x64-', '')
    }

    It 'gives the library an output folder per app RID, so x64 and ARM64 libraries never share one' {
        function OutDir([string[]] $Arguments) {
            $output = (& dotnet msbuild $script:Project -nologo -nr:false -getProperty:OutDir @Arguments 2>&1) -join "`n"
            if ($LASTEXITCODE -ne 0) { throw "dotnet msbuild failed: $output" }
            $output.Trim()
        }
        OutDir '-p:LayerPeekAppRuntimeIdentifier=win-arm64', '-p:Configuration=Release' | Should -BeLike 'bin\Release\app-win-arm64\net*\'
        OutDir '-p:LayerPeekAppRuntimeIdentifier=win-x64', '-p:Configuration=Release' | Should -BeLike 'bin\Release\app-win-x64\net*\'
        OutDir @() | Should -BeLike 'bin\Debug\net*\'
    }

    It 'extracts llvm-mingw into the cache root, in a folder of the pinned release' {
        $folder = [regex]::Match($script:Targets, '<LayerPeekLlvmMingwFolder>([^<]+)<').Groups[1].Value
        (Get-FFmpegBuildProperties '-p:LayerPeekAppRuntimeIdentifier=win-arm64')._LayerPeekLlvmMingwDir | Should -BeExactly "$script:CacheRoot\$folder\"
    }
}

Describe 'The pinned llvm-mingw release (T-225)' {
    It 'is a release zip of mstorsjo/llvm-mingw for Windows x64 hosts (ucrt) with its SHA-256 and top folder' {
        $url = [regex]::Matches($script:Targets, '<LayerPeekLlvmMingwUrl>([^<]+)</LayerPeekLlvmMingwUrl>')
        $sha = [regex]::Matches($script:Targets, '<LayerPeekLlvmMingwSha256>([^<]+)</LayerPeekLlvmMingwSha256>')
        $folder = [regex]::Matches($script:Targets, '<LayerPeekLlvmMingwFolder>([^<]+)</LayerPeekLlvmMingwFolder>')
        $url.Count | Should -Be 1
        $sha.Count | Should -Be 1
        $folder.Count | Should -Be 1
        $release = [regex]::Match($folder[0].Groups[1].Value, '^llvm-mingw-(\d{8})-ucrt-x86_64$')
        $release.Success | Should -BeTrue -Because "$($folder[0].Groups[1].Value) is the top folder of an llvm-mingw release zip for x64 hosts (ucrt)"
        $release = $release.Groups[1].Value
        $url[0].Groups[1].Value | Should -BeExactly "https://github.com/mstorsjo/llvm-mingw/releases/download/$release/$($folder[0].Groups[1].Value).zip"
        $sha[0].Groups[1].Value | Should -Match '^[0-9a-f]{64}$'
    }
}

Describe 'Toolchain errors before any download (T-225)' -Skip:(-not $IsWindows) {
    BeforeEach {
        $script:NoMsys2 = Join-Path $script:TempRoot "no-msys2-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    }

    It 'names MSYS2, its packages, llvm-mingw and the way out for win-arm64, and creates nothing' {
        $run = Invoke-FFmpegTarget 'LayerPeekAcquireFFmpeg' '-p:LayerPeekAppRuntimeIdentifier=win-arm64', "-p:LayerPeekMsys2Root=$script:NoMsys2"
        $run.ExitCode | Should -Not -Be 0
        $run.Output | Should -Match ([regex]::Escape("MSYS2 is not installed in $script:NoMsys2 (missing: usr\bin\bash.exe, usr\bin\make.exe, usr\bin\cmp.exe, usr\bin\tar.exe, usr\bin\xz.exe)"))
        $run.Output | Should -Match 'cross-compiles the FFmpeg camera libraries for ARM64 from source with llvm-mingw'
        $run.Output | Should -Match ([regex]::Escape('pacman -S --needed make diffutils,'))
        $run.Output | Should -Match ([regex]::Escape('dotnet publish -r win-arm64 -p:LayerPeekFFmpegEnabled=false'))
        $run.Output | Should -Not -Match 'Downloading'
        Test-Path -LiteralPath $script:CacheRoot | Should -BeFalse
    }

    It 'keeps the win-x64 error of T-202 (MinGW-w64 packages, no llvm-mingw)' {
        $run = Invoke-FFmpegTarget 'LayerPeekAcquireFFmpeg' '-p:LayerPeekAppRuntimeIdentifier=win-x64', "-p:LayerPeekMsys2Root=$script:NoMsys2"
        $run.ExitCode | Should -Not -Be 0
        $run.Output | Should -Match ([regex]::Escape("MSYS2 with MinGW-w64 is not installed in $script:NoMsys2"))
        $run.Output | Should -Match ([regex]::Escape('mingw64\bin\gcc.exe'))
        $run.Output | Should -Match ([regex]::Escape('pacman -S --needed make diffutils mingw-w64-x86_64-gcc mingw-w64-x86_64-nasm'))
        $run.Output | Should -Not -Match 'llvm-mingw'
        Test-Path -LiteralPath $script:CacheRoot | Should -BeFalse
    }
}

Describe 'build/windows/build-ffmpeg.sh (T-225)' {
    It 'gives win-arm64 the configure line of win-x64 plus only the cross compiler of llvm-mingw' {
        (Get-ScriptCaseValue 'win-x64' 'TOOLCHAIN_FLAGS') | Should -BeExactly '()'
        (Get-ScriptCaseValue 'win-arm64' 'TOOLCHAIN_FLAGS') | Should -BeExactly '(--enable-cross-compile --cross-prefix=aarch64-w64-mingw32- --host-cc=x86_64-w64-mingw32-gcc)'
        (Get-ScriptCaseValue 'win-x64' 'ARCH') | Should -BeExactly 'x86_64'
        (Get-ScriptCaseValue 'win-arm64' 'ARCH') | Should -BeExactly 'aarch64'
        (Get-ScriptCaseValue 'win-arm64' 'PE_FORMAT') | Should -BeExactly 'coff-arm64'
        $script:BuildScript | Should -Match '(?m)^CONFIGURE_FLAGS\+=\("\$\{TOOLCHAIN_FLAGS\[@\]\}"\)$'
        # One configure line for both (no second CONFIGURE_FLAGS list).
        [regex]::Matches($script:BuildScript, '(?m)^CONFIGURE_FLAGS=\(').Count | Should -Be 1
    }

    It 'lets the ARM64 libraries import only DLLs of Windows (the Universal C Runtime instead of msvcrt.dll)' {
        $arm = (Get-ScriptCaseValue 'win-arm64' 'SYSTEM_DLLS') -split '\s+' | Where-Object { $_ }
        $arm | Should -Contain 'kernel32.dll'
        $arm | Should -Contain 'api-ms-win-crt-runtime-l1-1-0.dll'
        $arm | Should -Not -Contain 'msvcrt.dll'
        $arm | Where-Object { $_ -match 'unwind|c\+\+|pthread|gcc|clang' } | Should -BeNullOrEmpty
        $arm | ForEach-Object { $_ | Should -Match '^(kernel32|user32|advapi32|bcrypt|ws2_32|ole32|shell32|api-ms-win-crt-[a-z]+-l1-1-0)\.dll$' }
    }

    Context 'arguments' -Skip:(-not $HasMsys2Bash) {
        BeforeAll {
            function Invoke-BuildScript([string[]] $Arguments) {
                $output = (& $script:Bash --noprofile --norc ($script:BuildScriptPath.Replace('\', '/')) @Arguments 2>&1) -join "`n"
                [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
            }
            $script:Work = Join-Path $script:TempRoot 'script'
            $script:Tarball = Join-Path $script:Work 'ffmpeg.tar.xz'
            $null = New-Item -ItemType Directory -Force -Path $script:Work
            [System.IO.File]::WriteAllText($script:Tarball, 'not a tarball')
        }

        It 'refuses win-arm64 without the llvm-mingw folder and win-x64 with one (usage)' {
            $missing = Invoke-BuildScript 'win-arm64', $script:Tarball, 'ffmpeg-9.0.2', (Join-Path $script:Work 'w'), (Join-Path $script:Work 'o')
            $missing.ExitCode | Should -Be 2
            $missing.Output | Should -Match ([regex]::Escape('build-ffmpeg.sh win-arm64 <tarball> <archive folder> <work folder> <output folder> <llvm-mingw folder>'))
            (Invoke-BuildScript 'win-x64', $script:Tarball, 'ffmpeg-9.0.2', (Join-Path $script:Work 'w'), (Join-Path $script:Work 'o'), $script:Work).ExitCode | Should -Be 2
            Test-Path -LiteralPath (Join-Path $script:Work 'w') | Should -BeFalse
        }

        It 'refuses an unknown runtime identifier and an llvm-mingw folder without bin, before anything is extracted' {
            $unknown = Invoke-BuildScript 'win-x86', $script:Tarball, 'ffmpeg-9.0.2', (Join-Path $script:Work 'w'), (Join-Path $script:Work 'o')
            $unknown.ExitCode | Should -Be 1
            $unknown.Output | Should -Match "the runtime identifier 'win-x86' is not supported \(win-x64 or win-arm64\)"
            $noBin = Invoke-BuildScript 'win-arm64', $script:Tarball, 'ffmpeg-9.0.2', (Join-Path $script:Work 'w'), (Join-Path $script:Work 'o'), (Join-Path $script:Work 'llvm-mingw')
            $noBin.ExitCode | Should -Be 1
            $noBin.Output | Should -Match 'has no bin folder \(the extracted llvm-mingw-<version>-ucrt-x86_64\.zip'
            Test-Path -LiteralPath (Join-Path $script:Work 'w') | Should -BeFalse
        }
    }
}

Describe 'The ARM64 FFmpeg libraries in the cache (T-225)' -Skip:(-not $IsWindows) {
    BeforeAll {
        # The real cache of this machine (read only): filled by dotnet publish src/LayerPeek.App -r win-arm64.
        $realRoot = if ($env:LayerPeekFFmpegCacheRoot) { $env:LayerPeekFFmpegCacheRoot } else { Join-Path $env:LOCALAPPDATA 'LayerPeek.Build' 'ffmpeg' }
        $script:ArmCache = (Get-FFmpegBuildProperties '-p:LayerPeekAppRuntimeIdentifier=win-arm64' -CacheRoot $realRoot).LayerPeekFFmpegCacheDir
        $script:ArmBuilt = Test-Path -LiteralPath (Join-Path $script:ArmCache 'verified.sha256') -PathType Leaf
        $script:Libraries = 'avcodec-63.dll', 'avformat-63.dll', 'avutil-61.dll', 'swscale-10.dll'
    }

    It 'are ARM64 PE images (0xAA64) that import only each other and DLLs of Windows' {
        if (-not $script:ArmBuilt) { Set-ItResult -Skipped -Because "no win-arm64 FFmpeg cache in $script:ArmCache (dotnet publish src/LayerPeek.App -c Release -r win-arm64 builds it)" ; return }
        $allowed = @($script:Libraries) + @((Get-ScriptCaseValue 'win-arm64' 'SYSTEM_DLLS') -split '\s+' | Where-Object { $_ })
        foreach ($name in $script:Libraries) {
            $path = Join-Path $script:ArmCache 'bin' $name
            Get-PeMachine $path | Should -Be 0xAA64 -Because $name
            $imports = @(Get-PeImportNames $path)
            $imports | Should -Contain 'KERNEL32.dll' -Because $name
            $imports | Where-Object { $allowed -notcontains $_.ToLowerInvariant() -and $allowed -notcontains $_ } | Should -BeNullOrEmpty -Because "$name imports only siblings and DLLs of Windows"
        }
    }

    It 'come with an llvm-mingw marked with the pinned SHA-256 and without the folders the build leaves out' {
        if (-not $script:ArmBuilt) { Set-ItResult -Skipped -Because "no win-arm64 FFmpeg cache in $script:ArmCache" ; return }
        $llvm = (Get-FFmpegBuildProperties '-p:LayerPeekAppRuntimeIdentifier=win-arm64' -CacheRoot (Split-Path -Parent $script:ArmCache.TrimEnd('\')))._LayerPeekLlvmMingwDir
        $pinned = [regex]::Match($script:Targets, '<LayerPeekLlvmMingwSha256>([^<]+)<').Groups[1].Value
        ([System.IO.File]::ReadAllText((Join-Path $llvm 'verified.sha256'))).Trim() | Should -BeExactly $pinned
        foreach ($left in 'python', 'i686-w64-mingw32', 'armv7-w64-mingw32') { Test-Path -LiteralPath (Join-Path $llvm $left) | Should -BeFalse -Because $left }
        Test-Path -LiteralPath (Join-Path $llvm 'x86_64-w64-mingw32') | Should -BeTrue -Because 'the generators of the FFmpeg build are x86_64 programs'
    }

    It 'are LGPL 3 with D3D11VA and the configure line of win-x64 except architecture and toolchain' {
        if (-not $script:ArmBuilt) { Set-ItResult -Skipped -Because "no win-arm64 FFmpeg cache in $script:ArmCache" ; return }
        $build = [System.IO.File]::ReadAllText((Join-Path $script:ArmCache 'BUILD.txt'))
        $build | Should -Match 'built by build/windows/build-ffmpeg\.sh for win-arm64'
        $build | Should -Match '(?m)^License: LGPL version 3 or later'
        $build | Should -Match '(?m)^Toolchain: llvm-mingw llvm-mingw-\d{8}-ucrt-x86_64, cross build, clang version'
        # The licences window of the ARM64 app names this version (ThirdPartyLicenses.MinGWRuntimeVersionArm64).
        $licenses = [System.IO.File]::ReadAllText((Join-Path $script:RepoRoot 'src' 'LayerPeek.App' 'Licenses' 'ThirdPartyLicenses.cs'))
        $listed = [regex]::Match($licenses, 'const string MinGWRuntimeVersionArm64 = "([^"]+)";')
        $listed.Success | Should -BeTrue
        $build | Should -Match "(?m)^MinGW-w64 runtime: $([regex]::Escape($listed.Groups[1].Value))\r?$"
        $line = [regex]::Match($build, '(?m)^configure --prefix=<staging> (.*)$').Groups[1].Value.Trim()
        $expected = [regex]::Match($script:BuildScript, '(?ms)^CONFIGURE_FLAGS=\((.*?)^\)').Groups[1].Value -split '\s+' | Where-Object { $_ } | ForEach-Object { $_ -replace '\$ARCH', 'aarch64' }
        $line | Should -BeExactly ((@($expected) + '--enable-cross-compile', '--cross-prefix=aarch64-w64-mingw32-', '--host-cc=x86_64-w64-mingw32-gcc') -join ' ')
        # The library itself says the same (read without loading it: ARM64 code does not load on x64).
        $facts = Get-FFmpegLibraryFacts -Bytes ([System.IO.File]::ReadAllBytes((Join-Path $script:ArmCache 'bin' 'avcodec-63.dll')))
        $facts.Version | Should -BeExactly '9.0.2'
        # configure stores comma lists quoted: --enable-hwaccel='h264_d3d11va,h264_d3d11va2'.
        $facts.Configuration | Should -Match "--enable-d3d11va --enable-hwaccel='?h264_d3d11va,h264_d3d11va2'? .*--arch=aarch64 .*--enable-cross-compile"
        $facts.Configuration | Should -Not -Match '--enable-gpl|--enable-nonfree|--enable-lib'
        [System.Text.Encoding]::Latin1.GetString([System.IO.File]::ReadAllBytes((Join-Path $script:ArmCache 'bin' 'avcodec-63.dll'))) | Should -Match 'h264_d3d11va2'
        [System.IO.File]::ReadAllText((Join-Path $script:ArmCache 'LICENSE.txt')) | Should -Match 'GNU LESSER GENERAL PUBLIC LICENSE'
    }
}
