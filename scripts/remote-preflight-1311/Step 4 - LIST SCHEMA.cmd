@echo off
title Sati - list installed version and database schema - makes no changes
setlocal
set "SCRIPT=%~dp0Dump-Release1311Schema.ps1"

if not exist "%SCRIPT%" goto missing

echo.
echo Listing the installed Sati version and this database's tables and indexes.
echo This makes NO changes to your database.
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command "$d=[Environment]::GetFolderPath('Desktop'); if (-not $d -or -not (Test-Path $d)) { $d = $env:USERPROFILE }; $log = Join-Path $d 'Sati-schema-dump.txt'; Unblock-File -LiteralPath '%SCRIPT%' -ErrorAction SilentlyContinue; & '%SCRIPT%' *>&1 | Tee-Object -FilePath $log; Write-Host ''; Write-Host '============================================================'; Write-Host (' Saved to: ' + $log); Write-Host ''; Write-Host ' Send that file to Josh. Do not install anything else yet.'; Write-Host '============================================================'"

echo.
pause
exit /b 0

:missing
echo.
echo Could not find Dump-Release1311Schema.ps1 next to this file.
echo Copy both files into the same folder and try again.
echo.
pause
exit /b 1
