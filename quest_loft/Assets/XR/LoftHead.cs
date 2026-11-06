// VIRTUAL LOFT — LoftHead.cs: the camera follows the headset. Runs before render
// too, so the view uses the newest pose. Tracking space = CameraRig local space.

using UnityEngine;
using UnityEngine.XR;

namespace VirtualLoft.XR
{
    public class LoftHead : MonoBehaviour
    {
        public LoftWorld world;
        bool logged;
        GameObject opaque;

        void OnEnable() => Application.onBeforeRender += Apply;
        void OnDisable() => Application.onBeforeRender -= Apply;
        void Update() => Apply();

        // Desktop simulators show their environment through any alpha below 1. A headset
        // treats the frame as solid, so Quest skips this full-screen pass.
        void EnsureOpaque()
        {
            if (opaque != null || Application.isMobilePlatform) return;
            opaque = GameObject.CreatePrimitive(PrimitiveType.Quad);
            opaque.name = "OpaqueFrame";
            LoftUtil.Kill(opaque.GetComponent<Collider>());
            opaque.layer = LoftCinema.LAYER; // stays drawn while the cinema hides the world
            opaque.transform.SetParent(world.cam.transform, false);
            opaque.transform.localPosition = Vector3.forward;
            var mesh = opaque.GetComponent<MeshFilter>().mesh;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f); // never culled
            var r = opaque.GetComponent<Renderer>();
            r.sharedMaterial = new Material(Resources.Load<Shader>("Shaders/LoftAlphaOne"));
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        void Apply()
        {
            if (!XRSettings.isDeviceActive) return;
            if (world == null) world = FindFirstObjectByType<LoftWorld>();
            if (world == null || world.cam == null) return;
            EnsureOpaque();
            var head = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            if (!head.isValid) head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!head.isValid) return;
            var t = world.cam.transform;
            if (head.TryGetFeatureValue(CommonUsages.devicePosition, out var p)) t.localPosition = p;
            if (head.TryGetFeatureValue(CommonUsages.deviceRotation, out var r)) t.localRotation = r;
            if (!logged) { logged = true; Debug.Log($"[Head] tracking {head.name} at {p}"); }
        }
    }
}
