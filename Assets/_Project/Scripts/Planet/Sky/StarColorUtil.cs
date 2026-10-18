using System;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Absolute black-body (stellar photosphere) color — pure math, no Unity API,
    /// covered by a harness test (T80).
    /// Planck spectrum → CIE 1931 eye response functions (analytic fit
    /// Wyman et al. 2013: piecewise gaussians, no tables) → XYZ →
    /// linear sRGB (HDRP works in linear, so the conversion is exact).
    /// The result is normalized by the maximum channel to [0,1]; the brightness
    /// multiplier is the caller's job (HDR color >1 feeds bloom).
    /// Physical check (T80): 1500 K — red dominates, 5772 K — warm white
    /// (R≥G≥B; stars of this temperature are WHITE — Rayleigh scattering in the
    /// atmosphere makes them look yellow), 10000 K — blue dominates.
    /// </summary>
    public static class StarColorUtil
    {
        /// <summary>hc/k in nm·K — for Planck expressed in wavelength nanometers.</summary>
        private const double C2 = 1.4387768775039337e7;

        /// <summary>
        /// Linear sRGB black-body color at temperatureKelvin, normalized by the
        /// maximum channel. Components ≥ 0, max = 1.
        /// </summary>
        public static Vector3d FromTemperature(double temperatureKelvin)
        {
            if (!(temperatureKelvin > 0d) || double.IsInfinity(temperatureKelvin))
            {
                throw new ArgumentOutOfRangeException(nameof(temperatureKelvin), "Температура обязана быть положительной и конечной.");
            }

            double x = 0d;
            double y = 0d;
            double z = 0d;
            for (int lambda = 380; lambda <= 780; lambda += 5)
            {
                double planck = PlanckSpectralRadiance(lambda, temperatureKelvin);
                x += CieX(lambda) * planck;
                y += CieY(lambda) * planck;
                z += CieZ(lambda) * planck;
            }

            // XYZ → linear sRGB (standard sRGB D65 matrix).
            double r = (3.2406255d * x) + (-1.537208d * y) + (-0.4986286d * z);
            double g = (-0.9689307d * x) + (1.8757561d * y) + (0.0415175d * z);
            double b = (0.0557101d * x) + (-0.2040261d * y) + (1.0569959d * z);

            r = Math.Max(0d, r);
            g = Math.Max(0d, g);
            b = Math.Max(0d, b);
            double max = Math.Max(r, Math.Max(g, b));
            if (max <= 0d)
            {
                return new Vector3d(1d, 1d, 1d);
            }

            return new Vector3d(r / max, g / max, b / max);
        }

        /// <summary>Planck spectral radiance (an arbitrary common factor — it cancels in the normalization).</summary>
        private static double PlanckSpectralRadiance(double lambdaNm, double temperatureKelvin)
        {
            double lambda5 = lambdaNm * lambdaNm * lambdaNm * lambdaNm * lambdaNm;
            return 1d / (lambda5 * (Math.Exp(C2 / (lambdaNm * temperatureKelvin)) - 1d));
        }

        /// <summary>Piecewise gaussian (σ differs before and after μ — fit of the CIE curves).</summary>
        private static double PiecewiseGaussian(double lambda, double mu, double sigma1, double sigma2)
        {
            double t = lambda - mu;
            double sigma = t < 0d ? sigma1 : sigma2;
            return Math.Exp(-(t * t) / (2d * sigma * sigma));
        }

        private static double CieX(double lambda)
        {
            return (1.056d * PiecewiseGaussian(lambda, 599.8d, 37.9d, 31.0d))
                + (0.362d * PiecewiseGaussian(lambda, 442.0d, 16.0d, 26.7d))
                - (0.065d * PiecewiseGaussian(lambda, 501.1d, 20.4d, 26.2d));
        }

        private static double CieY(double lambda)
        {
            return (0.821d * PiecewiseGaussian(lambda, 568.8d, 46.9d, 40.5d))
                + (0.286d * PiecewiseGaussian(lambda, 530.9d, 16.3d, 31.1d));
        }

        private static double CieZ(double lambda)
        {
            return (1.217d * PiecewiseGaussian(lambda, 437.0d, 11.8d, 36.0d))
                + (0.681d * PiecewiseGaussian(lambda, 459.0d, 26.0d, 13.8d));
        }
    }
}
