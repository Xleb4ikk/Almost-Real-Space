using System.Collections.Generic;

namespace Galilego.Spacecraft
{
    /// <summary>Итог нажатия «ступень» (Space): что сделала модель.</summary>
    public enum StagingResult
    {
        /// <summary>Ничего: нечего зажигать или последнюю ступень отделять нечем.</summary>
        Nothing,

        /// <summary>Зажжена нижняя присоединённая ступень (первое нажатие).</summary>
        Ignited,

        /// <summary>Нижняя присоединённая ступень отделена, следующая зажжена.</summary>
        JettisonedAndIgnited
    }

    /// <summary>
    /// Рантайм-модель двигательной установки в полёте: ядра ступеней (StageCore,
    /// из DeltaV.BuildStageAssembly) плюс состояние «что присоединено и что
    /// зажжено». Чистая (без Unity), ОДНА на физику и панель: StageThrustSource
    /// берёт отсюда тягу/расход активной ступени, VesselStep списывает сюда
    /// ФАКТИЧЕСКУЮ дельту массы шага, панель читает пулы и флаги через
    /// AppendState / JettisonedFlags.
    ///
    /// Правила (этапы 1–2):
    ///  • ступени отделяются снизу вверх: присоединённые — суффикс [FirstAttached..];
    ///  • зажигание — только по Space (Stage): первое нажатие зажигает нижнюю
    ///    присоединённую, следующее — отделяет её и зажигает новую нижнюю (KSP);
    ///  • AutoIgnite (отладка этапа 1) — зажигает ступени сами, БЕЗ отделения:
    ///    выработанная ступень остаётся на борту мёртвой массой, панель честно
    ///    показывает «выработанные ступени ещё на борту»;
    ///  • топливо не перекачивается между ступенями; расход — из ядер.
    /// </summary>
    public sealed class StagePropulsion
    {
        private readonly List<StageCore> cores;
        private int firstAttached;
        private int ignited = -1;

        /// <summary>Масса деталей вне основной сборки: в Ship.Mass они есть, в ядрах — нет.</summary>
        public double DetachedMassKg { get; private set; }

        /// <summary>Списать массу аппаратуры вне ядер (расход РСУ/САС, отстрел).</summary>
        public void ReleaseDetached(double kg)
        {
            DetachedMassKg = System.Math.Max(0d, DetachedMassKg - kg);
            Revision++;
        }

        /// <summary>Все ступени отделены (аварийный увод): остаток корабля — масса вне ядер.</summary>
        public void DetachAllStages(double remainingMassKg)
        {
            firstAttached = cores.Count;
            ignited = -1;
            DetachedMassKg = remainingMassKg;
            Revision++;
        }

        /// <summary>Отладка: зажигать ступени автоматически, без отделения (проверка этапа 1).</summary>
        public bool AutoIgnite;

        /// <summary>Счётчик изменений состояния (панель пересчитывает по нему).</summary>
        public int Revision { get; private set; }

        public StagePropulsion(List<StageCore> cores, double detachedMassKg, bool autoIgnite)
        {
            this.cores = cores ?? new List<StageCore>();
            DetachedMassKg = detachedMassKg;
            AutoIgnite = autoIgnite;
            if (autoIgnite && this.cores.Count > 0)
            {
                ignited = 0;
                Revision++;
            }
        }

        public IReadOnlyList<StageCore> Cores { get { return cores; } }

        public int StageCount { get { return cores.Count; } }

        /// <summary>Индекс первой присоединённой ступени; всё ниже — сброшено.</summary>
        public int FirstAttached { get { return firstAttached; } }

        /// <summary>Индекс зажжённой ступени, −1 — ничего не горит.</summary>
        public int ActiveStageIndex { get { return ignited; } }

        /// <summary>Ядро активной ступени; null — нечего жечь.</summary>
        public StageCore ActiveCore
        {
            get { return ignited >= 0 && ignited < cores.Count ? cores[ignited] : null; }
        }

        /// <summary>Тяга работающих двигателей активной ступени при газе 1.0, Н.</summary>
        public double ThrustN
        {
            get
            {
                StageCore core = ActiveCore;
                return core != null ? core.ThrustN : 0d;
            }
        }

        /// <summary>Расход активной ступени при газе 1.0, кг/с.</summary>
        public double MassFlowKgS
        {
            get
            {
                StageCore core = ActiveCore;
                return core != null ? core.MassFlowKgS : 0d;
            }
        }

        /// <summary>Масса до смены состава двигателей активной ступени, кг (0 — тяги нет).</summary>
        public double SegmentBurnKg
        {
            get
            {
                StageCore core = ActiveCore;
                return core != null ? core.SegmentBurnKg() : 0d;
            }
        }

        /// <summary>Ступень отделена?</summary>
        public bool IsJettisoned(int stage)
        {
            return stage < firstAttached;
        }

        /// <summary>Над зажжённой ступенью есть ещё ступень (для автозажигания).</summary>
        public bool CanIgniteNext { get { return ignited + 1 < cores.Count; } }

        /// <summary>Масса присоединённых ступеней (сухая + топливо сейчас), кг.</summary>
        public double AttachedTotalKg()
        {
            double sum = 0d;
            for (int k = firstAttached; k < cores.Count; k++)
            {
                sum += cores[k].TotalMassKg;
            }

            return sum;
        }

        /// <summary>Полная масса ступени (сухая + топливо сейчас), кг.</summary>
        public double StageTotalKg(int stage)
        {
            return stage >= 0 && stage < cores.Count ? cores[stage].TotalMassKg : 0d;
        }

        /// <summary>
        /// Списать фактически сгоревшую массу с активной ступени (единственный
        /// мутатор пулов). Возвращает, сколько списано на самом деле: меньше
        /// запрошенного — топлива не хватило, это нарушение инварианта массы,
        /// ловит вызывающий (VesselStep).
        /// </summary>
        public double CommitBurn(double burnedKg)
        {
            StageCore core = ActiveCore;
            if (core == null || !(burnedKg > 0d))
            {
                return 0d;
            }

            double committed = core.Commit(burnedKg);
            Revision++;
            return committed;
        }

        /// <summary>
        /// Нажатие «ступень» (Space): первое — зажигание нижней присоединённой;
        /// следующее — отделение нижней присоединённой и зажигание новой нижней.
        /// Последнюю ступень отделять нечем (над ней нет отделителя) — Nothing.
        /// </summary>
        public StagingResult Stage()
        {
            if (ignited < 0)
            {
                if (firstAttached >= cores.Count)
                {
                    return StagingResult.Nothing;
                }

                ignited = firstAttached;
                Revision++;
                return StagingResult.Ignited;
            }

            if (firstAttached + 1 >= cores.Count)
            {
                return StagingResult.Nothing;
            }

            firstAttached++;
            if (ignited < firstAttached)
            {
                ignited = firstAttached;
            }

            Revision++;
            return StagingResult.JettisonedAndIgnited;
        }

        /// <summary>
        /// Автозажигание (отладка этапа 1): зажечь следующую присоединённую
        /// ступень, НЕ отделяя выработанную. true — зажгли.
        /// </summary>
        public bool IgniteNextAttached()
        {
            int next = ignited >= 0 ? ignited + 1 : firstAttached;
            if (next >= cores.Count)
            {
                return false;
            }

            ignited = next;
            Revision++;
            return true;
        }

        /// <summary>Состояние всех пулов — панели (StageDeltaV.EvaluateFromState).</summary>
        public void AppendState(List<StagePoolState> into)
        {
            for (int k = 0; k < cores.Count; k++)
            {
                cores[k].AppendState(into);
            }
        }

        /// <summary>Флаги сброшенных ступеней по индексу ядра.</summary>
        public bool[] JettisonedFlags()
        {
            var flags = new bool[cores.Count];
            for (int k = 0; k < firstAttached && k < cores.Count; k++)
            {
                flags[k] = true;
            }

            return flags;
        }
    }
}
