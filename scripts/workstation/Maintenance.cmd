@echo off
setlocal
pwsh -NoLogo -NoProfile -File "%~dp0Maintenance.ps1" -Menu
if errorlevel 1 pause
