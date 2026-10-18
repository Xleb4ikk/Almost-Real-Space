using System;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Single source of physical atmosphere coefficients for CPU and GPU.
    ///
    /// Values are real Rayleigh/Mie scattering and ozone absorption coefficients
    /// (Hillaire, "A Scalable and Production Ready Sky and Atmosphere Rendering
    /// Technique", EGSR 2020; the same numbers are used by UE SkyAtmosphere and
    /// HDRP Physically Based Sky).
    ///
    /// Altitude profiles: exponentials with separate scale heights for Rayleigh
    /// and Mie; ozone — a triangular layer around 25 km.
    ///
    /// Also generates on the CPU the two LUTs the shader only samples
    /// (no secondary march to the Sun in the shader):
    ///   * Transmittance LUT — transmittance of a point to the Sun from (height, cos θ);
    ///   * Multi-Scattering LUT — infinite multi-scattering approximation after
    ///     Hillaire (equations 9-10); without it the horizon burns out to white
    ///     and the zenith stays dim.
    ///
    /// The module is pure (double + Vector3d only) — testable standalone.
    /// </summary>
    public static class AtmosphereOptics
    {
        // Rayleigh scattering at sea level (1/m), channels R~680 / G~550 / B~440 nm.
        public static readonly Vector3d EarthRayleighScattering = new Vector3d(5.802e-6, 13.558e-6, 33.1e-6);

        // Mie scattering at sea level (1/m), nearly independent of wavelength.
        public const double EarthMieScattering = 3.996e-6;

        // Ozone absorption (1/m) at the layer peak.
        public static readonly Vector3d EarthOzoneAbsorption = new Vector3d(0.650e-6, 1.881e-6, 0.085e-6);

        private static readonly Vector3d One = new Vector3d(1d, 1d, 1d);

        public const double EarthSeaLevelDensityKgPerCubicMeter = 1.225;
        public const double EarthRayleighScaleHeightMeters = 8000d;
        public const double EarthMieScaleHeightMeters = 1200d;
        public const double EarthOzoneCenterAltitudeMeters = 25000d;
        public const double EarthOzoneHalfWidthMeters = 15000d;
        public const double EarthGroundAlbedo = 0.1d;

        /// <summary>Isotropic phase (1/4π): anisotropy is lost in multi-scattering.</summary>
        public const double IsotropicPhase = 1d / (4d * Math.PI);

        /// <summary>Coefficient and altitude-parameter set of one body.</summary>
        public struct Coefficients
        {
            public Vector3d RayleighScattering;   // 1/m at sea level
            public Vector3d MieScattering;         // 1/m at sea level
            public Vector3d OzoneAbsorption;       // 1/m at the layer peak
            public double RayleighScaleHeight;     // m
            public double MieScaleHeight;          // m
            public double OzoneCenterAltitude;     // m
            public double OzoneHalfWidth;          // m
            public double MieAnisotropy;           // -1..1
            public double GroundAlbedo;            // 0..1
            public bool OzoneEnabled;
        }

        /// <summary>
        /// Builds a body's coefficients from the profile's physical fields.
        /// Sea-level density linearly scales all β (rarer atmosphere — less
        /// scattering); heights fall back to Earth values when the profile has 0.
        /// </summary>
        public static Coefficients FromProfile(
            double seaLevelDensityKgPerCubicMeter,
            double rayleighScaleHeightMeters,
            double mieScaleHeightMeters,
            double mieAnisotropy,
            bool ozoneEnabled,
            double aerosolScale = 1d)
        {
            double densityScale = seaLevelDensityKgPerCubicMeter > 0d
                ? seaLevelDensityKgPerCubicMeter / EarthSeaLevelDensityKgPerCubicMeter
                : 0d;
            // Turbidity: surface aerosol amount (not the altitude shape — Hm sets that).
            double mieScale = densityScale * (aerosolScale > 0d ? aerosolScale : 0d);

            Coefficients c;
            c.RayleighScattering = EarthRayleighScattering * densityScale;
            c.MieScattering = new Vector3d(EarthMieScattering * mieScale, EarthMieScattering * mieScale, EarthMieScattering * mieScale);
            c.OzoneAbsorption = EarthOzoneAbsorption * densityScale;
            c.RayleighScaleHeight = rayleighScaleHeightMeters > 0d ? rayleighScaleHeightMeters : EarthRayleighScaleHeightMeters;
            c.MieScaleHeight = mieScaleHeightMeters > 0d ? mieScaleHeightMeters : EarthMieScaleHeightMeters;
            c.OzoneCenterAltitude = EarthOzoneCenterAltitudeMeters;
            c.OzoneHalfWidth = EarthOzoneHalfWidthMeters;
            c.MieAnisotropy = Clamp(mieAnisotropy, -0.9d, 0.9d);
            c.GroundAlbedo = EarthGroundAlbedo;
            c.OzoneEnabled = ozoneEnabled;
            return c;
        }

        /// <summary>Triangular altitude profile of the ozone layer (as in HDRP PBS), 0..1.</summary>
        public static double OzoneDensity(in Coefficients c, double height)
        {
            if (!c.OzoneEnabled || c.OzoneHalfWidth <= 0d)
            {
                return 0d;
            }

            return Saturate(1d - Math.Abs((height - c.OzoneCenterAltitude) / c.OzoneHalfWidth));
        }

        /// <summary>Total extinction (Rayleigh/Mie scattering + ozone absorption), 1/m.</summary>
        public static Vector3d Extinction(in Coefficients c, double height)
        {
            double dR = Math.Exp(-height / c.RayleighScaleHeight);
            double dM = Math.Exp(-height / c.MieScaleHeight);
            double dO = OzoneDensity(c, height);

            return (c.RayleighScattering * dR) + (c.MieScattering * dM) + (c.OzoneAbsorption * dO);
        }

        /// <summary>Scattering (without absorption) — the multi-scattering source, 1/m.</summary>
        public static Vector3d Scattering(in Coefficients c, double height)
        {
            double dR = Math.Exp(-height / c.RayleighScaleHeight);
            double dM = Math.Exp(-height / c.MieScaleHeight);

            return (c.RayleighScattering * dR) + (c.MieScattering * dM);
        }

        /// <summary>
        /// Intersection of a ray (point at radius r, zenith cosine cosChi) with a
        /// sphere of the given radius. Numerically stable form via difference of
        /// squares: t = r·(−cosChi ± √d), d = (radius/r)² − (1 − cosChi²).
        /// </summary>
        public static bool IntersectSphere(double radius, double r, double cosChi, out double t0, out double t1)
        {
            double ratio = radius / r;
            double d = (ratio * ratio) - Saturate(1d - (cosChi * cosChi));
            if (d < 0d)
            {
                t0 = 0d;
                t1 = 0d;
                return false;
            }

            double s = Math.Sqrt(d);
            t0 = r * (-cosChi - s);
            t1 = r * (-cosChi + s);
            return true;
        }

        /// <summary>Cosine of the visible horizon for an observer at radius r.</summary>
        public static double CosineOfHorizon(double planetRadius, double r)
        {
            double sinHor = planetRadius / r;
            return -Math.Sqrt(Saturate(1d - (sinHor * sinHor)));
        }

        /// <summary>
        /// Horizon smoothing band in zenith cosine (~±0.46°): the Sun has a finite
        /// angular radius (~0.26°) plus refraction at the horizon (~0.5°), so
        /// direct light fades over about half a degree of sunset rather than
        /// stepping. Deep below the horizon it is exactly 0.
        /// </summary>
        public const double HorizonSoftening = 0.008d;

        /// <summary>
        /// Transmittance to the Sun for a point at radius r and zenith cosine cosTheta.
        /// Below the geometric horizon the ray hits the planet → 0, but with the soft
        /// HorizonSoftening band (finite disc + refraction), otherwise sunset pops the
        /// disc/light within one frame. Numerically integrates the exponential density
        /// (consistent with the GPU model).
        /// </summary>
        public static Vector3d TransmittanceToSpace(
            in Coefficients c, double planetRadius, double atmosphereRadius, double r, double cosTheta, int steps = 8)
        {
            double cosHor = CosineOfHorizon(planetRadius, Math.Max(r, planetRadius));
            double horizonFade = Saturate((cosTheta - (cosHor - HorizonSoftening)) / (2d * HorizonSoftening));
            horizonFade = horizonFade * horizonFade * (3d - (2d * horizonFade));
            if (horizonFade <= 0d)
            {
                return Vector3d.Zero;
            }

            if (!IntersectSphere(atmosphereRadius, r, cosTheta, out _, out double t1))
            {
                return One;
            }

            double tExit = Math.Max(t1, 0d);
            if (tExit <= 0d)
            {
                return One;
            }

            double ds = tExit / steps;
            Vector3d tau = Vector3d.Zero;
            for (int i = 0; i < steps; i++)
            {
                double t = (i + 0.5d) * ds;
                double pr = Math.Sqrt((r * r) + (2d * r * cosTheta * t) + (t * t));
                double height = Math.Max(0d, pr - planetRadius);
                tau += Extinction(c, height) * ds;
            }

            return Exp(-tau) * horizonFade;
        }

        /// <summary>
        /// UV index flipped for the GPU (linear). Consistent with the shader:
        /// uv = (cosTheta·0.5 + 0.5, height / atmosphereDepth).
        /// </summary>
        public static double UvCosTheta(double cosTheta) => Saturate(cosTheta * 0.5d + 0.5d);

        public static double UvHeight(double height, double atmosphereDepth) =>
            atmosphereDepth > 0d ? Saturate(height / atmosphereDepth) : 0d;

        /// <summary>Flat LUT (RGB channels), row-major: y * Width + x.</summary>
        public struct Lut
        {
            public int Width;
            public int Height;
            public Vector3d[] Pixels;
        }

        /// <summary>
        /// Transmittance LUT: X — cos θ (low sun → up), Y — altitude.
        /// Below the horizon 0 is stored (the Sun is blocked by the planet).
        /// </summary>
        public static Lut BuildTransmittanceLut(
            in Coefficients c, double planetRadius, double atmosphereRadius, int size = 64)
        {
            size = Math.Max(2, size);
            double depth = Math.Max(1d, atmosphereRadius - planetRadius);

            var lut = new Lut { Width = size, Height = size, Pixels = new Vector3d[size * size] };
            for (int y = 0; y < size; y++)
            {
                double height = depth * (y + 0.5d) / size;
                double r = planetRadius + height;
                for (int x = 0; x < size; x++)
                {
                    double cosTheta = ((x + 0.5d) / size) * 2d - 1d;
                    lut.Pixels[(y * size) + x] = TransmittanceToSpace(c, planetRadius, atmosphereRadius, r, cosTheta);
                }
            }

            return lut;
        }

        /// <summary>
        /// Multi-Scattering LUT — infinite multi-scattering approximation
        /// (Hillaire, EGSR 2020, eq. 9-10): averages single scattering and planet
        /// light over the sphere, accumulates the multi-scattering "leak", then
        /// F_ms = 1/(1 − radianceMS), MS = radiance · F_ms.
        /// Map: uv = (cos θ·0.5 + 0.5, height / atmosphereDepth).
        /// </summary>
        public static Lut BuildMultiScatteringLut(
            in Coefficients c, double planetRadius, double atmosphereRadius, int size = 32, int sampleCount = 64)
        {
            size = Math.Max(2, size);
            sampleCount = Math.Max(4, sampleCount);
            double depth = Math.Max(1d, atmosphereRadius - planetRadius);

            var lut = new Lut { Width = size, Height = size, Pixels = new Vector3d[size * size] };
            double dS = 1d / sampleCount;

            for (int y = 0; y < size; y++)
            {
                double height = depth * (y + 0.5d) / size;
                double r = planetRadius + height;

                for (int x = 0; x < size; x++)
                {
                    double cosChi = ((x + 0.5d) / size) * 2d - 1d;
                    double sinChi = Math.Sqrt(Saturate(1d - (cosChi * cosChi)));
                    var origin = new Vector3d(0d, r, 0d);
                    var sunDir = new Vector3d(0d, cosChi, sinChi);

                    Vector3d radiance = Vector3d.Zero;
                    Vector3d radianceMs = Vector3d.Zero;

                    for (int s = 0; s < sampleCount; s++)
                    {
                        Vector3d viewDir = UniformSphere(s, sampleCount);
                        IntegrateSkyRay(c, planetRadius, atmosphereRadius, origin, viewDir, sunDir,
                            out Vector3d skyColor, out Vector3d multiScattering);
                        radiance += skyColor;
                        radianceMs += multiScattering;
                    }

                    radiance *= dS;
                    radianceMs *= dS;

                    Vector3d fMs = new Vector3d(
                        1d / Math.Max(1e-3d, 1d - radianceMs.X),
                        1d / Math.Max(1e-3d, 1d - radianceMs.Y),
                        1d / Math.Max(1e-3d, 1d - radianceMs.Z));

                    lut.Pixels[(y * size) + x] = new Vector3d(
                        radiance.X * fMs.X, radiance.Y * fMs.Y, radiance.Z * fMs.Z);
                }
            }

            return lut;
        }

        /// <summary>
        /// Integrates along a ray from origin (in the planet-centric frame) to the
        /// atmosphere exit or the surface. Returns the sky color and accumulated
        /// multi-scattering (for the LUT).
        /// </summary>
        private static void IntegrateSkyRay(
            in Coefficients c, double planetRadius, double atmosphereRadius,
            Vector3d origin, Vector3d viewDir, Vector3d sunDir,
            out Vector3d skyColor, out Vector3d multiScattering)
        {
            double r = origin.Magnitude;
            double cosView = viewDir.Y; // origin = (0, r, 0), N = +Y
            double cosHorizon = CosineOfHorizon(planetRadius, r);
            bool hitsGround = cosView < cosHorizon;

            double tExit;
            if (hitsGround)
            {
                if (!IntersectSphere(planetRadius, r, cosView, out double g0, out _))
                {
                    skyColor = Vector3d.Zero;
                    multiScattering = Vector3d.Zero;
                    return;
                }

                tExit = Math.Max(g0, 0d);
            }
            else
            {
                if (!IntersectSphere(atmosphereRadius, r, cosView, out _, out double a1) || a1 <= 0d)
                {
                    skyColor = Vector3d.Zero;
                    multiScattering = Vector3d.Zero;
                    return;
                }

                tExit = a1;
            }

            if (tExit <= 0d)
            {
                skyColor = Vector3d.Zero;
                multiScattering = Vector3d.Zero;
                return;
            }

            const int steps = 16;
            skyColor = Vector3d.Zero;
            multiScattering = Vector3d.Zero;
            Vector3d transmittance = One;

            for (int i = 0; i < steps; i++)
            {
                double t0 = (i / (double)steps);
                double t1 = ((i + 1d) / steps);
                double s0 = t0 * t0 * tExit;
                double s1 = t1 * t1 * tExit;
                double t = (s0 + s1) * 0.5d;
                double dt = s1 - s0;

                Vector3d p = origin + (viewDir * t);
                double pr = Math.Max(p.Magnitude, planetRadius);
                double h = pr - planetRadius;
                Vector3d n = p / pr;

                Vector3d sigmaE = Extinction(c, h);
                Vector3d scattering = Scattering(c, h);
                Vector3d transSeg = Exp(-sigmaE * dt);

                multiScattering += IntegrateOverSegment(scattering, transSeg, transmittance, sigmaE);

                double cosSun = Vector3d.Dot(n, sunDir);
                Vector3d sunTransmittance = TransmittanceToSpace(c, planetRadius, atmosphereRadius, pr, cosSun);
                Vector3d source = VecMul(sunTransmittance, scattering * IsotropicPhase);
                skyColor += IntegrateOverSegment(source, transSeg, transmittance, sigmaE);

                transmittance = VecMul(transmittance, transSeg);
            }

            if (hitsGround)
            {
                Vector3d p = origin + (viewDir * tExit);
                double pr = Math.Max(p.Magnitude, planetRadius);
                Vector3d n = p / pr;
                double cosSun = Vector3d.Dot(n, sunDir);
                double cosHorizonGround = CosineOfHorizon(planetRadius, pr);
                Vector3d sunIntensity = cosSun >= cosHorizonGround
                    ? TransmittanceToSpace(c, planetRadius, atmosphereRadius, pr, cosSun)
                    : Vector3d.Zero;

                // The planet is treated as grey: albedo/π · max(0, N·L) · T_sun.
                double grdf = c.GroundAlbedo / Math.PI;
                double nl = Saturate(cosSun);
                Vector3d planet = new Vector3d(
                    grdf * nl * sunIntensity.X,
                    grdf * nl * sunIntensity.Y,
                    grdf * nl * sunIntensity.Z);
                skyColor += VecMul(planet, transmittance);
            }
        }

        /// <summary>
        /// Analytic integral of a homogeneous segment (Frostbite/Hillaire):
        /// transmittance · (S − S·transSeg) / sigmaE.
        /// </summary>
        private static Vector3d IntegrateOverSegment(Vector3d source, Vector3d transSeg, Vector3d transmittance, Vector3d sigmaE)
        {
            double sx = (source.X - (source.X * transSeg.X)) / Math.Max(1e-12d, sigmaE.X);
            double sy = (source.Y - (source.Y * transSeg.Y)) / Math.Max(1e-12d, sigmaE.Y);
            double sz = (source.Z - (source.Z * transSeg.Z)) / Math.Max(1e-12d, sigmaE.Z);
            return new Vector3d(
                transmittance.X * sx, transmittance.Y * sy, transmittance.Z * sz);
        }

        /// <summary>Deterministic uniform sphere (Fibonacci spiral).</summary>
        private static Vector3d UniformSphere(int index, int count)
        {
            const double goldenAngle = Math.PI * (3d - 2.2360679774997896964d);
            double y = 1d - (2d * (index + 0.5d) / count);
            double radius = Math.Sqrt(Saturate(1d - (y * y)));
            double phi = goldenAngle * index;
            return new Vector3d(Math.Cos(phi) * radius, y, Math.Sin(phi) * radius);
        }

        /// <summary>
        /// Sky brightness estimate for surface ambient lighting, using the same
        /// single-scatter integrator as the Multi-Scattering LUT. Returns
        /// "dominant hue × mean energy": the magnitude is the cosine-weighted mean
        /// radiance over the upper hemisphere (as irradiance/π), the hue is the mean
        /// color of bright sky areas without cosine weight, so the warm horizon band
        /// at sunset is not buried by zenith blue or by its own ~0 cosine weight.
        /// Color matches the GPU sky: blue by day, warm at sunset, ≈ 0 at night.
        ///
        /// The frame is local: observer at (0, r, 0), zenith +Y, sun in the YZ plane
        /// (cosSunZenith = cosine of the sun's zenith angle). Cheap: directions × 16
        /// march steps (12 directions by default). Above the atmosphere top — exactly
        /// 0 (nothing to scatter).
        /// </summary>
        public static Vector3d SkyAmbientRadiance(
            in Coefficients c, double planetRadius, double atmosphereRadius,
            double observerRadius, double cosSunZenith, int directions = 12)
        {
            if (directions <= 0 || observerRadius >= atmosphereRadius)
            {
                return Vector3d.Zero;
            }

            double r = Math.Max(observerRadius, planetRadius);
            double sinSun = Math.Sqrt(Saturate(1d - (cosSunZenith * cosSunZenith)));
            var origin = new Vector3d(0d, r, 0d);
            var sunDir = new Vector3d(0d, cosSunZenith, sinSun);

            Vector3d energySum = Vector3d.Zero;
            Vector3d hueSum = Vector3d.Zero;
            double energyWeight = 0d;
            double hueWeight = 0d;
            for (int i = 0; i < directions; i++)
            {
                Vector3d dir = HemisphereFibonacci(i, directions);
                IntegrateSkyRay(c, planetRadius, atmosphereRadius, origin, dir, sunDir,
                    out Vector3d skyColor, out _);
                double w = dir.Y;
                double lum = Luminance(skyColor);
                energySum += skyColor * w;
                energyWeight += w;
                // Hue uses luminance without cosine weight: the horizon (weight ≈ 0) would never show otherwise.
                hueSum += skyColor * lum;
                hueWeight += lum;
            }

            if (energyWeight <= 0d || hueWeight <= 0d)
            {
                return Vector3d.Zero;
            }

            double meanLuminance = Luminance(energySum / energyWeight);
            Vector3d dominantHue = hueSum / hueWeight;
            double hueLuminance = Luminance(dominantHue);
            if (meanLuminance <= 0d || hueLuminance <= 0d)
            {
                return Vector3d.Zero;
            }

            return dominantHue * (meanLuminance / hueLuminance);
        }

        private static double Luminance(Vector3d v) =>
            (0.2126d * v.X) + (0.7152d * v.Y) + (0.0722d * v.Z);

        /// <summary>Deterministic uniform upper hemisphere (Fibonacci spiral), y ∈ (0, 1].</summary>
        private static Vector3d HemisphereFibonacci(int index, int count)
        {
            const double goldenAngle = Math.PI * (3d - 2.2360679774997896964d);
            double y = 1d - ((index + 0.5d) / count);
            double radius = Math.Sqrt(Saturate(1d - (y * y)));
            double phi = goldenAngle * index;
            return new Vector3d(Math.Cos(phi) * radius, y, Math.Sin(phi) * radius);
        }

        public static Vector3d Exp(Vector3d v) =>
            new Vector3d(Math.Exp(v.X), Math.Exp(v.Y), Math.Exp(v.Z));

        public static Vector3d VecMul(Vector3d a, Vector3d b) =>
            new Vector3d(a.X * b.X, a.Y * b.Y, a.Z * b.Z);

        public static double Saturate(double v) => v < 0d ? 0d : (v > 1d ? 1d : v);

        public static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);
    }
}
