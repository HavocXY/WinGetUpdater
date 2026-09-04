@echo off
rem Startet build.ps1 in einem PowerShell-Fenster, das nach Abschluss offen bleibt.
rem Zusatzargumente werden durchgereicht, z. B.: build-interactive.cmd -SkipTests
powershell -NoExit -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
