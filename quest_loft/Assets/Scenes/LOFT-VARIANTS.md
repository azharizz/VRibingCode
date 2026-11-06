# LOFT VARIANTS — Night Sprint + Calm Meadow (V3 mood lofts)

**Stack:** Unity 6000.6.7f1 + Meta XR v71, Quest 3/3S virtual-only, Simulator-first (no headset).
**Parents:** [FOCUS-LOFT-SPEC.md](FOCUS-LOFT-SPEC.md) (geometry authority) · `../Boards/LoftLayout.json` (position authority) · `../Agents/agent-configs.json` (desks/colors) · `../XR/COMFORT` (`Comfort.cs`) + `../XR/PERF-BUDGET` (`PERF-BUDGET.md`) (budgets authority) · `PRODUCT.md` / `TASK.md` / `R0-pack.md` (vision authority).
**Rule (from PERF-BUDGET): mood variants swap light color + background audio + material tint, NOT geometry.** One room loaded at a time; same 12 m curved wall; same desks; same board ids.

---

## 0. Geometry lock (shared by all 3 lofts — do not re-author per variant)

- 1 main curved wall: 12 m wide arc (radius 6 m, inner face toward user), usable band height 0.6–2.6 m. Side walls = archive, dimmed to 20 % in Focus mode.
- User spawn `(0, 0, 0)` facing −Z; seated eye height 1.2 m (see `Comfort.cs` `SEATED_EYE_HEIGHT`).
- Desks (semi-circle, top 0.75 m, facing user) — identical in every variant:
  - desk-1 Scout `(-2.0, 0, 1.5)` · desk-2 Builder `(0, 0, 1.5)` · desk-3 Keeper `(+2.0, 0, 1.5)`.
- Boards (main wall, wall-local `[x, y, z]`, sizes `[w, h]`) — identical in every variant:
  - Notes `(-4.2, 1.6)` 2.4×1.4 · Kanban `(-1.4, 1.6)` 2.8×1.6 · Calendar `(+1.6, 1.6)` 2.4×1.2 · Log `(+4.2, 1.6)` 2.0×1.4.
- No Scene Mesh / Spatial Anchors / camera APIs in any variant (virtual-only).
- Lighting rig is shared: 1 mixed directional + baked lightmaps + one cheap blob shadow. Variants change that rig's **color/intensity + skybox/background + emissive tint**, never light count (perf: 72 fps sustained).

---

## 1. Night Sprint — dark + neon (deep-work / deadline mood)

**Fantasy:** kos noise gone; lights drop; the wall glows cyan/magenta. Agents' eyes glow so state reads from 3 m in the dark. For the 30-min evening sprint.

- **Background / skybox:** solid very-dark blue-black `#0A0E1A` (no stars texture — vertex-color/gradient skybox only, no extra draw pass). Fog: subtle, same color, far 30 m (hides wall edges, zero cost).
- **Light color / intensity (same rig, re-tuned):**
  - Directional: cool steel blue `#7DB8FF`, intensity **0.35** (vs Focus ~0.9 warm). No realtime shadows except the one blob shadow.
  - Ambient / baked: deep indigo `#141B33` ambient, exposure −0.6 vs Focus.
  - Accent (emissive ONLY — no new realtime lights): wall edge light-strips cyan `#00E5FF` + magenta `#FF2E88`, agent eye/glow emissive boosted 1.5×. Emissive does not cost a light.
- **Wall material tint:** dark slate stained wood `#1B2233` (same mesh/material slot as Focus wood; tint color only, ASTC ≤1024 px shared atlas). Board frames get thin neon border (shared emissive UI material) so boards pop from the dark wall.
- **Ambient audio:** `loft_ambient_night_hum_loop.wav` — low neon hum (50 Hz bed + soft synth pad, slow LFO, no transients). 2D stereo loop, −18 LUFS bed level, ducks −12 dB under push-to-talk (see `../Audio/SPATIAL-AUDIO-SPEC.md`). Replaces Focus lo-fi rain; never layers with it.
- **Board theme (readability):** dark-mode cards — near-black card `#10151F` + off-white text `#F2F6FF`, accent per agent color. Min font still ≥32 sp at 2 m; contrast re-checked (see `V3-POLISH-CHECKLIST.md` font audit). No readability rule is relaxed for mood.
- **Agent readability in the dark:** working-glow and done-pop emissive carry state (Scout cyan ring, Builder green hands, Keeper amber bell flash). Name tags switch to additive glow text.

### Board/desk moves in Night Sprint: NONE

All 4 board positions/sizes and all 3 desk positions are **byte-identical to Focus** (`LoftLayout_Night.json` = same `pos`/`size`, `roomId: "night-sprint"` only). Why:
1. **Muscle memory:** teleport spots + ray rest pose (Builder center) must survive a mood switch mid-session.
2. **Multiplayer shared truth:** "wall left" (Kanban) must mean the same coordinates for every user in the Photon room regardless of their selected mood — per PRODUCT.md §1, layout is what syncs, mood is local preference.
3. **Travel/cloud rule:** layout JSON stays the source of truth; mood is a render/audio skin, not a layout fork, so version conflicts (R0-pack §e last-writer-wins) can't be caused by switching lofts.

---

## 2. Calm Meadow — bright + soft (morning standup / anxious-day mood)

**Fantasy:** warm morning light through a soft meadow haze; pale wood, sage accents; birdsong instead of rain. For the 15-min standup and motion-sensitive users.

- **Background / skybox:** soft vertical gradient — zenith pale sky `#BFE3D0` → horizon warm white `#FFF8EC` (procedural gradient skybox, no texture). Fog: warm white, far 30 m.
- **Light color / intensity (same rig, re-tuned):**
  - Directional: warm daylight `#FFF2DC`, intensity **1.1** (brightest of the 3, still no extra realtime lights).
  - Ambient / baked: soft sage-white `#EAF4EC` ambient, exposure +0.2 vs Focus.
  - Accent (emissive only, kept minimal): thin warm-gold wall trim `#E8C87E` at low emissive; agent glow at 0.8× Focus (bright room already reads well).
- **Wall material tint:** whitewashed pale wood `#EDE4D3` with pale-sage side walls `#DCE8DA` (same mesh/material slot; tint only). Board frames: light wood + sage border.
- **Ambient audio:** `loft_ambient_meadow_birds_loop.wav` — sparse birdsong + soft wind bed (no rain, no hum). 2D stereo loop, −20 LUFS (quietest bed; birds are transient chirps at −26 LUFS peak so they never mask voice). Ducks −12 dB under push-to-talk. Never layers with rain/hum.
- **Board theme (readability):** light-mode cards — white `#FFFFFF` / warm paper `#FFFDF6` + near-black text `#1A1A1A`, agent-color column accents. Min font still ≥32 sp at 2 m; contrast re-checked against the brighter wall (dark text on light board keeps ratio, unlike light text on light wall — hence boards keep their dark frame edge).
- **Agent readability in bright room:** emissive glow is dialed DOWN (washes out in bright light); state reads via silhouette animation instead — Scout paper-fan spread, Builder big typing bounce, Keeper bell + confetti burst (motion, not glow).

### Board/desk moves in Calm Meadow: NONE

All 4 board positions/sizes and all 3 desk positions are **byte-identical to Focus** (`LoftLayout_Meadow.json` = same `pos`/`size`, `roomId: "calm-meadow"` only). Why: same three reasons as Night Sprint (muscle memory, multiplayer shared truth, cloud-layout-not-skin rule). Meadow's brightness changes contrast theme, never coordinates — a user standing at the Kanban in Focus teleports to the identical spot in Meadow.

---

## 3. Mood-switch table (3 lofts × background / audio / light)

Mood switch = local render/audio skin swap only: background color + directional color/intensity + wall tint + ambient loop + board theme. Geometry, desks, board `pos`/`size`, teleport spots, and interaction bindings never change. Target switch time <1 s (no scene reload — one room, per PERF-BUDGET cap).

|  | **Focus Loft** (default) | **Night Sprint** | **Calm Meadow** |
|---|---|---|---|
| `roomId` | `focus-loft` | `night-sprint` | `calm-meadow` |
| **Background** | Warm loft interior `#2A2118` ambient room tone | Dark blue-black `#0A0E1A` + matching fog | Sky gradient `#BFE3D0 → #FFF8EC` + warm fog |
| **Ambient audio** | Lo-fi rain `loft_ambient_focus_rain_loop.wav` | Neon hum `loft_ambient_night_hum_loop.wav` | Meadow birds `loft_ambient_meadow_birds_loop.wav` |
| **Directional light** | Warm soft `#FFE3B3` · 0.9 | Steel blue `#7DB8FF` · 0.35 | Warm daylight `#FFF2DC` · 1.1 |
| **Ambient / exposure** | Warm `#3A2E22` · ±0 | Indigo `#141B33` · −0.6 | Sage-white `#EAF4EC` · +0.2 |
| **Wall tint** | Warm wood (base) | Dark slate `#1B2233` | Whitewash `#EDE4D3` / sage sides |
| **Accent** | Soft gold trim | Cyan `#00E5FF` + magenta `#FF2E88` emissive strips | Gold trim `#E8C87E`, low emissive |
| **Board theme** | Wood-frame, cream cards | Dark cards `#10151F` + neon borders | White/paper cards + dark text |
| **Agent state cue** | Glow + motion balanced | Emissive-led (glow/eyes lead) | Motion-led (silhouette anim leads) |
| **Best session** | Any 15–30 min | Evening deep sprint 30 min | Morning standup 15 min / sensitive users |

**Switching rules:**
1. Only one ambient loop plays at a time; crossfade 800 ms, no stacking (perf + masking).
2. Mood is per-user local preference; Photon room syncs **layout JSON**, never mood (two users can share one Kanban while one sees Night, other sees Meadow).
3. Focus mode (dims side walls to 20 %) works identically in all 3 lofts.
4. Comfort policy is mood-independent: teleport-only, seated 1.2 m, vignette 0.45/0.35 s, 15–30 min nudge — re-verified per variant (see `V3-POLISH-CHECKLIST.md`).
