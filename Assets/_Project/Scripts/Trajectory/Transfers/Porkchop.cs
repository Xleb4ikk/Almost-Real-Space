using System;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Клетка porkchop-скана: окно вылет/прилёт, суммарная Δv rendezvous
    /// (уход + прибытие). Невалидна (Δv = +∞, Valid = false), если окно
    /// вырождено (прилёт не позже вылета), Ламберт не сошёлся, дуга идёт
    /// сквозь тело (проверка сэмплами через StarSystem.TryEvaluate — тот же
    /// предикат, что у планировщика, кандидат помечается invalid, а не тихо
    /// зануляется) или окно выходит за испечённый горизонт. NaN в таблице
    /// нет по построению.
    /// </summary>
    public readonly struct PorkchopCell
    {
        public readonly double DepartTimeSeconds;
        public readonly double ArriveTimeSeconds;
        public readonly double DeltaV;
        public readonly bool Valid;

        public PorkchopCell(double departTimeSeconds, double arriveTimeSeconds, double deltaV, bool valid)
        {
            DepartTimeSeconds = departTimeSeconds;
            ArriveTimeSeconds = arriveTimeSeconds;
            DeltaV = deltaV;
            Valid = valid;
        }

        public static PorkchopCell Invalid(double departTimeSeconds, double arriveTimeSeconds)
        {
            return new PorkchopCell(departTimeSeconds, arriveTimeSeconds, double.PositiveInfinity, false);
        }
    }

    /// <summary>
    /// Скан окон перелёта между двумя телами вокруг общего центра.
    /// Короткий путь Ламберта (документровано); rendezvous с обоих концов.
    /// Дуга валидируется 16 сэмплами KeplerAdvance в мировой рамке (центр
    /// движется — его позиция берётся на момент сэмпла, не вылета):
    /// сквозной пролёт сквозь любое тело — invalid. Стоимость клетки —
    /// микросекунды (бисекция z + аналитика), сетка 25 клеток — доли
    /// миллисекунды. Детерминирован бит-в-бит при тех же входах.
    /// </summary>
    public static class Porkchop
    {
        private const int ValiditySamples = 16;

        public static PorkchopCell[,] Scan(
            StarSystem system, OrbitingBody centralBody, OrbitingBody departBody, OrbitingBody arriveBody,
            double departStartSeconds, double departEndSeconds, int departCount,
            double arriveStartSeconds, double arriveEndSeconds, int arriveCount)
        {
            if (system == null)
                throw new ArgumentNullException(nameof(system));
            if (centralBody == null || departBody == null || arriveBody == null)
                throw new ArgumentNullException("Тела перелёта обязаны быть заданы.");
            if (departCount < 1 || arriveCount < 1)
                throw new ArgumentOutOfRangeException("Сетка обязана иметь хотя бы одну клетку.");

            double mu = centralBody.ResolveStandardGravitationalParameter();
            var table = new PorkchopCell[departCount, arriveCount];
            for (int i = 0; i < departCount; i++)
            {
                double departTime = departCount == 1
                    ? departStartSeconds
                    : departStartSeconds + ((departEndSeconds - departStartSeconds) * i / (departCount - 1));
                for (int j = 0; j < arriveCount; j++)
                {
                    double arriveTime = arriveCount == 1
                        ? arriveStartSeconds
                        : arriveStartSeconds + ((arriveEndSeconds - arriveStartSeconds) * j / (arriveCount - 1));
                    table[i, j] = SolveCell(system, centralBody, departBody, arriveBody, mu, departTime, arriveTime);
                }
            }

            return table;
        }

        public static bool TryBest(PorkchopCell[,] table, out int bestDepart, out int bestArrive)
        {
            bestDepart = -1;
            bestArrive = -1;
            double bestDv = double.PositiveInfinity;
            for (int i = 0; i < table.GetLength(0); i++)
            {
                for (int j = 0; j < table.GetLength(1); j++)
                {
                    PorkchopCell cell = table[i, j];
                    if (cell.Valid && cell.DeltaV < bestDv)
                    {
                        bestDv = cell.DeltaV;
                        bestDepart = i;
                        bestArrive = j;
                    }
                }
            }

            return bestDepart >= 0;
        }

        private static PorkchopCell SolveCell(
            StarSystem system, OrbitingBody centralBody, OrbitingBody departBody, OrbitingBody arriveBody,
            double mu, double departTime, double arriveTime)
        {
            double dt = arriveTime - departTime;
            if (dt <= 0d)
            {
                return PorkchopCell.Invalid(departTime, arriveTime);
            }

            try
            {
                return SolveCellInner(system, centralBody, departBody, arriveBody, mu, departTime, arriveTime, dt);
            }
            catch (EphemerisRangeException)
            {
                return PorkchopCell.Invalid(departTime, arriveTime);
            }
        }

        private static PorkchopCell SolveCellInner(
            StarSystem system, OrbitingBody centralBody, OrbitingBody departBody, OrbitingBody arriveBody,
            double mu, double departTime, double arriveTime, double dt)
        {
            departBody.EvaluateWorldState(departTime, out Vector3d departPosition, out Vector3d departVelocity);
            arriveBody.EvaluateWorldState(arriveTime, out Vector3d arrivePosition, out Vector3d arriveVelocity);
            centralBody.EvaluateWorldState(departTime, out Vector3d centralAtDepart, out Vector3d centralVelocityAtDepart);
            centralBody.EvaluateWorldState(arriveTime, out _, out Vector3d centralVelocityAtArrive);

            Vector3d relativeDepart = departPosition - centralAtDepart;
            Vector3d relativeDepartVelocity = departVelocity - centralVelocityAtDepart;
            centralBody.EvaluateWorldState(arriveTime, out Vector3d centralAtArrive, out _);
            Vector3d relativeArrive = arrivePosition - centralAtArrive;
            Vector3d relativeArriveVelocity = arriveVelocity - centralVelocityAtArrive;

            LambertSolution lambert;
            try
            {
                lambert = LambertSolver.Solve(relativeDepart, relativeArrive, dt, mu, false);
            }
            catch (ArgumentException)
            {
                return PorkchopCell.Invalid(departTime, arriveTime);
            }
            catch (NotSupportedException)
            {
                return PorkchopCell.Invalid(departTime, arriveTime);
            }

            double deltaV = (lambert.DepartureVelocity - relativeDepartVelocity).Magnitude
                + (relativeArriveVelocity - lambert.ArrivalVelocity).Magnitude;
            if (double.IsNaN(deltaV) || double.IsInfinity(deltaV))
            {
                return PorkchopCell.Invalid(departTime, arriveTime);
            }

            if (!IsArcClear(system, centralBody, mu, relativeDepart, lambert.DepartureVelocity, dt, departTime))
            {
                return PorkchopCell.Invalid(departTime, arriveTime);
            }

            return new PorkchopCell(departTime, arriveTime, deltaV, true);
        }

        /// <summary>
        /// Концы дуги (k=0, k=N) исключены осознанно: это центры стартового
        /// и целевого тел (парковочные орбиты), TryEvaluate там всегда false.
        /// Проверяется транзитная дуга — сквозной пролёт ловится внутренними
        /// сэмплами. Без этого валидна была бы ровно ноль клеток (поймано T25).
        /// </summary>
        private static bool IsArcClear(StarSystem system, OrbitingBody centralBody, double mu, Vector3d relativeDepart, Vector3d departureVelocity, double dt, double departTime)
        {
            for (int k = 1; k < ValiditySamples; k++)
            {
                double sampleDt = (dt * k) / ValiditySamples;
                Vector3d relativePosition;
                try
                {
                    // Околопараболическая/вырожденная дуга — не краш всего Scan,
                    // а невалидная клетка (аудит S1/1.4).
                    KeplerPredictor.Advance(relativeDepart, departureVelocity, mu, sampleDt, out relativePosition, out _);
                }
                catch (ArgumentException)
                {
                    return false;
                }
                catch (NotSupportedException)
                {
                    return false;
                }

                centralBody.EvaluateWorldState(departTime + sampleDt, out Vector3d centralPosition, out _);
                if (!system.TryEvaluateAcceleration(centralPosition + relativePosition, departTime + sampleDt, out _))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
