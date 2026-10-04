"""Render the packaged preview images from the saved .blend.

    python Tools/blender_bridge_client.py --file Tools/blender_render_pad.py --no-state --timeout 600

Cameras are placed from the mesh's measured bounds, and every call passes keyword
arguments because the previous version mixed up positional distance and height and
quietly produced an oblique shot where a vertical one was intended.
"""

import json
import math
import os
from mathutils import Vector

import bpy
import numpy as np

WORKSPACE = r"C:\MyData\UnityProj\Almost Real Space"
OUT = os.path.join(WORKSPACE, "Blender", "LaunchPad")
BLEND = os.path.join(OUT, "LaunchPad_40x40.blend")

bpy.ops.wm.open_mainfile(filepath=BLEND)

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.image_settings.file_format = "PNG"
scene.render.film_transparent = False

camera = bpy.data.objects["Camera"]
pad = bpy.data.objects["LaunchPad_40x40"]
scene.camera = camera

verts = np.array([v.co[:] for v in pad.data.vertices], dtype=np.float64)
lo, hi = verts.min(axis=0), verts.max(axis=0)
centre = (lo + hi) / 2.0
span = float(max(hi[0] - lo[0], hi[1] - lo[1]))
top = float(hi[2])


def shoot(name, *, azimuth_deg, distance, height, target, ortho=None,
          lens=40.0, res=(1400, 900)):
    """Aim and render. All placement arguments are keyword-only on purpose."""
    scene.render.resolution_x, scene.render.resolution_y = res
    if ortho:
        camera.data.type = "ORTHO"
        camera.data.ortho_scale = ortho
    else:
        camera.data.type = "PERSP"
        camera.data.lens = lens

    angle = math.radians(azimuth_deg)
    location = Vector((math.cos(angle) * distance, math.sin(angle) * distance, height))
    target = Vector(target)

    # straight-down views make to_track_quat degenerate; a tiny offset fixes it
    toward = target - location
    if abs(toward.x) < 1e-6 and abs(toward.y) < 1e-6:
        location.y += 0.01 * max(span, 1.0)

    camera.location = location
    camera.rotation_euler = (target - location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(OUT, name)
    bpy.ops.render.render(write_still=True)
    return {
        "file": name + ".png",
        "ok": os.path.exists(scene.render.filepath + ".png"),
        "camera": [round(float(v), 1) for v in camera.location],
        "type": camera.data.type,
    }


target = (float(centre[0]), float(centre[1]), top)
shots = [
    # straight down: the shape of the painted square
    shoot("preview_top", azimuth_deg=90.0, distance=0.0, height=span * 2.25,
          target=(0.0, 0.0, 0.0), ortho=span * 1.2, res=(1200, 1200)),
    # three quarter: the slab and its edge
    shoot("preview_aerial", azimuth_deg=145.0, distance=span * 1.8,
          height=span * 0.55, target=target),
    # from the side, low: how it reads standing next to it
    shoot("preview_low", azimuth_deg=200.0, distance=span * 2.0,
          height=span * 0.28, target=(0.0, 0.0, top * 0.6)),
    # close on one corner: the paint line, its width and its 3 cm step
    shoot("preview_corner", azimuth_deg=35.0, distance=span * 0.72,
          height=span * 0.20, target=(float(lo[0]) * 0.85, float(lo[1]) * 0.85, top),
          lens=50.0),
]
print(json.dumps(shots, indent=2))
