// VIRTUAL LOFT — LoftMove.cs: moving around the room.
//   Teleport anywhere: push the right stick forward -> arc + landing marker,
//   let go -> you are there (head ends on the marker, clamped to the walkable
//   floor, brief comfort dim). Desktop: hold G and point the mouse at the floor.
//   Smooth walk on the LEFT stick: OFF by default, toggled in the wrist menu.
//   Snap turn lives in Comfort / LoftInteraction.

using UnityEngine;

namespace VirtualLoft.XR
{
    public class LoftMove : MonoBehaviour
    {
        public const float FloorY = 0f;
        public const float WalkSpeed = 1.6f;
        const float Speed = 7f, Gravity = 9.8f, Step = 0.04f;
        const int MaxSteps = 60;

        public bool Aiming { get; private set; }
        public bool HasLanding { get; private set; }
        public bool Clamped { get; private set; }
        public Vector3 Landing { get; private set; }
        public bool SmoothWalk
        {
            get => PlayerPrefs.GetInt("loft.smoothwalk", 0) == 1;
            set => PlayerPrefs.SetInt("loft.smoothwalk", value ? 1 : 0);
        }

        LineRenderer arc;
        GameObject marker, markerRing;
        bool aborted;

        // Ballistic arc from the hand. Returns the floor crossing, or false if it never lands.
        public static bool Trace(Vector3 origin, Vector3 dir, Vector3[] pts, out int count, out Vector3 landing)
        {
            landing = default; count = 0;
            var v = (dir.normalized + Vector3.up * 0.25f).normalized * Speed; // a level aim still throws forward
            var p = origin;
            pts[count++] = p;
            for (int i = 0; i < MaxSteps && count < pts.Length; i++)
            {
                var n = p + v * Step;
                v.y -= Gravity * Step;
                if (n.y <= FloorY && p.y > FloorY)
                {
                    float k = (p.y - FloorY) / Mathf.Max(1e-4f, p.y - n.y);
                    landing = Vector3.Lerp(p, n, k); landing.y = FloorY;
                    pts[count++] = landing;
                    return true;
                }
                p = n; pts[count++] = p;
            }
            return false;
        }

        public static Vector3 ClampTo(Rect b, Vector3 p, out bool clamped)
        {
            var q = new Vector3(Mathf.Clamp(p.x, b.xMin, b.xMax), p.y, Mathf.Clamp(p.z, b.yMin, b.yMax));
            clamped = (q - p).sqrMagnitude > 1e-6f;
            return q;
        }

        // Head ends on `ground` (x/z). Keeps eye height and facing.
        public bool TeleportTo(LoftWorld world, Comfort comfort, Vector3 ground)
        {
            if (world == null || world.rig == null || world.cam == null) return false;
            var g = ClampTo(LoftBridge.WalkRect(), ground, out _);
            var head = world.cam.transform.position;
            comfort?.BeginTeleport();
            world.rig.position += new Vector3(g.x - head.x, 0f, g.z - head.z);
            comfort?.EndTeleport();
            LoftAudio.PlayAt("sfx_teleport", g + Vector3.up * 1.2f, 0.8f);
            return true;
        }

        public void AbortAim() { if (Aiming) aborted = true; }

        readonly Vector3[] pts = new Vector3[MaxSteps + 2];

        public void Tick(LoftInput input, LoftWorld world, Comfort comfort, float dt)
        {
            if (world == null || world.rig == null) return;
            if (!input.aimTeleport) aborted = false;
            bool aim = input.aimTeleport && input.hasRay && !aborted;
            if (aim)
            {
                Aiming = true;
                Aim(input);
            }
            else if (Aiming)
            {
                Aiming = false;
                bool go = input.teleportRelease && !aborted && HasLanding;
                Hide();
                if (go) TeleportTo(world, comfort, Landing);
            }
            if (SmoothWalk && !Aiming && input.walk.sqrMagnitude > 0.04f && world.cam != null)
            {
                var f = world.cam.transform.forward; f.y = 0f; f.Normalize();
                var r = new Vector3(f.z, 0f, -f.x);
                var move = (f * input.walk.y + r * input.walk.x) * WalkSpeed * dt;
                var p = world.rig.position + move;
                var head = world.cam.transform.position + move;
                var c = ClampTo(LoftBridge.WalkRect(), head, out bool cl);
                world.rig.position = p + (cl ? new Vector3(c.x - head.x, 0f, c.z - head.z) : Vector3.zero);
            }
        }

        void Aim(LoftInput input)
        {
            HasLanding = false;
            int n = 0;
            if (input.directAim)
            {
                // Mouse: straight ray to the floor.
                if (input.rayDir.y < -0.02f)
                {
                    float t = (FloorY - input.rayOrigin.y) / input.rayDir.y;
                    if (t > 0f && t < 40f)
                    {
                        Landing = input.rayOrigin + input.rayDir * t; HasLanding = true;
                        pts[n++] = input.handPos; pts[n++] = Landing;
                    }
                }
                if (!HasLanding) { pts[n++] = input.handPos; pts[n++] = input.handPos + input.rayDir * 3f; }
            }
            else
            {
                HasLanding = Trace(input.handPos, input.rayDir, pts, out n, out var l);
                if (HasLanding) Landing = l;
            }
            if (HasLanding) { Landing = ClampTo(LoftBridge.WalkRect(), Landing, out var cl); Clamped = cl; } else Clamped = false;
            Draw(n);
        }

        static Material Sprite(Color c)
        {
            var sh = Shader.Find("Sprites/Default");
            return new Material(sh != null ? sh : Shader.Find("Standard")) { color = c };
        }

        void Draw(int n)
        {
            if (arc == null)
            {
                arc = new GameObject("TeleportArc").AddComponent<LineRenderer>();
                arc.widthMultiplier = 0.012f;
                arc.sharedMaterial = Sprite(Color.white);
            }
            if (marker == null)
            {
                marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                marker.name = "TeleportMarker";
                LoftUtil.Kill(marker.GetComponent<Collider>());
                marker.transform.localScale = new Vector3(0.45f, 0.004f, 0.45f);
                marker.GetComponent<Renderer>().sharedMaterial = Sprite(new Color(0.4f, 0.9f, 1f, 0.55f));
                markerRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                markerRing.name = "TeleportMarkerRing";
                LoftUtil.Kill(markerRing.GetComponent<Collider>());
                markerRing.transform.SetParent(marker.transform, false);
                markerRing.transform.localScale = new Vector3(1.35f, 0.6f, 1.35f);
                markerRing.GetComponent<Renderer>().sharedMaterial = Sprite(new Color(0.4f, 0.9f, 1f, 0.25f));
            }
            var col = HasLanding ? (Clamped ? new Color(1f, 0.85f, 0.3f, 0.9f) : new Color(0.4f, 0.95f, 1f, 0.9f)) : new Color(1f, 0.4f, 0.35f, 0.7f);
            arc.enabled = true;
            arc.positionCount = n;
            for (int i = 0; i < n; i++) arc.SetPosition(i, pts[i]);
            arc.startColor = arc.endColor = col;
            marker.SetActive(HasLanding);
            if (HasLanding)
            {
                marker.transform.position = Landing + Vector3.up * 0.02f;
                marker.GetComponent<Renderer>().sharedMaterial.color = new Color(col.r, col.g, col.b, 0.55f);
            }
        }

        void Hide()
        {
            if (arc != null) arc.enabled = false;
            if (marker != null) marker.SetActive(false);
        }

        void OnDisable() { Hide(); Aiming = false; }
    }
}
