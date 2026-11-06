// VIRTUAL LOFT — BoardViews.cs: what each board looks like, assembled from
// PanelKit parts. Every board is cards and hierarchy, never a block of text.

using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VirtualLoft.Panels
{
    public static class BoardViews
    {
        const float PAD = 48f;

        public static void Draw(LoftBoard b, RectTransform c, float w, float h)
        {
            switch (b.type)
            {
                case "kanban": Kanban(b, c, w, h); break;
                case "review": Review(b, c, w, h); break;
                case "log": Log(b, c, w, h); break;
                case "notes": Notes(b, c, w, h); break;
                case "files": Files(b, c, w, h); break;
                case "diff": Diff(b, c, w, h); break;
                default: Preview(b, c, w, h); break;
            }
        }

        static void Empty(RectTransform c, float w, float h, string title, string hint, Color accent)
        {
            PanelKit.RingShape(c, w / 2f, h / 2f - 130f, 150f, accent);
            PanelKit.IconCheck(c, w / 2f, h / 2f - 130f, 62f, 11f, accent);
            PanelKit.Text(c, title, PAD, h / 2f - 20f, w - PAD * 2, 90f, PT.H1, PT.Text, true, TextAlignmentOptions.Center);
            PanelKit.Text(c, hint, PAD * 2, h / 2f + 80f, w - PAD * 4, 150f, PT.Body, PT.Text2, false, TextAlignmentOptions.Top, 2);
        }

        // A filled action. Returns its rect so callers can attach a click.
        static void Btn(RectTransform c, float x, float y, float w, float h, string s, Color fill, Color text)
        {
            PanelKit.Box(c, x, y, w, h, fill, PT.RSmall, "Btn");
            PanelKit.Text(c, s, x, y, w, h, PT.Body, text, true, TextAlignmentOptions.Center, 1, "BtnText");
        }

        // ---- kanban ----
        static void Kanban(LoftBoard b, RectTransform c, float w, float h)
        {
            var cols = new[]
            {
                ("To do", PT.Todo, new[] { "queued" }),
                ("Working", PT.Doing, new[] { "running" }),
                ("Review", PT.Review, new[] { "review", "conflict", "failed" }),
                ("Done", PT.Done, new[] { "approved" }),
            };
            const float gap = 24f, headH = 104f, cardH = 268f, cardGap = 16f;
            float colW = (w - PAD * 2 - gap * 3) / 4f, colH = h - PAD - 28f;
            for (int i = 0; i < 4; i++)
            {
                var (name, accent, st) = cols[i];
                var list = b.state.tasks.Where(t => st.Contains(t.status)).ToList();
                if (i == 3) list.Reverse();
                float x = PAD + i * (colW + gap), y = 28f;
                PanelKit.Box(c, x, y, colW, colH, PT.Column, PT.RCard, "Lane");
                PanelKit.Box(c, x + 26f, y + 36f, 12f, 40f, accent, -1f, "LaneMark");
                PanelKit.Text(c, name, x + 54f, y + 18f, colW - 170f, 76f, PT.Body, PT.Text, true, TextAlignmentOptions.Left);
                PanelKit.Badge(c, list.Count.ToString(), x + colW - 100f, y + 26f, 60f, accent * new Color(1, 1, 1, 0.22f), accent);

                int fit = Mathf.Max(1, Mathf.FloorToInt((colH - headH - 20f) / (cardH + cardGap)));
                bool more = list.Count > fit;
                int show = more ? fit - 1 : list.Count;
                for (int k = 0; k < show; k++)
                    Card(b, c, list[k], x + 16f, y + headH + k * (cardH + cardGap), colW - 32f, cardH, accent);
                if (more)
                    PanelKit.Text(c, $"+{list.Count - show} more", x + 16f, y + headH + show * (cardH + cardGap) + 20f, colW - 32f, 70f, PT.Body, PT.Muted, false, TextAlignmentOptions.Center);
                if (list.Count == 0)
                    PanelKit.Text(c, "Nothing here", x + 16f, y + headH + 60f, colW - 32f, 70f, PT.Meta, PT.Muted, false, TextAlignmentOptions.Center);
            }
        }

        static void Card(LoftBoard b, RectTransform c, PTask t, float x, float y, float w, float h, Color accent)
        {
            PanelKit.Box(c, x, y, w, h, PT.Card, PT.RSmall, "Card");
            PanelKit.Text(c, t.title, x + 26f, y + 22f, w - 52f, 132f, PT.Body, PT.Text, false, TextAlignmentOptions.TopLeft, 2, "CardTitle");
            float by = y + h - 84f;
            PanelKit.Avatar(c, x + 58f, by + 32f, 62f, t.agentName);
            if (t.status == "running")
                PanelKit.Progress(c, x + 104f, by + 26f, w - 134f, 14f, t.progress, accent);
            else if (t.status == "failed")
                PanelKit.Pill(c, "Failed", x + 104f, by + 6f, 56f, PT.Bad * new Color(1, 1, 1, 0.22f), PT.Bad);
            else
                PanelKit.Text(c, t.agentName, x + 104f, by, w - 134f, 64f, PT.Meta, PT.Text2, false, TextAlignmentOptions.Left);
            if (t.status == "review" || t.status == "failed") b.Click(x, y, w, h, () => LoftPanels.Open("review"));
        }

        // ---- review ----
        static void Review(LoftBoard b, RectTransform c, float w, float h)
        {
            var q = b.state.tasks.Where(t => t.status == "review" || t.status == "conflict").ToList();
            if (q.Count == 0) { Empty(c, w, h, "Nothing to review", "When an agent finishes, the work shows up here.", PT.Done); return; }
            b.page = Mathf.Clamp(b.page, 0, q.Count - 1);
            var t = q[b.page];
            float x = PAD, iw = w - PAD * 2, y = 34f;

            PanelKit.Avatar(c, x + 44f, y + 44f, 88f, t.agentName);
            PanelKit.Text(c, t.agentName, x + 108f, y, iw * 0.5f, 56f, PT.Body, PT.Text, true, TextAlignmentOptions.Left);
            PanelKit.Text(c, t.role, x + 108f, y + 50f, iw * 0.5f, 50f, PT.Meta, PT.Muted, false, TextAlignmentOptions.Left);
            if (q.Count > 1)
            {
                float rx = x + iw;
                PanelKit.Text(c, $"{b.page + 1} of {q.Count}", rx - 370f, y, 200f, 90f, PT.Meta, PT.Text2, false, TextAlignmentOptions.Center);
                PanelKit.Box(c, rx - 170f, y + 6f, 78f, 78f, PT.Card, -1f, "Prev"); PanelKit.IconChevron(c, rx - 131f, y + 45f, 30f, 7f, PT.Text, -1);
                PanelKit.Box(c, rx - 78f, y + 6f, 78f, 78f, PT.Card, -1f, "Next"); PanelKit.IconChevron(c, rx - 39f, y + 45f, 30f, 7f, PT.Text, 1);
                b.Click(rx - 170f, y + 6f, 78f, 78f, () => { b.page = (b.page + q.Count - 1) % q.Count; b.Refresh(); });
                b.Click(rx - 78f, y + 6f, 78f, 78f, () => { b.page = (b.page + 1) % q.Count; b.Refresh(); });
            }
            else PanelKit.PillRight(c, t.status == "conflict" ? "Conflict" : "In review", x + iw, y + 16f, 62f, PT.Review * new Color(1, 1, 1, 0.22f), PT.Review);
            y += 118f;

            PanelKit.Text(c, t.title, x, y, iw, 170f, PT.H1, PT.Text, true, TextAlignmentOptions.TopLeft, 2);
            y += 176f;

            float tw = (iw - 32f) / 3f;
            Tile(c, x, y, tw, t.files.ToString(), t.files == 1 ? "file" : "files", PT.Text);
            Tile(c, x + tw + 16f, y, tw, "+" + t.added, "added", PT.Add);
            Tile(c, x + (tw + 16f) * 2, y, tw, "-" + t.removed, "removed", PT.Del);
            y += 186f;

            float sumH = h - y - 320f;
            PanelKit.Box(c, x, y, iw, sumH, PT.Column, PT.RCard, "Summary");
            PanelKit.Text(c, "WHAT THE AGENT SAYS", x + 34f, y + 22f, iw - 68f, 50f, PT.Meta, PT.Muted, true, TextAlignmentOptions.Left);
            PanelKit.Text(c, t.result, x + 34f, y + 80f, iw - 68f, sumH - 96f, PT.Body, PT.Text2, false, TextAlignmentOptions.TopLeft, Mathf.Max(1, Mathf.FloorToInt((sumH - 96f) / 66f)));
            y += sumH + 24f;

            float bw = (iw - 20f) / 2f;
            Btn(c, x, y, bw, 116f, "See changes", PT.CardHi, PT.Text);
            b.Click(x, y, bw, 116f, () => LoftPanels.Open("diff"));
            Btn(c, x + bw + 20f, y, bw, 116f, "Try it live", PT.CardHi, PT.Text);
            b.Click(x + bw + 20f, y, bw, 116f, () => LoftPanels.Open("preview"));
            y += 134f;

            string tid = t.id;
            Btn(c, x, y, bw, 116f, "Hold to approve", PT.Done * new Color(0.62f, 0.62f, 0.62f, 1f), PT.Text);
            b.Hold(x, y, bw, 116f, 1.2f, PT.Done, () => LoftPanels.Approve(tid));
            Btn(c, x + bw + 20f, y, bw, 116f, "Hold to send back", PT.Bad * new Color(0.55f, 0.55f, 0.55f, 1f), PT.Text);
            b.Hold(x + bw + 20f, y, bw, 116f, 1.2f, PT.Bad, () => LoftPanels.Reject(tid));
        }

        static void Tile(RectTransform c, float x, float y, float w, string big, string label, Color col)
        {
            PanelKit.Box(c, x, y, w, 164f, PT.Card, PT.RSmall, "Tile");
            PanelKit.Text(c, big, x, y + 20f, w, 80f, PT.H1 + 8f, col, true, TextAlignmentOptions.Center);
            PanelKit.Text(c, label, x, y + 100f, w, 50f, PT.Meta, PT.Muted, false, TextAlignmentOptions.Center);
        }

        // ---- live log ----
        static void Log(LoftBoard b, RectTransform c, float w, float h)
        {
            if (b.state.log.Count == 0) { Empty(c, w, h, "All quiet", "Team activity appears here as it happens.", PT.Doing); return; }
            const float rowH = 164f;
            int fit = Mathf.FloorToInt((h - 40f) / rowH);
            float x = PAD, y = 26f;
            float rail = x + 44f;
            int n = Mathf.Min(fit, b.state.log.Count);
            PanelKit.Flat(c, rail - 2f, y + 44f, 4f, Mathf.Max(0f, (n - 1) * rowH), PT.Line, "Rail");
            for (int i = 0; i < n; i++)
            {
                var e = b.state.log[i];
                float ry = y + i * rowH;
                PanelKit.Avatar(c, rail, ry + 44f, 84f, e.who);
                PanelKit.Text(c, e.who, x + 108f, ry, w - 500f, 56f, PT.Meta, PT.Text2, true, TextAlignmentOptions.Left);
                PanelKit.Text(c, e.text, x + 108f, ry + 48f, w - PAD * 2 - 124f, 110f, PT.Body, PT.Text, false, TextAlignmentOptions.TopLeft, 2);
                PanelKit.Text(c, e.time, w - PAD - 170f, ry, 170f, 56f, PT.Meta, PT.Muted, false, TextAlignmentOptions.Right);
                PanelKit.Dot(c, w - PAD - 196f, ry + 26f, 22f, e.kind == "info" ? PT.Info : PT.Status(e.kind));
            }
        }

        // ---- notes ----
        static Color Sticky(string name)
        {
            switch (name)
            {
                case "pink": return new Color(1f, 0.74f, 0.82f);
                case "mint": return new Color(0.70f, 0.93f, 0.78f);
                case "blue": return new Color(0.72f, 0.85f, 1f);
                default: return new Color(1f, 0.90f, 0.58f);
            }
        }

        static void Notes(LoftBoard b, RectTransform c, float w, float h)
        {
            const float gap = 26f;
            float nw = (w - PAD * 2 - gap * 2) / 3f, nh = (h - 56f - gap) / 2f;
            var notes = b.state.notes;
            int cap = 5;
            for (int i = 0; i < Mathf.Min(cap, notes.Count); i++)
            {
                var n = notes[i];
                float x = PAD + (i % 3) * (nw + gap), y = 30f + (i / 3) * (nh + gap);
                PanelKit.Box(c, x, y, nw, nh, Sticky(n.color), PT.RSmall, "Sticky");
                PanelKit.Text(c, n.text, x + 32f, y + 30f, nw - 64f, nh - 120f, PT.Body, PT.Ink, false, TextAlignmentOptions.TopLeft, Mathf.Max(1, Mathf.FloorToInt((nh - 120f) / 66f)));
                PanelKit.Text(c, n.agentName, x + 32f, y + nh - 78f, nw - 130f, 56f, PT.Meta, new Color(0.08f, 0.1f, 0.15f, 0.65f), true, TextAlignmentOptions.Left);
                PanelKit.IconX(c, x + nw - 54f, y + nh - 50f, 26f, 6f, new Color(0.08f, 0.1f, 0.15f, 0.55f));
                string id = n.id;
                b.Click(x + nw - 100f, y + nh - 96f, 92f, 92f, () => LoftPanels.DeleteNote(id));
            }
            int slot = Mathf.Min(cap, notes.Count);
            float ax = PAD + (slot % 3) * (nw + gap), ay = 30f + (slot / 3) * (nh + gap);
            PanelKit.Outline(c, ax, ay, nw, nh, PT.Edge, PT.RSmall);
            PanelKit.Box(c, ax + nw / 2f - 40f, ay + nh / 2f - 90f, 80f, 12f, PT.Text2, -1f, "PlusH");
            PanelKit.Box(c, ax + nw / 2f - 6f, ay + nh / 2f - 124f, 12f, 80f, PT.Text2, -1f, "PlusV");
            PanelKit.Text(c, "New note", ax, ay + nh / 2f - 20f, nw, 70f, PT.Body, PT.Text2, true, TextAlignmentOptions.Center);
            if (notes.Count > cap) PanelKit.Text(c, $"+{notes.Count - cap} more", ax, ay + nh / 2f + 50f, nw, 60f, PT.Meta, PT.Muted, false, TextAlignmentOptions.Center);
            b.Click(ax, ay, nw, nh, LoftPanels.AddNote);
        }

        // ---- shared list + scrolling code pane ----
        static void ScrollBtns(LoftBoard b, RectTransform c, float x, float y, int total, int page)
        {
            PanelKit.Box(c, x, y, 78f, 78f, PT.Card, -1f, "Up");
            PanelKit.Line(c, new Vector2(x + 24f, y + 46f), new Vector2(x + 39f, y + 30f), 7f, PT.Text);
            PanelKit.Line(c, new Vector2(x + 39f, y + 30f), new Vector2(x + 54f, y + 46f), 7f, PT.Text);
            PanelKit.Box(c, x + 92f, y, 78f, 78f, PT.Card, -1f, "Down");
            PanelKit.Line(c, new Vector2(x + 116f, y + 32f), new Vector2(x + 131f, y + 48f), 7f, PT.Text);
            PanelKit.Line(c, new Vector2(x + 131f, y + 48f), new Vector2(x + 146f, y + 32f), 7f, PT.Text);
            b.Click(x, y, 78f, 78f, () => { b.scroll = Mathf.Max(0, b.scroll - page); b.Refresh(); });
            b.Click(x + 92f, y, 78f, 78f, () => { b.scroll = Mathf.Clamp(b.scroll + page, 0, Mathf.Max(0, total - page)); b.Refresh(); });
        }

        static void Files(LoftBoard b, RectTransform c, float w, float h)
        {
            var s = b.state;
            float lw = 760f, y0 = 28f;
            PanelKit.Box(c, PAD, y0, lw, h - 56f, PT.Column, PT.RCard, "Tree");
            PanelKit.Text(c, s.project, PAD + 30f, y0 + 16f, lw - 60f, 64f, PT.Body, PT.Text, true, TextAlignmentOptions.Left);
            PanelKit.Text(c, string.IsNullOrEmpty(s.treePath) ? "/" : "/" + s.treePath, PAD + 30f, y0 + 74f, lw - 60f, 50f, PT.Meta, PT.Muted, false, TextAlignmentOptions.Left);
            float ry = y0 + 136f;
            const float rowH = 88f;
            int fit = Mathf.FloorToInt((h - 56f - 150f) / rowH);
            if (!string.IsNullOrEmpty(s.treePath))
            {
                PanelKit.IconChevron(c, PAD + 54f, ry + rowH / 2f, 30f, 7f, PT.Text2, -1);
                PanelKit.Text(c, "Back", PAD + 92f, ry, lw - 120f, rowH, PT.Body, PT.Text2, false, TextAlignmentOptions.Left);
                b.Click(PAD + 12f, ry, lw - 24f, rowH, LoftPanels.BrowseUp);
                ry += rowH; fit--;
            }
            foreach (var f in s.tree.Take(fit))
            {
                if (f.dir) PanelKit.IconFolder(c, PAD + 32f, ry + 24f, 44f, PT.Doing);
                else PanelKit.IconFile(c, PAD + 32f, ry + 22f, 44f, PT.Text2);
                bool open = f.path == s.openFilePath;
                PanelKit.Text(c, f.name, PAD + 92f, ry, lw - 120f, rowH, PT.Body, open ? PT.Info : PT.Text, open, TextAlignmentOptions.Left);
                string p = f.path; bool dir = f.dir;
                b.Click(PAD + 12f, ry, lw - 24f, rowH, () => { b.scroll = 0; if (dir) LoftPanels.Browse(p); else LoftPanels.OpenFile(p); });
                ry += rowH;
            }

            float vx = PAD + lw + 24f, vw = w - vx - PAD;
            PanelKit.Box(c, vx, y0, vw, h - 56f, PT.Column, PT.RCard, "Viewer");
            if (string.IsNullOrEmpty(s.openFilePath))
            {
                PanelKit.Text(c, "Pick a file to read it", vx, h / 2f - 40f, vw, 80f, PT.Body, PT.Muted, false, TextAlignmentOptions.Center);
                return;
            }
            PanelKit.Text(c, s.openFilePath, vx + 30f, y0 + 16f, vw - 260f, 80f, PT.Body, PT.Text, true, TextAlignmentOptions.Left);
            var lines = (s.openFileText ?? "").Replace("\r", "").Split('\n');
            const float lineH = 62f;
            int page = Mathf.FloorToInt((h - 56f - 124f) / lineH);
            ScrollBtns(b, c, vx + vw - 196f, y0 + 16f, lines.Length, page);
            b.scroll = Mathf.Clamp(b.scroll, 0, Mathf.Max(0, lines.Length - 1));
            for (int i = 0; i < page && b.scroll + i < lines.Length; i++)
            {
                float ly = y0 + 110f + i * lineH;
                PanelKit.Mono(c, (b.scroll + i + 1).ToString().PadLeft(4), vx + 16f, ly, 130f, lineH, PT.Mono, PT.Muted);
                PanelKit.Mono(c, lines[b.scroll + i].Replace("\t", "  "), vx + 150f, ly, vw - 176f, lineH, PT.Mono, PT.Text);
            }
        }

        static void Diff(LoftBoard b, RectTransform c, float w, float h)
        {
            var s = b.state;
            if (s.diff.Count == 0) { Empty(c, w, h, "No changes to show", "Open this while a task is in review.", PT.Add); return; }
            b.sel = Mathf.Clamp(b.sel, 0, s.diff.Count - 1);
            float lw = 760f, y0 = 28f;
            PanelKit.Box(c, PAD, y0, lw, h - 56f, PT.Column, PT.RCard, "List");
            PanelKit.Text(c, $"{s.diff.Count} file{(s.diff.Count == 1 ? "" : "s")} changed", PAD + 30f, y0 + 18f, lw - 60f, 70f, PT.Body, PT.Text, true, TextAlignmentOptions.Left);
            const float rowH = 124f;
            int fit = Mathf.FloorToInt((h - 56f - 110f) / rowH);
            for (int i = 0; i < Mathf.Min(fit, s.diff.Count); i++)
            {
                var f = s.diff[i];
                float ry = y0 + 100f + i * rowH;
                if (i == b.sel) PanelKit.Box(c, PAD + 12f, ry, lw - 24f, rowH - 8f, PT.CardHi, PT.RSmall, "Sel");
                int cut = f.path.LastIndexOf('/');
                PanelKit.Text(c, cut < 0 ? f.path : f.path.Substring(cut + 1), PAD + 32f, ry + 4f, lw - 64f, 62f, PT.Body, PT.Text, i == b.sel, TextAlignmentOptions.Left);
                float px = PAD + 32f;
                px += PanelKit.Pill(c, "+" + f.added, px, ry + 64f, 44f, PT.AddBg, PT.Add, 34f, true, 18f) + 10f;
                PanelKit.Pill(c, "-" + f.removed, px, ry + 64f, 44f, PT.DelBg, PT.Del, 34f, true, 18f);
                int idx = i;
                b.Click(PAD + 12f, ry, lw - 24f, rowH - 8f, () => { b.sel = idx; b.scroll = 0; b.Refresh(); });
            }

            float vx = PAD + lw + 24f, vw = w - vx - PAD;
            var file = s.diff[b.sel];
            PanelKit.Box(c, vx, y0, vw, h - 56f, PT.Column, PT.RCard, "Viewer");
            PanelKit.Text(c, file.path, vx + 30f, y0 + 16f, vw - 260f, 80f, PT.Body, PT.Text, true, TextAlignmentOptions.Left);
            const float lineH = 62f;
            int page = Mathf.FloorToInt((h - 56f - 124f) / lineH);
            ScrollBtns(b, c, vx + vw - 196f, y0 + 16f, file.lines.Count, page);
            b.scroll = Mathf.Clamp(b.scroll, 0, Mathf.Max(0, file.lines.Count - 1));
            for (int i = 0; i < page && b.scroll + i < file.lines.Count; i++)
            {
                var l = file.lines[b.scroll + i];
                float ly = y0 + 110f + i * lineH;
                if (l.type == '+') PanelKit.Flat(c, vx + 10f, ly, vw - 20f, lineH - 2f, PT.AddBg, "RowAdd");
                else if (l.type == '-') PanelKit.Flat(c, vx + 10f, ly, vw - 20f, lineH - 2f, PT.DelBg, "RowDel");
                var col = l.type == '+' ? PT.Add : l.type == '-' ? PT.Del : l.type == '@' ? PT.Review : PT.Text2;
                PanelKit.Mono(c, l.type == '@' ? "" : l.type.ToString(), vx + 26f, ly, 40f, lineH, PT.Mono, col);
                PanelKit.Mono(c, l.text.Replace("\t", "  "), vx + 70f, ly, vw - 96f, lineH, PT.Mono, l.type == ' ' ? PT.Text2 : col == PT.Review ? col : PT.Text);
            }
        }

        // ---- browser ----
        static void Square(RectTransform c, float x, float y, float s)
        {
            PanelKit.Box(c, x, y, s, s, PT.Card, PT.RSmall, "Nav");
        }

        static void Preview(LoftBoard b, RectTransform c, float w, float h)
        {
            const float bar = 100f;
            float x = PAD, y = 22f, iw = w - PAD * 2, cx = y + bar / 2f;
            // back, forward, reload
            Square(c, x, y, bar);
            PanelKit.IconChevron(c, x + 50f, cx, 34f, 8f, PT.Text, -1);
            b.Click(x, y, bar, bar, () => LoftPanels.PreviewNav(b, "back"));
            Square(c, x + 114f, y, bar);
            PanelKit.IconChevron(c, x + 164f, cx, 34f, 8f, PT.Text, 1);
            b.Click(x + 114f, y, bar, bar, () => LoftPanels.PreviewNav(b, "forward"));
            Square(c, x + 228f, y, bar);
            PanelKit.IconRefresh(c, x + 278f, cx, 42f, 7f, PT.Text);
            b.Click(x + 228f, y, bar, bar, () => LoftPanels.PreviewNav(b, "reload"));
            // address bar: click it to go anywhere
            float ax = x + 346f, aw = iw - 346f - 490f;
            PanelKit.Box(c, ax, y, aw, bar, PT.Column, -1f, "Address");
            b.address = PanelKit.Text(c, string.IsNullOrEmpty(b.previewUrl) ? "Type an address or search" : b.previewUrl, ax + 44f, y, aw - 88f, bar, PT.Meta, PT.Text2, false, TextAlignmentOptions.Left);
            b.Click(ax, y, aw, bar, () => LoftPanels.PreviewGo(b));
            float rx = x + iw - 476f;
            Btn(c, rx, y, 220f, bar, "Type", PT.Card, PT.Text);
            b.Click(rx, y, 220f, bar, () => LoftPanels.PreviewType(b));
            rx += 236f;
            Square(c, rx, y, bar);
            PanelKit.Line(c, new Vector2(rx + 32f, cx + 10f), new Vector2(rx + 50f, cx - 10f), 8f, PT.Text);
            PanelKit.Line(c, new Vector2(rx + 50f, cx - 10f), new Vector2(rx + 68f, cx + 10f), 8f, PT.Text);
            b.Click(rx, y, bar, bar, () => LoftPanels.PreviewScroll(b, -500));
            rx += 116f;
            Square(c, rx, y, bar);
            PanelKit.Line(c, new Vector2(rx + 32f, cx - 10f), new Vector2(rx + 50f, cx + 10f), 8f, PT.Text);
            PanelKit.Line(c, new Vector2(rx + 50f, cx + 10f), new Vector2(rx + 68f, cx - 10f), 8f, PT.Text);
            b.Click(rx, y, bar, bar, () => LoftPanels.PreviewScroll(b, 500));

            float py = y + bar + 16f, ph = h - py - 40f;
            PanelKit.Box(c, x, py, iw, ph, PT.Column, PT.RSmall, "Page");
            if (!string.IsNullOrEmpty(b.previewId))
            {
                PanelKit.Text(c, "Loading the page...", x, py + ph / 2f - 40f, iw, 80f, PT.Body, PT.Muted, false, TextAlignmentOptions.Center);
                var rt = PanelKit.Rect(c, "Frame", x + 6f, py + 6f, iw - 12f, ph - 12f);
                b.frame = rt.gameObject.AddComponent<RawImage>();
                b.frame.raycastTarget = false;
                b.frame.texture = b.previewTex;
                b.frame.color = new Color(1, 1, 1, b.previewTex != null ? 1f : 0f); // shows once the first frame lands
                b.ClickAt(x + 6f, py + 6f, iw - 12f, ph - 12f, uv => LoftPanels.PreviewClick(b, uv));
                return;
            }
            bool failed = !string.IsNullOrEmpty(b.previewError);
            PanelKit.Text(c, failed ? "Could not open that" : "Where to?", x + 60f, py + ph / 2f - 150f, iw - 120f, 90f, PT.H1, PT.Text, true, TextAlignmentOptions.Center);
            PanelKit.Text(c, failed ? b.previewError : "Hold A and say \"open youtube.com\", or click the address bar.", x + 120f, py + ph / 2f - 50f, iw - 240f, 170f, PT.Body, PT.Text2, false, TextAlignmentOptions.Top, 2);
        }
    }
}
