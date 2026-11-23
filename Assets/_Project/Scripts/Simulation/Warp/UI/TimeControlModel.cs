using System;
using System.Collections.Generic;

namespace Galilego.Universe
{
    /// <summary>Which kind of time acceleration is active.</summary>
    internal enum WarpKind
    {
        /// <summary>Physical warp: the physics stays the same, only more sim time per frame (x1, x2, x3, x4, x6).</summary>
        Physical,

        /// <summary>Full (long) warp: huge factors, ballistics only (x10 ... x1 000 000).</summary>
        Full
    }

    /// <summary>
    /// Everything <see cref="TimeControlPanel"/> draws and nothing else — the view-model.
    /// The panel only READS this and reports what the player did through the events; the
    /// real rewind / warp logic lives in <see cref="TimeControlRuntime"/> and plugs in
    /// through these events.
    ///
    /// Track axis = REAL minutes of the flight: point k is created when the k-th real
    /// minute is reached (k = 0 is the start). The time written on a point is the
    /// FLIGHT time (sim time since launch) at that moment, which differs from k minutes
    /// as soon as warp was used.
    /// </summary>
    internal sealed class TimeControlModel
    {
        /// <summary>Sim time since launch, in seconds — the main clock.</summary>
        public double CurrentFlightSeconds;

        /// <summary>Playhead position on the track, in real flight minutes (fractional, e.g. 14.62).</summary>
        public double CurrentRealMinutes;

        /// <summary>
        /// Flight seconds stored at checkpoint k (index = real minute). Points whose index is
        /// greater than <see cref="CurrentRealMinutes"/> are "future": left over after a
        /// rewind, still selectable, overwritten when the playhead passes them again.
        /// </summary>
        public readonly List<double> PointFlightSeconds = new List<double>();

        /// <summary>
        /// Можно ли откатиться на точку k. false — точка снята в контактной зоне (PhysX
        /// невосстановим) или её состояние освобождено лимитом памяти. Серые точки на шкале.
        /// </summary>
        public readonly List<bool> PointAvailable = new List<bool>();

        public WarpKind ActiveKind = WarpKind.Physical;

        /// <summary>
        /// Active physical rung (one of <see cref="TimeControlPanel.PhysicalFactors"/>).
        /// The ×1 slot is normal time; this value only highlights the rung in its flyout.
        /// </summary>
        public int PhysicalFactor = 1;

        /// <summary>Index into <see cref="TimeControlPanel.FullFactors"/>, valid when ActiveKind == Full.</summary>
        public int FullIndex = -1;

        /// <summary>
        /// Фактор, реально применённый в этом кадре (кап атмосферы/посадки, режим EVA,
        /// запрет долгого варпа). Отличается от выбранного — панель обязана это показать,
        /// молчаливый срез запрещён.
        /// </summary>
        public double AppliedFactor = 1d;

        /// <summary>true — применённый фактор меньше выбранного (сработал кап).</summary>
        public bool AppliedFactorClamped;

        /// <summary>Почему фактор срезан: «площадка», «стройка», «поверхность», «атмосфера».</summary>
        public string ClampReason = string.Empty;

        /// <summary>Короткое сообщение панели: почему откат отклонён и т. п. Пусто — нет сообщения.</summary>
        public string Notice = string.Empty;

        /// <summary>Player asked to rewind/jump to this point index (button "Перемотать время").</summary>
        public event Action<int> RewindRequested;

        /// <summary>Player picked a physical factor (x1..x6).</summary>
        public event Action<int> PhysicalFactorChosen;

        /// <summary>Player picked a full warp rung (index into FullFactors).</summary>
        public event Action<int> FullFactorChosen;

        public int PointCount { get { return PointFlightSeconds.Count; } }

        public bool IsPointAvailable(int index)
        {
            return index >= 0 && index < PointAvailable.Count && PointAvailable[index];
        }

        internal void RaiseRewind(int point) { RewindRequested?.Invoke(point); }
        internal void RaisePhysical(int factor) { PhysicalFactorChosen?.Invoke(factor); }
        internal void RaiseFull(int index) { FullFactorChosen?.Invoke(index); }
    }
}
