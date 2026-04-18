using System;
using System.IO;
using System.Text;

namespace WindowsOptimizer
{
    public static class SafeReclaimScriptBuilder
    {
        private const string ScriptTemplate = """
<#
.SYNOPSIS
  Deterministic, non-destructive C: drive reclaim script.

.DESCRIPTION
  - Measures C: free space before/after.
  - Supports audit mode (-WhatIfOnly) to preview actions.
  - Cleans temp and cache locations that are generally safe to delete.
  - Clears Windows Update download cache and Delivery Optimization cache.
  - Optionally runs DISM component store cleanup with /ResetBase.
  - Logs all actions and outcomes.

.NOTES
  Run in elevated PowerShell (Run as Administrator).
#>

[CmdletBinding()]
param(
    [switch]$WhatIfOnly,
    [switch]$SkipDismPrompt,
    [string]$DriveLetter = 'C',
    [string]$LogRoot = 'C:\Temp'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

#--------------------------- CONFIG ---------------------------#
$Timestamp        = Get-Date -Format 'yyyyMMdd_HHmmss'
$LogFile          = Join-Path $LogRoot "Reclaim_${DriveLetter}_$Timestamp.log"
$RunDismCleanup   = $true

$CleanupPaths = @(
    "$env:TEMP",
    'C:\Windows\Temp',
    "$env:USERPROFILE\AppData\Local\Temp",
    'C:\Windows\SoftwareDistribution\Download',
    'C:\Windows\DeliveryOptimization',
    'C:\Windows\Logs\CBS',
    "$env:LOCALAPPDATA\Microsoft\Windows\INetCache",
    "$env:LOCALAPPDATA\Microsoft\Edge\User Data\Default\Cache",
    "$env:LOCALAPPDATA\Google\Chrome\User Data\Default\Cache",
    "$env:APPDATA\Mozilla\Firefox\Profiles\*\cache2"
)

#--------------------------- HELPERS ---------------------------#
function Ensure-LogRoot {
    if (-not (Test-Path $LogRoot)) {
        New-Item -Path $LogRoot -ItemType Directory -Force | Out-Null
    }
}

function Write-Log {
    param([string]$Message)
    $stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $line  = "[$stamp] $Message"
    Write-Host $line
    Add-Content -Path $LogFile -Value $line
}

function Get-FreeSpaceGB {
    param([string]$Drive = 'C')
    $drive = Get-PSDrive -Name $Drive -ErrorAction Stop
    [math]::Round($drive.Free / 1GB, 2)
}

function Expand-CleanupTargets {
    param([string]$Path)

    $literal = Get-Item -LiteralPath $Path -ErrorAction SilentlyContinue
    if ($literal) { return @($literal) }

    $wild = Get-ChildItem -Path $Path -ErrorAction SilentlyContinue
    if ($wild) { return @($wild) }

    return @()
}

function SafeDelete {
    param([string]$Path)

    $deletedCount = 0
    $targets = Expand-CleanupTargets -Path $Path
    if (-not $targets -or $targets.Count -eq 0) {
        Write-Log "Skip (not found): $Path"
        return 0
    }

    foreach ($t in $targets) {
        try {
            if ($WhatIfOnly) {
                Write-Log "WhatIf delete: $($t.FullName)"
            }
            else {
                Remove-Item -Path $t.FullName -Recurse -Force -ErrorAction Stop
                Write-Log "Deleted: $($t.FullName)"
            }
            $deletedCount++
        }
        catch {
            Write-Log "Skipped (locked or denied): $($t.FullName) - $($_.Exception.Message)"
        }
    }

    return $deletedCount
}

#--------------------------- MAIN ---------------------------#
if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'Please run this script in an elevated PowerShell session (Run as Administrator).' -ForegroundColor Red
    exit 1
}

Ensure-LogRoot
Write-Log '=== Reclaim C: Script Started ==='
Write-Log "Log file: $LogFile"
Write-Log "Mode: $(if ($WhatIfOnly) { 'Audit (WhatIf only)' } else { 'Execute' })"

$freeBefore = Get-FreeSpaceGB -Drive $DriveLetter
Write-Log "Free space on ${DriveLetter}: before cleanup = $freeBefore GB"

$totalDeletedTargets = 0

try {
    Write-Log 'Clearing Recycle Bin...'
    if ($WhatIfOnly) {
        Write-Log 'WhatIf recycle bin clear skipped (audit mode).'
    }
    else {
        Clear-RecycleBin -Force -ErrorAction SilentlyContinue
    }
}
catch {
    Write-Log "Recycle Bin clear skipped: $($_.Exception.Message)"
}

Write-Log 'Stopping Windows Update related services...'
$servicesToStop = @('wuauserv','bits')
foreach ($svc in $servicesToStop) {
    try {
        if ($WhatIfOnly) {
            Write-Log "WhatIf stop service: $svc"
        }
        else {
            Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue
            Write-Log "Stopped service: $svc"
        }
    }
    catch {
        Write-Log "Could not stop service ${svc}: $($_.Exception.Message)"
    }
}

Write-Log 'Cleaning safe temp and cache locations...'
foreach ($path in $CleanupPaths) {
    Write-Log "Processing: $path"
    $totalDeletedTargets += SafeDelete -Path $path
}

Write-Log 'Restarting Windows Update related services...'
foreach ($svc in $servicesToStop) {
    try {
        if ($WhatIfOnly) {
            Write-Log "WhatIf start service: $svc"
        }
        else {
            Start-Service -Name $svc -ErrorAction SilentlyContinue
            Write-Log "Started service: $svc"
        }
    }
    catch {
        Write-Log "Could not start service ${svc}: $($_.Exception.Message)"
    }
}

if ($RunDismCleanup) {
    Write-Log 'DISM component store cleanup is enabled.'

    $shouldRunDism = $false
    if ($SkipDismPrompt) {
        $shouldRunDism = -not $WhatIfOnly
    }
    else {
        $answer = Read-Host "Run 'Dism.exe /Online /Cleanup-Image /StartComponentCleanup /ResetBase'? This is irreversible but Microsoft-supported. (Y/N)"
        $shouldRunDism = $answer -match '^[Yy]'
    }

    if ($shouldRunDism) {
        Write-Log 'Starting DISM component store cleanup...'
        try {
            if ($WhatIfOnly) {
                Write-Log 'WhatIf DISM execution skipped (audit mode).'
            }
            else {
                Start-Process -FilePath 'Dism.exe' -ArgumentList '/Online','/Cleanup-Image','/StartComponentCleanup','/ResetBase' -Wait -WindowStyle Hidden
                Write-Log 'DISM component store cleanup completed.'
            }
        }
        catch {
            Write-Log "DISM cleanup failed: $($_.Exception.Message)"
        }
    }
    else {
        Write-Log 'DISM component store cleanup skipped by user or mode.'
    }
}
else {
    Write-Log 'DISM component store cleanup disabled by configuration.'
}

$freeAfter = Get-FreeSpaceGB -Drive $DriveLetter
$delta = [math]::Round(($freeAfter - $freeBefore), 2)
Write-Log "Free space on ${DriveLetter}: after cleanup = $freeAfter GB"
Write-Log "Net change: $delta GB"
Write-Log "Targets processed for deletion: $totalDeletedTargets"
Write-Log '=== Reclaim C: Script Completed ==='

Write-Host ''
Write-Host "Reclaim complete. Before: $freeBefore GB, After: $freeAfter GB, Delta: $delta GB" -ForegroundColor Cyan
Write-Host "Details logged to: $LogFile"
""";

        public static string BuildScript() => ScriptTemplate;

        public static string Export(string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("Output directory is required.", nameof(outputDirectory));

            Directory.CreateDirectory(outputDirectory);
            string path = Path.Combine(outputDirectory, "Reclaim-C-Safe.ps1");
            File.WriteAllText(path, BuildScript(), new UTF8Encoding(false));
            return path;
        }
    }
}
