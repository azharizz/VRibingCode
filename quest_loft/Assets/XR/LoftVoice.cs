// VIRTUAL LOFT — LoftVoice.cs: push-to-talk microphone capture.
// Hold to record (Unity Microphone, 16 kHz mono), release to get a 16-bit WAV
// the backend's POST /voice accepts. Never always-listening. Handles the
// runtime mic permission (Android Permission.Microphone / macOS authorization)
// and reports a plain-language reason whenever recording cannot work.

using System;
using System.Text;
using UnityEngine;

namespace VirtualLoft.XR
{
    public enum MicPermission { Unknown, Asking, Granted, Denied }

    public class LoftVoice : MonoBehaviour
    {
        public const int SampleRate = 16000;
        public const float MaxSeconds = 20f;   // hard stop; 20 s of 16 kHz mono is 640 KB
        public const float MinSeconds = 0.4f;  // shorter = accidental tap, ignored
        public const float SilentPeak = 0.004f;

        public MicPermission permission = MicPermission.Unknown;
        public bool forceDenied;   // tests: pretend the CEO refused the permission
        public bool Recording { get; private set; }
        public float Level { get; private set; }   // 0..1 live input level for the card
        public float Elapsed => Recording ? Time.realtimeSinceStartup - startedAt : 0f;
        public bool HitMax { get; private set; }

        AudioClip clip;
        string device;
        float startedAt;

        public struct Take
        {
            public byte[] wav;
            public float seconds, peak;
            public string problem;   // null = usable
            public bool tap;         // too short: ignore silently
        }

        // ---- permission -------------------------------------------------------
        public bool HasDevice => Microphone.devices != null && Microphone.devices.Length > 0;

        // Refreshes `permission` from the OS and, when still unknown, asks.
        public void CheckPermission(bool ask)
        {
            if (forceDenied) { permission = MicPermission.Denied; return; }
#if UNITY_ANDROID && !UNITY_EDITOR
            if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone)) { permission = MicPermission.Granted; return; }
            if (permission == MicPermission.Denied || permission == MicPermission.Asking) return;
            if (!ask) return;
            permission = MicPermission.Asking;
            var cb = new UnityEngine.Android.PermissionCallbacks();
            cb.PermissionGranted += _ => permission = MicPermission.Granted;
            cb.PermissionDenied += _ => permission = MicPermission.Denied;
            cb.PermissionDeniedAndDontAskAgain += _ => permission = MicPermission.Denied;
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, cb);
#else
            if (Application.HasUserAuthorization(UserAuthorization.Microphone)) { permission = MicPermission.Granted; return; }
            if (permission == MicPermission.Asking) return; // the popup may still be open: never treat waiting as a refusal
            if (permission == MicPermission.Denied || !ask) return;
            permission = MicPermission.Asking;
            Application.RequestUserAuthorization(UserAuthorization.Microphone);
#endif
        }

        // Null when we can record, else the sentence to show the CEO.
        public string Problem()
        {
            if (permission == MicPermission.Denied)
                return Application.platform == RuntimePlatform.Android
                    ? "Microphone is blocked. Allow it in Quest Settings > Apps > Permissions, then try again."
                    : "Microphone is blocked. Allow Unity/Loft in System Settings > Privacy > Microphone.";
            if (permission == MicPermission.Asking) return "Allow the microphone in the popup, or in System Settings > Privacy > Microphone, then try again.";
            if (permission == MicPermission.Unknown) return "Microphone permission not asked yet. Try again.";
            if (!HasDevice) return "No microphone found.";
            return null;
        }

        // ---- capture ------------------------------------------------------------
        // Returns null when recording started, else why not.
        public string Begin()
        {
            if (Recording) return null;
            CheckPermission(true);
            var why = Problem();
            if (why != null) return why;
            device = null; // default input
            clip = Microphone.Start(device, false, (int)MaxSeconds, SampleRate);
            if (clip == null) return "Could not start the microphone.";
            Recording = true;
            HitMax = false;
            startedAt = Time.realtimeSinceStartup;
            return null;
        }

        void Update()
        {
            if (!Recording) return;
            int pos = Microphone.GetPosition(device);
            if (pos <= 0 && Elapsed > 1.5f) { Cancel(); return; }
            if (pos > 0 && clip != null)
            {
                // Level = peak of the last ~50 ms.
                int n = Mathf.Min(clip.frequency / 20, pos);
                var buf = new float[n * clip.channels];
                if (clip.GetData(buf, pos - n)) Level = Mathf.Clamp01(Peak(buf, buf.Length) * 3f);
            }
            if (Elapsed >= MaxSeconds - 0.1f) HitMax = true;
        }

        public void Cancel()
        {
            if (!Recording) return;
            Microphone.End(device);
            Recording = false;
            Level = 0f;
        }

        public Take End()
        {
            var take = new Take();
            if (!Recording) { take.problem = "Not recording."; return take; }
            int pos = Microphone.GetPosition(device);
            Microphone.End(device);
            Recording = false;
            Level = 0f;
            if (clip == null || pos <= 0) { take.problem = "The microphone gave no audio."; return take; }
            var data = new float[pos * clip.channels];
            clip.GetData(data, 0);
            take = Encode(data, clip.frequency, clip.channels);
            return take;
        }

        // Float PCM (any rate/channels) -> usable 16 kHz mono WAV, or a problem.
        public static Take Encode(float[] data, int rate, int channels)
        {
            var t = new Take();
            var mono = ToMono16k(data, rate, channels);
            t.seconds = mono.Length / (float)SampleRate;
            t.peak = Peak(mono, mono.Length);
            if (t.seconds < MinSeconds) { t.tap = true; t.problem = "Too short."; return t; }
            if (t.peak < SilentPeak) { t.problem = "I heard silence. Check the microphone."; return t; }
            t.wav = Wav(mono, SampleRate);
            return t;
        }

        static float Peak(float[] d, int n)
        {
            float p = 0f;
            for (int i = 0; i < n; i++) { float a = Mathf.Abs(d[i]); if (a > p) p = a; }
            return p;
        }

        // Downmix + box-filter resample to 16 kHz.
        public static float[] ToMono16k(float[] data, int rate, int channels)
        {
            channels = Mathf.Max(1, channels);
            int frames = data.Length / channels;
            var mono = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                float s = 0f;
                for (int c = 0; c < channels; c++) s += data[i * channels + c];
                mono[i] = s / channels;
            }
            if (rate == SampleRate || rate <= 0) return mono;
            double ratio = (double)rate / SampleRate;
            int outLen = (int)(frames / ratio);
            var res = new float[outLen];
            for (int i = 0; i < outLen; i++)
            {
                int a = (int)(i * ratio), b = Mathf.Min(frames, Mathf.Max(a + 1, (int)((i + 1) * ratio)));
                float s = 0f;
                for (int k = a; k < b; k++) s += mono[k];
                res[i] = s / (b - a);
            }
            return res;
        }

        // 44-byte RIFF header + little-endian 16-bit PCM.
        public static byte[] Wav(float[] mono, int rate)
        {
            int dataBytes = mono.Length * 2;
            var b = new byte[44 + dataBytes];
            void Str(int at, string s) { Encoding.ASCII.GetBytes(s, 0, 4, b, at); }
            void U32(int at, int v) { b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); b[at + 2] = (byte)(v >> 16); b[at + 3] = (byte)(v >> 24); }
            void U16(int at, int v) { b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); }
            Str(0, "RIFF"); U32(4, 36 + dataBytes); Str(8, "WAVE"); Str(12, "fmt ");
            U32(16, 16); U16(20, 1); U16(22, 1); U32(24, rate); U32(28, rate * 2); U16(32, 2); U16(34, 16);
            Str(36, "data"); U32(40, dataBytes);
            for (int i = 0; i < mono.Length; i++)
            {
                int v = Mathf.Clamp(Mathf.RoundToInt(mono[i] * 32767f), -32768, 32767);
                b[44 + i * 2] = (byte)v; b[45 + i * 2] = (byte)(v >> 8);
            }
            return b;
        }

        void OnDisable() { if (Recording) Cancel(); }
    }
}
