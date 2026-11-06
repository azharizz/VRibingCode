# Animal checks (headless Blender). Exit code 0 = all pass.
#   blender -b animalgen/out/fox/fox.blend -P animalgen/test.py -- fox
#
# (clips: Idle, Working, Done, Failed, Party)
# 1. mesh     : budget, ground contact, size, vertex colors, faces -Y, left = +X
# 2. skin     : every vertex weighted, sums to 1, <= 4 influences, weights only
#               to NEARBY bones (catches mis-assigned parts)
# 3. clips    : 4 clips, right lengths; per frame on the DEFORMED mesh: above
#               floor, feet planted (not Done), no pops between frames, joint
#               limits, loops close seamlessly, blink actually closes the eye,
#               every clip moves what it should
# 4. export   : FBX re-imported into an empty scene: same bones, 4 takes with
#               the same lengths, deformed shape matches the source per frame
# 5. unity    : FBX GlobalSettings are Y-up / Z-front so Unity's
#               "Bake Axis Conversion" lands the animal upright facing +Z

import math
import os
import sys

import bpy
from mathutils import Vector
from mathutils.kdtree import KDTree

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, ".."))
animal = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "fox"
FBX = os.path.join(REPO, "quest_loft", "Assets", "Models", "Animals", f"{animal}.fbx")
sys.path.insert(0, HERE)

EXPECT = {"Idle": (48, True), "Working": (24, True), "Done": (24, True), "Failed": (36, False), "Party": (24, True)}
HOPS = {"Done", "Party"}  # feet leave the floor on purpose
# Limb pairs that must mirror each other per clip (Party waves its arms in turn).
MIRROR = {"Party": ()}  # hips sway: legs are deliberately uneven
results = []


def check(ok, msg):
    results.append((bool(ok), msg))
    print(f"[test] {'PASS' if ok else 'FAIL'}: {msg}")


def find(kind):
    return next(o for o in bpy.context.scene.objects if o.type == kind)


def deformed(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = ev.to_mesh()
    pts = [ob.matrix_world @ v.co for v in me.vertices]
    ev.to_mesh_clear()
    return pts


def use_clip(arm, name):
    for t in arm.animation_data.nla_tracks:
        t.mute = True
    arm.animation_data.action = bpy.data.actions[name] if name else None


def group_verts(ob, prefix, min_w=0.5):
    idx = {g.index for g in ob.vertex_groups if g.name.startswith(prefix)}
    return [v.index for v in ob.data.vertices if any(g.group in idx and g.weight >= min_w for g in v.groups)]


arm, mesh = find("ARMATURE"), find("MESH")
sc = bpy.context.scene
for pb in arm.pose.bones:  # measure "rest" from the true bind pose, never a saved pose
    pb.rotation_mode = "QUATERNION"
    pb.rotation_quaternion = (1, 0, 0, 0)
    pb.location = (0, 0, 0)
    pb.scale = (1, 1, 1)
use_clip(arm, None)
sc.frame_set(0)
rest = deformed(mesh)

# ---------------------------------------------------------------- 1. mesh
tris = sum(len(p.vertices) - 2 for p in mesh.data.polygons)
check(tris <= 5000, f"triangle budget {tris} <= 5000 (x20 animals on Quest)")
zs = [p.z for p in rest]
check(abs(min(zs)) < 0.02, f"feet on the floor (min z {min(zs):.3f})")
check(0.8 <= max(zs) <= 1.4, f"height {max(zs):.2f} m in 0.8-1.4")
check("Col" in mesh.data.color_attributes, "vertex colors present")
nose = min(rest, key=lambda p: p.y)
check(nose.y < -0.2 and abs(nose.x) < 0.05, f"faces -Y (front at y {nose.y:.2f})")
earL = group_verts(mesh, "ear.L")
eyeL0 = group_verts(mesh, "eye.L", 0.9)
check(sum(rest[i].x for i in eyeL0) / len(eyeL0) > 0.05, "left eye is on +X (not mirrored)")
if earL:
    check(sum(rest[i].x for i in earL) / len(earL) > 0.05, "left ear is on +X (not mirrored)")

# ---------------------------------------------------------------- 2. skin
bones = {b.name: (b.head_local, b.tail_local) for b in arm.data.bones}
gname = {g.index: g.name for g in mesh.vertex_groups}
check(set(gname.values()) <= set(bones), "every vertex group is a bone")
unweighted = [v.index for v in mesh.data.vertices if not v.groups]
check(not unweighted, f"every vertex weighted ({len(unweighted)} missing)")
bad_sum = [v.index for v in mesh.data.vertices if abs(sum(g.weight for g in v.groups) - 1) > 1e-3]
check(not bad_sum, f"weights sum to 1 ({len(bad_sum)} off)")
check(max(len(v.groups) for v in mesh.data.vertices) <= 4, "<= 4 bone influences per vertex (Unity default)")


def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
    return (p - (a + ab * t)).length


far = [(v.index, gname[g.group]) for v in mesh.data.vertices for g in v.groups
       if g.weight > 0.3 and seg_dist(v.co, *bones[gname[g.group]]) > 0.45]
check(not far, f"weights only on nearby bones ({len(far)} far, e.g. {far[:3]})")

# ---------------------------------------------------------------- 3. clips
check(set(EXPECT) <= {a.name for a in bpy.data.actions}, f"clips {'/'.join(EXPECT)} exist")
feet = group_verts(mesh, "foot")
eyeL = group_verts(mesh, "eye.L", 0.9)
eye_h = max(rest[i].z for i in eyeL) - min(rest[i].z for i in eyeL)
for name, (frames, loops) in EXPECT.items():
    act = bpy.data.actions.get(name)
    if not act:
        continue
    use_clip(arm, name)
    s, e = (int(x) for x in act.frame_range)
    check(e - s == frames, f"{name}: {frames} frames (got {e - s})")
    prev, prev2, first = None, None, None
    low, slip, speed, jerk, maxrot, moved, asym = 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0
    min_eye = eye_h
    for f in range(s, e + 1):
        sc.frame_set(f)
        pts = deformed(mesh)
        first = first or pts
        low = min(low, min(p.z for p in pts))
        if feet:
            slip = max(slip, max((pts[i] - rest[i]).length for i in feet))
        if prev:
            speed = max(speed, max((a - b).length for a, b in zip(pts, prev)))
        if prev2:  # 2nd difference: sudden change of velocity = visible pop
            jerk = max(jerk, max((a - 2 * b + c).length for a, b, c in zip(pts, prev, prev2)))
        prev2 = prev
        moved = max(moved, max((a - b).length for a, b in zip(pts, rest)))
        min_eye = min(min_eye, max(pts[i].z for i in eyeL) - min(pts[i].z for i in eyeL))
        for pb in arm.pose.bones:
            maxrot = max(maxrot, math.degrees(pb.rotation_quaternion.angle))
        for b in MIRROR.get(name, ("upperarm", "thigh", "foot")):  # limbs move as mirror pairs
            L, R = (arm.matrix_world @ arm.pose.bones[f"{b}.{x}"].tail for x in "LR")
            asym = max(asym, (L - Vector((-R.x, R.y, R.z))).length)
        prev = pts
    check(low > -0.02, f"{name}: never sinks through the floor (min z {low:.3f})")
    check(asym < 0.01, f"{name}: left/right limbs mirror each other (max diff {asym * 100:.1f} cm)")
    if name not in HOPS:
        check(slip < 0.01, f"{name}: feet stay planted (max slip {slip * 100:.1f} cm)")
    check(speed < 0.12, f"{name}: no teleporting parts (max per-frame move {speed * 100:.1f} cm)")
    check(jerk < 0.05, f"{name}: smooth motion, no pops (max velocity change {jerk * 100:.1f} cm/frame)")
    check(maxrot <= 170, f"{name}: joint limits (max bone rotation {maxrot:.0f} deg)")
    check(moved > 0.03, f"{name}: visibly animates (max move {moved * 100:.1f} cm)")
    if loops:
        seam = max((a - b).length for a, b in zip(first, prev))
        check(seam < 0.001, f"{name}: loops seamlessly (first vs last {seam * 1000:.2f} mm)")
    if name in ("Idle", "Working"):
        check(min_eye < 0.35 * eye_h, f"{name}: blinks (eye closes to {min_eye / eye_h:.0%})")
    if name == "Failed":
        sc.frame_set(e)
        hz = lambda pts: max(p.z for p in pts)  # noqa: E731
        check(hz(deformed(mesh)) < hz(rest) - 0.05, "Failed: ends slumped (top of head lowered)")
    if name == "Party":
        sc.frame_set(s + frames // 4)  # top of the first bounce
        check(min(p.z for p in deformed(mesh)) > 0.03, "Party: bounces on the beat")
        sc.frame_set(s + frames // 2)
        hands = [arm.matrix_world @ arm.pose.bones[f"forearm.{x}"].tail for x in "LR"]
        shoulder_z = max(arm.data.bones[f"upperarm.{x}"].head_local.z for x in "LR")
        check(all(h.z > shoulder_z + 0.1 for h in hands),
              f"Party: both paws raised well above the shoulders ({min(h.z for h in hands):.2f} > {shoulder_z + 0.1:.2f} m)")
    if name == "Done":
        sc.frame_set(s + frames // 2)
        check(min(p.z for p in deformed(mesh)) > 0.08, "Done: actually leaves the ground mid-hop")

# keep source shapes for the round-trip comparison
SAMPLES = {}
for name, (frames, _) in EXPECT.items():
    if name in bpy.data.actions:
        use_clip(arm, name)
        SAMPLES[name] = {}
        for f in (0, frames // 3, frames // 2, frames):
            sc.frame_set(f)
            SAMPLES[name][f] = deformed(mesh)
src_bones = set(bones)

# ---------------------------------------------------------------- 4. export
check(os.path.exists(FBX), f"FBX exported ({os.path.relpath(FBX, REPO)})")
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX, automatic_bone_orientation=False)
iarm, imesh = find("ARMATURE"), find("MESH")
check(set(b.name for b in iarm.data.bones) == src_bones, f"re-import: same {len(src_bones)} bones")
takes = {a.name.split("|")[-1]: a for a in bpy.data.actions}
check(set(EXPECT) <= set(takes), f"re-import: {len(EXPECT)} takes ({sorted(takes)})")
iarm.animation_data_create()
for name, frames_pts in SAMPLES.items():
    act = takes.get(name)
    if not act:
        continue
    s, e = (int(x) for x in act.frame_range)
    check(e - s == EXPECT[name][0], f"re-import {name}: {EXPECT[name][0]} frames (got {e - s})")
    iarm.animation_data.action = act
    worst = 0.0
    for f, src in frames_pts.items():
        bpy.context.scene.frame_set(s + f)
        got = deformed(imesh)
        tree = KDTree(len(got))
        for i, p in enumerate(got):
            tree.insert(p, i)
        tree.balance()
        worst = max(worst, max(tree.find(p)[2] for p in src))
    check(worst < 0.005, f"re-import {name}: deformed shape matches source (worst {worst * 1000:.1f} mm)")

# ---------------------------------------------------------------- 5. unity axes
from io_scene_fbx import parse_fbx  # noqa: E402

root, _ = parse_fbx.parse(FBX)
gs = {}
for el in root.elems:
    if el.id == b"GlobalSettings":
        for props in el.elems:
            for p in props.elems:
                if len(p.props) > 4 and isinstance(p.props[4], int):
                    gs[p.props[0].decode()] = p.props[4]
check(gs.get("UpAxis") == 1 and gs.get("UpAxisSign") == 1, f"FBX up axis = +Y ({gs.get('UpAxis')}, {gs.get('UpAxisSign')})")
check(gs.get("FrontAxis") == 2, f"FBX front axis = Z ({gs.get('FrontAxis')}, sign {gs.get('FrontAxisSign')})")

failed = [m for ok, m in results if not ok]
print(f"[test] SUMMARY {animal}: {len(results) - len(failed)} pass, {len(failed)} fail")
sys.exit(1 if failed else 0)
