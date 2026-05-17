[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [switch]$Clean,
    [switch]$Publish,
    [switch]$Zip,
    [switch]$BuildInstaller
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Info([string]$Message) { Write-Host "[INFO] $Message" -ForegroundColor Cyan }
function Write-Ok([string]$Message) { Write-Host "[OK]   $Message" -ForegroundColor Green }
function Fail([string]$Message) { Write-Host "[FAIL] $Message" -ForegroundColor Red; exit 1 }

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
