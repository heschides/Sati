@echo off
title Sati - record past-due compliance items as done
setlocal
set "TOOL=%~dp0SatiComplianceSeed.exe"

if not exist "%TOOL%" goto missing

echo.
echo This records every compliance item that is already past due as done on its
echo own due date, so Sati stops showing old work as overdue.
echo.
echo Close Sati first if it is open.
echo.
echo It backs up your database, tries the change on a copy, and changes your
echo real database only if the copy comes out exactly as Step 1 described.
echo.
set /p TOTAL=Type the TOTAL CHANGES number from Step 1: 
set /p CONFIRM=Type YES to continue: 
if /I not "%CONFIRM%"=="YES" goto cancelled

powershell -NoProfile -ExecutionPolicy Bypass -Command "$d=[Environment]::GetFolderPath('Desktop'); if (-not $d -or -not (Test-Path $d)) { $d = $env:USERPROFILE }; $log = Join-Path $d 'Sati-compliance-seed-apply.txt'; Unblock-File -LiteralPath '%TOOL%' -ErrorAction SilentlyContinue; & '%TOOL%' SatiProduction --apply --expect '%TOTAL%' *>&1 | Tee-Object -FilePath $log; Write-Host ''; Write-Host '============================================================'; Write-Host (' Saved to: ' + $log); Write-Host ''; Write-Host ' If it says DONE, start Sati normally. Otherwise send the file to Josh.'; Write-Host '============================================================'"

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
echo Could not find SatiComplianceSeed.exe next to this file.
echo Copy the whole folder to your Desktop and try again.
echo.
pause
exit /b 1
