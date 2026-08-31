# Windows Optimizer

**Windows Optimizer** is a Windows 10/11 housekeeping and performance utility from **1LG Digital**.

The application deliberately favours safe, understandable maintenance over aggressive "tweaks". It does not contain a registry cleaner and shipped application code is prohibited by CI from disabling or removing installed Windows optional features.

## Interface

Windows Optimizer opens in **Simple** mode by default.

### Simple

Simple mode provides one primary action:

- **Run safe cleanup** — removes only stale top-level temporary files, empties the Recycle Bin, and clears the Windows Update download cache when Windows servicing is idle.

A visible progress panel shows the current step and completion state. Browser profiles, Microsoft Store app data, Windows identity stores and user-defined exclusions are protected from cleanup.

Simple mode does **not** uninstall applications, disable services or indexing, alter the pagefile, delete restore points, disable hibernation, change background-app policies, or remove Windows features.

### Advanced

Advanced mode contains only Windows maintenance/performance areas:

- **Housekeeping** — safe cleanup, built-in protected folders, persistent custom exclusions, and an explicitly confirmed optional consumer-app removal action
- **Performance** — Windows-native system-drive optimisation, Ethernet/Wi-Fi optimisation, Private Internet Access guidance, Startup Apps/Storage Settings shortcuts, and LAN media streaming controls
- **Benchmark** — before/after disk and memory snapshots

WSL, Visual Studio, OneDrive, user-folder relocation, large-file candidate management and similar non-performance modules are not compiled into the shipped application.

## Cleanup protection

Cleanup is deliberately constrained so application profiles cannot become generic cleanup targets.

Built-in protected locations include:

- Microsoft Edge user data
- Chrome, Brave and Firefox profiles
- Microsoft Store application data
- Microsoft OneAuth / IdentityCache
- Windows credential and DPAPI stores

Additional folders can be entered in **Advanced > Housekeeping > Your additional exclusions**. They are stored under `%LOCALAPPDATA%\1LG Digital\WindowsOptimizer\cleanup-exclusions.txt`.

Temporary-file cleanup is non-recursive and limited to the expected user and Windows Temp roots. Reparse points/junctions are skipped.

## Windows feature safety

Windows Optimizer never disables or removes an installed Windows optional feature.

The Windows build workflow fails if shipped application source contains commands such as:

- `Disable-WindowsOptionalFeature`
- `Remove-WindowsCapability`
- `Uninstall-WindowsFeature`
- DISM `/Disable-Feature`

LAN media streaming is **add-only**. If required Microsoft media components are missing, Windows Optimizer asks before enabling/adding them. Turning streaming off disables sharing but leaves the Windows media components installed.

## Ethernet and Wi-Fi optimisation

The network optimiser is intentionally conservative and VPN-aware. It:

- targets only physical Ethernet/Wi-Fi adapters
- restores Windows TCP receive-window auto-tuning to `Normal`
- enables Windows global Receive Side Scaling (RSS)
- enables per-adapter RSS where the physical adapter supports it
- disables selective suspend and device sleep-on-disconnect where supported
- uses `-NoRestart` for adapter settings so Windows Optimizer does not deliberately interrupt the active link
- reports the detected physical adapter/link speeds

It deliberately does **not** change:

- DNS servers
- MTU
- routes
- network bindings
- Windows Firewall rules
- PIA/WinTUN/TAP/WireGuard/OpenVPN virtual adapters
- checksum, LSO or RSC offloads
- vendor-specific advanced NIC properties such as Energy Efficient Ethernet or Wi-Fi Preferred Band

These exclusions are intentional because such settings are hardware-, driver- or VPN-specific and blanket changes can reduce throughput or break connectivity.

### Private Internet Access (PIA)

If PIA is installed in its normal Windows location, Windows Optimizer detects it and keeps PIA-controlled network settings untouched.

For LAN/media-device access while PIA is connected, enable **Allow LAN Traffic** in PIA **Settings > Network**. Windows Optimizer provides an **Open PIA** button but does not modify PIA's configuration files.

PIA's own VPN DNS, MTU and tunnel-driver choices remain under PIA control.

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
- browser/application-profile cleanup protections
- non-recursive temporary-file cleanup
- .NET restore/build
- self-contained Windows publish
- compiled executable presence
- portable ZIP and SHA256
- installable setup EXE and SHA256
- release-artifact upload
