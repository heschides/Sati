@echo off
title Sati - check the installed files - makes no changes
setlocal
set "SCRIPT=%~dp0Check-InstalledFiles.ps1"

if not exist "%SCRIPT%" goto missing

echo.
echo Listing the version of every Sati file actually installed.
echo This makes NO changes and does not open your database.
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command "$d=[Environment]::GetFolderPath('Desktop'); if (-not $d -or -not (Test-Path $d)) { $d = $env:USERPROFILE }; $log = Join-Path $d 'Sati-installed-files.txt'; Unblock-File -LiteralPath '%SCRIPT%' -ErrorAction SilentlyContinue; & '%SCRIPT%' *>&1 | Tee-Object -FilePath $log; Write-Host ''; Write-Host '============================================================'; Write-Host (' Saved to: ' + $log); Write-Host ''; Write-Host ' Send that file to Josh.'; Write-Host '============================================================'"

echo.
pause
exit /b 0

:missing
echo.
echo Could not find Check-InstalledFiles.ps1 next to this file.
echo Copy the whole folder to your Desktop and try again.
echo.
pause
exit /b 1
