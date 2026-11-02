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
if not exist "%~dp0Logs" mkdir "%~dp0Logs"
start "Robot Arm Studio build" /wait "%UNITY_EDITOR%" -batchmode -nographics -projectPath "%~dp0" -executeMethod RobotArmStudio.EditorTools.BuildStandalone.BuildWindows -quit -logFile "%~dp0Logs\Build-Windows.log"
if errorlevel 1 (
  echo Build failed. See Logs\Build-Windows.log
  pause
  exit /b 1
)
echo Build complete: Build\Windows\RobotArmStudio.exe
pause
