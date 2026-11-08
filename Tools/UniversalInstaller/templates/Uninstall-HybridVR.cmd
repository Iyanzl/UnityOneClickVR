@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0_HybridVR\Universal.ps1" -Mode Uninstall -GameRoot "%~dp0."
pause
