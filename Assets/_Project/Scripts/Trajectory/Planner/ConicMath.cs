using System;

namespace Galilego.Planning
{
    /// <summary>
    /// Скалярные формулы манёвров (патч-коники). Каждая — из проверенного справочника навыка астронома,
    /// ID в комментариях. Единицы: СИ (м, с, м³/с²), углы в радианах.
    /// </summary>
    public static class ConicMath
    {
        public static double CircularSpeed(double mu, double r)
        {
            return Math.Sqrt(mu / r);
        }

        /// <summary>F-MAN-05: отлёт с круговой орбиты радиуса r на гиперболический избыток vInf.</summary>
        public static double EscapeDvCircular(double mu, double r, double vInf)
        {
            return Math.Sqrt((vInf * vInf) + (2d * mu / r)) - Math.Sqrt(mu / r);
        }

        /// <summary>F-MAN-05: захват в периапсисе rp на круговую орбиту (то же выражение, что и отлёт).</summary>
        public static double CaptureDvCircular(double mu, double rp, double vInf)
        {
            return EscapeDvCircular(mu, rp, vInf);
        }

        /// <summary>F-MAN-05: захват в периапсисе rp на эллипс эксцентриситета e.</summary>
        public static double CaptureDvEllipse(double mu, double rp, double e, double vInf)
        {
            return Math.Sqrt((vInf * vInf) + (2d * mu / rp)) - Math.Sqrt(mu * (1d + e) / rp);
        }

        /// <summary>F-MAN-10: эксцентриситет пролётной гиперболы.</summary>
        public static double FlybyEccentricity(double mu, double rp, double vInf)
        {
            return 1d + (rp * vInf * vInf / mu);
        }

        /// <summary>F-MAN-10: угол поворота вектора v∞, δ = 2·asin(1/e).</summary>
        public static double FlybyTurn(double mu, double rp, double vInf)
        {
            return 2d * Math.Asin(1d / FlybyEccentricity(mu, rp, vInf));
        }

        /// <summary>F-MAN-10: |Δv| вектора скорости аппарата при пассивном пролёте = 2·v∞/e.</summary>
        public static double FlybyVelocityChange(double mu, double rp, double vInf)
        {
            return 2d * vInf / FlybyEccentricity(mu, rp, vInf);
        }

        /// <summary>F-MAN-10 (обращение): периапсис для заданного угла поворота при равных v∞: rp = μ/v∞²·(1/sin(δ/2) − 1).</summary>
        public static double PeriapsisForTurn(double mu, double vInf, double delta)
        {
            return mu / (vInf * vInf) * ((1d / Math.Sin(0.5d * delta)) - 1d);
        }

        /// <summary>
        /// Суммарный угол поворота при ОБЩЕМ периапсисе и разных v∞ на входе и выходе (манёвр в периапсисе):
        /// δ = asin(1/e_in) + asin(1/e_out) (две ветви гиперболы с разными e, F-MAN-10 для каждой ветви).
        /// </summary>
        public static double PoweredFlybyTurn(double mu, double rp, double vInfIn, double vInfOut)
        {
            return Math.Asin(1d / FlybyEccentricity(mu, rp, vInfIn)) + Math.Asin(1d / FlybyEccentricity(mu, rp, vInfOut));
        }

        /// <summary>
        /// Периапсис, дающий требуемый поворот delta ∈ (0,π) при v∞ вход/выход. Строго монотонно по rp:
        /// rp→0 ⇒ δ→π, rp→∞ ⇒ δ→0, поэтому корень единственный (бисекция по ln rp, без производных).
        /// </summary>
        public static bool TryPeriapsisForTurn(double mu, double vInfIn, double vInfOut, double delta, out double rp)
        {
            rp = double.NaN;
            if (!(delta > 0d) || !(delta < Math.PI) || !(vInfIn > 0d) || !(vInfOut > 0d) || !(mu > 0d))
            {
                return false;
            }

            if (Math.Abs(vInfIn - vInfOut) <= 1e-12d * vInfIn)
            {
                rp = PeriapsisForTurn(mu, vInfIn, delta);
                return double.IsFinite(rp) && rp > 0d;
            }

            double scale = mu / (Math.Min(vInfIn, vInfOut) * Math.Min(vInfIn, vInfOut));
            double lo = Math.Log(scale * 1e-12d);
            double hi = Math.Log(scale * 1e12d);
            for (int i = 0; i < 200; i++)
            {
                double mid = 0.5d * (lo + hi);
                double turn = PoweredFlybyTurn(mu, Math.Exp(mid), vInfIn, vInfOut);
                if (turn > delta)
                {
                    lo = mid; // поворот слишком большой ⇒ периапсис надо увеличить
                }
                else
                {
                    hi = mid;
                }

                if (hi - lo < 1e-15d)
                {
                    break;
                }
            }

            rp = Math.Exp(0.5d * (lo + hi));
            return true;
        }

        /// <summary>
        /// Манёвр в общем периапсисе пролёта (оба ветви касаются периапсиса тангенциально): |v_p,out − v_p,in|
        /// (F-MAN-05, скорость в периапсисе гиперболы √(v∞²+2μ/rp)).
        /// </summary>
        public static double PoweredFlybyDv(double mu, double rp, double vInfIn, double vInfOut)
        {
            return Math.Abs(Math.Sqrt((vInfOut * vInfOut) + (2d * mu / rp)) - Math.Sqrt((vInfIn * vInfIn) + (2d * mu / rp)));
        }

        /// <summary>F-MAN-12: синодический период S = T1·T2/|T1−T2|.</summary>
        public static double SynodicPeriod(double period1, double period2)
        {
            return period1 * period2 / Math.Abs(period1 - period2);
        }

        /// <summary>F-NBP-02: сфера действия (Лаплас) r = a·(m/M)^(2/5); m, M можно давать как μ тел.</summary>
        public static double LaplaceSoi(double semiMajorAxis, double muBody, double muParent)
        {
            return semiMajorAxis * Math.Pow(muBody / muParent, 0.4d);
        }
    }
}
