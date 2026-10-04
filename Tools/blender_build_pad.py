"""Build the flat stone launch pad inside the running Blender, then export it.

    python Tools/blender_bridge_client.py --file Tools/blender_build_pad.py --no-state --timeout 300

Writes into the workspace: the .blend, an FBX and a GLB, plus a JSON report.
"""

import importlib
import json
import os
import sys
import time

import bpy
import numpy as np
from mathutils import Vector

WORKSPACE = r"C:\MyData\UnityProj\Almost Real Space"
OUT_DIR = os.path.join(WORKSPACE, "Blender", "LaunchPad")
NAME = "LaunchPad_40x40"

# A stale __pycache__ here silently builds yesterday's geometry, so never trust
# the bytecode cache for the generator.
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.join(WORKSPACE, "Tools"))
sys.modules.pop("launch_pad_geom", None)
importlib.invalidate_caches()

import launch_pad_geom as geom  # noqa: E402

geom = importlib.reload(geom)


def reset_scene():
    """Start from an empty scene so the pad does not mix with the default cube."""
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.materials, bpy.data.lights,
                       bpy.data.cameras, bpy.data.images):
        for block in list(datablocks):
            if block.users == 0:
                datablocks.remove(block)


# --------------------------------------------------------------------------
# materials
# --------------------------------------------------------------------------

def make_material(name, roughness, bump_strength, bump_scale):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    tree = mat.node_tree
    tree.nodes.clear()

    out = tree.nodes.new("ShaderNodeOutputMaterial")
    out.location = (620, 0)
    bsdf = tree.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.location = (300, 0)
    bsdf.inputs["Roughness"].default_value = roughness
    if "Specular IOR Level" in bsdf.inputs:
        bsdf.inputs["Specular IOR Level"].default_value = 0.35

    color = tree.nodes.new("ShaderNodeVertexColor")
    color.layer_name = "Col"
    color.location = (-140, 120)

    # faint surface relief only; the top is meant to read as one flat surface,
    # so this is a texture, not displaced geometry
    noise = tree.nodes.new("ShaderNodeTexNoise")
    noise.location = (-360, -200)
    noise.inputs["Scale"].default_value = bump_scale
    noise.inputs["Detail"].default_value = 6.0
    noise.inputs["Roughness"].default_value = 0.5

    bump = tree.nodes.new("ShaderNodeBump")
    bump.location = (60, -220)
    bump.inputs["Strength"].default_value = bump_strength
    bump.inputs["Distance"].default_value = 0.01

    tree.links.new(color.outputs["Color"], bsdf.inputs["Base Color"])
    tree.links.new(noise.outputs["Fac"], bump.inputs["Height"])
    tree.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    tree.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


def build_materials():
    return [make_material("Pad_Stone", 0.82, 0.18, 7.0),
            make_material("Pad_Paint_White", 0.58, 0.10, 18.0)]


# --------------------------------------------------------------------------
# mesh
# --------------------------------------------------------------------------

def build_object():
    data = geom.build()
    stats = data.stats()

    mesh = bpy.data.meshes.new(NAME)
    mesh.from_pydata(data.verts, [], data.faces)

    loop_colors = data.loops()
    if len(mesh.loops) != len(loop_colors):
        raise RuntimeError(
            "loop count mismatch: mesh has %d loops, geometry supplied %d colours"
            % (len(mesh.loops), len(loop_colors))
        )

    for mat in build_materials():
        mesh.materials.append(mat)
    mesh.polygons.foreach_set("material_index", data.mats)

    attribute = mesh.color_attributes.new(name="Col", type="FLOAT_COLOR", domain="CORNER")
    flat = np.ones(len(mesh.loops) * 4, dtype=np.float32)
    flat.reshape(-1, 4)[:, :3] = np.asarray(loop_colors, dtype=np.float32)
    attribute.data.foreach_set("color", flat)
    mesh.color_attributes.active_color_index = 0
    mesh.color_attributes.render_color_index = 0

    obj = bpy.data.objects.new(NAME, mesh)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)

    mesh.update()

    # shade smooth so the 0.5 m grid does not read as faceting, then keep the
    # paint step crisp
    bpy.ops.object.shade_smooth()
    bpy.ops.object.shade_smooth_by_angle(angle=np.radians(30.0))

    stats["loops"] = len(mesh.loops)
    return obj, stats


def validate(obj, stats):
    """Measure the mesh from what Blender actually holds.

    Deliberately no volume figure: the slab keeps its underside open because
    nothing sees it, and the divergence formula is only meaningful on a closed
    shell. A hand rolled version of it reported -399 m3 for an obviously solid
    slab, which is worse than no number at all. Plan area and the set of Z levels
    say what actually matters here - that the top is flat and the marking sits
    3 cm proud of it.
    """
    mesh = obj.data
    stats["mesh_valid"] = bool(mesh.validate(verbose=False))

    mesh = obj.data
    verts = np.array([v.co[:] for v in mesh.vertices], dtype=np.float64)
    loops = np.array([p.vertices[:] for p in mesh.polygons], dtype=np.int64)
    normals = np.array([p.normal[:] for p in mesh.polygons], dtype=np.float64)
    areas = np.array([p.area for p in mesh.polygons], dtype=np.float64)
    mats = np.zeros(len(mesh.polygons), dtype=np.int32)
    mesh.polygons.foreach_get("material_index", mats)

    up = normals[:, 2] > 0.9
    span_x = float(verts[:, 0].max() - verts[:, 0].min())
    span_y = float(verts[:, 1].max() - verts[:, 1].min())

    return {
        "verts": len(mesh.vertices),
        "loops": len(mesh.loops),
        "faces": len(mesh.polygons),
        "tris": int(sum(len(p.vertices) - 2 for p in mesh.polygons)),
        "materials_used": {mesh.materials[i].name: int((mats == i).sum())
                           for i in np.unique(mats)},
        "plan_area_m2": round(span_x * span_y, 1),
        "surface_area_m2": round(float(areas.sum()), 1),
        "top_area_m2": round(float(areas[up].sum()), 1),
        "faces_facing_up": int(up.sum()),
        "faces_facing_down": int((normals[:, 2] < -0.9).sum()),
        "degenerate_faces": int((areas < 1e-9).sum()),
        "loose_verts": int(len(mesh.vertices) - len(np.unique(loops))),
        "non_quads": int(sum(1 for p in mesh.polygons if len(p.vertices) != 4)),
        "top_z_levels": sorted({round(float(v), 3) for v in verts[:, 2]
                                if v > 0.1}),
        "bounds": {
            "min": [round(float(v), 3) for v in verts.min(axis=0)],
            "max": [round(float(v), 3) for v in verts.max(axis=0)],
        },
    }


# --------------------------------------------------------------------------
# scene
# --------------------------------------------------------------------------

def dress_scene(obj):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1600
    scene.render.resolution_y = 1000
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = "PNG"

    world = bpy.data.worlds.new("PadWorld")
    world.use_nodes = True
    background = world.node_tree.nodes["Background"]
    background.inputs[0].default_value = (0.055, 0.062, 0.075, 1.0)
    background.inputs[1].default_value = 1.0
    scene.world = world

    sun_data = bpy.data.lights.new("Sun", type="SUN")
    sun_data.energy = 2.4
    sun_data.angle = np.radians(3.0)
    sun = bpy.data.objects.new("Sun", sun_data)
    sun.rotation_euler = (np.radians(52.0), 0.0, np.radians(38.0))
    bpy.context.collection.objects.link(sun)

    ground_mesh = bpy.data.meshes.new("Ground")
    size = 160.0
    ground_mesh.from_pydata(
        [(-size, -size, 0.0), (size, -size, 0.0), (size, size, 0.0), (-size, size, 0.0)],
        [], [(0, 1, 2, 3)],
    )
    ground_mat = bpy.data.materials.new("Ground")
    ground_mat.use_nodes = True
    ground_bsdf = ground_mat.node_tree.nodes["Principled BSDF"]
    ground_bsdf.inputs["Base Color"].default_value = (0.10, 0.095, 0.085, 1.0)
    ground_bsdf.inputs["Roughness"].default_value = 0.95
    ground_mesh.materials.append(ground_mat)
    ground = bpy.data.objects.new("Ground", ground_mesh)
    bpy.context.collection.objects.link(ground)

    cam_data = bpy.data.cameras.new("Camera")
    camera = bpy.data.objects.new("Camera", cam_data)
    bpy.context.collection.objects.link(camera)
    scene.camera = camera
    return camera, ground


def aim(camera, location, target=(0.0, 0.0, 3.0)):
    camera.location = Vector(location)
    direction = Vector(target) - Vector(location)
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def set_viewport_colors():
    """Show the colour attribute in Solid shading.

    Blender's Solid mode defaults to MATERIAL, which paints the whole object one
    grey and hides the painted frame; without this the marking is invisible in the
    viewport even though it is in the mesh. It has to be part of the build because
    saving the file is what stores it, and a rebuild would otherwise drop it.
    """
    modes = []
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == "VIEW_3D":
                shading = area.spaces.active.shading
                modes.append(shading.color_type)
                shading.color_type = "VERTEX"
    return modes


def main():
    started = time.time()
    reset_scene()
    obj, stats = build_object()
    report = validate(obj, stats)
    report["viewport_color_modes_before"] = set_viewport_colors()
    camera, ground = dress_scene(obj)

    os.makedirs(OUT_DIR, exist_ok=True)
    blend_path = os.path.join(OUT_DIR, NAME + ".blend")
    fbx_path = os.path.join(OUT_DIR, NAME + ".fbx")
    glb_path = os.path.join(OUT_DIR, NAME + ".glb")

    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=fbx_path, use_selection=True,
                             object_types={"MESH"}, path_mode="COPY",
                             mesh_smooth_type="FACE", apply_unit_scale=True)
    bpy.ops.export_scene.gltf(filepath=glb_path, use_selection=True,
                              export_format="GLB", export_apply=True)
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)

    report["exported"] = {
        "blend": os.path.exists(blend_path),
        "fbx": os.path.getsize(fbx_path) if os.path.exists(fbx_path) else 0,
        "glb": os.path.getsize(glb_path) if os.path.exists(glb_path) else 0,
    }
    report["seconds"] = round(time.time() - started, 2)
    report["files"] = {"blend": blend_path, "fbx": fbx_path, "glb": glb_path}

    with open(os.path.join(OUT_DIR, "build_report.json"), "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    print(json.dumps(report, indent=2))


main()
