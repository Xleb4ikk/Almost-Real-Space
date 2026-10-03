using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;
using Ship = Galilego.Spacecraft.Spacecraft;

namespace Galilego.Events
{
    /// <summary>
    /// Драйвер propagate-to-event-and-stop поверх SpacecraftPhysics.
    /// Степперы не трогает и не переписывает: чанки идут через
    /// SpacecraftPhysics.Step, уточнение корней — перепогоном Step от начала
    /// чанка (бисекция, не секущие: event-функции геометрические, бисекция
    /// детерминирована и не требует производных). P1a только детектирует и
    /// останавливается с точными временем/состоянием в корне; реакции нет.
    ///
    /// Корабль мутирует только коммитом принятого чанка или усечённого корня:
    /// зондирование (конец чанка, середины бисекции) идёт через временные
    /// Spacecraft, видимый вызывающему объект не трогается до решения.
    /// Детекторы при этом остаются pure — бейзлайн начала чанка хранит драйвер.
    ///
    /// Защита от zero-time loop держится на четырёх механизмах: пересечение
    /// требует СТРОГОЙ смены знака (g=0 в точке рестарта — не событие);
    /// усечение оставляет корабль строго ЗА событием, а не в середине брекета
    /// (рестарт видит пост-сторону и не переоткрывает тот же корень);
    /// плановое срабатывает только строго позже текущего времени; плюс жёсткая
    /// граница числа чанков с throw при превышении.
    /// </summary>
    public sealed class EventDrivenPropagator
    {
        /// <summary>
        /// Фиксированный абсолютный допуск локализации корня в секундах.
        /// Именно абсолютный, а не доля шага: чанк в будущем станет варпом в
        /// тысячи секунд, и допуск не должен раздуваться вместе с ним — влёт
        /// в атмосферу после долгого перелёта требует той же точности, что и
        /// в реальном времени. Численно безопасен: при t ~ 3e7 с ulp double
        /// ~7.5e-9, запас до 1e-6 — два порядка.
        /// </summary>
        public const double DefaultRootToleranceSeconds = 1e-6;

        public const int MaxRefineIterations = 100;

        public readonly List<ICrossingDetector> CrossingDetectors = new List<ICrossingDetector>();
        public readonly List<ITransitionDetector> TransitionDetectors = new List<ITransitionDetector>();
        public readonly List<TimedEvent> TimedEvents = new List<TimedEvent>();

        /// <summary>
        /// Верхняя граница чанка. Дефолт — тот же MaxStepSize интегратора,
        /// а не новое число: брекет события никогда не шире внутреннего шага.
        /// </summary>
        public double MaxChunkSeconds = OrbitIntegrator.DefaultMaxStepSize;

        /// <summary>
        /// На сколько подчанков делится каждый чанк для детекта. Закрывает долг
        /// P1b: при DetectionSubdivisions=1 (старое поведение) нырок в атмосферу
        /// туда-обратно внутри одного 600с чанка терялся — знаки g на границах
        /// чанка совпадают, пересечений как будто нет. При 20 зерно детекта 30с:
        /// выдерживаются нырки дольше зерна. Остаточный риск честно фиксируется:
        /// пара пересечений целиком внутри одного подчанка (&lt;30с) по-прежнему
        /// теряется — для маневров с мелкими характерными временами зерно
        /// загрубляется явно через это поле, а не молча. Суммарная работа
        /// интегрирования не растёт (сумма подчанков = чанк), дорожают только
        /// дешёвые проверки детекторов на границах.
        /// </summary>
        public int DetectionSubdivisions = 20;

        public double RootToleranceSeconds = DefaultRootToleranceSeconds;

        public double HardHorizonSeconds = double.PositiveInfinity;

        private readonly SpacecraftPhysics physics;

        public EventDrivenPropagator(SpacecraftPhysics physics)
        {
            this.physics = physics ?? throw new ArgumentNullException(nameof(physics));
        }

        /// <summary>
        /// Переключатель уточнения корней: true — бисекция по dense-сегментам
        /// чанка (0 реинтеграций), false — legacy-перепогон PropagateState от
        /// начала чанка. Оставлен для gate-теста T18 и как страховка: инварианты
        /// (строгая смена знака, возврат конца b, anti-zero-time-loop) одинаковы
        /// на обоих путях, расходятся только ценой и точностью интерполянта.
        /// </summary>
        public bool UseDenseRefinement = true;

        /// <summary>
        /// Система для адаптивного зерна детекта (A5). null — зерно фиксировано
        /// значением DetectionSubdivisions (старое поведение). Когда задана:
        /// число подчанков на чанк считается из характерного времени состояния,
        /// но никогда не меньше DetectionSubdivisions — только мельче, не крупнее.
        /// </summary>
        public StarSystem SystemForGrain { get; set; }

        /// <summary>
        /// Продвигает корабль вперёд не дальше targetDt. Вернул событие —
        /// остановился ровно в корне (корабль уже в нём). Вернул null — дошёл
        /// до конца без событий. Только вперёд: события назад во времени
        /// (плановые в прошлом, детект в обратную сторону) — вне рамок P1a.
        /// </summary>
        public EventOccurrence? Propagate(Ship ship, double currentTimeSeconds, double targetDt)
        {
            return PropagateWithSegments(ship, currentTimeSeconds, targetDt, null);
        }

        /// <summary>
        /// Тот же propagate-to-event-and-stop, плюс цепочка dense-сегментов
        /// пройденного пути для сэмплирования рендера без единого Step.
        /// Цепочка — только до события: сегмент, накрывающий корень, входит
        /// целиком (хвост за корнем не длиннее одного внутреннего шага).
        /// render == null — цепочка не отдаётся; сегменты чанка при этом всё
        /// равно собираются, если включён UseDenseRefinement (для бисекции).
        /// </summary>
        public EventOccurrence? PropagateWithSegments(Ship ship, double currentTimeSeconds, double targetDt, List<DenseSegment> render)
        {
            if (ship == null)
                throw new ArgumentNullException(nameof(ship));
            if (targetDt < 0d)
                throw new ArgumentOutOfRangeException(nameof(targetDt), "Драйвер событий работает только вперёд.");
            if (targetDt == 0d)
                return null;
            if (MaxChunkSeconds <= 0d)
                throw new InvalidOperationException("MaxChunkSeconds обязан быть положительным.");
            if (DetectionSubdivisions < 1)
                throw new InvalidOperationException("DetectionSubdivisions обязан быть не меньше 1.");
            // Инвариант границы чанков: фактическое зерно = MaxChunkSeconds/DetectionSubdivisions,
            // а guard ниже считает по DetectionGrain.MaxSubdivisions. Subdivisions больше
            // капа → guard не сходится на честном дальнем варпе — громко при
            // конфигурации, а не посреди варпа (аудит S1/A6).
            if (DetectionSubdivisions > DetectionGrain.MaxSubdivisions)
                throw new InvalidOperationException(
                    "DetectionSubdivisions=" + DetectionSubdivisions + " > MaxSubdivisions=" +
                    DetectionGrain.MaxSubdivisions + " — граница числа чанков считается от капа, конфигурация уйдёт в throw на варпе.");
            if (RootToleranceSeconds <= 0d)
                throw new InvalidOperationException("RootToleranceSeconds обязан быть положительным.");
            bool capped = false;
            if (currentTimeSeconds + targetDt > HardHorizonSeconds)
            {
                targetDt = HardHorizonSeconds - currentTimeSeconds;
                capped = true;
            }

            if (capped && targetDt <= OrbitIntegrator.DefaultMinStepSize)
            {
                return new EventOccurrence(currentTimeSeconds, Capture(ship), "EphemerisEnd", null, EventKind.EphemerisEnd, null);
            }

            // Длительные аккумуляторы — компенсированные: за тысячи чанков
            // наивное t += chunk дрейфует к допуску корня. Остаток тоже
            // компенсирован: от него зависит размер последнего чанка и выход.
            var timeAcc = new KahanAccumulator(currentTimeSeconds);
            var remainingAcc = new KahanAccumulator(targetDt);
            double finestGrain = MaxChunkSeconds / DetectionGrain.MaxSubdivisions;
            long maxChunks = (long)Math.Ceiling(targetDt / finestGrain) + 2;
            long chunks = 0;

            while (remainingAcc.Sum > OrbitIntegrator.DefaultMinStepSize)
            {
                if (++chunks > maxChunks)
                    throw new InvalidOperationException("Исчерпана граница числа чанков — цикл детекта не сошёлся.");

                double time = timeAcc.Sum;
                double remaining = remainingAcc.Sum;
                SpacecraftIntegrationState start = Capture(ship);
                int subdiv = DetectionSubdivisions;
                if (SystemForGrain != null)
                {
                    // A0: пол DetectionSubdivisions применяется только когда
                    // пересечение поверхности физически возможно внутри чанка.
                    // Вдали от тел (чистая баллистика) зерно не нужно — чанк
                    // идёт целиком, иначе дальний варп платит ×20 без пользы.
                    if (DetectionGrain.NeedsFineGrain(SystemForGrain, CrossingDetectors, start, time, MaxChunkSeconds))
                    {
                        double tau = DetectionGrain.CharacteristicTimeSeconds(SystemForGrain, start, time);
                        subdiv = DetectionGrain.Subdivisions(MaxChunkSeconds, DetectionSubdivisions, tau);
                    }
                    else
                    {
                        subdiv = 1;
                    }
                }

                double chunkDt = Math.Min(MaxChunkSeconds / subdiv, remaining);
                double chunkEnd = time + chunkDt;
                List<DenseSegment> seg = (UseDenseRefinement || render != null) ? new List<DenseSegment>() : null;
                SpacecraftIntegrationState end = PropagateState(start, time, chunkEnd, seg);

                TimedEvent timed = FindTimedEvent(time, chunkEnd);
                Candidate best = Candidate.None;

                for (int i = 0; i < CrossingDetectors.Count; i++)
                {
                    ICrossingDetector detector = CrossingDetectors[i];
                    double g0 = detector.Evaluate(start, time);
                    double g1 = detector.Evaluate(end, chunkEnd);
                    // Не-конечное g — не «нет пересечения» (false в IsFiring
                    // из-за упавших сравнений), а порча состояния: молчаливый
                    // пропуск события недопустим (аудит S1, NaN-каскад).
                    if (!double.IsFinite(g0) || !double.IsFinite(g1))
                    {
                        throw new InvalidOperationException(
                            "Детектор " + detector.GetType().Name + " получил не-конечное значение g (" +
                            g0.ToString("R") + " → " + g1.ToString("R") + ") на [" + time + "; " + chunkEnd +
                            "] — состояние корабля или тела испорчено.");
                    }

                    if (!IsFiring(detector.Direction, g0, g1))
                        continue;
                    double rootTime;
                    SpacecraftIntegrationState rootState;
                    if (UseDenseRefinement)
                        RefineCrossingDense(detector, seg, time, g0, chunkEnd, out rootTime, out rootState);
                    else
                        RefineCrossing(detector, start, time, g0, chunkEnd, out rootTime, out rootState);
                    // Тело-участник знает только конкретный детектор; базовый
                    // интерфейс его не обещает — забираем через известный тип.
                    OrbitingBody involved = (detector as AltitudeCrossingDetector)?.Body;
                    best = Closer(best, Candidate.ForCrossing(rootTime, rootState, detector, involved));
                }

                for (int i = 0; i < TransitionDetectors.Count; i++)
                {
                    ITransitionDetector detector = TransitionDetectors[i];
                    object key0 = detector.GetKey(start, time);
                    object key1 = detector.GetKey(end, chunkEnd);
                    if (ReferenceEquals(key0, key1))
                        continue;
                    double rootTime;
                    SpacecraftIntegrationState rootState;
                    if (UseDenseRefinement)
                        RefineTransitionDense(detector, seg, time, key1, chunkEnd, out rootTime, out rootState);
                    else
                        RefineTransition(detector, start, time, key1, chunkEnd, out rootTime, out rootState);
                    best = Closer(best, Candidate.ForTransition(rootTime, rootState, detector, key1 as OrbitingBody));
                }

                if (timed != null)
                {
                    SpacecraftIntegrationState timedState = UseDenseRefinement
                        ? DenseSegment.EvaluateTrack(seg, timed.TimeSeconds)
                        : PropagateState(start, time, timed.TimeSeconds);
                    best = Closer(best, Candidate.ForTimed(timedState, timed));
                }

                if (!best.Found)
                {
                    Commit(ship, end);
                    AppendRender(render, seg, double.PositiveInfinity);
                    timeAcc.Add(chunkDt);
                    remainingAcc.Add(-chunkDt);
                    continue;
                }

                if (best.Timed != null)
                {
                    AppendRender(render, seg, best.Time);
                    return ApplyTimedEvent(ship, best);
                }

                AppendRender(render, seg, best.Time);
                Commit(ship, best.State);
                return new EventOccurrence(best.Time, best.State, best.Name, best.Body, best.Kind, best.Source);
            }

            if (capped)
            {
                return new EventOccurrence(HardHorizonSeconds, Capture(ship), "EphemerisEnd", null, EventKind.EphemerisEnd, null);
            }

            return null;
        }

        private static void AppendRender(List<DenseSegment> render, List<DenseSegment> seg, double uptoTime)
        {
            if (render == null || seg == null)
                return;
            for (int i = 0; i < seg.Count; i++)
            {
                if (seg[i].T0 < uptoTime)
                    render.Add(seg[i]);
            }
        }

        private static bool IsFiring(EventDirection direction, double g0, double g1)
        {
            // Строгая смена знака: g == 0 на любом конце — не пересечение.
            // Именно это не даёт рестарту из корня тут же «найти» то же событие.
            if (g0 == 0d || g1 == 0d)
                return false;
            bool falling = g0 > 0d && g1 < 0d;
            bool rising = g0 < 0d && g1 > 0d;
            if (direction == EventDirection.Falling)
                return falling;
            if (direction == EventDirection.Rising)
                return rising;
            return falling || rising;
        }

        private Candidate Closer(Candidate current, Candidate contender)
        {
            if (!contender.Found)
                return current;
            if (!current.Found)
                return contender;
            // Ранний корень побеждает; совпавшие в пределах допуска бисекции —
            // неразличимы, решает приоритет (меньше число = выше).
            if (contender.Time < current.Time - RootToleranceSeconds)
                return contender;
            if (Math.Abs(contender.Time - current.Time) <= RootToleranceSeconds && contender.Priority < current.Priority)
                return contender;
            return current;
        }

        private TimedEvent FindTimedEvent(double time, double chunkEnd)
        {
            TimedEvent winner = null;
            for (int i = 0; i < TimedEvents.Count; i++)
            {
                TimedEvent te = TimedEvents[i];
                // Строго позже текущего времени: только что обработанное событие
                // (Time == now после рестарта) не должно срабатывать повторно.
                // События в прошлом пропускаются молча — они уже потреблены.
                if (te.TimeSeconds > time && te.TimeSeconds <= chunkEnd)
                {
                    if (winner == null || te.TimeSeconds < winner.TimeSeconds)
                        winner = te;
                }
            }
            return winner;
        }

        private void RefineCrossing(
            ICrossingDetector detector,
            SpacecraftIntegrationState start, double t0, double g0, double t1,
            out double rootTime, out SpacecraftIntegrationState rootState)
        {
            // Инвариант брекета: g(a) — знака g0 (до пересечения), g(b) — знака
            // g1 (после). Возвращается конец b, а НЕ середина: усечение обязано
            // оставлять корабль строго ЗА событием (в пределах допуска), иначе
            // рестарт из корня тут же переоткрывал бы тот же корень — классика
            // zero-time loop. Ошибка времени при этом всё равно не хуже допуска.
            double a = t0;
            double ga = g0;
            double b = t1;
            for (int i = 0; i < MaxRefineIterations; i++)
            {
                if (b - a <= RootToleranceSeconds)
                    break;
                double m = 0.5 * (a + b);
                SpacecraftIntegrationState sm = PropagateState(start, t0, m, null, true);
                double gm = detector.Evaluate(sm, m);
                if (gm == 0d)
                {
                    // Точное попадание: g = 0 в точке рестарта подавляется
                    // строгим правилом смены знака — переоткрытия не будет.
                    rootTime = m;
                    rootState = sm;
                    return;
                }
                if (Math.Sign(gm) == Math.Sign(ga))
                {
                    a = m;
                    ga = gm;
                }
                else
                {
                    b = m;
                }
            }
            rootTime = b;
            rootState = PropagateState(start, t0, b, null, true);
        }

        private void RefineTransition(
            ITransitionDetector detector,
            SpacecraftIntegrationState start, double t0, object newKey, double t1,
            out double rootTime, out SpacecraftIntegrationState rootState)
        {
            // Бисекция по предикату «ключ уже новый». Предполагается один
            // переход в брекете (для SOI локально верно); скользящий случай с
            // третьим ключом внутри брекета сойдётся лишь приблизительно —
            // чанки короткие относительно геометрии встречи, это приемлемо для P1a.
            // Возвращается конец b (сторона нового ключа) — тот же принцип, что
            // в RefineCrossing: рестарт обязан видеть новый ключ в бейзлайне.
            double a = t0;
            double b = t1;
            for (int i = 0; i < MaxRefineIterations; i++)
            {
                if (b - a <= RootToleranceSeconds)
                    break;
                double m = 0.5 * (a + b);
                SpacecraftIntegrationState sm = PropagateState(start, t0, m, null, true);
                if (ReferenceEquals(detector.GetKey(sm, m), newKey))
                    b = m;
                else
                    a = m;
            }
            rootTime = b;
            rootState = PropagateState(start, t0, b, null, true);
        }

        /// <summary>
        /// Та же бисекция, что RefineCrossing, но состояние в середине берётся
        /// из dense-трека чанка, а не реинтеграцией от его начала: 0 вызовов
        /// интегратора на итерацию. Инвариант тот же — возвращается конец b
        /// (пост-сторона); «строго за событием» здесь — в пределах точности
        /// интерполянта, доказательство — тест рестарта T18, не формулировка.
        /// </summary>
        private void RefineCrossingDense(
            ICrossingDetector detector,
            List<DenseSegment> seg, double t0, double g0, double t1,
            out double rootTime, out SpacecraftIntegrationState rootState)
        {
            double a = t0;
            double ga = g0;
            double b = t1;
            for (int i = 0; i < MaxRefineIterations; i++)
            {
                if (b - a <= RootToleranceSeconds)
                    break;
                double m = 0.5 * (a + b);
                SpacecraftIntegrationState sm = DenseSegment.EvaluateTrack(seg, m);
                double gm = detector.Evaluate(sm, m);
                if (gm == 0d)
                {
                    rootTime = m;
                    rootState = sm;
                    return;
                }
                if (Math.Sign(gm) == Math.Sign(ga))
                {
                    a = m;
                    ga = gm;
                }
                else
                {
                    b = m;
                }
            }
            rootTime = b;
            rootState = DenseSegment.EvaluateTrack(seg, b);
        }

        private void RefineTransitionDense(
            ITransitionDetector detector,
            List<DenseSegment> seg, double t0, object newKey, double t1,
            out double rootTime, out SpacecraftIntegrationState rootState)
        {
            double a = t0;
            double b = t1;
            for (int i = 0; i < MaxRefineIterations; i++)
            {
                if (b - a <= RootToleranceSeconds)
                    break;
                double m = 0.5 * (a + b);
                SpacecraftIntegrationState sm = DenseSegment.EvaluateTrack(seg, m);
                if (ReferenceEquals(detector.GetKey(sm, m), newKey))
                    b = m;
                else
                    a = m;
            }
            rootTime = b;
            rootState = DenseSegment.EvaluateTrack(seg, b);
        }

        private EventOccurrence ApplyTimedEvent(Ship ship, Candidate best)
        {
            TimedEvent te = best.Timed;
            double newMass = best.State.Mass + te.DeltaMass;
            if (newMass < te.MinimumMassKg)
            {
                throw new InvalidOperationException(
                    "TimedEvent @" + te.TimeSeconds.ToString("F3") + "s: масса после сброса " +
                    newMass.ToString("F3") + " кг ниже минимума " + te.MinimumMassKg.ToString("F3") + " кг.");
            }
            var applied = new SpacecraftIntegrationState
            {
                Position = best.State.Position,
                Velocity = best.State.Velocity,
                Mass = newMass
            };
            // DeltaVelocity в P1a намеренно НЕ применяется (задел под импульс
            // отделения, см. TimedEvent) — разрыв только по массе.
            Commit(ship, applied);
            return new EventOccurrence(best.Time, applied, te.Name, null, EventKind.Timed, null);
        }

        private SpacecraftIntegrationState PropagateState(SpacecraftIntegrationState from, double fromTime, double toTime)
        {
            return PropagateState(from, fromTime, toTime, null);
        }

        private SpacecraftIntegrationState PropagateState(SpacecraftIntegrationState from, double fromTime, double toTime, List<DenseSegment> segments)
        {
            return PropagateState(from, fromTime, toTime, segments, false);
        }

        /// <summary>
        /// isProbe — уточняющие прогоны (бисекция, dense-уточнение): они идут
        /// через тот же physics-экземпляр, и снимок момента вокруг них
        /// save/restore-ится, чтобы TotalTorqueBody после Propagate отражал
        /// шаг ПРИНЯТОЙ траектории корабля, а не probe-состояние (аудит
        /// pre-Unity/3.1). Основные шаги корабля (чанки, движок к timed-событию)
        /// идут без save/restore — их подстадии и есть «принятые».
        /// </summary>
        private SpacecraftIntegrationState PropagateState(
            SpacecraftIntegrationState from, double fromTime, double toTime, List<DenseSegment> segments, bool isProbe)
        {
            Vector3d savedLast = default;
            Vector3d savedTotal = default;
            if (isProbe)
            {
                physics.SuspendTorqueSnapshot(out savedLast, out savedTotal);
            }

            var probe = new Ship(from.Position, from.Velocity, from.Mass);
            physics.StepWithSegments(probe, fromTime, toTime - fromTime, segments);
            if (isProbe)
            {
                physics.RestoreTorqueSnapshot(savedLast, savedTotal);
            }

            return new SpacecraftIntegrationState
            {
                Position = probe.Position,
                Velocity = probe.Velocity,
                Mass = probe.Mass
            };
        }

        private static SpacecraftIntegrationState Capture(Ship ship)
        {
            return new SpacecraftIntegrationState
            {
                Position = ship.Position,
                Velocity = ship.Velocity,
                Mass = ship.Mass
            };
        }

        private static void Commit(Ship ship, SpacecraftIntegrationState state)
        {
            ship.Position = state.Position;
            ship.Velocity = state.Velocity;
            ship.Mass = state.Mass;
        }

        private struct Candidate
        {
            public bool Found;
            public double Time;
            public int Priority;
            public SpacecraftIntegrationState State;
            public string Name;
            public OrbitingBody Body;
            public TimedEvent Timed;
            public EventKind Kind;
            public object Source;

            public static Candidate None => new Candidate { Found = false };

            public static Candidate ForCrossing(double time, SpacecraftIntegrationState state, ICrossingDetector detector, OrbitingBody body)
            {
                return new Candidate
                {
                    Found = true,
                    Time = time,
                    Priority = detector.Priority,
                    State = state,
                    Name = detector.Name,
                    Body = body,
                    Timed = null,
                    Kind = detector.Kind,
                    Source = detector
                };
            }

            public static Candidate ForTransition(double time, SpacecraftIntegrationState state, ITransitionDetector detector, OrbitingBody body)
            {
                return new Candidate
                {
                    Found = true,
                    Time = time,
                    Priority = detector.Priority,
                    State = state,
                    Name = detector.Name,
                    Body = body,
                    Timed = null,
                    Kind = detector.Kind,
                    Source = detector
                };
            }

            public static Candidate ForTimed(SpacecraftIntegrationState state, TimedEvent timed)
            {
                return new Candidate
                {
                    Found = true,
                    Time = timed.TimeSeconds,
                    Priority = timed.Priority,
                    State = state,
                    Name = timed.Name,
                    Body = null,
                    Timed = timed,
                    Kind = EventKind.Timed,
                    Source = null
                };
            }
        }
    }
}
