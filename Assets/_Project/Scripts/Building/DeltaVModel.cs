using System;
using System.Collections.Generic;
using Galilego.Spacecraft;

namespace Galilego.Building
{
    /// <summary>
    /// Conditions under which ΔV is computed: the body (surface g for TWR) and
    /// engine characteristics in those conditions. Currently vacuum only:
    /// vacuum thrust and Isp. Planet/altitude selection is meant to change ONLY
    /// this class: ThrustN / IspS would then account for ambient pressure
    /// (F = F_vac − p·A_nozzle at constant flow, Isp = F / (ṁ·g0)), leaving the
    /// stage math untouched.
    /// </summary>
    public sealed class DvEnvironment
    {
        /// <summary>Standard gravity for Isp, m/s².</summary>
        public const double G0 = 9.80665d;

        /// <summary>Vacuum with no body: g = g0 (for TWR only).</summary>
        public static readonly DvEnvironment Default = new DvEnvironment(null, G0);

        public readonly string BodyName;

        /// <summary>Surface gravity of the body, m/s² (for TWR).</summary>
        public readonly double SurfaceGravity;

        /// <summary>Altitude above the surface, m (has no effect in vacuum).</summary>
        public readonly double AltitudeMeters;

        public DvEnvironment(string bodyName, double surfaceGravity, double altitudeMeters = 0d)
        {
            BodyName = string.IsNullOrEmpty(bodyName) ? "—" : bodyName;
            SurfaceGravity = surfaceGravity > 0d ? surfaceGravity : G0;
            AltitudeMeters = altitudeMeters;
        }

        /// <summary>Conditions label for the UI.</summary>
        public string Label { get { return "вакуум"; } }

        /// <summary>Engine thrust in these conditions, N.</summary>
        public double ThrustN(EngineSpec engine)
        {
            return engine.ThrustVacuumNewtons;
        }

        /// <summary>Engine specific impulse in these conditions, s.</summary>
        public double IspS(EngineSpec engine)
        {
            return engine.IspVacuumSeconds;
        }

        /// <summary>Engine mass flow, kg/s (constant: F_vac / (Isp_vac·g0)).</summary>
        public double MassFlowKgS(EngineSpec engine)
        {
            return engine.ThrustVacuumNewtons / (engine.IspVacuumSeconds * G0);
        }
    }

    /// <summary>Summary for one stage (0 — bottom, burns first).</summary>
    public sealed class DvStage
    {
        public int Index;

        /// <summary>Draft part indices belonging to the stage (bottom-up).</summary>
        public readonly List<int> PartIndices = new List<int>();

        /// <summary>Stage engines: "RS-25 ×2". Empty if there is no engine.</summary>
        public string EngineLabel = string.Empty;

        /// <summary>Stage engine propellant: "LOX/LH2". Empty if there is no engine.</summary>
        public string PropellantLabel = string.Empty;

        public int EngineCount;

        /// <summary>Stage dry mass (metal, engines, separator), kg.</summary>
        public double DryMassKg;

        /// <summary>Propellant loaded in the stage tanks at ignition, kg.</summary>
        public double PropellantMassKg;

        /// <summary>Propellant in the stage tanks NOW, kg (equals loaded before flight; 0 for a jettisoned stage).</summary>
        public double PropellantRemainingKg;

        /// <summary>Propellant that will still burn in this stage, kg.</summary>
        public double BurnedMassKg;

        /// <summary>Vessel mass at stage (ignition), including upper stages, kg.</summary>
        public double StartMassKg;

        /// <summary>Vessel mass at stage burnout (before separation), kg.</summary>
        public double EndMassKg;

        /// <summary>Thrust at ignition, N.</summary>
        public double ThrustN;

        /// <summary>Mass flow at ignition, kg/s.</summary>
        public double MassFlowKgS;

        /// <summary>Effective stage Isp (over all remaining burn), s.</summary>
        public double IspS;

        /// <summary>Burn time (remaining in flight), s.</summary>
        public double BurnTimeS;

        /// <summary>Stage ΔV (remaining in flight), m/s.</summary>
        public double DeltaVMs;

        /// <summary>Planned stage ΔV (full tanks), m/s. Equals DeltaVMs before flight.</summary>
        public double PlanDeltaVMs;

        /// <summary>Planned burn time (full tanks), s.</summary>
        public double PlanBurnTimeS;

        /// <summary>TWR at stage (ignition) and at burnout (relative to the body's surface g).</summary>
        public double TwrStart;
        public double TwrEnd;

        /// <summary>In flight: the stage had planned ΔV, but none remains (the stage may still be on board).</summary>
        public bool IsSpent;

        /// <summary>In flight: the stage is jettisoned and no longer part of the vessel (from the model state).</summary>
        public bool IsJettisoned;

        /// <summary>In flight: the stage that will run/is running (the first one with ΔV left).</summary>
        public bool IsActive;

        public bool HasEngine { get { return EngineCount > 0; } }

        /// <summary>Total mass of the stage itself (without upper stages) at ignition, kg.</summary>
        public double OwnMassKg { get { return DryMassKg + PropellantMassKg; } }

        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>Computation result for the whole vessel.</summary>
    public sealed class DvResult
    {
        public readonly List<DvStage> Stages = new List<DvStage>();
        public double TotalDeltaVMs;
        public double TotalBurnTimeS;

        /// <summary>Total main-assembly mass at launch, kg.</summary>
        public double TotalMassKg;

        /// <summary>Parts not connected to the main assembly (excluded from the computation).</summary>
        public int DetachedParts;

        public DvEnvironment Environment;

        /// <summary>The result is built for flight (from remaining propellant).</summary>
        public bool InFlight;

        /// <summary>
        /// Current vessel mass, kg. By mass (legacy) — the passed-in value;
        /// by model state — Σ (dry + propellant) of non-jettisoned stages.
        /// </summary>
        public double CurrentMassKg;

        /// <summary>Active stage index, −1 if no ΔV remains (flight only).</summary>
        public int ActiveStageIndex = -1;

        /// <summary>
        /// Mass of spent but not yet jettisoned stages below the active one, kg.
        /// ΔV is computed assuming they are jettisoned.
        /// </summary>
        public double AttachedSpentMassKg;

        /// <summary>
        /// (legacy, by mass) How much lighter the vessel got beyond what the
        /// assembly's propellant burn can explain, kg (0 — all explained).
        /// </summary>
        public double UnexplainedMassLossKg;

        /// <summary>Computation notes for the UI (in flight).</summary>
        public readonly List<string> Notes = new List<string>();

        public bool IsEmpty { get { return Stages.Count == 0; } }
    }

    /// <summary>
    /// Pure (no Unity) ΔV computation over stage cores. Input is StageCore
    /// (from DeltaV.BuildStageAssembly or built by hand, e.g. in a test rig),
    /// never a PlacedPart. Cores are NOT modified: the computation works on clones.
    ///
    /// Stage ΔV is the Tsiolkovsky formula over constant-thrust segments (see
    /// StageCore.Burn); mass decreases by the burned amount on each segment:
    ///   ΔV = Σ (F / ṁ) · ln(m_start / m_end).
    /// Mass at stage k's (ignition) = Σ over non-jettisoned j ≥ k (dry_j +
    /// propellant_j): spent stages below the active one are treated as
    /// jettisoned (as in a nominal separation); their not-yet-jettisoned mass is
    /// reported back in AttachedSpentMassKg and a note in Notes.
    /// </summary>
    public static class StageDeltaV
    {
        /// <summary>
        /// ΔV from the cores' current state. inFlight=false — plan (propellant
        /// is treated as fully loaded, jettisoned ignored).
        /// jettisoned[k]=true — stage k is jettisoned: contributes no mass, ΔV 0.
        /// </summary>
        public static DvResult Evaluate(
            DvEnvironment env,
            IReadOnlyList<StageCore> cores,
            IReadOnlyList<bool> jettisoned,
            int detachedParts,
            bool inFlight)
        {
            var result = new DvResult { Environment = env, DetachedParts = detachedParts, InFlight = inFlight };
            if (cores == null || cores.Count == 0)
            {
                return result;
            }

            // Plan — the same cores with full tanks and no jettison.
            DvResult plan = null;
            if (inFlight)
            {
                var planCores = new List<StageCore>(cores.Count);
                for (int k = 0; k < cores.Count; k++)
                {
                    StageCore c = cores[k].Clone();
                    c.FillToStart();
                    planCores.Add(c);
                }

                plan = Evaluate(env, planCores, null, detachedParts, false);
            }

            int n = cores.Count;
            var gone = new bool[n];
            double launch = 0d;
            var work = new StageCore[n];
            for (int k = 0; k < n; k++)
            {
                work[k] = cores[k].Clone();
                if (!inFlight)
                {
                    work[k].FillToStart();
                }

                gone[k] = inFlight && jettisoned != null && k < jettisoned.Count && jettisoned[k];
                launch += work[k].DryMassKg + work[k].StartPropellantKg;
            }

            result.TotalMassKg = launch;

            // "Stage + all upper stages" mass over non-jettisoned stages.
            var above = new double[n + 1];
            for (int k = n - 1; k >= 0; k--)
            {
                above[k] = above[k + 1] + (gone[k] ? 0d : work[k].TotalMassKg);
            }

            result.CurrentMassKg = above[0];
            double g = env.SurfaceGravity;

            for (int k = 0; k < n; k++)
            {
                StageCore core = work[k];
                var stage = new DvStage
                {
                    Index = core.Index,
                    DryMassKg = core.DryMassKg,
                    PropellantMassKg = core.StartPropellantKg,
                    PropellantRemainingKg = gone[k] ? 0d : core.PropellantKg,
                    EngineCount = core.EngineCount,
                    IsJettisoned = gone[k],
                };
                stage.PartIndices.AddRange(core.PartIndices);
                FillLabels(stage, core);

                if (inFlight)
                {
                    stage.PlanDeltaVMs = plan.Stages[k].DeltaVMs;
                    stage.PlanBurnTimeS = plan.Stages[k].BurnTimeS;
                }

                if (gone[k])
                {
                    result.Stages.Add(stage);
                    continue;
                }

                stage.StartMassKg = above[k];

                // For warnings the remainder is the LOADED amount (otherwise a
                // spent stage would complain about missing propellant).
                var basis = new double[core.Pools.Count];
                for (int i = 0; i < basis.Length; i++)
                {
                    basis[i] = inFlight ? core.Pools[i].Kg : core.Pools[i].StartKg;
                }

                StageBurnResult o = core.Burn(above[k], double.PositiveInfinity, true);
                stage.DeltaVMs = o.DeltaV;
                stage.BurnTimeS = o.TimeS;
                stage.EndMassKg = o.EndMassKg;
                stage.BurnedMassKg = o.BurnedKg;
                stage.ThrustN = o.ThrustFirstN;
                stage.MassFlowKgS = o.FlowFirstKgS;
                if (o.Started)
                {
                    stage.TwrStart = o.ThrustFirstN / (above[k] * g);
                    stage.TwrEnd = o.ThrustLastN / (o.EndMassKg * g);
                    double ratio = Math.Log(above[k] / o.EndMassKg);
                    stage.IspS = ratio > 0d ? o.DeltaV / (DvEnvironment.G0 * ratio) : 0d;
                }

                if (inFlight)
                {
                    stage.IsSpent = stage.PlanDeltaVMs > 0d && stage.DeltaVMs < 1e-3d;
                }
                else
                {
                    stage.PlanDeltaVMs = stage.DeltaVMs;
                    stage.PlanBurnTimeS = stage.BurnTimeS;
                }

                if (stage.IsSpent)
                {
                    stage.DeltaVMs = 0d;
                    stage.BurnTimeS = 0d;
                    stage.ThrustN = 0d;
                    stage.TwrStart = 0d;
                    stage.TwrEnd = 0d;
                    stage.IspS = 0d;
                }
                else
                {
                    double basisSum = 0d;
                    for (int i = 0; i < basis.Length; i++)
                    {
                        basisSum += basis[i];
                    }

                    FillWarnings(stage, core, basis, basisSum, o.Started);
                }

                result.Stages.Add(stage);
                result.TotalDeltaVMs += stage.DeltaVMs;
                result.TotalBurnTimeS += stage.BurnTimeS;
            }

            if (inFlight)
            {
                FinishFlight(result, cores, gone);
            }

            return result;
        }

        /// <summary>
        /// ΔV from the state reported by physics: pool remainders and jettisoned
        /// stage flags. States are applied to clones of the cores; a pool without
        /// a record stays loaded. Records with no matching pool in a stage are
        /// counted and reported in Notes (the state is not from this assembly).
        /// </summary>
        public static DvResult EvaluateFromState(
            DvEnvironment env,
            IReadOnlyList<StageCore> freshCores,
            IReadOnlyList<StagePoolState> states,
            IReadOnlyList<bool> jettisoned,
            int detachedParts)
        {
            var cores = new List<StageCore>(freshCores.Count);
            for (int k = 0; k < freshCores.Count; k++)
            {
                StageCore c = freshCores[k].Clone();
                c.FillToStart();
                cores.Add(c);
            }

            int unmatched = 0;
            if (states != null)
            {
                for (int i = 0; i < states.Count; i++)
                {
                    StagePoolState s = states[i];
                    StageCore target = null;
                    for (int k = 0; k < cores.Count; k++)
                    {
                        if (cores[k].Index == s.StageIndex)
                        {
                            target = cores[k];
                        }
                    }

                    if (target == null || !target.ApplyState(s.Component, s.Kg))
                    {
                        unmatched++;
                    }
                }
            }

            DvResult result = Evaluate(env, cores, jettisoned, detachedParts, true);
            if (unmatched > 0)
            {
                result.Notes.Add("Состояние двигательной установки не совпадает со сборкой ("
                    + unmatched + " зап.) — расчёт остатка неточен");
            }

            return result;
        }

        /// <summary>
        /// (legacy) Remaining ΔV by the vessel's current mass: burned = launch
        /// mass − currentMassKg, distributed over the stages in plan order
        /// (bottom burns first). Needed while physics doesn't report model state.
        /// </summary>
        public static DvResult EvaluateByMass(
            DvEnvironment env, IReadOnlyList<StageCore> freshCores, double currentMassKg, int detachedParts)
        {
            var cores = new List<StageCore>(freshCores.Count);
            double launch = 0d;
            for (int k = 0; k < freshCores.Count; k++)
            {
                StageCore c = freshCores[k].Clone();
                c.FillToStart();
                cores.Add(c);
                launch += c.DryMassKg + c.StartPropellantKg;
            }

            double consumed = Math.Max(0d, launch - currentMassKg);
            double budget = consumed;
            for (int k = 0; k < cores.Count && budget > 0d; k++)
            {
                budget -= cores[k].Commit(budget);
            }

            DvResult result = Evaluate(env, cores, null, detachedParts, true);
            result.CurrentMassKg = currentMassKg;

            // Budget left: the vessel is lighter than the assembly's burn can
            // explain (propellant is gone, yet mass keeps dropping) — flight data and assembly disagree.
            if (budget > Math.Max(0.5d, consumed * 1e-4d))
            {
                result.UnexplainedMassLossKg = budget;
                result.Notes.Insert(0, "Масса упала на " + FormatKg(budget)
                    + " больше, чем может сгореть по сборке — расчёт остатка неточен");
            }

            return result;
        }

        private static void FinishFlight(DvResult result, IReadOnlyList<StageCore> cores, bool[] gone)
        {
            for (int k = 0; k < result.Stages.Count; k++)
            {
                if (!result.Stages[k].IsJettisoned && result.Stages[k].DeltaVMs > 0d)
                {
                    result.ActiveStageIndex = k;
                    result.Stages[k].IsActive = true;
                    break;
                }
            }

            // Spent stages below the active one are still on the vessel (not jettisoned).
            int active = result.ActiveStageIndex >= 0 ? result.ActiveStageIndex : result.Stages.Count;
            double attached = 0d;
            for (int k = 0; k < active && k < cores.Count; k++)
            {
                if (!gone[k])
                {
                    attached += result.Stages[k].DryMassKg + result.Stages[k].PropellantRemainingKg;
                }
            }

            result.AttachedSpentMassKg = attached;
            if (result.ActiveStageIndex >= 0 && attached > 1d)
            {
                result.Notes.Add("Выработанные ступени (" + FormatKg(attached)
                    + ") ещё на борту: ΔV посчитана для отделённых");
            }
        }

        // ─── Labels and warnings ────────────────────────────────────────

        private static void FillLabels(DvStage stage, StageCore core)
        {
            var names = new List<string>();
            var fuels = new List<string>();
            for (int e = 0; e < core.Engines.Count; e++)
            {
                StageEngineGroup eg = core.Engines[e];
                names.Add(eg.Count > 1 ? eg.Name + " ×" + eg.Count : eg.Name);
                if (eg.Mix != null)
                {
                    string pair = eg.Mix.Oxidizer.Id + "/" + eg.Mix.Fuel.Id;
                    if (!fuels.Contains(pair))
                    {
                        fuels.Add(pair);
                    }
                }
            }

            stage.EngineLabel = string.Join(", ", names);
            stage.PropellantLabel = string.Join(", ", fuels);

            // Stage without an engine: show what the tanks are loaded with (if anything).
            if (core.EngineCount == 0 && core.Pools.Count > 0)
            {
                var ids = new List<string>();
                for (int i = 0; i < core.Pools.Count; i++)
                {
                    ids.Add(core.Pools[i].Component.Id);
                }

                stage.PropellantLabel = string.Join("/", ids);
            }
        }

        /// <summary>
        /// core is the core AFTER the burn (Kg = what won't burn); basis[i] is the
        /// amount pool i held at the start (loaded/remainder); basisSum is the sum.
        /// </summary>
        private static void FillWarnings(DvStage stage, StageCore core, double[] basis, double basisSum, bool burned)
        {
            if (core.Engines.Count == 0)
            {
                stage.Warnings.Add(basisSum > 1d
                    ? "Нет двигателя — топливо ступени не используется"
                    : "Нет двигателя — ступень не разгоняет");
                return;
            }

            // What the engine lacks from the very start.
            for (int e = 0; e < core.Engines.Count; e++)
            {
                StageEngineGroup eg = core.Engines[e];
                if (eg.Mix == null)
                {
                    stage.Warnings.Add(eg.Name + ": нет данных о топливе");
                    continue;
                }

                int ox = IndexOf(core, eg.Mix.Oxidizer);
                int fuel = IndexOf(core, eg.Mix.Fuel);
                string missing = string.Empty;
                if (ox < 0 || basis[ox] <= StageCore.Eps)
                {
                    missing = eg.Mix.Oxidizer.Id;
                }

                if (fuel < 0 || basis[fuel] <= StageCore.Eps)
                {
                    missing += (missing.Length > 0 ? " и " : string.Empty) + eg.Mix.Fuel.Id;
                }

                if (missing.Length > 0)
                {
                    stage.Warnings.Add(eg.Name + ": нет топлива " + missing);
                }
            }

            // Propellant that fits none of the stage's engines.
            var foreign = new List<string>();
            for (int i = 0; i < core.Pools.Count; i++)
            {
                if (basis[i] <= StageCore.Eps)
                {
                    continue;
                }

                bool used = false;
                for (int e = 0; e < core.Engines.Count; e++)
                {
                    BipropellantMix mix = core.Engines[e].Mix;
                    if (mix != null && (mix.Oxidizer == core.Pools[i].Component || mix.Fuel == core.Pools[i].Component))
                    {
                        used = true;
                    }
                }

                if (!used)
                {
                    foreign.Add(core.Pools[i].Component.Id);
                }
            }

            if (foreign.Count > 0)
            {
                stage.Warnings.Add("Топливо " + string.Join(", ", foreign) + " не подходит двигателю");
            }

            // Remainder after the burn: loaded off the engine's ratio.
            double left = core.PropellantKg;
            if (burned && foreign.Count == 0 && left > Math.Max(1d, basisSum * 0.005d))
            {
                stage.Warnings.Add("Не сгорит " + FormatKg(left) + " топлива — баки не в пропорцию двигателя");
            }
        }

        private static int IndexOf(StageCore core, Propellant component)
        {
            for (int i = 0; i < core.Pools.Count; i++)
            {
                if (core.Pools[i].Component == component)
                {
                    return i;
                }
            }

            return -1;
        }

        public static string FormatKg(double kg)
        {
            return kg >= 1000d ? (kg / 1000d).ToString("0.0") + " т" : kg.ToString("0") + " кг";
        }
    }
}
