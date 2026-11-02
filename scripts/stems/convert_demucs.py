"""Converts the Demucs v4 models (htdemucs, htdemucs_6s) to ONNX for jGlancer's stem splitter (W-85).

Developer machine only (needs PyTorch, demucs, onnx, onnxruntime; downloads the weights from Meta once):

    python scripts/stems/convert_demucs.py <output folder>

ONNX gets only the network. The spectrogram (STFT) before it and the inverse (iSTFT) after it run in the app's own
C# code (`JGlancer.Core.Stems.Spectrogram`), because STFT/iSTFT export badly. The exported graph takes

    mix  float32 [1, 2, L]            the normalized stereo piece (L = 343980 samples, 7.8 s at 44.1 kHz)
    spec float32 [1, 4, 2048, 336]    its spectrogram as (left re, left im, right re, right im)

and returns

    freq float32 [1, S, 4, 2048, 336] each source's spectrogram, same layout (to be inverted and added)
    time float32 [1, S, 2, L]         each source's time-branch waveform

Prints each model's SHA-256 and the largest difference against Demucs' own forward on a test signal. The app
downloads them after `half_weights.py` (float16 weights): their checksums are in `StemModelDownload.Source`.
"""

import hashlib
import math
import sys
from pathlib import Path

import numpy as np
import onnxruntime as ort
import torch
from demucs.pretrained import get_model


class Core(torch.nn.Module):
    """HTDemucs.forward from the magnitude to just before the mask, for a full training-length piece."""

    def __init__(self, model):
        super().__init__()
        self.m = model

    def forward(self, mix, spec):
        m = self.m
        x = spec
        B, C, Fq, T = x.shape
        mean = x.mean(dim=(1, 2, 3), keepdim=True)
        std = x.std(dim=(1, 2, 3), keepdim=True)
        x = (x - mean) / (1e-5 + std)
        xt = mix
        meant = xt.mean(dim=(1, 2), keepdim=True)
        stdt = xt.std(dim=(1, 2), keepdim=True)
        xt = (xt - meant) / (1e-5 + stdt)
        saved, saved_t, lengths, lengths_t = [], [], [], []
        for idx, encode in enumerate(m.encoder):
            lengths.append(x.shape[-1])
            inject = None
            if idx < len(m.tencoder):
                lengths_t.append(xt.shape[-1])
                tenc = m.tencoder[idx]
                xt = tenc(xt)
                if not tenc.empty:
                    saved_t.append(xt)
                else:
                    inject = xt
            x = encode(x, inject)
            if idx == 0 and m.freq_emb is not None:
                frs = torch.arange(x.shape[-2], device=x.device)
                emb = m.freq_emb(frs).t()[None, :, :, None].expand_as(x)
                x = x + m.freq_emb_scale * emb
            saved.append(x)
        if m.crosstransformer:
            if m.bottom_channels:
                b, c, f, t = x.shape
                x = x.reshape(b, c, f * t)
                x = m.channel_upsampler(x)
                x = x.reshape(b, -1, f, t)
                xt = m.channel_upsampler_t(xt)
            x, xt = m.crosstransformer(x, xt)
            if m.bottom_channels:
                b, c, f, t = x.shape
                x = x.reshape(b, c, f * t)
                x = m.channel_downsampler(x)
                x = x.reshape(b, -1, f, t)
                xt = m.channel_downsampler_t(xt)
        for idx, decode in enumerate(m.decoder):
            skip = saved.pop(-1)
            x, pre = decode(x, skip, lengths.pop(-1))
            offset = m.depth - len(m.tdecoder)
            if idx >= offset:
                tdec = m.tdecoder[idx - offset]
                length_t = lengths_t.pop(-1)
                if tdec.empty:
                    pre = pre[:, :, 0]
                    xt, _ = tdec(pre, None, length_t)
                else:
                    skip = saved_t.pop(-1)
                    xt, _ = tdec(xt, skip, length_t)
        S = len(m.sources)
        x = x.view(B, S, -1, Fq, T)
        x = x * std[:, None] + mean[:, None]
        xt = xt.view(B, S, -1, mix.shape[-1])
        xt = xt * stdt[:, None] + meant[:, None]
        return x, xt


def spectrogram(model, mix):
    z = model._spec(mix)
    return model._magnitude(z), z


def reference(model, mix):
    with torch.no_grad():
        return model(mix)


def ours(session, model, mix):
    spec, z = spectrogram(model, mix)
    freq, time = session.run(None, {"mix": mix.numpy(), "spec": spec.numpy()})
    freq = torch.from_numpy(freq)
    zout = model._mask(z, freq)
    x = model._ispec(zout, mix.shape[-1])
    return x + torch.from_numpy(time)


def convert(name, folder):
    # The fused attention kernel PyTorch uses in inference has no ONNX form; the plain one does.
    torch.backends.mha.set_fastpath_enabled(False)
    model = get_model(name).models[0].eval()
    length = int(model.segment * model.samplerate)
    torch.manual_seed(0)
    t = torch.arange(length) / model.samplerate
    mix = (0.3 * torch.sin(2 * math.pi * 220 * t) + 0.1 * torch.randn(length)).repeat(2, 1)[None].float()
    spec, _ = spectrogram(model, mix)
    # Before the export: exporting leaves the model in another state.
    expected = reference(model, mix)
    out = Path(folder) / f"{name}.onnx"
    with torch.no_grad():
        torch.onnx.export(Core(model), (mix, spec), str(out), input_names=["mix", "spec"],
                          output_names=["freq", "time"], opset_version=17, dynamo=False)
    session = ort.InferenceSession(str(out), providers=["CPUExecutionProvider"])
    error = (ours(session, model, mix) - expected).abs().max().item()
    digest = hashlib.sha256(out.read_bytes()).hexdigest()
    print(f"{name}: {out.stat().st_size / 1e6:.1f} MB, sha256 {digest}, sources {model.sources}, "
          f"segment {length}, max error {error:.2e}")
    # A reference for the C# spectrogram tests: a short signal and its spectrogram.
    np.save(Path(folder) / f"{name}-mix.npy", mix.numpy())
    np.save(Path(folder) / f"{name}-spec.npy", spec.numpy())


if __name__ == "__main__":
    target = sys.argv[1] if len(sys.argv) > 1 else "."
    Path(target).mkdir(parents=True, exist_ok=True)
    for model_name in ("htdemucs", "htdemucs_6s"):
        convert(model_name, target)
