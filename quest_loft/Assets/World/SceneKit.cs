// VIRTUAL LOFT — pieces shared by several scenes (planks, rugs, lights, palms-like trees).
using System;
using System.Collections.Generic;
using UnityEngine;

public static class SceneKit
{
    public static Color Hex(string h) => Mk.Hex(h);

    // Planks running along Z over a rectangle at height y.
    public static void Planks(Mk m, float x0, float x1, float z0, float z1, float y, float w, float gap, Color[] tones, float thick = 0.05f)
    {
        m.Box(new Vector3((x0 + x1) / 2, y - thick - 0.01f, (z0 + z1) / 2), new Vector3(x1 - x0, 0.02f, z1 - z0), Hex("#2B1B10"), 0f);
        int n = Mathf.FloorToInt((x1 - x0) / (w + gap));
        float split = Mathf.Lerp(z0, z1, 0.35f + m.R() * 0.3f);
        for (int i = 0; i < n; i++)
        {
            float x = x0 + i * (w + gap) + w / 2;
            var tone = tones[m.rng.Next(tones.Length)];
            float cut = split + (m.R() - 0.5f) * 1.6f;
            float yy = y - thick / 2 + (m.R() - 0.5f) * 0.004f;
            m.Box(new Vector3(x, yy, (z0 + cut) / 2 - 0.004f), new Vector3(w, thick, cut - z0 - 0.01f), m.Jit(tone, 0.08f), 0f);
            m.Box(new Vector3(x, yy, (cut + z1) / 2 + 0.004f), new Vector3(w, thick, z1 - cut - 0.01f), m.Jit(tone, 0.08f), 0f);
        }
    }

    // Planks across X (boardwalks), along a line of Z.
    public static void CrossPlanks(Mk m, float xc, float width, float z0, float z1, float y, float w, float gap, Color[] tones, float thick = 0.05f)
    {
        int n = Mathf.FloorToInt(Mathf.Abs(z1 - z0) / (w + gap));
        float dir = Mathf.Sign(z1 - z0);
        for (int i = 0; i < n; i++)
        {
            float z = z0 + dir * (i * (w + gap) + w / 2);
            m.Box(new Vector3(xc, y - thick / 2 + (m.R() - 0.5f) * 0.01f, z), new Vector3(width + (m.R() - 0.5f) * 0.1f, thick, w), m.Jit(tones[m.rng.Next(tones.Length)], 0.09f), 0f);
        }
    }

    // Round woven rug made of concentric thin discs.
    public static void Rug(Mk m, Vector3 c, float r, Color[] rings)
    {
        for (int i = 0; i < rings.Length; i++)
        {
            float rr = r * (1f - i * (0.9f / rings.Length));
            m.Prism(c + Vector3.up * (0.012f + i * 0.004f), c + Vector3.up * (0.016f + i * 0.004f), rr, rr, 20, rings[i], false, true, 0f);
        }
    }

    // Rectangular rug with a border (agents stand on this so they pop off the floor).
    public static void RectRug(Mk m, Vector3 c, Vector2 size, Color border, Color field, Color stripe)
    {
        m.Box(c + Vector3.up * 0.012f, new Vector3(size.x, 0.014f, size.y), border, 0f);
        m.Box(c + Vector3.up * 0.019f, new Vector3(size.x - 0.28f, 0.014f, size.y - 0.28f), field, 0.02f);
        int stripes = Mathf.RoundToInt(size.x / 0.9f);
        for (int i = 0; i < stripes; i++)
            m.Box(c + new Vector3(-size.x / 2 + 0.45f + i * ((size.x - 0.9f) / Mathf.Max(1, stripes - 1)), 0.027f, 0f), new Vector3(0.04f, 0.006f, size.y - 0.5f), stripe, 0f);
    }

    // Post with a slight taper and a cap.
    public static void Post(Mk m, Vector3 basePt, float h, float w, Color col)
    {
        m.Prism(basePt, basePt + Vector3.up * h, w * 0.58f, w * 0.5f, 6, col, false, true, 0.05f);
    }

    // Catenary string of bulbs from a to b; bulbs go in the glow mesh.
    public static void StringLights(Mk wire, Mk glow, Vector3 a, Vector3 b, float sag, float spacing, Color bulb, Color cord)
    {
        int n = Mathf.Max(2, Mathf.RoundToInt((b - a).magnitude / spacing));
        Vector3 prev = a;
        for (int i = 1; i <= n; i++)
        {
            float u = (float)i / n;
            var p = Vector3.Lerp(a, b, u) + Vector3.down * (sag * 4f * u * (1f - u));
            wire.Beam(prev, p, 0.012f, 0.012f, cord, 0f);
            glow.Blob(p + Vector3.down * 0.05f, new Vector3(0.05f, 0.065f, 0.05f), bulb, bulb, 2, 5, 0f);
            prev = p;
        }
    }

    // Small rounded cloud: flat-bottomed lumpy blobs, baked-shaded (top bright, base tinted).
    public static void Cloud(Mk m, Vector3 c, float scale, Color top, Color bottom)
    {
        int puffs = 4 + m.rng.Next(3);
        for (int i = 0; i < puffs; i++)
        {
            float u = (i - (puffs - 1) / 2f);
            var r = new Vector3(m.R(0.7f, 1.2f), m.R(0.5f, 0.9f), m.R(0.6f, 1f)) * scale * (1.1f - Mathf.Abs(u) * 0.14f);
            m.Blob(c + new Vector3(u * scale * 0.85f, r.y * 0.25f * (i % 2), m.R(-0.2f, 0.2f) * scale), r, bottom, top, 3, 7, 0.1f, null, true);
        }
    }

    // One roof plane from an eave line (at x=0) up to a ridge line. Seen from below (that is where
    // the CEO is): stepped rows of bundles, each row hanging a little lower than the one above,
    // with real teeth along the eave (`teeth`: thatch; plain shingle edge otherwise). The outer
    // face is one cheap golden sheet because nobody ever looks at it.
    public static void Roof(Mk m, Vector3 eave, Vector3 ridge, Color lo, Color hi, Color under0, Color under1, Color rafter, bool teeth, float hx = 7.8f)
    {
        var up = (ridge - eave).normalized; float len = (ridge - eave).magnitude;
        var nrm = Vector3.Cross(up, Vector3.right); if (nrm.y < 0) nrm = -nrm;   // outward (up and away)
        Vector3 P(float px, float s, float off) => eave + Vector3.right * px + up * s + nrm * off;
        // outer sheet
        m.Quad2(P(-hx, -0.1f, 0.12f), P(hx, -0.1f, 0.12f), P(hx, len, 0.12f), P(-hx, len, 0.12f), hi, hi);
        float row = 0.62f; int rows = Mathf.CeilToInt(len / row);
        float bw = teeth ? 0.3f : 0.45f;
        for (int k = 0; k < rows; k++)
        {
            float s0 = k * row, s1 = Mathf.Min(len, s0 + row + 0.05f);
            float sag = -0.05f * (rows - k) / rows - 0.02f;     // lower rows hang lower: steps catch the light
            for (float px = -hx; px < hx - 0.01f; px += bw)
            {
                var cA = m.Jit(Color.Lerp(under0, lo, 0.35f), 0.12f); var cB = m.Jit(Color.Lerp(under1, hi, 0.1f), 0.1f);
                float jig = (m.R() - 0.5f) * 0.12f;
                var a = P(px, s0, sag); var b = P(px + bw, s0, sag); var c = P(px + bw, s1, sag - 0.04f); var d = P(px, s1, sag - 0.04f);
                // facing down/in: lower edge cA (shadowed), upper edge cB
                m.QuadFacing(a, b, c, d, cA, cB, -nrm);
                if (teeth && k < 3)
                    for (int t = 0; t < 2; t++)
                    {
                        float tx = px + bw * (0.25f + 0.5f * t) + jig, tl = 0.1f + m.R() * 0.22f;
                        var tc = m.Jit(lo, 0.15f);
                        m.Tri2(P(tx - 0.07f, s0, sag), P(tx + 0.07f, s0, sag), P(tx + (m.R() - 0.5f) * 0.05f, s0 - tl, sag - 0.03f), tc, tc, m.Jit(under0, 0.1f));
                    }
            }
        }
        for (float rx = -hx + 0.6f; rx <= hx - 0.5f; rx += 1.0f) m.Beam(P(rx, -0.3f, -0.2f), P(rx, len, -0.2f), 0.1f, 0.17f, rafter, 0.04f);
        for (float s = 1.4f; s < len; s += 1.7f) m.Beam(P(-hx, s, -0.14f), P(hx, s, -0.14f), 0.07f, 0.09f, rafter, 0.04f); // purlins
    }

    // Smooth value noise 0..1 and a few octaves of it.
    static float Hash(int x, int y) { unchecked { int h = x * 374761393 + y * 668265263; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xffff) / 65535f; } }
    public static float Noise(float x, float y)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = x - xi, fy = y - yi; fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        return Mathf.Lerp(Mathf.Lerp(Hash(xi, yi), Hash(xi + 1, yi), fx), Mathf.Lerp(Hash(xi, yi + 1), Hash(xi + 1, yi + 1), fx), fy);
    }
    public static float Ridged(float x, float y, int oct = 4)
    {
        float a = 1f, sum = 0f, norm = 0f;
        for (int i = 0; i < oct; i++) { sum += a * (1f - Mathf.Abs(Noise(x, y) * 2f - 1f)); norm += a; x *= 2.07f; y *= 2.07f; a *= 0.5f; }
        return sum / norm;
    }

    // Soft cloud banks drifting at two speeds (two draw calls).
    public static void Clouds(WorldCtx x, Color top, Color bottom, int n, float y0, float y1, float scaleLo, float scaleHi, float d0 = 380f, float d1 = 700f, float spread = 0.6f)
    {
        if (x.theme.panorama) return; // a photographic sky has its own
        for (int layer = 0; layer < 2; layer++)
        {
            var cm = new Mk(40 + layer);
            for (int i = 0; i < n; i++)
            {
                float az = (i - n / 2f) * spread + cm.R(-0.2f, 0.2f) + layer * 0.3f;
                if (Mathf.Abs(az) < 0.16f) az += 0.35f;
                float dist = cm.R(d0, d1);
                Cloud(cm, new Vector3(Mathf.Sin(az) * dist, cm.R(y0, y1) + layer * 40f, -Mathf.Cos(az) * dist), cm.R(scaleLo, scaleHi), top, bottom);
            }
            var g = x.Mesh(cm, "Clouds" + layer, x.Unlit(null, 1f)).transform;
            x.life.Drift(g, new Vector3(layer == 0 ? 1.1f : 0.6f, 0, 0), 600f);
        }
    }

    // Birds circling a point: V wings flap by scaling Y.
    public static void Birds(WorldCtx x, int count, Color wing, Color tip, Vector3 center, float spanM = 1.5f)
    {
        for (int i = 0; i < count; i++)
        {
            var bm = new Mk(60 + i);
            bm.Tri2(new Vector3(0, 0, 0.4f), new Vector3(0, 0, -0.3f), new Vector3(-spanM, 0.3f, -0.15f), wing, wing, tip);
            bm.Tri2(new Vector3(0, 0, 0.4f), new Vector3(0, 0, -0.3f), new Vector3(spanM, 0.3f, -0.15f), wing, wing, tip);
            bm.Blob(Vector3.zero, new Vector3(0.18f, 0.16f, 0.5f), wing, wing, 2, 5, 0f);
            var t = x.Mesh(bm, "Bird" + i, x.Lit()).transform;
            x.life.Bird(t, center + new Vector3(i * 12f, i * 1.6f, -i * 9f), 11f + i * 3f, 0.28f + 0.04f * i, i * 1.7f, 1.6f);
        }
    }

    // Round-ish cone-stacked pine; rigidity falls with height (wind sway).
    public static void Pine(Mk m, Vector3 b, float h, Color dark, Color light)
    {
        m.Prism(b, b + Vector3.up * h * 0.25f, 0.18f * h / 8f, 0.14f * h / 8f, 5, new Color(0.3f, 0.2f, 0.12f), false, true, 0.05f);
        int tiers = 4;
        for (int i = 0; i < tiers; i++)
        {
            float u = (float)i / tiers;
            float y0 = b.y + h * (0.15f + u * 0.62f), r = h * (0.36f - u * 0.2f) * 0.55f * 1.0f;
            float r0 = Mathf.Max(0.3f, r);
            var c = m.Jit(Color.Lerp(dark, light, u), 0.08f);
            m.Prism(new Vector3(b.x, y0, b.z), new Vector3(b.x, y0 + h * 0.4f, b.z), r0, 0.02f, 7, c, true, false, 0.04f, 1f - u * 0.2f - 0.1f, 0.15f);
        }
    }

    // ---------- shared hall: same roof line in every scene ----------
    public const float EAVE_Y = 4.4f, RIDGE_Y = 7.6f, EAVE_FRONT = 2.8f, EAVE_BACK = 18.6f, RIDGE_Z = 10.4f, POST_H = 4.5f, ROOF_HX = 13.9f;

    // Checkered tiles over the whole deck.
    public static void Tiles(Mk m, Color a, Color b, float size = 1.3f)
    {
        int nx = Mathf.CeilToInt((Plan.X1 - Plan.X0) / size), nz = Mathf.CeilToInt((Plan.Z1 - Plan.Z0) / size);
        for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
                m.Box(new Vector3(Plan.X0 + (ix + 0.5f) * size, -0.015f, Plan.Z0 + (iz + 0.5f) * size), new Vector3(size - 0.03f, 0.03f, size - 0.03f), m.Jit(((ix + iz) & 1) == 0 ? a : b, 0.03f), 0f);
    }

    // The CEO's own spot: round rug + lounger.
    public static void Seat(Mk m, Color[] rug)
    {
        Rug(m, new Vector3(0, 0, 6.6f), 1.35f, rug);
        BeachSc.Lounger(m, new Vector3(0f, 0f, 6.9f));
    }

    // ---------- shared furniture: same plan in every scene, restyled ----------
    public struct Style
    {
        public Color wood, wood2, fabric, fabric2, metal, accent, plant, screen, pot;
        public bool holo; // desks built as glowing slabs (galaxy)
    }

    public static void Furniture(Mk m, Mk glow, Style st)
    {
        // standing desks (agents stand on the -X side facing +X)
        foreach (var d in Plan.DeskPos)
        {
            m.Box(d + new Vector3(0, 0.74f, 0), new Vector3(0.95f, 0.06f, 1.75f), m.Jit(st.wood, 0.05f), 0.02f);
            if (st.holo) glow.Box(d + new Vector3(0, 0.705f, 0), new Vector3(0.97f, 0.01f, 1.77f), st.accent, 0f);
            foreach (float sz in new[] { -0.8f, 0.8f }) m.Box(d + new Vector3(0, 0.36f, sz), new Vector3(0.85f, 0.72f, 0.06f), st.wood2, 0.02f);
            m.Box(d + new Vector3(0.12f, 0.98f, 0), new Vector3(0.04f, 0.42f, 0.78f), Quaternion.Euler(0, 0, 0), st.metal, 0.02f);
            glow.Box(d + new Vector3(0.095f, 0.98f, 0), new Vector3(0.012f, 0.36f, 0.7f), st.screen, 0.05f);
            m.Box(d + new Vector3(0.12f, 0.8f, 0), new Vector3(0.06f, 0.1f, 0.2f), st.metal, 0f);
            m.Box(d + new Vector3(-0.22f, 0.785f, 0.1f), new Vector3(0.22f, 0.02f, 0.5f), st.metal, 0f);       // keyboard
            m.Prism(d + new Vector3(-0.12f, 0.77f, -0.62f), d + new Vector3(-0.12f, 0.87f, -0.62f), 0.04f, 0.04f, 6, st.accent, false, true);   // mug
        }
        // lounge: two sofas facing a low table, an armchair, a rug, a lamp
        RectRug(m, new Vector3(-8.5f, 0f, 10f), new Vector2(5.4f, 5.4f), st.fabric2, Color.Lerp(st.fabric2, Color.white, 0.55f), st.accent);
        Sofa(m, new Vector3(-8.5f, 0f, 7.2f), 0f, st);
        Sofa(m, new Vector3(-8.5f, 0f, 12.8f), 180f, st);
        Armchair(m, new Vector3(-11.6f, 0f, 10f), 90f, st);
        m.Box(new Vector3(-8.5f, 0.38f, 10f), new Vector3(1.7f, 0.07f, 0.95f), st.wood, 0.03f);
        foreach (float tx in new[] { -0.7f, 0.7f }) foreach (float tz in new[] { -0.35f, 0.35f }) m.Prism(new Vector3(-8.5f + tx, 0, 10f + tz), new Vector3(-8.5f + tx, 0.38f, 10f + tz), 0.04f, 0.04f, 5, st.wood2, false, false);
        m.Prism(new Vector3(-8.3f, 0.41f, 10f), new Vector3(-8.3f, 0.5f, 10f), 0.07f, 0.06f, 6, st.accent, false, true);
        m.Prism(new Vector3(-8.8f, 0.41f, 10.1f), new Vector3(-8.8f, 0.49f, 10.1f), 0.05f, 0.05f, 6, st.fabric, false, true);
        // plants
        foreach (var b in Plan.Blocks) if (b.kind == "plant") Plant(m, new Vector3(b.c.x, 0, b.c.y), st);
        // back bar with stools
        foreach (var b in Plan.Blocks)
            if (b.kind == "bar")
            {
                m.Box(new Vector3(b.c.x, 0.52f, b.c.y), new Vector3(b.half.x * 2, 1.04f, b.half.y * 2), st.wood2, 0.03f);
                m.Box(new Vector3(b.c.x, 1.07f, b.c.y), new Vector3(b.half.x * 2 + 0.1f, 0.07f, b.half.y * 2 + 0.12f), st.wood, 0.03f);
                for (float sx = -b.half.x + 0.8f; sx < b.half.x; sx += 1.5f)
                {
                    m.Prism(new Vector3(b.c.x + sx, 0, b.c.y - 0.95f), new Vector3(b.c.x + sx, 0.62f, b.c.y - 0.95f), 0.05f, 0.04f, 6, st.metal, false, false);
                    m.Prism(new Vector3(b.c.x + sx, 0.62f, b.c.y - 0.95f), new Vector3(b.c.x + sx, 0.68f, b.c.y - 0.95f), 0.2f, 0.2f, 8, st.fabric, true, true);
                }
            }
    }

    static void Sofa(Mk m, Vector3 c, float yaw, Style st)
    {
        var q = Quaternion.Euler(0, yaw, 0);
        Vector3 W(float x, float y, float z) => c + q * new Vector3(x, y, z);
        m.Box(W(0, 0.2f, 0), new Vector3(3.2f, 0.36f, 1.05f), q, st.wood2, 0.03f);
        m.Box(W(-0.8f, 0.5f, 0.04f), new Vector3(1.5f, 0.2f, 0.85f), q, st.fabric, 0.03f);
        m.Box(W(0.8f, 0.5f, 0.04f), new Vector3(1.5f, 0.2f, 0.85f), q, st.fabric, 0.03f);
        m.Box(W(0, 0.78f, -0.45f), new Vector3(3.2f, 0.7f, 0.2f), q, st.fabric, 0.03f);
        foreach (float sx in new[] { -1.55f, 1.55f }) m.Box(W(sx, 0.55f, 0), new Vector3(0.2f, 0.5f, 1.05f), q, st.fabric, 0.03f);
        m.Box(W(-1.1f, 0.78f, -0.15f), new Vector3(0.5f, 0.45f, 0.14f), q * Quaternion.Euler(-14, 0, 0), st.accent, 0.03f);
    }

    static void Armchair(Mk m, Vector3 c, float yaw, Style st)
    {
        var q = Quaternion.Euler(0, yaw, 0);
        Vector3 W(float x, float y, float z) => c + q * new Vector3(x, y, z);
        m.Box(W(0, 0.2f, 0), new Vector3(1.0f, 0.36f, 1.0f), q, st.wood2, 0.03f);
        m.Box(W(0, 0.46f, 0.05f), new Vector3(0.8f, 0.16f, 0.8f), q, st.fabric, 0.03f);
        m.Box(W(0, 0.75f, -0.42f), new Vector3(1.0f, 0.7f, 0.16f), q, st.fabric, 0.03f);
        foreach (float sx in new[] { -0.46f, 0.46f }) m.Box(W(sx, 0.5f, 0), new Vector3(0.14f, 0.4f, 0.95f), q, st.fabric, 0.03f);
    }

    static void Plant(Mk m, Vector3 c, Style st)
    {
        m.Prism(c, c + Vector3.up * 0.5f, 0.3f, 0.36f, 8, st.pot, false, true, 0.04f);
        for (int i = 0; i < 7; i++)
        {
            float a = i * 0.9f + m.R() * 0.4f;
            var tip = c + new Vector3(Mathf.Cos(a) * 0.55f, 1.2f + m.R() * 0.7f, Mathf.Sin(a) * 0.55f);
            var s = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)) * 0.13f;
            var g = m.Jit(st.plant, 0.15f);
            m.Tri2(c + Vector3.up * 0.5f - s, c + Vector3.up * 0.5f + s, tip, Mk.Rig(g, 1f), Mk.Rig(g, 1f), Color.Lerp(g, Color.white, 0.2f));
        }
    }
}
