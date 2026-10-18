using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Surface palette: height bands (beach -> lowland -> rock -> snow) plus
    /// biomes by "wetness" from a noise mask (desert/steppe/forest). Layer order:
    /// (0) the mask shifts t BEFORE the layers and sets the biome; (1) base by height;
    /// (2) rock override by slope; (3) snow only on gentle slopes. With all features
    /// off (rock/snow/mask = 0) -> neutral base (= HeightColorLegacy).
    /// </summary>
    internal static class TerrainPalette
    {
        // Colors are authored in familiar sRGB (as in the editor) while the project
        // renders in Linear: without conversion the albedo enters the shader as
        // linear and comes out gamma-brightened/washed out (pale surface).
        internal static float ToLinear(float c)
        {
            return c <= 0.04045f
                ? c / 12.92f
                : (float)System.Math.Pow((c + 0.055f) / 1.055f, 2.4d);
        }

        /// <summary>sRGB->Linear for the palette from TerrainProfile.Palette.</summary>
        internal static Color ToLinear(Color c)
        {
            return new Color(ToLinear(c.r), ToLinear(c.g), ToLinear(c.b), 1f);
        }

        private static Color Srgb(float r, float g, float b)
        {
            return new Color(ToLinear(r), ToLinear(g), ToLinear(b), 1f);
        }

        internal static readonly Color Sand = Srgb(0.80f, 0.73f, 0.55f);
        internal static readonly Color Desert = Srgb(0.74f, 0.60f, 0.36f);
        internal static readonly Color DryGrass = Srgb(0.50f, 0.52f, 0.27f);
        internal static readonly Color Grass = Srgb(0.102f, 0.290f, 0.145f);
        internal static readonly Color Forest = Srgb(0.12f, 0.28f, 0.10f);
        internal static readonly Color Tundra = Srgb(0.46f, 0.44f, 0.37f);
        internal static readonly Color Rock = Srgb(0.45f, 0.41f, 0.36f);
        internal static readonly Color Snow = Srgb(0.96f, 0.97f, 0.99f);
        internal static readonly Color Sea = Srgb(0.05f, 0.20f, 0.42f);
        // Ground mottling: patches of soil (warm brown) and lush green.
        internal static readonly Color Soil = Srgb(0.50f, 0.40f, 0.26f);
        internal static readonly Color Lush = Srgb(0.13f, 0.40f, 0.12f);

        /// <summary>
        /// Slope angle tangent from the cosine between normal and radial.
        /// Dimensionless (rise/run), so thresholds don't drift with tile/LOD scale.
        /// cos &lt;= 1e-6 (overhang/degeneracy) counts as a vertical slope.
        /// </summary>
        internal static double SlopeTan(double cosA)
        {
            if (!(cosA > 1e-6d))
            {
                return 1e6d;
            }

            double sinA = System.Math.Sqrt(System.Math.Max(0d, 1d - (cosA * cosA)));
            return sinA / cosA;
        }

        /// <summary>
        /// Sea level tolerance in ABSOLUTE meters — the same as in
        /// PlanetSurface.shader (SeaEpsilon), so both sides decide identically.
        /// </summary>
        internal static double SeaEpsilon(double amplitude)
        {
            return System.Math.Max(0.05d, amplitude * 1e-6d);
        }

        internal static Color HeightColorEx(
            double height, double seaLevel, double amplitude,
            double slopeTan, double colorMask,
            double rockSlopeTan, double rockWidth,
            double snowSlopeTan, double maskStrength,
            double colorDetail = 0d, double detailStrength = 0d,
            double latitudeRadians = 0d, double rockHeightMin = 0d,
            double beachHeightMeters = 0d)
        {
            if (rockSlopeTan <= 0d && snowSlopeTan <= 0d && maskStrength == 0d
                && detailStrength == 0d && beachHeightMeters <= 0d)
            {
                return HeightColorLegacy(height, seaLevel, amplitude);
            }

            bool maskOn = maskStrength != 0d;
            Color c = BaseColor(height, seaLevel, amplitude, colorMask, maskOn, maskStrength, latitudeRadians, beachHeightMeters);
            bool isSea = height <= seaLevel + SeaEpsilon(amplitude);
            // Beach is in absolute meters: mottling and rock leave it alone
            // (shader parity) even if the mask raised t above the sand band.
            // Bounds are the same as in BaseColor (SandWeight).
            double beachWeight = SandWeight(height - seaLevel, beachHeightMeters);
            bool isBeach = !isSea && beachWeight > 0d;

            // Normalized height t as in the shader (mask shifts the thresholds):
            // used both to keep mottling off the beach and for the rock-override
            // height threshold (rock only on tall mountains).
            double tMasked = (height - seaLevel) / System.Math.Max(1d, amplitude);
            if (maskOn)
            {
                tMasked += colorMask * maskStrength;
            }

            if (tMasked < 0d)
            {
                tMasked = 0d;
            }

            // Small-scale ground variation: patches of soil (warm brown) and
            // lusher/darker green. Only on land above the beach zone — no patches
            // on sand.
            if (!isSea && !isBeach && detailStrength != 0d)
            {
                double d = Clamp(colorDetail, -1d, 1d);
                if (d > 0d)
                {
                    c = Color.Lerp(c, Soil, (float)(d * detailStrength));
                }
                else
                {
                    c = Color.Lerp(c, Lush, (float)((-d) * detailStrength * 0.6d));
                }
            }

            bool snowBand = !isSea && IsSnowBand(height, seaLevel, amplitude);

            // Rock only on steep AND sufficiently high slopes (tall mountains).
            // The height threshold cuts off the beach, dunes and lowlands.
            if (!isSea && !isBeach && rockSlopeTan > 0d && slopeTan >= rockSlopeTan
                && (rockHeightMin <= 0d || tMasked >= rockHeightMin))
            {
                double w = (slopeTan - rockSlopeTan) / System.Math.Max(1e-9d, rockWidth);
                w = System.Math.Min(1d, w);
                w = w * w * (3d - (2d * w));
                c = w >= 1d ? Rock : Color.Lerp(c, Rock, (float)w);
            }

            if (snowBand && snowSlopeTan > 0d && slopeTan > snowSlopeTan)
            {
                c = Rock;
            }

            return c;
        }

        internal static Color HeightColorLegacy(double height, double seaLevel, double amplitude)
        {
            return BaseColor(height, seaLevel, amplitude, 0d, false, 0d, 0d, 0d);
        }

        private static bool IsSnowBand(double height, double seaLevel, double amplitude)
        {
            double t = (height - seaLevel) / System.Math.Max(1d, amplitude);
            return t >= 0.55d;
        }

        private static Color BaseColor(
            double height, double seaLevel, double amplitude,
            double mask, bool maskOn, double maskStrength, double latitudeRadians,
            double beachHeightMeters)
        {
            if (height <= seaLevel + SeaEpsilon(amplitude))
            {
                return Sea;
            }

            // Beach is in absolute meters above sea and is not erased by the mask:
            // otherwise on "positive" shores green would start at the water's edge.
            // A fade, not a step: full sand band only up to 0.4·beach, nothing left by
            // 1.6·beach, and exactly half at beach. Mirrors the smoothstep in
            // PlanetSurface.
            double sandW = SandWeight(height - seaLevel, beachHeightMeters);
            if (sandW >= 1d)
            {
                return Sand;
            }

            double t = (height - seaLevel) / System.Math.Max(1d, amplitude);
            if (maskOn)
            {
                t += mask * maskStrength;
            }

            if (t < 0d)
            {
                t = 0d;
            }

            // Biome by wetness: dry -> desert -> dry grass -> meadow -> forest.
            // No normalized sand band below: sand is only the absolute beach
            // (beachHeightMeters) earlier in the code.
            //
            // The wet curve is shared with the decor
            // (GroundDecorDistribution.BiomeWetness, mirrored in PlanetSurface.shader):
            // soft tanh instead of clamp.
            double wet = maskOn ? GroundDecorDistribution.BiomeWetness(mask) : 0.5d;
            Color lowland = BiomeColor(wet);
            Color c;
            if (t < 0.45d)
            {
                c = lowland;
            }
            else if (t < 0.70d)
            {
                c = Color.Lerp(lowland, Rock, (float)((t - 0.45d) * (1d / 0.25d)));
            }
            else
            {
                c = Color.Lerp(Rock, Snow, (float)((t - 0.70d) * (1d / 0.30d)));
            }

            // Polar latitudes: tundra at the tree line, ice cap at the poles.
            double lat01 = Clamp01(System.Math.Abs(latitudeRadians) / (System.Math.PI * 0.5d));
            double tundra = Smoothstep01((lat01 - 0.52d) / 0.22d);
            double ice = Smoothstep01((lat01 - 0.70d) / 0.16d);
            if (tundra > 0d)
            {
                c = Color.Lerp(c, Tundra, (float)(tundra * 0.85d));
            }

            if (ice > 0d)
            {
                c = Color.Lerp(c, Snow, (float)ice);
            }

            // Sand goes on top of the biome rather than replacing the branch: in the
            // band's fade the green biome must show through the sand, not vanish.
            if (sandW > 0d)
            {
                c = Color.Lerp(c, Sand, (float)sandW);
            }

            return c;
        }

        /// <summary>
        /// Sand weight by height above sea: 1 up to 0.4·beach, 0 from 1.6·beach,
        /// exactly 0.5 at the beach edge. Zero beach means no beach.
        /// Single source of the numbers: the shader and the decor use the same beach.
        /// </summary>
        internal static double SandWeight(double aboveSea, double beachHeightMeters)
        {
            if (beachHeightMeters <= 0d)
            {
                return 0d;
            }

            return 1d - Smoothstep01((aboveSea - (0.4d * beachHeightMeters)) / (1.2d * beachHeightMeters));
        }

        /// <summary>Lowland color by normalized wetness 0..1.</summary>
        private static Color BiomeColor(double wet)
        {
            if (wet < 0.18d)
            {
                return Desert;
            }

            if (wet < 0.38d)
            {
                return Color.Lerp(Desert, DryGrass, (float)((wet - 0.18d) / 0.20d));
            }

            if (wet < 0.58d)
            {
                return Color.Lerp(DryGrass, Grass, (float)((wet - 0.38d) / 0.20d));
            }

            if (wet < 0.80d)
            {
                return Color.Lerp(Grass, Forest, (float)((wet - 0.58d) / 0.22d));
            }

            return Forest;
        }

        private static double Smoothstep01(double t)
        {
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

        private static double Clamp(double v, double min, double max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        private static double Clamp01(double v)
        {
            return v < 0d ? 0d : (v > 1d ? 1d : v);
        }
    }
}
