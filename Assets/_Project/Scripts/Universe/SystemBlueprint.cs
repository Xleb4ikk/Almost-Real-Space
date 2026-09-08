using System;
using System.Collections.Generic;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Описание одного тела системы: чистые данные без UnityEngine-типов
    /// (кроме Vector3d), сериализуемые и в Unity-инспекторе, и в тестах.
    /// Иерархия задается ParentIndex (-1 = корень-звезда, ровно один).
    /// </summary>
    [Serializable]
    public sealed class BodyBlueprint
    {
        public string Name = "Body";
        public double StandardGravitationalParameter = 1d;
        public double Radius = 1d;
        public double CrashToleranceMps = 5d;

        /// <summary>Трение поверхности (Кулон, см. SurfaceMotion): стик пока |a_t| ≤ μ_s·|a_n|.</summary>
        public double SurfaceStaticFrictionMu = 0.7d;

        public double SurfaceKineticFrictionMu = 0.5d;

        public double SemiMajorAxis = 1d;
        public double Eccentricity;
        public double InclinationDegrees;
        public double LongitudeOfAscendingNodeDegrees;
        public double ArgumentOfPeriapsisDegrees;
        public double MeanAnomalyAtEpochDegrees;
        public double EpochTimeSeconds;

        public double RotationPeriodSeconds;
        public double PrimeMeridianOffsetDegrees;
        public double NorthPoleX;
        public double NorthPoleY = 1d;
        public double NorthPoleZ;

        public AtmosphereProfile Atmosphere;

        /// <summary>Индекс родителя в списке; -1 — корень дерева (звезда).</summary>
        public int ParentIndex = -1;
    }

    /// <summary>
    /// Чертёж системы: сборка StarSystem из плоских данных. Отделена от
    /// MonoBehaviour-оберток (BodyAuthoring) ровно для того, чтобы логика
    /// сборки была тестируемой на чистом стенде (T65) и не тащила Unity API
    /// в горячий цикл. На Build() срабатывает громкая Hill-валидация (P2) —
    /// нестабильный контент падает при создании системы, до пекаря не доходит.
    /// </summary>
    [Serializable]
    public sealed class SystemBlueprint
    {
        public List<BodyBlueprint> Bodies = new List<BodyBlueprint>();

        public StarSystem Build()
        {
            if (Bodies == null || Bodies.Count == 0)
            {
                throw new InvalidOperationException("Чертёж системы пуст: добавьте хотя бы звезду.");
            }

            int rootCount = 0;
            int rootIndex = -1;
            for (int i = 0; i < Bodies.Count; i++)
            {
                if (Bodies[i].ParentIndex < 0)
                {
                    rootCount++;
                    rootIndex = i;
                }
            }

            if (rootCount != 1)
            {
                throw new InvalidOperationException(
                    "Чертёж обязан содержать ровно одну звезду (тело без родителя); найдено " + rootCount + ".");
            }

            var created = new List<OrbitingBody>(Bodies.Count);
            for (int i = 0; i < Bodies.Count; i++)
            {
                BodyBlueprint bp = Bodies[i];
                var body = new OrbitingBody
                {
                    Name = string.IsNullOrEmpty(bp.Name) ? ("Body" + i) : bp.Name,
                    StandardGravitationalParameter = bp.StandardGravitationalParameter,
                    Radius = bp.Radius,
                    CrashToleranceMps = bp.CrashToleranceMps,
                    SurfaceStaticFrictionMu = bp.SurfaceStaticFrictionMu,
                    SurfaceKineticFrictionMu = bp.SurfaceKineticFrictionMu,
                    SemiMajorAxis = bp.SemiMajorAxis,
                    Eccentricity = bp.Eccentricity,
                    InclinationDegrees = bp.InclinationDegrees,
                    LongitudeOfAscendingNodeDegrees = bp.LongitudeOfAscendingNodeDegrees,
                    ArgumentOfPeriapsisDegrees = bp.ArgumentOfPeriapsisDegrees,
                    MeanAnomalyAtEpochDegrees = bp.MeanAnomalyAtEpochDegrees,
                    EpochTimeSeconds = bp.EpochTimeSeconds,
                    RotationPeriodSeconds = bp.RotationPeriodSeconds,
                    PrimeMeridianOffsetDegrees = bp.PrimeMeridianOffsetDegrees,
                    NorthPoleDirection = new Vector3d(bp.NorthPoleX, bp.NorthPoleY, bp.NorthPoleZ),
                    Atmosphere = bp.Atmosphere
                };
                created.Add(body);
            }

            for (int i = 0; i < Bodies.Count; i++)
            {
                int p = Bodies[i].ParentIndex;
                if (p < 0)
                {
                    continue;
                }

                if (p >= Bodies.Count || p == i)
                {
                    throw new InvalidOperationException(
                        "Тело " + Bodies[i].Name + ": некорректный ParentIndex " + p + ".");
                }

                created[i].Parent = created[p];
                created[p].Children.Add(created[i]);
            }

            return new StarSystem(created[rootIndex]);
        }
    }
}
