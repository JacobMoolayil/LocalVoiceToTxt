import sounddevice as sd
import numpy as np

# Find devices
devices = sd.query_devices()
print("Available input devices:")
for i, d in enumerate(devices):
    if d['max_input_channels'] > 0:
        print(f"[{i}] {d['name']} (Channels: {d['max_input_channels']})")

default_in = sd.default.device[0]
print(f"\nCurrent default input device: [{default_in}] {devices[default_in]['name']}")
