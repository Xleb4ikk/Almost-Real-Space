using System;
using System.Collections.Generic;
using Galilego.Spacecraft;

namespace Galilego.Building
{
    /// <summary>
    /// Мост «черновик сборки → ядра ступеней»: единственное место, где
    /// применяется правило «отделитель заканчивает ступень» и находится
    /// основная сборка. Весь расчёт — в чистых StageBurnCore / StageDeltaV.
    ///
    /// Ступени. Основная сборка — связная по снапу группа деталей с наибольшей
    /// массой. Детали упорядочиваются снизу вверх по высоте пивота; ОТДЕЛИТЕЛЬ
    /// делит стопку: всё ниже него — одна ступень, и сам отделитель уходит
    /// вместе с ней. Нижняя ступень горит первой.
    /// </summary>
    public static class DeltaV
    {
        /// <summary>План: все баки полные.</summary>
        public static DvResult Compute(IReadOnlyList<PlacedPart> parts, DvEnvironment env)
        {
            List<StageCore> cores = BuildStageAssembly(parts, env, out int detached);
            return StageDeltaV.Evaluate(env, cores, null, detached, false);
        }

        /// <summary>
        /// (legacy) Остаток по текущей массе корабля currentMassKg (Ship.Mass):
        /// сгоревшее распределяется по ступеням в порядке плана. Точнее и
        /// предпочтительнее — ComputeFromState от модели физики.
        /// </summary>
        public static DvResult ComputeInFlight(
            IReadOnlyList<PlacedPart> parts, DvEnvironment env, double currentMassKg)
        {
            List<StageCore> cores = BuildStageAssembly(parts, env, out int detached);
            return StageDeltaV.EvaluateByMass(env, cores, currentMassKg, detached);
        }

        /// <summary>
        /// Остаток по состоянию модели физики: остатки по пулам и флаги сброшенных
        /// ступеней (индексация — StageCore.Index из BuildStageAssembly той же
        /// сборки). Пул без записи считается полным; jettisoned может быть
        /// короче числа ступеней (недостающие — не сброшены).
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

        // ─── Ядра ступеней из черновика ──────────────────────────────────────

        /// <summary>Ядра ступеней сборки в вакууме без тела (тяга/расход вакуумные).</summary>
        public static List<StageCore> BuildStageAssembly(IReadOnlyList<PlacedPart> parts)
        {
            return BuildStageAssembly(parts, DvEnvironment.Default, out _);
        }

        public static List<StageCore> BuildStageAssembly(IReadOnlyList<PlacedPart> parts, DvEnvironment env)
        {
            return BuildStageAssembly(parts, env, out _);
        }

        /// <summary>
        /// Ядра ступеней основной сборки, снизу вверх; Index — порядковый номер
        /// (0 — нижняя) и стабилен для одной и той же сборки; PartIndices —
        /// индексы деталей в parts (маппинг сброса). Пулы залиты полностью.
        /// detachedParts — деталей вне основной сборки (в ядра не входят).
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

            // Снизу вверх по высоте пивота (стабильно по индексу).
            main.Sort((a, b) =>
            {
                int c = parts[a].Pivot.y.CompareTo(parts[b].Pivot.y);
                return c != 0 ? c : a.CompareTo(b);
            });

            // Делим по отделителям: отделитель входит в ступень НИЖЕ себя.
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

        // ─── Основная сборка ─────────────────────────────────────────────────

        /// <summary>Связная (по ParentIndex) группа с наибольшей массой.</summary>
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
