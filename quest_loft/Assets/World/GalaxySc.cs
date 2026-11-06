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

        m.Box(new Vector3(0, -0.04f, 9f), new Vector3(26f, 0.04f, 18f), H("#07091A"), 0f);
        SceneKit.Tiles(m, floorA, floorB);
        // glowing seams + edge light
        for (float sx = Plan.X0; sx <= Plan.X1 + 0.01f; sx += 2.6f) glow.Box(new Vector3(sx, 0.002f, 9f), new Vector3(0.014f, 0.004f, 18f), H("#3E6BFF"), 0f);
        glow.Box(new Vector3(0, 0.01f, 0.06f), new Vector3(26f, 0.02f, 0.07f), H("#7FD4FF"), 0f);
        foreach (float sx in new[] { -12.97f, 12.97f }) glow.Box(new Vector3(sx, 0.01f, 9f), new Vector3(0.07f, 0.02f, 18f), H("#7FD4FF"), 0f);
        m.Beam(new Vector3(Plan.X0, -0.15f, 0f), new Vector3(Plan.X1, -0.15f, 0f), 0.2f, 0.3f, ribD, 0.02f);

        // curved ribs: tall arches over the deck, front ones far ahead so the view stays open
        const float RX = 13f, RH = 8.2f;
        var zs = new[] { 4f, 8.6f, 13.2f, 17.8f };
        foreach (float rz in zs)
        {
            var pts = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i <= 22; i++)
            {
                float a = Mathf.PI * i / 22f; // 0 right wall -> pi left wall
                pts.Add(new Vector3(Mathf.Cos(a) * RX, 0.1f + Mathf.Sin(a) * RH, rz));
            }
            m.Tube(pts, t => 0.11f + 0.06f * Mathf.Sin(t * Mathf.PI), 6, t => Color.Lerp(ribD, rib, Mathf.Sin(t * Mathf.PI)), null);
            for (int i = 2; i < 21; i += 3) glow.Box(pts[i] + Vector3.down * 0.14f, new Vector3(0.14f, 0.03f, 0.14f), H("#9FD0FF"), 0f);
        }
        for (int k = 0; k < 7; k++)
        {   // longitudinal ties between the ribs
            float a = Mathf.PI * (1 + k * 2) / 14f;
            var p = new Vector3(Mathf.Cos(a) * RX, 0.1f + Mathf.Sin(a) * RH, 0f);
            m.Beam(p + Vector3.forward * 4f, p + Vector3.forward * 17.8f, 0.08f, 0.08f, ribD, 0.02f);
        }
        foreach (float sx in new[] { -RX, RX }) m.Beam(new Vector3(sx, 0f, 4f), new Vector3(sx, 0f, 17.8f), 0.18f, 0.22f, ribD, 0.02f);
        glow.Box(new Vector3(0, RH - 0.1f, 10.9f), new Vector3(0.05f, 0.05f, 13.8f), H("#8FB8FF"), 0f);
        SceneKit.Seat(m, new[] { H("#222850"), H("#4C6BFF"), H("#222850") });
        SceneKit.Furniture(m, glow, new SceneKit.Style { wood = H("#1C2244"), wood2 = H("#12162B"), fabric = H("#3A4A9A"), fabric2 = H("#222A58"), metal = H("#8E9BC9"), accent = H("#7FD4FF"), plant = H("#4C7BE0"), screen = H("#7FD4FF"), pot = H("#222850"), holo = true });
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
