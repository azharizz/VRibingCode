// VIRTUAL LOFT — BEACH: a wooden cabana (thatched roof, posts, open to the sea),
// a boardwalk jetty leading to the water, curved palms swaying, animated ocean to
// a real horizon, a low sun, clouds, gulls, a sailboat. `night` = the party look.
using System.Collections.Generic;
using UnityEngine;

public static class BeachSc
{
    const float WATER_Y = -0.55f;
    static Color H(string s) => Mk.Hex(s);

    public static float SandY(float x, float z)
    {
        float baseY = z > 2f ? -0.18f : Mathf.Max(-1.6f, -0.18f + (z - 2f) * 0.16f); // the sea comes up under the deck
        float side = Mathf.SmoothStep(0f, 1f, (Mathf.Abs(x) - 12f) / 8f);
        float near = Mathf.SmoothStep(0f, 1f, (z + 9f) / 5f) * Mathf.SmoothStep(1f, 0f, (z - 2f) / 6f);
        float bump = side * near * (1.6f + 0.7f * Mathf.Sin(x * 0.33f + 1f) + 0.45f * Mathf.Sin(z * 0.55f + x * 0.21f));
        return baseY + Mathf.Max(0f, bump);
    }

    public static void Build(WorldCtx x, bool night)
    {
        var m = new Mk(7);       // rigid shell (everything wooden/sandy)
        var glow = new Mk(8);    // lanterns, bulbs, flames
        var leaf = new Mk(9);    // swaying palms + grass
        var wood = new[] { H("#B07A4A"), H("#A06B3F"), H("#B98558"), H("#94613A") };
        var dark = H("#6E4528"); var mid = H("#8B5A33");

        // ---------- deck on stilts ----------
        SceneKit.Planks(m, Plan.X0, Plan.X1, Plan.Z0, Plan.Z1, 0f, 0.23f, 0.012f, wood);
        m.Beam(new Vector3(Plan.X0, -0.14f, 0f), new Vector3(Plan.X1, -0.14f, 0f), 0.16f, 0.26f, dark, 0.03f);
        for (float px = Plan.X0 + 0.2f; px <= Plan.X1; px += 2.55f)
            m.Prism(new Vector3(px, -1.8f, 0.1f), new Vector3(px, -0.1f, 0.1f), 0.11f, 0.11f, 6, mid, false, true);
        foreach (float sx in new[] { Plan.X0 + 0.05f, Plan.X1 - 0.05f })
        {
            m.Beam(new Vector3(sx, 1.0f, 0.2f), new Vector3(sx, 1.0f, 17.8f), 0.07f, 0.09f, dark);
            m.Beam(new Vector3(sx, 0.5f, 0.2f), new Vector3(sx, 0.5f, 17.8f), 0.05f, 0.06f, mid);
            for (float pz = 0.2f; pz <= 17.9f; pz += 2.2f) SceneKit.Post(m, new Vector3(sx, 0f, pz), 1.05f, 0.11f, dark);
        }
        foreach (float bx in new[] { -12.6f, -7f, -2.4f, 2.4f, 7f, 12.6f })
            SceneKit.Post(m, new Vector3(bx, 0f, 0.1f), 0.55f, 0.16f, mid);

        // ---------- cabana frame ----------
        foreach (var pp in Plan.Posts)
        {
            SceneKit.Post(m, new Vector3(pp.x, 0f, pp.y), SceneKit.POST_H, 0.3f, dark);
            m.Box(new Vector3(pp.x, 0.12f, pp.y), new Vector3(0.52f, 0.24f, 0.52f), H("#9A9588"), 0.06f);
        }
        foreach (float bz in new[] { 4f, 10.3f, 16.6f })
        {
            m.Beam(new Vector3(-13.2f, SceneKit.POST_H - 0.1f, bz), new Vector3(13.2f, SceneKit.POST_H - 0.1f, bz), 0.22f, 0.3f, dark);
            foreach (float px in new[] { -12.5f, -5f, 5f, 12.5f })
                foreach (float sgn in new[] { -1f, 1f })
                    if (Mathf.Abs(px + sgn * 0.9f) < 13f)
                        m.Beam(new Vector3(px, SceneKit.POST_H - 1f, bz), new Vector3(px + sgn * 0.9f, SceneKit.POST_H - 0.12f, bz), 0.1f, 0.1f, mid);
        }
        foreach (float px in new[] { -12.5f, -5f, 5f, 12.5f })
            m.Beam(new Vector3(px, SceneKit.POST_H - 0.1f, 2.9f), new Vector3(px, SceneKit.POST_H - 0.1f, 17.5f), 0.2f, 0.26f, dark);

        // ---------- thatched roof ----------
        var front = new Vector3(0, SceneKit.EAVE_Y, SceneKit.EAVE_FRONT); var back = new Vector3(0, SceneKit.EAVE_Y, SceneKit.EAVE_BACK); var ridge = new Vector3(0, SceneKit.RIDGE_Y, SceneKit.RIDGE_Z);
        SceneKit.Roof(m, front, ridge, H("#A9792F"), H("#DDB65E"), H("#B98A43"), H("#D3A956"), H("#6E4528"), true, SceneKit.ROOF_HX);
        SceneKit.Roof(m, back, ridge, H("#A9792F"), H("#DDB65E"), H("#B98A43"), H("#D3A956"), H("#6E4528"), true, SceneKit.ROOF_HX);
        m.Prism(new Vector3(-14f, SceneKit.RIDGE_Y + 0.05f, SceneKit.RIDGE_Z), new Vector3(14f, SceneKit.RIDGE_Y + 0.05f, SceneKit.RIDGE_Z), 0.36f, 0.36f, 7, H("#C99A45"), true, true, 0.06f);
        m.Beam(new Vector3(-13.7f, SceneKit.RIDGE_Y - 0.1f, SceneKit.RIDGE_Z), new Vector3(13.7f, SceneKit.RIDGE_Y - 0.1f, SceneKit.RIDGE_Z), 0.2f, 0.24f, dark);

        // ---------- back wall: bamboo slats ----------
        for (float bx = -12.9f; bx <= 12.9f; bx += 0.22f)
            m.Prism(new Vector3(bx, 0f, 17.9f), new Vector3(bx, 4.1f + m.R(-0.06f, 0.06f), 17.9f), 0.06f, 0.05f, 5, m.Jit(H("#CDB36F"), 0.1f), false, true, 0f);
        foreach (float ry in new[] { 0.4f, 2f, 3.6f }) m.Beam(new Vector3(-13f, ry, 17.8f), new Vector3(13f, ry, 17.8f), 0.09f, 0.1f, dark);

        // ---------- the seat, side table, furniture ----------
        SceneKit.Seat(m, new[] { H("#E9D9B8"), H("#C98A5B"), H("#E9D9B8") });
        m.Prism(new Vector3(-1.25f, 0, 6.3f), new Vector3(-1.25f, 0.5f, 6.3f), 0.05f, 0.05f, 6, dark, false, false);
        m.Prism(new Vector3(-1.25f, 0.5f, 6.3f), new Vector3(-1.25f, 0.53f, 6.3f), 0.3f, 0.3f, 10, H("#B98558"), false, true);
        m.Blob(new Vector3(-1.25f, 0.66f, 6.3f), new Vector3(0.1f, 0.11f, 0.1f), H("#8A5B35"), H("#B58A58"), 3, 6, 0.04f); // coconut drink
        m.Beam(new Vector3(-1.25f, 0.7f, 6.3f), new Vector3(-1.2f, 0.9f, 6.28f), 0.012f, 0.012f, H("#F2735A"), 0f);
        SceneKit.Furniture(m, glow, new SceneKit.Style { wood = H("#B98558"), wood2 = H("#8B5A33"), fabric = H("#F0E4CB"), fabric2 = H("#2F8F8A"), metal = H("#4A3A2C"), accent = H("#F2735A"), plant = H("#4C9A4A"), screen = H("#9FE3DA"), pot = H("#C98A5B") });

        // ---------- boardwalk + jetty ----------
        SceneKit.CrossPlanks(m, 0f, 2.1f, 0f, -17f, -0.05f, 0.22f, 0.02f, wood);
        foreach (float sx in new[] { -1.12f, 1.12f })
            m.Beam(new Vector3(sx, -0.1f, 0f), new Vector3(sx, -0.1f, -17f), 0.1f, 0.14f, dark);
        for (float pz = -1f; pz >= -17f; pz -= 2f)
            foreach (float sx in new[] { -1.12f, 1.12f })
            {
                m.Prism(new Vector3(sx, -1.6f, pz), new Vector3(sx, 0.55f, pz), 0.07f, 0.065f, 6, mid, false, true);
                if (((int)pz & 3) == 0) glow.Blob(new Vector3(sx, 0.64f, pz), new Vector3(0.09f, 0.11f, 0.09f), H("#FFD99A"), H("#FFF1CC"), 2, 6, 0f);
            }
        m.Box(new Vector3(0f, -0.06f, -18f), new Vector3(3.4f, 0.08f, 2.4f), dark, 0.03f);

        // ---------- sand ----------
        m.Grid(-70f, 70f, -62f, 3f, 70, 33, SandY, (px, py, pz) =>
        {
            if (py < WATER_Y + 0.1f) return m.Jit(H("#B79F6C"), 0.05f);
            if (py < WATER_Y + 0.35f) return m.Jit(H("#D6C08B"), 0.05f);
            return m.Jit(Color.Lerp(H("#EBD7A3"), H("#F3E4B8"), Mathf.Clamp01(py * 0.7f)), 0.05f);
        });
        // beach props on the sand either side of the deck
        Umbrella(m, new Vector3(16.5f, SandY(16.5f, 1.5f), 1.5f), H("#F2735A"));
        Lounger2(m, new Vector3(15.6f, SandY(15.6f, 0.9f), 0.9f), 20f, H("#2BA7A3"));
        Lounger2(m, new Vector3(17.3f, SandY(17.3f, 1.1f), 1.1f), -10f, H("#F7F1DD"));
        Surfboard(m, new Vector3(-15.2f, SandY(-15.2f, 2.2f), 2.2f), -8f, 14f, H("#F7C948"));
        Surfboard(m, new Vector3(-15.8f, SandY(-15.8f, 2f), 2f), 6f, -18f, H("#2BA7A3"));
        Surfboard(m, new Vector3(-14.65f, SandY(-14.65f, 1.9f), 1.9f), 12f, 4f, H("#F2735A"));
        for (int i = 0; i < 6; i++)
        {
            float sx = (i < 3 ? 1 : -1) * (14f + i % 3 * 2.8f);
            Rock(m, new Vector3(sx + m.R(-1, 1), SandY(sx, -2f - i % 3), -2f - i % 3 - m.R(0, 2)), m.R(0.25f, 0.55f));
        }

        // ---------- palms + grass (sway) ----------
        var palms = new[] { (15.2f, -1.2f, 8.2f, -1.4f, -0.3f), (-15.6f, -0.6f, 8.8f, 1.5f, -0.2f), (-19.5f, -4.2f, 7.4f, 1.2f, 0.3f), (20f, -3.6f, 7.8f, -1.2f, 0.2f), (-17f, 6.5f, 9f, 0.6f, -0.8f), (17.4f, 7f, 8.6f, -0.7f, -0.7f), (24f, 1.5f, 7f, -1f, 0.4f), (-24.5f, 2f, 7.4f, 1f, 0.2f) };
        foreach (var (px, pz, h, lx, lz) in palms) Palm(leaf, new Vector3(px, SandY(px, pz) - 0.1f, pz), h, new Vector2(lx, lz));
        for (int i = 0; i < 34; i++)
        {
            float gx = (m.R() < 0.5f ? -1 : 1) * m.R(13.6f, 28f), gz = m.R(-4f, 10f);
            if (SandY(gx, gz) < WATER_Y + 0.5f) continue;
            Grass(leaf, new Vector3(gx, SandY(gx, gz) - 0.03f, gz));
        }

        // ---------- lights ----------
        var wire = m;
        var bulb = night ? H("#FFE7A8") : H("#FFF0CE");
        var cord = H("#3A2A1C");
        float ly = SceneKit.POST_H - 0.2f;
        foreach (var (a, b) in new[] { (-12.5f, -5f), (-5f, 5f), (5f, 12.5f) })
        {
            SceneKit.StringLights(wire, glow, new Vector3(a, ly, 4.1f), new Vector3(b, ly, 4.1f), 0.5f, 0.75f, bulb, cord);
            SceneKit.StringLights(wire, glow, new Vector3(a, ly, 10.3f), new Vector3(b, ly, 10.3f), 0.5f, 0.75f, bulb, cord);
        }
        foreach (float lx in new[] { -9f, 0f, 9f })
        {
            m.Beam(new Vector3(lx, SceneKit.POST_H, 10.3f), new Vector3(lx, SceneKit.POST_H - 0.7f, 10.3f), 0.015f, 0.015f, dark, 0f);
            glow.Blob(new Vector3(lx, SceneKit.POST_H - 0.92f, 10.3f), new Vector3(0.2f, 0.28f, 0.2f), H("#FF9F5A"), H("#FFD27A"), 3, 6, 0.02f);
        }
        if (night)
            foreach (var tp in new[] { new Vector3(-14.5f, 0f, 3.5f), new Vector3(14.5f, 0f, 3.5f), new Vector3(-17f, 0f, 0.6f), new Vector3(17f, 0f, 0.4f) })
            {
                var b = new Vector3(tp.x, SandY(tp.x, tp.z), tp.z);
                m.Prism(b, b + Vector3.up * 1.5f, 0.05f, 0.04f, 5, mid, false, true);
                glow.Prism(b + Vector3.up * 1.5f, b + Vector3.up * 1.95f, 0.09f, 0.01f, 6, H("#FFB347"), true, false, 0.1f);
            }

        // ---------- mesh out ----------
        var shell = x.Mesh(m, "Shell", x.Lit());
        x.Mesh(glow, "Glow", x.Unlit(null, night ? 1.6f : 1.1f));
        x.Mesh(leaf, "Palms", x.Lit(0.34f, 1.3f));

        Water(x);
        Distant(x, night);
        Life(x, night);
    }

    static void Palm(Mk m, Vector3 bs, float height, Vector2 lean)
    {
        int n = 11;
        var pts = new List<Vector3>();
        for (int i = 0; i <= n; i++)
        {
            float t = (float)i / n;
            pts.Add(bs + new Vector3(lean.x * t * t * height * 0.42f, height * t, lean.y * t * t * height * 0.42f));
        }
        m.Tube(pts, t => Mathf.Lerp(0.27f, 0.12f, Mathf.Sqrt(t)), 7, t => ((int)(t * n * 2.2f) & 1) == 0 ? H("#8F6A45") : H("#6F4F35"), null);
        var top = pts[n];
        // three tiers: spears up, a wide arch, then old fronds hanging down
        var tiers = new[] { (5, 1.05f, 2.7f), (7, 0.62f, 3.6f), (6, 0.2f, 3.3f) };
        int k = 0;
        foreach (var (count, lift, len) in tiers)
        {
            for (int i = 0; i < count; i++, k++)
            {
                float a = (i + k * 0.37f) * Mathf.PI * 2f / count + m.R(-0.15f, 0.15f);
                PalmFrond(m, top, new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)), len * m.R(0.92f, 1.1f), lift, H("#1F6B38"), H("#7CC653"));
            }
        }
        for (int i = 0; i < 4; i++)
            m.Blob(top + new Vector3(Mathf.Cos(i * 1.6f) * 0.24f, -0.3f, Mathf.Sin(i * 1.6f) * 0.24f), Vector3.one * 0.16f, H("#4A3320"), H("#6B4A2C"), 2, 5, 0f);
    }

    // A long arching stem with narrow leaflets combed outward: the shape that says "palm".
    static void PalmFrond(Mk m, Vector3 top, Vector3 dir, float len, float lift, Color c0, Color c1)
    {
        var side = Vector3.Cross(Vector3.up, dir);
        const int n = 15;
        var shade = m.R(0.85f, 1.1f);
        Vector3 At(float u) => top + dir * (len * u * (1f - 0.1f * u)) + Vector3.up * (len * (lift * u - (lift + 0.62f) * u * u));
        for (int i = 0; i < n; i++)
        {
            float u = (float)i / n, u1 = (float)(i + 1) / n;
            var p = At(u); var q = At(u1);
            var rig = 1f - u * 0.95f;
            var ca = Mk.Rig(Color.Lerp(c0, c1, u) * shade, rig);
            var ct = Mk.Rig(Color.Lerp(c0, c1, Mathf.Min(1f, u + 0.3f)) * shade, Mathf.Max(0f, rig - 0.2f));
            var stem = side * 0.022f;
            m.Tri2(p - stem, p + stem, q, Mk.Rig(H("#B7B26A"), rig), Mk.Rig(H("#B7B26A"), rig), Mk.Rig(H("#B7B26A"), rig));
            float leaf = len * 0.31f * Mathf.Sin(Mathf.Min(1f, u * 1.08f + 0.1f) * Mathf.PI);
            var along = (q - p).normalized;
            foreach (float sgn in new[] { -1f, 1f })
            {
                var tip = p + side * (sgn * leaf * 0.82f) + along * (leaf * 0.5f) + Vector3.down * (leaf * 0.42f);
                m.Tri2(p, q, tip, ca, ca, ct);
            }
        }
    }

    static void Grass(Mk m, Vector3 p)
    {
        for (int i = 0; i < 6; i++)
        {
            float a = i * 1.05f + m.R(), h = m.R(0.5f, 0.9f), lean = m.R(0.15f, 0.4f);
            var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var side = Vector3.Cross(Vector3.up, d) * 0.04f;
            var tip = p + d * lean + Vector3.up * h;
            var g0 = Mk.Rig(H("#6E9B3C"), 1f); var g1 = Mk.Rig(H("#B7D56B"), 0f);
            m.Tri2(p - side, p + side, tip, g0, g0, g1);
        }
    }

    static void Rock(Mk m, Vector3 c, float s) =>
        m.Blob(c + Vector3.up * s * 0.2f, new Vector3(s, s * 0.6f, s * 0.8f), H("#7C776E"), H("#B3ADA0"), 3, 6, 0.2f, Quaternion.Euler(0, m.R(0, 360), 0), true);

    public static void Lounger(Mk m, Vector3 c)
    {   // rattan lounge the CEO sits in (seat center behind the eye point)
        var rat = H("#C9A06A"); var cush = H("#F4EBD6"); var accent = H("#2BA7A3");
        m.Box(c + new Vector3(0, 0.38f, 0), new Vector3(0.78f, 0.1f, 0.7f), rat, 0.04f);
        m.Box(c + new Vector3(0, 0.46f, -0.02f), new Vector3(0.7f, 0.12f, 0.62f), cush, 0.02f);
        m.Box(c + new Vector3(0, 0.78f, 0.42f), new Vector3(0.74f, 0.9f, 0.1f), rat, 0.04f);
        m.Box(c + new Vector3(0, 0.76f, 0.34f), new Vector3(0.62f, 0.72f, 0.1f), cush, 0.02f);
        m.Box(c + new Vector3(0.1f, 0.62f, 0.3f), new Vector3(0.28f, 0.28f, 0.1f), accent, 0.02f);
        foreach (float sx in new[] { -0.4f, 0.4f })
        {
            m.Box(c + new Vector3(sx, 0.62f, 0.03f), new Vector3(0.07f, 0.06f, 0.62f), rat, 0.04f);
            m.Prism(c + new Vector3(sx, 0.02f, -0.28f), c + new Vector3(sx, 0.38f, -0.28f), 0.035f, 0.035f, 5, rat, false, false);
            m.Prism(c + new Vector3(sx, 0.02f, 0.3f), c + new Vector3(sx, 0.62f, 0.3f), 0.035f, 0.035f, 5, rat, false, false);
        }
    }

    static void Lounger2(Mk m, Vector3 c, float yaw, Color cloth)
    {
        var q = Quaternion.Euler(0, yaw, 0);
        var wood = H("#B98558");
        m.Box(c + q * new Vector3(0, 0.3f, 0), new Vector3(0.62f, 0.07f, 1.5f), q, cloth, 0.03f);
        m.Box(c + q * new Vector3(0, 0.55f, 0.62f), new Vector3(0.62f, 0.07f, 0.7f), q * Quaternion.Euler(-48, 0, 0), cloth, 0.03f);
        foreach (float sx in new[] { -0.3f, 0.3f })
            foreach (float sz in new[] { -0.68f, 0.68f })
                m.Beam(c + q * new Vector3(sx, 0f, sz), c + q * new Vector3(sx, 0.3f, sz), 0.05f, 0.05f, wood, 0f);
    }

    static void Umbrella(Mk m, Vector3 b, Color accent)
    {
        m.Prism(b, b + Vector3.up * 2.3f, 0.035f, 0.03f, 5, H("#F1E6CC"), false, true);
        var tip = b + Vector3.up * 2.6f;
        for (int i = 0; i < 8; i++)
        {
            float a0 = i * Mathf.PI / 4f, a1 = (i + 1) * Mathf.PI / 4f;
            var p0 = b + Vector3.up * 2.12f + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * 1.35f;
            var p1 = b + Vector3.up * 2.12f + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * 1.35f;
            var c = (i & 1) == 0 ? accent : H("#F7F1DD");
            m.Tri2(tip, p0, p1, c, c, c);
        }
    }

    static void Surfboard(Mk m, Vector3 b, float yaw, float tilt, Color c)
    {
        var q = Quaternion.Euler(tilt, yaw, 0);
        m.Blob(b + q * new Vector3(0, 1.0f, 0), new Vector3(0.26f, 1.05f, 0.05f), c, c, 4, 8, 0.01f, q);
        m.Blob(b + q * new Vector3(0, 1.0f, 0.03f), new Vector3(0.03f, 1.0f, 0.045f), H("#FFFFFF"), H("#FFFFFF"), 2, 5, 0f, q);
    }

    static void Rowboat(Mk m, Vector3 b, float yaw)
    {
        var q = Quaternion.Euler(0, yaw, 0);
        var hull = H("#F2735A"); var inner = H("#C9A06A");
        m.Box(b + q * new Vector3(0, 0.25f, 0), new Vector3(1.0f, 0.1f, 2.4f), q, hull, 0.03f);
        m.Box(b + q * new Vector3(-0.5f, 0.45f, 0), new Vector3(0.08f, 0.4f, 2.5f), q * Quaternion.Euler(0, 0, 14), hull, 0.03f);
        m.Box(b + q * new Vector3(0.5f, 0.45f, 0), new Vector3(0.08f, 0.4f, 2.5f), q * Quaternion.Euler(0, 0, -14), hull, 0.03f);
        m.Box(b + q * new Vector3(0, 0.5f, 1.25f), new Vector3(0.7f, 0.5f, 0.1f), q * Quaternion.Euler(30, 0, 0), hull, 0.03f);
        m.Box(b + q * new Vector3(0, 0.5f, 0.1f), new Vector3(0.95f, 0.06f, 0.2f), q, inner, 0.03f);
        m.Box(b + q * new Vector3(0, 0.3f, 0.0f), new Vector3(0.9f, 0.03f, 2.2f), q, H("#E2C58E"), 0.03f);
    }

    // ---------- the sea ----------
    static void Water(WorldCtx x)
    {
        var m = new Mk(3);
        var one = Color.white;
        // dense near patch (waves live here) ...
        const float X0 = -90f, X1 = 90f, Z0 = 4f, Z1 = -90f; const int NX = 90, NZ = 50;
        for (int i = 0; i < NX; i++)
            for (int j = 0; j < NZ; j++)
            {
                float xa = Mathf.Lerp(X0, X1, (float)i / NX), xb = Mathf.Lerp(X0, X1, (float)(i + 1) / NX);
                float za = Mathf.Lerp(Z0, Z1, (float)j / NZ), zb = Mathf.Lerp(Z0, Z1, (float)(j + 1) / NZ);
                Quad(m, xa, xb, za, zb);
            }
        // ... and four huge flat slabs out to the horizon
        const float F = 1400f;
        Quad(m, -F, X0, F, Z1); Quad(m, X1, F, F, Z1);
        Quad(m, X0, X1, Z1, -F); Quad(m, -F, F, F, Z0);
        Quad(m, X0, X1, F, Z0);
        var g = x.Mesh(m, "Water", x.water, null, new Vector3(0, WATER_Y, 0));
        g.GetComponent<MeshRenderer>().allowOcclusionWhenDynamic = false;
        var mesh = g.GetComponent<MeshFilter>().sharedMesh;
        mesh.bounds = new Bounds(new Vector3(0, 0, -300f), new Vector3(3000f, 40f, 3000f));
    }
    static void Quad(Mk m, float x0, float x1, float z0, float z1)
    {
        var up = Color.white;
        m.TriOut(new Vector3(x0, 0, z0), new Vector3(x1, 0, z0), new Vector3(x1, 0, z1), up, up, up, Vector3.down);
        m.TriOut(new Vector3(x0, 0, z0), new Vector3(x1, 0, z1), new Vector3(x0, 0, z1), up, up, up, Vector3.down);
    }

    // ---------- far away: island, sailboat, clouds ----------
    static void Distant(WorldCtx x, bool night)
    {
        var land = new Mk(21);
        land.Blob(new Vector3(-150f, -3f, -430f), new Vector3(110f, 38f, 60f), H("#2E6B57"), H("#6FA85F"), 4, 9, 0.18f);
        land.Blob(new Vector3(-60f, -2f, -440f), new Vector3(70f, 22f, 40f), H("#2E6B57"), H("#7DB36A"), 3, 8, 0.15f);
        land.Blob(new Vector3(260f, -4f, -620f), new Vector3(180f, 55f, 70f), H("#3B6E5F"), H("#6FA48B"), 4, 9, 0.2f);
        for (int i = 0; i < 7; i++) Palm2(land, new Vector3(-190f + i * 14f, 20f + (i % 3) * 3f, -420f + (i % 2) * 10f), 9f);
        x.Mesh(land, "Islands", x.Lit());

        var boat = new Mk(22);
        boat.Box(new Vector3(0, 0.6f, 0), new Vector3(1.6f, 0.9f, 5.4f), H("#F3EDE0"), 0.02f);
        boat.Box(new Vector3(0, 0.5f, 3.0f), new Vector3(0.8f, 0.8f, 1.4f), Quaternion.Euler(0, 0, 0), H("#E8DFCB"), 0.02f);
        boat.Beam(new Vector3(0, 0.9f, 0.4f), new Vector3(0, 8.5f, 0.4f), 0.14f, 0.14f, H("#6E4528"), 0f);
        boat.Tri2(new Vector3(0, 1.4f, 0.5f), new Vector3(0, 8.0f, 0.5f), new Vector3(0, 1.4f, -2.8f), H("#FFFFFF"), H("#FFFFFF"), H("#F5E2C8"));
        boat.Tri2(new Vector3(0, 1.4f, 0.3f), new Vector3(0, 6.6f, 0.3f), new Vector3(0, 1.4f, 2.5f), H("#F2735A"), H("#F2735A"), H("#F2735A"));
        var bg = x.Mesh(boat, "Sailboat", x.Lit(), null, new Vector3(-30f, WATER_Y - 0.25f, -130f));
        bg.transform.rotation = Quaternion.Euler(0, 80f, 0);
        x.life.Add((t, dt) => { if (bg != null) bg.transform.position = new Vector3(-30f + Mathf.Repeat(t * 0.8f, 160f), WATER_Y - 0.25f + Mathf.Sin(t * 0.9f) * 0.12f, -130f); });
    }
    static void Palm2(Mk m, Vector3 bs, float h)
    {   // tiny silhouette palm for faraway shores
        m.Prism(bs, bs + new Vector3(0.6f, h, 0), 0.4f, 0.25f, 4, H("#3E3526"), false, true);
        for (int i = 0; i < 6; i++)
        {
            float a = i * 1.05f;
            m.Tri2(bs + new Vector3(0.6f, h, 0), bs + new Vector3(0.6f, h, 0) + new Vector3(Mathf.Cos(a) * 5.5f, -1.6f, Mathf.Sin(a) * 5.5f) + Vector3.right * 0.8f,
                bs + new Vector3(0.6f, h, 0) + new Vector3(Mathf.Cos(a + 0.5f) * 4f, -2.8f, Mathf.Sin(a + 0.5f) * 4f), H("#2F6B3A"), H("#2F6B3A"), H("#2F6B3A"));
        }
    }

    static void Life(WorldCtx x, bool night)
    {
        // clouds: two layers drifting at different speeds
        for (int layer = 0; layer < 2; layer++)
        {
            var cm = new Mk(40 + layer);
            var top = night ? H("#6C6F9A") : (layer == 0 ? H("#FFFFFF") : H("#FFF3E4"));
            var bot = night ? H("#2E3157") : (layer == 0 ? H("#E8C9C4") : H("#F0C7B2"));
            int n = night ? 4 : 8;
            for (int i = 0; i < n; i++)
            {
                float az = (i - n / 2f) * 0.55f + cm.R(-0.2f, 0.2f) + layer * 0.3f;
                float dist = cm.R(380f, 600f);
                if (Mathf.Abs(az) < 0.16f) az += 0.35f;
                SceneKit.Cloud(cm, new Vector3(Mathf.Sin(az) * dist, cm.R(55f, 120f) + layer * 40f, -Mathf.Cos(az) * dist), cm.R(22f, 38f), top, bot);
            }
            var g = x.Mesh(cm, "Clouds" + layer, x.Unlit(null, 1f)).transform;
            x.life.Drift(g, new Vector3(layer == 0 ? 1.1f : 0.6f, 0, 0), 500f);
        }
        // gulls: V wings, flap by scaling Y
        if (!night)
            for (int i = 0; i < 4; i++)
            {
                var bm = new Mk(60 + i);
                var w = H("#FFFFFF"); var tip = H("#3A3A44");
                bm.Tri2(new Vector3(0, 0, 0.4f), new Vector3(0, 0, -0.3f), new Vector3(-1.5f, 0.3f, -0.15f), w, w, tip);
                bm.Tri2(new Vector3(0, 0, 0.4f), new Vector3(0, 0, -0.3f), new Vector3(1.5f, 0.3f, -0.15f), w, w, tip);
                bm.Blob(Vector3.zero, new Vector3(0.18f, 0.16f, 0.5f), w, w, 2, 5, 0f);
                var t = x.Mesh(bm, "Gull" + i, x.Lit()).transform;
                x.life.Bird(t, new Vector3(-18f + i * 12f, 7f + i * 1.6f, -34f - i * 9f), 11f + i * 3f, 0.28f + 0.04f * i, i * 1.7f, 1.6f);
            }
        // shore foam: thin white bands sliding with the swell
        var fm = new Mk(70);
        var f0 = new Color(1f, 1f, 1f, 0.75f);
        for (int i = 0; i < 3; i++)
        {
            var strip = new Mk(71 + i);
            float z0 = 0f; float w = 0.28f - i * 0.05f;
            for (float sx = -60f; sx < 60f; sx += 3f)
            {
                float zz = Mathf.Sin(sx * 0.18f + i) * 0.35f;
                var a = new Vector3(sx, 0, z0 + zz); var b = new Vector3(sx + 3f, 0, z0 + Mathf.Sin((sx + 3f) * 0.18f + i) * 0.35f);
                strip.Quad2(a, b, b + Vector3.back * w, a + Vector3.back * w, f0, new Color(1, 1, 1, 0.1f));
            }
            var foam = x.Mesh(strip, "Foam" + i, x.Unlit(new Color(1, 1, 1, 0.8f), night ? 0.3f : 1f, 1, 0f, 0f, 2450 + i)).transform;
            int ii = i;
            x.life.Add((t, dt) => { if (foam != null) foam.position = new Vector3(0, WATER_Y + 0.045f, -0.9f - Mathf.Sin(t * 0.5f + ii * 1.3f) * 0.5f - ii * 0.7f); });
        }
    }
}
