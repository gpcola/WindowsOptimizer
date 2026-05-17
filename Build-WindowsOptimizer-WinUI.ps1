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

function Get-VsInstallPath {
    $VsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $VsWhere) {
        $Path = & $VsWhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath 2>$null
        if (-not [string]::IsNullOrWhiteSpace($Path)) { return $Path }

        $Path = & $VsWhere -latest -products * -property installationPath 2>$null
        if (-not [string]::IsNullOrWhiteSpace($Path)) { return $Path }
    }

    $Fallback = 'C:\Program Files\Microsoft Visual Studio\2022\Community'
    if (Test-Path $Fallback) { return $Fallback }

    return $null
}

function Get-MsBuildPath([string]$VsInstallPath) {
    $Candidates = @(
        (Join-Path $VsInstallPath 'MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path $VsInstallPath 'MSBuild\Current\Bin\amd64\MSBuild.exe')
    )

    foreach ($Candidate in $Candidates) {
        if (Test-Path $Candidate) { return $Candidate }
    }

    return $null
}

function Get-PlatformFromRuntime([string]$Rid) {
    if ($Rid -eq 'win-arm64') { return 'arm64' }
    return 'x64'
}

function Test-WinUiBuildToolchain([string]$VsInstallPath, [string]$MsBuildPath) {
    if ($SkipWinUiToolchainCheck) {
        Write-Info 'Skipping WinUI build-toolchain preflight.'
        return
    }

    if ([string]::IsNullOrWhiteSpace($VsInstallPath) -or -not (Test-Path $VsInstallPath)) {
        Fail 'Visual Studio 2022 is required to build this WinUI 3 project.'
    }

    if ([string]::IsNullOrWhiteSpace($MsBuildPath) -or -not (Test-Path $MsBuildPath)) {
        Fail "MSBuild.exe was not found in the Visual Studio installation: $VsInstallPath"
    }

    Write-Info "Visual Studio installation detected at: $VsInstallPath"
    Write-Info "Using MSBuild: $MsBuildPath"

    $VsPriTask = Join-Path $VsInstallPath 'MSBuild\Microsoft\VisualStudio\v17.0\AppxPackage\Microsoft.Build.Packaging.Pri.Tasks.dll'

    if (Test-Path $VsPriTask) {
        Write-Ok 'WinUI PRI/Appx build tasks found.'
        return
    }

    Fail "WinUI build tools still missing: $VsPriTask"
}

function Invoke-MSBuild {
    param(
        [Parameter(Mandatory = $true)][string]$MsBuildPath,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    Write-Info ("MSBuild " + ($Arguments -join ' '))
    & $MsBuildPath @Arguments
    if ($LASTEXITCODE -ne 0) { Fail "MSBuild failed with exit code $LASTEXITCODE" }
}

try {
    $ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
    $RepoRoot = Resolve-Path (Join-Path $ScriptRoot '.')
    Set-Location $RepoRoot

    $ProjectPath = Join-Path $RepoRoot 'WindowsOptimizer.WinUI\WindowsOptimizer.WinUI.csproj'

    $VsInstallPath = Get-VsInstallPath
    $MsBuildPath = if ($VsInstallPath) { Get-MsBuildPath $VsInstallPath } else { $null }
    $Platform = Get-PlatformFromRuntime $Runtime

    $CommonProps = @(
        "/p:Configuration=$Configuration",
        "/p:Platform=$Platform",
        "/p:RuntimeIdentifier=$Runtime",
        '/p:WindowsPackageType=None',
        '/p:WindowsAppSDKSelfContained=true',
        '/m'
    )

    Test-WinUiBuildToolchain -VsInstallPath $VsInstallPath -MsBuildPath $MsBuildPath

    if ($Clean) {
        Invoke-MSBuild -MsBuildPath $MsBuildPath -Arguments (@($ProjectPath, '/t:Clean') + $CommonProps)
    }

    Invoke-MSBuild -MsBuildPath $MsBuildPath -Arguments (@($ProjectPath, '/t:Restore') + $CommonProps)
    Invoke-MSBuild -MsBuildPath $MsBuildPath -Arguments (@($ProjectPath, '/t:Build') + $CommonProps)

    if ($Publish -or $Zip) {
        $PublishDir = Join-Path $RepoRoot ("publish\winui\$Runtime\$Configuration")

        if (Test-Path $PublishDir) {
            Remove-Item $PublishDir -Recurse -Force
        }

        New-Item -ItemType Directory -Path $PublishDir | Out-Null

        Invoke-MSBuild -MsBuildPath $MsBuildPath -Arguments (@(
            $ProjectPath,
            '/t:Publish',
            "/p:PublishDir=$PublishDir\",
            '/p:SelfContained=true'
        ) + $CommonProps)

        if ($Zip) {
            $ZipPath = Join-Path $RepoRoot "WindowsOptimizer.zip"
            if (Test-Path $ZipPath) { Remove-Item $ZipPath }
            Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipPath
            Write-Ok "ZIP created: $ZipPath"
        }
    }

    Write-Ok 'Build completed successfully.'
}
catch {
    Fail $_.Exception.Message
}
