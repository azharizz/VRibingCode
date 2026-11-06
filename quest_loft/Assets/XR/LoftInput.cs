// VIRTUAL LOFT — LoftInput.cs: one input abstraction, two sources.
// LoftInteraction reads ONLY this, so the headset and the desktop rig run the
// exact same pointing / talk / card / wrist code.
//   XrInput      right controller ray + trigger, A talk, B cancel, X wrist, stick turn
//   DesktopInput mouse aims, click = trigger, hold V = A, Esc/C = B, Q/E = turn, M = wrist
// Plain UnityEngine.XR + legacy Input Manager: no extra SDK.

using UnityEngine;
using UnityEngine.XR;

namespace VirtualLoft.XR
{
    public abstract class LoftInput
    {
        public bool hasRay;
        public Vector3 rayOrigin, rayDir;          // world space
        public Vector3 handPos;                    // where the (virtual) right hand is, for the laser start
        public bool triggerDown, triggerHeld, triggerUp;
        public bool talkDown, talkHeld, talkUp;    // A
        public bool cancelDown;                    // B
        public bool wristDown;                     // X
        public int snap;                           // -1 / 0 / +1 this frame
        public bool hasLeftHand;
        public Vector3 leftPos;                    // world space
        public Quaternion leftRot = Quaternion.identity;

        public abstract void Poll(LoftWorld world);
        public virtual void Haptic(float amplitude, float seconds) { }

        protected void ClearEdges()
        {
            triggerDown = triggerUp = talkDown = talkUp = cancelDown = wristDown = false;
            snap = 0;
        }
    }

    // Flick = one turn: arms past `fire`, must fall back under `rearm` to turn again.
    public struct SnapGate
    {
        bool armed;
        public int Step(float x, float fire = 0.65f, float rearm = 0.3f)
        {
            if (Mathf.Abs(x) < rearm) { armed = true; return 0; }
            if (!armed || Mathf.Abs(x) < fire) return 0;
            armed = false;
            return x > 0f ? 1 : -1;
        }
        public void Reset() => armed = false;
    }

    // Tests and tools drive this directly.
    public class FakeInput : LoftInput
    {
        public override void Poll(LoftWorld world) { }
        public void Frame() => ClearEdges();
    }

    public class XrInput : LoftInput
    {
        // Unverified on hardware: if the ray points below where the controller points, set this (degrees, + = up).
        public static float aimPitchDegrees = 0f;

        bool trigWas, aWas, bWas, xWas;
        SnapGate gate;

        public override void Poll(LoftWorld world)
        {
            ClearEdges();
            var rig = world != null ? world.rig : null;
            var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            hasRay = false;
            if (rig != null && right.isValid
                && right.TryGetFeatureValue(CommonUsages.devicePosition, out var pos)
                && right.TryGetFeatureValue(CommonUsages.deviceRotation, out var rot))
            {
                // Tracking space = CameraRig local space.
                rayOrigin = rig.TransformPoint(pos);
                rayDir = rig.rotation * rot * Quaternion.Euler(-aimPitchDegrees, 0f, 0f) * Vector3.forward;
                handPos = rayOrigin;
                hasRay = true;
            }
            hasLeftHand = false;
            if (rig != null && left.isValid
                && left.TryGetFeatureValue(CommonUsages.devicePosition, out var lp)
                && left.TryGetFeatureValue(CommonUsages.deviceRotation, out var lr))
            {
                leftPos = rig.TransformPoint(lp);
                leftRot = rig.rotation * lr;
                hasLeftHand = true;
            }
            Edge(right, CommonUsages.triggerButton, ref trigWas, ref triggerDown, ref triggerUp, out triggerHeld);
            Edge(right, CommonUsages.primaryButton, ref aWas, ref talkDown, ref talkUp, out talkHeld);
            bool b = false; right.TryGetFeatureValue(CommonUsages.secondaryButton, out b);
            cancelDown = b && !bWas; bWas = b;
            bool x = false; left.TryGetFeatureValue(CommonUsages.primaryButton, out x);
            wristDown = x && !xWas; xWas = x;
            snap = right.TryGetFeatureValue(CommonUsages.primary2DAxis, out var stick) ? gate.Step(stick.x) : 0;
        }

        static void Edge(InputDevice d, InputFeatureUsage<bool> usage, ref bool was, ref bool down, ref bool up, out bool held)
        {
            held = false;
            if (d.isValid) d.TryGetFeatureValue(usage, out held);
            down = held && !was;
            up = !held && was;
            was = held;
        }

        public override void Haptic(float amplitude, float seconds)
        {
            var d = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (d.isValid && d.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
                d.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), seconds);
        }
    }

    public class DesktopInput : LoftInput
    {
        public override void Poll(LoftWorld world)
        {
            ClearEdges();
            var cam = world != null ? world.cam : null;
            hasRay = false;
            hasLeftHand = false;
            if (cam == null) return;
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            rayOrigin = ray.origin;
            rayDir = ray.direction;
            hasRay = true;
            var t = cam.transform;
            handPos = t.position + t.right * 0.22f - t.up * 0.18f + t.forward * 0.3f; // a hand-ish point for the laser
            leftPos = t.position - t.right * 0.2f - t.up * 0.2f + t.forward * 0.45f;
            leftRot = t.rotation;
            bool typing = GUIUtility.keyboardControl != 0;
            triggerDown = Input.GetMouseButtonDown(0);
            triggerHeld = Input.GetMouseButton(0);
            triggerUp = Input.GetMouseButtonUp(0);
            if (typing) return;
            talkDown = Input.GetKeyDown(KeyCode.V);
            talkHeld = Input.GetKey(KeyCode.V);
            talkUp = Input.GetKeyUp(KeyCode.V);
            cancelDown = Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.C);
            wristDown = Input.GetKeyDown(KeyCode.M);
            snap = (Input.GetKeyDown(KeyCode.E) ? 1 : 0) - (Input.GetKeyDown(KeyCode.Q) ? 1 : 0);
        }
    }
}
