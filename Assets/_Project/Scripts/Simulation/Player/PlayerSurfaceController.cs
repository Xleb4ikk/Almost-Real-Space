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
        public const double CapsuleRadius = 0.45d;
        public const double CapsuleHeight = 1.8d;
        public const double StepUpMeters = 0.5d;
        public const double SupportDropMeters = 0.5d;
        public const double MaxSlopeDegrees = 30d;
        public const int MaxSweepIterations = 4;
        public const double SlideSpeedMps = 1.5d;

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

        /// <summary>Вход в подшаге заблокирован (стена/потолок): позиция не изменилась.</summary>
        public bool Blocked;

        /// <summary>Ускорение свободного падения вдоль локальной вертикали (м/с²).</summary>
        public double Gravity = 9.81d;

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
                Vector3d slid = SweepHorizontal(support, Position, move);
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
            Vector3d p = SweepHorizontal(support, Position, move);
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
            // контроль запаса над головой (для приседания/прыжка в будущем).
            if (support.Ceiling(new Vector3d(Position.X, Position.Y, finalHeight + CapsuleHeight), StepUpMeters,
                    out double ceilingHeight)
                && ceilingHeight - finalHeight < CapsuleHeight)
            {
                Position = saved;
                Blocked = true;
                return;
            }

            Position = new Vector3d(Position.X, Position.Y, finalHeight);
            GroundSourceId = finalId;
            Velocity = intent;

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
        /// итераций). Возвращает итоговую позицию; Z не меняется.
        /// </summary>
        private static Vector3d SweepHorizontal(IPlayerSupport support, Vector3d from, Vector3d move)
        {
            Vector3d p = from;
            Vector3d remaining = move;
            for (int iteration = 0; iteration < MaxSweepIterations; iteration++)
            {
                if (remaining.Magnitude < 1e-12d)
                {
                    break;
                }

                if (!support.Sweep(p, p + remaining, CapsuleRadius, out Vector3d wallNormal, out double fraction))
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
            Velocity = new Vector3d(Velocity.X, Velocity.Y, Velocity.Z - (Gravity * dt));
            Vector3d step = Velocity * dt;

            // Горизонталь — свипом вдоль стен (как было).
            Vector3d p = SweepHorizontal(support, Position, new Vector3d(step.X, step.Y, 0d));

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
                    // Допуск +5 см: Floor поднимает начало луча над ногами (см.
                    // SiteBoxSupport.Floor), поэтому видит опору чуть выше точки ног.
                    double fall = -chunk + 0.1d;
                    if (support.Floor(p, fall, out double floorHeight, out Vector3d floorNormal, out int floorId)
                        && floorHeight <= p.Z + 0.05d
                        && SlopeAngleDegrees(floorNormal) <= MaxSlopeDegrees)
                    {
                        Position = new Vector3d(p.X, p.Y, floorHeight);
                        Velocity = new Vector3d(Velocity.X, Velocity.Y, 0d);
                        Airborne = false;
                        GroundSourceId = floorId;
                        return;
                    }
                }

                p = new Vector3d(p.X, p.Y, p.Z + chunk);
                remainingZ -= chunk;
            }

            Position = p;
        }

        public static double SlopeAngleDegrees(Vector3d normal)
        {
            Vector3d n = normal.Normalized;
            double cosine = Math.Max(-1d, Math.Min(1d, Vector3d.Dot(n, Up)));
            return Math.Acos(cosine) * (180d / Math.PI);
        }
    }
}
