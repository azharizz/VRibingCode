// VIRTUAL LOFT — LoftCard.cs: the voice card. One world-space panel ~1.3 m in
// front of the CEO that walks through the whole voice round-trip:
//   LISTENING (live level) -> THINKING -> proposal (transcript + summary) -> DONE / PROBLEM
// Proposal: A or trigger on SEND sends, B cancels. A risky action has NO A-send:
// it needs a deliberate hold on a separate red button. Text via TextMeshPro.

using System;
using TMPro;
using UnityEngine;

namespace VirtualLoft.XR
{
    public class LoftCard : MonoBehaviour
    {
        public enum CardState { Hidden, Listening, Thinking, Proposal, Result, Error }

        public const float Distance = 1.3f;
        const float BtnY = -0.19f;
        public static readonly Vector2 Size = new Vector2(0.92f, 0.5f);

        public CardState State { get; private set; } = CardState.Hidden;
        public bool Risky { get; private set; }
        public string TitleText => title != null ? title.text : "";
        public string BodyText => body != null ? body.text : "";
        public string HeardText => heard != null ? heard.text : "";
        public Action onSend, onCancel;
        public LoftWorld world;

        Transform root;
        TMP_Text title, heard, body, hint;
        LoftButton send, cancel, riskyBtn, close;
        Transform levelBar, levelBack;
        float hideAt, anchorYaw;
        float level;
        bool placed;

        public Transform Root { get { Build(); return root; } }
        public bool Visible => State != CardState.Hidden;

        public void Build()
        {
            if (root != null) return;
            root = new GameObject("ConfirmCard").transform;
            root.SetParent(transform, false);
            LoftUi.Quad(root, "Back", Vector3.zero, Size, LoftUi.Panel, 0.02f, collider: true);
            LoftUi.Quad(root, "Edge", new Vector3(0f, 0f, 0.012f), Size - new Vector2(0.02f, 0.02f), new Color(0.15f, 0.18f, 0.25f), 0.004f);
            title = LoftUi.Text(root, "Title", new Vector3(0f, 0.195f, 0.016f), 0.042f, LoftUi.Accent, Size.x - 0.08f, TextAlignmentOptions.Left);
            heard = LoftUi.Text(root, "Heard", new Vector3(0f, 0.12f, 0.016f), 0.032f, new Color(0.7f, 0.76f, 0.86f), Size.x - 0.08f, TextAlignmentOptions.Left);
            body = LoftUi.Text(root, "Body", new Vector3(0f, 0.02f, 0.016f), 0.05f, Color.white, Size.x - 0.08f, TextAlignmentOptions.Left);
            hint = LoftUi.Text(root, "Hint", new Vector3(0f, -0.115f, 0.016f), 0.03f, new Color(0.7f, 0.76f, 0.86f), Size.x - 0.08f, TextAlignmentOptions.Left);
            levelBack = LoftUi.Quad(root, "LevelBack", new Vector3(0f, -0.07f, 0.016f), new Vector2(0.8f, 0.03f), new Color(0.2f, 0.24f, 0.32f), 0.004f).transform;
            levelBar = LoftUi.Quad(root, "Level", new Vector3(0.4f, -0.07f, 0.02f), new Vector2(0.01f, 0.03f), LoftUi.Go, 0.004f).transform;
            float y = BtnY;
            send = LoftButton.Make(root, "send", new Vector3(0.21f, y, 0.016f), new Vector2(0.4f, 0.1f), "SEND   (A)", LoftUi.Go, 0.04f, () => PressSend());
            cancel = LoftButton.Make(root, "cancel", new Vector3(-0.21f, y, 0.016f), new Vector2(0.4f, 0.1f), "CANCEL   (B)", LoftUi.Neutral, 0.04f, () => PressCancel());
            // Risky: Cancel on the left, the dangerous button far right with a wide gap, needs a hold.
            riskyBtn = LoftButton.Make(root, "risky", new Vector3(0.26f, y, 0.016f), new Vector2(0.34f, 0.1f), "HOLD TO CONFIRM", LoftUi.Danger, 0.034f, () => ConfirmRisky(), hold: 0.7f);
            close = LoftButton.Make(root, "close", new Vector3(0f, y, 0.016f), new Vector2(0.4f, 0.1f), "CLOSE   (B)", LoftUi.Neutral, 0.04f, () => PressCancel());
            root.gameObject.SetActive(false);
        }

        // ---- states ---------------------------------------------------------
        public void ShowListening(string target)
        {
            Begin(CardState.Listening, "LISTENING" + (string.IsNullOrEmpty(target) ? "" : "  >  " + target), false);
            heard.text = "";
            body.text = "Speak now...";
            hint.text = "Release A when you are done.";
            Buttons(false, false, false, false);
            ShowLevel(true);
        }

        public void SetLevel(float v) { level = Mathf.Clamp01(v); }

        public void ShowThinking(string target)
        {
            Begin(CardState.Thinking, "THINKING", false);
            heard.text = "";
            body.text = "Working out what you meant" + (string.IsNullOrEmpty(target) ? "" : " for " + target) + "...";
            hint.text = "B cancels.";
            Buttons(false, true, false, false);
            cancel.MoveTo(new Vector3(0f, BtnY, 0.016f));
            ShowLevel(false);
        }

        public void ShowProposal(string transcript, string summary, bool risky)
        {
            Begin(CardState.Proposal, risky ? "CONFIRM  (changes things)" : "SEND THIS?", risky);
            heard.text = string.IsNullOrWhiteSpace(transcript) ? "" : "You said: \"" + LoftUtil.Trunc(transcript, 110) + "\"";
            body.text = string.IsNullOrWhiteSpace(summary) ? "(no summary)" : LoftUtil.Trunc(summary, 140);
            hint.text = risky ? "Point at the red button and hold the trigger. A will not confirm this." : "A or trigger sends. B cancels.";
            ShowLevel(false);
            if (risky)
            {
                Buttons(false, true, true, false);
                cancel.MoveTo(new Vector3(-0.25f, BtnY, 0.016f));
            }
            else
            {
                Buttons(true, true, false, false);
                cancel.MoveTo(new Vector3(-0.21f, BtnY, 0.016f));
            }
        }

        public void ShowResult(string message, string transcript = null)
        {
            Begin(CardState.Result, "DONE", false);
            heard.text = string.IsNullOrWhiteSpace(transcript) ? "" : "You said: \"" + LoftUtil.Trunc(transcript, 110) + "\"";
            body.text = LoftUtil.Trunc(message, 140);
            hint.text = "";
            Buttons(false, false, false, true);
            ShowLevel(false);
            hideAt = Time.unscaledTime + 4f;
        }

        public void ShowError(string message, string transcript = null)
        {
            Begin(CardState.Error, "PROBLEM", false);
            heard.text = string.IsNullOrWhiteSpace(transcript) ? "" : "You said: \"" + LoftUtil.Trunc(transcript, 110) + "\"";
            body.text = LoftUtil.Trunc(message, 180);
            hint.text = "Hold A to say it again.";
            Buttons(false, false, false, true);
            ShowLevel(false);
            hideAt = Time.unscaledTime + 12f;
        }

        public void Hide()
        {
            State = CardState.Hidden;
            Risky = false;
            if (root != null) root.gameObject.SetActive(false);
        }

        void Begin(CardState s, string heading, bool risky)
        {
            Build();
            State = s;
            Risky = risky;
            hideAt = 0f;
            level = 0f;
            title.text = heading;
            title.color = risky ? new Color(1f, 0.55f, 0.5f) : LoftUi.Accent;
            root.gameObject.SetActive(true);
            if (!placed || s == CardState.Listening) Place(true);
        }

        void Buttons(bool sendOn, bool cancelOn, bool riskyOn, bool closeOn)
        {
            send.Show(sendOn); cancel.Show(cancelOn); riskyBtn.Show(riskyOn); close.Show(closeOn);
        }

        void ShowLevel(bool on)
        {
            levelBack.gameObject.SetActive(on);
            levelBar.gameObject.SetActive(on);
        }

        // ---- input ------------------------------------------------------------
        // A or trigger on SEND. Never confirms a risky card.
        public bool PressSend()
        {
            if (State != CardState.Proposal || Risky) return false;
            var cb = onSend;
            Hide();
            cb?.Invoke();
            return true;
        }

        // The separated red button (after its hold).
        public bool ConfirmRisky()
        {
            if (State != CardState.Proposal || !Risky) return false;
            var cb = onSend;
            Hide();
            cb?.Invoke();
            return true;
        }

        // B (or the CANCEL/CLOSE button). Does nothing but dismiss.
        public bool PressCancel()
        {
            if (!Visible) return false;
            var cb = onCancel;
            bool wasWaiting = State == CardState.Proposal || State == CardState.Thinking || State == CardState.Listening;
            Hide();
            if (wasWaiting) cb?.Invoke();
            return true;
        }

        // ---- placement (comfortable, lazy-follow: no head-locked jitter) --------
        public void Place(bool snap)
        {
            var cam = world != null ? world.cam : null;
            if (cam == null || root == null) return;
            var f = cam.transform.forward; f.y = 0f;
            if (f.sqrMagnitude < 0.01f) f = world.rig != null ? world.rig.forward : Vector3.forward;
            float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            if (snap || !placed) anchorYaw = yaw;
            placed = true;
            Apply(cam);
        }

        void Apply(Camera cam)
        {
            var dir = Quaternion.Euler(0f, anchorYaw, 0f) * Vector3.forward;
            var pos = cam.transform.position + dir * Distance;
            pos.y = cam.transform.position.y - 0.12f;
            root.position = pos;
            // Content faces +Z, so +Z must point back at the head.
            var toHead = cam.transform.position - pos; toHead.y = 0f;
            if (toHead.sqrMagnitude > 1e-4f) root.rotation = Quaternion.LookRotation(toHead.normalized, Vector3.up) * Quaternion.Euler(8f, 0f, 0f);
        }

        void Update()
        {
            if (!Visible) return;
            if (State == CardState.Result || State == CardState.Error)
                if (hideAt > 0f && Time.unscaledTime > hideAt) Hide();
            if (State == CardState.Thinking)
                body.alpha = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 4f);
            else if (body != null) body.alpha = 1f;
            if (levelBar != null && levelBar.gameObject.activeSelf)
            {
                float w = Mathf.Max(0.01f, 0.8f * level);
                levelBar.localScale = new Vector3(w, 0.03f, 0.004f);
                levelBar.localPosition = new Vector3(0.4f - w / 2f, -0.07f, 0.02f);
            }
            var cam = world != null ? world.cam : null;
            if (cam == null) return;
            var f = cam.transform.forward; f.y = 0f;
            if (f.sqrMagnitude > 0.01f)
            {
                float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                if (Mathf.Abs(Mathf.DeltaAngle(anchorYaw, yaw)) > 40f)
                    anchorYaw = Mathf.MoveTowardsAngle(anchorYaw, yaw, 150f * Time.unscaledDeltaTime);
            }
            Apply(cam);
        }
    }
}
