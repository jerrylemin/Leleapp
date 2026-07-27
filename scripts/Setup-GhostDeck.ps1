[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'GhostDeck.sln'
$appProject = Join-Path $root 'src\GhostDeck.App\GhostDeck.App.csproj'
$serviceProject = Join-Path $root 'src\GhostDeck.Service\GhostDeck.Service.csproj'
$distRoot = Join-Path $root 'dist\GhostDeck'
$appDist = Join-Path $distRoot 'App'
$serviceDist = Join-Path $distRoot 'Service'
$appExe = Join-Path $appDist 'GhostDeck.App.exe'
$serviceExe = Join-Path $serviceDist 'GhostDeck.Service.exe'
$serviceName = 'GhostDeck.Service'

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

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Setup requires an Administrator PowerShell window.'
    }
}

function Stop-GhostDeckRuntime {
    Write-Host '[0/5] Stopping running GhostDeck components'

    Get-Process -Name 'GhostDeck.App' -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force -ErrorAction Stop
        $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(20))
    }

    Get-Process -Name 'GhostDeck.Service' -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

    $deadline = (Get-Date).AddSeconds(10)
    do {
        $running = @(
            Get-Process -Name 'GhostDeck.App', 'GhostDeck.Service' -ErrorAction SilentlyContinue
        )
        if ($running.Count -eq 0) { break }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    if ($running.Count -gt 0) {
        throw "GhostDeck is still running and locking publish files. PIDs: $($running.Id -join ', ')"
    }

    Start-Sleep -Milliseconds 500
}

function Remove-DirectoryWithRetry([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }

    $lastError = $null
    for ($attempt = 1; $attempt -le 8; $attempt++) {
        try {
            Get-ChildItem -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue |
                ForEach-Object {
                    try { $_.IsReadOnly = $false } catch { }
                }

            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            $lastError = $_
            if ($attempt -lt 8) { Start-Sleep -Milliseconds (250 * $attempt) }
        }
    }

    throw "Unable to remove publish directory after stopping GhostDeck: $Path`n$($lastError.Exception.Message)"
}

Assert-Command 'dotnet'
Assert-Command 'sc.exe'
Assert-Administrator
Assert-File $solution 'Solution'
Assert-File $appProject 'Application project'
Assert-File $serviceProject 'Service project'

Push-Location $root
try {
    Stop-GhostDeckRuntime

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
    Remove-DirectoryWithRetry $distRoot
    New-Item $appDist -ItemType Directory -Force | Out-Null
    New-Item $serviceDist -ItemType Directory -Force | Out-Null

    & dotnet publish $appProject -c Release -r win-x64 -p:Platform=x64 --self-contained true --no-restore -o $appDist
    if ($LASTEXITCODE -ne 0) { throw 'Application publish failed' }

    & dotnet publish $serviceProject -c Release -r win-x64 -p:Platform=x64 --self-contained true --no-restore -o $serviceDist
    if ($LASTEXITCODE -ne 0) { throw 'Service publish failed' }

    Assert-File $appExe 'Published application'
    Assert-File $serviceExe 'Published service'

    Write-Host '[4/5] Installing GhostDeck Service'
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($service) {
        & sc.exe delete $serviceName | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Existing GhostDeck Service could not be removed.' }

        $deleteDeadline = (Get-Date).AddSeconds(15)
        do {
            Start-Sleep -Milliseconds 300
            $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
        } while ($service -and (Get-Date) -lt $deleteDeadline)

        if ($service) { throw 'Existing GhostDeck Service is pending deletion. Close Services.msc and rerun setup.' }
    }

    & sc.exe create $serviceName binPath= "`"$serviceExe`"" start= delayed-auto DisplayName= 'GhostDeck Service' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Service installation failed.' }

    Start-Service $serviceName

    Write-Host '[5/5] Creating Desktop shortcut'
    $desktop = [Environment]::GetFolderPath('Desktop')
    $shortcutPath = Join-Path $desktop 'GhostDeck.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $appExe
    $shortcut.WorkingDirectory = $appDist
    $shortcut.Save()

    Write-Host "Done. App: $appExe"
}
finally {
    Pop-Location
}
