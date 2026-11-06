# Packages setup — quest_loft (Unity 6000.6.7f1)

## Current state (opens offline, compiles with zero errors)

`Packages/manifest.json` contains **Unity-registry packages only**
(`textmeshpro`, `newtonsoft-json`). Verified 2026-11-02 on this Mac:

- `Unity -batchmode -nographics -quit` → `Exiting batchmode successfully now!`
  (return 0, 0× `error CS`) — see `Tools/unity-batch-offline3.log`.
- Our C# uses only `UnityEngine` / `System` (+ Newtonsoft for board JSON),
  so it compiles without any Meta/Photon package present.

## Full XR version (normal network required)

`Tools/manifest.with-meta-photon.json` is the production manifest:

- Meta XR All-in-One 71.0.0 + Interaction 71.0.0 + Voice 71.0.0
  (matches installed XR Simulator 71.0.0 — keep these three in lockstep)
- OpenXR 1.13.0, XR Management 4.5.0, Photon Fusion 2.2.6

To switch (on a network where `packages.meta.com` resolves):

```bash
cd quest_loft
cp Packages/manifest.json Tools/manifest.unity-only.json
cp Tools/manifest.with-meta-photon.json Packages/manifest.json
# reopen Unity, let Package Manager resolve, then Play with XR Simulator
```

## Known findings (sandbox-verified)

1. **Do NOT pin `com.unity.inputsystem`.** The registry's 1.11.0 Editor code uses
   `TreeView` APIs marked `[Obsolete(error:true)]` in 6000.6 → hard CS0619
   compile errors (see `Tools/unity-batch-offline.log`). Our code never uses it;
   Meta Interaction SDK brings its own input handling transitively.
2. **Use Newtonsoft, not `JsonUtility`, for board JSON** (`BoardManager.cs`).
   `JsonUtility` lives in the JSONSerialize engine module which is not guaranteed
   in stripped/batch contexts; Newtonsoft is an explicit package dependency.
3. This sandbox DNS-blocks `packages.meta.com` / `packages.photonengine.com`
   (Unity + npm registries work). Meta/Photon fetch was **not** executable here —
   run the switch above on your normal network before the Simulator weekend.
