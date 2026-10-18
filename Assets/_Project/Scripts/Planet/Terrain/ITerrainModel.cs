using System;
using Galilego.Core;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Geometry of a body's surface. Hard contract (clause 5/12 v3): height h and
    /// normal n refer to the SAME geometry — an implementation must derive n from
    /// the same shape function as h. A mismatch of "surface from one place, normal
    /// from another" is forbidden: the contact point (projection) and force
    /// decomposition must agree, otherwise friction holds where the projection has
    /// already let go, or vice versa.
    /// Implementations: SphericalTerrain (smooth sphere) and HeightfieldTerrain
    /// (procedural fBm relief + sea level).
    /// </summary>
    public interface ITerrainModel
    {
        /// <summary>
        /// Surface height above Radius (m) in body-fixed coordinates
        /// (lat/lon in radians — the same as produced by OrbitingBody.SurfaceLatLonAt).
        /// Body rotation is accounted for by the body-fixed frame itself.
        /// Includes the sea clamp: in the ocean it returns sea level (flat water for
        /// ship landing and touch detectors).
        /// </summary>
        double GetHeightMeters(OrbitingBody body, double latitudeRadians, double longitudeRadians);

        /// <summary>
        /// RAW relief height above Radius (m) WITHOUT the sea clamp: in the ocean the
        /// floor depth (negative relative to sea level). The only way to learn the real
        /// floor (for swimming, water geometry, depth).
        /// </summary>
        double GetRawHeightMeters(OrbitingBody body, double latitudeRadians, double longitudeRadians);

        /// <summary>
        /// Sea level above Radius (m). NegativeInfinity (and any value ≤ −1e29) =
        /// no sea. Matches the threshold at which the renderer paints water.
        /// </summary>
        double GetSeaLevelMeters();

        /// <summary>
        /// Outward surface normal at point relPos (relative to the body centre, world
        /// coordinates) at time timeSeconds. The time is mandatory: the relief rotates
        /// with the body, so the normal in the world frame depends on spin.
        /// </summary>
        Vector3d GetOutwardNormal(OrbitingBody body, Vector3d relativePosition, double timeSeconds);
    }

    /// <summary>
    /// Smooth sphere: h≡0, the normal is radial from the body centre. Exact by
    /// construction (not an approximation): the relative position already lies on the
    /// sphere. No poles or trigonometry — just normalization.
    /// </summary>
    public sealed class SphericalTerrain : ITerrainModel
    {
        public double GetHeightMeters(OrbitingBody body, double latitudeRadians, double longitudeRadians)
        {
            return 0d;
        }

        public double GetRawHeightMeters(OrbitingBody body, double latitudeRadians, double longitudeRadians)
        {
            return 0d;
        }

        public double GetSeaLevelMeters()
        {
            return double.NegativeInfinity;
        }

        public Vector3d GetOutwardNormal(OrbitingBody body, Vector3d relativePosition, double timeSeconds)
        {
            return relativePosition.Normalized;
        }
    }
}
