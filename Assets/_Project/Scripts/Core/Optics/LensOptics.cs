using System;

namespace Galilego.Core
{
    /// <summary>
    /// Optics of the observer's camera: the part of a lens flare that follows from
    /// the geometry and photometry of a real lens instead of from taste. Core, double,
    /// no UnityEngine, so every number here is testable without the Editor.
    ///
    /// All three artefacts this models live INSIDE the optics, between the scene and
    /// the sensor, and therefore exist in vacuum as well:
    ///
    ///  * veiling glare — a fraction of the flux collected by the entrance pupil is
    ///    scattered by the glass, its coatings and the sensor cover glass and lands
    ///    far from the image of the source; the glare spread function is a Moffat
    ///    profile and <see cref="VeilPeakRadiance"/> normalises it so the scattered
    ///    flux is exactly <c>scatterFraction</c> of the source flux;
    ///  * aperture ghosts — images of the iris formed by light that reflected twice
    ///    between two lens surfaces; they sit on the line through the optical centre
    ///    and the source, defocused to the shape of the aperture, and their radiance
    ///    is the reflectance product times the source radiance diluted by the area
    ///    ratio (a ghost much larger than the Sun is much dimmer than it);
    ///  * the diffraction star of the iris blades, whose angular scale is λ/D.
    ///
    /// Angles are in radians. Radiance is in the same scene units as
    /// <see cref="StarPhysics"/>: a white Lambertian surface under E_ref has
    /// radiance 1. The star disc is rendered through a logarithmic compression
    /// (V'), so the optics here are expressed relative to the RENDERED disc
    /// radiance, not to σT⁴/E_ref: a lens does not see the difference between a
    /// compressed buffer and a real photometer.
    /// </summary>
    public static class LensOptics
    {
        /// <summary>Photopic peak of the CIE 1931 observer, m.</summary>
        public const double PhotopicWavelengthMeters = 555e-9;

        /// <summary>Diameter of the entrance pupil, D = f/N (m).</summary>
        public static double ApertureDiameterMeters(double focalLengthMm, double fNumber)
        {
            if (!(focalLengthMm > 0d) || !(fNumber > 0d))
            {
                return 0d;
            }

            return (focalLengthMm * 1e-3d) / fNumber;
        }

        /// <summary>
        /// Veiling glare of the HUMAN EYE at angle <paramref name="thetaRad"/>.
        ///
        /// The eye's glare spread function is normalised to the source's RADIANCE,
        /// not to its flux: what the observer sees around a resolved source is a
        /// fraction of how bright the source itself looks, which is why the sky
        /// next to the Sun is washed out while the rest of it stays blue. The
        /// intraocular stray light of a healthy young eye is ≈10 % of the source
        /// at 1°, ≈1 % at 10° (van den Berg); <paramref name="centreFraction"/> is
        /// that 1° value. It is far stronger and wider than a clean camera lens,
        /// and it is what a living observer actually gets instead of a lens flare.
        /// </summary>
        public static double EyeGlareRadiance(
            double sourceRadiance,
            double centreFraction,
            double thetaRad,
            double theta0Rad,
            double beta)
        {
            if (!(sourceRadiance > 0d) || !(centreFraction > 0d) || !(theta0Rad > 0d) || !(beta > 0d))
            {
                return 0d;
            }

            double ratio = thetaRad / theta0Rad;
            return sourceRadiance * centreFraction
                * Math.Pow(1d + (ratio * ratio), -beta);
        }

        /// <summary>
        /// How much of a source's radiance reaches the eye as glare when the source
        /// is SMALLER than the eye's point-spread core. An extended source is
        /// scattered in proportion to its surface brightness (factor 1); a point
        /// source only delivers its flux, so the veil scales with its solid angle:
        /// (α/α_psf)². The Sun at 0.37 AU (α = 0.73°) is fully resolved; the same
        /// star at 5 AU (α = 0.05°) is down to ~0.3.
        /// </summary>
        public static double PointSourceFactor(
            double sourceAngularRadiusRad,
            double pointSourceScaleRad)
        {
            if (!(sourceAngularRadiusRad > 0d) || !(pointSourceScaleRad > 0d))
            {
                return 0d;
            }

            double ratio = Math.Min(1d, sourceAngularRadiusRad / pointSourceScaleRad);
            return ratio * ratio;
        }

        /// <summary>Hermite smoothstep (Core has no Unity Mathf).</summary>
        public static double Smoothstep(double edge0, double edge1, double x)
        {
            if (!(edge1 > edge0))
            {
                return x >= edge1 ? 1d : 0d;
            }

            double t = (x - edge0) / (edge1 - edge0);
            if (t <= 0d)
            {
                return 0d;
            }

            if (t >= 1d)
            {
                return 1d;
            }

            return t * t * (3d - (2d * t));
        }

        /// <summary>
        /// Angular scale of the aperture diffraction pattern, λ/D (rad): the width
        /// of one diffraction spike on the sensor. It is the reason a real sun does
        /// not show a star at f/2.8 — 0.005° is far below one pixel — and why a
        /// stopped-down lens does.
        /// </summary>
        public static double DiffractionAngleRad(
            double wavelengthMeters,
            double focalLengthMm,
            double fNumber)
        {
            double diameter = ApertureDiameterMeters(focalLengthMm, fNumber);
            if (!(diameter > 0d) || !(wavelengthMeters > 0d))
            {
                return 0d;
            }

            return wavelengthMeters / diameter;
        }

        /// <summary>
        /// Relative illumination of the entrance pupil at a field angle:
        /// cos⁴ θ = 1/(1 + tan²θ)². The pupil is seen at a slant and farther away
        /// off axis, so the whole flare — veil, ghosts, star — fades toward the
        /// frame edge. θ is the angle from the optical axis.
        /// </summary>
        public static double CameraVignetting(double tanFieldAngle)
        {
            double s = tanFieldAngle * tanFieldAngle;
            double k = 1d + s;
            return 1d / (k * k);
        }

        /// <summary>Peak-normalised glare spread function: 1 at the centre.</summary>
        public static double VeilProfile(double thetaRad, double theta0Rad, double beta)
        {
            if (!(theta0Rad > 0d) || !(beta > 1d))
            {
                return 0d;
            }

            double ratio = thetaRad / theta0Rad;
            return Math.Pow(1d + (ratio * ratio), -beta);
        }

        /// <summary>
        /// Radiance of the veiling glare at angle <paramref name="thetaRad"/> from
        /// the source centre.
        ///
        /// The source has radiance <paramref name="sourceRadiance"/> and angular
        /// radius <paramref name="sourceAngularRadiusRad"/>, so the optics collect
        /// the flux L·Ω with Ω = π α². <paramref name="scatterFraction"/> of it is
        /// redistributed by the Moffat profile
        /// (β−1)/(π θ0²)·(1+θ²/θ0²)^(−β), which integrates to 1 over the image
        /// plane; the peak is therefore k·L·(α/θ0)²·(β−1). 1–3 % is a clean lens
        /// (the photographic GSF), 5–15 % a scratched visor or a dirty front element.
        /// </summary>
        public static double VeilPeakRadiance(
            double sourceRadiance,
            double sourceAngularRadiusRad,
            double scatterFraction,
            double theta0Rad,
            double beta)
        {
            if (!(sourceRadiance > 0d)
                || !(sourceAngularRadiusRad > 0d)
                || !(scatterFraction > 0d)
                || !(theta0Rad > 0d)
                || !(beta > 1d))
            {
                return 0d;
            }

            double area = sourceAngularRadiusRad / theta0Rad;
            return scatterFraction * sourceRadiance * area * area * (beta - 1d);
        }

        /// <summary>Veiling glare radiance at one angle (peak from the function above).</summary>
        public static double VeilRadiance(
            double sourceRadiance,
            double sourceAngularRadiusRad,
            double scatterFraction,
            double thetaRad,
            double theta0Rad,
            double beta)
        {
            return VeilPeakRadiance(
                sourceRadiance, sourceAngularRadiusRad, scatterFraction, theta0Rad, beta)
                * VeilProfile(thetaRad, theta0Rad, beta);
        }

        /// <summary>
        /// Radiance of one aperture ghost, formed by two reflections with product
        /// <paramref name="reflectance"/> (R₁·R₂; 4·10⁻² per uncoated air–glass
        /// surface, 1·10⁻³…1·10⁻² per anti-reflection coated one).
        ///
        /// A ghost is an out-of-focus image of the iris: the source flux is spread
        /// over the aperture image of angular radius
        /// <paramref name="ghostAngularRadiusRad"/>. For a ghost larger than the Sun
        /// that dilutes the radiance by (α/γ)²; a ghost smaller than the Sun is
        /// overfilled and keeps the full reflectance (ratio clamped to 1).
        /// </summary>
        public static double GhostRadiance(
            double sourceRadiance,
            double sourceAngularRadiusRad,
            double ghostAngularRadiusRad,
            double reflectance)
        {
            if (!(sourceRadiance > 0d) || !(ghostAngularRadiusRad > 0d) || !(reflectance > 0d)
                || !(sourceAngularRadiusRad > 0d))
            {
                return 0d;
            }

            double ratio = Math.Min(1d, sourceAngularRadiusRad / ghostAngularRadiusRad);
            return reflectance * sourceRadiance * ratio * ratio;
        }

        /// <summary>
        /// NDC position of a ghost. Every ghost is a reflection through the optical
        /// axis, so on the sensor it appears on the opposite side of the frame
        /// centre, at <paramref name="reflection"/> times the source's distance:
        /// g = −k·s. k ≈ 0.3…1.5 for a real lens; k = 1 is the exact mirror image.
        /// </summary>
        public static void GhostCenter(
            double sourceNdcX,
            double sourceNdcY,
            double reflection,
            out double ghostNdcX,
            out double ghostNdcY)
        {
            ghostNdcX = -reflection * sourceNdcX;
            ghostNdcY = -reflection * sourceNdcY;
        }

        /// <summary>
        /// Number of diffraction SPIKE directions of an iris with N blades. A
        /// polygon with an even number of sides has N-fold symmetry, and a spike is
        /// one of a pair of opposite rays, so even N gives N/2 lines (N rays) and
        /// odd N gives N lines (2N rays). Blades &lt; 3 is a round iris: no star.
        /// </summary>
        public static int DiffractionLineCount(int apertureBlades)
        {
            if (apertureBlades < 3)
            {
                return 0;
            }

            return (apertureBlades % 2) == 0 ? apertureBlades / 2 : apertureBlades;
        }

        /// <summary>
        /// Angular width a spike must be drawn with, so that it lands on the raster:
        /// the physical λ/D, but never thinner than <paramref name="minimumPixelAngleRad"/>
        /// (a sub-pixel line otherwise aliases into a dotted mess).
        /// </summary>
        public static double SpikeDrawWidthRad(
            double physicalWidthRad,
            double minimumPixelAngleRad)
        {
            if (!(physicalWidthRad > 0d) || !(minimumPixelAngleRad > 0d))
            {
                return 0d;
            }

            return Math.Max(physicalWidthRad, minimumPixelAngleRad);
        }
    }
}
