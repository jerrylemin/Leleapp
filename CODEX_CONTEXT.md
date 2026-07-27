# GhostDeck Codex Context

## Stack

- C# and .NET 10.
- WinUI 3 through Microsoft.WindowsAppSDK 2.3.1.
- Windows 10 build 19041 minimum.
- x64 primary target.
- Non-elevated UI plus elevated Windows Service.
- Typed JSON messages over Named Pipes.

## Projects

- `src/GhostDeck.App`: WinUI shell and live views.
- `src/GhostDeck.Service`: privileged operations.
- `src/GhostDeck.Core`: records, grouping, protected-process policy.
- `src/GhostDeck.Infrastructure`: process, Registry, DNS and cleaner inspection.
- `src/GhostDeck.Contracts`: service protocol.

## Source inputs

- Cleanup source: `resources/prefetch.bat`.
- Ghost Mode actions are read at runtime from DesktopBackground, Directory Background, and CommandStore Registry paths.

## Current state

Implemented:

- Solution and projects.
- Live process collection and grouping.
- Process list, search and multi-selection.
- Privileged process termination with PID start-time verification.
- Power and memory Registry action discovery.
- Network adapter and IPv4 DNS inspection.
- Cleaner target scanning.
- Privileged DNS set, reset and flush handlers.
- Setup, run and uninstall scripts.

Pending:

- Compile validation on a Windows machine.
- Hardware temperature and per-process GPU telemetry.
- Audio playback endpoint switching.
- Tray integration.
- SQLite action-history repository and UI.
- Settings persistence.
- Cleaner deletion execution.
- UI buttons for applying power and DNS actions.
- Reduce Memory execution button and before-after measurement.

## Commands

```powershell
dotnet restore .\GhostDeck.sln
dotnet build .\GhostDeck.sln -c Release -p:Platform=x64
PowerShell -ExecutionPolicy Bypass -File .\scripts\Setup-GhostDeck.ps1
PowerShell -ExecutionPolicy Bypass -File .\scripts\Run-GhostDeck.ps1
```
