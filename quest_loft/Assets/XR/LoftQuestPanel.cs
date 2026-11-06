// VIRTUAL LOFT — LoftQuestPanel.cs: the CEO's in-world (VR) controls.
//
// Point with the RIGHT controller (laser) and pull the trigger — or click
// with the mouse on desktop — at:
//   * an animal          -> select that agent
//   * a panel button     -> + AGENT / – AGENT / SCENE ▶ / GOAL ⌨ / TASK ⌨ /
//                           STOP / APPROVE / REJECT / EXIT / ★ PARTY
//     + AGENT opens the new-agent page (same form as the desktop panel):
//       ROLE ▶ / ANIMAL ▶ / MODEL ⌨ / NAME ⌨ / DESCRIBE ⌨ (AI writes a role) /
//       CREATE / BACK — the console text shows the form, ⚠ marks tools that change files
//   * a teleport pad     -> teleport (moves the CameraRig, comfort vignette)
// GOAL/TASK/REJECT open the headset's system keyboard (TouchScreenKeyboard).
// Hold A to talk (VoiceCommands push-to-talk). Uses only UnityEngine.XR
// input — no extra SDK.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace VirtualLoft.XR
{
    [DisallowMultipleComponent]
    public class LoftQuestPanel : MonoBehaviour
    {
        static readonly (string id, string label, Color color)[] Buttons =
        {
            ("spawn", "+ AGENT", new Color(0.45f, 0.75f, 1f)),
            ("remove", "– AGENT", new Color(0.6f, 0.6f, 0.7f)),
            ("theme", "SCENE ▶", new Color(1f, 0.75f, 0.35f)),
            ("goal", "GOAL ⌨", new Color(0.5f, 1f, 0.6f)),
            ("task", "TASK ⌨", new Color(0.7f, 0.95f, 0.5f)),
            ("stop", "STOP", new Color(0.95f, 0.45f, 0.4f)),
            ("approve", "APPROVE", new Color(0.45f, 0.9f, 0.45f)),
            ("reject", "REJECT", new Color(0.95f, 0.6f, 0.35f)),
            ("exit", "EXIT", new Color(0.55f, 0.55f, 0.6f)),
            ("party", "★ PARTY", new Color(1f, 0.35f, 0.75f)),
            ("project", "PROJECT ▶", new Color(0.55f, 0.85f, 0.95f)),
        };
        static readonly (string id, string label, Color color)[] SpawnButtons =
        {
            ("role", "ROLE ▶", new Color(0.45f, 0.75f, 1f)),
            ("animal", "ANIMAL ▶", new Color(1f, 0.75f, 0.35f)),
            ("model", "MODEL ⌨", new Color(0.75f, 0.65f, 1f)),
            ("name", "NAME ⌨", new Color(0.7f, 0.95f, 0.5f)),
            ("describe", "DESCRIBE ⌨", new Color(0.5f, 1f, 0.8f)),
            ("create", "CREATE", new Color(0.45f, 0.9f, 0.45f)),
            ("back", "BACK", new Color(0.55f, 0.55f, 0.6f)),
        };
        public static readonly Vector3[] PadPos =
        {
            new Vector3(-3f, 0.03f, 4.6f),
            new Vector3(0f, 0.03f, 5.2f),
            new Vector3(3f, 0.03f, 4.6f),
        };

        LoftDesk desk;
        LoftWorld world;
        Comfort comfort;
        VoiceCommands voice;
        TextMesh statusTm;
        Transform panel;
        LineRenderer laser;
        TouchScreenKeyboard keyboard;
        string keyboardFor;
        Transform mainPage, spawnPage;
        bool triggerWas;
        float refreshTimer;

        void Awake()
        {
            Wire();
            EnsureBuilt();
        }

        // Lazy too, so editor tools can drive the panel outside Play mode.
        void Wire()
        {
            if (desk == null) desk = FindFirstObjectByType<LoftDesk>();
            if (world == null) world = FindFirstObjectByType<LoftWorld>();
            if (comfort == null) comfort = FindFirstObjectByType<Comfort>();
            if (voice == null) voice = FindFirstObjectByType<VoiceCommands>();
        }

        // ---- actions (public so tests/voice/other UIs can call them) ----
        public void Press(string id)
        {
            Wire();
            if (desk == null) return;
            switch (id)
            {
                case "spawn": ShowSpawnPage(true); break;
                case "back": ShowSpawnPage(false); break;
                case "role": desk.CycleRole(); break;
                case "animal": desk.CycleSkin(); break;
                case "model": OpenKeyboard("model", desk.formModel); break;
                case "name": OpenKeyboard("name", desk.formName); break;
                case "describe": OpenKeyboard("describe", ""); break;
                case "create": desk.SpawnFromForm(); ShowSpawnPage(false); break;
                case "remove": desk.RemoveSelected(); break;
                case "theme": desk.NextTheme(); break;
                case "goal": OpenKeyboard("goal"); break;
                case "task":
                    if (desk.selectedAgentId == null) desk.Note("Point at an animal first, then TASK");
                    else OpenKeyboard("task");
                    break;
                case "stop": desk.Stop(); break;
                case "approve": desk.Approve(); break;
                case "reject": if (!OpenKeyboard("reject")) desk.Reject(null); break;
                case "exit": desk.ExitApp(); break;
                case "party": desk.Party(); break;
                case "project": desk.CycleProject(); break;
            }
        }

        public void TeleportTo(int i)
        {
            Wire();
            if (world == null || world.rig == null || i < 0 || i >= PadPos.Length) return;
            if (comfort != null) comfort.BeginTeleport();
            var rig = world.rig;
            rig.SetPositionAndRotation(new Vector3(PadPos[i].x, rig.position.y, PadPos[i].z), Quaternion.Euler(0f, 180f, 0f));
            if (comfort != null) comfort.EndTeleport();
            LoftAudio.PlayAt("sfx_teleport", PadPos[i] + Vector3.up * 1.2f, 0.8f);
        }

        // Routes a pointed-at collider to the right action.
        public void Click(GameObject go)
        {
            for (var t = go.transform; t != null; t = t.parent)
            {
                var n = t.name;
                if (n.StartsWith("Btn_")) { LoftAudio.Play("sfx_click", 0.8f); Press(n.Substring(4)); return; }
                if (n.StartsWith("TelePad_") && int.TryParse(n.Substring(8), out int pad)) { TeleportTo(pad); return; }
                var agent = t.GetComponent<AgentStateMachine>();
                if (agent != null) { desk?.Select(agent.agentId); return; }
            }
        }

        public void ShowSpawnPage(bool on)
        {
            if (mainPage != null) mainPage.gameObject.SetActive(!on);
            if (spawnPage != null) spawnPage.gameObject.SetActive(on);
            RefreshStatus();
        }

        bool OpenKeyboard(string target, string text = "")
        {
            if (!TouchScreenKeyboard.isSupported)
            {
                desk.Note(XRSettings.isDeviceActive
                    ? "No system keyboard — hold A and speak (\"run …\")"
                    : "Type in the desk panel (top-left)");
                return false;
            }
            keyboardFor = target;
            string hint = target switch
            {
                "reject" => "What should change?",
                "task" => "Task for the selected agent",
                "model" => "Model id, e.g. qwen/qwen3-coder (empty = default)",
                "name" => "Agent name",
                "describe" => "What should this agent do? AI writes the role",
                _ => "Goal for the team",
            };
            keyboard = TouchScreenKeyboard.Open(text ?? "", TouchScreenKeyboardType.Default, false, target != "model" && target != "name", false, false, hint);
            return true;
        }

        void Update()
        {
            refreshTimer += Time.deltaTime;
            if (refreshTimer > 0.5f) { refreshTimer = 0f; RefreshStatus(); }
            PollKeyboard();

            if (XRSettings.isDeviceActive) UpdateControllerRay();
            else
            {
                if (laser != null) laser.enabled = false;
                if (!Input.GetMouseButtonDown(0) || world == null || world.cam == null) return;
                var gui = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                if (desk != null && desk.overlayRect.Contains(gui)) return;
                if (Physics.Raycast(world.cam.ScreenPointToRay(Input.mousePosition), out var hit, 50f)) Click(hit.collider.gameObject);
            }
        }

        void PollKeyboard()
        {
            if (keyboard == null) return;
            if (keyboard.status == TouchScreenKeyboard.Status.Done)
            {
                var text = keyboard.text;
                if (keyboardFor == "goal") desk.Goal(text);
                else if (keyboardFor == "task") desk.Assign(desk.selectedAgentId, text);
                else if (keyboardFor == "reject") desk.Reject(text);
                else if (keyboardFor == "model") desk.formModel = text.Trim();
                else if (keyboardFor == "name") desk.formName = text.Trim();
                else if (keyboardFor == "describe") desk.DraftRole(text);
                keyboard = null;
            }
            else if (keyboard.status != TouchScreenKeyboard.Status.Visible) keyboard = null;
        }

        // Right controller pose is in tracking space = CameraRig local space.
        void UpdateControllerRay()
        {
            var hand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (world == null || world.rig == null || !hand.isValid
                || !hand.TryGetFeatureValue(CommonUsages.devicePosition, out var pos)
                || !hand.TryGetFeatureValue(CommonUsages.deviceRotation, out var rot))
            {
                if (laser != null) laser.enabled = false;
                return;
            }
            var origin = world.rig.TransformPoint(pos);
            var dir = world.rig.rotation * rot * Vector3.forward;
            bool hitSomething = Physics.Raycast(origin, dir, out var hit, 30f);
            EnsureLaser();
            laser.enabled = true;
            laser.SetPosition(0, origin);
            laser.SetPosition(1, hitSomething ? hit.point : origin + dir * 5f);

            hand.TryGetFeatureValue(CommonUsages.triggerButton, out bool trigger);
            if (trigger && !triggerWas && hitSomething) Click(hit.collider.gameObject);
            triggerWas = trigger;

            if (voice != null && hand.TryGetFeatureValue(CommonUsages.primaryButton, out bool talk)) voice.pushToTalkHeld = talk;
        }

        void EnsureLaser()
        {
            if (laser != null) return;
            var go = new GameObject("Laser");
            laser = go.AddComponent<LineRenderer>();
            laser.positionCount = 2;
            laser.widthMultiplier = 0.006f;
            var shader = Shader.Find("Sprites/Default");
            if (shader != null) laser.material = new Material(shader) { color = new Color(0.4f, 0.9f, 1f, 0.8f) };
        }

        void RefreshStatus()
        {
            if (statusTm == null || desk == null) return;
            int working = 0, review = 0;
            foreach (var t in desk.tasks.Values)
            {
                if (t.status == "running") working++;
                else if (t.status == "review") review++;
            }
            if (spawnPage != null && spawnPage.gameObject.activeSelf)
            {
                statusTm.text = $"NEW AGENT {desk.agents.agents.Count + 1}/{desk.maxAgents}{(desk.drafting ? "  (AI is writing the role…)" : "")}\n" +
                                LoftUtil.Wrap(desk.FormSummary(), 52, 3);
                return;
            }
            string sel = $"{desk.selectedAgentId ?? "none (point at an animal)"}   project: {desk.ProjectName}";
            string last = desk.eventLog.Count > 0 ? desk.eventLog[desk.eventLog.Count - 1] : "—";
            statusTm.text = $"{(desk.Connected ? "● live" : "○ offline")}  team {desk.agents.agents.Count}/{desk.maxAgents}  working {working}  review {review}\n" +
                            $"selected: {sel}\n{LoftUtil.Trunc(last, 46)}";
        }

        // ---- build (idempotent; replaces the old fixed 3-agent panel) ----
        // The console rides on the CameraRig, low in front of the CEO (like a
        // desk), so it never blocks the view of the team and follows teleports.
        public void EnsureBuilt()
        {
            Wire();
            var root = GameObject.Find("QuestPanel");
            if (root != null && root.transform.Find("Main/Btn_project") == null) { LoftUtil.Kill(root); root = null; }
            if (root == null)
            {
                root = new GameObject("QuestPanel");
                root.transform.localScale = Vector3.one * 0.25f;
                LoftUtil.Prim(PrimitiveType.Cube, "Back", root.transform, new Vector3(0f, -0.16f, 0f), new Vector3(2.8f, 1.85f, 0.06f), new Color(0.13f, 0.14f, 0.19f));
                Page(root.transform, "Main", Buttons);
                Page(root.transform, "Spawn", SpawnButtons).gameObject.SetActive(false);
            }
            panel = root.transform;
            mainPage = panel.Find("Main");
            spawnPage = panel.Find("Spawn");
            if (world != null && world.rig != null && panel.parent != world.rig)
            {
                panel.SetParent(world.rig, false);
                panel.localPosition = new Vector3(0f, -0.5f, 0.6f);
                // Face the CEO (rig-local -Z), tilted up like a console.
                panel.localRotation = Quaternion.Euler(0f, 180f, 0f) * Quaternion.Euler(-30f, 0f, 0f);
            }
            statusTm = LoftUtil.Text(panel, "Status", new Vector3(0f, -0.9f, 0.04f), 32, 0.008f, new Color(0.85f, 0.92f, 1f));

            for (int i = 0; i < PadPos.Length; i++)
            {
                var pad = GameObject.Find($"TelePad_{i}");
                if (pad != null && pad.transform.Find("Disk") == null) { LoftUtil.Kill(pad); pad = null; }
                if (pad != null) continue;
                pad = new GameObject($"TelePad_{i}");
                pad.transform.position = PadPos[i];
                LoftUtil.Prim(PrimitiveType.Cylinder, "Disk", pad.transform, Vector3.zero, new Vector3(0.9f, 0.03f, 0.9f), new Color(0.3f, 0.75f, 0.9f), keepCollider: true);
                LoftUtil.Text(pad.transform, "Label", new Vector3(0f, 0.9f, 0f), 64, 0.008f, Color.white).text = $"PAD {i}";
            }
        }

        // 3 buttons per row. Unscaled parents keep label text proportions.
        static Transform Page(Transform root, string name, (string id, string label, Color color)[] buttons)
        {
            var page = new GameObject(name).transform;
            page.SetParent(root, false);
            for (int i = 0; i < buttons.Length; i++)
            {
                var (id, label, color) = buttons[i];
                var b = new GameObject($"Btn_{id}").transform;
                b.SetParent(page, false);
                b.localPosition = new Vector3(-0.9f + (i % 3) * 0.9f, 0.45f - (i / 3) * 0.36f, 0.04f);
                LoftUtil.Prim(PrimitiveType.Cube, "Face", b, Vector3.zero, new Vector3(0.8f, 0.3f, 0.04f), color, keepCollider: true);
                LoftUtil.Text(b, "Label", new Vector3(0f, 0f, 0.03f), 44, 0.008f, Color.black).text = label;
            }
            return page;
        }

        // Keep the console ~0.5 m below the eyes (seated or standing tracking).
        void LateUpdate()
        {
            if (panel == null) return;
            Wire();
            if (world == null || world.cam == null || world.rig == null) return;
            if (panel.parent != world.rig) EnsureBuilt();
            panel.localPosition = new Vector3(0f, world.cam.transform.localPosition.y - 0.5f, 0.6f);
        }
    }
}
