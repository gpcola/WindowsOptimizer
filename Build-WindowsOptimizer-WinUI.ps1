[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [switch]$Clean,
    [switch]$Publish,
    [switch]$Zip,
    [switch]$BuildInstaller,
    [switch]$SkipWinUiToolchainCheck
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Info([string]$Message) { Write-Host "[INFO] $Message" -ForegroundColor Cyan }
function Write-Ok([string]$Message) { Write-Host "[OK]   $Message" -ForegroundColor Green }
function Fail([string]$Message) { Write-Host "[FAIL] $Message" -ForegroundColor Red; exit 1 }

function Test-WinUiBuildToolchain {
    if ($SkipWinUiToolchainCheck) {
        Write-Info 'Skipping WinUI build-toolchain preflight.'
        return
    }

    $SdkRoot = Join-Path $env:ProgramFiles 'dotnet\sdk'
    $Sdk = Get-ChildItem -Path $SdkRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName 'Microsoft\VisualStudio\v17.0\AppxPackage\Microsoft.Build.Packaging.Pri.Tasks.dll') } |
        Sort-Object Name -Descending |
        Select-Object -First 1

    if ($Sdk) {
        Write-Ok "WinUI PRI/Appx build tasks found in .NET SDK $($Sdk.Name)."
        return
    }

    $VsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $VsInstall = $null
    if (Test-Path $VsWhere) {
        $VsInstall = & $VsWhere -latest -products * -property installationPath 2>$null
    }

    if (-not [string]::IsNullOrWhiteSpace($VsInstall)) {
        Write-Info "Visual Studio installation detected at: $VsInstall"
    }

    $SetupExe = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\setup.exe'
    $DetectedInstallPath = if (-not [string]::IsNullOrWhiteSpace($VsInstall)) { $VsInstall } else { 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools' }
    $ModifyCommand = '"{0}" modify --installPath "{1}" --add Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools --add Microsoft.VisualStudio.Workload.UniversalBuildTools --add Microsoft.VisualStudio.Component.Windows10SDK.19041 --includeRecommended --passive --norestart' -f $SetupExe, $DetectedInstallPath

    Fail @"
WinUI 3 build toolchain is incomplete.

The Windows App SDK build is looking for Microsoft.Build.Packaging.Pri.Tasks.dll, which is normally supplied by the Visual Studio Build Tools WinUI/UWP application build workload. The plain .NET SDK alone is not enough for this WinUI 3 project.

A Visual Studio Build Tools instance was detected, so do not keep trying to install Visual Studio Community. Modify the existing Build Tools instance instead.

Run PowerShell as Administrator, close Visual Studio/Build Tools/VS Installer if open, then run this command:

$ModifyCommand

Alternatively open Visual Studio Installer > Build Tools 2022 > Modify and add:
- .NET desktop build tools
- WinUI application development build tools
- Windows 10 SDK 10.0.19041.0 or later

After installation, close and reopen PowerShell, then rerun:
.\Build-WindowsOptimizer-WinUI.ps1 -Clean -Publish -Zip
"@
}

function Invoke-DotNet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    Write-Info ("dotnet " + ($Arguments -join ' '))
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { Fail "dotnet command failed with exit code $LASTEXITCODE" }
}

try {
    $ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
    $RepoRoot = Resolve-Path (Join-Path $ScriptRoot '.')
    Set-Location $RepoRoot

    $ProjectPath = Join-Path $RepoRoot 'WindowsOptimizer.WinUI\WindowsOptimizer.WinUI.csproj'
    if (-not (Test-Path $ProjectPath)) { Fail "Project file not found: $ProjectPath" }
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Fail 'dotnet is not available on PATH. Install the .NET 8 SDK and retry.' }

    Write-Info "Repository root: $RepoRoot"
    Write-Info "Project: $ProjectPath"
    Write-Info "Configuration: $Configuration"
    Write-Info "Runtime: $Runtime"

    Test-WinUiBuildToolchain

    if ($Clean) { Invoke-DotNet -Arguments @('clean', $ProjectPath, '-c', $Configuration) }

    Invoke-DotNet -Arguments @('restore', $ProjectPath)
    Invoke-DotNet -Arguments @('build', $ProjectPath, '-c', $Configuration, '-r', $Runtime, '--no-restore')

    if ($Publish -or $BuildInstaller -or $Zip) {
        $PublishDir = Join-Path $RepoRoot ("publish\winui\$Runtime\$Configuration")
        if (Test-Path $PublishDir) {
            Write-Info "Removing existing publish directory: $PublishDir"
            Remove-Item -Path $PublishDir -Recurse -Force
        }

        Invoke-DotNet -Arguments @(
            'publish',
            $ProjectPath,
            '-c', $Configuration,
            '-r', $Runtime,
            '--self-contained', 'true',
            '-o', $PublishDir,
            '--no-build'
        )

        $ExePath = Join-Path $PublishDir 'WindowsOptimizer.WinUI.exe'
        if (-not (Test-Path $ExePath)) { Fail "Publish output validation failed: missing executable at $ExePath" }
        if ((Get-Item $ExePath).Length -le 0) { Fail "Publish output validation failed: executable has zero bytes at $ExePath" }
        Write-Ok "Publish output validated: $ExePath"

        if ($Zip) {
            $ArtifactsDir = Join-Path $RepoRoot 'artifacts\releases'
            New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null
            $ZipPath = Join-Path $ArtifactsDir "WindowsOptimizer-WinUI-$Runtime-$Configuration.zip"
            if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
            Compress-Archive -Path (Join-Path $PublishDir '*') -DestinationPath $ZipPath -CompressionLevel Optimal
            $Hash = Get-FileHash -Algorithm SHA256 -Path $ZipPath
            "{0} *{1}" -f $Hash.Hash, (Split-Path -Leaf $ZipPath) | Set-Content -Path "$ZipPath.sha256" -Encoding ascii
            Write-Ok "Release ZIP created: $ZipPath"
        }

        if ($BuildInstaller) {
            $Iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
            if (-not $Iscc) { Fail 'Inno Setup is not installed or iscc.exe is not on PATH. Install Inno Setup 6 to build the setup EXE.' }
            $IssPath = Join-Path $RepoRoot 'installer\WindowsOptimizer.WinUI.iss'
            & $Iscc.Source "/DSourceDir=$PublishDir" $IssPath
            if ($LASTEXITCODE -ne 0) { Fail "Inno Setup failed with exit code $LASTEXITCODE" }
            Write-Ok 'Installer build completed.'
        }
    }

    Write-Ok 'Finished successfully.'
}
catch {
    Fail $_.Exception.Message
}
