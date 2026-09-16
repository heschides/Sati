@echo off
title Sati 1.3.11 - diagnose "Sati cannot start"
setlocal
set "SCRIPT=%~dp0Diagnose-Release1311PartialUpdate.ps1"

if not exist "%SCRIPT%" goto missing

echo.
echo Collecting details about the update that Sati stopped on.
echo This makes NO changes.
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command "$d=[Environment]::GetFolderPath('Desktop'); if (-not $d -or -not (Test-Path $d)) { $d = $env:USERPROFILE }; $log = Join-Path $d 'Sati-1311-diagnosis.txt'; Unblock-File -LiteralPath '%SCRIPT%' -ErrorAction SilentlyContinue; & '%SCRIPT%' *>&1 | Tee-Object -FilePath $log; Write-Host ''; Write-Host '============================================================'; Write-Host (' Saved to: ' + $log); Write-Host ''; Write-Host ' Send that file to Josh. Do not reinstall yet.'; Write-Host '============================================================'"

echo.
pause
exit /b 0

:missing
echo.
echo Could not find Diagnose-Release1311PartialUpdate.ps1 next to this file.
echo Copy all files into the same folder and try again.
echo.
pause
exit /b 1
