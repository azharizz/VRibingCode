// VIRTUAL LOFT — LoftTextEntry.cs: the keyboard BACKUP for exact text (a model
// id, a folder path). Headset: a laser keyboard in the room, plus any real keyboard.
// Desktop: a small centered text box. Voice stays the main way to talk.

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR;

namespace VirtualLoft.XR
{
    public class LoftTextEntry : MonoBehaviour
    {
        public bool Active { get; private set; }
        string hint = "", text = "";
        Action<string> done;
        TouchScreenKeyboard kb;
        bool focus;

        // Returns false (and says why through `why`) when no keyboard can open.
        public bool Request(string hint, string initial, Action<string> done, out string why)
        {
            why = null;
            this.hint = hint; this.done = done; text = initial ?? "";
            Active = true; focus = true;
            if (XRSettings.isDeviceActive || ForceWorld) ShowKeys();
            else if (Application.isMobilePlatform && TouchScreenKeyboard.isSupported)
                kb = TouchScreenKeyboard.Open(text, TouchScreenKeyboardType.Default, false, false, false, false, hint);
            return true;
        }

        // ---- laser keyboard: a headset has no reliable system keyboard, and the Simulator keeps real keys ----
        public static bool ForceWorld;           // tests
        public bool InWorld => keys != null && keys.gameObject.activeSelf;
        public string Typed => text;
        Transform keys;
        TMP_Text hintLabel, typedLabel;
        readonly List<(LoftButton btn, char c)> letters = new List<(LoftButton, char)>();
        bool shift;
        const float Pitch = 0.086f, KeyH = 0.075f;

        void BuildKeys()
        {
            keys = new GameObject("VrKeyboard").transform;
            keys.SetParent(transform, false);
            LoftUi.Quad(keys, "Back", Vector3.zero, new Vector2(0.94f, 0.6f), LoftUi.Panel, 0.02f, collider: true);
            hintLabel = LoftUi.Text(keys, "Hint", new Vector3(0f, 0.26f, 0.016f), 0.026f, LoftUi.Accent, 0.86f, TextAlignmentOptions.Left);
            typedLabel = LoftUi.Text(keys, "Typed", new Vector3(0f, 0.21f, 0.016f), 0.036f, Color.white, 0.86f, TextAlignmentOptions.Left);
            string[] rows = { "1234567890", "qwertyuiop", "asdfghjkl/", "zxcvbnm.-_" };
            for (int r = 0; r < rows.Length; r++)
                for (int i = 0; i < rows[r].Length; i++)
                {
                    char ch = rows[r][i];
                    // UI +X is the viewer's left
                    var at = new Vector3((rows[r].Length - 1) / 2f * Pitch - i * Pitch, 0.13f - r * 0.083f, 0.016f);
                    var key = LoftButton.Make(keys, "key_" + (int)ch, at, new Vector2(Pitch - 0.006f, KeyH), ch.ToString(), LoftUi.Neutral, 0.032f, () => Add(shift ? char.ToUpperInvariant(ch) : ch));
                    letters.Add((key, ch));
                }
            float x = 0.43f, y = 0.13f - 4 * 0.083f;
            void Wide(string id, string label, float width, Color color, Action act)
            {
                LoftButton.Make(keys, id, new Vector3(x - width / 2f, y, 0.016f), new Vector2(width - 0.006f, KeyH), label, color, 0.028f, act);
                x -= width;
            }
            Wide("shift", "SHIFT", 0.14f, LoftUi.Neutral, () => { shift = !shift; foreach (var l in letters) l.btn.SetText((shift ? char.ToUpperInvariant(l.c) : l.c).ToString()); });
            Wide("tilde", "~", 0.07f, LoftUi.Neutral, () => Add('~'));
            Wide("space", "SPACE", 0.21f, LoftUi.Neutral, () => Add(' '));
            Wide("del", "DELETE", 0.15f, LoftUi.Neutral, Delete);
            Wide("cancel", "CANCEL", 0.14f, LoftUi.Neutral, Cancel);
            Wide("ok", "OK", 0.15f, LoftUi.Go, () => Submit(text));
        }

        void ShowKeys()
        {
            if (keys == null) BuildKeys();
            keys.gameObject.SetActive(true);
            var cam = FindFirstObjectByType<LoftWorld>()?.cam;
            if (cam != null)
            {
                var f = cam.transform.forward; f.y = 0f;
                f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
                keys.position = cam.transform.position + f * 0.9f - Vector3.up * 0.16f;
                keys.rotation = Quaternion.LookRotation(-f, Vector3.up) * Quaternion.Euler(10f, 0f, 0f); // content faces +Z
            }
            ShowTyped();
        }

        void ShowTyped()
        {
            hintLabel.text = hint;
            typedLabel.text = (text.Length > 44 ? "..." + text.Substring(text.Length - 44) : text) + "_";
        }

        void Add(char ch) { text += ch; if (keys != null) ShowTyped(); }
        void Delete() { if (text.Length > 0) text = text.Substring(0, text.Length - 1); if (keys != null) ShowTyped(); }

        public void Submit(string value)
        {
            var cb = done; Finish();
            cb?.Invoke(value);
        }

        public void Cancel() => Finish();

        void Finish()
        {
            Active = false; kb = null; done = null; GUIUtility.keyboardControl = 0;
            if (keys != null) keys.gameObject.SetActive(false);
        }

        void Update()
        {
            if (Active && InWorld) // a real keyboard works too, when this window has focus
                foreach (char ch in Input.inputString)
                {
                    if (ch == '\b') Delete();
                    else if (ch == '\n' || ch == '\r') { Submit(text); return; }
                    else if (!char.IsControl(ch)) Add(ch);
                }
            if (kb == null) return;
            if (kb.status == TouchScreenKeyboard.Status.Done) Submit(kb.text);
            else if (kb.status == TouchScreenKeyboard.Status.Canceled || kb.status == TouchScreenKeyboard.Status.LostFocus) Cancel();
        }

        void OnGUI()
        {
            if (!Active || kb != null || InWorld) return;
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { Cancel(); e.Use(); return; }
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) { Submit(text); e.Use(); return; }
            var r = new Rect(Screen.width / 2f - 240, Screen.height / 2f - 40, 480, 80);
            GUILayout.BeginArea(r, GUI.skin.box);
            GUILayout.Label(hint + "   (Enter = OK, Esc = cancel)");
            GUI.SetNextControlName("loft-entry");
            text = GUILayout.TextField(text);
            if (focus) { GUI.FocusControl("loft-entry"); focus = false; }
            GUILayout.EndArea();
        }
    }
}
