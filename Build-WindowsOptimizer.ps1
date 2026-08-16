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
    [switch]$Zip,
    [switch]$Installer
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

function Resolve-InnoCompiler {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $candidates = @(
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    return $null
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

    if ($Installer -and -not $Publish) {
        Fail 'The -Installer option requires -Publish.'
    }

    if ($Installer -and $Runtime -ne 'win-x64') {
        Fail 'The installer currently targets win-x64. Use -Runtime win-x64.'
    }

    if ($Installer -and $Configuration -ne 'Release') {
        Fail 'The installer currently packages the Release configuration. Use -Configuration Release.'
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

        # Runtime-specific restore is required before publishing with -r. A normal
        # framework restore does not populate project.assets.json for runtime targets.
        Invoke-DotNet -Arguments @('restore', $ProjectPath, '-r', $Runtime)

        $PublishArgs = @(
            'publish',
            $ProjectPath,
            '-c', $Configuration,
            '-r', $Runtime,
            '--self-contained', $(if ($SelfContained) { 'true' } else { 'false' }),
            '-o', $PublishDir,
            '--no-restore'
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

        if ($Installer) {
            $InstallerScript = Join-Path $RepoRoot 'Installer\WindowsOptimizer.iss'
            if (-not (Test-Path -LiteralPath $InstallerScript)) {
                Fail "Installer script not found: $InstallerScript"
            }

            [xml]$ProjectXml = Get-Content -LiteralPath $ProjectPath -Raw
            $AppVersion = [string]$ProjectXml.Project.PropertyGroup.Version
            if ([string]::IsNullOrWhiteSpace($AppVersion)) {
                Fail 'Unable to read <Version> from WindowsOptimizer.csproj.'
            }

            $IsccPath = Resolve-InnoCompiler
            if ([string]::IsNullOrWhiteSpace($IsccPath)) {
                Fail 'Inno Setup 6 compiler (ISCC.exe) was not found. Install Inno Setup 6 and retry.'
            }

            $InstallerDir = Join-Path $RepoRoot 'artifacts\installer'
            if (Test-Path -LiteralPath $InstallerDir) {
                Remove-Item -LiteralPath $InstallerDir -Recurse -Force
            }
            New-Item -ItemType Directory -Path $InstallerDir -Force | Out-Null

            Write-Info "Building Inno Setup installer with: $IsccPath"
            & $IsccPath "/DAppVersion=$AppVersion" $InstallerScript
            if ($LASTEXITCODE -ne 0) {
                Fail "Inno Setup compiler failed with exit code $LASTEXITCODE"
            }

            $InstallerExe = Join-Path $InstallerDir 'WindowsOptimizer-Setup-win-x64.exe'
            if (-not (Test-Path -LiteralPath $InstallerExe)) {
                Fail "Installer validation failed: missing setup executable at $InstallerExe"
            }

            $InstallerItem = Get-Item -LiteralPath $InstallerExe
            if ($InstallerItem.Length -le 0) {
                Fail "Installer validation failed: setup executable has zero bytes at $InstallerExe"
            }

            $InstallerChecksumPath = "$InstallerExe.sha256"
            $InstallerHash = Get-FileHash -Algorithm SHA256 -LiteralPath $InstallerExe
            "{0} *{1}" -f $InstallerHash.Hash, (Split-Path -Leaf $InstallerExe) |
                Set-Content -Path $InstallerChecksumPath -Encoding ascii

            Write-Ok "Installer created: $InstallerExe"
            Write-Ok "Installer SHA256 created: $InstallerChecksumPath"
        }

        Write-Host ''
        Write-Host 'Final publish outputs:' -ForegroundColor White
        Write-Host "  Publish folder: $PublishDir" -ForegroundColor Gray
        if ($DoZip) {
            Write-Host "  Portable artifact folder: $(Join-Path $RepoRoot 'artifacts\releases')" -ForegroundColor Gray
        }
        if ($Installer) {
            Write-Host "  Installer artifact folder: $(Join-Path $RepoRoot 'artifacts\installer')" -ForegroundColor Gray
        }
    }

    Write-Host ''
    Write-Ok 'Finished successfully.'
}
catch {
    Fail $_.Exception.Message
}
