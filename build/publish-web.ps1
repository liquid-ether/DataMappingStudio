<#
.SYNOPSIS
  Publishes the Blazor Server web host as a self-contained folder (no .NET install required on the
  server). Run it with publish/web/App.Web.exe. Output: publish/web
.EXAMPLE
  ./build/publish-web.ps1
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\publish\web'),
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot '..\src\App.Web\App.Web.csproj'
dotnet publish $project -c Release -r $Runtime --self-contained true -o $Output

Write-Host ""
Write-Host "Published self-contained web host to: $Output"
Write-Host "Run it with: $Output\App.Web.exe --urls http://localhost:5000"
