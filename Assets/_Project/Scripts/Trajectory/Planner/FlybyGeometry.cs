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
        public double TurnMin, TurnMax;      // δ при rp = SOI и rp = rpMin (информационно: слабее TurnMin — бесплатно, сильнее TurnMax — DeficitDv)
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
        /// Верхний предел поворота — δ(rp = rpMin); слабее δ(rp = SOI) поворот получается периапсисом за SOI
        /// и бесплатен (SOI — граница патч-коник, не стена). Платная нехватка есть только сверху:
        /// DeficitDv = 2·|v∞_out|·sin(ε/2).
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
                // Даже на минимально допустимом периапсисе поворота не хватает: разницу
                // приходится докупать импульсом (это и есть DeficitDv).
                rp = rpMin;
                res.DeficitDv = 2d * vo * Math.Sin(0.5d * (need - max));
            }
            else if (need <= 1e-12d)
            {
                // Геометрия «насквозь»: пролёт ничего не делает, периапсис не определён.
                // Стоимости нет — SOI не является границей поля тяготения (см. ниже).
                rp = double.IsFinite(soiRadius) && soiRadius > 0d ? soiRadius : mu / (vi * vi);
            }
            else if (!ConicMath.TryPeriapsisForTurn(mu, vi, vo, need, out rp))
            {
                // need < min: более слабый поворот достигается периапсисом ЗА пределами SOI.
                // Это бесплатно (SOI — граница применимости патч-коник, а не стена); прежний
                // штраф 2·v∞·sin((min−need)/2) завышал цену самых дешёвых пролётов.
                rp = double.IsFinite(soiRadius) && soiRadius > 0d ? soiRadius : mu / (vi * vi);
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
            else
            {
                // v∞_in ∥ v∞_out: направление периапсиса не определено (любая нормаль подходит).
                // Раньше здесь оставался нулевой вектор, и PeriapsisPosition = (0,0,0) —
                // прицел в центр планеты. Даём детерминированную перпендикулярную ось.
                Vector3d t = Math.Abs(uIn.X) < 0.9d ? new Vector3d(1d, 0d, 0d) : new Vector3d(0d, 1d, 0d);
                Vector3d x = Vector3d.Cross(uIn, t).Normalized;
                res.PeriapsisDir = x;
                res.MotionDir = uIn;
            }

            return res;
        }
    }
}
