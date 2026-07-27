# GhostDeck V1

GhostDeck is a native Windows control center built with C#, .NET 10, WinUI 3, and Windows App SDK.

## Current implementation

- Modern WinUI 3 navigation shell with Mica backdrop.
- Live process collection and deterministic grouping by executable identity.
- CPU and RAM summaries.
- Multi-selection process termination through a separate Windows Service.
- Protected-process checks and PID start-time verification.
- Read-only import of Ghost Mode power and memory actions from Registry.
- IPv4 adapter and DNS inspection.
- Cleanup target scanning based on the supplied `prefetch.bat` targets.
- Typed Named Pipe contracts for privileged operations.
- DNS change, DNS reset, and DNS flush service handlers.
- Setup, run, and uninstall PowerShell scripts.

Audio switching, persistent action history, tray integration, hardware temperature telemetry, cleaner deletion, power-action execution UI, and full settings persistence still need an implementation pass. Their UI or architecture is present where stated, but they are not complete.

## Requirements

- Windows 10 build 19041 or newer.
- Windows 11 recommended.
- x64 CPU.
- .NET 10 SDK.
- Visual Studio 2022 or newer with Desktop development with C++ and Windows App SDK tooling, or equivalent Build Tools.
- Administrator access for service installation.

## First-time setup

Open PowerShell as Administrator in the repository folder:

```powershell
PowerShell -ExecutionPolicy Bypass -File .\scripts\Setup-GhostDeck.ps1
```

The script restores packages, builds Release x64, publishes to `dist\GhostDeck`, installs `GhostDeck.Service`, starts the service, and creates a Desktop shortcut.

## Normal launch

```powershell
PowerShell -ExecutionPolicy Bypass -File .\scripts\Run-GhostDeck.ps1
```

Direct executable after setup:

```text
.\dist\GhostDeck\GhostDeck.App.exe
```

## Uninstall service and shortcut

Run PowerShell as Administrator:

```powershell
PowerShell -ExecutionPolicy Bypass -File .\scripts\Uninstall-GhostDeck.ps1
```

Delete user data too:

```powershell
PowerShell -ExecutionPolicy Bypass -File .\scripts\Uninstall-GhostDeck.ps1 -DeleteUserData
```

## Project structure

```text
src/GhostDeck.App             WinUI 3 interface
src/GhostDeck.Service         privileged Windows Service
src/GhostDeck.Core            domain models and safety rules
src/GhostDeck.Infrastructure  Windows integration
src/GhostDeck.Contracts       versioned Named Pipe messages
resources/prefetch.bat         supplied cleanup source
scripts                       setup, launch, uninstall
```

## Data locations

- User settings: `%LOCALAPPDATA%\GhostDeck`
- Shared service data and logs: `%PROGRAMDATA%\GhostDeck`
- Published binaries: `dist\GhostDeck`

## Build directly

```powershell
dotnet restore .\GhostDeck.sln
dotnet build .\GhostDeck.sln -c Release -p:Platform=x64
```

No automated test project is included, as requested.
