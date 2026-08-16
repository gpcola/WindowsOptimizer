# Windows Optimizer

**Windows Optimizer** is a Windows 10/11 housekeeping and performance utility from **1LG Digital**.

The application deliberately favours safe, understandable maintenance over aggressive "tweaks". It does not contain a registry cleaner and shipped application code is prohibited by CI from disabling or removing installed Windows optional features.

## Interface

Windows Optimizer opens in **Simple** mode by default.

### Simple

One-click safe housekeeping:

- clean stale, unlocked top-level temporary files
- empty the Windows Recycle Bin
- clear the Windows Update download cache only when Windows servicing is idle

Simple mode does **not** uninstall applications, disable services or indexing, alter the pagefile, delete restore points, disable hibernation, change background-app policies, or remove Windows features.

It also includes a separate **Make this PC a LAN media streamer** button.

### Advanced

Advanced mode contains only Windows maintenance/performance areas:

- **Housekeeping** — safe cleanup plus an explicitly confirmed optional consumer-app removal action
- **Performance** — Windows-native system-drive optimisation, Startup Apps/Storage Settings shortcuts, and LAN media streaming controls
- **Benchmark** — before/after disk and memory snapshots

WSL, Visual Studio, OneDrive, user-folder relocation, large-file candidate management and similar non-performance modules are not compiled into the shipped application.

## Windows feature safety

Windows Optimizer never disables or removes an installed Windows optional feature.

The Windows build workflow fails if shipped application source contains commands such as:

- `Disable-WindowsOptionalFeature`
- `Remove-WindowsCapability`
- `Uninstall-WindowsFeature`
- DISM `/Disable-Feature`

LAN media streaming is **add-only**. If required Microsoft media components are missing, Windows Optimizer asks before enabling/adding them. Turning streaming off disables sharing but leaves the Windows media components installed.

## LAN media streaming

The media streaming workflow:

1. verifies the active Windows network profile;
2. asks before changing an active Public profile to Private;
3. checks for Windows Media Player network-sharing components;
4. asks before adding a missing Microsoft media component;
5. enables Windows Media Player network sharing using Windows' own media-sharing configuration.

The workflow is intended only for a trusted private LAN.

## Requirements

To run the self-contained build:

- Windows 10 or Windows 11 x64
- administrator approval when Windows requests elevation

To build locally:

- .NET 8 SDK
- PowerShell
- Inno Setup 6 when building the installer

## Build

```powershell
dotnet build -c Release
```

## Publish portable self-contained build

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-WindowsOptimizer.ps1 -Publish -SelfContained
```

Expected outputs:

```text
publish\win-x64\Release\WindowsOptimizer.exe
artifacts\releases\WindowsOptimizer-win-x64-Release-self-contained.zip
artifacts\releases\WindowsOptimizer-win-x64-Release-self-contained.zip.sha256
```

## Build installable setup

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-WindowsOptimizer.ps1 -Publish -SelfContained -Installer
```

Expected installer outputs:

```text
artifacts\installer\WindowsOptimizer-Setup-win-x64.exe
artifacts\installer\WindowsOptimizer-Setup-win-x64.exe.sha256
```

The installer places Windows Optimizer under the **1LG Digital** program folder and creates Start Menu/uninstall entries. A desktop shortcut is optional during setup.

## CI validation

`.github/workflows/windows-build.yml` validates:

- the no-Windows-feature-removal safety invariant
- .NET restore/build
- self-contained Windows publish
- compiled executable presence
- portable ZIP and SHA256
- installable setup EXE and SHA256
- release-artifact upload
