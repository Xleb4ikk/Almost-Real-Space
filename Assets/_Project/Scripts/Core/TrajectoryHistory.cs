using System;
using System.Collections.Generic;

namespace Galilego.Core
{
    /// <summary>
    /// Вид сэмпла истории. Обычные прореживаются шагом, событийные и граничные —
    /// никогда: touchdown/entry/SOI обязаны сохраняться, даже если событие легло
    /// между обычными сэмплами или почти ровно на границу интервала.
    /// </summary>
    public enum HistorySampleKind
    {
        Regular,
        Boundary,
        Event
    }

    public readonly struct HistorySample
    {
        public readonly double TimeSeconds;
        public readonly SpacecraftIntegrationState State;
        public readonly HistorySampleKind Kind;

        public HistorySample(double timeSeconds, SpacecraftIntegrationState state, HistorySampleKind kind)
        {
            TimeSeconds = timeSeconds;
            State = state;
            Kind = kind;
        }
    }

    /// <summary>
    /// История как представление траектории (не состояние физики). Инвариант 1:
    /// построение истории не меняет State/time/события propagator — builder
    /// только ЧИТАЕТ dense-трек, ни одного Step внутри.
    /// </summary>
    public sealed class TrajectoryHistory
    {
        private readonly List<HistorySample> samples = new List<HistorySample>();

        public IReadOnlyList<HistorySample> Samples => samples;

        public void Clear()
        {
            samples.Clear();
        }

        internal void AddSample(double timeSeconds, SpacecraftIntegrationState state, HistorySampleKind kind)
        {
            samples.Add(new HistorySample(timeSeconds, state, kind));
        }
    }

    /// <summary>
    /// Построение истории из dense-трека. Инвариант 2: сетка времён детерминирована
    /// шагом stride от абсолютного нуля (k·stride), а не от размера physics-чанков:
    /// один и тот же прогон с разной нарезкой даёт то же множество времён
    /// (значения — в пределах шума интегратора, времена — точно).
    /// Слияние в пределах MergeEpsilon (1e-9с): событие почти на сетке/границе не
    /// дублируется; приоритет вида Event &gt; Boundary &gt; Regular — событие
    /// важнее совпавшей границы, граница важнее регулярного.
    /// </summary>
    public static class HistoryBuilder
    {
        public const double MergeEpsilonSeconds = 1e-9d;

        public static void BuildFromTrack(
            TrajectoryHistory history,
            IReadOnlyList<DenseSegment> track,
            double startTimeSeconds,
            double endTimeSeconds,
            double strideSeconds,
            IReadOnlyList<double> eventTimes)
        {
            if (history == null)
                throw new ArgumentNullException(nameof(history));
            if (track == null || track.Count == 0)
                throw new ArgumentException("Трек пуст — историю строить не из чего.", nameof(track));
            if (strideSeconds <= 0d)
                throw new ArgumentOutOfRangeException(nameof(strideSeconds), "Шаг истории обязан быть положительным.");
            if (endTimeSeconds < startTimeSeconds)
                throw new ArgumentOutOfRangeException(nameof(endTimeSeconds), "Конец раньше начала.");

            history.Clear();
            var times = new List<double>();
            var kinds = new List<HistorySampleKind>();
            times.Add(startTimeSeconds);
            kinds.Add(HistorySampleKind.Boundary);

            long firstGrid = (long)Math.Floor(startTimeSeconds / strideSeconds) + 1;
            for (long k = firstGrid; ; k++)
            {
                double t = k * strideSeconds;
                if (t >= endTimeSeconds - MergeEpsilonSeconds)
                {
                    break;
                }

                if (t <= startTimeSeconds + MergeEpsilonSeconds)
                {
                    continue;
                }

                times.Add(t);
                kinds.Add(HistorySampleKind.Regular);
            }

            if (eventTimes != null)
            {
                for (int i = 0; i < eventTimes.Count; i++)
                {
                    double te = eventTimes[i];
                    if (te < startTimeSeconds - MergeEpsilonSeconds || te > endTimeSeconds + MergeEpsilonSeconds)
                    {
                        continue;
                    }

                    if (te < startTimeSeconds)
                    {
                        te = startTimeSeconds;
                    }
                    else if (te > endTimeSeconds)
                    {
                        te = endTimeSeconds;
                    }

                    AddOrMerge(times, kinds, te, HistorySampleKind.Event);
                }
            }

            times.Add(endTimeSeconds);
            kinds.Add(HistorySampleKind.Boundary);
            SortAndMerge(times, kinds);

            for (int i = 0; i < times.Count; i++)
            {
                history.AddSample(times[i], DenseSegment.EvaluateTrack(track, times[i]), kinds[i]);
            }
        }

        private static void AddOrMerge(List<double> times, List<HistorySampleKind> kinds, double time, HistorySampleKind kind)
        {
            for (int i = 0; i < times.Count; i++)
            {
                if (Math.Abs(times[i] - time) <= MergeEpsilonSeconds)
                {
                    if (kind > kinds[i])
                    {
                        kinds[i] = kind;
                    }

                    if (kind == HistorySampleKind.Event)
                    {
                        times[i] = time;
                    }

                    return;
                }
            }

            times.Add(time);
            kinds.Add(kind);
        }

        private static void SortAndMerge(List<double> times, List<HistorySampleKind> kinds)
        {
            for (int i = 1; i < times.Count; i++)
            {
                double keyTime = times[i];
                HistorySampleKind keyKind = kinds[i];
                int j = i - 1;
                while (j >= 0 && times[j] > keyTime)
                {
                    times[j + 1] = times[j];
                    kinds[j + 1] = kinds[j];
                    j--;
                }

                times[j + 1] = keyTime;
                kinds[j + 1] = keyKind;
            }

            int w = 0;
            for (int r = 0; r < times.Count; r++)
            {
                if (w > 0 && Math.Abs(times[r] - times[w - 1]) <= MergeEpsilonSeconds)
                {
                    if (kinds[r] > kinds[w - 1])
                    {
                        kinds[w - 1] = kinds[r];
                        times[w - 1] = times[r];
                    }
                }
                else
                {
                    times[w] = times[r];
                    kinds[w] = kinds[r];
                    w++;
                }
            }

            while (times.Count > w)
            {
                times.RemoveAt(times.Count - 1);
                kinds.RemoveAt(kinds.Count - 1);
            }
        }
    }
}
