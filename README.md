# WindowsOptimizer

WindowsOptimizer is a .NET 8 WPF desktop application for Windows storage analysis and optimization workflows.

## Prerequisites

- Windows 10 or Windows 11
- .NET 8 SDK
- PowerShell

## Build

From the repository root:

```powershell
dotnet build -c Release
```

## Publish (self-contained)

Primary release command:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-WindowsOptimizer.ps1 -Publish -SelfContained
```

This command now performs restore, build, publish, executable validation, and ZIP packaging by default.

### Expected compiled output

```text
publish\win-x64\Release\WindowsOptimizer.exe
```

### Expected release ZIP

```text
artifacts\releases\WindowsOptimizer-win-x64-Release-self-contained.zip
```

A SHA256 checksum is generated beside the ZIP:

```text
artifacts\releases\WindowsOptimizer-win-x64-Release-self-contained.zip.sha256
```

## Single-file publish (optional)

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-WindowsOptimizer.ps1 -Publish -SelfContained -SingleFile
```

## Notes on self-contained output

Self-contained publishing normally produces a folder containing `WindowsOptimizer.exe` and supporting runtime files. This is expected. Use `-SingleFile` only when you specifically want single-file packaging behavior.

## CI publish validation

GitHub Actions workflow: `.github/workflows/windows-build.yml`

The workflow runs on `windows-latest` and validates:

- restore/build
- script-based publish (`-Publish -SelfContained`)
- presence of `publish\win-x64\Release\WindowsOptimizer.exe`
- presence of release ZIP and SHA256 files under `artifacts\releases\`
- upload of release artifact `WindowsOptimizer-win-x64-self-contained`
