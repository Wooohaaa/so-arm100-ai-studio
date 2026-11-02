@echo off
setlocal
set "UNITY_EDITOR=%LOCALAPPDATA%\Programs\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe"
if not exist "%UNITY_EDITOR%" (
  echo Unity Editor 6000.6.7f1 was not found at:
  echo %UNITY_EDITOR%
  echo Install the required Editor in Unity Hub, then run this file again.
  pause
  exit /b 1
)
start "Robot Arm Studio" "%UNITY_EDITOR%" -projectPath "%~dp0"
