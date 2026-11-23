using System;

namespace Galilego.Core
{
    /// <summary>
    /// Star-disc occlusion geometry: umbra, penumbra and annular eclipses for
    /// spherical bodies. Distances are metres, angles radians. The actual overlap
    /// area lives in <see cref="StarPhysics.EclipseVisibleFraction"/>; this class
    /// is the bridge from world positions to that disc geometry, so the star
    /// renderer, the sky, the far discs and the moon light all use one rule.
    ///
    /// A body farther than the star never occludes it (a moon behind the Sun is
    /// not an eclipse), and a zero/negative radius is "no occluder".
    /// </summary>
    public static class EclipseGeometry
    {
        /// <summary>Angular radius (rad) of a sphere of radius R at distance d. 90° on the surface.</summary>
        public static double AngularRadius(double radiusMeters, double distanceMeters)
        {
            if (!(radiusMeters > 0d) || !(distanceMeters > 0d))
            {
                return 0d;
            }

            return Math.Asin(StarPhysics.Clamp(radiusMeters / distanceMeters, 0d, 1d));
        }

        /// <summary>
        /// True when the occluder's disc is at least as large as the body's, both
        /// seen from the same distance. A body behind a larger one can be covered as
        /// a whole; a small moon transiting a planet only shadows a spot, so its
        /// centre-factor must not be used as the planet's whole-disc brightness.
        /// </summary>
        public static bool CoversBody(double bodyRadiusMeters, double occluderRadiusMeters, double distanceMeters)
        {
            if (!(distanceMeters > 0d))
            {
                return false;
            }

            return AngularRadius(occluderRadiusMeters, distanceMeters)
                >= AngularRadius(bodyRadiusMeters, distanceMeters);
        }

        /// <summary>
        /// Fraction of the star's disc still visible from <paramref name="observer"/>
        /// around one spherical occluder. 1 = clear, 0 = the star is fully hidden.
        /// Penumbra and annular geometry fall out of the disc-overlap area, so a
        /// smaller occluder centred on the disc leaves an annulus, not a black disc.
        /// </summary>
        public static double VisibleFraction(
            Vector3d observer,
            Vector3d starPosition,
            double starRadiusMeters,
            Vector3d occluderPosition,
            double occluderRadiusMeters)
        {
            Vector3d toStar = starPosition - observer;
            Vector3d toOccluder = occluderPosition - observer;
            double starDistance = toStar.Magnitude;
            double occluderDistance = toOccluder.Magnitude;

            if (!(starDistance > 0d) || !(occluderDistance > 0d)
                || !(starRadiusMeters > 0d) || !(occluderRadiusMeters > 0d)
                || occluderDistance >= starDistance)
            {
                return 1d;
            }

            double starAngularRadius = AngularRadius(starRadiusMeters, starDistance);
            double occluderAngularRadius = AngularRadius(occluderRadiusMeters, occluderDistance);
            double separation = Math.Acos(StarPhysics.Clamp(
                Vector3d.Dot(toStar, toOccluder) / (starDistance * occluderDistance),
                -1d,
                1d));

            return StarPhysics.EclipseVisibleFraction(
                starAngularRadius, occluderAngularRadius, separation);
        }
    }
}
