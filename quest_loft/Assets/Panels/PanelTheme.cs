// VIRTUAL LOFT — PanelTheme.cs: design tokens + generated sprites for boards.
// One unit = 1 mm at board scale 1. Boards are read from about 3 m:
// body 56 units = 56 mm font, ~63 mm line = 1.2 deg at 3 m (1.6 deg at 2.3 m).
// No font icons (the font lacks them): icons are shapes, see PanelKit.

using System.Collections.Generic;
using UnityEngine;

namespace VirtualLoft.Panels
{
    public static class PT
    {
        static Color C(string hex, float a = 1f) { ColorUtility.TryParseHtmlString("#" + hex, out var c); c.a = a; return c; }

        // ---- surfaces (dark, slightly blue, translucent board) ----
        public static readonly Color Board = C("0B111C", 0.93f);
        public static readonly Color Column = C("131B2A");          // lane / list well
        public static readonly Color Card = C("1B2536");            // card on a well
        public static readonly Color CardHi = C("26334A");          // hover / selected
        public static readonly Color Line = new Color(1f, 1f, 1f, 0.09f);
        public static readonly Color Edge = new Color(1f, 1f, 1f, 0.16f);
        public static readonly Color Shadow = new Color(0f, 0f, 0f, 0.45f);

        // ---- text ----
        public static readonly Color Text = C("F3F7FD");
        public static readonly Color Text2 = C("B4C0D4");           // secondary (>= 7:1 on Card)
        public static readonly Color Muted = C("8593AB");           // meta (>= 4.5:1 on Card)
        public static readonly Color Ink = C("141A26");             // text on light fills

        // ---- one accent per status ----
        public static readonly Color Todo = C("8EA2FF");
        public static readonly Color Doing = C("FFC24D");
        public static readonly Color Review = C("C792FF");
        public static readonly Color Done = C("5FDB92");
        public static readonly Color Bad = C("FF6B6B");
        public static readonly Color Info = C("5CD6FF");            // brand accent

        public static readonly Color Add = C("5FDB92");
        public static readonly Color Del = C("FF7B7B");
        public static readonly Color AddBg = C("5FDB92", 0.16f);
        public static readonly Color DelBg = C("FF6B6B", 0.16f);

        // ---- type scale (mm font size) ----
        public const float H1 = 66, H2 = 60, Body = 54, Meta = 44, Mono = 42;
        // ---- spacing / radius ----
        public const float S1 = 8, S2 = 16, S3 = 24, S4 = 40;
        public const float RBoard = 56, RCard = 28, RSmall = 18;

        public static Color Status(string s)
        {
            switch (s)
            {
                case "queued": case "todo": return Todo;
                case "running": case "doing": return Doing;
                case "review": case "conflict": return Review;
                case "approved": case "done": return Done;
                case "failed": case "rejected": case "stopped": return Bad;
                default: return Info;
            }
        }

        public static string StatusWord(string s)
        {
            switch (s)
            {
                case "queued": return "Queued"; case "running": return "Working"; case "review": return "In review";
                case "approved": return "Done"; case "failed": return "Failed"; case "rejected": return "Redo";
                case "stopped": return "Stopped"; case "conflict": return "Conflict"; default: return s;
            }
        }

        static readonly Color[] AvatarCols =
        {
            C("FF9E57"), C("5CC8FF"), C("A98BFF"), C("5FDB92"), C("FF7BAC"), C("F2D14B"), C("4FD8C6"), C("FF8A6B"),
        };
        public static Color AvatarColor(string name)
        {
            int h = 7;
            foreach (char ch in name ?? "?") h = h * 31 + ch;
            return AvatarCols[(h & 0x7fffffff) % AvatarCols.Length];
        }
        public static string Initial(string name) => string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();

        // ---- generated sprites (cached, white, tinted by Image.color) ----
        public const int R = 24;                // corner radius in texels of the 9-slice
        static Sprite fill, stroke, shadow, circle, ring;
        static readonly List<Object> keep = new List<Object>();

        static Sprite Make(Texture2D t, float border)
        {
            t.hideFlags = HideFlags.HideAndDontSave;
            t.wrapMode = TextureWrapMode.Clamp; t.filterMode = FilterMode.Bilinear;
            t.Apply(false, true);
            var s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            s.hideFlags = HideFlags.HideAndDontSave;
            keep.Add(t); keep.Add(s);
            return s;
        }

        // signed distance to a rounded box of half size h and radius r (texels), 1 texel AA
        static float Sd(float x, float y, float hx, float hy, float r)
        {
            float qx = Mathf.Abs(x) - (hx - r), qy = Mathf.Abs(y) - (hy - r);
            return Mathf.Sqrt(Mathf.Max(qx, 0) * Mathf.Max(qx, 0) + Mathf.Max(qy, 0) * Mathf.Max(qy, 0)) + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
        }
        static float Cov(float d) => Mathf.Clamp01(0.5f - d);

        // 9-slice rounded rect. Image.pixelsPerUnitMultiplier = R / radius.
        public static Sprite Fill() { if (fill == null) fill = Rounded(false); return fill; }
        public static Sprite Stroke() { if (stroke == null) stroke = Rounded(true); return stroke; }
        static Sprite Rounded(bool outline)
        {
            int n = R * 2 + 4;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            float h = n / 2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Sd(x + 0.5f - h, y + 0.5f - h, h, h, R);
                    float a = Cov(d);
                    if (outline) a = Mathf.Min(a, 1f - Cov(d + 2.2f));
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            return Make(t, R);
        }

        // soft drop shadow: blurred rounded box, 9-slice. Draw it bigger than the panel.
        public static Sprite Shadow9()
        {
            if (shadow != null) return shadow;
            int n = 128; float h = n / 2f, pad = 44f;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Sd(x + 0.5f - h, y + 0.5f - h, h - pad, h - pad, 24f);
                    float a = Mathf.Pow(Mathf.Clamp01(1f - d / pad), 2.2f);
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            return shadow = Make(t, 56);
        }

        public static Sprite Circle()
        {
            if (circle != null) return circle;
            int n = 96; float h = n / 2f;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    t.SetPixel(x, y, new Color(1, 1, 1, Cov(Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(h, h)) - h + 0.5f)));
            circle = Make(t, 0);
            return circle;
        }

        public static Sprite Ring()
        {
            if (ring != null) return ring;
            int n = 96; float h = n / 2f;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(h, h)) - h + 0.5f;
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Min(Cov(d), 1f - Cov(d + 5f))));
                }
            ring = Make(t, 0);
            return ring;
        }

        public static float RadiusMul(float radius) => R / Mathf.Max(1f, radius);
    }
}
