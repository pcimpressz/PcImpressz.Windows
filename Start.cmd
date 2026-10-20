@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0dev.ps1" -Action Run
if errorlevel 1 pause
