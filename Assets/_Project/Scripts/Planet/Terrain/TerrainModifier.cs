using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// FLAT PAD AT A POINT ON THE PLANET — analogous to KSP's PQS FlattenArea mod,
    /// where the engine forcibly smooths the noise under each pad.
    ///
    /// The edit is applied to the SINGLE relief shape function
    /// (TerrainNoise.SampleHeight), so it automatically reaches the render mesh,
    /// ship and player physics, normals, decor, water depth and the editor preview.
    /// Visual/physics parity is not "maintained" but cannot drift: there is nowhere
    /// for a mismatch to arise.
    ///
    /// Shape: a flat core of radius InnerRadius and a smooth apron
    /// InnerRadius→OuterRadius. The apron is mandatory — a hard disc would give a
    /// visible wall at the edge and a kink in the normals.
    /// </summary>
    [Serializable]
    public struct TerrainModifier
    {
        [Tooltip("Широта центра площадки, градусы.")]
        public double LatitudeDegrees;

        [Tooltip("Долгота центра площадки, градусы.")]
        public double LongitudeDegrees;

        [Tooltip("Радиус ровного ядра, м. Внутри него высота ТОЧНО целевая. " +
            "Держи ядро заметно шире 240 м: нормаль рельефа усредняется примерно по 114 м " +
            "(столько берёт посадка корабля), и на ядре уже 100 м здания на краю встанут " +
            "с наклоном в доли градуса.")]
        public double InnerRadiusMeters;

        [Tooltip("Радиус, на котором влияние сходит на нет, м. Фартук InnerRadius→OuterRadius.")]
        public double OuterRadiusMeters;

        [Tooltip("Высота площадки, м. Задаётся автора при размещении, а не вручную: " +
            "иначе площадка либо повиснет, либо уйдёт в яму.")]
        public double TargetHeightMeters;

        [Tooltip("Площадка перебивает кламп уровня моря. Нужно только для раскопок НИЖЕ уровня моря " +
            "(сухой док, котлован): иначе GetHeightMeters поднимет их обратно до воды. " +
            "Обычная площадка над водой работает и без этого флага.")]
        public bool OverridesSeaLevel;

        /// <summary>The modifier is off — it cannot "accidentally" spoil the relief.</summary>
        public bool IsActive =>
            OuterRadiusMeters > 0d && !double.IsNaN(TargetHeightMeters) && !double.IsInfinity(TargetHeightMeters);
    }

    /// <summary>
    /// The modifier's precomputed form for the hot loop: direction instead of lat/lon
    /// and cosines of angular radii instead of metres. Everything computable once at
    /// build time is precomputed — SampleHeight is left with one dot product and one
    /// comparison per modifier.
    ///
    /// Blittable by construction (double3 + doubles + a byte), so it lives in a
    /// NativeArray and passes through the renderer's Burst job with no managed
    /// references.
    /// </summary>
    public struct TerrainModifierData
    {
        /// <summary>Unit body-fixed direction of the pad centre.</summary>
        public double3 Direction;

        /// <summary>cos(OuterRadius / Radius) — beyond this boundary the modifier has no effect.</summary>
        public double CosOuter;

        /// <summary>cos(InnerRadius / Radius) — inside this value the influence is full.</summary>
        public double CosInner;

        /// <summary>Target height, m. Divided by the amplitude only for affected vertices.</summary>
        public double TargetHeightMeters;

        /// <summary>See TerrainModifier.OverridesSeaLevel.</summary>
        public byte OverridesSeaLevel;
    }

    /// <summary>Modifier table building and comparison — shared logic for caches.</summary>
    public static class TerrainModifiers
    {
        /// <summary>
        /// An EMPTY BUT VALID table. Shared across all bodies and never released.
        ///
        /// It is needed because Unity's job scheduler requires EVERY container field of
        /// a job struct to be valid at Schedule time, regardless of whether the code reads
        /// it — a default(NativeArray) fails the renderer outright.
        ///
        /// Zero length, so sharing it is safe: there is no memory behind it, and job
        /// safety for empty containers is a no-op. Release rule: a zero-length table is
        /// always THIS object, and Dispose does not touch it (see Release).
        /// </summary>
        public static readonly NativeArray<TerrainModifierData> Empty =
            new NativeArray<TerrainModifierData>(0, Allocator.Persistent);

        /// <summary>Release the table. A zero-length one is left alone — it is the shared
        /// Empty, and disposing it would break every body at once.</summary>
        public static void Release(ref NativeArray<TerrainModifierData> table)
        {
            if (table.IsCreated && table.Length > 0)
            {
                table.Dispose();
            }

            table = Empty;
        }

        /// <summary>Rebuild the table for a body. Returns <see cref="Empty"/> when there
        /// are no active modifiers: SampleHeight then behaves exactly as before (the
        /// contract "empty parameters give bit-for-bit legacy fBm", on which all relief
        /// tests rest).</summary>
        public static NativeArray<TerrainModifierData> Build(
            System.Collections.Generic.IList<TerrainModifier> source, double bodyRadiusMeters)
        {
            int count = 0;
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    if (source[i].IsActive)
                    {
                        count++;
                    }
                }
            }

            if (count == 0 || bodyRadiusMeters <= 0d)
            {
                return Empty;
            }

            var table = new NativeArray<TerrainModifierData>(count, Allocator.Persistent);
            int slot = 0;
            for (int i = 0; i < source.Count; i++)
            {
                TerrainModifier m = source[i];
                if (!m.IsActive)
                {
                    continue;
                }

                double lat = m.LatitudeDegrees * (Math.PI / 180d);
                double lon = m.LongitudeDegrees * (Math.PI / 180d);
                double cosLat = Math.Cos(lat);

                // The angular radius cannot exceed a quarter circumference: beyond that
                // cos starts growing again, and a "pad of radius 2R" would become a ring
                // on the far side of the planet. Cosines are computed from arc length,
                // because both the pad radius and the distance between points here are
                // surface metres.
                double inner = Math.Min(Math.Max(m.InnerRadiusMeters / bodyRadiusMeters, 0d), Math.PI * 0.5d);
                double outer = Math.Min(Math.Max(m.OuterRadiusMeters / bodyRadiusMeters, inner), Math.PI * 0.5d);

                table[slot++] = new TerrainModifierData
                {
                    Direction = new double3(cosLat * Math.Cos(lon), cosLat * Math.Sin(lon), Math.Sin(lat)),
                    CosOuter = Math.Cos(outer),
                    CosInner = Math.Cos(inner),
                    TargetHeightMeters = m.TargetHeightMeters,
                    OverridesSeaLevel = (byte)(m.OverridesSeaLevel ? 1 : 0)
                };
            }

            return table;
        }

        /// <summary>Semantic equality of tables. NativeArray == compares the pointer, not
        /// the data, so a semantically identical set from a different build would be seen
        /// as different and the render cache would rebuild needlessly (worse, the reverse:
        /// a real modifier change could go unnoticed).</summary>
        public static bool TableEqual(NativeArray<TerrainModifierData> a, NativeArray<TerrainModifierData> b)
        {
            if (!a.IsCreated || !b.IsCreated)
            {
                return a.Length == b.Length;
            }

            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                TerrainModifierData x = a[i];
                TerrainModifierData y = b[i];
                if (!x.Direction.Equals(y.Direction)
                    || x.CosOuter != y.CosOuter
                    || x.CosInner != y.CosInner
                    || x.TargetHeightMeters != y.TargetHeightMeters
                    || x.OverridesSeaLevel != y.OverridesSeaLevel)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Table signature for the system hash — so a modifier edit rebuilds the cache.</summary>
        public static void MixInto(ref long hash, NativeArray<TerrainModifierData> table)
        {
            hash = (hash * 31L) + table.Length;
            for (int i = 0; i < table.Length; i++)
            {
                TerrainModifierData m = table[i];
                hash = (hash * 31L) + BitConverter.DoubleToInt64Bits(m.CosOuter);
                hash = (hash * 31L) + BitConverter.DoubleToInt64Bits(m.CosInner);
                hash = (hash * 31L) + BitConverter.DoubleToInt64Bits(m.TargetHeightMeters);
                hash = (hash * 31L) + m.OverridesSeaLevel;
            }
        }
    }
}
