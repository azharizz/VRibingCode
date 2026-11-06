# Puts the generated sound on the party preview video (same timeline as
# animalgen/party_preview.py and Unity's LoftParty):
#   loft rain ambience -> lights drop -> air horn + confetti pop -> each animal
#   chirps as it hops over -> 120 BPM party track.
#   python3 audiogen/mix_party_preview.py   (after animalgen/party_preview.py)
import os
import subprocess
import wave

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, ".."))
AUDIO = os.path.join(REPO, "quest_loft", "Assets", "Resources", "Audio")
VIDEO = os.path.join(REPO, "animalgen", "out", "party", "party.mp4")
SR, FPS, END = 44100, 24, 168
T_DIM, T_BALL, T_GO = 18 / FPS, 22 / FPS, 30 / FPS
ANIMALS = ["fox", "owl", "cat", "bear", "rabbit", "penguin", "frog", "panda"]


def load(name):
    with wave.open(os.path.join(AUDIO, f"{name}.wav")) as w:
        return np.frombuffer(w.readframes(w.getnframes()), "<i2").astype(float) / 32768


def place(buf, clip, at, gain=1.0):
    i = int(at * SR)
    j = min(len(buf), i + len(clip))
    buf[i:j] += clip[: j - i] * gain


n = int(END / FPS * SR)
mix = np.zeros(n)
rain = np.tile(load("loft_ambient_focus_rain_loop"), 2)[:n]
duck = np.clip(1 - (np.arange(n) / SR - T_DIM) / 0.4, 0, 1)  # rain fades as the lights drop
mix += rain * duck * 1.6
place(mix, load("sfx_party_horn"), T_BALL, 0.9)
place(mix, load("sfx_confetti_pop"), T_BALL + 0.35, 0.9)
for i, a in enumerate(ANIMALS):  # each one cheers as it sets off
    place(mix, load(f"voice_{a}"), T_GO + i * 3 / FPS, 0.7)
music = load("party")
start = T_BALL + 0.55
m = np.zeros(n)
place(m, music, start)
m *= np.clip((np.arange(n) / SR - start) / 0.3, 0, 1)  # quick fade-in
mix += m * 0.8
mix *= np.minimum(1.0, (n - np.arange(n)) / (0.5 * SR))  # fade out at the end
mix = np.clip(mix, -0.98, 0.98)

wav = os.path.join(HERE, "out", "party_preview_mix.wav")
os.makedirs(os.path.dirname(wav), exist_ok=True)
with wave.open(wav, "wb") as w:
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes((mix * 32767).astype("<i2").tobytes())
out = VIDEO.replace(".mp4", "_with_sound.mp4")
subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-i", VIDEO, "-i", wav, "-c:v", "copy", "-c:a", "aac", "-b:a", "160k",
                "-shortest", out], check=True)
print(f"[mix] {out}")
