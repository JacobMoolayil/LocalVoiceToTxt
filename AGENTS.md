# LocalVoice - AI Agent Codebase Guide (`AGENTS.md`)

This file guides AI coding assistants (Claude Opus, Gemini, GPT, etc.) to immediately understand the project architecture, locate relevant files without exploratory searches, and save context tokens/credits.

---

## 1. Project Overview & Architecture
LocalVoice is a low-latency, 100% offline Windows voice-to-text dictation application.
It consists of two decoupled subsystems:
1. **Frontend (Desktop GUI & System Integration)**: Built in **C# WPF (.NET 10)**. Handles the overlay window, non-stealing clipboard text injection, global hotkeys, and process lifecycle.
2. **Backend (Audio & AI Engine)**: Built in **Python 3.13**. Uses `faster-whisper` (CTranslate2 with NVIDIA CUDA FP16 and CPU INT8 fallback) and `Silero VAD v5` for real-time speech detection and transcription.

Communication between the WPF GUI and Python engine occurs via standard I/O (stdout/stdin) managed by `EngineProcessService.cs` and `engine_host.py`.

---

## 2. Fast File Map

### A. Frontend GUI & Services (`src/LocalVoice.App/`)
* **Window Layout, Positioning & Visuals**:
  - [`TranscriptionWindow.xaml`](src/LocalVoice.App/TranscriptionWindow.xaml) & [`TranscriptionWindow.xaml.cs`](src/LocalVoice.App/TranscriptionWindow.xaml.cs)
  - Controls window coordinates (Top, Left, Screen alignment), drag-to-move, always-on-top overlay, theme, and UI controls (mic selector, copy, clear, terminal toggle).
* **Application Lifecycle & Tray**:
  - [`App.xaml`](src/LocalVoice.App/App.xaml) & [`App.xaml.cs`](src/LocalVoice.App/App.xaml.cs)
  - Single-instance mutex, system tray icon, startup, shutdown cleanup.
* **Services (`src/LocalVoice.App/Services/`)**:
  - `AppSettingsService.cs`: Persists and loads user preferences (selected mic, hotkey, startup).
  - `AudioDeviceWatcher.cs`: Detects when Windows audio input devices change.
  - `EngineProcessService.cs`: Spawns, monitors, and communicates with the Python engine.
  - `GlobalHotkeyService.cs`: Registers and intercepts system-wide hotkeys (default: `Ctrl + Shift + Space`).
  - `TextInjectionService.cs`: Safely injects text into whatever application currently has focus without stealing focus.

### B. Python Speech & Audio Engine (`engine/`)
* [`engine_host.py`](engine/engine_host.py): Main entry point for the Python engine. Coordinates audio recording, VAD processing, and Whisper transcription, and emits JSON/events to stdout for the C# frontend.
* [`transcriber.py`](engine/transcriber.py): Manages `faster-whisper` model loading, GPU CUDA / CPU fallback, spoken punctuation replacement, and transcript generation.
* [`audio_capture.py`](engine/audio_capture.py): Captures microphone audio stream via `sounddevice`.
* [`vad_chunker.py`](engine/vad_chunker.py): Silero VAD v5 ONNX voice activity detection and chunking.
* [`config.py`](engine/config.py): Audio parameters (16kHz, mono), model settings, and thresholds.

### C. Build & Packaging Scripts (Root)
* `Run-LocalVoice.bat`: Development launcher that runs both frontend and backend.
* `package_release.ps1`: Release script that builds the self-contained WPF app and zips the distribution.

---

## 3. Strict Rules for AI Agents

1. **NEVER Search or Read Inside These Directories**:
   - `engine/venv/` (Thousands of Python virtual environment files)
   - `dist/` (Packaged release binaries and zips)
   - `src/LocalVoice.App/bin/` & `src/LocalVoice.App/obj/` (Compiler build artifacts)
2. **Targeted File Access**:
   - **UI requests** (window position, styling, buttons, colors) &rarr; Directly modify [`TranscriptionWindow.xaml`](src/LocalVoice.App/TranscriptionWindow.xaml) or its code-behind.
   - **Hotkey or text injection requests** &rarr; Directly modify files in `src/LocalVoice.App/Services/`.
   - **Speech recognition, VAD, or audio stream requests** &rarr; Directly modify files in `engine/`.
3. **Preserve Documentation & Comments**: Always preserve existing comments and docstrings.
