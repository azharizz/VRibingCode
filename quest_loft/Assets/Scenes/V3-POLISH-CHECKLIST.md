# V3 POLISH CHECKLIST — Daily-Use Gate (Quest 3/3S, virtual-only)

**Stack:** Unity 6000.6.7f1 + Meta XR v71 · Devices: Quest 3/3S ONLY (no Quest 2) · Simulator-first, truth on 1 borrowed Quest for 1 weekend.
**Parents:** `../XR/COMFORT` (`Comfort.cs` constants) · `../XR/PERF-BUDGET` (72 fps caps) · [FOCUS-LOFT-SPEC.md](FOCUS-LOFT-SPEC.md) · [LOFT-VARIANTS.md](LOFT-VARIANTS.md) · `../Audio/SPATIAL-AUDIO-SPEC.md` · TASK.md §V3 accept: **12/12 pass + 3 strangers complete standup without coaching**.
**How to use:** each item has Procedure → Pass bar → Evidence to record. Check the box only with evidence noted.

---

## A. Comfort re-verify — vignette / teleport / seated (per variant × per loft)

Constants under test (from `Comfort.cs`): teleport-only (`SMOOTH_LOCOMOTION_ALLOWED=false`), seated default 1.2 m, vignette 0.45 strength / 0.35 s fade, 15–30 min session nudge, 30 s autosave, 3 s resume.

- [ ] **A1. Teleport-only enforced (all 3 lofts).** Procedure: in each loft, attempt smooth stick locomotion + snap turn + teleport in Simulator, then on device. Confirm `allowSmoothLocomotion` resets to false if edited (`OnValidate` warning fires). Pass: no smooth movement possible in any loft; teleport arcs + landing markers work seated. Evidence: screen note per loft.
- [ ] **A2. Vignette on move.** Procedure: teleport 5× per loft on device; observe fullscreen darkening ~0.45 that fades in ~0.35 s. Confirm vignette never sticks (lands → fades to 0) and never fires when stationary. Pass: 15/15 teleports show clean fade; 0 stuck-vignette events. Evidence: observer tally.
- [ ] **A3. Seated default + eye height.** Procedure: fresh launch → confirm seated 1.2 m eye height (wall band 0.6–2.6 m comfortably framed, desks at natural reach); opt-in standing 1.6 m via menu, back to seated. Repeat per loft (Meadow brightness must not change perceived height comfort). Pass: seated is default every launch; boards readable without leaning in any loft. Evidence: photo of view per loft (optional) + tester yes/no.
- [ ] **A4. 15–30 min session behavior.** Procedure: run a 30-min timed session on device; confirm break suggestion fires at 30 min (log + gentle UI, never a hard block) and autosave ticks every 30 s (kill app at random minute → relayout restores in ~3 s). Pass: break nudge observed; resume-after-kill restores layout ≤5 s on device. Evidence: timestamps.

## B. Font audit procedure (readability = shippable gate)

Rule: **min 32 sp at 2 m, high contrast, no small laptop text** (Quest blurs small text).

- [ ] **B1. Simulator audit (all boards × all 3 lofts).** Procedure: place headset-camera proxy at 2.0 m from each board (Notes / Kanban / Calendar / Log) in each loft theme (Focus cream, Night dark `#10151F`, Meadow white). Screenshot each; measure smallest glyph ≥32 sp equivalent; compute contrast (target WCAG AA ≥4.5:1 — Night off-white on near-black, Meadow near-black on white). Pass: 12/12 screenshots pass size + contrast. Evidence: 12 screenshots filed.
- [ ] **B2. Device truth (borrowed Quest).** Procedure: tester with normal vision stands/sits at 2 m, reads aloud the smallest line on each board in each loft, quiet room. Pass: 12/12 read correctly first try; 0 squint/lean-in events. Evidence: tester initials + errors count (must be 0).
- [ ] **B3. Dynamic text.** Procedure: append 20 log lines (activity ring cap 256 respected), resize a board via pinch, toggle Focus mode — confirm no text shrinks below 32 sp and shared UI material stays batched. Pass: font floor holds under resize/log spam. Evidence: notes.

## C. Battery / session measurement (borrowed-Quest weekend protocol)

Goal: prove the 15–30 min burst model fits a real device (Quest battery ~2 h; eye strain; heat).

- [ ] **C1. Drain measurement.** Procedure: full charge → airplane-mode-except-cloud (Wi-Fi on for Supabase/Photon), brightness 70 %, run three 30-min standup sessions (Focus → Night → Meadow) with 10-min breaks; record battery % before/after each + device temp feel + any heat warning. Pass: ≤25 % drain per 30 min (≥2 h total runway); zero heat warnings. Evidence: table of 3 sessions.
- [ ] **C2. Frame-rate truth.** Procedure: enable on-device perf overlay (OVR Metrics / Meta Quest Developer Hub) during a full session with 3 agents animating + 4 boards + ambient loop; record sustained fps. Pass: **72 fps sustained**, dips only on teleport crossfade (<300 ms). If it dips, apply PERF-BUDGET cut order (render scale → vignette quality → anim layers → board count → voices) and re-measure. Evidence: fps log snippet.
- [ ] **C3. Session fit.** Procedure: 3 testers × one 15-min standup each (wake 3 agents → run 1 task → move card to Done, per P0 flow). Record nausea (0–3 scale), wear comfort, "would you do tomorrow's standup here again?" Pass: 0 severe nausea; ≥2/3 say yes to tomorrow. Evidence: 3 survey rows.

## D. Horizon Store VRC checklist mapping (virtual-only advantages explicit)

V3 ships store-prep, not store-upload. Map each VRC family to our evidence so submission later is paperwork, not rework. Virtual-only removes the entire camera/room-scan permission family — state that as a designed advantage.

| VRC family | What reviewers check | Our mapping (evidence) | Status |
|---|---|---|---|
| Comfort | No forced smooth locomotion; vignette; seated option; session guidance | A1–A4: teleport-only enforced in code, vignette 0.45/0.35 s, seated 1.2 m default, 30-min break nudge | ☐ pass with A-evidence |
| Motion sickness | Stable horizon, no fast camera, no vection sources | No camera animation except teleport crossfade; agents desk-bound (no walking); 15–30 min bursts | ☐ |
| Text / accessibility | Readable UI at distance, high contrast | B1–B3: 32 sp @2 m, 12/12 screenshots + device reads | ☐ |
| Audio | No sudden loud sounds; voice intelligible | Spatial spec: beds −18/−20 LUFS, chirps ≤−26 LUFS peak, −12 dB duck under PTT, one-shot pings ≤1.2 s | ☐ |
| Performance | Sustained fps, no overheating, clean suspend/resume | C1–C2: 72 fps log, ≤25 %/30 min, 3 s resume, 30 s autosave | ☐ |
| Privacy disclosure (virtual-only) | Declare camera/mic/location data use | **No camera APIs, no Scene Mesh, no passthrough, no room scan — camera permission never requested.** Mic: Quest mic for push-to-talk commands + team voice only (Photon Voice/Vivox), push-to-talk + mute-by-zone; no always-listening. Cloud: Supabase loft JSON (boards/tasks/agent_runs) + Photon room state; list retention in store privacy form. Location: none collected. | ☐ disclosure drafted |
| Permissions | Request minimum; justify each | Requested: mic (PTT voice), network (cloud + multiplayer), storage (autosave cache). NOT requested: camera, spatial data, body tracking. | ☐ manifest verified |
| Age / content | No UGC shock, safe multiplayer | 2–6 known team users per room; no public lobbies; activity log attributes every move; CEO/member roles | ☐ |

- [ ] **D1. Privacy disclosure drafted.** Procedure: write the store privacy answers from the table above (camera: none; mic: PTT-only; cloud fields per R0-pack §e schema). Pass: disclosure text exists and matches the actual manifest (no requested permission unmentioned). Evidence: disclosure paragraph filed.
- [ ] **D2. Manifest vs code audit.** Procedure: grep project for camera/passthrough/SpatialAnchor APIs → expect zero hits; confirm `AndroidManifest` requests only mic/network/storage. Pass: zero forbidden API hits; manifest diff reviewed. Evidence: grep output (0 hits) + manifest snippet.

## E. V3 accept gate (from TASK.md)

- [ ] **E1. 12/12 boxes above checked with evidence.**
- [ ] **E2. 3 strangers complete standup (wake → run → Done) without coaching** in <5 min each, any loft of their choice.
- [ ] **E3. Non-goals still out:** no MR/passthrough, no real-room anchors, no raw camera vision, no keyboard typing system, no 8-hour support claims, no IAP/entitlement code.

*(End of checklist — 12 boxes: A1–A4, B1–B3, C1–C3, D1–D2 mapped + E1–E3 gate.)*
