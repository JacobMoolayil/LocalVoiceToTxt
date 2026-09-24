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
  - Controls window coordinates (Top, Left, Screen alignment), drag-to-move, always-on-top overlay, theme, and UI controls (mic device label, copy, clear, settings flyout).
  - **Settings Flyout Architecture**: Uses `SettingsPopup` (`StaysOpen="True"`) with window-level dismiss (`PreviewMouseDown` on the window and `Deactivated`). **Do NOT set `StaysOpen="False"`** on this popup, as WPF's global mouse-capture will intercept outside clicks, creating race conditions with the toggle button.
  - Inside Settings Flyout: Microphone selector, Whisper AI Model dropdown with dynamic hardware capability detection (greys out / disables models that exceed available GPU VRAM or CPU RAM, displaying an 'UNSUPPORTED' badge and detailed requirement tooltips), Start with Windows, and Show Terminal.
* **Application Lifecycle & Tray**:
  - [`App.xaml`](src/LocalVoice.App/App.xaml) & [`App.xaml.cs`](src/LocalVoice.App/App.xaml.cs)
  - Single-instance mutex, system tray icon, startup, model selection wiring, hardware detection event routing, shutdown cleanup.
* **Services (`src/LocalVoice.App/Services/`)**:
  - `AppSettingsService.cs`: Persists and loads user preferences in the Windows Registry (`Software\LocalVoice`): selected mic, `WhisperModel`, startup, and `ShowTerminal`.
  - `AudioDeviceWatcher.cs`: Detects when Windows audio input devices change.
  - `EngineProcessService.cs`: Spawns, monitors, and communicates with the Python engine. Passes `--model` argument on launch, parses hardware detection payloads (`is_gpu`, `vram_gb`, `ram_gb`), and sends `set_model` IPC commands for live switching.
  - `GlobalHotkeyService.cs`: Registers and intercepts system-wide hotkeys (default: `Ctrl + Shift + Space`).
  - `TextInjectionService.cs`: Safely injects text into whatever application currently has focus without stealing focus.

### B. Python Speech & Audio Engine (`engine/`)
* [`engine_host.py`](engine/engine_host.py): Main entry point for the Python engine. Coordinates audio recording, VAD processing, and Whisper transcription. Accepts `--model` CLI argument, handles dynamic model switching (`set_model` command over stdin), guards against reloading models that exceed hardware memory to prevent CUDA OOM, and emits JSON/events (`ready`, `model_info` with `is_gpu`, `vram_gb`, `ram_gb`) to stdout for the C# frontend. Resolves `"auto"` to `"small"`.
* [`transcriber.py`](engine/transcriber.py): Manages `faster-whisper` model loading with GPU CUDA FP16 and CPU INT8 fallback, dynamic model hot-reloading (`reload_model`), hardware memory detection (`_detect_gpu_memory_gb` via `nvidia-smi` and `_detect_system_ram_gb` via `GlobalMemoryStatusEx`), spoken punctuation replacement, and transcript generation.
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
4. **Always Auto-Update `AGENTS.md`**:
   - Whenever you implement a new feature, modify GUI controls or windows, alter IPC message contracts or events, add/remove services, or change backend engine behavior, you **MUST automatically update `AGENTS.md`** to keep the architecture overview, Fast File Map, and component descriptions accurate and synchronized.
   - Do NOT wait for the user to ask for documentation updates—proactively maintain this file as the authoritative single source of truth across all AI pair-programming sessions.
5. **Git Commit Permission**:
   - Never run `git commit` unless explicitly requested by the user.

---

## 4. Multi-Agent Concurrency Protocol (`WORK_IN_PROGRESS.md`)

When multiple AI models or agents (e.g. Gemini 3.8 and Claude Opus) work in parallel conversations, follow this strict synchronization protocol:

1. **Check Locks First**:
   - Before editing or writing code, read [`WORK_IN_PROGRESS.md`](WORK_IN_PROGRESS.md).
   - Check if any files you plan to touch are listed under `Locked Files` by another agent.
2. **Acquire Lock**:
   - Before modifying a file, register your agent name (e.g. `Claude Opus` or `Gemini 3.8`), your task, and the target files in [`WORK_IN_PROGRESS.md`](WORK_IN_PROGRESS.md) with status `IN PROGRESS`.
3. **Handle File Contention**:
   - If a file you need is locked by another agent, **DO NOT modify it**.
   - Check if there are other independent scripts or tasks you can work on while that file is busy. Complete those first.
   - If all other independent updates are done and you still require the locked file, **stop and wait** (inform the user or check back later). Do NOT overwrite the other agent's work.
4. **Release Lock Promptly**:
   - Immediately after your file edits are complete and verified, update [`WORK_IN_PROGRESS.md`](WORK_IN_PROGRESS.md) to set your status back to `Available` and clear the locked files so other agents can proceed.
