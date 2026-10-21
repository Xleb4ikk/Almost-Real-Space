using System.Collections.Generic;
using Galilego.Building;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Orion capsule colliders for player walking/flight.
    ///
    /// The player support (MeshSupportBase) pushes the capsule via
    /// Physics.ComputePenetration, which does NOT work with non-convex MeshCollider.
    /// So a small set of CONVEX colliders is built from the real meshes: the hull shell
    /// is cut into wedges (window glass ends up inside a solid wall), a fine grid around
    /// the hatch keeps the opening open, and the door is a single box that moves with it.
    /// Everything is derived from the model itself, so re-exporting the FBX in Blender
    /// is picked up automatically.
    ///
    /// IMPORTANT: all geometry and thresholds are in WORLD METRES, not model axes: the
    /// FBX root has a ×100 scale (centimetres), so thresholds must not be measured in
    /// its axes.
    ///
    /// Needs Read/Write on Orion_CrewModule.fbx (set by OrionModelImport).
    /// </summary>
    public static class OrionCollisionBuilder
    {
        /// <summary>Number of angular sectors of the shell (48 → chord ~0.3 m, sagitta ~5 mm).</summary>
        private const int SectorCount = 48;

        /// <summary>Height band boundaries of the shell above the deck (m): profile convexity.</summary>
        private const float BandA = 0.75f;
        private const float BandB = 1.55f;

        /// <summary>Long triangles are split so they don't pull the convex hull across the opening.</summary>
        private const float MaxEdge = 0.10f;

        /// <summary>Grid step around the hatch: across (m) and vertically (m).</summary>
        private const float CellV = 0.12f;
        private const float CellW = 0.15f;

        /// <summary>
        /// Inset of the opening cut INTO the door edges (m). The door box uses the same
        /// inset from its bounds, so a closed door covers the opening without gaps
        /// (the door sits asymmetrically about the hatch axis).
        /// </summary>
        private const float DoorInset = 0.04f;

        /// <summary>Everything below the deck (m) is the underside, covered by the convex heat-shield cap.</summary>
        private const float BelowDeck = -0.15f;

        /// <summary>Directions for simplifying large convex bodies (hull ≤ 2n-4 = 236 faces &lt; 255).</summary>
        private const int ExtremeDirections = 120;

        private struct Tri
        {
            public Vector3 A;
            public Vector3 B;
            public Vector3 C;
        }

        /// <summary>Capsule coordinate frame: u — towards the hatch, v — sideways, w — up from the deck.</summary>
        private sealed class CapsuleFrame
        {
            public Vector3 Origin;
            public Vector3 Axis;
            public Vector3 Hatch;
            public Vector3 Side;
            public float DeckTop;

            public void ToUvw(Vector3 p, out float u, out float v, out float w)
            {
                Vector3 d = p - Origin;
                u = Vector3.Dot(d, Hatch);
                v = Vector3.Dot(d, Side);
                w = Vector3.Dot(d, Axis) - DeckTop;
            }
        }

        /// <summary>
        /// Build the capsule colliders under partRoot (the model is a descendant of
        /// partRoot). false — the model did not fit (missing meshes / non-readable
        /// mesh): the caller falls back.
        /// </summary>
        public static bool Build(Transform partRoot)
        {
            MeshFilter backshell = Find(partRoot, "Backshell");
            MeshFilter pressure = Find(partRoot, "PressureShell");
            MeshFilter hatchFrame = Find(partRoot, "Hatch_Frame");
            MeshFilter heatShield = Find(partRoot, "HeatShield");
            MeshFilter deck = Find(partRoot, "Deck_Floor");
            MeshFilter nose = Find(partRoot, "Nose_Glass");
            MeshFilter door = Find(partRoot, "Hatch_Door");
            if (backshell == null || pressure == null || hatchFrame == null || heatShield == null
                || deck == null || nose == null || door == null)
            {
                Debug.LogWarning("[Orion] Нет мешей Backshell/PressureShell/Hatch_Frame/HeatShield/Deck_Floor/Nose_Glass/Hatch_Door — "
                    + "точные коллайдеры не построены.");
                return false;
            }

            MeshFilter[] required = { backshell, pressure, hatchFrame, heatShield, deck, nose, door };
            for (int i = 0; i < required.Length; i++)
            {
                if (!required[i].sharedMesh.isReadable)
                {
                    Debug.LogError("[Orion] Меш '" + required[i].name + "' нечитаемый: включи Read/Write у Orion_CrewModule.fbx "
                        + "(Model → Read/Write) — иначе коллайдеры капсулы не построить.");
                    return false;
                }
            }

            var hostGo = new GameObject("Orion_Collision");
            hostGo.transform.SetParent(partRoot, false);
            var owner = hostGo.AddComponent<OrionCollisionMeshes>();
            // The host GO stores body vertices in ITS local axes (the model root has
            // a ×100 FBX scale, so conversion on save is mandatory). All geometry and
            // thresholds (zoneV, clipW, MaxEdge...) are in WORLD METRES: they must not
            // be computed in host axes, which are the model's centimetres.
            Matrix4x4 toHost = hostGo.transform.worldToLocalMatrix;

            List<Vector3> heatPts = Points(heatShield);
            List<Vector3> nosePts = Points(nose);
            List<Vector3> deckPts = Points(deck);
            List<Vector3> framePts = Points(hatchFrame);
            List<Vector3> doorPts = Points(door);

            // Capsule axes from the model data (independent of Blender's axis orientation).
            var f = new CapsuleFrame();
            Vector3 deckCentre = Mean(deckPts);
            f.Axis = (Mean(nosePts) - Mean(heatPts)).normalized;
            // Hatch axis through the DOOR centre: the frame has hinges on one side, so
            // its centre is skewed (~11°) and the opening cut would be slanted.
            Vector3 toHatch = Mean(doorPts) - deckCentre;
            toHatch -= f.Axis * Vector3.Dot(toHatch, f.Axis);
            f.Hatch = toHatch.normalized;
            f.Side = Vector3.Cross(f.Axis, f.Hatch);
            f.Origin = deckCentre;
            f.DeckTop = float.NegativeInfinity;
            for (int i = 0; i < deckPts.Count; i++)
            {
                f.DeckTop = Mathf.Max(f.DeckTop, Vector3.Dot(deckPts[i] - f.Origin, f.Axis));
            }

            // Opening dimensions — from the door and frame.
            float doorMinV = float.PositiveInfinity;
            float doorMaxV = float.NegativeInfinity;
            for (int i = 0; i < doorPts.Count; i++)
            {
                f.ToUvw(doorPts[i], out _, out float v, out _);
                doorMinV = Mathf.Min(doorMinV, v);
                doorMaxV = Mathf.Max(doorMaxV, v);
            }

            float frameMinW = float.PositiveInfinity;
            float frameMaxW = float.NegativeInfinity;
            for (int i = 0; i < framePts.Count; i++)
            {
                f.ToUvw(framePts[i], out _, out _, out float w);
                frameMinW = Mathf.Min(frameMinW, w);
                frameMaxW = Mathf.Max(frameMaxW, w);
            }

            // Cut bounds follow the DOOR edges (it sits asymmetrically about the hatch
            // axis; a symmetric cut would leave a gap at the side). The door box is 2 cm
            // wider, so there are no gaps.
            float clipVMin = doorMinV + DoorInset;
            float clipVMax = doorMaxV - DoorInset;
            float zoneVMin = clipVMin - 0.30f;      // fine grid around the hatch
            float zoneVMax = clipVMax + 0.30f;
            // Opening vertical. The hatch sill is a flat plate up to sillTop (instead
            // of a chamfer): the cut starts 12 cm below its top, so the top of the sill
            // stays intact.
            float sillTop = frameMinW + 0.15f;   // sill top above the deck
            float sillUIn = float.PositiveInfinity, sillUOut = float.NegativeInfinity;
            for (int i = 0; i < framePts.Count; i++)
            {
                f.ToUvw(framePts[i], out float fu, out _, out float fw);
                if (fw < frameMinW + 0.25f)
                {
                    sillUIn = Mathf.Min(sillUIn, fu);
                    sillUOut = Mathf.Max(sillUOut, fu);
                }
            }

            // Top margin 4 cm from the door edge: a physical clearance of ~1.0 m (convex
            // bodies eat another ~0.06 each side) — the crouched player capsule (0.8 m)
            // fits, standing (1.45 m) does not.
            float clipWMin = sillTop - 0.12f;   // the cut does not reach lower: leftover cells below the plate
            float clipWMax = frameMaxW - 0.04f;

            // Shell: wedges + grid at the hatch.
            var cells = new Dictionary<long, List<Vector3>>();
            AddShell(backshell, f, zoneVMin, zoneVMax, clipVMin, clipVMax, clipWMin, clipWMax, cells);
            AddShell(pressure, f, zoneVMin, zoneVMax, clipVMin, clipVMax, clipWMin, clipWMax, cells);
            AddShell(hatchFrame, f, zoneVMin, zoneVMax, clipVMin, clipVMax, clipWMin, clipWMax, cells);
            foreach (KeyValuePair<long, List<Vector3>> cell in cells)
            {
                AddTriangleHull(hostGo, cell.Value, owner, toHost);
            }

            // Hatch sill — a solid plate across the frame depth: shell cell fragments
            // under the cut are thin and must not serve as a floor.
            var sillPts = new List<Vector3>(8);
            foreach (float uu in new[] { sillUIn, sillUOut })
            {
                foreach (float vv in new[] { clipVMin, clipVMax })
                {
                    foreach (float ww in new[] { -0.02f, sillTop })
                    {
                        sillPts.Add(f.Origin + f.Hatch * uu + f.Side * vv + f.Axis * (ww + f.DeckTop));
                    }
                }
            }

            AddPointsHull(hostGo, sillPts, owner, toHost);

            // Underside and deck: heat-shield cap with its top cut off at the deck.
            var heatClamped = new List<Vector3>(heatPts.Count);
            for (int i = 0; i < heatPts.Count; i++)
            {
                Vector3 p = heatPts[i];
                f.ToUvw(p, out _, out _, out float w);
                heatClamped.Add(w > 0f ? p - (f.Axis * w) : p);
            }

            AddReducedHull(hostGo, heatClamped, owner, toHost);

            // Deck (Deck_Floor) — a separate slab body over the visual floor: the
            // heat-shield cap alone sits a couple of centimetres lower and can be
            // missing at the edges near the walls.
            AddReducedHull(hostGo, deckPts, owner, toHost);

            // Nose glass (cap above the shell ring).
            AddReducedHull(hostGo, nosePts, owner, toHost);

            // Furniture: pilot seat, operator chair and desk — one convex body each, on
            // their own object with the PlayerObstacleOnly marker (a wall, not a step).
            AddFurniture(partRoot, "Pilot_Seat", hostGo, owner, toHost);
            AddFurniture(partRoot, "Operator_Chair", hostGo, owner, toHost);
            AddFurniture(partRoot, "Operator_Desk", hostGo, owner, toHost);

            // Door: a box in the door's local axes — moves with it when opening.
            AddDoorBox(door);

            // Model's existing colliders (monitor bodies — 8 vertices, boxes): non-convex
            // does not work with ComputePenetration.
            MeshCollider[] existing = partRoot.GetComponentsInChildren<MeshCollider>(true);
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i].transform.IsChildOf(hostGo.transform))
                {
                    continue;
                }

                // Monitors and tablet sit at head/chest height and are needed for rays
                // (crosshair, pointer) but not for the body. Everything else existing is
                // just an obstacle.
                string n = existing[i].name;
                if (n.IndexOf("Monitor", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Tablet", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Keep these NON-convex: convex MeshColliders report
                    // RaycastHit.triangleIndex = -1 and textureCoord = (0,0), which put
                    // the pointer in the screen corner. The body ignores them anyway.
                    existing[i].convex = false;
                    MarkIgnored(existing[i].gameObject);
                }
                else
                {
                    existing[i].convex = true;
                    MarkObstacleOnly(existing[i].gameObject);
                }
            }

            // Monitors (pointer BoxCollider) — for rays only.
            foreach (ProceduralMonitor monitor in partRoot.GetComponentsInChildren<ProceduralMonitor>(true))
            {
                MarkIgnored(monitor.gameObject);
            }

            // Seat interaction box (E) — also for rays only. Physical furniture is the
            // Col_* bodies. The collider stays solid and NOT a Trigger, so the crosshair
            // sees it under any Physics.queriesHitTriggers setting.
            foreach (CockpitSeat seat in partRoot.GetComponentsInChildren<CockpitSeat>(true))
            {
                MarkIgnored(seat.gameObject);
            }

            return true;
        }

        private static void AddShell(MeshFilter source, CapsuleFrame f, float zoneVMin, float zoneVMax,
            float clipVMin, float clipVMax, float clipWMin, float clipWMax, Dictionary<long, List<Vector3>> cells)
        {
            Mesh mesh = source.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int[] indices = mesh.triangles;
            Matrix4x4 m = source.transform.localToWorldMatrix;
            var stack = new Stack<Tri>();

            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                stack.Push(new Tri
                {
                    A = m.MultiplyPoint3x4(vertices[indices[i]]),
                    B = m.MultiplyPoint3x4(vertices[indices[i + 1]]),
                    C = m.MultiplyPoint3x4(vertices[indices[i + 2]])
                });

                while (stack.Count > 0)
                {
                    Tri t = stack.Pop();
                    float ab = (t.B - t.A).sqrMagnitude;
                    float bc = (t.C - t.B).sqrMagnitude;
                    float ca = (t.A - t.C).sqrMagnitude;
                    float longest = Mathf.Max(ab, Mathf.Max(bc, ca));
                    if (longest > MaxEdge * MaxEdge)
                    {
                        // Split the longest edge in half.
                        if (ab >= bc && ab >= ca)
                        {
                            Vector3 mid = (t.A + t.B) * 0.5f;
                            stack.Push(new Tri { A = t.A, B = mid, C = t.C });
                            stack.Push(new Tri { A = mid, B = t.B, C = t.C });
                        }
                        else if (bc >= ca)
                        {
                            Vector3 mid = (t.B + t.C) * 0.5f;
                            stack.Push(new Tri { A = t.A, B = t.B, C = mid });
                            stack.Push(new Tri { A = t.A, B = mid, C = t.C });
                        }
                        else
                        {
                            Vector3 mid = (t.C + t.A) * 0.5f;
                            stack.Push(new Tri { A = t.A, B = t.B, C = mid });
                            stack.Push(new Tri { A = mid, B = t.B, C = t.C });
                        }

                        continue;
                    }

                    Classify(t, f, zoneVMin, zoneVMax, clipVMin, clipVMax, clipWMin, clipWMax, cells);
                }
            }
        }

        private static void Classify(Tri t, CapsuleFrame f, float zoneVMin, float zoneVMax,
            float clipVMin, float clipVMax, float clipWMin, float clipWMax,
            Dictionary<long, List<Vector3>> cells)
        {
            Vector3 centre = (t.A + t.B + t.C) / 3f;
            f.ToUvw(centre, out float u, out float v, out float w);
            if (w < BelowDeck)
            {
                return;
            }

            bool zone = u > 0f && v > zoneVMin && v < zoneVMax;
            if (zone && v > clipVMin && v < clipVMax && w >= clipWMin && w <= clipWMax)
            {
                // The opening: cut it out (the door covers it with its box when closed).
                return;
            }

            long key;
            if (zone)
            {
                int iv = Mathf.FloorToInt((v - zoneVMin) / CellV);
                // Opening jambs (v in the hatch band but outside the cut) within the cut
                // height band merge into one bucket: a single convex body without vertical
                // cell seams.
                int iw = (w >= clipWMin && w <= clipWMax) ? -100 : Mathf.FloorToInt(w / CellW);
                key = Key(1, iv, iw);
            }
            else
            {
                float angle = Mathf.Atan2(v, u) * Mathf.Rad2Deg;
                if (angle < 0f)
                {
                    angle += 360f;
                }

                int sector = Mathf.Min(SectorCount - 1, Mathf.FloorToInt(angle / (360f / SectorCount)));
                int band = w < BandA ? 0 : (w < BandB ? 1 : 2);
                key = Key(0, sector, band);
            }

            if (!cells.TryGetValue(key, out List<Vector3> list))
            {
                list = new List<Vector3>(64);
                cells.Add(key, list);
            }

            list.Add(t.A);
            list.Add(t.B);
            list.Add(t.C);
        }

        private static long Key(int kind, int a, int b)
        {
            return ((long)kind << 44) | ((long)(a & 0x3FFFFF) << 22) | (long)(b & 0x3FFFFF);
        }

        /// <summary>Convex body from a set of triangles (vertices in world metres, in triples).</summary>
        private static void AddTriangleHull(GameObject host, List<Vector3> triangleVertices, OrionCollisionMeshes owner,
            Matrix4x4 toHost)
        {
            var map = new Dictionary<Vector3Int, int>();
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            for (int i = 0; i + 2 < triangleVertices.Count; i += 3)
            {
                int a = Weld(map, vertices, toHost.MultiplyPoint3x4(triangleVertices[i]));
                int b = Weld(map, vertices, toHost.MultiplyPoint3x4(triangleVertices[i + 1]));
                int c = Weld(map, vertices, toHost.MultiplyPoint3x4(triangleVertices[i + 2]));
                if (a == b || b == c || a == c)
                {
                    continue;
                }

                indices.Add(a);
                indices.Add(b);
                indices.Add(c);
            }

            if (vertices.Count < 4 || indices.Count < 3)
            {
                return;
            }

            AttachHull(host, vertices, indices, owner);
        }

        /// <summary>
        /// Large body: take extreme points along fixed directions — the convex hull fits
        /// within the PhysX limit (255 faces) for certain and is not a "half hull".
        /// </summary>
        private static void AddReducedHull(GameObject host, List<Vector3> points, OrionCollisionMeshes owner,
            Matrix4x4 toHost)
        {
            if (points.Count < 4)
            {
                return;
            }

            var vertices = new List<Vector3>();
            var seen = new HashSet<int>();
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int d = 0; d < ExtremeDirections; d++)
            {
                float y = 1f - (2f * (d + 0.5f) / ExtremeDirections);
                float r = Mathf.Sqrt(Mathf.Max(0f, 1f - (y * y)));
                float phi = d * golden;
                var dir = new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r);

                int best = 0;
                float bestDot = float.NegativeInfinity;
                for (int i = 0; i < points.Count; i++)
                {
                    float dot = Vector3.Dot(points[i], dir);
                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        best = i;
                    }
                }

                if (seen.Add(best))
                {
                    vertices.Add(toHost.MultiplyPoint3x4(points[best]));
                }
            }

            if (vertices.Count < 4)
            {
                return;
            }

            // Topology does not matter: PhysX builds the convex hull from vertices; indices are just for the mesh.
            var indices = new List<int>();
            for (int i = 1; i + 1 < vertices.Count; i++)
            {
                indices.Add(0);
                indices.Add(i);
                indices.Add(i + 1);
            }

            AttachHull(host, vertices, indices, owner);
        }

        /// <summary>Convex body from a ready point set (the sill plate — its 8 corners).</summary>
        private static void AddPointsHull(GameObject host, List<Vector3> points, OrionCollisionMeshes owner,
            Matrix4x4 toHost)
        {
            if (points.Count < 4)
            {
                return;
            }

            var vertices = new List<Vector3>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                vertices.Add(toHost.MultiplyPoint3x4(points[i]));
            }

            // Topology does not matter: PhysX builds the convex hull from vertices.
            var indices = new List<int>();
            for (int i = 1; i + 1 < vertices.Count; i++)
            {
                indices.Add(0);
                indices.Add(i);
                indices.Add(i + 1);
            }

            AttachHull(host, vertices, indices, owner);
        }

        /// <summary>
        /// Furniture is an obstacle, not support: one convex body per item, on a separate
        /// object with the PlayerObstacleOnly marker (the marker affects all colliders of
        /// its GameObject, while the hull and deck on the host stay support).
        /// </summary>
        private static void AddFurniture(Transform partRoot, string meshName, GameObject host,
            OrionCollisionMeshes owner, Matrix4x4 toHost)
        {
            MeshFilter filter = Find(partRoot, meshName);
            if (filter == null || !filter.sharedMesh.isReadable)
            {
                return;
            }

            var go = new GameObject("Col_" + meshName);
            go.transform.SetParent(host.transform, false); // zero local pose: same axes as host
            go.AddComponent<PlayerObstacleOnly>();
            AddReducedHull(go, Points(filter), owner, toHost);
        }

        private static void MarkObstacleOnly(GameObject go)
        {
            if (go.GetComponent<PlayerObstacleOnly>() == null)
            {
                go.AddComponent<PlayerObstacleOnly>();
            }
        }

        private static void MarkIgnored(GameObject go)
        {
            if (go.GetComponent<PlayerIgnore>() == null)
            {
                go.AddComponent<PlayerIgnore>();
            }
        }

        /// <summary>Door: a single box by its mesh bounds (door local axes).</summary>
        private static void AddDoorBox(MeshFilter door)
        {
            Bounds b = door.sharedMesh.bounds;
            Vector3 lossy = door.transform.lossyScale;
            var box = door.gameObject.AddComponent<BoxCollider>();
            box.center = b.center;

            // The DoorInset margin and the 0.05 m minimum are in METRES: FBX nodes have a
            // hundredfold scale (mesh in centimetres, local size ~0.02), so centimetres
            // must not be subtracted from a local size.
            box.size = new Vector3(
                Mathf.Max(0.05f, (Mathf.Abs(lossy.x) * b.size.x) - DoorInset) / Mathf.Max(1e-6f, Mathf.Abs(lossy.x)),
                Mathf.Max(0.05f, (Mathf.Abs(lossy.y) * b.size.y) - DoorInset) / Mathf.Max(1e-6f, Mathf.Abs(lossy.y)),
                Mathf.Max(0.05f, (Mathf.Abs(lossy.z) * b.size.z) - DoorInset) / Mathf.Max(1e-6f, Mathf.Abs(lossy.z)));
        }

        private static void AttachHull(GameObject host, List<Vector3> vertices, List<int> indices,
            OrionCollisionMeshes owner)
        {
            var mesh = new Mesh { name = "OrionHull" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices, 0);
            owner.Meshes.Add(mesh);

            var collider = host.AddComponent<MeshCollider>();
            collider.convex = true;
            collider.sharedMesh = mesh;
        }

        private static int Weld(Dictionary<Vector3Int, int> map, List<Vector3> vertices, Vector3 p)
        {
            var key = new Vector3Int(
                Mathf.RoundToInt(p.x * 1000f), Mathf.RoundToInt(p.y * 1000f), Mathf.RoundToInt(p.z * 1000f));
            if (!map.TryGetValue(key, out int id))
            {
                id = vertices.Count;
                map.Add(key, id);
                vertices.Add(p);
            }

            return id;
        }

        private static MeshFilter Find(Transform root, string name)
        {
            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i].name == name && filters[i].sharedMesh != null)
                {
                    return filters[i];
                }
            }

            return null;
        }

        /// <summary>Mesh vertices in world metres (the model's world axes already include the FBX scale).</summary>
        private static List<Vector3> Points(MeshFilter filter)
        {
            Vector3[] vertices = filter.sharedMesh.vertices;
            Matrix4x4 m = filter.transform.localToWorldMatrix;
            var result = new List<Vector3>(vertices.Length);
            for (int i = 0; i < vertices.Length; i++)
            {
                result.Add(m.MultiplyPoint3x4(vertices[i]));
            }

            return result;
        }

        private static Vector3 Mean(List<Vector3> points)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < points.Count; i++)
            {
                sum += points[i];
            }

            return points.Count > 0 ? sum / points.Count : Vector3.zero;
        }
    }
}
