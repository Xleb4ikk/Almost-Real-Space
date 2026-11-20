using System;
using System.Collections.Generic;
using System.Numerics;
using Galilego.Simulation;
using GalilegoPhysicsTests.Support;
using Xunit;

namespace GalilegoPhysicsTests.Timeline
{
    /// <summary>
    /// Модульные тесты бухгалтерии перемотки: сетка реальных минут, перезапись после
    /// отката, «серые» точки, защита точки 0, Kahan-часы (R5).
    /// Снимки здесь — просто меченые объекты: логика таймлайна от их содержимого
    /// не зависит, а полноту снимка проверяют runtime-тесты (R4/R7).
    /// </summary>
    [Trait("Tier", "Fast")]
    public sealed class FlightCheckpointTimelineTests
    {
        private sealed class State
        {
            public int Id;

            public State(int id)
            {
                Id = id;
            }
        }

        private static void Capture(FlightCheckpointTimeline<State> timeline, int id)
        {
            timeline.Capture(
                timeline.NextCaptureIndex,
                timeline.RealSeconds,
                1000d + id,
                FlightPointKind.Flight,
                new State(id));
        }

        /// <summary>Снимок с уникальным номером: перезапись точки должна быть видна.</summary>
        private static int nextStateId = 1000;

        private static void CaptureFresh(FlightCheckpointTimeline<State> timeline)
        {
            int id = nextStateId++;
            timeline.Capture(
                timeline.NextCaptureIndex,
                timeline.RealSeconds,
                1000d + id,
                FlightPointKind.Flight,
                new State(id));
        }

        /// <summary>Продвинуть часы до указанной суммы и снять все должные минуты.</summary>
        private static void AdvanceAndCapture(FlightCheckpointTimeline<State> timeline, double realDt)
        {
            int due;
            if (timeline.Advance(realDt, out due))
            {
                while (timeline.NextCaptureIndex <= due)
                {
                    Capture(timeline, timeline.NextCaptureIndex);
                }
            }
        }

        // ── точная арифметика для R5: любой double ≥ 2^-80 кратен 2^-80 ──
        private const int ExactShiftBits = 80;
        private static readonly BigInteger QuantumDenominator = BigInteger.Pow(2, ExactShiftBits);

        private static BigInteger ToExactUnits(double value)
        {
            Assert.True(value >= 0d && !double.IsInfinity(value) && !double.IsNaN(value), "ToExactUnits: значение " + value);
            long bits = BitConverter.DoubleToInt64Bits(value);
            int exponent = (int)((bits >> 52) & 0x7FF);
            long mantissa = bits & 0xFFFFFFFFFFFFFL;

            if (exponent == 0)
            {
                // Субнормал: 0.mantissa × 2^-1022.
                return BigInteger.Multiply(mantissa, BigInteger.Pow(2, ExactShiftBits - 1074));
            }

            mantissa |= 1L << 52;
            int shift = exponent - 1023 - 52 + ExactShiftBits;   // value = mantissa × 2^shift
            Assert.True(shift >= 0, "ToExactUnits: значение " + value + " меньше кванта 2^-" + ExactShiftBits);
            return BigInteger.Multiply(mantissa, BigInteger.Pow(2, shift));
        }

        private static double Ulp(double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(Math.Abs(value));
            return BitConverter.Int64BitsToDouble(bits + 1) - Math.Abs(value);
        }

        [Fact]
        public void LaunchPointThenOnePointPerRealMinute()
        {
            var timeline = new FlightCheckpointTimeline<State>();
            timeline.ResetForLaunch();
            timeline.Capture(0, 0d, 0d, FlightPointKind.Launch, new State(0));

            for (int frame = 0; frame < 150 * 60; frame++)
            {
                AdvanceAndCapture(timeline, 1d / 60d);
            }

            // 150 секунд → минуты 0,1,2 (точка 2 снята при пересечении 120 с).
            Asserts.True(timeline.PointCount == 3, "за 150 с должно быть 3 точки, получено " + timeline.PointCount);
            Asserts.True(timeline.NextCaptureIndex == 3, "nextIndex должен стать 3, получено " + timeline.NextCaptureIndex);
            Asserts.True(timeline.PointAt(0).Kind == FlightPointKind.Launch, "точка 0 должна остаться Launch");
            Asserts.Near(timeline.PlayheadMinutes, 2.5d, 1e-12d, "playhead");
        }

        [Fact]
        public void HundredThousandJitteryFramesKeepExactIndicesAndClock()
        {
            var timeline = new FlightCheckpointTimeline<State>(100000);
            timeline.ResetForLaunch();
            timeline.Capture(0, 0d, 0d, FlightPointKind.Launch, new State(0));

            var random = new Random(20261120);
            BigInteger exactUnits = BigInteger.Zero;
            double naive = 0d;
            var captured = new List<int>();

            for (int frame = 0; frame < 100000; frame++)
            {
                // Рваный кадр: 3…40 мс, плюс редкие «рыбки» до 400 мс.
                double dt = frame % 5000 == 0 ? 0.4d : 0.003d + (random.NextDouble() * 0.037d);
                exactUnits += ToExactUnits(dt);
                naive += dt;

                int due;
                if (timeline.Advance(dt, out due))
                {
                    while (timeline.NextCaptureIndex <= due)
                    {
                        int index = timeline.NextCaptureIndex;
                        captured.Add(index);
                        Capture(timeline, index);
                    }
                }
            }

            double kahan = timeline.RealSeconds;
            double exact = (double)exactUnits / Math.Pow(2d, ExactShiftBits);
            double kahanError = Math.Abs(kahan - exact);
            double naiveError = Math.Abs(naive - exact);

            Asserts.True(kahanError <= 8d * Ulp(kahan),
                "Kahan-часы: |err|=" + kahanError.ToString("R") + " с при ulp=" + Ulp(kahan).ToString("R") +
                " (сумма " + kahan.ToString("R") + " с)");
            Asserts.True(naiveError > kahanError,
                "контроль: наивная сумма обязана быть хуже Kahan (naive " + naiveError.ToString("R") +
                " vs kahan " + kahanError.ToString("R") + ")");

            // Индексы: строго подряд, без пропусков и дублей, до floor(clock/60).
            long expectedLast = (long)Math.Floor(kahan / FlightCheckpointTimeline<State>.PointPeriodSeconds);
            Asserts.True(captured.Count == expectedLast,
                "снято минут " + captured.Count + " (без старта), ожидалось " + expectedLast);
            for (int i = 0; i < captured.Count; i++)
            {
                Asserts.True(captured[i] == i + 1, "индекс " + (i + 1) + " нарушен: " + captured[i]);
            }

            Asserts.True(timeline.NextCaptureIndex == expectedLast + 1,
                "nextIndex " + timeline.NextCaptureIndex + " != " + (expectedLast + 1));

            // Точная граница минут считалась целочисленно: расхождение с floor(clock/60)
            // означало бы, что часы стоят на границе минуты — отдельная проверка.
            BigInteger exactMinutes = BigInteger.Divide(exactUnits, BigInteger.Multiply(60, QuantumDenominator));
            Asserts.True(exactMinutes == expectedLast,
                "точное число прожитых минут " + exactMinutes + " не равно индексу " + expectedLast);
        }

        [Fact]
        public void RewindKeepsFuturePointsAndNextMinutesOverwriteThem()
        {
            // Сценарий заказчика: 14 минут полёта, откат на 6, далее минуты пишутся
            // поверх старых, а будущие точки остаются выбираемыми.
            var timeline = new FlightCheckpointTimeline<State>();
            timeline.ResetForLaunch();
            timeline.Capture(0, 0d, 0d, FlightPointKind.Launch, new State(-1));

            for (int minute = 1; minute <= 14; minute++)
            {
                AdvanceAndCapture(timeline, 60d);
            }

            Asserts.True(timeline.PointCount == 15, "после 14 минут должно быть 15 точек, получено " + timeline.PointCount);
            int oldFuture7 = timeline.PointAt(7).State.Id;
            int oldFuture14 = timeline.PointAt(14).State.Id;

            FlightCheckpoint<State> rewound = timeline.Rewind(6);
            Asserts.True(rewound != null, "откат на 6 должен быть разрешён");
            Asserts.Near(timeline.RealSeconds, 360d, 0d, "реальные часы встают на время точки");
            Asserts.True(timeline.NextCaptureIndex == 7, "nextIndex = 7, получено " + timeline.NextCaptureIndex);
            Asserts.True(timeline.PointCount == 15, "точки не удаляются при откате");

            // Минута 7 перезаписывается, 8…14 — ещё старые.
            int due;
            timeline.Advance(60d, out due);
            CaptureFresh(timeline);
            Asserts.True(timeline.PointAt(7).State.Id != oldFuture7, "точка 7 должна быть перезаписана");
            Asserts.True(timeline.PointAt(14).State.Id == oldFuture14, "точка 14 пока не тронута");
            Asserts.True(timeline.PointCount == 15, "перезапись не должна добавлять точки");

            // Будущую (ещё не перезаписанную) точку 14 выбирать можно.
            FlightCheckpoint<State> forward = timeline.Rewind(14);
            Asserts.True(forward != null, "перемотка вперёд на «устаревшую» точку разрешена");
            Asserts.True(forward.State.Id == oldFuture14, "перемотка вперёд возвращает старый снимок");
            Asserts.Near(timeline.RealSeconds, 840d, 0d, "часы прыгают вперёд на время точки");
            Asserts.True(timeline.NextCaptureIndex == 15, "после прыжка вперёд пишется минута 15");
        }

        [Fact]
        public void BlockedPointCannotBeRewoundTo()
        {
            var timeline = new FlightCheckpointTimeline<State>();
            timeline.ResetForLaunch();
            timeline.Capture(0, 0d, 0d, FlightPointKind.Launch, new State(0));

            AdvanceAndCapture(timeline, 60d);         // точка 1 — обычная, часы 60 с

            int due;
            timeline.Advance(60d, out due);           // часы 120 с, должная минута 2
            timeline.Capture(2, timeline.RealSeconds, 120d, FlightPointKind.Blocked, null);

            Asserts.False(timeline.IsAvailable(2), "точка в зоне недоступна");
            Asserts.True(timeline.IsAvailable(1), "обычная точка доступна");
            Asserts.True(timeline.Rewind(2) == null, "откат на серую точку возвращает null");
            Asserts.Near(timeline.RealSeconds, 120d, 0d, "неудачный откат не двигает часы");
            Asserts.True(timeline.NextCaptureIndex == 3, "неудачный откат не меняет очередь");
            Asserts.True(timeline.PointCount == 3, "серая точка остаётся на шкале");
        }

        [Fact]
        public void LaunchPointCannotBeOverwritten()
        {
            var timeline = new FlightCheckpointTimeline<State>();
            timeline.ResetForLaunch();
            timeline.Capture(0, 0d, 0d, FlightPointKind.Launch, new State(0));

            AdvanceAndCapture(timeline, 120d);
            timeline.Rewind(1);

            Assert.Throws<InvalidOperationException>(() =>
                timeline.Capture(0, 0d, 0d, FlightPointKind.Launch, new State(99)));
        }

        [Fact]
        public void CapturingOutOfOrderThrows()
        {
            var timeline = new FlightCheckpointTimeline<State>();
            timeline.ResetForLaunch();
            timeline.Capture(0, 0d, 0d, FlightPointKind.Launch, new State(0));

            Assert.Throws<InvalidOperationException>(() =>
                timeline.Capture(2, 120d, 120d, FlightPointKind.Flight, new State(2)));
        }

        [Fact]
        public void SeveralMinutesInOneFrameAreAllCapturedWithoutGaps()
        {
            var timeline = new FlightCheckpointTimeline<State>();
            timeline.ResetForLaunch();
            timeline.Capture(0, 0d, 0d, FlightPointKind.Launch, new State(0));

            // Рывок 130 с одним кадром: должны появиться минуты 1 и 2, обе с состоянием
            // этого кадра, и ни одной пропущенной.
            AdvanceAndCapture(timeline, 130d);
            Asserts.True(timeline.PointCount == 3, "после рывка 130 с должно быть 3 точки, получено " + timeline.PointCount);
            Asserts.True(timeline.IsAvailable(1) && timeline.IsAvailable(2), "обе точки доступны");
            Asserts.Near(timeline.RealSeconds, 130d, 0d, "часы после рывка");
        }

        [Fact]
        public void StateLimitReleasesOldestStatesButKeepsLaunchAndGeometry()
        {
            var timeline = new FlightCheckpointTimeline<State>(5);
            timeline.ResetForLaunch();
            timeline.Capture(0, 0d, 0d, FlightPointKind.Launch, new State(0));

            for (int minute = 1; minute <= 12; minute++)
            {
                AdvanceAndCapture(timeline, 60d);
            }

            Asserts.True(timeline.PointCount == 13, "геометрия шкалы сохраняется: 13 точек, получено " + timeline.PointCount);
            Asserts.True(timeline.StoredStateCount == 5,
                "хранится ровно лимит состояний, получено " + timeline.StoredStateCount);
            Asserts.True(timeline.IsAvailable(0), "точка 0 не освобождается никогда");
            Asserts.False(timeline.IsAvailable(1), "самое старое состояние освобождено");
            Asserts.False(timeline.IsAvailable(8), "освобождены все, кроме последних четырёх");
            Asserts.True(timeline.IsAvailable(9) && timeline.IsAvailable(10) && timeline.IsAvailable(11) && timeline.IsAvailable(12),
                "последние точки доступны");
            Asserts.True(timeline.Rewind(1) == null, "освобождённая точка не откатывается");
        }

        [Fact]
        public void AdvanceIgnoresNonPositiveAndNonFiniteSteps()
        {
            var timeline = new FlightCheckpointTimeline<State>();
            timeline.ResetForLaunch();

            int due;
            Asserts.False(timeline.Advance(0d, out due), "нулевой шаг не двигает часы");
            Asserts.False(timeline.Advance(-1d, out due), "отрицательный шаг не двигает часы");
            Asserts.False(timeline.Advance(double.NaN, out due), "NaN не двигает часы");
            Asserts.False(timeline.Advance(double.PositiveInfinity, out due), "бесконечность не двигает часы");
            Asserts.Near(timeline.RealSeconds, 0d, 0d, "часы остались на нуле");
        }

        [Fact]
        public void ResetForLaunchAllowsANewPointZero()
        {
            var timeline = new FlightCheckpointTimeline<State>();
            timeline.ResetForLaunch();
            timeline.Capture(0, 0d, 0d, FlightPointKind.Launch, new State(0));
            AdvanceAndCapture(timeline, 300d);
            Asserts.True(timeline.PointCount > 1, "точки появились");

            timeline.ResetForLaunch();
            Asserts.True(timeline.PointCount == 0, "новый полёт очищает шкалу");
            Asserts.Near(timeline.RealSeconds, 0d, 0d, "новый полёт обнуляет реальные часы");
            Asserts.True(timeline.NextCaptureIndex == 0, "новый полёт ждёт точку 0");

            timeline.Capture(0, 0d, 0d, FlightPointKind.Launch, new State(1000));
            Asserts.True(timeline.PointAt(0).State.Id == 1000, "новая точка 0 записана");
        }
    }
}
