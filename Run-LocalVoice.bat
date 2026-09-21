@echo off
title LocalVoice - AI Voice To Text
echo ===================================================
echo Starting LocalVoice (Offline GPU Voice to Text)...
echo ===================================================
cd /d "%~dp0src\LocalVoice.App\bin\Debug\net10.0-windows"
"LocalVoice.App.exe"
pause
