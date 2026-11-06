# SPATIAL AUDIO SPEC — VIRTUAL LOFT V3 (spec only, no implemented code)

**Stack:** Unity 6000.6.7f1 + Meta XR v71 (Meta XR Audio SDK for HRTF), Quest 3/3S virtual-only.
**Parents:** `../Agents/agent-configs.json` (desk positions + `sound` ids) · `../XR/PERF-BUDGET` (8 pooled voices rule) · `../Scenes/LOFT-VARIANTS.md` (ambient loops per mood) · `R0-pack.md` §a (push-to-talk).
**Status:** SPEC ONLY. No scripts, no prefabs, no `.wav` binaries in this pass. An audio implementer wires this 1:1 in V3.

---

## 1. Per-agent done-sounds + emitter positions

Each agent owns exactly ONE done-sound, fired once on task-complete (done-pop moment). Emitter = invisible point at the agent's head, derived from `agent-configs.json` desk positions + seated creature head height (desk top 0.75 m + ~0.4 m creature = **y ≈ 1.15 m**).

| Agent (`agent-configs.json` id) | Done-sound id | File | Emitter (x, y, z) meters | Character (design intent) |
|---|---|---|---|---|
| `scout` (desk-1, blue `#4FC3F7`) | `scout_done_ping` | `scout_done_ping.wav` | `(-2.0, 1.15, 1.5)` | Soft glass ping (research delivered) |
| `builder` (desk-2, green `#9CCC65`) | `builder_done_check` | `builder_done_check.wav` | `(0.0, 1.15, 1.5)` | Warm check-chime + tiny click (tests pass) |
| `keeper` (desk-3, amber `#FFB74D`) | `keeper_done_bell` | `keeper_done_bell.wav` | `(+2.0, 1.15, 1.5)` | Small hand-bell (card filed to Done) |

Rules:
- Done-sounds are **positional 3D, HRTF on** (see §2). User at origin hears Scout left, Builder center, Keeper right — direction = which agent finished, no reading required.
- One-shot, non-looping, ≤1.2 s each. Cooldown 500 ms per agent (a double-complete fires once; second event only flashes the visual pop).
- Fallback: if HRTF/head-tracking unavailable (Simulator without HMD pose), same files play as flat stereo at fixed L/C/R pan (−0.6 / 0 / +0.6). Priority order stays identical.

## 2. Meta XR Audio SDK HRTF usage

- All 3 done-sound emitters + any future agent voice blips route through the **Meta XR Audio SDK spatializer (HRTF)** with head-tracked binaural rendering on Quest 3/3S.
- Ambient mood beds (§4) are **excluded from HRTF** — they are 2D stereo loops (head-locked, no positionalization) so the room tone never falsely localizes.
- Team voice chat (Photon Voice / Vivox) uses per-remote-user HRTF emitters at avatar head positions (separate from the 8-voice SFX pool; voice uses its own codec path — do NOT count voice streams against the §3 pool).
- Simulator check (no headset): verify L/C/R separation on PC speakers + verify `AudioSource.spatialize` flag is true on the 3 emitters in the Inspector. Truth check (direction + elevation) is deferred to the borrowed-Quest weekend.

## 3. 8 pooled voices rule (from PERF-BUDGET — normative)

- **Max 8 concurrent SFX voices**, pre-allocated at scene load as a fixed pool of 8 `AudioSource`s. **Never `Instantiate`, never per-frame `PlayOneShot` allocations, never `new` in hot paths.**
- Pool membership (worst case fits in 8): 3 agent done-pings + 1 UI confirm + 1 UI error + 1 card-snap + 1 teleport whoosh + 1 spare for Focus-mode toggle. Ambient beds are dedicated looping sources OUTSIDE the pool (2 max alive: current + 800 ms crossfade next — see LOFT-VARIANTS.md rule 1).
- Priority steal order when all 8 busy (lowest stolen first): spare → UI confirm → card-snap → teleport whoosh → UI error → done-pings (done-pings are never stolen by UI; a 9th request drops with a counter increment, no log spam — activity log ring cap 256 still applies).
- All pool clips loaded once at scene start (compressed in build, decompressed to ≤ a fixed budget); pool reuse = set clip + position + play. No per-keystroke pooling (board text typing is silent by design).

## 4. Ambient beds per mood (one at a time)

| Mood (`roomId`) | Loop file | Bed character | Level |
|---|---|---|---|
| `focus-loft` | `loft_ambient_focus_rain_loop.wav` | Lo-fi rain, soft + continuous (masks kos/cafe bleed) | −18 LUFS |
| `night-sprint` | `loft_ambient_night_hum_loop.wav` | Low neon hum + slow synth pad, no transients | −18 LUFS |
| `calm-meadow` | `loft_ambient_meadow_birds_loop.wav` | Sparse birdsong chirps + soft wind (chirp peaks ≤ −26 LUFS so voice stays intelligible) | −20 LUFS |

- Seamless loop, zero-crossing edit, ≥30 s each. Crossfade 800 ms on mood switch; never stack two beds.
- Beds duck, never mute (room must not go dead-silent — silence reads as "app crashed" in VR).

## 5. Push-to-talk ducking

Push-to-talk = hold trigger (per R0-pack §a + PRODUCT.md §5C). While held (mic open for command or team voice):
- Ambient bed **−12 dB**, release ramps back over 250 ms.
- Agent done-pings + UI SFX **−6 dB** while held (command intelligibility > celebration).
- Incoming team voice is NOT ducked (it IS the signal); local mic waveform shows on wrist per PRODUCT.md.
- `Stop` command (safety interrupt) always wins: it cuts the working-loop sound of the targeted agent immediately, then fires that agent's done/interrupt blip at full level once.

## 6. File naming convention + format

- Format: **`.wav`, 44.1 kHz, mono, 16-bit** for every spatial/pool clip (HRTF wants mono in; stereo files are rejected at review). Ambient beds: same spec (stereo image is created by the 2D playback path, not the file).
- Names: lowercase snake_case, `agent_{state}_{variant}` shape:
  - `scout_done_ping.wav` · `builder_done_check.wav` · `keeper_done_bell.wav`
  - `loft_ambient_focus_rain_loop.wav` · `loft_ambient_night_hum_loop.wav` · `loft_ambient_meadow_birds_loop.wav`
  - `ui_confirm.wav` · `ui_error.wav` · `ui_card_snap.wav` · `ui_teleport_whoosh.wav` · `ui_focus_toggle.wav`
- Versioning: never overwrite a tuned file — `keeper_done_bell_v02.wav`, promote by renaming (keeps Simulator vs Quest tuning comparable).
- Placement (V3): `quest_loft/Assets/Audio/clips/` for wavs; this spec stays at `quest_loft/Assets/Audio/SPATIAL-AUDIO-SPEC.md`.

## 7. Mixamo / Ready Player Me animation hook list (V3 wiring map)

Blob agents, no lipsync, transform/procedural-first per PERF-BUDGET (cap skinned bones). Logical state names are locked by `agent-configs.json` `anims{}`; the table maps each to its V3 source clip. Mixamo = primary (free, no rig repurchase); Ready Player Me = fallback only if a Mixamo clip reads poorly on the blob deform.

| Agent | State | Logical clip (`agent-configs.json`) | Mixamo source clip | RPM fallback clip | Loop | Notes |
|---|---|---|---|---|---|---|
| Scout | idle | `scout_sleep` | `Sleeping Idle` (or `Idle Sleeping`) | `Idle Relaxed` | loop, breathe 0.25 Hz | Blink overlay procedural; wake = 200 ms scale-up |
| Scout | working | `scout_search` | `Looking Around / Search` (`Head Turn` + magnifier prop bob) | `Inspecting` | loop | Flying-paper motion is prop anim, not bones |
| Scout | done | `scout_present` | `Presenting / Arm Gesture Forward` | `Waving` (trimmed) | one-shot 1.0 s | Card flies to wall on beat 0.6 s |
| Builder | idle | `builder_breathe` | `Breathing Idle` (`Idle`) | `Idle Standing` | loop, breathe 0.22 Hz | Seated; hands rest glow-off |
| Builder | working | `builder_type` | `Typing` (seated, `Typing While Sitting`) | `Clapping`-trim → key-tap procedural | loop | Hand glow ON = state signal from 3 m |
| Builder | done | `builder_stretch` | `Stretching` (`Stretch Up`) | `Celebration Small` | one-shot 1.2 s | Green check pop at apex + `builder_done_check` ping |
| Keeper | idle | `keeper_tidy` | `Sweeping` (trimmed loop) | `Idle Organizing` | loop | Broom/tidy prop; never walks (desk-bound) |
| Keeper | working | `keeper_move` | `Carrying / Push` (short shuttle in place) | `Lifting` | loop | Card shuttle between columns = prop path |
| Keeper | done | `keeper_bell` | `Ring Bell` (`Bell Ring` gesture, arm up) | `Celebration` (trimmed) | one-shot 1.0 s | Bell flash + confetti burst + `keeper_done_bell` |

Hook rules: state machine per agent = 3 states only (idle/working/done); transitions are event-driven (command run → working; task complete → done 1.2 s → idle). State must read from 3 m via silhouette + emissive, never via face detail.
