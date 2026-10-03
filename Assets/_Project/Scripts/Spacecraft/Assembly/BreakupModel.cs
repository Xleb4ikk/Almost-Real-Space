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
    /// <summary>
    /// Разрушение по стыкам сборки (детали + прочность стыков из Parts).
    /// Модель импульсная, детерминированная:
    ///   v_n  — нормальная компонента ОТНОСИТЕЛЬНОЙ скорости удара;
    ///   load = m_детали·|v_n|/PulseSeconds — импульсная перегрузка на стык
    ///          (характерное время гашения PulseSeconds);
    ///   стык держит ⟺ load ≤ JointStrengthNewtons. Перегруженные детали
    ///   отсоединяются целиком, выжившие остаются кораблём (Landed).
    ///
    /// Отскок отсоединённой детали: нормальная компонента отражается с
    /// SurfaceRestitution («не сильно отскакивают»), тангенциальная гасится
    /// в TangentialDampFactor раз у ВСЕХ участков, сверху детерминированный
    /// разброс в КАСАТЕЛЬНОЙ плоскости (золотой угол × SpreadFactor·|v_n|).
    /// Книжка импульса (проверяется громко, как в SingleThreshold):
    ///   ТАНГЕНЦИАЛЬНЫЙ инвариант точный: Σ m_i·f_i^t = m_ship·v_t
    ///   (момент касания без внешнего тангенциального импульса; трение
    ///   грунта догасит скорость уже после).
    ///   НОРМАЛЬНАЯ компонента НЕ сохраняется по построению: есть внешний
    ///   импульс грунта (гасит прижатие) + отражённый отскок деталей —
    ///   осознанное отличие от free-разлёта SingleThreshold.
    /// Пустой список спеков = стыки выдержали удар → посадка с повреждениями.
    /// input.Parts null/пусто → вырожденный whole-ship путь SingleThreshold.
    /// </summary>
    public sealed class JointedBreakup : IBreakupModel
    {
        private const double GoldenAngle = 2.3999632297286535d;

        /// <summary>Характерное время гашения удара (с) для перегрузки стыков.</summary>
        public double PulseSeconds = 0.05d;

        /// <summary>Коэффициент отскока нормальной скорости отсоединённых деталей.</summary>
        public double SurfaceRestitution = 0.2d;

        /// <summary>Во сколько раз гасится тангенциальная скорость отломанных деталей.</summary>
        public double TangentialDampFactor = 0.5d;

        /// <summary>Разброс скоростей деталей (доля от |v_n|).</summary>
        public double SpreadFactor = 0.3d;

        private readonly SingleThresholdBreakup fallback = new SingleThresholdBreakup();

        public IReadOnlyList<PartSeparationSpec> PlanBreakup(BreakupInput input)
        {
            if (input.ShipMass <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(input), "Масса корабля обязана быть положительной.");
            }

            if (input.Parts == null || input.Parts.Count == 0)
            {
                return fallback.PlanBreakup(input);
            }

            Vector3d normal = input.SurfaceNormal.Normalized;
            double normalSpeed = Vector3d.Dot(input.ImpactVelocity, normal);
            Vector3d tangential = input.ImpactVelocity - (normal * normalSpeed);

            // Эффективные массы: детали нормируются к фактической массе корабля
            // (топливо выгорает — масса сборки дрейфует от сумки деталей).
            double partsMass = 0d;
            for (int i = 0; i < input.Parts.Count; i++)
            {
                partsMass += input.Parts[i].MassKg;
            }

            if (partsMass <= 0d)
            {
                throw new InvalidOperationException("JointedBreakup: суммарная масса деталей обязана быть положительной.");
            }

            double scale = input.ShipMass / partsMass;
            var effective = new double[input.Parts.Count];
            for (int i = 0; i < input.Parts.Count; i++)
            {
                effective[i] = input.Parts[i].MassKg * scale;
            }

            // Перегрузка на стык: импульс детали, делённый на время гашения.
            double pulse = PulseSeconds > 0d ? PulseSeconds : throw new InvalidOperationException("JointedBreakup: PulseSeconds обязан быть положительным.");
            double loadPerKg = Math.Abs(normalSpeed) / pulse;
            var broken = new List<int>();
            double survivorsMass = 0d;
            for (int i = 0; i < input.Parts.Count; i++)
            {
                double load = effective[i] * loadPerKg;
                if (load > input.Parts[i].JointStrengthNewtons)
                {
                    broken.Add(i);
                }
                else
                {
                    survivorsMass += effective[i];
                }
            }

            if (broken.Count == 0)
            {
                // Стыки держат: посадка с повреждениями, без спеков.
                return new PartSeparationSpec[0];
            }

            Vector3d survivorVelocity = tangential * (1d - TangentialDampFactor);
            double brokenMass = input.ShipMass - survivorsMass;

            // Средняя тангенциальная скорость отломанных деталей — из точного
            // тангенциального инварианта: Σ m_i·f_i^t = m_ship·v_t − m_surv·f_surv^t.
            Vector3d brokenMeanTangent = ((tangential * input.ShipMass) - (survivorVelocity * survivorsMass)) / brokenMass;

            // Детерминированный разброс в КАСАТЕЛЬНОЙ плоскости (золотой угол),
            // центрируется взвешенной поправкой в ноль — нормаль не трогает.
            double spreadSpeed = SpreadFactor * Math.Abs(normalSpeed);
            Vector3d t1 = Math.Abs(normal.X) < 0.9d ? new Vector3d(1d, 0d, 0d) : new Vector3d(0d, 1d, 0d);
            t1 = (t1 - (normal * Vector3d.Dot(t1, normal))).Normalized;
            Vector3d t2 = Vector3d.Cross(normal, t1);
            var scatter = new Vector3d[broken.Count];
            Vector3d weightedMean = Vector3d.Zero;
            for (int i = 0; i < broken.Count; i++)
            {
                double phi = i * GoldenAngle;
                scatter[i] = (t1 * (Math.Cos(phi) * spreadSpeed)) + (t2 * (Math.Sin(phi) * spreadSpeed));
                weightedMean += scatter[i] * effective[broken[i]];
            }

            weightedMean = weightedMean / brokenMass;

            var specs = new List<PartSeparationSpec>(broken.Count);
            Vector3d checkMomentum = Vector3d.Zero;
            double checkMass = 0d;
            for (int i = 0; i < broken.Count; i++)
            {
                int partIndex = broken[i];
                Vector3d fragmentVelocity = brokenMeanTangent
                    + (normal * (-normalSpeed * SurfaceRestitution))
                    + (scatter[i] - weightedMean);
                specs.Add(new PartSeparationSpec(
                    partIndex, effective[partIndex],
                    input.SurfacePoint + (normal * input.SpawnEpsilonMeters),
                    input.SurfacePointVelocity + fragmentVelocity,
                    fragmentVelocity));
                checkMass += effective[partIndex];
                Vector3d fragmentTangent = fragmentVelocity - (normal * Vector3d.Dot(fragmentVelocity, normal));
                checkMomentum += fragmentTangent * effective[partIndex];
            }

            double massTolerance = 1e-9d * Math.Max(1d, input.ShipMass);
            if (Math.Abs(checkMass - brokenMass) > massTolerance)
            {
                throw new InvalidOperationException("JointedBreakup: нарушен инвариант Σm_broken = m_ship − m_surv.");
            }

            Vector3d expectTangent = (tangential * input.ShipMass) - (survivorVelocity * survivorsMass);
            double tangentTolerance = 1e-9d * Math.Max(1d, expectTangent.Magnitude);
            if ((checkMomentum - expectTangent).Magnitude > tangentTolerance)
            {
                throw new InvalidOperationException("JointedBreakup: нарушен тангенциальный инвариант Σm_i·f_i^t = m_ship·v_t − m_surv·f_surv^t.");
            }

            return specs;
        }
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
