# Renders previews of a built animal (headless Workbench, vertex colors):
#   out/<animal>/turntable.png            front / 3-4 / side / back of the rest pose
#   out/<animal>/<Clip>_sheet.png         contact sheet of the clip (every 2nd frame)
#   out/<animal>/<Clip>.mp4               the clip as video, looped 3x
#
#   blender -b animalgen/out/fox/fox.blend -P animalgen/preview.py -- fox [Clip ...]

import math
import os
import shutil
import subprocess
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["fox"]
animal, only = args[0], set(args[1:])
OUT = os.path.join(HERE, "out", animal)
SIZE = 360

sc = bpy.context.scene
sc.render.engine = "BLENDER_WORKBENCH"
sc.render.resolution_x = sc.render.resolution_y = SIZE
sc.render.film_transparent = False
sh = sc.display.shading
sh.light = "STUDIO"
sh.color_type = "VERTEX"
sh.show_object_outline = True
sh.show_cavity = True
sc.world = sc.world or bpy.data.worlds.new("World")
sc.world.color = (0.82, 0.85, 0.9)
sc.render.image_settings.file_format = "PNG"

arm = bpy.data.objects["Armature"]
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
sc.collection.objects.link(cam)
sc.camera = cam
cam.data.lens = 50
TARGET = Vector((0, 0, 0.55))

# Floor so hops/feet read clearly.
bpy.ops.mesh.primitive_plane_add(size=4)
floor = bpy.context.object
floor.color = (0.6, 0.62, 0.66, 1)


def aim(angle_deg, dist=3.1, height=1.0):
    a = math.radians(angle_deg)
    cam.location = TARGET + Vector((math.sin(a) * dist, -math.cos(a) * dist, height - TARGET.z))
    cam.rotation_euler = (TARGET - cam.location).to_track_quat("-Z", "Y").to_euler()


def render(path):
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


def tile(frames_glob, cols, out_png):
    subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-pattern_type", "glob", "-i", frames_glob,
                    "-vf", f"tile={cols}x{math.ceil(N / cols)}", "-frames:v", "1", out_png], check=True)


def use_clip(name):
    for t in arm.animation_data.nla_tracks:
        t.mute = True
    arm.animation_data.action = bpy.data.actions[name] if name else None


tmp = os.path.join(OUT, "_frames")
if not only or "turntable" in only:
    use_clip(None)
    sc.frame_set(0)
    for t in arm.animation_data.nla_tracks:
        t.mute = True
    shutil.rmtree(tmp, ignore_errors=True)
    for i, ang in enumerate((0, 35, 90, 180)):
        aim(ang)
        render(os.path.join(tmp, f"{i}.png"))
    N = 4
    tile(os.path.join(tmp, "*.png"), 4, os.path.join(OUT, "turntable.png"))

aim(15)
for act in bpy.data.actions:
    if only and act.name not in only:
        continue
    use_clip(act.name)
    start, end = (int(x) for x in act.frame_range)
    shutil.rmtree(tmp, ignore_errors=True)
    frames = list(range(start, end + 1))
    for f in frames:
        sc.frame_set(f)
        render(os.path.join(tmp, f"{f:03d}.png"))
    # contact sheet: every 2nd frame, 6 per row
    sheet = os.path.join(OUT, "_sheet")
    shutil.rmtree(sheet, ignore_errors=True)
    os.makedirs(sheet)
    picked = frames[::2]
    for f in picked:
        shutil.copy(os.path.join(tmp, f"{f:03d}.png"), os.path.join(sheet, f"{f:03d}.png"))
    N = len(picked)
    tile(os.path.join(sheet, "*.png"), 6, os.path.join(OUT, f"{act.name}_sheet.png"))
    subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-stream_loop", "2", "-framerate", "24", "-pattern_type", "glob",
                    "-i", os.path.join(tmp, "*.png"), "-pix_fmt", "yuv420p", os.path.join(OUT, f"{act.name}.mp4")], check=True)
    print(f"[preview] {act.name}: {len(frames)} frames")
shutil.rmtree(tmp, ignore_errors=True)
shutil.rmtree(os.path.join(OUT, "_sheet"), ignore_errors=True)
print(f"[preview] wrote {OUT}")
