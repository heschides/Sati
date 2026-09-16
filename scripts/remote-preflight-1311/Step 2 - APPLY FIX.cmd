@echo off
title Sati 1.3.11 compliance-date repair
setlocal
set "SCRIPT=%~dp0Repair-Release1311Readiness.ps1"

if not exist "%SCRIPT%" goto missing

echo.
echo This repairs compliance scheduling dates so the annual-compliance update can finish.
echo.
echo It does NOT change your client list, your notes, or your scratchpad.
echo If any of those change, it undoes everything automatically.
echo.
set /p CONFIRM=Type YES to continue: 
if /I not "%CONFIRM%"=="YES" goto cancelled

powershell -NoProfile -ExecutionPolicy Bypass -Command "$d=[Environment]::GetFolderPath('Desktop'); if (-not $d -or -not (Test-Path $d)) { $d = $env:USERPROFILE }; $log = Join-Path $d 'Sati-1311-repair.txt'; Unblock-File -LiteralPath '%SCRIPT%' -ErrorAction SilentlyContinue; & '%SCRIPT%' *>&1 | Tee-Object -FilePath $log; Write-Host ''; Write-Host '============================================================'; Write-Host (' Saved to: ' + $log); Write-Host ''; Write-Host ' NEXT: run Step 1 again, then install Sati 1.3.12 and start it.'; Write-Host '============================================================'"

echo.
pause
exit /b 0

:cancelled
echo.
echo Cancelled. Nothing was changed.
echo.
pause
exit /b 0

:missing
echo.
echo Could not find Repair-Release1311Readiness.ps1 next to this file.
echo Copy all files into the same folder and try again.
echo.
pause
exit /b 1
