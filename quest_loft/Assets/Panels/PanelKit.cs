// VIRTUAL LOFT — PanelKit.cs: the parts boards are assembled from.
// Coordinates are millimetres from the parent's TOP-LEFT, y grows DOWN.
// Everything is a uGUI Image or TMP text on one world canvas per board, so a
// whole board batches into a handful of draw calls (shapes, text, +1 per texture).
// Icons are drawn from shapes: the font has no symbols.

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VirtualLoft.Panels
{
    public static class PanelKit
    {
        // ---- layout primitives ----
        public static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        static Image Img(RectTransform rt, Sprite s, Color c, Image.Type type = Image.Type.Simple)
        {
            var im = rt.gameObject.AddComponent<Image>();
            im.sprite = s; im.color = c; im.type = type; im.raycastTarget = false;
            return im;
        }

        // Rounded rectangle. radius < 0 = pill.
        public static Image Box(Transform parent, float x, float y, float w, float h, Color c, float radius = PT.RCard, string name = "Box")
        {
            if (radius < 0f) radius = Mathf.Min(w, h) / 2f;
            radius = Mathf.Min(radius, Mathf.Min(w, h) / 2f);
            var im = Img(Rect(parent, name, x, y, w, h), PT.Fill(), c, Image.Type.Sliced);
            im.pixelsPerUnitMultiplier = PT.RadiusMul(radius);
            return im;
        }

        // Square-cornered flat rectangle (dividers, bars, diff rows).
        public static Image Flat(Transform parent, float x, float y, float w, float h, Color c, string name = "Flat")
        {
            var im = Img(Rect(parent, name, x, y, w, h), PT.Fill(), c, Image.Type.Sliced);
            im.pixelsPerUnitMultiplier = PT.R * 2f; // corner collapses to ~half a unit: effectively square
            return im;
        }

        public static Image Outline(Transform parent, float x, float y, float w, float h, Color c, float radius = PT.RCard)
        {
            if (radius < 0f) radius = Mathf.Min(w, h) / 2f;
            radius = Mathf.Min(radius, Mathf.Min(w, h) / 2f);
            var im = Img(Rect(parent, "Outline", x, y, w, h), PT.Stroke(), c, Image.Type.Sliced);
            im.pixelsPerUnitMultiplier = PT.RadiusMul(radius);
            return im;
        }

        public static Image Dot(Transform parent, float cx, float cy, float d, Color c) =>
            Img(Rect(parent, "Dot", cx - d / 2f, cy - d / 2f, d, d), PT.Circle(), c);

        public static Image RingShape(Transform parent, float cx, float cy, float d, Color c) =>
            Img(Rect(parent, "Ring", cx - d / 2f, cy - d / 2f, d, d), PT.Ring(), c);

        // A line segment (icons): thickness t, from a to b.
        public static Image Line(Transform parent, Vector2 a, Vector2 b, float t, Color c)
        {
            var im = Flat(parent, 0, 0, Vector2.Distance(a, b), t, c, "Line");
            var rt = im.rectTransform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2((a.x + b.x) / 2f, -(a.y + b.y) / 2f);
            rt.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
            // rounded caps look better than butt ends
            im.sprite = PT.Fill(); im.pixelsPerUnitMultiplier = PT.RadiusMul(t / 2f);
            return im;
        }

        public static void Shade(Transform parent, float x, float y, float w, float h, float spread = 44f)
        {
            var im = Img(Rect(parent, "Shadow", x - spread, y - spread + spread * 0.4f, w + spread * 2, h + spread * 2), PT.Shadow9(), PT.Shadow, Image.Type.Sliced);
            im.pixelsPerUnitMultiplier = 1f;
        }

        // ---- text ----
        public static TextMeshProUGUI Text(Transform parent, string s, float x, float y, float w, float h, float size, Color c,
            bool bold = false, TextAlignmentOptions align = TextAlignmentOptions.TopLeft, int maxLines = 1, string name = "Text")
        {
            float need = size * 1.3f * Mathf.Max(1, maxLines > 1 ? 1 : maxLines); // a line taller than its rect would vanish
            if (h < need) { if (align == TextAlignmentOptions.TopLeft || align == TextAlignmentOptions.Top) { } else y -= (need - h) / 2f; h = need; }
            var rt = Rect(parent, name, x, y, w, h);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.raycastTarget = false;
            t.font = TMP_Settings.defaultFontAsset;
            t.fontSize = size; t.color = c; t.alignment = align;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.margin = Vector4.zero;
            t.richText = false;
            t.textWrappingMode = maxLines > 1 ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.maxVisibleLines = maxLines;
            t.lineSpacing = -4f;
            t.text = s ?? "";
            return t;
        }

        // Monospace with the proportional font: fixed advance via <mspace>.
        public static TextMeshProUGUI Mono(Transform parent, string s, float x, float y, float w, float h, float size, Color c, bool rich = false)
        {
            var t = Text(parent, "", x, y, w, h, size, c, false, TextAlignmentOptions.Left, 1, "Mono");
            t.richText = true;
            t.overflowMode = TextOverflowModes.Truncate;
            t.text = "<mspace=" + (size * 0.6f).ToString("0.#") + ">" + (rich ? s : Esc(s)) + "</mspace>";
            return t;
        }

        public static string Esc(string s) => "<noparse>" + (s ?? "").Replace("</noparse>", "") + "</noparse>";

        // ---- parts ----
        public static void Avatar(Transform parent, float cx, float cy, float d, string name, Color? color = null)
        {
            var col = color ?? PT.AvatarColor(name);
            Dot(parent, cx, cy, d, col);
            Text(parent, PT.Initial(name), cx - d / 2f, cy - d / 2f, d, d, d * 0.52f, PT.Ink, true, TextAlignmentOptions.Center, 1, "Initial");
        }

        // Rounded label chip. Returns its width so rows can chain.
        public static float Pill(Transform parent, string s, float x, float y, float h, Color fill, Color text, float size = PT.Meta, bool bold = true, float padX = 24f)
        {
            float w = Measure(s, size, bold) + padX * 2f;
            Box(parent, x, y, w, h, fill, -1f, "Pill");
            Text(parent, s, x, y, w, h, size, text, bold, TextAlignmentOptions.Center, 1, "PillText");
            return w;
        }

        public static float PillRight(Transform parent, string s, float right, float y, float h, Color fill, Color text, float size = PT.Meta, bool bold = true)
        {
            float w = Measure(s, size, bold) + 48f;
            Pill(parent, s, right - w, y, h, fill, text, size, bold);
            return w;
        }

        // Count badge (a circle/pill with a number).
        public static void Badge(Transform parent, string s, float x, float y, float h, Color fill, Color text)
        {
            float w = Mathf.Max(h, Measure(s, h * 0.58f, true) + h * 0.6f);
            Box(parent, x, y, w, h, fill, -1f, "Badge");
            Text(parent, s, x, y, w, h, h * 0.58f, text, true, TextAlignmentOptions.Center, 1, "BadgeText");
        }

        public static void Progress(Transform parent, float x, float y, float w, float h, float frac, Color c)
        {
            Box(parent, x, y, w, h, new Color(1, 1, 1, 0.10f), -1f, "Track");
            if (frac > 0.01f) Box(parent, x, y, Mathf.Max(h, w * Mathf.Clamp01(frac)), h, c, -1f, "Fill");
        }

        // Plain tab. Returns width.
        public static float Tab(Transform parent, string s, float x, float y, float h, bool on, Color accent)
        {
            float w = Measure(s, PT.Body, on) + 56f;
            if (on) Box(parent, x, y, w, h, accent * new Color(1, 1, 1, 0.20f), -1f, "Tab");
            Text(parent, s, x, y, w, h, PT.Body, on ? accent : PT.Text2, on, TextAlignmentOptions.Center, 1, "TabText");
            return w;
        }

        public static void Divider(Transform parent, float x, float y, float w) => Flat(parent, x, y, w, 2f, PT.Line, "Divider");

        // ---- icons (shapes) ----
        public static void IconX(Transform parent, float cx, float cy, float s, float t, Color c)
        {
            Line(parent, new Vector2(cx - s / 2, cy - s / 2), new Vector2(cx + s / 2, cy + s / 2), t, c);
            Line(parent, new Vector2(cx - s / 2, cy + s / 2), new Vector2(cx + s / 2, cy - s / 2), t, c);
        }
        public static void IconCheck(Transform parent, float cx, float cy, float s, float t, Color c)
        {
            Line(parent, new Vector2(cx - s * 0.5f, cy + s * 0.02f), new Vector2(cx - s * 0.15f, cy + s * 0.38f), t, c);
            Line(parent, new Vector2(cx - s * 0.15f, cy + s * 0.38f), new Vector2(cx + s * 0.5f, cy - s * 0.38f), t, c);
        }
        public static void IconChevron(Transform parent, float cx, float cy, float s, float t, Color c, int dir /* -1 left, 1 right */)
        {
            Line(parent, new Vector2(cx - dir * s * 0.25f, cy - s * 0.5f), new Vector2(cx + dir * s * 0.25f, cy), t, c);
            Line(parent, new Vector2(cx + dir * s * 0.25f, cy), new Vector2(cx - dir * s * 0.25f, cy + s * 0.5f), t, c);
        }
        public static void IconRefresh(Transform parent, float cx, float cy, float s, float t, Color c)
        {
            RingShape(parent, cx, cy, s, c).color = c;
            Line(parent, new Vector2(cx + s * 0.18f, cy - s * 0.5f), new Vector2(cx + s * 0.52f, cy - s * 0.5f), t, c);
            Line(parent, new Vector2(cx + s * 0.52f, cy - s * 0.5f), new Vector2(cx + s * 0.52f, cy - s * 0.16f), t, c);
        }
        // folder: tab + body ; file: page with a folded corner (a smaller square)
        public static void IconFolder(Transform parent, float x, float y, float s, Color c)
        {
            Box(parent, x, y, s * 0.46f, s * 0.3f, c, s * 0.08f, "FolderTab");
            Box(parent, x, y + s * 0.16f, s, s * 0.68f, c, s * 0.12f, "Folder");
        }
        public static void IconFile(Transform parent, float x, float y, float s, Color c)
        {
            Box(parent, x + s * 0.12f, y, s * 0.76f, s, c, s * 0.12f, "File");
            Box(parent, x + s * 0.28f, y + s * 0.3f, s * 0.44f, s * 0.07f, PT.Board, -1f, "Ln");
            Box(parent, x + s * 0.28f, y + s * 0.5f, s * 0.44f, s * 0.07f, PT.Board, -1f, "Ln");
            Box(parent, x + s * 0.28f, y + s * 0.7f, s * 0.28f, s * 0.07f, PT.Board, -1f, "Ln");
        }

        // ---- text measurement (cheap, cached TMP measure) ----
        static TextMeshProUGUI probe;
        static GameObject probeGo;
        public static float Measure(string s, float size, bool bold)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            if (probe == null)
            {
                probeGo = new GameObject("PanelProbe", typeof(RectTransform), typeof(Canvas)) { hideFlags = HideFlags.HideAndDontSave };
                probe = new GameObject("t", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
                probe.transform.SetParent(probeGo.transform, false);
                probe.textWrappingMode = TextWrappingModes.NoWrap;
                probe.margin = Vector4.zero;
            }
            probe.font = TMP_Settings.defaultFontAsset;
            probe.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            probe.fontSize = size;
            return probe.GetPreferredValues(s).x;
        }

        // Fit a string to a width by trimming whole characters (TMP ellipsis does the rest).
        public static string Fit(string s, float width, float size, bool bold = false)
        {
            if (string.IsNullOrEmpty(s) || Measure(s, size, bold) <= width) return s ?? "";
            int lo = 1, hi = s.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Measure(s.Substring(0, mid) + "...", size, bold) <= width) lo = mid; else hi = mid - 1;
            }
            return s.Substring(0, lo).TrimEnd() + "...";
        }
    }
}
