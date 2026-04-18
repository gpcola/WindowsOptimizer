# WindowsOptimizer

WindowsOptimizer is a WPF .NET 8 Windows desktop utility focused on practical housekeeping, safe storage cleanup, restore-aware changes, and Windows-native workflows.

## Current focus

- practical over gimmicky
- safer cleanup before aggressive deletion
- clearer run summaries and reboot messaging
- smarter storage analysis with scoring and risk labels
- quarantine-first handling for uncertain large items
- Windows-native PowerShell and Microsoft-supported workflows where possible

## Highlights in this revision

- improved PowerShell command execution using encoded commands to reduce quoting and escaping failures
- reduced duplicate log noise
- richer automatic run summaries with success and warning counts
- smarter storage candidate scoring, risk labels, and recommended actions
- multi-select move, quarantine, and delete handling for storage candidates
- cancellable background storage scanning
- safe reclaim PowerShell script export and launch support
- safe reclaim **compiled mode** execution from the WPF app (script export remains available for auditing/manual use)

## Build

### Visual Studio

Open `WindowsOptimizer.csproj` in Visual Studio 2022 or later with .NET desktop development tools installed, then build or publish as normal.

### PowerShell build script

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-WindowsOptimizer.ps1 -Publish -SelfContained
```

For a release-ready Windows binary:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-WindowsOptimizer.ps1 -Publish -Runtime win-x64 -Configuration Release -SelfContained
```
