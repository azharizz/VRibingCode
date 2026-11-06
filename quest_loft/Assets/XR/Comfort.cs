// VIRTUAL LOFT — Comfort.cs
// Comfort + session rules: teleport-only, seated default, vignette on move,
// 15-30 min sessions, 30s autosave. Quest 3/3S only (no Quest 2).
// Wire to Interaction SDK locomotion (Teleport) + OVR CameraRig height.

using UnityEngine;
using UnityEngine.Events;

namespace VirtualLoft.XR
{
    /// <summary>
    /// Enforces comfort policy in one place. Add to the loft scene root and
    /// wire locomotion/vignette/height to Interaction SDK + CameraRig.
    /// </summary>
    [DisallowMultipleComponent]
    public class Comfort : MonoBehaviour
    {
        // ---- Policy constants (locked by PRODUCT.md / TASK.md §4) -----------------
        public const bool SMOOTH_LOCOMOTION_ALLOWED = false; // TELEPORT ONLY. No smooth stick movement, ever.
        public const bool SEATED_DEFAULT = true;            // CameraRig seated eye height default.
        public const float SEATED_EYE_HEIGHT = 1.2f;        // meters.
        public const float STANDING_EYE_HEIGHT = 1.6f;      // meters (opt-in only).
        public const float VIGNETTE_STRENGTH_ON_MOVE = 0.45f; // 0..1 darkening while teleporting.
        public const float VIGNETTE_FADE_SECONDS = 0.35f;

        public const float SESSION_MIN_MINUTES = 15f;       // target burst length.
        public const float SESSION_MAX_MINUTES = 30f;       // hard nudge: suggest break.
        public const float AUTOSAVE_INTERVAL_SECONDS = 30f; // cloud JSON autosave cadence.
        public const float RESUME_TARGET_SECONDS = 3f;      // loft must reload in ~3s.

        [Header("Runtime state")]
        [Tooltip("Teleport is the ONLY locomotion. Keep false: smooth locomotion stays disabled.")]
        public bool allowSmoothLocomotion = false;
        [Tooltip("Seated by default. Standing is opt-in per user.")]
        public bool seatedMode = true;
        [Tooltip("0..1 vignette strength currently applied (fades after teleport).")]
        [Range(0f, 1f)] public float currentVignette;
        public float sessionElapsedSeconds;
        public bool breakSuggested;

        [Header("Events")]
        public UnityEvent onTeleportStart;
        public UnityEvent onTeleportEnd;
        public UnityEvent onAutosaveTick;
        public UnityEvent onBreakSuggested;

        float autosaveTimer;
        bool vignetteFading;

        void OnValidate()
        {
            if (allowSmoothLocomotion)
            {
                Debug.LogWarning("[Comfort] Smooth locomotion is BANNED (teleport-only policy). Resetting to false.");
                allowSmoothLocomotion = SMOOTH_LOCOMOTION_ALLOWED;
            }
        }

        void Update()
        {
            sessionElapsedSeconds += Time.deltaTime;

            if (!breakSuggested && sessionElapsedSeconds >= SESSION_MAX_MINUTES * 60f)
            {
                breakSuggested = true;
                Debug.Log("[Comfort] 30-min session reached: suggest break (eye strain / battery).");
                onBreakSuggested?.Invoke();
            }

            autosaveTimer += Time.deltaTime;
            if (autosaveTimer >= AUTOSAVE_INTERVAL_SECONDS)
            {
                autosaveTimer = 0f;
                onAutosaveTick?.Invoke(); // wire -> Supabase loft JSON save.
            }

            if (vignetteFading)
            {
                currentVignette = Mathf.MoveTowards(currentVignette, 0f,
                    (VIGNETTE_STRENGTH_ON_MOVE / VIGNETTE_FADE_SECONDS) * Time.deltaTime);
                ApplyVignette(currentVignette);
                if (currentVignette <= 0f) vignetteFading = false;
            }
        }

        /// <summary>
        /// Call when a teleport begins. Applies vignette, blocks input briefly.
        /// Wire from Interaction SDK TeleportLocomotion OnTeleportStart.
        /// </summary>
        public void BeginTeleport()
        {
            currentVignette = VIGNETTE_STRENGTH_ON_MOVE;
            ApplyVignette(currentVignette);
            onTeleportStart?.Invoke();
        }

        /// <summary>Call when a teleport lands. Vignette fades out.</summary>
        public void EndTeleport()
        {
            vignetteFading = true;
            onTeleportEnd?.Invoke();
        }

        /// <summary>Seated/standing switch. Seated default per policy.</summary>
        public void SetSeated(bool seated)
        {
            seatedMode = seated;
            float h = seated ? SEATED_EYE_HEIGHT : STANDING_EYE_HEIGHT;
            Debug.Log($"[Comfort] Eye height -> {h}m ({(seated ? "seated" : "standing")}). Wire to CameraRig tracking-space height.");
            // Wire: cameraRig.trackingSpace.localPosition / OVRManager eye height.
        }

        void ApplyVignette(float strength)
        {
            // Wire: fullscreen vignette material / URP post-process weight.
            // Stub: strength stored in currentVignette for the render hook.
        }

        void Awake()
        {
            allowSmoothLocomotion = false; // enforce teleport-only even if prefab was edited.
            if (seatedMode != SEATED_DEFAULT) seatedMode = SEATED_DEFAULT;
        }
    }
}
