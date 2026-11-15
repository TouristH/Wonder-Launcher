@echo off
rem SPDX-License-Identifier: GPL-3.0-only
rem Additional terms: see ADDITIONAL_TERMS.md in the repository root.
setlocal
set "WonderPowerShell=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if exist "%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe" set "WonderPowerShell=%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
"%WonderPowerShell%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Wonder.Runtime.ps1"
set "WonderExit=%ERRORLEVEL%"
if not "%WonderExit%"=="0" pause
exit /b %WonderExit%
