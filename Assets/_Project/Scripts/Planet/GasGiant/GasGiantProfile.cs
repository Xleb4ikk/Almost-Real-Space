using System;
using System.Collections.Generic;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// One latitude band of a gas giant. Latitudes are PLANETOGRAPHIC degrees,
    /// as in the Jupiter brief; the CPU converts them to planetocentric when it
    /// builds the LUT, because the shader works with asin(p.z).
    /// </summary>
    [Serializable]
    public sealed class GasGiantBand
    {
        public string Name = "Band";

        [Range(-90f, 90f)]
        public float LatitudeDegrees;

        [Range(0.2f, 45f)]
        public float HalfWidthDegrees = 4f;

        public Color Color = Color.white;

        /// <summary>0 = belt (dark, thin clouds), 1 = zone (light, high haze).</summary>
        [Range(0f, 1f)]
        public float ZoneMask = 0.5f;

        /// <summary>Soft edge of the band as a fraction of half-width.</summary>
        [Range(0.01f, 1f)]
        public float Softness = 0.45f;
    }

    /// <summary>Zonal wind speed at a planetographic latitude. + is prograde (eastward).</summary>
    [Serializable]
    public sealed class GasGiantWindSample
    {
        [Range(-90f, 90f)]
        public float LatitudeDegrees;

        [Range(-300f, 300f)]
        public float SpeedMps;
    }

    /// <summary>
    /// Vortex / storm. Latitude and longitude are planetographic; longitude is
    /// measured in the planet-fixed frame (the shader adds the art-clock spin).
    /// </summary>
    [Serializable]
    public sealed class GasGiantVortex
    {
        public string Name = "Vortex";

        [Range(-90f, 90f)]
        public float LatitudeDegrees;

        [Range(-180f, 180f)]
        public float LongitudeDegrees;

        [Range(0.2f, 30f)]
        public float RadiusLatDegrees = 5f;

        [Range(0.2f, 40f)]
        public float RadiusLonDegrees = 8f;

        /// <summary>+1 / -1: rotation direction. The shader multiplies by 2ПЂ / period.</summary>
        [Range(-1f, 1f)]
        public float Spin = 1f;

        public Color CoreColor = Color.white;
        public Color CollarColor = Color.gray;

        [Range(0f, 1f)]
        public float Intensity = 0.8f;

        [Range(0f, 1f)]
        public float CollarWidth = 0.25f;

        /// <summary>Degrees per day in the art clock. Small values are enough for a demo.</summary>
        [Range(-60f, 60f)]
        public float DriftDegreesPerDay;

        /// <summary>How strongly the vortex bends the surrounding bands.</summary>
        [Range(0f, 2f)]
        public float SwirlStrength = 0.5f;
    }

    /// <summary>
    /// Visual data for a gas giant. This is a plain [Serializable] class, like
    /// TerrainProfile/CloudProfile; GasGiantProfileAsset only stores a named
    /// preset. The shader and the view share this single source.
    ///
    /// The planet is deliberately NOT a Jupiter copy: the band layout, palette
    /// and hero vortex are the fictional Aeolus. The physical principles (zones,
    /// belts, differential winds, vortices, thin haze, limb darkening) are the
    /// same.
    /// </summary>
    [Serializable]
    public sealed class GasGiantProfile
    {
        [Header("Р’РёР·СѓР°Р»")]
        public bool VisualEnabled = true;

        [Tooltip("Опциональная экваториальная карта поверхности. Если задана, она заменяет процедурные полосы как альбедо.")]
        public Texture2D SurfaceMap;

        [Tooltip("Вес карты поверхности относительно процедурных полос.")]
        [Range(0f, 1f)]
        public float SurfaceMapStrength = 1f;

        [Tooltip("Сдвиг долготы карты (в долях оборота): ставит Большое красное пятно в нужный меридиан.")]
        [Range(-0.5f, 0.5f)]
        public float SurfaceMapLongitudeOffset = 0.1f;

        [Tooltip("Рельеф облаков по яркости карты (0 — плоская текстура).")]
        [Range(0f, 1f)]
        public float SurfaceMapBump = 0.35f;

        [Tooltip("РЎРїР»СЋСЃРЅСѓС‚РѕСЃС‚СЊ: РїРѕР»СЏСЂРЅС‹Р№ СЂР°РґРёСѓСЃ / СЌРєРІР°С‚РѕСЂРёР°Р»СЊРЅС‹Р№. Р®РїРёС‚РµСЂ 0.935.")]
        [Range(0.8f, 1f)]
        public float OblatenessRatio = 0.935f;

        [Header("РџРѕР»РѕСЃС‹, РІРµС‚СЂС‹ Рё РІРёС…СЂРё (РїР»Р°РЅРµС‚РѕРіСЂР°С„РёС‡РµСЃРєРёРµ С€РёСЂРѕС‚С‹)")]
        public List<GasGiantBand> Bands = new List<GasGiantBand>();
        public List<GasGiantWindSample> Winds = new List<GasGiantWindSample>();
        public List<GasGiantVortex> Vortices = new List<GasGiantVortex>();

        [Header("Р¦РІРµС‚Р° Рё РґС‹РјРєР°")]
        public Color NorthPoleColor = new Color(0.54f, 0.56f, 0.66f, 1f);
        public Color SouthPoleColor = new Color(0.37f, 0.39f, 0.47f, 1f);
        public Color HazeColor = new Color(0.72f, 0.77f, 0.78f, 1f);
        public Color RimColor = new Color(0.66f, 0.75f, 0.82f, 1f);

        [Header("РђРЅРёРјР°С†РёСЏ (Р°СЂС‚-С‡Р°СЃС‹)")]
        [Tooltip("Темп арт-часов анимации облаков, с. НЕ задаёт видимое вращение: тело вращается с периодом RotationPeriodSeconds.")]
        [Min(1f)]
        public float VisualSpinPeriodSeconds = 120f;

        [Tooltip("Период короткого цикла арт-времени для вихрей, с.")]
        [Min(1f)]
        public float PhasePeriodSeconds = 30f;

        [Tooltip("РџРµСЂРёРѕРґ Р·Р°РєСЂСѓС‚РєРё РІРёС…СЂРµР№, СЃ Р°СЂС‚-С‡Р°СЃРѕРІ.")]
        [Min(1f)]
        public float VortexSpinPeriodSeconds = 30f;

        [Tooltip("Усиление дифференциального сдвига зональных ветров. Больше — быстрее перетекают полосы.")]
        [Range(0f, 80f)]
        public float ShearExaggeration = 25f;

        [Tooltip("Разница скоростей двух облачных слоёв (доля от скорости ветра). 0 = один слой.")]
        [Range(0f, 0.5f)]
        public float LayerSpeedDelta = 0.1f;

        [Tooltip("Вес вторичного облачного слоя. 0 = только основной слой, 0.18 = лёгкий параллакс.")]
        [Range(0f, 0.5f)]
        public float LayerBlend = 0.18f;

        [Header("РЁСѓРј Рё РѕРїС‚РёРєР°")]
        [Range(0f, 0.3f)]
        public float WarpAmplitude = 0.12f;

        [Min(0.1f)]
        public float WarpScale = 3.5f;

        [Min(0.1f)]
        public float TurbulenceScale = 10f;

        [Min(1f)]
        public float TurbulenceAnisotropy = 5f;

        [Range(0f, 1f)]
        public float HazeDensity = 0.16f;

        [Range(0f, 1f)]
        public float LimbDarkening = 0.7f;

        [Range(0f, 1f)]
        public float WrapDiffuse = 0.3f;

        [Range(0f, 2f)]
        public float RimIntensity = 0.35f;

        [Range(0.5f, 1.2f)]
        public float Saturation = 1.0f;

        [Tooltip("Сила рельефа облачного слоя (нормали от градиента мелкого шума). 0 — плоская текстура.")]
        [Range(0f, 2f)]
        public float CloudBump = 0.45f;

        [Tooltip("Контраст полос после дымки. Больше 1 — темнее пояса и ярче зоны.")]
        [Range(0.6f, 1.8f)]
        public float Contrast = 1.08f;

        [Tooltip("Насколько полярные шапки перекрывают полосы.")]
        [Range(0f, 1f)]
        public float PolarTint = 0.38f;

        [Tooltip("Сила мелких филаментов и ячеек.")]
        [Range(0f, 2f)]
        public float DetailStrength = 1.0f;

        [Header("РљР°С‡РµСЃС‚РІРѕ Рё РјРµР»РєРёР№ РґРёСЃРє")]
        [Range(0f, 8f)]
        public float MinScreenRadiusPixels = 2f;

        [Range(32, 8192)]
        public int BandLutWidth = 1024;

        [Range(32, 2048)]
        public int WindLutWidth = 256;

        [Range(16, 256)]
        public int MeshResolution = 96;

        /// <summary>
        /// Aeolus preset: a Jupiter-class gas giant. The band layout follows the
        /// real Jovian wind system (NEB/SEB, temperate belts, bright tropical
        /// zones, cool mottled polar hoods) and the hero storm is a large
        /// salmon-red oval in the South Tropical Zone. The exact longitudes and
        /// the second red oval are Aeolus' own signature.
        /// </summary>
        public static GasGiantProfile CreateAeolus()
        {
            var profile = new GasGiantProfile
            {
                NorthPoleColor = Hex("#524773"),
                SouthPoleColor = Hex("#423862"),
                HazeColor = Hex("#D0E8E0"),
                RimColor = Hex("#B8A8E0"),
                VisualSpinPeriodSeconds = 120f,
                PhasePeriodSeconds = 30f,
                VortexSpinPeriodSeconds = 40f,
                ShearExaggeration = 18f,
                LayerSpeedDelta = 0.08f,
                WarpAmplitude = 0.06f,
                WarpScale = 5.5f,
                TurbulenceScale = 24f,
                TurbulenceAnisotropy = 9f,
                HazeDensity = 0.03f,
                LimbDarkening = 0.3f,
                WrapDiffuse = 0.45f,
                RimIntensity = 0.08f,
                Saturation = 1.0f,
                CloudBump = 0.2f,
                Contrast = 1.15f,
                PolarTint = 0.0f,
                DetailStrength = 0.0f,
                SurfaceMapStrength = 1f,
                SurfaceMapLongitudeOffset = 0f,
                SurfaceMapBump = 0f,
                MinScreenRadiusPixels = 2f,
                BandLutWidth = 2048,
                WindLutWidth = 512,
                MeshResolution = 128,
            };

            // Jupiter latitude structure. Belts are narrow and dark; zones are
            // broad and nearly white. The thin "filament" bands are the sharp
            // boundaries where the zonal jets shear the clouds.
            profile.Bands.Add(new GasGiantBand { Name = "North Polar Hood", LatitudeDegrees = 78f, HalfWidthDegrees = 11f, Color = Hex("#A79C8C"), ZoneMask = 0.25f, Softness = 0.75f });
            profile.Bands.Add(new GasGiantBand { Name = "N Polar Belt", LatitudeDegrees = 63f, HalfWidthDegrees = 4f, Color = Hex("#6C5A50"), ZoneMask = 0.2f, Softness = 0.6f });
            profile.Bands.Add(new GasGiantBand { Name = "N Polar Zone", LatitudeDegrees = 57f, HalfWidthDegrees = 4f, Color = Hex("#CDBB9E"), ZoneMask = 0.72f, Softness = 0.55f });
            profile.Bands.Add(new GasGiantBand { Name = "NNTB 3", LatitudeDegrees = 50f, HalfWidthDegrees = 2.2f, Color = Hex("#70402C"), ZoneMask = 0.12f, Softness = 0.38f });
            profile.Bands.Add(new GasGiantBand { Name = "NNTZ 3", LatitudeDegrees = 46f, HalfWidthDegrees = 2.8f, Color = Hex("#DDCBA8"), ZoneMask = 0.8f, Softness = 0.5f });
            profile.Bands.Add(new GasGiantBand { Name = "NNTB 2", LatitudeDegrees = 40f, HalfWidthDegrees = 2.0f, Color = Hex("#683722"), ZoneMask = 0.1f, Softness = 0.35f });
            profile.Bands.Add(new GasGiantBand { Name = "NNTZ 2", LatitudeDegrees = 36f, HalfWidthDegrees = 2.2f, Color = Hex("#E3D2AE"), ZoneMask = 0.85f, Softness = 0.5f });
            profile.Bands.Add(new GasGiantBand { Name = "NNTB", LatitudeDegrees = 32f, HalfWidthDegrees = 2.2f, Color = Hex("#6E3A24"), ZoneMask = 0.1f, Softness = 0.35f });
            profile.Bands.Add(new GasGiantBand { Name = "NNTZ", LatitudeDegrees = 28f, HalfWidthDegrees = 2.0f, Color = Hex("#E8D8B4"), ZoneMask = 0.85f, Softness = 0.5f });
            profile.Bands.Add(new GasGiantBand { Name = "NTB", LatitudeDegrees = 24f, HalfWidthDegrees = 3.0f, Color = Hex("#713A24"), ZoneMask = 0.08f, Softness = 0.3f });
            profile.Bands.Add(new GasGiantBand { Name = "NTrZ", LatitudeDegrees = 19f, HalfWidthDegrees = 2.5f, Color = Hex("#EADBB8"), ZoneMask = 0.9f, Softness = 0.5f });
            profile.Bands.Add(new GasGiantBand { Name = "NEB", LatitudeDegrees = 13f, HalfWidthDegrees = 4.0f, Color = Hex("#5E2D1A"), ZoneMask = 0.05f, Softness = 0.28f });
            profile.Bands.Add(new GasGiantBand { Name = "NEBn", LatitudeDegrees = 8f, HalfWidthDegrees = 1.7f, Color = Hex("#6E3820"), ZoneMask = 0.1f, Softness = 0.3f });
            profile.Bands.Add(new GasGiantBand { Name = "NEB s edge", LatitudeDegrees = 5f, HalfWidthDegrees = 1.0f, Color = Hex("#8A5232"), ZoneMask = 0.25f, Softness = 0.45f });
            profile.Bands.Add(new GasGiantBand { Name = "EZn", LatitudeDegrees = 2.5f, HalfWidthDegrees = 2.2f, Color = Hex("#D8B57A"), ZoneMask = 0.65f, Softness = 0.5f });
            profile.Bands.Add(new GasGiantBand { Name = "EZ", LatitudeDegrees = -1.5f, HalfWidthDegrees = 3.2f, Color = Hex("#E9CB8F"), ZoneMask = 0.82f, Softness = 0.5f });
            profile.Bands.Add(new GasGiantBand { Name = "SEBn", LatitudeDegrees = -5.5f, HalfWidthDegrees = 2.0f, Color = Hex("#6B3520"), ZoneMask = 0.08f, Softness = 0.3f });
            profile.Bands.Add(new GasGiantBand { Name = "SEB", LatitudeDegrees = -11f, HalfWidthDegrees = 4.0f, Color = Hex("#592A18"), ZoneMask = 0.04f, Softness = 0.28f });
            profile.Bands.Add(new GasGiantBand { Name = "SEBs", LatitudeDegrees = -16f, HalfWidthDegrees = 1.8f, Color = Hex("#7A4228"), ZoneMask = 0.1f, Softness = 0.3f });
            profile.Bands.Add(new GasGiantBand { Name = "STrZ", LatitudeDegrees = -20.5f, HalfWidthDegrees = 2.8f, Color = Hex("#E9D9B6"), ZoneMask = 0.9f, Softness = 0.5f });
            profile.Bands.Add(new GasGiantBand { Name = "GRS moat", LatitudeDegrees = -24.5f, HalfWidthDegrees = 2.0f, Color = Hex("#7D4A34"), ZoneMask = 0.15f, Softness = 0.4f });
            profile.Bands.Add(new GasGiantBand { Name = "STB", LatitudeDegrees = -28.5f, HalfWidthDegrees = 2.5f, Color = Hex("#6A3824"), ZoneMask = 0.08f, Softness = 0.3f });
            profile.Bands.Add(new GasGiantBand { Name = "STZ", LatitudeDegrees = -33f, HalfWidthDegrees = 2.6f, Color = Hex("#E2D1AC"), ZoneMask = 0.85f, Softness = 0.5f });
            profile.Bands.Add(new GasGiantBand { Name = "SSTB", LatitudeDegrees = -38.5f, HalfWidthDegrees = 2.2f, Color = Hex("#6E3D29"), ZoneMask = 0.1f, Softness = 0.35f });
            profile.Bands.Add(new GasGiantBand { Name = "SSTZ", LatitudeDegrees = -43f, HalfWidthDegrees = 2.6f, Color = Hex("#D9C8A6"), ZoneMask = 0.8f, Softness = 0.5f });
            profile.Bands.Add(new GasGiantBand { Name = "S Temperate Belt", LatitudeDegrees = -49f, HalfWidthDegrees = 2.0f, Color = Hex("#75432D"), ZoneMask = 0.12f, Softness = 0.35f });
            profile.Bands.Add(new GasGiantBand { Name = "S Temperate Zone", LatitudeDegrees = -54f, HalfWidthDegrees = 3.0f, Color = Hex("#C9B99C"), ZoneMask = 0.7f, Softness = 0.55f });
            profile.Bands.Add(new GasGiantBand { Name = "S Polar Belt", LatitudeDegrees = -62f, HalfWidthDegrees = 3.5f, Color = Hex("#6C5A50"), ZoneMask = 0.2f, Softness = 0.6f });
            profile.Bands.Add(new GasGiantBand { Name = "South Polar Hood", LatitudeDegrees = -78f, HalfWidthDegrees = 11f, Color = Hex("#9C9184"), ZoneMask = 0.25f, Softness = 0.75f });

            // Thin filament bands: the sharp bright/dark lanes along strong jets.
            profile.Bands.Add(new GasGiantBand { Name = "NEB filament", LatitudeDegrees = 6.5f, HalfWidthDegrees = 0.6f, Color = Hex("#A0703F"), ZoneMask = 0.3f, Softness = 0.5f });
            profile.Bands.Add(new GasGiantBand { Name = "SEB filament", LatitudeDegrees = -7.8f, HalfWidthDegrees = 0.7f, Color = Hex("#8A5232"), ZoneMask = 0.3f, Softness = 0.5f });
            profile.Bands.Add(new GasGiantBand { Name = "NTrZ filament", LatitudeDegrees = 17f, HalfWidthDegrees = 0.5f, Color = Hex("#B08050"), ZoneMask = 0.5f, Softness = 0.55f });
            profile.Bands.Add(new GasGiantBand { Name = "STrZ filament", LatitudeDegrees = -18.5f, HalfWidthDegrees = 0.5f, Color = Hex("#B08050"), ZoneMask = 0.5f, Softness = 0.55f });
            profile.Bands.Add(new GasGiantBand { Name = "NTB filament", LatitudeDegrees = 34f, HalfWidthDegrees = 0.5f, Color = Hex("#9A5A3A"), ZoneMask = 0.3f, Softness = 0.5f });
            profile.Bands.Add(new GasGiantBand { Name = "STB filament", LatitudeDegrees = -31f, HalfWidthDegrees = 0.5f, Color = Hex("#9A5A3A"), ZoneMask = 0.3f, Softness = 0.5f });

            // Jovian zonal winds: equatorial superrotation, alternating mid-latitude
            // jets and quiet poles. Speeds are m/s in the physical frame.
            profile.Winds.Add(new GasGiantWindSample { LatitudeDegrees = -90f, SpeedMps = 0f });
            profile.Winds.Add(new GasGiantWindSample { LatitudeDegrees = -70f, SpeedMps = -18f });
            profile.Winds.Add(new GasGiantWindSample { LatitudeDegrees = -60f, SpeedMps = 22f });
            profile.Winds.Add(new GasGiantWindSample { LatitudeDegrees = -50f, SpeedMps = -28f });
            profile.Winds.Add(new GasGiantWindSample { LatitudeDegrees = -40f, SpeedMps = 55f });
            profile.Winds.Add(new GasGiantWindSample { LatitudeDegrees = -33f, SpeedMps = -38f });
            profile.Winds.Add(new GasGiantWindSample { LatitudeDegrees = -27f, SpeedMps = 62f });
            profile.Winds.Add(new GasGiantWindSample { LatitudeDegrees = -20f, SpeedMps = -18f });
            profile.Winds.Add(new GasGiantWindSample { LatitudeDegrees = -14f, SpeedMps = 30f });
            profile.Winds.Add(new GasGiantWindSample { LatitudeDegrees = -8f, SpeedMps = -12f });
            profile.Winds.Add(new GasGiantWindSample { LatitudeDegrees = 0f, SpeedMps = 105f });
            profile.Winds.Add(new GasGiantWindSample { LatitudeDegrees = 8f, SpeedMps = 58f });
            profile.Winds.Add(new GasGiantWindSample { LatitudeDegrees = 15f, SpeedMps = -42f });
            profile.Winds.Add(new GasGiantWindSample { LatitudeDegrees = 21f, SpeedMps = 24f });
            profile.Winds.Add(new GasGiantWindSample { LatitudeDegrees = 27f, SpeedMps = -58f });
            profile.Winds.Add(new GasGiantWindSample { LatitudeDegrees = 34f, SpeedMps = 36f });
            profile.Winds.Add(new GasGiantWindSample { LatitudeDegrees = 42f, SpeedMps = -26f });
            profile.Winds.Add(new GasGiantWindSample { LatitudeDegrees = 55f, SpeedMps = 12f });
            profile.Winds.Add(new GasGiantWindSample { LatitudeDegrees = 70f, SpeedMps = -8f });
            profile.Winds.Add(new GasGiantWindSample { LatitudeDegrees = 90f, SpeedMps = 0f });

            profile.Vortices.Add(new GasGiantVortex
            {
                Name = "Great Salmon Spot",
                LatitudeDegrees = -22f,
                LongitudeDegrees = 30f,
                RadiusLatDegrees = 8.5f,
                RadiusLonDegrees = 15f,
                Spin = 1f,
                CoreColor = Hex("#E8A985"),
                CollarColor = Hex("#74351F"),
                Intensity = 1.0f,
                CollarWidth = 0.38f,
                DriftDegreesPerDay = 1.2f,
                SwirlStrength = 1.0f,
            });
            profile.Vortices.Add(new GasGiantVortex
            {
                Name = "Oval BA",
                LatitudeDegrees = -33f,
                LongitudeDegrees = 120f,
                RadiusLatDegrees = 4.2f,
                RadiusLonDegrees = 7.5f,
                Spin = -1f,
                CoreColor = Hex("#C98F72"),
                CollarColor = Hex("#8A4A32"),
                Intensity = 0.9f,
                CollarWidth = 0.36f,
                DriftDegreesPerDay = 0.9f,
                SwirlStrength = 0.7f,
            });
            profile.Vortices.Add(new GasGiantVortex
            {
                Name = "White Oval 1",
                LatitudeDegrees = -36f,
                LongitudeDegrees = 60f,
                RadiusLatDegrees = 2.2f,
                RadiusLonDegrees = 3.8f,
                Spin = -1f,
                CoreColor = Hex("#FFF4E2"),
                CollarColor = Hex("#9A5A3A"),
                Intensity = 0.85f,
                CollarWidth = 0.36f,
                DriftDegreesPerDay = 1.0f,
                SwirlStrength = 0.35f,
            });
            profile.Vortices.Add(new GasGiantVortex
            {
                Name = "White Oval 2",
                LatitudeDegrees = -40f,
                LongitudeDegrees = 20f,
                RadiusLatDegrees = 1.8f,
                RadiusLonDegrees = 3.2f,
                Spin = -1f,
                CoreColor = Hex("#FFF4E2"),
                CollarColor = Hex("#9A5A3A"),
                Intensity = 0.75f,
                CollarWidth = 0.36f,
                DriftDegreesPerDay = 1.1f,
                SwirlStrength = 0.3f,
            });
            profile.Vortices.Add(new GasGiantVortex
            {
                Name = "White Oval 3",
                LatitudeDegrees = -31f,
                LongitudeDegrees = -80f,
                RadiusLatDegrees = 1.6f,
                RadiusLonDegrees = 2.8f,
                Spin = -1f,
                CoreColor = Hex("#FFF0D8"),
                CollarColor = Hex("#8A5232"),
                Intensity = 0.7f,
                CollarWidth = 0.35f,
                DriftDegreesPerDay = 0.8f,
                SwirlStrength = 0.25f,
            });
            profile.Vortices.Add(new GasGiantVortex
            {
                Name = "Brown Barge 1",
                LatitudeDegrees = 15f,
                LongitudeDegrees = -50f,
                RadiusLatDegrees = 2.0f,
                RadiusLonDegrees = 5.0f,
                Spin = 1f,
                CoreColor = Hex("#3A2A20"),
                CollarColor = Hex("#6E3A24"),
                Intensity = 0.8f,
                CollarWidth = 0.4f,
                DriftDegreesPerDay = -0.7f,
                SwirlStrength = 0.4f,
            });
            profile.Vortices.Add(new GasGiantVortex
            {
                Name = "Brown Barge 2",
                LatitudeDegrees = 13f,
                LongitudeDegrees = 70f,
                RadiusLatDegrees = 1.5f,
                RadiusLonDegrees = 3.8f,
                Spin = 1f,
                CoreColor = Hex("#35443F"),
                CollarColor = Hex("#6E3A24"),
                Intensity = 0.65f,
                CollarWidth = 0.4f,
                DriftDegreesPerDay = -0.5f,
                SwirlStrength = 0.3f,
            });
            profile.Vortices.Add(new GasGiantVortex
            {
                Name = "Brown Barge 3",
                LatitudeDegrees = 16.5f,
                LongitudeDegrees = -105f,
                RadiusLatDegrees = 1.2f,
                RadiusLonDegrees = 3.0f,
                Spin = 1f,
                CoreColor = Hex("#3A2A20"),
                CollarColor = Hex("#6E3A24"),
                Intensity = 0.55f,
                CollarWidth = 0.4f,
                DriftDegreesPerDay = -0.4f,
                SwirlStrength = 0.25f,
            });

            return profile;
        }

        public GasGiantProfile Clone()
        {
            var copy = new GasGiantProfile
            {
                VisualEnabled = VisualEnabled,
                SurfaceMap = SurfaceMap,
                SurfaceMapStrength = SurfaceMapStrength,
                SurfaceMapLongitudeOffset = SurfaceMapLongitudeOffset,
                SurfaceMapBump = SurfaceMapBump,
                OblatenessRatio = OblatenessRatio,
                NorthPoleColor = NorthPoleColor,
                SouthPoleColor = SouthPoleColor,
                HazeColor = HazeColor,
                RimColor = RimColor,
                VisualSpinPeriodSeconds = VisualSpinPeriodSeconds,
                PhasePeriodSeconds = PhasePeriodSeconds,
                VortexSpinPeriodSeconds = VortexSpinPeriodSeconds,
                ShearExaggeration = ShearExaggeration,
                LayerSpeedDelta = LayerSpeedDelta,
                LayerBlend = LayerBlend,
                WarpAmplitude = WarpAmplitude,
                WarpScale = WarpScale,
                TurbulenceScale = TurbulenceScale,
                TurbulenceAnisotropy = TurbulenceAnisotropy,
                HazeDensity = HazeDensity,
                LimbDarkening = LimbDarkening,
                WrapDiffuse = WrapDiffuse,
                RimIntensity = RimIntensity,
                Saturation = Saturation,
                CloudBump = CloudBump,
                Contrast = Contrast,
                PolarTint = PolarTint,
                DetailStrength = DetailStrength,
                MinScreenRadiusPixels = MinScreenRadiusPixels,
                BandLutWidth = BandLutWidth,
                WindLutWidth = WindLutWidth,
                MeshResolution = MeshResolution,
            };

            copy.Bands = new List<GasGiantBand>(Bands.Count);
            for (int i = 0; i < Bands.Count; i++)
            {
                GasGiantBand b = Bands[i];
                copy.Bands.Add(new GasGiantBand
                {
                    Name = b.Name,
                    LatitudeDegrees = b.LatitudeDegrees,
                    HalfWidthDegrees = b.HalfWidthDegrees,
                    Color = b.Color,
                    ZoneMask = b.ZoneMask,
                    Softness = b.Softness,
                });
            }

            copy.Winds = new List<GasGiantWindSample>(Winds.Count);
            for (int i = 0; i < Winds.Count; i++)
            {
                GasGiantWindSample w = Winds[i];
                copy.Winds.Add(new GasGiantWindSample
                {
                    LatitudeDegrees = w.LatitudeDegrees,
                    SpeedMps = w.SpeedMps,
                });
            }

            copy.Vortices = new List<GasGiantVortex>(Vortices.Count);
            for (int i = 0; i < Vortices.Count; i++)
            {
                GasGiantVortex v = Vortices[i];
                copy.Vortices.Add(new GasGiantVortex
                {
                    Name = v.Name,
                    LatitudeDegrees = v.LatitudeDegrees,
                    LongitudeDegrees = v.LongitudeDegrees,
                    RadiusLatDegrees = v.RadiusLatDegrees,
                    RadiusLonDegrees = v.RadiusLonDegrees,
                    Spin = v.Spin,
                    CoreColor = v.CoreColor,
                    CollarColor = v.CollarColor,
                    Intensity = v.Intensity,
                    CollarWidth = v.CollarWidth,
                    DriftDegreesPerDay = v.DriftDegreesPerDay,
                    SwirlStrength = v.SwirlStrength,
                });
            }

            return copy;
        }

        public void Validate()
        {
            if (Bands == null) Bands = new List<GasGiantBand>();
            if (Winds == null) Winds = new List<GasGiantWindSample>();
            if (Vortices == null) Vortices = new List<GasGiantVortex>();

            if (Bands.Count == 0)
            {
                GasGiantProfile defaults = CreateAeolus();
                Bands = defaults.Bands;
                Winds = defaults.Winds;
                Vortices = defaults.Vortices;
            }

            VisualSpinPeriodSeconds = Mathf.Max(1f, VisualSpinPeriodSeconds);
            PhasePeriodSeconds = Mathf.Max(1f, PhasePeriodSeconds);
            VortexSpinPeriodSeconds = Mathf.Max(1f, VortexSpinPeriodSeconds);
            ShearExaggeration = Mathf.Clamp(ShearExaggeration, 0f, 80f);
            LayerSpeedDelta = Mathf.Clamp(LayerSpeedDelta, 0f, 0.5f);
            LayerBlend = Mathf.Clamp(LayerBlend, 0f, 0.5f);
            OblatenessRatio = Mathf.Clamp(OblatenessRatio, 0.8f, 1f);
            BandLutWidth = Mathf.Clamp(BandLutWidth, 32, 8192);
            WindLutWidth = Mathf.Clamp(WindLutWidth, 32, 2048);
            MeshResolution = Mathf.Clamp(MeshResolution, 16, 256);
            MinScreenRadiusPixels = Mathf.Clamp(MinScreenRadiusPixels, 0f, 8f);
            CloudBump = Mathf.Clamp(CloudBump, 0f, 2f);
            Contrast = Mathf.Clamp(Contrast, 0.6f, 1.8f);
            PolarTint = Mathf.Clamp(PolarTint, 0f, 1f);
            DetailStrength = Mathf.Clamp(DetailStrength, 0f, 2f);
            SurfaceMapStrength = Mathf.Clamp01(SurfaceMapStrength);
            SurfaceMapLongitudeOffset = Mathf.Clamp(SurfaceMapLongitudeOffset, -0.5f, 0.5f);
            SurfaceMapBump = Mathf.Clamp01(SurfaceMapBump);

            Winds.Sort((a, b) => a.LatitudeDegrees.CompareTo(b.LatitudeDegrees));
            if (Winds.Count == 0)
            {
                Winds.Add(new GasGiantWindSample { LatitudeDegrees = -90f, SpeedMps = 0f });
                Winds.Add(new GasGiantWindSample { LatitudeDegrees = 90f, SpeedMps = 0f });
            }
        }

        /// <summary>
        /// Build two 1D LUTs. The band LUT is RGBA: RGB = linear albedo,
        /// A = zone mask. The wind LUT is RGBA float: R = zonal wind m/s,
        /// G = turbulence amplitude, B = belt/zone boundary mask, A = haze.
        /// The LUT is indexed by PLANETOCENTRIC latitude, matching asin(p.z)
        /// in the shader; the profile itself stores planetographic latitudes.
        /// </summary>
        public void BuildLuts(out Vector4[] bandPixels, out Vector4[] windPixels)
        {
            Validate();

            int bw = Mathf.Max(32, BandLutWidth);
            int ww = Mathf.Max(32, WindLutWidth);
            bandPixels = new Vector4[bw];
            windPixels = new Vector4[ww];

            for (int i = 0; i < bw; i++)
            {
                float latC = (((i + 0.5f) / bw) - 0.5f) * Mathf.PI;
                float latG = PlanetographicFromPlanetocentric(latC) * Mathf.Rad2Deg;
                Vector3 color = EvaluateBandColor(latG, out float zone);
                bandPixels[i] = new Vector4(color.x, color.y, color.z, zone);
            }

            for (int i = 0; i < ww; i++)
            {
                float latC = (((i + 0.5f) / ww) - 0.5f) * Mathf.PI;
                float latG = PlanetographicFromPlanetocentric(latC) * Mathf.Rad2Deg;
                float wind = EvaluateWind(latG);
                float zone = EvaluateZone(latG);

                float zoneBefore = EvaluateZone(latG - 0.5f);
                float zoneAfter = EvaluateZone(latG + 0.5f);
                float gradient = Mathf.Abs(zoneAfter - zoneBefore);
                float boundary = Mathf.Clamp01(gradient * 14f);
                float turbulence = Mathf.Clamp01(0.22f + (0.85f * boundary));
                float haze = Mathf.Clamp01(HazeDensity * (0.25f + (0.9f * zone)));
                windPixels[i] = new Vector4(wind, turbulence, boundary, haze);
            }
        }

        public float EvaluateWind(float planetographicLatitudeDegrees)
        {
            if (Winds == null || Winds.Count == 0)
            {
                return 0f;
            }

            float lat = planetographicLatitudeDegrees;
            if (lat <= Winds[0].LatitudeDegrees)
            {
                return Winds[0].SpeedMps;
            }

            for (int i = 0; i < Winds.Count - 1; i++)
            {
                GasGiantWindSample a = Winds[i];
                GasGiantWindSample b = Winds[i + 1];
                if (lat > b.LatitudeDegrees)
                {
                    continue;
                }

                float span = Mathf.Max(1e-4f, b.LatitudeDegrees - a.LatitudeDegrees);
                float t = Mathf.Clamp01((lat - a.LatitudeDegrees) / span);
                t = t * t * (3f - (2f * t));
                return Mathf.Lerp(a.SpeedMps, b.SpeedMps, t);
            }

            return Winds[Winds.Count - 1].SpeedMps;
        }

        public Vector3 EvaluateBandColor(float planetographicLatitudeDegrees, out float zone)
        {
            // The gaps between bands must not fall back to the blue polar hoods:
            // that was what made Aeolus read as an ice planet. The base is a warm
            // mid-latitude haze; the explicit polar bands override it near ±60°.
            Color baseColor = Color.Lerp(
                new Color(0.55f, 0.38f, 0.22f, 1f),
                new Color(0.72f, 0.60f, 0.42f, 1f),
                Mathf.InverseLerp(-90f, 90f, planetographicLatitudeDegrees));
            Vector3 baseLinear = ToLinear(baseColor);
            const float baseWeight = 0.025f;
            float baseZone = 0.2f;

            Vector3 accum = Vector3.zero;
            float zoneAccum = 0f;
            float weight = 0f;

            if (Bands != null)
            {
                for (int i = 0; i < Bands.Count; i++)
                {
                    GasGiantBand band = Bands[i];
                    float half = Mathf.Max(0.01f, band.HalfWidthDegrees);
                    float d = Mathf.Abs(planetographicLatitudeDegrees - band.LatitudeDegrees) / half;
                    if (d >= 1f)
                    {
                        continue;
                    }

                    float soft = Mathf.Clamp(band.Softness, 0.01f, 1f);
                    float edge0 = 1f - soft;
                    float edge1 = 1f;
                    float w = 1f - Mathf.SmoothStep(edge0, edge1, d);
                    if (d <= edge0)
                    {
                        w = 1f;
                    }

                    if (w <= 0f)
                    {
                        continue;
                    }

                    accum += ToLinear(band.Color) * w;
                    zoneAccum += band.ZoneMask * w;
                    weight += w;
                }
            }

            Vector3 linear = (baseLinear * baseWeight + accum) / Mathf.Max(1e-5f, baseWeight + weight);
            zone = (baseZone * baseWeight + zoneAccum) / Mathf.Max(1e-5f, baseWeight + weight);
            return linear;
        }

        private float EvaluateZone(float planetographicLatitudeDegrees)
        {
            EvaluateBandColor(planetographicLatitudeDegrees, out float zone);
            return zone;
        }

        /// <summary>
        /// Planetographic latitude (normal-based, as in the Jupiter brief) to
        /// planetocentric latitude (radius-based, asin(p.z) in the shader):
        /// tan(phi_c) = tan(phi_g) * k^2, k = polar/equatorial.
        /// </summary>
        public float PlanetocentricFromPlanetographic(float planetographicDegrees)
        {
            float k = Mathf.Clamp(OblatenessRatio, 0.5f, 1f);
            float phi = planetographicDegrees * Mathf.Deg2Rad;
            return Mathf.Atan(Mathf.Tan(phi) * k * k) * Mathf.Rad2Deg;
        }

        public float PlanetographicFromPlanetocentric(float planetocentricDegrees)
        {
            float k = Mathf.Clamp(OblatenessRatio, 0.5f, 1f);
            float phi = planetocentricDegrees * Mathf.Deg2Rad;
            return Mathf.Atan(Mathf.Tan(phi) / (k * k)) * Mathf.Rad2Deg;
        }

        private static Vector3 ToLinear(Color color)
        {
            Color linear = color.linear;
            return new Vector3(linear.r, linear.g, linear.b);
        }

        private static Color Hex(string value)
        {
            return ColorUtility.TryParseHtmlString(value, out Color color) ? color : Color.magenta;
        }
    }
}







