using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Universe;

namespace Galilego.Spacecraft
{
    public enum SpacecraftIntegrator
    {
        Dop853,
        Dopri5
    }

    /// <summary>
    /// Advances Spacecraft through time via SpacecraftOrbitIntegrator (DOP853 by
    /// default, DOPRI5 as an option) over the full state (position, velocity, mass).
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

        public SpacecraftIntegrator Integrator = SpacecraftIntegrator.Dop853;

        // Separate tolerances per quantity: position in meters, velocity in m/s,
        // mass in kg — one shared atol for all three would be physically meaningless.
        // Defaults tightened (were 1e-2 m / 1e-5 m/s / 1e-10 on absolute magnitudes). With the
        // local-frame step (StepWithSegments, segments == null) a 400 km LEO coasting for a day
        // drifts ~0.1 m; the old absolute path was ~60 m/day, and the 1000-orbit case measured
        // 60.6 m before the local frame, 6.6 m after. At 1x a chunk is a single step regardless
        // of tolerance, so this costs nothing in real time.
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

        private const double MinMassForDerivative = 1e-3d;

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

            // The world state is absolute: near Earth at 1 AU the position update adds
            // ~3e5 m to ~1.5e11 m, so every step drops ~1e-5 m of the increment. Over
            // 1000 orbits that is the measured 60 m drift (and 158 m at t0=1.5e11 for
            // 600 s chunks). Integrating the ship in the dominant body's frame removes
            // the cancellation. Dense segments stay in the absolute frame for now, so
            // the event/render path keeps the old (bit-identical) behaviour.
            // No MinSpanForToleranceFrameSeconds floor here: that constant only
            // guarded the extrapolated error-norm reference. The coordinate
            // transform is needed for real-time 1/60 s frames too, where the
            // absolute update loses the same ~1e-5 m per step.
            bool useLocalFrame = ToleranceFrameSystem != null && dt > 0d;

            if (useLocalFrame)
            {
                int segmentStart = segments != null ? segments.Count : 0;
                OrbitingBody dominant = ToleranceFrameSystem.FindDominantBody(
                    ship.Position, currentTimeSeconds);
                ToleranceFrameSystem.EvaluateBodyState(
                    dominant, currentTimeSeconds,
                    out Vector3d bodyPosition, out Vector3d bodyVelocity);
                double localDelta = currentTimeSeconds - ship.LocalTimeSeconds;
                // The caller owns the clock. A compensated (Kahan) clock can land a few
                // ulps BEFORE currentTime + dt, while the naive t += dt lands after it.
                // Rejecting the negative few-ulp case forced a full absolute->local
                // re-projection every frame, which reintroduced ulp(1.5e11) per step:
                // 1/60 s frames went from 1.2e-2 to 1.0 m/day in E6. Accept a tiny
                // backward epsilon; anything larger is a real time jump and re-projects.
                bool localValid = ship.HasLocalState
                    && ReferenceEquals(ship.LocalBody, dominant)
                    && localDelta >= -1e-6d && localDelta <= 1e-3d
                    && ship.Position.X == ship.LocalAbsolutePosition.X
                    && ship.Position.Y == ship.LocalAbsolutePosition.Y
                    && ship.Position.Z == ship.LocalAbsolutePosition.Z
                    && ship.Velocity.X == ship.LocalAbsoluteVelocity.X
                    && ship.Velocity.Y == ship.LocalAbsoluteVelocity.Y
                    && ship.Velocity.Z == ship.LocalAbsoluteVelocity.Z;
                if (!localValid)
                {
                    ship.LocalPosition = ship.Position - bodyPosition;
                    ship.LocalVelocity = ship.Velocity - bodyVelocity;
                    ship.LocalBody = dominant;
                    ship.LocalTimeSeconds = currentTimeSeconds;
                }
                else if (localDelta > 0d)
                {
                    // The next call can start a few ulps later than the previous end
                    // (Kahan time). Advance the relative state instead of re-projecting
                    // absolute coordinates; this is what keeps ulp(1.5e11) out of the
                    // chunk boundary.
                    ship.LocalPosition += ship.LocalVelocity * localDelta;
                    ship.Position = bodyPosition + ship.LocalPosition;
                    ship.Velocity = bodyVelocity + ship.LocalVelocity;
                    ship.LocalAbsolutePosition = ship.Position;
                    ship.LocalAbsoluteVelocity = ship.Velocity;
                    ship.LocalTimeSeconds = currentTimeSeconds;
                }
                else if (localDelta < 0d)
                {
                    // A few-ulp backward epsilon: keep the local state, only refresh the
                    // absolute projection at the caller's time.
                    ship.Position = bodyPosition + ship.LocalPosition;
                    ship.Velocity = bodyVelocity + ship.LocalVelocity;
                    ship.LocalAbsolutePosition = ship.Position;
                    ship.LocalAbsoluteVelocity = ship.Velocity;
                    ship.LocalTimeSeconds = currentTimeSeconds;
                }

                var localStart = new SpacecraftIntegrationState
                {
                    Position = ship.LocalPosition,
                    Velocity = ship.LocalVelocity,
                    Mass = ship.Mass
                };
                SpacecraftOrbitIntegrator.DerivativeFunction localDerivative =
                    (state, time) => EvaluateLocalDerivative(state, time, dominant);
                SpacecraftIntegrationResult localResult = Integrator == SpacecraftIntegrator.Dopri5
                    ? SpacecraftOrbitIntegrator.StepForward(
                        localStart, currentTimeSeconds, dt, localDerivative,
                        PositionAbsoluteTolerance, VelocityAbsoluteTolerance,
                        MassAbsoluteTolerance, RelativeTolerance, segments, null)
                    : SpacecraftOrbitIntegrator.StepForwardDop853(
                        localStart, currentTimeSeconds, dt, localDerivative,
                        PositionAbsoluteTolerance, VelocityAbsoluteTolerance,
                        MassAbsoluteTolerance, RelativeTolerance, segments, null);

                if (segments != null)
                {
                    for (int i = segmentStart; i < segments.Count; i++)
                    {
                        segments[i] = segments[i].AsAbsolute(dominant);
                    }
                }

                ToleranceFrameSystem.EvaluateBodyState(
                    dominant, currentTimeSeconds + dt,
                    out Vector3d bodyEndPosition, out Vector3d bodyEndVelocity);
                ship.LocalPosition = localResult.State.Position;
                ship.LocalVelocity = localResult.State.Velocity;
                ship.Position = bodyEndPosition + ship.LocalPosition;
                ship.Velocity = bodyEndVelocity + ship.LocalVelocity;
                ship.Mass = localResult.State.Mass;
                ship.LocalAbsolutePosition = ship.Position;
                ship.LocalAbsoluteVelocity = ship.Velocity;
                ship.LocalBody = dominant;
                ship.LocalTimeSeconds = currentTimeSeconds + dt;
                ship.HasLocalState = true;
                UpdateTorqueSnapshot(ship, currentTimeSeconds + dt);
                return;
            }

            ship.HasLocalState = false;

            ToleranceReference? toleranceReference = null;
            if (ToleranceFrameSystem != null && dt >= MinSpanForToleranceFrameSeconds && dt <= ToleranceReference.MaxSpanSeconds)
            {
                OrbitingBody dominant = ToleranceFrameSystem.FindDominantBody(ship.Position, currentTimeSeconds);
                ToleranceFrameSystem.EvaluateBodyState(dominant, currentTimeSeconds, out Vector3d bodyPosition, out Vector3d bodyVelocity);
                toleranceReference = new ToleranceReference(bodyPosition, bodyVelocity, currentTimeSeconds);
            }

            SpacecraftIntegrationResult result = Integrator == SpacecraftIntegrator.Dopri5
                ? SpacecraftOrbitIntegrator.StepForward(
                    initialState, currentTimeSeconds, dt, EvaluateDerivative,
                    PositionAbsoluteTolerance, VelocityAbsoluteTolerance,
                    MassAbsoluteTolerance, RelativeTolerance, segments, toleranceReference)
                : SpacecraftOrbitIntegrator.StepForwardDop853(
                    initialState, currentTimeSeconds, dt, EvaluateDerivative,
                    PositionAbsoluteTolerance, VelocityAbsoluteTolerance,
                    MassAbsoluteTolerance, RelativeTolerance, segments, toleranceReference);

            ship.Position = result.State.Position;
            ship.Velocity = result.State.Velocity;
            ship.Mass = result.State.Mass;
            UpdateTorqueSnapshot(ship, currentTimeSeconds + dt);
        }

        /// <summary>
        /// The torque snapshot must belong to the ACCEPTED state. The last RHS stage
        /// of a DOP853 step is a stage state (and the dense path adds kEnd), so two
        /// equivalent paths could leave different snapshots. One evaluation at the
        /// accepted state makes the snapshot path-independent and is what attitude
        /// control actually reads.
        /// </summary>
        internal void RefreshTorqueSnapshot(Spacecraft ship, double timeSeconds)
        {
            UpdateTorqueSnapshot(ship, timeSeconds);
        }

        private void UpdateTorqueSnapshot(Spacecraft ship, double timeSeconds)
        {
            var state = new SpacecraftIntegrationState
            {
                Position = ship.Position,
                Velocity = ship.Velocity,
                Mass = ship.Mass
            };
            EvaluateDerivative(state, timeSeconds);
            TotalTorqueBody = lastEvaluatedTorque;
        }

        private SpacecraftIntegrationState EvaluateLocalDerivative(
            SpacecraftIntegrationState local, double timeSeconds, OrbitingBody dominant)
        {
            ToleranceFrameSystem.EvaluateBodyState(
                dominant, timeSeconds,
                out Vector3d bodyPosition, out Vector3d bodyVelocity);
            var absolute = new SpacecraftIntegrationState
            {
                Position = bodyPosition + local.Position,
                Velocity = bodyVelocity + local.Velocity,
                Mass = local.Mass
            };
            SpacecraftIntegrationState derivative = EvaluateDerivative(absolute, timeSeconds);
            // Replace the absolute-frame gravity term with the relative-vector field.
            // The force model is the only specific source (CachedGravitySource); drag
            // and thrust enter as Force and stay untouched.
            Vector3d absoluteGravity = ToleranceFrameSystem.EvaluateShipAccelerationCached(
                absolute.Position, timeSeconds);
            Vector3d localGravity = ToleranceFrameSystem.EvaluateShipAccelerationLocal(
                local.Position, dominant, timeSeconds);
            return new SpacecraftIntegrationState
            {
                Position = local.Velocity,
                Velocity = derivative.Velocity - absoluteGravity + localGravity,
                Mass = derivative.Mass
            };
        }

        private SpacecraftIntegrationState EvaluateDerivative(SpacecraftIntegrationState state, double timeSeconds)
        {
            if (!double.IsFinite(state.Mass))
            {
                throw new InvalidOperationException(
                    "Масса корабля " + state.Mass.ToString("R") + " кг на t=" + timeSeconds.ToString("R") +
                    " — интеграция невозможна (источник расходует больше, чем есть?).");
            }

            double mass = state.Mass > MinMassForDerivative ? state.Mass : MinMassForDerivative;
            Vector3d totalSpecificAcceleration = Vector3d.Zero;
            Vector3d totalForce = Vector3d.Zero;
            Vector3d totalTorque = Vector3d.Zero;
            double totalMassFlow = 0d;

            for (int i = 0; i < Sources.Count; i++)
            {
                DynamicsContribution c = Sources[i].Evaluate(state.Position, state.Velocity, mass, timeSeconds);
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

            Vector3d acceleration = totalSpecificAcceleration + (totalForce / mass);
            if (!acceleration.IsFinite)
            {
                throw new InvalidOperationException(
                    "Источник динамики дал не-конечное ускорение на t=" + timeSeconds.ToString("R") + ".");
            }

            lastEvaluatedTorque = totalTorque;

            return new SpacecraftIntegrationState
            {
                Position = state.Velocity,
                Velocity = acceleration,
                Mass = -totalMassFlow
            };
        }
    }
}
