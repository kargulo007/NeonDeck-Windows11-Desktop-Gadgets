@echo off
setlocal
chcp 65001 >nul
title NeonDeck - Mapped drive diagnostics
color 0A
echo.
echo ==== WINDOWS NETWORK MAPPINGS (CURRENT USER) ====
echo.
whoami
net use
echo.
echo ==== MAPPED LETTERS + CAPACITY (WMI) ====
echo.
powershell.exe -NoProfile -NonInteractive -Command "Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=4' -ErrorAction SilentlyContinue | Select-Object DeviceID,ProviderName,Size,FreeSpace | Format-Table -AutoSize"
echo.
echo ==== SAVED PERSISTENT MAPPINGS ====
echo.
reg query HKCU\Network /s 2>nul
if errorlevel 1 echo No saved persistent mappings or insufficient permission.
echo.
echo NOTE: Run this file normally, not as administrator.
echo If mappings are missing here, NeonDeck may not see them either.
pause
