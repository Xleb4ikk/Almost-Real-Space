using System;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Sky and star physics — pure math (tested on the harness, T88).
    ///
    /// The split matters both physically and numerically:
    ///   * AIRMASS — path length of the solar ray through the atmosphere
    ///     (Kasten–Young). At the horizon it is large but FINITE — that is what
    ///     gives the dim red sunset disc. It must not be zeroed "once the sun
    ///     has set": that would give exp(0)=1 (full transparency) and a
    ///     discontinuity.
    ///   * OCCLUSION — a separate geometric factor: whether the direct ray is
    ///     blocked by the planet body. It accounts for the observer's ALTITUDE
    ///     (geometric horizon dip: the higher the observer, the deeper a star
    ///     must go below the horizon to really be hidden) — ≈ sinEl<0 for a
    ///     ground observer.
    ///
    /// Optical depth = Airmass · ρ(h) · H; transmittance = exp(−τ·β) (Bouguer).
    /// β_rgb ∝ λ⁻⁴ (Rayleigh): blue dies first → red sunset.
    /// </summary>
    public static class SkyPhysics
    {
        /// <summary>Atmospheric density at altitude (0..1), zero above topAltitude.</summary>
        public static double Density(double altitude, double scaleHeight, double topAltitude)
        {
            if (altitude >= topAltitude)
            {
                return 0d;
            }

            double h = altitude < 0d ? 0d : altitude;
            return Math.Exp(-h / Math.Max(1d, scaleHeight));
        }

        /// <summary>
        /// Relative air mass (Kasten–Young). The clamp at the horizon exists
        /// only for numerical stability (≈ −2.9°), not as a physical "sunset":
        /// at the horizon am is finite and large (~38), which is what we need.
        /// </summary>
        public static double Airmass(double sinElevation)
        {
            double s = sinElevation < -0.05d ? -0.05d : sinElevation;
            return 1d / (s + (0.15d * Math.Pow(s + 3.885d, -1.253d)));
        }

        /// <summary>
        /// Distance to the observer's geometric horizon at altitude h above a
        /// sphere of radius R: √(2Rh + h²). Used for far-mountain rendering: the
        /// camera also sees a peak-height point beyond the horizon.
        /// </summary>
        public static double HorizonDistance(double planetRadius, double altitude)
        {
            if (planetRadius <= 0d)
            {
                return 0d;
            }

            double h = altitude < 0d ? 0d : altitude;
            return Math.Sqrt((2d * planetRadius * h) + (h * h));
        }

        /// <summary>
        /// Maximum sight distance: an observer at cameraHeight sees a point at
        /// peakHeight — the sum of their horizons (tangent to both elevations).
        /// Conservative: the terrain between the points is not tested.
        /// </summary>
        public static double MaxSightDistance(double planetRadius, double cameraHeight, double peakHeight)
        {
            return HorizonDistance(planetRadius, cameraHeight) + HorizonDistance(planetRadius, peakHeight);
        }

        /// <summary>Visible horizon dip (rad) for an observer at altitude: acos(R / (R+h)).</summary>
        public static double HorizonDip(double altitude, double planetRadius)
        {
            if (altitude <= 0d || planetRadius <= 0d)
            {
                return 0d;
            }

            double ratio = planetRadius / (planetRadius + altitude);
            return Math.Acos(Clamp(ratio, -1d, 1d));
        }

        /// <summary>
        /// 1 — the direct ray to the star is not blocked by the body; 0 — blocked.
        /// The boundary lies at the horizon dip angle (−dip); smooth transition
        /// of width edge. At the ground dip≈0 → boundary ≈ sinEl=0. With altitude
        /// dip grows → the star is visible even slightly below the local horizon
        /// (as from the stratosphere/space).
        /// </summary>
        public static double SunOcclusion(double sinElevation, double altitude, double planetRadius, double edge = 0.002d)
        {
            double sinLimit = -Math.Sin(HorizonDip(altitude, planetRadius));
            return Smoothstep(sinLimit - edge, sinLimit + edge, sinElevation);
        }

        /// <summary>Per-channel transmittance to the sun; extinctionPerMeter is β. In space (ρ=0) → (1,1,1).</summary>
        public static Vector3d SunTransmittance(double sinElevation, double density, double scaleHeight, Vector3d extinctionPerMeter)
        {
            double opticalDepth = Airmass(sinElevation) * density * Math.Max(1d, scaleHeight);
            return new Vector3d(
                Math.Exp(-opticalDepth * extinctionPerMeter.X),
                Math.Exp(-opticalDepth * extinctionPerMeter.Y),
                Math.Exp(-opticalDepth * extinctionPerMeter.Z));
        }

        /// <summary>
        /// Rayleigh extinction β (1/m) from sea-level density — the real
        /// AtmosphereOptics coefficients, shared with the sky shader.
        /// </summary>
        public static Vector3d ExtinctionPerMeter(double seaLevelDensityKgPerCubicMeter)
        {
            double scale = seaLevelDensityKgPerCubicMeter > 0d
                ? seaLevelDensityKgPerCubicMeter / AtmosphereOptics.EarthSeaLevelDensityKgPerCubicMeter
                : 0d;
            return AtmosphereOptics.EarthRayleighScattering * scale;
        }

        /// <summary>
        /// Transmittance to the Sun with the full model (Rayleigh + Mie + ozone)
        /// and per-component scale heights. Consistent with the GPU shader (same
        /// AtmosphereOptics.Coefficients), so the disc/light color and the sky
        /// color do not diverge at sunset.
        /// </summary>
        public static Vector3d SunTransmittance(double sinElevation, double relativeDensity, in AtmosphereOptics.Coefficients c)
        {
            double am = Airmass(sinElevation);
            double ozoneColumn = c.OzoneEnabled ? c.OzoneHalfWidth : 0d;

            Vector3d tau = (c.RayleighScattering * (c.RayleighScaleHeight * relativeDensity))
                + (c.MieScattering * (c.MieScaleHeight * relativeDensity))
                + (c.OzoneAbsorption * ozoneColumn);
            tau *= am;

            return AtmosphereOptics.Exp(-tau);
        }

        /// <summary>Relative sky luminance (day→twilight→night) × density above the observer.</summary>
        public static double SkyLuminance(double sinElevation, double density, double twilightSin = 0.15d)
        {
            return Smoothstep(-twilightSin, twilightSin, sinElevation) * density;
        }

        /// <summary>Star visibility: 1 in space/at night, →0 in bright daylight at the surface.</summary>
        public static double StarVisibility(double skyLuminance, double k = 6d)
        {
            return Math.Exp(-skyLuminance * k);
        }

        /// <summary>Day factor of direct light: fades in after sunrise, 0 at night.</summary>
        public static double DayFactor(double sinElevation)
        {
            return Smoothstep(0d, 0.1d, sinElevation);
        }

        public static double Clamp(double v, double min, double max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        public static double Smoothstep(double edge0, double edge1, double x)
        {
            if (edge1 <= edge0)
            {
                return x < edge0 ? 0d : 1d;
            }

            double t = Clamp((x - edge0) / (edge1 - edge0), 0d, 1d);
            return t * t * (3d - (2d * t));
        }
    }
}
