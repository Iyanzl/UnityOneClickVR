@echo off
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0Universal.ps1" -Mode Inspect -GameExe "%~1"
pause
