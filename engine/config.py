import os
from dataclasses import dataclass, field
from typing import List, Optional

@dataclass
class AppConfig:
    # Audio Settings
    sample_rate: int = 16000
    chunk_size: int = 512 # 32ms at 16kHz
    
    # VAD Settings
    vad_threshold: float = 0.5
    min_speech_duration_ms: int = 250
    min_silence_duration_ms: int = 500 # The time to wait before finalizing a chunk
    
    # Whisper Settings
    model_size: str = "small" # options: tiny, base, small, medium
    compute_type: str = "float16" # float16 for CUDA, int8 for CPU
    device: str = "cuda"
    language: Optional[str] = None # None means auto-detect
    
    # Vocabulary & Punctuation
    initial_prompt: str = "Unity, Unreal Engine, GameObject, MonoBehaviour, NavMeshAgent, ScriptableObject, C#, C++, DOTween"
    
    # IPC Settings
    ipc_mode: str = "stdio" # "stdio" or "named_pipe"
