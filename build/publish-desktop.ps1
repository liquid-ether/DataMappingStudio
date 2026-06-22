<#
.SYNOPSIS
  Publishes the desktop app as a single self-contained Windows .exe — no .NET install, no admin
  required to run it (the WebView2 runtime ships with Windows 10/11). Output: publish/desktop/MappingStudio.exe
.EXAMPLE
  ./build/publish-desktop.ps1
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\publish\desktop'),
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot '..\src\App.Desktop\App.Desktop.csproj'
dotnet publish $project -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o $Output

# Prune the non-runtime leftovers (XML doc files) so the distributable is a single .exe.
Get-ChildItem $Output -Recurse -File | Where-Object { $_.Extension -ne '.exe' } | Remove-Item -Force
Get-ChildItem $Output -Recurse -Directory | Sort-Object FullName -Descending | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Published single-file desktop app to: $Output"
Get-ChildItem $Output -Filter *.exe | ForEach-Object {
    Write-Host (" - {0} ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB))
}
