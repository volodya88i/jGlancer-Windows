"""Compares jGlancer's stems with Demucs' own split of the same audio (W-85).

    python scripts/stems/compare_demucs.py <folder from `JGlancer.Tools stems`> [htdemucs|htdemucs_6s]

The folder holds `_input.wav` (the decoded song) and the app's `* - <Stem>.wav` files. Demucs runs with no random
shift and the app's overlap (0.25); the script prints, per stem, the signal-to-difference ratio in dB (higher is
closer; above 40 dB the difference is inaudible) and the largest sample difference.
"""

import struct
import sys
from pathlib import Path

import numpy as np
import torch
from demucs.apply import apply_model
from demucs.pretrained import get_model


def read_wave(path):
    data = Path(path).read_bytes()
    assert data[:4] == b"RIFF" and data[8:12] == b"WAVE"
    at, fmt, payload = 12, None, None
    while at + 8 <= len(data):
        chunk, size = data[at:at + 4], struct.unpack("<I", data[at + 4:at + 8])[0]
        body = data[at + 8:at + 8 + size]
        if chunk == b"fmt ":
            fmt = struct.unpack("<HHIIHH", body[:16])
        elif chunk == b"data":
            payload = body
        at += 8 + size + (size & 1)
    tag, channels, rate, _, _, bits = fmt
    if bits == 32 and tag in (3, 0xFFFE):
        samples = np.frombuffer(payload, dtype="<f4")
    elif bits == 24:
        raw = np.frombuffer(payload, dtype=np.uint8).reshape(-1, 3).astype(np.int32)
        value = raw[:, 0] | (raw[:, 1] << 8) | (raw[:, 2] << 16)
        samples = np.where(value >= 1 << 23, value - (1 << 24), value) / 8388607.0
    else:
        raise ValueError(f"unsupported WAVE: tag {tag}, {bits} bits")
    return samples.reshape(-1, channels).T.astype(np.float32), rate


def main():
    folder = Path(sys.argv[1])
    name = sys.argv[2] if len(sys.argv) > 2 else "htdemucs"
    torch.backends.mha.set_fastpath_enabled(False)
    model = get_model(name)
    mix, _ = read_wave(folder / "_input.wav")
    wav = torch.from_numpy(mix)
    ref = wav.mean(0)
    wav = (wav - ref.mean()) / ref.std()
    with torch.no_grad():
        sources = apply_model(model, wav[None], shifts=0, split=True, overlap=0.25, progress=False)[0]
    sources = sources * ref.std() + ref.mean()
    for index, source in enumerate(model.sources):
        ours_path = next(folder.glob(f"* - {source.capitalize()}.wav"))
        ours, _ = read_wave(ours_path)
        theirs = sources[index].numpy()
        n = min(ours.shape[1], theirs.shape[1])
        diff = ours[:, :n] - theirs[:, :n]
        snr = 10 * np.log10((theirs[:, :n] ** 2).sum() / max((diff ** 2).sum(), 1e-20))
        print(f"{source:7s} {snr:6.1f} dB   max diff {np.abs(diff).max():.4f}")


if __name__ == "__main__":
    main()
