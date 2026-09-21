import os
import sys

# Automatically discover and add CUDA DLLs before importing ctranslate2/faster_whisper
try:
    import nvidia.cudnn
    import nvidia.cublas
    os.add_dll_directory(os.path.join(os.path.dirname(nvidia.cudnn.__file__), "bin"))
    os.add_dll_directory(os.path.join(os.path.dirname(nvidia.cublas.__file__), "bin"))
except Exception as e:
    # If not installed or fails, rely on system PATH or fallback to CPU
    pass

from faster_whisper import WhisperModel
import numpy as np
from config import AppConfig

class Transcriber:
    def __init__(self, config: AppConfig):
        self.config = config
        print(f"Loading Whisper model '{config.model_size}' on {config.device} ({config.compute_type})...", file=sys.stderr)
        
        self.model = WhisperModel(
            config.model_size,
            device=config.device,
            compute_type=config.compute_type,
            local_files_only=False
        )
        print("Whisper model loaded.", file=sys.stderr)

    def transcribe(self, audio: np.ndarray) -> str:
        """
        Transcribes the given 16kHz float32 audio array.
        Returns the transcription string.
        """
        segments, info = self.model.transcribe(
            audio,
            beam_size=5,
            language=self.config.language,
            initial_prompt=self.config.initial_prompt,
            vad_filter=False, # We do our own VAD chunking beforehand
            without_timestamps=True
        )
        
        # Combine segments (usually there's only 1 for small chunks)
        text = " ".join([segment.text for segment in segments]).strip()
        return text
