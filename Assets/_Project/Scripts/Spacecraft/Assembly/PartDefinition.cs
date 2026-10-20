using System;
using UnityEngine;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Part shape for the inertia tensor. Point — mass without geometry.
    /// Dimensions are in Dimensions, metres:
    ///   Box:      full size along the part axes (x, y, z);
    ///   Cylinder: x = radius, y = height along the part's Y axis, centred;
    ///   Sphere:   x = radius;
    ///   Frustum:  x = bottom radius, y = height, z = top radius
    ///             (truncated cone adapter, Y axis, centred).
    /// </summary>
    public enum PartShape
    {
        Point = 0,
        Box = 1,
        Cylinder = 2,
        Sphere = 3,
        Frustum = 4,
    }

    /// <summary>
    /// Serializable part description. Offset — the part centre in the ship frame, metres.
    /// Orientation — rotation of the part frame relative to the ship frame
    /// (quaternion x, y, z, w; identity by default). Mass is explicit: the shape
    /// only defines inertia, density is not used.
    /// ParentIndex — index of the parent part in the array, -1 for the root.
    /// JointAnchor and EngineOffset — points in the part frame, metres.
    /// BreakTorqueNm — joint torque limit; JointStrengthNewtons — force limit.
    /// Joint fields are data only at this stage: the graph is validated in phase 2.
    /// </summary>
    [Serializable]
    public class PartDefinition
    {
        public string Name = "Деталь";
        public double MassKg = 1000d;
        public double JointStrengthNewtons = 200000d;
        public double BreakTorqueNm = 1e12d;
        public Vector3 Offset;
        public PartShape Shape = PartShape.Point;
        public Vector3 Dimensions;
        public Vector4 Orientation = new Vector4(0f, 0f, 0f, 1f);
        public int ParentIndex = -1;
        public Vector3 JointAnchor;
        public Vector3 EngineOffset;

        /// <summary>
        /// The part has exact colliders on its visual (the capsule interior): the
        /// player walks on them and the E rays hit them, but they take no part in
        /// the ship physics — the proxy body of such a part is transparent to the
        /// player (no VesselPartCollider marker), so the solid prism does not push
        /// them out of the volume.
        /// </summary>
        public bool PlayerPassthrough;
    }
}
