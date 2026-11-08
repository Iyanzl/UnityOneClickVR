@echo off
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0Universal.ps1" -Mode Install -GameExe "%~1"
pause
