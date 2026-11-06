# XR Simulator status: working through a Mac build

The Unity Editor still cannot start it (`Failed to load openxr runtime loader`), but a Mac player build connects to the Meta XR Simulator app.

How to run it:
1. Open MetaXRSimulator and keep the runtime toggle on.
2. Build: `Unity -batchmode -projectPath <copy> -buildTarget StandaloneOSX -executeMethod LoftBuilder.BuildMac`. Never run this on a project the Editor has open.
3. Start `Builds/Loft.app`. It appears inside the Simulator as a Quest 3.

`LoftBuilder` copies `ARM64/libopenxr_loader.dylib` to `PlugIns/openxr_loader.dylib`: that is the name OpenXR looks for.

Simulator controls (`~/Library/Application Support/MetaXR/MetaXrSimulator/keybindings.json`): T trigger, B the A/X button, N the B/Y button, Y H G J the stick, W A S D move. Turn on Point & click so the mouse aims the controller.

Verified in the Simulator: rendering, session start, saved board layout, backend connection, controller tracking, wrist menu opening by itself, laser hitting boards, agents walking.

Found and fixed here:
- The laser used the grip pose, about 60 degrees too high. It now uses `PointerPosition` and `PointerRotation`.
- Release builds crashed on launch: `level0` could not be read. The scene no longer saves generated UI.

Not verified yet: trigger, A, B and stick. Keys did not reach the app in the automated test, so board drag, resize and teleport through the VR path are still open. Mouse clicks do not act as the trigger.

Careful: "Enable hand tracking" turns on the webcam. Leave it off unless you want that.
