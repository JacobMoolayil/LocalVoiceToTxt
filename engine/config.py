from dataclasses import dataclass
from typing import Optional

@dataclass
class AppConfig:
    # Audio Settings
    sample_rate: int = 16000
    chunk_size: int = 512 # 32ms at 16kHz
    
    # VAD Settings (0.35 is sensitive and responsive)
    vad_threshold: float = 0.35
    min_speech_duration_ms: int = 200
    min_silence_duration_ms: int = 500 # Wait 500ms pause to commit
    
    # Whisper Settings
    model_size: str = "small"
    compute_type: str = "float16" # CUDA FP16
    device: str = "cuda"
    language: Optional[str] = None # None means auto-detect
    
    # Initial vocabulary bias prompt
    initial_prompt: str = "Unity, Unreal Engine, GameObject, MonoBehaviour, NavMeshAgent, ScriptableObject, C#, C++, DOTween"
