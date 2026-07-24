# Pester tests for the PowerShell build scripts (Architecture §14). Run: Invoke-Pester tests/Build.Tests
# Requires Pester 5+. Setup lives in BeforeAll: top-level script code runs only during Pester's
# DISCOVERY phase, so a plain top-level variable would be null when the tests actually execute.
BeforeAll {
    $script:buildDir = Join-Path $PSScriptRoot '..\..\build'
}

Describe 'provision.ps1' {
    It 'creates the _changes and _meta folders' {
        $remote = Join-Path $TestDrive 'remote'
        & (Join-Path $buildDir 'provision.ps1') -RemoteFolder $remote
        Join-Path $remote '_changes' | Should -Exist
        Join-Path $remote '_meta' | Should -Exist
    }
}

# Mapping files are gone: the CLI runs mappings saved from the app's Data Import wizard
# (shared folder _meta/import-mappings); the built-in template lives in DefaultImportMapping.cs.

Describe 'import.ps1' {
    It 'requires a saved-mapping name' {
        (Get-Command (Join-Path $buildDir 'import.ps1')).Parameters['Mapping'].Attributes.Mandatory | Should -Contain $true
    }
}

Describe 'build scripts' {
    It 'all expected scripts exist' {
        foreach ($s in 'provision.ps1', 'import.ps1', 'rebuild-snapshots.ps1', 'convert-format.ps1') {
            Join-Path $buildDir $s | Should -Exist
        }
    }
}
