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

    $Fallback = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools'
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
        Fail 'Visual Studio 2022 Build Tools or Visual Studio 2022 is required to build this WinUI 3 project.'
    }

    if ([string]::IsNullOrWhiteSpace($MsBuildPath) -or -not (Test-Path $MsBuildPath)) {
        Fail "MSBuild.exe was not found in the Visual Studio installation: $VsInstallPath"
    }

    Write-Info "Visual Studio installation detected at: $VsInstallPath"
    Write-Info "Using MSBuild: $MsBuildPath"

    $VsPriTask = Join-Path $VsInstallPath 'MSBuild\Microsoft\VisualStudio\v17.0\AppxPackage\Microsoft.Build.Packaging.Pri.Tasks.dll'
    $DotNetPriTask = Get-ChildItem -Path (Join-Path $env:ProgramFiles 'dotnet\sdk') -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName 'Microsoft\VisualStudio\v17.0\AppxPackage\Microsoft.Build.Packaging.Pri.Tasks.dll' } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1

    if ((Test-Path $VsPriTask) -or $DotNetPriTask) {
        Write-Ok 'WinUI PRI/Appx build tasks found.'
        return
    }

    $SetupExe = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\setup.exe'
    $ModifyCommand = '"{0}" modify --installPath "{1}" --add Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools --add Microsoft.VisualStudio.Workload.UniversalBuildTools --add Microsoft.VisualStudio.Component.Windows10SDK.19041 --includeRecommended --passive --norestart' -f $SetupExe, $VsInstallPath

    Fail @"
WinUI 3 build toolchain is incomplete.

The Windows App SDK build needs Microsoft.Build.Packaging.Pri.Tasks.dll. Your Visual Studio/Build Tools installation exists, but the WinUI/UWP packaging workload is still missing.

Run PowerShell as Administrator, close Visual Studio/Build Tools/VS Installer if open, then run:

$ModifyCommand

Alternatively open Visual Studio Installer > Build Tools 2022 > Modify and add:
- .NET desktop build tools
- Universal Windows Platform build tools / Windows app packaging tools
- Windows 10 SDK 10.0.19041.0 or later

After installation, close and reopen PowerShell, then rerun:
.\Build-WindowsOptimizer-WinUI.ps1 -Clean -Publish -Zip
"@
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
    if (-not (Test-Path $ProjectPath)) { Fail "Project file not found: $ProjectPath" }
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Fail 'dotnet is not available on PATH. Install the .NET 8 SDK and retry.' }

    $VsInstallPath = Get-VsInstallPath
    $MsBuildPath = if ($VsInstallPath) { Get-MsBuildPath $VsInstallPath } else { $null }
    $Platform = Get-PlatformFromRuntime $Runtime

    Write-Info "Repository root: $RepoRoot"
    Write-Info "Project: $ProjectPath"
    Write-Info "Configuration: $Configuration"
    Write-Info "Runtime: $Runtime"
    Write-Info "Platform: $Platform"

    Test-WinUiBuildToolchain -VsInstallPath $VsInstallPath -MsBuildPath $MsBuildPath

    $CommonProps = @(
        "/p:Configuration=$Configuration",
        "/p:Platform=$Platform",
        "/p:RuntimeIdentifier=$Runtime",
        '/p:WindowsPackageType=None',
        '/p:WindowsAppSDKSelfContained=true',
        '/m'
    )

    if ($Clean) {
        Invoke-MSBuild -MsBuildPath $MsBuildPath -Arguments @($ProjectPath, '/t:Clean') + $CommonProps
    }

    Invoke-MSBuild -MsBuildPath $MsBuildPath -Arguments @($ProjectPath, '/t:Restore') + $CommonProps
    Invoke-MSBuild -MsBuildPath $MsBuildPath -Arguments @($ProjectPath, '/t:Build') + $CommonProps

    if ($Publish -or $BuildInstaller -or $Zip) {
        $PublishDir = Join-Path $RepoRoot ("publish\winui\$Runtime\$Configuration")
        if (Test-Path $PublishDir) {
            Write-Info "Removing existing publish directory: $PublishDir"
            Remove-Item -Path $PublishDir -Recurse -Force
        }
        New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null

        Invoke-MSBuild -MsBuildPath $MsBuildPath -Arguments @(
            $ProjectPath,
            '/t:Publish',
            "/p:PublishDir=$PublishDir\",
            '/p:SelfContained=true'
        ) + $CommonProps

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
