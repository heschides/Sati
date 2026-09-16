@echo off
title Sati - apply the annual compliance update
setlocal
set "TOOL=%~dp0SatiUpdateReport.exe"

if not exist "%TOOL%" goto missing

echo.
echo This applies the annual compliance update to your Sati database directly,
echo the same way it is applied to the Demo database.
echo.
echo Close Sati first if it is open.
echo.
echo It checks first and changes nothing unless that check is clean.
echo It takes and verifies a backup before it writes anything.
echo It confirms your client list, notes since September 1, and the last 30 days
echo of scratchpad are unchanged afterwards.
echo.
set /p CONFIRM=Type YES to continue:
if /I not "%CONFIRM%"=="YES" goto cancelled

powershell -NoProfile -ExecutionPolicy Bypass -Command "$d=[Environment]::GetFolderPath('Desktop'); if (-not $d -or -not (Test-Path $d)) { $d = $env:USERPROFILE }; $log = Join-Path $d 'Sati-apply-update.txt'; Unblock-File -LiteralPath '%TOOL%' -ErrorAction SilentlyContinue; & '%TOOL%' SatiProduction --apply *>&1 | Tee-Object -FilePath $log; Write-Host ''; Write-Host '============================================================'; Write-Host (' Saved to: ' + $log); Write-Host ''; Write-Host ' If it says DONE, start Sati normally. Otherwise send the file to Josh.'; Write-Host '============================================================'"

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
echo Could not find SatiUpdateReport.exe next to this file.
echo Copy the whole folder to your Desktop and try again.
echo.
pause
exit /b 1
