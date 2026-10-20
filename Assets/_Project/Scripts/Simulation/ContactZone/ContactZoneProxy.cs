// Contact zone (vertical slice).
// Builds Unity (PhysX) bodies from PartDefinition. Unity coordinates: x = north, y = up, z = east
// (rotation from (east, north, up), without mirroring). The ship's Y axis matches the site Up.
// The assembly is shifted up so the bottom of the shape matches the ship point (in game that is the vessel bottom).
// Cylinder and frustum are closed prisms: a convex MeshCollider needs triangles, not a vertex cloud.
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Spacecraft;
using UnityEngine;

namespace Galilego.Simulation.ContactZone
{
    public sealed class ContactZoneProxy : MonoBehaviour
    {
        public Vector3d CenterOfMassLocal { get; private set; }
        public Vector3d CenterOfMassVelocityLocal { get; private set; }

        private readonly List<Rigidbody> bodies = new List<Rigidbody>();
        private readonly List<Joint> joints = new List<Joint>();

        // Joint breaks: indices of CHILD parts with a broken joint; the root-island
        // mask marks which bodies still belong to the ship (the rest are debris,
        // living in the scene as PhysX physics until zone exit).
        private PartDefinition[] partDefs;
        private readonly List<int> brokenChildren = new List<int>();
        private bool[] rootMask;

        /// <summary>Indices of parts with broken joints (in break order).</summary>
        public IReadOnlyList<int> BrokenChildren => brokenChildren;

        public int BodyCount => bodies.Count;

        public Rigidbody BodyAt(int index)
        {
            return index >= 0 && index < bodies.Count ? bodies[index] : null;
        }

        /// <summary>Whether the body belongs to the root island (all bodies before any break).</summary>
        public bool IsRootBody(int index)
        {
            if (rootMask == null)
            {
                return index >= 0 && index < bodies.Count;
            }

            return index >= 0 && index < rootMask.Length && rootMask[index];
        }

        public void Build(IReadOnlyList<PartDefinition> parts, SiteFrame frame, Vector3d localPosition, Vector3d localVelocity, bool positionIsCom = false)
        {
            Clear();
            if (parts == null || parts.Count == 0) return;

            partDefs = new PartDefinition[parts.Count];
            for (int i = 0; i < parts.Count; i++)
            {
                partDefs[i] = parts[i];
            }

            Vector3 origin = ToUnity(localPosition);
            Vector3 vel = ToUnity(localVelocity);

            // In game the ship point is the vessel bottom (touch is detected by it), while
            // part Offset is measured from the assembly's internal origin, so T1 hangs 5.5 m
            // below. Shift the assembly so the lowest point of the shape lands on the ship point.
            float lowest = 0f;
            bool hasLowest = false;
            for (int i = 0; i < parts.Count; i++)
            {
                float low = parts[i].Offset.y - PartHalfHeight(parts[i]);
                if (!hasLowest || low < lowest)
                {
                    lowest = low;
                    hasLowest = true;
                }
            }

            Vector3 lift = new Vector3(0f, hasLowest ? -lowest : 0f, 0f);
            if (positionIsCom)
            {
                // The incoming point is the CENTER OF MASS (free 6-DOF flight): place the assembly with its COM at the point.
                Vector3 weighted = Vector3.zero;
                float total = 0f;
                for (int i = 0; i < parts.Count; i++)
                {
                    weighted += parts[i].Offset * (float)parts[i].MassKg;
                    total += (float)parts[i].MassKg;
                }

                lift = total > 0f ? -(weighted / total) : lift;
            }

            for (int i = 0; i < parts.Count; i++)
            {
                PartDefinition d = parts[i];
                var go = new GameObject("Part_" + i + "_" + d.Name);
                go.transform.SetParent(transform, false);
                // Part position relative to the ship point (the ship's Y axis is the site up).
                go.transform.localPosition = origin + d.Offset + lift;
                go.transform.localRotation = new Quaternion(d.Orientation.x, d.Orientation.y, d.Orientation.z, d.Orientation.w);

                var rb = go.AddComponent<Rigidbody>();
                rb.mass = (float)d.MassKg;
                rb.linearVelocity = vel;
                // Continuous collision detection: a fast vessel (hard landing, re-entry
                // debris) must not tunnel through the terrain patch between two physics steps.
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                AddShapeCollider(go, d);
                // Vessel part marker: the player support (VesselSupport) does not let
                // the player through the proxy bodies while in flight. A part with
                // exact colliders (the capsule) is skipped: the player walks on the
                // visual's own colliders, and the solid prism would push them out of
                // the volume.
                if (!d.PlayerPassthrough)
                {
                    go.AddComponent<Galilego.Building.VesselPartCollider>();
                }
                bodies.Add(rb);
            }

            // Joints: every part with ParentIndex >= 0 is attached to its parent by a
            // fixed joint. The part carries an OnJointBreak receiver (PhysX messages
            // the GameObject holding the joint), which reports the broken part index to the proxy.
            for (int i = 0; i < parts.Count; i++)
            {
                PartDefinition d = parts[i];
                if (d.ParentIndex < 0 || d.ParentIndex >= bodies.Count) continue;
                var fj = bodies[i].gameObject.AddComponent<FixedJoint>();
                fj.connectedBody = bodies[d.ParentIndex];
                fj.breakForce = (float)d.JointStrengthNewtons;
                fj.breakTorque = (float)d.BreakTorqueNm;
                fj.enableCollision = false; // adjacent parts of the same ship do not collide
                joints.Add(fj);
                bodies[i].gameObject.AddComponent<ZoneJointBreakListener>().Bind(this, i);
            }

            Physics.defaultSolverIterations = 20;
            Physics.defaultSolverVelocityIterations = 4;
        }

        /// <summary>Index of the root-island body (for ship orientation); −1 — none.</summary>
        public int RootBodyIndex()
        {
            for (int i = 0; i < bodies.Count; i++)
            {
                if (IsRootBody(i) && bodies[i] != null) return i;
            }

            return -1;
        }

        /// <summary>
        /// Zone entry with arbitrary orientation (re-entry from flight): rotates the whole
        /// assembly around its center of mass by qLocal (proxy local axes) and sets the
        /// angular velocity omegaLocal (rad/s, local axes). Each body's velocity = v_com + ω×r.
        /// </summary>
        public void ApplyRigidState(Quaternion qLocal, Vector3 omegaLocal)
        {
            if (bodies.Count == 0) return;
            Vector3 c = Vector3.zero; float m = 0f; Vector3 v = Vector3.zero;
            for (int i = 0; i < bodies.Count; i++)
            {
                float mi = bodies[i].mass;
                c += bodies[i].transform.localPosition * mi;
                v += transform.InverseTransformDirection(bodies[i].linearVelocity) * mi;
                m += mi;
            }

            c /= m; v /= m;
            Vector3 omegaWorld = transform.TransformDirection(omegaLocal);
            for (int i = 0; i < bodies.Count; i++)
            {
                Transform t = bodies[i].transform;
                Vector3 r = qLocal * (t.localPosition - c);
                t.localPosition = c + r;
                t.localRotation = qLocal * t.localRotation;
                bodies[i].linearVelocity = transform.TransformDirection(v) + Vector3.Cross(omegaWorld, transform.TransformDirection(r));
                bodies[i].angularVelocity = omegaWorld;
            }

            Physics.SyncTransforms();
        }

        /// <summary>
        /// Staged/emergency separation in the zone: breaks all joints between bodies with
        /// keep[i]==true and the rest; the remaining ones form the root island (the separated
        /// ones are PhysX debris until zone exit). kickMetersPerSecond is the relative
        /// separation speed along kickDirLocal (local axes).
        /// </summary>
        public void SeverForStaging(bool[] keep, Vector3 kickDirLocal, float kickMetersPerSecond)
        {
            for (int i = 0; i < bodies.Count; i++)
            {
                bool inKeep = i < keep.Length && keep[i];
                var fj = bodies[i] != null ? bodies[i].GetComponent<FixedJoint>() : null;
                if (fj == null || fj.connectedBody == null) continue;
                int parent = bodies.IndexOf(fj.connectedBody);
                bool parentKeep = parent >= 0 && parent < keep.Length && keep[parent];
                if (inKeep != parentKeep)
                {
                    Destroy(fj);
                }
            }

            // Joints that could connect the jettisoned part to a remaining one "below" (parent is jettisoned).
            rootMask = new bool[bodies.Count];
            for (int i = 0; i < bodies.Count; i++)
            {
                rootMask[i] = i < keep.Length && keep[i];
                if (!rootMask[i] && bodies[i] != null && kickMetersPerSecond != 0f)
                {
                    bodies[i].linearVelocity += transform.TransformDirection(kickDirLocal) * kickMetersPerSecond;
                }
            }
        }

        /// <summary>
        /// A joint broke (part's OnJointBreak): the part and its subtree become
        /// debris — excluded from the root island's center of mass and velocity, but
        /// they stay in the scene as PhysX bodies until zone exit (Clear). Handing
        /// debris to DebrisPool is phase 7 (deferred, see the slice report).
        /// </summary>
        public void OnPartJointBreak(int partIndex, float breakForce)
        {
            if (brokenChildren.Contains(partIndex))
            {
                return;
            }

            brokenChildren.Add(partIndex);
            RecomputeRootMask();
            Debug.Log("[ContactZone] стык разорван: деталь " + partIndex
                + " (сила " + breakForce.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " Н),"
                + " отломков-островов: " + (rootMask != null ? DetachedIslandCount() : 0)
                + ", корневых деталей: " + RootCount());
        }

        private void RecomputeRootMask()
        {
            if (partDefs == null)
            {
                rootMask = null;
                return;
            }

            ZoneIslands.SplitResult split = ZoneIslands.Split(partDefs, brokenChildren);
            rootMask = new bool[bodies.Count];
            for (int i = 0; i < split.Root.Length; i++)
            {
                int index = split.Root[i];
                if (index >= 0 && index < rootMask.Length)
                {
                    rootMask[index] = true;
                }
            }
        }

        private int RootCount()
        {
            int count = 0;
            for (int i = 0; i < bodies.Count; i++)
            {
                if (IsRootBody(i))
                {
                    count++;
                }
            }

            return count;
        }

        private int DetachedIslandCount()
        {
            if (partDefs == null)
            {
                return 0;
            }

            return ZoneIslands.Split(partDefs, brokenChildren).Detached.Count;
        }

        public void Clear()
        {
            for (int i = 0; i < bodies.Count; i++)
            {
                if (bodies[i] != null) Destroy(bodies[i].gameObject);
            }

            bodies.Clear();
            joints.Clear();
            partDefs = null;
            brokenChildren.Clear();
            rootMask = null;
        }

        public void SetFrameGravity(Vector3d localGravity)
        {
            // Frame gravity in Unity axes (north, up, east), then into WORLD through
            // the zone root rotation: Physics.gravity is world, while the root is
            // rotated by site axes.
            Physics.gravity = transform.rotation * ToUnity(localGravity);
        }

        // Center of mass and its velocity from the current bodies (site coordinates).
        // IMPORTANT: worldCenterOfMass and linearVelocity are WORLD; convert them into
        // the zone root axes (the root rotation is arbitrary).
        public void GetCenterOfMassState(SiteFrame frame, double t, out Vector3d position, out Vector3d velocity)
        {
            if (bodies.Count == 0)
            {
                position = CenterOfMassLocal;
                velocity = CenterOfMassVelocityLocal;
                return;
            }

            Vector3 c = Vector3.zero;
            Vector3 v = Vector3.zero;
            float m = 0f;
            for (int i = 0; i < bodies.Count; i++)
            {
                if (!IsRootBody(i))
                {
                    continue; // debris is not part of the ship's center of mass
                }

                float mi = bodies[i].mass;
                c += transform.InverseTransformPoint(bodies[i].worldCenterOfMass) * mi;
                v += transform.InverseTransformDirection(bodies[i].linearVelocity) * mi;
                m += mi;
            }

            if (m <= 0f)
            {
                position = CenterOfMassLocal;
                velocity = CenterOfMassVelocityLocal;
                return;
            }

            c /= m;
            v /= m;
            CenterOfMassLocal = FromUnity(c);
            CenterOfMassVelocityLocal = FromUnity(v);
            position = CenterOfMassLocal;
            velocity = CenterOfMassVelocityLocal;
        }

        // Half-height of the shape along the ship's Y axis (up): used to seat the bottom
        // of the assembly on the ship point. Cylinder/box/frustum — Dimensions.y/2,
        // sphere — radius, point — 0.
        private static float PartHalfHeight(PartDefinition d)
        {
            switch (d.Shape)
            {
                case PartShape.Box:
                case PartShape.Cylinder:
                case PartShape.Frustum:
                    return 0.5f * d.Dimensions.y;
                case PartShape.Sphere:
                    return d.Dimensions.x;
                default:
                    return 0f;
            }
        }

        private static void AddShapeCollider(GameObject go, PartDefinition d)
        {
            switch (d.Shape)
            {
                case PartShape.Box:
                    var box = go.AddComponent<BoxCollider>();
                    box.size = new Vector3(d.Dimensions.x, d.Dimensions.y, d.Dimensions.z);
                    break;
                case PartShape.Sphere:
                    var sphere = go.AddComponent<SphereCollider>();
                    sphere.radius = d.Dimensions.x;
                    break;
                case PartShape.Cylinder:
                    // Convex mesh prism (12 faces): PhysX has no direct cylinder.
                    var mc = go.AddComponent<MeshCollider>();
                    mc.convex = true;
                    mc.sharedMesh = Prism(d.Dimensions.x, d.Dimensions.x, d.Dimensions.y, 12);
                    break;
                case PartShape.Frustum:
                    // Tank adapter: the same prism but with different end radii.
                    var fmc = go.AddComponent<MeshCollider>();
                    fmc.convex = true;
                    fmc.sharedMesh = Prism(d.Dimensions.x, d.Dimensions.z, d.Dimensions.y, 16);
                    break;
                default:
                    // Point: mass without geometry — a tiny collider so the body stays put.
                    var tiny = go.AddComponent<SphereCollider>();
                    tiny.radius = 0.05f;
                    break;
            }
        }

        // CLOSED prism: side faces + caps, the end radii may differ (frustum).
        // A convex MeshCollider needs triangles — PhysX cooking does not accept
        // a vertex cloud.
        private static Mesh Prism(float bottomRadius, float topRadius, float height, int sides)
        {
            var mesh = new Mesh();
            var verts = new Vector3[(2 * sides) + 2];
            for (int i = 0; i < sides; i++)
            {
                float a = 2f * Mathf.PI * i / sides;
                float cs = Mathf.Cos(a);
                float sn = Mathf.Sin(a);
                verts[i] = new Vector3(bottomRadius * cs, -0.5f * height, bottomRadius * sn);
                verts[sides + i] = new Vector3(topRadius * cs, 0.5f * height, topRadius * sn);
            }

            verts[2 * sides] = new Vector3(0f, -0.5f * height, 0f);
            verts[(2 * sides) + 1] = new Vector3(0f, 0.5f * height, 0f);

            var tris = new int[(sides * 2 * 3) + (sides * 2 * 3)];
            int k = 0;
            for (int i = 0; i < sides; i++)
            {
                int i2 = (i + 1) % sides;
                int b0 = i;
                int b1 = i2;
                int t0 = sides + i;
                int t1 = sides + i2;

                // Side face (two triangles, outward orientation).
                tris[k++] = b0; tris[k++] = b1; tris[k++] = t1;
                tris[k++] = b0; tris[k++] = t1; tris[k++] = t0;

                int bc = 2 * sides;
                int tc = (2 * sides) + 1;
                tris[k++] = bc; tris[k++] = b1; tris[k++] = b0;
                tris[k++] = tc; tris[k++] = t0; tris[k++] = t1;
            }

            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        // Site local axes (east, north, up) → Unity (north, up, east).
        private static Vector3 ToUnity(Vector3d v) => new Vector3((float)v.Y, (float)v.Z, (float)v.X);
        private static Vector3d FromUnity(Vector3 v) => new Vector3d(v.z, v.x, v.y);
    }
}
