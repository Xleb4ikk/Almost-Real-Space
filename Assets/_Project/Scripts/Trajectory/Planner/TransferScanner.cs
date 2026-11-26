using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Galilego.Core;

namespace Galilego.Planning
{
    /// <summary>Орбитальный трек тела в инерциальной СК общего родителя (позиция, скорость в момент t).</summary>
    public interface IOrbitTrack
    {
        /// <summary>false — время вне диапазона эфемерид.</summary>
        bool TryEvaluate(double timeSeconds, out Vector3d position, out Vector3d velocity);
    }

    public enum CaptureKind { None = 0, Circular = 1, Elliptic = 2 }

    /// <summary>Постановка межпланетного перелёта «тело → тело» (патч-коники, нулевой радиус сферы действия).</summary>
    public sealed class TransferSpec
    {
        public double MuCentral;
        public double CentralRadius;
        /// <summary>Минимально допустимый радиус дуги вокруг родителя (R + атмосфера/корона + запас).</summary>
        public double MinCentralRadius;
        public IOrbitTrack From;
        public IOrbitTrack To;
        /// <summary>Нормаль «прямого» направления (момент импульса орбиты тела-старта вокруг родителя).</summary>
        public Vector3d Normal = new Vector3d(0d, 0d, 1d);
        public bool Prograde = true;

        public double MuFrom;
        public double ParkRadiusFrom;
        public double MuTo;
        public double ParkRadiusTo;
        public CaptureKind Capture = CaptureKind.Circular;
        public double CaptureEccentricity;
    }

    public struct TransferSolution
    {
        public bool Valid;
        public double T0, T1;
        public Vector3d RFrom, VFrom, RTo, VTo;           // состояния тел в моменты T0, T1 (СК родителя)
        public Vector3d VTransferDep, VTransferArr;       // скорости переходной дуги на концах (СК родителя)
        public Vector3d VInfDep, VInfArr;                 // избытки (v_дуги − v_тела)
        public double DepDv, ArrDv;                       // идеальные: с круговой парковочной, захват по режиму
        public double ArcMinRadius;                       // минимальный радиус дуги относительно родителя

        public double Tof { get { return T1 - T0; } }
        public double Total { get { return DepDv + ArrDv; } }
        public double C3 { get { return VInfDep.SqrMagnitude; } }
    }

    public sealed class PorkchopGrid
    {
        public double T0Start, StepS;
        public int NDep, KMin, NTof;
        public float[] Total, DepDv, ArrDv, VInfDep;      // [i*NTof + (k−KMin)], NaN — недоступно

        public double DepartTime(int i) { return T0Start + (i * StepS); }
        public double Tof(int k) { return k * StepS; }
        public int Index(int i, int k) { return (i * NTof) + (k - KMin); }
    }

    public struct GridMinimum
    {
        public int I, K;
        public double Total;
    }

    /// <summary>
    /// Окна перелёта: сетка «дата отлёта × время полёта», оценка клетки, локальные минимумы, уточнение.
    /// Сетка равномерная по времени: прибытие = t0 + (i+k)·шаг, поэтому треки тел вычисляются ОДИН раз на узел
    /// (N+M вызовов эфемерид, а не N·M), а N·M задач Ламберта — чистые функции над массивами (Parallel.For).
    /// </summary>
    public static class TransferScanner
    {
        public static bool Evaluate(TransferSpec s, double t0, double t1, out TransferSolution sol)
        {
            sol = default;
            if (!(t1 > t0))
            {
                return false;
            }

            if (!s.From.TryEvaluate(t0, out Vector3d rA, out Vector3d vA) || !s.To.TryEvaluate(t1, out Vector3d rB, out Vector3d vB))
            {
                return false;
            }

            return EvaluateStates(s, t0, t1, rA, vA, rB, vB, out sol);
        }

        public static bool EvaluateStates(TransferSpec s, double t0, double t1,
            Vector3d rA, Vector3d vA, Vector3d rB, Vector3d vB, out TransferSolution sol)
        {
            sol = default;
            if (!LambertPrograde.TrySolve(s.MuCentral, rA, rB, t1 - t0, s.Normal, s.Prograde, out Vector3d v1, out Vector3d v2))
            {
                return false;
            }

            double rMin = ArcMinRadius(s.MuCentral, rA, v1, rB);
            if (!(rMin >= s.MinCentralRadius))
            {
                return false;
            }

            Vector3d infDep = v1 - vA;
            Vector3d infArr = v2 - vB;
            double dep = ConicMath.EscapeDvCircular(s.MuFrom, s.ParkRadiusFrom, infDep.Magnitude);
            double arr = 0d;
            double vInfArr = infArr.Magnitude;
            if (s.Capture == CaptureKind.Circular)
            {
                arr = ConicMath.CaptureDvCircular(s.MuTo, s.ParkRadiusTo, vInfArr);
            }
            else if (s.Capture == CaptureKind.Elliptic)
            {
                arr = ConicMath.CaptureDvEllipse(s.MuTo, s.ParkRadiusTo, s.CaptureEccentricity, vInfArr);
            }

            if (!double.IsFinite(dep) || !double.IsFinite(arr))
            {
                return false;
            }

            sol = new TransferSolution
            {
                Valid = true,
                T0 = t0, T1 = t1,
                RFrom = rA, VFrom = vA, RTo = rB, VTo = vB,
                VTransferDep = v1, VTransferArr = v2,
                VInfDep = infDep, VInfArr = infArr,
                DepDv = dep, ArrDv = arr,
                ArcMinRadius = rMin,
            };
            return true;
        }

        /// <summary>
        /// Минимальный радиус дуги r1→r2 (коника через (r1,v1)): концы и периапсис, если дуга его проходит.
        /// ν1 ∈ (−π,π] — истинная аномалия начала, Δν — угол дуги вдоль движения; периапсис пройден при ν1&lt;0≤ν1+Δν
        /// или ν1+Δν≥2π.
        /// </summary>
        public static double ArcMinRadius(double mu, Vector3d r1, Vector3d v1, Vector3d r2)
        {
            double n1 = r1.Magnitude;
            double n2 = r2.Magnitude;
            double rMin = Math.Min(n1, n2);
            Vector3d h = Vector3d.Cross(r1, v1);
            double hm = h.Magnitude;
            if (!(hm > 0d))
            {
                return 0d; // радиальная дуга проходит через центр
            }

            Vector3d hHat = h / hm;
            Vector3d eVec = ((r1 * (v1.SqrMagnitude - (mu / n1))) - (v1 * Vector3d.Dot(r1, v1))) / mu;
            double e = eVec.Magnitude;
            if (e < 1e-9d)
            {
                return rMin;
            }

            Vector3d eHat = eVec / e;
            Vector3d rHat = r1 / n1;
            double nu1 = Math.Atan2(Vector3d.Dot(Vector3d.Cross(eHat, rHat), hHat), Vector3d.Dot(eHat, rHat));
            double dnu = Math.Atan2(Vector3d.Dot(Vector3d.Cross(r1, r2), hHat), Vector3d.Dot(r1, r2));
            if (dnu < 0d)
            {
                dnu += 2d * Math.PI;
            }

            double nu2 = nu1 + dnu;
            bool passed = (nu1 < 0d && nu2 >= 0d) || nu2 >= 2d * Math.PI;
            if (passed)
            {
                rMin = Math.Min(rMin, hm * hm / mu / (1d + e));
            }

            return rMin;
        }

        /// <summary>Строит сетку. Время: тела считаются в вызывающем потоке, Ламберт — параллельно.</summary>
        public static PorkchopGrid Scan(TransferSpec s, double t0Start, double stepS, int nDep, int kMin, int kMax)
        {
            var g = new PorkchopGrid
            {
                T0Start = t0Start, StepS = stepS, NDep = nDep, KMin = kMin, NTof = kMax - kMin + 1,
            };
            int cells = nDep * g.NTof;
            g.Total = new float[cells];
            g.DepDv = new float[cells];
            g.ArrDv = new float[cells];
            g.VInfDep = new float[cells];

            var rA = new Vector3d[nDep];
            var vA = new Vector3d[nDep];
            var okA = new bool[nDep];
            for (int i = 0; i < nDep; i++)
            {
                okA[i] = s.From.TryEvaluate(t0Start + (i * stepS), out rA[i], out vA[i]);
            }

            int nB = nDep - 1 + kMax - kMin + 1;
            var rB = new Vector3d[nB];
            var vB = new Vector3d[nB];
            var okB = new bool[nB];
            for (int j = 0; j < nB; j++)
            {
                okB[j] = s.To.TryEvaluate(t0Start + ((kMin + j) * stepS), out rB[j], out vB[j]);
            }

            Parallel.For(0, nDep, i =>
            {
                for (int k = kMin; k <= kMax; k++)
                {
                    int idx = (i * g.NTof) + (k - kMin);
                    int jb = i + k - kMin;
                    g.Total[idx] = g.DepDv[idx] = g.ArrDv[idx] = g.VInfDep[idx] = float.NaN;
                    if (k <= 0 || !okA[i] || !okB[jb])
                    {
                        continue;
                    }

                    double t0 = t0Start + (i * stepS);
                    if (EvaluateStates(s, t0, t0 + (k * stepS), rA[i], vA[i], rB[jb], vB[jb], out TransferSolution sol))
                    {
                        g.Total[idx] = (float)sol.Total;
                        g.DepDv[idx] = (float)sol.DepDv;
                        g.ArrDv[idx] = (float)sol.ArrDv;
                        g.VInfDep[idx] = (float)sol.VInfDep.Magnitude;
                    }
                }
            });
            return g;
        }

        /// <summary>Локальные минимумы сетки (8-соседство), по возрастанию цены, разделённые ≥ minSeparationCells по дате.</summary>
        public static List<GridMinimum> LocalMinima(PorkchopGrid g, int maxCount, int minSeparationCells)
        {
            var all = new List<GridMinimum>();
            for (int i = 0; i < g.NDep; i++)
            {
                for (int k = g.KMin; k < g.KMin + g.NTof; k++)
                {
                    float c = g.Total[g.Index(i, k)];
                    if (float.IsNaN(c)) { continue; }
                    bool isMin = true;
                    for (int di = -1; di <= 1 && isMin; di++)
                    {
                        for (int dk = -1; dk <= 1; dk++)
                        {
                            int ii = i + di, kk = k + dk;
                            if ((di == 0 && dk == 0) || ii < 0 || ii >= g.NDep || kk < g.KMin || kk >= g.KMin + g.NTof) { continue; }
                            float n = g.Total[g.Index(ii, kk)];
                            if (!float.IsNaN(n) && n < c) { isMin = false; break; }
                        }
                    }

                    if (isMin) { all.Add(new GridMinimum { I = i, K = k, Total = c }); }
                }
            }

            all.Sort((a, b) => a.Total.CompareTo(b.Total));
            var picked = new List<GridMinimum>();
            foreach (GridMinimum m in all)
            {
                bool far = true;
                foreach (GridMinimum p in picked)
                {
                    if (Math.Abs(p.I - m.I) < minSeparationCells) { far = false; break; }
                }

                if (far) { picked.Add(m); if (picked.Count >= maxCount) { break; } }
            }

            return picked;
        }

        /// <summary>Уточнение окна симплексом по (t0, t1) от клетки сетки. Допустимое t0 ≥ t0Min.</summary>
        public static bool Refine(TransferSpec s, double t0, double t1, double scaleS, double t0Min,
            out TransferSolution best)
        {
            double Cost(double[] x)
            {
                if (x[0] < t0Min || x[1] - x[0] < 1d) { return 1e15d; }
                return Evaluate(s, x[0], x[1], out TransferSolution sol) ? sol.Total : 1e15d;
            }

            double[] xb = NelderMead.Minimize(Cost, new[] { t0, t1 }, new[] { scaleS, scaleS }, 1e-9d, 600, out double fmin);
            best = default;
            return fmin < 1e14d && Evaluate(s, xb[0], xb[1], out best);
        }
    }
}
