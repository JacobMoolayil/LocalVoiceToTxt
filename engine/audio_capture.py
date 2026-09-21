import queue
import numpy as np
import sounddevice as sd
import threading

class AudioCapture:
    def __init__(self, sample_rate=16000, chunk_size=512):
        self.sample_rate = sample_rate
        self.chunk_size = chunk_size
        self.audio_queue = queue.Queue()
        self.stream = None
        self._is_recording = False

    def _audio_callback(self, indata, frames, time, status):
        """This is called for each audio block by sounddevice."""
        if status:
            # We can log status if needed (e.g. input overflow)
            pass
        if self._is_recording:
            # sounddevice gives us a 2D array (frames x channels). We want 1D numpy array of float32.
            # indata is float32 between -1.0 and 1.0
            data = indata.copy().flatten()
            self.audio_queue.put(data)

    def start(self, device_index=None):
        if self._is_recording:
            return

        self._is_recording = True
        self.audio_queue.queue.clear()
        
        self.stream = sd.InputStream(
            samplerate=self.sample_rate,
            device=device_index,
            channels=1,
            dtype='float32',
            blocksize=self.chunk_size,
            callback=self._audio_callback
        )
        self.stream.start()

    def stop(self):
        self._is_recording = False
        if self.stream:
            self.stream.stop()
            self.stream.close()
            self.stream = None

    def read_chunk(self, timeout=0.1):
        """Read a chunk from the queue. Returns None if empty."""
        try:
            return self.audio_queue.get(timeout=timeout)
        except queue.Empty:
            return None
