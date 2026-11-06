# VIRTUAL LOFT — Research + Build Plan
### TASK.md — Phases, Acceptance, Risks (No Headset First)
**Folder:** `web_playground/` | **Tech:** Unity 6 + Meta XR SDK + Interaction SDK + Voice SDK + XR Simulator (PC/Mac, no headset) | **Companions:** `PRODUCT.md` (vision)
**Rule:** `web_playground/` contains ONLY `PRODUCT.md` + `TASK.md`. No other .md, no html/css/js until Unity prototype approved.

> How we go from zero headset to validated virtual team HQ, with research gates at every phase. Research first, code second.

---

## 1. Tech Decision (locked for research phase)

- **Stack:** Unity 6 LTS + Meta XR All-in-One SDK (Quest) + Meta Interaction SDK (ray/grab) + Meta Voice SDK (commands) + Photon Fusion (multiplayer) + Supabase (loft layout + boards cloud save).
- **Why Unity not WebXR/Unreal:** most Quest docs + Building Blocks + XR Simulator support Unity first. Easiest hiring in ID. Unreal overkill for stylized loft.
- **Why virtual-only helps without Quest:** no Scene Mesh / Depth / Camera API needed. Simulator can fake virtual room 100%. MR would need real room scan and fail without headset.
- **Dev without headset:** Meta Quest Developer Hub + XR Simulator. Mouse = controller ray, keyboard = head move, PC mic = Quest mic. Simulates Quest 3/3S FOV + resolution + 2 fake users.
- **Mac Apple Silicon Requirements (your machine — YES you can do it all to V1):**
  - Chip/OS: Apple Silicon M1/M2/M3/M4 required (Intel Mac NOT supported for XR Simulator), macOS 14+ , 16GB RAM min, 30GB free.
  - Unity: Unity 6000 LTS Apple Silicon build + Android Build Support + OpenJDK + Android SDK/NDK. Use OpenXR Plugin 1.13.0+.
  - Meta tools: Meta XR All-in-One SDK + Interaction SDK + Voice SDK (via Package Manager), Meta Quest Developer Hub 6.5.2 for Mac, Standalone XR Simulator.
  - Target: Quest 3 / 3S ONLY. Quest 2 deploy from Apple Silicon Mac is broken — do not target Quest 2.
  - Limits on Mac: NO Quest Link cable preview (not supported on macOS). Workflow = build APK in Unity → `adb install` via USB/wireless → test. Simulator covers 80% to avoid constant builds.
  - What Mac CAN do: R0 research, P0 loft toy, V1 agents/boards/voice logic, cloud save, 2-fake-user multiplayer in Simulator — all without headset.
  - What Mac CANNOT prove: 72fps perf truth, text readability at 2m, motion sickness, noisy-room mic, store upload — needs 1 borrowed Quest 3/3S for 1 weekend at V2.
- **File layout (later, not now):**
  ```
  quest_loft/ (future Unity project, NOT in web_playground yet)
    Assets/Scenes/Loft_Focus, Loft_Night, Loft_Meadow
    Assets/Agents/Scout, Builder, Keeper (avatar + anim state)
    Assets/Boards/Notes, Kanban, Calendar, Log
    Backend/supabase schema (teams, lofts, boards, tasks)
  ```

**Do NOT build yet:** passthrough/MR mode, real-room anchors, raw camera vision, keyboard typing system, Horizon Store upload, IAP, mobile app, autonomous AI beyond scripted tasks.

---

## 2. Phases & Acceptance Criteria

### R0 — CEO Research (week 1, no code) — "Do traveling leads want this?"
- [ ] Interview 5 target users (founder/lead, works mobile). Q: where do you work? wall size? noise? 10 voice commands you'd say? agent look preference?
- [ ] Competitor map: Workrooms vs Immersed vs Virtual Desktop vs Vision Pro infinite canvas. What they lack (living agents)?
- [ ] Figma / paper loft: 1 curved wall, 3 desks, 4 boards. Test: can stranger point + say "run test" in <30s without tutorial?
- [ ] Price probe: $0 vs $15 vs $30/user/mo? What would make you wear headset 15 min for standup?

**Accept:** 3/5 say "I would try virtual loft if no rescan." Top 10 commands list locked. Agent style picked (blob/robot/human).

### P0 — Virtual Loft Toy (Simulator only, no headset) — "Does pointing feel like managing?"
- [ ] 1 Focus Loft room, 1 curved wall, 3 desks, 3 capsule agents (no art yet)
- [ ] Mouse-ray point + highlight + click = select agent/board
- [ ] 3 boards: Notes, Kanban (ToDo/Doing/Done), Log. Move card via click. Save to local JSON (mock cloud)
- [ ] 3 fake voice commands via buttons + PC mic test: `run / move that / done`

**Accept:** complete standup flow in Simulator <5 min: wake 3 agents → run 1 task → move card to Done. Zero console errors. No headset needed.

### V1 — Living Agents + Boards (Simulator) — "Does work look alive?"
- [ ] 3 agents with 3 states each: idle (breathe/sleep) / working (glow/type) / done (pop/check). State readable from 3m.
- [ ] 4 board types full: Notes + Kanban + Calendar strip + Agent log. Font >=32sp at 2m.
- [ ] 10 voice commands wired (Voice SDK test mode) + button fallback for every voice
- [ ] Cloud save loft JSON to Supabase: same loft loads on second PC (simulates travel: kos → cafe)

**Accept:** travel test passes: close app, open on different PC, same loft/boards/agents positions. Voice success >70% on PC mic in quiet room. Push-to-talk works.

### V2 — Team Multiplayer (Simulator 2 fake users, then 1 weekend with Quest)
- [ ] Photon room: 2-6 users same loft, see each other's rays + avatars + board edits live
- [ ] Roles: CEO assign, member move own. Activity log who moved what
- [ ] Team voice: push-to-talk, mute by zone
- [ ] Borrowed Quest validation (must): 30-min wear, read board at 2m, no nausea, voice >80% quiet room, perf 72fps no heat warning

**Accept:** 3 real users same loft, no confusion "where is checkboard?" No anchor needed because virtual. Simulator passes, Quest weekend passes.

### V3 — Polish for Daily Use (needs Quest)
- [ ] 3 lofts (Focus/Night/Meadow) + light/audio mood switch
- [ ] Agent polish: Mixamo / Ready Player Me anims, spatial audio "done!" ping from agent position
- [ ] Comfort: vignette on move, teleport only (no smooth locomotion), seated default
- [ ] QA checklist pass (see §4), Horizon Store checklist prep

**Accept:** checklist 12/12, 3 strangers complete standup without coaching. Battery drain measured, session target 15-30 min.

---

## 3. Immediate Next Tasks (in order, research only)

1. Write top 10 voice commands list (EN + ID mix) for Scout/Builder/Keeper
2. Sketch loft layout on paper: wall 12m, where are Notes / Kanban / Calendar / Log?
3. Pick agent look: blob vs robot (poll 5 users)
4. Map competitor gap table: Workrooms / Immersed / Vision Pro vs VIRTUAL LOFT
5. Mock Supabase schema: teams, lofts, boards, tasks, agent_runs (no code, just fields)
6. Simulator install check: Unity 6 + XR SDK + XR Simulator runs on your Mac/PC?
7. Recruit 5 interviewees + 3 Quest borrowers for V2 weekend
8. Price + fear survey: sickness? silly look? team won't join?

---

## 4. Top 10 Risks & Mitigations (virtual-only)

1. **No headset to test feel** → Simulator for logic, borrow Quest 3S for 1 weekend for truth (scale/comfort/perf).
2. **Text unreadable in VR** → min 32sp at 2m, high contrast, test on real Quest early V1.
3. **Voice fails with noise** → push-to-talk + button fallback, 10 commands only, quiet-room test first.
4. **Motion sickness** → teleport only, seated, no fast camera, vignette, 15-30 min sessions.
5. **Perf lie (PC fast, Quest weak)** → cap: 1 room, 3 agents, 12 boards, low-poly, pooled audio. Measure on device V2.
6. **Travel sync confusion** → cloud JSON is source of truth, not device. Version + timestamp per board.
7. **Team won't join VR** → design for solo CEO first (agents), multiplayer second. Solo must be useful alone.
8. **Meta copies feature** → moat = agent memory of team workflow (who owns what, templates), not rendering.
9. **Battery 2h limit** → bursts, auto-save every 30s, resume in 3s anywhere.
10. **Store rejection / permission** → virtual-only avoids camera permission. Follow Horizon OS VRCs for comfort + privacy disclosure.

---

## 5. Metrics (measure, don't guess)

- Research: 3/5 want to try, command list locked, agent style picked.
- Simulator: standup flow <5 min, travel reload same loft 100%, voice >70% PC mic.
- Quest weekend: 30-min wear completion >60%, nausea report 0 severe, text read at 2m 100%, voice >80% quiet, 72fps sustained.
- Team: 3 users same loft, task assign→done <10 min, no "where is board?" confusion.
- Retention proxy: would you do tomorrow's standup here again? Yes >50%.

---

## 6. QA Checklist — Docs Must Pass Before Unity Code (12 items)

- [ ] Core loop in 1 sentence (point agent → speak → wall updates)?
- [ ] Virtual-only confirmed (no home scan, no camera API)?
- [ ] Travel test defined (same loft on 2 PCs)?
- [ ] 3 agents with role + 3 anim states each defined?
- [ ] 10 voice commands listed with button fallback?
- [ ] 4 board types with font/contrast rule?
- [ ] Cloud schema fields listed (teams/lofts/boards/tasks)?
- [ ] Simulator workflow works without headset?
- [ ] Multiplayer roles + voice rules defined?
- [ ] Comfort rules (teleport/seated/vignette/15-30 min)?
- [ ] Perf budget (agents/boards/poly/fps)?
- [ ] Non-goals explicit (no MR, no camera vision, no 8h)?

*Self-check at write time: 12/12 pass — see PRODUCT.md §5-§8.*

---

## 7. Done Means

`web_playground/` contains **only** `PRODUCT.md`, `TASK.md`. Next step after R0 research approval: create `quest_loft/` Unity project + P0 loft toy in Simulator. No Unity code until R0 interviews done.
