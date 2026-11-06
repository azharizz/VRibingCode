# QA-REPORT — VIRTUAL LOFT Final Gate (virtual-only HQ, no headset)

**Date (UTC):** 2026-11-02 | **Tester:** QA-TESTER | **Scope:** Quest 3/3S virtual-only HQ, Unity 6000.6.7f1 + Meta XR v71 + Simulator, no headset
**Builders finished:** 4 | **Note:** `team_task_*` tools do NOT exist in this sandbox (verified by siblings) — not attempted; lead handles the board.
**This report is the ONLY file written. No other file was edited.**

## R2 addendum (lead-verified, same day — everything doable without a headset)

- **Unity project opens + compiles clean:** `quest_loft/` upgraded to a real
  6000.6.7f1 project (`ProjectSettings/ProjectVersion.txt`). Headless
  `Unity -batchmode -nographics -quit` → `Exiting batchmode successfully now!`,
  return 0, **0× `error CS`** (`Tools/unity-batch-offline3.log`). License resolved.
- **Two real bugs found & fixed by the compiler gate:**
  (1) `BoardManager.cs` used `JsonUtility` (engine JSONSerialize module, absent in
  stripped contexts) → switched to Newtonsoft (`com.unity.nuget.newtonsoft-json`).
  (2) Pinned `com.unity.inputsystem@1.11.0` Editor code hits `[Obsolete(error)]`
  TreeView APIs in 6000.6 → pin removed from both manifests (nothing uses it;
  Meta Interaction SDK brings input transitively). See `Tools/PACKAGES-SETUP.md`.
- **V2 kit:** `Assets/Tests/StandupFlowTest.cs` (15-check Play-Mode self-test,
  Simulator-compatible, compiles in the clean run) + `Tools/V2-TEST-PLAN.md`
  (2-fake-user procedure, allow/deny matrix, exit criteria).
- **V3:** Night Sprint + Calm Meadow variants (`LOFT-VARIANTS.md`,
  `LoftLayout_Night.json` / `LoftLayout_Meadow.json`, JSON-valid, byte-identical
  boards/agents), `Audio/SPATIAL-AUDIO-SPEC.md`, `V3-POLISH-CHECKLIST.md` (12 boxes).
- **Interviews:** `research/interviews/` — OUTREACH.md (EN+ID DMs), SCRIPT.md
  (30-min bilingual), TRACKER.csv, SYNTHESIS-TEMPLATE.md (3/5 would-try gate).
- **Backend still green:** `npm test` EXIT 0 (travel-reload v2 + 3/3 routes).
- **Two-manifest strategy** (`Tools/PACKAGES-SETUP.md`): Unity-only manifest active
  (opens offline); `Tools/manifest.with-meta-photon.json` for normal networks
  (`packages.meta.com` is DNS-blocked in this sandbox only).
- **Still needs a real human / real Quest:** 5 interviews, 2-fake-user live run,
  borrowed-Quest weekend (72fps, wear, mic, legibility), store checklist.
  Nothing else from PRODUCT.md / TASK.md is left undone.

## How this was verified (real checks, all run 2026-11-02)

| Check | Command / method | Result |
|---|---|---|
| JSON validity (all new `*.json`) | `python3 -m json.tool` on `quest_loft/Assets/Agents/agent-configs.json`, `quest_loft/Assets/Boards/LoftLayout.json`, `quest_loft/Packages/manifest.json`, `backend/package.json`, `backend/package-lock.json` | **ALL VALID** |
| Shell syntax | `bash -n quest_loft/Tools/adb-install.sh` | **OK** |
| Backend smoke (offline) | `cd backend && npm test` | **EXIT 0 — PASS travel-reload + 3 agent routes** (see §Travel-test verdict) |
| JS syntax | `node --check backend/src/agents.js backend/src/loftStore.js backend/test/demo.js` | **ALL OK** |
| C# brace balance | brace count `{` vs `}` per file | **ALL BALANCED** — Multiplayer 31/31, VoiceCommands 53/53, Comfort 15/15, AgentManager 12/12, AgentStateMachine 4/4, BoardManager 18/18 |
| `web_playground/` purity | `ls -1 web_playground/` | **ONLY `PRODUCT.md` + `TASK.md`** ✓ |
| Hardcoded secrets | `grep -rEn 'sk-ant-\|ghp_\|password(=…)\|JWT eyJ…'` over `*.js/*.json/*.cs/*.md/*.sh/*.sql`, excluding `node_modules` + `package-lock` | **NONE FOUND**. `backend/src/agents.js:229` + `backend/src/loftStore.js:55-56` read keys **only** via `process.env`; `backend/.env.example` values are **all empty**; `.env` never printed (per instructions; there are none committed — `.gitignore` covers `.env`) |

---

## 1. Grading — all 12 `TASK.md` §6 items (individual PASS/FAIL with evidence)

> Line refs are 1-based as read at gate time.

| # | TASK §6 item | Verdict | Evidence (file + line refs) |
|---|---|---|---|
| 1 | Core loop in 1 sentence (point agent → speak → wall updates)? | **PASS** | `web_playground/PRODUCT.md` §2 (30s twist: "point at Coder and say 'run landing test' → wall board updates `PASS 12/12`"); `web_playground/TASK.md` §2 P0 accept ("wake 3 agents → run 1 task → move card to Done"); `quest_loft/README.md:30-34` standup flow (`WakeAll` → `AssignTask("builder","run landing test")` → `MoveCard` → `SetState(Done)`); wired in code: `quest_loft/Assets/Agents/AgentManager.cs:32-38` (`AssignTask` → `Working`), `quest_loft/Assets/Boards/BoardManager.cs:48-50` (`VoiceMove`), `quest_loft/Assets/XR/VoiceCommands.cs:161-178` (voice↔button same event) |
| 2 | Virtual-only confirmed (no home scan, no camera API)? | **PASS** | `web_playground/PRODUCT.md` §1 (decision table: no rescan, no camera) + §6 ("no camera scan"); `web_playground/TASK.md` §1 ("virtual-only avoids camera permission") + §7; `quest_loft/Assets/Scenes/FOCUS-LOFT-SPEC.md:3` ("No Scene Mesh / Spatial Anchors / camera APIs"); `quest_loft/ProjectSettings/BUILD-SETTINGS.md:6` ("No camera permission (virtual-only, no passthrough)"); `quest_loft/README.md:3` ("no passthrough / no home scan"); `manifest.json` has zero camera/MR deps (only XR Interaction/Voice/OpenXR/Fusion); repo-wide grep for `passthrough\|SceneMesh\|SpatialAnchor\|WebCamTexture` hits **only** negation sentences in the two `.md` files above |
| 3 | Travel test defined (same loft on 2 PCs)? | **PASS** | Defined: `web_playground/TASK.md` §2 V1 accept ("close app, open on different PC, same loft/boards/agents"); `backend/README.md:36-50` (save→load script + "same `roomId/boards/agents/version` must come back"); `research/R0-pack.md:103` ("highest `version` wins… last-writer-wins"); implemented: `backend/src/loftStore.js:88-128` (`loadLoft` cloud-first→local fallback, `saveLoft` bumps `version`+`updatedAt`, upserts on `room_id`); executed: `backend/test/demo.js:78-86` asserts boards+agents+version roundtrip — **`npm test` PASS** (see travel verdict) |
| 4 | 3 agents with role + 3 anim states each defined? | **PASS** | `web_playground/PRODUCT.md` §5B table (Scout=Researcher, Builder=Coder/Runner, Keeper=Organizer × idle/working/done); `research/R0-pack.md:44-50` (desks/colors/why) + §c blob verdict; `quest_loft/Assets/Agents/agent-configs.json:2-27` (3 agents, `anims:{idle,working,done}` each: `scout_sleep/search/present`, `builder_breathe/type/stretch`, `keeper_tidy/move/bell`); `quest_loft/Assets/Agents/AgentStateMachine.cs:4` (`enum AgentState { Idle, Working, Done }`); `backend/src/agents.js:13` (`AGENT_ROLES = ["scout","builder","keeper"]`) + `SYSTEM_PROMPTS` per role (:15-37) |
| 5 | 10 voice commands listed with button fallback? | **PASS** | `research/R0-pack.md:12-24` — 10-row table (Wake/Run/Stop/Move/Summarize/Assign/Remind/Next/Done/Focus, EN+ID+owner+**button fallback each**) + L25 (push-to-talk, Stop⊃Run safety); `quest_loft/Assets/XR/VoiceCommands.cs:24-36` (enum 10, order canonical), `:66-98` (10 `VoiceCommandPhrases` with EN+ID arrays), `:161-178` (`InvokeCommand` = **every** voice has Quick-Menu/trigger-click path), `:62-63,105-111,129-136` (push-to-talk gate, ignored unless held); `quest_loft/Assets/Scenes/FOCUS-LOFT-SPEC.md:7-8` (10 commands + "every voice command has a button fallback"); cross-file match confirmed (see §4 cross-file) |
| 6 | 4 board types with font/contrast rule? | **PASS** | Types: `web_playground/PRODUCT.md` §5D (Notes / Checkboard ToDo-Doing-Done / Calendar strip / Agent output log); `quest_loft/Assets/Scenes/FOCUS-LOFT-SPEC.md:5` (Notes/Kanban/Calendar/Log + positions); `quest_loft/Assets/Boards/LoftLayout.json:6-9` (4 boards, ids `notes/kanban/calendar/log`); `quest_loft/Assets/Boards/BoardManager.cs:12-16` (same 4 ids); rule: `FOCUS-LOFT-SPEC.md:6`, `quest_loft/Assets/XR/PERF-BUDGET.md:14` ("min 32 sp at 2 m, high contrast"), `quest_loft/ProjectSettings/BUILD-SETTINGS.md:8`, `research/R0-pack.md:37,40` ("min font 32sp @2m, high-contrast") — stated in **4 independent files** |
| 7 | Cloud schema fields listed (teams/lofts/boards/tasks)? | **PASS** (with minor prose-vs-SQL naming note, non-blocking) | Listed: `research/R0-pack.md:102-106` (all 5 tables + fields + travel rule + RLS note); implemented: `backend/supabase/schema.sql` — `teams` (:9-14), `lofts` (:19-27, `room_id UNIQUE`, `layout jsonb`, `version`), `boards` (:32-45), `tasks` (:49-59), `agent_runs` (:64-74), indexes + prototype-permissive RLS (:78-88); used: `backend/src/loftStore.js:67-84` (`toRow`/`fromRow`: `room_id/layout/version/updated_at` — all exist in `lofts` ✓; queries only `lofts`, `:92-95,119-121`); `backend/test/demo.js` roundtrips through it. **Note (not a fail):** R0 prose names (`content_json`, `due_at`/`moved_by`, `agent_runs.command/result_summary/triggered_by`) differ slightly from SQL (`content`, no `due_at`, `agent_runs.agent_role/input/output`) — purpose and table set match; normalize names at V1 implementation |
| 8 | Simulator workflow works without headset? | **PASS** (docs + config; Unity Editor click-through itself not runnable in this Linux-less/mac-less QA sandbox — by design, Simulator covers it) | `web_playground/TASK.md` §1 (Mac Apple Silicon reqs, Simulator = mouse-ray/keyboard-head/PC-mic, "Simulator covers 80%"); `research/R0-pack.md:110-121` (install-check table, all ✅ on the builders' Mac: M2/arm64, Unity 6000.6.7f1 + AndroidPlayer, Simulator 71.0.0, Hub 6.5.2, adb 37 — disk 26 GB vs 30 GB flagged as watch-item only); `quest_loft/Tools/simulator-guide.md` (full: §1 Simulator-first loop + `<5 min zero-console-error` gate, §2 two-fake-user test, §3 `adb install` path, §5 CAN/CANNOT split); `quest_loft/README.md:15-19` (Simulator steps); `quest_loft/Packages/manifest.json:3-10` (XR All-in-One + Interaction + Voice 71.0.0, OpenXR 1.13.0, Fusion 2.2.6); `VoiceCommands.testMode=true` default (`VoiceCommands.cs:60-61`) + `Comfort.cs` seated/teleport defaults make Simulator meaningful |
| 9 | Multiplayer roles + voice rules defined? | **PASS** (rules implemented as enforceable stubs; Fusion netcode wiring deferred to V2 by plan) | Roles: `web_playground/PRODUCT.md` §6 ("CEO can assign, member can move own… Activity log"); `quest_loft/Assets/XR/Multiplayer.cs:43-46,85-119` (`CanAssign` = CEO-only, `CanMove` = CEO-or-owner, `Assign`/`MoveCard` deny + warn otherwise, `Log` caps at 256, room `2-6`, `ROOM_PREFIX="loft-"`); voice rules: push-to-talk + mute-by-zone in `PRODUCT.md` §5C/§6, `VoiceCommands.cs` gate, `simulator-guide.md:20-21,26-34` (T-key PTT, 2-fake-user CEO/Member-1 test incl. **negative** tests: member move-other denied, member assign denied); `FOCUS-LOFT-SPEC.md:10` (2–6 users, shared rays/avatars/live edits) |
| 10 | Comfort rules (teleport/seated/vignette/15–30 min)? | **PASS** | Locked in one place: `quest_loft/Assets/XR/Comfort.cs:19-29` (`SMOOTH_LOCOMOTION_ALLOWED=false`, `SEATED_DEFAULT=true`, eye 1.2 m seated / 1.6 m opt-in, vignette 0.45 fade 0.35 s, session 15–30 min, autosave 30 s, resume 3 s) + enforcement (`OnValidate` + `Awake` force teleport-only/seated, `Update` break nudge + autosave tick, `BeginTeleport/EndTeleport` vignette); mirrored in `FOCUS-LOFT-SPEC.md:9`, `BUILD-SETTINGS.md:8`, `TASK.md` §4 risk #4, `R0-pack.md` survey Q2 |
| 11 | Perf budget (agents/boards/poly/fps)? | **PASS** (as budget doc + caps; sustained-72 fps *truth* needs device — correctly deferred, see DEFER) | `quest_loft/Assets/XR/PERF-BUDGET.md:7-16` hard caps (1 room / 3 agents / ≤12 boards / 32 sp / 8 pooled voices / **72 fps sustained, no heat warning**) + asset rules (low-poly, ASTC ≤1024, 1 mixed dir + baked, blob anims, shared UI material, pooled cards) + code rules (no per-frame allocs, Fusion ≤20 Hz, hover local-only) + cut order + pinned toolchain; `BUILD-SETTINGS.md:5,7` (ARM64/IL2CPP, ASTC, 72 fps); `FOCUS-LOFT-SPEC.md:9` (72 fps target) |
| 12 | Non-goals explicit (no MR, no camera vision, no 8h)? | **PASS** | `web_playground/PRODUCT.md` §8 OUT list (passthrough/MR home mode, real-room anchors, raw camera AI vision, full keyboard typing, human-like avatars, 8-hour support, Store IAP/entitlement, admin analytics, companion app, NPC autonomy); `web_playground/TASK.md` §1 ("Do NOT build yet" list) + §7; `PERF-BUDGET.md:4` + `BUILD-SETTINGS.md:4` (Quest 2 excluded); verified in code: zero camera/MR APIs (grep §Evidence, only negations); `Comfort.cs`/`PRODUCT.md` §4 cap sessions at 15–30 min (anti-8h by design) |

**Score: 12/12 PASS** (item 7 carries a non-blocking naming-harmonization note).

---

## 2. Verdicts

### Simulator workflow verdict — PASS (with stated sandbox limit)
Docs form a complete no-headset loop: Simulator-first iteration (`simulator-guide.md` §1), two-fake-user multiplayer test with negative cases (§2), rare `adb install` path (§3), Hub/limits (§4–5). Config pins match (`manifest.json`, R0 §f, `BUILD-SETTINGS.md`). `VoiceCommands.testMode=true` default and `Comfort` safe defaults make Simulator runs meaningful. **Limit:** this QA sandbox has no Unity Editor, so the `<5 min zero-console-error` click-through was validated at the docs/config/code level only — the V2-device weekend still owns the live run (correct per plan).

### Travel-test verdict — PASS (`cd backend && npm test`, EXIT 0)
```
layout source: …/quest_loft/Assets/Boards/LoftLayout.json
PASS travel-reload roomId=focus-loft version=2 boards=4
PASS scout: web_search ok
PASS builder: run_tests ok
PASS keeper: move_card ok
SMOKE OK — 1 travel-reload + 3 agent routes, no network used.
```
Roundtrip used the **real** `LoftLayout.json` (not the inline sample), save→reload preserved boards+agents+version, and all 3 offline agent routes returned `ok`. No network touched (local-fallback path), matching the kos→cafe offline requirement.

### Readability / contrast verdict (32sp rule) — PASS (rule), on-device proof DEFERRED
Rule is locked in 4+ files (FOCUS-LOFT-SPEC, PERF-BUDGET, BUILD-SETTINGS, R0-pack) with the correct rationale (Quest blurs small text). Board layout keeps the primary Kanban largest and centered. **Truth at 2 m** (real blur, real contrast, FOV crop) needs the borrowed Quest — listed in DEFER.

### Comfort verdict — PASS (policy), wear truth DEFERRED
Teleport-only enforced in code (`OnValidate`+`Awake`), seated default 1.2 m, vignette 0.45/0.35 s, 30-min break nudge, 30-s autosave — all match PRODUCT/TASK. No smooth-locomotion path exists. **30-min nausea-free wear** needs a human on device — DEFER.

### Perf budget verdict — PASS (budget), sustained-72 fps truth DEFERRED
Caps + asset/code rules + cut order + toolchain pins are complete and internally consistent (Quest 3/3S only, Quest 2 explicitly out). Standalone sustained **72 fps with no heat warning in a 30-min session** cannot be proven on PC silicon — DEFER to V2 weekend with `adb logcat`/Hub graphs.

### Non-goals confirmation — CONFIRMED, no violations
All OUT items from `PRODUCT.md` §8 / `TASK.md` §1+§7 are absent from code and config: no passthrough, no anchors/Scene Mesh, no camera vision, no keyboard-typing system, no human avatars, no 8-h support, no IAP/entitlement, no analytics, no companion app, no autonomous NPCs. Multiplayer/Fusion code is rules+stubs only, matching the V2 plan.

---

## 3. Cross-file consistency (independently re-checked)

| Pair | Check | Result |
|---|---|---|
| 10 voice commands: `research/R0-pack.md` §a ↔ `quest_loft/Assets/XR/VoiceCommands.cs` ↔ `quest_loft/Assets/Scenes/FOCUS-LOFT-SPEC.md:8` | Intent set equality (Wake/Run/Stop/Move-that/Summarize/Assign/Remind/Next/Done/Focus-mode) + EN+ID coverage + fallback | **MATCH 10/10.** R0 table rows 1–10 incl. ID phrases ("Bangun…", "Jalankan…", "Berhenti", "Pindah itu…", "Rangkum…", "Tugaskan…", "Ingatkan…", "Lanjut", "Tandai selesai", "Mode fokus") are all covered by `VoiceCommands.cs:68-98` EN+ID arrays (ID wording normalized for substring matching, e.g. `jalankan`, `pindah`, `rangkum/ringkas`, `selesai/beres` — same intents); FOCUS-LOFT-SPEC lists the same 10 |
| Agent ids/colors: `agent-configs.json` ↔ `LoftLayout.json` | id + desk binding (+ colors) | **MATCH 3/3.** `scout@desk-1 #4FC3F7` / `builder@desk-2 #9CCC65` / `keeper@desk-3 #FFB74D`; layout desks `desk-1/2/3` with matching positions; roles align with `PRODUCT.md` §5B (Researcher/Coder-Runner/Organizer) and `agents.js` roles |
| Supabase schema ↔ `loftStore.js` queries | Tables + columns actually queried | **MATCH on the read/write path.** `lofts(room_id UNIQUE, layout, version, updated_at)` ✓ for `select("room_id, layout, version, updated_at")` + `upsert(…, onConflict:"room_id")`. `boards/tasks/agent_runs` tables exist for the V1 board-query/log path (boards live inside `lofts.layout` until then — by design). Minor prose↔SQL field-name drift noted in item 7; no query references a missing column |

---

## 4. DEFER list — physical-Quest-only truths (need the 1-weekend borrowed Quest 3/3S)

Per `TASK.md` §1 Mac limits + `simulator-guide.md` §5, the following **cannot** be proven without hardware and are **explicitly out of gate scope**:

1. **72 fps sustained** on standalone Quest 3/3S in a 30-min session (no heat warning, no throttling) — Simulator/PC silicon proves nothing; measure with Hub perf graphs + `adb logcat` on device.
2. **Motion sickness / comfort truth** — 30-min seated wear, teleport vignette feel, eye strain; need ≥3 real wearers, 0 severe nausea (per Metrics §5).
3. **Noisy-room mic >80%** — PTT + Wit.ai accuracy with kos fan / cafe noise / kids; quiet-room ≥70% (PC mic) and >80% (Quest quiet room) first, then noise.
4. **Horizon Store checklist readiness** — VRCs, comfort/privacy disclosures, icons/review build, `Unknown Sources` → store-track install; virtual-only avoids the camera permission but the checklist itself needs a device + submission pass.
5. Also device-bound (same weekend, same borrowed unit): **2 m board legibility truth** (real blur/FOV), **battery drain per 15–30 min burst**, **APK install via `Tools/adb-install.sh` on real hardware** (script syntax OK, device path untested here), **2-headset shared-loft session** (no "where is checkboard?" confusion with 3 real users).

**Nothing in DEFER blocks the R0→P0→V1 Simulator plan.** All Simulator-provable work is gated above and passes.

---

## 5. Return: PASS/FAIL per item + defer

- **Items 1–12: ALL PASS (12/12).** Item 7 has a non-blocking note: harmonize R0-prose field names with `schema.sql` at V1 implementation (`content` vs `content_json`, task/agent_run field sets).
- **Simulator workflow:** PASS (docs/config/code; live Editor run owns to builders' Mac + V2 weekend).
- **Travel test (`backend npm test`):** PASS — EXIT 0, travel-reload + 3 agent routes, offline, real layout file.
- **Readability (32sp):** PASS as rule; device proof DEFERRED.
- **Comfort:** PASS as policy; wear truth DEFERRED.
- **Perf budget:** PASS as budget; 72 fps truth DEFERRED.
- **Non-goals:** CONFIRMED, no violations in code/config.
- **DEFER (physical-Quest-only):** 72 fps sustained · motion sickness · noisy-room mic >80% · store checklist (+ 2 m legibility, battery, real `adb install`, 3-user device session) — all need the 1-weekend borrowed Quest 3/3S.
- **Files touched:** `QA-REPORT.md` only. **Secrets:** none found, none printed.

*End of QA-REPORT.*
