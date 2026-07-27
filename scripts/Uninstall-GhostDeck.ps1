[CmdletBinding()]
param([switch]$DeleteUserData)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Get-Process -Name 'GhostDeck.App' -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue

$service = Get-Service -Name 'GhostDeck.Service' -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') {
        Stop-Service $service.Name -Force
        $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(20))
    }
    sc.exe delete 'GhostDeck.Service' | Out-Null
}

$shortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'GhostDeck.lnk'
if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }

if ($DeleteUserData) {
    $userData = Join-Path $env:LOCALAPPDATA 'GhostDeck'
    if (Test-Path -LiteralPath $userData) { Remove-Item -LiteralPath $userData -Recurse -Force }
}

Write-Host 'GhostDeck process, service, and Desktop shortcut removed.'
