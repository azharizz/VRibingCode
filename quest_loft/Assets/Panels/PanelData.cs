// VIRTUAL LOFT — PanelData.cs: what boards show. One plain state object that
// boards read; LoftPanelsFeed fills it from the backend, PanelSample fills it
// with believable sample data (gallery, tests). Boards never talk to the network.

using System;
using System.Collections.Generic;

namespace VirtualLoft.Panels
{
    public class PTask
    {
        public string id, title, status, agentId, agentName, role, last, result, projectId, stat;
        public int files, added, removed;
        public float progress;          // 0..1 for running tasks (estimated)
        public DateTime time = DateTime.Now;
    }
    public class PLog { public string time, who, text, kind; }        // kind: task status or "info"
    public class PNote { public string id, text, agentName, color; }
    public class PFile { public string name, path; public bool dir; }
    public class PHunkLine { public char type; public string text; }  // ' ', '+', '-', '@'
    public class PDiffFile { public string path; public int added, removed; public List<PHunkLine> lines = new List<PHunkLine>(); }

    public class PanelState
    {
        public string project = "landing-site";
        public bool connected = true;
        public List<PTask> tasks = new List<PTask>();
        public List<PLog> log = new List<PLog>();
        public List<PNote> notes = new List<PNote>();
        public List<PFile> tree = new List<PFile>();
        public string treePath = "";
        public string openFilePath, openFileText;
        public List<PDiffFile> diff = new List<PDiffFile>();
        public int version;             // bump on change; boards rebuild when it moves
        public void Touch() => version++;
    }

    public static class PanelSample
    {
        public static PanelState Make()
        {
            var s = new PanelState();
            PTask T(string id, string title, string st, string who, string role, string last, string result, int f, int a, int r, float prog = 0f) =>
                new PTask { id = id, title = title, status = st, agentName = who, agentId = who.ToLowerInvariant(), role = role, last = last, result = result, projectId = "landing-site", files = f, added = a, removed = r, progress = prog, stat = f > 0 ? f + " files changed, +" + a + " -" + r : null };
            s.tasks.AddRange(new[]
            {
                T("t1", "Add pricing section with three plan cards", "queued", "Kiko", "Designer", null, null, 0, 0, 0),
                T("t2", "Write FAQ copy for the checkout page", "queued", "Pip", "Writer", null, null, 0, 0, 0),
                T("t3", "Set up dark mode toggle in settings", "queued", "Mochi", "Builder", null, null, 0, 0, 0),
                T("t4", "Make the navigation bar collapse on phones", "running", "Maya", "Builder", "editing nav.css", null, 0, 0, 0, 0.62f),
                T("t5", "Fix signup form validation errors", "running", "Otto", "Builder", "running tests", null, 0, 0, 0, 0.35f),
                T("t6", "Build login page with email validation", "review", "Luna", "Builder",
                    null, "Added a login page with email and password fields, inline validation and a friendly error state. Wired the submit button to the auth endpoint and added three tests.", 4, 182, 37),
                T("t7", "Compress hero images for faster loading", "review", "Pip", "Scout", null, "Re-encoded six hero images, total weight down from 4.2 MB to 610 KB.", 6, 12, 8),
                T("t8", "Create project README and setup notes", "approved", "Kiko", "Writer", null, "README with install steps.", 1, 64, 0),
                T("t9", "Add favicon and share image", "approved", "Maya", "Designer", null, "Added favicon set.", 3, 9, 0),
                T("t10", "Fix broken footer links", "approved", "Otto", "Builder", null, "Fixed four links.", 1, 4, 4),
                T("t11", "Swap the font to a rounder typeface", "failed", "Luna", "Designer", null, "Could not find the font file.", 0, 0, 0),
            });
            string[][] log =
            {
                new[]{"12:41","Luna","Finished the login page and asked for review","review"},
                new[]{"12:38","Maya","Working on the navigation bar: editing nav.css","running"},
                new[]{"12:36","Otto","Started on signup form validation","running"},
                new[]{"12:30","Kiko","README approved and merged to main","approved"},
                new[]{"12:24","Pip","Image compression ready for review","review"},
                new[]{"12:12","Luna","Could not find the rounder font, task failed","failed"},
                new[]{"12:05","You","Goal: polish the landing page for launch","info"},
                new[]{"11:58","Maya","Favicon set merged","approved"},
            };
            foreach (var l in log) s.log.Add(new PLog { time = l[0], who = l[1], text = l[2], kind = l[3] });
            string[][] notes =
            {
                new[]{"Launch is Friday. Keep the hero copy short and punchy.","You","yellow"},
                new[]{"Ask Pip to double check the pricing numbers with finance.","You","pink"},
                new[]{"Dark mode: use the same accent as the logo.","Kiko","mint"},
                new[]{"Mobile first. Test every page at 375 px wide.","Maya","blue"},
                new[]{"Idea: add a short demo video under the hero.","Otto","yellow"},
                new[]{"Do not touch checkout.js until the audit is done.","You","pink"},
            };
            foreach (var n in notes) s.notes.Add(new PNote { id = "n" + s.notes.Count, text = n[0], agentName = n[1], color = n[2] });
            string[] paths = { "src/", "src/pages/", "src/pages/login.html", "src/pages/pricing.html", "src/styles/", "src/styles/nav.css", "src/styles/theme.css", "src/app.js", "tests/", "tests/login.test.js", "README.md", "package.json" };
            foreach (var p in paths) if (p.IndexOf('/') == p.Length - 1 || p.IndexOf('/') < 0) s.tree.Add(new PFile { path = p.TrimEnd('/'), name = p.TrimEnd('/'), dir = p.EndsWith("/") });
            s.openFilePath = "src/styles/nav.css";
            s.openFileText = "/* Navigation bar */\n.nav {\n  display: flex;\n  align-items: center;\n  gap: 24px;\n  padding: 12px 32px;\n  background: #0b111c;\n}\n\n.nav a {\n  color: #f3f7fd;\n  font-weight: 600;\n}\n\n@media (max-width: 640px) {\n  .nav { flex-direction: column; }\n  .nav .menu { display: none; }\n}\n";
            var d1 = new PDiffFile { path = "src/pages/login.html", added = 96, removed = 4 };
            d1.lines.AddRange(new[]
            {
                H("@@ -1,8 +1,34 @@ login page"),
                L(' ', "<main class=\"login\">"), L('-', "  <h1>Sign in</h1>"), L('+', "  <h1>Welcome back</h1>"),
                L('+', "  <form id=\"login-form\" novalidate>"), L('+', "    <label for=\"email\">Email</label>"),
                L('+', "    <input id=\"email\" type=\"email\" required>"), L('+', "    <p class=\"error\" hidden></p>"),
                L(' ', "  <button type=\"submit\">Sign in</button>"), L('-', "  <a href=\"#\">Forgot?</a>"), L('+', "  <a href=\"/reset\">Forgot password?</a>"),
                L(' ', "</main>"),
            });
            var d2 = new PDiffFile { path = "src/styles/theme.css", added = 40, removed = 12 };
            d2.lines.AddRange(new[] { H("@@ -10,6 +10,12 @@ :root"), L(' ', ":root {"), L('-', "  --accent: #3b82f6;"), L('+', "  --accent: #5cd6ff;"), L('+', "  --radius: 14px;"), L(' ', "}") });
            var d3 = new PDiffFile { path = "tests/login.test.js", added = 46, removed = 0 };
            d3.lines.AddRange(new[] { H("@@ -0,0 +1,46 @@"), L('+', "test('rejects an empty email', () => {"), L('+', "  expect(validate('')).toBe(false);"), L('+', "});") });
            var d4 = new PDiffFile { path = "src/app.js", added = 0, removed = 21 };
            d4.lines.AddRange(new[] { H("@@ -40,21 +40,0 @@"), L('-', "// old login handler"), L('-', "function legacyLogin() { /* ... */ }") });
            s.diff.AddRange(new[] { d1, d2, d3, d4 });
            return s;
        }
        static PHunkLine L(char t, string x) => new PHunkLine { type = t, text = x };
        static PHunkLine H(string x) => new PHunkLine { type = '@', text = x };
    }
}
