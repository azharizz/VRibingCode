// VIRTUAL LOFT — SCENERY: a mountain lodge deck (log posts, shingle roof, stone
// fireplace) open to a valley: lake, layered peaks with snow, pines swaying,
// drifting clouds and mist, eagles.
using UnityEngine;

public static class ScenerySc
{
    const float LAKE_Y = -48f;
    static Color H(string s) => Mk.Hex(s);

    // One heightfield for foreground slope and far peaks (continuous, so no seams).
    public static float Terrain(float x, float z)
    {
        float d = -z;
        float valley = Mathf.SmoothStep(0f, 1f, (Mathf.Abs(x) - 200f) / 480f);
        float depth = Mathf.SmoothStep(0f, 1f, (d - 80f) / 1000f);
        float mt = SceneKit.Ridged(x * 0.0019f + 3f, z * 0.0019f, 3);
        float far = Mathf.SmoothStep(0f, 1f, (d - 650f) / 750f);
        float h = -62f + (valley * 0.85f + depth * 0.22f) * (mt * 430f + 60f) + far * mt * 260f;
        float pad = Mathf.Pow(Mathf.Clamp01(d / 150f), 1.25f);
        return Mathf.Lerp(-0.35f, h, Mathf.SmoothStep(0f, 1f, pad));
    }

    public static void Build(WorldCtx x)
    {
        var m = new Mk(31); var glow = new Mk(32); var trees = new Mk(33);
        var planks = new[] { H("#8A5F3B"), H("#7B5434"), H("#946A44"), H("#85583A") };
        var logC = H("#7A5230"); var logD = H("#4D3220"); var stone = H("#8C8A82"); var stoneL = H("#A8A59A");

        SceneKit.Planks(m, Plan.X0, Plan.X1, Plan.Z0, Plan.Z1, 0f, 0.34f, 0.012f, planks);
        m.Beam(new Vector3(Plan.X0, -0.15f, 0f), new Vector3(Plan.X1, -0.15f, 0f), 0.2f, 0.3f, logD, 0.03f);
        for (float px = Plan.X0 + 0.3f; px <= Plan.X1; px += 2.6f) m.Prism(new Vector3(px, -30f, 0.2f), new Vector3(px, -0.1f, 0.2f), 0.2f, 0.2f, 7, logD, false, true);
        foreach (float sx in new[] { Plan.X0 + 0.08f, Plan.X1 - 0.08f })
        {
            m.Prism(new Vector3(sx, 1.0f, 0.2f), new Vector3(sx, 1.0f, 17.8f), 0.07f, 0.07f, 6, logC, false, true);
            m.Prism(new Vector3(sx, 0.5f, 0.2f), new Vector3(sx, 0.5f, 17.8f), 0.055f, 0.055f, 6, logC, false, true);
            for (float pz = 0.2f; pz <= 17.9f; pz += 2.2f) m.Prism(new Vector3(sx, 0f, pz), new Vector3(sx, 1.1f, pz), 0.09f, 0.08f, 6, logD, false, true);
        }
        // boulders + lantern posts along the open front edge
        foreach (float bx in new[] { -12.4f, -8f, 8f, 12.4f }) m.Blob(new Vector3(bx, 0.15f, 0.25f), new Vector3(0.65f, 0.45f, 0.5f), stone, stoneL, 3, 7, 0.15f);
        foreach (float lx in new[] { -3f, 3f })
        {
            m.Prism(new Vector3(lx, 0f, 0.15f), new Vector3(lx, 0.95f, 0.15f), 0.07f, 0.06f, 6, logD, false, true);
            glow.Blob(new Vector3(lx, 1.08f, 0.15f), new Vector3(0.13f, 0.18f, 0.13f), H("#FF9F4A"), H("#FFD890"), 3, 6, 0.02f);
        }

        // log frame + shingle roof
        foreach (var pp in Plan.Posts)
        {
            m.Prism(new Vector3(pp.x, 0f, pp.y), new Vector3(pp.x, SceneKit.POST_H, pp.y), 0.24f, 0.2f, 8, logC, false, true, 0.05f);
            m.Box(new Vector3(pp.x, 0.25f, pp.y), new Vector3(0.74f, 0.5f, 0.74f), stone, 0.08f);
            m.Box(new Vector3(pp.x, 0.62f, pp.y), new Vector3(0.58f, 0.28f, 0.58f), stoneL, 0.08f);
        }
        foreach (float bz in new[] { 4f, 10.3f, 16.6f }) m.Prism(new Vector3(-13.2f, SceneKit.POST_H - 0.05f, bz), new Vector3(13.2f, SceneKit.POST_H - 0.05f, bz), 0.21f, 0.21f, 8, logD, true, true);
        foreach (float px in new[] { -12.5f, -5f, 5f, 12.5f }) m.Prism(new Vector3(px, SceneKit.POST_H - 0.05f, 2.9f), new Vector3(px, SceneKit.POST_H - 0.05f, 17.5f), 0.19f, 0.19f, 8, logD, true, true);
        Color s0 = H("#5B4636"), s1 = H("#8D7660"), u0 = H("#4A3626"), u1 = H("#6B523C");
        SceneKit.Roof(m, new Vector3(0, SceneKit.EAVE_Y, SceneKit.EAVE_FRONT), new Vector3(0, SceneKit.RIDGE_Y, SceneKit.RIDGE_Z), s0, s1, u0, u1, logD, false, SceneKit.ROOF_HX);
        SceneKit.Roof(m, new Vector3(0, SceneKit.EAVE_Y, SceneKit.EAVE_BACK), new Vector3(0, SceneKit.RIDGE_Y, SceneKit.RIDGE_Z), s0, s1, u0, u1, logD, false, SceneKit.ROOF_HX);
        m.Prism(new Vector3(-14f, SceneKit.RIDGE_Y + 0.04f, SceneKit.RIDGE_Z), new Vector3(14f, SceneKit.RIDGE_Y + 0.04f, SceneKit.RIDGE_Z), 0.32f, 0.32f, 6, H("#3E2D20"), true, true);

        // back wall: stacked logs, stone fireplace with a fire behind the lounge
        for (int i = 0; i < 14; i++) m.Prism(new Vector3(-13f, 0.17f + i * 0.31f, 17.9f), new Vector3(13f, 0.17f + i * 0.31f, 17.9f), 0.16f, 0.16f, 6, m.Jit(logC, 0.08f), true, true);
        for (int i = 0; i < 34; i++)
        {
            float bx = -12.2f + (i % 6) * 0.42f + (i / 6 % 2) * 0.2f, by = 0.2f + (i / 6) * 0.33f;
            m.Box(new Vector3(bx, by + 0.1f, 17.6f), new Vector3(0.4f, 0.3f, 0.35f), m.Jit(m.R() < 0.5f ? stone : stoneL, 0.1f), 0.02f);
        }
        glow.Blob(new Vector3(-11.2f, 0.4f, 17.3f), new Vector3(0.45f, 0.4f, 0.12f), H("#FF6A1F"), H("#FFD060"), 3, 6, 0.1f);

        SceneKit.Seat(m, new[] { H("#C9B28A"), H("#7A2E2A"), H("#C9B28A") });
        SceneKit.Furniture(m, glow, new SceneKit.Style { wood = H("#8A5F3B"), wood2 = H("#4D3220"), fabric = H("#B9A27A"), fabric2 = H("#7A2E2A"), metal = H("#2F2A26"), accent = H("#C9663A"), plant = H("#3C7A4A"), screen = H("#BFE3D0"), pot = H("#6B523C") });

        // hanging lanterns under the beams
        foreach (float lx in new[] { -9f, 0f, 9f })
            foreach (float lz in new[] { 4f, 10.3f })
            {
                m.Beam(new Vector3(lx, SceneKit.POST_H - 0.1f, lz), new Vector3(lx, SceneKit.POST_H - 0.6f, lz), 0.015f, 0.015f, logD, 0f);
                glow.Blob(new Vector3(lx, SceneKit.POST_H - 0.76f, lz), new Vector3(0.14f, 0.2f, 0.14f), H("#FFA34F"), H("#FFD890"), 3, 6, 0.02f);
            }

        // ---- terrain: foreground slope + far peaks ----
        var ter = new Mk(34);
        ter.Grid(-130f, 130f, 22f, -90f, 52, 44, Terrain, TerrainColor);
        ter.Grid(-1100f, 1100f, -90f, -1500f, 110, 78, Terrain, TerrainColor);
        x.Mesh(ter, "Terrain", x.Lit());

        // pines on the near slope + big framing pines
        for (int i = 0; i < 70; i++)
        {
            float tx = ter.R(-55f, 55f), tz = -ter.R(4f, 80f);
            if (Mathf.Abs(tx) < 15f && tz > -20f) continue;
            float ty = Terrain(tx, tz);
            if (ty < LAKE_Y + 2f) continue;
            SceneKit.Pine(trees, new Vector3(tx, ty - 0.3f, tz), ter.R(6f, 13f), H("#1F4A33"), H("#3C7A4A"));
        }
        foreach (var (px, pz, h) in new[] { (-15.5f, -1f, 11f), (16f, -1.5f, 12f), (-20f, -8f, 13f), (21f, -9f, 13f), (-17f, 8f, 12f), (17.5f, 8f, 12f) })
            SceneKit.Pine(trees, new Vector3(px, Terrain(px, pz) - 0.2f, pz), h, H("#1F4A33"), H("#43854F"));
        x.Mesh(m, "Shell", x.Lit());
        x.Mesh(glow, "Glow", x.Unlit(null, 1.3f));
        x.Mesh(trees, "Pines", x.Lit(0.28f, 1.1f));

        // lake
        var lake = new Mk(35);
        for (int i = 0; i < 40; i++)
            for (int j = 0; j < 40; j++)
            {
                float xa = Mathf.Lerp(-1100f, 1100f, i / 40f), xb = Mathf.Lerp(-1100f, 1100f, (i + 1) / 40f);
                float za = Mathf.Lerp(-90f, -1500f, j / 40f), zb = Mathf.Lerp(-90f, -1500f, (j + 1) / 40f);
                var c = Color.white;
                lake.TriOut(new Vector3(xa, 0, za), new Vector3(xb, 0, za), new Vector3(xb, 0, zb), c, c, c, Vector3.down);
                lake.TriOut(new Vector3(xa, 0, za), new Vector3(xb, 0, zb), new Vector3(xa, 0, zb), c, c, c, Vector3.down);
            }
        var lg = x.Mesh(lake, "Lake", x.water, null, new Vector3(0, LAKE_Y, 0));
        lg.GetComponent<MeshFilter>().sharedMesh.bounds = new Bounds(new Vector3(0, 0, -700), new Vector3(3000, 20, 3000));

        // life: clouds high and soft, mist banks low in the valley, eagles
        SceneKit.Clouds(x, H("#FFFFFF"), H("#D8DDE8"), 8, 110f, 220f, 30f, 55f, 500f, 900f);
        var mist = new Mk(36);
        for (int i = 0; i < 9; i++)
        {
            float az = (i - 4) * 0.5f + mist.R(-0.2f, 0.2f), dist = mist.R(250f, 700f);
            mist.Blob(new Vector3(Mathf.Sin(az) * dist, mist.R(-45f, -15f), -Mathf.Cos(az) * dist), new Vector3(mist.R(70f, 140f), mist.R(5f, 11f), mist.R(30f, 60f)),
                new Color(0.95f, 0.95f, 0.96f, 0.0f), new Color(1f, 1f, 1f, 0.5f), 2, 8, 0.08f, null, true);
        }
        var mg = x.Mesh(mist, "Mist", x.Unlit(new Color(1, 1, 1, 0.55f), 1f, 1, 0f, 0f, 2450)).transform;
        x.life.Drift(mg, new Vector3(0.7f, 0, 0), 500f);
        SceneKit.Birds(x, 3, H("#3A3128"), H("#1C1812"), new Vector3(-30f, 30f, -70f), 2.2f);
    }

    static Color TerrainColor(float px, float py, float pz)
    {
        float n = SceneKit.Noise(px * 0.02f, pz * 0.02f);
        if (py > 250f + n * 40f) return Color.Lerp(H("#E8EEF5"), H("#FFFFFF"), n);
        if (py > 150f) return Color.Lerp(H("#8B8A8C"), H("#B9B7B2"), n);
        if (py > 60f) return Color.Lerp(H("#5F6B56"), H("#8A8D77"), n);
        return Color.Lerp(H("#2F5C3E"), H("#4C8650"), n);
    }
}
