[CmdletBinding()]
param([switch]$DeleteUserData)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$service = Get-Service -Name 'GhostDeck.Service' -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') { Stop-Service $service.Name -Force }
    sc.exe delete 'GhostDeck.Service' | Out-Null
}
$shortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'GhostDeck.lnk'
if (Test-Path $shortcut) { Remove-Item $shortcut -Force }
if ($DeleteUserData) {
    $userData = Join-Path $env:LOCALAPPDATA 'GhostDeck'
    if (Test-Path $userData) { Remove-Item $userData -Recurse -Force }
}
Write-Host 'GhostDeck service and Desktop shortcut removed.'
