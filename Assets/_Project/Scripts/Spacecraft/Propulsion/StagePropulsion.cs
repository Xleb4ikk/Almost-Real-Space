using System.Collections.Generic;

namespace Galilego.Spacecraft
{
    /// <summary>Result of pressing "stage" (Space): what the model did.</summary>
    public enum StagingResult
    {
        /// <summary>Nothing: nothing to ignite, or the last stage cannot be jettisoned.</summary>
        Nothing,

        /// <summary>Bottom attached stage ignited (first press).</summary>
        Ignited,

        /// <summary>Bottom attached stage jettisoned, the next one ignited.</summary>
        JettisonedAndIgnited
    }

    /// <summary>
    /// Runtime model of the propulsion system in flight: stage cores (StageCore,
    /// from DeltaV.BuildStageAssembly) plus the state of "what is attached and what
    /// is ignited". Pure (no Unity), ONE for both physics and panel: StageThrustSource
    /// takes the active stage's thrust/flow from here, VesselStep commits the
    /// ACTUAL mass delta of the step here, the panel reads pools and flags through
    /// AppendState / JettisonedFlags.
    ///
    /// Rules:
    ///  • stages jettison bottom-up: attached ones are the suffix [FirstAttached..];
    ///  • ignition only via Space (Stage): the first press ignites the bottom
    ///    attached stage, the next jettisons it and ignites the new bottom one (KSP);
    ///  • AutoIgnite (debug) ignites stages itself, WITHOUT jettison:
    ///    a depleted stage stays aboard as dead mass, the panel honestly
    ///    shows "depleted stages still aboard";
    ///  • propellant is not transferred between stages; flow comes from the cores.
    /// </summary>
    public sealed class StagePropulsion
    {
        private readonly List<StageCore> cores;
        private int firstAttached;
        private int ignited = -1;

        /// <summary>Mass of parts outside the main assembly: present in Ship.Mass, absent from the cores.</summary>
        public double DetachedMassKg { get; private set; }

        /// <summary>Commit the mass of equipment outside the cores (RCS/SAS consumption, jettison).</summary>
        public void ReleaseDetached(double kg)
        {
            DetachedMassKg = System.Math.Max(0d, DetachedMassKg - kg);
            Revision++;
        }

        /// <summary>All stages jettisoned (emergency abort): the remaining ship is the mass outside the cores.</summary>
        public void DetachAllStages(double remainingMassKg)
        {
            firstAttached = cores.Count;
            ignited = -1;
            DetachedMassKg = remainingMassKg;
            Revision++;
        }

        /// <summary>Debug: ignite stages automatically without jettison.</summary>
        public bool AutoIgnite;

        /// <summary>State change counter (the panel recomputes off it).</summary>
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

        /// <summary>Index of the first attached stage; everything below is jettisoned.</summary>
        public int FirstAttached { get { return firstAttached; } }

        /// <summary>Index of the ignited stage, −1 — nothing is burning.</summary>
        public int ActiveStageIndex { get { return ignited; } }

        /// <summary>Core of the active stage; null — nothing to burn.</summary>
        public StageCore ActiveCore
        {
            get { return ignited >= 0 && ignited < cores.Count ? cores[ignited] : null; }
        }

        /// <summary>Thrust of the active stage's running engines at throttle 1.0, N.</summary>
        public double ThrustN
        {
            get
            {
                StageCore core = ActiveCore;
                return core != null ? core.ThrustN : 0d;
            }
        }

        /// <summary>Active stage flow at throttle 1.0, kg/s.</summary>
        public double MassFlowKgS
        {
            get
            {
                StageCore core = ActiveCore;
                return core != null ? core.MassFlowKgS : 0d;
            }
        }

        /// <summary>Mass until the active stage's engine set changes, kg (0 — no thrust).</summary>
        public double SegmentBurnKg
        {
            get
            {
                StageCore core = ActiveCore;
                return core != null ? core.SegmentBurnKg() : 0d;
            }
        }

        /// <summary>Is the stage jettisoned?</summary>
        public bool IsJettisoned(int stage)
        {
            return stage < firstAttached;
        }

        /// <summary>There is a stage above the ignited one (for auto-ignition).</summary>
        public bool CanIgniteNext { get { return ignited + 1 < cores.Count; } }

        /// <summary>Mass of attached stages (dry + propellant now), kg.</summary>
        public double AttachedTotalKg()
        {
            double sum = 0d;
            for (int k = firstAttached; k < cores.Count; k++)
            {
                sum += cores[k].TotalMassKg;
            }

            return sum;
        }

        /// <summary>Total mass of a stage (dry + propellant now), kg.</summary>
        public double StageTotalKg(int stage)
        {
            return stage >= 0 && stage < cores.Count ? cores[stage].TotalMassKg : 0d;
        }

        /// <summary>
        /// Commits the actually burned mass from the active stage (the only
        /// pool mutator). Returns how much was actually committed: less than
        /// requested means propellant ran short, a mass invariant violation
        /// caught by the caller (VesselStep).
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
        /// "Stage" press (Space): first ignites the bottom attached stage;
        /// the next jettisons the bottom attached one and ignites the new bottom.
        /// The last stage cannot be jettisoned (no decoupler above it) — Nothing.
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
        /// Auto-ignition (debug): ignite the next attached stage WITHOUT
        /// jettisoning the depleted one. true — ignited.
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

        /// <summary>State of all pools — for the panel (StageDeltaV.EvaluateFromState).</summary>
        public void AppendState(List<StagePoolState> into)
        {
            for (int k = 0; k < cores.Count; k++)
            {
                cores[k].AppendState(into);
            }
        }

        /// <summary>Jettisoned-stage flags by core index.</summary>
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
