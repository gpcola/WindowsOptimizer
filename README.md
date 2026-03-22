# WindowsOptimizer

WindowsOptimizer is a Windows desktop utility designed to help reclaim space, review storage usage, and apply safe system clean-up and optimisation actions from a simple interface.

The project is built as a Windows desktop application with a focus on practical housekeeping rather than aggressive �registry cleaner� style changes. It aims to provide visible, understandable actions, sensible defaults, and recovery-aware behaviour where possible.

## Current Goals

WindowsOptimizer is intended to help with tasks such as:

- identifying large or unnecessary files
- reviewing storage pressure and likely space-saving opportunities
- assessing installed applications and removable items
- supporting system clean-up workflows
- helping the user make better decisions about disk usage and storage recovery
- capturing snapshots before certain changes
- supporting rollback or restore-aware workflows where practical

## Main Features

The current codebase includes functionality and structure around:

- **Storage analysis**
  - scans and evaluates storage candidates
  - reviews likely space-saving opportunities
  - helps surface folders, files, and paths worth investigating

- **Optimisation workflows**
  - central optimiser logic for clean-up and system-improvement tasks
  - descriptors for structured optimisation actions
  - support for pagefile-related options and snapshots

- **Snapshot and restore support**
  - snapshot creation and management
  - restore-aware design via dedicated manager classes
  - application and settings history structures where appropriate

- **PowerShell-backed system operations**
  - helper layer for Windows and PowerShell operations
  - script-assisted build and publish workflow

- **Logging and diagnostics**
  - logging support for traceability and troubleshooting

- **Windows desktop UI**
  - WPF application structure
  - XAML-based main window and app shell
  - project assets and app manifest included

## Project Structure

    WindowsOptimizer/
    +-- .github/
    �   +-- ISSUE_TEMPLATE/
    �   +-- workflows/
    �   +-- PULL_REQUEST_TEMPLATE.md
    +-- Assets/
    �   +-- App.ico
    �   +-- App.png
    +-- Models/
    �   +-- AppSnapshot.cs
    �   +-- OptimizationDescriptor.cs
    �   +-- PagefileOptions.cs
    �   +-- PagefileSettingSnapshot.cs
    �   +-- PathEntry.cs
    �   +-- StorageCandidate.cs
    �   +-- UserFolderEntry.cs
    +-- App.xaml
    +-- App.xaml.cs
    +-- MainWindow.xaml
    +-- MainWindow.xaml.cs
    +-- BenchmarkHelper.cs
    +-- Build-WindowsOptimizer.ps1
    +-- DiskHelper.cs
    +-- Logger.cs
    +-- Optimizer.cs
    +-- PowerShellHelper.cs
    +-- RestoreManager.cs
    +-- SnapshotManager.cs
    +-- StorageAdvisor.cs
    +-- WindowsOptimizer.csproj
    +-- app.manifest

## Technology Stack

- **.NET 8**
- **WPF**
- **C#**
- **PowerShell integration**
- **GitHub Actions** for build validation

## Requirements

To build or run the project locally, you will typically need:

- Windows 10 or Windows 11
- .NET 8 SDK
- PowerShell
- Visual Studio 2022 or later with .NET desktop development tools

or

- the `dotnet` CLI for command-line builds

## Build

### Using Visual Studio

1. Open `WindowsOptimizer.csproj` in Visual Studio.
2. Restore NuGet packages if prompted.
3. Build the project in `Debug` or `Release`.
4. Run the application.

### Using the .NET CLI

    dotnet restore
    dotnet build -c Release

## Publish

A PowerShell build script is included.

Example:

    powershell -ExecutionPolicy Bypass -File .\Build-WindowsOptimizer.ps1 -Publish -SelfContained

Depending on the script options and local environment, this can be used to produce a publish-ready build.

## GitHub Actions

The repository includes a GitHub Actions workflow for .NET build validation:

- restore
- build
- basic CI verification

This helps keep the project in a buildable state as it evolves.

## Design Principles

This project is intended to follow a few simple principles:

- **practical over gimmicky**
- **clear user-facing actions**
- **avoid unsafe �magic� optimisation claims**
- **prefer reviewable changes**
- **support recovery where possible**
- **keep logic modular and maintainable**

## Safety Note

WindowsOptimizer may perform or support actions that affect system configuration, disk usage, or storage-related settings. Even where recovery and snapshot logic exists, system changes should be approached carefully.

Recommended precautions:

- review actions before applying them
- test first on a non-critical machine where possible
- ensure backups exist for important data
- use administrative privileges only when necessary
- validate behaviour before wider use or distribution

## Development Status

This repository is an active working project. Some parts are already structured and functional, while others may still be under refinement, testing, or hardening.

Areas likely to continue evolving include:

- UI polish
- optimisation safety checks
- storage heuristics
- restore/snapshot robustness
- packaging and release flow
- additional diagnostics and reporting

## Roadmap Ideas

Potential future improvements may include:

- richer storage visualisation
- better candidate scoring for removable data
- safer guided clean-up recommendations
- improved restore checkpoints
- exportable reports
- scheduled scans
- extended admin-only system maintenance actions
- benchmark-informed recommendations

## Contributing

Contributions, fixes, and suggestions are welcome.

Typical contribution flow:

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Test locally
5. Submit a pull request

Please keep changes focused, readable, and aligned with the project�s safety-first intent.

## Issues and Feature Requests

Use the GitHub issue templates for:

- bug reports
- feature requests
- improvement suggestions

## Licence

Licence to be confirmed.

If you plan to publish or distribute this project publicly, add the appropriate licence file before release.
