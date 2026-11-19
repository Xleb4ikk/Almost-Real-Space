using System;

namespace Galilego.Core
{
    /// <summary>
    /// Input of the stellar model. Pure data: no UnityEngine, no scene objects.
    /// Mass is the only required value; the three overrides are optional and, when
    /// they are set, deliberately break the mass–luminosity relation (the caller
    /// is warned by StarPreset).
    /// </summary>
    public readonly struct StarDefinition
    {
        public readonly double MassSolar;
        public readonly double RadiusSolarOverride;
        public readonly double LuminositySolarOverride;
        public readonly double EffectiveTemperatureKOverride;
        public readonly double Activity01;
        public readonly double GranulationContrastMultiplier;
        public readonly double Adaptation01;

        public StarDefinition(
            double massSolar,
            double radiusSolarOverride = 0d,
            double luminositySolarOverride = 0d,
            double effectiveTemperatureKOverride = 0d,
            double activity01 = 1d,
            double granulationContrastMultiplier = 1d,
            double adaptation01 = 0.25d)
        {
            MassSolar = massSolar;
            RadiusSolarOverride = radiusSolarOverride;
            LuminositySolarOverride = luminositySolarOverride;
            EffectiveTemperatureKOverride = effectiveTemperatureKOverride;
            Activity01 = activity01;
            GranulationContrastMultiplier = granulationContrastMultiplier;
            Adaptation01 = adaptation01;
        }

        public bool HasRadiusOverride => RadiusSolarOverride > 0d;
        public bool HasLuminosityOverride => LuminositySolarOverride > 0d;
        public bool HasTemperatureOverride => EffectiveTemperatureKOverride > 0d;
        public bool HasAnyOverride => HasRadiusOverride || HasLuminosityOverride || HasTemperatureOverride;
    }

    /// <summary>
    /// Everything the renderer needs, derived once from <see cref="StarDefinition"/>.
    /// All values are SI / solar units as documented on the fields. The class is
    /// immutable and independent of Unity: the same state is used by the disc
    /// shader, the Directional Light and the EditMode tests.
    /// </summary>
    public sealed class StarState
    {
        public readonly double MassSolar;
        public readonly double LuminositySolar;
        public readonly double RadiusSolar;
        public readonly double RadiusKm;
        public readonly double EffectiveTemperatureK;
        public readonly double SurfaceGravityMps2;
        public readonly double GranulationCellKm;
        public readonly double GranulationCells;

        /// <summary>Base contrast (0.07, faded to zero at 7000…8500 K) × the artistic multiplier.</summary>
        public readonly double GranulationContrast;

        public readonly double SpotMaxCoverage;
        public readonly double SpotCoverage;
        public readonly double SpotUmbraTemperatureK;
        public readonly double SpotPenumbraTemperatureK;
        public readonly double SpotUmbraR;
        public readonly double SpotUmbraG;
        public readonly double SpotUmbraB;
        public readonly double SpotPenumbraR;
        public readonly double SpotPenumbraG;
        public readonly double SpotPenumbraB;

        /// <summary>Kim et al. 1931 CIE xy chromaticity of the black body.</summary>
        public readonly double ChromaticityX;
        public readonly double ChromaticityY;

        /// <summary>Linear sRGB, negative channels clipped, luminance = 1, eye adaptation applied.</summary>
        public readonly double ColorR;
        public readonly double ColorG;
        public readonly double ColorB;

        public readonly double VisibleEta;
        public readonly double VisibleEtaRatioToSun;

        /// <summary>Physical disc radiance in scene units (before the HDR compression).</summary>
        public readonly double DiscBrightness;

        /// <summary>Compressed disc radiance used by the shader (K = 8 logarithmic knee).</summary>
        public readonly double DiscBrightnessCompressed;

        /// <summary>Limb-darkening coefficients per channel (linear law).</summary>
        public readonly double LimbR;
        public readonly double LimbG;
        public readonly double LimbB;

        public readonly double Activity01;
        public readonly double Adaptation01;
        public readonly double GranulationContrastMultiplier;

        /// <summary>True when radius, luminosity or temperature was overridden.</summary>
        public readonly bool HasOverrides;

        /// <summary>Non-null when HasOverrides: the exact warning the preset logs.</summary>
        public readonly string OverrideWarning;

        public StarState(
            double massSolar,
            double luminositySolar,
            double radiusSolar,
            double effectiveTemperatureK,
            double surfaceGravityMps2,
            double granulationCellKm,
            double granulationCells,
            double granulationContrast,
            double spotMaxCoverage,
            double spotCoverage,
            double spotUmbraTemperatureK,
            double spotPenumbraTemperatureK,
            double spotUmbraR,
            double spotUmbraG,
            double spotUmbraB,
            double spotPenumbraR,
            double spotPenumbraG,
            double spotPenumbraB,
            double chromaticityX,
            double chromaticityY,
            double colorR,
            double colorG,
            double colorB,
            double visibleEta,
            double visibleEtaRatioToSun,
            double discBrightness,
            double discBrightnessCompressed,
            double limbR,
            double limbG,
            double limbB,
            double activity01,
            double adaptation01,
            double granulationContrastMultiplier,
            bool hasOverrides,
            string overrideWarning)
        {
            MassSolar = massSolar;
            LuminositySolar = luminositySolar;
            RadiusSolar = radiusSolar;
            RadiusKm = radiusSolar * StarPhysics.RsunKm;
            EffectiveTemperatureK = effectiveTemperatureK;
            SurfaceGravityMps2 = surfaceGravityMps2;
            GranulationCellKm = granulationCellKm;
            GranulationCells = granulationCells;
            GranulationContrast = granulationContrast;
            SpotMaxCoverage = spotMaxCoverage;
            SpotCoverage = spotCoverage;
            SpotUmbraTemperatureK = spotUmbraTemperatureK;
            SpotPenumbraTemperatureK = spotPenumbraTemperatureK;
            SpotUmbraR = spotUmbraR;
            SpotUmbraG = spotUmbraG;
            SpotUmbraB = spotUmbraB;
            SpotPenumbraR = spotPenumbraR;
            SpotPenumbraG = spotPenumbraG;
            SpotPenumbraB = spotPenumbraB;
            ChromaticityX = chromaticityX;
            ChromaticityY = chromaticityY;
            ColorR = colorR;
            ColorG = colorG;
            ColorB = colorB;
            VisibleEta = visibleEta;
            VisibleEtaRatioToSun = visibleEtaRatioToSun;
            DiscBrightness = discBrightness;
            DiscBrightnessCompressed = discBrightnessCompressed;
            LimbR = limbR;
            LimbG = limbG;
            LimbB = limbB;
            Activity01 = activity01;
            Adaptation01 = adaptation01;
            GranulationContrastMultiplier = granulationContrastMultiplier;
            HasOverrides = hasOverrides;
            OverrideWarning = overrideWarning;
        }

        /// <summary>Angular radius of the star at a distance in km (small-angle exact: asin(R/d)).</summary>
        public double AngularRadiusRad(double distanceKm)
        {
            if (!(distanceKm > 0d))
            {
                return Math.PI / 2d;
            }

            return Math.Asin(StarPhysics.Clamp(RadiusKm / distanceKm, 0d, 1d));
        }

        /// <summary>Angular diameter in degrees at a distance in km.</summary>
        public double AngularDiameterDeg(double distanceKm) =>
            AngularRadiusRad(distanceKm) * (360d / Math.PI);

        /// <summary>Irradiance at the observer, W/m². Distance in km.</summary>
        public double IrradianceWm2(double distanceKm)
        {
            if (!(distanceKm > 0d))
            {
                return double.PositiveInfinity;
            }

            double distanceAu = distanceKm / StarPhysics.AuKm;
            return StarPhysics.Esun1Au * LuminositySolar / (distanceAu * distanceAu);
        }

        /// <summary>Apparent magnitude (Sun at 1 AU = −26.74).</summary>
        public double ApparentMagnitude(double distanceKm)
        {
            double irradiance = IrradianceWm2(distanceKm);
            return StarPhysics.ApparentMagnitudeFromIrradiance(irradiance);
        }

        /// <summary>Scene-unit Directional Light irradiance: (E/E_ref)·(eta/eta_sun).</summary>
        public double DirectionalIntensity(double distanceKm) =>
            (IrradianceWm2(distanceKm) / StarPhysics.Esun1Au) * VisibleEtaRatioToSun;
    }

    /// <summary>
    /// Main-sequence star physics. Pure C# (System only, double precision): no
    /// UnityEngine and no scene state, so it is covered by both the EditMode tests
    /// and the Core-only xUnit suite.
    ///
    /// Input: mass in M☉. Output: luminosity, radius, effective temperature, surface
    /// gravity, granulation scale, spot temperatures/colours, black-body colour and
    /// the visible fraction of the spectrum. The only artistic inputs are the
    /// activity, the granulation multiplier and the eye-adaptation blend.
    /// </summary>
    public static class StarPhysics
    {
        public const double RsunKm = 695700d;
        public const double Tsun = 5772d;
        public const double AuKm = 149597870.7d;
        public const double Esun1Au = 1361d;
        public const double Sigma = 5.670374e-8d;
        public const double PlanckH = 6.62607e-34d;
        public const double SpeedOfLight = 2.99792458e8d;
        public const double BoltzmannKb = 1.380649e-23d;
        public const double SurfaceGravitySun = 274d;
        public const double MagnitudeSunAt1Au = -26.74d;

        /// <summary>HDR knee: above this radiance the disc is compressed logarithmically.</summary>
        public const double BrightnessKnee = 8d;

        /// <summary>Reference wavelengths of the R/G/B spot contrast (nm).</summary>
        public const double SpotLambdaRnm = 610d;
        public const double SpotLambdaGnm = 550d;
        public const double SpotLambdaBnm = 465d;

        /// <summary>Visible-band luminous fraction of the Sun, computed by the same integrator.</summary>
        public static readonly double EtaSun = ComputeVisibleEta(Tsun);

        // ------------------------------------------------------------------
        // Main sequence relations
        // ------------------------------------------------------------------

        /// <summary>Stefan–Boltzmann luminosity, L☉, piecewise in M☉.</summary>
        public static double LuminositySolar(double massSolar)
        {
            RequirePositiveMass(massSolar);
            if (massSolar < 0.43d)
            {
                return 0.23d * Math.Pow(massSolar, 2.3d);
            }

            if (massSolar < 2d)
            {
                return Math.Pow(massSolar, 4d);
            }

            if (massSolar <= 55d)
            {
                return 1.4d * Math.Pow(massSolar, 3.5d);
            }

            return 32000d * massSolar;
        }

        /// <summary>Main-sequence radius, R☉, piecewise in M☉.</summary>
        public static double RadiusSolar(double massSolar)
        {
            RequirePositiveMass(massSolar);
            if (massSolar < 0.5d)
            {
                return Math.Pow(massSolar, 0.9d);
            }

            if (massSolar < 1d)
            {
                return Math.Pow(massSolar, 0.8d);
            }

            return Math.Pow(massSolar, 0.57d);
        }

        /// <summary>Teff from L and R: Tsun·(L/R²)^0.25.</summary>
        public static double EffectiveTemperatureK(double luminositySolar, double radiusSolar)
        {
            if (!(luminositySolar > 0d) || !(radiusSolar > 0d))
            {
                throw new ArgumentOutOfRangeException(nameof(luminositySolar), "L и R обязаны быть положительными.");
            }

            return Tsun * Math.Pow(luminositySolar / (radiusSolar * radiusSolar), 0.25d);
        }

        /// <summary>Surface gravity, m/s²: g_sun·M/R².</summary>
        public static double SurfaceGravity(double massSolar, double radiusSolar)
        {
            RequirePositiveMass(massSolar);
            if (!(radiusSolar > 0d))
            {
                throw new ArgumentOutOfRangeException(nameof(radiusSolar));
            }

            return SurfaceGravitySun * massSolar / (radiusSolar * radiusSolar);
        }

        /// <summary>Granulation cell size, km: 1000·(Teff/Tsun)/(g/g_sun).</summary>
        public static double GranulationCellKm(double effectiveTemperatureK, double surfaceGravityMps2)
        {
            if (!(effectiveTemperatureK > 0d) || !(surfaceGravityMps2 > 0d))
            {
                throw new ArgumentOutOfRangeException(nameof(effectiveTemperatureK));
            }

            return 1000d * (effectiveTemperatureK / Tsun) / (surfaceGravityMps2 / SurfaceGravitySun);
        }

        /// <summary>Cells across the visible disc, minimum 3.</summary>
        public static double GranulationCells(double radiusSolar, double cellKm)
        {
            if (!(radiusSolar > 0d) || !(cellKm > 0d))
            {
                return 3d;
            }

            return Math.Max(3d, (2d * radiusSolar * RsunKm) / cellKm);
        }

        /// <summary>Physical granulation contrast: 0.07 below 7000 K, zero at/above 8500 K.</summary>
        public static double BaseGranulationContrast(double effectiveTemperatureK)
        {
            if (effectiveTemperatureK <= 7000d)
            {
                return 0.07d;
            }

            if (effectiveTemperatureK >= 8500d)
            {
                return 0d;
            }

            return 0.07d * (8500d - effectiveTemperatureK) / 1500d;
        }

        /// <summary>Spot coverage limit from mass and Teff (0..0.4).</summary>
        public static double SpotMaxCoverage(double massSolar, double effectiveTemperatureK)
        {
            RequirePositiveMass(massSolar);
            if (effectiveTemperatureK > 7500d)
            {
                return 0d;
            }

            if (massSolar <= 0.35d)
            {
                return 0.4d;
            }

            if (massSolar < 1d)
            {
                return Lerp(0.4d, 0.02d, (massSolar - 0.35d) / 0.65d);
            }

            return 0.02d;
        }

        /// <summary>Umbra/penumbra temperatures from Teff and the mass-dependent factor.</summary>
        public static void SpotTemperatures(
            double effectiveTemperatureK,
            out double umbraK,
            out double penumbraK)
        {
            double factor = Clamp(
                Lerp(0.12d, 0.30d, (effectiveTemperatureK - 3000d) / (5800d - 3000d)),
                0.12d,
                0.30d);
            double delta = effectiveTemperatureK * factor;
            umbraK = Math.Max(1000d, effectiveTemperatureK - delta);
            penumbraK = Math.Max(umbraK, effectiveTemperatureK - (0.35d * delta));
        }

        // ------------------------------------------------------------------
        // Colour
        // ------------------------------------------------------------------

        /// <summary>Planck spectral radiance, λ in nm, T in K. Arbitrary common factor is preserved.</summary>
        public static double PlanckRadiance(double lambdaNm, double temperatureK)
        {
            if (!(lambdaNm > 0d) || !(temperatureK > 0d))
            {
                return 0d;
            }

            double lambdaM = lambdaNm * 1e-9d;
            double lambda5 = lambdaM * lambdaM * lambdaM * lambdaM * lambdaM;
            double exponent = PlanckH * SpeedOfLight / (lambdaM * BoltzmannKb * temperatureK);
            if (exponent > 700d)
            {
                return 0d;
            }

            return (2d * PlanckH * SpeedOfLight * SpeedOfLight)
                / (lambda5 * (Math.Exp(exponent) - 1d));
        }

        /// <summary>B(λ,T_spot)/B(λ,T_photosphere), the channel brightness of a spot.</summary>
        public static double SpotBrightnessRatio(double lambdaNm, double spotK, double photosphereK)
        {
            double photosphere = PlanckRadiance(lambdaNm, photosphereK);
            if (!(photosphere > 0d))
            {
                return 1d;
            }

            return PlanckRadiance(lambdaNm, spotK) / photosphere;
        }

        /// <summary>Kim et al. (2002) black-body chromaticity, valid 1667…25000 K.</summary>
        public static void Chromaticity(double temperatureK, out double x, out double y)
        {
            double t = Clamp(temperatureK, 1667d, 25000d);
            if (t < 4000d)
            {
                x = (-0.2661239e9d / (t * t * t))
                    - (0.2343589e6d / (t * t))
                    + (0.8776956e3d / t)
                    + 0.179910d;
            }
            else
            {
                x = (-3.0258469e9d / (t * t * t))
                    + (2.1070379e6d / (t * t))
                    + (0.2226347e3d / t)
                    + 0.240390d;
            }

            if (t < 2222d)
            {
                y = (-1.1063814d * x * x * x)
                    - (1.34811020d * x * x)
                    + (2.18555832d * x)
                    - 0.20219683d;
            }
            else if (t < 4000d)
            {
                y = (-0.9549476d * x * x * x)
                    - (1.37418593d * x * x)
                    + (2.09137015d * x)
                    - 0.16748867d;
            }
            else
            {
                y = (3.0817580d * x * x * x)
                    - (5.87338670d * x * x)
                    + (3.75112997d * x)
                    - 0.37001483d;
            }
        }

        /// <summary>
        /// Linear sRGB from CIE xy. Negative channels are clipped, then the vector is
        /// normalized so that its luminance (0.2126R + 0.7152G + 0.0722B) is exactly 1.
        /// </summary>
        public static void LinearSrgbFromChromaticity(
            double x,
            double y,
            out double r,
            out double g,
            out double b)
        {
            if (!(y > 1e-12d))
            {
                r = g = b = 1d;
                return;
            }

            double bigX = x / y;
            double bigY = 1d;
            double bigZ = (1d - x - y) / y;

            r = (3.2406d * bigX) - (1.5372d * bigY) - (0.4986d * bigZ);
            g = (-0.9689d * bigX) + (1.8758d * bigY) + (0.0415d * bigZ);
            b = (0.0557d * bigX) - (0.2040d * bigY) + (1.0570d * bigZ);

            r = Math.Max(0d, r);
            g = Math.Max(0d, g);
            b = Math.Max(0d, b);

            double luminance = (0.2126d * r) + (0.7152d * g) + (0.0722d * b);
            if (!(luminance > 1e-12d))
            {
                r = g = b = 1d;
                return;
            }

            r /= luminance;
            g /= luminance;
            b /= luminance;
        }

        /// <summary>cT' = lerp(cT, white, adaptation). Luminance is preserved (both ends sum to 1).</summary>
        public static void ApplyEyeAdaptation(
            double r,
            double g,
            double b,
            double adaptation01,
            out double adaptedR,
            out double adaptedG,
            out double adaptedB)
        {
            double a = Clamp(adaptation01, 0d, 1d);
            adaptedR = Lerp(r, 1d, a);
            adaptedG = Lerp(g, 1d, a);
            adaptedB = Lerp(b, 1d, a);
        }

        // ------------------------------------------------------------------
        // Visible fraction of the spectrum (CIE 1931 ybar)
        // ------------------------------------------------------------------

        /// <summary>Wyman et al. 2013 analytic fit of the CIE 1931 ybar curve (λ in nm).</summary>
        public static double CieY(double lambdaNm)
        {
            return (0.821d * PiecewiseGaussian(lambdaNm, 568.8d, 46.9d, 40.5d))
                + (0.286d * PiecewiseGaussian(lambdaNm, 530.9d, 16.3d, 31.1d));
        }

        /// <summary>
        /// eta(T) = ∫ybar·B dλ / ∫B dλ. Visible band 380…780 nm, total 100…3000 nm,
        /// 1 nm trapezoid integration (called once per star at start-up).
        /// </summary>
        public static double ComputeVisibleEta(double temperatureK)
        {
            if (!(temperatureK > 0d))
            {
                throw new ArgumentOutOfRangeException(nameof(temperatureK));
            }

            double numerator = 0d;
            for (int lambda = 380; lambda <= 780; lambda++)
            {
                numerator += CieY(lambda) * PlanckRadiance(lambda, temperatureK);
            }

            double denominator = 0d;
            for (int lambda = 100; lambda <= 3000; lambda++)
            {
                denominator += PlanckRadiance(lambda, temperatureK);
            }

            if (!(denominator > 0d))
            {
                return 0d;
            }

            return numerator / denominator;
        }

        // ------------------------------------------------------------------
        // Geometry, brightness, eclipse
        // ------------------------------------------------------------------

        /// <summary>Angular radius α = asin(R·Rsun/d); distance in km.</summary>
        public static double AngularRadiusRad(double radiusSolar, double distanceKm)
        {
            if (!(distanceKm > 0d))
            {
                return Math.PI / 2d;
            }

            double ratio = radiusSolar * RsunKm / distanceKm;
            return Math.Asin(Clamp(ratio, 0d, 1d));
        }

        /// <summary>W/m² at a distance in AU.</summary>
        public static double IrradianceWm2(double luminositySolar, double distanceAu)
        {
            if (!(distanceAu > 0d))
            {
                return double.PositiveInfinity;
            }

            return Esun1Au * luminositySolar / (distanceAu * distanceAu);
        }

        /// <summary>m = −26.74 − 2.5·log10(E/1361).</summary>
        public static double ApparentMagnitudeFromIrradiance(double irradianceWm2)
        {
            if (!(irradianceWm2 > 0d))
            {
                return double.PositiveInfinity;
            }

            return MagnitudeSunAt1Au - (2.5d * Math.Log10(irradianceWm2 / Esun1Au));
        }

        /// <summary>Physical disc radiance in scene units: (σT⁴/E_ref)·(eta/eta_sun).</summary>
        public static double DiscBrightness(double effectiveTemperatureK, double etaRatioToSun)
        {
            if (!(effectiveTemperatureK > 0d))
            {
                return 0d;
            }

            double t2 = effectiveTemperatureK * effectiveTemperatureK;
            double surfaceFlux = Sigma * t2 * t2;
            return (surfaceFlux / Esun1Au) * etaRatioToSun;
        }

        /// <summary>Logarithmic HDR compression, monotonic and order-preserving above the knee.</summary>
        public static double CompressBrightness(double brightness, double knee = BrightnessKnee)
        {
            if (!(brightness > knee) || !(knee > 0d))
            {
                return brightness;
            }

            return knee * (1d + Math.Log(brightness / knee));
        }

        /// <summary>Angular size of one pixel: fovVertical / screenHeight.</summary>
        public static double PixelAngleRad(double verticalFovRad, int screenHeight)
        {
            if (screenHeight <= 0)
            {
                return 0d;
            }

            return verticalFovRad / screenHeight;
        }

        /// <summary>
        /// Sub-pixel disc: draw at least half a pixel of radius, but lower the surface
        /// brightness by the area ratio so the total flux V·πα² is preserved.
        /// </summary>
        public static void SubpixelDisc(
            double angularRadiusRad,
            double pixelAngleRad,
            out double drawRadiusRad,
            out double energyScale)
        {
            double minimum = 0.5d * pixelAngleRad;
            drawRadiusRad = Math.Max(angularRadiusRad, minimum);
            if (!(drawRadiusRad > 0d))
            {
                energyScale = 0d;
                return;
            }

            double ratio = angularRadiusRad / drawRadiusRad;
            energyScale = ratio * ratio;
        }

        /// <summary>Limb darkening u_G, u_R = 0.8u_G, u_B = min(1.25u_G, 0.95).</summary>
        public static void LimbDarkening(
            double effectiveTemperatureK,
            out double uR,
            out double uG,
            out double uB)
        {
            double t = effectiveTemperatureK;
            double g;
            if (t <= 3000d)
            {
                g = 0.78d;
            }
            else if (t <= 6000d)
            {
                g = Lerp(0.78d, 0.62d, (t - 3000d) / 3000d);
            }
            else if (t <= 10000d)
            {
                g = Lerp(0.62d, 0.45d, (t - 6000d) / 4000d);
            }
            else if (t <= 30000d)
            {
                g = Lerp(0.45d, 0.28d, (t - 10000d) / 20000d);
            }
            else
            {
                g = 0.28d;
            }

            uG = g;
            uR = 0.8d * g;
            uB = Math.Min(1.25d * g, 0.95d);
        }

        /// <summary>
        /// Fraction of the stellar disc visible around one spherical occluder.
        /// α = stellar angular radius, a_o = occluder angular radius, δ = angular
        /// separation of the centres, all radians. Multiple occluders multiply.
        /// </summary>
        public static double EclipseVisibleFraction(double alphaRad, double occluderRad, double separationRad)
        {
            double alpha = Math.Max(0d, alphaRad);
            double occluder = Math.Max(0d, occluderRad);
            double delta = Math.Max(0d, separationRad);

            if (!(alpha > 0d))
            {
                return occluder > 0d && delta < occluder ? 0d : 1d;
            }

            if (delta >= alpha + occluder)
            {
                return 1d;
            }

            if (delta <= Math.Abs(alpha - occluder))
            {
                if (occluder >= alpha)
                {
                    return 0d;
                }

                return Clamp(1d - (occluder * occluder) / (alpha * alpha), 0d, 1d);
            }

            double d2 = delta * delta;
            double a2 = alpha * alpha;
            double o2 = occluder * occluder;

            double arg1 = (d2 + a2 - o2) / (2d * delta * alpha);
            double arg2 = (d2 + o2 - a2) / (2d * delta * occluder);
            double area = a2 * Math.Acos(Clamp(arg1, -1d, 1d))
                + o2 * Math.Acos(Clamp(arg2, -1d, 1d))
                - (0.5d * Math.Sqrt(Math.Max(0d,
                    (-delta + alpha + occluder)
                    * (delta + alpha - occluder)
                    * (delta - alpha + occluder)
                    * (delta + alpha + occluder))));

            double visible = 1d - (area / (Math.PI * a2));
            return Clamp(visible, 0d, 1d);
        }

        /// <summary>Normalized Moffat profile, ∫P dΩ = 1 over the plane.</summary>
        public static double MoffatProfile(double thetaRad, double theta0Rad, double beta = 1.8d)
        {
            if (!(theta0Rad > 0d) || !(beta > 1d))
            {
                return 0d;
            }

            double ratio = thetaRad / theta0Rad;
            return ((beta - 1d) / (Math.PI * theta0Rad * theta0Rad))
                * Math.Pow(1d + (ratio * ratio), -beta);
        }

        /// <summary>Moffat profile normalized to 1 at the centre (shader-friendly).</summary>
        public static double MoffatProfilePeak(double thetaRad, double theta0Rad, double beta = 1.8d)
        {
            if (!(theta0Rad > 0d) || !(beta > 0d))
            {
                return 0d;
            }

            double ratio = thetaRad / theta0Rad;
            return Math.Pow(1d + (ratio * ratio), -beta);
        }

        /// <summary>Corona surface brightness at ρ = r/R★ (ρ ≥ 1).</summary>
        public static double CoronaIntensity(double rho, double ic)
        {
            double p = Math.Max(1d, rho);
            return ic * ((0.7d * Math.Pow(p, -7d)) + (0.3d * Math.Pow(p, -2.5d)));
        }

        /// <summary>Luminance of a linear RGB triple.</summary>
        public static double Luminance(double r, double g, double b) =>
            (0.2126d * r) + (0.7152d * g) + (0.0722d * b);

        // ------------------------------------------------------------------
        // Aggregate
        // ------------------------------------------------------------------

        /// <summary>Derive the complete immutable star state from the preset input.</summary>
        public static StarState Derive(in StarDefinition definition)
        {
            RequirePositiveMass(definition.MassSolar);

            double mass = definition.MassSolar;
            double luminosity = definition.HasLuminosityOverride
                ? definition.LuminositySolarOverride
                : LuminositySolar(mass);
            double radius = definition.HasRadiusOverride
                ? definition.RadiusSolarOverride
                : RadiusSolar(mass);

            if (!(luminosity > 0d) || double.IsInfinity(luminosity))
            {
                luminosity = LuminositySolar(mass);
            }

            if (!(radius > 0d) || double.IsInfinity(radius))
            {
                radius = RadiusSolar(mass);
            }

            double temperature = definition.HasTemperatureOverride
                ? definition.EffectiveTemperatureKOverride
                : EffectiveTemperatureK(luminosity, radius);
            if (!(temperature > 0d) || double.IsInfinity(temperature))
            {
                temperature = EffectiveTemperatureK(luminosity, radius);
            }

            double gravity = SurfaceGravity(mass, radius);
            double cell = GranulationCellKm(temperature, gravity);
            double cells = GranulationCells(radius, cell);
            double baseContrast = BaseGranulationContrast(temperature);
            double granulationContrast = baseContrast * Clamp(definition.GranulationContrastMultiplier, 0d, 4d);
            double activity = Clamp(definition.Activity01, 0d, 1d);
            double spotMax = SpotMaxCoverage(mass, temperature);
            double spotCoverage = activity * spotMax;
            SpotTemperatures(temperature, out double umbraK, out double penumbraK);

            double spotUmbraR = SpotBrightnessRatio(SpotLambdaRnm, umbraK, temperature);
            double spotUmbraG = SpotBrightnessRatio(SpotLambdaGnm, umbraK, temperature);
            double spotUmbraB = SpotBrightnessRatio(SpotLambdaBnm, umbraK, temperature);
            double spotPenumbraR = SpotBrightnessRatio(SpotLambdaRnm, penumbraK, temperature);
            double spotPenumbraG = SpotBrightnessRatio(SpotLambdaGnm, penumbraK, temperature);
            double spotPenumbraB = SpotBrightnessRatio(SpotLambdaBnm, penumbraK, temperature);

            Chromaticity(temperature, out double cx, out double cy);
            LinearSrgbFromChromaticity(cx, cy, out double cr, out double cg, out double cb);
            ApplyEyeAdaptation(cr, cg, cb, definition.Adaptation01, out cr, out cg, out cb);

            double eta = ComputeVisibleEta(temperature);
            double etaRatio = EtaSun > 0d ? eta / EtaSun : 1d;
            double brightness = DiscBrightness(temperature, etaRatio);
            double compressed = CompressBrightness(brightness);
            LimbDarkening(temperature, out double uR, out double uG, out double uB);

            bool overrides = definition.HasAnyOverride;
            string warning = overrides
                ? "StarPreset: задано переопределение радиуса/светимости/температуры — звезда вне зависимости масса–светимость."
                : null;

            return new StarState(
                mass,
                luminosity,
                radius,
                temperature,
                gravity,
                cell,
                cells,
                granulationContrast,
                spotMax,
                spotCoverage,
                umbraK,
                penumbraK,
                spotUmbraR,
                spotUmbraG,
                spotUmbraB,
                spotPenumbraR,
                spotPenumbraG,
                spotPenumbraB,
                cx,
                cy,
                cr,
                cg,
                cb,
                eta,
                etaRatio,
                brightness,
                compressed,
                uR,
                uG,
                uB,
                activity,
                Clamp(definition.Adaptation01, 0d, 1d),
                Clamp(definition.GranulationContrastMultiplier, 0d, 4d),
                overrides,
                warning);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        public static double Clamp(double value, double min, double max) =>
            value < min ? min : (value > max ? max : value);

        public static double Lerp(double a, double b, double t) => a + ((b - a) * Clamp(t, 0d, 1d));

        private static double PiecewiseGaussian(double lambda, double mu, double sigma1, double sigma2)
        {
            double t = lambda - mu;
            double sigma = t < 0d ? sigma1 : sigma2;
            return Math.Exp(-(t * t) / (2d * sigma * sigma));
        }

        private static void RequirePositiveMass(double massSolar)
        {
            if (!(massSolar > 0d) || double.IsInfinity(massSolar))
            {
                throw new ArgumentOutOfRangeException(nameof(massSolar), "Масса звезды обязана быть положительной и конечной.");
            }
        }
    }
}
