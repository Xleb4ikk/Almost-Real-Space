using System;

namespace GalilegoPhysicsTests.Support.Oracles
{
    /// <summary>
    /// Independent analytic two-body propagator used as the oracle. It uses only
    /// System.Math and its own Double3 type; it never calls Core.
    /// Elliptic, circular, hyperbolic and near-parabolic states are handled by
    /// their closed-form anomaly equations (Newton with a deterministic bracket).
    /// </summary>
    internal static class KeplerOracle
    {
        public static OracleState Propagate(Double3 r0, Double3 v0, double mu, double dt)
        {
            if (!(mu > 0d))
            {
                throw new ArgumentOutOfRangeException(nameof(mu));
            }

            double r0Mag = r0.Magnitude;
            if (!(r0Mag > 0d))
            {
                throw new ArgumentOutOfRangeException(nameof(r0));
            }

            double v2 = v0.SqrMagnitude;
            Double3 h = Double3.Cross(r0, v0);
            double hMag = h.Magnitude;
            if (!(hMag > 0d))
            {
                throw new InvalidOperationException("Oracle: radial trajectory has no orbital plane.");
            }

            Double3 hHat = h / hMag;
            Double3 eVec = ((r0 * (v2 - (mu / r0Mag))) - (v0 * Double3.Dot(r0, v0))) / mu;
            double e = eVec.Magnitude;
            double energy = (0.5d * v2) - (mu / r0Mag);
            double a = -mu / (2d * energy);
            double p = (hMag * hMag) / mu;

            if (e < 1e-9d)
            {
                return PropagateCircular(r0, r0Mag, hHat, p, mu, dt);
            }

            Double3 phat = eVec / e;
            Double3 qhat = Double3.Cross(hHat, phat);
            double nu0 = Math.Atan2(Double3.Dot(r0, qhat), Double3.Dot(r0, phat));

            if (a > 0d)
            {
                return PropagateElliptic(phat, qhat, a, e, nu0, mu, dt);
            }

            return PropagateHyperbolic(phat, qhat, a, e, nu0, mu, dt);
        }

        private static OracleState PropagateCircular(Double3 r0, double radius, Double3 hHat, double p, double mu, double dt)
        {
            Double3 phat = r0 / radius;
            Double3 qhat = Double3.Cross(hHat, phat);
            double n = Math.Sqrt(mu / (radius * radius * radius));
            double nu = n * dt;
            double cos = Math.Cos(nu);
            double sin = Math.Sin(nu);
            double speed = Math.Sqrt(mu / radius);
            return new OracleState(
                (phat * (radius * cos)) + (qhat * (radius * sin)),
                (phat * (-speed * sin)) + (qhat * (speed * cos)));
        }

        private static OracleState PropagateElliptic(Double3 phat, Double3 qhat, double a, double e, double nu0, double mu, double dt)
        {
            double n = Math.Sqrt(mu / (a * a * a));
            double e0 = 2d * Math.Atan2(
                Math.Sqrt(1d - e) * Math.Sin(0.5d * nu0),
                Math.Sqrt(1d + e) * Math.Cos(0.5d * nu0));
            double mean0 = e0 - (e * Math.Sin(e0));
            double mean = mean0 + (n * dt);
            double eccentric = SolveElliptic(mean, e);
            double nu = 2d * Math.Atan2(
                Math.Sqrt(1d + e) * Math.Sin(0.5d * eccentric),
                Math.Sqrt(1d - e) * Math.Cos(0.5d * eccentric));
            double p = a * (1d - (e * e));
            double radius = p / (1d + (e * Math.Cos(nu)));
            double speedScale = Math.Sqrt(mu / p);
            return new OracleState(
                phat * (radius * Math.Cos(nu)) + qhat * (radius * Math.Sin(nu)),
                phat * (-speedScale * Math.Sin(nu)) + qhat * (speedScale * (e + Math.Cos(nu))));
        }

        private static OracleState PropagateHyperbolic(Double3 phat, Double3 qhat, double a, double e, double nu0, double mu, double dt)
        {
            double alpha = -a;
            double nu = NormalizeSigned(nu0);
            double halfFactor = Math.Sqrt((e - 1d) / (e + 1d)) * Math.Tan(0.5d * nu);
            double h0 = 2d * Atanh(halfFactor);
            double mean0 = (e * Math.Sinh(h0)) - h0;
            double n = Math.Sqrt(mu / (alpha * alpha * alpha));
            double mean = mean0 + (n * dt);
            double h = SolveHyperbolic(mean, e);
            double radius = a * (1d - (e * Math.Cosh(h)));
            double cosh = Math.Cosh(h);
            double sinh = Math.Sinh(h);
            double x = a * (cosh - e);
            double y = -a * Math.Sqrt((e * e) - 1d) * sinh;
            double vx = -Math.Sqrt(mu * alpha) * sinh / radius;
            double vy = Math.Sqrt(mu * alpha) * Math.Sqrt((e * e) - 1d) * cosh / radius;
            return new OracleState(
                phat * x + qhat * y,
                phat * vx + qhat * vy);
        }

        private static double SolveElliptic(double mean, double e)
        {
            // E lies within [M-e, M+e] because E = M + e·sin(E). Bisection is
            // monotone and cannot jump to a different 2π branch (which Newton
            // does for e close to 1 and small M).
            double lower = mean - e;
            double upper = mean + e;
            for (int i = 0; i < 200; i++)
            {
                double mid = 0.5d * (lower + upper);
                double f = mid - (e * Math.Sin(mid)) - mean;
                if (f > 0d)
                {
                    upper = mid;
                }
                else
                {
                    lower = mid;
                }
            }

            return 0.5d * (lower + upper);
        }

        private static double SolveHyperbolic(double mean, double e)
        {
            double x = mean >= 0d
                ? Asinh(mean / e)
                : -Asinh(-mean / e);
            if (Math.Abs(mean) < e)
            {
                x = mean / (e - 1d);
            }

            for (int i = 0; i < 100; i++)
            {
                double f = (e * Math.Sinh(x)) - x - mean;
                double df = (e * Math.Cosh(x)) - 1d;
                double step = f / df;
                x -= step;
                if (Math.Abs(step) <= 1e-15d * Math.Max(1d, Math.Abs(x)))
                {
                    return x;
                }
            }

            return x;
        }

        private static double Atanh(double x)
        {
            if (x >= 1d)
            {
                return 20d;
            }

            if (x <= -1d)
            {
                return -20d;
            }

            return 0.5d * Math.Log((1d + x) / (1d - x));
        }

        private static double Asinh(double x)
        {
            return Math.Log(x + Math.Sqrt((x * x) + 1d));
        }

        private static double NormalizeSigned(double angle)
        {
            double twoPi = 2d * Math.PI;
            angle %= twoPi;
            if (angle > Math.PI)
            {
                angle -= twoPi;
            }

            if (angle < -Math.PI)
            {
                angle += twoPi;
            }

            return angle;
        }
    }
}
