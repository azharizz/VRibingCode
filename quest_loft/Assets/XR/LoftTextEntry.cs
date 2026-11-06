// VIRTUAL LOFT — LoftTextEntry.cs: the keyboard BACKUP for exact text (a model
// id, a project name). Headset: the system keyboard (TouchScreenKeyboard).
// Desktop: a small centered text box. Voice stays the main way to talk.

using System;
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
            if (XRSettings.isDeviceActive && !TouchScreenKeyboard.isSupported)
            {
                why = "No system keyboard on this device. Use your voice instead.";
                return false;
            }
            this.hint = hint; this.done = done; text = initial ?? "";
            Active = true; focus = true;
            if (XRSettings.isDeviceActive || (Application.isMobilePlatform && TouchScreenKeyboard.isSupported))
                kb = TouchScreenKeyboard.Open(text, TouchScreenKeyboardType.Default, false, false, false, false, hint);
            return true;
        }

        public void Submit(string value)
        {
            var cb = done; Finish();
            cb?.Invoke(value);
        }

        public void Cancel() => Finish();

        void Finish() { Active = false; kb = null; done = null; GUIUtility.keyboardControl = 0; }

        void Update()
        {
            if (kb == null) return;
            if (kb.status == TouchScreenKeyboard.Status.Done) Submit(kb.text);
            else if (kb.status == TouchScreenKeyboard.Status.Canceled || kb.status == TouchScreenKeyboard.Status.LostFocus) Cancel();
        }

        void OnGUI()
        {
            if (!Active || kb != null) return;
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
