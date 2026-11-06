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

        void OnEnable() => Application.onBeforeRender += Apply;
        void OnDisable() => Application.onBeforeRender -= Apply;
        void Update() => Apply();

        void Apply()
        {
            if (!XRSettings.isDeviceActive) return;
            if (world == null) world = FindFirstObjectByType<LoftWorld>();
            if (world == null || world.cam == null) return;
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
