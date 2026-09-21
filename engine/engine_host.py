import sys
import json
import threading
import time
import traceback
import numpy as np

from config import AppConfig
from audio_capture import AudioCapture
from vad_chunker import SileroVAD
from transcriber import Transcriber

class EngineHost:
    def __init__(self):
        self.config = AppConfig()
        self.audio_capture = AudioCapture(self.config.sample_rate, self.config.chunk_size)
        
        self.vad = SileroVAD(self.config.vad_threshold, self.config.sample_rate)
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
        print(json.dumps(data), flush=True)

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
                    # Print audio level pulse every ~1 sec while recording
                    print(f"[Mic Audio Level] RMS: {rms:.5f}", file=sys.stderr)

                # Run VAD
                prob = self.vad.is_speech(chunk)
                is_speech_now = prob > self.config.vad_threshold
                
                if is_speech_now:
                    if not self.is_speaking:
                        self.is_speaking = True
                        self.speech_buffer = []
                        self._emit("vad_start")
                        print(f"[VAD] Speech started! (prob: {prob:.2f}, rms: {rms:.4f})", file=sys.stderr)
                    
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

    def start(self):
        self.worker_thread.start()
        self._emit("ready")
        print("\n=======================================================", file=sys.stderr)
        print(" LocalVoice Engine is READY! (RTX 3050 CUDA FP16)", file=sys.stderr)
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
                    
                    # 2. Fail-safe: If nothing was committed, but audio was recorded
                    elif not self.has_committed_in_session and len(self.all_session_audio) > int(16000 * 0.4 / 512):
                        audio_data = np.concatenate(self.all_session_audio)
                        rms = float(np.sqrt(np.mean(audio_data**2)))
                        print(f"[Fail-safe Check] Total audio duration: {len(audio_data)/16000:.1f}s, RMS: {rms:.5f}", file=sys.stderr)
                        if rms > 0.001: # Check if there is actual sound
                            text = self.transcriber.transcribe(audio_data)
                            if text:
                                print(f"[Transcribed Fail-safe] {text}", file=sys.stderr)
                                self._emit("commit", {"text": text})
                                
                    self.is_speaking = False
                    self.all_session_audio = []
                    self._emit("status", {"recording": False})
                    
                elif action == "exit":
                    self.is_running = False
                    break
            except Exception as e:
                print(f"[Command Error] {e}", file=sys.stderr)

if __name__ == "__main__":
    host = EngineHost()
    host.start()
