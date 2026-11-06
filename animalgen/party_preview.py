# PARTY MODE preview: the whole moment, rendered outside Unity (EEVEE).
#   blender -b --factory-startup -P animalgen/party_preview.py [-- fox owl cat ...]
# Output: out/party/party.mp4, party_sheet.png (every 12th frame), party_hero.png
#
# Same layout as Unity party mode (LoftParty.cs). Blender space: CEO looks
# toward +Y, animals face -Y; Unity (x, y, z) == Blender (-x, -z, y).

import math
import os
import random
import shutil
import subprocess
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out", "party")
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ANIMALS = args or ["fox", "owl", "cat", "bear", "rabbit", "penguin", "frog", "panda"]
FPS, END = 24, 168
T_DIM, T_BALL, T_GO = 18, 22, 30  # lights dim, ball drops, agents start hopping over
CENTER = Vector((0, -3.4, 0))  # Unity (0, 0, 3.4)
random.seed(7)


def party_spots(n):
    """Arc behind the dance floor, facing the CEO. Mirrors AgentManager.PartySpots."""
    spots, i, row = [], 0, 0
    while i < n:
        cap = (5, 7, 8)[min(row, 2)]
        k = min(cap, n - i)
        r = 1.9 + row * 0.95
        off = 0.5 if row % 2 else 0.0  # stagger rows so nobody hides behind a friend
        for j in range(k):
            a = math.radians(-62 + 124 * (j + 0.5 + off * (k < cap)) / (k + off * (k < cap)))
            spots.append(CENTER + Vector((-math.sin(a) * r, math.cos(a) * r, 0)))
        i += k
        row += 1
    return spots


def mat(name, color=(1, 1, 1), emit=0.0, metal=0.0, rough=0.5, attr=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Metallic"].default_value = metal
    bsdf.inputs["Roughness"].default_value = rough
    if attr:  # vertex colors -> base color (the animals)
        a = nt.nodes.new("ShaderNodeAttribute")
        a.attribute_name = attr
        nt.links.new(a.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Emission Color"].default_value = (*color, 1)  # glow color even if it starts at 0
    bsdf.inputs["Emission Strength"].default_value = emit
    return m


def box(name, loc, size, material):
    bpy.ops.mesh.primitive_cube_add(location=loc)
    o = bpy.context.object
    o.name = name
    o.scale = (size[0] / 2, size[1] / 2, size[2] / 2)
    o.data.materials.append(material)
    return o


def key_emit(m, frame, strength):
    s = m.node_tree.nodes["Principled BSDF"].inputs["Emission Strength"]
    s.default_value = strength
    s.keyframe_insert("default_value", frame=frame)


bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
sc.render.fps = FPS
sc.frame_start, sc.frame_end = 0, END - 1
try:
    sc.render.engine = "BLENDER_EEVEE"
except TypeError:
    sc.render.engine = "BLENDER_EEVEE_NEXT"
sc.eevee.taa_render_samples = 16
sc.render.resolution_x, sc.render.resolution_y = 960, 540
sc.view_settings.view_transform = "AgX"
sc.view_settings.look = "AgX - Punchy"

# ---- world: calm loft -> dark club
world = bpy.data.worlds.new("World")
sc.world = world
world.use_nodes = True
bg = world.node_tree.nodes["Background"]
for f, c, s in ((0, (0.55, 0.5, 0.45), 0.6), (T_DIM, (0.55, 0.5, 0.45), 0.6), (T_DIM + 8, (0.05, 0.01, 0.09), 0.25)):
    bg.inputs["Color"].default_value = (*c, 1)
    bg.inputs["Strength"].default_value = s
    bg.inputs["Color"].keyframe_insert("default_value", frame=f)
    bg.inputs["Strength"].keyframe_insert("default_value", frame=f)

# ---- room: floor, wall with the 4 boards (same layout as the loft)
box("Floor", (0, -4, -0.05), (14, 12, 0.1), mat("floor", (0.18, 0.14, 0.12), rough=0.35))
box("Wall", (0, 0.5, 2), (12, 0.2, 4), mat("wall", (0.45, 0.36, 0.28)))
board = mat("board", (0.1, 0.11, 0.15))
for x, w, h in ((4.2, 2.4, 1.4), (1.4, 2.8, 1.6), (-1.6, 2.4, 1.2), (-4.2, 2.0, 1.4)):
    box("Board", (x, 0.38, 1.6), (w, 0.06, h), board)
banner_mat = mat("banner", (1.0, 0.2, 0.6), emit=0.0)
banner_mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.08, 0.06, 0.06, 1)  # invisible until it lights up
for word, x in (("LAUNCH", -2.4), ("PARTY!", 2.4)):  # either side of the ball (camera looks +Y: +X is right)
    bpy.ops.object.text_add(location=(x, 0.33, 3.15), rotation=(math.radians(90), 0, 0))
    banner = bpy.context.object
    banner.data.body = word
    banner.data.align_x = "CENTER"
    banner.data.size = 0.55
    banner.data.extrude = 0.02
    banner.data.materials.append(banner_mat)
key_emit(banner_mat, T_BALL, 0.0)
key_emit(banner_mat, T_BALL + 6, 6.0)

# ---- lights: warm key light that dims, colored spots that sweep
key = bpy.data.objects.new("Key", bpy.data.lights.new("Key", "AREA"))
key.data.size = 6
key.location = (0, -5, 5)
key.rotation_euler = (math.radians(35), 0, 0)
sc.collection.objects.link(key)
for f, e in ((0, 900), (T_DIM, 900), (T_DIM + 8, 25)):
    key.data.energy = e
    key.data.keyframe_insert("energy", frame=f)

fill = bpy.data.objects.new("Fill", bpy.data.lights.new("Fill", "AREA"))
fill.data.size = 4
fill.data.color = (1.0, 0.8, 0.95)
fill.location = (0, -7.5, 2.2)
fill.rotation_euler = (math.radians(80), 0, 0)
sc.collection.objects.link(fill)
for f, e in ((0, 0), (T_DIM, 0), (T_DIM + 10, 110)):
    fill.data.energy = e
    fill.data.keyframe_insert("energy", frame=f)

SPOT_COLORS = [(1, 0.15, 0.55), (0.1, 0.85, 1), (1, 0.85, 0.1), (0.4, 1, 0.3)]
for i, c in enumerate(SPOT_COLORS):
    sp = bpy.data.objects.new(f"Spot{i}", bpy.data.lights.new(f"Spot{i}", "SPOT"))
    sp.data.color = c
    sp.data.spot_size = math.radians(38)
    sp.data.spot_blend = 0.4
    sp.location = (-4.5 + 3 * i, -4.6, 4.2)
    sc.collection.objects.link(sp)
    for f, e in ((0, 0), (T_DIM + 2, 0), (T_DIM + 8, 1500)):
        sp.data.energy = e
        sp.data.keyframe_insert("energy", frame=f)
    for f in range(0, END + 12, 12):  # sweep across the dancers
        ph = f / FPS * 1.7 + i * 1.6
        tgt = CENTER + Vector((math.sin(ph) * 2.2, 1.2 + math.cos(ph * 0.7) * 1.0, 0))
        sp.rotation_euler = (tgt - sp.location).to_track_quat("-Z", "Y").to_euler()
        sp.keyframe_insert("rotation_euler", frame=f)

# ---- disco ball: drops in with a bounce, spins, sparkles
bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=14, radius=0.38)
ball = bpy.context.object
ball.name = "DiscoBall"
bm = mat("disco", (0.9, 0.9, 1.0), metal=1.0, rough=0.08)
nt = bm.node_tree
vor = nt.nodes.new("ShaderNodeTexVoronoi")
vor.inputs["Scale"].default_value = 14
ramp = nt.nodes.new("ShaderNodeValToRGB")
ramp.color_ramp.elements[0].position = 0.55
ramp.color_ramp.elements[0].color = (0, 0, 0, 1)
ramp.color_ramp.elements[1].color = (1, 1, 1, 1)
nt.links.new(vor.outputs["Distance"], ramp.inputs["Fac"])
nt.links.new(ramp.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Emission Color"])
nt.nodes["Principled BSDF"].inputs["Emission Strength"].default_value = 4
ball.data.materials.append(bm)
rod = box("Rod", (0, CENTER.y, 0), (0.03, 0.03, 2.0), mat("rod", (0.2, 0.2, 0.2)))
for f, z in ((0, 6.2), (T_BALL, 6.2), (T_BALL + 8, 2.45), (T_BALL + 11, 2.7), (T_BALL + 14, 2.55)):
    ball.location = (0, CENTER.y, z)
    rod.location = (0, CENTER.y, z + 1.38)
    ball.keyframe_insert("location", frame=f)
    rod.keyframe_insert("location", frame=f)
for f, rz in ((T_BALL, 0), (END, math.radians(70) * (END - T_BALL) / FPS)):
    ball.rotation_euler = (0, 0, rz)
    ball.keyframe_insert("rotation_euler", frame=f)
for fc in ball.animation_data.action.fcurves if hasattr(ball.animation_data.action, "fcurves") else []:
    for kp in fc.keyframe_points:
        kp.interpolation = "LINEAR"

# ---- dance floor tiles: pulse on the beat (2 beats/s)
palette = [(1, 0.05, 0.4), (0.0, 0.7, 1), (1, 0.7, 0.0), (0.2, 1, 0.15), (0.5, 0.15, 1)]
for ix in range(-3, 4):
    for iy in range(-2, 3):
        m = mat(f"tile{ix}{iy}", random.choice(palette), emit=0.0, rough=0.3)
        m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.03, 0.03, 0.035, 1)
        box("Tile", (ix * 0.62, CENTER.y + 0.9 + iy * 0.62, 0.005), (0.58, 0.58, 0.01), m)
        key_emit(m, T_DIM, 0)
        for f in range(T_DIM + 6, END, 12):
            key_emit(m, f, random.choice((0.0, 0.6, 1.6)))

# ---- confetti
conf = [mat(f"conf{i}", c, emit=1.5) for i, c in enumerate(palette)]
for i in range(140):
    start = random.randint(T_BALL + 6, T_BALL + 70)
    x, y = random.uniform(-3.2, 3.2), CENTER.y + random.uniform(-1.2, 2.6)
    bpy.ops.mesh.primitive_plane_add(size=0.06, location=(x, y, 6))
    c = bpy.context.object
    c.data.materials.append(random.choice(conf))
    c.scale = (1, 0.6, 1)
    fall = random.randint(50, 80)
    for f, z, rot in ((start, 4.8, 0), (start + fall, 0.01, random.uniform(6, 14))):
        c.location = (x + random.uniform(-0.4, 0.4) * (z < 1), y, z)
        c.rotation_euler = (rot, rot * 0.7, rot * 0.3)
        c.keyframe_insert("location", frame=f)
        c.keyframe_insert("rotation_euler", frame=f)
    c.hide_render = True
    c.keyframe_insert("hide_render", frame=start - 1)
    c.hide_render = False
    c.keyframe_insert("hide_render", frame=start)

# ---- the team: Working at desks -> hop over (Done) -> dance (Party)
animal_mat = mat("animal", attr="Col", rough=0.55)
spots = party_spots(len(ANIMALS))
for i, name in enumerate(ANIMALS):
    path = os.path.join(HERE, "out", name, f"{name}.blend")
    with bpy.data.libraries.load(path) as (src, dst):
        dst.objects = ["Armature", name]
    arm, body = dst.objects
    for o in (arm, body):
        sc.collection.objects.link(o)
    body.data.materials.clear()
    body.data.materials.append(animal_mat)
    acts = {s.action.name.split(".")[0]: s.action for t in arm.animation_data.nla_tracks for s in t.strips}
    for t in list(arm.animation_data.nla_tracks):
        arm.animation_data.nla_tracks.remove(t)
    arm.animation_data.action = None
    desk = Vector((-(i - (len(ANIMALS) - 1) / 2) * 1.15, -1.6, 0))
    go = T_GO + i * 3
    arrive = go + 22
    track = arm.animation_data.nla_tracks.new()
    for act, a, b in ((acts["Working"], 0, go), (acts["Done"], go, arrive), (acts["Party"], arrive, END + 24)):
        st = track.strips.new(act.name, a, act)
        st.repeat = (b - a) / (act.frame_range[1] - act.frame_range[0])
    for f, loc in ((go, desk), (arrive, spots[i])):
        arm.location = loc
        arm.keyframe_insert("location", frame=f)

# ---- camera: the CEO's view, slow push-in
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
cam.data.lens = 26
sc.collection.objects.link(cam)
sc.camera = cam
look = Vector((0, -2.4, 1.25))
for f, pos in ((0, Vector((0, -9.6, 2.0))), (END, Vector((0, -8.0, 1.7)))):
    cam.location = pos
    cam.rotation_euler = (look - pos).to_track_quat("-Z", "Y").to_euler()
    cam.keyframe_insert("location", frame=f)
    cam.keyframe_insert("rotation_euler", frame=f)

# ---- render
frames = os.path.join(OUT, "_frames")
shutil.rmtree(frames, ignore_errors=True)
os.makedirs(frames)
sc.render.image_settings.file_format = "PNG"
sc.render.filepath = os.path.join(frames, "f")
bpy.ops.render.render(animation=True)
ff = ["ffmpeg", "-loglevel", "error", "-y"]
subprocess.run(ff + ["-framerate", str(FPS), "-i", os.path.join(frames, "f%04d.png"), "-pix_fmt", "yuv420p",
                     "-vf", "scale=960:-2", os.path.join(OUT, "party.mp4")], check=True)
subprocess.run(ff + ["-i", os.path.join(frames, "f%04d.png"), "-vf", "select='not(mod(n\\,12))',scale=480:-1,tile=4x4",
                     "-frames:v", "1", os.path.join(OUT, "party_sheet.png")], check=True)
shutil.copy(os.path.join(frames, f"f{END - 30:04d}.png"), os.path.join(OUT, "party_hero.png"))
shutil.rmtree(frames, ignore_errors=True)
print(f"[party] wrote {OUT}/party.mp4, party_sheet.png, party_hero.png")
