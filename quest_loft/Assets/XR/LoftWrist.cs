// VIRTUAL LOFT — LoftWrist.cs: the wrist menu for the RARE actions.
// Replaces the old 11-button panel. Opens when you look at your left wrist (or
// press X / M on desktop). Everything is visible at once: real scene list,
// project list, voice-first "add agent", and a separated danger row where STOP
// ALL and EXIT need a hold. A keyboard page is the backup for exact text.

using System;
using System.Linq;
using TMPro;
using UnityEngine;

namespace VirtualLoft.XR
{
    public class LoftWrist : MonoBehaviour
    {
        public LoftDesk desk;
        public LoftWorld world;
        public LoftTextEntry entry;
        public Func<string> voiceStatus = () => "";
        public Action<string> say = s => { };   // message to the CEO (card or note)

        public enum Page { Main, Keys }
        public Page page = Page.Main;
        public bool IsOpen { get; private set; }
        public bool pinned;                      // opened by X/M: stays until toggled/B

        public const float W = 0.42f;
        const float Bh = 0.052f, Gap = 0.008f, Txt = 0.022f;

        Transform root, content;
        string signature = "";
        float lookDwell, rebuildTimer;

        public Transform Root { get { EnsureRoot(); return root; } }

        void EnsureRoot()
        {
            if (root != null) return;
            root = new GameObject("WristMenu").transform;
            root.SetParent(transform, false);
            content = new GameObject("Content").transform;
            content.SetParent(root, false);
            root.gameObject.SetActive(false);
        }

        public void SetOpen(bool open, bool pin = false)
        {
            EnsureRoot();
            IsOpen = open;
            pinned = open && pin;
            root.gameObject.SetActive(open);
            if (open) { page = Page.Main; Rebuild(); }
        }

        public void Toggle() => SetOpen(!(IsOpen && pinned), true);

        // B: Keys page -> main -> closed.
        public bool Back()
        {
            if (!IsOpen) return false;
            if (page == Page.Keys) { page = Page.Main; Rebuild(); }
            else SetOpen(false);
            return true;
        }

        // Called every frame by LoftInteraction. `hasHand`: a real left controller pose.
        public void Track(Camera cam, bool hasHand, Vector3 handPos, Quaternion handRot, bool xrDevice)
        {
            EnsureRoot();
            if (cam == null) return;
            var head = cam.transform;
            Vector3 anchor = hasHand && xrDevice
                ? handPos + Vector3.up * 0.2f
                : head.position + head.forward * 0.8f - head.right * 0.22f - head.up * 0.05f;
            if (xrDevice && hasHand && !pinned)
            {
                var to = anchor - head.position;
                float angle = Vector3.Angle(head.forward, to);
                if (!IsOpen) { lookDwell = angle < 24f && to.magnitude < 0.9f ? lookDwell + Time.unscaledDeltaTime : 0f; if (lookDwell > 0.25f) { SetOpen(true); lookDwell = 0f; } }
                else if (angle > 45f || to.magnitude > 1.1f) SetOpen(false);
            }
            if (!IsOpen) return;
            root.position = anchor;
            var face = head.position - anchor;
            if (face.sqrMagnitude > 1e-4f) root.rotation = Quaternion.LookRotation(face.normalized, Vector3.up);
            rebuildTimer += Time.unscaledDeltaTime;
            if (rebuildTimer > 0.4f) { rebuildTimer = 0f; if (Sig() != signature) Rebuild(); }
        }

        string Sig()
        {
            if (desk == null) return "";
            return string.Join("|", LoftWorld.ThemeIds()) + "#" + (world != null && world.Current != null ? world.Current.id : "") + "#" +
                   string.Join(",", desk.projects.Select(p => (string)p["id"])) + "#" + desk.projectId + "#" + page + "#" +
                   (desk.formDraft != null) + "#" + desk.FormSummary() + "#" + voiceStatus() + "#" + (desk.selectedAgentId ?? "");
        }

        // ---- layout ---------------------------------------------------------------
        float y;
        public void Rebuild()
        {
            EnsureRoot();
            LoftUi.DestroyChildren(content);
            foreach (Transform t in root) if (t.name == "Back") LoftUtil.Kill(t.gameObject);
            signature = Sig();
            y = -0.02f;
            if (page == Page.Main) BuildMain(); else BuildKeys();
            float h = -y + 0.02f;
            var back = LoftUi.Quad(root, "Back", new Vector3(0f, 0f, -0.01f), new Vector2(W + 0.03f, h), LoftUi.Panel, 0.01f);
            content.localPosition = new Vector3(0f, h / 2f, 0f);
        }

        void Header(string text)
        {
            var l = LoftUi.Text(content, "H_" + text, new Vector3(0f, y - 0.014f, 0f), 0.02f, LoftUi.Accent, W, TextAlignmentOptions.Left);
            l.text = text;
            y -= 0.032f;
        }

        void Note(string text, Color c, float h = 0.019f, int lines = 1)
        {
            var l = LoftUi.Text(content, "N_" + y, new Vector3(0f, y - (h * lines) / 2f, 0f), h, c, W, TextAlignmentOptions.Left);
            l.text = text;
            y -= h * lines + 0.012f;
        }

        void Row(params (string id, string label, Color color, Action act, float hold, bool sel)[] items)
        {
            float bw = (W - Gap * (items.Length - 1)) / items.Length;
            for (int i = 0; i < items.Length; i++)
            {
                var it = items[i];
                var b = LoftButton.Make(content, it.id, new Vector3(W / 2f - bw / 2f - i * (bw + Gap), y - Bh / 2f, 0.008f),
                    new Vector2(bw, Bh), it.label, it.color, Txt, it.act, it.hold);
                b.selectedLook = it.sel;
            }
            y -= Bh + Gap;
        }

        void BuildMain()
        {
            var title = LoftUi.Text(content, "Title", new Vector3(0f, y - 0.016f, 0f), 0.026f, Color.white, W, TextAlignmentOptions.Left);
            title.text = "LOFT MENU";
            var vs = LoftUi.Text(content, "Voice", new Vector3(0f, y - 0.016f, 0f), 0.018f, voiceStatus().StartsWith("Voice: ready") ? LoftUi.Go : new Color(1f, 0.75f, 0.4f), W, TextAlignmentOptions.Right);
            vs.text = voiceStatus();
            y -= 0.042f;

            Header("SCENE");
            var ids = LoftWorld.ThemeIds();
            string cur = world != null && world.Current != null ? world.Current.id : null;
            for (int i = 0; i < ids.Length; i += 3)
            {
                var row = ids.Skip(i).Take(3).Select(id => ("scene_" + id, Cap(id), LoftUi.Neutral, (Action)(() => desk.SetScene(id)), 0f, id == cur)).ToArray();
                Row(row);
            }

            Header("PROJECT");
            if (desk.projects.Count == 0) Note("No projects yet (backend offline?)", new Color(0.7f, 0.76f, 0.86f));
            foreach (var p in desk.projects.Take(5))
            {
                var pid = (string)p["id"];
                Row(("proj_" + pid, LoftUtil.Trunc((string)p["name"] ?? pid, 30), LoftUi.Neutral, () => desk.SetProject(pid), 0f, pid == desk.projectId));
            }

            Header("NEW AGENT");
            Note("Point at nothing, hold A and say:\n\"add a designer, make it a panda\"", new Color(0.8f, 0.86f, 0.95f), 0.019f, 2);
            Row(("keys", "KEYBOARD...", LoftUi.Neutral, () => { page = Page.Keys; Rebuild(); }, 0f, false));

            y -= 0.03f; // clear gap: dangerous actions live apart from the rest
            var rule = LoftUi.Quad(content, "Rule", new Vector3(0f, y, 0f), new Vector2(W, 0.003f), LoftUi.Danger, 0.002f);
            y -= 0.012f;
            Header("DANGER  (hold the trigger)");
            Row(("stop", "STOP ALL", LoftUi.Danger, () => { desk.Stop(); say("Stopped everyone."); }, 1.0f, false),
                ("exit", "EXIT", LoftUi.Danger, () => desk.ExitApp(), 1.5f, false));
        }

        void BuildKeys()
        {
            var title = LoftUi.Text(content, "Title", new Vector3(0f, y - 0.016f, 0f), 0.026f, Color.white, W, TextAlignmentOptions.Left);
            title.text = "TYPE (backup)";
            y -= 0.042f;
            Row(("k_goal", "GOAL", LoftUi.Neutral, () => Ask("Goal for the team", "", t => desk.Goal(t)), 0f, false),
                ("k_task", "TASK", LoftUi.Neutral, () => { if (desk.selectedAgentId == null) say("Point at an animal and pull the trigger first."); else Ask("Task for " + desk.selectedAgentId, "", t => desk.Assign(desk.selectedAgentId, t)); }, 0f, false),
                ("k_proj", "NEW PROJECT", LoftUi.Neutral, () => Ask("New project name", "", t => desk.CreateProject(t)), 0f, false));
            Row(("k_model", "MODEL ID", LoftUi.Neutral, () => Ask("Model id (empty = default)", desk.formModel, t => desk.formModel = t.Trim()), 0f, false),
                ("k_name", "NAME", LoftUi.Neutral, () => Ask("Agent name", desk.formName, t => desk.formName = t.Trim()), 0f, false),
                ("k_desc", "DESCRIBE ROLE", LoftUi.Neutral, () => Ask("What should this agent do? AI writes the role", "", t => desk.DraftRole(t)), 0f, false));
            Note("Next agent: " + desk.FormSummary(), new Color(0.8f, 0.86f, 0.95f), 0.019f, 3);
            Row(("k_create", "CREATE AGENT", LoftUi.Go, () => desk.SpawnFromForm(), 0f, false),
                ("k_back", "BACK", LoftUi.Neutral, () => { page = Page.Main; Rebuild(); }, 0f, false));
        }

        void Ask(string hint, string initial, Action<string> done)
        {
            if (entry == null) { say("No keyboard available."); return; }
            if (!entry.Request(hint, initial, t => { if (!string.IsNullOrWhiteSpace(t)) done(t); }, out var why)) say(why);
        }

        static string Cap(string id) => string.IsNullOrEmpty(id) ? id : char.ToUpperInvariant(id[0]) + id.Substring(1);
    }
}
