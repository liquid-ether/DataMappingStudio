<#
.SYNOPSIS
  Startup smoke test for the packaged desktop app. Launches MappingStudio.exe, waits, and verifies the
  process is still alive (i.e. it got past WPF/WebView2 initialization without crashing — which is where
  startup failures such as the missing-WinRT-projection error surface). Requires an interactive Windows
  desktop session (a window will briefly appear). Exit code 0 = pass, 1 = fail.
.EXAMPLE
  ./build/smoke-desktop.ps1
#>
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\publish\desktop\MappingStudio.exe'),
    [int]$Seconds = 8
)
$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Exe)) {
    throw "Executable not found: $Exe. Run ./build/publish-desktop.ps1 first."
}

Write-Host "Launching $Exe ..."
$proc = Start-Process -FilePath $Exe -PassThru
$deadline = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $deadline -and -not $proc.HasExited) {
    Start-Sleep -Milliseconds 500
}

try {
    if ($proc.HasExited) {
        Write-Error "STARTUP SMOKE FAILED: the app exited after $([int]((Get-Date) - $proc.StartTime).TotalSeconds)s with exit code $($proc.ExitCode). Check Windows Event Viewer (Application log) for the .NET exception."
        exit 1
    }

    Write-Host "STARTUP SMOKE PASSED: the app is still running after $Seconds s (no startup crash)."
    exit 0
}
finally {
    # Tear down the app and any WebView2 child processes it spawned.
    & taskkill /PID $proc.Id /T /F *> $null
}
