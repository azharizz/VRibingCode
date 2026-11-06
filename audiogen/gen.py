# VIRTUAL LOFT — every sound, synthesized in code (numpy). No samples, no downloads.
#   python3 audiogen/gen.py            -> quest_loft/Assets/Resources/Audio/*.wav
# Unity loads them by name (Resources/Audio/<name>): theme ambience, party music,
# sound effects, one voice per animal. audiogen/test.py checks them all.

import os
import wave

import numpy as np

SR = 44100
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.abspath(os.path.join(HERE, "..", "quest_loft", "Assets", "Resources", "Audio"))
rng = np.random.default_rng(42)


# ---------------------------------------------------------------- building blocks
def t_(dur):
    return np.arange(int(dur * SR)) / SR


def env(n, a=0.005, d=0.1, s=0.0, r=0.05, hold=0.0):
    """ADSR over n samples (seconds for a/d/r/hold, s = sustain level)."""
    A, D, H, R = (int(x * SR) for x in (a, d, hold, r))
    S = max(0, n - A - D - H - R)
    e = np.concatenate([np.linspace(0, 1, A, endpoint=False), np.linspace(1, s, D, endpoint=False) if D else [],
                        np.full(H + S, s if D else 1.0), np.linspace(s if D else 1.0, 0, R)])
    return np.pad(e, (0, max(0, n - len(e))))[:n]


def osc(freq, dur, shape="sine"):
    """freq: number or per-sample array (glides). Phase-continuous."""
    n = int(dur * SR)
    f = np.broadcast_to(np.asarray(freq, float), (n,))
    ph = 2 * np.pi * np.cumsum(f) / SR
    if shape == "sine":
        return np.sin(ph)
    if shape == "tri":
        return 2 / np.pi * np.arcsin(np.sin(ph))
    if shape == "saw":  # band-limited-ish: 8 harmonics
        return sum(np.sin(k * ph) / k for k in range(1, 9)) * 0.6
    if shape == "square":
        return sum(np.sin(k * ph) / k for k in range(1, 12, 2)) * 0.9
    raise ValueError(shape)


def noise(dur):
    return rng.standard_normal(int(dur * SR))


def spectral(x, lo=20.0, hi=20000.0, tilt=0.0):
    """FFT band-pass with optional spectral tilt (dB/octave, -3 = pink)."""
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1 / SR)
    g = ((f >= lo) & (f <= hi)).astype(float)
    if tilt:
        g *= np.where(f > 0, (np.maximum(f, 1) / 1000.0) ** (tilt / 6.02), 0)
    return np.fft.irfft(X * g, len(x))


def lowpass(x, cutoff):
    """One-pole low-pass; cutoff may be a per-sample array (sweeps)."""
    c = np.broadcast_to(np.asarray(cutoff, float), x.shape)
    a = 1 - np.exp(-2 * np.pi * c / SR)
    y = np.empty_like(x)
    acc = 0.0
    for i in range(len(x)):  # short clips only; beds use spectral()
        acc += a[i] * (x[i] - acc)
        y[i] = acc
    return y


def bell(freq, dur, bright=1.0):
    """Struck-bell tone: inharmonic partials decaying at different rates."""
    t = t_(dur)
    parts = [(1.0, 1.0, 3.0), (2.0, 0.5 * bright, 5.0), (2.76, 0.35 * bright, 7.0), (5.4, 0.15 * bright, 11.0)]
    return sum(a * np.sin(2 * np.pi * freq * m * t) * np.exp(-t * k) for m, a, k in parts) * env(len(t), a=0.002, d=0, r=0.02, hold=dur)


def place(buf, clip, at, gain=1.0):
    i = int(at * SR)
    j = min(len(buf), i + len(clip))
    buf[i:j] += clip[: j - i] * gain
    return buf


def rms_db(x):
    return 20 * np.log10(np.sqrt(np.mean(x ** 2)) + 1e-12)


def master(x, rms=-18.0, peak=0.89, fade=0.03):
    """Fades first (no clicks), THEN loudness to a target RMS, then a soft limiter."""
    x = x - np.mean(x)
    f = int(fade * SR)
    if f:
        x[:min(f, int(0.004 * SR))] *= np.linspace(0, 1, min(f, int(0.004 * SR)))
        x[-f:] *= np.linspace(1, 0, f) ** 2  # quadratic: rings out instead of chopping
    x = x * 10 ** ((rms - rms_db(x)) / 20)
    over = np.abs(x) > peak * 0.8
    if over.any():  # soft knee above 80% of the ceiling
        x = np.where(over, np.sign(x) * (peak * 0.8 + (peak * 0.2) * np.tanh((np.abs(x) - peak * 0.8) / (peak * 0.2))), x)
    return x


def loop_xfade(x, n, xf):
    """x has n + xf samples -> seamless n-sample loop (tail crossfaded into head)."""
    w = np.linspace(0, 1, xf)
    out = x[:n].copy()
    out[:xf] = x[:xf] * w + x[n:n + xf] * (1 - w)
    return out


def loop_fold(x, n):
    """Music: ring-out past the loop end is added back onto the start."""
    out = x[:n].copy()
    tail = x[n:]
    out[: len(tail)] += tail
    return out


def save(name, x):
    os.makedirs(OUT, exist_ok=True)
    pcm = (np.clip(x, -1, 1) * 32767).astype("<i2")
    with wave.open(os.path.join(OUT, f"{name}.wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    print(f"[audio] {name:34s} {len(x) / SR:5.2f}s  rms {rms_db(x):6.1f} dBFS  peak {20 * np.log10(np.max(np.abs(x)) + 1e-12):5.1f} dBFS")


# ---------------------------------------------------------------- sound effects
def sfx_click():
    t = t_(0.05)
    x = np.sin(2 * np.pi * 2600 * t) * np.exp(-t * 180) + 0.3 * spectral(noise(0.05), 3000, 9000) * np.exp(-t * 300)
    return master(x, -20, fade=0.01)


def sfx_spawn():  # bubbly "pop!" rising
    a = osc(np.geomspace(320, 980, int(0.12 * SR)), 0.12) * env(int(0.12 * SR), a=0.003, d=0.11)
    b = osc(np.geomspace(600, 1500, int(0.09 * SR)), 0.09) * env(int(0.09 * SR), a=0.002, d=0.08)
    x = place(np.zeros(int(0.26 * SR)), a, 0.0)
    return master(place(x, b, 0.11, 0.7), -16)


def sfx_remove():  # the pop in reverse, falling
    a = osc(np.geomspace(980, 260, int(0.2 * SR)), 0.2) * env(int(0.2 * SR), a=0.01, d=0.19)
    return master(a, -18, fade=0.06)


def sfx_done():  # happy two-note chime (C6 -> E6)
    x = place(np.zeros(int(1.4 * SR)), bell(1046.5, 1.3), 0.0, 0.8)
    return master(place(x, bell(1318.5, 1.25), 0.12), -16, fade=0.2)


def sfx_failed():  # soft "bwomp": two falling square-ish notes through a low-pass
    n = int(0.28 * SR)
    a = osc(np.linspace(240, 200, n), 0.28, "square") * env(n, a=0.01, d=0.25)
    b = osc(np.linspace(190, 140, int(0.4 * SR)), 0.4, "square") * env(int(0.4 * SR), a=0.01, d=0.38)
    x = place(np.zeros(int(0.75 * SR)), a, 0)
    x = place(x, b, 0.26)
    return master(lowpass(x, 900), -18)


def sfx_approve():  # bright ding with shimmer
    x = bell(1568, 1.6, bright=1.3) + 0.25 * bell(3136, 1.6, 0.5)
    return master(x, -15, fade=0.25)


def sfx_reject():  # low thud
    n = int(0.32 * SR)
    body = osc(np.geomspace(140, 55, n), 0.32) * env(n, a=0.002, d=0.3)
    hit = lowpass(noise(0.32), 600) * np.exp(-t_(0.32) * 40)
    return master(body + 0.5 * hit, -16)


def sfx_teleport():  # whoosh: noise with a sweeping low-pass, swelling then fading
    n = int(0.55 * SR)
    sweep = np.concatenate([np.geomspace(300, 6000, n // 2), np.geomspace(6000, 400, n - n // 2)])
    x = lowpass(noise(0.55), sweep) * np.sin(np.linspace(0, np.pi, n)) ** 1.5
    return master(x, -20)


def sfx_party_horn():  # da-da-daaa air horn: detuned saws + vibrato, a little grit
    x = np.zeros(int(1.45 * SR))
    for at, dur in ((0.0, 0.16), (0.22, 0.16), (0.44, 0.95)):
        n = int(dur * SR)
        vib = 1 + 0.008 * np.sin(2 * np.pi * 6 * t_(dur))
        tone = sum(osc(f * vib, dur, "saw") for f in (466, 469, 587, 590))
        x = place(x, np.tanh(1.6 * tone) * env(n, a=0.015, d=0.0, r=0.05, hold=dur), at)
    return master(lowpass(x, 3500), -14)


def sfx_confetti_pop():  # cork pop + falling sparkles
    x = np.zeros(int(1.3 * SR))
    pop = spectral(noise(0.06), 400, 6000) * np.exp(-t_(0.06) * 90) + osc(np.geomspace(180, 60, int(0.06 * SR)), 0.06) * 0.8
    x = place(x, pop, 0.0)
    for _ in range(26):
        f = rng.uniform(2500, 7000)
        x = place(x, osc(f, 0.06) * np.exp(-t_(0.06) * 70) * rng.uniform(0.15, 0.4), rng.uniform(0.08, 1.15))
    return master(x, -18)


# ---------------------------------------------------------------- animal voices
def chirp(f0, f1, dur, shape="sine", am=0.0):
    n = int(dur * SR)
    x = osc(np.geomspace(f0, f1, n), dur, shape)
    if am:  # croaky pulses (frog)
        x *= 0.5 + 0.5 * np.sign(np.sin(2 * np.pi * am * t_(dur)))
    return x * env(n, a=0.01, d=0.0, r=0.03, hold=dur)


VOICES = {  # two syllables each; pitch/shape gives each animal its own "personality"
    "fox": [(820, 1150, 0.08, "tri"), (1150, 900, 0.1, "tri")],        # yip-yip
    "owl": [(380, 330, 0.18, "sine"), (330, 300, 0.24, "sine")],          # hoo-hoo
    "cat": [(620, 900, 0.12, "tri"), (900, 560, 0.18, "tri")],            # mi-aow
    "bear": [(180, 230, 0.14, "square"), (230, 170, 0.16, "square")],     # low grumble
    "rabbit": [(1300, 1600, 0.06, "sine"), (1500, 1800, 0.06, "sine")],   # tiny squeaks
    "penguin": [(400, 560, 0.09, "square"), (560, 380, 0.13, "square")],  # honk-squeak
    "frog": [(260, 240, 0.12, "saw"), (240, 220, 0.14, "saw")],           # rib-bit
    "panda": [(420, 520, 0.1, "tri"), (520, 400, 0.14, "tri")],
}


def voice(name):
    x = np.zeros(int(0.6 * SR))
    at = 0.0
    for f0, f1, dur, shape in VOICES[name]:
        x = place(x, chirp(f0, f1, dur, shape, am=32 if name == "frog" else 0), at)
        at += dur + 0.04
    x = lowpass(x, 2500 if name in ("bear", "frog", "penguin") else 6000)
    return master(x, -20)


# ---------------------------------------------------------------- ambience beds (seamless loops)
LOOP = 16.0


def amb_rain():
    n, xf = int(LOOP * SR), int(1.0 * SR)
    total = (n + xf) / SR
    bed = spectral(noise(total), 200, 9000, tilt=-3)
    x = bed / np.std(bed)
    for _ in range(int(total * 22)):  # droplets on the window
        f = rng.uniform(1800, 5200)
        x = place(x, osc(f, 0.025) * np.exp(-t_(0.025) * 160) * rng.uniform(0.6, 2.2), rng.uniform(0, total - 0.03))
    return master(loop_xfade(x, n, xf), -32, fade=0)


def amb_night_hum():
    n, xf = int(LOOP * SR), int(1.0 * SR)
    total = (n + xf) / SR
    t = t_(total)
    hum = sum(a * np.sin(2 * np.pi * f * t) for f, a in ((55, 1.0), (110, 0.5), (165.4, 0.25), (220.2, 0.12)))
    hum *= 1 + 0.15 * np.sin(2 * np.pi * t / 4.0)
    pad = 0.25 * (np.sin(2 * np.pi * 440 * t) + np.sin(2 * np.pi * 659.3 * t)) * (0.5 + 0.5 * np.sin(2 * np.pi * t / 8.0))
    air = 0.4 * spectral(noise(total), 2000, 8000, tilt=-3) / 1.0
    return master(loop_xfade(hum + pad * 0.3 + air * 0.05, n, xf), -34, fade=0)


def bird_trill(base):
    x = np.zeros(int(0.7 * SR))
    for k in range(rng.integers(3, 7)):
        f0 = base * rng.uniform(0.9, 1.15)
        x = place(x, chirp(f0, f0 * rng.uniform(1.2, 1.6), rng.uniform(0.04, 0.08)), k * rng.uniform(0.07, 0.11))
    return x


def amb_meadow():
    n, xf = int(LOOP * SR), int(1.0 * SR)
    total = (n + xf) / SR
    t = t_(total)
    wind = spectral(noise(total), 80, 1500, tilt=-3)
    wind = wind / np.std(wind) * (0.6 + 0.4 * np.sin(2 * np.pi * t / 5.3) ** 2)
    x = wind * 0.5
    for _ in range(9):
        x = place(x, bird_trill(rng.uniform(2400, 4200)), rng.uniform(0, total - 0.8), rng.uniform(0.6, 1.4))
    return master(loop_xfade(x, n, xf), -32, fade=0)


def amb_beach():
    n, xf = int(LOOP * SR), int(1.0 * SR)
    total = (n + xf) / SR
    t = t_(total)
    swell = np.sin(np.pi * ((t % 8.0) / 8.0)) ** 3  # a wave every 8 s
    crash = spectral(noise(total), 150, 7000, tilt=-3)
    foam = spectral(noise(total), 3000, 10000)
    x = crash / np.std(crash) * (0.25 + swell) + foam / np.std(foam) * swell ** 4 * 0.4
    return master(loop_xfade(x, n, xf), -30, fade=0)


# ---------------------------------------------------------------- music
def midi(m):
    return 440.0 * 2 ** ((m - 69) / 12)


def kick(dur=0.35):
    n = int(dur * SR)
    return osc(np.concatenate([np.geomspace(150, 48, n // 3), np.full(n - n // 3, 48)]), dur) * np.exp(-t_(dur) * 9)


def snare_clap(dur=0.25):
    return spectral(noise(dur), 700, 7000) * np.exp(-t_(dur) * 22) * 0.7


def hat(dur=0.06, open_=False):
    return spectral(noise(dur), 6000, 14000) * np.exp(-t_(dur) * (18 if open_ else 70)) * 0.35


def keys(freq, dur):  # electric-piano-ish: sine + soft bell partial, gentle decay
    t = t_(dur)
    x = np.sin(2 * np.pi * freq * t) + 0.25 * np.sin(2 * np.pi * freq * 2 * t) * np.exp(-t * 6) + 0.1 * np.sin(2 * np.pi * freq * 3.01 * t) * np.exp(-t * 9)
    return x * env(len(t), a=0.008, d=dur * 0.8, s=0.25, r=0.15)


def party_track():
    """120 BPM, 8 bars (16 s) house loop in A minor: 4-on-the-floor kick, clap on
    2 & 4, off-beat hats, pumping octave bass, side-chained chord stabs (Am F C G)."""
    bpm, bars = 120, 8
    beat = 60 / bpm
    n = int(bars * 4 * beat * SR)
    mix = np.zeros(n + int(2 * SR))
    drums, bass, chords = mix.copy(), mix.copy(), mix.copy()
    prog = [(57, [57, 60, 64]), (53, [53, 57, 60]), (48, [48, 52, 55]), (55, [55, 59, 62])]  # Am F C G
    for b in range(bars * 4):
        at = b * beat
        place(drums, kick(), at, 1.0)
        if b % 2 == 1:
            place(drums, snare_clap(), at, 0.8)
        place(drums, hat(open_=b % 4 == 3), at + beat / 2, 0.9)
        root, chord = prog[(b // 4) % 4]
        for k, off in enumerate((0, 0.5)):  # octave-jumping bass on 8ths
            f = midi(root - 24 + 12 * k)
            place(bass, lowpass(osc(f, beat / 2 * 0.9, "saw"), 700) * env(int(beat / 2 * 0.9 * SR), a=0.005, d=0.15, s=0.6, r=0.03), at + off * beat)
        stab = sum(osc(midi(m + 12), 0.18, "saw") for m in chord) / 3
        place(chords, lowpass(stab * env(len(stab), a=0.003, d=0.15), 2400), at + beat / 2)
    duck = np.ones_like(mix)  # side-chain pump from the kick
    for b in range(bars * 4 + 4):
        i = int(b * beat * SR)
        d = np.linspace(0.35, 1.0, int(0.22 * SR))
        duck[i:i + len(d)] = np.minimum(duck[i:i + len(d)], d[: len(duck[i:i + len(d)])])
    mixed = drums + bass * duck * 0.7 + chords * duck * 0.55
    return master(loop_fold(mixed, n), -14, fade=0)


def party_lofi():
    """Chill lo-fi loop for the party THEME (not party mode): 80 BPM, 4 bars
    (12 s), lazy 7th chords on keys, soft kick/snare, vinyl crackle."""
    bpm, bars = 80, 4
    beat = 60 / bpm
    n = int(bars * 4 * beat * SR)
    mix = np.zeros(n + int(3 * SR))
    prog = [[48, 52, 55, 59], [45, 48, 52, 55], [50, 53, 57, 60], [43, 47, 50, 53]]  # Cmaj7 Am7 Dm7 G7
    for bar, chord in enumerate(prog):
        at = bar * 4 * beat
        for k, m in enumerate(chord):
            place(mix, keys(midi(m + 12), 4 * beat), at + k * 0.03, 0.35)  # strummed
        for b in range(4):
            if b in (0, 2):
                place(mix, kick(0.3) * 0.8, at + b * beat + (0.03 if b == 2 else 0))
            if b in (1, 3):
                place(mix, snare_clap(0.2) * 0.5, at + b * beat)
            place(mix, hat(), at + b * beat + beat / 2, 0.5)
    mix = lowpass(mix, 3800)
    out = loop_fold(mix, n)
    crackle = np.zeros(n)
    idx = rng.integers(0, n, 600)
    crackle[idx] = rng.uniform(-1, 1, 600)
    out += spectral(crackle, 1000, 9000) * 0.6 + spectral(noise(n / SR), 100, 4000, tilt=-3) * 0.01
    return master(out, -22, fade=0)


SOUNDS = {
    # effects
    "sfx_click": sfx_click, "sfx_spawn": sfx_spawn, "sfx_remove": sfx_remove, "sfx_done": sfx_done,
    "sfx_failed": sfx_failed, "sfx_approve": sfx_approve, "sfx_reject": sfx_reject, "sfx_teleport": sfx_teleport,
    "sfx_party_horn": sfx_party_horn, "sfx_confetti_pop": sfx_confetti_pop,
    # animal voices
    **{f"voice_{a}": (lambda a=a: voice(a)) for a in VOICES},
    # theme ambience (names from Resources/Themes/*.json)
    "loft_ambient_focus_rain_loop": amb_rain, "loft_ambient_night_hum_loop": amb_night_hum,
    "loft_ambient_meadow_birds_loop": amb_meadow, "beach_waves_loop": amb_beach, "party_lofi_loop": party_lofi,
    # party mode music (LoftParty loads Resources/Audio/party)
    "party": party_track,
}

if __name__ == "__main__":
    for name, make in SOUNDS.items():
        save(name, make())
    print(f"[audio] {len(SOUNDS)} sounds -> {OUT}")
