"""Verify the exported FBX and GLB: format magic, primitives, vertex colours."""

import json
import os
import struct
import sys

WORKSPACE = r"C:\MyData\UnityProj\Almost Real Space"
OUT_DIR = os.path.join(WORKSPACE, "Blender", "LaunchPad")

report = {}


def check_glb(path):
    with open(path, "rb") as handle:
        data = handle.read()
    magic, version, length = struct.unpack_from("<4sII", data, 0)
    result = {
        "magic": magic.decode("ascii", "replace"),
        "version": version,
        "declared_length": length,
        "actual_length": len(data),
    }
    offset, chunk_index = 12, 0
    gltf = None
    while offset < len(data):
        chunk_len, chunk_type = struct.unpack_from("<II", data, offset)
        payload = data[offset + 8: offset + 8 + chunk_len]
        if chunk_type == 0x4E4F534A:      # JSON
            gltf = json.loads(payload.decode("utf-8"))
            result["json_chunk"] = chunk_index
        offset += 8 + chunk_len + ((4 - chunk_len % 4) % 4 if chunk_len % 4 else 0)
        chunk_index += 1

    if gltf is None:
        result["error"] = "no JSON chunk"
        return result

    meshes = gltf.get("meshes", [])
    prims = [p for m in meshes for p in m.get("primitives", [])]
    result["meshes"] = len(meshes)
    result["primitives"] = len(prims)
    result["attributes"] = sorted({a for p in prims for a in p.get("attributes", {})})
    result["materials"] = [m.get("name") for m in gltf.get("materials", [])]
    result["accessors"] = len(gltf.get("accessors", []))
    total = 0
    for prim in prims:
        index = prim.get("indices")
        if index is not None:
            total += gltf["accessors"][index]["count"] // 3
    result["triangles"] = total
    result["colour0_present"] = any("COLOR_0" in p.get("attributes", {}) for p in prims)
    return result


def check_fbx(path):
    with open(path, "rb") as handle:
        head = handle.read(27)
    return {
        "magic": head[:20].decode("ascii", "replace").rstrip("\x00"),
        "bytes": os.path.getsize(path),
        "binary": head.startswith(b"Kaydara FBX Binary"),
    }


for name, fn in (("LaunchPad_40x40.glb", check_glb), ("LaunchPad_40x40.fbx", check_fbx)):
    path = os.path.join(OUT_DIR, name)
    report[name] = fn(path) if os.path.exists(path) else "MISSING"

report["files"] = {}
for name in sorted(os.listdir(OUT_DIR)):
    full = os.path.join(OUT_DIR, name)
    report["files"][name] = os.path.getsize(full)

print(json.dumps(report, indent=2))
