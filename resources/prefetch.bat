@echo off
setlocal EnableExtensions EnableDelayedExpansion
net session >nul 2>&1
if %errorlevel% neq 0 (
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
set /a ERR=0
set /a CLEANED_ITEMS=0
set /a FILES_REMOVED=0
set /a BYTES_REMOVED=0
call :CleanDir "Prefetch" "%SystemRoot%\Prefetch"
call :CleanDir "WinTemp" "%SystemRoot%\Temp"
if defined TEMP call :CleanDir "Temp" "%TEMP%"
if defined TMP call :CleanDir "Tmp" "%TMP%"
if defined LOCALAPPDATA call :CleanDir "LocalTemp" "%LOCALAPPDATA%\Temp"
if defined APPDATA call :CleanDir "Recent" "%APPDATA%\Microsoft\Windows\Recent"
if defined APPDATA call :CleanDir "JumpList-Auto" "%APPDATA%\Microsoft\Windows\Recent\AutomaticDestinations"
if defined APPDATA call :CleanDir "JumpList-Custom" "%APPDATA%\Microsoft\Windows\Recent\CustomDestinations"
if defined LOCALAPPDATA call :CleanDir "CrashDumps" "%LOCALAPPDATA%\CrashDumps"
if defined LOCALAPPDATA call :CleanFiles "ThumbCache" "%LOCALAPPDATA%\Microsoft\Windows\Explorer" "thumbcache*.db"
if defined LOCALAPPDATA call :CleanFiles "IconCache" "%LOCALAPPDATA%\Microsoft\Windows\Explorer" "iconcache*.db"
if defined LOCALAPPDATA call :CleanDir "INetCache" "%LOCALAPPDATA%\Microsoft\Windows\INetCache"
if defined PROGRAMDATA call :CleanDir "DeliveryOpt" "%PROGRAMDATA%\Microsoft\Windows\DeliveryOptimization\Cache"
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Clear-RecycleBin -Force -ErrorAction Stop } catch { exit 1 }" >nul 2>&1
exit /b
:CleanDir
set "DIR=%~2"
if not exist "%DIR%" exit /b
del /f /q "%DIR%\*.*" >nul 2>&1
for /d %%D in ("%DIR%\*") do rd /s /q "%%D" >nul 2>&1
exit /b
:CleanFiles
set "DIR=%~2"
set "PAT=%~3"
if not exist "%DIR%" exit /b
del /f /q "%DIR%\%PAT%" >nul 2>&1
exit /b
