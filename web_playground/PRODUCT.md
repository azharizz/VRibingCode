# VIRTUAL LOFT — Team OS with Living Agents
### PRODUCT.md — Vision, Rooms, Agents & Scope
**Folder:** `web_playground/` | **Status:** Research-only, pre-prototype | **Target:** Meta Quest 3 / 3S (Horizon OS), virtual-only, no passthrough home mapping
**Companion:** `TASK.md` (research + build order)

> One-sentence pitch: **Your team lives as living agents in one virtual loft you can carry anywhere — point at an agent, speak to run or edit work, and pin everything to infinite walls that load the same in kos, cafe, or hotel.**

This doc answers *what* we're building and *why virtual-only wins for a traveling CEO*. For *how we validate + build it without a headset first*, see `TASK.md`.

---

## 1. CEO Decision: Why Virtual-Only (Not Hybrid)

CEO call: **no home-camera mapping. Virtual only.**

Reason: if the person needs to go somewhere, MR home setup breaks. Virtual loads the same everywhere.

| If we used camera/home (MR) | If we use virtual only (THIS PRODUCT) |
|---|---|
| Must rescan room every new place (5-10 min) | Same loft loads in ~3s anywhere |
| Small kos wall = only 2 boards fit, layout breaks | Infinite curved wall, always fits 10+ boards |
| Camera permission popup, sees strangers' house | No camera needed, no privacy fear |
| My wall ≠ your wall, team sync confusing | Everyone sees same loft, "wall left" is same for all |
| Light/mirror/glass = tracking fail | No tracking of real room, never fails |

Tradeoff accepted: lose familiar home feeling. Fix with relaxing backgrounds + spatial audio + agents that feel alive.

Reference: Horizon Workrooms + Immersed + Apple Vision Pro infinite canvas all prove virtual rooms win for mobile teams.

---

## 2. Fantasy & Hook

**Start:** you put on Quest in a noisy kos. Noise gone. You are in a warm wooden loft, lo-fi rain. Three small creatures blink awake on desks: Researcher, Coder, Organizer.

**Twist at 30s:** you point at Coder and say "run landing test." Coder glows, types in air, wall board updates: `PASS 12/12`. It stretches happy and says "done."

**Hook for whole product:** agents are not chatbots. They are **living windows**. When idle they breathe/sleep. When working they animate. Where they stand = what they own. Point + voice = you manage like a CEO, not like typing.

---

## 3. Pillars (if a feature doesn't serve these, cut it)

1. **Carry Anywhere** — same office in kos, cafe, hotel. No rescan. Cloud layout, not room anchor.
2. **See Work Living** — no hidden progress. If agent works, you see it move. If stuck, you see it tired.
3. **Point + Speak, Don't Type** — no keyboard in VR. Ray + 10 voice commands cover 90% of organizing.
4. **Infinite Walls, Zero Mess** — notes / checkboard / calendar live on virtual walls that never run out, never get lost.

---

## 4. Audience & Platform

- **Who:** solo founder / small team lead (3-10 people), works from anywhere, Jakarta-style mobile: kos → cafe → coworking. 25-40y, already uses Discord/Notion/ChatGPT, curious about Quest.
- **Session:** 15-30 min bursts, 2-3x/day. Morning standup (15 min), afternoon sprint (30 min). NOT 8-hour laptop replacement (Quest battery ~2h, eye strain).
- **Platform:** Meta Quest 3 / 3S standalone (Horizon OS, Android-based). Unity 6 + Meta XR SDK + Interaction SDK + Voice SDK. Multiplayer via Photon Fusion / Meta Netcode. Backend Supabase/Firebase for loft layout + tasks.
- **Dev constraint now:** no headset owned. Use Meta XR Simulator on PC/Mac for logic. Borrow/buy Quest 3S for 1 weekend for feel/comfort/perf validation only.

---

## 5. Core System (4 parts)

### A. Virtual Rooms (not your house)
- 3 launch lofts: **Focus Loft** (wood + rain), **Night Sprint** (dark + neon), **Calm Meadow** (bright + soft). User picks by mood.
- Infinite wall: 1 main curved wall 12m wide, holds 6-12 boards. Side walls for archive.
- No Scene Mesh, no Spatial Anchors to real room needed. Layout = cloud JSON: `{ roomId, boards:[{id, wall, pos, size}], agents:[{id, desk}] }` loads same everywhere.
- Relaxing benefit: background audio + soft light = lower stress than messy home. Infinite benefit: never limited by physical wall size.

### B. Living Agents (the team)
Each agent = 1 role, 1 desk, 1 color, 1 animation set:

| Agent | Owns | Idle anim | Working anim | Done anim |
|---|---|---|---|---|
| **Scout (Researcher)** | Search, summarize links | Sleeping / blinking | Flying papers, magnifier | Presents card to wall |
| **Builder (Coder/Runner)** | Run tests, edit docs/code | Sitting, breathing | Typing in air, glowing hands | Stretch + green check pop |
| **Keeper (Organizer)** | Tasks, checkboard, reminders | Sweeping / tidying | Moving cards between columns | Bell ring + confetti |

Rule: animation must mean status. Never cute-random. User can tell state from 3m away without reading text.

Cap V1: 3 agents max. More = noise + perf cost.

### C. Point + Voice to Run / Edit (multimodal)
- **Point:** controller ray or hand ray. Hover agent = highlight ring + name tag. Hover board = show handles.
- **Voice (10 commands V1, English + simple Indonesian):** `wake / run [...] / stop / move that to wall / summarize this / assign to [name] / remind at [...] / next / done / focus mode`
- **Fallback mandatory:** every voice has button equivalent (trigger click, quick menu). Mic fails 10-20% with fan/kids/noise.
- **Mic use:** push-to-talk (hold trigger) to avoid always-listening + battery drain. Show mic waveform on wrist.

### D. Wall Displays (notes / checkboard / other)
- **Board types V1:** Notes (sticky wall), Checkboard (ToDo / Doing / Done kanban), Calendar strip, Agent output log.
- **Interaction:** point + grab to move, voice "move that to main wall", pinch to resize. Boards persist in cloud per team, not per device.
- **Readability rule:** min font 32sp at 2m distance, high contrast, no small laptop text. Quest blurs small text.

---

## 6. Mapping, Mic, Organizing — How Without Camera?

- **Mapping:** virtual mapping only. No camera scan. User maps work, not room: zones = Backlog wall / Active wall / Done wall. Saved as team template.
- **Showing:** virtual boards + agent screens. No passthrough needed.
- **Mic:** Quest mic for commands + team voice chat (Photon Voice / Vivox). Push-to-talk + mute by room zone.
- **Organizing team:** same loft instance for all (2-6 users). Avatars + agents share space. Roles: CEO can assign, member can move own cards. Activity log = who moved what.

---

## 7. Success Metrics (research phase, no headset needed)

- 5 interviews: where would you pin notes if wall infinite? What 10 voice commands daily?
- Paper / Figma VR mock: can user point + say command without tutorial in <30s?
- Simulator click-through: complete standup flow (wake 3 agents → run 1 task → move card to Done) in <5 min with mouse-ray.
- Later with borrowed Quest: wear 30 min without nausea, read board at 2m, voice success >80% in quiet room.
- Team test: 3 users in same virtual loft, no confusion about "where is checkboard?"

---

## 8. Scope & Non-Goals (to stay shippable)

**IN V1:** 1 loft, 3 agents, 4 board types, 10 voice commands, point + trigger, cloud save loft, 2-6 multiplayer, push-to-talk team voice.

**OUT (do NOT build yet):** passthrough/MR home mode, real-room anchors, raw camera AI vision, full keyboard typing, human-like AI avatars, 8-hour work support, Horizon Store IAP/entitlement, admin analytics, mobile companion app, NPC autonomy beyond assigned tasks.

---

## 9. Open Research Questions (CEO to answer before code)

1. Agent look: blob creature vs robot vs human mini? Cute = liked but less trusted for work?
2. Voice language: English-only first or ID+EN mixed? (Wit.ai / Voice SDK accuracy?)
3. Where do traveling users actually open Quest? Noise level for mic?
4. Willing to pay $15-30/user/mo vs one-time? vs free Workrooms?
5. Biggest fear: motion sickness, looking silly, or team won't join VR?

*Next: see TASK.md for validation order without headset.*
