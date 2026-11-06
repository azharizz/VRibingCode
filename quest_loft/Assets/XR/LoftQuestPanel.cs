// VIRTUAL LOFT — LoftQuestPanel.cs (Quest-shaped in-world UI)
// The SAME LoftDesk.Assign()/Complete()/StopAll() API the desktop overlay
// uses, but on physical 3D buttons you point at: mouse-click raycast today,
// XR controller ray tomorrow (wire UF/press straight to PressAgent(i) etc).
// Teleport pads move the camera with the Comfort vignette events, so the
// locomotion path is already teleport-only — Quest policy from day one.

using System.Collections.Generic;
using UnityEngine;

namespace VirtualLoft.XR
{
    [DisallowMultipleComponent]
    public class LoftQuestPanel : MonoBehaviour
    {
        LoftDesk desk;
        Comfort comfort;
        Camera cam;

        // preset task per agent (same words as desktop keys 1/2/3)
        readonly string[] agentIds = { "scout", "builder", "keeper" };
        readonly string[] presets = { "review travel notes", "run landing test", "tidy log wall" };
        readonly Color[] colors = { new Color(0.4f, 0.7f, 1f), new Color(0.5f, 1f, 0.5f), new Color(1f, 0.8f, 0.4f) };

        string lastAgent = "builder";
        TextMesh statusTm;
        float pollTimer;

        static readonly Vector3[] PadPos =
        {
            new Vector3(-3f, 0.03f, 4.6f),
            new Vector3(0f, 0.03f, 5.2f),
            new Vector3(3f, 0.03f, 4.6f),
        };

        void Awake()
        {
            desk = FindFirstObjectByType<LoftDesk>();
            comfort = FindFirstObjectByType<Comfort>();
            cam = Camera.main ?? FindFirstObjectByType<Camera>();
            EnsureBuilt();
            Refresh();
        }

        // ---- public: XR ray (and mouse today) calls these ----
        public void PressAgent(int i)
        {
            if (desk == null) desk = FindFirstObjectByType<LoftDesk>();
            if (desk == null || i < 0 || i >= agentIds.Length) return;
            lastAgent = agentIds[i];
            desk.Assign(lastAgent, presets[i]);
            Refresh();
        }

        public void PressDone()
        {
            if (desk == null) desk = FindFirstObjectByType<LoftDesk>();
            if (desk == null) return;
            desk.Complete(lastAgent);
            Refresh();
        }

        public void PressStop()
        {
            if (desk == null) desk = FindFirstObjectByType<LoftDesk>();
            if (desk == null) return;
            desk.StopAll();
            Refresh();
        }

        public void PressExit()
        {
            if (desk == null) desk = FindFirstObjectByType<LoftDesk>();
            if (desk != null) { desk.ExitApp(); return; }
            Application.Quit();
        }

        public void TeleportTo(int i)
        {
            if (cam == null) cam = Camera.main ?? FindFirstObjectByType<Camera>();
            if (cam == null || i < 0 || i >= PadPos.Length) return;
            if (comfort != null) comfort.BeginTeleport();
            var p = PadPos[i];
            p.y = Comfort.SEATED_EYE_HEIGHT;
            cam.transform.position = p;
            cam.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // face the wall
            if (comfort != null) comfort.EndTeleport();
            Debug.Log($"[QuestPanel] teleport -> pad {i} ({p})");
        }

        public void Refresh()
        {
            if (statusTm == null || desk == null || desk.boards == null) return;
            var kan = desk.boards.ListCards("kanban");
            int todo = 0, doing = 0, done = 0;
            foreach (var c in kan)
            {
                if (c.column == "ToDo") todo++;
                else if (c.column == "Doing") doing++;
                else if (c.column == "Done") done++;
            }
            string last = desk.eventLog.Count > 0 ? desk.eventLog[desk.eventLog.Count - 1] : "—";
            if (last.Length > 42) last = last.Substring(0, 41) + "…";
            statusTm.text = $"TODO {todo}  DOING {doing}  DONE {done}\n{last}";
        }

        // ---- per-frame: mouse point-and-click (desktop stand-in for XR ray) ----
        void Update()
        {
            pollTimer += Time.deltaTime;
            if (pollTimer > 0.5f) { pollTimer = 0f; Refresh(); }
            if (cam == null) cam = Camera.main ?? FindFirstObjectByType<Camera>();
            if (cam == null) return;
            // Left click only; right-drag look stays with LoftDesk.
            if (!Input.GetMouseButtonDown(0)) return;
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, 50f)) return;
            string n = hit.collider.gameObject.name;
            if (n.StartsWith("Btn_"))
            {
                string key = n.Substring(4);
                for (int i = 0; i < agentIds.Length; i++)
                    if (agentIds[i] == key) { PressAgent(i); return; }
                if (key == "done") PressDone();
                else if (key == "stop") PressStop();
                else if (key == "exit") PressExit();
            }
            else if (n.StartsWith("TelePad_"))
            {
                if (int.TryParse(n.Substring(8), out int idx)) TeleportTo(idx);
            }
        }

        // ---- build (idempotent: safe to run on an already-built scene) ----
        public void EnsureBuilt()
        {
            var root = GameObject.Find("QuestPanel");
            if (root == null)
            {
                root = new GameObject("QuestPanel");
                root.transform.position = new Vector3(0f, 1.35f, 4.6f);
            }
            // Unrotated root: legacy TextMesh children already carry their own
            // Y=180 so they read correctly from the camera side (+Z). (An
            // earlier build rotated the root too and mirrored every label.)

            var back = root.transform.Find("Back");
            if (back == null)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = "Back";
                b.transform.SetParent(root.transform, false);
                b.transform.localPosition = Vector3.zero;
                b.transform.localScale = new Vector3(2.8f, 1.7f, 0.1f);
                b.GetComponent<Renderer>().material = new Material(Shader.Find("Standard"))
                    { color = new Color(0.13f, 0.14f, 0.19f) };
            }

            Text("Title", "◆ ASSIGN ◆", 64, new Vector3(0f, 0.62f, 0.08f), 0.008f, new Color(1f, 0.95f, 0.8f), root.transform);

            for (int i = 0; i < 3; i++)
            {
                float x = -0.9f + i * 0.9f;
                Button(root.transform, $"Btn_{agentIds[i]}",
                    $"{agentIds[i].ToUpperInvariant()}\n{presets[i]}",
                    new Vector3(x, 0.22f, 0.08f), new Vector3(0.8f, 0.42f, 0.08f), colors[i]);
            }
            Button(root.transform, "Btn_done", "DONE", new Vector3(-0.9f, -0.38f, 0.08f), new Vector3(0.8f, 0.3f, 0.08f), new Color(0.45f, 0.85f, 0.45f));
            Button(root.transform, "Btn_stop", "STOP", new Vector3(0f, -0.38f, 0.08f), new Vector3(0.8f, 0.3f, 0.08f), new Color(0.9f, 0.45f, 0.4f));
            Button(root.transform, "Btn_exit", "EXIT", new Vector3(0.9f, -0.38f, 0.08f), new Vector3(0.8f, 0.3f, 0.08f), new Color(0.55f, 0.55f, 0.6f));
            SetBtnPos(root.transform, "Btn_done", new Vector3(-0.9f, -0.38f, 0.08f));
            SetBtnPos(root.transform, "Btn_stop", new Vector3(0f, -0.38f, 0.08f));
            SetBtnPos(root.transform, "Btn_exit", new Vector3(0.9f, -0.38f, 0.08f));

            var st = root.transform.Find("Status");
            if (st == null)
            {
                var go = new GameObject("Status");
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(0f, -0.68f, 0.08f);
                go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                var tm = go.AddComponent<TextMesh>();
                tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                tm.fontSize = 36;
                tm.characterSize = 0.008f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.color = new Color(0.85f, 0.92f, 1f);
                tm.text = "…";
            }
            statusTm = root.transform.Find("Status").GetComponent<TextMesh>();
            // Repair pre-fix scenes: Status must face the camera side (+Z).
            root.transform.Find("Status").localRotation = Quaternion.Euler(0f, 180f, 0f);

            for (int i = 0; i < PadPos.Length; i++) EnsurePad(i);
        }

        void EnsurePad(int i)
        {
            var pad = GameObject.Find($"TelePad_{i}");
            if (pad == null)
            {
                pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pad.name = $"TelePad_{i}";
                pad.transform.position = PadPos[i];
                pad.transform.localScale = new Vector3(0.9f, 0.15f, 0.9f);
                pad.GetComponent<Renderer>().material = new Material(Shader.Find("Standard"))
                    { color = new Color(0.3f, 0.75f, 0.9f) };
            }
            var lab = pad.transform.Find("Label");
            if (lab == null)
            {
                lab = new GameObject("Label").transform;
                lab.SetParent(pad.transform, false);
                lab.localPosition = new Vector3(0f, 1.2f, 0f);
                var tm = lab.gameObject.AddComponent<TextMesh>();
                tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                tm.fontSize = 64;
                tm.characterSize = 0.008f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.color = Color.white;
                tm.text = $"PAD {i}";
                lab.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }
        }

        static void SetBtnPos(Transform parent, string name, Vector3 pos)
        {
            var t = parent.Find(name);
            if (t != null) t.localPosition = pos;
        }

        static void Button(Transform parent, string name, string label, Vector3 pos, Vector3 size, Color color)
        {
            Transform t = parent.Find(name);
            if (t == null)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = name;
                b.transform.SetParent(parent, false);
                b.transform.localPosition = pos;
                b.transform.localScale = size;
                b.GetComponent<Renderer>().material = new Material(Shader.Find("Standard")) { color = color };
                t = b.transform;
            }
            var l = t.Find("Label");
            if (l == null)
            {
                var go = new GameObject("Label");
                go.transform.SetParent(t, false);
                go.transform.localPosition = new Vector3(0f, 0f, 0.6f);
                go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                var tm = go.AddComponent<TextMesh>();
                tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                tm.fontSize = 44;
                tm.characterSize = 0.008f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.color = Color.black;
                tm.text = label;
            }
        }

        static void Text(string name, string s, int size, Vector3 pos, float cs, Color color, Transform parent)
        {
            var t = parent.Find(name);
            TextMesh tm;
            if (t == null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = pos;
                go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                tm = go.AddComponent<TextMesh>();
            }
            else tm = t.GetComponent<TextMesh>();
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.fontSize = size;
            tm.characterSize = cs;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            tm.text = s;
        }
    }
}
