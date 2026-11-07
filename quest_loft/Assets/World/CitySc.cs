// VIRTUAL LOFT — CITY: a glass penthouse (slim frames, dark stone floor, light
// strips) high above a dusk skyline: layered towers with blinking windows, an
// elevated expressway with traffic streaks, planes and a beacon tower.
using UnityEngine;

public static class CitySc
{
    const float GROUND = -150f;
    static Color H(string s) => Mk.Hex(s);

    public static void Build(WorldCtx x)
    {
        var m = new Mk(51); var glow = new Mk(52);
        var tileA = H("#2A2D37"); var tileB = H("#323644"); var steel = H("#9AA3B2"); var darkSteel = H("#3A404C");

        // floor: stone tiles + glowing seam at the edge
        m.Box(new Vector3(0, -0.04f, 9f), new Vector3(26f, 0.04f, 18f), darkSteel, 0f);
        SceneKit.Tiles(m, tileA, tileB);
        m.Beam(new Vector3(Plan.X0, -0.18f, 0f), new Vector3(Plan.X1, -0.18f, 0f), 0.3f, 0.42f, darkSteel, 0.02f);
        glow.Box(new Vector3(0, 0.005f, 0.1f), new Vector3(26f, 0.012f, 0.06f), H("#5FE6FF"), 0f);
        foreach (float sx in new[] { -12.9f, 12.9f }) glow.Box(new Vector3(sx, 0.005f, 9f), new Vector3(0.05f, 0.012f, 17.8f), H("#FF7AC8"), 0f);

        // glass walls front and sides: slim mullions and transoms only, nothing in the way
        const float CEIL = 5.8f;
        foreach (float mx in new[] { -12.9f, -9.6f, -6.4f, -3.2f, 3.2f, 6.4f, 9.6f, 12.9f })
            m.Box(new Vector3(mx, CEIL / 2, 0.35f), new Vector3(0.07f, CEIL, 0.1f), steel, 0.02f);
        foreach (float sx in new[] { -12.95f, 12.95f })
        {
            for (float mz = 3.5f; mz < 18f; mz += 3.5f) m.Box(new Vector3(sx, CEIL / 2, mz), new Vector3(0.1f, CEIL, 0.07f), steel, 0.02f);
            m.Box(new Vector3(sx, 4.6f, 9f), new Vector3(0.1f, 0.07f, 17.6f), steel, 0.02f);
            m.Box(new Vector3(sx, 0.08f, 9f), new Vector3(0.12f, 0.16f, 17.6f), darkSteel, 0.02f);
        }
        m.Box(new Vector3(0, 4.6f, 0.35f), new Vector3(26f, 0.07f, 0.1f), steel, 0.02f);
        m.Box(new Vector3(0, 0.08f, 0.35f), new Vector3(26f, 0.16f, 0.12f), darkSteel, 0.02f);
        // roof slab with light panels, back wall, columns
        m.Box(new Vector3(0, CEIL + 0.15f, 9f), new Vector3(26.4f, 0.28f, 18.2f), H("#20232C"), 0.02f);
        m.Box(new Vector3(0, CEIL - 0.1f, 0.3f), new Vector3(26.4f, 0.3f, 0.25f), steel, 0.01f);
        var ceil = new Mk(50);
        ceil.QuadFacing(new Vector3(-13f, CEIL, 0.2f), new Vector3(13f, CEIL, 0.2f), new Vector3(13f, CEIL, 18f), new Vector3(-13f, CEIL, 18f), H("#4B3F44"), H("#5C4D52"), Vector3.down);
        for (int i = 0; i < 6; i++) glow.Box(new Vector3(0, CEIL - 0.01f, 2.4f + i * 2.8f), new Vector3(20f, 0.015f, 0.1f), H("#E8C9A0"), 0f);
        foreach (var pp in Plan.Posts) if (Mathf.Abs(pp.x) < 12f) m.Box(new Vector3(pp.x, CEIL / 2, pp.y), new Vector3(0.3f, CEIL, 0.3f), H("#2F3440"), 0.02f);
        m.Box(new Vector3(0, CEIL / 2, 18f), new Vector3(26f, CEIL, 0.2f), H("#2A2630"), 0.02f);
        for (int i = 0; i < 24; i++) m.Box(new Vector3(-11.9f + i * 1.03f, CEIL / 2, 17.87f), new Vector3(0.55f, CEIL - 0.4f, 0.04f), m.Jit(H("#5A4636"), 0.12f), 0f);
        glow.Box(new Vector3(0, 0.25f, 17.84f), new Vector3(25.6f, 0.03f, 0.03f), H("#FFB27A"), 0f);
        SceneKit.Seat(m, new[] { H("#3A3F52"), H("#FF7AC8"), H("#3A3F52") });
        SceneKit.Furniture(m, glow, new SceneKit.Style { wood = H("#3A3442"), wood2 = H("#23252E"), fabric = H("#6A6480"), fabric2 = H("#3A3550"), metal = H("#9AA3B2"), accent = H("#FF7AC8"), plant = H("#3C8A6A"), screen = H("#5FE6FF"), pot = H("#2F3440") });

        // ---- skyline ----
        var bld = new Mk(53); var win = new Mk(54);
        var faces = new[] { H("#3B3F63"), H("#4A4468"), H("#2F3555"), H("#5A4A6E") };
        for (int i = 0; i < 260; i++)
        {
            float az = bld.R(-125f, 125f) * Mathf.Deg2Rad;
            float d = Mathf.Lerp(140f, 1500f, Mathf.Pow(bld.R(), 0.8f));
            float w = bld.R(24f, 62f) * (0.6f + d / 900f), dep = bld.R(24f, 50f), top = Mathf.Min(GROUND + bld.R(90f, 230f) + d * 0.09f, 300f);
            if (d < 600f) top = Mathf.Min(top, 60f + bld.R(0f, 70f)); // near towers don't wall off the view
            var c = new Vector3(Mathf.Sin(az) * d, 0, -Mathf.Cos(az) * d);
            if (Mathf.Abs(az) < 0.14f && d < 900f) continue; // keep the centre open for the horizon
            var col = bld.Jit(faces[bld.rng.Next(faces.Length)], 0.12f);
            bld.Box(new Vector3(c.x, (GROUND + top) / 2, c.z), new Vector3(w, top - GROUND, dep), Quaternion.Euler(0, -az * Mathf.Rad2Deg, 0), col, 0.03f);
            if (bld.R() < 0.4f) bld.Box(new Vector3(c.x, top + 4f, c.z), new Vector3(w * 0.5f, 8f, dep * 0.5f), Quaternion.Euler(0, -az * Mathf.Rad2Deg, 0), col * 0.9f, 0.03f);
            // windows on the face toward the deck
            var toward = new Vector3(-c.x, 0, 6.5f - c.z).normalized;
            var right = Vector3.Cross(Vector3.up, toward);
            float pitch = Mathf.Clamp(d / 55f, 4f, 14f);
            float yMin = Mathf.Max(GROUND + 6f, 1.2f - 0.2f * (d + 6.5f));
            int cols = Mathf.Max(2, Mathf.FloorToInt(w / pitch));
            var faceC = c + toward * (dep / 2 + 0.05f);
            for (float wy = yMin; wy < top - 3f; wy += pitch)
                for (int k = 0; k < cols; k++)
                {
                    if (win.R() < 0.18f) continue;
                    var p = faceC + right * ((k - (cols - 1) / 2f) * pitch) + Vector3.up * wy;
                    float sz = pitch * 0.55f;
                    var wc = win.R() < 0.75f ? H("#FFD58A") : (win.R() < 0.5f ? H("#9FD8FF") : H("#FFB0E0"));
                    wc.a = win.R();
                    var r = right * sz * 0.5f; var u = Vector3.up * sz * 0.6f;
                    win.Tri(p - r - u, p + r - u, p + r + u, wc, wc, wc);
                    win.Tri(p - r - u, p + r + u, p - r + u, wc, wc, wc);
                }
        }
        // beacon tower
        var tw = new Vector3(Mathf.Sin(0.4f) * 700f, 0, -Mathf.Cos(0.4f) * 700f);
        bld.Box(new Vector3(tw.x, 110f, tw.z), new Vector3(46f, 520f, 46f), H("#3F4468"), 0.02f);
        bld.Beam(new Vector3(tw.x, 370f, tw.z), new Vector3(tw.x, 450f, tw.z), 3f, 3f, H("#9AA3B2"), 0f);
        x.Mesh(m, "Shell", x.Lit());
        x.Mesh(ceil, "Ceiling", x.Unlit(null, 0.9f));
        x.Mesh(bld, "Towers", x.Lit());
        x.windows = x.Unlit(null, 1.25f, 0, 0.22f);
        x.Mesh(win, "Windows", x.windows);
        x.glow = x.Unlit(null, 1.4f);
        x.Mesh(glow, "Glow", x.glow);
        var beacon = new Mk(55);
        beacon.Box(new Vector3(tw.x, 452f, tw.z), new Vector3(5f, 5f, 5f), new Color(1f, 0.2f, 0.2f, 0.37f), 0f);
        x.Mesh(beacon, "Beacon", x.Unlit(null, 2f, 0, 0.9f));

        // elevated expressway with traffic streaks
        var road = new Mk(56); var cars = new Mk(57); var cars2 = new Mk(58);
        road.Box(new Vector3(0, -12f, -95f), new Vector3(1400f, 1.6f, 14f), H("#2C2D3A"), 0f);
        for (float px = -650f; px <= 650f; px += 40f) road.Box(new Vector3(px, -50f, -95f), new Vector3(2.5f, 80f, 2.5f), H("#3A3D4F"), 0f);
        for (float px = -700f; px < 700f; px += 3f)
        {
            var w1 = new Color(1f, 0.93f, 0.8f, cars.R()); var w2 = new Color(1f, 0.25f, 0.2f, cars2.R());
            cars.Quad2(new Vector3(px, -11f, -92.5f), new Vector3(px + 3f, -11f, -92.5f), new Vector3(px + 3f, -11f, -91.5f), new Vector3(px, -11f, -91.5f), w1, w1);
            cars.Quad2(new Vector3(px, -11f, -94f), new Vector3(px + 3f, -11f, -94f), new Vector3(px + 3f, -11f, -93f), new Vector3(px, -11f, -93f), w1, w1);
            cars2.Quad2(new Vector3(px, -11f, -96.5f), new Vector3(px + 3f, -11f, -96.5f), new Vector3(px + 3f, -11f, -95.5f), new Vector3(px, -11f, -95.5f), w2, w2);
            cars2.Quad2(new Vector3(px, -11f, -98f), new Vector3(px + 3f, -11f, -98f), new Vector3(px + 3f, -11f, -97f), new Vector3(px, -11f, -97f), w2, w2);
        }
        x.Mesh(road, "Expressway", x.Lit());
        x.Mesh(cars, "TrafficA", x.Unlit(null, 2.2f, 0, 0f, 3.2f));
        x.Mesh(cars2, "TrafficB", x.Unlit(null, 2.0f, 0, 0f, -2.4f));

        // plane crossing, high clouds
        var pl = new Mk(59);
        pl.Box(Vector3.zero, new Vector3(6f, 0.8f, 0.8f), H("#E8EAF2"), 0f);
        pl.Box(new Vector3(0, 0, 0), new Vector3(0.8f, 0.1f, 7f), H("#E8EAF2"), 0f);
        pl.Box(new Vector3(0, 0.12f, 0), new Vector3(0.3f, 0.3f, 0.3f), new Color(1f, 0.15f, 0.15f, 0.2f), 0f);
        var pg = x.Mesh(pl, "Plane", x.Unlit(null, 1.2f, 0, 1.2f)).transform;
        pg.localPosition = new Vector3(-400f, 90f, -600f);
        x.life.Drift(pg, new Vector3(18f, 0, 0), 450f);
        SceneKit.Clouds(x, H("#B58AA8"), H("#4B3A66"), 6, 80f, 150f, 40f, 70f, 500f, 900f);
    }
}
