using System;
using System.Collections.Generic;

namespace GalilegoPhysicsTests.Support
{
    internal static class OrderConditions
    {
        /// <summary>
        /// Largest deviation between the RK step series and the exact solution
        /// series for y' = f(y), up to h^order, over several polynomial f and
        /// several y0. Exact rational arithmetic removes accumulation rounding.
        ///
        /// This is a sampled necessary condition, not a proof of every Butcher
        /// order condition (17 at order 5, 200 at order 8). It catches a damaged
        /// coefficient cheaply; empirical order is pinned by the dense-order,
        /// convergence and P1b tests.
        /// </summary>
        public static double MaxSeriesResidual(ExactRational[][] A, ExactRational[] B, int order)
        {
            int stages = B.Length;
            double worst = 0d;
            double[] y0Values = { 0.3d, 1.7d };
            int[][] polynomials =
            {
                new[] { 0, 0, 1 },
                new[] { 0, 0, 0, 1 },
                new[] { 0, 0, 0, 0, 1 },
                new[] { 0, 0, 0, 0, 0, 1 },
                new[] { 0, 0, 0, 0, 0, 0, 1 },
                new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 },
                new[] { 1, -2, 3, -4, 5, -6, 7, -8, 9 }
            };

            foreach (double y0Value in y0Values)
            {
                ExactRational y0 = ExactRational.FromDouble(y0Value);
                foreach (int[] coefficients in polynomials)
                {
                    if (!CoversOrder(coefficients, order))
                    {
                        continue;
                    }

                    double residual = ResidualForPolynomial(A, B, stages, coefficients, y0, order);
                    if (residual > worst)
                    {
                        worst = residual;
                    }
                }
            }

            return worst;
        }

        public static double ResidualForPolynomial(
            ExactRational[][] A,
            ExactRational[] B,
            int stages,
            int[] coefficients,
            ExactRational y0,
            int order)
        {
            ExactRational[] exact = ExactSolution(coefficients, y0, order);
            ExactRational[] rk = RungeKuttaSeries(A, B, stages, coefficients, y0, order);
            double worst = 0d;
            for (int k = 1; k <= order; k++)
            {
                double scale = Math.Max(1d, exact[k].AbsToDouble());
                double residual = (rk[k] - exact[k]).AbsToDouble() / scale;
                if (residual > worst)
                {
                    worst = residual;
                }
            }

            return worst;
        }

        public static double MaxRowSumResidual(ExactRational[][] A, ExactRational[] C)
        {
            double worst = 0d;
            for (int i = 0; i < C.Length; i++)
            {
                ExactRational sum = ExactRational.Zero;
                for (int j = 0; j < A[i].Length; j++)
                {
                    sum += A[i][j];
                }

                double residual = (sum - C[i]).AbsToDouble();
                if (residual > worst)
                {
                    worst = residual;
                }
            }

            return worst;
        }

        public static double SumResidual(ExactRational[] values, double expected)
        {
            ExactRational sum = ExactRational.Zero;
            foreach (ExactRational value in values)
            {
                sum += value;
            }

            return (sum - ExactRational.FromDouble(expected)).AbsToDouble();
        }

        public static ExactRational[] ToRational(double[] values)
        {
            var result = new ExactRational[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                result[i] = ExactRational.FromDouble(values[i]);
            }

            return result;
        }

        public static ExactRational[][] ToRational(double[][] values)
        {
            var result = new ExactRational[values.Length][];
            for (int i = 0; i < values.Length; i++)
            {
                result[i] = ToRational(values[i]);
            }

            return result;
        }

        private static bool CoversOrder(int[] coefficients, int order)
        {
            int degree = 0;
            for (int i = coefficients.Length - 1; i >= 0; i--)
            {
                if (coefficients[i] != 0)
                {
                    degree = i;
                    break;
                }
            }

            return degree >= 2 && degree <= Math.Max(2, order);
        }

        private static ExactRational[] ExactSolution(int[] polynomial, ExactRational y0, int order)
        {
            var y = new ExactRational[order + 1];
            y[0] = y0;
            for (int k = 0; k < order; k++)
            {
                ExactRational[] f = Compose(polynomial, y, order);
                y[k + 1] = f[k] / ExactRational.FromDouble(k + 1);
            }

            return y;
        }

        private static ExactRational[] RungeKuttaSeries(
            ExactRational[][] A,
            ExactRational[] B,
            int stages,
            int[] polynomial,
            ExactRational y0,
            int order)
        {
            var stageSeries = new ExactRational[stages][];
            var stageDerivative = new ExactRational[stages][];

            for (int s = 0; s < stages; s++)
            {
                var y = new ExactRational[order + 1];
                y[0] = y0;
                for (int k = 1; k <= order; k++)
                {
                    ExactRational sum = ExactRational.Zero;
                    for (int j = 0; j < s; j++)
                    {
                        sum += A[s][j] * stageDerivative[j][k - 1];
                    }

                    y[k] = sum;
                }

                stageSeries[s] = y;
                stageDerivative[s] = Compose(polynomial, y, order);
            }

            var result = new ExactRational[order + 1];
            result[0] = y0;
            for (int k = 1; k <= order; k++)
            {
                ExactRational sum = ExactRational.Zero;
                for (int s = 0; s < stages; s++)
                {
                    sum += B[s] * stageDerivative[s][k - 1];
                }

                result[k] = sum;
            }

            return result;
        }

        private static ExactRational[] Compose(int[] coefficients, ExactRational[] y, int order)
        {
            var result = new ExactRational[order + 1];
            for (int i = 0; i <= order; i++)
            {
                result[i] = ExactRational.Zero;
            }

            var power = new ExactRational[order + 1];
            for (int i = 0; i <= order; i++)
            {
                power[i] = ExactRational.Zero;
            }

            power[0] = ExactRational.One;
            for (int k = 0; k < coefficients.Length; k++)
            {
                if (coefficients[k] != 0)
                {
                    ExactRational coefficient = ExactRational.FromDouble(coefficients[k]);
                    for (int i = 0; i <= order; i++)
                    {
                        result[i] += coefficient * power[i];
                    }
                }

                power = Multiply(power, y, order);
            }

            return result;
        }

        private static ExactRational[] Multiply(ExactRational[] a, ExactRational[] b, int order)
        {
            var result = new ExactRational[order + 1];
            for (int i = 0; i <= order; i++)
            {
                result[i] = ExactRational.Zero;
            }

            for (int i = 0; i <= order; i++)
            {
                if (a[i].Numerator.IsZero)
                {
                    continue;
                }

                for (int j = 0; i + j <= order; j++)
                {
                    if (b[j].Numerator.IsZero)
                    {
                        continue;
                    }

                    result[i + j] += a[i] * b[j];
                }
            }

            return result;
        }
    }
}
