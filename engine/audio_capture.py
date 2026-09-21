import sys
import queue
import numpy as np
import sounddevice as sd

def find_best_input_device():
    try:
        devices = sd.query_devices()
        default_in = sd.default.device[0]
        
        # Priority 1: Realtek Microphone Array (laptop physical mic)
        for i, d in enumerate(devices):
            if d['max_input_channels'] > 0:
                name = d['name'].lower()
                if 'microphone array' in name and 'realtek' in name:
                    return i, d['name']
                    
        # Priority 2: Any Microphone Array
        for i, d in enumerate(devices):
            if d['max_input_channels'] > 0:
                if 'microphone array' in d['name'].lower():
                    return i, d['name']
                    
        # Priority 3: Default input
        if default_in >= 0:
            return default_in, devices[default_in]['name']
    except Exception as e:
        print(f"[Audio Error] Error detecting audio devices: {e}", file=sys.stderr)
        
    return None, "Default Device"

class AudioCapture:
    def __init__(self, sample_rate=16000, chunk_size=512):
        self.sample_rate = sample_rate
        self.chunk_size = chunk_size
        self.audio_queue = queue.Queue()
        self.stream = None
        self._is_recording = False
        self.active_device_index = None
        self.active_device_name = None

    def _audio_callback(self, indata, frames, time_info, status):
        if status:
            pass
        if self._is_recording:
            data = indata.copy().flatten()
            self.audio_queue.put(data)

    def start(self, device_index=None):
        if self._is_recording:
            return

        self._is_recording = True
        self.audio_queue.queue.clear()
        
        if device_index is None:
            dev_idx, dev_name = find_best_input_device()
        else:
            dev_idx = device_index
            dev_name = sd.query_devices(dev_idx)['name']
            
        self.active_device_index = dev_idx
        self.active_device_name = dev_name
        
        print(f"[Audio] Recording from: [{dev_idx}] {dev_name}", file=sys.stderr)

        self.stream = sd.InputStream(
            samplerate=self.sample_rate,
            device=dev_idx,
            channels=1,
            dtype='float32',
            blocksize=self.chunk_size,
            callback=self._audio_callback
        )
        self.stream.start()

    def stop(self):
        self._is_recording = False
        if self.stream:
            try:
                self.stream.stop()
                self.stream.close()
            except Exception:
                pass
            self.stream = None

    def read_chunk(self, timeout=0.1):
        try:
            return self.audio_queue.get(timeout=timeout)
        except queue.Empty:
            return None
