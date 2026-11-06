// VIRTUAL LOFT — the shared floor plan, identical in every scene (scenes only restyle it).
// All coordinates are world meters, floor at y=0. The CEO faces -Z (the view / sea side);
// the CEO's RIGHT is -X (lounge side), the CEO's LEFT is +X (work area side).
//
//   z=0   view edge (open front, sea beyond)
//   z=4   front roof line      z=6.5 CEO seat      z=18 back wall
//   work area: +X, two columns of 5 standing desks      lounge: -X, sofas around a table
using System.Collections.Generic;
using UnityEngine;

public static class Plan
{
    public const float X0 = -13f, X1 = 13f, Z0 = 0f, Z1 = 18f;       // deck outline (about 3x the old 14.8 x 10.4 m)
    public static readonly Rect Walk = new Rect(-12.2f, 0.7f, 24.4f, 16.6f); // walkable area (x, z, w, h)
    public static readonly Vector3 Ceo = new Vector3(0f, 0f, 6.5f);
    public static readonly Vector3 Spawn = new Vector3(0f, 0f, 15.4f);        // where new agents arrive
    public static readonly Vector3 LoungeCenter = new Vector3(-8.5f, 0f, 10f);
    public static readonly Vector3 WorkCenter = new Vector3(8.8f, 0f, 10.5f);

    // Furniture footprints (axis aligned, xz): the nav grid and the scenes both use these.
    public struct Block { public string kind; public Vector2 c, half; public Block(string k, float x, float z, float hx, float hz) { kind = k; c = new Vector2(x, z); half = new Vector2(hx, hz); } }

    public static readonly Vector3[] DeskPos;    // desk centers (the desk top, 0.9 x 1.7 m)
    public static readonly Vector3[] DeskSpots;  // where an agent stands to work (facing +X, at the desk)
    public static readonly Vector3[] LoungeSpots;
    public static readonly Vector3[] ViewSpots;
    public static readonly Block[] Blocks;
    public static readonly Vector2[] Posts;      // roof posts (xz)
    public static readonly Dictionary<string, Vector3> Spots = new Dictionary<string, Vector3>();

    static Plan()
    {
        var desks = new List<Vector3>(); var stands = new List<Vector3>(); var blocks = new List<Block>();
        foreach (float dx in new[] { 7.3f, 10.6f })
            foreach (float dz in new[] { 5.0f, 7.9f, 10.8f, 13.7f, 16.0f })
            {
                desks.Add(new Vector3(dx, 0f, dz));
                stands.Add(new Vector3(dx - 1.0f, 0f, dz));
                blocks.Add(new Block("desk", dx, dz, 0.5f, 0.95f));
            }
        DeskPos = desks.ToArray(); DeskSpots = stands.ToArray();
        // lounge: two sofas facing a low table, an armchair, a plant
        blocks.Add(new Block("sofa", -8.5f, 7.2f, 1.6f, 0.55f));
        blocks.Add(new Block("sofa", -8.5f, 12.8f, 1.6f, 0.55f));
        blocks.Add(new Block("armchair", -11.6f, 10f, 0.55f, 0.6f));
        blocks.Add(new Block("table", -8.5f, 10f, 0.9f, 0.5f));
        blocks.Add(new Block("plant", -12f, 5.4f, 0.5f, 0.5f));
        blocks.Add(new Block("plant", -12f, 15.6f, 0.5f, 0.5f));
        blocks.Add(new Block("plant", 12f, 3.2f, 0.5f, 0.5f));
        blocks.Add(new Block("bar", -4.2f, 17.1f, 3.2f, 0.5f));
        blocks.Add(new Block("bar", 4.2f, 17.1f, 3.2f, 0.5f));
        blocks.Add(new Block("ceo", 0f, 6.9f, 0.7f, 0.7f));            // the CEO's lounger + the CEO
        Blocks = blocks.ToArray();
        LoungeSpots = new[]
        {
            new Vector3(-9.5f, 0f, 8.35f), new Vector3(-7.5f, 0f, 8.35f), new Vector3(-9.5f, 0f, 11.65f), new Vector3(-7.5f, 0f, 11.65f),
            new Vector3(-10.5f, 0f, 10f), new Vector3(-6.4f, 0f, 10f),
        };
        ViewSpots = new[] { new Vector3(-6f, 0f, 1.7f), new Vector3(-2.2f, 0f, 1.5f), new Vector3(2.2f, 0f, 1.5f), new Vector3(6f, 0f, 1.7f) };
        var posts = new List<Vector2>();
        foreach (float px in new[] { -12.5f, -5f, 5f, 12.5f }) foreach (float pz in new[] { 4f, 10.3f, 16.6f }) posts.Add(new Vector2(px, pz));
        Posts = posts.ToArray();

        Spots["ceo"] = Ceo;
        Spots["spawn"] = Spawn;
        Spots["lounge"] = LoungeCenter;
        Spots["work"] = WorkCenter;
        Spots["view"] = new Vector3(0f, 0f, 1.5f);
        for (int i = 0; i < DeskSpots.Length; i++) Spots["desk" + i] = DeskSpots[i];
        for (int i = 0; i < LoungeSpots.Length; i++) Spots["lounge" + i] = LoungeSpots[i];
        for (int i = 0; i < ViewSpots.Length; i++) Spots["view" + i] = ViewSpots[i];
    }

    public static bool InWalk(Vector3 p) => Walk.Contains(new Vector2(p.x, p.z));
    public static Vector3 ClampToWalk(Vector3 p) =>
        new Vector3(Mathf.Clamp(p.x, Walk.xMin, Walk.xMax), p.y, Mathf.Clamp(p.z, Walk.yMin, Walk.yMax));
}
