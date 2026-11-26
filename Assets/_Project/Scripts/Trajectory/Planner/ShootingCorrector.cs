using System;
using System.Collections.Generic;
using Galilego.Core;

namespace Galilego.Planning
{
    /// <summary>Модель «импульс → положение в момент цели относительно тела цели» поверх НАСТОЯЩЕЙ физики игры.</summary>
    public interface IShootingModel
    {
        /// <summary>Положение корабля относительно тела-цели в момент targetTime (СК тела) после импульса dvWorld.</summary>
        bool TryShoot(Vector3d dvWorld, double targetTime, out Vector3d relPositionAtTarget);
    }

    /// <summary>
    /// Дифференциальная коррекция (метод стрельбы): подбирает вектор импульса так, чтобы в фиксированный момент
    /// положение относительно тела-цели совпало с заданным (прицел в периапсис). Патч-коники (нулевая SOI) дают
    /// только начальное приближение; промах из-за настоящей n-body динамики здесь убирается до допуска. 3 неизвестных
    /// (ΔV) — 3 условия (положение). Якобиан — конечные разности (4 прогона на итерацию), шаг Ньютона с делением пополам.
    /// </summary>
    public static class ShootingCorrector
    {
        /// <summary>Необязательная трассировка итераций (отладка/тесты).</summary>
        public static Action<string> Trace;

        /// <summary>Относительное затухание λ/‖J‖ (регуляризация вырожденных направлений).</summary>
        public static double DampingRatio = 1e-4d;

        /// <summary>Запрос одного пробного прогона. Корутина отдаёт его наружу, драйвер выполняет и заполняет Ok/RelPos.</summary>
        public sealed class Shot
        {
            public IShootingModel Model;
            public Vector3d Dv;
            public double Time;
            public bool Ok;
            public Vector3d RelPos;
        }

        /// <summary>Итог решения (корутины не могут иметь out-параметры).</summary>
        public sealed class Result
        {
            public bool Ok;
            public Vector3d Dv;
            public double Miss = double.PositiveInfinity;
            public int Iterations;
        }

        /// <summary>Выполняет запросы корутины синхронно (тесты, простые вызовы). В игре запросы выполняются по одному за кадр.</summary>
        public static void Drive(IEnumerable<Shot> co)
        {
            foreach (Shot s in co) { s.Ok = s.Model.TryShoot(s.Dv, s.Time, out s.RelPos); }
        }

        public static bool Solve(IShootingModel model, double targetTime, Vector3d dv0, Vector3d target, double toleranceM, int maxIterations,
            double differenceStep, out Vector3d dv, out double miss, out int iterations)
        {
            var res = new Result();
            Drive(SolveCo(model, targetTime, dv0, target, toleranceM, maxIterations, differenceStep, res));
            dv = res.Dv; miss = res.Miss; iterations = res.Iterations;
            return res.Ok;
        }

        /// <summary>Ньютон с затуханием как корутина: каждый вызов модели — точка передачи управления.</summary>
        public static IEnumerable<Shot> SolveCo(IShootingModel model, double targetTime, Vector3d dv0, Vector3d target, double toleranceM,
            int maxIterations, double differenceStep, Result res)
        {
            res.Dv = dv0; res.Miss = double.PositiveInfinity; res.Iterations = 0; res.Ok = false;
            Vector3d dv = dv0;
            var s0 = new Shot { Model = model, Dv = dv, Time = targetTime };
            yield return s0;
            if (!s0.Ok) { yield break; }
            Vector3d p = s0.RelPos;
            Vector3d resid = p - target;
            double miss = resid.Magnitude;
            res.Miss = miss;
            for (int it = 0; it < maxIterations && miss > toleranceM; it++)
            {
                res.Iterations = it + 1;
                var jac = new double[3, 3];
                for (int j = 0; j < 3; j++)
                {
                    Vector3d step = new Vector3d(j == 0 ? differenceStep : 0d, j == 1 ? differenceStep : 0d, j == 2 ? differenceStep : 0d);
                    var sj = new Shot { Model = model, Dv = dv + step, Time = targetTime };
                    yield return sj;
                    if (!sj.Ok) { yield break; }
                    Vector3d col = (sj.RelPos - p) / differenceStep;
                    jac[0, j] = col.X; jac[1, j] = col.Y; jac[2, j] = col.Z;
                }

                // Затухающий метод наименьших квадратов (min-norm): δ = −Jᵀ(JJᵀ+λ²I)⁻¹·res. Прицеливание вдоль почти
                // вырожденного направления (дуга ≈180°: положение по нормали не зависит от начальной скорости по нормали)
                // не тратит ΔV на неуправляемое, и шум конечных разностей не раскачивает решение.
                double fro = 0d;
                for (int a0 = 0; a0 < 3; a0++) { for (int b0 = 0; b0 < 3; b0++) { fro += jac[a0, b0] * jac[a0, b0]; } }
                double lambda2 = DampingRatio * DampingRatio * fro;
                var jjt = new double[3, 3];
                for (int a0 = 0; a0 < 3; a0++)
                {
                    for (int b0 = 0; b0 < 3; b0++)
                    {
                        double sum = a0 == b0 ? lambda2 : 0d;
                        for (int k0 = 0; k0 < 3; k0++) { sum += jac[a0, k0] * jac[b0, k0]; }
                        jjt[a0, b0] = sum;
                    }
                }

                if (!TrySolve3(jjt, -resid, out Vector3d y)) { Trace?.Invoke("  singular"); yield break; }
                Vector3d delta = new Vector3d(
                    (jac[0, 0] * y.X) + (jac[1, 0] * y.Y) + (jac[2, 0] * y.Z),
                    (jac[0, 1] * y.X) + (jac[1, 1] * y.Y) + (jac[2, 1] * y.Z),
                    (jac[0, 2] * y.X) + (jac[1, 2] * y.Y) + (jac[2, 2] * y.Z));
                double scale = 1d;
                bool improved = false;
                for (int k = 0; k < 8; k++)
                {
                    Vector3d trial = dv + (delta * scale);
                    var st = new Shot { Model = model, Dv = trial, Time = targetTime };
                    yield return st;
                    if (st.Ok && (st.RelPos - target).Magnitude < miss)
                    {
                        dv = trial; p = st.RelPos; resid = p - target; miss = resid.Magnitude; improved = true; break;
                    }

                    scale *= 0.5d;
                }

                res.Dv = dv; res.Miss = miss;
                if (!improved) { Trace?.Invoke("  no improvement; step norm=" + delta.Magnitude.ToString("E2")); break; }
            }

            res.Dv = dv; res.Miss = miss; res.Ok = miss <= toleranceM;
        }

        /// <summary>Очередная цель лестницы: положение относительно тела в момент времени и допуск.</summary>
        public struct Aim
        {
            public double Time;
            public Vector3d Position;
            public double ToleranceM;
        }

        /// <summary>
        /// Лестница целей от далёкой точки входной гиперболы к периапсису. Одним шагом в периапсис Ньютон не сходится:
        /// цель на дне потенциальной ямы, отображение «импульс → положение» там резко нелинейно (сдвиг времени прилёта
        /// на сотни секунд = сдвиг на тысячи км вдоль гиперболы). Каждая следующая цель решается из близкого
        /// приближения предыдущей, поэтому остаётся в зоне сходимости.
        /// </summary>
        public static bool SolveLadder(IShootingModel model, Vector3d dv0, Aim[] ladder, int maxIterations, double differenceStep,
            out Vector3d dv, out double miss, out int iterations)
        {
            var res = new Result();
            Drive(SolveLadderCo(model, dv0, ladder, maxIterations, differenceStep, res));
            dv = res.Dv; miss = res.Miss; iterations = res.Iterations;
            return res.Ok;
        }

        public static IEnumerable<Shot> SolveLadderCo(IShootingModel model, Vector3d dv0, Aim[] ladder, int maxIterations, double differenceStep, Result res)
        {
            res.Dv = dv0; res.Miss = double.PositiveInfinity; res.Iterations = 0; res.Ok = false;
            Vector3d dv = dv0;
            int total = 0;
            for (int i = 0; i < ladder.Length; i++)
            {
                var r = new Result();
                foreach (Shot s in SolveCo(model, ladder[i].Time, dv, ladder[i].Position, ladder[i].ToleranceM, maxIterations, differenceStep, r)) { yield return s; }
                total += r.Iterations;
                if (r.Miss < double.PositiveInfinity) { dv = r.Dv; }
                res.Dv = dv; res.Miss = r.Miss; res.Iterations = total;
                if (!r.Ok) { yield break; }
            }

            res.Ok = true;
        }

        private static bool TrySolve3(double[,] a, Vector3d b, out Vector3d x)
        {
            x = Vector3d.Zero;
            double det = (a[0, 0] * ((a[1, 1] * a[2, 2]) - (a[1, 2] * a[2, 1])))
                       - (a[0, 1] * ((a[1, 0] * a[2, 2]) - (a[1, 2] * a[2, 0])))
                       + (a[0, 2] * ((a[1, 0] * a[2, 1]) - (a[1, 1] * a[2, 0])));
            if (!(Math.Abs(det) > 1e-300d) || double.IsNaN(det)) { return false; }
            double x0 = ((b.X * ((a[1, 1] * a[2, 2]) - (a[1, 2] * a[2, 1]))) - (a[0, 1] * ((b.Y * a[2, 2]) - (a[1, 2] * b.Z))) + (a[0, 2] * ((b.Y * a[2, 1]) - (a[1, 1] * b.Z)))) / det;
            double x1 = ((a[0, 0] * ((b.Y * a[2, 2]) - (a[1, 2] * b.Z))) - (b.X * ((a[1, 0] * a[2, 2]) - (a[1, 2] * a[2, 0]))) + (a[0, 2] * ((a[1, 0] * b.Z) - (b.Y * a[2, 0])))) / det;
            double x2 = ((a[0, 0] * ((a[1, 1] * b.Z) - (b.Y * a[2, 1]))) - (a[0, 1] * ((a[1, 0] * b.Z) - (b.Y * a[2, 0]))) + (b.X * ((a[1, 0] * a[2, 1]) - (a[1, 1] * a[2, 0])))) / det;
            x = new Vector3d(x0, x1, x2);
            return x.IsFinite;
        }
    }
}
