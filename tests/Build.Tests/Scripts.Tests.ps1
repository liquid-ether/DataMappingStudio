# Pester tests for the PowerShell build scripts (Architecture §14). Run: Invoke-Pester tests/Build.Tests
$buildDir = Join-Path $PSScriptRoot '..\..\build'

Describe 'provision.ps1' {
    It 'creates the _changes and _meta folders' {
        $remote = Join-Path $TestDrive 'remote'
        & (Join-Path $buildDir 'provision.ps1') -RemoteFolder $remote
        Join-Path $remote '_changes' | Should -Exist
        Join-Path $remote '_meta' | Should -Exist
    }
}

Describe 'import-mapping.json' {
    BeforeAll {
        $script:map = Get-Content (Join-Path $buildDir 'import-mapping.json') -Raw | ConvertFrom-Json
    }

    It 'is valid JSON with a worksheets array' {
        $map.worksheets | Should -Not -BeNullOrEmpty
    }

    It 'maps the core entity tables' {
        $tables = $map.worksheets.table
        $tables | Should -Contain 'application'
        $tables | Should -Contain 'data_source'
        $tables | Should -Contain 'mapping'
    }

    It 'resolves the data_source -> application reference by app_code' {
        $source = $map.worksheets | Where-Object { $_.table -eq 'data_source' }
        $source.references.application_id.table | Should -Be 'application'
        $source.references.application_id.by | Should -Be 'app_code'
    }
}

Describe 'build scripts' {
    It 'all expected scripts exist' {
        foreach ($s in 'provision.ps1', 'import.ps1', 'rebuild-snapshots.ps1', 'convert-format.ps1') {
            Join-Path $buildDir $s | Should -Exist
        }
    }
}
