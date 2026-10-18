using System.Collections.Generic;
using UnityEngine;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Celestial body component. Attach to a GameObject; the scene's Transform
    /// hierarchy is the system's orbital hierarchy (a hierarchy parent is the orbital
    /// parent). The tree root (star) is a GameObject with no parent among the
    /// authorings. The fields are read by SystemBlueprint (pure data) — all system
    /// assembly logic lives there and is unit-tested; here there is only serialization.
    /// </summary>
    public sealed class BodyAuthoring : MonoBehaviour
    {
        [Header("Физика (СИ)")]
        public double StandardGravitationalParameter = 1d;
        public double Radius = 1d;
        public double CrashToleranceMps = 5d;

        [Header("Трение поверхности (см. SurfaceMotion)")]
        public double SurfaceStaticFrictionMu = 0.7d;
        public double SurfaceKineticFrictionMu = 0.5d;

        [Header("Кеплеровы элементы (относительно родителя; для звезды не используются)")]
        public double SemiMajorAxis = 1e11;
        public double Eccentricity;
        public double InclinationDegrees;
        public double LongitudeOfAscendingNodeDegrees;
        public double ArgumentOfPeriapsisDegrees;
        public double MeanAnomalyAtEpochDegrees;
        public double EpochTimeSeconds;

        [Header("Вращение вокруг оси")]
        [Tooltip("Период вращения, с. Игнорируется при включённом приливном захвате.")]
        public double RotationPeriodSeconds;

        [Tooltip("Приливный захват 1:1: период = орбитальному (по приведённой массе родитель+поддерево), к родителю всегда одна сторона (долгота 0 в эпоху). Ручные период и нулевой меридиан игнорируются.")]
        public bool TidallyLocked;

        public double PrimeMeridianOffsetDegrees;
        public Vector3d NorthPoleDirection = new Vector3d(0d, 0d, 1d);

        [Header("Профили-ассеты (пресеты)")]
        [Tooltip("Пресет рельефа (форма/цвет/палитра). null = гладкая сфера. Зерно шума — TerrainSeed (per-body).")]
        public TerrainProfileAsset TerrainPreset;

        [Tooltip("Пресет атмосферы (физика+визуал). null = атмосферы нет.")]
        public AtmosphereProfileAsset AtmospherePreset;

        [Tooltip("Пресет облаков (см. PlanetCloudsView). null = облаков нет.")]
        public CloudProfileAsset CloudsPreset;

        [Tooltip("Пресет декора местности (трава/камни/деревья). null = декора нет.")]
        public GroundDecorProfileAsset GroundDecorPreset;

        [Tooltip("Зерно шума рельефа: одинаковый seed = одинаковый рельеф.")]
        public int TerrainSeed;

        [Tooltip("Ровные площадки под базы (аналог PQS-модов FlattenArea в KSP). " +
            "Каждая: ровное ядро + гладкий фартук. Список идёт в чертёж системы, " +
            "поэтому правка одинаково видна и в превью, и в Play.")]
        public List<TerrainModifier> TerrainPads = new List<TerrainModifier>();

        public BodyBlueprint ToBlueprint(int parentIndex)
        {
            return new BodyBlueprint
            {
                Name = gameObject.name,
                StandardGravitationalParameter = StandardGravitationalParameter,
                Radius = Radius,
                CrashToleranceMps = CrashToleranceMps,
                SurfaceStaticFrictionMu = SurfaceStaticFrictionMu,
                SurfaceKineticFrictionMu = SurfaceKineticFrictionMu,
                SemiMajorAxis = SemiMajorAxis,
                Eccentricity = Eccentricity,
                InclinationDegrees = InclinationDegrees,
                LongitudeOfAscendingNodeDegrees = LongitudeOfAscendingNodeDegrees,
                ArgumentOfPeriapsisDegrees = ArgumentOfPeriapsisDegrees,
                MeanAnomalyAtEpochDegrees = MeanAnomalyAtEpochDegrees,
                EpochTimeSeconds = EpochTimeSeconds,
                RotationPeriodSeconds = RotationPeriodSeconds,
                TidallyLocked = TidallyLocked,
                PrimeMeridianOffsetDegrees = PrimeMeridianOffsetDegrees,
                NorthPoleX = NorthPoleDirection.X,
                NorthPoleY = NorthPoleDirection.Y,
                NorthPoleZ = NorthPoleDirection.Z,
                Atmosphere = ResolveAtmosphere(),
                Clouds = ResolveClouds(),
                Terrain = ResolveTerrain(),
                TerrainSeed = TerrainSeed,
                TerrainModifiers = ResolveTerrainModifiers(),
                ParentIndex = parentIndex
            };
        }

        /// <summary>
        /// The body's flat pads into the blueprint. Copied rather than passed as a
        /// reference to the scene list: the blueprint is flat data that survives a system
        /// rebuild, while the list is serialized and can be changed from the inspector at
        /// any moment.
        /// </summary>
        private TerrainModifier[] ResolveTerrainModifiers()
        {
            if (TerrainPads == null || TerrainPads.Count == 0)
            {
                return null;
            }

            var copy = new TerrainModifier[TerrainPads.Count];
            for (int i = 0; i < TerrainPads.Count; i++)
            {
                copy[i] = TerrainPads[i];
            }

            return copy;
        }

        /// <summary>Runtime terrain profile from the preset asset; null — smooth sphere.</summary>
        private TerrainProfile ResolveTerrain()
        {
            return TerrainPreset != null && TerrainPreset.Profile != null
                ? TerrainPreset.Profile.Clone()
                : null;
        }

        /// <summary>Runtime atmosphere profile from the preset asset; null — no atmosphere.</summary>
        private AtmosphereProfile ResolveAtmosphere()
        {
            return AtmospherePreset != null && AtmospherePreset.Profile != null
                ? AtmospherePreset.Profile.Clone()
                : null;
        }

        private CloudProfile ResolveClouds()
        {
            return CloudsPreset != null && CloudsPreset.Profile != null
                ? CloudsPreset.Profile.Clone()
                : null;
        }

        /// <summary>
        /// Copy the terrain preset parameters into a live HeightfieldTerrain (live tuning
        /// in Play mode). The single place that copies them, not duplicated across callers.
        /// </summary>
        public void ApplyToTerrain(HeightfieldTerrain terrain)
        {
            if (terrain == null || TerrainPreset == null || TerrainPreset.Profile == null)
            {
                return;
            }

            terrain.ApplyProfile(TerrainPreset.Profile, TerrainSeed);
        }
    }
}

