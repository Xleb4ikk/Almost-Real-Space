using System;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Local surface frame (ENU) math — pure double, no UnityEngine. The basis of
    /// planet authoring: without it there is nothing to build the scene on.
    ///
    /// Frame basis: +X north, +Y zenith, +Z east. Right-handed: N × U = E. Matches
    /// the Unity convention (forward=X, up=Y, right=Z): "facing north" means right
    /// points east.
    ///
    /// All directions are body-fixed (longitude is measured from the body's prime
    /// meridian), exactly as in OrbitingBody.GetSurfaceState / SurfaceLatLonAt and
    /// HeightfieldTerrain.GetRawHeightMeters. Never mix with inertial longitudes:
    /// that misses by the body's rotation angle.
    /// </summary>
    public static class SurfaceFrameMath
    {
        /// <summary>Zenith (body-fixed direction to the reference point), radians.</summary>
        public static Vector3d Up(double latitudeRadians, double longitudeRadians)
        {
            double cosLat = Math.Cos(latitudeRadians);
            return new Vector3d(
                cosLat * Math.Cos(longitudeRadians),
                cosLat * Math.Sin(longitudeRadians),
                Math.Sin(latitudeRadians));
        }

        /// <summary>North tangent at the reference point, unit length.</summary>
        public static Vector3d North(double latitudeRadians, double longitudeRadians)
        {
            double sinLat = Math.Sin(latitudeRadians);
            double cosLat = Math.Cos(latitudeRadians);
            return new Vector3d(
                -sinLat * Math.Cos(longitudeRadians),
                -sinLat * Math.Sin(longitudeRadians),
                cosLat);
        }

        /// <summary>East tangent at the reference point, unit length.</summary>
        public static Vector3d East(double latitudeRadians, double longitudeRadians)
        {
            return new Vector3d(-Math.Sin(longitudeRadians), Math.Cos(longitudeRadians), 0d);
        }

        /// <summary>
        /// Rotation of the frame's local axes into body axes: q · x̂ = north,
        /// q · ŷ = zenith, q · ẑ = east. Built from the analytic basis as a matrix
        /// (not from axis-angle composition: no breakdown at the poles or meridian,
        /// one formula for the whole sphere).
        /// </summary>
        public static QuaternionD Rotation(double latitudeRadians, double longitudeRadians)
        {
            return FromBasis(
                North(latitudeRadians, longitudeRadians),
                Up(latitudeRadians, longitudeRadians),
                East(latitudeRadians, longitudeRadians));
        }

        /// <summary>
        /// Body-fixed direction in SIMULATION axes (Y-up): the same permutation
        /// (x, z, −y) as AstroFrame.ToSimulation, but in double — the class knows
        /// nothing of Unity and must not depend on the bridge.
        /// </summary>
        public static Vector3d ToSimulation(Vector3d bodyFixed)
        {
            return new Vector3d(bodyFixed.X, bodyFixed.Z, -bodyFixed.Y);
        }

        /// <summary>
        /// Rotation of the frame's local axes DIRECTLY into simulation axes:
        /// q · x̂ north, q · ŷ zenith, q · ẑ east, all three already in the sim frame.
        ///
        /// The AstroFrame.ToSimulation(QuaternionD) bridge CONJUGATES a rotation
        /// (q_sim = R·q·R⁻¹), which is valid only when the object's local axes are
        /// the sim axes. The frame's local axes are given in body-fixed (North/Up/
        /// East), so this composes the images directly instead.
        /// </summary>
        public static QuaternionD SimulationRotation(
            QuaternionD bodyOrientation, double latitudeRadians, double longitudeRadians)
        {
            return FromBasis(
                ToSimulation(bodyOrientation.Rotate(North(latitudeRadians, longitudeRadians))),
                ToSimulation(bodyOrientation.Rotate(Up(latitudeRadians, longitudeRadians))),
                ToSimulation(bodyOrientation.Rotate(East(latitudeRadians, longitudeRadians))));
        }

        /// <summary>
        /// Quaternion from an orthonormal RIGHT basis: the matrix columns are the
        /// images of the local axes. Standard trace-based conversion with four
        /// branches (as inside Unity Quaternion.LookRotation) — at branch boundaries
        /// (trace ≈ 0) the matrix degenerates and a single formula gives NaN.
        /// </summary>
        public static QuaternionD FromBasis(Vector3d xAxis, Vector3d yAxis, Vector3d zAxis)
        {
            double m00 = xAxis.X, m01 = yAxis.X, m02 = zAxis.X;
            double m10 = xAxis.Y, m11 = yAxis.Y, m12 = zAxis.Y;
            double m20 = xAxis.Z, m21 = yAxis.Z, m22 = zAxis.Z;

            double trace = m00 + m11 + m22;
            double qx, qy, qz, qw;
            if (trace > 0d)
            {
                double s = Math.Sqrt(trace + 1d) * 2d;
                qw = 0.25d * s;
                qx = (m21 - m12) / s;
                qy = (m02 - m20) / s;
                qz = (m10 - m01) / s;
            }
            else if (m00 > m11 && m00 > m22)
            {
                double s = Math.Sqrt(1d + m00 - m11 - m22) * 2d;
                qw = (m21 - m12) / s;
                qx = 0.25d * s;
                qy = (m01 + m10) / s;
                qz = (m02 + m20) / s;
            }
            else if (m11 > m22)
            {
                double s = Math.Sqrt(1d + m11 - m00 - m22) * 2d;
                qw = (m02 - m20) / s;
                qx = (m01 + m10) / s;
                qy = 0.25d * s;
                qz = (m12 + m21) / s;
            }
            else
            {
                double s = Math.Sqrt(1d + m22 - m00 - m11) * 2d;
                qw = (m10 - m01) / s;
                qx = (m02 + m20) / s;
                qy = (m12 + m21) / s;
                qz = 0.25d * s;
            }

            return new QuaternionD(qx, qy, qz, qw).Normalized;
        }

        /// <summary>
        /// Body-fixed direction of a point offset from the reference by (east, north)
        /// METRES along the tangent. The offset is placed on a sphere of radius R:
        /// the real surface lies at R + h, but h is unknown until the direction is
        /// sampled, and the error from substituting the radius is second order in
        /// h/R. Returns a normalized direction in body axes.
        /// </summary>
        public static Vector3d DirectionOffset(
            double latitudeRadians, double longitudeRadians, double radiusMeters,
            double eastMeters, double northMeters)
        {
            Vector3d p = (Up(latitudeRadians, longitudeRadians) * radiusMeters)
                        + (East(latitudeRadians, longitudeRadians) * eastMeters)
                        + (North(latitudeRadians, longitudeRadians) * northMeters);
            double len = p.Magnitude;
            return len > 1e-12d ? (p / len) : Up(latitudeRadians, longitudeRadians);
        }

        /// <summary>
        /// ENU offset (east, north, up — metres) into the frame's local axes
        /// (x=north, y=zenith, z=east). There is no trigonometry here and there
        /// should be none: the frame axes are ALREADY permuted, all that is needed
        /// is to permute the components.
        ///
        /// Separate from DirectionOffset: that carries an offset in BODY axes (for
        /// terrain height sampling), this one in frame axes (for local object
        /// transforms). Confusing the two gives an object rotated by the frame
        /// angle.
        /// </summary>
        public static Vector3d EnuToFrameLocal(double eastMeters, double northMeters, double upMeters)
        {
            return new Vector3d(northMeters, upMeters, eastMeters);
        }

        /// <summary>
        /// Inverse of DirectionOffset: the offset FROM one surface point TO another,
        /// resolved along the frame axes (x=north, y=zenith, z=east).
        ///
        /// Used by anything that draws or places an object at a neighbouring point:
        /// site gizmos, the tape-measure ring, "show where this place is relative to
        /// me". Computed from the CHORD between surface points, not the arc: the
        /// editor tangent plane is straight anyway, and over kilometres the chord
        /// and arc differ by millimetres.
        /// </summary>
        public static Vector3d SurfaceOffsetBetween(
            double fromLatRadians, double fromLonRadians, double fromAltitudeMeters,
            double toLatRadians, double toLonRadians, double toAltitudeMeters,
            double radiusMeters)
        {
            Vector3d from = Up(fromLatRadians, fromLonRadians) * (radiusMeters + fromAltitudeMeters);
            Vector3d to = Up(toLatRadians, toLonRadians) * (radiusMeters + toAltitudeMeters);
            Vector3d delta = to - from;

            // Resolve along the FROM point's basis: exactly the tangent plane in
            // which the author sees the scene.
            return EnuToFrameLocal(
                Vector3d.Dot(delta, East(fromLatRadians, fromLonRadians)),
                Vector3d.Dot(delta, North(fromLatRadians, fromLonRadians)),
                Vector3d.Dot(delta, Up(fromLatRadians, fromLonRadians)));
        }

        /// <summary>Projection of a body-fixed vector onto the frame's local axes (x=north, y=zenith, z=east).</summary>
        public static Vector3d ToLocal(double latitudeRadians, double longitudeRadians, Vector3d bodyFixed)
        {
            return new Vector3d(
                Vector3d.Dot(bodyFixed, North(latitudeRadians, longitudeRadians)),
                Vector3d.Dot(bodyFixed, Up(latitudeRadians, longitudeRadians)),
                Vector3d.Dot(bodyFixed, East(latitudeRadians, longitudeRadians)));
        }
    }
}
