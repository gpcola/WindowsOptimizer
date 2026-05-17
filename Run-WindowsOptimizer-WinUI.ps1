param(
    [string]$ExecutablePath = ".\publish\winui\win-x64\Release\WindowsOptimizer.WinUI.exe"
)

$ErrorActionPreference = 'Continue'

Write-Host '=== Windows Optimizer WinUI Diagnostic Launcher ===' -ForegroundColor Cyan
Write-Host "Executable: $ExecutablePath"

if (-not (Test-Path $ExecutablePath)) {
    Write-Host 'Executable not found.' -ForegroundColor Red
    exit 1
}

$ResolvedExe = Resolve-Path $ExecutablePath
$ExeDir = Split-Path $ResolvedExe -Parent

Write-Host "Resolved executable: $ResolvedExe"
Write-Host "Working directory: $ExeDir"

Push-Location $ExeDir

try {
    Write-Host 'Launching application...' -ForegroundColor Yellow

    $Process = Start-Process -FilePath $ResolvedExe -PassThru -WorkingDirectory $ExeDir

    Start-Sleep -Seconds 5

    if ($Process.HasExited) {
        Write-Host "Process exited immediately with code $($Process.ExitCode)" -ForegroundColor Red

        $LogPath = Join-Path $env:LOCALAPPDATA '1LG Digital\Windows Optimizer\startup-error.log'

        if (Test-Path $LogPath) {
            Write-Host ''
            Write-Host '=== Startup Error Log ===' -ForegroundColor Red
            Get-Content $LogPath -Tail 200
        }
        else {
            Write-Host 'No startup log found.' -ForegroundColor Yellow
        }
    }
    else {
        Write-Host 'Application appears to be running.' -ForegroundColor Green
    }
}
finally {
    Pop-Location
}
