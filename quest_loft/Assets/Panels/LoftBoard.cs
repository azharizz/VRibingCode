// VIRTUAL LOFT — LoftBoard.cs: one floating board. The shared frame (title bar to
// grab, corner to resize, X to close) around content drawn by BoardViews.
// One world canvas in millimetres; a single box collider takes all pointing, and
// `hots` map millimetre rects to actions, so no EventSystem is needed.

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VirtualLoft.XR;

namespace VirtualLoft.Panels
{
    public class LoftBoard : MonoBehaviour, IPointerTarget
    {
        public const float TITLE = 132f, GRIP = 150f, MIN_SCALE = 0.4f, MAX_SCALE = 4f;

        public string id, type;
        public float W, H;
        public PanelState state;
        public int builtVersion = -1;
        public int page, sel, scroll;          // per-board view state (review queue, file, scroll line)
        public string previewId, previewUrl, previewError, startUrl;
        public int frameBytes;                 // size of the frame on screen: skip decoding an unchanged one
        public RawImage frame;                 // live preview image
        public TMP_Text address, watchLabel;
        public string videoUrl;                // a real video file on this page: can open in 360
        public Texture2D previewTex;           // owned here: survives Refresh, freed by LoftPanels
        public Action<Vector3> onPointerUp = _ => { };

        class Hot { public Rect r; public Action act; public Action<Vector2> actAt; public float hold; public Image bar; }
        readonly List<Hot> hots = new List<Hot>();
        RectTransform pageRt, content;
        Image hoverBox, closeBack;
        BoxCollider box;

        enum Mode { None, Move, Resize, Hot }
        Mode mode;
        Hot pressed, hovered;
        float grabDist, startScale, startDist, holdTime;
        Vector3 grabOffset;
        bool pointing, changed;

        public static (float w, float h, string title, Color accent) Spec(string type)
        {
            switch (type)
            {
                case "kanban": return (2640f, 1560f, "Team board", PT.Info);
                case "review": return (1760f, 1600f, "Review", PT.Review);
                case "log": return (1560f, 1560f, "Live log", PT.Doing);
                case "notes": return (1900f, 1420f, "Notes", PT.Done);
                case "files": return (2400f, 1500f, "Files", PT.Todo);
                case "diff": return (2500f, 1560f, "Changes", PT.Add);
                default: return (2300f, 1600f, "Browser", PT.Info);
            }
        }

        public static LoftBoard Create(string id, string type, PanelState state, Transform parent = null)
        {
            var (w, h, title, accent) = Spec(type);
            var go = new GameObject("Board_" + type);
            if (parent != null) go.transform.SetParent(parent, false);
            var b = go.AddComponent<LoftBoard>();
            b.id = id; b.type = type; b.state = state; b.W = w; b.H = h;
            b.Build(title, accent);
            return b;
        }

        void Build(string title, Color accent)
        {
            var c = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            c.transform.SetParent(transform, false);
            var canvas = c.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            var rt = (RectTransform)c.transform;
            rt.sizeDelta = new Vector2(W, H);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one * 0.001f;
            rt.localPosition = Vector3.zero;

            pageRt = PanelKit.Rect(rt, "Page", 0, 0, W, H);
            PanelKit.Shade(pageRt, 0, 0, W, H, 60f);
            PanelKit.Box(pageRt, 0, 0, W, H, PT.Board, PT.RBoard, "Back");
            PanelKit.Outline(pageRt, 0, 0, W, H, PT.Edge, PT.RBoard);
            // title bar: the whole strip is the handle
            PanelKit.Dot(pageRt, 74f, TITLE / 2f, 30f, accent);
            PanelKit.Text(pageRt, title, 108f, 0, W * 0.6f, TITLE, PT.H2, PT.Text, true, TextAlignmentOptions.Left, 1, "Title");
            PanelKit.Box(pageRt, W / 2f - 90f, 28f, 180f, 12f, new Color(1, 1, 1, 0.22f), -1f, "Handle");
            closeBack = PanelKit.Box(pageRt, W - 118f, 26f, 80f, 80f, new Color(1, 1, 1, 0.08f), -1f, "CloseBack");
            PanelKit.IconX(pageRt, W - 78f, 66f, 30f, 7f, PT.Text2);
            PanelKit.Divider(pageRt, 0, TITLE, W);
            content = PanelKit.Rect(pageRt, "Content", 0, TITLE, W, H - TITLE);
            // resize corner: three diagonal strokes
            for (int i = 0; i < 3; i++)
            {
                float o = 36f + i * 26f;
                PanelKit.Line(pageRt, new Vector2(W - 28f - o, H - 28f), new Vector2(W - 28f, H - 28f - o), 7f, new Color(1, 1, 1, 0.34f));
            }
            hoverBox = PanelKit.Box(pageRt, 0, 0, 10, 10, new Color(1, 1, 1, 0.10f), PT.RSmall, "Hover");
            hoverBox.enabled = false;

            box = gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(W * 0.001f, H * 0.001f, 0.03f);
            Refresh();
        }

        public void Refresh()
        {
            hots.Clear();
            hovered = null; pressed = null;
            frame = null;
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var ch = content.GetChild(i);
                ch.SetParent(null, false); // Destroy waits for frame end: keep it out of this rebuild
                ch.gameObject.SetActive(false);
                LoftUtil.Kill(ch.gameObject);
            }
            if (state != null) { BoardViews.Draw(this, content, W, H - TITLE); builtVersion = state.version; }
            if (hoverBox != null) { hoverBox.transform.SetAsLastSibling(); hoverBox.enabled = false; }
        }

        // Content coordinates (top-left of the content area), like every PanelKit call.
        public void Click(float x, float y, float w, float h, Action act) =>
            hots.Add(new Hot { r = new Rect(x, y + TITLE, w, h), act = act });
        public void ClickAt(float x, float y, float w, float h, Action<Vector2> act) =>
            hots.Add(new Hot { r = new Rect(x, y + TITLE, w, h), actAt = act });
        public void Hold(float x, float y, float w, float h, float seconds, Color bar, Action act)
        {
            var im = PanelKit.Box(content, x, y + h - 12f, 1f, 12f, bar, -1f, "HoldBar");
            hots.Add(new Hot { r = new Rect(x, y + TITLE, w, h), act = act, hold = seconds, bar = im });
        }

        Vector2 ToMm(Vector3 world)
        {
            var l = transform.InverseTransformPoint(world);
            return new Vector2(l.x * 1000f + W / 2f, H / 2f - l.y * 1000f);
        }
        bool InClose(Vector2 p) => p.y < TITLE && p.x > W - TITLE;
        bool InCorner(Vector2 p) => p.x > W - GRIP && p.y > H - GRIP;
        Hot HotAt(Vector2 p)
        {
            for (int i = hots.Count - 1; i >= 0; i--) if (hots[i].r.Contains(p)) return hots[i];
            return null;
        }

        // ---- pointing ----
        public void OnHover(bool on)
        {
            pointing = on;
            if (!on) { hovered = null; if (hoverBox != null) hoverBox.enabled = false; if (closeBack != null) closeBack.color = new Color(1, 1, 1, 0.08f); }
        }

        public void OnPress(Ray r, RaycastHit h)
        {
            var p = ToMm(h.point);
            changed = false;
            if (InClose(p)) { LoftPanels.CloseBoard(this); return; }
            if (InCorner(p))
            {
                mode = Mode.Resize; startScale = transform.localScale.x;
                startDist = Mathf.Max(0.05f, Vector3.Distance(h.point, transform.position));
                return;
            }
            if (p.y < TITLE)
            {
                mode = Mode.Move; grabDist = h.distance; grabOffset = transform.position - h.point;
                return;
            }
            var hot = HotAt(p);
            if (hot == null) return;
            if (hot.hold > 0f) { mode = Mode.Hot; pressed = hot; holdTime = 0f; return; }
            LoftAudio.Play("sfx_click", 0.6f);
            hot.act?.Invoke();
            hot.actAt?.Invoke(new Vector2(p.x - hot.r.x, p.y - hot.r.y) / new Vector2(hot.r.width, hot.r.height));
        }

        public void OnDrag(Ray r)
        {
            if (mode == Mode.Move)
            {
                var pos = r.origin + r.direction * grabDist + grabOffset;
                float half = H * 0.0005f * transform.localScale.x;
                pos.y = Mathf.Max(pos.y, half + 0.05f);
                transform.position = pos;
                LoftPanels.FaceViewer(transform);
                changed = true;
            }
            else if (mode == Mode.Resize)
            {
                var plane = new Plane(transform.forward, transform.position);
                if (!plane.Raycast(r, out float d)) return;
                float now = Vector3.Distance(r.GetPoint(d), transform.position);
                float s = Mathf.Clamp(startScale * now / startDist, MIN_SCALE, MAX_SCALE);
                transform.localScale = Vector3.one * s;
                changed = true;
            }
        }

        public void OnRelease()
        {
            if (changed) LoftPanels.SaveSoon();
            mode = Mode.None; pressed = null; holdTime = 0f; changed = false;
            foreach (var h in hots) if (h.bar != null) h.bar.rectTransform.sizeDelta = new Vector2(1f, 12f);
        }

        void Update()
        {
            if (mode == Mode.Hot && pressed != null)
            {
                holdTime += Time.unscaledDeltaTime;
                if (pressed.bar != null) pressed.bar.rectTransform.sizeDelta = new Vector2(Mathf.Max(12f, pressed.r.width * Mathf.Clamp01(holdTime / pressed.hold)), 12f);
                if (holdTime >= pressed.hold)
                {
                    var act = pressed.act; pressed = null; mode = Mode.None;
                    LoftAudio.Play("sfx_click", 0.8f);
                    act?.Invoke();
                }
            }
            if (!pointing || box == null || !LoftPanels.TryPointer(out var ray)) return;
            if (!box.Raycast(ray, out var hit, 40f)) return;
            var p = ToMm(hit.point);
            closeBack.color = InClose(p) ? new Color(PT.Bad.r, PT.Bad.g, PT.Bad.b, 0.5f) : new Color(1, 1, 1, 0.08f);
            var hot = HotAt(p);
            if (hot == hovered) return;
            hovered = hot;
            hoverBox.enabled = hot != null && hot.actAt == null;
            if (hoverBox.enabled)
            {
                hoverBox.rectTransform.anchoredPosition = new Vector2(hot.r.x, -hot.r.y);
                hoverBox.rectTransform.sizeDelta = hot.r.size;
            }
        }
    }
}
