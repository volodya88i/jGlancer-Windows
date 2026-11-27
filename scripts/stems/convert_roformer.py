"""Converts Mel-Band RoFormer (Kimberley Jensen's vocals model, MIT) to ONNX for jGlancer's 2-stem split at High
Quality (MAC-056, W-120).

Developer machine only (needs PyTorch, onnx, onnxruntime and numpy; the weights are MelBandRoformer.ckpt from
huggingface.co/KimberleyJSN/melbandroformer, read as weights only, so no code from the file runs):

    python scripts/stems/convert_roformer.py <MelBandRoformer.ckpt> <output folder>
    python scripts/stems/convert_roformer.py <MelBandRoformer.ckpt> <output folder> reference
    python scripts/stems/convert_roformer.py <MelBandRoformer.ckpt> <model.onnx> check

The network is written out below with the shapes of `config_vocals_mel_band_roformer.yaml` (dim 384, depth 6, 60 mel
bands, STFT 2048 / hop 441, stereo, one stem: the vocals), as the Mac's `convert_roformer_coreml.py` has it; the
checkpoint must load into it strictly. ONNX gets only the network. The app's C# code computes the STFT, gathers each
band's bins, then averages the masks where bands overlap, applies them and inverts the STFT
(`JGlancer.Core.Stems.BandSpectrogram`, `MelBands`, `RoFormerSeparator`). The model takes

    bands float32 [1, 801, 7916]  the 801 STFT frames of an 8 s stereo piece (352800 samples at 44.1 kHz); per frame,
                                  band after band, its bins ascending, left then right, real then imaginary

and returns

    masks float32 [1, 801, 7916]  the complex vocals mask in the same layout

The first form writes `mel_band_roformer-full.onnx` (float32 weights) and prints its SHA-256 and size; then
`half_weights.py` makes `mel_band_roformer.onnx` (the compact one: float16 weights, float32 compute). `check` runs an
ONNX file on the CPU against this script's PyTorch forward and prints the masks and vocals in dB. `reference` writes
`reference.f32` (a 12 s test mix, planar stereo float32) and `reference-vocals.f32` (PyTorch's vocals of it, the app's
way: 8 s pieces, half overlap, triangular weights) for the app's test (`JG_ROFORMER_MODEL`, `JG_ROFORMER_REFERENCE`).
"""

import hashlib
import math
import sys
import time
from pathlib import Path

import numpy as np
import torch
from torch import nn

SAMPLE_RATE = 44100
N_FFT = 2048
HOP = 441
CHUNK = 352800
FRAMES = CHUNK // HOP + 1
BANDS = 60
DIM = 384
DEPTH = 6
HEADS = 8
DIM_HEAD = 64
# Attention scores computed at once, in values: 10 sequences of the time attention (10 × 8 × 801 × 801).
ATTENTION_BUDGET = 52_000_000


def mel_bands():
    """Each band's first bin and count: librosa.filters.mel(sr=44100, n_fft=2048, n_mels=60) (Slaney) > 0, with the
    first bin of the first band and the last bin of the last forced in, as MelBandRoformer does."""
    def hz_to_mel(hz):
        hz = np.asanyarray(hz, dtype=np.float64)
        f_sp = 200.0 / 3
        mels = hz / f_sp
        min_log_mel = 1000.0 / f_sp
        logstep = np.log(6.4) / 27.0
        return np.where(hz >= 1000.0, min_log_mel + np.log(np.maximum(hz, 1e-10) / 1000.0) / logstep, mels)

    def mel_to_hz(mels):
        f_sp = 200.0 / 3
        min_log_mel = 1000.0 / f_sp
        logstep = np.log(6.4) / 27.0
        return np.where(mels >= min_log_mel, 1000.0 * np.exp(logstep * (mels - min_log_mel)), f_sp * mels)

    fft_freqs = np.fft.rfftfreq(n=N_FFT, d=1.0 / SAMPLE_RATE)
    mel_f = mel_to_hz(np.linspace(hz_to_mel(0.0), hz_to_mel(SAMPLE_RATE / 2), BANDS + 2))
    fdiff = np.diff(mel_f)
    ramps = np.subtract.outer(mel_f, fft_freqs)
    weights = np.zeros((BANDS, N_FFT // 2 + 1), dtype=np.float32)
    for i in range(BANDS):
        lower = -ramps[i] / fdiff[i]
        upper = ramps[i + 2] / fdiff[i + 1]
        weights[i] = np.maximum(0, np.minimum(lower, upper))
    weights *= (2.0 / (mel_f[2:BANDS + 2] - mel_f[:BANDS]))[:, None]
    weights[0, 0] = 1
    weights[-1, -1] = 1
    bands = []
    for row in weights > 0:
        bins = np.flatnonzero(row)
        assert len(bins) == bins[-1] - bins[0] + 1, "a band is not contiguous"
        bands.append((int(bins[0]), len(bins)))
    return bands


def rms_norm(x, gamma):
    return x / x.pow(2).sum(dim=-1, keepdim=True).sqrt().clamp(min=1e-12) * (x.shape[-1] ** 0.5) * gamma


class RMSNorm(nn.Module):
    def __init__(self, dim):
        super().__init__()
        self.gamma = nn.Parameter(torch.ones(dim))

    def forward(self, x):
        return rms_norm(x, self.gamma)


class Rotary(nn.Module):
    """rotary_embedding_torch's RotaryEmbedding(dim=64) as used: positions 0…n-1, frequencies interleaved in pairs."""

    def __init__(self):
        super().__init__()
        self.freqs = nn.Parameter(torch.zeros(DIM_HEAD // 2), requires_grad=False)

    def forward(self, t):
        n = t.shape[-2]
        angles = torch.arange(n, dtype=torch.float32, device=t.device)[:, None] * self.freqs[None, :]
        angles = angles.repeat_interleave(2, dim=-1)
        pairs = t.reshape(*t.shape[:-1], DIM_HEAD // 2, 2)
        rotated = torch.stack((-pairs[..., 1], pairs[..., 0]), dim=-1).reshape(t.shape)
        return t * angles.cos() + rotated * angles.sin()


class Attention(nn.Module):
    def __init__(self, rotary):
        super().__init__()
        self.rotary_embed = rotary
        self.norm = RMSNorm(DIM)
        self.to_qkv = nn.Linear(DIM, HEADS * DIM_HEAD * 3, bias=False)
        self.to_gates = nn.Linear(DIM, HEADS)
        self.to_out = nn.Sequential(nn.Linear(HEADS * DIM_HEAD, DIM, bias=False))

    def forward(self, x):
        x = self.norm(x)
        b, n, _ = x.shape
        q, k, v = self.to_qkv(x).reshape(b, n, 3, HEADS, DIM_HEAD).permute(2, 0, 3, 1, 4)
        q = self.rotary_embed(q)
        k = self.rotary_embed(k)
        # The time attention's scores of all 60 bands at once are 1.2 GB (60 × 8 × 801 × 801), several such at a time,
        # more than a GTX 1070's 8 GB hold: on Windows DirectML then pages them through system memory, 8 s a piece.
        # The same arithmetic a few sequences at a time keeps each under 210 MB (W-120).
        step = max(1, ATTENTION_BUDGET // (HEADS * n * n))
        outs = []
        for start in range(0, b, step):
            scores = torch.matmul(q[start:start + step], k[start:start + step].transpose(-1, -2)) * DIM_HEAD ** -0.5
            outs.append(torch.matmul(torch.softmax(scores, dim=-1), v[start:start + step]))
        out = torch.cat(outs, dim=0) if len(outs) > 1 else outs[0]
        out = out * torch.sigmoid(self.to_gates(x)).permute(0, 2, 1)[..., None]
        return self.to_out(out.permute(0, 2, 1, 3).reshape(b, n, HEADS * DIM_HEAD))


class FeedForward(nn.Module):
    def __init__(self):
        super().__init__()
        self.net = nn.Sequential(
            RMSNorm(DIM), nn.Linear(DIM, DIM * 4), nn.GELU(), nn.Identity(), nn.Linear(DIM * 4, DIM)
        )

    def forward(self, x):
        return self.net(x)


class Transformer(nn.Module):
    def __init__(self, rotary):
        super().__init__()
        self.layers = nn.ModuleList([nn.ModuleList([Attention(rotary), FeedForward()])])
        self.norm = RMSNorm(DIM)

    def forward(self, x):
        for attention, feed_forward in self.layers:
            x = attention(x) + x
            x = feed_forward(x) + x
        return self.norm(x)


class BandSplit(nn.Module):
    def __init__(self, dims):
        super().__init__()
        self.dims = dims
        self.to_features = nn.ModuleList([nn.Sequential(RMSNorm(d), nn.Linear(d, DIM)) for d in dims])

    def forward(self, x):
        return torch.stack([f(part) for part, f in zip(x.split(self.dims, dim=-1), self.to_features)], dim=-2)


class Glu(nn.Module):
    """nn.GLU(dim=-1) written out: the TorchScript ONNX exporter takes its last dimension for the second."""

    def forward(self, x):
        value, gate = x.chunk(2, dim=-1)
        return value * torch.sigmoid(gate)


class MaskEstimator(nn.Module):
    def __init__(self, dims):
        super().__init__()
        hidden = DIM * 4

        def mlp(d):
            return nn.Sequential(
                nn.Sequential(nn.Linear(DIM, hidden), nn.Tanh(), nn.Linear(hidden, hidden), nn.Tanh(),
                              nn.Linear(hidden, d * 2)),
                Glu(),
            )

        self.to_freqs = nn.ModuleList([mlp(d) for d in dims])

    def forward(self, x):
        return torch.cat([f(band) for band, f in zip(x.unbind(dim=-2), self.to_freqs)], dim=-1)


class Network(nn.Module):
    """MelBandRoformer.forward from the gathered bands to the masks before they are averaged."""

    def __init__(self, dims):
        super().__init__()
        time_rotary, freq_rotary = Rotary(), Rotary()
        self.layers = nn.ModuleList(
            [nn.ModuleList([Transformer(time_rotary), Transformer(freq_rotary)]) for _ in range(DEPTH)]
        )
        self.band_split = BandSplit(dims)
        self.mask_estimators = nn.ModuleList([MaskEstimator(dims)])

    def forward(self, bands):
        x = self.band_split(bands)
        b, t, f, d = x.shape
        for time_transformer, freq_transformer in self.layers:
            x = time_transformer(x.permute(0, 2, 1, 3).reshape(b * f, t, d)).reshape(b, f, t, d).permute(0, 2, 1, 3)
            x = freq_transformer(x.reshape(b * t, f, d)).reshape(b, t, f, d)
        return self.mask_estimators[0](x)


def load(checkpoint):
    bands = mel_bands()
    network = Network(tuple(4 * count for _, count in bands)).eval()
    state = torch.load(checkpoint, map_location="cpu", weights_only=True)
    network.load_state_dict(state.get("state_dict", state), strict=True)
    return network, bands


class Pipeline:
    """The whole MelBandRoformer forward around the network, in PyTorch: STFT, gathering, mask averaging, iSTFT."""

    def __init__(self, bands):
        self.window = torch.hann_window(N_FFT)
        index, covered = [], np.zeros(N_FFT // 2 + 1)
        for start, count in bands:
            for bin_ in range(start, start + count):
                index += [bin_ * 2, bin_ * 2 + 1]
                covered[bin_] += 1
        self.index = torch.tensor(index)
        self.covered = torch.from_numpy(covered).float().repeat_interleave(2)[:, None]

    def bands(self, piece):
        """[2, CHUNK] → the network's input [1, FRAMES, D] and the STFT [(f s), t] (complex)."""
        stft = torch.stft(piece, N_FFT, HOP, N_FFT, self.window, return_complex=True)
        merged = stft.permute(1, 0, 2).reshape(-1, stft.shape[-1])
        gathered = torch.view_as_real(merged[self.index])
        return gathered.permute(1, 0, 2).reshape(1, merged.shape[-1], -1), merged

    def vocals(self, merged, masks):
        masks = torch.view_as_complex(masks.reshape(masks.shape[1], -1, 2).permute(1, 0, 2).contiguous())
        summed = torch.zeros_like(merged).index_add_(0, self.index, masks)
        stft = (merged * summed / self.covered).reshape(-1, 2, merged.shape[-1]).permute(1, 0, 2)
        return torch.istft(stft, N_FFT, HOP, N_FFT, self.window, length=CHUNK)


def test_mix(length, seed=0):
    """A voice-like tone with vibrato and formants, a bass line, a beat and noise."""
    torch.manual_seed(seed)
    t = torch.arange(length) / SAMPLE_RATE
    pitch = 220 * (1 + 0.02 * torch.sin(2 * math.pi * 5 * t))
    phase = 2 * math.pi * torch.cumsum(pitch, 0) / SAMPLE_RATE
    voice = sum(0.2 / k * torch.sin(k * phase) * (1 + math.cos(2 * math.pi * k * 220 / 900)) for k in range(1, 12))
    voice = voice * (0.5 + 0.5 * torch.sin(2 * math.pi * 0.5 * t).abs())
    bass = 0.3 * torch.sin(2 * math.pi * 55 * t)
    beat = 0.4 * torch.exp(-((t % 0.5) * 30)) * torch.randn(length)
    left = voice + bass + beat + 0.02 * torch.randn(length)
    right = 0.9 * voice + bass + 0.8 * beat + 0.02 * torch.randn(length)
    return torch.stack([left, right]).float()


def snr(expected, got):
    noise = (got - expected).pow(2).mean().sqrt().item()
    signal = expected.pow(2).mean().sqrt().item()
    return 20 * math.log10(signal / max(noise, 1e-12))


def describe(path):
    data = Path(path).read_bytes()
    print(f"{Path(path).name}: {len(data) / 1e6:.1f} MB, sha256 {hashlib.sha256(data).hexdigest()}, {len(data)} bytes")


def check(checkpoint, model_path):
    """The ONNX model on the CPU against PyTorch, on one piece of the test mix."""
    import onnxruntime as ort

    network, bands = load(checkpoint)
    pipeline = Pipeline(bands)
    example, merged = pipeline.bands(test_mix(CHUNK))
    with torch.no_grad():
        expected = network(example)
    options = ort.SessionOptions()
    options.enable_cpu_mem_arena = False
    session = ort.InferenceSession(str(model_path), options, providers=["CPUExecutionProvider"])
    began = time.time()
    got = torch.from_numpy(session.run(["masks"], {"bands": example.numpy()})[0])
    seconds = time.time() - began
    with torch.no_grad():
        vocals = pipeline.vocals(merged, expected)
        vocals_onnx = pipeline.vocals(merged, got)
    print(f"{Path(model_path).name}: CPU {seconds:.1f} s per piece; masks {snr(expected, got):.1f} dB, "
          f"vocals {snr(vocals, vocals_onnx):.1f} dB against PyTorch")


def convert(checkpoint, folder):
    network, bands = load(checkpoint)
    example, _ = Pipeline(bands).bands(test_mix(CHUNK))
    out = Path(folder) / "mel_band_roformer-full.onnx"
    # The dynamo exporter: PyTorch 2.15's TorchScript one puts negative dimensions on the wrong axis here (a stack
    # and a transpose of the attention), which onnxruntime then refuses.
    with torch.no_grad():
        program = torch.onnx.export(network, (example,), input_names=["bands"], output_names=["masks"],
                                    opset_version=18, dynamo=True, external_data=False)
    program.save(str(out), external_data=False)
    describe(out)
    check(checkpoint, out)


def split(network, pipeline, mix):
    """The app's way (StemSplitter): 8 s pieces overlapping by half, triangular weights, a short piece centred."""
    frames = mix.shape[-1]
    stride = CHUNK // 2
    half = CHUNK // 2
    weights = torch.tensor([float(p + 1 if p < half else CHUNK - p) for p in range(CHUNK)])
    weights = weights / weights.max()
    total = torch.zeros_like(mix)
    weight_sum = torch.zeros(frames)
    for offset in range(0, frames, stride):
        length = min(CHUNK, frames - offset)
        pad = (CHUNK - length) // 2
        piece = torch.zeros(2, CHUNK)
        lo, hi = max(offset - pad, 0), min(offset - pad + CHUNK, frames)
        piece[:, lo - (offset - pad):hi - (offset - pad)] = mix[:, lo:hi]
        example, merged = pipeline.bands(piece)
        with torch.no_grad():
            vocals = pipeline.vocals(merged, network(example))
        total[:, offset:offset + length] += vocals[:, pad:pad + length] * weights[:length]
        weight_sum[offset:offset + length] += weights[:length]
    return total / weight_sum


def reference(checkpoint, folder):
    network, bands = load(checkpoint)
    mix = test_mix(12 * SAMPLE_RATE, seed=1) * 0.5
    vocals = split(network, Pipeline(bands), mix)
    mix.numpy().astype("<f4").tofile(Path(folder) / "reference.f32")
    vocals.numpy().astype("<f4").tofile(Path(folder) / "reference-vocals.f32")
    print(f"reference: {mix.shape[-1]} frames, vocals {vocals.pow(2).mean().sqrt():.4f} rms of {mix.pow(2).mean().sqrt():.4f}")
    print("bands:", bands)


if __name__ == "__main__":
    ckpt, target = sys.argv[1], sys.argv[2]
    mode = sys.argv[3] if len(sys.argv) > 3 else "convert"
    if mode == "check":
        check(ckpt, target)
    else:
        Path(target).mkdir(parents=True, exist_ok=True)
        if mode == "reference":
            reference(ckpt, target)
        else:
            convert(ckpt, target)
