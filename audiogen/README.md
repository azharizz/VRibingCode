# audiogen — every sound in the loft, synthesized in code

`python3 audiogen/gen.py` (numpy) writes 24 WAVs to
`quest_loft/Assets/Resources/Audio/`; Unity loads them by name through `LoftAudio`.

| sound | when it plays (Unity) |
| --- | --- |
| `party` | PARTY MODE music: 120 BPM house loop, 16 s (kick, clap, hats, pumping bass, Am-F-C-G stabs), in sync with the dance |
| `loft_ambient_focus_rain_loop`, `..._night_hum_loop`, `..._meadow_birds_loop`, `beach_waves_loop`, `party_lofi_loop` | theme ambience (seamless loops, quiet) |
| `sfx_spawn` / `sfx_remove` | an agent joins / leaves (3D, at the animal) |
| `voice_<animal>` | the animal starts a task, in its own voice (owl hoo-hoo, frog rib-bit, …) |
| `sfx_done` / `sfx_failed` | task finished (two-note chime) / failed (soft bwomp), 3D at the animal |
| `sfx_approve` / `sfx_reject` | CEO approves / rejects a result |
| `sfx_click` | every console button (VR press feedback) |
| `sfx_teleport` | teleport whoosh |
| `sfx_party_horn`, `sfx_confetti_pop` | the party drop |

`python3 audiogen/test.py`: 57 checks covering format, no clipping or DC offset,
clean starts and ends, seamless loops, the loudness order (effects > voices > ambience),
120 BPM measured from the audio, 8 distinct voices, and every name the game uses existing.
It also writes `out/spectrograms_*.png`.

`python3 audiogen/mix_party_preview.py` puts the sound on the party video →
`animalgen/out/party/party_with_sound.mp4`.

Tweak a sound: edit its function in `gen.py` (e.g. `sfx_done`), rerun gen + test.
