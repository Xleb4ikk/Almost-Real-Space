using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Spacecraft;

namespace Galilego.Events
{
    /// <summary>
    /// Separation spec for one part: DATA, not a spawn. Position is world
    /// (impact point projected + ε along the normal; all specs at one point is
    /// valid, spreading is the assembly system's job). Velocity is stored in
    /// TWO spaces at once so they cannot be confused:
    /// FragmentVelocity — relative f_i (for the momentum invariant),
    /// Velocity — world v_surface + f_i (for spawning). Converting one into the
    /// other inside the spec is forbidden: the model constructor is the source of truth.
    /// </summary>
    public readonly struct PartSeparationSpec
    {
        public readonly int PartIndex;
        public readonly double Mass;
        public readonly Vector3d Position;
        public readonly Vector3d Velocity;
        public readonly Vector3d FragmentVelocity;

        public PartSeparationSpec(int partIndex, double mass, Vector3d position, Vector3d velocity, Vector3d fragmentVelocity)
        {
            PartIndex = partIndex;
            Mass = mass;
            Position = position;
            Velocity = velocity;
            FragmentVelocity = fragmentVelocity;
        }
    }

    /// <summary>
    /// Breakup model input. ImpactVelocity is strictly RELATIVE
    /// (ship minus surface point); mixing it with the world one is forbidden by
    /// usage: the model adds SurfacePointVelocity itself, exactly once, in one place.
    /// </summary>
    public readonly struct BreakupInput
    {
        public readonly double ShipMass;
        public readonly Vector3d ImpactVelocity;
        public readonly Vector3d SurfacePointVelocity;
        public readonly Vector3d SurfacePoint;
        public readonly Vector3d SurfaceNormal;
        public readonly double SpawnEpsilonMeters;

        /// <summary>
        /// Assembly parts. null/empty = degenerate whole-ship case:
        /// SingleThresholdBreakup divides the mass itself and does not read the
        /// list. JointedBreakup takes the real joints from here.
        /// </summary>
        public readonly IReadOnlyList<Part> Parts;

        public BreakupInput(
            double shipMass, Vector3d impactVelocity, Vector3d surfacePointVelocity,
            Vector3d surfacePoint, Vector3d surfaceNormal, double spawnEpsilonMeters,
            IReadOnlyList<Part> parts = null)
        {
            ShipMass = shipMass;
            ImpactVelocity = impactVelocity;
            SurfacePointVelocity = surfacePointVelocity;
            SurfacePoint = surfacePoint;
            SurfaceNormal = surfaceNormal;
            SpawnEpsilonMeters = spawnEpsilonMeters;
            Parts = parts;
        }
    }

    /// <summary>
    /// Breakup model contract. The number of parts is NOT prescribed: that is up
    /// to the implementation (JointedBreakup takes N from the joint list).
    /// Mathematical output invariants (enforced by exceptions, not words):
    ///   Σ m_i = m_ship,
    ///   Σ m_i·f_i = m_ship·v_impact   (corollary: Σ m_i·v_i = m_ship·v_ship).
    /// </summary>
    public interface IBreakupModel
    {
        IReadOnlyList<PartSeparationSpec> PlanBreakup(BreakupInput input);
    }

    /// <summary>
    /// Whole-ship as one "part" split into N fragments. N=10 is a private constant
    /// of THIS implementation; the IBreakupModel contract does not fix the count.
    /// The spread is deterministic (Fibonacci sphere × SpreadFactor·|v_impact|),
    /// centred by a MASS-WEIGHTED mean (matches the plain mean for equal masses, but
    /// the contract is written for unequal parts):
    ///   f_i = v_impact + s_i,  s_i ← s_i − (Σ m_j·s_j)/(Σ m_j).
    /// Invariants are checked with a loud exception, not a comment.
    /// </summary>
    /// <summary>
    /// Breakup at assembly joints (parts + joint strength from Parts).
    /// The model is impulsive and deterministic:
    ///   v_n  — normal component of the RELATIVE impact velocity;
    ///   load = m_part·|v_n|/PulseSeconds — impulsive overload on the joint
    ///          (characteristic damping time PulseSeconds);
    ///   a joint holds ⟺ load ≤ JointStrengthNewtons. Overloaded parts
    ///   detach entirely, survivors stay the ship (Landed).
    ///
    /// Detached-part bounce: the normal component is reflected with
    /// SurfaceRestitution ("they don't bounce much"), the tangential one is damped
    /// by TangentialDampFactor for ALL fragments, plus a deterministic spread in
    /// the TANGENTIAL plane (golden angle × SpreadFactor·|v_n|).
    /// Momentum bookkeeping (checked loudly, as in SingleThreshold):
    ///   The TANGENTIAL invariant is exact: Σ m_i·f_i^t = m_ship·v_t
    ///   (at the instant of contact, with no external tangential impulse; ground
    ///   friction damps the velocity afterwards).
    ///   The NORMAL component is NOT conserved by construction: there is an external
    ///   ground impulse (damping the press-in) + the reflected part bounce — a
    ///   deliberate difference from SingleThreshold's free break-up.
    /// An empty spec list = the joints survived the impact → landing with damage.
    /// input.Parts null/empty → the degenerate whole-ship SingleThreshold path.
    /// </summary>
    public sealed class JointedBreakup : IBreakupModel
    {
        private const double GoldenAngle = 2.3999632297286535d;

        /// <summary>Characteristic impact damping time (s) for joint overload.</summary>
        public double PulseSeconds = 0.05d;

        /// <summary>Restitution coefficient for the normal velocity of detached parts.</summary>
        public double SurfaceRestitution = 0.2d;

        /// <summary>Factor by which the tangential velocity of broken-off parts is damped.</summary>
        public double TangentialDampFactor = 0.5d;

        /// <summary>Spread of part velocities (fraction of |v_n|).</summary>
        public double SpreadFactor = 0.3d;

        private readonly SingleThresholdBreakup fallback = new SingleThresholdBreakup();

        public IReadOnlyList<PartSeparationSpec> PlanBreakup(BreakupInput input)
        {
            if (input.ShipMass <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(input), "Масса корабля обязана быть положительной.");
            }

            if (input.Parts == null || input.Parts.Count == 0)
            {
                return fallback.PlanBreakup(input);
            }

            Vector3d normal = input.SurfaceNormal.Normalized;
            double normalSpeed = Vector3d.Dot(input.ImpactVelocity, normal);
            Vector3d tangential = input.ImpactVelocity - (normal * normalSpeed);

            // Effective masses: parts are normalised to the actual ship mass
            // (fuel burns off, so the assembly mass drifts from the parts sum).
            double partsMass = 0d;
            for (int i = 0; i < input.Parts.Count; i++)
            {
                partsMass += input.Parts[i].MassKg;
            }

            if (partsMass <= 0d)
            {
                throw new InvalidOperationException("JointedBreakup: суммарная масса деталей обязана быть положительной.");
            }

            double scale = input.ShipMass / partsMass;
            var effective = new double[input.Parts.Count];
            for (int i = 0; i < input.Parts.Count; i++)
            {
                effective[i] = input.Parts[i].MassKg * scale;
            }

            // Overload on the joint: the part's impulse divided by the damping time.
            double pulse = PulseSeconds > 0d ? PulseSeconds : throw new InvalidOperationException("JointedBreakup: PulseSeconds обязан быть положительным.");
            double loadPerKg = Math.Abs(normalSpeed) / pulse;
            var broken = new List<int>();
            double survivorsMass = 0d;
            for (int i = 0; i < input.Parts.Count; i++)
            {
                double load = effective[i] * loadPerKg;
                if (load > input.Parts[i].JointStrengthNewtons)
                {
                    broken.Add(i);
                }
                else
                {
                    survivorsMass += effective[i];
                }
            }

            if (broken.Count == 0)
            {
                // Joints hold: landing with damage, no specs.
                return new PartSeparationSpec[0];
            }

            Vector3d survivorVelocity = tangential * (1d - TangentialDampFactor);
            double brokenMass = input.ShipMass - survivorsMass;

            // Mean tangential velocity of broken-off parts — from the exact
            // tangential invariant: Σ m_i·f_i^t = m_ship·v_t − m_surv·f_surv^t.
            Vector3d brokenMeanTangent = ((tangential * input.ShipMass) - (survivorVelocity * survivorsMass)) / brokenMass;

            // Deterministic spread in the TANGENTIAL plane (golden angle),
            // centred to zero by a weighted correction — leaves the normal untouched.
            double spreadSpeed = SpreadFactor * Math.Abs(normalSpeed);
            Vector3d t1 = Math.Abs(normal.X) < 0.9d ? new Vector3d(1d, 0d, 0d) : new Vector3d(0d, 1d, 0d);
            t1 = (t1 - (normal * Vector3d.Dot(t1, normal))).Normalized;
            Vector3d t2 = Vector3d.Cross(normal, t1);
            var scatter = new Vector3d[broken.Count];
            Vector3d weightedMean = Vector3d.Zero;
            for (int i = 0; i < broken.Count; i++)
            {
                double phi = i * GoldenAngle;
                scatter[i] = (t1 * (Math.Cos(phi) * spreadSpeed)) + (t2 * (Math.Sin(phi) * spreadSpeed));
                weightedMean += scatter[i] * effective[broken[i]];
            }

            weightedMean = weightedMean / brokenMass;

            var specs = new List<PartSeparationSpec>(broken.Count);
            Vector3d checkMomentum = Vector3d.Zero;
            double checkMass = 0d;
            for (int i = 0; i < broken.Count; i++)
            {
                int partIndex = broken[i];
                Vector3d fragmentVelocity = brokenMeanTangent
                    + (normal * (-normalSpeed * SurfaceRestitution))
                    + (scatter[i] - weightedMean);
                specs.Add(new PartSeparationSpec(
                    partIndex, effective[partIndex],
                    input.SurfacePoint + (normal * input.SpawnEpsilonMeters),
                    input.SurfacePointVelocity + fragmentVelocity,
                    fragmentVelocity));
                checkMass += effective[partIndex];
                Vector3d fragmentTangent = fragmentVelocity - (normal * Vector3d.Dot(fragmentVelocity, normal));
                checkMomentum += fragmentTangent * effective[partIndex];
            }

            double massTolerance = 1e-9d * Math.Max(1d, input.ShipMass);
            if (Math.Abs(checkMass - brokenMass) > massTolerance)
            {
                throw new InvalidOperationException("JointedBreakup: нарушен инвариант Σm_broken = m_ship − m_surv.");
            }

            Vector3d expectTangent = (tangential * input.ShipMass) - (survivorVelocity * survivorsMass);
            double tangentTolerance = 1e-9d * Math.Max(1d, expectTangent.Magnitude);
            if ((checkMomentum - expectTangent).Magnitude > tangentTolerance)
            {
                throw new InvalidOperationException("JointedBreakup: нарушен тангенциальный инвариант Σm_i·f_i^t = m_ship·v_t − m_surv·f_surv^t.");
            }

            return specs;
        }
    }

    /// <summary>
    /// Whole-ship as one "part" split into N fragments. N=10 is a private constant
    /// of THIS implementation; the IBreakupModel contract does not fix the count.
    /// The spread is deterministic (Fibonacci sphere × SpreadFactor·|v_impact|),
    /// centred by a MASS-WEIGHTED mean (matches the plain mean for equal masses, but
    /// the contract is written for unequal parts):
    ///   f_i = v_impact + s_i,  s_i ← s_i − (Σ m_j·s_j)/(Σ m_j).
    /// Invariants are checked with a loud exception, not a comment.
    /// </summary>
    public sealed class SingleThresholdBreakup : IBreakupModel
    {
        private const int FragmentCount = 10;

        private const double GoldenAngle = 2.3999632297286535d;

        public double SpreadFactor = 0.3d;

        public IReadOnlyList<PartSeparationSpec> PlanBreakup(BreakupInput input)
        {
            if (input.ShipMass <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(input), "Масса корабля обязана быть положительной.");
            }

            double fragmentMass = input.ShipMass / FragmentCount;
            double impactSpeed = input.ImpactVelocity.Magnitude;
            double spreadSpeed = SpreadFactor * impactSpeed;

            var spread = new Vector3d[FragmentCount];
            for (int i = 0; i < FragmentCount; i++)
            {
                double y = 1d - (2d * (i + 0.5d) / FragmentCount);
                double r = Math.Sqrt(Math.Max(0d, 1d - (y * y)));
                double phi = i * GoldenAngle;
                spread[i] = new Vector3d(r * Math.Cos(phi) * spreadSpeed, y * spreadSpeed, r * Math.Sin(phi) * spreadSpeed);
            }

            Vector3d weightedMean = Vector3d.Zero;
            for (int i = 0; i < FragmentCount; i++)
            {
                weightedMean += spread[i] * fragmentMass;
            }

            weightedMean = weightedMean / input.ShipMass;

            Vector3d spawnAt = input.SurfacePoint + (input.SurfaceNormal * input.SpawnEpsilonMeters);
            var specs = new List<PartSeparationSpec>(FragmentCount);
            Vector3d checkMomentum = Vector3d.Zero;
            double checkMass = 0d;
            for (int i = 0; i < FragmentCount; i++)
            {
                Vector3d fragmentVelocity = input.ImpactVelocity + (spread[i] - weightedMean);
                specs.Add(new PartSeparationSpec(
                    i, fragmentMass, spawnAt,
                    input.SurfacePointVelocity + fragmentVelocity, fragmentVelocity));
                checkMass += fragmentMass;
                checkMomentum += fragmentVelocity * fragmentMass;
            }

            double massTolerance = 1e-9d * Math.Max(1d, input.ShipMass);
            if (Math.Abs(checkMass - input.ShipMass) > massTolerance)
            {
                throw new InvalidOperationException("Breakup: нарушен инвариант Σm_i = m_ship.");
            }

            Vector3d expectMomentum = input.ImpactVelocity * input.ShipMass;
            double momentumTolerance = 1e-9d * Math.Max(1d, expectMomentum.Magnitude);
            if ((checkMomentum - expectMomentum).Magnitude > momentumTolerance)
            {
                throw new InvalidOperationException("Breakup: нарушен инвариант Σm_i·f_i = m·v_impact.");
            }

            return specs;
        }
    }
}
