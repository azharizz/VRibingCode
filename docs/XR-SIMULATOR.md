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

Not verified yet: trigger, A, B, teleport, board drag and resize through the VR path. Mouse clicks do not act as the trigger.

Careful: "Enable hand tracking" turns on the webcam. Leave it off unless you want that.
