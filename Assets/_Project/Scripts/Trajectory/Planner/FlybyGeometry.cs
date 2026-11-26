using System;
using Galilego.Core;

namespace Galilego.Planning
{
    /// <summary>Пролёт у тела в СК этого тела: что нужно для стыковки двух дуг Ламберта и что это даёт.</summary>
    public struct FlybyResult
    {
        /// <summary>false — геометрия вне допустимого диапазона углов (см. DeficitDv), пролёт не реализуется без доп. импульса.</summary>
        public bool Feasible;

        public double TurnNeeded;            // угол между v∞_in и v∞_out
        public double TurnMin, TurnMax;      // достижимый коридор: rp=SOI … rp=rpMin
        public double PeriapsisRadius;
        public double PoweredDv;             // импульс в периапсисе (разные |v∞|)
        public double DeficitDv;             // что не закрывается периапсисом: |v∞_out − достижимый|
        public Vector3d PeriapsisDir;        // от тела к периапсису (ось гиперболы)
        public Vector3d MotionDir;           // единичная скорость в периапсисе
        public double SpeedPeriIn, SpeedPeriOut;
        public double ImpactParameterIn;     // прицельная дальность входной ветви b = (μ/v∞²)·√(e²−1)
        public double VelocityChange;        // |v∞_out − v∞_in|, изменение вектора скорости аппарата (все СК)
    }

    public static class FlybyGeometry
    {
        /// <summary>
        /// Стыковка v∞_in → v∞_out. Один периапсис на обе ветви: угол поворота δ = asin(1/e_in)+asin(1/e_out)
        /// (F-MAN-10 для каждой ветви), разность |v∞| закрывается импульсом в периапсисе (F-MAN-05).
        /// Коридор поворота: [δ(rp=SOI), δ(rp=rpMin)]; вне коридора — недобор, цена DeficitDv = 2·|v∞_out|·sin(ε/2).
        /// </summary>
        public static FlybyResult Evaluate(double mu, double rpMin, double soiRadius, Vector3d vInfIn, Vector3d vInfOut)
        {
            var res = new FlybyResult();
            double vi = vInfIn.Magnitude;
            double vo = vInfOut.Magnitude;
            if (!(vi > 1e-6d) || !(vo > 1e-6d))
            {
                return res;
            }

            Vector3d uIn = vInfIn / vi;
            Vector3d uOut = vInfOut / vo;
            double c = Math.Max(-1d, Math.Min(1d, Vector3d.Dot(uIn, uOut)));
            double need = Math.Atan2(Vector3d.Cross(uIn, uOut).Magnitude, c);
            double max = ConicMath.PoweredFlybyTurn(mu, rpMin, vi, vo);
            double min = double.IsFinite(soiRadius) && soiRadius > rpMin ? ConicMath.PoweredFlybyTurn(mu, soiRadius, vi, vo) : 0d;
            res.TurnNeeded = need;
            res.TurnMax = max;
            res.TurnMin = min;
            res.VelocityChange = (vInfOut - vInfIn).Magnitude;

            double rp;
            if (need > max)
            {
                rp = rpMin;
                res.DeficitDv = 2d * vo * Math.Sin(0.5d * (need - max));
            }
            else if (need < min)
            {
                rp = soiRadius;
                res.DeficitDv = 2d * vo * Math.Sin(0.5d * (min - need));
            }
            else if (!ConicMath.TryPeriapsisForTurn(mu, vi, vo, need, out rp))
            {
                rp = soiRadius;
            }

            res.Feasible = !(res.DeficitDv > 0d);
            res.PeriapsisRadius = rp;
            res.SpeedPeriIn = Math.Sqrt((vi * vi) + (2d * mu / rp));
            res.SpeedPeriOut = Math.Sqrt((vo * vo) + (2d * mu / rp));
            res.PoweredDv = Math.Abs(res.SpeedPeriOut - res.SpeedPeriIn);
            double eIn = ConicMath.FlybyEccentricity(mu, rp, vi);
            res.ImpactParameterIn = mu / (vi * vi) * Math.Sqrt((eIn * eIn) - 1d);

            // Ось гиперболы x̂ (к периапсису): x̂·û_in = 1/e_in, x̂·û_out = −1/e_out, x̂ в плоскости (û_in, û_out).
            double eOut = ConicMath.FlybyEccentricity(mu, rp, vo);
            double den = 1d - (c * c);
            if (den > 1e-18d)
            {
                double a = ((1d / eIn) + (c / eOut)) / den;
                double b = ((-1d / eOut) - (c / eIn)) / den;
                Vector3d x = ((uIn * a) + (uOut * b)).Normalized;
                res.PeriapsisDir = x;
                res.MotionDir = (uIn - (x * Vector3d.Dot(uIn, x))).Normalized;
            }

            return res;
        }
    }
}
