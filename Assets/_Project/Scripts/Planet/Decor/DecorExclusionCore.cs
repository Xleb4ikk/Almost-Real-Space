using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Galilego.Universe
{
    // Чистое ядро таблицы зон декора: без Unity-рендерера, Time и сцены.
    // Подключается и стендом (Tools/P1bTests), и игрой. Реализация сборки и
    // отложенного освобождения — в DecorExclusion.cs (тот же partial class).
    /// <summary>
    /// Зона, где декор (деревья, трава, камни) не растёт: повёрнутый прямоугольник
    /// на поверхности. Только blittable — читается из Burst-джоб.
    /// Центр и оси — единичные body-fixed векторы, полуразмеры — угловые (м / R).
    /// </summary>
    public struct DecorExclusionData
    {
        public double3 Center;
        public double3 AxisX;
        public double3 AxisZ;
        public double HalfX;
        public double HalfZ;

        /// <summary>Малый прямоугольник (мягкий отступ, трава): HalfX/HalfZ + узкая зона дорог.</summary>
        public double SoftHalfX;
        public double SoftHalfZ;
    }

    public static partial class DecorExclusionTable
    {
        public static readonly NativeArray<DecorExclusionData> Empty =
            new NativeArray<DecorExclusionData>(0, Allocator.Persistent);

        // ---------- проверка (Burst) ----------

        public static bool IsExcluded(NativeArray<DecorExclusionData> zones, double3 direction)
        {
            if (!zones.IsCreated)
            {
                return false;
            }

            for (int i = 0; i < zones.Length; i++)
            {
                DecorExclusionData z = zones[i];
                if (math.dot(direction, z.Center) < 0.5d)
                {
                    continue;
                }

                double3 d = direction - z.Center;
                if (math.abs(math.dot(d, z.AxisX)) <= z.HalfX && math.abs(math.dot(d, z.AxisZ)) <= z.HalfZ)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Проверка по малому прямоугольнику — трава у края дороги (отступ 0.3 м).</summary>
        public static bool IsExcludedSoft(NativeArray<DecorExclusionData> zones, double3 direction)
        {
            if (!zones.IsCreated)
            {
                return false;
            }

            for (int i = 0; i < zones.Length; i++)
            {
                DecorExclusionData z = zones[i];
                if (math.dot(direction, z.Center) < 0.5d)
                {
                    continue;
                }

                double3 d = direction - z.Center;
                if (math.abs(math.dot(d, z.AxisX)) <= z.SoftHalfX && math.abs(math.dot(d, z.AxisZ)) <= z.SoftHalfZ)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TableEqual(NativeArray<DecorExclusionData> a, NativeArray<DecorExclusionData> b)
        {
            int na = a.IsCreated ? a.Length : 0;
            int nb = b.IsCreated ? b.Length : 0;
            if (na != nb)
            {
                return false;
            }

            for (int i = 0; i < na; i++)
            {
                if (!Same(a[i], b[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Same(DecorExclusionData x, DecorExclusionData y)
        {
            return x.Center.Equals(y.Center) && x.AxisX.Equals(y.AxisX) && x.AxisZ.Equals(y.AxisZ)
                && x.HalfX == y.HalfX && x.HalfZ == y.HalfZ
                && x.SoftHalfX == y.SoftHalfX && x.SoftHalfZ == y.SoftHalfZ;
        }

        static partial void AfterRelease();   // реализация в DecorExclusion.cs (игра), в стенде — пусто

        public static void Release(ref NativeArray<DecorExclusionData> table)
        {
            if (table.IsCreated && table.Length > 0)
            {
                table.Dispose();
            }

            table = Empty;
            AfterRelease();
        }
    }
}
