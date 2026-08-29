@echo off
net session >nul 2>&1
if %errorLevel% == 0 (
    echo Administrator privileges confirmed.
) else (
    echo Set UAC = CreateObject^("Shell.Application"^) > "%temp%\getadmin.vbs"
    echo UAC.ShellExecute "%~s0", "", "", "runas", 1 >> "%temp%\getadmin.vbs"
    "%temp%\getadmin.vbs"
    del "%temp%\getadmin.vbs"
    exit /B
)

cd /d "%~dp0"

if not exist SonyMusicCenterSMTC.exe (
    echo SonyMusicCenterSMTC.exe not found! Please download the full release.
    pause
    exit /b
)

echo Closing Sony Music Center and Bridge...
taskkill /F /IM "Music Center.exe" /T >nul 2>&1
taskkill /F /IM SonyMusicCenterSMTC.exe /T >nul 2>&1

set "APP_DIR=C:\Program Files (x86)\Sony\Music Center"
if not exist "%APP_DIR%" (
    echo Sony Music Center not found at %APP_DIR%
    pause
    exit /b
)

echo Installing SonyMusicCenterSMTC.exe...
copy /Y SonyMusicCenterSMTC.exe "%APP_DIR%\SonyMusicCenterSMTC.exe" >nul

set "INDEX_JS=%APP_DIR%\resources\app\index.js"
if not exist "%INDEX_JS%.bak" (
    echo Backing up original index.js...
    copy /Y "%INDEX_JS%" "%INDEX_JS%.bak" >nul
)

echo Patching index.js...
copy /Y renderer-hook.js "%temp%\index.js.tmp" >nul
echo. >> "%temp%\index.js.tmp"
echo 'use strict'; >> "%temp%\index.js.tmp"
echo require('@z-app/core'); >> "%temp%\index.js.tmp"
copy /Y "%temp%\index.js.tmp" "%INDEX_JS%" >nul
del "%temp%\index.js.tmp"

echo Installation Complete! You can now start Sony Music Center.
pause
