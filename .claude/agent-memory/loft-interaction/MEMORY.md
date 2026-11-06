# loft-interaction notes

## Done (source of truth quest_loft/Assets, no commits)
- XR/: LoftInput (XrInput/DesktopInput/FakeInput, SnapGate), LoftInteraction (pointer, hover ring, cursor dot, haptics, hold buttons, talk flow, HandleResponse), LoftVoice (Microphone 16k mono WAV, permission, tap/silence rules), LoftCard (voice card), LoftWrist (wrist menu), LoftUi (LoftButton kit), LoftIntent (route rules), LoftTextEntry (keyboard backup), Comfort.SnapTurn.
- Deleted LoftQuestPanel.cs and VoiceCommands.cs (matcher was voice-only approve/stop = unsafe, no STT fed it).
- LoftClient.Voice/VoiceText/ThenAny; LoftDesk.ExecuteIntent/SetScene/SetProject, F1 debug strip only.
- Editor/LoftAndroidSetup.cs: manifest post-process (RECORD_AUDIO, usesCleartextTraffic) + player settings.
- LoftVerify updated: fake HttpListener backend, 125 pass.

## Rules I enforce
- Risky (approve/remove/reject/stop-all) never run from voice; need red card button hold or wrist hold. Non-harmless actions always go to card even if backend says needsConfirm=false.

## Verified: LoftVerify in scratch copy (batch). NOT verified: real headset (ray pitch XrInput.aimPitchDegrees, TouchScreenKeyboard on Quest, Android permission popup, manifest in a real Gradle project, Android-only code in LoftVoice never compiled), TMP text look, desktop Play mode with real mic.
- Real backend (PORT=4199) answered /voice/text and accepted our WAV on /voice.

## Failing in verify, not mine: boards (LoftDesk.SetBoard still TextMesh; loft-world owns), party props count, animal prefab checks.

## Lessons
- Scratch copy needs TMP essentials: extract the .unitypackage by hand (AssetDatabase import silently no-ops in batch); sync.sh excludes them.
- Disk is tight: no more Library copies (use cp -cR), no APK.

## Integration lessons (lead, 2026-11-06)
- UI roots face the head with +Z, so local +X is the VIEWER'S LEFT. Lay out rows and meters accordingly. Backing panels go at negative z.
- `DestroyChildren` must detach children: Label finds by name and would reuse an object queued for Destroy (text vanished on every other rebuild).
- JSON null is a JValue, not C# null: use `(x["k"] as JObject)?[...]`.
- Verified live in Editor: pointing, select, card, send, risky hold, wrist, snap turn, scene fade. Real mic NOT verified: blocked by macOS permission for Unity.
- Editor Play has no XR ("Failed to load openxr runtime loader"), so the desktop rig is the test path.
