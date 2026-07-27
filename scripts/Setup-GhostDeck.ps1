[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'GhostDeck.sln'
$dist = Join-Path $root 'dist\GhostDeck'
$serviceExe = Join-Path $dist 'GhostDeck.Service.exe'
$appExe = Join-Path $dist 'GhostDeck.App.exe'

function Assert-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) { throw "Missing required command: $Name" }
}

Assert-Command dotnet
Write-Host '[1/5] Restoring packages'
dotnet restore $solution
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }

Write-Host '[2/5] Building Release x64'
dotnet build $solution -c Release -p:Platform=x64 --no-restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }

Write-Host '[3/5] Publishing application and service'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item $dist -ItemType Directory -Force | Out-Null
dotnet publish (Join-Path $root 'src\GhostDeck.App\GhostDeck.App.csproj') -c Release -r win-x64 --self-contained true -o $dist
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed' }
dotnet publish (Join-Path $root 'src\GhostDeck.Service\GhostDeck.Service.csproj') -c Release -r win-x64 --self-contained true -o $dist
if ($LASTEXITCODE -ne 0) { throw 'Service publish failed' }

Write-Host '[4/5] Installing GhostDeck Service'
$service = Get-Service -Name 'GhostDeck.Service' -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') { Stop-Service $service.Name -Force }
    sc.exe delete 'GhostDeck.Service' | Out-Null
    Start-Sleep -Seconds 1
}
sc.exe create 'GhostDeck.Service' binPath= "`"$serviceExe`"" start= delayed-auto DisplayName= "GhostDeck Service" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Service installation failed. Run this script as Administrator.' }
Start-Service 'GhostDeck.Service'

Write-Host '[5/5] Creating Desktop shortcut'
$desktop = [Environment]::GetFolderPath('Desktop')
$shortcutPath = Join-Path $desktop 'GhostDeck.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $appExe
$shortcut.WorkingDirectory = $dist
$shortcut.Save()

Write-Host "Done. App: $appExe"
