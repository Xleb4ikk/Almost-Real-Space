using System;
using System.Collections.Generic;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Чистое ядро выработки ступени (без Unity): пулы компонентов топлива,
    /// группы двигателей, выработка участками постоянной тяги. ОДНО на двоих:
    /// его же используют физика полёта (StagePropulsion — Commit/тяга/отсечка)
    /// и панель ΔV (StageDeltaV.Evaluate), поэтому остаток и ΔV совпадают с
    /// физикой по построению.
    ///
    /// Модель: двигатели ступени работают одновременно; каждый берёт свои два
    /// компонента (BipropellantMix) в пропорции O/F. Когда любой из них у
    /// группы кончился, группа глохнет, остальные продолжают. Перекачки между
    /// ступенями нет. Все массы — кг, тяга — Н, расход — кг/с; тяга и расход
    /// считаются при газе 1.0 (газ только масштабирует оба, пропорции пулов
    /// не меняет).
    /// </summary>
    public sealed class StagePool
    {
        public Propellant Component;

        /// <summary>Сколько залито при запуске, кг.</summary>
        public double StartKg;

        /// <summary>Сколько осталось сейчас, кг.</summary>
        public double Kg;

        internal double Flow;
    }

    /// <summary>Одинаковые двигатели ступени (одна пара топлива, одна тяга).</summary>
    public sealed class StageEngineGroup
    {
        /// <summary>Идентичность двигателя (в игре — его EngineSpec): равные ключи сливаются в группу.</summary>
        public object Key;

        /// <summary>Название для интерфейса.</summary>
        public string Name;

        public BipropellantMix Mix;
        public int Count;

        /// <summary>Тяга ОДНОГО двигателя при газе 1.0, Н.</summary>
        public double ThrustN;

        /// <summary>Расход ОДНОГО двигателя при газе 1.0, кг/с.</summary>
        public double MassFlowKgS;

        public int OxPool = -1;
        public int FuelPool = -1;
    }

    /// <summary>Состояние одного пула — для передачи панели (и обратно).</summary>
    public readonly struct StagePoolState
    {
        public readonly int StageIndex;
        public readonly Propellant Component;
        public readonly double Kg;

        public StagePoolState(int stageIndex, Propellant component, double kg)
        {
            StageIndex = stageIndex;
            Component = component;
            Kg = kg;
        }
    }

    /// <summary>Итог интегрирования выработки.</summary>
    public struct StageBurnResult
    {
        /// <summary>ΔV по Циолковскому, м/с (только если задана стартовая масса).</summary>
        public double DeltaV;

        public double TimeS;
        public double BurnedKg;

        /// <summary>Тяга/расход на первом участке и тяга на последнем.</summary>
        public double ThrustFirstN;
        public double FlowFirstKgS;
        public double ThrustLastN;

        public double EndMassKg;
        public bool Started;
    }

    public sealed class StageCore
    {
        // 0.1 г: меньше — компонент считается кончившимся.
        public const double Eps = 1e-4d;

        /// <summary>Индекс ступени в сборке: 0 — нижняя (горит первой). Стабилен для одной сборки.</summary>
        public int Index;

        /// <summary>Сухая масса ступени (металл, двигатели, отделитель), кг.</summary>
        public double DryMassKg;

        /// <summary>Индексы деталей черновика (снизу вверх) — для маппинга сброса.</summary>
        public readonly List<int> PartIndices = new List<int>();

        public readonly List<StagePool> Pools = new List<StagePool>();
        public readonly List<StageEngineGroup> Engines = new List<StageEngineGroup>();

        private bool dirty = true;

        public int EngineCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Engines.Count; i++)
                {
                    n += Engines[i].Count;
                }

                return n;
            }
        }

        public bool HasEngine { get { return EngineCount > 0; } }

        /// <summary>Топливо сейчас, кг.</summary>
        public double PropellantKg
        {
            get
            {
                double sum = 0d;
                for (int i = 0; i < Pools.Count; i++)
                {
                    sum += Pools[i].Kg;
                }

                return sum;
            }
        }

        /// <summary>Топливо при запуске, кг.</summary>
        public double StartPropellantKg
        {
            get
            {
                double sum = 0d;
                for (int i = 0; i < Pools.Count; i++)
                {
                    sum += Pools[i].StartKg;
                }

                return sum;
            }
        }

        /// <summary>Сухая масса + топливо сейчас, кг.</summary>
        public double TotalMassKg { get { return DryMassKg + PropellantKg; } }

        // ─── Сборка ──────────────────────────────────────────────────────────

        public StagePool GetOrAddPool(Propellant component)
        {
            for (int i = 0; i < Pools.Count; i++)
            {
                if (Pools[i].Component == component)
                {
                    return Pools[i];
                }
            }

            var pool = new StagePool { Component = component };
            Pools.Add(pool);
            dirty = true;
            return pool;
        }

        /// <summary>Добавляет двигатель (с одинаковым key объединяются в группу).</summary>
        public StageEngineGroup AddEngine(
            object key, string name, BipropellantMix mix, double thrustN, double massFlowKgS)
        {
            for (int i = 0; i < Engines.Count; i++)
            {
                if (Equals(Engines[i].Key, key))
                {
                    Engines[i].Count++;
                    return Engines[i];
                }
            }

            var group = new StageEngineGroup
            {
                Key = key,
                Name = name,
                Mix = mix,
                Count = 1,
                ThrustN = thrustN,
                MassFlowKgS = massFlowKgS,
            };
            Engines.Add(group);
            dirty = true;
            return group;
        }

        /// <summary>Заливает пулы: Kg = StartKg. Зовут после сборки пулов.</summary>
        public void FillToStart()
        {
            for (int i = 0; i < Pools.Count; i++)
            {
                Pools[i].Kg = Pools[i].StartKg;
            }
        }

        public StageCore Clone()
        {
            var copy = new StageCore { Index = Index, DryMassKg = DryMassKg };
            copy.PartIndices.AddRange(PartIndices);
            for (int i = 0; i < Pools.Count; i++)
            {
                copy.Pools.Add(new StagePool
                {
                    Component = Pools[i].Component,
                    StartKg = Pools[i].StartKg,
                    Kg = Pools[i].Kg,
                });
            }

            for (int i = 0; i < Engines.Count; i++)
            {
                StageEngineGroup g = Engines[i];
                copy.Engines.Add(new StageEngineGroup
                {
                    Key = g.Key,
                    Name = g.Name,
                    Mix = g.Mix,
                    Count = g.Count,
                    ThrustN = g.ThrustN,
                    MassFlowKgS = g.MassFlowKgS,
                });
            }

            copy.dirty = true;
            return copy;
        }

        // ─── Состояние (для панели) ──────────────────────────────────────────

        /// <summary>Добавляет состояние всех пулов ступени в список.</summary>
        public void AppendState(List<StagePoolState> into)
        {
            for (int i = 0; i < Pools.Count; i++)
            {
                into.Add(new StagePoolState(Index, Pools[i].Component, Pools[i].Kg));
            }
        }

        /// <summary>
        /// Применяет состояние пула этой ступени. Значение зажимается в
        /// [0, StartKg]. Возвращает false, если такого компонента в ступени нет.
        /// </summary>
        public bool ApplyState(Propellant component, double kg)
        {
            for (int i = 0; i < Pools.Count; i++)
            {
                if (Pools[i].Component == component)
                {
                    Pools[i].Kg = Math.Max(0d, Math.Min(kg, Pools[i].StartKg));
                    return true;
                }
            }

            return false;
        }

        // ─── Текущая тяга ────────────────────────────────────────────────────

        /// <summary>Двигатель получает оба компонента.</summary>
        public bool IsFed(StageEngineGroup group)
        {
            Resolve();
            return group.Mix != null
                && group.OxPool >= 0 && group.FuelPool >= 0
                && Pools[group.OxPool].Kg > Eps && Pools[group.FuelPool].Kg > Eps;
        }

        /// <summary>Тяга работающих двигателей при газе 1.0, Н.</summary>
        public double ThrustN
        {
            get
            {
                double thrust = 0d;
                for (int e = 0; e < Engines.Count; e++)
                {
                    if (IsFed(Engines[e]))
                    {
                        thrust += Engines[e].ThrustN * Engines[e].Count;
                    }
                }

                return thrust;
            }
        }

        /// <summary>Расход работающих двигателей при газе 1.0, кг/с.</summary>
        public double MassFlowKgS
        {
            get
            {
                double flow = 0d;
                for (int e = 0; e < Engines.Count; e++)
                {
                    if (IsFed(Engines[e]))
                    {
                        flow += Engines[e].MassFlowKgS * Engines[e].Count;
                    }
                }

                return flow;
            }
        }

        /// <summary>
        /// Сколько массы сгорит до смены состава работающих двигателей
        /// (кончился какой-то компонент). 0 — тяги нет. Это граница участка
        /// постоянной тяги: детектор исчерпания ставится на неё.
        /// </summary>
        public double SegmentBurnKg()
        {
            StageCore probe = Clone();
            probe.Resolve();
            double flow = probe.ComputeFlows(out _, out double tPool, out _);
            return flow > 0d ? flow * tPool : 0d;
        }

        /// <summary>
        /// Сколько массы ступень ещё может сжечь до полного останова (с учётом
        /// того, что состав двигателей меняется по ходу). Масса отсечки
        /// корабля = текущая масса − CutoffBurnKg().
        /// </summary>
        public double CutoffBurnKg()
        {
            return Clone().Integrate(0d, double.PositiveInfinity, false).BurnedKg;
        }

        // ─── Выработка ───────────────────────────────────────────────────────

        /// <summary>
        /// Списывает burnedKg массы с пулов, как при реальной выработке (участки,
        /// пропорции O/F, глохнущие группы). Возвращает, сколько списано
        /// на самом деле (меньше запрошенного, если топливо кончилось).
        /// </summary>
        public double Commit(double burnedKg)
        {
            if (!(burnedKg > 0d))
            {
                return 0d;
            }

            return Integrate(0d, burnedKg, false).BurnedKg;
        }

        /// <summary>
        /// Выработка от стартовой массы корабля startMassKg: ΔV по Циолковскому,
        /// время, расход. budgetKg — сколько массы можно сжечь (бесконечность —
        /// до останова). mutate=false — на копии, пулы ступени не меняются.
        /// </summary>
        public StageBurnResult Burn(double startMassKg, double budgetKg, bool mutate)
        {
            StageCore target = mutate ? this : Clone();
            return target.Integrate(startMassKg, budgetKg, true);
        }

        private void Resolve()
        {
            if (!dirty)
            {
                return;
            }

            for (int e = 0; e < Engines.Count; e++)
            {
                BipropellantMix mix = Engines[e].Mix;
                Engines[e].OxPool = -1;
                Engines[e].FuelPool = -1;
                if (mix == null)
                {
                    continue;
                }

                for (int i = 0; i < Pools.Count; i++)
                {
                    if (Pools[i].Component == mix.Oxidizer)
                    {
                        Engines[e].OxPool = i;
                    }

                    if (Pools[i].Component == mix.Fuel)
                    {
                        Engines[e].FuelPool = i;
                    }
                }
            }

            dirty = false;
        }

        /// <summary>Потоки по пулам для работающих двигателей; возвращает суммарный расход.</summary>
        private double ComputeFlows(out double thrust, out double tPool, out int firstPool)
        {
            Resolve();
            thrust = 0d;
            double flow = 0d;
            for (int i = 0; i < Pools.Count; i++)
            {
                Pools[i].Flow = 0d;
            }

            for (int e = 0; e < Engines.Count; e++)
            {
                StageEngineGroup eg = Engines[e];
                if (!IsFed(eg))
                {
                    continue;
                }

                double mdot = eg.MassFlowKgS * eg.Count;
                double oxShare = eg.Mix.OxidizerMassFraction;
                Pools[eg.OxPool].Flow += mdot * oxShare;
                Pools[eg.FuelPool].Flow += mdot * (1d - oxShare);
                thrust += eg.ThrustN * eg.Count;
                flow += mdot;
            }

            tPool = double.MaxValue;
            firstPool = -1;
            for (int i = 0; i < Pools.Count; i++)
            {
                if (Pools[i].Flow > 0d)
                {
                    double t = Pools[i].Kg / Pools[i].Flow;
                    if (t < tPool)
                    {
                        tPool = t;
                        firstPool = i;
                    }
                }
            }

            return flow;
        }

        private StageBurnResult Integrate(double startMassKg, double budgetKg, bool wantDeltaV)
        {
            var o = new StageBurnResult { EndMassKg = startMassKg };
            double mass = startMassKg;

            int guard = Pools.Count + Engines.Count + 4;
            for (int iter = 0; iter < guard; iter++)
            {
                double flow = ComputeFlows(out double thrust, out double tPool, out int first);
                double room = budgetKg - o.BurnedKg;
                if (flow <= 0d || room <= 0d)
                {
                    break;
                }

                double tBudget = room / flow;
                bool byBudget = tBudget < tPool;
                double tEvent = byBudget ? tBudget : tPool;

                if (!o.Started)
                {
                    o.ThrustFirstN = thrust;
                    o.FlowFirstKgS = flow;
                    o.Started = true;
                }

                if (wantDeltaV)
                {
                    double after = mass - (flow * tEvent);
                    o.DeltaV += (thrust / flow) * Math.Log(mass / after);
                    mass = after;
                }

                o.TimeS += tEvent;
                o.ThrustLastN = thrust;
                o.BurnedKg += flow * tEvent;

                for (int i = 0; i < Pools.Count; i++)
                {
                    Pools[i].Kg -= Pools[i].Flow * tEvent;
                    if ((!byBudget && i == first) || Pools[i].Kg < Eps)
                    {
                        Pools[i].Kg = 0d;
                    }
                }

                if (byBudget)
                {
                    break;
                }
            }

            o.EndMassKg = wantDeltaV ? mass : startMassKg - o.BurnedKg;
            return o;
        }
    }
}
