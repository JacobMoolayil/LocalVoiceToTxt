from dataclasses import dataclass
from typing import Optional

@dataclass
class AppConfig:
    # Audio Settings
    sample_rate: int = 16000
    chunk_size: int = 512
    
    # VAD Settings
    vad_threshold: float = 0.30
    energy_threshold: float = 0.009 # RMS threshold for microphone
    min_silence_duration_ms: int = 500
    
    # Whisper Settings
    model_size: str = "small"
    compute_type: str = "float16"
    device: str = "cuda"
    language: Optional[str] = None # None means auto-detect
    
    # Initial vocabulary prompt with punctuation cues
    initial_prompt: str = "Hello, comma, period. Voice to text dictation in C#, Unity, Unreal Engine, GameObject, MonoBehaviour."
