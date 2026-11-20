using System;
using System.Collections.Generic;
using Galilego.Core;

namespace Galilego.Simulation
{
    /// <summary>Почему точка существует и можно ли на неё откатиться.</summary>
    public enum FlightPointKind
    {
        /// <summary>Точка 0: момент включения физики (EnterFlightAt). Откат идёт через билдер.</summary>
        Launch,

        /// <summary>Обычная точка свободного полёта: снимок восстанавливается напрямую.</summary>
        Flight,

        /// <summary>Точка снята в контактной зоне (PhysX) — откат невозможен, состояние не хранится.</summary>
        Blocked
    }

    /// <summary>
    /// Одна точка таймлайна. Индекс = номер РЕАЛЬНОЙ минуты полёта (0 — старт).
    /// <see cref="FlightSeconds"/> — модельное время от старта; при варпе оно НЕ равно
    /// <c>60·Index</c>, поэтому хранится отдельно и показывается как есть.
    /// </summary>
    public sealed class FlightCheckpoint<T> where T : class
    {
        public readonly int Index;

        /// <summary>Реальные секунды полёта на момент снятия (фактические, не 60·Index).</summary>
        public double RealSeconds;

        /// <summary>Модельные секунды от старта на момент снятия.</summary>
        public double FlightSeconds;

        public FlightPointKind Kind;

        /// <summary>Снимок мира; null — состояние не хранится (зона или освобождено лимитом).</summary>
        public T State;

        public FlightCheckpoint(int index)
        {
            Index = index;
        }
    }

    /// <summary>
    /// Таймлайн перемотки: сетка по РЕАЛЬНОМУ времени полёта (точка на минуту), модельное
    /// время точки хранится рядом и может быть немонотонным (перемотка вперёд на
    /// «устаревшую» точку). Задача — только бухгалтерия: какие точки есть, когда снимать,
    /// куда откатились. Снимками владеет вызывающий код (Unity-сторона), поэтому класс
    /// чистый и тестируется без движка.
    ///
    /// Инварианты:
    ///  • индекс точки == её позиция в списке, дыр в нумерации нет;
    ///  • точка 0 не перезаписывается никогда — это ошибка, а не «обновление»;
    ///  • откат не удаляет точки; следующая минута пишется поверх старой;
    ///  • сетка не продвигается, когда симуляция не шла (решает вызывающий: пауза, билд,
    ///    разрушенный корабль), — сюда приходит только реально прожитый realDt.
    /// </summary>
    public sealed class FlightCheckpointTimeline<T> where T : class
    {
        /// <summary>Одна точка на минуту реального полёта.</summary>
        public const double PointPeriodSeconds = 60d;

        /// <summary>
        /// Лимит ХРАНИМЫХ состояний (не точек). Точки остаются как геометрия шкалы и как
        /// «серая» точка, освобождается только снимок — самые старые по индексу, кроме
        /// точки 0. 4096 снимков ≈ 68 часов полёта.
        /// </summary>
        public const int DefaultMaxStoredStates = 4096;

        private readonly List<FlightCheckpoint<T>> points = new List<FlightCheckpoint<T>>();
        private KahanAccumulator real = new KahanAccumulator(0d);
        private int nextIndex;
        private int storedStates;

        public FlightCheckpointTimeline()
            : this(DefaultMaxStoredStates)
        {
        }

        public FlightCheckpointTimeline(int maxStoredStates)
        {
            if (maxStoredStates < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(maxStoredStates),
                    "Лимит состояний должен быть не меньше 2 (точка 0 + хотя бы одна).");
            }

            MaxStoredStates = maxStoredStates;
        }

        public int MaxStoredStates { get; }

        /// <summary>Реальные секунды полёта (Kahan; не t += dt).</summary>
        public double RealSeconds { get { return real.Sum; } }

        /// <summary>Позиция playhead на шкале в реальных минутах (дробная).</summary>
        public double PlayheadMinutes { get { return real.Sum / PointPeriodSeconds; } }

        /// <summary>Число точек в списке, включая «будущие» и освобождённые лимитом.</summary>
        public int PointCount { get { return points.Count; } }

        /// <summary>Индекс следующей минуты, которую нужно снять.</summary>
        public int NextCaptureIndex { get { return nextIndex; } }

        /// <summary>Сколько точек реально держат снимок (для отчёта/тестов).</summary>
        public int StoredStateCount { get { return storedStates; } }

        public FlightCheckpoint<T> PointAt(int index)
        {
            if (index < 0 || index >= points.Count)
            {
                return null;
            }

            return points[index];
        }

        /// <summary>Можно ли откатиться на точку: она есть, снимок жив и точка не «серая».</summary>
        public bool IsAvailable(int index)
        {
            FlightCheckpoint<T> point = PointAt(index);
            return point != null && point.State != null && point.Kind != FlightPointKind.Blocked;
        }

        /// <summary>
        /// Продвинуть реальные часы. Возвращает true и последний «должный» индекс, если
        /// пора снимать точки <c>NextCaptureIndex..lastDueIndex</c> включительно. За кадр
        /// может быть пересечено несколько минут (рывок) — снимаются все, без пропусков.
        /// </summary>
        public bool Advance(double realDt, out int lastDueIndex)
        {
            lastDueIndex = -1;
            if (!(realDt > 0d) || double.IsNaN(realDt) || double.IsInfinity(realDt))
            {
                return false;
            }

            real.Add(realDt);
            double minutes = real.Sum / PointPeriodSeconds;

            // Защита от абсурда (реальные часы больше int.MaxValue минут — 4085 лет полёта).
            if (!(minutes < int.MaxValue))
            {
                throw new InvalidOperationException(
                    "Таймлайн: реальные часы ушли за int.MaxValue минут (" + real.Sum + " с) — сетка точек не может быть пронумерована.");
            }

            int due = (int)Math.Floor(minutes);
            if (due < nextIndex)
            {
                return false;
            }

            lastDueIndex = due;
            return true;
        }

        /// <summary>
        /// Записать точку. Индекс обязан идти строго по порядку (следующий за последним
        /// снятым). Существующая точка с этим индексом ПЕРЕЗАПИСЫВАЕТСЯ (минута прожита
        /// заново), кроме точки 0 — её перезапись является ошибкой.
        /// </summary>
        public void Capture(int index, double realSeconds, double flightSeconds, FlightPointKind kind, T state)
        {
            if (index != nextIndex)
            {
                throw new InvalidOperationException(
                    "Таймлайн: точка " + index + " снимается вне очереди (ожидалась " + nextIndex + ").");
            }

            if (kind != FlightPointKind.Blocked && state == null)
            {
                throw new ArgumentNullException(nameof(state),
                    "Таймлайн: точка " + index + " объявлена доступной, но снимок null.");
            }

            if (kind == FlightPointKind.Blocked && state != null)
            {
                throw new ArgumentException(
                    "Таймлайн: у недоступной точки " + index + " не должно быть снимка.", nameof(state));
            }

            if (!(realSeconds >= 0d) || double.IsNaN(realSeconds) || double.IsInfinity(realSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(realSeconds),
                    "Таймлайн: реальное время точки должно быть конечным и неотрицательным, получено " + realSeconds + ".");
            }

            if (index == 0 && points.Count > 0)
            {
                throw new InvalidOperationException(
                    "Таймлайн: точка 0 уже снята — перезапись старта запрещена (это ошибка, а не обновление).");
            }

            FlightCheckpoint<T> point;
            if (index == points.Count)
            {
                point = new FlightCheckpoint<T>(index);
                points.Add(point);
                if (state != null)
                {
                    storedStates++;
                }
            }
            else
            {
                // Перезапись прожитой заново минуты. Учёт состояний: мог быть, мог не быть.
                point = points[index];
                if (point.State == null && state != null)
                {
                    storedStates++;
                }
                else if (point.State != null && state == null)
                {
                    storedStates--;
                }
            }

            point.RealSeconds = realSeconds;
            point.FlightSeconds = flightSeconds;
            point.Kind = kind;
            point.State = state;
            nextIndex = index + 1;

            TrimStoredStates();
        }

        /// <summary>
        /// Откат: часы возвращаются на РЕАЛЬНОЕ время точки, следующая минута — k+1.
        /// Точки не удаляются. Возвращает саму точку или null, если откат запрещён.
        /// </summary>
        public FlightCheckpoint<T> Rewind(int index)
        {
            if (!IsAvailable(index))
            {
                return null;
            }

            FlightCheckpoint<T> point = points[index];
            real.Reset(point.RealSeconds);
            nextIndex = index + 1;
            return point;
        }

        /// <summary>
        /// Новый полёт (включение физики в билдере или чит-спавн). Единственный законный
        /// способ снять новую точку 0.
        /// </summary>
        public void ResetForLaunch()
        {
            points.Clear();
            real.Reset(0d);
            nextIndex = 0;
            storedStates = 0;
        }

        /// <summary>
        /// Самые старые по индексу состояния освобождаются (кроме точки 0), сама точка
        /// остаётся серой на шкале. Вызывается после записи.
        /// </summary>
        private void TrimStoredStates()
        {
            if (storedStates <= MaxStoredStates)
            {
                return;
            }

            for (int i = 1; i < points.Count && storedStates > MaxStoredStates; i++)
            {
                FlightCheckpoint<T> point = points[i];
                if (point.State == null)
                {
                    continue;
                }

                point.State = null;
                storedStates--;

                // Причина освобождения — не зона: точка остаётся Blocked для отката,
                // но исходный Kind сохраняем в отчёте через FlightSeconds.
                if (point.Kind == FlightPointKind.Flight)
                {
                    point.Kind = FlightPointKind.Blocked;
                }
            }
        }
    }
}
