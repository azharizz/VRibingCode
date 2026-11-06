# Build Settings — Quest 3 / 3S Only

- Platform: Android. Architecture: ARM64 only. Scripting: IL2CPP. API: minimum Android 12 (Quest 3/3S Horizon OS).
- Target devices: Quest 3 / 3S ONLY. Quest 2 NOT supported (deploy from Apple Silicon Mac is broken — do not target it).
- XR: OpenXR 1.13+ + XR Management. Stereo rendering: Single Pass Instanced. Frame rate: 72 fps sustained (no heat warning).
- Orientation: Landscape. Internet: required (cloud layout + Photon). No camera permission (virtual-only, no passthrough).
- Texture: ASTC, compressed, max 2048 (boards 1024). Quality: 1 room, 3 agents max, ≤12 boards, low-poly, pooled audio.
- Text: min 32sp at 2 m, high contrast. Comfort: teleport only, seated default, vignette on move, 15–30 min sessions.
- Mac limit: no Quest Link preview on macOS → Build APK in Unity → `adb install` via USB/wireless.
