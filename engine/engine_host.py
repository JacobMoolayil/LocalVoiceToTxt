import sys
import json
import threading
import time
import traceback
import numpy as np

from config import AppConfig
from audio_capture import AudioCapture, get_audio_devices
from vad_chunker import SileroVAD
from transcriber import Transcriber

MODEL_VRAM_REQUIREMENTS = {
    "tiny": 1.0,
    "base": 1.5,
    "small": 2.0,
    "medium": 5.0,
    "turbo": 6.0,
    "large-v3": 10.0,
    "large": 10.0,
}

MODEL_RAM_REQUIREMENTS = {
    "tiny": 1.5,
    "base": 2.0,
    "small": 3.5,
    "medium": 6.0,
    "turbo": 8.0,
    "large-v3": 12.0,
    "large": 12.0,
}

def resolve_model_size(model_name: str) -> str:
    """Resolves 'auto' to a balanced default model ('small') or normalizes the model name."""
    if not model_name or model_name.strip().lower() == "auto":
        return "small"
    return model_name.strip().lower()

class EngineHost:
    def __init__(self, requested_model: str = "auto"):
        self.requested_model = requested_model
        resolved_model = resolve_model_size(requested_model)
        
        self._emit("loading", {"message": "Initializing audio capture and VAD..."})
        self.config = AppConfig()
        self.config.model_size = resolved_model
        self.audio_capture = AudioCapture(self.config.sample_rate, self.config.chunk_size)
        
        self.vad = SileroVAD(self.config.vad_threshold, self.config.sample_rate)
        self._emit("loading", {"message": f"Loading Whisper AI model '{resolved_model}'..."})
        self.transcriber = Transcriber(self.config)
        
        self.is_running = True
        self.worker_thread = threading.Thread(target=self._worker_loop, daemon=True)
        
        # Speech state
        self.speech_buffer = []
        self.all_session_audio = []
        self.is_speaking = False
        self.silence_chunks = 0
        self.interim_timer = 0
        self.has_committed_in_session = False
        
    def _emit(self, event_type: str, data: dict = None):
        if data is None:
            data = {}
        data['event'] = event_type
        payload = json.dumps(data)
        print(payload, flush=True)

    def _worker_loop(self):
        chunks_per_second = self.config.sample_rate / self.config.chunk_size
        silence_threshold_chunks = int(chunks_per_second * (self.config.min_silence_duration_ms / 1000.0))
        interim_interval_chunks = int(chunks_per_second * 0.7)
        
        log_counter = 0

        while self.is_running:
            try:
                chunk = self.audio_capture.read_chunk(timeout=0.1)
                if chunk is None:
                    continue

                self.all_session_audio.append(chunk)

                # Audio RMS level
                rms = float(np.sqrt(np.mean(chunk**2)))
                log_counter += 1
                if log_counter % 30 == 0 and self.audio_capture._is_recording:
                    print(f"[Mic Audio Level] RMS: {rms:.5f}", file=sys.stderr)

                # Run VAD & Energy test
                prob = self.vad.is_speech(chunk)
                is_speech_now = (prob > self.config.vad_threshold) or (rms > self.config.energy_threshold)
                
                if is_speech_now:
                    if not self.is_speaking:
                        self.is_speaking = True
                        self.speech_buffer = []
                        self._emit("vad_start")
                        print(f"[VAD] Speech started! (rms: {rms:.4f})", file=sys.stderr)
                    
                    self.silence_chunks = 0
                    self.speech_buffer.append(chunk)
                    self.interim_timer += 1
                    
                    if self.interim_timer >= interim_interval_chunks:
                        self.interim_timer = 0
                        if len(self.speech_buffer) > 0:
                            audio_data = np.concatenate(self.speech_buffer)
                            text = self.transcriber.transcribe(audio_data)
                            if text:
                                print(f"[Interim] {text}", file=sys.stderr)
                                self._emit("interim", {"text": text})
                                
                else:
                    if self.is_speaking:
                        self.silence_chunks += 1
                        self.speech_buffer.append(chunk)
                        
                        if self.silence_chunks > silence_threshold_chunks:
                            # Natural pause detected
                            self.is_speaking = False
                            self._emit("vad_end")
                            print("[VAD] Speech pause detected. Transcribing...", file=sys.stderr)
                            
                            if len(self.speech_buffer) > 0:
                                audio_data = np.concatenate(self.speech_buffer)
                                text = self.transcriber.transcribe(audio_data)
                                if text:
                                    print(f"[Transcribed] {text}", file=sys.stderr)
                                    self._emit("commit", {"text": text})
                                    self.has_committed_in_session = True
                                self.speech_buffer = []

            except Exception as e:
                print(f"[Worker Error] {e}", file=sys.stderr)
                traceback.print_exc(file=sys.stderr)
                time.sleep(0.05)

    def _get_model_info_payload(self):
        actual_dev = getattr(self.transcriber, "actual_device", self.config.device)
        actual_comp = getattr(self.transcriber, "actual_compute_type", self.config.compute_type)
        hw_name = getattr(self.transcriber, "hardware_name", "RTX 3050")
        is_gpu = getattr(self.transcriber, "is_gpu", actual_dev == "cuda")
        vram_gb = getattr(self.transcriber, "vram_gb", 0.0)
        ram_gb = getattr(self.transcriber, "ram_gb", 8.0)
        comp_str = "FP16" if "16" in actual_comp else actual_comp.upper()
        if actual_dev == "cuda":
            hw_label = f"GPU: {hw_name} (CUDA {comp_str})"
        else:
            hw_label = f"CPU ({comp_str})" if hw_name == "CPU" else f"CPU: {hw_name} ({comp_str})"

        model_name = getattr(self.transcriber, "model_name", self.config.model_size)
        requested_model = getattr(self, "requested_model", "auto")
        return {
            "model": model_name,
            "requested_model": requested_model,
            "device": actual_dev,
            "compute_type": actual_comp,
            "hardware_name": hw_name,
            "hardware_label": hw_label,
            "label": f"{model_name} | {hw_label}",
            "is_gpu": is_gpu,
            "vram_gb": vram_gb,
            "ram_gb": ram_gb
        }

    def start(self):
        self.worker_thread.start()
        model_info = self._get_model_info_payload()
        self._emit("ready", model_info)
        self._emit("model_info", model_info)
        
        # Emit available audio devices
        devs, def_id = self.audio_capture.refresh_devices()
        self._emit("devices", {
            "devices": devs,
            "selected_id": self.audio_capture.selected_device_id
        })
        
        print("\n=======================================================", file=sys.stderr)
        print(f" LocalVoice Engine is READY! (Whisper: {model_info['label']})", file=sys.stderr)
        print(" Default Microphone: Windows Default", file=sys.stderr)
        print(" Press Ctrl+Shift+Space to Dictate", file=sys.stderr)
        print("=======================================================\n", file=sys.stderr)
        
        for line in sys.stdin:
            line = line.strip()
            if not line:
                continue
            try:
                cmd = json.loads(line)
                action = cmd.get("cmd")
                
                if action == "start":
                    print("\n>>> START RECORDING <<<", file=sys.stderr)
                    self.vad.reset_states()
                    self.speech_buffer = []
                    self.all_session_audio = []
                    self.is_speaking = False
                    self.silence_chunks = 0
                    self.has_committed_in_session = False
                    self.audio_capture.start()
                    self._emit("status", {"recording": True})
                    
                elif action == "stop":
                    print(">>> STOP RECORDING <<<", file=sys.stderr)
                    self.audio_capture.stop()
                    
                    # 1. If buffer has speech, transcribe it
                    if len(self.speech_buffer) > 0:
                        audio_data = np.concatenate(self.speech_buffer)
                        text = self.transcriber.transcribe(audio_data)
                        if text:
                            print(f"[Transcribed Final] {text}", file=sys.stderr)
                            self._emit("commit", {"text": text})
                            self.has_committed_in_session = True
                        self.speech_buffer = []
                    
                    # 2. Fail-safe: If nothing was committed, transcribe full audio
                    elif not self.has_committed_in_session and len(self.all_session_audio) > int(16000 * 0.4 / 512):
                        audio_data = np.concatenate(self.all_session_audio)
                        rms = float(np.sqrt(np.mean(audio_data**2)))
                        print(f"[Fail-safe Check] Total audio duration: {len(audio_data)/16000:.1f}s, RMS: {rms:.5f}", file=sys.stderr)
                        if rms > 0.001:
                            text = self.transcriber.transcribe(audio_data)
                            if text:
                                print(f"[Transcribed Fail-safe] {text}", file=sys.stderr)
                                self._emit("commit", {"text": text})
                                
                    self.is_speaking = False
                    self.all_session_audio = []
                    self._emit("status", {"recording": False})

                    # Always ensure devices are fresh on stop
                    devs, def_id = self.audio_capture.refresh_devices()
                    self._emit("devices", {
                        "devices": devs,
                        "selected_id": self.audio_capture.selected_device_id
                    })
                    
                elif action == "list_devices":
                    devs, def_id = self.audio_capture.refresh_devices()
                    self._emit("devices", {
                        "devices": devs,
                        "selected_id": self.audio_capture.selected_device_id
                    })
                    
                elif action == "set_device":
                    dev_id = cmd.get("device_id", -1)
                    self.audio_capture.set_device(dev_id)
                    self._emit("device_changed", {"device_id": dev_id})

                elif action == "set_model":
                    req_model = cmd.get("model", "auto")
                    resolved = resolve_model_size(req_model)
                    self.requested_model = req_model
                    print(f"\n[Engine] Received command to switch Whisper model to '{req_model}' (resolved: {resolved})", file=sys.stderr)

                    # Hardware suitability check
                    is_gpu = getattr(self.transcriber, "is_gpu", self.transcriber.actual_device == "cuda")
                    vram_gb = getattr(self.transcriber, "vram_gb", 0.0)
                    ram_gb = getattr(self.transcriber, "ram_gb", 8.0)
                    min_vram = MODEL_VRAM_REQUIREMENTS.get(resolved, 0.0)
                    min_ram = MODEL_RAM_REQUIREMENTS.get(resolved, 0.0)

                    if is_gpu and vram_gb > 0 and min_vram > vram_gb:
                        print(f"[Engine Warning] Model '{resolved}' requires ~{min_vram} GB VRAM, but device has {vram_gb} GB VRAM. Rejecting model switch to prevent crash.", file=sys.stderr)
                        info = self._get_model_info_payload()
                        self._emit("model_info", info)
                        continue

                    if not is_gpu and min_ram > ram_gb:
                        print(f"[Engine Warning] Model '{resolved}' requires ~{min_ram} GB RAM, but system has {ram_gb} GB RAM. Rejecting model switch to prevent freeze.", file=sys.stderr)
                        info = self._get_model_info_payload()
                        self._emit("model_info", info)
                        continue

                    self._emit("loading", {"message": f"Switching to Whisper model '{resolved}'..."})
                    try:
                        if self.audio_capture._is_recording:
                            self.audio_capture.stop()
                            self._emit("status", {"recording": False})

                        self.transcriber.reload_model(resolved)
                        info = self._get_model_info_payload()
                        self._emit("model_info", info)
                        self._emit("ready", info)
                        print(f"[Engine] Whisper model successfully switched to: {resolved} ({info['hardware_label']})", file=sys.stderr)
                    except Exception as ex:
                        print(f"[Engine Error] Failed to switch Whisper model: {ex}", file=sys.stderr)
                        traceback.print_exc(file=sys.stderr)
                        self._emit("ready", self._get_model_info_payload())

                elif action == "get_model_info":
                    self._emit("model_info", self._get_model_info_payload())
                    
                elif action == "exit":
                    self.cleanup_and_exit()
            except Exception as e:
                print(f"[Command Error] {e}", file=sys.stderr)

        # Stdin EOF reached (parent closed standard input)
        self.cleanup_and_exit()

    def cleanup_and_exit(self):
        print("\n[Engine] Shutdown signal received. Offloading GPU model & terminating...", file=sys.stderr)
        self.is_running = False
        try:
            self.audio_capture.stop()
        except Exception:
            pass
        try:
            import sounddevice as sd
            sd._terminate()
        except Exception:
            pass
        try:
            if hasattr(self, 'transcriber') and hasattr(self.transcriber, 'model'):
                del self.transcriber.model
        except Exception:
            pass
        try:
            import gc
            gc.collect()
        except Exception:
            pass
        print("[Engine] GPU memory offloaded successfully. Process terminating.", file=sys.stderr)
        import os
        os._exit(0)

if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", type=str, default="auto", help="Whisper model size")
    args, _ = parser.parse_known_args()

    host = EngineHost(requested_model=args.model)
    try:
        host.start()
    finally:
        host.cleanup_and_exit()
