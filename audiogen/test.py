# Checks every generated sound. Exit 0 = all pass.   python3 audiogen/test.py
# Also writes audiogen/out/spectrograms_*.png (what each sound looks like).

import glob
import json
import os
import re
import subprocess
import sys
import wave

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, ".."))
AUDIO = os.path.join(REPO, "quest_loft", "Assets", "Resources", "Audio")
results = []


def check(ok, msg):
    results.append(bool(ok))
    print(f"[test] {'PASS' if ok else 'FAIL'}: {msg}")


def load(name):
    with wave.open(os.path.join(AUDIO, f"{name}.wav")) as w:
        fmt = (w.getnchannels(), w.getframerate(), w.getsampwidth())
        x = np.frombuffer(w.readframes(w.getnframes()), "<i2").astype(float) / 32768
    return fmt, x


def db(v):
    return 20 * np.log10(v + 1e-12)


def rms(x):
    return db(np.sqrt(np.mean(x ** 2)))


sys.path.insert(0, HERE)
from gen import SOUNDS, VOICES  # noqa: E402

# Every name the game asks for must exist: themes' "audio", C# string literals.
wanted = set(SOUNDS)
for f in glob.glob(os.path.join(REPO, "quest_loft", "Assets", "Resources", "Themes", "*.json")):
    a = json.load(open(f)).get("audio")
    if a:
        wanted.add(a)
for f in glob.glob(os.path.join(REPO, "quest_loft", "Assets", "**", "*.cs"), recursive=True):
    wanted |= set(re.findall(r'LoftAudio\.Play\w*\("([a-z_]+)"(?!\s*\+)', open(f).read()))  # "voice_" + skin = prefix
    wanted |= {f"voice_{a}" for a in re.findall(r'"voice_" \+ ', open(f).read()) and VOICES}
missing = sorted(n for n in wanted if not os.path.exists(os.path.join(AUDIO, f"{n}.wav")))
check(not missing, f"all {len(wanted)} sounds the game uses exist (missing: {missing})")

SFX = [n for n in SOUNDS if n.startswith("sfx_")]
VOICE = [n for n in SOUNDS if n.startswith("voice_")]
BEDS = {"loft_ambient_focus_rain_loop": 16, "loft_ambient_night_hum_loop": 16, "loft_ambient_meadow_birds_loop": 16, "beach_waves_loop": 16}
MUSIC = {"party": (16, (-18, -10)), "party_lofi_loop": (12, (-26, -18))}

levels = {}
for name in SOUNDS:
    fmt, x = load(name)
    levels[name] = rms(x)
    peak = db(np.max(np.abs(x)))
    ok = fmt == (1, 44100, 2) and peak <= -0.9 and abs(np.mean(x)) < 0.005
    check(ok, f"{name}: mono 44.1k 16-bit, peak {peak:.1f} dBFS (no clipping), no DC offset")
    if name in SFX or name in VOICE:
        dur = len(x) / 44100
        start = np.argmax(np.abs(x) > 10 ** (-40 / 20)) / 44100
        tail = rms(x[-441:])
        check(0.03 <= dur <= 2.0 and start < (0.06 if "teleport" in name else 0.02) and tail < -45,
              f"{name}: {dur:.2f}s, starts in {start * 1000:.0f} ms, fades out clean (last 10 ms {tail:.0f} dB)")
    else:
        length = BEDS.get(name) or MUSIC[name][0]
        d = np.abs(np.diff(x))
        seam = abs(x[0] - x[-1])
        jump = abs(rms(x[: 22050]) - rms(x[-22050:])) if name in BEDS else 0.0  # music may start loud
        check(abs(len(x) / 44100 - length) < 1e-3 and seam <= 3 * np.percentile(d, 99) and jump < 3,
              f"{name}: seamless {length}s loop (seam {seam:.4f} vs p99 step {np.percentile(d, 99):.4f}, level jump {jump:.1f} dB)")

# Loudness hierarchy: effects clearly above beds; music in its lane.
sfx_avg = np.mean([levels[n] for n in SFX])
check(all(-24 <= levels[n] <= -12 for n in SFX), f"effects at -24..-12 dBFS RMS (avg {sfx_avg:.1f})")
check(all(-24 <= levels[n] <= -16 for n in VOICE), "voices at -24..-16 dBFS RMS (under the effects)")
check(all(-38 <= levels[n] <= -26 for n in BEDS), f"ambience at -38..-26 dBFS RMS ({', '.join(f'{levels[n]:.0f}' for n in BEDS)})")
check(sfx_avg - max(levels[n] for n in BEDS) >= 8, "effects at least 8 dB above the loudest ambience bed")
for n, (_, (lo, hi)) in MUSIC.items():
    check(lo <= levels[n] <= hi, f"{n}: {levels[n]:.1f} dBFS RMS in {lo}..{hi}")

# Party music is really 120 BPM (in sync with the dance): onset-envelope autocorrelation.
_, x = load("party")
envl = np.abs(x).reshape(-1, 441).mean(axis=1)  # 100 Hz envelope
envl = np.maximum(np.diff(envl), 0)
ac = np.correlate(envl, envl, "full")[len(envl) - 1:]
lag = 30 + int(np.argmax(ac[30:80]))  # 0.3 .. 0.8 s
check(abs(lag / 100 - 0.5) <= 0.02, f"party: beat period {lag * 10} ms = {6000 / lag:.0f} BPM (want 120)")

# Every animal sounds different (spectral centroid at least 40 Hz apart).
cents = {}
for n in VOICE:
    _, x = load(n)
    X = np.abs(np.fft.rfft(x))
    f = np.fft.rfftfreq(len(x), 1 / 44100)
    cents[n] = float((X * f).sum() / X.sum())
srt = sorted(cents.values())
check(min(b - a for a, b in zip(srt, srt[1:])) >= 40, "8 distinct animal voices (" + ", ".join(f"{k[6:]} {v:.0f}Hz" for k, v in sorted(cents.items(), key=lambda kv: kv[1])) + ")")

# Spectrogram sheets for eyeballing.
out = os.path.join(HERE, "out")
os.makedirs(out, exist_ok=True)
for sheet, names in (("effects", SFX + VOICE), ("beds", list(BEDS) + list(MUSIC))):
    tiles = []
    for n in names:
        png = os.path.join(out, f"_{n}.png")
        subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-i", os.path.join(AUDIO, f"{n}.wav"), "-lavfi",
                        "showspectrumpic=s=360x140:legend=0:color=intensity:scale=log",
                        png], check=True)
        tiles.append(png)
    cols = 3
    args = sum((["-i", p] for p in tiles), [])
    pad = (-len(tiles)) % cols
    subprocess.run(["ffmpeg", "-loglevel", "error", "-y", *args, "-filter_complex",
                    f"{''.join(f'[{i}]' for i in range(len(tiles)))}xstack=inputs={len(tiles)}:layout=" +
                    "|".join(f"{(i % cols) * 360}_{(i // cols) * 140}" for i in range(len(tiles))) + (f":fill=black" if pad else ""),
                    os.path.join(out, f"spectrograms_{sheet}.png")], check=True)
    for p in tiles:
        os.remove(p)
    print(f"[test] spectrograms_{sheet}.png, left->right, top->bottom: {', '.join(names)}")

print(f"[test] SUMMARY audio: {sum(results)} pass, {len(results) - sum(results)} fail")
sys.exit(0 if all(results) else 1)
