import os
import urllib.request
import numpy as np
import onnxruntime as ort

class SileroVAD:
    def __init__(self, threshold=0.5, sample_rate=16000):
        self.threshold = threshold
        self.sample_rate = sample_rate
        self.model_path = os.path.join(os.path.dirname(__file__), "silero_vad.onnx")
        self._ensure_model_exists()
        
        # Load ONNX model
        opts = ort.SessionOptions()
        opts.inter_op_num_threads = 1
        opts.intra_op_num_threads = 1
        self.session = ort.InferenceSession(self.model_path, providers=['CPUExecutionProvider'], sess_options=opts)
        
        self.reset_states()

    def _ensure_model_exists(self):
        if not os.path.exists(self.model_path):
            url = "https://github.com/snakers4/silero-vad/raw/master/src/silero_vad/data/silero_vad.onnx"
            print(f"Downloading Silero VAD model from {url}...")
            urllib.request.urlretrieve(url, self.model_path)
            print("Downloaded.")

    def reset_states(self):
        self._h = np.zeros((2, 1, 64)).astype('float32')
        self._c = np.zeros((2, 1, 64)).astype('float32')

    def is_speech(self, audio_chunk: np.ndarray) -> float:
        """
        Process a chunk of audio (float32, 16kHz) and return speech probability.
        Chunk must be exactly 512 samples for 16kHz.
        """
        if len(audio_chunk) != 512:
            raise ValueError(f"Silero VAD requires chunks of 512 samples for 16kHz, got {len(audio_chunk)}")
            
        # Add batch dimension: (1, 512)
        input_data = np.expand_dims(audio_chunk, axis=0)
        
        ort_inputs = {
            'input': input_data,
            'sr': np.array([self.sample_rate], dtype='int64'),
            'h': self._h,
            'c': self._c
        }
        
        ort_outs = self.session.run(None, ort_inputs)
        
        out, self._h, self._c = ort_outs
        probability = out[0][0]
        
        return float(probability)
