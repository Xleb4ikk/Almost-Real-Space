using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Galilego.Core;

namespace Galilego.Planning
{
    public sealed class FlybySpec
    {
        public double MuCentral;
        public double MinCentralRadius;
        public IOrbitTrack From, Via, To;
        public Vector3d Normal = new Vector3d(0d, 0d, 1d);
        public bool Prograde = true;

        public double MuFrom, ParkRadiusFrom;
        public double MuVia, ViaMinPeriapsis, ViaSoiRadius;
        public double MuTo, ParkRadiusTo;
        public CaptureKind Capture = CaptureKind.Circular;
        public double CaptureEccentricity;

        /// <summary>Минимальная длина участка (с), отсекает вырожденные узкие дуги.</summary>
        public double MinLegS = 86400d;
    }

    public struct FlybySolution
    {
        public bool Valid;
        public double T0, T1, T2;
        public Vector3d RFrom, VFrom, RVia, VVia, RTo, VTo;       // состояния тел (СК родителя)
        public Vector3d VLeg1Dep, VLeg1Arr, VLeg2Dep, VLeg2Arr;   // скорости дуг на концах (СК родителя)
        public Vector3d VInfDep, VInfIn, VInfOut, VInfArr;        // избытки: отлёт (СК старта), пролёт (СК тела), прилёт
        public double DepDv, ArrDv;
        public FlybyResult Flyby;
        public double ArcMinRadius;

        public bool Feasible { get { return Valid && Flyby.Feasible; } }
        public double Total { get { return DepDv + Flyby.PoweredDv + Flyby.DeficitDv + ArrDv; } }

        /// <summary>Выигрыш скорости относительно родителя от пассивной части (изменение вектора, F-MAN-10).</summary>
        public double FreeVelocityChange { get { return Flyby.VelocityChange; } }

        /// <summary>
        /// Энергия в СК родителя: |v_out|²−|v_in|² = 2·V_тела·(v∞_out − v∞_in) + (|v∞_out|²−|v∞_in|²).
        /// Второе слагаемое нулевое только для пассивного пролёта; для powered-пролёта его нельзя терять.
        /// </summary>
        public double HelioEnergyGain
        {
            get
            {
                return (2d * Vector3d.Dot(VVia, VInfOut - VInfIn)) + (VInfOut.SqrMagnitude - VInfIn.SqrMagnitude);
            }
        }

        /// <summary>true — проход позади планеты по её движению (разгон), false — впереди (торможение).</summary>
        public bool Trailing { get { return Vector3d.Dot(Flyby.PeriapsisDir, VVia) < 0d; } }
    }

    /// <summary>
    /// Перелёт с одним гравитационным манёвром: A(t0) → G(t1) → B(t2), две дуги Ламберта, стыковка у G.
    /// Поиск: грубая сетка N0×N1×N2 (таблицы дуг считаются ОДИН раз на пару узлов: 2·N² Ламберта, затем N³ дешёвых
    /// оценок пролёта), лучшие кандидаты уточняются симплексом по трём датам.
    /// </summary>
    public static class FlybySearch
    {
        private struct Leg
        {
            public bool Ok;
            public Vector3d V1, V2;
        }

        public static bool Evaluate(FlybySpec s, double t0, double t1, double t2, out FlybySolution sol)
        {
            sol = default;
            if (!(t1 - t0 >= s.MinLegS) || !(t2 - t1 >= s.MinLegS))
            {
                return false;
            }

            if (!s.From.TryEvaluate(t0, out Vector3d rA, out Vector3d vA)
                || !s.Via.TryEvaluate(t1, out Vector3d rG, out Vector3d vG)
                || !s.To.TryEvaluate(t2, out Vector3d rB, out Vector3d vB))
            {
                return false;
            }

            return EvaluateStates(s, t0, t1, t2, rA, vA, rG, vG, rB, vB, out sol);
        }

        private static bool EvaluateStates(FlybySpec s, double t0, double t1, double t2,
            Vector3d rA, Vector3d vA, Vector3d rG, Vector3d vG, Vector3d rB, Vector3d vB, out FlybySolution sol)
        {
            sol = default;
            if (!LambertPrograde.TrySolve(s.MuCentral, rA, rG, t1 - t0, s.Normal, s.Prograde, out Vector3d a1, out Vector3d a2)
                || !LambertPrograde.TrySolve(s.MuCentral, rG, rB, t2 - t1, s.Normal, s.Prograde, out Vector3d b1, out Vector3d b2))
            {
                return false;
            }

            double rmin = Math.Min(TransferScanner.ArcMinRadius(s.MuCentral, rA, a1, rG),
                TransferScanner.ArcMinRadius(s.MuCentral, rG, b1, rB));
            if (!(rmin >= s.MinCentralRadius))
            {
                return false;
            }

            Vector3d infDep = a1 - vA;
            Vector3d infIn = a2 - vG;
            Vector3d infOut = b1 - vG;
            Vector3d infArr = b2 - vB;
            FlybyResult fl = FlybyGeometry.Evaluate(s.MuVia, s.ViaMinPeriapsis, s.ViaSoiRadius, infIn, infOut);
            if (!(fl.PeriapsisRadius > 0d))
            {
                return false;
            }

            double dep = ConicMath.EscapeDvCircular(s.MuFrom, s.ParkRadiusFrom, infDep.Magnitude);
            double arr = 0d;
            if (s.Capture == CaptureKind.Circular) { arr = ConicMath.CaptureDvCircular(s.MuTo, s.ParkRadiusTo, infArr.Magnitude); }
            else if (s.Capture == CaptureKind.Elliptic) { arr = ConicMath.CaptureDvEllipse(s.MuTo, s.ParkRadiusTo, s.CaptureEccentricity, infArr.Magnitude); }

            if (!double.IsFinite(dep + arr + fl.PoweredDv + fl.DeficitDv))
            {
                return false;
            }

            sol = new FlybySolution
            {
                Valid = true, T0 = t0, T1 = t1, T2 = t2,
                RFrom = rA, VFrom = vA, RVia = rG, VVia = vG, RTo = rB, VTo = vB,
                VLeg1Dep = a1, VLeg1Arr = a2, VLeg2Dep = b1, VLeg2Arr = b2,
                VInfDep = infDep, VInfIn = infIn, VInfOut = infOut, VInfArr = infArr,
                DepDv = dep, ArrDv = arr, Flyby = fl, ArcMinRadius = rmin,
            };
            return true;
        }

        /// <summary>
        /// Поиск. Окна: [lo,hi] для t0, t1, t2 и размеры сетки. Возвращает до topK различных решений, лучшие первыми.
        /// Недопустимые по повороту клетки не отбрасываются (цена включает DeficitDv) — у оптимизатора есть градиент к
        /// допустимой области; для экспорта используйте Feasible.
        /// </summary>
        public static List<FlybySolution> Search(FlybySpec s,
            double t0Lo, double t0Hi, int n0, double t1Lo, double t1Hi, int n1, double t2Lo, double t2Hi, int n2, int topK)
        {
            double h0 = (t0Hi - t0Lo) / Math.Max(1, n0 - 1);
            double h1 = (t1Hi - t1Lo) / Math.Max(1, n1 - 1);
            double h2 = (t2Hi - t2Lo) / Math.Max(1, n2 - 1);

            var rA = new Vector3d[n0]; var vA = new Vector3d[n0]; var okA = new bool[n0];
            var rG = new Vector3d[n1]; var vG = new Vector3d[n1]; var okG = new bool[n1];
            var rB = new Vector3d[n2]; var vB = new Vector3d[n2]; var okB = new bool[n2];
            for (int i = 0; i < n0; i++) { okA[i] = s.From.TryEvaluate(t0Lo + (i * h0), out rA[i], out vA[i]); }
            for (int j = 0; j < n1; j++) { okG[j] = s.Via.TryEvaluate(t1Lo + (j * h1), out rG[j], out vG[j]); }
            for (int k = 0; k < n2; k++) { okB[k] = s.To.TryEvaluate(t2Lo + (k * h2), out rB[k], out vB[k]); }

            var leg1 = new Leg[n0, n1];
            var leg2 = new Leg[n1, n2];
            Parallel.For(0, n0, i =>
            {
                for (int j = 0; j < n1; j++)
                {
                    double dt = (t1Lo + (j * h1)) - (t0Lo + (i * h0));
                    if (!okA[i] || !okG[j] || dt < s.MinLegS) { continue; }
                    leg1[i, j].Ok = LambertPrograde.TrySolve(s.MuCentral, rA[i], rG[j], dt, s.Normal, s.Prograde, out leg1[i, j].V1, out leg1[i, j].V2)
                        && TransferScanner.ArcMinRadius(s.MuCentral, rA[i], leg1[i, j].V1, rG[j]) >= s.MinCentralRadius;
                }
            });
            Parallel.For(0, n1, j =>
            {
                for (int k = 0; k < n2; k++)
                {
                    double dt = (t2Lo + (k * h2)) - (t1Lo + (j * h1));
                    if (!okG[j] || !okB[k] || dt < s.MinLegS) { continue; }
                    leg2[j, k].Ok = LambertPrograde.TrySolve(s.MuCentral, rG[j], rB[k], dt, s.Normal, s.Prograde, out leg2[j, k].V1, out leg2[j, k].V2)
                        && TransferScanner.ArcMinRadius(s.MuCentral, rG[j], leg2[j, k].V1, rB[k]) >= s.MinCentralRadius;
                }
            });

            // Предрасчёт цен концов (зависят от одной пары узлов).
            var depDv = new float[n0, n1];
            var arrDv = new float[n1, n2];
            Parallel.For(0, n0, i =>
            {
                for (int j = 0; j < n1; j++)
                {
                    depDv[i, j] = leg1[i, j].Ok
                        ? (float)ConicMath.EscapeDvCircular(s.MuFrom, s.ParkRadiusFrom, (leg1[i, j].V1 - vA[i]).Magnitude)
                        : float.NaN;
                }
            });
            Parallel.For(0, n1, j =>
            {
                for (int k = 0; k < n2; k++)
                {
                    if (!leg2[j, k].Ok) { arrDv[j, k] = float.NaN; continue; }
                    double vinf = (leg2[j, k].V2 - vB[k]).Magnitude;
                    arrDv[j, k] = (float)(s.Capture == CaptureKind.Circular ? ConicMath.CaptureDvCircular(s.MuTo, s.ParkRadiusTo, vinf)
                        : s.Capture == CaptureKind.Elliptic ? ConicMath.CaptureDvEllipse(s.MuTo, s.ParkRadiusTo, s.CaptureEccentricity, vinf) : 0d);
                }
            });

            var cost = new float[n0 * n1 * n2];
            Parallel.For(0, n1, j =>
            {
                for (int i = 0; i < n0; i++)
                {
                    if (!leg1[i, j].Ok) { for (int k = 0; k < n2; k++) { cost[(((i * n1) + j) * n2) + k] = float.NaN; } continue; }
                    Vector3d infIn = leg1[i, j].V2 - vG[j];
                    for (int k = 0; k < n2; k++)
                    {
                        int idx = (((i * n1) + j) * n2) + k;
                        if (!leg2[j, k].Ok) { cost[idx] = float.NaN; continue; }
                        FlybyResult fl = FlybyGeometry.Evaluate(s.MuVia, s.ViaMinPeriapsis, s.ViaSoiRadius, infIn, leg2[j, k].V1 - vG[j]);
                        cost[idx] = fl.PeriapsisRadius > 0d ? (float)(depDv[i, j] + arrDv[j, k] + fl.PoweredDv + fl.DeficitDv) : float.NaN;
                    }
                }
            });

            // Кандидаты: лучшие клетки, разнесённые ≥ 3 узлов по любой оси (разные окна, а не один склон одной долины).
            var order = new List<int>();
            for (int idx = 0; idx < cost.Length; idx++) { if (!float.IsNaN(cost[idx])) { order.Add(idx); } }
            order.Sort((a, b) => cost[a].CompareTo(cost[b]));
            var picked = new List<int>();
            foreach (int idx in order)
            {
                int k = idx % n2; int j = (idx / n2) % n1; int i = idx / (n1 * n2);
                bool far = true;
                foreach (int p in picked)
                {
                    int pk = p % n2, pj = (p / n2) % n1, pi = p / (n1 * n2);
                    if (Math.Abs(pi - i) < 3 && Math.Abs(pj - j) < 3 && Math.Abs(pk - k) < 3) { far = false; break; }
                }

                if (far) { picked.Add(idx); if (picked.Count >= topK) { break; } }
            }

            var result = new List<FlybySolution>();
            foreach (int idx in picked)
            {
                int k = idx % n2; int j = (idx / n2) % n1; int i = idx / (n1 * n2);
                double a0 = t0Lo + (i * h0), a1 = t1Lo + (j * h1), a2 = t2Lo + (k * h2);
                double Cost(double[] x)
                {
                    if (x[0] < t0Lo - h0 || x[2] > t2Hi + h2) { return 1e15d; }
                    return Evaluate(s, x[0], x[1], x[2], out FlybySolution sl) ? sl.Total : 1e15d;
                }

                double[] xb = NelderMead.Minimize(Cost, new[] { a0, a1, a2 }, new[] { h0, h1, h2 }, 1e-10d, 1500, out double fmin);
                if (fmin < 1e14d && Evaluate(s, xb[0], xb[1], xb[2], out FlybySolution best)) { result.Add(best); }
            }

            result.Sort((a, b) => a.Total.CompareTo(b.Total));
            return result;
        }
    }
}
