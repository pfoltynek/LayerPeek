<#
.SYNOPSIS
    Pester 5+ tests of the kept results of failed end-to-end runs (tools/RunE2E.Results.psm1, T-132).

.DESCRIPTION
    No end-to-end test runs: the results folders are made up under a stand-in temp folder, which the functions get as
    -TempRoot. Temporary files live in one folder under $env:LAYERPEEK_PESTER_TESTS_TEMP (set by the xUnit wrapper
    tests/LayerPeek.Scripts.Tests) or under the system temp folder; AfterAll deletes it.

.EXAMPLE
    Invoke-Pester tools/RunE2E.Results.Tests.ps1 -Output Detailed
#>
#Requires -Version 7.2

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot 'RunE2E.Results.psm1') -Force

    $base = if ($env:LAYERPEEK_PESTER_TESTS_TEMP) { $env:LAYERPEEK_PESTER_TESTS_TEMP } else { [System.IO.Path]::GetTempPath() }
    $script:TempRoot = Join-Path $base "layerpeek-re-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    $null = New-Item -ItemType Directory -Path $script:TempRoot

    $script:Marker = '.layerpeek-e2e-results'
    $script:Name = 'LayerPeek.App.E2E.Results.0a1b2c3d'

    # A fresh stand-in temp folder per test.
    function New-TempRoot {
        $root = Join-Path $script:TempRoot ([guid]::NewGuid().ToString('N').Substring(0, 8))
        $null = New-Item -ItemType Directory -Path $root
        $root
    }

    # A results folder as the tests leave it: the marker, a screenshot, an app log in a subfolder.
    function New-ResultsFolder([string] $Path, [switch] $NoMarker) {
        $null = New-Item -ItemType Directory -Path (Join-Path $Path 'logs') -Force
        if (-not $NoMarker) { [System.IO.File]::WriteAllText((Join-Path $Path $script:Marker), 'worktree') }
        [System.IO.File]::WriteAllText((Join-Path $Path 'shot.png'), 'png')
        [System.IO.File]::WriteAllText((Join-Path $Path 'logs' 'app.log'), 'log')
        $Path
    }

    function Get-Names([string] $Root) {
        @(Get-ChildItem -LiteralPath $Root -Force | ForEach-Object Name | Sort-Object)
    }
}

AfterAll {
    if ($script:TempRoot -and (Test-Path -LiteralPath $script:TempRoot)) { Remove-Item -LiteralPath $script:TempRoot -Recurse -Force }
}

Describe 'Which passes are kept' {
    It 'keeps nothing of a successful run' {
        $passes = @([pscustomobject] @{ Name = 'Debug exe'; ExitCode = 0; Results = 'a' }, [pscustomobject] @{ Name = 'AOT'; ExitCode = 0; Results = 'b' })
        @(Get-PassesToKeep -Passes $passes -ExitCode 0).Count | Should -Be 0
        @(Get-PassesToKeep -Passes @() -ExitCode 0).Count | Should -Be 0
    }

    It 'keeps only the failed passes of a failed run' {
        $passes = @([pscustomobject] @{ Name = 'Debug exe'; ExitCode = 0; Results = 'a' }, [pscustomobject] @{ Name = 'AOT'; ExitCode = 1; Results = 'b' })
        @(Get-PassesToKeep -Passes $passes -ExitCode 1 | ForEach-Object Name) | Should -Be @('AOT')
    }

    It 'keeps every pass when only the restore after the run failed' {
        $passes = @([pscustomobject] @{ Name = 'Debug exe'; ExitCode = 0; Results = 'a' })
        @(Get-PassesToKeep -Passes $passes -ExitCode 1 | ForEach-Object Name) | Should -Be @('Debug exe')
        @(Get-PassesToKeep -Passes @() -ExitCode 1).Count | Should -Be 0
    }
}

Describe 'Copy of a failed pass' {
    It 'copies the whole folder to a folder with -failed- and the time stamp next to it and leaves the original' {
        $root = New-TempRoot
        $results = New-ResultsFolder (Join-Path $root $script:Name)

        $copy = Save-FailedTestResults -Results $results -TempRoot $root -Now ([datetime]::new(2026, 11, 1, 9, 5, 7))

        $copy | Should -Be (Join-Path $root "$($script:Name)-failed-20261101-090507")
        [System.IO.File]::ReadAllText((Join-Path $copy 'shot.png')) | Should -Be 'png'
        [System.IO.File]::ReadAllText((Join-Path $copy 'logs' 'app.log')) | Should -Be 'log'
        Test-Path -LiteralPath (Join-Path $copy $script:Marker) | Should -BeTrue
        Get-Names $results | Should -Be (@($script:Marker, 'logs', 'shot.png') | Sort-Object)
    }

    It 'copies the results of the Native AOT pass next to them and a second copy in the same second under its own name' {
        $root = New-TempRoot
        $results = New-ResultsFolder (Join-Path $root "$($script:Name)-aot")
        $now = [datetime]::new(2026, 11, 1, 23, 59, 59)

        $first = Save-FailedTestResults -Results $results -TempRoot $root -Now $now
        $second = Save-FailedTestResults -Results $results -TempRoot $root -Now $now

        $first | Should -Be (Join-Path $root "$($script:Name)-aot-failed-20261101-235959")
        $second | Should -Be (Join-Path $root "$($script:Name)-aot-failed-20261101-235959-2")
        Test-Path -LiteralPath (Join-Path $second 'logs' 'app.log') | Should -BeTrue
    }

    It 'copies a log that an app still has open for writing' {
        $root = New-TempRoot
        $results = New-ResultsFolder (Join-Path $root $script:Name)
        $open = [System.IO.FileStream]::new((Join-Path $results 'logs' 'app.log'), [System.IO.FileMode]::Append, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read)
        try {
            $copy = Save-FailedTestResults -Results $results -TempRoot $root -WarningVariable warnings -WarningAction SilentlyContinue
        }
        finally {
            $open.Dispose()
        }

        [System.IO.File]::ReadAllText((Join-Path $copy 'logs' 'app.log')) | Should -Be 'log'
        $warnings | Should -BeNullOrEmpty
    }

    It 'keeps nothing when the pass left no results folder or one without the marker of the tests' {
        $root = New-TempRoot
        Save-FailedTestResults -Results (Join-Path $root $script:Name) -TempRoot $root | Should -BeNullOrEmpty
        $results = New-ResultsFolder (Join-Path $root $script:Name) -NoMarker
        Save-FailedTestResults -Results $results -TempRoot $root | Should -BeNullOrEmpty
        Get-Names $root | Should -Be @($script:Name)
    }

    It 'refuses a folder that is not a results folder of the tests directly in the temp folder' -TestCases @(
        @{ Case = 'another name'; Relative = 'Documents' }
        @{ Case = 'another prefix'; Relative = 'LayerPeek.App.E2E.Results' }
        @{ Case = 'an upper-case id'; Relative = 'LayerPeek.App.E2E.Results.0A1B2C3D' }
        @{ Case = 'a kept copy'; Relative = 'LayerPeek.App.E2E.Results.0a1b2c3d-failed-20261101-090507' }
        @{ Case = 'a subfolder'; Relative = 'x/LayerPeek.App.E2E.Results.0a1b2c3d' }
    ) {
        $root = New-TempRoot
        $folder = New-ResultsFolder (Join-Path $root $Relative)

        { Save-FailedTestResults -Results $folder -TempRoot $root } | Should -Throw '*Refusing*'
        { Remove-OldFailedTestResults -Results $folder -TempRoot $root } | Should -Throw '*Refusing*'
        Test-Path -LiteralPath (Join-Path $folder 'shot.png') | Should -BeTrue
    }

    It 'refuses a results folder outside the temp folder' {
        $root = New-TempRoot
        $other = New-TempRoot
        $folder = New-ResultsFolder (Join-Path $other $script:Name)

        { Save-FailedTestResults -Results $folder -TempRoot $root } | Should -Throw '*not directly in the temp folder*'
        { Remove-OldFailedTestResults -Results $folder -TempRoot $root } | Should -Throw '*not directly in the temp folder*'
    }

    It 'refuses a results folder that is a link and does not follow links inside it' -Skip:(-not $IsWindows) {
        $root = New-TempRoot
        $target = New-ResultsFolder (Join-Path $root 'target')
        $link = Join-Path $root $script:Name
        $null = New-Item -ItemType Junction -Path $link -Target $target

        { Save-FailedTestResults -Results $link -TempRoot $root } | Should -Throw '*is a link*'

        Remove-Item -LiteralPath $link -Force
        $results = New-ResultsFolder $link
        $null = New-Item -ItemType Junction -Path (Join-Path $results 'outside') -Target $target
        $copy = Save-FailedTestResults -Results $results -TempRoot $root -WarningAction SilentlyContinue
        Test-Path -LiteralPath (Join-Path $copy 'outside') | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $copy 'shot.png') | Should -BeTrue
    }
}

Describe 'Pruning of old copies' {
    It 'keeps 10 copies by default' {
        Get-DefaultFailedResultsToKeep | Should -Be 10
    }

    It 'deletes the oldest copies beyond Keep, by the time stamp in the name' {
        $root = New-TempRoot
        $results = Join-Path $root $script:Name
        $stamps = @('20261030-120000', '20261101-080000', '20261031-235959', '20261101-080000-2', '20251231-235959')
        foreach ($stamp in $stamps) { $null = New-ResultsFolder "$results-failed-$stamp" }

        $deleted = @(Remove-OldFailedTestResults -Results $results -TempRoot $root -Keep 3)

        @($deleted | ForEach-Object { Split-Path -Leaf $_ } | Sort-Object) | Should -Be @("$($script:Name)-failed-20251231-235959", "$($script:Name)-failed-20261030-120000")
        Get-Names $root | Should -Be (@('20261031-235959', '20261101-080000', '20261101-080000-2') | ForEach-Object { "$($script:Name)-failed-$_" } | Sort-Object)
    }

    It 'deletes nothing while there are no more copies than Keep, and nothing when there are none' {
        $root = New-TempRoot
        $results = Join-Path $root $script:Name
        @(Remove-OldFailedTestResults -Results $results -TempRoot $root).Count | Should -Be 0
        $null = New-ResultsFolder "$results-failed-20261101-080000"
        @(Remove-OldFailedTestResults -Results $results -TempRoot $root -Keep 1).Count | Should -Be 0
        Get-Names $root | Should -Be @("$($script:Name)-failed-20261101-080000")
    }

    It 'leaves everything that is not a kept copy of exactly this results folder' {
        $root = New-TempRoot
        $results = Join-Path $root $script:Name
        $foreign = @(
            $script:Name                                                    # the results folder itself
            "$($script:Name)-aot-failed-20200101-000000"                    # a copy of the other pass
            'LayerPeek.App.E2E.Results.ffffffff-failed-20200101-000000'     # a copy of another worktree
            "$($script:Name)-failed-20200101-000000-x"                      # another suffix
            "$($script:Name)-failed-2020-01-01"                             # another time stamp
            "$($script:Name)-failed-20200101-000000.bak"
        )
        foreach ($name in $foreign) { $null = New-ResultsFolder (Join-Path $root $name) }
        $null = New-ResultsFolder (Join-Path $root "$($script:Name)-failed-20200101-000001") -NoMarker
        [System.IO.File]::WriteAllText((Join-Path $root "$($script:Name)-failed-20200101-000002"), 'a file')
        $before = Get-Names $root

        @(Remove-OldFailedTestResults -Results $results -TempRoot $root -Keep 0).Count | Should -Be 0

        Get-Names $root | Should -Be $before
    }

    It 'warns about a copy with a locked file, keeps its marker for the next run and still deletes the older copies' -Skip:(-not $IsWindows) {
        $root = New-TempRoot
        $results = Join-Path $root $script:Name
        $older = New-ResultsFolder "$results-failed-20200101-000000"
        $locked = New-ResultsFolder "$results-failed-20200101-000001"
        $lock = [System.IO.File]::Open((Join-Path $locked 'shot.png'), [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::None)
        try {
            $deleted = @(Remove-OldFailedTestResults -Results $results -TempRoot $root -Keep 0 -WarningVariable warnings -WarningAction SilentlyContinue)
        }
        finally {
            $lock.Dispose()
        }

        $deleted | Should -Be @($older)
        @($warnings).Count | Should -Be 1
        Test-Path -LiteralPath (Join-Path $locked $script:Marker) | Should -BeTrue
        @(Remove-OldFailedTestResults -Results $results -TempRoot $root -Keep 0) | Should -Be @($locked)
        Get-Names $root | Should -BeNullOrEmpty
    }

    It 'removes a link inside an old copy without touching its target, and never deletes a linked copy' -Skip:(-not $IsWindows) {
        $root = New-TempRoot
        $results = Join-Path $root $script:Name
        $target = New-ResultsFolder (Join-Path $root 'target')
        $old = New-ResultsFolder "$results-failed-20200101-000000"
        $null = New-Item -ItemType Junction -Path (Join-Path $old 'outside') -Target $target
        $linked = "$results-failed-20200101-000001"
        $null = New-Item -ItemType Junction -Path $linked -Target $target

        $deleted = @(Remove-OldFailedTestResults -Results $results -TempRoot $root -Keep 0)

        $deleted | Should -Be @($old)
        Test-Path -LiteralPath (Join-Path $target 'shot.png') | Should -BeTrue
        Test-Path -LiteralPath $linked | Should -BeTrue
        Remove-Item -LiteralPath $linked -Force
    }
}

Describe 'run-e2e.ps1' {
    It 'keeps the results of a failed run after the restore and prints the kept copy in the summary' {
        $script = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'run-e2e.ps1'))
        $restore = $script.IndexOf("Invoke-SystemChangeRestore 'after the run'", [StringComparison]::Ordinal)
        $keep = $script.IndexOf('Get-PassesToKeep -Passes $passes.ToArray() -ExitCode $exitCode', [StringComparison]::Ordinal)
        $restore | Should -BeGreaterThan 0
        $keep | Should -BeGreaterThan $restore
        $script | Should -Match ([regex]::Escape('Save-FailedTestResults -Results $pass.Results'))
        $script | Should -Match ([regex]::Escape('foreach ($folder in @($results, $resultsAot))'))
        $script | Should -Match ([regex]::Escape('Remove-OldFailedTestResults -Results $folder'))
        $script | Should -Match ([regex]::Escape('kept copy of the failed run'))
    }
}
