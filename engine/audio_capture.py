import sys
import queue
import numpy as np
import sounddevice as sd

def get_audio_devices(is_recording=False):
    """
    Returns a clean list of unique input devices and the system default index.
    Device ID -1 represents 'Windows Default'.
    When not recording, PortAudio is re-initialized to discover newly plugged/unplugged
    devices and pick up changes to the Windows default microphone.
    """
    try:
        if not is_recording:
            try:
                sd._terminate()
                sd._initialize()
            except Exception as e:
                print(f"[Audio] Re-init error: {e}", file=sys.stderr)

        devices = sd.query_devices()
        hostapis = sd.query_hostapis()
        def_idx = sd.default.device[0]
        
        # 1. Resolve raw default device name
        raw_def_name = devices[def_idx]['name'].strip() if (def_idx >= 0 and def_idx < len(devices)) else 'Default'

        # Untruncate MME name if possible by checking DirectSound/WASAPI equivalents
        clean_def_name = raw_def_name
        for d in devices:
            if d['max_input_channels'] > 0 and '@System32' not in d['name']:
                d_name = d['name'].strip()
                if d_name.startswith(raw_def_name) and len(d_name) > len(clean_def_name):
                    clean_def_name = d_name

        result = [{
            "id": -1,
            "name": f"Windows Default ({clean_def_name})",
            "is_default": True
        }]

        seen_names = set()

        # 2. Prefer DirectSound for clean names & automatic 16kHz resampling
        ds_api = next((h for h in hostapis if 'DirectSound' in h['name']), None)
        if ds_api and ds_api.get('devices'):
            for i in ds_api['devices']:
                d = devices[i]
                if d['max_input_channels'] > 0:
                    name = d['name'].strip()
                    if not name.startswith('Primary Sound Capture') and '@System32' not in name:
                        seen_names.add(name)
                        result.append({
                            "id": i,
                            "name": name,
                            "is_default": (name == clean_def_name)
                        })

        # 3. Also include any unique input devices from other host APIs not in DirectSound
        for i, d in enumerate(devices):
            if d['max_input_channels'] > 0:
                name = d['name'].strip()
                # Check if this name (or a close variant) has already been seen
                if (name not in seen_names and 
                    not any(s.startswith(name) or name.startswith(s) for s in seen_names) and
                    '@System32' not in name and 
                    not name.startswith('Microsoft Sound Mapper') and 
                    not name.startswith('Primary Sound Capture') and
                    not name.startswith('Input (')):
                    seen_names.add(name)
                    result.append({
                        "id": i,
                        "name": name,
                        "is_default": (name == clean_def_name)
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

    def refresh_devices(self):
        """
        Safely re-enumerates audio devices. If currently recording, momentarily
        cycles the active stream so PortAudio can re-enumerate Windows audio endpoints,
        pick up the new default device, and seamlessly resume recording on the new mic.
        """
        was_recording = self._is_recording
        if was_recording and self.stream:
            try:
                self.stream.stop()
                self.stream.close()
            except Exception:
                pass
            self.stream = None

        try:
            sd._terminate()
            sd._initialize()
        except Exception as e:
            print(f"[Audio] Re-init error: {e}", file=sys.stderr)

        devs, def_id = get_audio_devices(is_recording=True)

        if was_recording:
            # Re-open stream on active/default device
            device_to_use = None if self.selected_device_id == -1 else self.selected_device_id
            
            if self.selected_device_id == -1:
                try:
                    def_idx = sd.default.device[0]
                    raw_name = sd.query_devices(def_idx)['name'].strip()
                    clean_name = raw_name
                    for d in sd.query_devices():
                        if d['max_input_channels'] > 0 and '@System32' not in d['name']:
                            d_name = d['name'].strip()
                            if d_name.startswith(raw_name) and len(d_name) > len(clean_name):
                                clean_name = d_name
                    self.active_device_name = f"Windows Default ({clean_name})"
                except Exception:
                    self.active_device_name = "Windows Default"
            else:
                try:
                    self.active_device_name = sd.query_devices(self.selected_device_id)['name']
                except Exception:
                    self.active_device_name = f"Device #{self.selected_device_id}"

            print(f"[Audio] Recording seamlessly switched to: [{self.selected_device_id}] {self.active_device_name}", file=sys.stderr)

            try:
                self.stream = sd.InputStream(
                    samplerate=self.sample_rate,
                    device=device_to_use,
                    channels=1,
                    dtype='float32',
                    blocksize=self.chunk_size,
                    callback=self._audio_callback
                )
                self.stream.start()
            except Exception as e:
                print(f"[Audio Warning] Error resuming stream: {e}", file=sys.stderr)

        return devs, def_id

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
        
        # Update active device name dynamically before opening stream
        if self.selected_device_id == -1:
            try:
                def_idx = sd.default.device[0]
                raw_name = sd.query_devices(def_idx)['name'].strip()
                clean_name = raw_name
                for d in sd.query_devices():
                    if d['max_input_channels'] > 0 and '@System32' not in d['name']:
                        d_name = d['name'].strip()
                        if d_name.startswith(raw_name) and len(d_name) > len(clean_name):
                            clean_name = d_name
                self.active_device_name = f"Windows Default ({clean_name})"
            except Exception:
                self.active_device_name = "Windows Default"
        else:
            try:
                self.active_device_name = sd.query_devices(self.selected_device_id)['name']
            except Exception:
                self.active_device_name = f"Device #{self.selected_device_id}"

        device_to_use = None if self.selected_device_id == -1 else self.selected_device_id
        print(f"[Audio] Recording started from: [{self.selected_device_id}] {self.active_device_name}", file=sys.stderr)

        try:
            self.stream = sd.InputStream(
                samplerate=self.sample_rate,
                device=device_to_use,
                channels=1,
                dtype='float32',
                blocksize=self.chunk_size,
                callback=self._audio_callback
            )
            self.stream.start()
        except Exception as e:
            # Fallback to Windows default if the selected device fails or was disconnected
            if device_to_use is not None:
                print(f"[Audio Warning] Failed to open device {device_to_use}: {e}. Falling back to default device.", file=sys.stderr)
                self.selected_device_id = -1
                self.active_device_name = "Windows Default"
                self.stream = sd.InputStream(
                    samplerate=self.sample_rate,
                    device=None,
                    channels=1,
                    dtype='float32',
                    blocksize=self.chunk_size,
                    callback=self._audio_callback
                )
                self.stream.start()
            else:
                raise

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
