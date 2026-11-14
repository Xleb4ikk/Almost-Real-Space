using System;

namespace Galilego.Core
{
    /// <summary>
    /// Two-body propagation in universal variables (Stumpff functions, Laguerre-Conway
    /// iteration). Works for ellipses, parabolas and hyperbolas and for negative dt.
    ///
    /// This is the drift used by the ported WH-BM14A composition. The state is advanced by
    /// increments, x = x0 + (f-1) x0 + g v0, so each substep contributes only ~eps*|increment|
    /// instead of rebuilding orbital elements from absolute angles.
    ///
    /// Ported from the study implementation (Tools/P1bTests/Study/framework/KeplerUniversal.cs),
    /// which was verified against SciPy DOP853: 1e-12..1e-19 on ellipses/hyperbola/near-parabola,
    /// 200 000 substeps of a circular orbit end with 4e-11 relative position error.
    /// </summary>
    public static class KeplerUniversalDrift
    {
        /// <summary>Stumpff C(z) = sum (-z)^k/(2k+2)!, S(z) = sum (-z)^k/(2k+3)!, without cancellation.</summary>
        public static void Stumpff(double z, out double c, out double s)
        {
            if (Math.Abs(z) < 1d)
            {
                double cc = 0d;
                double ss = 0d;
                double pw = 1d;
                double f2 = 2d;
                double f3 = 6d;
                for (int k = 0; k < 14; k++)
                {
                    cc += pw / f2;
                    ss += pw / f3;
                    pw *= -z;
                    f2 *= ((2d * k) + 3d) * ((2d * k) + 4d);
                    f3 *= ((2d * k) + 4d) * ((2d * k) + 5d);
                }

                c = cc;
                s = ss;
                return;
            }

            if (z > 0d)
            {
                double w = Math.Sqrt(z);
                double sw = Math.Sin(0.5d * w);
                c = 2d * sw * sw / z;
                s = (w - Math.Sin(w)) / (w * z);
                return;
            }

            double wh = Math.Sqrt(-z);
            double sh = Math.Sinh(0.5d * wh);
            c = 2d * sh * sh / (-z);
            s = (Math.Sinh(wh) - wh) / (wh * (-z));
        }

        private static double KeplerF(
            double chi, double rvs, double q, double r0, double sm, double dt, double alpha)
        {
            double chi2 = chi * chi;
            double z = alpha * chi2;
            double c;
            double s;
            Stumpff(z, out c, out s);
            return (rvs * chi2 * c) + (q * chi2 * chi * s) + (r0 * chi) - (sm * dt);
        }

        /// <summary>
        /// Advances position/velocity of <paramref name="state"/> by <paramref name="dt"/> in the
        /// central field mu (centre at the origin), in place. Mass is untouched. Returns the
        /// number of iterations. Throws InvalidOperationException when the root cannot be found.
        /// </summary>
        public static int Propagate(double mu, ref SpacecraftIntegrationState state, double dt)
        {
            if (dt == 0d)
            {
                return 0;
            }

            if (!(mu > 0d))
            {
                throw new InvalidOperationException("Kepler drift needs mu > 0.");
            }

            double x0 = state.Position.X;
            double y0 = state.Position.Y;
            double z0 = state.Position.Z;
            double vx = state.Velocity.X;
            double vy = state.Velocity.Y;
            double vz = state.Velocity.Z;
            double r0 = Math.Sqrt((x0 * x0) + (y0 * y0) + (z0 * z0));
            double v2 = (vx * vx) + (vy * vy) + (vz * vz);
            double rv = (x0 * vx) + (y0 * vy) + (z0 * vz);
            double sm = Math.Sqrt(mu);
            double alpha = (2d / r0) - (v2 / mu);
            double chi;
            if (alpha > 1e-12d)
            {
                chi = sm * dt * alpha;
                if (Math.Abs(chi) > 1d)
                {
                    chi = sm * dt / r0;
                }
            }
            else if (alpha < -1e-9d)
            {
                double a = 1d / alpha;
                double sgn = dt > 0d ? 1d : -1d;
                double arg = (-2d * mu * alpha * dt)
                    / (rv + (sgn * Math.Sqrt(-mu * a) * (1d - (r0 * alpha))));
                if (arg > 0d && !double.IsInfinity(arg) && !double.IsNaN(arg))
                {
                    chi = sgn * Math.Sqrt(-a) * Math.Log(arg);
                }
                else
                {
                    chi = sm * dt / r0;
                }
            }
            else
            {
                chi = sm * dt / r0;
            }

            double q = 1d - (alpha * r0);
            double rvs = rv / sm;
            int it = 0;
            bool converged = false;
            for (; it < 60; it++)
            {
                double chi2 = chi * chi;
                double z = alpha * chi2;
                double c;
                double s;
                Stumpff(z, out c, out s);
                double f = (rvs * chi2 * c) + (q * chi2 * chi * s) + (r0 * chi) - (sm * dt);
                double f1 = (rvs * chi * (1d - (z * s))) + (q * chi2 * c) + r0;
                double f2 = (rvs * (1d - (z * c))) + (q * chi * (1d - (z * s)));
                double disc = Math.Abs((16d * f1 * f1) - (20d * f * f2));
                double den = f1 + ((f1 >= 0d ? 1d : -1d) * Math.Sqrt(disc));
                if (den == 0d || double.IsNaN(den))
                {
                    break;
                }

                double d = 5d * f / den;
                chi -= d;
                double fScale = Math.Abs(sm * dt) + (r0 * Math.Abs(chi)) + 1d;
                if (Math.Abs(d) <= (1e-15d * Math.Abs(chi)) + 1e-300d
                    || Math.Abs(f) <= 1e-14d * fScale)
                {
                    converged = true;
                    it++;
                    break;
                }
            }

            if (!converged || double.IsNaN(chi) || double.IsInfinity(chi))
            {
                double sign = dt >= 0d ? 1d : -1d;
                double lo = 0d;
                double hi = sign * Math.Max(1d, sm * Math.Abs(dt) / Math.Max(r0, 1d));
                double fhi = KeplerF(hi, rvs, q, r0, sm, dt, alpha);
                int expand = 0;
                while ((sign * fhi) < 0d && expand++ < 200
                    && Math.Abs(hi) < 1e300d && !double.IsNaN(fhi))
                {
                    hi *= 2d;
                    fhi = KeplerF(hi, rvs, q, r0, sm, dt, alpha);
                }

                if (!double.IsNaN(fhi) && (sign * fhi) >= 0d)
                {
                    for (int k = 0; k < 200; k++)
                    {
                        double mid = 0.5d * (lo + hi);
                        double fm = KeplerF(mid, rvs, q, r0, sm, dt, alpha);
                        it++;
                        if (double.IsNaN(fm))
                        {
                            break;
                        }

                        if ((sign * fm) >= 0d)
                        {
                            hi = mid;
                            fhi = fm;
                        }
                        else
                        {
                            lo = mid;
                        }

                        if (Math.Abs(hi - lo) <= (1e-15d * Math.Max(1d, Math.Abs(mid))) + 1e-300d)
                        {
                            chi = mid;
                            converged = true;
                            break;
                        }
                    }

                    if (!converged)
                    {
                        chi = 0.5d * (lo + hi);
                        converged = !double.IsNaN(chi) && !double.IsInfinity(chi);
                    }

                    if (converged)
                    {
                        double fCheck = KeplerF(chi, rvs, q, r0, sm, dt, alpha);
                        double fScale = Math.Abs(sm * dt) + (r0 * Math.Abs(chi)) + 1d;
                        converged = !double.IsNaN(fCheck)
                            && Math.Abs(fCheck) <= 1e-12d * fScale;
                    }
                }
            }

            if (!converged || double.IsNaN(chi) || double.IsInfinity(chi))
            {
                throw new InvalidOperationException(
                    "Kepler solver did not converge: r0=" + r0.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                    + " alpha=" + alpha.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                    + " dt=" + dt.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                    + " chi=" + chi.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                    + " it=" + it.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            double chiSq = chi * chi;
            double zf = alpha * chiSq;
            double cf;
            double sf;
            Stumpff(zf, out cf, out sf);
            double df = -chiSq * cf / r0;
            double g = dt - (chiSq * chi * sf / sm);
            double nx = x0 + (df * x0) + (g * vx);
            double ny = y0 + (df * y0) + (g * vy);
            double nz = z0 + (df * z0) + (g * vz);
            double r = Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
            double dfd = sm / (r * r0) * chi * ((zf * sf) - 1d);
            double dgd = -chiSq * cf / r;
            state.Position = new Vector3d(nx, ny, nz);
            state.Velocity = new Vector3d(
                vx + (dfd * x0) + (dgd * vx),
                vy + (dfd * y0) + (dgd * vy),
                vz + (dfd * z0) + (dgd * vz));
            return it;
        }
    }
}
