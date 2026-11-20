namespace Galilego.Events
{
    /// <summary>Вид ускорения времени.</summary>
    public enum WarpMode
    {
        /// <summary>Физическое: та же физика и тот же шаг модели, только больше модельного времени за кадр.</summary>
        Physical,

        /// <summary>Полное (долгое): баллистика без тяги и сопротивления, адаптивный шаг по событиям.</summary>
        Full
    }

    /// <summary>
    /// Immutable input snapshot at a control tick. Passed INTO the RHS as a
    /// parameter rather than read from the live controller inside the call: the RHS
    /// must stay pure (multiple calls per step, including rejected ones), so side
    /// effects of reading input inside the derivative are forbidden. The snapshot's
    /// Throttle is consumed by ThrustSource through the ThrottleAt schedule — the
    /// snapshot update rate sets the control grain.
    /// </summary>
    public readonly struct ControlInputSnapshot
    {
        public readonly double TickTimeSeconds;
        public readonly double Throttle;

        public ControlInputSnapshot(double tickTimeSeconds, double throttle)
        {
            TickTimeSeconds = tickTimeSeconds;
            Throttle = throttle;
        }

        public static ControlInputSnapshot Idle(double tickTimeSeconds)
        {
            return new ControlInputSnapshot(tickTimeSeconds, 0d);
        }
    }

    /// <summary>
    /// Ускорение времени двумя режимами (решение заказчика):
    ///
    ///  • Physical ×1, ×2, ×3, ×4, ×6 — ТЯГА РАЗРЕШЕНА, физика прежняя: шаг модели не
    ///    огрубляется, тик управления 0.5 с и чанк полётной модели 0.05 с остаются
    ///    модельными. В кадр просто укладывается больше чанков.
    ///  • Full ×10 … ×1 000 000 — только баллистика: тяга принудительно ноль, вход
    ///    формально проверяется LongWarpDriver.CanEnterLongWarp (нет тяги и нет
    ///    сопротивления). При невыполнении — физическая цепочка с капом ×3.
    ///
    /// Кап в атмосфере/на посадке (×3) сохраняется: там модель шага теснее связана с
    /// поверхностью и контактной зоной.
    ///
    /// Старый SetWarpFactor(double) остаётся: клавиши 1…7 и чит-меню работают по той же
    /// лестнице, ни одна ступень (включая 3000) не теряется.
    /// </summary>
    public sealed class WarpController
    {
        public const double ControlTickSeconds = 0.5d;
        public const double MinWarpFactor = 1d;

        /// <summary>Физические ступени (тяга разрешена, шаг модели не меняется).</summary>
        public static readonly int[] PhysicalRungs = { 1, 2, 3, 4, 6 };

        /// <summary>Полные ступени (баллистика, тяга запрещена).</summary>
        public static readonly double[] FullRungs = { 10d, 50d, 100d, 500d, 1000d, 10000d, 100000d, 1000000d };

        /// <summary>Верхняя физическая ступень.</summary>
        public const double PhysicalMaxWarpFactor = 6d;

        /// <summary>Верхняя ступень полного варпа.</summary>
        public const double MaxWarpFactor = 1000000d;

        /// <summary>Кап на эффективный фактор в атмосфере и на посадке.</summary>
        public const double AtmosphereMaxWarpFactor = 3d;

        /// <summary>
        /// Потолок для игрока ВНЕ корабля (EVA/на поверхности): тот же верх физической
        /// цепочки, чтобы настройка ×6 не срезалась молча. Долгий варп без игрока в
        /// корабле запрещён отдельно (playerCap конечен).
        /// </summary>
        public const double PlayerOutsideMaxWarpFactor = PhysicalMaxWarpFactor;

        /// <summary>
        /// Лестница старых клавиш 1…7 (историческое поведение): 1, 5, 10, 100, 500, 1000,
        /// 3000. Значения попадают в объединённую таблицу SetWarpFactor без потерь.
        /// </summary>
        public static readonly double[] WarpRungs = { 1d, 5d, 10d, 100d, 500d, 1000d, 3000d };

        /// <summary>Объединённая таблица для SetWarpFactor: обе лестницы плюс 3000.</summary>
        private static readonly double[] AllRungs =
            { 1d, 2d, 3d, 4d, 5d, 6d, 10d, 50d, 100d, 500d, 1000d, 3000d, 10000d, 100000d, 1000000d };

        public WarpMode Mode { get; private set; } = WarpMode.Physical;

        /// <summary>Запрошенный фактор (в режиме Physical ≤ 6, в Full ≥ 10).</summary>
        public double Factor { get; private set; } = 1d;

        /// <summary>Последняя выбранная физическая ступень — её показывает слот панели.</summary>
        public int LastPhysicalFactor { get; private set; } = 1;

        public ControlInputSnapshot CurrentControlSnapshot { get; private set; } =
            ControlInputSnapshot.Idle(0d);

        private double nextTickBoundary = ControlTickSeconds;

        /// <summary>
        /// true — the next SampleControl must bypass the tick grid
        /// (after ResetTicks/PrepareForLongWarp/start).
        /// </summary>
        private bool snapshotPending = true;

        /// <summary>
        /// Физическое ускорение: тяга разрешена, шаг модели не огрубляется.
        /// </summary>
        public void SetPhysicalWarp(int factor)
        {
            Mode = WarpMode.Physical;
            Factor = factor < MinWarpFactor ? MinWarpFactor
                : (factor > PhysicalMaxWarpFactor ? PhysicalMaxWarpFactor : factor);
            LastPhysicalFactor = (int)Factor;
        }

        /// <summary>
        /// Полное ускорение: баллистика без тяги. Ниже ×10 не опускаем — это уже
        /// физическая цепочка, и молчаливое смешивание режимов запрещено.
        /// </summary>
        public void SetFullWarp(double factor)
        {
            Mode = WarpMode.Full;
            Factor = factor < FullRungs[0] ? FullRungs[0]
                : (factor > MaxWarpFactor ? MaxWarpFactor : factor);
        }

        /// <summary>
        /// Старый вход (клавиши 1…7, чит-меню, билдер): снимок вниз по объединённой
        /// таблице и выбор режима по значению. Поведение ступеней сохранено.
        /// </summary>
        public void SetWarpFactor(double factor)
        {
            double rung = AllRungs[0];
            for (int i = 0; i < AllRungs.Length; i++)
            {
                if (factor >= AllRungs[i])
                {
                    rung = AllRungs[i];
                }
            }

            if (rung <= PhysicalMaxWarpFactor)
            {
                SetPhysicalWarp((int)rung);
            }
            else
            {
                SetFullWarp(rung);
            }
        }

        /// <summary>
        /// Сброс в физический ×1 — обязательное состояние после любой перемотки.
        /// </summary>
        public void ResetToPhysicalOne()
        {
            Mode = WarpMode.Physical;
            Factor = 1d;
            LastPhysicalFactor = 1;
        }

        /// <summary>
        /// Фактор, который РЕАЛЬНО применён в этом кадре: в атмосфере кап ×3.
        /// В режиме Full в атмосфере это значение относится к физической цепочке.
        /// </summary>
        public double EffectiveWarpFactor(bool inAtmosphere)
        {
            double cap = inAtmosphere ? AtmosphereMaxWarpFactor : MaxWarpFactor;
            return Factor > cap ? cap : Factor;
        }

        /// <summary>
        /// Тяга разрешена только в физической цепочке. В долгом варпе она нулевая ВСЕГДА,
        /// и это не проверка «на глаз»: LongWarpDriver.CanEnterLongWarp формально требует
        /// нулевой вклад всех негравитационных источников.
        /// </summary>
        public double EffectiveThrottle(double rawThrottle, bool useLongWarp)
        {
            return useLongWarp ? 0d : rawThrottle;
        }

        /// <summary>
        /// Zeroes the throttle snapshot before entering long warp: CanEnterLongWarp
        /// checks the actual contribution of thrust sources, which reads the last
        /// snapshot — without zeroing, an old non-zero throttle would silently block
        /// legitimate fuel-less warp. Unconditional (not via SampleControl): the old
        /// sample must be overwritten at any time.
        /// </summary>
        public void PrepareForLongWarp(double currentTimeSeconds)
        {
            CurrentControlSnapshot = ControlInputSnapshot.Idle(currentTimeSeconds);
            nextTickBoundary = currentTimeSeconds + ControlTickSeconds;
            snapshotPending = true;
        }

        /// <summary>
        /// Updates the input snapshot at the tick boundary. Called from outside, not from the RHS.
        /// </summary>
        public void SampleControl(double currentTimeSeconds, double throttle)
        {
            if (snapshotPending || currentTimeSeconds >= nextTickBoundary)
            {
                CurrentControlSnapshot = new ControlInputSnapshot(currentTimeSeconds, throttle);
                nextTickBoundary = currentTimeSeconds + ControlTickSeconds;
                snapshotPending = false;
            }
        }

        /// <summary>
        /// Maximum size of the next chunk taking the control-tick boundary into account.
        /// Without thrust the only limit is above (maxChunk); with thrust the chunk is cut
        /// at the tick boundary and never crosses it.
        /// Границу двигает ТОЛЬКО SampleControl (он вызывается первым в кадровом цикле):
        /// раньше она сдвигалась здесь, и снимок газа переставал обновляться вообще.
        /// </summary>
        public double GetMaxChunkSeconds(double currentTimeSeconds, double maxChunk, bool thrustActive)
        {
            if (!thrustActive)
            {
                return maxChunk;
            }

            double untilTick = nextTickBoundary - currentTimeSeconds;
            if (untilTick <= 0d)
            {
                return maxChunk;
            }

            return untilTick < maxChunk ? untilTick : maxChunk;
        }

        public void ResetTicks(double currentTimeSeconds)
        {
            nextTickBoundary = currentTimeSeconds + ControlTickSeconds;
            CurrentControlSnapshot = ControlInputSnapshot.Idle(currentTimeSeconds);
            snapshotPending = true;
        }
    }
}
