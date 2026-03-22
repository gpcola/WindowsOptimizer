param(
    [string]$ProjectPath = ".\WindowsOptimizer.csproj",
    [ValidateSet("Debug","Release")]
    [string]$Configuration = "Release",
    [switch]$Publish,
    [ValidateSet("win-x64","win-arm64","win-x86")]
    [string]$Runtime = "win-x64",
    [switch]$SelfContained,
    [switch]$Clean,
    [switch]$OpenOutput
)

$ErrorActionPreference = "Stop"

function Write-Info($Message) {
    Write-Host "[INFO] $Message" -ForegroundColor Cyan
}

function Write-Ok($Message) {
    Write-Host "[OK]   $Message" -ForegroundColor Green
}

function Fail($Message) {
    Write-Host "[FAIL] $Message" -ForegroundColor Red
    exit 1
}

try {
    Write-Info "Windows Optimizer build script starting..."

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        Fail "The .NET SDK is not installed or dotnet is not on PATH. Install .NET 8 SDK and try again."
    }

    if (-not (Test-Path $ProjectPath)) {
        Fail "Project file not found: $ProjectPath"
    }

    $resolvedProject = Resolve-Path $ProjectPath
    $projectDir = Split-Path -Parent $resolvedProject

    Set-Location $projectDir
    Write-Info "Project: $resolvedProject"
    Write-Info "Configuration: $Configuration"

    if ($Clean) {
        Write-Info "Cleaning project..."
        & dotnet clean $resolvedProject -c $Configuration
        if ($LASTEXITCODE -ne 0) { Fail "dotnet clean failed." }
        Write-Ok "Clean completed."
    }

    Write-Info "Restoring packages..."
    & dotnet restore $resolvedProject
    if ($LASTEXITCODE -ne 0) { Fail "dotnet restore failed." }
    Write-Ok "Restore completed."

    if ($Publish) {
        $publishDir = Join-Path $projectDir "publish\$Runtime\$Configuration"
        Write-Info "Publishing application..."
        & dotnet publish $resolvedProject `
            -c $Configuration `
            -r $Runtime `
            --self-contained:$($SelfContained.IsPresent.ToString().ToLower()) `
            -o $publishDir

        if ($LASTEXITCODE -ne 0) { Fail "dotnet publish failed." }

        Write-Ok "Publish completed."
        Write-Host ""
        Write-Host "Output folder:" -ForegroundColor White
        Write-Host "  $publishDir" -ForegroundColor Gray

        if ($OpenOutput) {
            Start-Process explorer.exe $publishDir
        }
    }
    else {
        Write-Info "Building application..."
        & dotnet build $resolvedProject -c $Configuration
        if ($LASTEXITCODE -ne 0) { Fail "dotnet build failed." }

        $buildDir = Join-Path $projectDir "bin\$Configuration\net8.0-windows"
        Write-Ok "Build completed."
        Write-Host ""
        Write-Host "Build folder:" -ForegroundColor White
        Write-Host "  $buildDir" -ForegroundColor Gray

        if ($OpenOutput -and (Test-Path $buildDir)) {
            Start-Process explorer.exe $buildDir
        }
    }

    Write-Host ""
    Write-Ok "Finished successfully."
}
catch {
    Fail $_.Exception.Message
}
