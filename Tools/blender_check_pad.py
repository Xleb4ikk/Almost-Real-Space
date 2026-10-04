"""Final state check: viewport shading saved in the file, plus mesh facts.

Run this after any rebuild - a rebuild overwrites the .blend and would drop the
viewport setting that makes the paint visible in Solid shading.
"""

import json
import os

import bpy
import numpy as np

OUT = r"C:\MyData\UnityProj\Almost Real Space\Blender\LaunchPad"
BLEND = os.path.join(OUT, "LaunchPad_40x40.blend")

bpy.ops.wm.open_mainfile(filepath=BLEND)

pad = bpy.data.objects["LaunchPad_40x40"]
mesh = pad.data
verts = np.array([v.co[:] for v in mesh.vertices], dtype=np.float64)
faces = np.array([p.vertices[:] for p in mesh.polygons], dtype=np.int64)
mats = np.zeros(len(mesh.polygons), dtype=np.int32)
mesh.polygons.foreach_get("material_index", mats)
centres = verts[faces].mean(axis=1)
radius = np.hypot(centres[:, 0], centres[:, 1])

shading = []
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type == "VIEW_3D":
            shading.append(area.spaces.active.shading.color_type)

paint_slot = next((i for i, m in enumerate(mesh.materials)
                   if "Paint" in m.name), None)
paint = mats == paint_slot

report = {
    "blend_bytes": os.path.getsize(BLEND),
    "viewport_color_modes": sorted(set(shading)),
    "paint_visible_in_solid": all(s == "VERTEX" for s in shading),
    "verts": int(len(verts)),
    "faces": int(len(faces)),
    "bounds_min": [round(float(v), 2) for v in verts.min(axis=0)],
    "bounds_max": [round(float(v), 2) for v in verts.max(axis=0)],
    "top_z_levels": sorted({round(float(v), 3) for v in verts[:, 2] if v > 0.1}),
    "painted_faces": int(paint.sum()),
    "paint_radius_min": round(float(radius[paint].min()), 2),
    "paint_radius_max": round(float(radius[paint].max()), 2),
    "paint_faces_in_the_middle": int((radius[paint] < 17.0).sum()),
}
report["verdict"] = ("flat slab with a painted frame along the edge"
                     if report["paint_faces_in_the_middle"] == 0
                     and report["paint_radius_min"] > 17.0
                     else "WRONG")

print(json.dumps(report, indent=2))
