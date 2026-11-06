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

## Round 2 (stopped early: quota)
DONE in source: IPointerTarget (XR/LoftInput.cs) routed first in LoftInteraction.Point; LoftClient is partial; LoftMove (teleport arc + marker, clamp to WalkRect, G on desktop, smooth walk off by default, left stick); LoftBridge (reflection for LoftWorld.WalkBounds, AgentSkin.Say, LoftPanels, with fallbacks); XrInput: isTracked check, stick aim/release, left stick; ExecuteIntent chat + board; say + suggestion events in LoftDesk; suggestion card (Accept by trigger only, A defers it); wrist Boards page + walk toggle; LoftClient.Chat/AnswerSuggestion; old TelePad pads removed; LoftVerify extended (~45 new checks).
VERIFIED: one scratch run before the last test tweak: 168 pass, 1 fail (a bad test, since fixed in source, not re-run). Process exit code was 139 after the summary (Unity teardown), cause unknown.
NOT DONE: Play-mode smoke run; re-run of LoftVerify after the last edit; the last compile was blocked by loft-world AgentSkin.cs(367) GetInstanceID error (not mine).
NEXT: sync.sh, run LoftVerify, then a Play-mode smoke; swap LoftBridge reflection for direct calls once WalkBounds / AgentSkin.Say / LoftPanels exist (IL2CPP stripping risk).
