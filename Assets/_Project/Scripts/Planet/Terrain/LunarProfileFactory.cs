using System;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>Flavour of an airless cratered body. All of them share the same generator; only parameters and palette differ.</summary>
    public enum LunarStyle
    {
        /// <summary>Dark maria on the near side, bright saturated highlands, a giant far-side basin (the Moon).</summary>
        MoonLike = 0,

        /// <summary>No maria: saturated highlands with basins (the lunar far side).</summary>
        HighlandsOnly = 1,

        /// <summary>Large smooth plains of the same albedo as the rest, lobate scarps, a Caloris-like basin (Mercury).</summary>
        MercuryLike = 2,

        /// <summary>Ancient, very densely cratered, bright icy-gray crust with a huge multi-ring basin (Callisto, Rhea).</summary>
        AncientCratered = 3
    }

    /// <summary>Small physical helpers for rocky airless bodies of roughly lunar mass.</summary>
    public static class LunarBodyPhysics
    {
        public const double GravitationalConstant = 6.6743e-11d;
        public const double MoonMassKg = 7.342e22d;
        public const double MoonRadiusMeters = 1737400d;
        public const double MoonSurfaceGravity = 1.62d;
        public const double MoonComplexTransitionMeters = 18000d;

        public static double RadiusFromMass(double massKg, double densityKgM3)
        {
            return Math.Pow(3d * massKg / (4d * Math.PI * Math.Max(100d, densityKgM3)), 1d / 3d);
        }

        public static double SurfaceGravity(double massKg, double radiusMeters)
        {
            return GravitationalConstant * massKg / (radiusMeters * radiusMeters);
        }

        /// <summary>Simple→complex crater transition scales inversely with gravity (Moon 18 km, Mercury ~11 km, Mars ~7 km).</summary>
        public static double ComplexTransition(double surfaceGravity)
        {
            return Math.Max(3000d, Math.Min(60000d, MoonComplexTransitionMeters * MoonSurfaceGravity / Math.Max(0.05d, surfaceGravity)));
        }
    }

    /// <summary>
    /// Builds complete TerrainProfiles (shape + lunar generator + palette + colour settings) for airless bodies.
    /// The profile is self-contained: assign the resulting asset to BodyAuthoring.TerrainPreset, no atmosphere.
    /// </summary>
    public static class LunarProfileFactory
    {
        /// <summary>
        /// radiusMeters / surfaceGravity drive crater scaling; seed only drives the variation roll
        /// (the actual terrain seed is BodyAuthoring.TerrainSeed). randomize = true gives every body
        /// its own mix of maria coverage, crater age and density, basin position and size.
        /// </summary>
        public static TerrainProfile Create(LunarStyle style, double radiusMeters, double surfaceGravity, int seed, bool randomize)
        {
            var rng = new System.Random(seed ^ 0x5EED1234);
            double R = Math.Max(50000d, radiusMeters);
            double reliefScale = Math.Max(0.5d, Math.Min(2.5d, LunarBodyPhysics.MoonSurfaceGravity / Math.Max(0.05d, surfaceGravity)));

            var L = new LunarReliefSettings();
            L.Enabled = true;
            L.RadiusMeters = R;
            // Which body-fixed hemisphere is "near". OrbitingBody.ApplyTidalLock pins body
            // longitude 180 (-X) at the parent, so the maria and the depressed near side have
            // to live at -X: otherwise a tidally locked body shows the parent its far side.
            L.NearX = -1d;
            L.NearY = 0d;
            L.NearZ = 0d;
            L.ComplexTransitionMeters = LunarBodyPhysics.ComplexTransition(surfaceGravity);
            L.MaxCraterDiameterMeters = 0.8d * R;
            L.RegolithFrequency = R / 1000d;
            L.MegaregolithFrequency = R / 7000d;
            L.SecondaryClusterFrequency = R / 55000d;

            L.GlobalReliefMeters *= reliefScale;
            L.NearFarOffsetMeters *= reliefScale;
            L.RoughnessMeters *= reliefScale;
            L.MareLevelMeters *= reliefScale;
            L.MareLevelVariationMeters *= reliefScale;
            L.MareUndulationMeters *= reliefScale;
            L.WrinkleRidgeMeters *= reliefScale;
            L.RilleDepthMeters *= reliefScale;
            L.DomeHeightMeters *= reliefScale;
            L.MegaregolithMeters *= reliefScale;

            Color baseCol;
            Color dark;
            Color bright;
            Color rock;

            switch (style)
            {
                case LunarStyle.HighlandsOnly:
                    L.MareCoverage = 0d;
                    L.CraterDensity = 0.36d;
                    L.NearFarOffsetMeters = 0d;
                    L.GiantBasinDiameterMeters = R * 1.1d;
                    baseCol = new Color(0.52f, 0.51f, 0.49f);
                    dark = new Color(0.30f, 0.30f, 0.30f);
                    bright = new Color(0.88f, 0.87f, 0.84f);
                    rock = new Color(0.38f, 0.37f, 0.36f);
                    break;

                case LunarStyle.MercuryLike:
                    L.MareCoverage = 0.55d;
                    L.MareLevelMeters = -250d * reliefScale;
                    L.MareNearsideBias = 0.3d;
                    L.MareUndulationMeters = 70d * reliefScale;
                    L.MareDarkness = 0.22d;
                    L.WrinkleRidgeMeters = 170d * reliefScale;
                    L.RilleDepthMeters = 0d;
                    L.DomeHeightMeters = 0d;
                    L.NearFarOffsetMeters = 0d;
                    L.ScarpHeightMeters = 700d * reliefScale;
                    L.ScarpAmount = 0.30d;
                    L.CraterDensity = 0.34d;
                    L.FreshnessExponent = 2.0d;
                    L.GiantBasinDiameterMeters = R * 0.64d;
                    L.GiantDepthScale = 0.55d;
                    baseCol = new Color(0.43f, 0.41f, 0.39f);
                    dark = new Color(0.33f, 0.31f, 0.30f);
                    bright = new Color(0.78f, 0.74f, 0.70f);
                    rock = new Color(0.31f, 0.30f, 0.29f);
                    break;

                case LunarStyle.AncientCratered:
                    L.MareCoverage = 0d;
                    L.CraterDensity = 0.52d;
                    L.FreshnessExponent = 1.8d;
                    L.NearFarOffsetMeters = 0d;
                    L.RoughnessMeters *= 0.6d;
                    L.GlobalReliefMeters *= 0.6d;
                    L.GiantBasinDiameterMeters = R * 0.9d;
                    L.GiantDepthScale = 0.45d;
                    L.RayDensity = 0.2d;
                    baseCol = new Color(0.56f, 0.55f, 0.54f);
                    dark = new Color(0.30f, 0.30f, 0.31f);
                    bright = new Color(0.95f, 0.96f, 0.98f);
                    rock = new Color(0.40f, 0.40f, 0.41f);
                    break;

                default:
                    // South-Pole–Aitken analogue: 2.4 Mm on a 1737 km body, i.e. 1.38 R. Keep the RATIO,
                    // not the absolute diameter, or a body the size of Ceres gets a basin bigger than
                    // itself (the cavity then wraps the whole sphere and reads as a global dimple).
                    L.GiantBasinDiameterMeters = R * (2400000d / LunarBodyPhysics.MoonRadiusMeters);
                    baseCol = new Color(0.50f, 0.49f, 0.47f);
                    dark = new Color(0.17f, 0.17f, 0.18f);
                    bright = new Color(0.88f, 0.87f, 0.84f);
                    rock = new Color(0.36f, 0.35f, 0.34f);
                    break;
            }

            if (randomize)
            {
                L.CraterDensity *= Lerp(rng, 0.8d, 1.2d);
                L.FreshnessExponent *= Lerp(rng, 0.85d, 1.2d);
                if (L.MareCoverage > 0d)
                {
                    L.MareCoverage = Math.Max(0.15d, Math.Min(0.6d, L.MareCoverage * Lerp(rng, 0.65d, 1.25d)));
                    L.MareLevelMeters += Lerp(rng, -250d, 250d) * reliefScale;
                    L.MareFrequency = Lerp(rng, 1.1d, 2.2d);
                    L.MareNearsideBias = Math.Max(0d, L.MareNearsideBias + Lerp(rng, -0.4d, 0.4d));
                    L.MareAge = Lerp(rng, 0.45d, 0.75d);
                }

                L.ProductionSlope = Lerp(rng, 1.95d, 2.3d);
                L.GlobalFrequency = Lerp(rng, 1.0d, 2.0d);
                L.RoughnessMeters *= Lerp(rng, 0.7d, 1.3d);
                L.GiantBasinDiameterMeters *= Lerp(rng, 0.65d, 1.1d);
                L.WrinkleRidgeFrequency = Lerp(rng, 20d, 34d);
            }

            // Giant basin: always on the FAR hemisphere (opposite the near axis, +X), random
            // latitude/longitude — the South-Pole–Aitken analogue.
            double lat = Lerp(rng, -0.9d, 0.9d);
            double lon = Lerp(rng, -1.1d, 1.1d);
            L.GiantX = Math.Cos(lat) * Math.Cos(lon);
            L.GiantY = Math.Cos(lat) * Math.Sin(lon);
            L.GiantZ = Math.Sin(lat);

            var profile = new TerrainProfile();
            profile.AmplitudeMeters = 10000d; // normalisation scale only; heights come out in metres
            profile.SeaLevelMeters = -1e30d;  // no water, no ocean patch
            profile.Lunar = L;

            // Colour: the renderer paints "no sea" bodies with the Snow colour (height band is huge);
            // slopes switch to Rock; the lunar albedo field rides in the colour-detail channel:
            // positive -> Soil (dark maria), negative -> Lush (bright fresh ejecta / rays).
            profile.ColorRockSlopeTan = 0.8d;
            profile.ColorRockSlopeWidth = 0.3d;
            profile.ColorDetailFrequency = 600d;
            profile.ColorDetailOctaves = 3;
            profile.ColorDetailStrength = 1d;
            profile.NormalEpsilonMeters = 2d;

            TerrainPaletteData pal = profile.Palette;
            pal.Snow = baseCol;
            pal.Tundra = baseCol;
            pal.Sand = baseCol;
            pal.Desert = baseCol;
            pal.DryGrass = baseCol;
            pal.Grass = baseCol;
            pal.Forest = baseCol;
            pal.Rock = rock;
            pal.Sea = dark;
            pal.Soil = dark;
            pal.Lush = bright;
            return profile;
        }

        private static double Lerp(System.Random rng, double a, double b)
        {
            return a + ((b - a) * rng.NextDouble());
        }
    }
}
