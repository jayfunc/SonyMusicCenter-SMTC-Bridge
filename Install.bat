@echo off
net session >nul 2>&1
if %errorLevel% == 0 (
    echo [OK] Administrator privileges confirmed. / 已获取管理员权限。
) else (
    echo Requesting administrator privileges... / 正在请求管理员权限...
    echo Set UAC = CreateObject^("Shell.Application"^) > "%temp%\getadmin.vbs"
    echo UAC.ShellExecute "%~s0", "", "", "runas", 1 >> "%temp%\getadmin.vbs"
    "%temp%\getadmin.vbs"
    del "%temp%\getadmin.vbs"
    exit /B
)

cd /d "%~dp0"

if not exist SonyMusicCenterSMTC.exe (
    echo [ERROR] SonyMusicCenterSMTC.exe not found! Please download the full release.
    echo [错误] 找不到 SonyMusicCenterSMTC.exe！请确保下载了完整的发行版。
    pause
    exit /b
)

echo Closing Sony Music Center and Bridge... / 正在关闭 Sony Music Center 及旧版后台服务...
taskkill /F /IM "Music Center.exe" /T >nul 2>&1
taskkill /F /IM SonyMusicCenterSMTC.exe /T >nul 2>&1

set "APP_DIR=C:\Program Files (x86)\Sony\Music Center"
if not exist "%APP_DIR%" (
    echo [ERROR] Sony Music Center not found at %APP_DIR%
    echo [错误] 找不到 Sony Music Center 安装目录：%APP_DIR%
    pause
    exit /b
)

echo Installing SonyMusicCenterSMTC.exe... / 正在安装桥接程序...
copy /Y SonyMusicCenterSMTC.exe "%APP_DIR%\SonyMusicCenterSMTC.exe" >nul

set "INDEX_JS=%APP_DIR%\resources\app\index.js"
if not exist "%INDEX_JS%.bak" (
    echo Backing up original index.js... / 正在备份原始启动脚本...
    copy /Y "%INDEX_JS%" "%INDEX_JS%.bak" >nul
)

echo Patching index.js... / 正在注入桥接脚本...
copy /Y renderer-hook.js "%temp%\index.js.tmp" >nul
echo. >> "%temp%\index.js.tmp"
echo 'use strict'; >> "%temp%\index.js.tmp"
echo require('@z-app/core'); >> "%temp%\index.js.tmp"
copy /Y "%temp%\index.js.tmp" "%INDEX_JS%" >nul
del "%temp%\index.js.tmp"

echo.
echo ==============================================================
echo [SUCCESS] Installation Complete! You can now start Sony Music Center.
echo [成功] 安装完成！现在您可以正常启动 Sony Music Center 了。
echo ==============================================================
echo.
pause
