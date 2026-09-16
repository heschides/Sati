@echo off
title Sati - what the startup check actually found - makes no changes
setlocal
set "TOOL=%~dp0SatiUpdateReport.exe"

if not exist "%TOOL%" goto missing

echo.
echo Running the real startup update check and printing everything it found.
echo This makes NO changes to your database and applies no update.
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command "$d=[Environment]::GetFolderPath('Desktop'); if (-not $d -or -not (Test-Path $d)) { $d = $env:USERPROFILE }; $log = Join-Path $d 'Sati-update-report.txt'; Unblock-File -LiteralPath '%TOOL%' -ErrorAction SilentlyContinue; & '%TOOL%' SatiProduction *>&1 | Tee-Object -FilePath $log; Write-Host ''; Write-Host '============================================================'; Write-Host (' Saved to: ' + $log); Write-Host ''; Write-Host ' Send that file to Josh.'; Write-Host '============================================================'"

echo.
pause
exit /b 0

:missing
echo.
echo Could not find SatiUpdateReport.exe next to this file.
echo Copy the whole folder to your Desktop and try again.
echo.
pause
exit /b 1
