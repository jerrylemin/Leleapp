[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root 'dist\GhostDeck\GhostDeck.App.exe'
if (-not (Test-Path $app)) { throw 'GhostDeck is not published. Run scripts\Setup-GhostDeck.ps1 first.' }
$service = Get-Service -Name 'GhostDeck.Service' -ErrorAction SilentlyContinue
if (-not $service) { throw 'GhostDeck.Service is not installed. Run setup as Administrator.' }
if ($service.Status -ne 'Running') { Start-Service $service.Name }
$existing = Get-Process -Name 'GhostDeck.App' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($existing) { Write-Host 'GhostDeck is already running.'; exit 0 }
Start-Process -FilePath $app -WorkingDirectory (Split-Path $app -Parent)
