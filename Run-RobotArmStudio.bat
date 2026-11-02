@echo off
setlocal
set "PLAYER=%~dp0Build\Windows\RobotArmStudio.exe"
if not exist "%PLAYER%" (
  echo The standalone player has not been built yet.
  echo Run Build-RobotArmStudio.bat first, or open the project in Unity and press Play.
  pause
  exit /b 1
)
start "Robot Arm Studio" "%PLAYER%"
