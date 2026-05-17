[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('win-x64', 'win-arm64', 'win-x86')]
    [string]$Runtime = 'win-x64',

    [switch]$Publish,
    [switch]$SelfContained,
    [switch]$SingleFile,
    [switch]$Clean,
    [switch]$Zip
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Info([string]$Message) {
    Write-Host "[INFO] $Message" -ForegroundColor Cyan
}

function Write-Ok([string]$Message) {
    Write-Host "[OK]   $Message" -ForegroundColor Green
}

function Fail([string]$Message) {
    Write-Host "[FAIL] $Message" -ForegroundColor Red
    exit 1
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    Write-Info ("dotnet " + ($Arguments -join ' '))
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        Fail "dotnet command failed with exit code $LASTEXITCODE"
    }
}

try {
    $ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
    $RepoRoot = Resolve-Path (Join-Path $ScriptRoot '.')
    Set-Location $RepoRoot

    $ProjectPath = Join-Path $RepoRoot 'WindowsOptimizer.csproj'
    if (-not (Test-Path $ProjectPath)) {
        Fail "Project file not found: $ProjectPath"
    }

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Fail 'dotnet is not available on PATH. Install the .NET 8 SDK and retry.'
    }

    Write-Info "Repository root: $RepoRoot"
    Write-Info "Project: $ProjectPath"
    Write-Info "Configuration: $Configuration"
    Write-Info "Runtime: $Runtime"

    if ($Clean) {
        Invoke-DotNet -Arguments @('clean', $ProjectPath, '-c', $Configuration)
    }

    Invoke-DotNet -Arguments @('restore', $ProjectPath)
    Invoke-DotNet -Arguments @('build', $ProjectPath, '-c', $Configuration, '--no-restore')

    if ($Publish) {
        $PublishRelative = Join-Path (Join-Path 'publish' $Runtime) $Configuration
        $PublishDir = Join-Path $RepoRoot $PublishRelative

        if (Test-Path $PublishDir) {
            Write-Info "Removing existing publish directory: $PublishDir"
            Remove-Item -Path $PublishDir -Recurse -Force
        }

        $PublishArgs = @(
            'publish',
            $ProjectPath,
            '-c', $Configuration,
            '-r', $Runtime,
            '--self-contained', $(if ($SelfContained) { 'true' } else { 'false' }),
            '-o', $PublishDir,
            '--no-build'
        )

        if ($SingleFile) {
            $PublishArgs += '/p:PublishSingleFile=true'
            $PublishArgs += '/p:IncludeNativeLibrariesForSelfExtract=true'
        }

        Invoke-DotNet -Arguments $PublishArgs

        $ExePath = Join-Path $PublishDir 'WindowsOptimizer.exe'
        if (-not (Test-Path $ExePath)) {
            Fail "Publish output validation failed: missing executable at $ExePath"
        }

        $ExeItem = Get-Item $ExePath
        if ($ExeItem.Length -le 0) {
            Fail "Publish output validation failed: executable has zero bytes at $ExePath"
        }

        Write-Ok "Publish output validated: $ExePath ($($ExeItem.Length) bytes)"

        $DoZip = $Zip.IsPresent -or $Publish.IsPresent
        if ($DoZip) {
            $ArtifactsDir = Join-Path $RepoRoot 'artifacts\releases'
            if (-not (Test-Path $ArtifactsDir)) {
                New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null
            }

            $PackageKind = if ($SelfContained) { 'self-contained' } else { 'framework-dependent' }
            $ZipName = "WindowsOptimizer-$Runtime-$Configuration-$PackageKind.zip"
            $ZipPath = Join-Path $ArtifactsDir $ZipName

            if (Test-Path $ZipPath) {
                Remove-Item -Path $ZipPath -Force
            }

            Compress-Archive -Path (Join-Path $PublishDir '*') -DestinationPath $ZipPath -CompressionLevel Optimal

            $ChecksumPath = "$ZipPath.sha256"
            $Hash = Get-FileHash -Algorithm SHA256 -Path $ZipPath
            "{0} *{1}" -f $Hash.Hash, (Split-Path -Leaf $ZipPath) | Set-Content -Path $ChecksumPath -Encoding ascii

            Write-Ok "Release ZIP created: $ZipPath"
            Write-Ok "SHA256 file created: $ChecksumPath"
        }

        Write-Host ''
        Write-Host 'Final publish outputs:' -ForegroundColor White
        Write-Host "  Publish folder: $PublishDir" -ForegroundColor Gray
        if ($DoZip) {
            Write-Host "  Artifact folder: $(Join-Path $RepoRoot 'artifacts\releases')" -ForegroundColor Gray
        }
    }

    Write-Host ''
    Write-Ok 'Finished successfully.'
}
catch {
    Fail $_.Exception.Message
}
