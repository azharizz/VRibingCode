# XR Simulator Guide — Mac Apple Silicon (no headset)

> Covers all logic work to V1 without owning a headset. Borrow a Quest 3/3S for
> one weekend at V2 for feel / comfort / perf truth (72 fps, 2 m text, mic, nausea).

## 0. Requirements (your machine — yes, it can do V1)

- Apple Silicon M1/M2/M3/M4 (Intel Mac NOT supported for XR Simulator), macOS 14+, 16 GB RAM min, 30 GB free
- Unity **6000 LTS** Apple Silicon build + Android Build Support + OpenJDK + Android SDK/NDK
- Packages: Meta XR All-in-One SDK v207.0 (baseline v71 line) + Interaction SDK +
  Voice SDK v85.0 via Package Manager; Unity OpenXR Plugin 1.13.0+; Photon Fusion 2.1.x
- Meta Quest Developer Hub **6.5.2** for Mac + Standalone XR Simulator
- Target devices: **Quest 3 / 3S only** (Quest 2 deploy from Apple Silicon Mac is broken)

## 1. Mac workflow (Simulator-first, build rarely)

1. Open `quest_loft/` in Unity 6000 (Apple Silicon). Platform stays **PC, Mac & Linux Standalone** while iterating logic.
2. Play the `Loft_Focus` scene with **XR Simulator** active — no headset needed:
   - **Mouse = controller ray.** Hover agent → highlight ring + name tag; click/trigger = select/grab. Hover board → handles.
   - **Keyboard = head.** `WASD` walk-free look / arrow keys turn; hold right-mouse + move = head look. (Locomotion stays teleport-only on device; Simulator head-keys are for inspection only.)
   - **PC mic = Quest mic.** Set `VoiceCommands.testMode = true`; hold the mapped push-to-talk key (default: hold **T** or right-trigger-click in Simulator) while speaking. Say `run landing test`, `move that to done`, `selesai`.
3. Iterate agents/boards/voice/cloud-save in Simulator until the standup flow passes:
   wake 3 agents → run 1 task → move card to Done, **< 5 min, zero console errors**.
4. Only then switch to Android + build APK (step 3). Simulator covers ~80% — avoid constant builds.

## 2. Two-fake-user test (multiplayer without headsets)

1. Duplicate the project play session: open the scene twice via **ParrelSync clone** (or two Editor instances), both with `Multiplayer.roomName = loft-focus-01`.
2. Client A joins as `CEO`, client B as `Member-1` (`simulatedUsers = ["CEO", "Member-1"]`).
3. On A (CEO): call `Assign("CEO", "agent:builder", "Member-1")` → must succeed.
4. On B (member): `MoveCard("Member-1", "launch-copy", "Member-1", "Doing", "Done")` → must succeed + activity entry `Member-1 move card:launch-copy (Doing -> Done)` appears on **both** clients' logs.
5. Negative test: B tries to move A's card → **denied**; B tries `Assign` → **denied** (CEO only).
6. Later with real Fusion netcode: same steps over Shared-mode room, verify remote rays render in team colors and hover stays local (no RPC spam).

## 3. Build APK + install (Mac → Quest 3/3S)

1. Unity → Switch Platform to **Android**, Texture Compression **ASTC**, Target Device Quest 3/3S, minimum API per Meta XR SDK v71 requirements, scripting backend IL2CPP + ARM64.
2. `Build Settings → Build` → `loft-focus.apk`.
3. Install over USB or Wi-Fi with the provided script (no Quest Link cable preview on macOS — Link is not supported, always build + `adb install`):

```bash
cd quest_loft/Tools
chmod +x adb-install.sh
./adb-install.sh ../Build/loft-focus.apk
```

4. Put headset on → open Unknown Sources / your app → run the V2 weekend checklist: 30-min wear, read board at 2 m, voice > 80% quiet room, 72 fps no heat warning.

## 4. Developer Hub (Mac)

- Use **Meta Quest Developer Hub 6.5.2 for Mac** for: device connection over USB/Wi-Fi (`adb devices` must list the headset), log capture (`adb logcat`), screenshots/video, performance graphs, and managing builds.
- Enable **Developer Mode** on the headset (phone app → headset settings) + accept the USB debugging prompt in-headset on first connect.
- If `adb` is missing: Developer Hub bundles platform-tools, or `brew install android-platform-tools`.

## 5. What Mac CAN vs CANNOT prove

- CAN: R0 research, P0 loft toy, V1 agents/boards/voice logic, cloud save, 2-fake-user multiplayer in Simulator — all without headset.
- CANNOT: 72 fps perf truth, text readability at 2 m, motion sickness, noisy-room mic, store upload — needs 1 borrowed Quest 3/3S for 1 weekend at V2.
