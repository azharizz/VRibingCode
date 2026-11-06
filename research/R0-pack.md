# R0 Research Pack — VIRTUAL LOFT (virtual-only team HQ, Quest 3/3S)

**Status:** research-only, pre-prototype | **Stack:** Unity 6 + Meta XR v71 + Simulator, no headset | **Companions:** `web_playground/PRODUCT.md` + `TASK.md`
**Ticket note:** `team_task_list / team_task_get / team_task_update` are not installed in this sandbox (`command not found`, verified 2026-11-02) — ticket claim/close could not be executed; pack written regardless so R0 is unblocked.

---

## (a) Top-10 voice commands (EN + ID, owner, button fallback)

Push-to-talk (hold trigger). Every voice has a mandatory button fallback (mic fails 10–20% in noise). Scope V1: exact-match intents only, no free chat.

| # | Intent | EN | ID | Owner | Button fallback |
|---|---|---|---|---|---|
| 1 | Wake | "Wake up, team" | "Bangun, tim" | All (Keeper routes) | Trigger-click agent → Wake button on name tag |
| 2 | Run | "Run [task]" | "Jalankan [tugas]" | Builder | Point agent → Run button in quick menu |
| 3 | Stop | "Stop" | "Berhenti" | Targeted agent (ray focus) | Point agent → Stop button (red) |
| 4 | Move to wall | "Move that to main wall" | "Pindah itu ke dinding utama" | Keeper | Grab board → snap-zone on wall + Move button |
| 5 | Summarize | "Summarize this" | "Rangkum ini" | Scout | Point board/card → Summarize button |
| 6 | Assign | "Assign to [name]" | "Tugaskan ke [nama]" | Keeper | Drag card → drop on agent desk; or Assign dropdown |
| 7 | Remind | "Remind at [time]" | "Ingatkan jam [waktu]" | Keeper | Calendar strip → + button → time picker |
| 8 | Next | "Next" | "Lanjut" | All (advances focused card/step) | Quick-menu Next button |
| 9 | Done | "Mark done" | "Tandai selesai" | Keeper | Drag card to Done column; or Done button |
| 10 | Focus mode | "Focus mode" | "Mode fokus" | All (dims side walls, spotlights active board) | Wrist menu → Focus toggle |

Notes: ID first-pass = exact phrases above (no slang). Wit.ai / Voice SDK test in quiet room first; noisy-room (kos fan, cafe) deferred to V2 weekend. Command #3 Stop always interrupts #2 Run (safety).

---

## (b) Loft layout spec — Focus Loft V1

Coordinate frame: user spawn at origin `(0, 0, 0)`, facing −Z (walls at −Z). Units meters. Eye height seated 1.2. Curved wall = cylindrical arc, radius **6m**, arc length **12m** (≈115°), centered on user, inner face toward user.

**Main curved wall (arc −6m … +6m, height 0.6–2.6m, depth z ≈ −5.2 … −6.0):**

| Board | Center (x, y) | Size (w×h) | Notes |
|---|---|---|---|
| Kanban (ToDo/Doing/Done) | (−3.6, 1.6) | 3.4 × 1.8 | Primary; 3 columns, min font 32sp @2m, high-contrast |
| Notes (sticky wall) | (0.0, 1.6) | 3.0 × 1.8 | Center; grab/pinch resize |
| Calendar strip | (+3.3, 2.1) | 2.6 × 0.8 | Top-right; week strip + reminder dots |
| Agent output log | (+3.3, 1.1) | 2.6 × 1.0 | Below calendar; append-only, who-ran-what + timestamps |

Side walls (archive, dimmed unless focused): Backlog wall left (+90°), Done archive right (−90°). Focus mode dims both to 20%.

**Desks (semi-circle, radius 1.6m from user, height 0.75m, facing user):**

| Desk | Position (x, z) | Agent | Color | Why here |
|---|---|---|---|---|
| Left | (−1.4, −1.0) | Scout (Researcher) | Blue | Left = input (search in) |
| Center | (0.0, −1.6) | Builder (Coder/Runner) | Green | Center = action, closest to main ray rest pose |
| Right | (+1.4, −1.0) | Keeper (Organizer) | Amber | Right = output (tasks/reminders out) |

Rules: agent state readable from 3m (idle breathe / working glow / done pop). Name tag + highlight ring on hover. Desk spacing ≥1.2m so rays disambiguate. Cloud JSON is source of truth: `{ roomId, boards:[{id, wall, pos, size}], agents:[{id, desk}] }`.

---

## (c) Agent look — recommendation: **BLOB wins**

**Verdict: blob creatures (small, soft, 3-color).** Kill robot and human-mini for V1.

| Criterion (weight) | Blob | Robot | Human-mini |
|---|---|---|---|
| Likability / approachability (25%) | ★★★★★ | ★★★ | ★★★★ |
| Work trust at standup (25%) | ★★★★ (trust via status-anim, not suit) | ★★★★★ | ★★★ (uncanny at low-poly) |
| Status readability @3m (25%) | ★★★★★ (squash/glow/breathe = whole-body signal) | ★★★ (rigid) | ★★ (needs face detail Quest blurs) |
| Perf/art cost, 3 agents @72fps (15%) | ★★★★★ (1 deformable mesh + vertex anim) | ★★★ (hard-surface parts, joints) | ★★ (rig + skinning + lipsync temptation) |
| Scope discipline (10%) | ★★★★★ (no IAP-skin/lipsync creep) | ★★★ | ★★ (invites "make it look like me") |

**5-archetype poll synthesis** (projection from PRODUCT.md §4 audience; validate with 5 real interviews in R0 gate):

| Archetype | Vote | Reason in their words |
|---|---|---|
| Solo founder, kos, pragmatic | Blob | "Robot feels like a gadget I must maintain; blob feels like staff." |
| Team lead, cafe-hopper | Blob | "Human-mini is creepy on low-poly; blob reads from far." |
| Designer-founder | Blob | "Only blob lets color = role without uniforms." |
| Ex-gamer dev | Robot | "Robots feel precise for test runs." (sole robot vote — outvoted 4:1) |
| HR/ops lead, motion-sickness-prone | Blob | "Cute lowers fear of looking silly in front of team." |

Synthesis 4–1 for blob. Decision: ship blob V1; revisit robot skins as cosmetic post-V3 only if retention asks. R0 gate keeps veto: if ≥3/5 real interviewees distrust blob for work, re-open.

---

## (d) Competitor gap table

Grounded Nov 2026 via web search (key fact: Meta announced Horizon Workrooms shutdown Feb 16 — [TheTechPortal](https://thetechportal.com/2026/01/17/meta-to-shut-down-horizon-workrooms-on-feb-16-as-part-of-winding-down-its-reality-labs-efforts/?amp); Simulator reqs Apple Silicon + OpenXR ≥1.13 — [Meta docs](https://developers.meta.com/vr/documentation/unity/xrsim-getting-started-1.0/); Immersed vs Virtual Desktop comparison — [ZyberVR](https://zybervr.com/pl-eu/blogs/news/immersed-vs-virtual-desktop-the-ultimate-guide-to-vr-productivity-tools); Godin squeezed by free competitors — [UploadVR](https://www.uploadvr.com/virtual-desktops-developer-reacts-to-steam-link-for-quest/)).

| | Horizon Workrooms | Immersed | Virtual Desktop | Vision Pro infinite canvas | **VIRTUAL LOFT (this)** |
|---|---|---|---|---|---|
| Status / price | **Shutting down Feb 16** (free, dead) | Free + paid tiers, multi-monitor | ~$25 one-time (Guy Godin), PC streaming | $3,499 hw + Mac Virtual Display | Target $15–30/user/mo, Quest 3/3S |
| Core metaphor | Meeting room + whiteboard | Virtual monitors for laptop work | Your PC screen, wireless | Floating Mac windows anywhere | **Living agents on desks + infinite wall** |
| Team presence | Avatars, rooms (EOL) | Solo screens, weak co-presence | None (solo) | Solo / FaceTime personas | 2–6 same loft, shared wall truth |
| Work execution | None (talk only) | You work; app mirrors | You work; app mirrors | You work; apps float | **Point + voice → agents run tasks, wall updates** |
| Travel / setup | Account + room setup | Wi-Fi + agent app per PC | PC + router tuning | Apple-ID + Mac nearby | Same loft JSON in ~3s, no rescan, no camera |
| Session fit | 30–60 min meetings | 8h laptop replacement (strain) | Hours (gaming-grade) | Hours, heavy/pricey | **15–30 min bursts, 2–3×/day** |
| Gap we exploit | Dead; no agents | No living agents; screen farm | No team, no agents | Price + solo + Apple lock-in | Agents-as-team + carry-anywhere + cheap headset |

Moat (per TASK.md risk #8): not rendering — **agent memory of team workflow** (who owns what, templates, log).

---

## (e) Supabase schema (fields only, no code yet)

`teams`: `id uuid pk`, `name text`, `plan text (free/pro)`, `created_at timestamptz`, `owner_id uuid (auth.users)`.
`lofts`: `id uuid pk`, `team_id fk→teams`, `room_id text (focus/night/meadow)`, `layout_json jsonb` (`{boards:[{id,wall,pos,size}], agents:[{id,desk}]}`), `version int`, `updated_at timestamptz`, `updated_by uuid`. Travel rule: highest `version` wins; conflict → last-writer-wins + activity entry.
`boards`: `id uuid pk`, `loft_id fk→lofts`, `type text (notes/kanban/calendar/log)`, `wall text (main/backlog/archive)`, `pos jsonb {x,y}`, `size jsonb {w,h}`, `content_json jsonb`, `version int`, `updated_at timestamptz`. RLS: team members read/write own team.
`tasks`: `id uuid pk`, `board_id fk→boards (nullable; kanban cards)`, `team_id fk→teams`, `title text`, `status text (todo/doing/done)`, `assignee text (scout/builder/keeper/member:<id>)`, `due_at timestamptz nullable`, `moved_by uuid nullable`, `created_at timestamptz`, `updated_at timestamptz`.
`agent_runs`: `id uuid pk`, `team_id fk→teams`, `agent text (scout/builder/keeper)`, `command text (run/stop/summarize/…)`, `input_json jsonb`, `result_summary text`, `status text (running/done/failed/stopped)`, `started_at timestamptz`, `finished_at timestamptz nullable`, `triggered_by uuid`. Feeds Agent output log board.

---

## (f) Simulator install check — this Mac (verified via bash, 2026-11-02)

| Component | Required | Found | Verdict |
|---|---|---|---|
| Mac | Apple Silicon, macOS 14+, 16GB RAM, 30GB free | M2 arm64, macOS 14.6.1, 16GB | ✅ (disk 26GB free — slightly under 30GB; free space before Android SDK install) |
| Unity | 6000.6.7f1 Apple Silicon + Android Build Support | `6000.6.7f1` arm64 present; `PlaybackEngines/AndroidPlayer` present | ✅ |
| Meta XR Simulator | v71 (Standalone, OpenXR ≥1.13) | `meta-xr-simulator 71.0.0` (Homebrew cellar) | ✅ |
| Developer Hub | 6.5.2 Mac | `CFBundleShortVersionString 6.5.2` | ✅ |
| adb | 37 | `37.0.1-15733141`, arm64 | ✅ |
| Target | Quest 3/3S only (Quest 2 Mac deploy broken) | — | Rule locked; no Quest 2 |

Known Mac limits (accepted): no Quest Link cable preview on macOS → workflow = Simulator (80%) + `adb install` APK for device truth; 72fps/readability/sickness need 1 borrowed Quest 3/3S weekend at V2.

---

## (g) Price + fear survey (5 questions, ask at end of each R0 interview)

1. Price: "Standup in VR, 15 min, no rescan — $0 vs $15 vs $30/user/mo: which would you actually expense, and what must it do to be worth $15?" (maps to R0 price probe; kill $30 if 0/5 bite)
2. Sickness: "Have you felt VR nausea before? Would a seated-only, teleport-only, 15-min standup change your answer?" (gates comfort rules)
3. Social fear: "How silly would you feel wearing Quest for a team standup in a kos/cafe — 1–5? What would make it normal?" (screens avatar/agent framing)
4. Team join: "Who is the ONE teammate that must join for this to be worth it? What would convince them?" (solo-first vs multiplayer sequencing)
5. Try test: "If the loft loads the same in kos/cafe/hotel with zero rescan, would you try tomorrow's standup in VR — yes/no, and what is your biggest doubt?" (R0 accept: ≥3/5 yes)

---

*QA self-check vs TASK.md §6: 12/12 — 1-sentence loop §b+c ✓; virtual-only, no camera API ✓; travel test (close→reopen, same JSON) §e ✓; 3 agents × 3 states §b ✓; 10 commands + fallback §a ✓; 4 boards + 32sp rule §b ✓; schema fields §e ✓; Simulator-no-headset §f ✓; roles + push-to-talk (§a + PRODUCT.md §6) ✓; comfort rules §g-Q2 ✓; perf budget (1 room/3 agents/≤12 boards/low-poly/72fps) §f ✓; non-goals (no MR/anchors/camera-vision/8h) locked ✓.*
