# Changelog

## 1.3.1 — 2026-08-31

### Added
- Prominent post-run reporting of the net disk space reclaimed.
- Exact C: free-space measurement before and after maintenance workflows.
- Adaptive MB/GB formatting for reclaimed-space results.
- Reclaimed-space results in Simple mode, the shared progress area, activity log and Advanced run summary.

### Changed
- Negative free-space movement is reported as a net change rather than attributed directly to cleanup, because Windows background activity can affect the measurement.
- Version bumped from 1.3.0 to 1.3.1.

### Validation
- .NET 8 WPF build passed.
- Cleanup safety gates passed.
- Self-contained Windows publish passed.
- Installer generation and artifact validation passed.
- Release workflow re-run successfully on 2026-09-01.

## 1.3.0 — 2026-08-31

### Added
- Central cleanup safety boundary.
- Built-in protection for Microsoft Edge user data, Chrome/Brave/Firefox profiles, Microsoft Store app data and sensitive Windows identity/credential locations.
- Persistent custom cleanup exclusions.
- Shared progress reporting for housekeeping, network and media-streaming workflows.
- Stronger 1LG Digital branding in Simple and Advanced modes.

### Changed
- Simplified default UI around one primary safe-cleanup action.
- Temporary-file cleanup moved to guarded C# logic limited to expected Temp roots and top-level entries only.
- Reparse points/junctions, protected paths and in-use items are skipped.

### Safety
- CI now fails if core application-profile protections are removed, if temp cleanup becomes recursive, or if cleanup candidates bypass the exclusion guard.
