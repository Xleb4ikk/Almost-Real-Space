using System;
using Galilego.Core;

namespace Galilego.Planning
{
    /// <summary>Результат построения отлётной гиперболы через заданную точку орбиты.</summary>
    public struct EscapeBurn
    {
        public bool Valid;

        /// <summary>Импульс в инерциальных осях тела (ΔV = v_гиперболы − v_корабля), м/с.</summary>
        public Vector3d DvWorld;

        /// <summary>Скорость сразу после импульса, относительно тела.</summary>
        public Vector3d VPost;

        public double PeriapsisRadius;
        public double Eccentricity;

        /// <summary>true: корабль ещё не прошёл периапсис гиперболы (ν&lt;0), он реально достижим.</summary>
        public bool PeriapsisAhead;

        public double Dv { get { return DvWorld.Magnitude; } }
    }

    /// <summary>
    /// Импульс, переводящий корабль из точки r со скоростью vShip на гиперболу с заданным вектором избытка v∞.
    /// Вывод (проверен в tests против оракула — движение до бесконечности):
    /// асимптота гиперболы лежит в плоскости (r, Ŝ), Ŝ = v∞/|v∞|; в этой плоскости, θ — угол от r̂ к Ŝ вдоль h,
    /// a = μ/v∞², k = √(e²−1) удовлетворяет  a·k² − r·sinθ·k − r·(1−cosθ) = 0  (из r = p/(1+e·cos ν), ν_r = ν∞ − θ,
    /// cos ν∞ = −1/e). Скорость: v_r = (μ/h)·e·sin ν_r, v_t = h/r, h = √(μ·p), p = a·k².
    /// Два направления обращения (h = ±n̂) дают два решения; выбирается дешевле. Смена плоскости входит в цену сама.
    /// </summary>
    public static class EscapeSolver
    {
        /// <param name="mu">μ тела отлёта</param>
        /// <param name="minPeriapsis">минимальный допустимый радиус периапсиса (R + атмосфера + запас)</param>
        /// <param name="r">положение корабля относительно тела</param>
        /// <param name="vShip">скорость корабля относительно тела</param>
        /// <param name="vInf">требуемый вектор гиперболического избытка (в осях тела)</param>
        public static bool TrySolve(double mu, double minPeriapsis, Vector3d r, Vector3d vShip, Vector3d vInf, out EscapeBurn burn)
        {
            burn = default;
            double rm = r.Magnitude;
            double vinf = vInf.Magnitude;
            if (!(mu > 0d) || !(rm > 0d) || !(vinf > 1e-6d) || !r.IsFinite || !vShip.IsFinite || !vInf.IsFinite)
            {
                return false;
            }

            Vector3d rHat = r / rm;
            Vector3d sHat = vInf / vinf;
            Vector3d cross = Vector3d.Cross(rHat, sHat);
            double sinT = cross.Magnitude;
            double cosT = Vector3d.Dot(rHat, sHat);
            double theta = Math.Atan2(sinT, cosT); // 0…π
            Vector3d nHat;
            if (sinT > 1e-9d)
            {
                nHat = cross / sinT;
            }
            else
            {
                // r̂ ∥ Ŝ: плоскость не определена — берём плоскость текущей орбиты корабля (минимум смены плоскости).
                Vector3d h = Vector3d.Cross(r, vShip);
                nHat = h.Magnitude > 0d ? h.Normalized : new Vector3d(0d, 0d, 1d);
            }

            double a = mu / (vinf * vinf);
            bool found = false;
            EscapeBurn best = default;
            for (int orient = 0; orient < 2; orient++)
            {
                double sign = orient == 0 ? 1d : -1d;
                Vector3d hHat = nHat * sign;
                double thetaS = sign * theta; // знаковый угол от r̂ к Ŝ вокруг hHat
                double sinS = Math.Sin(thetaS);
                double omc = 1d - Math.Cos(thetaS);
                double disc = (rm * rm * sinS * sinS) + (4d * a * rm * omc);
                double k = ((rm * sinS) + Math.Sqrt(disc)) / (2d * a);
                if (!double.IsFinite(k) || k < 0d)
                {
                    continue;
                }

                double ecc = Math.Sqrt(1d + (k * k));
                double nuInf = Math.Atan2(k, -1d); // cos ν∞ = −1/e, sin ν∞ = k/e
                double nuR = nuInf - thetaS;
                nuR = Math.IEEERemainder(nuR, 2d * Math.PI);
                double p = a * k * k;
                double hMag = Math.Sqrt(mu * p);
                double vRad = hMag > 0d ? mu / hMag * ecc * Math.Sin(nuR) : Math.Sqrt((vinf * vinf) + (2d * mu / rm));
                double vTan = hMag / rm;
                Vector3d tHat = Vector3d.Cross(hHat, rHat);
                Vector3d vPost = (rHat * vRad) + (tHat * vTan);
                double rp = a * (ecc - 1d);
                bool ahead = nuR < 0d;
                if (ahead && rp < minPeriapsis)
                {
                    continue; // гипербола нырнёт ниже допустимого периапсиса ещё до ухода
                }

                var cand = new EscapeBurn
                {
                    Valid = true,
                    DvWorld = vPost - vShip,
                    VPost = vPost,
                    PeriapsisRadius = rp,
                    Eccentricity = ecc,
                    PeriapsisAhead = ahead,
                };
                if (!found || cand.Dv < best.Dv)
                {
                    best = cand;
                    found = true;
                }
            }

            burn = best;
            return found && best.DvWorld.IsFinite;
        }
    }
}
