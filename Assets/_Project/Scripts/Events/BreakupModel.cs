using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Spacecraft;

namespace Galilego.Events
{
    /// <summary>
    /// Спека отделения одной детали: ДАННЫЕ, не спавн. Позиция — мировая
    /// (проекция точки удара + ε вдоль нормали; все спеки в одной точке —
    /// валидно, разведением занимается сборочная система). Скорость хранится
    /// в ДВУХ пространствах сразу, чтобы их нельзя было перепутать:
    /// FragmentVelocity — относительная f_i (для инварианта импульса),
    /// Velocity — мировая v_surface + f_i (для спавна). Пересчёт одного
    /// в другое внутри спеки запрещён: источник правды — конструктор модели.
    /// </summary>
    public readonly struct PartSeparationSpec
    {
        public readonly int PartIndex;
        public readonly double Mass;
        public readonly Vector3d Position;
        public readonly Vector3d Velocity;
        public readonly Vector3d FragmentVelocity;

        public PartSeparationSpec(int partIndex, double mass, Vector3d position, Vector3d velocity, Vector3d fragmentVelocity)
        {
            PartIndex = partIndex;
            Mass = mass;
            Position = position;
            Velocity = velocity;
            FragmentVelocity = fragmentVelocity;
        }
    }

    /// <summary>
    /// Вход модели разрушения. ImpactVelocity — строго ОТНОСИТЕЛЬНАЯ скорость
    /// (корабль минус точка поверхности); смешивать её с мировой запрещено
    /// типами использования: модель складывает с SurfacePointVelocity сама,
    /// ровно один раз, в одном месте.
    /// </summary>
    public readonly struct BreakupInput
    {
        public readonly double ShipMass;
        public readonly Vector3d ImpactVelocity;
        public readonly Vector3d SurfacePointVelocity;
        public readonly Vector3d SurfacePoint;
        public readonly Vector3d SurfaceNormal;
        public readonly double SpawnEpsilonMeters;

        /// <summary>
        /// Детали сборки (R2). null/пусто = вырожденный whole-ship случай:
        /// SingleThresholdBreakup массу делит сам и список не читает
        /// (зафиксировано тестом). Будущий JointedBreakup возьмёт отсюда
        /// реальные стыки. Опциональный параметр — старые вызовы не ломаются.
        /// </summary>
        public readonly IReadOnlyList<Part> Parts;

        public BreakupInput(
            double shipMass, Vector3d impactVelocity, Vector3d surfacePointVelocity,
            Vector3d surfacePoint, Vector3d surfaceNormal, double spawnEpsilonMeters,
            IReadOnlyList<Part> parts = null)
        {
            ShipMass = shipMass;
            ImpactVelocity = impactVelocity;
            SurfacePointVelocity = surfacePointVelocity;
            SurfacePoint = surfacePoint;
            SurfaceNormal = surfaceNormal;
            SpawnEpsilonMeters = spawnEpsilonMeters;
            Parts = parts;
        }
    }

    /// <summary>
    /// Контракт модели разрушения. Число деталей НЕ оговаривается: это дело
    /// реализации (будущий JointedBreakup возьмёт N из списка стыков).
    /// Математические инварианты выхода (проверяются тестом, не словами):
    ///   Σ m_i = m_ship,
    ///   Σ m_i·f_i = m_ship·v_impact   (следствие: Σ m_i·v_i = m_ship·v_ship).
    /// </summary>
    public interface IBreakupModel
    {
        IReadOnlyList<PartSeparationSpec> PlanBreakup(BreakupInput input);
    }

    /// <summary>
    /// Тестовая реализация: whole-ship как сборка из одной «детали» делится на
    /// N фрагментов. N=10 — приватная константа ЭТОЙ реализации (см. п.3 v3),
    /// контракту IBreakupModel количество неизвестно. Разброс детерминированный
    /// (Фибоначчи-сфера × SpreadFactor·|v_impact|), центрирование — ВЗВЕШЕННЫМ
    /// по массе средним (при равных массах совпадает с обычным, но контракт
    /// писан под будущие неравные детали):
    ///   f_i = v_impact + s_i,  s_i ← s_i − (Σ m_j·s_j)/(Σ m_j).
    /// Проверка инвариантов — громким исключением, не комментарием.
    /// </summary>
    public sealed class SingleThresholdBreakup : IBreakupModel
    {
        private const int FragmentCount = 10;

        private const double GoldenAngle = 2.3999632297286535d;

        public double SpreadFactor = 0.3d;

        public IReadOnlyList<PartSeparationSpec> PlanBreakup(BreakupInput input)
        {
            if (input.ShipMass <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(input), "Масса корабля обязана быть положительной.");
            }

            double fragmentMass = input.ShipMass / FragmentCount;
            double impactSpeed = input.ImpactVelocity.Magnitude;
            double spreadSpeed = SpreadFactor * impactSpeed;

            var spread = new Vector3d[FragmentCount];
            for (int i = 0; i < FragmentCount; i++)
            {
                double y = 1d - (2d * (i + 0.5d) / FragmentCount);
                double r = Math.Sqrt(Math.Max(0d, 1d - (y * y)));
                double phi = i * GoldenAngle;
                spread[i] = new Vector3d(r * Math.Cos(phi) * spreadSpeed, y * spreadSpeed, r * Math.Sin(phi) * spreadSpeed);
            }

            Vector3d weightedMean = Vector3d.Zero;
            for (int i = 0; i < FragmentCount; i++)
            {
                weightedMean += spread[i] * fragmentMass;
            }

            weightedMean = weightedMean / input.ShipMass;

            Vector3d spawnAt = input.SurfacePoint + (input.SurfaceNormal * input.SpawnEpsilonMeters);
            var specs = new List<PartSeparationSpec>(FragmentCount);
            Vector3d checkMomentum = Vector3d.Zero;
            double checkMass = 0d;
            for (int i = 0; i < FragmentCount; i++)
            {
                Vector3d fragmentVelocity = input.ImpactVelocity + (spread[i] - weightedMean);
                specs.Add(new PartSeparationSpec(
                    i, fragmentMass, spawnAt,
                    input.SurfacePointVelocity + fragmentVelocity, fragmentVelocity));
                checkMass += fragmentMass;
                checkMomentum += fragmentVelocity * fragmentMass;
            }

            double massTolerance = 1e-9d * Math.Max(1d, input.ShipMass);
            if (Math.Abs(checkMass - input.ShipMass) > massTolerance)
            {
                throw new InvalidOperationException("Breakup: нарушен инвариант Σm_i = m_ship.");
            }

            Vector3d expectMomentum = input.ImpactVelocity * input.ShipMass;
            double momentumTolerance = 1e-9d * Math.Max(1d, expectMomentum.Magnitude);
            if ((checkMomentum - expectMomentum).Magnitude > momentumTolerance)
            {
                throw new InvalidOperationException("Breakup: нарушен инвариант Σm_i·f_i = m·v_impact.");
            }

            return specs;
        }
    }
}
