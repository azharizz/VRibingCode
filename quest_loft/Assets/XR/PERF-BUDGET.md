# VIRTUAL LOFT — Performance Budget (Quest 3 / 3S only)

> Target: sustained **72 fps** standalone on Quest 3/3S (Horizon OS).
> **Quest 2 is NOT a target** — Quest 2 deploy from Apple Silicon Mac is broken;
> do not spend budget on it. Validate perf truth on a borrowed Quest 3/3S (V2 weekend).

## Hard caps (V1 — exceeding any cap = must cut before merge)

| Resource | Cap | Why |
|---|---|---|
| Rooms (scenes loaded) | **1** | One loft at a time; mood variants swap light/audio, not geometry |
| Agents | **3** (Scout / Builder / Keeper) | More = noise + draw calls + anim cost |
| Boards | **max 12** (6–12 typical, 1 main curved wall 12 m) | Each board = 1 draw batch + text mesh |
| Board text | **min 32 sp at 2 m**, high contrast | Quest blurs small text; no laptop-size fonts |
| Audio voices | **pooled, max 8 concurrent**, 1 spatial ping per agent event | No per-frame `AudioSource.PlayOneShot` allocs; pool + reuse |
| Frame rate | **72 fps sustained**, no heat warning in 30-min session | If it dips, cut pixels first (see below) |

## Asset rules

- **Low-poly stylized loft only.** No photogrammetry, no 4K textures. Prefer
  vertex colors / small atlases; compress all textures (ASTC), max 1024 px.
- **Agents:** capsule/blob or low-poly robot, 3 anim states each
  (idle breathe / working glow-type / done pop). State readable from 3 m —
  no extra VFX passes. Cap skinned-mesh bones; prefer transform/procedural anim.
- **Lighting:** 1 mixed directional + baked lightmaps for the loft; no realtime
  shadows on Quest except one cheap blob shadow. Mood variants (Focus / Night /
  Meadow) change light color + background audio, not geometry.
- **Boards:** share 1 text/UI material; pool card objects; never `Instantiate`
  per keystroke. Curved wall = single mesh, boards are quads on it.

## Code rules

- No per-frame allocations in Update (no LINQ, no `new`, no string concat in
  hot paths). Activity log capped at 256 entries (ring).
- Networked structs (Fusion) at ≤ 20 Hz for avatars; hover highlight is
  **local only** — RPC only on select/grab commit.
- Autosave loft JSON every **30 s** + on every board commit; reload target **3 s**.

## Cut order (when over budget)

1. Pixel density / render scale → 2. Vignette quality → 3. Agent anim layers →
   4. Board count (12 → 8 → 6) → 5. Spatial-audio voices (8 → 4).
   Never cut: teleport-only, 32 sp text, push-to-talk, button fallbacks.

## Pinned toolchain (verified Nov 2026)

- Unity **6000 LTS** (6.0 LTS through Oct 2026; 6.3 LTS `6000.3.8f1` Feb 2026) + Android Build Support + OpenJDK + SDK/NDK
- Meta XR All-in-One SDK **v207.0** (Sep 2026) — task baseline: Meta XR Core **v71** line
- Meta Interaction SDK (via All-in-One / Essentials) — ray / grab / teleport
- Meta Voice SDK **v85.0** (Wit.ai, Feb 2026)
- Unity OpenXR Plugin **1.13.0+**
- Photon Fusion **2.1.1 Stable** (Build 2177, Jun 2026)
- Meta Quest Developer Hub **6.5.2** (Mac) + XR Simulator
- Devices: **Quest 3 / 3S only**
