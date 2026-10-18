using System;
using Galilego.Core;

namespace Galilego.Simulation.Player
{
    /// <summary>
    /// Чистая логика ходьбы игрока по опорам (без UnityEngine): вертикальная опора,
    /// шаг вверх, наклон, потолок, углы, падение, перенос движущейся платформой.
    /// Работает в локальных метрах касательной системы места (x = восток, y = север,
    /// z = вверх). Состояние — ноги капсулы (Position), скорость (Velocity),
    /// Airborne. Шаг фиксированный, вызывается на каждом подшаге игрока.
    ///
    /// Порядок в подшаге (как в старом StepPlayerSurface): блокировка проверяется
    /// В НАЧАЛЕ подшага — до применения движения. Render/astro-рассинхрон не
    /// переносится: источник опоры обязан работать на той же позе, что и запрос.
    ///
    /// Правила (значения по умолчанию, менять только с записью в отчёт):
    ///  - шаг вверх ≤ StepUpMeters поднимает без прыжка, выше — блокировка;
    ///  - уклон ≤ MaxSlopeDegrees ходимый, круче — скольжение без подъёма;
    ///  - потолок ниже CapsuleHeight блокирует вход (физически — свипом капсулы);
    ///  - нет опоры в пределах SupportDropMeters — свободное падение;
    ///  - угол: скольжение вдоль стены, до MaxSweepIterations за подшаг.
    /// </summary>
    public sealed class PlayerSurfaceController
    {
        /// <summary>Радиус капсулы игрока (м): плечи ~0.5 м (в тесной кабине Orion шире не пролезть).</summary>
        public const double CapsuleRadius = 0.25d;

        /// <summary>Высота капсулы (м): рост игрока 1.40 м + запас на макушку.</summary>
        public const double CapsuleHeight = 1.45d;

        /// <summary>
        /// Высота капсулы в приседе (м). Проём люка Orion (~1.2 м визуально,
        /// ~1.0 м по физике) ниже полного роста: пригнувшись игрок проходит,
        /// стоя — нет. Пригибание автоматическое и работает и на земле
        /// (StepGrounded), и в полёте (StepAirborne), кнопок нет. Ниже 0.7 м
        /// опускать нельзя — это диаметр капсулы.
        /// </summary>
        public const double CapsuleHeightCrouched = 0.8d;

        /// <summary>Ступени пригибания сверху вниз: от полного роста до минимума.</summary>
        private static readonly double[] StoopLevels =
            { CapsuleHeight, 1.25d, 1.05d, 0.92d, CapsuleHeightCrouched };

        /// <summary>После пригибания не вставать столько секунд (чтобы высота не дрожала у стены).</summary>
        private const double StoopHoldSeconds = 0.25d;

        /// <summary>Запас (м) вокруг капсулы при вставании: встаём, только если вокруг есть свободный зазор, иначе высота дрожит у стены.</summary>
        private const double GrowClearanceMeters = 0.04d;

        public const double StepUpMeters = 0.5d;
        public const double SupportDropMeters = 0.5d;
        public const double MaxSlopeDegrees = 30d;

        /// <summary>
        /// Максимальный уклон, на который разрешена посадка из полёта/падения
        /// (град). Согласован с порогом игнора свипа (|n·up| &gt; 0.7 ≈ 45.6°):
        /// склоны между MaxSlopeDegrees и этим порогом свип не считает стеной,
        /// и раньше игрок проваливался сквозь них (скос порога люка ~31–33°).
        /// Круче MaxSlopeDegrees на земле уже обрабатывает скольжение.
        /// </summary>
        public const double LandingMaxSlopeDegrees = 46d;

        public const int MaxSweepIterations = 4;
        public const double SlideSpeedMps = 1.5d;

        /// <summary>
        /// Скорость плавного опускания, когда в висении проход срезан почти
        /// полностью (притолока люка), а чуть ниже путь свободнее (м/с).
        /// </summary>
        public const double HoverSinkSpeed = 1d;

        /// <summary>
        /// Численный допуск швов между коллайдерами (м): ступень чуть выше StepUp
        /// на стыке двух мешей (погрешность запечки/масштаба) не должна блокировать
        /// шаг. Это НЕ игровой порог (StepUpMeters), а эпсилон геометрии.
        /// </summary>
        public const double SeamToleranceMeters = 0.02d;

        /// <summary>Ноги капсулы в локальном кадре места.</summary>
        public Vector3d Position;

        /// <summary>Скорость относительно места (м/с).</summary>
        public Vector3d Velocity;

        /// <summary>Свободное падение (нет опоры под ногами).</summary>
        public bool Airborne;

        /// <summary>
        /// Висение в воздухе: гравитации нет, скорость едет к HoverVelocity
        /// (ноль — замирание). Ставит SimulationRunner при hover-режиме.
        /// </summary>
        public bool Hovering;

        /// <summary>Желаемая скорость висения в локальном кадре (м/с).</summary>
        public Vector3d HoverVelocity;

        /// <summary>Отклик скорости в висении (1/с).</summary>
        public double HoverResponse = 6d;

        /// <summary>Вход в подшаге заблокирован (стена/потолок): позиция не изменилась.</summary>
        public bool Blocked;

        /// <summary>Диагностика: на сколько подняли ноги за последний подшаг (м).</summary>
        public double DebugLift;

        /// <summary>Ускорение свободного падения вдоль локальной вертикали (м/с²).</summary>
        public double Gravity = 9.81d;

        /// <summary>Пригнулся ли игрок (высота капсулы ниже полного роста).</summary>
        public bool Crouched
        {
            get { return height < CapsuleHeight - 1e-6d; }
        }

        /// <summary>
        /// Текущая высота капсулы (м), от CapsuleHeightCrouched до CapsuleHeight.
        /// Игрок пригибается ступенчато (StoopLevels) ровно настолько, чтобы идти
        /// под скошенной стеной/сводом/притолокой, и встаёт, как только над
        /// головой есть место. Камера следит за этой высотой (FirstPersonCamera).
        /// </summary>
        public double CurrentHeightMeters
        {
            get { return height; }
        }

        private double height = CapsuleHeight;

        /// <summary>Сколько ещё секунд нельзя вставать после пригибания (гистерезис).</summary>
        private double growHold;

        private double CurrentHeight
        {
            get { return height; }
        }

        /// <summary>Опора под ногами (sourceId последнего Floor); -1 — нет.</summary>
        public int GroundSourceId { get; private set; } = -1;

        /// <summary>Задать текущую опору (после телепорта/спавна на платформе).</summary>
        public void SetGroundSource(int sourceId)
        {
            GroundSourceId = sourceId;
        }

        private static readonly Vector3d Up = new Vector3d(0d, 0d, 1d);

        public void Step(IPlayerSupport support, double dt, Vector3d intentVelocity, Vector3d extraAcceleration = default)
        {
            if (support == null)
            {
                throw new ArgumentNullException(nameof(support));
            }

            if (!(dt > 0d) || !double.IsFinite(dt))
            {
                throw new ArgumentOutOfRangeException(nameof(dt), "Шаг игрока обязан быть конечным и положительным.");
            }

            Blocked = false;
            if (growHold > 0d)
            {
                growHold = Math.Max(0d, growHold - dt);
            }

            if (extraAcceleration.SqrMagnitude > 0d)
            {
                // Дополнительное ускорение (джетпак): добавляется к скорости до шага.
                Velocity += extraAcceleration * dt;
            }

            Vector3d intent = new Vector3d(intentVelocity.X, intentVelocity.Y, 0d);

            if (Airborne)
            {
                StepAirborne(support, dt);
            }
            else
            {
                StepGrounded(support, dt, intent);
            }
        }

        private void StepGrounded(IPlayerSupport support, double dt, Vector3d intent)
        {
            Vector3d saved = Position;
            Vector3d move = intent * dt;

            // Перенос движущейся опорой (платформа, деталь корабля).
            if (GroundSourceId >= 0 && support is IMovingSupport moving
                && moving.TryGetSupportVelocity(GroundSourceId, out Vector3d supportVelocity))
            {
                move += new Vector3d(supportVelocity.X, supportVelocity.Y, 0d) * dt;
            }

            // Шаг вверх: опора под кандидатом и на радиусе вперёд (капсула уже
            // касается уступа, когда его кромка ещё впереди точки ног) в пределах
            // StepUp поднимает ноги, чтобы низкий уступ не считался стеной при свипе.
            Vector3d candidate = Position + move;
            if (!TryFindStepFloor(support, candidate, move, out double floorHeight, out Vector3d floorNormal, out int floorId))
            {
                // Опоры нет — сход с края: падение в этом же подшаге.
                Airborne = true;
                GroundSourceId = -1;
                Velocity = intent;
                return;
            }

            if (SlopeAngleDegrees(floorNormal) > MaxSlopeDegrees)
            {
                // Круче предела: не поднимаемся, скользим вниз вдоль поверхности.
                // Горизонталь режется свипом о стены (как при ходьбе): у крутого
                // ската стена не должна пропускать игрока.
                Vector3d slid = SweepHorizontal(support, Position, move, CurrentHeight);
                Position = new Vector3d(slid.X, slid.Y, Position.Z);

                // Опора под итоговой точкой (свип мог сдвинуть вдоль стены).
                Vector3d slideProbe = new Vector3d(Position.X, Position.Y, Position.Z + StepUpMeters);
                if (support.Floor(slideProbe, StepUpMeters + SupportDropMeters,
                        out double slideHeight, out _, out int slideId))
                {
                    Position = new Vector3d(Position.X, Position.Y, Math.Min(Position.Z, slideHeight));
                    GroundSourceId = slideId;
                }
                else
                {
                    GroundSourceId = floorId;
                }

                Velocity = new Vector3d(intent.X, intent.Y, -SlideSpeedMps);
                return;
            }

            Position = new Vector3d(Position.X, Position.Y, floorHeight);

            // Горизонтальный свип со скольжением вдоль стен (до 4 итераций).
            // Высота капсулы подбирается по ступеням: если пригибание даёт
            // заметно больший ход (скошенная стена кабины, люк), пригибаемся
            // ровно настолько, чтобы получить этот ход.
            Vector3d p = SweepStoop(support, Position, move);

            Position = new Vector3d(p.X, p.Y, Position.Z);

            // Точная опора под итоговой позицией (и повторная проверка уклона).
            Vector3d finalProbe = new Vector3d(Position.X, Position.Y, Position.Z + StepUpMeters);
            if (!support.Floor(finalProbe, StepUpMeters + SupportDropMeters,
                    out double finalHeight, out Vector3d finalNormal, out int finalId)
                || finalHeight > Position.Z + StepUpMeters + SeamToleranceMeters
                || SlopeAngleDegrees(finalNormal) > MaxSlopeDegrees)
            {
                Airborne = true;
                GroundSourceId = -1;
                Velocity = intent;
                return;
            }

            // Потолок: вход под низкий свод физически блокируется свипом; здесь —
            // контроль запаса над головой (высота — текущая: присед учитывается).
            if (support.Ceiling(new Vector3d(Position.X, Position.Y, finalHeight + CurrentHeight), StepUpMeters,
                    out double ceilingHeight)
                && ceilingHeight - finalHeight < CurrentHeight)
            {
                Position = saved;
                Blocked = true;
                return;
            }

            DebugLift = finalHeight - saved.Z;
            Position = new Vector3d(Position.X, Position.Y, finalHeight);
            GroundSourceId = finalId;
            Velocity = intent;

            // Встать, когда над головой снова есть полный рост (вышли из проёма).
            TryGrow(support);

            // Полностью зажатый вход (все итерации свипа съедены) — «вход блокирован».
            if (move.Magnitude > 1e-9d && (Position - saved).Magnitude < 1e-3d * move.Magnitude)
            {
                Position = saved;
                Blocked = true;
            }
        }

        /// <summary>
        /// Самая высокая опора в пределах шага вверх: под кандидатом и на радиусе
        /// капсулы вперёд по движению. Уступ выше StepUp не поднимает (свип заблокирует).
        /// </summary>
        private static bool TryFindStepFloor(IPlayerSupport support, Vector3d candidate, Vector3d move,
            out double height, out Vector3d normal, out int sourceId)
        {
            double limit = double.NegativeInfinity;
            height = 0d;
            normal = Up;
            sourceId = -1;
            bool found = false;
            double maxDrop = StepUpMeters + SupportDropMeters;

            Vector3d probeFrom = new Vector3d(candidate.X, candidate.Y, candidate.Z + StepUpMeters);
            if (support.Floor(probeFrom, maxDrop, out double h1, out Vector3d n1, out int id1)
                && h1 <= candidate.Z + StepUpMeters + SeamToleranceMeters)
            {
                height = h1;
                normal = n1;
                sourceId = id1;
                found = true;
            }

            double moveLength = move.Magnitude;
            if (moveLength > 1e-12d)
            {
                // Несколько сэмплов по радиусу вперёд: одиночный зонд на полный радиус
                // перелетает узкие ступени (ширина меньше радиуса) и не видит уступ.
                const int samples = 4;
                Vector3d direction = move * (1d / moveLength);
                for (int k = 1; k <= samples; k++)
                {
                    Vector3d ahead = candidate + (direction * (CapsuleRadius * k / samples));
                    Vector3d aheadProbe = new Vector3d(ahead.X, ahead.Y, candidate.Z + StepUpMeters);
                    if (support.Floor(aheadProbe, maxDrop, out double h2, out Vector3d n2, out int id2)
                        && h2 <= candidate.Z + StepUpMeters + SeamToleranceMeters
                        && (!found || h2 > height))
                    {
                        height = h2;
                        normal = n2;
                        sourceId = id2;
                        found = true;
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Горизонтальный свип капсулы со скольжением вдоль стен (до MaxSweepIterations
        /// итераций). height — высота капсулы (полный рост/присед). Возвращает
        /// итоговую позицию; Z не меняется.
        /// </summary>
        private static Vector3d SweepHorizontal(IPlayerSupport support, Vector3d from, Vector3d move, double height)
        {
            Vector3d p = from;
            Vector3d remaining = move;
            for (int iteration = 0; iteration < MaxSweepIterations; iteration++)
            {
                if (remaining.Magnitude < 1e-12d)
                {
                    break;
                }

                if (!support.Sweep(p, p + remaining, CapsuleRadius, height, out Vector3d wallNormal, out double fraction))
                {
                    p += remaining;
                    break;
                }

                if (!(fraction > 0d) || !(fraction < 1d))
                {
                    fraction = fraction < 0d ? 0d : (fraction > 1d ? 1d : fraction);
                }

                p += remaining * fraction;
                Vector3d left = remaining * (1d - fraction);
                // Скользим по ПЛОСКОЙ стене: у наклонной поверхности нормаль имеет
                // Z-составляющую, и её проекция «съедала» бы часть хода, а
                // оставшееся движение уводила бы по вертикали (Z «плыл» между
                // итерациями). Нормаль свипа — только по горизонтали.
                Vector3d wallFlat = new Vector3d(wallNormal.X, wallNormal.Y, 0d);
                if (wallFlat.SqrMagnitude > 1e-12d)
                {
                    wallFlat = wallFlat.Normalized;
                    left -= wallFlat * Vector3d.Dot(left, wallFlat);
                }

                remaining = new Vector3d(left.X, left.Y, 0d);
            }

            return p;
        }

        private void StepAirborne(IPlayerSupport support, double dt)
        {
            if (Hovering)
            {
                // Висение: гравитации нет, скорость едет к желаемой (ввод);
                // без ввода — к нулю: игрок замирает в воздухе.
                double blend = Math.Min(1d, dt * HoverResponse);
                Velocity += (HoverVelocity - Velocity) * blend;
            }
            else
            {
                Velocity = new Vector3d(Velocity.X, Velocity.Y, Velocity.Z - (Gravity * dt));
            }

            Vector3d step = Velocity * dt;

            // Горизонталь — свипом вдоль стен; в полёте пригибание тоже работает:
            // в полный рост свип режется (люк капсулы), пробуем присед и, если он
            // продвигает дальше, пригибаемся (раньше присед был только на земле —
            // висящий у люка игрок упирался головой и застревал).
            Vector3d flatStep = new Vector3d(step.X, step.Y, 0d);
            Vector3d p = SweepStoop(support, Position, flatStep);

            // Помощь при зависании: ход срезан почти полностью (голова в притолоке
            // люка). Если чуть ниже свип продвигает заметно дальше — плавно
            // опускаемся, пока не откроется проход. Обычную стену так не «топит»:
            // срабатывает только когда пониже ход реально свободнее.
            if (Hovering && flatStep.Magnitude > 1e-9d
                && (p - Position).Magnitude < 0.5d * flatStep.Magnitude)
            {
                double progress = (p - Position).Magnitude;
                double lowest = support.Floor(new Vector3d(p.X, p.Y, p.Z + StepUpMeters), 3d,
                    out double underFloor, out _, out _) ? underFloor : double.NegativeInfinity;
                for (int k = 1; k <= 2; k++)
                {
                    double targetZ = Math.Max(lowest, p.Z - (0.15d * k));
                    if (targetZ >= p.Z - 1e-6d)
                    {
                        continue;
                    }

                    Vector3d lowered = SweepHorizontal(support,
                        new Vector3d(Position.X, Position.Y, targetZ), flatStep, CapsuleHeightCrouched);
                    double loweredProgress =
                        new Vector3d(lowered.X - Position.X, lowered.Y - Position.Y, 0d).Magnitude;
                    if (loweredProgress > progress + 0.03d)
                    {
                        step = new Vector3d(step.X, step.Y, -Math.Min(HoverSinkSpeed * dt, p.Z - targetZ));
                        break;
                    }
                }
            }

            // Вертикаль дробим чанками: подшаг может быть до 0.5 с (метры пролёта),
            // и тонкая плита на пути не должна проскакиваться целиком. Посадка
            // проверяется ДО сдвига чанка, поэтому плита внутри чанка видна.
            const double maxVerticalChunk = 0.2d;
            double remainingZ = step.Z;
            while (Math.Abs(remainingZ) > 1e-12d)
            {
                double chunk = Math.Max(-maxVerticalChunk, Math.Min(maxVerticalChunk, remainingZ));
                if (chunk < 0d)
                {
                    // Посадка только на спуске: подъём (прыжок) не должен мгновенно
                    // «приземляться» обратно на ту же опору. Отскока нет (v_z = 0).
                    // Луч начинается на StepUp выше ног, и допуск тот же StepUp:
                    // свип игнорирует склоны крутизной до ~45° (см. SiteBoxSupport.
                    // Sweep), поэтому падение с горизонтальным ходом может внести
                    // ноги под скос плиты/уступ. Тогда тело не зарывается в меш, а
                    // «шагает» на найденную поверхность — зеркало TryFindStepFloor
                    // у земли (запись в отчёт «дорога: скос и посадка», 2026-10-09).
                    double fall = -chunk + 0.1d;
                    Vector3d landFrom = new Vector3d(p.X, p.Y, p.Z + StepUpMeters);
                    if (support.Floor(landFrom, StepUpMeters + fall, out double floorHeight, out Vector3d floorNormal, out int floorId)
                        && floorHeight <= p.Z + StepUpMeters
                        && SlopeAngleDegrees(floorNormal) <= LandingMaxSlopeDegrees)
                    {
                        Position = new Vector3d(p.X, p.Y, floorHeight);
                        Velocity = new Vector3d(Velocity.X, Velocity.Y, 0d);
                        Airborne = false;
                        GroundSourceId = floorId;
                        TryGrow(support);
                        return;
                    }
                }

                if (chunk > 0d && TryCeilingStop(support, p, chunk, out double allowedRise))
                {
                    p = new Vector3d(p.X, p.Y, p.Z + allowedRise);
                    Velocity = new Vector3d(Velocity.X, Velocity.Y, 0d);
                    break;
                }

                p = new Vector3d(p.X, p.Y, p.Z + chunk);
                remainingZ -= chunk;
            }

            Position = p;
            TryGrow(support);
        }

        /// <summary>
        /// Свип с подбором высоты капсулы. Если ход при текущей высоте урезан, а
        /// самая низкая ступень проходит заметно дальше — значит мешает голова
        /// (скос стены, притолока): берём САМУЮ ВЫСОКУЮ ступень, дающую почти
        /// тот же ход, что и минимум. Если и минимум не лучше — это настоящая
        /// стена, высоту не трогаем. Порог выигрыша относительный (доля шага):
        /// прежний абсолютный порог 2 см при шаге кадра ~1.7 см (60 fps)
        /// никогда не срабатывал, и игрок не пригибался вовсе.
        /// </summary>
        private Vector3d SweepStoop(IPlayerSupport support, Vector3d from, Vector3d move)
        {
            Vector3d p = SweepHorizontal(support, from, move, height);
            double want = move.Magnitude;
            if (!(want > 1e-9d) || height <= CapsuleHeightCrouched + 1e-9d)
            {
                return p;
            }

            double got = (p - from).Magnitude;
            if (got >= want - 1e-4d)
            {
                return p;
            }

            Vector3d pMin = SweepHorizontal(support, from, move, CapsuleHeightCrouched);
            double best = (pMin - from).Magnitude;
            if (best <= got + Math.Max(1e-3d, 0.1d * want))
            {
                return p;
            }

            double tolerance = Math.Max(1e-3d, 0.05d * want);
            for (int i = 0; i < StoopLevels.Length - 1; i++)
            {
                double level = StoopLevels[i];
                if (level >= height - 1e-9d)
                {
                    continue;
                }

                Vector3d pl = SweepHorizontal(support, from, move, level);
                if ((pl - from).Magnitude >= best - tolerance)
                {
                    height = level;
                    growHold = StoopHoldSeconds;
                    return pl;
                }
            }

            height = CapsuleHeightCrouched;
            growHold = StoopHoldSeconds;
            return pMin;
        }

        /// <summary>
        /// Встать повыше: самая высокая ступень (выше текущей), в которую капсула
        /// ВЛЕЗАЕТ на этом месте. Проверка — по объёму капсулы (IOverlapSupport),
        /// а не по лучам вверх: скошенная стена и притолока бокового луча не
        /// видны, и игрок вставал прямо в стену. Работает на земле и в воздухе.
        /// </summary>
        private void TryGrow(IPlayerSupport support)
        {
            if (height >= CapsuleHeight - 1e-9d || growHold > 0d)
            {
                return;
            }

            for (int i = 0; i < StoopLevels.Length; i++)
            {
                double level = StoopLevels[i];
                if (level <= height + 1e-9d)
                {
                    break;
                }

                if (Fits(support, level))
                {
                    height = level;
                    return;
                }
            }
        }

        private bool Fits(IPlayerSupport support, double level)
        {
            IOverlapSupport overlap = support as IOverlapSupport;
            if (overlap != null)
            {
                return !overlap.Overlaps(Position, CapsuleRadius + GrowClearanceMeters, level);
            }

            // Опора без объёмной проверки (рельеф): как раньше — потолок над головой.
            return !support.Ceiling(
                new Vector3d(Position.X, Position.Y, Position.Z + height),
                level - height + 0.05d, out _);
        }

        /// <summary>
        /// Проверка потолка при подъёме в воздухе (прыжок, джетпак): без неё
        /// игрок вылетал сквозь крышу кабины и носовое стекло. Возвращает true,
        /// если подъём урезан (rise — желаемый, allowed — до потолка).
        /// </summary>
        private bool TryCeilingStop(IPlayerSupport support, Vector3d p, double rise, out double allowed)
        {
            double headZ = p.Z + CurrentHeight;
            allowed = rise;
            if (!support.Ceiling(new Vector3d(p.X, p.Y, headZ), rise + 0.02d, out double ceilingZ)) return false;
            double room = ceilingZ - headZ - 0.01d;
            if (room >= rise) return false;
            allowed = Math.Max(0d, room);
            return true;
        }

        public static double SlopeAngleDegrees(Vector3d normal)
        {
            Vector3d n = normal.Normalized;
            double cosine = Math.Max(-1d, Math.Min(1d, Vector3d.Dot(n, Up)));
            return Math.Acos(cosine) * (180d / Math.PI);
        }
    }
}

