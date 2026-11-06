// VIRTUAL LOFT — GALAXY: a curved-rib observation deck floating in space: dark
// glass floor with glowing seams, a ringed planet and moon, nebula + stars in the
// sky shader (slowly turning), drifting dust motes and comets.
using UnityEngine;

public static class GalaxySc
{
    static Color H(string s) => Mk.Hex(s);

    public static void Build(WorldCtx x)
    {
        var m = new Mk(71); var glow = new Mk(72);
        var floorA = H("#12162B"); var floorB = H("#181D3A"); var rib = H("#8E9BC9"); var ribD = H("#4A557F");

        m.Box(new Vector3(0, -0.04f, 5.2f), new Vector3(LoftWorld.DECK_HALF * 2, 0.04f, 10.4f), H("#07091A"), 0f);
        for (int ix = 0; ix < 12; ix++)
            for (int iz = 0; iz < 9; iz++)
                m.Box(new Vector3(-LoftWorld.DECK_HALF + 0.6f + ix * 1.23f, -0.015f, 0.62f + iz * 1.2f), new Vector3(1.2f, 0.03f, 1.17f), m.Jit(((ix + iz) & 1) == 0 ? floorA : floorB, 0.05f), 0f);
        // glowing seams + front edge light
        for (int i = 0; i <= 12; i++) glow.Box(new Vector3(-LoftWorld.DECK_HALF + i * 1.233f, 0.002f, 5.2f), new Vector3(0.014f, 0.004f, 10.4f), H("#3E6BFF"), 0f);
        glow.Box(new Vector3(0, 0.01f, 0.06f), new Vector3(LoftWorld.DECK_HALF * 2, 0.02f, 0.07f), H("#7FD4FF"), 0f);
        m.Beam(new Vector3(-LoftWorld.DECK_HALF, -0.15f, 0f), new Vector3(LoftWorld.DECK_HALF, -0.15f, 0f), 0.2f, 0.3f, ribD, 0.02f);

        // curved ribs: arches over the deck, front ones high so the view stays open
        var zs = new[] { 5.4f, 7.4f, 9.4f, 10.5f };
        foreach (float rz in zs)
        {
            var pts = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i <= 18; i++)
            {
                float a = Mathf.PI * i / 18f; // 0 right wall -> pi left wall
                pts.Add(new Vector3(Mathf.Cos(a) * 7.4f, 0.1f + Mathf.Sin(a) * 4.6f * (1f - (rz - 5.4f) * 0.01f), rz));
            }
            m.Tube(pts, t => 0.08f + 0.05f * Mathf.Sin(t * Mathf.PI), 6, t => Color.Lerp(ribD, rib, Mathf.Sin(t * Mathf.PI)), null);
            for (int i = 2; i < 17; i += 3) glow.Box(pts[i] + Vector3.down * 0.1f, new Vector3(0.1f, 0.025f, 0.1f), H("#9FD0FF"), 0f);
        }
        for (int k = 0; k < 7; k++)
        {   // longitudinal ties between the ribs
            float a = Mathf.PI * (1 + k * 2) / 14f;
            var p0 = new Vector3(Mathf.Cos(a) * 7.4f, 0.1f + Mathf.Sin(a) * 4.6f, 5.4f); var p1 = new Vector3(Mathf.Cos(a) * 7.4f, 0.1f + Mathf.Sin(a) * 4.6f * 0.95f, 10.5f);
            m.Beam(p0, p1, 0.06f, 0.06f, ribD, 0.02f);
        }
        foreach (float sx in new[] { -7.4f, 7.4f }) m.Beam(new Vector3(sx, 0f, 5.4f), new Vector3(sx, 0f, 10.5f), 0.16f, 0.2f, ribD, 0.02f);
        glow.Box(new Vector3(0, 4.6f, 7.95f), new Vector3(0.04f, 0.04f, 5.2f), H("#8FB8FF"), 0f);
        // seat + agents' pad
        SceneKit.Rug(m, new Vector3(0, 0, 6.6f), 1.35f, new[] { H("#222850"), H("#4C6BFF"), H("#222850") });
        BeachSc.Lounger(m, new Vector3(0f, 0f, 6.9f));
        SceneKit.RectRug(m, new Vector3(0, 0, 3.2f), new Vector2(6.4f, 2.6f), H("#3E5BEA"), H("#222A58"), H("#34428C"));
        glow.Box(new Vector3(0, 0.03f, 1.92f), new Vector3(7.2f, 0.01f, 0.04f), H("#7FD4FF"), 0f);
        glow.Box(new Vector3(0, 0.03f, 5.28f), new Vector3(7.2f, 0.01f, 0.04f), H("#7FD4FF"), 0f);
        x.Mesh(m, "Shell", x.Lit());
        x.Mesh(glow, "Glow", x.Unlit(null, 1.5f));

        // planet with rings + moon, shading baked toward a star at the viewer's left
        var pm = new Mk(73);
        var L = new Vector3(0.55f, 0.35f, 0.75f).normalized;
        var c0 = H("#D9A066"); var c1 = H("#8E5A3C"); var c2 = H("#F0D2A0");
        var pc = new Vector3(-300f, 200f, -950f);
        pm.Sphere(pc, 170f, 16, 28, n =>
        {
            float band = 0.5f + 0.5f * Mathf.Sin(n.y * 9f + Mathf.Sin(n.x * 3f) * 0.6f);
            var c = Color.Lerp(Color.Lerp(c1, c0, band), c2, Mathf.Clamp01(Mathf.Sin(n.y * 21f) * 0.35f));
            float l = Mathf.Clamp01(Vector3.Dot(n, L)) * 0.85f + 0.12f;
            return c * l;
        });
        pm.Annulus(pc, Quaternion.Euler(-15f, 0f, 22f), 225f, 345f, 40, new Color(0.9f, 0.8f, 0.65f), new Color(0.45f, 0.4f, 0.5f));
        pm.Annulus(pc, Quaternion.Euler(-15f, 0f, 22f), 360f, 395f, 40, new Color(0.6f, 0.55f, 0.6f), new Color(0.3f, 0.3f, 0.4f));
        var mc = new Vector3(300f, 110f, -650f);
        pm.Sphere(mc, 46f, 8, 14, n => Color.Lerp(H("#6E7184"), H("#C9CCD8"), Mathf.Clamp01(Vector3.Dot(n, L))) * (0.2f + 0.8f * Mathf.Clamp01(Vector3.Dot(n, L) + 0.2f)));
        var pg = x.Mesh(pm, "Planet", x.Unlit(null, 1f)).transform;
        x.life.Spin(pg, new Vector3(0f, 0.15f, 0f)); // never visibly: tiny drift keeps the sky alive
        pg.localPosition = Vector3.zero;

        // dust motes drifting past (parallax), comets
        var dust = new Mk(74);
        for (int i = 0; i < 160; i++)
        {
            var p = new Vector3(dust.R(-40f, 40f), dust.R(-6f, 18f), dust.R(-60f, 10f));
            float s = dust.R(0.02f, 0.06f);
            var c = Color.Lerp(H("#9FC4FF"), H("#FFD9F5"), dust.R());
            dust.Tri(p, p + new Vector3(s, 0, 0), p + new Vector3(0, s, 0), c, c, c);
            dust.Tri(p, p + new Vector3(0, s, 0), p + new Vector3(s, 0, 0), c, c, c);
        }
        var dg = x.Mesh(dust, "Dust", x.Unlit(null, 1.6f, 0, 0.5f)).transform;
        x.life.Drift(dg, new Vector3(0.08f, 0f, 0f), 6f);
        for (int i = 0; i < 2; i++)
        {
            var cm = new Mk(80 + i);
            var head = new Color(1f, 1f, 1f, 1f); var tail = new Color(0.4f, 0.7f, 1f, 0f);
            cm.Quad2(new Vector3(0, 0, 0), new Vector3(-60f, 0, 4f), new Vector3(-60f, 1.2f, 4f), new Vector3(0, 1.2f, 0), head, tail);
            cm.Quad2(new Vector3(0, 0.6f, 0), new Vector3(-60f, 1.2f, 4f), new Vector3(-60f, -0.4f, 4f), new Vector3(0, -0.2f, 0), head, tail);
            var cg = x.Mesh(cm, "Comet" + i, x.Unlit(null, 2f, 2, 0f, 0f, 2500)).transform;
            int ii = i;
            x.life.Add((t, dt) =>
            {
                if (cg == null) return;
                float u = Mathf.Repeat(t * 0.035f + ii * 0.5f, 1f);
                cg.localPosition = new Vector3(Mathf.Lerp(700f, -700f, u), 120f + ii * 70f - u * 60f, -900f + ii * 150f);
            });
        }
        // sky turns very slowly (stars + nebula)
        x.life.Add((t, dt) => { if (x.sky != null) x.sky.SetFloat("_Rot", t * 0.004f); });
    }
}
