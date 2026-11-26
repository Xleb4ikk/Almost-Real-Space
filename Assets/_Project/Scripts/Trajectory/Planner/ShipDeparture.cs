using System;
using System.Collections.Generic;
using Galilego.Core;

namespace Galilego.Planning
{
    public struct DeparturePlan
    {
        public bool Valid;
        public double BurnTime;
        public Vector3d RShip, VShip;         // состояние корабля в момент импульса, относительно тела старта
        public EscapeBurn Burn;
        public TransferSolution Transfer;     // дуга для этого момента (прибытие фиксировано)
        public double ArrivalDv;
        public double Total { get { return Burn.Dv + ArrivalDv; } }
    }

    /// <summary>Положение/скорость корабля относительно тела старта (инерциальные оси) в момент t: n-body прогноз или Кеплер.</summary>
    public interface IShipTrack
    {
        bool TryEvaluate(double t, out Vector3d rRel, out Vector3d vRel);
    }

    /// <summary>Двухтельная орбита корабля (годится на часы-сутки; на долгих сроках фаза уплывает от возмущений).</summary>
    public sealed class KeplerShipTrack : IShipTrack
    {
        private readonly double mu, epoch; private readonly Vector3d r0, v0;
        public KeplerShipTrack(double mu, double epoch, Vector3d r0, Vector3d v0) { this.mu = mu; this.epoch = epoch; this.r0 = r0; this.v0 = v0; }
        public bool TryEvaluate(double t, out Vector3d r, out Vector3d v) { return UniversalKepler.TryPropagate(mu, r0, v0, t - epoch, out r, out v); }
    }

    /// <summary>
    /// Корабль по n-body прогнозу игры. Контрольные точки кэшируются: перебор моментов идёт по возрастанию, и каждый
    /// запрос продолжает от ближайшей точки слева, а не от начала.
    /// </summary>
    public sealed class NBodyShipTrack : IShipTrack
    {
        private readonly IPlannerWorld world; private readonly IPlannerPropagator prop; private readonly int body;
        private readonly List<(double t, Vector3d p, Vector3d v)> checkpoints = new List<(double, Vector3d, Vector3d)>();

        public NBodyShipTrack(IPlannerWorld world, IPlannerPropagator prop, int body, double t0, Vector3d worldPos, Vector3d worldVel)
        { this.world = world; this.prop = prop; this.body = body; checkpoints.Add((t0, worldPos, worldVel)); }

        public bool TryEvaluate(double t, out Vector3d rRel, out Vector3d vRel)
        {
            rRel = vRel = Vector3d.Zero;
            int lo = 0, hi = checkpoints.Count - 1;
            if (t < checkpoints[0].t) { return false; }
            while (lo < hi) { int mid = (lo + hi + 1) / 2; if (checkpoints[mid].t <= t) { lo = mid; } else { hi = mid - 1; } }
            (double tc, Vector3d pc, Vector3d vc) = checkpoints[lo];
            Vector3d p = pc, v = vc;
            if (t > tc)
            {
                if (!prop.TryPropagate(tc, pc, vc, t, out p, out v)) { return false; }
                checkpoints.Insert(lo + 1, (t, p, v));
            }

            if (!world.TryBodyState(body, t, out Vector3d bp, out Vector3d bv)) { return false; }
            rRel = p - bp; vRel = v - bv;
            return true;
        }
    }

    /// <summary>
    /// Отлёт с ТЕКУЩЕЙ орбиты корабля. Идеальная цена из сетки (круговая парковочная, оптимальная плоскость) здесь
    /// заменяется настоящей: перебирается момент импульса на орбите, для каждого пересчитывается дуга Ламберта на
    /// фиксированное прибытие, затем решается гипербола через точку корабля (EscapeSolver). Смена плоскости входит в цену.
    /// Орбита корабля внутри окна — двухтельная (declared deviation; n-body уточняется следующим этапом).
    /// </summary>
    public static class ShipDeparture
    {
        public static bool Find(TransferSpec s, double muShipBody, double minPeriapsis,
            double shipEpoch, Vector3d rShip0, Vector3d vShip0,
            double arrivalTime, double burnFrom, double burnTo, int samples, out DeparturePlan plan)
        {
            return Find(s, muShipBody, minPeriapsis, new KeplerShipTrack(muShipBody, shipEpoch, rShip0, vShip0), arrivalTime, burnFrom, burnTo, samples, out plan);
        }

        public static bool Find(TransferSpec s, double muShipBody, double minPeriapsis, IShipTrack ship,
            double arrivalTime, double burnFrom, double burnTo, int samples, out DeparturePlan plan)
        {
            plan = default;
            double Cost(double tb, out DeparturePlan p)
            {
                p = default;
                if (!(arrivalTime - tb > 60d)) { return double.PositiveInfinity; }
                if (!ship.TryEvaluate(tb, out Vector3d r, out Vector3d v)) { return double.PositiveInfinity; }
                if (!s.From.TryEvaluate(tb, out Vector3d rA, out Vector3d vA) || !s.To.TryEvaluate(arrivalTime, out Vector3d rB, out Vector3d vB)) { return double.PositiveInfinity; }
                if (!EvaluateArc(s, tb, arrivalTime, rA, vA, rB, vB, out TransferSolution arc)) { return double.PositiveInfinity; }
                if (!EscapeSolver.TrySolve(muShipBody, minPeriapsis, r, v, arc.VInfDep, out EscapeBurn burn)) { return double.PositiveInfinity; }
                double arrDv = arc.ArrDv;
                p = new DeparturePlan { Valid = true, BurnTime = tb, RShip = r, VShip = v, Burn = burn, Transfer = arc, ArrivalDv = arrDv };
                return burn.Dv + arrDv;
            }

            samples = Math.Max(samples, 8);
            double h = (burnTo - burnFrom) / (samples - 1);
            int best = -1;
            double bestCost = double.PositiveInfinity;
            for (int i = 0; i < samples; i++)
            {
                double c = Cost(burnFrom + (i * h), out _);
                if (c < bestCost) { bestCost = c; best = i; }
            }

            if (best < 0) { return false; }

            // Золотое сечение в окне ±1 шаг вокруг лучшего узла.
            double lo = Math.Max(burnFrom, burnFrom + ((best - 1) * h));
            double hi = Math.Min(burnTo, burnFrom + ((best + 1) * h));
            const double phi = 0.6180339887498949d;
            double x1 = hi - (phi * (hi - lo)), x2 = lo + (phi * (hi - lo));
            double f1 = Cost(x1, out _), f2 = Cost(x2, out _);
            for (int it = 0; it < 80 && hi - lo > 1e-3d; it++)
            {
                if (f1 < f2) { hi = x2; x2 = x1; f2 = f1; x1 = hi - (phi * (hi - lo)); f1 = Cost(x1, out _); }
                else { lo = x1; x1 = x2; f1 = f2; x2 = lo + (phi * (hi - lo)); f2 = Cost(x2, out _); }
            }

            double tBest = 0.5d * (lo + hi);
            double cBest = Cost(tBest, out DeparturePlan refined);
            double cGrid = Cost(burnFrom + (best * h), out DeparturePlan gridPlan);
            plan = cBest <= cGrid ? refined : gridPlan;
            return plan.Valid;
        }

        private static bool EvaluateArc(TransferSpec s, double t0, double t1, Vector3d rA, Vector3d vA, Vector3d rB, Vector3d vB, out TransferSolution arc)
        {
            return TransferScanner.EvaluateStates(s, t0, t1, rA, vA, rB, vB, out arc);
        }
    }
}
