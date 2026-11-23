using System;

namespace Galilego.Events
{
    /// <summary>Frame decision: how much simulated time to advance and by which layer.</summary>
    public readonly struct WarpFrameDecision
    {
        /// <summary>Effective factor after the altitude cap.</summary>
        public double EffectiveFactor { get; }

        /// <summary>Simulated seconds for this frame (effective × realDt).</summary>
        public double SimSeconds { get; }

        /// <summary>true — long warp (LongWarpDriver, ballistics, thrust forbidden);
        /// false — physical chain (Propagate/SpacecraftPhysics, thrust allowed).</summary>
        public bool UseLongWarp { get; }

        /// <summary>
        /// true — игрок выбрал полный варп, но он недоступен (полоса высоты или
        /// ненулевой вклад негравитационных сил). Кадр идёт физической цепочкой с
        /// полосовым капом; вызывающий обязан один раз залогировать это.
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
    /// Pure accelerated-time frame planner: altitude band (WarpAltitudeGate) + the
    /// formal long-warp gate (CanEnterLongWarp) → effective factor and the execution
    /// layer.
    ///
    /// Полосы высоты (owner's rule, 2026-11):
    ///   • ниже 15 км  — полный варп запрещён (эффективный ×1), физическая цепочка ×2…×6 жива;
    ///   • 15…80 км    — полный варп не выше ×50;
    ///   • выше 80 км  — вся лестница.
    /// Стоящий аппарат эти полосы не видит: парковочный путь выбирает любой фактор
    /// на любой высоте (TryParkedWarpStep), и это не дело планировщика.
    ///
    /// Полный варп выбирается ТОЛЬКО при выполненном формальном условии
    /// <paramref name="longWarpAllowed"/> (LongWarpDriver.CanEnterLongWarp: нулевой вклад
    /// тяги и сопротивления). Иначе — физическая цепочка с полосовым капом:
    /// молчаливой баллистики в атмосфере не бывает. horizonSeconds caps at the end of the
    /// world (BakedEndSeconds − t): past the end a frame simply does not fly.
    /// </summary>
    public static class WarpPlanner
    {
        public static WarpFrameDecision ComputeFrame(
            WarpController warp, double maxFullFactor, bool longWarpAllowed, double realDtSeconds, double horizonSeconds)
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

            bool fullRequested = warp.Mode == WarpMode.Full;
            bool fullAllowed = maxFullFactor >= WarpController.FullRungs[0];
            bool useLongWarp = fullRequested && fullAllowed && longWarpAllowed;
            bool refused = fullRequested && !useLongWarp;

            // Physical ladder: the bands never touch it (owner: "физическое разрешено
            // везде"), the ×6 ceiling lives in WarpController. Full: the band cap, and
            // 1 when the band forbids it outright.
            double effective = warp.EffectiveWarpFactor(maxFullFactor);

            double sim = effective * realDtSeconds;
            if (double.IsFinite(horizonSeconds) && sim > horizonSeconds)
            {
                sim = Math.Max(0d, horizonSeconds);
            }

            return new WarpFrameDecision(effective, sim, useLongWarp, refused);
        }
    }
}
