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
            print(f"Downloading Silero VAD model from {url}...", file=sys.stderr)
            urllib.request.urlretrieve(url, self.model_path)
            print("Downloaded.", file=sys.stderr)

    def reset_states(self):
        # Silero VAD v5 uses [2, batch_size, 128]
        self._state = np.zeros((2, 1, 128), dtype=np.float32)

    def is_speech(self, audio_chunk: np.ndarray) -> float:
        """
        Process a chunk of audio (float32, 16kHz) and return speech probability.
        Chunk must be exactly 512 samples for 16kHz.
        """
        if len(audio_chunk) != 512:
            raise ValueError(f"Silero VAD requires chunks of 512 samples for 16kHz, got {len(audio_chunk)}")
            
        # Add batch dimension: (1, 512)
        input_data = np.expand_dims(audio_chunk.astype(np.float32), axis=0)
        sr = np.array(self.sample_rate, dtype=np.int64)
        
        ort_inputs = {
            'input': input_data,
            'state': self._state,
            'sr': sr
        }
        
        out, self._state = self.session.run(None, ort_inputs)
        probability = float(out[0][0])
        
        return probability
