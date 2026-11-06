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
    // Anything pointable that wants raw trigger events (boards: grab, resize, scroll).
    // Put it on a MonoBehaviour on or above the collider. LoftInteraction calls it
    // BEFORE its own click logic; a target that was hit on press swallows the click.
    public interface IPointerTarget
    {
        void OnHover(bool on);
        void OnPress(Ray r, RaycastHit h);
        void OnDrag(Ray r);     // every frame while the trigger is held after a press, even off the collider
        void OnRelease();
    }

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
        public bool aimTeleport;                   // right stick pushed forward (desktop: hold G)
        public bool teleportRelease;               // stick let go: go there
        public bool directAim;                     // desktop: land where the mouse ray meets the floor (no arc)
        public Vector2 walk;                       // left stick (smooth walk, if enabled)
        public bool hasLeftHand;
        public Vector3 leftPos;                    // world space
        public Quaternion leftRot = Quaternion.identity;

        public abstract void Poll(LoftWorld world);
        public virtual void Haptic(float amplitude, float seconds) { }

        protected void ClearEdges()
        {
            triggerDown = triggerUp = talkDown = talkUp = cancelDown = wristDown = teleportRelease = false;
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
        static readonly InputFeatureUsage<Vector3> PointerPosition = new InputFeatureUsage<Vector3>("PointerPosition");
        static readonly InputFeatureUsage<Quaternion> PointerRotation = new InputFeatureUsage<Quaternion>("PointerRotation");

        bool trigWas, aWas, bWas, xWas, aiming;
        SnapGate gate;
        public const float AimStart = 0.7f, AimEnd = 0.3f;

        public override void Poll(LoftWorld world)
        {
            ClearEdges();
            var rig = world != null ? world.rig : null;
            var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            hasRay = false;
            if (rig != null && right.isValid && Tracked(right)
                && right.TryGetFeatureValue(CommonUsages.devicePosition, out var pos)
                && right.TryGetFeatureValue(CommonUsages.deviceRotation, out var rot))
            {
                // Tracking space = CameraRig local space. devicePosition/Rotation is the GRIP pose, which
                // points far above where a person aims: the ray must use the pointer (aim) pose.
                handPos = rig.TransformPoint(pos);
                var aimPos = right.TryGetFeatureValue(PointerPosition, out var pp) ? pp : pos;
                var aimRot = right.TryGetFeatureValue(PointerRotation, out var pr) ? pr : rot;
                rayOrigin = rig.TransformPoint(aimPos);
                rayDir = rig.rotation * aimRot * Quaternion.Euler(-aimPitchDegrees, 0f, 0f) * Vector3.forward;
                hasRay = true;
            }
            hasLeftHand = false;
            if (rig != null && left.isValid && Tracked(left)
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
            right.TryGetFeatureValue(CommonUsages.primary2DAxis, out var stick);
            // Push forward = aim the teleport arc, let go = teleport. Left/right flick (stick near centre on y) = snap turn.
            if (!aiming && stick.y > AimStart) aiming = true;
            else if (aiming && stick.y < AimEnd) { aiming = false; teleportRelease = true; }
            aimTeleport = aiming;
            snap = !aiming && Mathf.Abs(stick.y) < 0.5f ? gate.Step(stick.x) : 0;
            left.TryGetFeatureValue(CommonUsages.primary2DAxis, out walk);
        }

        // A controller that lost tracking reports a zero pose: ignore it instead of pointing from the floor.
        static bool Tracked(InputDevice d) => !d.TryGetFeatureValue(CommonUsages.isTracked, out bool t) || t;

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
            directAim = true;
            walk = Vector2.zero;
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
            if (typing) { aimTeleport = false; return; }
            aimTeleport = Input.GetKey(KeyCode.G);
            teleportRelease = Input.GetKeyUp(KeyCode.G);
            talkDown = Input.GetKeyDown(KeyCode.V);
            talkHeld = Input.GetKey(KeyCode.V);
            talkUp = Input.GetKeyUp(KeyCode.V);
            cancelDown = Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.C);
            wristDown = Input.GetKeyDown(KeyCode.M);
            snap = (Input.GetKeyDown(KeyCode.E) ? 1 : 0) - (Input.GetKeyDown(KeyCode.Q) ? 1 : 0);
        }
    }
}
