using System;

namespace Galilego.Events
{
    /// <summary>Решение кадра: сколько симулированного времени пройти и каким слоем.</summary>
    public readonly struct WarpFrameDecision
    {
        /// <summary>Effective-фактор после атмосферного капа.</summary>
        public double EffectiveFactor { get; }

        /// <summary>Симулированных секунд за этот кадр (effective × realDt).</summary>
        public double SimSeconds { get; }

        /// <summary>true — дальний варп (LongWarpDriver, баллистика, тяга запрещена);
        /// false — физическая цепочка (Propagate/SpacecraftPhysics, тяга разрешена).</summary>
        public bool UseLongWarp { get; }

        public WarpFrameDecision(double effectiveFactor, double simSeconds, bool useLongWarp)
        {
            EffectiveFactor = effectiveFactor;
            SimSeconds = simSeconds;
            UseLongWarp = useLongWarp;
        }
    }

    /// <summary>
    /// Чистый планировщик кадра ускоренного времени: effective-фактор (кап
    /// атмосферы живёт в WarpController) → симулированные секунды → слой
    /// исполнения. Тяга разрешена только при effective ≤ ThrustMaxWarpFactor;
    /// ступени выше — баллистика (LongWarpDriver), где CanEnterLongWarp сам
    /// громко охраняет от забытой тяги. horizonSeconds — кэп по концу мира
    /// (BakedEndSeconds − t): выше конца кадр просто не летит.
    /// </summary>
    public static class WarpPlanner
    {
        public static WarpFrameDecision ComputeFrame(WarpController warp, bool inAtmosphere, double realDtSeconds, double horizonSeconds)
        {
            if (warp == null)
            {
                throw new ArgumentNullException(nameof(warp));
            }

            if (!(realDtSeconds > 0d) || !double.IsFinite(realDtSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(realDtSeconds),
                    "realDtSeconds обязан быть конечным и положительным, получено " + realDtSeconds + ".");
            }

            double effective = warp.EffectiveWarpFactor(inAtmosphere);
            double sim = effective * realDtSeconds;
            bool useLongWarp = effective > WarpController.ThrustMaxWarpFactor;

            if (double.IsFinite(horizonSeconds) && sim > horizonSeconds)
            {
                sim = Math.Max(0d, horizonSeconds);
            }

            return new WarpFrameDecision(effective, sim, useLongWarp);
        }
    }
}
