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

    // One roof plane from an eave line (at x=0) up to a ridge line: underlay, rafters, overlapping rows
    // (scalloped thatch when `teeth`, plain shingles otherwise).
    public static void Roof(Mk m, Vector3 eave, Vector3 ridge, Color lo, Color hi, Color under0, Color under1, Color rafter, bool teeth, float hx = 7.8f)
    {
        var up = (ridge - eave).normalized; float len = (ridge - eave).magnitude;
        var nrm = Vector3.Cross(up, Vector3.right); if (nrm.y < 0) nrm = -nrm;
        Vector3 P(float px, float s, float off) => eave + Vector3.right * px + up * s + nrm * off;
        m.Quad2(P(-hx, 0, -0.07f), P(hx, 0, -0.07f), P(hx, len, -0.07f), P(-hx, len, -0.07f), under0, under1);
        for (float rx = -hx + 0.6f; rx <= hx - 0.5f; rx += 0.9f) m.Beam(P(rx, -0.25f, -0.15f), P(rx, len, -0.15f), 0.09f, 0.16f, rafter, 0.04f);
        int rows = Mathf.CeilToInt(len / 0.5f);
        for (int k = 0; k < rows; k++)
        {
            float s0 = k * 0.5f, s1 = Mathf.Min(len, s0 + 0.78f), off = (rows - k) * 0.011f;
            for (float px = -hx; px < hx - 0.01f; px += 0.4f)
            {
                var c0 = m.Jit(lo, 0.1f); var c1 = m.Jit(hi, 0.07f);
                var a = P(px, s0, off); var b = P(px + 0.4f, s0, off);
                m.Quad2(a, b, P(px + 0.4f, s1, off), P(px, s1, off), c0, c1);
                if (teeth) m.Tri2(a, P(px + 0.2f, s0 - 0.17f, off), b, c0, c0, c0);
            }
        }
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
}
