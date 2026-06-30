<#
.SYNOPSIS
  Supply-chain gate: fails if any project references a NuGet package (including transitive) with a
  known vulnerability, and reports deprecated packages as a warning. Run in CI and before a release.
.EXAMPLE
  ./build/check-dependencies.ps1
#>
param(
    [string]$Solution = (Join-Path $PSScriptRoot '..' 'DataMappingStudio.slnx')
)
$ErrorActionPreference = 'Stop'

dotnet restore $Solution | Out-Null

Write-Host '== Vulnerable packages (including transitive) ==' -ForegroundColor Cyan
$vuln = dotnet list $Solution package --vulnerable --include-transitive 2>&1
$vuln | ForEach-Object { Write-Host $_ }
if ($vuln | Select-String -Pattern 'has the following vulnerable packages' -Quiet) {
    throw 'Vulnerable package(s) found (see list above). Update or pin the offending package(s).'
}

Write-Host "`n== Deprecated packages (warning only) ==" -ForegroundColor Cyan
$deprecated = dotnet list $Solution package --deprecated --include-transitive 2>&1
$deprecated | ForEach-Object { Write-Host $_ }
if ($deprecated | Select-String -Pattern 'has the following deprecated packages' -Quiet) {
    Write-Warning 'Deprecated package(s) referenced — plan to replace them.'
}

Write-Host "`nDependency scan passed: no known-vulnerable packages." -ForegroundColor Green
