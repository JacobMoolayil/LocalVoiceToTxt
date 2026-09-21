# LocalVoice 🎙️

A lightweight, completely offline, low-latency Windows voice-to-text dictation application powered by **faster-whisper (CTranslate2)** on your **NVIDIA RTX GPU** with **Silero VAD v5** and **automatic CPU fallback**.

> **Press one global hotkey (`Ctrl + Shift + Space`) → speak naturally → text appears wherever your cursor is.**

---

## ✨ Features

- **⚡ 100% Offline & Private**: Zero cloud requests, zero telemetry. All audio processing and AI inference runs locally on your PC.
- **🎯 System-Wide Global Hotkey (`Ctrl + Shift + Space`)**: Dictate directly into any application—Notepad, VS Code, Word, Discord, Obsidian, Unity, browsers, or terminal.
- **🛡️ Safe Clipboard Injection**: Preserves your original clipboard content while providing instant, reliable text insertion across all Windows controls.
- **🎙️ Live Microphone Selector**: Automatically defaults to your Windows default microphone, with an in-window dropdown to switch inputs on the fly.
- **✨ Spoken Punctuation & Coding Vocabulary**:
  - Automatically replaces spoken words: `"comma"` &rarr; `,`, `"period"` &rarr; `.`, `"question mark"` &rarr; `?`, `"new line"` &rarr; `\n`.
  - Built-in recognition for technical game development terms: `C#`, `C++`, `GameObject`, `MonoBehaviour`, `NavMeshAgent`, `ScriptableObject`, `DOTween`, `Unreal Engine`.
- **🪟 Non-Stealing Floating Overlay**: Displays live transcription with Copy and Clear controls without stealing focus from your active window.
- **🚀 Windows Startup Support**: One-click checkbox to automatically launch LocalVoice when your PC turns on.
- **💻 Dynamic On-Demand Terminal**: Starts as a silent, pure Windows GUI app. Need to see GPU or audio levels? Check the "Show Terminal" box to pop open the console and view live and historical logs!

---

## 💻 System Requirements & Hardware Compatibility

LocalVoice is optimized for **NVIDIA RTX GPUs** using CUDA FP16, with an **automatic high-performance CPU INT8 fallback** so it can run on any Windows machine (including AMD and Intel systems).

### 🖥️ Hardware Tiers

| Hardware Category | Compatibility | Operating Mode | Expected Latency | Memory Required |
| :--- | :--- | :--- | :--- | :--- |
| **NVIDIA RTX (3050, 3060, 4050, 4060+)** | 🟢 **Optimal (Recommended)** | CUDA FP16 (GPU) | ~100ms – 200ms | 4 GB VRAM (or higher) |
| **NVIDIA GTX (1650, 1660, 1060+)** | 🟢 Supported | CUDA FP16 (GPU) | ~200ms – 300ms | 3 GB – 4 GB VRAM |
| **AMD Radeon (RX 6000, 7000 series, etc.)** | 🟡 Supported | CPU INT8 (Fallback) | ~400ms – 800ms | 8 GB System RAM |
| **Intel Arc / Iris Xe / Integrated** | 🟡 Supported | CPU INT8 (Fallback) | ~500ms – 1s | 8 GB System RAM |
| **CPU Only (Intel Core i5+, AMD Ryzen 5+)** | 🟡 Supported | CPU INT8 (AVX2) | ~500ms – 1s | 8 GB System RAM |

### 🧠 Whisper Model VRAM Guide

- **`small` (Default)**: Uses **~1.5 GB – 2.0 GB VRAM**. Perfectly balanced for high accuracy and instant speed on 4GB GPUs (like the RTX 3050 Laptop GPU).
- **`base` / `tiny`**: Uses **< 1.0 GB VRAM**. Ultra-fast for lightweight hardware.
- **`medium`**: Uses **~3.0 GB VRAM**.
- **`large-v3`**: Uses **~4.8 GB – 5.0 GB VRAM** (Recommended for GPUs with 6GB+ VRAM).

> **Note**: If no NVIDIA CUDA GPU is detected on the machine, LocalVoice **automatically switches to high-performance CPU INT8 mode** without crashing!

---

## 📦 Download & Quick Start

1. Go to the [Releases](https://github.com/JacobMoolayil/LocalVoiceToTxt/releases) page on GitHub.
2. Download **`LocalVoice-v1.0.0.zip`** and extract it anywhere on your PC.
3. Open the folder:
   * **First time only**: Double-click `engine\setup_environment.bat` to install local AI dependencies.
   * **To run**: Double-click `LocalVoice.App.exe`!
4. Click inside any text box (e.g. Notepad), press **`Ctrl + Shift + Space`**, and start speaking!

---

## 🛠️ Tech Stack & Architecture

- **Frontend**: C# / .NET 10 WPF (Win32 Global Hotkeys, Input Injection, Window Interop, Tray Management)
- **AI Speech Engine**: Python 3.13 + `faster-whisper` (CTranslate2 CUDA FP16 + CPU INT8 fallback)
- **Voice Activity Detection (VAD)**: Silero VAD v5 via ONNX Runtime + Audio Energy Floor
- **IPC Protocol**: Bidirectional asynchronous JSON-lines over standard I/O streams

---

## ⌨️ Controls & Shortcuts

| Action | Control |
| :--- | :--- |
| **Start / Stop Dictating** | `Ctrl + Shift + Space` |
| **Switch Microphone** | Select from `🎙 Mic:` dropdown in the app window |
| **Toggle Console Logs** | Check / Uncheck `Show Terminal` |
| **Launch on Boot** | Check / Uncheck `Start with Windows` |
| **Show / Hide Window** | Right-click the blue microphone icon in the System Tray |

---

## 📄 License
MIT License. Free and open source for personal and commercial use.
