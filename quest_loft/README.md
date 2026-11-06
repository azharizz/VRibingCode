# Quest Loft — Focus Loft (Unity Prototype)

Target: Quest 3 / 3S only (Quest 2 excluded). Virtual-only, no passthrough / no home scan.

## Versions (locked)

- Unity `6000.6.7f1` (Apple Silicon build) + Android Build Support + OpenJDK + Android SDK/NDK
- Meta XR All-in-One SDK `v71` + Interaction SDK + Voice SDK
- OpenXR Plugin `1.13+` + XR Management
- Photon Fusion `2.x`
- XR Simulator `71.0.0` + Meta Quest Developer Hub `6.5.2` for Mac
- macOS 14+ on M1/M2/M3/M4, 16 GB RAM min, 30 GB free

## Simulator workflow (no headset, Mac Apple Silicon)

1. Open `quest_loft/` in Unity 6000.6.7f1. Window → XR Simulator → Quest 3/3S profile.
2. Mouse = controller ray (point/click = select agent/board). Keyboard = head move. PC mic = Quest mic test.
3. Play `Assets/Scenes/FOCUS-LOFT-SPEC.md` layout: 1 curved wall, 3 desks, 4 boards.
4. No Quest Link on macOS — cable preview is NOT supported. Simulator covers ~80% of logic.

## On-device workflow (1 weekend with borrowed Quest 3/3S)

```bash
# Build APK in Unity (Android ARM64, IL2CPP), then:
adb install -r FocusLoft.apk
adb logcat -s Unity
```

## Standup flow (<5 min, Simulator accept gate)

1. `WakeAll` — 3 capsule agents blink awake on desks (Scout / Builder / Keeper).
2. `AssignTask("builder", "run landing test")` — Builder glows/types, wall Log updates `PASS 12/12`.
3. `MoveCard("kanban", "card-id", "Done")` — or voice "move that to Done" / button fallback.
4. Agent `SetState(Done)` → stretch/check pop + board shows Done. Zero console errors.
