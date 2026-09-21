# LocalVoice (VoiceToTxt)

A completely offline, low-latency Windows desktop voice-to-text application optimized for NVIDIA RTX GPUs.

## Architecture
- **Frontend**: .NET 10 / C# WPF (System Tray, Global Hotkeys, UI Automation text injection)
- **Engine**: Python 3.13 (faster-whisper, CTranslate2, Silero VAD via ONNX)
- **IPC**: Local Named Pipes / Standard I/O

## Features
- Real-time VAD (Voice Activity Detection)
- Incremental speech processing with zero duplicated partial text
- Fallback text injection (Direct UIA -> SendInput -> Clipboard -> Floating Window)
- 100% Local and Privacy-first
