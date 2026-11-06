// VIRTUAL LOFT — tiny procedural mesh kit for the world. Flat-shaded, vertex
// colored (alpha = rigidity: 1 rigid, 0 sways in the wind shader). Everything
// the scenes show (planks, posts, palms, ridges, skylines) is built from this.
using System;
using System.Collections.Generic;
using UnityEngine;

public class Mk
{
    readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
    readonly List<Color32> c = new List<Color32>();
    readonly List<int> t = new List<int>();
    public readonly System.Random rng;
    public Mk(int seed = 1) { rng = new System.Random(seed); }

    public int Tris => t.Count / 3;
    public int Verts => v.Count;
    public float R() => (float)rng.NextDouble();
    public float R(float a, float b) => a + (b - a) * R();
    public static Color Hex(string h) => ColorUtility.TryParseHtmlString(h, out var col) ? col : Color.magenta;
    public static Color Rig(Color col, float rigidity) { col.a = rigidity; return col; }
    public Color Jit(Color col, float amt)
    {
        float k = 1f + R(-amt, amt);
        return new Color(Mathf.Clamp01(col.r * k), Mathf.Clamp01(col.g * k), Mathf.Clamp01(col.b * k), col.a);
    }

    // ---- primitives of the primitives ----
    public void Tri(Vector3 a, Vector3 b, Vector3 d, Color ca, Color cb, Color cd)
    {
        var nn = Vector3.Cross(b - a, d - a);
        if (nn.sqrMagnitude < 1e-12f) return;
        nn.Normalize();
        int i = v.Count;
        v.Add(a); v.Add(b); v.Add(d);
        n.Add(nn); n.Add(nn); n.Add(nn);
        c.Add(ca); c.Add(cb); c.Add(cd);
        t.Add(i); t.Add(i + 1); t.Add(i + 2);
    }
    public void Tri(Vector3 a, Vector3 b, Vector3 d, Color col) => Tri(a, b, d, col, col, col);

    // Tri facing away from `inside` (so winding never has to be hand-checked).
    public void TriOut(Vector3 a, Vector3 b, Vector3 d, Color ca, Color cb, Color cd, Vector3 inside)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, d - a), (a + b + d) / 3f - inside) < 0f) Tri(a, d, b, ca, cd, cb);
        else Tri(a, b, d, ca, cb, cd);
    }
    public void QuadOut(Vector3 a, Vector3 b, Vector3 d, Vector3 e, Color c0, Color c1, Vector3 inside)
    {   // a-b one edge (color c0), e-d the opposite (color c1); corners in ring order a,b,d,e
        TriOut(a, b, d, c0, c0, c1, inside);
        TriOut(a, d, e, c0, c1, c1, inside);
    }
    public void QuadOut(Vector3 a, Vector3 b, Vector3 d, Vector3 e, Color col, Vector3 inside) => QuadOut(a, b, d, e, col, col, inside);
    // Both sides visible (thatch, leaves, flags).
    public void Quad2(Vector3 a, Vector3 b, Vector3 d, Vector3 e, Color c0, Color c1)
    {
        Tri(a, b, d, c0, c0, c1); Tri(a, d, e, c0, c1, c1);
        Tri(a, d, b, c0, c1, c0); Tri(a, e, d, c0, c1, c1);
    }
    public void Tri2(Vector3 a, Vector3 b, Vector3 d, Color ca, Color cb, Color cd) { Tri(a, b, d, ca, cb, cd); Tri(a, d, b, ca, cd, cb); }
    // Quad with an explicit facing (a,b,d,e ring order, normal toward `facing`).
    public void QuadFacing(Vector3 a, Vector3 b, Vector3 d, Vector3 e, Color c0, Color c1, Vector3 facing)
    {
        QuadOut(a, b, d, e, c0, c1, (a + b + d + e) / 4f - facing.normalized);
    }

    // ---- solids ----
    public void Box(Vector3 center, Vector3 size, Quaternion rot, Color col, float jitter = 0.04f)
    {
        var h = size * 0.5f;
        var p = new Vector3[8];
        for (int i = 0; i < 8; i++)
            p[i] = center + rot * new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
        int[][] f = { new[] {0,1,3,2}, new[] {4,5,7,6}, new[] {0,1,5,4}, new[] {2,3,7,6}, new[] {0,2,6,4}, new[] {1,3,7,5} };
        foreach (var q in f) QuadOut(p[q[0]], p[q[1]], p[q[2]], p[q[3]], Jit(col, jitter), center);
    }
    public void Box(Vector3 center, Vector3 size, Color col, float jitter = 0.04f) => Box(center, size, Quaternion.identity, col, jitter);
    // Square-ish beam from a to b.
    public void Beam(Vector3 a, Vector3 b, float w, float h, Color col, float jitter = 0.04f, float roll = 0f)
    {
        var d = b - a;
        if (d.sqrMagnitude < 1e-8f) return;
        var up = Mathf.Abs(Vector3.Dot(d.normalized, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
        Box((a + b) * 0.5f, new Vector3(w, h, d.magnitude), Quaternion.LookRotation(d, up) * Quaternion.Euler(0, 0, roll), col, jitter);
    }

    // Prism / cone / frustum between two points. Rigidity ra at a, rb at b (wind).
    public void Prism(Vector3 a, Vector3 b, float ra, float rb, int seg, Color col, bool capA = false, bool capB = true,
        float jitter = 0.03f, float rigA = 1f, float rigB = 1f)
    {
        var d = b - a;
        if (d.sqrMagnitude < 1e-8f) return;
        var dir = d.normalized;
        var u = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.95f ? Vector3.right : Vector3.up).normalized;
        var w = Vector3.Cross(dir, u);
        var mid = (a + b) * 0.5f;
        for (int i = 0; i < seg; i++)
        {
            float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
            Vector3 o0 = u * Mathf.Cos(a0) + w * Mathf.Sin(a0), o1 = u * Mathf.Cos(a1) + w * Mathf.Sin(a1);
            var cc = Jit(col, jitter);
            QuadOut(a + o0 * ra, a + o1 * ra, b + o1 * rb, b + o0 * rb, Rig(cc, rigA), Rig(cc, rigB), mid);
            if (capA) TriOut(a, a + o0 * ra, a + o1 * ra, Rig(cc, rigA), Rig(cc, rigA), Rig(cc, rigA), mid);
            if (capB) TriOut(b, b + o0 * rb, b + o1 * rb, Rig(cc, rigB), Rig(cc, rigB), Rig(cc, rigB), mid);
        }
    }

    // Lumpy ellipsoid (rocks, bushes, clouds, planets). top/bottom blend by height.
    public void Blob(Vector3 center, Vector3 radii, Color bottom, Color top, int lat = 4, int lon = 7, float lump = 0.12f,
        Quaternion? rot = null, bool flatBase = false, float rigidity = 1f)
    {
        var q = rot ?? Quaternion.identity;
        var grid = new Vector3[lat + 1, lon];
        for (int i = 0; i <= lat; i++)
        {
            float ph = Mathf.PI * i / lat;
            for (int j = 0; j < lon; j++)
            {
                float th = Mathf.PI * 2f * j / lon;
                float k = 1f + (i == 0 || i == lat ? 0f : R(-lump, lump));
                var dir = new Vector3(Mathf.Sin(ph) * Mathf.Cos(th), Mathf.Cos(ph), Mathf.Sin(ph) * Mathf.Sin(th));
                if (flatBase && dir.y < -0.2f) dir.y = -0.2f;
                grid[i, j] = center + q * Vector3.Scale(dir * k, radii);
            }
        }
        for (int i = 0; i < lat; i++)
            for (int j = 0; j < lon; j++)
            {
                int j2 = (j + 1) % lon;
                float y0 = 1f - (float)i / lat, y1 = 1f - (float)(i + 1) / lat;
                var c0 = Rig(Color.Lerp(bottom, top, y0 * 0.5f + 0.5f), rigidity);
                var c1 = Rig(Color.Lerp(bottom, top, y1 * 0.5f + 0.5f), rigidity);
                var jit = 1f + R(-0.04f, 0.04f);
                c0 *= jit; c1 *= jit; c0.a = c1.a = rigidity;
                QuadOut(grid[i, j], grid[i, j2], grid[i + 1, j2], grid[i + 1, j], c0, c1, center);
            }
    }

    // Tube along a path (curved trunks, rails). rad/col/rig take t in 0..1 along the path.
    public void Tube(IList<Vector3> pts, Func<float, float> rad, int seg, Func<float, Color> col, Func<float, float> rig = null, bool cap = true)
    {
        int m = pts.Count;
        var rings = new Vector3[m][];
        var centers = new Vector3[m];
        for (int i = 0; i < m; i++)
        {
            float tt = m == 1 ? 0 : (float)i / (m - 1);
            var tan = (pts[Mathf.Min(i + 1, m - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
            var u = Vector3.Cross(tan, Mathf.Abs(tan.y) > 0.95f ? Vector3.right : Vector3.up).normalized;
            var w = Vector3.Cross(tan, u);
            rings[i] = new Vector3[seg];
            for (int s = 0; s < seg; s++)
            {
                float a = s * Mathf.PI * 2f / seg;
                rings[i][s] = pts[i] + (u * Mathf.Cos(a) + w * Mathf.Sin(a)) * rad(tt);
            }
            centers[i] = pts[i];
        }
        for (int i = 0; i < m - 1; i++)
        {
            float t0 = (float)i / (m - 1), t1 = (float)(i + 1) / (m - 1);
            var c0 = col(t0); var c1 = col(t1);
            c0.a = rig != null ? rig(t0) : 1f; c1.a = rig != null ? rig(t1) : 1f;
            for (int s = 0; s < seg; s++)
            {
                int s2 = (s + 1) % seg;
                var jit = 1f + R(-0.03f, 0.03f);
                QuadOut(rings[i][s], rings[i][s2], rings[i + 1][s2], rings[i + 1][s], c0 * jit, c1 * jit, (centers[i] + centers[i + 1]) * 0.5f);
            }
        }
        if (cap)
            for (int s = 0; s < seg; s++)
            {
                var ct = col(1f); ct.a = rig != null ? rig(1f) : 1f;
                TriOut(centers[m - 1], rings[m - 1][s], rings[m - 1][(s + 1) % seg], ct, ct, ct, centers[m - 2]);
            }
    }

    // Heightfield (sand, ridges, hills). Flat shaded; x0..x1 by z0..z1.
    public void Grid(float x0, float x1, float z0, float z1, int nx, int nz, Func<float, float, float> h, Func<float, float, float, Color> col, float baseY = 0f)
    {
        var p = new Vector3[nx + 1, nz + 1];
        for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= nz; j++)
            {
                float x = Mathf.Lerp(x0, x1, (float)i / nx), z = Mathf.Lerp(z0, z1, (float)j / nz);
                p[i, j] = new Vector3(x, h(x, z), z);
            }
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                var a = p[i, j]; var b = p[i + 1, j]; var d = p[i + 1, j + 1]; var e = p[i, j + 1];
                var up = a + Vector3.up * 100f;
                var c1 = col(a.x, a.y, a.z); var c2 = col(d.x, d.y, d.z);
                TriOut(a, e, d, c1, c1, c2, a - Vector3.up * 100f);
                TriOut(a, d, b, c1, c2, c2, a - Vector3.up * 100f);
            }
    }

    // Layered ridge silhouette: a wall of triangles along X with a noisy top edge,
    // facing +Z (toward the seat). Cheap far mountains / skylines.
    public void Ridge(float x0, float x1, float z, float yBase, Func<float, float> top, int steps, Color baseCol, Color topCol)
    {
        for (int i = 0; i < steps; i++)
        {
            float xa = Mathf.Lerp(x0, x1, (float)i / steps), xb = Mathf.Lerp(x0, x1, (float)(i + 1) / steps);
            var a = new Vector3(xa, yBase, z); var b = new Vector3(xb, yBase, z);
            var ta = new Vector3(xa, top(xa), z); var tb = new Vector3(xb, top(xb), z);
            Quad2(a, b, tb, ta, baseCol, topCol);
        }
    }

    // Sphere colored by a function of the unit direction (planets, moons).
    public void Sphere(Vector3 c, float r, int lat, int lon, Func<Vector3, Color> col)
    {
        var g = new Vector3[lat + 1, lon + 1];
        for (int i = 0; i <= lat; i++)
            for (int j = 0; j <= lon; j++)
            {
                float ph = Mathf.PI * i / lat, th = Mathf.PI * 2f * j / lon;
                g[i, j] = new Vector3(Mathf.Sin(ph) * Mathf.Cos(th), Mathf.Cos(ph), Mathf.Sin(ph) * Mathf.Sin(th));
            }
        for (int i = 0; i < lat; i++)
            for (int j = 0; j < lon; j++)
            {
                var a = g[i, j]; var b = g[i, j + 1]; var d = g[i + 1, j + 1]; var e = g[i + 1, j];
                var cm = col((a + b + d + e).normalized);
                QuadOut(c + a * r, c + b * r, c + d * r, c + e * r, cm, c);
            }
    }

    // Flat ring (planet rings), visible from both sides; colors fade inner->outer.
    public void Annulus(Vector3 c, Quaternion rot, float r0, float r1, int seg, Color inner, Color outer)
    {
        for (int i = 0; i < seg; i++)
        {
            float a0 = Mathf.PI * 2f * i / seg, a1 = Mathf.PI * 2f * (i + 1) / seg;
            Vector3 d0 = rot * new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = rot * new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
            Quad2(c + d0 * r0, c + d1 * r0, c + d1 * r1, c + d0 * r1, inner, outer);
        }
    }

    // ---- leaves ----
    // Palm-like frond: a drooping spine with paired leaflets. Rigidity grows base->tip sway.
    public void Frond(Vector3 basePt, Vector3 dirOut, float length, float droop, float width, Color c0, Color c1, int segs = 10)
    {
        dirOut.y = 0f; dirOut = dirOut.sqrMagnitude < 1e-6f ? Vector3.forward : dirOut.normalized;
        var side = Vector3.Cross(Vector3.up, dirOut);
        var spine = new Vector3[segs + 1];
        for (int i = 0; i <= segs; i++)
        {
            float u = (float)i / segs;
            spine[i] = basePt + dirOut * (length * u) + Vector3.up * (length * 0.5f * Mathf.Sin(u * 2.1f) - droop * u * u * length);
        }
        float shade = R(0.88f, 1.08f);
        for (int i = 0; i < segs; i++)
        {
            float u0 = (float)i / segs, u1 = (float)(i + 1) / segs;
            var ca = Rig(Color.Lerp(c0, c1, u0) * shade, 1f - u0 * 0.95f);
            var cb = Rig(Color.Lerp(c0, c1, u1) * shade, 1f - u1 * 0.95f);
            ca.a = 1f - u0 * 0.95f; cb.a = 1f - u1 * 0.95f;
            float wd = width * Mathf.Sin((u0 + 0.1f) * Mathf.PI * 0.88f);
            float fwd = (spine[i + 1] - spine[i]).magnitude * 0.7f;
            var dn = (spine[i + 1] - spine[i]).normalized;
            var tipL = spine[i] + side * wd + dn * fwd - Vector3.up * (wd * 0.5f);
            var tipR = spine[i] - side * wd + dn * fwd - Vector3.up * (wd * 0.5f);
            var ct = cb; ct.a = Mathf.Max(0f, cb.a - 0.2f);
            Tri2(spine[i], spine[i + 1], tipL, ca, cb, ct);
            Tri2(spine[i], spine[i + 1], tipR, ca, cb, ct);
        }
    }

    // ---- output ----
    public Mesh Build(string name)
    {
        var m = new Mesh { name = name, hideFlags = HideFlags.DontSave };
        if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(v);
        m.SetNormals(n);
        m.SetColors(c);
        m.SetTriangles(t, 0);
        m.RecalculateBounds();
        return m;
    }
}
