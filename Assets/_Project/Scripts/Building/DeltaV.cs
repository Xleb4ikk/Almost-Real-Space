using System;
using System.Collections.Generic;
using Galilego.Spacecraft;

namespace Galilego.Building
{
    /// <summary>
    /// Bridge "assembly draft → stage cores": the single place that applies the
    /// "a separator ends a stage" rule and finds the main assembly. All the math
    /// lives in the pure StageBurnCore / StageDeltaV.
    ///
    /// Stages. The main assembly is the snap-connected group of parts with the
    /// greatest mass. Parts are ordered bottom-up by pivot height; a SEPARATOR
    /// divides the stack: everything below it is one stage, and the separator
    /// itself goes with it. The bottom stage burns first.
    /// </summary>
    public static class DeltaV
    {
        /// <summary>Plan: all tanks full.</summary>
        public static DvResult Compute(IReadOnlyList<PlacedPart> parts, DvEnvironment env)
        {
            List<StageCore> cores = BuildStageAssembly(parts, env, out int detached);
            return StageDeltaV.Evaluate(env, cores, null, detached, false);
        }

        /// <summary>
        /// (legacy) Remaining ΔV by the vessel's current mass currentMassKg
        /// (Ship.Mass): the burned amount is distributed over the stages in plan
        /// order. ComputeFromState from the physics model is more accurate and preferred.
        /// </summary>
        public static DvResult ComputeInFlight(
            IReadOnlyList<PlacedPart> parts, DvEnvironment env, double currentMassKg)
        {
            List<StageCore> cores = BuildStageAssembly(parts, env, out int detached);
            return StageDeltaV.EvaluateByMass(env, cores, currentMassKg, detached);
        }

        /// <summary>
        /// Remaining ΔV by the physics model state: pool remainders and jettisoned
        /// stage flags (indexed by StageCore.Index from BuildStageAssembly of the
        /// same assembly). A pool without a record is treated as full; jettisoned
        /// may be shorter than the stage count (missing entries are not jettisoned).
        /// </summary>
        public static DvResult ComputeFromState(
            IReadOnlyList<PlacedPart> parts,
            DvEnvironment env,
            IReadOnlyList<StagePoolState> pools,
            IReadOnlyList<bool> jettisoned)
        {
            List<StageCore> cores = BuildStageAssembly(parts, env, out int detached);
            return StageDeltaV.EvaluateFromState(env, cores, pools, jettisoned, detached);
        }

        public static string FormatKg(double kg)
        {
            return StageDeltaV.FormatKg(kg);
        }

        // ─── Stage cores from the draft ──────────────────────────────────────

        /// <summary>Stage cores of the assembly in vacuum without a body (vacuum thrust/flow).</summary>
        public static List<StageCore> BuildStageAssembly(IReadOnlyList<PlacedPart> parts)
        {
            return BuildStageAssembly(parts, DvEnvironment.Default, out _);
        }

        public static List<StageCore> BuildStageAssembly(IReadOnlyList<PlacedPart> parts, DvEnvironment env)
        {
            return BuildStageAssembly(parts, env, out _);
        }

        /// <summary>
        /// Stage cores of the main assembly, bottom-up; Index is the ordinal
        /// number (0 — bottom) and is stable for the same assembly; PartIndices
        /// are indices into parts (jettison mapping). Pools are filled completely.
        /// detachedParts is the number of parts outside the main assembly (not in cores).
        /// </summary>
        public static List<StageCore> BuildStageAssembly(
            IReadOnlyList<PlacedPart> parts, DvEnvironment env, out int detachedParts)
        {
            var cores = new List<StageCore>();
            detachedParts = 0;
            if (parts == null || parts.Count == 0)
            {
                return cores;
            }

            List<int> main = MainAssembly(parts, out detachedParts);
            if (main.Count == 0)
            {
                return cores;
            }

            // Bottom-up by pivot height (stable by index).
            main.Sort((a, b) =>
            {
                int c = parts[a].Pivot.y.CompareTo(parts[b].Pivot.y);
                return c != 0 ? c : a.CompareTo(b);
            });

            // Split at separators: a separator belongs to the stage BELOW itself.
            var groups = new List<List<int>>();
            var current = new List<int>();
            for (int i = 0; i < main.Count; i++)
            {
                current.Add(main[i]);
                if (parts[main[i]].Kind == PartKind.Separator)
                {
                    groups.Add(current);
                    current = new List<int>();
                }
            }

            if (current.Count > 0)
            {
                groups.Add(current);
            }

            for (int g = 0; g < groups.Count; g++)
            {
                cores.Add(BuildCore(parts, groups[g], g, env));
            }

            return cores;
        }

        private static StageCore BuildCore(
            IReadOnlyList<PlacedPart> parts, List<int> members, int index, DvEnvironment env)
        {
            var core = new StageCore { Index = index };
            for (int k = 0; k < members.Count; k++)
            {
                int partIndex = members[k];
                PlacedPart part = parts[partIndex];
                core.PartIndices.Add(partIndex);
                core.DryMassKg += part.Metrics.DryMassKg;

                if (part.Kind == PartKind.FuelTank && part.Metrics.Load != null)
                {
                    PropellantLoad load = part.Metrics.Load;
                    for (int c = 0; c < load.Count; c++)
                    {
                        double kg = load.ComponentMassKg(c, part.Metrics.VolumeM3, 1d);
                        core.GetOrAddPool(load.Component(c)).StartKg += kg;
                    }
                }
                else if (part.Kind == PartKind.Engine && part.EngineSpec != null)
                {
                    EngineSpec spec = part.EngineSpec;
                    core.AddEngine(spec, spec.DisplayName, spec.Mix, env.ThrustN(spec), env.MassFlowKgS(spec));
                }
            }

            core.FillToStart();
            return core;
        }

        // ─── Main assembly ───────────────────────────────────────────────────

        /// <summary>Connected group (by ParentIndex) with the greatest mass.</summary>
        private static List<int> MainAssembly(IReadOnlyList<PlacedPart> parts, out int detached)
        {
            int n = parts.Count;
            var root = new int[n];
            for (int i = 0; i < n; i++)
            {
                root[i] = i;
            }

            for (int i = 0; i < n; i++)
            {
                int p = parts[i].ParentIndex;
                if (p >= 0 && p < n)
                {
                    Union(root, i, p);
                }
            }

            var mass = new Dictionary<int, double>();
            for (int i = 0; i < n; i++)
            {
                int r = Find(root, i);
                mass.TryGetValue(r, out double m);
                mass[r] = m + parts[i].Metrics.MassKg;
            }

            int best = -1;
            double bestMass = -1d;
            for (int i = 0; i < n; i++)
            {
                int r = Find(root, i);
                if (r == i && mass[r] > bestMass)
                {
                    best = r;
                    bestMass = mass[r];
                }
            }

            var main = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (Find(root, i) == best)
                {
                    main.Add(i);
                }
            }

            detached = n - main.Count;
            return main;
        }

        private static int Find(int[] root, int x)
        {
            while (root[x] != x)
            {
                root[x] = root[root[x]];
                x = root[x];
            }

            return x;
        }

        private static void Union(int[] root, int a, int b)
        {
            int ra = Find(root, a);
            int rb = Find(root, b);
            if (ra != rb)
            {
                root[ra] = rb;
            }
        }
    }
}
