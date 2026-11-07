// VIRTUAL LOFT — LoftCinema.cs: watch a 360 video all around you. The world stops being
// drawn (camera mask), the video plays with sound on a sphere around the head, and the card,
// wrist menu and pointer stay usable. Agents keep working; B or Exit brings the room back.

using TMPro;
using UnityEngine;
using UnityEngine.Video;

namespace VirtualLoft.XR
{
    public class LoftCinema : MonoBehaviour
    {
        public const int LAYER = 5; // Unity's UI layer: what stays visible while watching
        public LoftWorld world;
        public LoftDesk desk;
        public LoftInteraction interaction;
        public bool Active { get; private set; }

        VideoPlayer player;
        RenderTexture target;
        Transform sphere, panel;
        LoftButton playBtn;
        TMP_Text info, toast;
        int savedMask;
        CameraClearFlags savedClear;
        float layerTick, toastUntil;
        string reviewSeen;
        AudioSource[] muted;
        float[] volumes;

        public void Enter(string url)
        {
            if (Active) Exit();
            if (world == null || world.cam == null) return;
            EnsureBuilt();
            Active = true;
            reviewSeen = desk != null ? desk.ReviewTask()?.id : null;
            savedMask = world.cam.cullingMask;
            world.cam.cullingMask = 1 << LAYER;
            savedClear = world.cam.clearFlags;
            world.cam.clearFlags = CameraClearFlags.SolidColor; // the skybox draws after solid objects, over the sphere
            world.cam.backgroundColor = Color.black;
            interaction.pointMask = 1 << LAYER;

            muted = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            volumes = new float[muted.Length];
            for (int i = 0; i < muted.Length; i++) { volumes[i] = muted[i].volume; muted[i].volume = 0f; }

            // Start with the middle of the video in front of the CEO.
            var head = world.cam.transform;
            var fwd = head.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude < 1e-4f ? Vector3.forward : fwd.normalized;
            sphere.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            sphere.gameObject.SetActive(true);
            panel.gameObject.SetActive(true);
            panel.position = head.position + fwd * 1.3f + Vector3.down * 0.6f;
            panel.rotation = Quaternion.LookRotation(head.position - panel.position, Vector3.up);
            info.text = "Loading the video...";
            player.url = url;
            player.Prepare();
        }

        public void Exit()
        {
            if (!Active) return;
            Active = false;
            player.Stop();
            sphere.gameObject.SetActive(false);
            panel.gameObject.SetActive(false);
            world.cam.cullingMask = savedMask;
            world.cam.clearFlags = savedClear;
            interaction.pointMask = ~0;
            for (int i = 0; i < muted.Length; i++) if (muted[i] != null) muted[i].volume = volumes[i];
        }

        void EnsureBuilt()
        {
            if (player != null) return;
            sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
            sphere.name = "Cinema360";
            LoftUtil.Kill(sphere.GetComponent<Collider>());
            sphere.localScale = Vector3.one * 60f;
            sphere.gameObject.layer = LAYER;
            var mat = new Material(Resources.Load<Shader>("Shaders/LoftVideo360"));
            sphere.GetComponent<Renderer>().sharedMaterial = mat;
            sphere.gameObject.SetActive(false);

            target = new RenderTexture(2880, 1440, 0, RenderTextureFormat.ARGB32) { wrapMode = TextureWrapMode.Repeat, name = "Cinema" };
            mat.mainTexture = target;
            player = gameObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.isLooping = true;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = target;
            player.audioOutputMode = VideoAudioOutputMode.Direct;
            player.prepareCompleted += p => { p.Play(); };
            player.errorReceived += (p, msg) => { Debug.LogWarning("[Cinema] " + msg); if (info != null) info.text = "This video could not play."; };

            panel = new GameObject("CinemaPanel").transform;
            LoftUi.Quad(panel, "Back", new Vector3(0f, 0f, -0.012f), new Vector2(0.78f, 0.25f), LoftUi.Panel, 0.012f, collider: true);
            info = LoftUi.Text(panel, "Info", new Vector3(0f, 0.075f, 0f), 0.03f, LoftUi.Soft, 0.72f);
            toast = LoftUi.Text(panel, "Toast", new Vector3(0f, 0.165f, 0f), 0.03f, LoftUi.Accent, 0.9f);
            // UI +X is the viewer's left.
            LoftButton.Make(panel, "c_back", new Vector3(0.285f, -0.04f, 0f), new Vector2(0.17f, 0.1f), "-10 s", LoftUi.Neutral, 0.03f, () => Seek(-10));
            playBtn = LoftButton.Make(panel, "c_play", new Vector3(0.095f, -0.04f, 0f), new Vector2(0.17f, 0.1f), "Pause", LoftUi.Neutral, 0.03f, TogglePlay);
            LoftButton.Make(panel, "c_fwd", new Vector3(-0.095f, -0.04f, 0f), new Vector2(0.17f, 0.1f), "+10 s", LoftUi.Neutral, 0.03f, () => Seek(10));
            LoftButton.Make(panel, "c_exit", new Vector3(-0.285f, -0.04f, 0f), new Vector2(0.17f, 0.1f), LoftWords.Key("Exit", "B"), LoftUi.Danger, 0.03f, Exit);
            panel.gameObject.SetActive(false);
            SetLayer(panel);
        }

        void TogglePlay()
        {
            if (player.isPlaying) { player.Pause(); playBtn.SetText("Play"); }
            else { player.Play(); playBtn.SetText("Pause"); }
        }

        void Seek(double delta)
        {
            if (player.length > 0) player.time = System.Math.Max(0, System.Math.Min(player.length - 1, player.time + delta));
        }

        static void SetLayer(Transform t)
        {
            t.gameObject.layer = LAYER;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i));
        }

        static string Clock(double s) => $"{(int)s / 60}:{(int)s % 60:00}";

        void Update()
        {
            if (!Active) return;
            sphere.position = world.cam.transform.position; // far away, so the head never leaves the middle
            if (player.isPrepared && player.length > 0) info.text = $"{Clock(player.time)} / {Clock(player.length)}";

            layerTick += Time.unscaledDeltaTime;
            if (layerTick > 0.4f)
            {   // these rebuild their children, and new children start on the default layer
                layerTick = 0f;
                interaction.KeepVisible(SetLayer);
                SetLayer(panel);
                var review = desk != null ? desk.ReviewTask() : null;
                if (review != null && review.id != reviewSeen)
                {
                    reviewSeen = review.id;
                    toast.text = "An agent finished. Exit to review it.";
                    toastUntil = Time.unscaledTime + 8f;
                    LoftAudio.Play("sfx_done", 0.6f);
                }
            }
            if (!string.IsNullOrEmpty(toast.text) && Time.unscaledTime > toastUntil) toast.text = "";
        }

        void OnDestroy() { if (target != null) target.Release(); }
    }
}
