// VIRTUAL LOFT — LoftHands.cs: the whole app with bare hands, no controller.
//   right pinch        = trigger: point and click, hold, drag
//   left pinch held    = A: talk.   A quick left pinch opens the menu.
//   left fist          = B: cancel
//   right fist         = aim a teleport, open the hand to go
// Fills the same LoftInput as a controller, so nothing past LoftInput knows about hands.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Hands;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace VirtualLoft.XR
{
    // One hand this frame, in tracking space.
    public struct HandFrame
    {
        public bool tracked;
        public Vector3 aimPos, gripPos;
        public Quaternion aimRot, gripRot;
        public float pinch, grasp;
    }

    // Pure gesture logic: tests feed it frames directly.
    public class HandGestures
    {
        public const float On = 0.8f, Off = 0.5f;   // a gap, so a trembling pinch doesn't click twice
        bool rPinch, lPinch, rFist, lFist;

        static bool Hold(bool was, float v) => was ? v > Off : v >= On;

        public void Apply(LoftInput i, HandFrame right, HandFrame left, Transform rig)
        {
            bool wasFist = rFist, wasLeftFist = lFist, wasPinch = rPinch, wasTalk = lPinch;
            rFist = right.tracked && Hold(rFist, right.grasp);
            lFist = left.tracked && Hold(lFist, left.grasp);
            // A closing fist also reads as a pinch: the fist wins.
            rPinch = right.tracked && !rFist && Hold(rPinch, right.pinch);
            lPinch = left.tracked && !lFist && Hold(lPinch, left.pinch);

            i.hasRay = right.tracked && rig != null;
            if (i.hasRay)
            {
                i.handPos = rig.TransformPoint(right.gripPos);
                i.rayOrigin = rig.TransformPoint(right.aimPos);
                i.rayDir = rig.rotation * right.aimRot * Vector3.forward;
            }
            i.hasLeftHand = left.tracked && rig != null;
            if (i.hasLeftHand)
            {
                i.leftPos = rig.TransformPoint(left.gripPos);
                i.leftRot = rig.rotation * left.gripRot;
            }
            i.triggerHeld = rPinch; i.triggerDown = rPinch && !wasPinch; i.triggerUp = !rPinch && wasPinch;
            i.talkHeld = lPinch; i.talkDown = lPinch && !wasTalk; i.talkUp = !lPinch && wasTalk;
            i.wristHeld = lPinch; // a quick tap of both = the menu (LoftInteraction)
            i.wristDown = false;
            i.cancelDown = lFist && !wasLeftFist;
            i.aimTeleport = rFist;
            i.teleportRelease = !rFist && wasFist;
            i.walk = Vector2.zero;
            i.snap = 0;
        }
    }

    // What the app tells the CEO to do, in terms of what they are holding.
    public static class LoftWords
    {
        public static bool Hands;
        public static string Talk => Hands ? "pinch and hold your left hand" : "hold A";
        public static string TalkCap => Hands ? "Pinch and hold your left hand" : "Hold A";
        public static string Release => Hands ? "Open your left hand when you are done." : "Release A when you are done.";
        public static string Cancel => Hands ? "Left fist" : "B";
        public static string Click => Hands ? "pinch your right hand" : "pull the trigger";
        public static string Confirm => Hands ? "Hold a right-hand pinch to confirm. A left pinch will not confirm this." : "Hold the trigger to confirm (T in the Simulator). A will not confirm this.";
        public static string HoldClick => Hands ? "hold a right pinch" : "hold the trigger";
        public static string Teleport => Hands ? "Teleport: make a right fist, aim, open your hand." : "Teleport: push the right stick forward, let go to go.";
        public static string Key(string label, string key) => Hands ? label : $"{label}   ({key})";
    }

    // Fills both hands: joints from XR Hands, the runtime's own aim and pinch when it offers them.
    public static class HandDevices
    {
        static readonly List<XRHandSubsystem> found = new List<XRHandSubsystem>();
        // Wrist out to each fingertip, in drawing order. Five fingers of five points.
        public static readonly XRHandJointID[] Chain =
        {
            XRHandJointID.Wrist, XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal, XRHandJointID.ThumbDistal, XRHandJointID.ThumbTip,
            XRHandJointID.Wrist, XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, XRHandJointID.IndexTip,
            XRHandJointID.Wrist, XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip,
            XRHandJointID.Wrist, XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, XRHandJointID.RingTip,
            XRHandJointID.Wrist, XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal, XRHandJointID.LittleTip,
        };
        public static readonly Vector3[][] Bones = { new Vector3[Chain.Length], new Vector3[Chain.Length] }; // right, left; tracking space

        static readonly XRHandJointID[] CurlTips = { XRHandJointID.MiddleTip, XRHandJointID.RingTip, XRHandJointID.LittleTip };

        // Gesture strength from distances in metres: 1 = fully closed.
        public static float Closed(float metres, float closed, float open) => Mathf.Clamp01((open - metres) / (open - closed));

        static bool Joint(XRHand hand, XRHandJointID id, out Pose pose)
        {
            pose = default;
            return hand.GetJoint(id).TryGetPose(out pose);
        }

        static HandFrame FromJoints(XRHand hand, Vector3 head, Quaternion headRot, float side, Vector3[] bones)
        {
            var f = new HandFrame();
            if (!hand.isTracked || !Joint(hand, XRHandJointID.Palm, out var palm) || !Joint(hand, XRHandJointID.ThumbTip, out var thumb)
                || !Joint(hand, XRHandJointID.IndexTip, out var index) || !Joint(hand, XRHandJointID.IndexProximal, out var knuckle)) return f;
            for (int i = 0; i < Chain.Length; i++) if (Joint(hand, Chain[i], out var p)) bones[i] = p.position;
            f.tracked = true;
            f.gripPos = palm.position; f.gripRot = palm.rotation;
            f.pinch = Closed(Vector3.Distance(thumb.position, index.position), 0.015f, 0.05f);
            float curl = 0f; int n = 0;
            foreach (var tip in CurlTips)
                if (Joint(hand, tip, out var t)) { curl += Vector3.Distance(t.position, palm.position); n++; }
            f.grasp = n > 0 ? Closed(curl / n, 0.045f, 0.095f) : 0f;
            // A hand aims best from the shoulder through the knuckles: steady, and it reaches across the room.
            var flat = Quaternion.Euler(0f, headRot.eulerAngles.y, 0f);
            var shoulder = head + flat * new Vector3(side * 0.17f, -0.18f, 0f);
            f.aimPos = knuckle.position;
            f.aimRot = Quaternion.LookRotation((knuckle.position - shoulder).normalized, Vector3.up);
            return f;
        }

        static void Runtime(HandInteractionProfile.HandInteraction h, ref HandFrame f)
        {
            if (!h.isTracked.isPressed) return;
            f.tracked = true;
            f.aimPos = h.pointerPosition.ReadValue(); f.aimRot = h.pointerRotation.ReadValue();
            f.pinch = Mathf.Max(f.pinch, h.pinchValue.ReadValue());
            f.grasp = Mathf.Max(f.grasp, h.graspValue.ReadValue());
            if (f.gripRot == default) { f.gripPos = h.devicePosition.ReadValue(); f.gripRot = h.deviceRotation.ReadValue(); }
        }

        public static bool Read(Vector3 head, Quaternion headRot, out HandFrame right, out HandFrame left)
        {
            right = default; left = default;
            SubsystemManager.GetSubsystems(found);
            foreach (var s in found)
            {
                if (!s.running) continue;
                right = FromJoints(s.rightHand, head, headRot, 1f, Bones[0]);
                left = FromJoints(s.leftHand, head, headRot, -1f, Bones[1]);
                break;
            }
            foreach (var d in InputSystem.devices)
            {
                if (!(d is HandInteractionProfile.HandInteraction h)) continue;
                if (h.usages.Contains(UnityEngine.InputSystem.CommonUsages.LeftHand)) Runtime(h, ref left);
                else Runtime(h, ref right);
            }
            return right.tracked || left.tracked;
        }
    }
}
