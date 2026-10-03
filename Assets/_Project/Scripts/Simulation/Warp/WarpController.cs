namespace Galilego.Events
{
    /// <summary>
    /// Неизменяемый снимок ввода на control-tick. Передаётся В RHS как параметр,
    /// а не читается из живого контроллера внутри вызова: RHS обязан оставаться
    /// pure (многократные вызовы на шаг, включая отклонённые), побочные эффекты
        /// чтения ввода внутри производной запрещены. Throttle снимка потребляет
        /// ThrustSource через расписание ThrottleAt — частота обновления снимка
        /// задаёт зерно управления (доказано T2/T14).
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
    /// Физический варп: то же RHS/интегратор/событийный слой, что и на ×1 —
    /// разница только в том, сколько симулированного времени просят пройти за
    /// реальный кадр. Квант Δt_control — граница, которую ни один принятый чанк
    /// не может пересечь, пока активна тяга: при физическом варпе управление
    /// не грубеет, ввод сэмплируется с тем же шагом по симулированному времени,
    /// что и на ×1 (доказано T2/T14).
    ///
    /// Лестница факторов дискретная [1, 5, 10, 100, 500, 1000, 3000] с двумя
    /// капами: в атмосфере effective = min(ступень, 3); тяга разрешена только
    /// при effective ≤ 3 — выше двигатель принудительно на нуле, полёт
    /// баллистический (дальний варп). Дальний варп внутри атмосферы невозможен
    /// по построению (drag-вклад → CanEnterLongWarp=false, T22e), а кап ×3
    /// делает физический варп с тягой там легитимным.
    /// </summary>
    public sealed class WarpController
    {
        public const double ControlTickSeconds = 0.5d;
        public const double MinWarpFactor = 1d;

        /// <summary>Дискретные ступени ускорения времени.</summary>
        public static readonly double[] WarpRungs = { 1d, 5d, 10d, 100d, 500d, 1000d, 3000d };

        /// <summary>Верхняя ступень (космос, без тяги — дальний варп).</summary>
        public const double MaxWarpFactor = 3000d;

        /// <summary>Кап effective-фактора внутри атмосферы.</summary>
        public const double AtmosphereMaxWarpFactor = 3d;

        /// <summary>Тяга разрешена только при effective ≤ этого значения.</summary>
        public const double ThrustMaxWarpFactor = 3d;

        public double WarpFactor { get; private set; } = 1d;

        public ControlInputSnapshot CurrentControlSnapshot { get; private set; } =
            ControlInputSnapshot.Idle(0d);

        private double nextTickBoundary = ControlTickSeconds;

        /// <summary>
        /// true — следующий SampleControl обязан пройти мимо тиковой сетки
        /// (после ResetTicks/PrepareForLongWarp/старта). Прежний sentinel
        /// «TickTimeSeconds == 0» был проверкой ЭПОХИ, а не отсутствия сэмпла:
        /// после ресета с ненулевым t (зажигание на t=5с) первый сэмпл до
        /// t+0.5 молча терялся — начало прожига шло с нулевым газом, а
        /// PrepareForLongWarp мог молча НЕ обнулить старый ненулевой throttle
        /// (аудит pre-Unity/9.1).
        /// </summary>
        private bool snapshotPending = true;

        /// <summary>Снап запроса к ближайшей ступени лестницы вниз (запрос 7 → ×5).</summary>
        public void SetWarpFactor(double factor)
        {
            double rung = WarpRungs[0];
            for (int i = 0; i < WarpRungs.Length; i++)
            {
                if (factor >= WarpRungs[i])
                {
                    rung = WarpRungs[i];
                }
            }

            WarpFactor = rung;
        }

        /// <summary>
        /// Effective-фактор кадра: внутри атмосферы любая ступень выше капа
        /// даёт ровно AtmosphereMaxWarpFactor. Правило живёт здесь, а не в
        /// вызывающем коде, чтобы капитуляция капа была невозможна молча.
        /// </summary>
        public double EffectiveWarpFactor(bool inAtmosphere)
        {
            return inAtmosphere && WarpFactor > AtmosphereMaxWarpFactor ? AtmosphereMaxWarpFactor : WarpFactor;
        }

        /// <summary>
        /// Тяга выше физического варпа запрещена целиком: при effective > 3
        /// throttle принудительно 0 (ступени 5+ — только баллистика).
        /// </summary>
        public double EffectiveThrottle(double rawThrottle, double effectiveFactor)
        {
            return effectiveFactor > ThrustMaxWarpFactor ? 0d : rawThrottle;
        }

        /// <summary>
        /// Обнуляет снимок газа перед входом в дальний варп: CanEnterLongWarp
        /// проверяет фактический вклад тяг-источников, а он читает последний
        /// snapshot — без обнуления старый ненулевой throttle молча блокировал бы
        /// легитимный безтопливный варп. Безусловно (не через SampleControl):
        /// старый сэмпл обязан быть затёрт при любых временах.
        /// </summary>
        public void PrepareForLongWarp(double currentTimeSeconds)
        {
            CurrentControlSnapshot = ControlInputSnapshot.Idle(currentTimeSeconds);
            nextTickBoundary = currentTimeSeconds + ControlTickSeconds;
            snapshotPending = true;
        }

        /// <summary>
        /// Обновляет снимок ввода на границе тика. Вызывается снаружи, не из RHS.
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
        /// Максимальный размер следующего чанка с учётом границы control-tick.
        /// Без тяги — ограничение только сверху (maxChunk); с тягой чанк
        /// обрезается по границе тика и никогда её не пересекает.
        /// Побочный эффект зафиксирован контрактом: при отставании границы
        /// от currentTime граница сдвигается прямо здесь (однопоточный
        /// последовательный вызов из игрового цикла — единственная схема
        /// потребления).
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
                untilTick = ControlTickSeconds;
                nextTickBoundary = currentTimeSeconds + ControlTickSeconds;
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
