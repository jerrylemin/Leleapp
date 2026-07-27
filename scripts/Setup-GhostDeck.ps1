[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'GhostDeck.sln'
$appProject = Join-Path $root 'src\GhostDeck.App\GhostDeck.App.csproj'
$serviceProject = Join-Path $root 'src\GhostDeck.Service\GhostDeck.Service.csproj'
$dist = Join-Path $root 'dist\GhostDeck'
$serviceExe = Join-Path $dist 'GhostDeck.Service.exe'
$appExe = Join-Path $dist 'GhostDeck.App.exe'

function Assert-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Missing required command: $Name"
    }
}

function Assert-File([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description was not found: $Path"
    }
}

Assert-Command 'dotnet'
Assert-File $solution 'Solution'
Assert-File $appProject 'Application project'
Assert-File $serviceProject 'Service project'

Push-Location $root
try {
    Write-Host '[1/5] Cleaning and restoring packages'
    Get-ChildItem (Join-Path $root 'src') -Directory -Recurse -Force |
        Where-Object { $_.Name -in @('bin', 'obj') } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

    & dotnet restore $solution -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }

    Write-Host '[2/5] Building Release x64'
    & dotnet build $solution -c Release -p:Platform=x64 --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }

    Write-Host '[3/5] Publishing application and service'
    if (Test-Path -LiteralPath $dist) { Remove-Item $dist -Recurse -Force }
    New-Item $dist -ItemType Directory -Force | Out-Null

    & dotnet publish $appProject -c Release -r win-x64 -p:Platform=x64 --self-contained true --no-restore -o $dist
    if ($LASTEXITCODE -ne 0) { throw 'Application publish failed' }

    & dotnet publish $serviceProject -c Release -r win-x64 -p:Platform=x64 --self-contained true --no-restore -o $dist
    if ($LASTEXITCODE -ne 0) { throw 'Service publish failed' }

    Assert-File $appExe 'Published application'
    Assert-File $serviceExe 'Published service'

    Write-Host '[4/5] Installing GhostDeck Service'
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Service installation requires an Administrator PowerShell window.'
    }

    $service = Get-Service -Name 'GhostDeck.Service' -ErrorAction SilentlyContinue
    if ($service) {
        if ($service.Status -ne 'Stopped') { Stop-Service $service.Name -Force }
        & sc.exe delete 'GhostDeck.Service' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Existing GhostDeck Service could not be removed.' }
        Start-Sleep -Seconds 2
    }

    & sc.exe create 'GhostDeck.Service' binPath= "`"$serviceExe`"" start= delayed-auto DisplayName= 'GhostDeck Service' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Service installation failed.' }

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
}
finally {
    Pop-Location
}
