@echo off
title Sati - what the compliance clean-up would change - makes no changes
setlocal
set "TOOL=%~dp0SatiComplianceSeed.exe"

if not exist "%TOOL%" goto missing

echo.
echo Checking what the compliance clean-up would change.
echo This makes NO changes to your database.
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command "$d=[Environment]::GetFolderPath('Desktop'); if (-not $d -or -not (Test-Path $d)) { $d = $env:USERPROFILE }; $log = Join-Path $d 'Sati-compliance-seed-check.txt'; Unblock-File -LiteralPath '%TOOL%' -ErrorAction SilentlyContinue; & '%TOOL%' SatiProduction *>&1 | Tee-Object -FilePath $log; Write-Host ''; Write-Host '============================================================'; Write-Host (' Saved to: ' + $log); Write-Host ''; Write-Host ' Note the TOTAL CHANGES number. Step 2 asks for it.'; Write-Host '============================================================'"

echo.
pause
exit /b 0

:missing
echo.
echo Could not find SatiComplianceSeed.exe next to this file.
echo Copy the whole folder to your Desktop and try again.
echo.
pause
exit /b 1
