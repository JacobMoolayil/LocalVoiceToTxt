@echo off
title LocalVoice - Python Environment Setup
echo =======================================================
echo Setting up LocalVoice AI Engine Environment...
echo =======================================================

cd /d "%~dp0"

where python >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Python is not found in PATH!
    echo Please install Python 3.10-3.13 from python.org (check "Add Python to PATH").
    pause
    exit /b 1
)

if not exist "venv" (
    echo [1/3] Creating Python virtual environment (venv)...
    python -m venv venv
)

echo [2/3] Upgrading pip...
venv\Scripts\python.exe -m pip install --upgrade pip

echo [3/3] Installing faster-whisper, sounddevice, and CUDA acceleration libraries...
venv\Scripts\python.exe -m pip install -r requirements.txt

echo =======================================================
echo Setup complete! LocalVoice Engine is ready.
echo You can now launch LocalVoice.App.exe.
echo =======================================================
pause
