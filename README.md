# LocalVoice 🎙️

A lightweight, completely offline, low-latency Windows voice-to-text dictation application powered by **faster-whisper (CTranslate2)** on your **NVIDIA RTX GPU** with **Silero VAD v5**.

> **Press one global hotkey (`Ctrl + Shift + Space`) → speak naturally → text appears wherever your cursor is.**

---

## ✨ Features

- **⚡ 100% Offline & Private**: Zero cloud requests, zero data collection. All audio inference runs locally on your NVIDIA GPU (CUDA FP16).
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

## 📦 Download & Quick Start

1. Go to the [Releases](https://github.com/JacobMoolayil/LocalVoiceToTxt/releases) page on GitHub.
2. Download **`LocalVoice-v1.0.0.zip`** and extract it anywhere on your PC.
3. Open the folder and run:
   * **First time only**: Double-click `engine\setup_environment.bat` to install the local AI dependencies.
   * **To run**: Double-click `LocalVoice.App.exe`!
4. Click inside any text box (e.g. Notepad), press **`Ctrl + Shift + Space`**, and start speaking!

---

## 🛠️ Tech Stack & Architecture

- **Frontend**: C# / .NET 10 WPF (Win32 Global Hotkeys, Input Injection, Window Interop, Tray Management)
- **AI Speech Engine**: Python 3.13 + `faster-whisper` (CTranslate2 CUDA FP16)
- **Voice Activity Detection (VAD)**: Silero VAD v5 via ONNX Runtime
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
