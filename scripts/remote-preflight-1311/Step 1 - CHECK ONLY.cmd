@echo off
title Sati 1.3.11 readiness check - makes no changes
setlocal
set "SCRIPT=%~dp0Check-Release1311Readiness.ps1"

if not exist "%SCRIPT%" goto missing

echo.
echo Checking whether this computer's Sati database is ready for version 1.3.11.
echo This makes NO changes to it.
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command "$d=[Environment]::GetFolderPath('Desktop'); if (-not $d -or -not (Test-Path $d)) { $d = $env:USERPROFILE }; $log = Join-Path $d 'Sati-1311-readiness.txt'; Unblock-File -LiteralPath '%SCRIPT%' -ErrorAction SilentlyContinue; & '%SCRIPT%' *>&1 | Tee-Object -FilePath $log; Write-Host ''; Write-Host '============================================================'; Write-Host (' Saved to: ' + $log); Write-Host ''; Write-Host ' Send that file to Josh BEFORE installing 1.3.11.'; Write-Host '============================================================'"

echo.
pause
exit /b 0

:missing
echo.
echo Could not find Check-Release1311Readiness.ps1 next to this file.
echo Copy both files into the same folder and try again.
echo.
pause
exit /b 1
