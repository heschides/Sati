@echo off
title Sati 1.3.11 - why the repair stopped - makes no changes
setlocal
set "SCRIPT=%~dp0Diagnose-Release1311Collisions.ps1"

if not exist "%SCRIPT%" goto missing

echo.
echo Working out why Step 2 stopped on a duplicate date.
echo This makes NO changes to your database.
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command "$d=[Environment]::GetFolderPath('Desktop'); if (-not $d -or -not (Test-Path $d)) { $d = $env:USERPROFILE }; $log = Join-Path $d 'Sati-1311-collisions.txt'; Unblock-File -LiteralPath '%SCRIPT%' -ErrorAction SilentlyContinue; & '%SCRIPT%' *>&1 | Tee-Object -FilePath $log; Write-Host ''; Write-Host '============================================================'; Write-Host (' Saved to: ' + $log); Write-Host ''; Write-Host ' Send that file to Josh. Do not install anything yet.'; Write-Host '============================================================'"

echo.
pause
exit /b 0

:missing
echo.
echo Could not find Diagnose-Release1311Collisions.ps1 next to this file.
echo Copy both files into the same folder and try again.
echo.
pause
exit /b 1
