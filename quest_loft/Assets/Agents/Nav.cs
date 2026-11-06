// VIRTUAL LOFT — tiny grid pathfinder over the shared floor plan. Agents plan with this so
// they never walk through furniture, posts or the CEO's lounger. A* on 0.35 m cells,
// obstacles inflated by the agent radius, then line-of-sight smoothing.
using System.Collections.Generic;
using UnityEngine;

public static class Nav
{
    public const float CELL = 0.35f, RADIUS = 0.42f;
    static bool[,] blocked;
    static int nx, nz;
    static float ox, oz;

    static void Ensure()
    {
        if (blocked != null) return;
        var w = Plan.Walk;
        ox = w.xMin; oz = w.yMin;
        nx = Mathf.CeilToInt(w.width / CELL); nz = Mathf.CeilToInt(w.height / CELL);
        blocked = new bool[nx, nz];
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                var p = Center(i, j);
                bool b = false;
                foreach (var bl in Plan.Blocks)
                    if (Mathf.Abs(p.x - bl.c.x) < bl.half.x + RADIUS && Mathf.Abs(p.z - bl.c.y) < bl.half.y + RADIUS) { b = true; break; }
                if (!b) foreach (var po in Plan.Posts) if ((new Vector2(p.x, p.z) - po).sqrMagnitude < (0.3f + RADIUS) * (0.3f + RADIUS)) { b = true; break; }
                blocked[i, j] = b;
            }
    }

    static Vector3 Center(int i, int j) => new Vector3(ox + (i + 0.5f) * CELL, 0f, oz + (j + 0.5f) * CELL);
    static bool Cell(Vector3 p, out int i, out int j)
    {
        i = Mathf.FloorToInt((p.x - ox) / CELL); j = Mathf.FloorToInt((p.z - oz) / CELL);
        return i >= 0 && j >= 0 && i < nx && j < nz;
    }

    public static bool Free(Vector3 p)
    {
        Ensure();
        return Cell(p, out int i, out int j) && !blocked[i, j];
    }

    // Closest free point to p (ring search). Used for spawn points and CEO-relative wait spots.
    public static Vector3 Nearest(Vector3 p)
    {
        Ensure();
        p = Plan.ClampToWalk(p);
        if (Free(p)) return p;
        Cell(p, out int ci, out int cj);
        ci = Mathf.Clamp(ci, 0, nx - 1); cj = Mathf.Clamp(cj, 0, nz - 1);
        for (int r = 1; r < 40; r++)
        {
            float best = float.MaxValue; Vector3 bp = p; bool found = false;
            for (int i = ci - r; i <= ci + r; i++)
                for (int j = cj - r; j <= cj + r; j++)
                {
                    if (Mathf.Max(Mathf.Abs(i - ci), Mathf.Abs(j - cj)) != r || i < 0 || j < 0 || i >= nx || j >= nz || blocked[i, j]) continue;
                    var c = Center(i, j); float d = (c - p).sqrMagnitude;
                    if (d < best) { best = d; bp = c; found = true; }
                }
            if (found) return bp;
        }
        return p;
    }

    public static bool LineFree(Vector3 a, Vector3 b)
    {
        Ensure();
        float len = Vector3.Distance(a, b);
        int n = Mathf.Max(1, Mathf.CeilToInt(len / (CELL * 0.5f)));
        for (int k = 0; k <= n; k++) if (!Free(Vector3.Lerp(a, b, (float)k / n))) return false;
        return true;
    }

    // Waypoints from `from` to `to` (both snapped to free cells). Always returns at least [to].
    public static List<Vector3> Path(Vector3 from, Vector3 to, Vector2? avoid = null, float avoidR = 0f)
    {
        Ensure();
        from = Nearest(from); to = Nearest(to);
        var res = new List<Vector3>();
        if (LineFree(from, to) && (avoid == null || !SegHits(from, to, avoid.Value, avoidR))) { res.Add(to); return res; }
        Cell(from, out int si, out int sj); Cell(to, out int ti, out int tj);
        var g = new float[nx, nz]; var from_ = new int[nx, nz]; var closed = new bool[nx, nz];
        for (int i = 0; i < nx; i++) for (int j = 0; j < nz; j++) { g[i, j] = float.MaxValue; from_[i, j] = -1; }
        var open = new List<int>(); g[si, sj] = 0; open.Add(si * nz + sj);
        float H(int i, int j) { float dx = Mathf.Abs(i - ti), dz = Mathf.Abs(j - tj); return (dx + dz + (1.414f - 2f) * Mathf.Min(dx, dz)) * CELL; }
        bool Blocked(int i, int j)
        {
            if (i < 0 || j < 0 || i >= nx || j >= nz || blocked[i, j]) return true;
            if (avoid != null) { var c = Center(i, j); if ((new Vector2(c.x, c.z) - avoid.Value).sqrMagnitude < avoidR * avoidR) return true; }
            return false;
        }
        int guard = 0;
        while (open.Count > 0 && guard++ < 20000)
        {
            int bi = 0; float bf = float.MaxValue;
            for (int k = 0; k < open.Count; k++) { int ci = open[k] / nz, cj = open[k] % nz; float f = g[ci, cj] + H(ci, cj); if (f < bf) { bf = f; bi = k; } }
            int cur = open[bi]; open.RemoveAt(bi);
            int x = cur / nz, z = cur % nz;
            if (closed[x, z]) continue;
            closed[x, z] = true;
            if (x == ti && z == tj) break;
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int a = x + dx, b = z + dz;
                    if (Blocked(a, b) || closed[a, b]) continue;
                    if (dx != 0 && dz != 0 && (Blocked(x + dx, z) || Blocked(x, z + dz))) continue;
                    float ng = g[x, z] + ((dx != 0 && dz != 0) ? 1.414f : 1f) * CELL;
                    if (ng < g[a, b]) { g[a, b] = ng; from_[a, b] = cur; open.Add(a * nz + b); }
                }
        }
        if (from_[ti, tj] < 0 && !(si == ti && sj == tj)) { res.Add(to); return res; }
        var cells = new List<Vector3>();
        int c2 = ti * nz + tj;
        while (c2 >= 0) { cells.Add(Center(c2 / nz, c2 % nz)); c2 = from_[c2 / nz, c2 % nz]; }
        cells.Reverse();
        // string pulling: keep only the corners that matter
        var pts = new List<Vector3> { from };
        int anchor = 0;
        for (int k = 2; k < cells.Count; k++)
            if (!LineFree(pts[pts.Count - 1], cells[k]) || (avoid != null && SegHits(pts[pts.Count - 1], cells[k], avoid.Value, avoidR)))
            { pts.Add(cells[k - 1]); anchor = k - 1; }
        pts.Add(to);
        pts.RemoveAt(0);
        return pts;
    }

    static bool SegHits(Vector3 a, Vector3 b, Vector2 c, float r)
    {
        var p = new Vector2(a.x, a.z); var d = new Vector2(b.x - a.x, b.z - a.z);
        float t = d.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(c - p, d) / d.sqrMagnitude);
        return (p + d * t - c).sqrMagnitude < r * r;
    }
}
