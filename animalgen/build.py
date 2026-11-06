# VIRTUAL LOFT — animal generator (Blender, headless). Pure code: mesh,
# vertex colors, skeleton, skin weights, 4 animation clips, FBX export.
#
#   blender -b --factory-startup -P animalgen/build.py -- fox
#
# Conventions (Blender space): Z up, the animal FACES -Y, its left is +X.
# Exported FBX (-Z forward, Y up) + Unity "Bake Axis Conversion" => faces +Z.
# Clips map to AgentSkin's Animator int "State": Idle 0, Working 1, Done 2, Failed 3, Party 4.

import math
import os
import sys

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, ".."))
FPS = 24

sys.path.insert(0, HERE)
from recipes import RECIPES  # noqa: E402


def col(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)) + (1.0,)


# ---------------------------------------------------------------- mesh parts
PARTS = []  # (object, allowed bone names)


def link(name, me):
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def bake_modifiers(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    old = ob.data
    ob.modifiers.clear()
    ob.data = me
    bpy.data.meshes.remove(old)


def paint(ob, color_fn):
    """Per-face flat color (low-poly look); color_fn(face_center) -> rgba."""
    me = ob.data
    attr = me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    for poly in me.polygons:
        c = color_fn(ob.matrix_world @ poly.center)
        for li in poly.loop_indices:
            attr.data[li].color_srgb = c
    me.color_attributes.active_color = attr


def part(name, ob, bones, color_fn):
    paint(ob, color_fn)
    PARTS.append((ob, bones))
    return ob


def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
    return (p - (a + ab * t)).length


def weight_part(ob, bones, segs):
    """Inverse-distance weights to this part's allowed bones (top 3, normalized).
    Done per part BEFORE joining: join reorders vertices, group names survive."""
    groups = {b: ob.vertex_groups.new(name=b) for b in bones}
    for v in ob.data.vertices:
        ws = sorted(((1.0 / (seg_dist(v.co, *segs[b]) + 0.01) ** 4, b) for b in bones), reverse=True)[:3]
        ws = [(w, b) for w, b in ws if w / ws[0][0] > 0.02]
        total = sum(w for w, _ in ws)
        for w, b in ws:
            groups[b].add([v.index], w / total, "REPLACE")


def blob(name, center, scale, segs=14, rings=9, rot=(0, 0, 0)):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=1.0)
    m = Matrix.Translation(center) @ Euler([math.radians(a) for a in rot]).to_matrix().to_4x4() @ Matrix.Diagonal((*scale, 1))
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    return link(name, me)


def cone(name, base, height, radius, rot, segs=6):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=radius, radius2=0.0, depth=height)
    bmesh.ops.translate(bm, vec=(0, 0, height / 2), verts=bm.verts)  # base at origin
    m = Matrix.Translation(base) @ Euler([math.radians(a) for a in rot]).to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    return link(name, me)


def tube(name, pts, radii, subdiv=1):
    """Organic limb/tail: skin modifier around a polyline + subdivision."""
    me = bpy.data.meshes.new(name)
    me.from_pydata([Vector(p) for p in pts], [(i, i + 1) for i in range(len(pts) - 1)], [])
    ob = link(name, me)
    ob.modifiers.new("skin", "SKIN")
    for i, r in enumerate(radii):
        r = r if isinstance(r, tuple) else (r, r)
        me.skin_vertices[0].data[i].radius = r
    me.skin_vertices[0].data[0].use_root = True
    if subdiv:
        ob.modifiers.new("sub", "SUBSURF").levels = subdiv
    bake_modifiers(ob)
    return ob


def ear_base(r, sx):
    H, hs = r["head_center"], r["head_size"]
    return (sx * r["ear_x"], H[1] + 0.02, H[2] + hs[2] * 0.72)


def build_mesh(r):
    c_ = lambda k, fallback=None: col(r[k] or r[fallback]) if (r.get(k) or fallback) else None  # noqa: E731
    fur, light, dark, inner, eye, accent = (col(r[k]) for k in ("fur", "light", "dark", "inner", "eye", "accent"))
    ear_c, limb, feet = c_("ear_color", "fur"), c_("limb", "fur"), c_("feet", "dark")
    nose_c = c_("nose", "dark")
    white = col("#FFFFFF")
    H, hs = r["head_center"], r["head_size"]
    ex, ey, ez = r["eye_pos"]

    # Body: pear shape, optional light belly.
    neck = (0, H[1] * 0.5, H[2] - hs[2] * 0.6)
    part("body", tube("body", [(0, 0, 0.22), (0, 0, 0.38), (0, 0, 0.54), neck], r["body_r"] + [(0.085, 0.08)]), ["hips", "chest"],
         lambda c: light if (r["belly"] and c.y < -0.07 and c.z < 0.5) else fur)

    # Head with a face pattern.
    def face(c):
        f = r["face"]
        if f == "cheeks" and c.z < H[2] - 0.04 and c.y < H[1] - 0.06:
            return light
        if f == "mask" and c.y < H[1] - hs[1] * 0.45 and c.z < H[2] + hs[2] * 0.55:
            return light
        if f == "patches" and min((c - Vector((sx * ex, ey, ez))).length for sx in (1, -1)) < 0.085:
            return dark
        return fur
    part("head", blob("head", H, hs), ["head"], face)

    front = H[1] - hs[1] * 0.95
    m = r["muzzle"]
    if m in ("snout", "small"):
        w = 0.085 if m == "snout" else 0.075
        sn = (0, front, H[2] - 0.05)
        part("snout", blob("snout", sn, (w, r["snout"], 0.06), segs=10, rings=7), ["head"], lambda c: light)
        part("nose", blob("nose", (0, sn[1] - r["snout"] * 0.95, sn[2] + 0.015), (0.03, 0.024, 0.024), segs=8, rings=6),
             ["head"], lambda c: nose_c)
    elif m == "beak":
        part("beak", cone("beak", (0, front + 0.04, H[2] - 0.04), r["snout"] + 0.04, 0.045, (90, 0, 0), segs=6),
             ["head"], lambda c: accent)

    # Eyes (own bones: blink) + highlights, optional ring / frog bulge.
    k = r["eye_scale"]
    for side, sx in (("L", 1), ("R", -1)):
        if r["bulge"]:
            part(f"bulge.{side}", blob(f"bulge.{side}", (sx * ex, ey + 0.035, ez - 0.01), (0.075, 0.065, 0.07), segs=10, rings=7),
                 ["head"], lambda c: fur)
        if r["eye_ring"]:
            rs = r.get("ring_scale", (1.0, 1.0))
            part(f"ring.{side}", blob(f"ring.{side}", (sx * ex, ey + 0.012, ez - 0.01 * (rs[1] - 1)),
                                       (0.068 * k * rs[0], 0.02, 0.07 * k * rs[1]), segs=12, rings=6, rot=(0, sx * r.get("ring_tilt", 0), 0)),
                 ["head"], lambda c: col(r["eye_ring"]))
        part(f"eye.{side}", blob(f"eye.{side}", (sx * ex, ey, ez), (0.042 * k, 0.03, 0.05 * k), segs=10, rings=7),
             [f"eye.{side}"], lambda c: eye)
        part(f"spark.{side}", blob(f"spark.{side}", (sx * ex - sx * 0.012 * k, ey - 0.026, ez + 0.018 * k),
                                    (0.013 * k, 0.008, 0.013 * k), segs=6, rings=4), [f"eye.{side}"], lambda c: white)

    # Ears.
    for side, sx in (("L", 1), ("R", -1)):
        base = ear_base(r, sx)
        kind = r["ears"]
        bones = [f"ear.{side}", "head"]
        if kind in ("pointy", "tufts"):
            tip_z = base[2] + r["ear_len"] * 0.85
            part(f"ear.{side}", cone(f"ear.{side}", base, r["ear_len"], r["ear_w"], (8, -sx * (18 if kind == "pointy" else 28), 0)), bones,
                 lambda c, tip_z=tip_z, bx=base[0]: (dark if kind == "pointy" else ear_c) if c.z > tip_z - 0.05 else
                 (inner if (kind == "pointy" and c.y < H[1] + 0.01 and abs(c.x - bx) < r["ear_w"] * 0.4 and c.z < tip_z - 0.08) else ear_c))
        elif kind == "round":
            part(f"ear.{side}", blob(f"ear.{side}", (base[0], base[1], base[2] + 0.02), (r["ear_w"], 0.04, r["ear_w"]), segs=10, rings=7),
                 bones, lambda c, bx=base[0]: inner if (c.y < H[1] - 0.01 and abs(c.x - bx) < r["ear_w"] * 0.5 and r["ear_color"] is None) else ear_c)
        elif kind == "long":
            cz = base[2] + r["ear_len"] * 0.5
            part(f"ear.{side}", blob(f"ear.{side}", (base[0] + sx * 0.02, base[1], cz), (r["ear_w"], 0.035, r["ear_len"] * 0.5),
                                      segs=10, rings=8, rot=(0, -sx * 8, 0)), bones,
                 lambda c, bx=base[0] + sx * 0.02: inner if (c.y < H[1] + 0.0 and abs(c.x - bx) < r["ear_w"] * 0.55) else ear_c)

    # Arms or wings (2 bones each).
    for side, sx in (("L", 1), ("R", -1)):
        a = [(sx * x, y, z) for x, y, z in r["arm"]]
        radii = [0.05, 0.045, 0.05] if r["arms"] == "arm" else [(0.08, 0.03), (0.07, 0.03), (0.04, 0.025)]
        part(f"arm.{side}", tube(f"arm.{side}", a, radii), [f"upperarm.{side}", f"forearm.{side}", "chest"],
             lambda c, pz=a[-1][2]: (dark if r["arms"] == "arm" else limb) if c.z < pz + 0.04 else limb)

    # Legs (thigh + foot), colored socks/feet.
    for side, sx in (("L", 1), ("R", -1)):
        lg = [(sx * x, y, z) for x, y, z in r["leg"]]
        part(f"leg.{side}", tube(f"leg.{side}", lg, [0.07, 0.06, 0.055, 0.05]),
             [f"thigh.{side}", f"foot.{side}", "hips"], lambda c: feet if c.z < 0.12 else limb)

    # Tail.
    t = r["tail_pts"]
    if r["tail"] in ("bushy", "thin"):
        top = max(p[2] for p in t)
        tip = light if r["tail"] == "bushy" else dark
        part("tail", tube("tail", t, r["tail_r"]), ["tail.1", "tail.2", "tail.3", "hips"],
             lambda c: tip if c.z > top - (0.1 if r["tail"] == "bushy" else 0.05) else fur)
    elif r["tail"] == "puff":
        rad = r["tail_r"][0]
        back = r["body_r"][0][1]
        part("tail", blob("tail", (0, back + rad * 0.4, 0.28), (rad, rad, rad), segs=10, rings=7), ["tail.1", "hips"],
             lambda c: light if r["fur"] != r["light"] and r["face"] != "mask" else fur)


# ---------------------------------------------------------------- skeleton
def build_armature(r):
    H = r["head_center"]
    hs = r["head_size"]
    ex, ey, ez = r["eye_pos"]
    a = r["arm"]
    lg = r["leg"]
    t = r["tail_pts"]
    bones = {
        "root": ((0, 0, 0), (0, 0, 0.12), None),
        "hips": ((0, 0, 0.22), (0, 0, 0.42), "root"),
        "chest": ((0, 0, 0.42), (0, 0, H[2] - hs[2] * 0.8), "hips"),
        "head": ((0, 0, H[2] - hs[2] * 0.8), (0, 0, H[2] + hs[2] * 1.3), "chest"),
        "tail.1": (t[0], t[1], "hips"),
        "tail.2": (t[1], t[2], "tail.1"),
        "tail.3": (t[2], t[-1], "tail.2"),
    }
    for side, sx in (("L", 1), ("R", -1)):
        m = lambda p: (sx * p[0], p[1], p[2])  # noqa: E731
        eb = ear_base(r, sx)
        bones[f"ear.{side}"] = (eb, (eb[0] + sx * 0.06, eb[1] + 0.02, eb[2] + r["ear_len"]), "head")
        bones[f"eye.{side}"] = ((sx * ex, ey + 0.02, ez), (sx * ex, ey - 0.06, ez), "head")
        bones[f"upperarm.{side}"] = (m(a[0]), m(a[1]), "chest")
        bones[f"forearm.{side}"] = (m(a[1]), m(a[2]), f"upperarm.{side}")
        bones[f"thigh.{side}"] = (m(lg[0]), m(lg[2]), "hips")
        bones[f"foot.{side}"] = (m(lg[2]), m(lg[3]), f"thigh.{side}")

    arm_data = bpy.data.armatures.new("Armature")
    arm = link("Armature", arm_data)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for name, (h, tl, _) in bones.items():
        eb = arm_data.edit_bones.new(name)
        eb.head, eb.tail = Vector(h), Vector(tl)
        eb.roll = 0.0
    for name, (_, _, parent) in bones.items():
        if parent:
            arm_data.edit_bones[name].parent = arm_data.edit_bones[parent]
            arm_data.edit_bones[name].use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm


def skin(arm, name):
    """Weight every part, then join into one skinned mesh."""
    segs = {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in arm.data.bones}
    for ob, bones in PARTS:
        weight_part(ob, bones, segs)
    target = PARTS[0][0]
    bpy.ops.object.select_all(action="DESELECT")
    for ob, _ in PARTS:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = target
    bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = ob.data.name = name
    # Subdivision shrinks the feet off the floor: snap the lowest point to z=0.
    lift = min(v.co.z for v in ob.data.vertices)
    for v in ob.data.vertices:
        v.co.z -= lift
    ob.parent = arm
    mod = ob.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    return ob


# ---------------------------------------------------------------- animation
def pose(arm, values):
    """values: {bone: {"rot": (x,y,z) deg in ARMATURE axes, "loc": (x,y,z), "squash": s}}.
    Unlisted bones return to rest. Armature axes: +X rot = lean/nod forward,
    +Y rot = tilt to the animal's right, +Z rot = turn to its left."""
    for pb in arm.pose.bones:
        rest = arm.data.bones[pb.name].matrix_local.to_quaternion()
        v = values.get(pb.name, {})
        pb.rotation_mode = "QUATERNION"
        q = Euler([math.radians(a) for a in v.get("rot", (0, 0, 0))]).to_quaternion()
        pb.rotation_quaternion = rest.inverted() @ q @ rest
        pb.location = rest.inverted() @ Vector(v.get("loc", (0, 0, 0)))
        s = v.get("squash", 1.0)  # scale along armature Z (blink)
        up = rest.inverted() @ Vector((0, 0, 1))
        ax = max(range(3), key=lambda i: abs(up[i]))
        sc = [1.0, 1.0, 1.0]
        sc[ax] = s
        pb.scale = sc


def bump(p, at, width):
    d = min(abs(p - at), 1 - abs(p - at))  # periodic distance
    return math.exp(-(d / width) ** 2)


def sstep(x):
    x = max(0.0, min(1.0, x))
    return x * x * (3 - 2 * x)


TAU = 2 * math.pi
S = math.sin


def clip_idle(p):
    return {
        "chest": {"rot": (1.5 * S(2 * TAU * p), 0, 0)},
        "head": {"rot": (1.0 * S(2 * TAU * p), 3 * S(TAU * p), 0)},
        "tail.1": {"rot": (0, 0, 12 * S(TAU * p))},
        "tail.2": {"rot": (0, 0, 10 * S(TAU * p - 0.6))},
        "tail.3": {"rot": (0, 0, 8 * S(TAU * p - 1.2))},
        "ear.L": {"rot": (0, 10 * bump(p, 0.6, 0.04), 0)},
        "upperarm.L": {"rot": (2 * S(TAU * p), 0, 0)},
        "upperarm.R": {"rot": (2 * S(TAU * p), 0, 0)},
        "eye.L": {"squash": 1 - 0.9 * bump(p, 0.85, 0.06)},
        "eye.R": {"squash": 1 - 0.9 * bump(p, 0.85, 0.06)},
    }


def clip_working(p):
    tap = lambda ph: 12 * S(4 * TAU * p + ph)  # noqa: E731  4 taps/s per paw
    return {
        "chest": {"rot": (6, 0, 0)},
        "head": {"rot": (8 + 3 * S(2 * TAU * p), 0, 0)},
        "upperarm.L": {"rot": (-55, 0, -10)},
        "upperarm.R": {"rot": (-55, 0, 10)},
        "forearm.L": {"rot": (-30 + tap(0), 0, 0)},
        "forearm.R": {"rot": (-30 + tap(math.pi), 0, 0)},
        "tail.1": {"rot": (0, 0, 15 * S(2 * TAU * p))},
        "tail.2": {"rot": (0, 0, 12 * S(2 * TAU * p - 0.8))},
        "ear.L": {"rot": (-6, 0, 0)},
        "ear.R": {"rot": (-6, 0, 0)},
        "eye.L": {"squash": 1 - 0.9 * bump(p, 0.5, 0.12)},
        "eye.R": {"squash": 1 - 0.9 * bump(p, 0.5, 0.12)},
    }


def clip_done(p):
    hop = S(math.pi * p)  # 0 -> 1 -> 0, one hop per loop
    return {
        "root": {"loc": (0, 0, 0.16 * hop)},
        "chest": {"rot": (-6 * hop, 0, 0)},
        "head": {"rot": (-10 * hop, 0, 0)},
        "upperarm.L": {"rot": (-150 * (0.6 + 0.4 * hop), 25, 0)},
        "upperarm.R": {"rot": (-150 * (0.6 + 0.4 * hop), -25, 0)},
        "thigh.L": {"rot": (20 * hop, 0, 0)},
        "thigh.R": {"rot": (20 * hop, 0, 0)},
        "foot.L": {"rot": (-20 * hop, 0, 0)},
        "foot.R": {"rot": (-20 * hop, 0, 0)},
        "tail.1": {"rot": (-15 * hop, 0, 25 * S(2 * TAU * p))},
        "ear.L": {"rot": (0, -8 * hop, 0)},
        "ear.R": {"rot": (0, 8 * hop, 0)},
    }


def clip_failed(p):
    k = sstep(p / 0.6)  # slump over 60%, then hold
    return {
        "chest": {"rot": (22 * k, 0, 0)},
        "head": {"rot": (25 * k, 0, 0)},
        "ear.L": {"rot": (-50 * k, 0, -15 * k)},
        "ear.R": {"rot": (-50 * k, 0, 15 * k)},
        "upperarm.L": {"rot": (8 * k, 0, 0)},
        "upperarm.R": {"rot": (8 * k, 0, 0)},
        "tail.1": {"rot": (-40 * k, 0, 0)},
        "tail.2": {"rot": (-20 * k, 0, 0)},
        "eye.L": {"squash": 1 - 0.5 * k},
        "eye.R": {"squash": 1 - 0.5 * k},
    }


def clip_party(p):
    """Party dance, 1 s loop = 2 beats at 120 BPM: bounce on every beat, hips sway,
    'raise the roof' arms waving in turn, head bob, wild tail, happy ^^ eyes."""
    beat = (1 - math.cos(2 * TAU * p)) / 2  # 0..1 twice per loop, smooth at the bottom
    sway = S(TAU * p)
    return {
        "root": {"loc": (0, 0, 0.07 * beat)},
        "hips": {"rot": (0, 8 * sway, 0)},
        "chest": {"rot": (-4, -5 * sway, 9 * sway)},
        "head": {"rot": (5 * S(2 * TAU * p), -8 * sway, -6 * sway)},
        "upperarm.L": {"rot": (-140 + 14 * sway, 18 + 6 * S(2 * TAU * p), 0)},
        "upperarm.R": {"rot": (-140 - 14 * sway, -18 - 6 * S(2 * TAU * p), 0)},
        "forearm.L": {"rot": (-20 + 10 * S(2 * TAU * p), 0, 0)},
        "forearm.R": {"rot": (-20 + 10 * S(2 * TAU * p + math.pi), 0, 0)},
        "thigh.L": {"rot": (14 * beat, 0, 0)},
        "thigh.R": {"rot": (14 * beat, 0, 0)},
        "foot.L": {"rot": (-14 * beat, 0, 0)},
        "foot.R": {"rot": (-14 * beat, 0, 0)},
        "tail.1": {"rot": (-8 * beat, 0, 16 * S(2 * TAU * p))},
        "tail.2": {"rot": (0, 0, 11 * S(2 * TAU * p - 0.7))},
        "tail.3": {"rot": (0, 0, 7 * S(2 * TAU * p - 1.4))},
        "ear.L": {"rot": (0, 10 * S(2 * TAU * p), 0)},
        "ear.R": {"rot": (0, -10 * S(2 * TAU * p), 0)},
        "eye.L": {"squash": 0.45},
        "eye.R": {"squash": 0.45},
    }


# name, frames, loops, pose function (State index = list order: Idle 0 .. Party 4)
CLIPS = [("Idle", 48, True, clip_idle), ("Working", 24, True, clip_working),
         ("Done", 24, True, clip_done), ("Failed", 36, False, clip_failed),
         ("Party", 24, True, clip_party)]


def animate(arm):
    arm.animation_data_create()
    for name, frames, _, fn in CLIPS:
        act = bpy.data.actions.new(name)
        act.use_fake_user = True
        arm.animation_data.action = act
        for f in range(frames + 1):
            pose(arm, fn(f / frames))
            for pb in arm.pose.bones:
                for path in ("rotation_quaternion", "location", "scale"):
                    pb.keyframe_insert(path, frame=f, group=pb.name)
        track = arm.animation_data.nla_tracks.new()
        track.name = name
        track.strips.new(name, 0, act)
        track.mute = True
        arm.animation_data.action = None
    pose(arm, {})


def export_fbx(arm, mesh, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    mesh.select_set(True)
    for t in arm.animation_data.nla_tracks:
        t.mute = False
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
        add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
        mesh_smooth_type="FACE", colors_type="SRGB", use_armature_deform_only=True,
        bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=True,
        bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
        bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
    )
    for t in arm.animation_data.nla_tracks:
        t.mute = True


def main():
    animal = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "fox"
    r = RECIPES[animal]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.fps = FPS
    build_mesh(r)
    arm = build_armature(r)
    mesh = skin(arm, animal)
    animate(arm)
    out = os.path.join(HERE, "out", animal)
    os.makedirs(out, exist_ok=True)
    fbx = os.path.join(REPO, "quest_loft", "Assets", "Models", "Animals", f"{animal}.fbx")
    export_fbx(arm, mesh, fbx)
    pose(arm, {})  # the exporter leaves the skeleton on its last baked frame: back to rest
    bpy.context.scene.frame_set(0)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, f"{animal}.blend"))
    tris = sum(len(p.vertices) - 2 for p in mesh.data.polygons)
    print(f"[build] {animal}: {len(mesh.data.vertices)} verts, {tris} tris, {len(arm.data.bones)} bones -> {fbx}")


main()
