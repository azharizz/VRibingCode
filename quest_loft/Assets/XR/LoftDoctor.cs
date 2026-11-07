// VIRTUAL LOFT — LoftDoctor.cs: what we can't see on a headset we don't have.
//   LoftLog      keeps recent log lines in memory and in loft-log.txt
//   FrameGovernor lowers quality when frames drop, and never raises it again
//   LoftDoctor   reports to the server, and says on the card when something is wrong
// A helper only has to open the app: the report arrives in the backend's logs.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.XR;

namespace VirtualLoft.XR
{
    public static class LoftLog
    {
        public const int Keep = 300;
        static readonly object gate = new object();
        static readonly Queue<string> lines = new Queue<string>();
        static readonly Queue<string> unsent = new Queue<string>();
        public static int Exceptions { get; private set; }
        public static string File => Path.Combine(Application.persistentDataPath, "loft-log.txt");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Start() => Application.logMessageReceivedThreaded += Add;

        // The emulator and Simulator say this on every run: it isn't news.
        static bool Noise(string text) => text.StartsWith("[XR]") || text.Contains("AssetPackManager");

        public static void Add(string text, string stack, LogType type)
        {
            bool bad = (type == LogType.Exception || type == LogType.Error) && !Noise(text);
            lock (gate)
            {
                lines.Enqueue($"{DateTime.UtcNow:HH:mm:ss} {type}: {text}");
                while (lines.Count > Keep) lines.Dequeue();
                if (type == LogType.Exception) Exceptions++;
                if (bad && unsent.Count < 20) unsent.Enqueue($"{type}: {text}\n{stack}");
            }
        }

        public static string NextUnsent() { lock (gate) return unsent.Count > 0 ? unsent.Dequeue() : null; }
        public static string[] Recent() { lock (gate) return lines.ToArray(); }

        public static void Save()
        {
            try { System.IO.File.WriteAllLines(File, Recent()); } catch (IOException) { }
        }
    }

    public class FrameGovernor
    {
        public const float SlowFps = 58f, Settle = 10f, Window = 5f, LowScale = 0.75f;
        public bool Low { get; private set; }
        public float Fps { get; private set; }
        float age, spent; int frames;

        // True on the frame quality should drop.
        public bool Step(float dt)
        {
            // A long gap is a pause (headset off, a system dialog), not a slow frame: start the window over.
            if (dt > 0.25f) { spent = 0f; frames = 0; return false; }
            age += dt; spent += dt; frames++;
            if (spent < Window) return false;
            Fps = frames / spent; spent = 0f; frames = 0;
            if (Low || age < Settle || Fps >= SlowFps) return false;
            return Low = true;
        }
    }

    public class LoftDoctor : MonoBehaviour
    {
        public const float OfflineWarn = 20f, FirstReport = 15f, Report = 60f;
        public static bool LowQuality { get; private set; }
        public static bool ShouldWarnOffline(float seconds, bool warned) => seconds >= OfflineWarn && !warned;
        public static string OfflineMessage => "Can't reach the Virtual Loft server. Check the headset's internet. It keeps trying by itself.";

        public LoftInteraction interaction;
        public LoftClient client;
        readonly FrameGovernor governor = new FrameGovernor();
        float offline, nextReport = FirstReport, nextSend, nextSave;
        bool warnedOffline, warnedCrash;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (governor.Step(dt) && Application.isMobilePlatform) GoLow(); // a desktop simulator's frame rate says nothing
            if (interaction == null || interaction.Desk == null || client == null) return;

            offline = interaction.Desk.Connected ? 0f : offline + dt;
            if (offline == 0f) warnedOffline = false;
            if (ShouldWarnOffline(offline, warnedOffline)) { warnedOffline = true; interaction.Tell(OfflineMessage, true); }

            if (LoftLog.Exceptions > 0 && !warnedCrash)
            {
                warnedCrash = true;
                LoftLog.Save();
                interaction.Tell("Something went wrong inside the app. A report was sent. You can keep going.", true);
            }
            if (Time.unscaledTime >= nextSave) { nextSave = Time.unscaledTime + 30f; LoftLog.Save(); }
            if (Time.unscaledTime >= nextSend)
            {
                var problem = LoftLog.NextUnsent();
                if (problem != null) { nextSend = Time.unscaledTime + 3f; Send("error", problem); }
            }
            if (Time.unscaledTime >= nextReport && interaction.Desk.Connected)
            {
                nextReport = Time.unscaledTime + Report;
                var status = Status();
                Debug.Log("[Doctor] " + status);
                Send("status", status);
            }
        }

        void Send(string kind, string text) => _ = client.ClientLog(kind, text);

        string Status() =>
            $"{SystemInfo.deviceModel} | {SystemInfo.operatingSystem} | {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceName} | ram {SystemInfo.systemMemorySize} MB | " +
            $"xr {XRSettings.isDeviceActive} {XRSettings.loadedDeviceName} | hands {LoftWords.Hands} | fps {governor.Fps:0.0} low {LowQuality} | " +
            $"mic {interaction.voice?.permission} device {interaction.voice?.HasDevice} | exceptions {LoftLog.Exceptions} | scene {interaction.World?.Current?.id} | {SceneCost()}";

        // What the GPU is asked to draw now. A Quest wants roughly under 150 draws and 500k triangles.
        static string SceneCost()
        {
            int draws = 0; long tris = 0;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var mesh = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                draws += mesh.subMeshCount;
                for (int i = 0; i < mesh.subMeshCount; i++) tris += mesh.GetIndexCount(i) / 3;
            }
            return $"{draws} draws {tris} triangles";
        }

        // Fewer pixels is the one cut that helps whatever the cause, and nobody has to choose it.
        void GoLow()
        {
            LowQuality = true;
            XRSettings.eyeTextureResolutionScale = FrameGovernor.LowScale;
            QualitySettings.antiAliasing = 0;
            Debug.Log($"[Doctor] {governor.Fps:0} fps: quality lowered");
        }
    }
}
