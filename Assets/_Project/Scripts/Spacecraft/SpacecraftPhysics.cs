using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Universe;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Advances Spacecraft through time via SpacecraftOrbitIntegrator (same DOPRI5 as
    /// Core.OrbitIntegrator, but over the full state (position, velocity, mass)).
    /// Sums the contributions of all registered IDynamicsSource: ready accelerations
    /// (gravity) plus forces (thrust, drag) divided by the SUBSTEP mass — once, in one place.
    /// Mass evolves inside the integrator as dm/dt = -flow, in sync with
    /// position/velocity on every RK substage.
    ///
    /// EvaluateDerivative is pure: called many times per step, including rejected
    /// steps, so it only sums contributions and returns the derivative. The ship is
    /// mutated only after an accepted step, in Step.
    /// </summary>
    public sealed class SpacecraftPhysics
    {
        public readonly List<IDynamicsSource> Sources = new List<IDynamicsSource>();

        // Separate tolerances per quantity: position in meters, velocity in m/s,
        // mass in kg — one shared atol for all three would be physically meaningless.
        // Defaults tightened (were 1e-2 m / 1e-5 m/s / 1e-10 on absolute magnitudes): with ToleranceFrameSystem set the
        // relative part is measured against the dominant body, a 400 km LEO coasting for a day drifts ~0.6 m
        // (was ~60 m). At 1x a chunk is a single step regardless of tolerance, so this costs nothing in real time.
        public double PositionAbsoluteTolerance = 1e-4;
        public double VelocityAbsoluteTolerance = 1e-8;
        public double MassAbsoluteTolerance = 1e-4;
        public double RelativeTolerance = 1e-11;

        /// <summary>
        /// Optional: when set, the integrator's error norm is scaled by distance/speed relative to the dominant body
        /// instead of the world-frame magnitudes (see ToleranceReference). null = old absolute scaling (debris, tests).
        /// </summary>
        public StarSystem ToleranceFrameSystem;

        /// <summary>Calls shorter than this are a single step anyway; the dominant-body lookup is skipped.</summary>
        private const double MinSpanForToleranceFrameSeconds = 1d;

        /// <summary>
        /// Snapshot of the total torque (N·m, body frame) of the last substage of the last
        /// ACCEPTED step. The game loop reads it after phys.Step and feeds
        /// AttitudePhysics.Step. NOT the derivative for rotation integration: EvaluateDerivative
        /// is called many times per step (including rejected ones), the snapshot is control only.
        /// </summary>
        public Vector3d TotalTorqueBody { get; private set; }

        private Vector3d lastEvaluatedTorque = Vector3d.Zero;

        /// <summary>
        /// Save/restore of the snapshot around event-driver probe steps, so that
        /// bisection and dense output do not leave a snapshot from the probe state
        /// after Propagate. internal — a driver tool; the game loop reads only
        /// TotalTorqueBody.
        /// </summary>
        internal void SuspendTorqueSnapshot(out Vector3d lastEvaluated, out Vector3d total)
        {
            lastEvaluated = lastEvaluatedTorque;
            total = TotalTorqueBody;
        }

        internal void RestoreTorqueSnapshot(Vector3d lastEvaluated, Vector3d total)
        {
            lastEvaluatedTorque = lastEvaluated;
            TotalTorqueBody = total;
        }

        public void Step(Spacecraft ship, double currentTimeSeconds, double dt)
        {
            StepWithSegments(ship, currentTimeSeconds, dt, null);
        }

        /// <summary>
        /// Same step, plus dense output of accepted internal steps into segments
        /// (null — do not collect, bit-for-bit behavior of Step).
        /// </summary>
        public void StepWithSegments(Spacecraft ship, double currentTimeSeconds, double dt, System.Collections.Generic.IList<DenseSegment> segments)
        {
            var initialState = new SpacecraftIntegrationState
            {
                Position = ship.Position,
                Velocity = ship.Velocity,
                Mass = ship.Mass
            };

            ToleranceReference? toleranceReference = null;
            if (ToleranceFrameSystem != null && dt >= MinSpanForToleranceFrameSeconds && dt <= ToleranceReference.MaxSpanSeconds)
            {
                OrbitingBody dominant = ToleranceFrameSystem.FindDominantBody(ship.Position, currentTimeSeconds);
                ToleranceFrameSystem.EvaluateBodyState(dominant, currentTimeSeconds, out Vector3d bodyPosition, out Vector3d bodyVelocity);
                toleranceReference = new ToleranceReference(bodyPosition, bodyVelocity, currentTimeSeconds);
            }

            SpacecraftIntegrationResult result = SpacecraftOrbitIntegrator.StepForward(
                initialState,
                currentTimeSeconds,
                dt,
                EvaluateDerivative,
                PositionAbsoluteTolerance,
                VelocityAbsoluteTolerance,
                MassAbsoluteTolerance,
                RelativeTolerance,
                segments,
                toleranceReference);

            ship.Position = result.State.Position;
            ship.Velocity = result.State.Velocity;
            ship.Mass = result.State.Mass;
            TotalTorqueBody = lastEvaluatedTorque;
        }

        private SpacecraftIntegrationState EvaluateDerivative(SpacecraftIntegrationState state, double timeSeconds)
        {
            // Mass ≤ 0 or non-finite would give NaN/Inf; fail loudly here.
            if (!(state.Mass > 0d) || !double.IsFinite(state.Mass))
            {
                throw new InvalidOperationException(
                    "Масса корабля " + state.Mass.ToString("R") + " кг на t=" + timeSeconds.ToString("R") +
                    " — интеграция невозможна (источник расходует больше, чем есть?).");
            }

            Vector3d totalSpecificAcceleration = Vector3d.Zero;
            Vector3d totalForce = Vector3d.Zero;
            Vector3d totalTorque = Vector3d.Zero;
            double totalMassFlow = 0d;

            for (int i = 0; i < Sources.Count; i++)
            {
                DynamicsContribution c = Sources[i].Evaluate(state.Position, state.Velocity, state.Mass, timeSeconds);
                totalSpecificAcceleration += c.SpecificAcceleration;
                totalForce += c.Force;
                totalTorque += c.Torque;
                totalMassFlow += c.MassFlow;
            }

            if (!totalForce.IsFinite || !totalSpecificAcceleration.IsFinite || !double.IsFinite(totalMassFlow) || !totalTorque.IsFinite)
            {
                throw new InvalidOperationException(
                    "Источник динамики вернул не-конечный вклад на t=" + timeSeconds.ToString("R") + ".");
            }

            lastEvaluatedTorque = totalTorque;
            Vector3d acceleration = totalSpecificAcceleration + (totalForce / state.Mass);

            return new SpacecraftIntegrationState
            {
                Position = state.Velocity,
                Velocity = acceleration,
                Mass = -totalMassFlow
            };
        }
    }
}
