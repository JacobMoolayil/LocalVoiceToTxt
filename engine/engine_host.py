import sys
import json
import threading
import time
import numpy as np

from config import AppConfig
from audio_capture import AudioCapture
from vad_chunker import SileroVAD
from transcriber import Transcriber

class EngineHost:
    def __init__(self):
        self.config = AppConfig()
        self.audio_capture = AudioCapture(self.config.sample_rate, self.config.chunk_size)
        
        # Load heavy models in a separate thread to not block startup if possible, 
        # but for simplicity we load them here synchronously before sending READY.
        self.vad = SileroVAD(self.config.vad_threshold, self.config.sample_rate)
        self.transcriber = Transcriber(self.config)
        
        self.is_running = True
        self.worker_thread = threading.Thread(target=self._worker_loop, daemon=True)
        
        # Speech state
        self.speech_buffer = []
        self.is_speaking = False
        self.silence_chunks = 0
        self.interim_timer = 0
        
    def _emit(self, event_type: str, data: dict = None):
        if data is None:
            data = {}
        data['event'] = event_type
        print(json.dumps(data), flush=True)

    def _worker_loop(self):
        # Calculate thresholds based on chunk size
        # 512 samples @ 16kHz = 32ms per chunk
        chunks_per_second = self.config.sample_rate / self.config.chunk_size
        silence_threshold_chunks = int(chunks_per_second * (self.config.min_silence_duration_ms / 1000.0))
        interim_interval_chunks = int(chunks_per_second * 1.0) # Send interim every 1 sec
        
        while self.is_running:
            chunk = self.audio_capture.read_chunk(timeout=0.1)
            if chunk is None:
                continue
                
            # Run VAD
            prob = self.vad.is_speech(chunk)
            is_speech_now = prob > self.config.vad_threshold
            
            if is_speech_now:
                if not self.is_speaking:
                    self.is_speaking = True
                    self.speech_buffer = []
                    self._emit("vad_start")
                
                self.silence_chunks = 0
                self.speech_buffer.append(chunk)
                self.interim_timer += 1
                
                # Emit interim text occasionally
                if self.interim_timer >= interim_interval_chunks:
                    self.interim_timer = 0
                    if len(self.speech_buffer) > 0:
                        audio_data = np.concatenate(self.speech_buffer)
                        text = self.transcriber.transcribe(audio_data)
                        if text:
                            self._emit("interim", {"text": text})
                            
            else:
                if self.is_speaking:
                    self.silence_chunks += 1
                    self.speech_buffer.append(chunk) # Include some silence padding
                    
                    if self.silence_chunks > silence_threshold_chunks:
                        # End of phrase detected!
                        self.is_speaking = False
                        self._emit("vad_end")
                        
                        if len(self.speech_buffer) > 0:
                            audio_data = np.concatenate(self.speech_buffer)
                            # Final transcription for this segment
                            text = self.transcriber.transcribe(audio_data)
                            if text:
                                self._emit("commit", {"text": text})
                            self.speech_buffer = []

    def start(self):
        self.worker_thread.start()
        self._emit("ready")
        
        # Stdin command loop
        for line in sys.stdin:
            line = line.strip()
            if not line:
                continue
            try:
                cmd = json.loads(line)
                action = cmd.get("cmd")
                
                if action == "start":
                    self.vad.reset_states()
                    self.speech_buffer = []
                    self.is_speaking = False
                    self.silence_chunks = 0
                    self.audio_capture.start()
                    self._emit("status", {"recording": True})
                    
                elif action == "stop":
                    self.audio_capture.stop()
                    # If stopped mid-speech, flush the buffer!
                    if self.is_speaking and len(self.speech_buffer) > 0:
                        self.is_speaking = False
                        audio_data = np.concatenate(self.speech_buffer)
                        text = self.transcriber.transcribe(audio_data)
                        if text:
                            self._emit("commit", {"text": text})
                        self.speech_buffer = []
                    self._emit("status", {"recording": False})
                    
                elif action == "exit":
                    self.is_running = False
                    break
            except json.JSONDecodeError:
                pass

if __name__ == "__main__":
    host = EngineHost()
    host.start()
