using System;

namespace Galilego.Events
{
    /// <summary>Frame decision: how much simulated time to advance and by which layer.</summary>
    public readonly struct WarpFrameDecision
    {
        /// <summary>Effective factor after the atmosphere cap.</summary>
        public double EffectiveFactor { get; }

        /// <summary>Simulated seconds for this frame (effective × realDt).</summary>
        public double SimSeconds { get; }

        /// <summary>true — long warp (LongWarpDriver, ballistics, thrust forbidden);
        /// false — physical chain (Propagate/SpacecraftPhysics, thrust allowed).</summary>
        public bool UseLongWarp { get; }

        /// <summary>
        /// true — игрок выбрал полный варп, но он запрещён (атмосфера или ненулевой вклад
        /// негравитационных сил). Кадр идёт физической цепочкой с капом ×3; вызывающий
        /// обязан один раз залогировать это, чтобы срез не был молчаливым.
        /// </summary>
        public bool LongWarpRefused { get; }

        public WarpFrameDecision(double effectiveFactor, double simSeconds, bool useLongWarp, bool longWarpRefused)
        {
            EffectiveFactor = effectiveFactor;
            SimSeconds = simSeconds;
            UseLongWarp = useLongWarp;
            LongWarpRefused = longWarpRefused;
        }
    }

    /// <summary>
    /// Pure accelerated-time frame planner: effective factor (the atmosphere cap
    /// lives in WarpController) → simulated seconds → execution layer.
    ///
    /// Полный варп выбирается ТОЛЬКО при выполненном формальном условии
    /// <paramref name="longWarpAllowed"/> (LongWarpDriver.CanEnterLongWarp: нулевой вклад
    /// тяги и сопротивления). Иначе — физическая цепочка с капом ×3: молчаливая
    /// баллистика в атмосфере запрещена. horizonSeconds caps at the end of the world
    /// (BakedEndSeconds − t): past the end a frame simply does not fly.
    /// </summary>
    public static class WarpPlanner
    {
        public static WarpFrameDecision ComputeFrame(
            WarpController warp, bool inAtmosphere, bool longWarpAllowed, double realDtSeconds, double horizonSeconds)
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

            bool useLongWarp = warp.Mode == WarpMode.Full && !inAtmosphere && longWarpAllowed;
            bool refused = warp.Mode == WarpMode.Full && !useLongWarp;

            double effective = warp.EffectiveWarpFactor(inAtmosphere);
            if (refused)
            {
                // Полный варп недоступен: работаем физической цепочкой, и её потолок —
                // тот же кап, что в атмосфере/на посадке.
                effective = Math.Min(effective, WarpController.AtmosphereMaxWarpFactor);
            }

            double sim = effective * realDtSeconds;

            if (double.IsFinite(horizonSeconds) && sim > horizonSeconds)
            {
                sim = Math.Max(0d, horizonSeconds);
            }

            return new WarpFrameDecision(effective, sim, useLongWarp, refused);
        }
    }
}
