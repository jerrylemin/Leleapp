[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root 'dist\GhostDeck\App\GhostDeck.App.exe'
$workingDirectory = Split-Path $app -Parent
$startupLog = Join-Path $env:LOCALAPPDATA 'GhostDeck\Logs\startup.log'

if (-not (Test-Path -LiteralPath $app -PathType Leaf)) {
    throw 'GhostDeck is not published. Run scripts\Setup-GhostDeck.ps1 first.'
}

$service = Get-Service -Name 'GhostDeck.Service' -ErrorAction SilentlyContinue
if (-not $service) {
    throw 'GhostDeck.Service is not installed. Run setup as Administrator.'
}
if ($service.Status -ne 'Running') {
    Start-Service $service.Name
}

$existing = Get-Process -Name 'GhostDeck.App' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($existing) {
    Write-Host "GhostDeck is already running. PID: $($existing.Id)"
    exit 0
}

Write-Host "Launching: $app"
$process = Start-Process -FilePath $app -WorkingDirectory $workingDirectory -PassThru
Start-Sleep -Seconds 4

if (-not $process.HasExited) {
    Write-Host "GhostDeck started. PID: $($process.Id)"
    exit 0
}

Write-Host "GhostDeck exited during startup. Exit code: $($process.ExitCode)" -ForegroundColor Red

if (Test-Path -LiteralPath $startupLog) {
    Write-Host "`nStartup log: $startupLog" -ForegroundColor Yellow
    Get-Content -LiteralPath $startupLog -Tail 80
}
else {
    Write-Host 'No managed startup log was created. The failure likely happened before the WinUI application initialized.' -ForegroundColor Yellow
}

Write-Host "`nRecent Windows application errors:" -ForegroundColor Yellow
try {
    Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = (Get-Date).AddMinutes(-10) } -ErrorAction Stop |
        Where-Object {
            $_.ProviderName -in @('Application Error', '.NET Runtime', 'Windows Error Reporting') -and
            $_.Message -match 'GhostDeck\.App'
        } |
        Select-Object -First 5 TimeCreated, ProviderName, Id, LevelDisplayName, Message |
        Format-List
}
catch {
    Write-Host "Unable to read Windows Event Log: $($_.Exception.Message)"
}

throw 'GhostDeck failed during startup. Copy the startup log and recent Windows application errors shown above.'
