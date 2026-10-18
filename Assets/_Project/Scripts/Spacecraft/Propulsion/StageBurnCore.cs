using System;
using System.Collections.Generic;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Pure stage depletion core (no Unity): propellant component pools,
    /// engine groups, depletion in constant-thrust segments. Shared by both:
    /// flight physics (StagePropulsion — Commit/thrust/cutoff) and the ΔV
    /// panel (StageDeltaV.Evaluate), so remaining propellant and ΔV match
    /// physics by construction.
    ///
    /// Model: stage engines run simultaneously; each draws its two components
    /// (BipropellantMix) at the O/F ratio. When either runs out for a group,
    /// that group cuts out, the rest keep running. No transfer between stages.
    /// All masses in kg, thrust in N, flow in kg/s; thrust and flow are computed
    /// at throttle 1.0 (throttle only scales both, it does not change pool ratios).
    /// </summary>
    public sealed class StagePool
    {
        public Propellant Component;

        /// <summary>Amount loaded at startup, kg.</summary>
        public double StartKg;

        /// <summary>Amount remaining now, kg.</summary>
        public double Kg;

        internal double Flow;
    }

    /// <summary>Identical stage engines (one propellant pair, one thrust).</summary>
    public sealed class StageEngineGroup
    {
        /// <summary>Engine identity (in game its EngineSpec): equal keys merge into one group.</summary>
        public object Key;

        /// <summary>Display name.</summary>
        public string Name;

        public BipropellantMix Mix;
        public int Count;

        /// <summary>Thrust of a SINGLE engine at throttle 1.0, N.</summary>
        public double ThrustN;

        /// <summary>Mass flow of a SINGLE engine at throttle 1.0, kg/s.</summary>
        public double MassFlowKgS;

        public int OxPool = -1;
        public int FuelPool = -1;
    }

    /// <summary>State of one pool — passed to the panel (and back).</summary>
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

    /// <summary>Result of the depletion integration.</summary>
    public struct StageBurnResult
    {
        /// <summary>Tsiolkovsky ΔV, m/s (only when a start mass is given).</summary>
        public double DeltaV;

        public double TimeS;
        public double BurnedKg;

        /// <summary>Thrust/flow on the first segment and thrust on the last.</summary>
        public double ThrustFirstN;
        public double FlowFirstKgS;
        public double ThrustLastN;

        public double EndMassKg;
        public bool Started;
    }

    public sealed class StageCore
    {
        // 0.1 g: below this a component is treated as depleted.
        public const double Eps = 1e-4d;

        /// <summary>Stage index in the assembly: 0 is bottom (burns first). Stable for one assembly.</summary>
        public int Index;

        /// <summary>Stage dry mass (metal, engines, decoupler), kg.</summary>
        public double DryMassKg;

        /// <summary>Draft part indices (bottom to top) — for jettison mapping.</summary>
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

        /// <summary>Propellant now, kg.</summary>
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

        /// <summary>Propellant at startup, kg.</summary>
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

        /// <summary>Dry mass + propellant now, kg.</summary>
        public double TotalMassKg { get { return DryMassKg + PropellantKg; } }

        // ─── Assembly ──────────────────────────────────────────────────────────

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

        /// <summary>Adds an engine (equal key merges into the group).</summary>
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

        /// <summary>Fills pools: Kg = StartKg. Called after pools are assembled.</summary>
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

        // ─── State (for panel) ──────────────────────────────────────────

        /// <summary>Appends the state of all pools of this stage to the list.</summary>
        public void AppendState(List<StagePoolState> into)
        {
            for (int i = 0; i < Pools.Count; i++)
            {
                into.Add(new StagePoolState(Index, Pools[i].Component, Pools[i].Kg));
            }
        }

        /// <summary>
        /// Applies a pool state for this stage. The value is clamped to
        /// [0, StartKg]. Returns false if the stage has no such component.
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

        // ─── Current thrust ────────────────────────────────────────────────────

        /// <summary>Engine is fed with both components.</summary>
        public bool IsFed(StageEngineGroup group)
        {
            Resolve();
            return group.Mix != null
                && group.OxPool >= 0 && group.FuelPool >= 0
                && Pools[group.OxPool].Kg > Eps && Pools[group.FuelPool].Kg > Eps;
        }

        /// <summary>Thrust of running engines at throttle 1.0, N.</summary>
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

        /// <summary>Mass flow of running engines at throttle 1.0, kg/s.</summary>
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
        /// Mass that burns before the set of running engines changes
        /// (some component runs out). 0 — no thrust. This is the boundary of the
        /// constant-thrust segment: the depletion detector is placed on it.
        /// </summary>
        public double SegmentBurnKg()
        {
            StageCore probe = Clone();
            probe.Resolve();
            double flow = probe.ComputeFlows(out _, out double tPool, out _);
            return flow > 0d ? flow * tPool : 0d;
        }

        /// <summary>
        /// Mass the stage can still burn before a complete cutoff (accounting for
        /// the engine set changing along the way). Ship cutoff mass
        /// = current mass − CutoffBurnKg().
        /// </summary>
        public double CutoffBurnKg()
        {
            return Clone().Integrate(0d, double.PositiveInfinity, false).BurnedKg;
        }

        // ─── Depletion ───────────────────────────────────────────────────────

        /// <summary>
        /// Commits burnedKg of mass from the pools as real depletion would (segments,
        /// O/F ratios, cutting-out groups). Returns how much was actually
        /// committed (less than requested if propellant ran out).
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
        /// Depletion from the ship start mass startMassKg: Tsiolkovsky ΔV,
        /// time, flow. budgetKg is how much mass may burn (infinity —
        /// down to cutoff). mutate=false runs on a copy, the stage pools are unchanged.
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

        /// <summary>Per-pool flows for running engines; returns total flow.</summary>
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
