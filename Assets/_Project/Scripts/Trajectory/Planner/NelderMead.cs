using System;

namespace Galilego.Planning
{
    /// <summary>
    /// Симплекс Нелдера–Мида для гладких целей малой размерности (даты окна). Без производных: цель планировщика
    /// кусочно-гладкая (Ламберт, недоступные клетки), градиент ненадёжен. Детерминирован.
    /// </summary>
    public static class NelderMead
    {
        public static double[] Minimize(Func<double[], double> f, double[] x0, double[] scale,
            double tolerance, int maxIterations, out double fMin)
        {
            int n = x0.Length;
            var simplex = new double[n + 1][];
            var value = new double[n + 1];
            simplex[0] = (double[])x0.Clone();
            for (int i = 0; i < n; i++)
            {
                simplex[i + 1] = (double[])x0.Clone();
                simplex[i + 1][i] += scale[i];
            }

            for (int i = 0; i <= n; i++) { value[i] = f(simplex[i]); }

            for (int iter = 0; iter < maxIterations; iter++)
            {
                int lo = 0, hi = 0;
                for (int i = 1; i <= n; i++)
                {
                    if (value[i] < value[lo]) { lo = i; }
                    if (value[i] > value[hi]) { hi = i; }
                }

                int second = hi == 0 ? 1 : 0;
                for (int i = 0; i <= n; i++)
                {
                    if (i != hi && value[i] > value[second]) { second = i; }
                }

                double size = 0d;
                for (int i = 0; i <= n; i++)
                {
                    for (int d = 0; d < n; d++)
                    {
                        size = Math.Max(size, Math.Abs(simplex[i][d] - simplex[lo][d]) / scale[d]);
                    }
                }

                if (size < tolerance) { break; }

                var centroid = new double[n];
                for (int i = 0; i <= n; i++)
                {
                    if (i == hi) { continue; }
                    for (int d = 0; d < n; d++) { centroid[d] += simplex[i][d] / n; }
                }

                double[] Point(double t)
                {
                    var p = new double[n];
                    for (int d = 0; d < n; d++) { p[d] = centroid[d] + (t * (simplex[hi][d] - centroid[d])); }
                    return p;
                }

                double[] xr = Point(-1d);
                double fr = f(xr);
                if (fr < value[lo])
                {
                    double[] xe = Point(-2d);
                    double fe = f(xe);
                    if (fe < fr) { simplex[hi] = xe; value[hi] = fe; }
                    else { simplex[hi] = xr; value[hi] = fr; }
                }
                else if (fr < value[second])
                {
                    simplex[hi] = xr; value[hi] = fr;
                }
                else
                {
                    double[] xc = fr < value[hi] ? Point(-0.5d) : Point(0.5d);
                    double fc = f(xc);
                    if (fc < Math.Min(fr, value[hi])) { simplex[hi] = xc; value[hi] = fc; }
                    else
                    {
                        for (int i = 0; i <= n; i++)
                        {
                            if (i == lo) { continue; }
                            for (int d = 0; d < n; d++) { simplex[i][d] = simplex[lo][d] + (0.5d * (simplex[i][d] - simplex[lo][d])); }
                            value[i] = f(simplex[i]);
                        }
                    }
                }
            }

            int best = 0;
            for (int i = 1; i <= n; i++) { if (value[i] < value[best]) { best = i; } }
            fMin = value[best];
            return simplex[best];
        }
    }
}
