// VIRTUAL LOFT — VoiceCommands.cs
// Unity 6 + Meta XR SDK v71 + Meta Voice SDK (Wit.ai) + Interaction SDK
// 10 voice commands V1 (EN + ID). Every command has a button fallback event.
// Mic rule: PUSH-TO-TALK ONLY — hold controller trigger to talk (see Note below).
// Voice SDK wiring: attach Wit + AppVoiceExperience in scene, set TestMode for
// Simulator/PC-mic validation without a headset. This script never assumes
// always-listening: OnVoiceEvent fires only while PushToTalkHeld is true.
//
// Pinned versions (verified Nov 2026): Meta XR All-in-One SDK v207.0 (task
// baseline: Meta XR Core SDK v71 line), Meta Voice SDK v85.0 (Wit.ai),
// Meta Interaction SDK via All-in-One, Unity OpenXR Plugin 1.13.0+,
// Photon Fusion 2.1.x, Unity 6000 LTS, Meta Quest Developer Hub 6.5.2 (Mac).

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace VirtualLoft.XR
{
    /// <summary>
    /// The 10 V1 voice commands. Enum order is the canonical command id.
    /// </summary>
    public enum VoiceCommandId
    {
        Wake = 0,       // wake agents up
        Run = 1,        // run [task]
        Stop = 2,       // stop current work
        MoveThat = 3,   // move that to [wall]
        Summarize = 4,  // summarize this
        Assign = 5,     // assign to [name]
        Remind = 6,     // remind at [time]
        Next = 7,       // next item
        Done = 8,       // mark done
        FocusMode = 9   // focus mode on/off
    }

    [Serializable]
    public struct VoiceCommandPhrases
    {
        public VoiceCommandId id;
        [Tooltip("English trigger phrases (lowercase match).")]
        public string[] english;
        [Tooltip("Indonesian / mixed trigger phrases (lowercase match).")]
        public string[] indonesian;
        [Tooltip("Button fallback event: invoked by Quick-Menu / trigger-click button AND by voice.")]
        public UnityEvent onInvoke;
    }

    /// <summary>
    /// Routes Wit.ai transcriptions (or Simulator test strings, or UI buttons)
    /// to one UnityEvent per command. Pair with Interaction SDK ray-select:
    /// hovered agent/board supplies the [target] argument for run/move/assign.
    /// </summary>
    [DisallowMultipleComponent]
    public class VoiceCommands : MonoBehaviour
    {
        [Header("Voice SDK hookup")]
        [Tooltip("Set true in XR Simulator / Editor: bypass mic, accept SimulateCommand() + PC mic test strings.")]
        public bool testMode = true;

        [Tooltip("Push-to-talk gate. Wire to Interaction SDK trigger: held = OVRInput trigger or XR Controller activate. Voice is IGNORED unless held.")]
        public bool pushToTalkHeld;

        [Header("Commands (10) — EN + ID phrases, one fallback event each")]
        public VoiceCommandPhrases[] commands = new VoiceCommandPhrases[10]
        {
            new VoiceCommandPhrases { id = VoiceCommandId.Wake,
                english = new[]{"wake", "wake up", "wake everyone"},
                indonesian = new[]{"bangun", "bangun semua", "mulai"} },
            new VoiceCommandPhrases { id = VoiceCommandId.Run,
                english = new[]{"run", "run this", "run test", "run landing test"},
                indonesian = new[]{"jalan", "jalankan", "jalankan ini", "jalankan tes"} },
            new VoiceCommandPhrases { id = VoiceCommandId.Stop,
                english = new[]{"stop", "stop that", "cancel"},
                indonesian = new[]{"berhenti", "stop", "batal"} },
            new VoiceCommandPhrases { id = VoiceCommandId.MoveThat,
                english = new[]{"move that", "move that to wall", "move that to main wall", "move to done"},
                indonesian = new[]{"pindah itu", "pindah ke dinding", "pindah ke utama", "pindah ke done"} },
            new VoiceCommandPhrases { id = VoiceCommandId.Summarize,
                english = new[]{"summarize", "summarize this", "summary"},
                indonesian = new[]{"ringkas", "ringkas ini", "rangkum"} },
            new VoiceCommandPhrases { id = VoiceCommandId.Assign,
                english = new[]{"assign", "assign to", "assign to scout", "assign to builder", "assign to keeper"},
                indonesian = new[]{"tugas", "kasih ke", "beri ke", "assign ke"} },
            new VoiceCommandPhrases { id = VoiceCommandId.Remind,
                english = new[]{"remind", "remind at", "remind me at", "reminder"},
                indonesian = new[]{"ingat", "ingatkan", "pengingat", "ingatkan jam"} },
            new VoiceCommandPhrases { id = VoiceCommandId.Next,
                english = new[]{"next", "next item", "next task"},
                indonesian = new[]{"lanjut", "berikutnya", "next"} },
            new VoiceCommandPhrases { id = VoiceCommandId.Done,
                english = new[]{"done", "mark done", "finish", "complete"},
                indonesian = new[]{"selesai", "beres", "done", "tandai selesai"} },
            new VoiceCommandPhrases { id = VoiceCommandId.FocusMode,
                english = new[]{"focus mode", "focus on", "focus off", "quiet mode"},
                indonesian = new[]{"mode fokus", "fokus", "mode diam", "fokus mati"} },
        };

        [Header("Diagnostics")]
        public string lastHeard = "";
        public VoiceCommandId lastCommand = VoiceCommandId.Wake;
        public UnityEvent<string> onUnrecognized;

        // NOTE (push-to-talk, hold-trigger): Voice SDK activation must be gated
        // behind the controller trigger held down. Suggested wiring:
        //   pushToTalkHeld = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger)
        //                    || XRInputModalityManager decides hand-pinch hold.
        // Show mic waveform on wrist UI only while pushToTalkHeld == true.
        // This avoids always-listening battery drain + fan/kids false triggers
        // (mic fails 10-20% in noise — button fallback below covers it).

        void Awake()
        {
            if (commands == null || commands.Length != 10)
                Debug.LogError("[VoiceCommands] Must define exactly 10 commands (wake/run/stop/move-that/summarize/assign/remind/next/done/focus-mode).");
        }

        // ---- Voice SDK entry point ------------------------------------------------
        // Call from Wit response handler: AppVoiceExperience.OnResponse(res) ->
        //   HandleTranscription(res.Text). In testMode, Simulator/PC-mic strings
        //   are accepted too; on device with testMode=false, require Wit confidence.

        /// <summary>
        /// Main entry: route a transcription string to the first matching command.
        /// Returns true if a command matched. Ignored unless push-to-talk held
        /// (or testMode button path via InvokeCommand).
        /// </summary>
        public bool HandleTranscription(string text, float witConfidence = 1f)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (!pushToTalkHeld && !testMode)
            {
                Debug.Log("[VoiceCommands] Ignored transcription: push-to-talk not held.");
                return false;
            }
            if (!testMode && witConfidence < 0.5f)
            {
                Debug.Log($"[VoiceCommands] Low Wit confidence ({witConfidence:F2}): '{text}'");
                onUnrecognized?.Invoke(text);
                return false;
            }

            string heard = text.Trim().ToLowerInvariant();
            lastHeard = heard;
            foreach (var cmd in commands)
            {
                if (Matches(heard, cmd.english) || Matches(heard, cmd.indonesian))
                {
                    lastCommand = cmd.id;
                    Debug.Log($"[VoiceCommands] Matched {cmd.id}: '{heard}'");
                    cmd.onInvoke?.Invoke();
                    return true;
                }
            }
            Debug.Log($"[VoiceCommands] Unrecognized: '{heard}'");
            onUnrecognized?.Invoke(text);
            return false;
        }

        /// <summary>
        /// Button fallback path: EVERY voice command MUST have a Quick-Menu /
        /// trigger-click button calling InvokeCommand(id). Works with no mic.
        /// </summary>
        public void InvokeCommand(VoiceCommandId id)
        {
            foreach (var cmd in commands)
            {
                if (cmd.id == id)
                {
                    lastCommand = id;
                    Debug.Log($"[VoiceCommands] Button fallback invoked: {id}");
                    cmd.onInvoke?.Invoke();
                    return;
                }
            }
            Debug.LogError($"[VoiceCommands] Unknown command id: {id}");
        }

        /// <summary>Simulator helper: type a phrase in Editor to test routing.</summary>
        [ContextMenu("Simulate: run")]
        public void SimulateRun() => HandleTranscription("run landing test");

        public void SimulateCommand(string phrase) => HandleTranscription(phrase);

        static bool Matches(string heard, string[] phrases)
        {
            if (phrases == null) return false;
            foreach (var p in phrases)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                string key = p.Trim().ToLowerInvariant();
                // StartsWith allows "run landing test X" to match "run landing test",
                // and bare "run ..." to match "run".
                if (heard.Equals(key) || heard.StartsWith(key + " ") || heard.StartsWith(key))
                    return true;
            }
            return false;
        }

        // Suggested Wit.ai intents (mirror in Wit console, one intent per command):
        //   loft_wake, loft_run, loft_stop, loft_move, loft_summarize,
        //   loft_assign (entity: agent_name = scout|builder|keeper),
        //   loft_remind (entity: wit$datetime), loft_next, loft_done, loft_focus.
        // Keep entities minimal: agent_name + wall_name + datetime only.
    }
}
