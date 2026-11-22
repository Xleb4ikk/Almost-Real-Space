using System;

namespace Galilego.Core
{
    /// <summary>
    /// Reflected-light physics of an airless moon — pure C#, no UnityEngine, so the
    /// fast xUnit suite and the EditMode runner see the same numbers as the game.
    ///
    /// The chain is deliberately one-directional:
    ///   geometry (phase angle) → lunar phase law → moon/sun irradiance ratio →
    ///   scene intensity.
    /// Every step is physical except the last: a moonlit landscape is ~2.9·10⁻⁶ of
    /// daylight, which is invisible on a display whose exposure is calibrated for
    /// day. <see cref="SceneIntensity"/> applies the eye's dark-adaptation gain in
    /// EV as one explicit, documented number instead of hiding it in a hand-tuned
    /// light intensity. All DYNAMICS (phase, distance, eclipse, horizon) stay
    /// physical and are computed before that gain.
    ///
    /// Phase law: Allen's empirical visual magnitude of the Moon,
    /// m(α) = −12.73 + 0.026·α + 4·10⁻⁹·α⁴ (α in degrees, normalised to m(0)),
    /// which reproduces the strong opposition surge — the quarter moon is ~1/11 of
    /// the full moon, not the 1/π of a Lambert sphere. The α⁴ term is cut smoothly
    /// to zero between 170° and 180°: the empirical fit is not valid at new moon
    /// where the true flux is zero.
    /// </summary>
    public static class MoonlightPhysics
    {
        /// <summary>Geometric albedo of the lunar regolith (real Moon: 0.136).</summary>
        public const double MoonGeometricAlbedo = 0.136d;

        /// <summary>Photopic luminous efficacy used to turn W/m² into lux (683 lm/W at 555 nm).</summary>
        public const double LuminousEfficacyLmPerW = 683d;

        /// <summary>Angle at the moon between the star and the observer, radians (0 = full, π = new).</summary>
        public static double PhaseAngleRad(Vector3d moonPosition, Vector3d starPosition, Vector3d observerPosition)
        {
            Vector3d toStar = starPosition - moonPosition;
            Vector3d toObserver = observerPosition - moonPosition;
            double starDistance = toStar.Magnitude;
            double observerDistance = toObserver.Magnitude;
            if (!(starDistance > 0d) || !(observerDistance > 0d))
            {
                return 0d;
            }

            double cosine = Vector3d.Dot(toStar, toObserver) / (starDistance * observerDistance);
            return Math.Acos(Clamp(cosine, -1d, 1d));
        }

        /// <summary>Fraction of the lunar disc that is lit, (1 + cos α)/2. Diagnostic only.</summary>
        public static double IlluminatedFraction(double phaseAngleRad)
        {
            return 0.5d * (1d + Math.Cos(Clamp(phaseAngleRad, 0d, Math.PI)));
        }

        /// <summary>
        /// Apparent brightness of the moon relative to full phase, 0..1. Empirical
        /// lunar phase law: 1 at α=0, ~0.091 at quarter (α=90°), 0 at new moon.
        /// </summary>
        public static double PhaseFunction(double phaseAngleRad)
        {
            double alphaDegrees = Clamp(phaseAngleRad, 0d, Math.PI) * (180d / Math.PI);
            if (!(alphaDegrees > 0d))
            {
                return 1d;
            }

            if (alphaDegrees >= 180d)
            {
                return 0d;
            }

            double alpha2 = alphaDegrees * alphaDegrees;
            double magnitude = (0.026d * alphaDegrees) + (4e-9d * alpha2 * alpha2);
            double curve = Math.Pow(10d, -0.4d * magnitude);

            // The empirical fit has a spurious tail at new moon; the true flux is 0.
            double newMoonCutoff = 1d - Smoothstep(170d, 180d, alphaDegrees);
            return Clamp(curve * newMoonCutoff, 0d, 1d);
        }

        /// <summary>
        /// Moon irradiance at the observer / star irradiance at the observer.
        /// A full disc reflects p·E·(R/d)² (geometric albedo p, radius R, distance d);
        /// the phase law folds in the illuminated fraction and the opposition surge.
        /// eclipseVisibility (0..1) is the fraction of the Sun's disc still visible
        /// from the moon — Terra's shadow during a lunar eclipse.
        /// </summary>
        public static double IrradianceRatioToSun(
            double geometricAlbedo,
            double moonRadiusMeters,
            double distanceObserverToMoonMeters,
            double phaseAngleRad,
            double eclipseVisibility = 1d)
        {
            if (!(geometricAlbedo > 0d) || !(moonRadiusMeters > 0d) || !(distanceObserverToMoonMeters > 0d))
            {
                return 0d;
            }

            double sizeRatio = moonRadiusMeters / distanceObserverToMoonMeters;
            double fullMoonRatio = geometricAlbedo * sizeRatio * sizeRatio;
            double eclipse = Clamp(eclipseVisibility, 0d, 1d);
            return fullMoonRatio * PhaseFunction(phaseAngleRad) * eclipse;
        }

        /// <summary>
        /// Physical ratio → scene units. gainEv is the simulated eye's dark
        /// adaptation: 16 EV turns the game's full moon into ~0.19 of the noon sun,
        /// a value a tonemapped night can actually show; 0 is the physical night.
        /// </summary>
        public static double SceneIntensity(double irradianceRatioToSun, double gainEv)
        {
            if (!(irradianceRatioToSun > 0d))
            {
                return 0d;
            }

            return irradianceRatioToSun * Math.Pow(2d, gainEv);
        }

        /// <summary>
        /// Physical illuminance of the moon on a surface facing it, lux. The ratio
        /// is relative to the star's total irradiance at the observer, so a brighter
        /// star scales the moon and the sun together (the ratio itself does not
        /// depend on the star).
        /// </summary>
        public static double IlluminanceLux(double irradianceRatioToSun, double starIrradianceAtObserverWm2)
        {
            if (!(irradianceRatioToSun > 0d) || !(starIrradianceAtObserverWm2 > 0d))
            {
                return 0d;
            }

            return irradianceRatioToSun * starIrradianceAtObserverWm2
                * StarPhysics.EtaSun * LuminousEfficacyLmPerW;
        }

        /// <summary>
        /// Fraction of the Sun's disc visible from the moon around one spherical
        /// occluder (the parent planet). 0 = total lunar eclipse (umbra),
        /// 1 = no eclipse; the penumbra falls out of the disc-overlap geometry.
        /// </summary>
        public static double LunarEclipseVisibility(
            Vector3d moonPosition,
            Vector3d occluderPosition,
            double occluderRadiusMeters,
            Vector3d starPosition,
            double starRadiusMeters)
        {
            Vector3d toStar = starPosition - moonPosition;
            Vector3d toOccluder = occluderPosition - moonPosition;
            double starDistance = toStar.Magnitude;
            double occluderDistance = toOccluder.Magnitude;
            if (!(starDistance > 0d) || !(occluderDistance > 0d)
                || !(starRadiusMeters > 0d) || !(occluderRadiusMeters > 0d))
            {
                return 1d;
            }

            double starAngularRadius = Math.Asin(Clamp(starRadiusMeters / starDistance, 0d, 1d));
            double occluderAngularRadius = Math.Asin(Clamp(occluderRadiusMeters / occluderDistance, 0d, 1d));
            double separation = Math.Acos(Clamp(
                Vector3d.Dot(toStar, toOccluder) / (starDistance * occluderDistance), -1d, 1d));

            return StarPhysics.EclipseVisibleFraction(starAngularRadius, occluderAngularRadius, separation);
        }

        private static double Clamp(double value, double min, double max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        private static double Smoothstep(double edge0, double edge1, double value)
        {
            if (edge1 <= edge0)
            {
                return value < edge0 ? 0d : 1d;
            }

            double t = Clamp((value - edge0) / (edge1 - edge0), 0d, 1d);
            return t * t * (3d - (2d * t));
        }
    }
}
