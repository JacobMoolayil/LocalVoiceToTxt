import sys
import queue
import numpy as np
import sounddevice as sd

def get_audio_devices():
    """
    Returns a clean list of unique input devices and the system default index.
    Device ID -1 represents 'Windows Default'.
    """
    try:
        devices = sd.query_devices()
        def_idx = sd.default.device[0]
        def_name = devices[def_idx]['name'] if def_idx >= 0 else 'Default'
        
        result = [{
            "id": -1,
            "name": f"Windows Default ({def_name})",
            "is_default": True
        }]
        
        seen_names = set()
        for i, d in enumerate(devices):
            if d['max_input_channels'] > 0:
                name = d['name'].strip()
                # Filter redundant internal wrappers
                if (name not in seen_names and 
                    not name.startswith('Microsoft Sound Mapper') and 
                    not name.startswith('Primary Sound Capture') and 
                    not name.startswith('Input (')):
                    seen_names.add(name)
                    result.append({
                        "id": i,
                        "name": name,
                        "is_default": (i == def_idx)
                    })
        return result, def_idx
    except Exception as e:
        print(f"[Audio Error] Error getting devices: {e}", file=sys.stderr)
        return [{"id": -1, "name": "Windows Default", "is_default": True}], -1

class AudioCapture:
    def __init__(self, sample_rate=16000, chunk_size=512):
        self.sample_rate = sample_rate
        self.chunk_size = chunk_size
        self.audio_queue = queue.Queue()
        self.stream = None
        self._is_recording = False
        
        # Default to Windows Default (-1 means device=None in sounddevice)
        self.selected_device_id = -1
        self.active_device_name = "Windows Default"

    def set_device(self, device_id):
        self.selected_device_id = device_id
        if device_id == -1:
            try:
                def_idx = sd.default.device[0]
                self.active_device_name = f"Windows Default ({sd.query_devices(def_idx)['name']})"
            except Exception:
                self.active_device_name = "Windows Default"
        else:
            try:
                self.active_device_name = sd.query_devices(device_id)['name']
            except Exception:
                self.active_device_name = f"Device #{device_id}"
                
        print(f"[Audio] Selected Input Device: [{self.selected_device_id}] {self.active_device_name}", file=sys.stderr)
        
        # If currently recording, restart stream with new device
        if self._is_recording:
            self.stop()
            self.start()

    def _audio_callback(self, indata, frames, time_info, status):
        if self._is_recording:
            data = indata.copy().flatten()
            self.audio_queue.put(data)

    def start(self):
        if self._is_recording:
            return

        self._is_recording = True
        self.audio_queue.queue.clear()
        
        # If selected_device_id is -1, use None (which tells sounddevice to use Windows Default)
        device_to_use = None if self.selected_device_id == -1 else self.selected_device_id
        
        print(f"[Audio] Recording started from: [{self.selected_device_id}] {self.active_device_name}", file=sys.stderr)

        self.stream = sd.InputStream(
            samplerate=self.sample_rate,
            device=device_to_use,
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
