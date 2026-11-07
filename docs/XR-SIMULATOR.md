# XR Simulator status: working through a Mac build

The Unity Editor still cannot start it (`Failed to load openxr runtime loader`), but a Mac player build connects to the Meta XR Simulator app.

How to run it:
1. Open MetaXRSimulator and keep the runtime toggle on.
2. Build: `Unity -batchmode -projectPath <copy> -buildTarget StandaloneOSX -executeMethod LoftBuilder.BuildMac`. Never run this on a project the Editor has open.
3. Copy `Builds/Loft.app` to `~/Applications` and start it from there. It appears inside the Simulator as a Quest 3.

Do not run it from `Documents`: each new build is a new app to macOS, which holds it at a hidden privacy prompt and it hangs on startup.

`LoftBuilder` copies `ARM64/libopenxr_loader.dylib` to `PlugIns/openxr_loader.dylib`: that is the name OpenXR looks for.

Simulator controls (`~/Library/Application Support/MetaXR/MetaXrSimulator/keybindings.json`): T trigger, B the A/X button, N the B/Y button, Y H G J the stick, W A S D move. Turn on Point & click so the mouse aims the controller.

Verified in the Simulator: walking, looking around, rendering, session start, saved board layout, backend connection, controller tracking, wrist menu opening by itself, laser hitting boards, agents walking.

Found and fixed here:
- The laser used the grip pose, about 60 degrees too high. It now uses `PointerPosition` and `PointerRotation`.
- Release builds crashed on launch: `level0` could not be read. The scene no longer saves generated UI.

Also found here: nothing moved the camera with the headset, so the view was frozen. `XR/LoftHead.cs` now applies the head pose every frame and before render.

Keys only reach the Simulator after clicking inside its view. W A S D walking and arrow-key looking are verified.

Also verified: the trigger (T) clicks web pages, and B (N) leaves 360 video.

Not verified yet: A sending a card, teleport, board drag and resize, pressing the Watch button with the trigger (the voice path uses the same code), and video sound (played, but nobody has listened yet). Mouse clicks do not act as the trigger.

Careful: "Enable hand tracking" turns on the webcam. Leave it off unless you want that.

## Bare hands

Set Left and Right input to **Hand**. Hands draw as white skeletons and the app switches
to hand controls by itself, with no restart. Back to **Controller** works the same way.

| Gesture | Does | In the Simulator |
|---|---|---|
| Right pinch | Click, hold, drag | Left mouse button |
| Left pinch held | Talk | Left mouse button (needs the left hand on) |
| Quick left pinch | Menu | Quick left click |
| Left fist | Cancel | Key 4 (grab pose), left hand |
| Right fist, then open | Teleport | Key 4, then 1 |

"Use same input for both sides" pinches both hands at once, so one click also talks.
Turn it off and set Left to None to test clicking alone. In this mode the hand moves
with the head, so turn the head to aim. Keys 1 to 4 did not change the pose when tried.
Fist gestures are only proven by `LoftVerify`, not live.
