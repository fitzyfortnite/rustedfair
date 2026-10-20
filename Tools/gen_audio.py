#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Rusted Fair - procedural audio generator
========================================

Synthesizes every sound effect, ambience and music cue used by the game
"Rusted Fair" from scratch (no samples).  Run:

    python3 Tools/gen_audio.py              # render everything
    python3 Tools/gen_audio.py --only a,b   # render a subset (debugging)

Output: 16-bit PCM WAV, 44.1 kHz, written to
    Assets/RustedFair/Resources/RF/Audio/<name>.wav

Techniques used
---------------
* Band-limited oscillators (polyBLEP saw/pulse, additive partials that are
  muted above Nyquist) so nothing aliases.
* Modal synthesis for all metal / wood / glass / bells (sums of exponentially
  damped sinusoids with inharmonic ratios, or noise/impulse excitation run
  through a bank of 2-pole resonators).
* Formant synthesis (parallel band-pass banks with vowel morphing) for every
  voice-like sound: breathing, whispers, PA announcer, robot voices, screams.
* Stick-slip impulse trains through resonators for creaks, groans, scrapes.
* Oversampled tanh saturation for distortion without fold-back aliasing.
* Convolution reverb with synthetic stereo IRs (decorrelated, multi-band
  exponential decay, early reflections, pre-delay) via FFT convolution.
* Loops are built to be periodic by construction: periodic colored noise
  (shaped in the frequency domain), LFOs/oscillator frequencies quantized to
  whole cycles per loop, events whose tails run past the loop end are
  wrap-added into the head (the "render longer and fold the tail into the
  head" crossfade, done exactly), IIR filters warmed up on the loop's own
  tail, and circular (FFT) convolution for reverb.  Every loop is verified
  for sample continuity across the wrap point after it has been written.

All randomness uses fixed seeds, so the output is reproducible.
"""

import os
import sys
import time
import argparse

import numpy as np
from scipy import signal
from scipy.io import wavfile

SR = 44100
NYQ = SR / 2.0
HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.normpath(os.path.join(HERE, '..', 'Assets', 'RustedFair',
                                        'Resources', 'RF', 'Audio'))

# =============================================================================
#  Basic helpers
# =============================================================================


def ns(sec):
    """Seconds -> samples."""
    return int(round(sec * SR))


def tvec(n):
    return np.arange(n) / SR


def dbamp(d):
    return 10.0 ** (d / 20.0)


def mtof(m):
    return 440.0 * 2.0 ** ((np.asarray(m, dtype=float) - 69.0) / 12.0)


def R(seed):
    return np.random.default_rng(seed)


def to_len(x, n):
    """Pad with zeros / truncate along the last axis."""
    m = x.shape[-1]
    if m == n:
        return x
    if m > n:
        return x[..., :n].copy()
    pad = [(0, 0)] * (x.ndim - 1) + [(0, n - m)]
    return np.pad(x, pad)


def stereo(x):
    x = np.asarray(x, dtype=float)
    return x if x.ndim == 2 else np.stack([x, x])


def mono(x):
    x = np.asarray(x, dtype=float)
    return x if x.ndim == 1 else x.mean(axis=0)


def pan(x, p):
    """Equal-power pan of a mono signal, p in [-1, 1]."""
    a = (np.clip(p, -1, 1) + 1.0) * np.pi / 4.0
    return np.stack([np.cos(a) * x, np.sin(a) * x])


def fade(x, fin=0.003, fout=0.01):
    """Raised-cosine fade in/out (starts and ends exactly at zero)."""
    x = np.array(x, dtype=float, copy=True)
    a, b = ns(fin), ns(fout)
    if a > 1:
        x[..., :a] *= np.sin(0.5 * np.pi * np.linspace(0, 1, a)) ** 2
    if b > 1:
        x[..., -b:] *= np.cos(0.5 * np.pi * np.linspace(0, 1, b)) ** 2
    return x


def add(buf, sig, pos, gain=1.0, wrap=False):
    """Mix `sig` into `buf` at sample `pos`.  wrap=True folds anything that
    runs past the end back to the start (used for seamless loops)."""
    pos = int(round(pos))
    sig = np.asarray(sig, dtype=float)
    if buf.ndim == 2 and sig.ndim == 1:
        sig = np.stack([sig, sig])
    n, m = buf.shape[-1], sig.shape[-1]
    if wrap:
        s, i = pos % n, 0
        while i < m:
            take = min(m - i, n - s)
            buf[..., s:s + take] += gain * sig[..., i:i + take]
            i += take
            s = 0
    else:
        a0, b0 = max(pos, 0), min(pos + m, n)
        if b0 > a0:
            buf[..., a0:b0] += gain * sig[..., a0 - pos:b0 - pos]
    return buf


def env_pts(n, pts):
    """Piecewise-linear envelope from [(time_s, value), ...]."""
    tp = np.array([p[0] for p in pts], dtype=float)
    vp = np.array([p[1] for p in pts], dtype=float)
    return np.interp(tvec(n), tp, vp)


def env_perc(n, att=0.002, t60=0.5, hold=0.0):
    """Raised-cosine attack, optional hold, exponential (t60) decay."""
    t = tvec(n)
    a = np.ones(n) if att <= 0 else np.sin(0.5 * np.pi * np.clip(t / att, 0, 1)) ** 2
    d = np.exp(-6.9078 * np.maximum(t - att - hold, 0.0) / t60)
    return a * d


def smoothstep(x):
    x = np.clip(x, 0, 1)
    return x * x * (3 - 2 * x)


def rms(x):
    return float(np.sqrt(np.mean(np.square(x)) + 1e-20))


def norm_peak(x, p=1.0):
    m = np.max(np.abs(x))
    return x * (p / m) if m > 0 else x


# =============================================================================
#  Filters
# =============================================================================


def _clipf(f):
    return float(min(max(f, 1.0), NYQ * 0.98))


def _sos(kind, fc, order):
    if kind in ('bandpass', 'bandstop'):
        fc = [_clipf(fc[0]), _clipf(fc[1])]
    else:
        fc = _clipf(fc)
    return signal.butter(order, fc, btype=kind, fs=SR, output='sos')


def lp(x, fc, order=2):
    return signal.sosfilt(_sos('lowpass', fc, order), x, axis=-1)


def hp(x, fc, order=2):
    return signal.sosfilt(_sos('highpass', fc, order), x, axis=-1)


def bp(x, lo, hi, order=2):
    return signal.sosfilt(_sos('bandpass', (lo, hi), order), x, axis=-1)


def lp1(x, fc):
    a = np.exp(-2 * np.pi * fc / SR)
    return signal.lfilter([1 - a], [1, -a], x, axis=-1)


def _rbj(kind, f, q, gain_db=0.0):
    w = 2 * np.pi * _clipf(f) / SR
    c, s = np.cos(w), np.sin(w)
    al = s / (2 * q)
    if kind == 'bp':
        b = [al, 0.0, -al]
        a = [1 + al, -2 * c, 1 - al]
    elif kind == 'peak':
        A = 10 ** (gain_db / 40)
        b = [1 + al * A, -2 * c, 1 - al * A]
        a = [1 + al / A, -2 * c, 1 - al / A]
    elif kind == 'notch':
        b = [1, -2 * c, 1]
        a = [1 + al, -2 * c, 1 - al]
    else:
        raise ValueError(kind)
    b, a = np.array(b) / a[0], np.array(a) / a[0]
    return b, a


def bpf(x, f, q):
    """Constant 0 dB peak gain band-pass biquad."""
    b, a = _rbj('bp', f, q)
    return signal.lfilter(b, a, x, axis=-1)


def peq(x, f, q, gain_db):
    b, a = _rbj('peak', f, q, gain_db)
    return signal.lfilter(b, a, x, axis=-1)


def periodic(fn, x, *args, warm=2.0, **kw):
    """Run a fixed-parameter process on a loop so that its output is periodic:
    the loop's own tail is prepended (and head appended) as warm-up."""
    x = np.asarray(x, dtype=float)
    n = x.shape[-1]
    w = min(ns(warm), n)
    ext = np.concatenate([x[..., n - w:], x, x[..., :w]], axis=-1)
    y = fn(ext, *args, **kw)
    return y[..., w:w + n]


def tv_filter(x, fc, q=0.707, kind='lp', block=64, is_periodic=False,
              warm=3.0, stages=1):
    """Time-varying RBJ biquad (lp/hp/bp), coefficients updated every
    `block` samples with state carried over.  fc/q may be arrays."""
    x = np.asarray(x, dtype=float)
    if x.ndim == 2:
        return np.stack([tv_filter(c, fc, q, kind, block, is_periodic, warm,
                                   stages) for c in x])
    n = len(x)
    fc = np.broadcast_to(np.asarray(fc, dtype=float), (n,))
    qa = np.broadcast_to(np.asarray(q, dtype=float), (n,))
    w = 0
    if is_periodic:
        w = min(ns(warm), n)
        x = np.concatenate([x[n - w:], x])
        fc = np.concatenate([fc[n - w:], fc])
        qa = np.concatenate([qa[n - w:], qa])
    N = len(x)
    nb = (N + block - 1) // block
    idx = np.minimum(np.arange(nb) * block + block // 2, N - 1)
    f = np.clip(fc[idx], 15.0, NYQ * 0.95)
    qq = np.maximum(qa[idx], 0.1)
    w0 = 2 * np.pi * f / SR
    c, s = np.cos(w0), np.sin(w0)
    al = s / (2 * qq)
    if kind == 'lp':
        b0 = (1 - c) / 2; b1 = 1 - c; b2 = b0
    elif kind == 'hp':
        b0 = (1 + c) / 2; b1 = -(1 + c); b2 = b0
    elif kind == 'bp':
        b0 = al; b1 = np.zeros_like(al); b2 = -al
    else:
        raise ValueError(kind)
    a0 = 1 + al
    B = np.stack([b0, b1, b2], 1) / a0[:, None]
    A = np.stack([np.ones_like(a0), -2 * c / a0, (1 - al) / a0], 1)
    y = x
    for _ in range(stages):
        out = np.empty(N)
        z = np.zeros(2)
        lf = signal.lfilter
        for i in range(nb):
            sl = slice(i * block, (i + 1) * block)
            out[sl], z = lf(B[i], A[i], y[sl], zi=z)
        y = out
    return y[w:]


def bw_mag(f, fc, order, kind):
    f = np.maximum(np.asarray(f, dtype=float), 1e-6)
    if kind == 'lp':
        return 1.0 / np.sqrt(1.0 + (f / fc) ** (2 * order))
    return 1.0 / np.sqrt(1.0 + (fc / f) ** (2 * order))


def fft_eq(x, fn):
    """Circular zero-phase filtering by an arbitrary magnitude response
    fn(freqs_hz).  Exact for periodic (loop) signals."""
    n = x.shape[-1]
    X = np.fft.rfft(x, axis=-1)
    f = np.fft.rfftfreq(n, 1.0 / SR)
    return np.fft.irfft(X * fn(f), n, axis=-1)


# =============================================================================
#  Noise and control signals
# =============================================================================


def pnoise(n, rng, slope=0.0, lo=None, hi=None, order=2, ch=None):
    """Colored noise shaped in the frequency domain (periodic over n).
    slope in dB/octave: 0 white, -3 pink, -6 brown.  Unit RMS."""
    shape = (n,) if ch is None else (ch, n)
    w = rng.standard_normal(shape)

    def g(f):
        ff = np.maximum(f, 8.0)
        m = (ff / 1000.0) ** (slope / 6.0206)
        if lo:
            m = m * bw_mag(f, lo, order, 'hp')
        if hi:
            m = m * bw_mag(f, hi, order, 'lp')
        m = m.copy()
        m[0] = 0.0
        return m
    y = fft_eq(w, g)
    return y / (np.sqrt(np.mean(y ** 2, axis=-1, keepdims=True)) + 1e-12)


def lfo_noise(n, cycles, rng, slope=1.0):
    """Smooth random control signal in [-1, 1], periodic over n samples,
    containing components up to `cycles` cycles per n samples."""
    k = int(max(1, round(cycles)))
    k = min(k, n // 2 - 1)
    X = np.zeros(n // 2 + 1, dtype=complex)
    i = np.arange(1, k + 1)
    X[1:k + 1] = (rng.standard_normal(k) + 1j * rng.standard_normal(k)) / i ** slope
    y = np.fft.irfft(X, n)
    return y / (np.max(np.abs(y)) + 1e-12)


def contour(n, rate_hz, rng, slope=1.0):
    """Random smooth contour with features up to ~rate_hz."""
    return lfo_noise(n, max(1.0, rate_hz * n / SR), rng, slope)


def impulses(n, times, amps=1.0):
    """Sparse impulse train (fractional positions spread over 2 samples)."""
    x = np.zeros(n)
    times = np.atleast_1d(np.asarray(times, dtype=float))
    amps = np.broadcast_to(np.asarray(amps, dtype=float), times.shape)
    p = times * SR
    i0 = np.floor(p).astype(int)
    fr = p - i0
    ok = (i0 >= 0) & (i0 < n - 1)
    np.add.at(x, i0[ok], amps[ok] * (1 - fr[ok]))
    np.add.at(x, i0[ok] + 1, amps[ok] * fr[ok])
    return x


def poisson_times(rate, dur, rng, t0=0.0):
    """Event times of a (possibly time-varying) Poisson process.
    rate: scalar or callable rate(t)."""
    if not callable(rate):
        r = float(rate)
        k = rng.poisson(r * dur)
        return np.sort(rng.uniform(t0, t0 + dur, k))
    # thinning
    grid = np.linspace(t0, t0 + dur, 512)
    rmax = max(float(np.max([rate(g) for g in grid])), 1e-6)
    k = rng.poisson(rmax * dur)
    cand = np.sort(rng.uniform(t0, t0 + dur, k))
    keep = rng.uniform(0, rmax, k) < np.array([rate(c) for c in cand])
    return cand[keep]


def stick_slip(n, rate, rng, jitter=0.15, amp_jit=0.4, t0=0.0, t1=None):
    """Irregular impulse train for friction (creaks, scrapes, groans).
    rate: array (Hz, per sample) or scalar."""
    rate = np.broadcast_to(np.asarray(rate, dtype=float), (n,))
    t1 = n / SR if t1 is None else t1
    times, amps = [], []
    t = t0
    while t < t1:
        i = min(int(t * SR), n - 1)
        r = max(rate[i], 0.5)
        t += (1.0 / r) * max(0.2, 1 + jitter * rng.standard_normal())
        times.append(t)
        amps.append(max(0.05, 1 + amp_jit * rng.standard_normal()))
    return impulses(n, np.array(times), np.array(amps))


# =============================================================================
#  Oscillators
# =============================================================================


def cycles(f, n, is_periodic=False):
    """Phase in cycles for a (possibly time-varying) frequency.  With
    is_periodic the total phase is nudged to a whole number of cycles."""
    f = np.broadcast_to(np.asarray(f, dtype=float), (n,))
    c = np.empty(n)
    c[0] = 0.0
    np.cumsum(f[:-1] / SR, out=c[1:])
    if is_periodic:
        total = c[-1] + f[-1] / SR
        if total > 0:
            c *= max(1, round(total)) / total
    return c


def qf(f, loop_sec):
    """Quantize a frequency so it completes whole cycles over a loop."""
    return max(1, round(f * loop_sec)) / loop_sec


def _polyblep(p, dt):
    y = np.zeros_like(p)
    m = p < dt
    t = p[m] / dt[m]
    y[m] = t + t - t * t - 1.0
    m = p > 1.0 - dt
    t = (p[m] - 1.0) / dt[m]
    y[m] = t * t + t + t + 1.0
    return y


def saw(f, n, phase0=0.0, is_periodic=False):
    """PolyBLEP band-limited sawtooth."""
    f = np.broadcast_to(np.asarray(f, dtype=float), (n,))
    p = (cycles(f, n, is_periodic) + phase0) % 1.0
    dt = np.clip(np.abs(f) / SR, 1e-9, 0.5)
    return 2.0 * p - 1.0 - _polyblep(p, dt)


def pulse(f, n, duty=0.5, phase0=0.0, is_periodic=False):
    """Band-limited pulse = difference of two polyBLEP saws."""
    return 0.5 * (saw(f, n, phase0, is_periodic) - saw(f, n, phase0 + duty, is_periodic))


def sine(f, n, phase0=0.0, is_periodic=False):
    return np.sin(2 * np.pi * (cycles(f, n, is_periodic) + phase0))


def additive(f, n, amps, ratios=None, phases=None, is_periodic=False):
    """Sum of partials; any partial that would exceed ~0.45*SR is faded out
    sample by sample (so pitch sweeps never alias)."""
    f = np.broadcast_to(np.asarray(f, dtype=float), (n,))
    c = cycles(f, n, is_periodic)
    out = np.zeros(n)
    fmax = np.max(np.abs(f))
    for k, a in enumerate(amps):
        if a == 0:
            continue
        r = (k + 1) if ratios is None else ratios[k]
        if fmax * r > NYQ * 0.98:
            g = np.clip((NYQ * 0.95 - np.abs(f) * r) / (NYQ * 0.05), 0, 1)
            if not np.any(g):
                continue
        else:
            g = 1.0
        ph = 0.0 if phases is None else phases[k]
        out += a * g * np.sin(2 * np.pi * (r * c + ph))
    return out


def fm(fc, fm_ratio, index, n, is_periodic=False):
    """Two-operator FM (phase modulation).  index may be an array."""
    cc = cycles(fc, n, is_periodic)
    fmod = np.broadcast_to(np.asarray(fc, dtype=float), (n,)) * fm_ratio
    cm = cycles(fmod, n, is_periodic)
    return np.sin(2 * np.pi * cc + index * np.sin(2 * np.pi * cm))


# =============================================================================
#  Non-linear processing
# =============================================================================


def sat(x, drive=2.0, os_=4, asym=0.0):
    """Oversampled tanh saturation (optionally asymmetric)."""
    x = np.asarray(x, dtype=float)
    up = signal.resample_poly(x, os_, 1, axis=-1) if os_ > 1 else x
    y = (np.tanh(drive * (up + asym)) - np.tanh(drive * asym)) / np.tanh(drive)
    if os_ > 1:
        y = signal.resample_poly(y, 1, os_, axis=-1)
    return y[..., :x.shape[-1]]


def hardclip(x, thr=0.5, os_=4):
    up = signal.resample_poly(x, os_, 1, axis=-1)
    y = np.clip(up, -thr, thr) / thr
    return signal.resample_poly(y, 1, os_, axis=-1)[..., :x.shape[-1]]


def crush(x, bits=8, div=4):
    """Bit-depth and sample-rate reduction (intentional lo-fi)."""
    n = x.shape[-1]
    y = np.repeat(x[..., ::div], div, axis=-1)[..., :n]
    q = 2.0 / (2 ** bits)
    return np.round(y / q) * q


def varispeed(x, speed):
    """Tape-style speed change (pitch+time) with per-sample speed."""
    n = x.shape[-1]
    speed = np.broadcast_to(np.asarray(speed, dtype=float), (n,))
    pos = np.concatenate([[0.0], np.cumsum(speed[:-1])])
    pos = np.clip(pos, 0, n - 1)
    idx = np.arange(n)
    if x.ndim == 1:
        return np.interp(pos, idx, x)
    return np.stack([np.interp(pos, idx, c) for c in x])


# =============================================================================
#  Modal / resonator synthesis
# =============================================================================


def resonators(x, freqs, t60s, gains):
    """Bank of 2-pole resonators driven by x (impulse response of each is a
    unit-amplitude damped sine)."""
    y = np.zeros_like(np.asarray(x, dtype=float))
    for f, T, g in zip(freqs, t60s, gains):
        if f <= 0 or f >= NYQ * 0.95 or g == 0:
            continue
        r = np.exp(-6.9078 / (T * SR))
        w = 2 * np.pi * f / SR
        y += g * signal.lfilter([np.sin(w)], [1.0, -2 * r * np.cos(w), r * r], x, axis=-1)
    return y


def modal(n, freqs, t60s, amps, phases=None, att=0.0):
    """Direct sum of damped sinusoids."""
    t = tvec(n)
    y = np.zeros(n)
    for k, (f, T, a) in enumerate(zip(freqs, t60s, amps)):
        if f <= 0 or f >= NYQ * 0.95:
            continue
        ph = 0.0 if phases is None else phases[k]
        y += a * np.exp(-6.9078 * t / T) * np.sin(2 * np.pi * f * t + ph)
    if att > 0:
        y *= np.sin(0.5 * np.pi * np.clip(t / att, 0, 1)) ** 2
    return y


def metal_modes(f0, rng, count=14, t60=1.0, bright=4000.0, spread=0.05,
                damp_exp=0.7, ratios=None):
    """Inharmonic plate/shell-like mode set."""
    if ratios is None:
        k = np.arange(count)
        ratios = ((k + 0.6) / 0.6) ** 0.85 * (1 + spread * rng.standard_normal(count))
        ratios[0] = 1.0
    ratios = np.asarray(ratios, dtype=float)
    fr = f0 * ratios
    t60s = t60 * (f0 / fr) ** damp_exp * rng.uniform(0.7, 1.3, len(fr))
    amps = rng.uniform(0.35, 1.0, len(fr)) * np.exp(-fr / bright) / (1 + 0.08 * np.arange(len(fr)))
    return fr, t60s, amps


def metal_hit(dur, f0, rng, count=14, t60=1.0, bright=4000.0, spread=0.05,
              noise=0.3, noise_hz=(1500, 9000), mallet=0.0008, ratios=None):
    """Struck metal: modal body + short noise transient."""
    n = ns(dur)
    fr, ts, am = metal_modes(f0, rng, count, t60, bright, spread, ratios=ratios)
    y = modal(n, fr, ts, am, phases=rng.uniform(0, 0.15, len(fr)), att=mallet)
    y = y / (np.max(np.abs(y)) + 1e-9)
    if noise > 0:
        nb = bp(rng.standard_normal(n), noise_hz[0], noise_hz[1]) * env_perc(n, 0.0004, 0.02)
        y += noise * nb / (np.max(np.abs(nb)) + 1e-9)
    return y


def thump(dur, f_hi, f_lo, decay, rng, drop=0.025, noise=0.4, noise_lp=500.0):
    """Pitch-dropping sine impact with a low noise burst."""
    n = ns(dur)
    t = tvec(n)
    f = f_lo + (f_hi - f_lo) * np.exp(-t / drop)
    y = np.sin(2 * np.pi * cycles(f, n)) * env_perc(n, 0.0015, decay)
    if noise > 0:
        nb = lp(rng.standard_normal(n), noise_lp, 2) * env_perc(n, 0.0005, decay * 0.35)
        y += noise * nb / (np.max(np.abs(nb)) + 1e-9)
    return y


def click(dur, rng, f_lo=2000, f_hi=9000, t60=0.01, modes=4, mf=(2500, 6000)):
    """Small tactile click: noise tick + a few short modes."""
    n = ns(dur)
    y = bp(rng.standard_normal(n), f_lo, f_hi) * env_perc(n, 0.0002, 0.004)
    y /= np.max(np.abs(y)) + 1e-9
    fr = rng.uniform(mf[0], mf[1], modes)
    y += 0.6 * modal(n, fr, rng.uniform(0.6, 1.4, modes) * t60, rng.uniform(0.4, 1, modes))
    return y


# =============================================================================
#  Voice / formant synthesis
# =============================================================================

VOWELS = {
    #       formant freqs (Hz)              bandwidths (Hz)            gains
    'a':  ([730, 1090, 2440, 3400, 4300], [90, 110, 160, 250, 320], [1.0, 0.55, 0.30, 0.15, 0.07]),
    'e':  ([530, 1840, 2480, 3500, 4300], [70, 100, 150, 250, 320], [1.0, 0.45, 0.35, 0.15, 0.07]),
    'i':  ([300, 2290, 3010, 3600, 4400], [50, 100, 150, 250, 320], [1.0, 0.33, 0.30, 0.18, 0.07]),
    'o':  ([570, 840, 2410, 3300, 4200], [80, 90, 150, 250, 320], [1.0, 0.60, 0.14, 0.10, 0.05]),
    'u':  ([320, 870, 2240, 3300, 4200], [60, 90, 150, 250, 320], [1.0, 0.40, 0.10, 0.08, 0.04]),
    'ae': ([660, 1720, 2410, 3400, 4300], [90, 110, 160, 250, 320], [1.0, 0.50, 0.30, 0.15, 0.07]),
    'er': ([490, 1350, 1690, 3300, 4200], [80, 110, 150, 250, 320], [1.0, 0.50, 0.40, 0.12, 0.05]),
    'h':  ([700, 1220, 2600, 3500, 4500], [200, 250, 300, 400, 500], [1.0, 0.70, 0.50, 0.35, 0.25]),
}


def formant(src, vowel, shift=1.0, bwmul=1.0):
    F, B, G = VOWELS[vowel]
    y = np.zeros_like(src)
    for f, b, g in zip(F, B, G):
        fs = f * shift
        if fs >= NYQ * 0.9:
            continue
        y += g * bpf(src, fs, fs / (b * bwmul * max(shift, 0.3)))
    return y


def vowel_morph(src, weights, shift=1.0, bwmul=1.0):
    """weights: dict vowel -> per-sample weight array (crossfaded banks)."""
    y = np.zeros_like(src)
    for v, w in weights.items():
        if np.max(w) <= 1e-4:
            continue
        y += w * formant(src, v, shift, bwmul)
    return y


def vowel_track(n, segs, smooth=0.03):
    """segs: list of (t_start, vowel).  Returns smoothed one-hot weights."""
    t = tvec(n)
    names = sorted(set(v for _, v in segs))
    W = {v: np.zeros(n) for v in names}
    for i, (t0, v) in enumerate(segs):
        t1 = segs[i + 1][0] if i + 1 < len(segs) else t[-1] + 1
        W[v][(t >= t0) & (t < t1)] = 1.0
    k = max(3, ns(smooth))
    win = np.hanning(k)
    win /= win.sum()
    for v in names:
        W[v] = np.convolve(W[v], win, mode='same')
    return W


def glottal(f0, n, rng, jitter=0.008, shimmer=0.05, tilt=900.0, breath=0.05):
    """Voiced source: band-limited saw with jitter/shimmer, spectrally tilted,
    plus pitch-synchronous aspiration noise."""
    f0 = np.broadcast_to(np.asarray(f0, dtype=float), (n,))
    jit = lp(rng.standard_normal(n), 40.0, 1)
    jit /= np.std(jit) + 1e-9
    f = f0 * (1 + jitter * jit)
    c = cycles(f, n)
    p = c % 1.0
    dt = np.clip(f / SR, 1e-9, 0.5)
    s = -(2 * p - 1 - _polyblep(p, dt))
    s = lp1(s, tilt)
    sh = lp(rng.standard_normal(n), 30.0, 1)
    s *= 1 + shimmer * sh / (np.std(sh) + 1e-9)
    if breath > 0:
        nz = hp(rng.standard_normal(n), 1200.0) * (0.4 + 0.6 * (p < 0.4))
        s += breath * nz * np.std(s)
    return s


# =============================================================================
#  Reverb (synthetic impulse responses, FFT convolution)
# =============================================================================

_IRC = {}


def make_ir(rt60=2.0, length=None, predelay=0.012, lf=1.25, hf=0.45,
            density_ms=12.0, er_n=10, er_max=0.07, er_gain=0.5, seed=7,
            dark_hz=None):
    """Stereo synthetic IR: decorrelated L/R noise with band-dependent
    exponential decay (rt60 * lf in the lows ... rt60 * hf in the highs),
    gradual density build-up, discrete early reflections and pre-delay.
    Normalized to unit energy per channel."""
    key = (rt60, length, predelay, lf, hf, density_ms, er_n, er_max, er_gain, seed, dark_hz)
    if key in _IRC:
        return _IRC[key]
    rng = R(seed)
    L = length if length else rt60 * 1.15
    n = ns(L)
    t = tvec(n)
    edges = [0, 250, 1000, 4000, 9000, None]
    mults = [lf, 1.0, (1 + hf) / 2, hf, hf * 0.6]
    ir = np.zeros((2, n))
    for ch in range(2):
        w = rng.standard_normal(n)
        acc = np.zeros(n)
        for b in range(5):
            lo_, hi_ = edges[b], edges[b + 1]
            if lo_ == 0:
                y = lp(w, hi_, 4)
            elif hi_ is None:
                y = hp(w, lo_, 4)
            else:
                y = bp(w, lo_, hi_, 2)
            acc += y * np.exp(-6.9078 * t / (rt60 * mults[b]))
        acc *= 1 - np.exp(-t / (density_ms / 1000.0))
        acc /= np.sqrt(np.sum(acc ** 2)) + 1e-12
        er = np.zeros(n)
        if er_n > 0:
            tm = rng.uniform(0.002, er_max, er_n)
            am = rng.uniform(0.4, 1.0, er_n) * (1 - 0.7 * tm / er_max) * rng.choice([-1, 1], er_n)
            er = lp(impulses(n, tm, am), 7000.0, 2)
            er /= np.sqrt(np.sum(er ** 2)) + 1e-12
        x = acc + er_gain * er
        x /= np.sqrt(np.sum(x ** 2)) + 1e-12
        if dark_hz:
            # filter AFTER normalising: a darker room is quieter in the highs,
            # it must not get louder in the lows
            x = lp(x, dark_hz, 2)
        ir[ch] = x
    pd = ns(predelay)
    ir = np.concatenate([np.zeros((2, pd)), ir], axis=1)
    ir[1] = np.roll(ir[1], ns(0.0004))
    ir = fade(ir, 0.0, L * 0.1)
    _IRC[key] = ir
    return ir


def reverb(x, ir, wet=0.3, dry=1.0, is_periodic=False, out_len=None, mono_out=False):
    """Convolve with a stereo IR.  Mono in -> stereo out (decorrelated by the
    IR).  is_periodic: circular convolution (seamless loop reverb)."""
    x = np.asarray(x, dtype=float)
    xin = stereo(x)
    chans = [0] if mono_out else [0, 1]
    n = xin.shape[-1]
    if is_periodic:
        m = ir.shape[-1]
        irw = np.zeros((2, n))
        for s in range(0, m, n):
            seg = ir[:, s:s + n]
            irw[:, :seg.shape[1]] += seg
        wetsig = np.stack([np.fft.irfft(np.fft.rfft(xin[c]) * np.fft.rfft(irw[c]), n)
                           for c in chans])
        drys = xin[chans]
    else:
        L = out_len if out_len else n + ir.shape[-1] - 1
        wetsig = np.stack([to_len(signal.oaconvolve(xin[c], ir[c]), L) for c in chans])
        drys = to_len(xin[chans], L)
    out = dry * drys + wet * wetsig
    return out[0] if mono_out else out


def room(x, rt60=0.5, wet=0.15, dur=None, seed=11, mono_out=True, **kw):
    """Convenience: small/medium space for one-shots, cropped to dur."""
    ir = make_ir(rt60=rt60, seed=seed, **kw)
    n = x.shape[-1] if dur is None else ns(dur)
    return reverb(x, ir, wet=wet, out_len=n, mono_out=mono_out)


# =============================================================================
#  Registry, mastering and output
# =============================================================================

SOUNDS = []          # (name, fn, channels, peak_db, is_loop)


def sound(name, ch, peak, loop=False):
    """Register a generator.  ch: 1 (3D mono) or 2 (2D stereo)."""
    def deco(fn):
        SOUNDS.append((name, fn, ch, peak, loop))
        return fn
    return deco


def master(x, ch, peak_db, loop):
    x = np.asarray(x, dtype=float)
    if ch == 1:
        x = mono(x)
    else:
        x = stereo(x)
    if loop:
        # exact, circular DC / subsonic removal (keeps the loop periodic)
        x = fft_eq(x, lambda f: bw_mag(f, 18.0, 2, 'hp') * (f > 0))
    else:
        # causal DC blocker from a zero start (no mean subtraction: that
        # would leave a constant offset = sub-bass bump on short files)
        x = fade(x, 0.001, 0.0)
        x = signal.sosfilt(_sos('highpass', 20.0, 2), x, axis=-1)
        x = fade(x, 0.002, 0.012)
    m = np.max(np.abs(x))
    if not np.isfinite(m) or m <= 0:
        raise RuntimeError('silent or invalid output')
    return x * (dbamp(peak_db) / m)


def write_wav(path, x):
    q = np.clip(np.round(x * 32767.0), -32768, 32767).astype(np.int16)
    wavfile.write(path, SR, q.T if q.ndim == 2 else q)


def check_loop(x):
    """Return (ok, wrap_jump, typical_jump).  The wrap step x[0]-x[-1] must be
    statistically indistinguishable from ordinary sample-to-sample steps, and
    the slope must also be continuous."""
    x = stereo(x) if x.ndim == 1 else x
    worst, ok = 0.0, True
    typ = 0.0
    for c in x:
        d = np.diff(c)
        jump = abs(c[0] - c[-1])
        lim = max(np.percentile(np.abs(d), 99.9), 4 * np.std(d)) + 2.0 / 32767
        d2 = abs((c[1] - c[0]) - (c[-1] - c[-2]))
        lim2 = max(np.percentile(np.abs(np.diff(d)), 99.9), 4 * np.std(np.diff(d))) + 2.0 / 32767
        ok &= bool(jump <= lim and d2 <= lim2)
        worst = max(worst, jump)
        typ = max(typ, float(np.std(d)))
    return ok, worst, typ


# =============================================================================
#  Shared building blocks for several sounds
# =============================================================================


def creak(dur, rng, f0=180.0, rate=(25, 70), q_mult=1.0, bright=2500.0):
    """Wood/metal friction creak: stick-slip impulses with a wandering rate
    exciting a resonant body (a few inharmonic modes + formant-like bands)."""
    n = ns(dur)
    c = contour(n, 2.5, rng)
    r = rate[0] + (rate[1] - rate[0]) * (0.5 + 0.5 * c)
    exc = stick_slip(n, r, rng, jitter=0.08, amp_jit=0.3)
    exc = lp(exc, bright, 1)
    fr = f0 * np.array([1.0, 2.31, 3.9, 5.6, 8.3, 11.2]) * rng.uniform(0.95, 1.05, 6)
    y = resonators(exc, fr, q_mult * np.array([0.12, 0.09, 0.06, 0.05, 0.04, 0.03]),
                   [1.0, 0.7, 0.5, 0.35, 0.25, 0.15])
    e = smoothstep(np.clip(tvec(n) / (dur * 0.15), 0, 1)) * smoothstep(np.clip((dur - tvec(n)) / (dur * 0.3), 0, 1))
    e *= 0.6 + 0.4 * (0.5 + 0.5 * contour(n, 4, rng))
    return y * e


def groan(dur, rng, f0=55.0, f1=42.0, rough=0.4):
    """Large metal structure groan: low stick-slip at audio rate through a
    big inharmonic modal body, slowly bending in pitch."""
    n = ns(dur)
    t = tvec(n)
    fpitch = f0 + (f1 - f0) * smoothstep(t / dur) + 3 * contour(n, 1.5, rng)
    exc = stick_slip(n, fpitch, rng, jitter=0.06 + 0.1 * rough, amp_jit=0.25 + rough)
    fr, ts, am = metal_modes(f0 * 1.7, rng, count=12, t60=0.6, bright=1800.0, spread=0.08)
    y = resonators(exc, fr, ts * 0.4, am)
    y += 0.5 * lp(exc, 400.0, 2)
    e = smoothstep(t / (dur * 0.25)) * smoothstep((dur - t) / (dur * 0.4))
    return y * e


def rattle(dur, rng, rate=30.0, f_lo=1500.0, f_hi=6000.0, parts=6, t60=0.03, density=1.0):
    """Loose metal parts rattling: random micro-impacts on small modal objects."""
    n = ns(dur)
    y = np.zeros(n)
    for _ in range(parts):
        k = max(1, rng.poisson(rate * dur * density / parts))
        tm = np.sort(rng.uniform(0, dur, k))
        exc = impulses(n, tm, rng.uniform(0.2, 1.0, k))
        fr = rng.uniform(f_lo, f_hi, 3) * np.array([1.0, 1.53, 2.37])
        y += resonators(exc, fr, rng.uniform(0.6, 1.4, 3) * t60, [1.0, 0.6, 0.4])
    return y


def servo_whine(dur, rng, f_start=600.0, f_end=1400.0, grit=0.5, curve=None):
    """Electric servo/gearmotor: harmonic whine with gear-mesh sidebands,
    commutator noise and pitch sweep."""
    n = ns(dur)
    t = tvec(n)
    s = t / dur if curve is None else curve
    f = f_start + (f_end - f_start) * smoothstep(s)
    f = f * (1 + 0.006 * contour(n, 18, rng))
    amps = [1.0, 0.55, 0.42, 0.18, 0.25, 0.1, 0.12, 0.05]
    y = additive(f, n, amps)
    gear = 0.5 + 0.5 * np.sin(2 * np.pi * cycles(f / 7.0, n))
    y *= 1 - 0.35 * grit * gear
    nz = bpf(rng.standard_normal(n), 3500.0, 1.2) * (0.5 + 0.5 * np.sign(np.sin(2 * np.pi * cycles(f / 2, n))))
    y += grit * 0.25 * nz
    y = sat(y * 0.8, 1.6)
    return y


def stomp_body(dur, rng, f_hi=160.0, f_lo=45.0, decay=0.35):
    y = thump(dur, f_hi, f_lo, decay, rng, drop=0.018, noise=0.5, noise_lp=900.0)
    return y


def rain_layer(n, rng, rate, f_lo, f_hi, t60=0.01, dur_s=None):
    """Field of individual drop impacts: each drop = tiny damped resonance
    with randomized pitch (rendered by exciting a resonator bank)."""
    dur_s = n / SR if dur_s is None else dur_s
    y = np.zeros(n)
    nb = 6
    edges = np.geomspace(f_lo, f_hi, nb + 1)
    for b in range(nb):
        k = rng.poisson(rate * dur_s / nb)
        tm = rng.uniform(0, dur_s, k)
        am = rng.pareto(2.5, k) + 0.2
        exc = impulses(n, tm, am * rng.choice([-1, 1], k))
        fr = rng.uniform(edges[b], edges[b + 1], 3)
        y += resonators(exc, fr, [t60, t60 * 0.8, t60 * 0.6], [1, 0.7, 0.5])
    return y


def wrap_events(n, events):
    """events: iterable of (pos_samples, signal, gain).  Wrap-adds into a
    mono/stereo loop buffer of length n."""
    ch2 = any(np.asarray(s).ndim == 2 for _, s, _ in events)
    buf = np.zeros((2, n)) if ch2 else np.zeros(n)
    for pos, s, g in events:
        add(buf, s, pos, g, wrap=True)
    return buf


def music_box_note(freq, dur, rng, detune_c=0.0, bright=1.0):
    """Music-box tine: inharmonic cantilever-beam modes (1, 2.76, 5.40...
    ratios softened), fast pluck attack, very slight beating."""
    n = ns(dur)
    f = freq * 2 ** (detune_c / 1200.0)
    ratios = np.array([1.0, 1.0035, 2.756, 5.404, 8.933, 3.01])
    t60 = np.array([2.2, 2.0, 0.7, 0.3, 0.15, 0.5]) * (440.0 / max(f, 200)) ** 0.3
    amps = np.array([1.0, 0.35, 0.28 * bright, 0.12 * bright, 0.05 * bright, 0.05])
    y = modal(n, f * ratios, t60, amps, phases=rng.uniform(0, 0.2, 6), att=0.0015)
    tick = hp(rng.standard_normal(n), 4000.0) * env_perc(n, 0.0001, 0.003)
    return y + 0.15 * bright * tick


def piano_like(freq, dur, rng, vel=1.0):
    """Low piano-ish note: slightly inharmonic string partials (B ~ 0.0004),
    3 detuned strings, hammer thump, two-stage decay."""
    n = ns(dur)
    t = tvec(n)
    B = 0.0004
    y = np.zeros(n)
    for s, dc in enumerate([-1.2, 0.0, 1.4]):
        f = freq * 2 ** (dc / 1200.0)
        for k in range(1, 18):
            fk = f * k * np.sqrt(1 + B * k * k)
            if fk > 9000:
                break
            a = vel * (1.0 / k ** 1.1) * (0.6 + 0.4 * np.sin(np.pi * k * 0.13) ** 2)
            T1 = 1.2 * (6.0 / (1 + 0.25 * k))
            T2 = T1 * 4
            e = 0.7 * np.exp(-6.9 * t / T1) + 0.3 * np.exp(-6.9 * t / T2)
            y += a * e * np.sin(2 * np.pi * fk * t + rng.uniform(0, 6.28))
    y /= np.max(np.abs(y)) + 1e-9
    ham = lp(rng.standard_normal(n), 900.0) * env_perc(n, 0.001, 0.03)
    y += 0.2 * ham / (np.max(np.abs(ham)) + 1e-9)
    return y * env_perc(n, 0.004, 100.0)


def bowed_metal(dur, f0, rng, swell=0.5):
    """Bowed metal (cymbal/saw): noise+stick-slip exciting a sharp inharmonic
    mode bank, with a slow crescendo/decrescendo."""
    n = ns(dur)
    t = tvec(n)
    e = np.sin(np.pi * np.clip(t / dur, 0, 1)) ** 2
    exc = rng.standard_normal(n) * 0.2 + stick_slip(n, f0 * 0.5, rng, 0.05, 0.2)
    exc = bp(exc, 200.0, 6000.0)
    fr = f0 * np.array([1.0, 1.414, 1.87, 2.41, 2.98, 3.61, 4.47]) * rng.uniform(0.99, 1.01, 7)
    y = resonators(exc * e, fr, [3.0, 2.6, 2.2, 1.8, 1.5, 1.2, 1.0],
                   [1.0, 0.8, 0.6, 0.5, 0.35, 0.25, 0.15])
    return y


def bell(dur, f0, rng, t60=6.0, bright=1.0):
    """Church/clock bell partials (hum, prime, tierce, quint, nominal...)."""
    n = ns(dur)
    ratios = np.array([0.5, 1.0, 1.183, 1.506, 2.0, 2.514, 2.662, 3.011, 4.166, 5.433, 6.796])
    t60s = t60 * np.array([1.6, 1.0, 0.8, 0.6, 0.55, 0.4, 0.38, 0.3, 0.22, 0.15, 0.1])
    amps = np.array([0.6, 0.8, 0.7, 0.35, 1.0, 0.45, 0.3, 0.3, 0.25 * bright, 0.15 * bright, 0.1 * bright])
    fr = f0 * ratios
    # pairs of slightly split modes -> natural beating
    y = modal(n, fr, t60s, amps, phases=rng.uniform(0, 6.28, len(fr)), att=0.001)
    y += modal(n, fr * 1.0015, t60s, amps * 0.5, phases=rng.uniform(0, 6.28, len(fr)), att=0.001)
    clang = bp(rng.standard_normal(n), 1500.0, 8000.0) * env_perc(n, 0.0003, 0.03)
    return y / (np.max(np.abs(y)) + 1e-9) + 0.25 * bright * clang / (np.max(np.abs(clang)) + 1e-9)


def circ_delay(x, d):
    """Circular fractional delay (d in samples, per-sample array) - exact
    for loops; used for wow/flutter and chorus."""
    x = np.asarray(x, dtype=float)
    n = x.shape[-1]
    p = np.arange(n) - np.asarray(d, dtype=float)
    i0 = np.floor(p).astype(np.int64)
    fr = p - i0
    a, b = i0 % n, (i0 + 1) % n
    return x[..., a] * (1 - fr) + x[..., b] * fr


def mod_delay(x, d):
    """Non-circular fractional modulated delay (zeros before start)."""
    x = np.asarray(x, dtype=float)
    n = x.shape[-1]
    p = np.arange(n) - np.asarray(d, dtype=float)
    p = np.clip(p, 0, n - 1)
    i0 = np.floor(p).astype(np.int64)
    fr = p - i0
    i1 = np.minimum(i0 + 1, n - 1)
    return x[..., i0] * (1 - fr) + x[..., i1] * fr


def circ_smooth(x, sec):
    """Circular Gaussian smoothing (for loop envelopes)."""
    n = x.shape[-1]
    f = np.fft.rfftfreq(n, 1.0 / SR)
    sig = sec / 2.0
    return np.fft.irfft(np.fft.rfft(x) * np.exp(-2 * (np.pi * f * sig) ** 2), n)


def psat(x, drive=2.0, asym=0.0):
    """Saturation for loops (oversampling FIR applied periodically)."""
    return periodic(lambda y: sat(y, drive, 4, asym), x, warm=0.05)


def string_note(freqs, dur, rng, n_voices=4, vib=5.5, vib_depth=0.006,
                bright=3500.0, att=0.08, rel=0.25):
    """Ensemble string section on one or more pitches: detuned polyBLEP saws
    with independent vibrato, bow noise, and a violin-body EQ."""
    n = ns(dur)
    y = np.zeros(n)
    for f0 in np.atleast_1d(freqs):
        for v in range(n_voices):
            det = 2 ** (rng.uniform(-9, 9) / 1200.0)
            vr = vib * rng.uniform(0.85, 1.15)
            vib_s = 1 + vib_depth * np.sin(2 * np.pi * vr * tvec(n) + rng.uniform(0, 6.28))
            drift = 1 + 0.002 * contour(n, 0.7, rng)
            y += saw(f0 * det * vib_s * drift, n, rng.uniform())
    bow = bp(rng.standard_normal(n), 2000.0, 7000.0) * 0.04 * np.std(y)
    y = y + bow
    y = lp(y, bright, 2)
    y = peq(y, 280.0, 1.2, 4.0)
    y = peq(y, 1100.0, 1.5, 3.0)
    y = peq(y, 2600.0, 2.0, 3.5)
    e = env_pts(n, [(0, 0), (att, 1), (max(att, dur - rel), 1), (dur, 0)])
    return y * e


# =============================================================================
#  MUSIC / AMBIENCE LOOPS
# =============================================================================

MENU_MELODY = [
    # (beat, midi, length_beats)   E phrygian, 60 bpm (1 beat = 1 s)
    (0.0, 71, 2), (1.5, 72, 1), (2.0, 71, 2), (3.0, 67, 3),
    (6.0, 64, 2), (7.5, 65, 1), (8.0, 67, 2), (10.0, 65, 1.5), (11.0, 64, 4),
    (16.0, 76, 2), (17.0, 74, 1), (18.0, 72, 1), (19.0, 71, 2),
    (21.0, 72, 1), (21.5, 71, 1), (22.0, 67, 2), (24.5, 65, 1), (25.0, 64, 4),
]


@sound('mus_menu', 2, -6.0, loop=True)
def mus_menu():
    T = 96.0
    n = ns(T)
    rng = R(1001)
    t = tvec(n)
    out = np.zeros((2, n))

    # --- evolving drone pad: chord colours fade in and out (E phrygian) ---
    chords = [  # 16 s each
        [40, 47, 52, 55, 59],          # Em
        [41, 45, 52, 57, 60],          # F/E (phrygian bII over pedal)
        [40, 47, 52, 55, 62],          # Em(add b7 up top)
        [45, 48, 52, 57, 64],          # Am/E
        [41, 48, 53, 57, 60],          # F
        [40, 47, 52, 55, 59],          # Em
    ]
    seg = n // len(chords)
    pitches = sorted(set(p for c in chords for p in c))
    pad = np.zeros((2, n))
    for p in pitches:
        gate = np.zeros(n)
        for i, c in enumerate(chords):
            if p in c:
                gate[i * seg:(i + 1) * seg] = 1.0
        gate = np.clip(circ_smooth(gate, 4.0), 0, 1)
        if gate.max() < 1e-3:
            continue
        f0 = mtof(p)
        for side, det in ((0, -7.0), (1, 7.0)):
            for v in range(2):
                dcent = det + rng.uniform(-3, 3) + 4 * lfo_noise(n, 3, rng)
                f = f0 * 2 ** (dcent / 1200.0)
                pad[side] += gate * saw(f, n, rng.uniform(), is_periodic=True) * (0.8 if p > 55 else 1.0)
    cut = 380.0 * 2 ** (1.6 * (0.5 + 0.5 * lfo_noise(n, 5, rng, slope=1.4)))
    pad = tv_filter(pad, cut, q=1.1, kind='lp', block=128, is_periodic=True, stages=2)
    pad *= 0.8 + 0.2 * lfo_noise(n, 7, rng)
    out += 0.11 * pad
    # sub drone on E1 with slow breathing
    sub = sine(qf(mtof(28), T), n, is_periodic=True) * (0.7 + 0.3 * lfo_noise(n, 4, rng))
    sub += 0.4 * sine(qf(mtof(40), T), n, is_periodic=True)
    out += 0.25 * sub

    # --- lonely music box, slightly out of tune with wow & flutter ---
    box = np.zeros((2, n))
    evs = [(8.0, MENU_MELODY, 0), (56.0, MENU_MELODY, 1)]
    for start, mel, var in evs:
        for k, (b, m, L) in enumerate(mel):
            mm = m - 12 if (var == 1 and k % 5 == 3) else m
            note = music_box_note(mtof(mm), 4.5, rng, detune_c=-9 + rng.uniform(-14, 14),
                                  bright=0.8 + 0.4 * rng.uniform())
            vel = 0.55 + 0.35 * rng.uniform()
            add(box, pan(note, rng.uniform(-0.35, 0.35)), ns(start + b + rng.uniform(-0.03, 0.05)),
                vel, wrap=True)
            if var == 1 and k in (4, 9, 13):   # dissonant "wrong" grace note
                g = music_box_note(mtof(mm + 1), 3.0, rng, detune_c=rng.uniform(-20, 20), bright=0.6)
                add(box, pan(g, 0.4), ns(start + b - 0.12), 0.25, wrap=True)
    # fragments in the gap
    for b, m in ((44.0, 76), (46.5, 77), (49.0, 71), (90.0, 64), (92.5, 65)):
        note = music_box_note(mtof(m), 4.5, rng, detune_c=-12 + rng.uniform(-10, 10))
        add(box, pan(note, rng.uniform(-0.6, 0.6)), ns(b), 0.35, wrap=True)
    wow = 0.0016 * SR * (0.5 + 0.5 * np.sin(2 * np.pi * qf(0.55, T) * t)) \
        + 0.00012 * SR * np.sin(2 * np.pi * qf(7.3, T) * t) \
        + 0.0006 * SR * (0.5 + 0.5 * lfo_noise(n, 40, rng))
    box = circ_delay(box, wow)
    box = periodic(hp, box, 250.0)
    out += 1.25 * box

    # --- distant bowed-metal swells ---
    bm = np.zeros((2, n))
    for st, f0, L in ((14.0, 311.0, 9.0), (38.0, 233.0, 11.0), (66.0, 277.0, 10.0), (84.0, 207.0, 9.0)):
        s = bowed_metal(L, f0, rng)
        s = lp(s, 2500.0, 2)
        add(bm, pan(s / (np.max(np.abs(s)) + 1e-9), rng.uniform(-0.8, 0.8)), ns(st), 1.0, wrap=True)
    out += 0.2 * bm

    # --- occasional low piano notes ---
    pn = np.zeros((2, n))
    for st, m, v in ((0.0, 28, 1.0), (16.0, 29, 0.8), (32.0, 28, 0.9), (48.0, 33, 0.8),
                     (64.0, 29, 0.85), (80.0, 28, 0.9), (88.0, 35, 0.4)):
        s = piano_like(mtof(m + 12), 8.0, rng, vel=v)
        s = lp(s, 1800.0, 2)
        add(pn, pan(s, -0.15), ns(st + 0.05), v, wrap=True)
    out += 0.22 * pn

    # --- big reverb (circular) ---
    ir = make_ir(rt60=6.5, predelay=0.04, lf=1.2, hf=0.4, seed=101, er_gain=0.25)
    out = reverb(out, ir, wet=0.55, dry=0.75, is_periodic=True)

    # --- tape hiss + vinyl crackle ---
    hiss = pnoise(n, rng, slope=-1.5, lo=600.0, hi=9000.0, ch=2) * 0.006
    hiss *= 0.85 + 0.15 * lfo_noise(n, 30, rng)
    tm = poisson_times(1.6, T, rng)
    crk = impulses(n, tm, rng.pareto(2.0, len(tm)) * rng.choice([-1, 1], len(tm)))
    crk = periodic(bp, crk, 1500.0, 8000.0, warm=0.05) * 0.02
    out += hiss + np.stack([crk, np.roll(crk, 7)])
    return out


@sound('mus_chase', 2, -3.0, loop=True)
def mus_chase():
    bpm = 150.0
    beat = 60.0 / bpm
    T = 80 * beat                     # 32.0 s, 20 bars of 4/4
    n = ns(T)
    rng = R(1002)
    out = np.zeros((2, n))
    drums = np.zeros((2, n))

    def taiko(big=1.0):
        d = 0.9
        y = thump(d, 125.0 * (0.9 + 0.2 * big), 52.0, 0.45 * big + 0.15, rng, drop=0.03, noise=0.6, noise_lp=1200.0)
        skin = bpf(rng.standard_normal(ns(d)), 180.0, 1.5) * env_perc(ns(d), 0.001, 0.2)
        return sat(y + 0.5 * skin / (np.max(np.abs(skin)) + 1e-9), 1.8)

    def clank():
        y = metal_hit(0.5, rng.uniform(380, 520), rng, count=12, t60=0.25, bright=7000, noise=0.6)
        return hp(y, 300.0)

    for bar in range(20):
        b0 = bar * 4
        hits = [(0, 1.0), (1.5, 0.7), (2.0, 0.9), (3.0, 0.8)]
        if bar % 4 == 3:
            hits += [(3.25, 0.6), (3.5, 0.75), (3.75, 0.9)]
        if bar % 2 == 1:
            hits += [(0.75, 0.5)]
        for bt, v in hits:
            s = taiko(v)
            add(drums, pan(s, rng.uniform(-0.2, 0.2)), ns((b0 + bt) * beat), v, wrap=True)
        for off in (0.5, 1.5, 2.5, 3.5):
            if off == 1.5 and bar % 2 == 0:
                continue
            s = clank()
            add(drums, pan(s, rng.choice([-0.55, 0.55])), ns((b0 + off) * beat), 0.35 + 0.15 * rng.uniform(), wrap=True)
        # industrial hat-like grind on 16ths
        for k in range(16):
            s = bp(rng.standard_normal(ns(0.06)), 4000, 12000) * env_perc(ns(0.06), 0.0005, 0.03)
            add(drums, pan(s, 0.3 if k % 2 else -0.3), ns((b0 + k * 0.25) * beat), 0.08 + 0.06 * (k % 2), wrap=True)

    # distorted sub pulses (side-chain feel, on every beat)
    t = tvec(n)
    subf = qf(36.71, T)  # D1
    sub = sine(subf, n, is_periodic=True) + 0.3 * sine(2 * subf, n, is_periodic=True)
    ph = (t / beat) % 1.0
    pump = np.minimum(1, ph / 0.04) * np.exp(-ph * 3.0)
    sub = psat(sub * pump * 1.3, 3.0) * 0.5
    sub = periodic(lp, sub, 180.0, 4)

    # dissonant tremolo string clusters (minor seconds), 4-bar segments
    strings = np.zeros((2, n))
    clusters = [[50, 51, 57, 58], [49, 50, 56, 57], [50, 51, 57, 58, 62, 63],
                [51, 52, 58, 59], [50, 51, 57, 58, 69, 70]]
    seg = 16 * beat
    for i, cl in enumerate(clusters):
        L = seg + 0.4
        for side, sub_cl in ((0, cl[0::2]), (1, cl[1::2])):
            s = string_note(mtof(np.array(sub_cl)), L, rng, n_voices=3, vib=6.0, att=0.15, rel=0.4)
            tt = tvec(len(s))
            sixteenth = beat / 4
            tp = (tt / sixteenth) % 1.0
            trem = 0.35 + 0.65 * np.exp(-tp * 6.0) * np.minimum(1, tp / 0.08 + 0.2)
            s *= trem
            add(strings[side], s, ns(i * seg), 1.0, wrap=True)
    # high screeching cluster pairs (sul ponticello) on last bars of each segment
    for i in range(5):
        s = string_note(mtof(np.array([86, 87])), 4 * beat + 0.3, rng, n_voices=3, vib=7.0,
                        vib_depth=0.01, bright=7000.0, att=1.2, rel=0.3)
        s = hp(s, 1200.0)
        add(strings, pan(s, -0.6 if i % 2 else 0.6), ns((i * 16 + 12) * beat), 0.45, wrap=True)

    # shepard-tone rising tension: one octave per 8 s, folded so it is periodic
    Ts = 8.0
    K = 6
    vn = ns(K * Ts)
    tv = tvec(vn)
    oct_pos = tv / Ts                     # 0..K octaves
    f = 55.0 * 2 ** oct_pos
    win = np.exp(-0.5 * ((oct_pos - K / 2) / (K / 5.5)) ** 2)
    win *= np.sin(np.pi * tv / (K * Ts)) ** 0.5
    v = additive(f, vn, [1.0, 0.3, 0.12]) * win
    v2 = additive(f * 2 ** (1 / 12.0), vn, [1.0, 0.3, 0.12]) * win  # minor-second shadow
    vv = v + 0.5 * v2
    ns_ = ns(Ts)
    fold = np.zeros(ns_)
    for k in range(K):
        fold += vv[k * ns_:(k + 1) * ns_]
    shep = np.tile(fold, int(round(T / Ts)))[:n]
    shep = to_len(shep, n)
    shep = np.stack([shep, circ_delay(shep, np.full(n, ns(0.011)))])

    mix = 0.55 * drums + 0.5 * sub + 0.22 * strings + 0.10 * shep
    ir = make_ir(rt60=1.8, predelay=0.015, seed=102, er_gain=0.6)
    mix = reverb(mix, ir, wet=0.28, dry=1.0, is_periodic=True)
    mix = psat(mix / (np.max(np.abs(mix)) + 1e-9) * 1.2, 1.6)
    return mix


@sound('heartbeat_loop', 2, -6.0, loop=True)
def heartbeat_loop():
    T = 4.0
    n = ns(T)
    rng = R(1003)
    buf = np.zeros(n)
    period = 0.8                        # 75 bpm -> five beats per 4 s loop
    for k in range(5):
        t0 = k * period + 0.01 * rng.standard_normal()
        lub = thump(0.35, 75.0, 38.0, 0.16, rng, drop=0.03, noise=0.3, noise_lp=250.0)
        dub = thump(0.30, 95.0, 48.0, 0.11, rng, drop=0.02, noise=0.25, noise_lp=300.0)
        a = 0.9 + 0.1 * rng.uniform()
        add(buf, lub, ns(t0), a, wrap=True)
        add(buf, dub, ns(t0 + 0.27), 0.65 * a, wrap=True)
    buf = periodic(lp, buf, 160.0, 2, warm=0.5)
    buf = psat(buf / np.max(np.abs(buf)) * 1.4, 1.5)
    body = periodic(resonators, buf, [62.0, 110.0], [0.15, 0.08], [0.15, 0.08], warm=1.0)
    y = buf + body
    ir = make_ir(rt60=0.35, predelay=0.002, seed=103, er_gain=0.2, dark_hz=600.0)
    return reverb(y, ir, wet=0.25, is_periodic=True)


CAROUSEL = {
    # 8 bars of 3/4 at 90 bpm (2 s per bar). Melody (beat, midi, beats)
    'mel': [(0, 76, 2), (2, 75, 1), (3, 76, 1), (4, 72, 1), (5, 69, 1),
            (6, 71, 2), (8, 68, 1), (9, 69, 3),
            (12, 72, 1), (13, 71, 1), (14, 69, 1), (15, 68, 1), (16, 65, 1), (17, 64, 1),
            (18, 65, 1), (19, 64, 1), (20, 63, 1), (21, 64, 3)],
    'chords': [(45, [57, 60, 64]), (45, [57, 60, 64]), (40, [56, 59, 64]), (45, [57, 60, 64]),
               (38, [57, 62, 65]), (40, [56, 59, 64]), (47, [53, 59, 62]), (40, [56, 59, 62, 64])],
}


def calliope_pipe(freq, dur, rng, reed=0.35):
    """Steam calliope / band-organ pipe: flue tone (sine + a few harmonics),
    breathy chiff, plus a tinny reed rank (band-limited narrow pulse)."""
    n = ns(dur)
    t = tvec(n)
    vib = 1 + 0.004 * np.sin(2 * np.pi * 6.2 * t + rng.uniform(0, 6))
    f = freq * vib
    flue = additive(f, n, [1.0, 0.35, 0.22, 0.08, 0.05])
    rd = pulse(f * 1.003, n, duty=0.18, phase0=rng.uniform())
    rd = lp(rd, 5000.0, 2)
    chiff = bp(rng.standard_normal(n), freq * 2, freq * 6) * env_perc(n, 0.004, 0.06)
    y = flue + reed * rd + 0.3 * chiff
    e = env_pts(n, [(0, 0), (0.012, 1), (max(0.02, dur - 0.04), 0.9), (dur, 0)])
    return y * e


@sound('mus_carousel', 2, -6.0, loop=True)
def mus_carousel():
    T = 16.0
    n = ns(T)
    rng = R(1004)
    beat = 2.0 / 3.0
    mel = np.zeros(n)
    acc = np.zeros(n)
    for b, m, L in CAROUSEL['mel']:
        d = L * beat * 0.92
        s = calliope_pipe(mtof(m) * 2 ** (rng.uniform(-12, 12) / 1200), d, rng, reed=0.45)
        s += 0.4 * calliope_pipe(mtof(m + 12) * 2 ** (rng.uniform(-20, 20) / 1200), d, rng, reed=0.2)
        add(mel, s, ns(b * beat), 0.5, wrap=True)
    for bar, (root, ch) in enumerate(CAROUSEL['chords']):
        t0 = bar * 3 * beat
        s = calliope_pipe(mtof(root - 12), beat * 0.8, rng, reed=0.6)
        s += 0.6 * calliope_pipe(mtof(root), beat * 0.8, rng, reed=0.3)
        add(acc, s, ns(t0), 0.5, wrap=True)
        for k in (1, 2):
            for p in ch:
                s = calliope_pipe(mtof(p) * 2 ** (rng.uniform(-15, 15) / 1200), beat * 0.45, rng, reed=0.5)
                add(acc, s, ns(t0 + k * beat), 0.16, wrap=True)
        # little glockenspiel accents on the bar
        g = music_box_note(mtof(ch[-1] + 12), 1.5, rng, detune_c=rng.uniform(-25, 25))
        add(acc, g, ns(t0), 0.12, wrap=True)
        # bass-drum "oom" and cymbal tick of the band organ
        add(acc, thump(0.3, 110, 50, 0.12, rng, noise=0.2), ns(t0), 0.35, wrap=True)
        tk = hp(rng.standard_normal(ns(0.12)), 6000) * env_perc(ns(0.12), 0.0005, 0.05)
        add(acc, tk, ns(t0 + 2 * beat), 0.06, wrap=True)
    y = mel + acc
    # mechanical clatter of the organ / wooden figures
    tm = poisson_times(9.0, T, rng)
    clat = impulses(n, tm, rng.uniform(0.2, 1.0, len(tm)))
    clat = periodic(resonators, clat, [900, 1800, 2700], [0.02, 0.015, 0.01], [1, 0.6, 0.4], warm=0.1)
    y += 0.08 * clat / (np.max(np.abs(clat)) + 1e-9)
    # tinny: band-limit like a horn speaker, slight drive
    y = periodic(hp, y, 220.0, 2, warm=0.3)
    y = periodic(lp, y, 5500.0, 2, warm=0.3)
    y = periodic(peq, y, 1800.0, 1.0, 5.0, warm=0.3)
    y = psat(y / np.max(np.abs(y)) * 1.1, 1.8)
    # wobble: periodic wow with two "drags" where the motor seems to slow
    t = tvec(n)
    drag = np.exp(-0.5 * ((((t - 5.0) + T / 2) % T - T / 2) / 0.6) ** 2) \
        + 0.7 * np.exp(-0.5 * ((((t - 13.0) + T / 2) % T - T / 2) / 0.5) ** 2)
    dly = SR * (0.004 * (0.5 + 0.5 * np.sin(2 * np.pi * qf(0.8, T) * t)) + 0.012 * circ_smooth(drag, 0.2))
    y = circ_delay(y, dly)
    yst = np.stack([y, circ_delay(y, np.full(n, ns(0.013)))])
    ir = make_ir(rt60=2.6, predelay=0.03, seed=104, er_gain=0.4)
    return reverb(yst, ir, wet=0.35, is_periodic=True)


def hum(n, T, rng, base=50.0, amps=(1.0, 0.5, 0.35, 0.2, 0.12, 0.08, 0.05), buzz=0.0):
    """Mains hum, periodic over T: harmonics of 50 Hz with slow amplitude
    drift; optional rectified-buzz component (odd/even rich)."""
    y = np.zeros(n)
    for k, a in enumerate(amps):
        f = qf(base * (k + 1), T)
        y += a * (0.8 + 0.2 * lfo_noise(n, 2 + k, rng)) * sine(f, n, rng.uniform(), is_periodic=True)
    if buzz > 0:
        b = np.abs(sine(qf(base, T), n, is_periodic=True)) ** 8
        b = periodic(lambda z: bp(z, 400.0, 6000.0), b - b.mean(), warm=0.2)
        y += buzz * b / (np.max(np.abs(b)) + 1e-9)
    return y


@sound('amb_hall', 2, -12.0, loop=True)
def amb_hall():
    T = 64.0
    n = ns(T)
    rng = R(2001)
    out = np.zeros((2, n))
    # HVAC / air rumble: brown-ish noise, slowly breathing
    air = pnoise(n, rng, slope=-5.0, lo=25.0, hi=900.0, ch=2)
    air *= 0.8 + 0.2 * np.stack([lfo_noise(n, 5, rng), lfo_noise(n, 5, rng)])
    out += 0.35 * air
    duct = pnoise(n, rng, slope=-2.0, lo=150.0, hi=2500.0, ch=2) * 0.05
    out += duct
    # quiet electrical hum
    h = hum(n, T, rng)
    out += 0.05 * np.stack([h, h])
    # events (dry) -> reverb
    ev = np.zeros((2, n))
    for t0 in (3.0, 19.0, 37.0, 52.0):
        c = creak(rng.uniform(1.6, 2.6), rng, f0=rng.uniform(120, 260), rate=(18, 55))
        add(ev, pan(c / (np.max(np.abs(c)) + 1e-9), rng.uniform(-0.9, 0.9)), ns(t0), 0.35, wrap=True)
    for t0 in (10.0, 44.0, 60.5):
        g = groan(rng.uniform(3.0, 4.5), rng, f0=rng.uniform(40, 60), f1=rng.uniform(32, 45))
        add(ev, pan(g / (np.max(np.abs(g)) + 1e-9), rng.uniform(-0.7, 0.7)), ns(t0), 0.45, wrap=True)
    for t0 in (27.5, 57.0):
        c = metal_hit(2.0, rng.uniform(90, 160), rng, count=16, t60=1.6, bright=2500, noise=0.4)
        c = lp(c, 2200.0)
        add(ev, pan(c, rng.uniform(-0.8, 0.8)), ns(t0), 0.4, wrap=True)
    # faint drips (a slow leak somewhere and a random one)
    drip_t = list(np.arange(1.3, T, 4.7)) + list(rng.uniform(0, T, 6))
    for t0 in drip_t:
        n_d = ns(0.25)
        tt = tvec(n_d)
        f = 1200 + 1800 * (1 - np.exp(-tt / 0.012))
        d = np.sin(2 * np.pi * cycles(f * rng.uniform(0.8, 1.25), n_d)) * env_perc(n_d, 0.0005, 0.05)
        add(ev, pan(d, rng.uniform(-0.6, 0.6)), ns(t0), 0.06 * rng.uniform(0.5, 1.0), wrap=True)
    ir = make_ir(rt60=4.5, predelay=0.06, lf=1.3, hf=0.35, seed=201, er_gain=0.2, dark_hz=6000.0)
    evw = reverb(ev, ir, wet=1.0, dry=0.12, is_periodic=True)
    out += evw
    return out


@sound('amb_dark', 2, -10.0, loop=True)
def amb_dark():
    T = 48.0
    n = ns(T)
    rng = R(2002)
    t = tvec(n)
    out = np.zeros((2, n))
    # sub drone (C#1 + fifth) with slow swell
    sub = sine(qf(34.65, T), n, is_periodic=True) * (0.75 + 0.25 * lfo_noise(n, 3, rng))
    sub += 0.35 * sine(qf(51.9, T), n, is_periodic=True)
    # 2nd/3rd harmonics so the drone still reads on small speakers
    sub += 0.25 * sine(qf(69.3, T), n, is_periodic=True) + 0.12 * sine(qf(103.9, T), n, is_periodic=True)
    out += 0.22 * sub
    # low dark pad (detuned saws, heavily lowpassed) in the 80-500 Hz range
    padd = np.zeros((2, n))
    for side in (0, 1):
        for m_, a in ((37, 1.0), (44, 0.7), (49, 0.5), (50, 0.35)):  # C#2 G#2 C#3 D3 (b9 rub)
            f = mtof(m_) * 2 ** ((rng.uniform(-6, 6) + 3 * lfo_noise(n, 3, rng)) / 1200)
            padd[side] += a * saw(f, n, rng.uniform(), is_periodic=True)
    padd = tv_filter(padd, 260.0 * 2 ** (0.8 * lfo_noise(n, 4, rng)), q=0.9, kind='lp', block=128,
                     is_periodic=True, stages=2)
    out += 0.06 * padd
    # slow dissonant beating tones (pairs a few cents/Hz apart)
    for f0, df, a in ((138.6, 0.25, 0.10), (146.8, 0.5, 0.07), (277.2, 0.75, 0.04), (415.3, 0.375, 0.03)):
        for side in (0, 1):
            f1 = qf(f0 + (df if side else 0.0), T)
            f2 = qf(f0 + df * 2 + (0 if side else df), T)
            tone = additive(np.full(n, f1), n, [1, 0.25, 0.1], is_periodic=True) + \
                additive(np.full(n, f2), n, [1, 0.25, 0.1], is_periodic=True)
            out[side] += a * tone * (0.5 + 0.5 * lfo_noise(n, 4, rng))
    # breathy formant whispers
    wh = np.zeros((2, n))
    vow = list(VOWELS.keys())
    for t0 in (2.0, 9.5, 17.0, 24.0, 31.5, 39.0, 44.5):
        L = rng.uniform(1.5, 3.0)
        m = ns(L)
        src = hp(rng.standard_normal(m), 300.0)
        segs = []
        tt = 0.0
        while tt < L:
            segs.append((tt, rng.choice(vow)))
            tt += rng.uniform(0.08, 0.22)
        W = vowel_track(m, segs, smooth=0.05)
        w = vowel_morph(src, W, shift=rng.uniform(0.9, 1.25), bwmul=1.6)
        syl = 0.3 + 0.7 * (0.5 + 0.5 * contour(m, 6, rng)) ** 2
        w *= syl * np.sin(np.pi * np.clip(tvec(m) / L, 0, 1)) ** 1.5
        w = hp(w, 500.0)
        w /= np.max(np.abs(w)) + 1e-9
        add(wh, pan(w, rng.uniform(-1, 1)), ns(t0), 0.13, wrap=True)
    # very quiet ticks (like something mechanical ticking in the walls)
    tk = np.zeros((2, n))
    for t0 in np.arange(0.0, T, 0.5):
        if rng.uniform() < 0.35:
            c = click(0.05, rng, 2500, 9000, t60=0.008)
            add(tk, pan(c, rng.uniform(-0.8, 0.8)), ns(t0 + rng.uniform(-0.02, 0.02)), 0.02, wrap=True)
    ir = make_ir(rt60=3.5, predelay=0.03, seed=202, er_gain=0.3, hf=0.4)
    out += reverb(wh + tk, ir, wet=0.8, dry=0.35, is_periodic=True)
    # dark air bed
    out += 0.06 * pnoise(n, rng, slope=-4.0, lo=40.0, hi=1500.0, ch=2)
    return out


def wind_gust(n, T, rng, strength=1.0):
    """Wind: band-limited noise through time-varying band-passes whose centre
    follows gust intensity, plus whistling resonances."""
    g = 0.5 + 0.5 * lfo_noise(n, T / 6.0, rng, slope=1.2)
    g = g ** 2
    base = pnoise(n, rng, slope=-3.0, lo=60.0, hi=6000.0, ch=2)
    fc = 250.0 + 900.0 * g
    y = np.stack([tv_filter(base[c], fc, q=0.9, kind='bp', block=256, is_periodic=True) for c in range(2)])
    whistle = tv_filter(base[0], 900 + 700 * g, q=12.0, kind='bp', block=256, is_periodic=True)
    y += 0.25 * np.stack([whistle, np.roll(whistle, ns(0.02))])
    return strength * y * (0.25 + g)


@sound('amb_rain_ext', 2, -8.0, loop=True)
def amb_rain_ext():
    T = 30.0
    n = ns(T)
    rng = R(2003)
    out = np.zeros((2, n))
    # dense bed (pinkish noise, lightly band-limited)
    bed = pnoise(n, rng, slope=-1.0, lo=200.0, hi=12000.0, ch=2)
    bed *= 0.85 + 0.15 * np.stack([lfo_noise(n, 9, rng), lfo_noise(n, 9, rng)])
    out += 0.45 * bed
    # low "roar" of rain on ground
    roar = pnoise(n, rng, slope=-4.0, lo=60.0, hi=600.0, ch=2)
    out += 0.35 * roar
    # individual close splatters on surfaces (wrap-safe: periodic resonators)
    for side in (0, 1):
        exc_n = np.zeros(n)
        k = rng.poisson(900 * T)
        np.add.at(exc_n, rng.integers(0, n, k), (np.minimum(rng.pareto(2.2, k), 2.5) + 0.1) * rng.choice([-1, 1], k))
        splat = periodic(lambda z: resonators(z, [2400, 3700, 5200, 7600], [0.008, 0.006, 0.005, 0.004],
                                              [1, 0.8, 0.6, 0.4]), exc_n, warm=0.1)
        splat = periodic(hp, splat, 1500.0, warm=0.1)
        out[side] += 0.08 * splat / (np.std(splat) + 1e-9)
        # bigger drops on puddles / tin
        k2 = rng.poisson(14 * T)
        exc2 = np.zeros(n)
        np.add.at(exc2, rng.integers(0, n, k2), rng.uniform(0.3, 1.0, k2))
        tin = periodic(lambda z: resonators(z, [820, 1330, 2210, 3480], [0.05, 0.04, 0.03, 0.02],
                                            [1, 0.7, 0.5, 0.3]), exc2, warm=0.2)
        out[side] += 0.06 * tin / (np.max(np.abs(tin)) + 1e-9)
    # gutter drips: steady streams of drops in two places
    for t_per, f, p in ((0.31, 1700.0, -0.7), (0.47, 1150.0, 0.6)):
        for t0 in np.arange(0, T, t_per):
            tt0 = t0 + rng.normal(0, 0.03)
            m = ns(0.12)
            tt = tvec(m)
            ff = f * rng.uniform(0.9, 1.1) * (1 + 0.6 * (1 - np.exp(-tt / 0.01)))
            d = np.sin(2 * np.pi * cycles(ff, m)) * env_perc(m, 0.0005, 0.04)
            add(out, pan(d, p), ns(tt0), 0.07 * rng.uniform(0.6, 1.0), wrap=True)
    # gusts of wind
    out += 0.35 * wind_gust(n, T, rng)
    ir = make_ir(rt60=1.2, predelay=0.01, seed=203, er_gain=0.3)
    return reverb(out, ir, wet=0.2, is_periodic=True)


@sound('amb_rain_roof', 2, -14.0, loop=True)
def amb_rain_roof():
    T = 30.0
    n = ns(T)
    rng = R(2004)
    out = np.zeros((2, n))
    # drumming on corrugated metal: dense drop impacts exciting low-ish
    # panel modes, heard through the structure (low-passed)
    panel_f = [180, 265, 410, 560, 780, 1020, 1380]
    panel_t = [0.20, 0.16, 0.12, 0.09, 0.07, 0.05, 0.04]
    panel_g = [1.0, 0.8, 0.7, 0.5, 0.4, 0.3, 0.2]
    for side in (0, 1):
        exc = np.zeros(n)
        k = rng.poisson(1400 * T)
        np.add.at(exc, rng.integers(0, n, k), (np.minimum(rng.pareto(2.5, k), 2.5) + 0.2))
        exc *= 0.8 + 0.2 * (0.5 + 0.5 * lfo_noise(n, 8, rng))
        drum = periodic(lambda z: resonators(z, [f * rng.uniform(0.97, 1.03) for f in panel_f],
                                             panel_t, panel_g), exc, warm=0.5)
        out[side] += drum / (np.std(drum) + 1e-9)
    out += 1.2 * pnoise(n, rng, slope=-3.0, lo=80.0, hi=2000.0, ch=2)
    out = fft_eq(out, lambda f: bw_mag(f, 900.0, 3, 'lp') * bw_mag(f, 50.0, 2, 'hp'))
    # occasional heavier water runs off the gutter (soft, far)
    out += 0.4 * wind_gust(n, T, rng) * 0.3
    ir = make_ir(rt60=3.8, predelay=0.04, seed=204, er_gain=0.2, hf=0.35, dark_hz=4000)
    return reverb(out, ir, wet=0.6, is_periodic=True)


# =============================================================================
#  ONE-SHOT MUSIC / STINGERS (stereo)
# =============================================================================


@sound('mus_escape', 2, -6.0)
def mus_escape():
    D = 20.0
    n = ns(D)
    rng = R(3001)
    t = tvec(n)
    out = np.zeros((2, n))
    # soft pad: E minor -> C -> G -> E major (picardy resolution = dawn)
    prog = [(0.0, [40, 52, 55, 59, 64]), (4.0, [36, 48, 55, 60, 64]),
            (8.0, [43, 50, 55, 59, 62]), (12.0, [40, 52, 56, 59, 64, 68])]
    for i, (t0, ch) in enumerate(prog):
        L = (prog[i + 1][0] - t0 + 2.5) if i + 1 < len(prog) else D - t0
        m = ns(L)
        e = env_pts(m, [(0, 0), (1.8, 1), (max(1.9, L - 2.5), 1), (L, 0)])
        for p in ch:
            for side, dc in ((0, -6), (1, 6)):
                s = saw(mtof(p) * 2 ** ((dc + rng.uniform(-3, 3)) / 1200) * (1 + 0.002 * contour(m, 0.5, rng)), m, rng.uniform())
                s = lp(s, 900.0 + 500.0 * (i / 3.0), 2)
                add(out[side], s * e * (0.8 if p > 60 else 1.0), ns(t0), 0.05)
    # music-box motif, slower, resolved upward to the major third
    mel = [(0.8, 71, 0.6), (2.0, 72, 0.5), (2.6, 71, 0.6), (4.0, 67, 0.6), (5.6, 64, 0.5),
           (7.2, 66, 0.5), (8.0, 67, 0.6), (9.4, 71, 0.6), (10.6, 74, 0.5), (12.0, 76, 0.7),
           (13.2, 75, 0.45), (14.0, 71, 0.5), (15.2, 68, 0.55), (16.4, 76, 0.4)]
    box = np.zeros((2, n))
    for t0, m_, v in mel:
        s = music_box_note(mtof(m_), 5.0, rng, detune_c=rng.uniform(-6, 6), bright=0.9)
        add(box, pan(s, rng.uniform(-0.3, 0.3)), ns(t0), v)
    wow = SR * (0.0012 * (0.5 + 0.5 * np.sin(2 * np.pi * 0.5 * t)))
    box = mod_delay(box, wow)
    out += 0.35 * box
    # warm low piano root & a distant bell at dawn
    pn = piano_like(mtof(40), 8.0, rng)
    add(out, pan(lp(pn, 1500.0), -0.1), ns(12.0), 0.18)
    b = bell(7.0, mtof(64), rng, t60=5.0, bright=0.6)
    add(out, pan(lp(b, 3000.0), 0.4), ns(12.05), 0.07)
    # subtle airy shimmer rising toward dawn
    air = hp(rng.standard_normal((2, n)), 4000.0) * 0.008 * smoothstep(t / D)[None, :]
    out += air
    ir = make_ir(rt60=5.0, predelay=0.03, seed=301, er_gain=0.2, hf=0.45)
    out = reverb(out, ir, wet=0.45, out_len=n)
    out *= env_pts(n, [(0, 1), (D - 6.0, 1), (D, 0)]) ** 1.5
    return out


@sound('detect_sting', 2, -2.0)
def detect_sting():
    D = 2.5
    n = ns(D)
    rng = R(3002)
    t = tvec(n)
    hit_t = 0.32
    out = np.zeros((2, n))
    # reverse swell into the impact (reversed reverb'd noise + cymbal)
    m = ns(hit_t)
    cym = metal_hit(1.2, 420.0, rng, count=24, t60=0.9, bright=9000, noise=0.8)
    cym = reverb(cym, make_ir(rt60=1.2, seed=302), wet=0.7, out_len=ns(1.2))
    rev = cym[:, :m][:, ::-1] * np.linspace(0, 1, m) ** 2
    out[:, :m] += 0.9 * rev / (np.max(np.abs(rev)) + 1e-9)
    # impact: metal crash + low boom
    imp = np.zeros((2, n - ns(hit_t)))
    mh = metal_hit(D - hit_t, 140.0, rng, count=20, t60=1.6, bright=5000, noise=0.7)
    boom = thump(D - hit_t, 120.0, 38.0, 0.9, rng, drop=0.05, noise=0.6, noise_lp=800.0)
    imp += pan(sat(0.8 * mh + boom, 2.0), 0.0)
    # string screech cluster: high ponticello minor seconds with fast bow
    cl = mtof(np.array([84, 85, 86, 90, 91]))
    s = string_note(cl, D - hit_t, rng, n_voices=3, vib=7.5, vib_depth=0.012, bright=9000.0, att=0.01, rel=1.2)
    s = hp(s, 900.0)
    gliss = string_note(mtof(97), D - hit_t, rng, n_voices=2, vib=9.0,
                        vib_depth=0.02, bright=10000.0, att=0.01, rel=1.5)
    sl = np.stack([s, hp(gliss, 2000.0)])
    sl /= np.max(np.abs(sl)) + 1e-9
    imp += 0.55 * sl * env_perc(imp.shape[1], 0.005, 1.6)[None, :]
    out[:, ns(hit_t):] += imp
    ir = make_ir(rt60=2.2, predelay=0.01, seed=303, er_gain=0.5)
    out = reverb(out, ir, wet=0.35, out_len=n)
    return sat(out / np.max(np.abs(out)) * 1.3, 1.5)


@sound('death_sting', 2, -3.0)
def death_sting():
    D = 4.0
    n = ns(D)
    rng = R(3003)
    t = tvec(n)
    out = np.zeros((2, n))
    # low brass-like doom: band-limited saws through a brassy envelope-
    # controlled lowpass (filter opens with the attack = "blat")
    brass = np.zeros(n)
    for m in (26, 33, 38, 39):       # D1 A1 D2 Eb2 -> cluster of doom
        for v in range(3):
            f = mtof(m) * 2 ** (rng.uniform(-8, 8) / 1200) * (1 - 0.03 * smoothstep(t / D))
            brass += saw(f, n, rng.uniform())
    fc = 120.0 + 2200.0 * env_perc(n, 0.03, 1.3) + 200.0 * env_perc(n, 0.0, 3.5)
    brass = tv_filter(brass, fc, q=1.4, kind='lp', block=64, stages=2)
    brass = sat(brass / (np.max(np.abs(brass)) + 1e-9) * 1.4, 2.0)
    brass *= env_pts(n, [(0, 0), (0.02, 1), (1.0, 0.7), (D, 0)])
    out += 0.6 * stereo(brass)
    # descending drone (gliss down an octave)
    f = 73.4 * 2 ** (-1.2 * smoothstep(t / D))
    dr = additive(f, n, [1, 0.5, 0.4, 0.25, 0.2, 0.12, 0.1, 0.06])
    dr2 = additive(f * 1.06, n, [1, 0.5, 0.4, 0.25, 0.2, 0.12])
    out += 0.3 * np.stack([dr, dr2]) * env_pts(n, [(0, 0), (0.3, 1), (D, 0.2)])[None, :]
    # impact
    hit = thump(D, 90.0, 30.0, 1.6, rng, drop=0.06, noise=0.7, noise_lp=600.0)
    gong = metal_hit(D, 70.0, rng, count=20, t60=3.0, bright=2500, noise=0.4)
    out += stereo(0.8 * hit + 0.35 * gong)
    ir = make_ir(rt60=3.5, predelay=0.02, seed=304, er_gain=0.4)
    out = reverb(out, ir, wet=0.4, out_len=n)
    return out


@sound('wake_up', 2, -8.0)
def wake_up():
    D = 5.0
    n = ns(D)
    rng = R(3004)
    t = tvec(n)
    # tinnitus: high sine with slow beating, swelling then fading as senses clear
    tin = sine(6800.0 + 30 * contour(n, 0.5, rng), n) + 0.5 * sine(6812.0, n)
    tin_e = env_pts(n, [(0, 0), (1.8, 1), (3.2, 0.6), (D, 0)])
    out = 0.12 * stereo(tin * tin_e)
    # low swelling rumble
    rum = lp(rng.standard_normal((2, n)), 120.0, 2)
    rum /= np.max(np.abs(rum))
    out += 0.4 * rum * env_pts(n, [(0, 0), (1.5, 1), (D, 0)])[None, :]
    # heartbeat that starts muffled and clears (lowpass opens, reverb shrinks)
    hb = np.zeros(n)
    for k, t0 in enumerate(np.arange(0.3, D - 0.5, 0.92 - 0.0)):
        lub = thump(0.35, 80.0, 40.0, 0.18, rng, drop=0.03, noise=0.35, noise_lp=500.0)
        dub = thump(0.3, 100.0, 50.0, 0.12, rng, drop=0.02, noise=0.3, noise_lp=600.0)
        add(hb, lub, ns(t0), 1.0)
        add(hb, dub, ns(t0 + 0.26), 0.65)
    fc = 150.0 + 1600.0 * smoothstep(t / D) ** 2
    hb = tv_filter(hb, fc, q=0.7, kind='lp', block=128, stages=2)
    out += 0.9 * stereo(hb)
    # filter the whole tinnitus/rumble from "underwater" to clear
    ir = make_ir(rt60=1.5, predelay=0.01, seed=305, dark_hz=2000.0)
    out = reverb(out, ir, wet=0.3, out_len=n)
    return out


@sound('power_surge', 2, -2.0)
def power_surge():
    D = 4.0
    n = ns(D)
    rng = R(3005)
    t = tvec(n)
    bang_t = 2.6
    out = np.zeros(n)
    # rising buzzing hum: 50 Hz harmonic stack whose frequency/brightness rise
    f = 50.0 * (1 + 1.2 * smoothstep(np.clip(t / bang_t, 0, 1)) ** 2)
    amps = [1.0 / (k + 1) ** 0.7 for k in range(40)]
    b = additive(f, n, amps)
    b = sat(b * (0.5 + 2.0 * np.clip(t / bang_t, 0, 1)), 2.5)
    out += 0.5 * b * env_pts(n, [(0, 0), (0.4, 0.4), (bang_t, 1.0), (bang_t + 0.02, 0)])
    # crackles getting denser
    tm = poisson_times(lambda x: 4 + 90 * (x / bang_t) ** 2 if x < bang_t else 0, bang_t, rng)
    cr = impulses(n, tm, rng.pareto(1.6, len(tm)) * rng.choice([-1, 1], len(tm)))
    cr = hp(cr, 1500.0) + 0.5 * resonators(cr, [3200, 5100], [0.01, 0.006], [1, 0.6])
    out += 0.25 * cr / (np.max(np.abs(cr)) + 1e-9)
    # the bang
    m = n - ns(bang_t)
    bang = thump(D - bang_t, 160.0, 35.0, 0.9, rng, drop=0.04, noise=1.2, noise_lp=6000.0)
    snap = hp(rng.standard_normal(m), 2000.0) * env_perc(m, 0.0002, 0.08)
    bang = sat(bang + 0.8 * snap, 3.0)
    # fizzle: decaying sparse sparks + sizzle
    tm2 = poisson_times(lambda x: 120 * np.exp(-x / 0.4), D - bang_t, rng)
    fz = impulses(m, tm2, rng.uniform(0.2, 1, len(tm2)) * rng.choice([-1, 1], len(tm2)))
    fz = hp(fz, 2500.0) + 0.3 * hp(rng.standard_normal(m), 5000) * env_perc(m, 0.01, 0.9)
    add(out, bang + 0.4 * fz, ns(bang_t), 1.0)
    ir = make_ir(rt60=2.5, predelay=0.02, seed=306, er_gain=0.5)
    return reverb(out, ir, wet=0.35, out_len=n)


@sound('power_down', 2, -6.0)
def power_down():
    D = 3.0
    n = ns(D)
    rng = R(3006)
    t = tvec(n)
    sp = np.exp(-t / 0.9)              # rotor speed
    f = 60.0 * sp + 3.0
    amps = [1.0, 0.6, 0.45, 0.3, 0.3, 0.2, 0.15, 0.1, 0.08, 0.06]
    motor = additive(f * 2, n, amps) * sp ** 0.5
    whine = additive(f * 23, n, [1.0, 0.3]) * sp
    blades = (0.5 + 0.5 * np.sin(2 * np.pi * cycles(f * 0.5, n))) ** 4
    air = bp(rng.standard_normal(n), 300.0, 3000.0) * blades * sp
    clunk = thump(0.4, 120.0, 50.0, 0.15, rng, noise=0.5)
    y = 0.5 * motor + 0.15 * whine + 0.2 * air
    add(y, clunk, ns(0.02), 0.5)
    # lights clicking off + hum dying
    h = hum(n, D, rng) * np.exp(-t / 0.5)
    y += 0.15 * h
    y = y * env_pts(n, [(0, 0), (0.01, 1), (D, 0)]) ** 0.3
    ir = make_ir(rt60=2.2, predelay=0.02, seed=307, er_gain=0.4)
    return reverb(y, ir, wet=0.35, out_len=n)


@sound('ui_click', 2, -6.0)
def ui_click():
    rng = R(3101)
    n = ns(0.12)
    y = click(0.12, rng, 2500, 10000, t60=0.012, modes=5, mf=(2800, 6500))
    y += 0.35 * modal(n, [1250.0, 1890.0], [0.02, 0.015], [1, 0.5])
    return stereo(y)


@sound('ui_back', 2, -6.0)
def ui_back():
    rng = R(3102)
    n = ns(0.15)
    y = click(0.15, rng, 1200, 6000, t60=0.015, modes=4, mf=(1400, 3200))
    y += 0.5 * modal(n, [640.0, 1010.0], [0.03, 0.02], [1, 0.5])
    return stereo(y)


@sound('ui_achievement', 2, -6.0)
def ui_achievement():
    rng = R(3103)
    D = 1.5
    n = ns(D)
    out = np.zeros((2, n))
    # two bell strikes, rising fifth then octave; eerie because of slight detune
    for t0, m, p, a in ((0.0, 76, -0.3, 0.8), (0.12, 83, 0.3, 0.7), (0.24, 88, 0.0, 0.55)):
        b = bell(D, mtof(m - 12), rng, t60=1.6, bright=0.8)
        add(out, pan(b, p), ns(t0), a)
    sh = hp(rng.standard_normal((2, n)), 6000.0) * env_pts(n, [(0, 0), (0.25, 1), (D, 0)])[None, :] * 0.03
    out += sh
    ir = make_ir(rt60=1.8, predelay=0.02, seed=310, er_gain=0.3)
    return reverb(out, ir, wet=0.35, out_len=n)


@sound('ui_note', 2, -6.0)
def ui_note():
    rng = R(3104)
    D = 0.8
    n = ns(D)
    t = tvec(n)
    # paper: crinkle impulses + broadband rustle with fast amplitude modulation
    rate = 30 + 400 * env_pts(n, [(0, 0), (0.08, 1), (0.35, 0.6), (0.55, 0.9), (D, 0)])
    cr = stick_slip(n, rate, rng, jitter=0.6, amp_jit=0.8)
    cr = bp(cr, 1500.0, 9000.0)
    rus = bp(rng.standard_normal(n), 800.0, 7000.0) * (0.5 + 0.5 * contour(n, 30, rng)) ** 2
    e = env_pts(n, [(0, 0), (0.03, 1), (0.3, 0.5), (0.5, 0.8), (D, 0)])
    y = (0.6 * cr / (np.max(np.abs(cr)) + 1e-9) + 0.5 * rus / (np.max(np.abs(rus)) + 1e-9)) * e
    return reverb(y, make_ir(rt60=0.35, seed=311), wet=0.12, out_len=n)


@sound('ui_error', 2, -6.0)
def ui_error():
    rng = R(3105)
    D = 0.3
    n = ns(D)
    f = np.full(n, 180.0)
    y = pulse(f, n, 0.35) + 0.7 * pulse(f * 1.06, n, 0.35)
    y = lp(y, 2500.0)
    gate = env_pts(n, [(0, 0), (0.005, 1), (0.11, 1), (0.12, 0.0), (0.15, 0), (0.155, 1), (0.27, 1), (D, 0)])
    y = sat(y * gate, 1.5)
    y += 0.3 * click(D, rng, 1500, 6000, t60=0.01, modes=3, mf=(1500, 3000))
    return stereo(y)


@sound('door_knock', 2, -2.0)
def door_knock():
    rng = R(3201)
    D = 3.5
    n = ns(D)
    out = np.zeros(n)
    for k, t0 in enumerate((0.15, 1.05, 1.95)):
        L = 1.4
        m = ns(L)
        # fist on a huge door: wood panel modes + metal banding + low boom
        boom = thump(L, 95.0, 48.0, 0.55, rng, drop=0.02, noise=0.6, noise_lp=700.0)
        exc = lp(rng.standard_normal(m), 2500.0) * env_perc(m, 0.0008, 0.012)
        wood = resonators(exc, np.array([92, 141, 210, 288, 375, 520, 690]) * rng.uniform(0.98, 1.02),
                          [0.25, 0.2, 0.15, 0.12, 0.1, 0.08, 0.06], [1, 0.8, 0.7, 0.5, 0.4, 0.3, 0.2])
        wood /= np.max(np.abs(wood)) + 1e-9
        metal = metal_hit(L, 210.0, rng, count=14, t60=0.9, bright=3000, noise=0.2)
        y = boom + 0.7 * wood + 0.18 * metal
        y = sat(y, 1.8)
        add(out, y, ns(t0), 1.0 - 0.05 * k)
    ir = make_ir(rt60=3.8, predelay=0.03, seed=320, er_gain=0.6, hf=0.4)
    return reverb(out, ir, wet=0.45, out_len=n)


def thunder(D, seed, crack_t=0.05, crack=1.0, roll_len=5.0, low=1.0, rumbles=6):
    """Thunder: sharp near-field crack (broadband impulses with high-passed
    noise), then a long rolling rumble built from many delayed, low-passed
    'arrivals' (each a burst of brown noise) - the way a long lightning
    channel produces sound arriving from different distances."""
    rng = R(seed)
    n = ns(D)
    t = tvec(n)
    out = np.zeros((2, n))
    # crack
    m = ns(0.6)
    tm = crack_t + np.cumsum(rng.exponential(0.006, 40))
    cr = impulses(n, tm, rng.uniform(0.3, 1.0, 40) * rng.choice([-1, 1], 40))
    cr = hp(cr, 300.0) + 0.6 * hp(rng.standard_normal(n), 1500.0) * env_perc(n, 0.001, 0.25) * (t > crack_t)
    cr = sat(cr * 2, 2.0)
    out += crack * 0.5 * stereo(cr)
    # rolling arrivals
    for k in range(rumbles * 6):
        t0 = crack_t + 0.05 + rng.gamma(1.6, roll_len / 5.0)
        if t0 > D - 0.5:
            continue
        L = rng.uniform(0.4, 1.8)
        mm = ns(L)
        b = pnoise(mm, rng, slope=-6.0, lo=25.0, hi=rng.uniform(150, 600))
        b *= np.sin(np.pi * np.clip(tvec(mm) / L, 0, 1)) ** rng.uniform(0.5, 2)
        b *= np.exp(-tvec(mm) / (L * 0.5))
        dist = 1.0 / (1 + (t0 - crack_t) * 0.35)
        add(out, pan(b, rng.uniform(-0.8, 0.8)), ns(t0), low * dist * rng.uniform(0.4, 1.0))
    # deep sub swell
    sub = lp(rng.standard_normal(n), 60.0, 4)
    sub /= np.max(np.abs(sub)) + 1e-9
    sub *= env_pts(n, [(0, 0), (crack_t + 0.1, 1), (D * 0.6, 0.5), (D, 0)])
    out += 0.6 * low * stereo(sub)
    ir = make_ir(rt60=3.0, predelay=0.03, seed=seed + 1, er_gain=0.2, hf=0.5)
    out = reverb(out, ir, wet=0.45, out_len=n)
    return out * env_pts(n, [(0, 1), (D * 0.7, 1), (D, 0)])[None, :]


@sound('thunder_1', 2, -2.0)
def thunder_1():
    return thunder(7.0, 3301, crack_t=0.03, crack=1.0, roll_len=4.0)


@sound('thunder_2', 2, -2.0)
def thunder_2():
    # more distant: weaker crack, longer roll
    return thunder(9.0, 3311, crack_t=0.25, crack=0.35, roll_len=6.5, low=1.2, rumbles=8)


@sound('thunder_3', 2, -2.0)
def thunder_3():
    # double-strike
    a = thunder(6.5, 3321, crack_t=0.02, crack=1.0, roll_len=3.0)
    b = thunder(6.5, 3331, crack_t=0.02, crack=0.7, roll_len=3.5)
    out = np.zeros((2, ns(8.0)))
    add(out, a, 0, 1.0)
    add(out, b, ns(1.2), 0.8)
    return out


@sound('lightning_crack', 2, -2.0)
def lightning_crack():
    rng = R(3401)
    D = 1.5
    n = ns(D)
    t = tvec(n)
    # extremely sharp snap: dense cluster of impulses + sizzling HF tail
    tm = 0.01 + np.cumsum(rng.exponential(0.0025, 70))
    cr = impulses(n, tm, rng.uniform(0.2, 1.0, 70) * rng.choice([-1, 1], 70) * np.exp(-np.arange(70) / 25))
    cr = hp(cr, 500.0)
    hiss = hp(rng.standard_normal(n), 2500.0) * env_perc(n, 0.0005, 0.35)
    boom = thump(D, 140.0, 40.0, 0.7, rng, drop=0.03, noise=0.8, noise_lp=500.0)
    y = sat(2.0 * cr / (np.max(np.abs(cr)) + 1e-9) + 0.6 * hiss + 0.8 * boom, 2.2)
    ir = make_ir(rt60=1.6, predelay=0.005, seed=3402, er_gain=0.6)
    return reverb(y, ir, wet=0.4, out_len=n)


def babble(D, rng, f0_curve, shift_curve=None, syl_rate=5.0):
    """Unintelligible speech-like babble: glottal source with intonation,
    random vowel sequence + syllabic amplitude + fricatives."""
    n = ns(D)
    src = glottal(f0_curve, n, rng, jitter=0.015, shimmer=0.1, tilt=1200.0, breath=0.08)
    vow = ['a', 'e', 'i', 'o', 'u', 'ae', 'er']
    segs, tt = [], 0.0
    while tt < D:
        segs.append((tt, str(rng.choice(vow))))
        tt += rng.uniform(0.6, 1.5) / syl_rate
    W = vowel_track(n, segs, smooth=0.04)
    sh = 1.0 if shift_curve is None else shift_curve
    if np.ndim(sh) == 0:
        y = vowel_morph(src, W, shift=sh)
    else:
        # block-wise shift (pitch-dropping formants)
        y = np.zeros(n)
        blk = ns(0.25)
        for s0 in range(0, n, blk):
            s1 = min(n, s0 + blk + ns(0.05))
            seg = vowel_morph(src[s0:s1], {k: v[s0:s1] for k, v in W.items()}, shift=float(np.mean(sh[s0:s1])))
            w = np.ones(s1 - s0)
            if s0 > 0:
                w[:ns(0.05)] = np.linspace(0, 1, ns(0.05))
            if s1 < n:
                w[-ns(0.05):] = np.linspace(1, 0, ns(0.05))
            y[s0:s1] += seg * w
    # syllables: amplitude contour with gaps (words/phrases)
    syl = 0.5 + 0.5 * np.sin(2 * np.pi * cycles(syl_rate * (1 + 0.3 * contour(n, 1.0, rng)), n))
    phrase = (contour(n, 0.7, rng) > -0.35).astype(float)
    phrase = np.convolve(phrase, np.hanning(ns(0.08)) / np.sum(np.hanning(ns(0.08))), 'same')
    fric_t = rng.uniform(0, D, int(D * 2.5))
    fric = np.zeros(n)
    for ft in fric_t:
        m = ns(rng.uniform(0.05, 0.12))
        add(fric, bp(rng.standard_normal(m), 3500, 8000) * np.hanning(m), ns(ft))
    y = y * (0.15 + 0.85 * syl ** 1.5) * phrase
    y = y / (np.max(np.abs(y)) + 1e-9) + 0.25 * fric * phrase
    return y


@sound('pa_announcement', 2, -3.0)
def pa_announcement():
    rng = R(3501)
    D = 20.0
    n = ns(D)
    t = tvec(n)
    out = np.zeros(n)

    def chime(f_hi, f_lo, slow=1.0):
        L = 2.6 * slow
        a = bell(L, f_hi * 0.5, rng, t60=1.8 * slow, bright=0.5)
        b = bell(L, f_lo * 0.5, rng, t60=1.8 * slow, bright=0.5)
        y = np.zeros(ns(L + 0.7 * slow))
        add(y, a, 0, 1.0)
        add(y, b, ns(0.65 * slow), 1.0)
        return y

    # ding-dong at the start
    add(out, chime(659.3, 523.3), ns(0.1), 0.8)
    # voice: pitch drops over time; formants drop too (broken tape)
    vs, ve = 3.4, 16.0
    m = ns(ve - vs)
    tv = tvec(m)
    prog = tv / (ve - vs)
    f0 = 135.0 * (1 + 0.18 * contour(m, 1.5, rng)) * (1 - 0.55 * prog ** 1.5)
    # occasional stutter/drag
    f0 *= 1 - 0.25 * (contour(m, 0.8, rng) > 0.7)
    shift = 1.05 - 0.45 * prog ** 1.3
    v = babble(ve - vs, rng, f0, shift, syl_rate=4.5)
    # broken announcer processing: distortion, bit-crush, phone band
    v = sat(v * 2.5, 3.0)
    v = crush(v, bits=6, div=3)
    v = bp(v, 300.0, 3800.0, 2)
    v = peq(v, 1700.0, 2.0, 8.0)
    # dropouts
    drop = (contour(m, 3.0, rng) > -0.75).astype(float)
    drop = np.convolve(drop, np.ones(ns(0.01)) / ns(0.01), 'same')
    v *= drop
    add(out, v, ns(vs), 0.55)
    # feedback squeals: narrow resonant tones swelling
    for t0, f, L in ((5.2, 2840.0, 1.2), (9.8, 3610.0, 1.6), (13.6, 2215.0, 2.0)):
        mm = ns(L)
        sq = sine(f * (1 + 0.004 * contour(mm, 3, rng)), mm) * np.sin(np.pi * tvec(mm) / L) ** 3
        add(out, sat(sq * 1.5, 2.0), ns(t0), 0.22)
    # static / hum of the PA line
    st = bp(rng.standard_normal(n), 800.0, 7000.0) * 0.05 * (0.6 + 0.4 * contour(n, 8, rng))
    tm = poisson_times(25.0, D, rng)
    crk = hp(impulses(n, tm, rng.pareto(1.8, len(tm)) * rng.choice([-1, 1], len(tm))), 1500) * 0.08
    hm = hum(n, D, rng, amps=(1.0, 0.7, 0.4, 0.3, 0.2), buzz=0.6) * 0.04
    pa_env = env_pts(n, [(0, 0), (0.05, 1), (D - 0.5, 1), (D, 0)])
    out += (st + crk + hm) * pa_env
    # slowed-down, detuned chime at the end
    sc = chime(659.3 * 0.71, 523.3 * 0.69, slow=1.6)
    add(out, sc, ns(15.9), 0.75)
    # PA horn speaker coloring (applied to whole signal), big hall
    out = bp(out, 250.0, 5500.0, 2)
    out = peq(out, 1200.0, 1.2, 4.0)
    out = sat(out * 1.2, 1.4)
    ir = make_ir(rt60=4.2, predelay=0.07, seed=3502, er_gain=0.5, hf=0.4)
    return reverb(out, ir, wet=0.5, dry=0.8, out_len=n)


def scream(D, rng, f0, f1, vowels, shift=1.0, fm_ratio=1.41, fm_idx=6.0, grit=1.0):
    """Mechanical scream: harsh FM source + glottal/noise, through vowel
    formants (shifted), saturated."""
    n = ns(D)
    t = tvec(n)
    f = f0 + (f1 - f0) * smoothstep(t / D)
    f = f * (1 + 0.03 * np.sin(2 * np.pi * 7.5 * t) + 0.02 * contour(n, 12, rng))
    idx = fm_idx * (0.6 + 0.4 * contour(n, 6, rng))
    src = fm(f, fm_ratio, idx, n) + 0.6 * glottal(f, n, rng, jitter=0.04, shimmer=0.3, tilt=2500.0, breath=0.4)
    src += 0.6 * grit * rng.standard_normal(n) * (0.6 + 0.4 * np.sin(2 * np.pi * cycles(f, n)))
    segs = [(i * D / len(vowels), v) for i, v in enumerate(vowels)]
    W = vowel_track(n, segs, smooth=0.12)
    y = vowel_morph(src, W, shift=shift, bwmul=1.4)
    y = y / (np.max(np.abs(y)) + 1e-9) + 0.25 * src / (np.max(np.abs(src)) + 1e-9)
    return y


def jumpscare(rng, voice, under_f0, servo_rng, grind=1.0):
    D = 2.2
    n = ns(D)
    t = tvec(n)
    out = np.zeros((2, n))
    e = env_pts(n, [(0, 0), (0.008, 1), (1.5, 0.9), (D, 0)])
    v = sat(voice * 1.6, 3.5) * e
    out += np.stack([v, mod_delay(v, np.full(n, ns(0.007)))])
    # metal crash at the hit
    crash = metal_hit(D, under_f0, rng, count=24, t60=1.2, bright=7000, noise=1.0)
    out += 0.55 * pan(crash, -0.2) + 0.55 * pan(metal_hit(D, under_f0 * 1.37, rng, count=20, t60=1.0,
                                                           bright=8000, noise=0.8), 0.2)
    # servo grind underneath
    gr = servo_whine(D, servo_rng, 220.0, 520.0, grit=1.0)
    gr = gr * (1 + 0.8 * np.sign(np.sin(2 * np.pi * 23 * t)))  # gear teeth chatter
    out += 0.35 * grind * stereo(gr) * e[None, :]
    # low body punch
    out += 0.9 * stereo(thump(D, 110.0, 38.0, 0.5, rng, drop=0.03, noise=0.8, noise_lp=1500.0))
    out = sat(out / np.max(np.abs(out)) * 2.2, 2.0)
    ir = make_ir(rt60=1.0, predelay=0.005, seed=3601, er_gain=0.7)
    out = reverb(out, ir, wet=0.2, out_len=n)
    # final dense soft-limit so the scare is as loud as possible at -0.5 dBFS
    return sat(out / np.max(np.abs(out)) * 1.8, 1.6)


def fnaf_voice(D, rng, f0, f1, shift=1.0, vowels=('a', 'ae', 'a', 'ae'), layers=(1.0, 1.47, 0.71), rasp=1.4):
    """v1.0.3: animatronic scream in the FNAF manner - a raspy, layered, dissonant shriek that holds its
    pitch (random drift only, no siren-like vibrato), pitched down at the end like a dying motor."""
    n = ns(D)
    t = tvec(n)
    base = f0 + (f1 - f0) * smoothstep(np.clip(t / D, 0, 1))
    base = base * (1 + 0.05 * contour(n, 3.0, rng) + 0.015 * contour(n, 25.0, rng))
    y = np.zeros(n)
    segs = [(i * D / len(vowels), v) for i, v in enumerate(vowels)]
    W = vowel_track(n, segs, smooth=0.15)
    for k, mul in enumerate(layers):
        f = base * mul * (1 + 0.01 * k)
        src = glottal(f, n, rng, jitter=0.12, shimmer=0.55, tilt=4000.0, breath=0.6)
        src = src / (np.max(np.abs(src)) + 1e-9) + 0.5 * saw(f, n)
        src += rasp * rng.standard_normal(n) * (0.5 + 0.5 * np.abs(np.sin(np.pi * cycles(f, n))))
        v = vowel_morph(src, W, shift=shift * (1 + 0.04 * k), bwmul=2.3)
        y += (1.0 if k == 0 else 0.55) * v / (np.max(np.abs(v)) + 1e-9)
    y = sat(y * 3.0, 6.0)
    y = 0.6 * y + 0.4 * crush(hardclip(y, 0.6), bits=7, div=2)
    # electrical crackle riding on the scream
    cr = hp(stick_slip(n, 250 + 700 * (0.5 + 0.5 * contour(n, 8, rng)), rng, jitter=0.8, amp_jit=1.0), 2500.0)
    y += 0.25 * cr / (np.max(np.abs(cr)) + 1e-9)
    # dying-motor pitch fall over the last 30 %
    sp = 1.0 - 0.45 * smoothstep(np.clip((t - D * 0.7) / (D * 0.3), 0, 1))
    y = varispeed(y, sp)
    return y


def fnaf_hit(y, rng, under_f0, D):
    """Instant full-level attack: metal crash + body punch under the voice, stereo, limited."""
    n = ns(D)
    e = env_pts(n, [(0, 0), (0.004, 1), (D * 0.72, 0.95), (D, 0)])
    v = y[:n] * e
    out = np.stack([v, mod_delay(v, np.full(n, ns(0.006)))])
    crash = metal_hit(D, under_f0, rng, count=24, t60=0.9, bright=8000, noise=1.0)
    out += 0.45 * pan(crash, -0.25) + 0.45 * pan(metal_hit(D, under_f0 * 1.41, rng, count=20, t60=0.8, bright=9000, noise=0.8), 0.25)
    out += 0.8 * stereo(thump(D, 120.0, 40.0, 0.4, rng, drop=0.025, noise=0.9, noise_lp=2000.0))
    out = sat(out / np.max(np.abs(out)) * 2.4, 2.2)
    ir = make_ir(rt60=0.8, predelay=0.004, seed=3601, er_gain=0.7)
    out = reverb(out, ir, wet=0.15, out_len=n)
    return sat(out / np.max(np.abs(out)) * 1.8, 1.6)


@sound('jumpscare_grin', 2, -0.5)
def jumpscare_grin():
    rng = R(3602)
    D = 2.0
    v = fnaf_voice(D, rng, 330.0, 290.0, shift=1.3, vowels=('ae', 'a', 'ae', 'a', 'i'), layers=(1.0, 1.47, 0.73))
    return fnaf_hit(v, rng, 310.0, D)


@sound('jumpscare_rattle', 2, -0.5)
def jumpscare_rattle():
    rng = R(3612)
    D = 2.0
    n = ns(D)
    v = fnaf_voice(D, rng, 430.0, 400.0, shift=1.5, vowels=('i', 'ae', 'i', 'e'), layers=(1.0, 1.33, 1.89), rasp=1.2)
    r = rattle(D, rng, rate=500.0, f_lo=1200.0, f_hi=5000.0, parts=10, t60=0.02)   # loose plastic shell chattering
    v[:n] += 0.5 * r / (np.max(np.abs(r)) + 1e-9)
    return fnaf_hit(v, rng, 450.0, D)


@sound('jumpscare_brutus', 2, -0.5)
def jumpscare_brutus():
    """Brutus grabs you (0 s), lifts you to his mouth roaring, and bites (1.85 s) - synced with the grab sequence."""
    rng = R(3622)
    D = 3.0
    n = ns(D)
    t = tvec(n)
    out = np.zeros(n)
    # grab: paws slam into you
    g = thump(0.6, 150.0, 45.0, 0.25, rng, drop=0.02, noise=0.9, noise_lp=1500.0) + 0.6 * metal_hit(0.6, 140.0, rng, count=16, t60=0.4, bright=4000, noise=0.7)
    add(out, g, 0, 1.0)
    # lifting: servo/hydraulic grind
    gr = servo_whine(1.8, R(3623), 160.0, 420.0, grit=1.2)
    add(out, 0.45 * gr / (np.max(np.abs(gr)) + 1e-9) * env_pts(len(gr), [(0, 0), (0.15, 1), (1.6, 1), (1.8, 0)]), ns(0.05))
    # roar while you are carried to the mouth
    v = fnaf_voice(1.65, rng, 120.0, 92.0, shift=0.66, vowels=('o', 'a', 'o', 'a'), layers=(1.0, 0.5, 1.41), rasp=1.3)
    sub = lp(glottal(55.0 * (1 + 0.05 * contour(ns(1.65), 6, rng)), ns(1.65), rng, jitter=0.08, shimmer=0.5, tilt=400.0), 600.0)
    v = v[:ns(1.65)] + 0.5 * sub / (np.max(np.abs(sub)) + 1e-9)
    add(out, v * env_pts(len(v), [(0, 0), (0.2, 0.55), (1.45, 1.0), (1.65, 0.9)]), ns(0.22))
    # bite at 1.85 s: steel jaw slams shut + crunch
    b0 = ns(1.85)
    add(out, 1.4 * metal_hit(1.1, 105.0, rng, count=26, t60=0.9, bright=7000, noise=1.0), b0)
    add(out, 1.2 * thump(1.0, 160.0, 38.0, 0.45, rng, drop=0.015, noise=1.0, noise_lp=3000.0), b0)
    cr = np.zeros(ns(0.35))
    for tc in np.sort(rng.uniform(0, 0.25, 40)):
        add(cr, bp(rng.standard_normal(ns(0.02)), 800, 6000) * env_perc(ns(0.02), 0.0002, 0.006), ns(tc), rng.uniform(0.3, 1.0))
    add(out, 0.9 * cr / (np.max(np.abs(cr)) + 1e-9), b0 + ns(0.01))
    st = hp(rng.standard_normal(n), 3000.0) * env_pts(n, [(0, 0), (1.86, 0), (1.9, 0.5), (3.0, 0)])   # signal lost
    out += 0.25 * st
    out = sat(out / np.max(np.abs(out)) * 2.0, 2.0)
    o2 = np.stack([out, mod_delay(out, np.full(n, ns(0.007)))])
    ir = make_ir(rt60=0.9, predelay=0.004, seed=3624, er_gain=0.7)
    o2 = reverb(o2, ir, wet=0.15, out_len=n)
    return sat(o2 / np.max(np.abs(o2)) * 1.7, 1.6)


@sound('mus_clock_chime', 2, -3.0)
def mus_clock_chime():
    rng = R(3701)
    D = 8.0
    n = ns(D)
    out = np.zeros(n)
    # mechanism whirr before the strikes
    w = rattle(0.5, rng, rate=120, f_lo=1500, f_hi=4000, parts=4, t60=0.02)
    add(out, w, ns(0.0), 0.15)
    for k, t0 in enumerate((0.45, 1.95, 3.45, 4.95)):
        b = bell(D - t0, 98.0 * (1 + rng.uniform(-0.001, 0.001)), rng, t60=4.5, bright=0.8)
        # hammer
        ham = click(0.05, rng, 1000, 5000, t60=0.01, modes=3, mf=(900, 2000))
        y = to_len(b, ns(D - t0))
        y[:len(ham)] += 0.3 * ham
        add(out, y, ns(t0), 1.0 - 0.04 * k)
        # tick-tock escapement under it
    for t0 in np.arange(0.0, D, 0.5):
        tk = click(0.04, rng, 1500, 7000, t60=0.008, modes=3, mf=(1800, 3500))
        add(out, tk, ns(t0), 0.04 if int(t0 * 2) % 2 else 0.03)
    ir = make_ir(rt60=3.0, predelay=0.02, seed=3702, er_gain=0.5)
    y = reverb(out, ir, wet=0.35, out_len=n)
    m = mono(y)
    return 0.7 * np.stack([m, m]) + 0.3 * y


# =============================================================================
#  3D ONE-SHOTS (mono)
# =============================================================================


def step_concrete(seed):
    """Sneaker on concrete: heel thump (rubber, low), then sole-slap and a
    small grit scuff; parameters randomised per variation."""
    rng = R(seed)
    D = 0.25
    n = ns(D)
    heel = thump(D, rng.uniform(140, 190), rng.uniform(60, 80), 0.05, rng, drop=0.01, noise=0.7, noise_lp=1800.0)
    toe_t = rng.uniform(0.045, 0.075)
    toe = bp(rng.standard_normal(n), 400, 3500) * env_perc(n, 0.001, 0.035)
    grit_rate = 400 * env_pts(n, [(0, 0), (toe_t, 1), (toe_t + 0.08, 0.2), (D, 0)])
    grit = hp(stick_slip(n, grit_rate + 1, rng, jitter=0.7, amp_jit=0.9), 2500.0)
    y = heel
    add(y, 0.5 * toe / (np.max(np.abs(toe)) + 1e-9), ns(toe_t), rng.uniform(0.6, 1.0))
    y += 0.25 * grit / (np.max(np.abs(grit)) + 1e-9) * rng.uniform(0.4, 1.0)
    if rng.uniform() < 0.35:   # occasional rubber squeak
        m = ns(0.05)
        sq = sine(rng.uniform(1800, 2600) * (1 + 0.1 * np.linspace(0, 1, m)), m) * np.hanning(m)
        add(y, sq, ns(toe_t + 0.02), 0.08)
    return room(y, rt60=0.5, wet=0.12, seed=seed + 7)


@sound('fs_player_concrete_1', 1, -10.0)
def fs_player_concrete_1():
    return step_concrete(4001)


@sound('fs_player_concrete_2', 1, -10.0)
def fs_player_concrete_2():
    return step_concrete(4002)


@sound('fs_player_concrete_3', 1, -10.0)
def fs_player_concrete_3():
    return step_concrete(4003)


@sound('fs_player_concrete_4', 1, -10.0)
def fs_player_concrete_4():
    return step_concrete(4004)


def step_metal(seed):
    rng = R(seed)
    D = 0.3
    n = ns(D)
    base = thump(D, 170.0, 70.0, 0.05, rng, noise=0.5, noise_lp=2000)
    grate = metal_hit(D, rng.uniform(380, 460), rng, count=16, t60=0.22, bright=6000, noise=0.5)
    rat = rattle(D, rng, rate=60, f_lo=1500, f_hi=4500, parts=4, t60=0.03)
    y = base + 0.55 * grate
    add(y, 0.3 * rat / (np.max(np.abs(rat)) + 1e-9), ns(0.01))
    return room(y, rt60=0.6, wet=0.15, seed=seed + 7)


@sound('fs_player_metal_1', 1, -10.0)
def fs_player_metal_1():
    return step_metal(4011)


@sound('fs_player_metal_2', 1, -10.0)
def fs_player_metal_2():
    return step_metal(4012)


def fs_grin(seed):
    rng = R(seed)
    D = 0.6
    n = ns(D)
    stomp = stomp_body(D, rng, rng.uniform(130, 160), 48.0, 0.25)
    clang = metal_hit(D, rng.uniform(170, 230), rng, count=16, t60=0.45, bright=5000, noise=0.6)
    y = sat(stomp + 0.6 * clang, 2.0)
    # joints rattle after the impact
    r = rattle(D, rng, rate=90, f_lo=1200, f_hi=5000, parts=6, t60=0.025)
    r *= env_pts(n, [(0, 0), (0.03, 1), (0.35, 0.3), (D, 0)])
    y += 0.35 * r / (np.max(np.abs(r)) + 1e-9)
    # servo whine tail
    curve = np.clip((tvec(n) - 0.08) / 0.45, 0, 1)
    sw = servo_whine(D, rng, rng.uniform(700, 850), rng.uniform(450, 550), grit=0.6, curve=curve)
    sw *= env_pts(n, [(0, 0), (0.08, 0), (0.15, 1), (0.5, 0.6), (D, 0)])
    y += 0.18 * sw
    return room(y, rt60=0.9, wet=0.15, seed=seed + 7)


@sound('fs_grin_1', 1, -1.0)
def fs_grin_1():
    return fs_grin(4101)


@sound('fs_grin_2', 1, -1.0)
def fs_grin_2():
    return fs_grin(4102)


@sound('fs_grin_3', 1, -1.0)
def fs_grin_3():
    return fs_grin(4103)


def fs_rattle(seed):
    rng = R(seed)
    D = 0.4
    n = ns(D)
    y = np.zeros(n)
    # claws: 3-4 quick clacks (hard keratin/metal tips on concrete)
    k = rng.integers(3, 5)
    for i in range(k):
        t0 = 0.005 + i * rng.uniform(0.025, 0.045)
        c = click(0.1, rng, 1500, 9000, t60=0.015, modes=4, mf=(1800, 5000))
        add(y, c, ns(t0), rng.uniform(0.5, 1.0))
    # scrape: stick-slip drag
    sr = 900 * env_pts(n, [(0, 0), (0.1, 1), (0.25, 0.5), (D, 0)])
    sc = stick_slip(n, sr + 1, rng, jitter=0.5, amp_jit=0.7)
    sc = resonators(sc, [2300, 3900, 5600], [0.01, 0.008, 0.006], [1, 0.7, 0.5]) + hp(sc, 3000)
    y += 0.35 * sc / (np.max(np.abs(sc)) + 1e-9)
    r = rattle(D, rng, rate=150, f_lo=1800, f_hi=6500, parts=8, t60=0.02)
    y += 0.4 * r / (np.max(np.abs(r)) + 1e-9) * env_perc(n, 0.01, 0.3)
    y += 0.4 * thump(D, 200.0, 90.0, 0.04, rng, noise=0.3)
    return room(y, rt60=0.7, wet=0.15, seed=seed + 7)


@sound('fs_rattle_1', 1, -2.0)
def fs_rattle_1():
    return fs_rattle(4201)


@sound('fs_rattle_2', 1, -2.0)
def fs_rattle_2():
    return fs_rattle(4202)


@sound('fs_rattle_3', 1, -2.0)
def fs_rattle_3():
    return fs_rattle(4203)


def chain_clank(D, rng, links=8):
    n = ns(D)
    y = np.zeros(n)
    t0 = 0.02
    for i in range(links):
        t0 += rng.exponential(0.035)
        if t0 > D - 0.1:
            break
        h = metal_hit(0.25, rng.uniform(1100, 2200), rng, count=6, t60=0.08, bright=8000, noise=0.4)
        add(y, h, ns(t0), rng.uniform(0.3, 1.0) * np.exp(-i / 5))
    return y


def fs_brutus(seed):
    rng = R(seed)
    D = 0.9
    n = ns(D)
    sub = thump(D, rng.uniform(80, 95), 30.0, 0.55, rng, drop=0.035, noise=0.9, noise_lp=700.0)
    body = metal_hit(D, rng.uniform(85, 110), rng, count=18, t60=0.8, bright=2500, noise=0.5)
    y = sat(1.3 * sub + 0.4 * body, 2.4)
    ch = chain_clank(D, rng, links=9)
    y += 0.3 * ch / (np.max(np.abs(ch)) + 1e-9)
    # floor debris shaking
    deb = rattle(D, rng, rate=60, f_lo=900, f_hi=4000, parts=6, t60=0.03) * env_perc(n, 0.02, 0.5)
    y += 0.12 * deb / (np.max(np.abs(deb)) + 1e-9)
    return room(y, rt60=1.1, wet=0.15, seed=seed + 7)


@sound('fs_brutus_1', 1, -0.5)
def fs_brutus_1():
    return fs_brutus(4301)


@sound('fs_brutus_2', 1, -0.5)
def fs_brutus_2():
    return fs_brutus(4302)


@sound('fs_brutus_3', 1, -0.5)
def fs_brutus_3():
    return fs_brutus(4303)


@sound('servo_1', 1, -3.0)
def servo_1():
    rng = R(4401)
    D = 0.8
    n = ns(D)
    t = tvec(n)
    curve = env_pts(n, [(0, 0), (0.15, 0.6), (0.5, 1.0), (0.7, 0.85), (D, 0.8)])
    y = servo_whine(D, rng, 520.0, 1250.0, grit=0.5, curve=curve)
    y *= env_pts(n, [(0, 0), (0.03, 1), (0.65, 1), (D, 0)])
    stop = click(0.08, rng, 1200, 6000, t60=0.02, modes=3, mf=(900, 2500))
    add(y, stop, ns(0.68), 0.6)
    return room(y, rt60=0.6, wet=0.12, seed=4402)


@sound('servo_2', 1, -3.0)
def servo_2():
    rng = R(4411)
    D = 0.8
    n = ns(D)
    # two-step jerky move, lower, grindier
    curve = env_pts(n, [(0, 0.2), (0.2, 0.8), (0.3, 0.3), (0.55, 1.0), (D, 0.6)])
    y = servo_whine(D, rng, 300.0, 780.0, grit=1.0, curve=curve)
    y *= env_pts(n, [(0, 0), (0.02, 1), (0.25, 1), (0.3, 0.3), (0.35, 1), (0.7, 1), (D, 0)])
    return room(y, rt60=0.6, wet=0.12, seed=4412)


def tape_warble(x, rng, depth=0.003, rate=5.0, drift=0.002):
    n = x.shape[-1]
    t = tvec(n)
    d = SR * (depth * (0.5 + 0.5 * np.sin(2 * np.pi * rate * t)) + drift * (0.5 + 0.5 * contour(n, 2.0, rng)))
    return mod_delay(x, d)


@sound('robot_voice_grin', 1, -3.0)
def robot_voice_grin():
    rng = R(4501)
    D = 3.0
    n = ns(D)
    t = tvec(n)
    # giggle: series of "hee/ha" syllables at falling pitch, glitch-repeated
    out = np.zeros(n)
    syl_t = 0.15
    k = 0
    while syl_t < 2.5:
        L = rng.uniform(0.11, 0.17)
        m = ns(L)
        f0 = (420 - 90 * syl_t / 2.5) * (1 + 0.15 * np.sin(np.pi * tvec(m) / L)) * rng.uniform(0.95, 1.05)
        src = glottal(f0, m, rng, jitter=0.02, shimmer=0.2, tilt=1800.0, breath=0.3)
        v = 'i' if k % 2 == 0 else 'ae'
        s = formant(src, v, shift=1.35) * np.sin(np.pi * tvec(m) / L) ** 0.8
        h = hp(rng.standard_normal(m), 2000.0) * env_perc(m, 0.002, 0.04) * 0.3 * np.std(s) * 4
        s = s + h
        add(out, s, ns(syl_t), 1.0)
        # glitch: occasionally repeat the syllable immediately (stutter)
        if rng.uniform() < 0.3:
            add(out, s[:m // 2], ns(syl_t + L * 0.5), 0.8)
            syl_t += L * 0.5
        syl_t += L + rng.uniform(0.02, 0.06)
        k += 1
    out = tape_warble(out, rng, depth=0.004, rate=4.0, drift=0.006)
    out = crush(out, bits=8, div=2)
    out = bp(out, 250.0, 6000.0)
    out = sat(out / (np.max(np.abs(out)) + 1e-9) * 1.8, 2.0)
    # mechanical servo flicks and speaker buzz
    sb = servo_whine(D, rng, 900.0, 1100.0, grit=0.4) * 0.06
    out += sb * env_pts(n, [(0, 0), (0.1, 1), (2.6, 1), (D, 0)])
    buzz = hum(n, D, rng, amps=(0.2, 0.4, 0.3, 0.3), buzz=1.0) * 0.05
    out += buzz * env_pts(n, [(0, 0), (0.05, 1), (D - 0.1, 1), (D, 0)])
    return room(out, rt60=0.8, wet=0.15, seed=4502)


@sound('robot_voice_rattle', 1, -3.0)
def robot_voice_rattle():
    rng = R(4511)
    D = 2.5
    n = ns(D)
    t = tvec(n)
    # insect-like chitter: bursts of very fast clicks with resonant body
    rate = 25 + 120 * (0.5 + 0.5 * contour(n, 3.0, rng)) ** 2
    gate = (contour(n, 2.5, rng) > -0.2).astype(float)
    gate = np.convolve(gate, np.hanning(ns(0.02)) / np.sum(np.hanning(ns(0.02))), 'same')
    clk = stick_slip(n, rate, rng, jitter=0.25, amp_jit=0.4) * gate
    body = resonators(clk, [1650, 2480, 3900, 5200], [0.02, 0.015, 0.01, 0.008], [1, 0.8, 0.6, 0.4])
    y = body / (np.max(np.abs(body)) + 1e-9)
    # vocal-ish tone under the chitter (FM, mandible-like)
    v = fm(310 * (1 + 0.2 * contour(n, 4, rng)), 3.5, 3.0, n) * gate * 0.25
    y += formant(v, 'i', shift=1.2) * 2
    # hiss (snake-like) at the end
    hs = bp(rng.standard_normal(n), 3000.0, 11000.0) * env_pts(n, [(0, 0), (1.6, 0), (1.9, 1), (D - 0.05, 0.5), (D, 0)])
    y += 0.25 * hs
    r = rattle(D, rng, rate=60, f_lo=1500, f_hi=6000, parts=5, t60=0.02)
    y += 0.2 * r / (np.max(np.abs(r)) + 1e-9)
    y = sat(y * 1.3, 1.6)
    return room(y, rt60=0.8, wet=0.15, seed=4512)


@sound('robot_voice_brutus', 1, -3.0)
def robot_voice_brutus():
    rng = R(4521)
    D = 3.5
    n = ns(D)
    t = tvec(n)
    # deep groaning metal moan: low voice in "o/u", plus metal groan resonance
    f0 = 62.0 * (1 + 0.12 * np.sin(np.pi * t / D)) * (1 - 0.15 * smoothstep(t / D)) * (1 + 0.02 * contour(n, 3, rng))
    src = glottal(f0, n, rng, jitter=0.03, shimmer=0.25, tilt=700.0, breath=0.15)
    W = vowel_track(n, [(0, 'u'), (0.8, 'o'), (2.0, 'a'), (2.8, 'o')], smooth=0.4)
    v = vowel_morph(src, W, shift=0.7, bwmul=1.2)
    v /= np.max(np.abs(v)) + 1e-9
    g = groan(D, rng, f0=48.0, f1=38.0, rough=0.5)
    g /= np.max(np.abs(g)) + 1e-9
    y = sat(1.2 * v + 0.6 * g, 2.0)
    y = tape_warble(y, rng, depth=0.002, rate=1.5, drift=0.004)
    y *= env_pts(n, [(0, 0), (0.25, 1), (D - 0.6, 0.9), (D, 0)])
    return room(y, rt60=1.2, wet=0.2, seed=4522)


def breath_cycle(dur_in, dur_out, rng, gain_in=0.7, gain_out=1.0, vowel_in='h', vowel_out='a',
                 shift=1.0, voiced=0.0, f0=120.0, shaky=0.0):
    """One breath: inhale (thinner, higher formants, narrow) then exhale
    (fuller), from turbulent noise through vocal-tract formants."""
    parts = []
    for L, g, v, ex in ((dur_in, gain_in, vowel_in, False), (dur_out, gain_out, vowel_out, True)):
        m = ns(L)
        src = rng.standard_normal(m)
        src = hp(src, 250.0 if ex else 500.0)
        y = formant(src, v, shift=shift * (1.0 if ex else 1.15), bwmul=2.2)
        if voiced > 0 and ex:
            gl = glottal(f0 * (1 - 0.1 * np.linspace(0, 1, m)), m, rng, jitter=0.03, shimmer=0.2, tilt=600.0)
            y += voiced * formant(gl, v, shift=shift) * 0.05
        y += 0.3 * hp(src, 3000.0) * (0.3 if ex else 0.6)
        tt = tvec(m)
        if ex:
            e = np.minimum(1, tt / (L * 0.12)) * np.exp(-tt / (L * 0.55)) * (1 - tt / L) ** 0.5
        else:
            e = np.sin(np.pi * np.clip(tt / L, 0, 1)) ** 1.2
        if shaky > 0:
            e *= 1 + shaky * np.sin(2 * np.pi * cycles(rng.uniform(9, 13), m)) * (0.5 + 0.5 * contour(m, 5, rng))
        y = y / (np.max(np.abs(y)) + 1e-9) * e * g
        parts.append(y)
    return parts


@sound('breath_heavy', 1, -6.0)
def breath_heavy():
    rng = R(4601)
    D = 5.0
    n = ns(D)
    out = np.zeros(n)
    t0 = 0.05
    while t0 < D - 0.5:
        din = rng.uniform(0.28, 0.38)
        dout = rng.uniform(0.35, 0.45)
        inh, exh = breath_cycle(din, dout, rng, gain_in=0.8, gain_out=1.0, vowel_in='h',
                                vowel_out=str(rng.choice(['a', 'ae', 'h'])), voiced=0.6, f0=rng.uniform(110, 135))
        add(out, inh, ns(t0), rng.uniform(0.85, 1.0))
        add(out, exh, ns(t0 + din + 0.02), rng.uniform(0.85, 1.0))
        t0 += din + dout + rng.uniform(0.04, 0.12)
    out = lp(out, 7000.0)
    return room(out, rt60=0.4, wet=0.08, seed=4602)


@sound('breath_hide', 1, -6.0)
def breath_hide():
    rng = R(4611)
    D = 4.0
    n = ns(D)
    out = np.zeros(n)
    t0 = 0.1
    while t0 < D - 0.8:
        din = rng.uniform(0.6, 0.9)
        dout = rng.uniform(0.8, 1.1)
        inh, exh = breath_cycle(din, dout, rng, gain_in=0.6, gain_out=0.7, vowel_in='u', vowel_out='h',
                                shift=1.1, shaky=0.55)
        add(out, inh, ns(t0), 1.0)
        add(out, exh, ns(t0 + din + 0.12), 1.0)
        t0 += din + dout + rng.uniform(0.2, 0.35)
    out = lp(out, 5000.0)
    return out


@sound('breath_wake', 1, -6.0)
def breath_wake():
    rng = R(4621)
    D = 1.5
    n = ns(D)
    out = np.zeros(n)
    # sharp gasp (fast voiced-ish inhale), short hold, shaky exhale
    m = ns(0.38)
    src = hp(rng.standard_normal(m), 400.0)
    W = vowel_track(m, [(0, 'h'), (0.12, 'a'), (0.25, 'ae')], smooth=0.05)
    g = vowel_morph(src, W, shift=1.15, bwmul=1.6)
    gl = glottal(np.linspace(220, 300, m), m, rng, jitter=0.03, tilt=1500, breath=0.6)
    g = g / (np.max(np.abs(g)) + 1e-9) + 0.12 * formant(gl, 'a', 1.1) / (np.max(np.abs(formant(gl, 'a', 1.1))) + 1e-9)
    e = env_pts(m, [(0, 0), (0.04, 1), (0.25, 0.9), (0.38, 0)])
    add(out, g * e, ns(0.02), 1.0)
    _, exh = breath_cycle(0.1, 0.8, rng, gain_out=0.45, vowel_out='h', shaky=0.4)
    add(out, exh, ns(0.6), 1.0)
    return room(out, rt60=0.4, wet=0.08, seed=4622)


@sound('flashlight_on', 1, -6.0)
def flashlight_on():
    rng = R(4701)
    D = 0.15
    n = ns(D)
    y = click(D, rng, 2000, 10000, t60=0.01, modes=5, mf=(2500, 7000))
    add(y, 0.6 * click(0.05, rng, 3000, 9000, t60=0.006, modes=3, mf=(3500, 6000)), ns(0.012))
    y += 0.2 * modal(n, [880.0, 1430.0], [0.03, 0.02], [1, 0.6])   # plastic body
    return y


@sound('flashlight_off', 1, -6.0)
def flashlight_off():
    rng = R(4711)
    D = 0.15
    n = ns(D)
    y = click(D, rng, 1500, 8000, t60=0.012, modes=5, mf=(1800, 5000))
    add(y, 0.4 * click(0.05, rng, 2000, 7000, t60=0.006, modes=3, mf=(2200, 4000)), ns(0.018))
    y += 0.25 * modal(n, [760.0, 1220.0], [0.03, 0.02], [1, 0.6])
    return y


@sound('door_open', 1, -4.0)
def door_open():
    rng = R(4801)
    D = 1.5
    n = ns(D)
    # latch click, then hinge creak (long, pitch-gliding), air movement
    y = np.zeros(n)
    add(y, click(0.1, rng, 1500, 7000, t60=0.02, modes=4, mf=(1200, 3500)), ns(0.02), 0.7)
    cr = creak(1.25, rng, f0=330.0, rate=(35, 110), q_mult=1.4, bright=4000.0)
    cr /= np.max(np.abs(cr)) + 1e-9
    add(y, cr, ns(0.15), 0.9)
    air = lp(rng.standard_normal(n), 400.0) * env_pts(n, [(0, 0), (0.4, 1), (1.3, 0.3), (D, 0)])
    y += 0.15 * air / (np.max(np.abs(air)) + 1e-9)
    return room(y, rt60=1.0, wet=0.18, seed=4802)


@sound('door_close', 1, -3.0)
def door_close():
    rng = R(4811)
    D = 1.0
    n = ns(D)
    y = thump(D, 120.0, 55.0, 0.25, rng, drop=0.02, noise=0.7, noise_lp=1200.0)
    exc = lp(rng.standard_normal(n), 3000.0) * env_perc(n, 0.0008, 0.01)
    wood = resonators(exc, [120, 185, 260, 410, 570], [0.15, 0.12, 0.1, 0.07, 0.05], [1, 0.8, 0.6, 0.4, 0.3])
    y += 0.6 * wood / (np.max(np.abs(wood)) + 1e-9)
    add(y, metal_hit(0.3, 1350.0, rng, count=6, t60=0.08, bright=8000, noise=0.5), ns(0.03), 0.4)
    add(y, click(0.1, rng, 2000, 8000, t60=0.02, modes=4, mf=(1500, 4000)), ns(0.12), 0.35)
    return room(sat(y, 1.4), rt60=1.0, wet=0.2, seed=4812)


@sound('door_locked', 1, -4.0)
def door_locked():
    rng = R(4821)
    D = 0.8
    n = ns(D)
    y = np.zeros(n)
    # handle jiggles: 4 rattling knocks of the latch against the strike plate
    for t0 in (0.02, 0.17, 0.33, 0.5):
        h = metal_hit(0.25, rng.uniform(900, 1300), rng, count=8, t60=0.07, bright=7000, noise=0.5)
        th = thump(0.25, 220.0, 120.0, 0.04, rng, noise=0.4)
        r = rattle(0.25, rng, rate=150, f_lo=1500, f_hi=5000, parts=4, t60=0.015)
        add(y, h + 0.6 * th + 0.3 * r / (np.max(np.abs(r)) + 1e-9), ns(t0 + rng.uniform(-0.01, 0.01)), rng.uniform(0.7, 1.0))
    return room(y, rt60=0.8, wet=0.15, seed=4822)


@sound('locker_open', 1, -4.0)
def locker_open():
    rng = R(4901)
    D = 1.0
    n = ns(D)
    y = np.zeros(n)
    add(y, metal_hit(0.3, 1500.0, rng, count=6, t60=0.06, bright=8000, noise=0.6), ns(0.01), 0.6)  # latch
    cr = creak(0.6, rng, f0=520.0, rate=(60, 160), q_mult=1.2, bright=5000.0)
    add(y, 0.5 * cr / (np.max(np.abs(cr)) + 1e-9), ns(0.08))
    # thin sheet-metal door wobble (low panel modes) as it swings
    pan_ = metal_hit(0.9, 140.0, rng, count=12, t60=0.6, bright=2000, noise=0.0)
    add(y, pan_ * env_pts(len(pan_), [(0, 0), (0.1, 1), (0.9, 0)]), ns(0.08), 0.35)
    return room(y, rt60=0.9, wet=0.18, seed=4902)


@sound('locker_close', 1, -2.0)
def locker_close():
    rng = R(4911)
    D = 0.8
    n = ns(D)
    sheet = metal_hit(D, 165.0, rng, count=22, t60=0.55, bright=5000, noise=0.8, spread=0.08)
    y = sheet + 0.6 * thump(D, 150.0, 60.0, 0.12, rng, noise=0.5)
    add(y, metal_hit(0.3, 1700.0, rng, count=6, t60=0.05, bright=9000, noise=0.5), ns(0.035), 0.5)
    r = rattle(D, rng, rate=80, f_lo=1500, f_hi=5000, parts=5, t60=0.02) * env_perc(n, 0.01, 0.25)
    y += 0.25 * r / (np.max(np.abs(r)) + 1e-9)
    return room(sat(y, 1.6), rt60=0.9, wet=0.2, seed=4912)


@sound('pickup', 1, -6.0)
def pickup():
    rng = R(5001)
    D = 0.3
    n = ns(D)
    # cloth/hand swipe + small object clink
    sw = bp(rng.standard_normal(n), 600, 5000) * env_pts(n, [(0, 0), (0.06, 1), (0.18, 0.2), (D, 0)])
    y = 0.5 * sw / (np.max(np.abs(sw)) + 1e-9)
    add(y, metal_hit(0.2, 2100.0, rng, count=6, t60=0.12, bright=9000, noise=0.3), ns(0.05), 0.4)
    add(y, thump(0.15, 260.0, 150.0, 0.03, rng, noise=0.3), ns(0.04), 0.3)
    return y


@sound('item_insert', 1, -5.0)
def item_insert():
    rng = R(5011)
    D = 0.5
    n = ns(D)
    y = np.zeros(n)
    # slide (scrape) then a solid seated clunk + click
    sc = stick_slip(n, 300 * env_pts(n, [(0, 0.2), (0.15, 1), (0.2, 0)]) + 1, rng, jitter=0.4, amp_jit=0.5)
    sc = resonators(sc, [1800, 2900, 4300], [0.01, 0.008, 0.006], [1, 0.6, 0.4])
    y += 0.3 * sc / (np.max(np.abs(sc)) + 1e-9) * env_pts(n, [(0, 0), (0.02, 1), (0.2, 1), (0.21, 0), (D, 0)])
    add(y, metal_hit(0.3, 520.0, rng, count=10, t60=0.12, bright=5000, noise=0.5), ns(0.21), 0.8)
    add(y, thump(0.25, 180.0, 80.0, 0.06, rng, noise=0.4), ns(0.21), 0.6)
    add(y, click(0.08, rng, 2000, 9000, t60=0.01), ns(0.3), 0.4)
    return room(y, rt60=0.6, wet=0.12, seed=5012)


@sound('lever_pull', 1, -3.0)
def lever_pull():
    rng = R(5101)
    D = 1.0
    n = ns(D)
    y = np.zeros(n)
    # rusty pivot creak during pull
    cr = creak(0.45, rng, f0=210.0, rate=(40, 90), q_mult=1.2)
    add(y, 0.5 * cr / (np.max(np.abs(cr)) + 1e-9), ns(0.02))
    # detent ratchet clicks
    for k in range(4):
        add(y, click(0.06, rng, 1500, 6000, t60=0.015, modes=3, mf=(1500, 3000)), ns(0.08 + k * 0.08), 0.3)
    # heavy clunk at the end of travel
    cl = thump(0.6, 140.0, 50.0, 0.25, rng, noise=0.7, noise_lp=1500.0) + \
        0.6 * metal_hit(0.6, 260.0, rng, count=14, t60=0.4, bright=4000, noise=0.5)
    add(y, sat(cl, 1.8), ns(0.42), 1.0)
    return room(y, rt60=1.2, wet=0.18, seed=5102)


@sound('generator_start', 1, -3.0)
def generator_start():
    rng = R(5201)
    D = 6.0
    n = ns(D)
    t = tvec(n)
    y = np.zeros(n)
    # starter cranking: compression cycles at ~4-6 Hz with starter whine
    crank_end = 2.4
    for k, t0 in enumerate(np.arange(0.1, crank_end, 0.19)):
        h = thump(0.25, 90.0, 55.0, 0.1, rng, drop=0.02, noise=0.6, noise_lp=1200.0)
        r = rattle(0.25, rng, rate=60, f_lo=900, f_hi=3000, parts=4, t60=0.02)
        add(y, h + 0.25 * r / (np.max(np.abs(r)) + 1e-9), ns(t0), 0.6 + 0.1 * (k % 2))
    sm = np.clip(t / crank_end, 0, 1)
    starter = servo_whine(D, rng, 300.0, 380.0, grit=0.8) * env_pts(n, [(0, 0), (0.08, 1), (crank_end, 1), (crank_end + 0.1, 0)])
    y += 0.12 * starter * (1 + 0.5 * np.sin(2 * np.pi * 5.2 * t))
    # engine catches: firing rate ramps up then settles at idle (~25 Hz)
    catch_t = crank_end
    fr = np.where(t < catch_t, 0.0, 25.0 + 20.0 * np.exp(-(t - catch_t) / 0.4) * (t >= catch_t))
    fr = fr * smoothstep((t - catch_t) / 0.15)
    ph = cycles(np.maximum(fr, 0.01), n)
    pulses = np.maximum(0, np.cos(2 * np.pi * ph)) ** 12
    eng = lp(pulses - pulses.mean(), 900.0)
    eng = resonators(eng, [85, 160, 240, 400], [0.05, 0.04, 0.03, 0.02], [1, 0.7, 0.5, 0.3]) + 0.5 * eng
    eng = sat(eng / (np.max(np.abs(eng)) + 1e-9) * 1.5, 2.0)
    hum_ = additive(np.maximum(fr, 0.01) * 2, n, [1, 0.5, 0.3, 0.2, 0.1]) * smoothstep((t - catch_t - 0.3) / 1.0)
    mech = bp(rng.standard_normal(n), 1500, 6000) * pulses * 0.2
    y += (0.8 * eng + 0.3 * hum_ + 0.3 * mech) * (t >= catch_t)
    # first cough
    add(y, sat(thump(0.4, 160.0, 60.0, 0.15, rng, noise=1.0, noise_lp=3000.0), 2.5), ns(catch_t), 0.9)
    return room(y, rt60=1.2, wet=0.18, seed=5202)


@sound('carousel_crank', 1, -4.0, loop=True)
def carousel_crank():
    rng = R(5301)
    T = 1.0
    n = ns(T)
    y = np.zeros(n)
    # ratchet pawl clicks, 8 per loop, alternating accent, with gear body
    for k in range(8):
        c = metal_hit(0.15, rng.uniform(1700, 2100), rng, count=6, t60=0.04, bright=9000, noise=0.6)
        add(y, c, ns(k * T / 8 + rng.uniform(-0.004, 0.004)), 1.0 if k % 2 == 0 else 0.7, wrap=True)
        b = thump(0.12, 300.0, 180.0, 0.03, rng, noise=0.3)
        add(y, b, ns(k * T / 8), 0.35, wrap=True)
    # gear grind continuous (periodic)
    gr = pnoise(n, rng, slope=-1.0, lo=500.0, hi=4000.0) * (0.6 + 0.4 * np.sin(2 * np.pi * 8 * tvec(n)) ** 2)
    y += 0.05 * gr
    ir = make_ir(rt60=0.7, predelay=0.005, seed=5302, er_gain=0.4)
    return reverb(y, ir, wet=0.15, is_periodic=True, mono_out=True)


def ice_hit(seed):
    rng = R(seed)
    D = 0.5
    n = ns(D)
    # hard brittle impact: very high inharmonic modes with fast decay + crack
    fr = rng.uniform(2200, 9000, 14)
    y = modal(n, fr, rng.uniform(0.02, 0.09, 14), rng.uniform(0.3, 1, 14))
    y /= np.max(np.abs(y)) + 1e-9
    y += 0.5 * thump(D, 220.0, 120.0, 0.05, rng, noise=0.6, noise_lp=3000.0)
    tm = 0.01 + np.cumsum(rng.exponential(0.006, 12))
    cr = hp(impulses(n, tm, rng.uniform(0.2, 1.0, 12)), 2500.0)
    y += 0.5 * cr / (np.max(np.abs(cr)) + 1e-9)
    return room(y, rt60=0.7, wet=0.15, seed=seed + 7)


@sound('ice_hit_1', 1, -3.0)
def ice_hit_1():
    return ice_hit(5401)


@sound('ice_hit_2', 1, -3.0)
def ice_hit_2():
    return ice_hit(5402)


def shatter(D, rng, n_shards=90, f_range=(2500, 11000), spread=0.7, chunk=0.6):
    """Brittle shatter: an initial break plus a cloud of tumbling shard
    impacts (each a tiny modal object), with density decaying over time."""
    n = ns(D)
    y = np.zeros(n)
    times = np.sort(rng.gamma(1.2, spread * D / 4.0, n_shards))
    for i, t0 in enumerate(times):
        if t0 > D - 0.05:
            continue
        m = ns(0.12)
        fr = rng.uniform(f_range[0], f_range[1], 4)
        s = modal(m, fr, rng.uniform(0.01, 0.06, 4), rng.uniform(0.3, 1, 4))
        add(y, s, ns(t0), rng.uniform(0.2, 1.0) * np.exp(-t0 / (D * 0.5)))
    y /= np.max(np.abs(y)) + 1e-9
    brk = hp(rng.standard_normal(n), 1500.0) * env_perc(n, 0.0005, 0.12)
    y += 0.8 * brk / (np.max(np.abs(brk)) + 1e-9)
    y += chunk * thump(D, 180.0, 70.0, 0.15, rng, noise=0.7, noise_lp=3000.0)
    return y


@sound('ice_break', 1, -2.0)
def ice_break():
    rng = R(5411)
    y = shatter(1.5, rng, n_shards=110, f_range=(1800, 9000), spread=0.6, chunk=0.9)
    return room(y, rt60=1.0, wet=0.18, seed=5412)


@sound('glass_break', 1, -2.0)
def glass_break():
    rng = R(5421)
    y = shatter(1.0, rng, n_shards=130, f_range=(3000, 14000), spread=0.5, chunk=0.4)
    # glassy ring of the pane at the break
    n = ns(1.0)
    ring = modal(n, rng.uniform(1800, 6000, 10), rng.uniform(0.05, 0.25, 10), rng.uniform(0.3, 1, 10))
    y += 0.35 * ring / (np.max(np.abs(ring)) + 1e-9)
    return room(y, rt60=0.9, wet=0.18, seed=5422)


@sound('gear_place', 1, -3.0)
def gear_place():
    rng = R(5501)
    D = 0.6
    n = ns(D)
    y = np.zeros(n)
    # gear seats on its axle: teeth mesh tick, then heavy clank
    for k in range(3):
        add(y, click(0.05, rng, 2000, 8000, t60=0.01, modes=3, mf=(2500, 5000)), ns(0.02 + k * 0.03), 0.35)
    g = metal_hit(0.5, 610.0, rng, count=16, t60=0.35, bright=6000, noise=0.5,
                  ratios=[1.0, 1.59, 2.14, 2.30, 2.65, 2.92, 3.50, 3.60, 4.06, 4.15, 4.6, 5.1, 5.5, 6.2, 6.9, 7.4])
    add(y, g + 0.5 * thump(0.5, 200.0, 90.0, 0.06, rng, noise=0.4), ns(0.12), 1.0)
    return room(y, rt60=0.8, wet=0.15, seed=5502)


def beep(D, f, rng, duty=0.5, gate=None, detune=0.0):
    n = ns(D)
    y = pulse(np.full(n, f), n, duty) * 0.6 + 0.4 * sine(f, n)
    if detune:
        y += 0.5 * pulse(np.full(n, f * (1 + detune)), n, duty)
    y = lp(y, 5000.0)
    y = peq(y, 2800.0, 1.5, 6.0)   # tiny piezo speaker resonance
    if gate is not None:
        y *= gate
    return y


@sound('keypad_beep', 1, -6.0)
def keypad_beep():
    rng = R(5601)
    D = 0.1
    n = ns(D)
    y = beep(D, 1975.5, rng, duty=0.5, gate=env_pts(n, [(0, 0), (0.003, 1), (0.075, 1), (0.085, 0), (D, 0)]))
    y += 0.4 * click(D, rng, 1500, 6000, t60=0.008, modes=3, mf=(1500, 3000))   # button press
    return y


@sound('keypad_error', 1, -6.0)
def keypad_error():
    rng = R(5611)
    D = 0.4
    n = ns(D)
    g = env_pts(n, [(0, 0), (0.004, 1), (0.15, 1), (0.16, 0), (0.2, 0), (0.205, 1), (0.36, 1), (0.37, 0), (D, 0)])
    return sat(beep(D, 330.0, rng, duty=0.3, gate=g, detune=0.035), 1.6)


@sound('keypad_ok', 1, -6.0)
def keypad_ok():
    rng = R(5621)
    D = 0.6
    n = ns(D)
    y = np.zeros(n)
    for k, f in enumerate((1318.5, 1760.0, 2637.0)):
        L = 0.12 if k < 2 else 0.25
        m = ns(L)
        g = env_pts(m, [(0, 0), (0.003, 1), (L - 0.015, 1), (L, 0)])
        add(y, beep(L, f, rng, duty=0.5, gate=g), ns(0.02 + k * 0.13))
    # lock solenoid clunk
    add(y, metal_hit(0.2, 700.0, rng, count=8, t60=0.06, bright=5000, noise=0.5), ns(0.45), 0.5)
    return y


@sound('safe_open', 1, -3.0)
def safe_open():
    rng = R(5701)
    D = 2.0
    n = ns(D)
    y = np.zeros(n)
    # handle spin ratchet
    for k in range(6):
        add(y, click(0.06, rng, 1500, 7000, t60=0.015, modes=4, mf=(1600, 3500)), ns(0.03 + k * 0.055), 0.4)
    # 3 heavy bolts retracting (sliding + clunk each)
    for k, t0 in enumerate((0.38, 0.55, 0.72)):
        m = ns(0.16)
        sl = stick_slip(m, 500.0, rng, jitter=0.4, amp_jit=0.5)
        sl = resonators(sl, [900, 1600, 2600], [0.02, 0.015, 0.01], [1, 0.6, 0.4])
        add(y, 0.3 * sl / (np.max(np.abs(sl)) + 1e-9) * np.hanning(m), ns(t0))
        cl = metal_hit(0.4, 330.0 + 30 * k, rng, count=12, t60=0.25, bright=4000, noise=0.5)
        cl += 0.8 * thump(0.4, 150.0, 60.0, 0.1, rng, noise=0.5)
        add(y, cl, ns(t0 + 0.15), 0.9)
    # heavy door swings: low creak
    cr = creak(1.0, rng, f0=140.0, rate=(20, 45), q_mult=1.6)
    add(y, 0.6 * cr / (np.max(np.abs(cr)) + 1e-9), ns(0.95))
    return room(sat(y, 1.4), rt60=1.0, wet=0.18, seed=5702)


@sound('gate_alarm', 1, -3.0, loop=True)
def gate_alarm():
    rng = R(5801)
    T = 2.0
    n = ns(T)
    t = tvec(n)
    # electro-mechanical alarm bell: clapper striking at 20 Hz on a gong bell
    y = np.zeros(n)
    fr, ts, am = metal_modes(780.0, rng, count=12, t60=0.6, bright=7000, spread=0.03)
    exc = np.zeros(n)
    for k in range(40):
        exc[ns(k * T / 40) % n] += rng.uniform(0.8, 1.0)
    bellsig = periodic(resonators, exc, fr, ts, am, warm=2.0)
    bellsig /= np.max(np.abs(bellsig)) + 1e-9
    # klaxon: two-tone harsh square (band-limited), alternating every 0.5 s
    f = np.where((t % 1.0) < 0.5, qf(440.0, T), qf(370.0, T))
    sq = pulse(f, n, 0.5)
    sq = periodic(lp, sq, 4000.0, warm=0.1)
    sq = periodic(peq, sq, 1500.0, 1.0, 6.0, warm=0.1)
    gate = circ_smooth(((t % 0.5) < 0.42).astype(float), 0.004)
    sq *= gate
    y = 0.6 * bellsig + 0.5 * sq
    y = psat(y * 1.4, 2.2)
    ir = make_ir(rt60=1.6, predelay=0.02, seed=5802, er_gain=0.5)
    return reverb(y, ir, wet=0.25, is_periodic=True, mono_out=True)


@sound('gate_open', 1, -3.0)
def gate_open():
    rng = R(5901)
    D = 6.0
    n = ns(D)
    t = tvec(n)
    speed = env_pts(n, [(0, 0), (0.6, 1), (4.8, 1), (5.3, 0), (D, 0)])
    # rolling shutter: slat-by-slat clatter at a rate following speed
    clat = stick_slip(n, 2 + 22 * speed, rng, jitter=0.15, amp_jit=0.4) * speed
    clat = resonators(clat, [220, 340, 510, 760, 1100, 1650], [0.15, 0.12, 0.1, 0.08, 0.06, 0.04],
                      [1, 0.8, 0.6, 0.5, 0.35, 0.25])
    clat /= np.max(np.abs(clat)) + 1e-9
    # motor drone + chain drive
    motor = servo_whine(D, rng, 110.0, 120.0, grit=0.7) * speed
    chain = rattle(D, rng, rate=200, f_lo=1500, f_hi=5000, parts=8, t60=0.02) * speed
    rumble = lp(rng.standard_normal(n), 150.0, 4) * speed
    rumble /= np.max(np.abs(rumble)) + 1e-9
    y = 0.7 * clat + 0.25 * motor + 0.2 * chain / (np.max(np.abs(chain)) + 1e-9) + 0.5 * rumble
    # start lurch + end slam
    add(y, metal_hit(1.5, 120.0, rng, count=18, t60=1.0, bright=3000, noise=0.5), ns(0.05), 0.6)
    slam = thump(1.0, 130.0, 45.0, 0.5, rng, noise=0.8, noise_lp=1500.0) + metal_hit(1.0, 150.0, rng, count=20, t60=0.9, bright=4000, noise=0.6)
    add(y, sat(slam, 2.0), ns(5.15), 1.0)
    return room(y, rt60=2.2, wet=0.25, seed=5902)


@sound('card_swipe', 1, -6.0)
def card_swipe():
    rng = R(6001)
    D = 0.5
    n = ns(D)
    # plastic card sliding through the reader slot (friction noise) + reader blip
    sw = bp(rng.standard_normal(n), 1500.0, 8000.0)
    ss = stick_slip(n, 900.0, rng, jitter=0.5, amp_jit=0.6)
    sw = sw / (np.max(np.abs(sw)) + 1e-9) + 0.5 * resonators(ss, [2600, 4100], [0.006, 0.004], [1, 0.6])
    e = env_pts(n, [(0, 0), (0.03, 1), (0.22, 0.8), (0.26, 0), (D, 0)])
    y = 0.6 * sw * e
    m = ns(0.08)
    add(y, beep(0.08, 2349.0, rng, gate=env_pts(m, [(0, 0), (0.004, 1), (0.07, 1), (0.08, 0)])), ns(0.32), 0.4)
    return y


def spark(seed, D=0.6):
    """Electric arc: bursts of crackling impulses (HV discharge) through
    a few metallic resonances, with a buzzing 100 Hz-modulated sizzle."""
    rng = R(seed)
    n = ns(D)
    t = tvec(n)
    bursts = rng.uniform(0, D * 0.6, 3)
    y = np.zeros(n)
    for b in bursts:
        k = rng.integers(20, 60)
        tm = b + np.cumsum(rng.exponential(0.0015, k))
        cr = impulses(n, tm, rng.pareto(1.5, k) * rng.choice([-1, 1], k))
        y += cr
    y = hp(y, 1200.0) + 0.4 * resonators(y, [2900, 4700, 7300], [0.008, 0.006, 0.004], [1, 0.7, 0.5])
    y /= np.max(np.abs(y)) + 1e-9
    sz = hp(rng.standard_normal(n), 3000.0) * (0.5 + 0.5 * np.sin(2 * np.pi * 100 * t)) ** 2
    sz *= env_pts(n, [(0, 0), (0.01, 1), (D * 0.6, 0.5), (D, 0)])
    y += 0.25 * sz
    pop = thump(D, 300.0, 80.0, 0.04, rng, noise=1.0, noise_lp=6000.0)
    y += 0.4 * pop
    y = lp(y, 13000.0, 2)
    return room(sat(y * 1.2, 1.8), rt60=0.8, wet=0.15, seed=seed + 7)


@sound('spark_1', 1, -3.0)
def spark_1():
    return spark(6101)


@sound('spark_2', 1, -3.0)
def spark_2():
    return spark(6102)


@sound('water_splash', 1, -4.0)
def water_splash():
    rng = R(6201)
    D = 1.0
    n = ns(D)
    t = tvec(n)
    # impact + many bubbles (Minnaert resonances: rising-pitch damped sines)
    y = thump(D, 160.0, 70.0, 0.08, rng, noise=1.0, noise_lp=2500.0)
    spl = bp(rng.standard_normal(n), 500.0, 8000.0) * env_pts(n, [(0, 0), (0.005, 1), (0.15, 0.4), (0.5, 0.1), (D, 0)])
    y += 0.7 * spl / (np.max(np.abs(spl)) + 1e-9)
    times = np.sort(rng.gamma(1.5, 0.08, 70))
    for t0 in times:
        if t0 > D - 0.1:
            continue
        m = ns(0.06)
        f0 = rng.uniform(500, 3500)
        tt = tvec(m)
        b = np.sin(2 * np.pi * cycles(f0 * (1 + 2.0 * tt / 0.06), m)) * env_perc(m, 0.0005, rng.uniform(0.01, 0.04))
        add(y, b, ns(t0), rng.uniform(0.1, 0.35))
    # droplets falling back
    for t0 in rng.uniform(0.25, 0.85, 14):
        m = ns(0.05)
        b = np.sin(2 * np.pi * cycles(rng.uniform(1500, 4000) * (1 + 1.5 * tvec(m) / 0.05), m)) * env_perc(m, 0.0003, 0.015)
        add(y, b, ns(t0), 0.12)
    return room(y, rt60=0.8, wet=0.15, seed=6202)


@sound('oil_pour', 1, -5.0)
def oil_pour():
    rng = R(6301)
    D = 1.5
    n = ns(D)
    t = tvec(n)
    # viscous glugs: low-pitched bubble resonances (rising), irregular rhythm,
    # plus a thick, low-passed stream noise
    y = np.zeros(n)
    t0 = 0.08
    while t0 < D - 0.25:
        m = ns(0.14)
        f0 = rng.uniform(180, 320)
        tt = tvec(m)
        g = np.sin(2 * np.pi * cycles(f0 * (1 + 1.2 * (tt / 0.14) ** 1.5), m)) * env_perc(m, 0.008, 0.09)
        g = formant(g, 'o', shift=0.8) * 3 + 0.4 * g
        add(y, g, ns(t0), rng.uniform(0.5, 1.0))
        t0 += rng.uniform(0.11, 0.2)
    stream = lp(rng.standard_normal(n), 900.0, 2) * env_pts(n, [(0, 0), (0.1, 1), (1.3, 1), (D, 0)])
    y = y / (np.max(np.abs(y)) + 1e-9) + 0.12 * stream / (np.max(np.abs(stream)) + 1e-9)
    return room(y, rt60=0.5, wet=0.1, seed=6302)


@sound('oil_slip', 1, -2.0)
def oil_slip():
    rng = R(6311)
    D = 1.2
    n = ns(D)
    t = tvec(n)
    # robot slips: skid (stick-slip squeal on slick floor), then metal clatter
    sk_e = env_pts(n, [(0, 0), (0.03, 1), (0.35, 0.8), (0.4, 0), (D, 0)])
    sk = stick_slip(n, 650 - 300 * np.clip(t / 0.4, 0, 1), rng, jitter=0.1, amp_jit=0.3)
    sk = resonators(sk, [1300, 2600, 3900], [0.03, 0.02, 0.015], [1, 0.5, 0.3]) * sk_e
    y = 0.4 * sk / (np.max(np.abs(sk)) + 1e-9)
    # big fall / clatter
    add(y, thump(0.8, 120.0, 40.0, 0.35, rng, noise=0.9, noise_lp=1500.0), ns(0.38), 1.0)
    for k in range(7):
        t0 = 0.38 + rng.exponential(0.12)
        h = metal_hit(0.6, rng.uniform(250, 1200), rng, count=12, t60=rng.uniform(0.15, 0.4), bright=6000, noise=0.5)
        add(y, h, ns(t0), rng.uniform(0.3, 0.8))
    r = rattle(0.8, rng, rate=120, f_lo=1200, f_hi=5000, parts=8, t60=0.02) * env_perc(ns(0.8), 0.01, 0.6)
    add(y, 0.4 * r / (np.max(np.abs(r)) + 1e-9), ns(0.4))
    return room(sat(y, 1.5), rt60=1.0, wet=0.18, seed=6312)


@sound('windup_toy', 1, -6.0, loop=True)
def windup_toy():
    rng = R(6401)
    T = 5.0
    n = ns(T)
    y = np.zeros(n)
    # escapement ticking at 10 Hz (tick/tock alternating), clockwork whirr
    for k in range(50):
        c = click(0.04, rng, 2500, 9000, t60=0.006, modes=3, mf=(3000, 6000) if k % 2 else (2200, 4500))
        add(y, c, ns(k * 0.1 + rng.uniform(-0.002, 0.002)), 0.5 if k % 2 else 0.35, wrap=True)
    whirr = pnoise(n, rng, slope=0.0, lo=2000.0, hi=7000.0) * (0.5 + 0.5 * np.sin(2 * np.pi * qf(40.0, T) * tvec(n))) ** 4
    y += 0.03 * whirr
    # tinkly comb tune (A minor-ish), 10 notes over 5 s
    tune = [81, 84, 83, 81, 76, 77, 76, 72, 74, 76]
    for k, m in enumerate(tune):
        s = music_box_note(mtof(m), 1.6, rng, detune_c=rng.uniform(-18, 18), bright=1.2)
        add(y, s, ns(k * 0.5 + 0.02), 0.45, wrap=True)
    y = periodic(hp, y, 400.0, warm=0.5)
    y = periodic(lp, y, 9000.0, warm=0.5)
    # tiny tin body resonance
    y = y + 0.3 * periodic(resonators, y, [1250.0, 2100.0], [0.02, 0.015], [0.1, 0.06], warm=0.5)
    ir = make_ir(rt60=0.6, predelay=0.004, seed=6402, er_gain=0.4)
    return reverb(y, ir, wet=0.15, is_periodic=True, mono_out=True)


@sound('throw_whoosh', 1, -6.0)
def throw_whoosh():
    rng = R(6501)
    D = 0.4
    n = ns(D)
    t = tvec(n)
    e = np.sin(np.pi * np.clip(t / D, 0, 1)) ** 2.5
    fc = 400 + 2500 * e
    y = tv_filter(rng.standard_normal(n), fc, q=1.8, kind='bp', block=32)
    y += 0.3 * tv_filter(rng.standard_normal(n), fc * 2.2, q=4.0, kind='bp', block=32)
    return y * e


@sound('mask_on', 1, -6.0)
def mask_on():
    rng = R(6601)
    D = 0.7
    n = ns(D)
    # rubber stretch squeak + plastic tap + muffled breath inside the mask
    st = stick_slip(n, 180 + 200 * env_pts(n, [(0, 0), (0.2, 1), (0.3, 0)]), rng, jitter=0.2, amp_jit=0.3)
    st = resonators(st, [700, 1150, 2300], [0.03, 0.02, 0.01], [1, 0.6, 0.3]) * env_pts(n, [(0, 0), (0.02, 1), (0.25, 0.6), (0.3, 0), (D, 0)])
    y = 0.4 * st / (np.max(np.abs(st)) + 1e-9)
    tap = thump(0.15, 500.0, 260.0, 0.03, rng, noise=0.6, noise_lp=3000.0) + 0.3 * modal(ns(0.15), [1450, 2380], [0.03, 0.02], [1, 0.5])
    add(y, tap, ns(0.3), 0.8)
    _, ex = breath_cycle(0.1, 0.35, rng, gain_out=1.0, vowel_out='u', shift=0.85)
    ex = lp(ex, 1200.0)
    ex = resonators(ex, [420.0, 900.0], [0.03, 0.02], [0.08, 0.05]) + ex
    add(y, 0.5 * ex / (np.max(np.abs(ex)) + 1e-9), ns(0.33))
    return y


@sound('mask_off', 1, -6.0)
def mask_off():
    rng = R(6611)
    D = 0.5
    n = ns(D)
    st = stick_slip(n, 300 - 150 * env_pts(n, [(0, 0), (0.2, 1)]), rng, jitter=0.2, amp_jit=0.3)
    st = resonators(st, [650, 1300], [0.02, 0.015], [1, 0.5]) * env_pts(n, [(0, 0), (0.02, 1), (0.18, 0.4), (0.2, 0), (D, 0)])
    y = 0.4 * st / (np.max(np.abs(st)) + 1e-9)
    # suction release pop + fresh-air inhale
    add(y, thump(0.1, 380.0, 200.0, 0.02, rng, noise=0.5, noise_lp=2500.0), ns(0.17), 0.6)
    inh, _ = breath_cycle(0.25, 0.05, rng, gain_in=1.0, vowel_in='h', shift=1.1)
    add(y, 0.4 * inh / (np.max(np.abs(inh)) + 1e-9), ns(0.22))
    return y


@sound('mask_repel', 1, -2.0)
def mask_repel():
    rng = R(6621)
    D = 1.5
    n = ns(D)
    t = tvec(n)
    # deep pulse whoosh: sub sine drop + band-swept noise + distorted pulse wave
    sub = thump(D, 90.0, 32.0, 0.8, rng, drop=0.15, noise=0.0)
    e = env_pts(n, [(0, 0), (0.05, 1), (0.6, 0.5), (D, 0)])
    fc = 2500 * np.exp(-t / 0.35) + 150
    wh = tv_filter(rng.standard_normal(n), fc, q=1.5, kind='bp', block=64) * e
    wh /= np.max(np.abs(wh)) + 1e-9
    pw = fm(55.0 * (1 - 0.4 * np.clip(t / D, 0, 1)), 1.5, 3.0 * np.exp(-t / 0.4), n) * e
    y = 1.0 * sub + 0.5 * wh + 0.5 * pw
    y = sat(y * 1.8, 2.8)
    # phasey "wobble" ringing out
    y = y + 0.5 * mod_delay(y, SR * (0.003 + 0.002 * np.sin(2 * np.pi * 3 * t)))
    return room(y, rt60=1.6, wet=0.25, seed=6622)


@sound('fortune_laugh', 1, -3.0)
def fortune_laugh():
    rng = R(6701)
    D = 4.0
    n = ns(D)
    # slow, theatrical "ha... ha... ha-ha-haaa" from a worn animatronic
    out = np.zeros(n)
    plan = [(0.15, 0.32, 190), (0.65, 0.3, 175), (1.15, 0.3, 165), (1.6, 0.22, 170), (1.86, 0.22, 160),
            (2.12, 0.22, 150), (2.4, 1.1, 140)]
    for t0, L, f0 in plan:
        m = ns(L)
        tt = tvec(m)
        f = f0 * (1 + 0.15 * np.exp(-tt / 0.05)) * (1 - 0.18 * tt / L) * (1 + 0.012 * np.sin(2 * np.pi * 6 * tt))
        src = glottal(f, m, rng, jitter=0.02, shimmer=0.15, tilt=1100.0, breath=0.25)
        W = vowel_track(m, [(0, 'h'), (0.03, 'a'), (L * 0.7, 'o')], smooth=0.04)
        s = vowel_morph(src, W, shift=0.88)
        s *= env_pts(m, [(0, 0), (0.02, 1), (L * 0.6, 0.8), (L, 0)])
        add(out, s, ns(t0), 1.0)
    out = tape_warble(out, rng, depth=0.003, rate=1.2, drift=0.01)
    out = bp(out, 180.0, 4500.0)
    out = sat(out / (np.max(np.abs(out)) + 1e-9) * 1.6, 2.0)
    # jaw servo clacks and mechanism
    for t0, _, _ in plan:
        add(out, click(0.06, rng, 1200, 5000, t60=0.02, modes=3, mf=(900, 2000)), ns(t0 - 0.02), 0.25)
    sv = servo_whine(D, rng, 400.0, 460.0, grit=0.5) * 0.05
    out += sv * env_pts(n, [(0, 0), (0.1, 1), (3.5, 1), (D, 0)])
    return room(out, rt60=1.4, wet=0.22, seed=6702)


@sound('fortune_card', 1, -5.0)
def fortune_card():
    rng = R(6711)
    D = 2.0
    n = ns(D)
    t = tvec(n)
    # dot-matrix/thermal printer: motor whirr + stepping clicks, card slides out
    run = env_pts(n, [(0, 0), (0.05, 1), (1.45, 1), (1.5, 0), (D, 0)])
    motor = servo_whine(D, rng, 900.0, 950.0, grit=0.6) * run
    steps = stick_slip(n, 60.0, rng, jitter=0.03, amp_jit=0.1) * run
    steps = resonators(steps, [2100, 3300, 5200], [0.01, 0.008, 0.005], [1, 0.6, 0.4])
    sl = bp(rng.standard_normal(n), 2000.0, 8000.0) * env_pts(n, [(0, 0), (1.2, 0), (1.3, 1), (1.6, 0.6), (1.65, 0), (D, 0)])
    y = 0.25 * motor + 0.5 * steps / (np.max(np.abs(steps)) + 1e-9) + 0.25 * sl
    add(y, click(0.08, rng, 1200, 6000, t60=0.02), ns(1.65), 0.6)   # card drops into tray
    return room(y, rt60=0.6, wet=0.12, seed=6712)


@sound('ghost_scare', 1, -1.0)
def ghost_scare():
    rng = R(6801)
    D = 1.5
    n = ns(D)
    t = tvec(n)
    # cheap animatronic ghost prop: pneumatic hiss + shrieking siren-like wail
    v = scream(D, rng, 980.0, 720.0, ['i', 'e', 'a', 'o'], shift=1.5, fm_ratio=2.0, fm_idx=3.0, grit=0.6)
    v *= env_pts(n, [(0, 0), (0.01, 1), (1.0, 0.7), (D, 0)])
    pn = hp(rng.standard_normal(n), 2500.0) * env_perc(n, 0.002, 0.35)
    bang = thump(0.5, 200.0, 70.0, 0.1, rng, noise=0.7, noise_lp=3000.0)
    y = sat(v * 1.5, 2.4) + 0.5 * pn
    add(y, bang, 0, 0.8)
    add(y, rattle(0.5, rng, rate=150, f_lo=1500, f_hi=6000, parts=6, t60=0.02), ns(0.02), 0.2)
    return room(y, rt60=1.2, wet=0.2, seed=6802)


@sound('skeleton_drop', 1, -2.0)
def skeleton_drop():
    rng = R(6901)
    D = 1.0
    n = ns(D)
    y = np.zeros(n)
    # plastic/bone pieces: hollow, dry wood-like modes, many collisions
    times = np.sort(0.05 + rng.gamma(1.4, 0.07, 45))
    for t0 in times:
        if t0 > D - 0.12:
            continue
        m = ns(0.1)
        fr = rng.uniform(700, 3800, 4)
        s = modal(m, fr, rng.uniform(0.012, 0.04, 4), rng.uniform(0.3, 1.0, 4))
        add(y, s, ns(t0), rng.uniform(0.3, 1.0) * np.exp(-(t0 - 0.05) / 0.4))
    # chain/hook clank from the drop mechanism
    add(y, chain_clank(0.5, rng, links=5), ns(0.0), 0.35)
    add(y, thump(0.4, 180.0, 80.0, 0.08, rng, noise=0.5), ns(0.06), 0.6)
    return room(y, rt60=1.0, wet=0.18, seed=6902)


@sound('tape_insert', 1, -5.0)
def tape_insert():
    rng = R(7001)
    D = 0.6
    n = ns(D)
    y = np.zeros(n)
    add(y, click(0.1, rng, 1200, 7000, t60=0.02, modes=4, mf=(900, 2600)), ns(0.02), 0.6)      # into slot
    add(y, thump(0.2, 320.0, 180.0, 0.03, rng, noise=0.6, noise_lp=4000.0), ns(0.16), 0.7)    # door shut
    add(y, click(0.1, rng, 1500, 8000, t60=0.015, modes=4, mf=(1200, 3200)), ns(0.17), 0.8)
    m = ns(0.3)
    motor = servo_whine(0.3, rng, 1200.0, 1300.0, grit=0.3) * env_pts(m, [(0, 0), (0.03, 1), (0.25, 1), (0.3, 0)])
    add(y, motor, ns(0.27), 0.12)
    return room(y, rt60=0.5, wet=0.1, seed=7002)


def metal_creak(seed, D):
    rng = R(seed)
    g = groan(D, rng, f0=rng.uniform(55, 85), f1=rng.uniform(40, 60), rough=0.5)
    c = creak(D * 0.8, rng, f0=rng.uniform(160, 260), rate=(15, 45), q_mult=1.5, bright=1800.0)
    y = g / (np.max(np.abs(g)) + 1e-9)
    add(y, 0.5 * c / (np.max(np.abs(c)) + 1e-9), ns(D * 0.1))
    y = lp(y, 2500.0)   # distant
    ir = make_ir(rt60=4.0, predelay=0.05, seed=seed + 7, er_gain=0.2, hf=0.35)
    out = reverb(y, ir, wet=0.7, dry=0.5, out_len=ns(D), mono_out=True)
    return out * env_pts(ns(D), [(0, 1), (D * 0.8, 1), (D, 0)])


@sound('metal_creak_1', 1, -4.0)
def metal_creak_1():
    return metal_creak(7101, 2.5)


@sound('metal_creak_2', 1, -4.0)
def metal_creak_2():
    return metal_creak(7102, 3.0)


@sound('metal_creak_3', 1, -4.0)
def metal_creak_3():
    return metal_creak(7103, 2.2)


def distant_bang(seed):
    rng = R(seed)
    D = 2.0
    n = ns(D)
    y = metal_hit(D, rng.uniform(90, 140), rng, count=20, t60=1.4, bright=2500, noise=0.5)
    y += 0.9 * thump(D, 110.0, 45.0, 0.4, rng, noise=0.6, noise_lp=800.0)
    # second, softer bounce
    add(y, 0.35 * metal_hit(1.5, rng.uniform(150, 200), rng, count=14, t60=0.8, bright=2000, noise=0.3), ns(rng.uniform(0.18, 0.3)))
    y = lp(sat(y, 1.5), 1800.0)
    ir = make_ir(rt60=4.5, predelay=0.06, seed=seed + 7, er_gain=0.3, hf=0.35)
    out = reverb(y, ir, wet=0.9, dry=0.35, out_len=n, mono_out=True)
    return out * env_pts(n, [(0, 1), (1.5, 1), (D, 0)])


@sound('distant_bang_1', 1, -3.0)
def distant_bang_1():
    return distant_bang(7201)


@sound('distant_bang_2', 1, -3.0)
def distant_bang_2():
    return distant_bang(7202)


def whisper(seed, D=2.5):
    """Unintelligible whisper: unvoiced turbulence through moving vowel
    formants, fricative bursts (s/sh/f), syllabic envelope, close-mic
    intimacy (little reverb)."""
    rng = R(seed)
    n = ns(D)
    src = hp(rng.standard_normal(n), 250.0)
    vow = ['a', 'e', 'i', 'o', 'u', 'er', 'ae']
    segs, tt = [], 0.0
    while tt < D:
        segs.append((tt, str(rng.choice(vow))))
        tt += rng.uniform(0.09, 0.2)
    W = vowel_track(n, segs, smooth=0.035)
    y = vowel_morph(src, W, shift=rng.uniform(0.95, 1.15), bwmul=1.3)
    syl = (0.5 + 0.5 * np.sin(2 * np.pi * cycles(5.5 * (1 + 0.3 * contour(n, 1.5, rng)), n))) ** 1.5
    phrase = (contour(n, 1.2, rng) > -0.3).astype(float)
    phrase = np.convolve(phrase, np.hanning(ns(0.06)) / np.sum(np.hanning(ns(0.06))), 'same')
    y = y / (np.max(np.abs(y)) + 1e-9) * (0.1 + 0.9 * syl) * phrase
    for ft in rng.uniform(0.1, D - 0.2, int(D * 2)):
        m = ns(rng.uniform(0.08, 0.18))
        lo_ = rng.choice([2500.0, 4000.0, 1800.0])
        fz = bp(rng.standard_normal(m), lo_, min(lo_ * 2.5, 12000)) * np.hanning(m)
        add(y, fz, ns(ft), 0.3)
    y *= env_pts(n, [(0, 0), (0.1, 1), (D - 0.25, 1), (D, 0)])
    return room(y, rt60=0.6, wet=0.1, seed=seed + 7)


@sound('whisper_1', 1, -6.0)
def whisper_1():
    return whisper(7301)


@sound('whisper_2', 1, -6.0)
def whisper_2():
    return whisper(7302)


@sound('electric_buzz', 1, -6.0, loop=True)
def electric_buzz():
    rng = R(7401)
    T = 2.0
    n = ns(T)
    t = tvec(n)
    # neon transformer buzz: 100 Hz rectified harmonics + arcing flicker
    h = hum(n, T, rng, base=100.0, amps=(1.0, 0.7, 0.6, 0.45, 0.4, 0.3, 0.25, 0.2, 0.15, 0.12, 0.1), buzz=1.2)
    flick = 0.7 + 0.3 * lfo_noise(n, 30, rng)
    # sporadic arcing crackles (wrap-safe)
    tm = rng.uniform(0, T, 40)
    cr = np.zeros(n)
    np.add.at(cr, (tm * SR).astype(int) % n, rng.pareto(1.5, 40) * rng.choice([-1, 1], 40))
    cr = periodic(hp, cr, 2500.0, warm=0.05)
    y = h * flick + 0.15 * cr / (np.max(np.abs(cr)) + 1e-9)
    y = psat(y / (np.max(np.abs(y)) + 1e-9) * 1.3, 1.8)
    return y


@sound('stun_zap', 1, -2.0)
def stun_zap():
    rng = R(7501)
    D = 1.0
    n = ns(D)
    t = tvec(n)
    # short-circuit: FM-buzzing arc with frequency sweep + crackle + pop + fizz
    e = env_pts(n, [(0, 0), (0.005, 1), (0.45, 0.8), (0.55, 0.2), (D, 0)])
    f = 120.0 * (1 + 0.5 * np.exp(-t / 0.1)) * (1 + 0.1 * contour(n, 20, rng))
    arc = fm(f, 3.03, 6.0 + 4 * contour(n, 10, rng), n)
    arc = sat(arc * 1.5, 2.5) * e
    k = 220
    tm = np.sort(rng.uniform(0, 0.6, k))
    cr = hp(impulses(n, tm, rng.pareto(1.4, k) * rng.choice([-1, 1], k)), 1500.0)
    y = 0.5 * arc + 0.5 * cr / (np.max(np.abs(cr)) + 1e-9)
    y += 0.6 * thump(D, 260.0, 60.0, 0.12, rng, noise=1.0, noise_lp=5000.0)
    fz = hp(rng.standard_normal(n), 4000.0) * env_pts(n, [(0, 0), (0.5, 0.2), (0.6, 1), (D, 0)]) * 0.15
    y += fz
    return room(y, rt60=0.9, wet=0.15, seed=7502)


# =============================================================================
#  Main
# =============================================================================


# =============================================================================
#  v1.0.3: material-specific doors (wood / metal) + curtain
# =============================================================================

def _nz(x):
    return x / (np.max(np.abs(x)) + 1e-9)


def _wood_body(n, rng, f0=105.0, t60=0.18, amp_lp=3500.0, att=0.006):
    """Knock on a wooden panel: low noise burst through warm, fast-decaying plank modes."""
    exc = lp(rng.standard_normal(n), amp_lp) * env_perc(n, 0.0006, att)
    fr = f0 * np.array([1.0, 1.62, 2.31, 3.4, 4.9, 6.8]) * rng.uniform(0.96, 1.04, 6)
    return _nz(resonators(exc, fr, t60 * np.array([1.0, 0.85, 0.7, 0.5, 0.35, 0.25]), [1, 0.85, 0.6, 0.4, 0.25, 0.15]))


def _brass_latch(dur, rng, f0=2300.0):
    """Small brass latch / doorknob click."""
    return _nz(click(dur, rng, 1800, 7500, t60=0.03, modes=5, mf=(f0 * 0.8, f0 * 1.8)))


def _steel_panel(dur, rng, f0=95.0, t60=1.1, bright=2600.0, count=22):
    """Big sheet-steel door panel: long, low, inharmonic ring."""
    return metal_hit(dur, f0, rng, count=count, t60=t60, bright=bright, noise=0.35, spread=0.07)


@sound('door_open_wood', 1, -4.0)
def door_open_wood():
    rng = R(6101)
    D = 1.7
    n = ns(D)
    y = np.zeros(n)
    add(y, _brass_latch(0.12, rng), ns(0.015), 0.55)                               # knob turns
    add(y, 0.35 * _wood_body(ns(0.3), rng, f0=140.0, t60=0.08), ns(0.05))          # door leaves the frame
    cr = creak(1.3, rng, f0=210.0, rate=(18, 55), q_mult=1.7, bright=2600.0)       # slow, woody hinge groan
    add(y, 0.85 * _nz(cr), ns(0.16))
    air = lp(rng.standard_normal(n), 300.0) * env_pts(n, [(0, 0), (0.45, 1), (1.4, 0.25), (D, 0)])
    y += 0.12 * _nz(air)
    return room(y, rt60=1.0, wet=0.2, seed=6102)


@sound('door_close_wood', 1, -3.0)
def door_close_wood():
    rng = R(6111)
    D = 1.1
    n = ns(D)
    y = 0.9 * _wood_body(n, rng, f0=98.0, t60=0.22)                                # hollow wooden thud
    y += 0.6 * thump(D, 140.0, 60.0, 0.16, rng, drop=0.02, noise=0.5, noise_lp=900.0)
    add(y, 0.35 * _wood_body(ns(0.25), rng, f0=260.0, t60=0.05), ns(0.008))        # frame knock
    add(y, _brass_latch(0.1, rng, 2600.0), ns(0.07), 0.45)                         # latch snaps in
    return room(sat(y, 1.3), rt60=1.0, wet=0.22, seed=6112)


@sound('door_slam_wood', 1, -1.5)
def door_slam_wood():
    rng = R(6121)
    D = 1.8
    n = ns(D)
    y = _wood_body(n, rng, f0=88.0, t60=0.32, amp_lp=6000.0, att=0.01)
    y += 0.9 * thump(D, 180.0, 48.0, 0.3, rng, drop=0.018, noise=0.8, noise_lp=1600.0)
    crack = bp(rng.standard_normal(n), 900.0, 4500.0) * env_perc(n, 0.0003, 0.035)  # splintering crack
    y += 0.14 * _nz(crack)
    r = rattle(0.6, rng, rate=60, f_lo=900, f_hi=3500, parts=5, t60=0.03)           # frame and hinges shake
    add(y, 0.25 * _nz(r) * env_perc(ns(0.6), 0.005, 0.3), ns(0.02))
    add(y, 0.4 * _wood_body(ns(0.4), rng, f0=150.0, t60=0.1), ns(0.11))           # bounces off the wall
    return room(sat(y, 1.8), rt60=1.4, wet=0.28, seed=6122)


@sound('door_locked_wood', 1, -4.0)
def door_locked_wood():
    rng = R(6131)
    D = 0.8
    n = ns(D)
    y = np.zeros(n)
    for t0 in (0.02, 0.18, 0.34, 0.52):                                            # knob rattles, door knocks the frame
        add(y, 0.7 * _brass_latch(0.08, rng, rng.uniform(1900, 2700)), ns(t0))
        add(y, 0.8 * _wood_body(ns(0.2), rng, f0=rng.uniform(120, 150), t60=0.06), ns(t0 + 0.006), rng.uniform(0.6, 1.0))
    return room(y, rt60=0.8, wet=0.15, seed=6132)


@sound('door_open_metal', 1, -4.0)
def door_open_metal():
    rng = R(6201)
    D = 2.0
    n = ns(D)
    y = np.zeros(n)
    add(y, metal_hit(0.3, 820.0, rng, count=8, t60=0.12, bright=6000, noise=0.6), ns(0.01), 0.6)   # push-bar clunk
    add(y, 0.4 * thump(0.3, 160.0, 80.0, 0.08, rng, noise=0.4), ns(0.012))
    sq = creak(1.5, rng, f0=520.0, rate=(70, 190), q_mult=2.2, bright=7000.0)      # rusty hinge squeal
    add(y, 0.75 * _nz(sq), ns(0.14))
    pan_ = _steel_panel(1.8, rng, f0=88.0, t60=1.0, bright=1800.0)                  # big panel wobbles as it swings
    add(y, 0.35 * pan_ * env_pts(len(pan_), [(0, 0), (0.15, 1), (1.8, 0)]), ns(0.1))
    return room(y, rt60=1.3, wet=0.25, seed=6202)


@sound('door_close_metal', 1, -2.5)
def door_close_metal():
    rng = R(6211)
    D = 1.8
    n = ns(D)
    y = _steel_panel(D, rng, f0=92.0, t60=1.2, bright=3200.0)                       # heavy steel clang
    y += 0.7 * thump(D, 160.0, 55.0, 0.2, rng, noise=0.6, noise_lp=1400.0)
    add(y, metal_hit(0.3, 1450.0, rng, count=7, t60=0.07, bright=9000, noise=0.5), ns(0.05), 0.45)  # latch bolt
    r = rattle(D, rng, rate=40, f_lo=1200, f_hi=4500, parts=5, t60=0.025) * env_perc(n, 0.01, 0.35)
    y += 0.2 * _nz(r)
    return room(sat(y, 1.5), rt60=1.4, wet=0.26, seed=6212)


@sound('door_slam_metal', 1, -1.0)
def door_slam_metal():
    rng = R(6221)
    D = 2.6
    n = ns(D)
    y = _steel_panel(D, rng, f0=78.0, t60=1.9, bright=5000.0, count=28)
    y += 0.9 * thump(D, 200.0, 45.0, 0.3, rng, drop=0.015, noise=0.9, noise_lp=2500.0)
    add(y, 0.5 * _steel_panel(1.2, rng, f0=131.0, t60=0.7, bright=6000.0), ns(0.13))   # rebound off the wall
    r = rattle(1.2, rng, rate=90, f_lo=1000, f_hi=5000, parts=6, t60=0.03)
    add(y, 0.3 * _nz(r) * env_perc(ns(1.2), 0.003, 0.5), ns(0.01))
    return room(sat(y, 2.0), rt60=1.8, wet=0.3, seed=6222)


@sound('door_locked_metal', 1, -4.0)
def door_locked_metal():
    rng = R(6231)
    D = 0.9
    n = ns(D)
    y = np.zeros(n)
    for t0 in (0.02, 0.17, 0.33, 0.5):                                             # handle yanks, steel panel thuds
        add(y, metal_hit(0.25, rng.uniform(900, 1300), rng, count=8, t60=0.07, bright=7000, noise=0.5), ns(t0), 0.7)
        add(y, 0.55 * _steel_panel(0.4, rng, f0=rng.uniform(95, 110), t60=0.3, bright=1500.0), ns(t0 + 0.004))
    return room(y, rt60=1.0, wet=0.2, seed=6232)


@sound('curtain', 1, -6.0)
def curtain():
    rng = R(6301)
    D = 0.9
    n = ns(D)
    sw = bp(rng.standard_normal(n), 400.0, 5000.0) * env_pts(n, [(0, 0), (0.12, 1), (0.5, 0.6), (D, 0)])   # fabric swish
    y = 0.8 * _nz(sw)
    for t0 in np.sort(rng.uniform(0.02, 0.5, 9)):                                  # rings sliding on the rod
        add(y, metal_hit(0.12, rng.uniform(2200, 3200), rng, count=5, t60=0.05, bright=9000, noise=0.3), ns(t0), rng.uniform(0.12, 0.3))
    return room(y, rt60=0.7, wet=0.15, seed=6302)


def main():
    ap = argparse.ArgumentParser(description='Rusted Fair procedural audio generator')
    ap.add_argument('--only', default='', help='comma-separated subset of names')
    ap.add_argument('--out', default=OUT_DIR)
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)
    only = set(s for s in args.only.split(',') if s)
    rows, bad = [], []
    t_all = time.time()
    for name, fn, ch, peak, loop in SOUNDS:
        if only and name not in only:
            continue
        t0 = time.time()
        x = master(fn(), ch, peak, loop)
        path = os.path.join(args.out, name + '.wav')
        write_wav(path, x)
        # verify what was actually written
        sr, d = wavfile.read(path)
        d = d.astype(float) / 32768.0
        if d.ndim == 2:
            d = d.T
        pk = 20 * np.log10(np.max(np.abs(d)) + 1e-12)
        dc = float(np.max(np.abs(np.mean(d, axis=-1))))
        edge = float(max(np.max(np.abs(d[..., :1])), np.max(np.abs(d[..., -1:]))))
        loop_info = ''
        if loop:
            ok, jump, typ = check_loop(d)
            loop_info = 'OK  (wrap %.4f, step sd %.4f)' % (jump, typ) if ok else 'FAIL (wrap %.4f, step sd %.4f)' % (jump, typ)
            if not ok:
                bad.append(name)
        else:
            if edge > 2.0 / 32768:
                bad.append(name + ' (edge click)')
        rows.append((name, d.shape[-1] / sr, 1 if d.ndim == 1 else d.shape[0], pk, dc,
                     os.path.getsize(path), loop_info, time.time() - t0))
        print('  %-24s %6.2fs  %.1fs' % (name, d.shape[-1] / sr, time.time() - t0), flush=True)

    print()
    print('%-24s %8s %3s %8s %8s %9s  %s' % ('name', 'dur(s)', 'ch', 'peak dB', 'DC', 'size KB', 'loop check'))
    print('-' * 100)
    total = 0
    for name, dur, ch, pk, dc, size, li, _ in rows:
        total += size
        print('%-24s %8.2f %3d %8.2f %8.5f %9.1f  %s' % (name, dur, ch, pk, dc, size / 1024, li))
    print('-' * 100)
    print('%d files, %.1f MB total, %.0f s' % (len(rows), total / 1e6, time.time() - t_all))
    if bad:
        print('PROBLEMS:', ', '.join(bad))
        sys.exit(1)
    print('All loop boundaries continuous, all one-shots start/end at zero.')


if __name__ == '__main__':
    main()
