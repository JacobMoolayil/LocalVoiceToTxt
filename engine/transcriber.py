import os
import sys
import re

# Automatically discover and add CUDA DLLs before importing ctranslate2/faster_whisper
try:
    import nvidia.cublas
    import nvidia.cudnn
    cublas_dir = list(nvidia.cublas.__path__)[0]
    cudnn_dir = list(nvidia.cudnn.__path__)[0]
    cublas_bin = os.path.join(cublas_dir, 'bin')
    cudnn_bin = os.path.join(cudnn_dir, 'bin')
    
    os.environ['PATH'] = cublas_bin + os.pathsep + cudnn_bin + os.pathsep + os.environ.get('PATH', '')
    if hasattr(os, 'add_dll_directory'):
        if os.path.exists(cublas_bin):
            os.add_dll_directory(cublas_bin)
        if os.path.exists(cudnn_bin):
            os.add_dll_directory(cudnn_bin)
except Exception as e:
    pass

from faster_whisper import WhisperModel
import numpy as np
from config import AppConfig

PUNCTUATION_MAP = [
    (r'\b(comma|coma|goma|koma)\b', ','),
    (r'\b(period|full stop|fullstop)\b', '.'),
    (r'\b(question mark)\b', '?'),
    (r'\b(exclamation mark|exclamation point)\b', '!'),
    (r'\b(colon)\b', ':'),
    (r'\b(semicolon)\b', ';'),
    (r'\b(new line)\b', '\n'),
    (r'\b(new paragraph)\b', '\n\n'),
]

VOCABULARY_MAP = [
    (r'\b(shisha|c sharp|see sharp)\b', 'C#'),
    (r'\b(game\s*object)\b', 'GameObject'),
    (r'\b(mono\s*behaviour)\b', 'MonoBehaviour'),
    (r'\b(nav\s*mesh\s*agent)\b', 'NavMeshAgent'),
    (r'\b(scriptable\s*object)\b', 'ScriptableObject'),
    (r'\b(c\s*plus\s*plus)\b', 'C++'),
    (r'\b(do\s*tween)\b', 'DOTween'),
    (r'\b(unreal\s*engine)\b', 'Unreal Engine'),
]

class Transcriber:
    def __init__(self, config: AppConfig):
        self.config = config
        self.model_name = config.model_size
        try:
            print(f"Loading Whisper model '{config.model_size}' on {config.device} ({config.compute_type})...", file=sys.stderr)
            self.model = WhisperModel(
                config.model_size,
                device=config.device,
                compute_type=config.compute_type,
                local_files_only=False
            )
            print("Whisper model loaded on GPU (CUDA FP16).", file=sys.stderr)
            self.actual_device = config.device
            self.actual_compute_type = config.compute_type
            self.hardware_name = self._detect_gpu_name()
        except Exception as e:
            print(f"[Warning] GPU acceleration unavailable ({e}). Falling back to CPU mode (int8)...", file=sys.stderr)
            self.model = WhisperModel(
                config.model_size,
                device="cpu",
                compute_type="int8",
                local_files_only=False
            )
            print("Whisper model loaded on CPU (int8 mode).", file=sys.stderr)
            self.actual_device = "cpu"
            self.actual_compute_type = "int8"
            self.hardware_name = self._detect_cpu_name()

    def _detect_gpu_name(self) -> str:
        try:
            import subprocess
            out = subprocess.check_output(
                ['nvidia-smi', '--query-gpu=name', '--format=csv,noheader'],
                text=True, stderr=subprocess.DEVNULL
            ).strip()
            if out:
                name = out.replace("NVIDIA GeForce ", "").replace("NVIDIA ", "").strip()
                return name.replace(" Laptop GPU", "")
        except Exception:
            pass
        return "RTX 3050"

    def _detect_cpu_name(self) -> str:
        try:
            import platform
            proc = platform.processor()
            if proc:
                return "CPU"
        except Exception:
            pass
        return "CPU"

    def _post_process(self, text: str) -> str:
        if not text:
            return ""
            
        # 1. Custom Technical Vocabulary
        for pattern, repl in VOCABULARY_MAP:
            text = re.sub(pattern, repl, text, flags=re.IGNORECASE)
            
        # 2. Spoken Punctuation
        for pattern, repl in PUNCTUATION_MAP:
            text = re.sub(pattern, repl, text, flags=re.IGNORECASE)
            
        # 3. Clean up spaces and duplicate punctuation
        text = re.sub(r'[,]+', ',', text)
        text = re.sub(r'[.]+', '.', text)
        text = re.sub(r'([,])\.', r'\1', text)
        text = re.sub(r'\s+([,.\?!:;])', r'\1', text)
        return text.strip()

    def transcribe(self, audio: np.ndarray) -> str:
        if len(audio) < 1600:
            return ""

        segments, info = self.model.transcribe(
            audio,
            beam_size=5,
            language=self.config.language,
            initial_prompt=self.config.initial_prompt,
            vad_filter=False,
            without_timestamps=True
        )
        
        raw_text = " ".join([segment.text for segment in segments]).strip()
        final_text = self._post_process(raw_text)
        return final_text
