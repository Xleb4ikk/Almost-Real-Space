using System;

namespace Galilego.Core
{
    /// <summary>
    /// DOPRI5 stepper for the full spacecraft state (position, velocity, mass).
    /// Uses the SAME DOPRI5Coefficients as OrbitIntegrator: not an alternative
    /// integrator, but the same Dormand-Prince 5(4) method applied to a wider
    /// state vector. OrbitIntegrator.cs is left untouched by this file.
    ///
    /// Step control (adaptive scaling, FSAL, step-update formulas) mirrors
    /// OrbitIntegrator.StepForward. The ONLY semantic difference: the error norm is
    /// computed separately for Position / Velocity / Mass, each with its own
    /// absolute tolerance — metres, m/s and kilograms have different scales, so a
    /// single error number for them is physically meaningless.
    /// </summary>
    public static class SpacecraftOrbitIntegrator
    {
        /// <summary>
        /// Right-hand side of the spacecraft ODE: given the substage state and time,
        /// returns the derivative (dPosition/dt = velocity, dVelocity/dt = acceleration,
        /// dMass/dt = -mass flow). Must be pure (no side effects): it is called many
        /// times per step, including rejected ones, so it must not consume fuel or
        /// modify the spacecraft or its sources.
        /// </summary>
        public delegate SpacecraftIntegrationState DerivativeFunction(
            SpacecraftIntegrationState state, double timeSeconds);

        // Butcher tableau coefficients — the same single source of truth as
        // OrbitIntegrator. No number is duplicated literally here.
        private const double a21 = DOPRI5Coefficients.a21;
        private const double a31 = DOPRI5Coefficients.a31;
        private const double a32 = DOPRI5Coefficients.a32;
        private const double a41 = DOPRI5Coefficients.a41;
        private const double a42 = DOPRI5Coefficients.a42;
        private const double a43 = DOPRI5Coefficients.a43;
        private const double a51 = DOPRI5Coefficients.a51;
        private const double a52 = DOPRI5Coefficients.a52;
        private const double a53 = DOPRI5Coefficients.a53;
        private const double a54 = DOPRI5Coefficients.a54;
        private const double a61 = DOPRI5Coefficients.a61;
        private const double a62 = DOPRI5Coefficients.a62;
        private const double a63 = DOPRI5Coefficients.a63;
        private const double a64 = DOPRI5Coefficients.a64;
        private const double a65 = DOPRI5Coefficients.a65;
        private const double a71 = DOPRI5Coefficients.a71;
        private const double a73 = DOPRI5Coefficients.a73;
        private const double a74 = DOPRI5Coefficients.a74;
        private const double a75 = DOPRI5Coefficients.a75;
        private const double a76 = DOPRI5Coefficients.a76;

        private const double b1 = DOPRI5Coefficients.b1;
        private const double b3 = DOPRI5Coefficients.b3;
        private const double b4 = DOPRI5Coefficients.b4;
        private const double b5 = DOPRI5Coefficients.b5;
        private const double b6 = DOPRI5Coefficients.b6;

        private const double bStar1 = DOPRI5Coefficients.bStar1;
        private const double bStar3 = DOPRI5Coefficients.bStar3;
        private const double bStar4 = DOPRI5Coefficients.bStar4;
        private const double bStar5 = DOPRI5Coefficients.bStar5;
        private const double bStar6 = DOPRI5Coefficients.bStar6;
        private const double bStar7 = DOPRI5Coefficients.bStar7;

        private const double d1 = DOPRI5Coefficients.d1;
        private const double d3 = DOPRI5Coefficients.d3;
        private const double d4 = DOPRI5Coefficients.d4;
        private const double d5 = DOPRI5Coefficients.d5;
        private const double d6 = DOPRI5Coefficients.d6;
        private const double d7 = DOPRI5Coefficients.d7;

        // Step bounds match OrbitIntegrator's so step control behaves identically
        // instead of drifting apart through two sets of numbers.
        private const double MaxStepSize = OrbitIntegrator.DefaultMaxStepSize;
        private const double MinStepSize = OrbitIntegrator.DefaultMinStepSize;

        /// <summary>
        /// Integrates the full state forward by a target time step with adaptive
        /// DOPRI5. Step control matches OrbitIntegrator.StepForward.
        ///
        /// Optional dense-output collector: every accepted internal step appends a
        /// DenseSegment with Shampine coefficients (built from stages already
        /// computed; k7 is the FSAL estimate, so 0 extra RHS evaluations).
        /// segments == null leaves the result bit-identical to the no-collector path.
        /// </summary>
        public static SpacecraftIntegrationResult StepForward(
            SpacecraftIntegrationState initialState,
            double currentTimeSeconds,
            double targetDt,
            DerivativeFunction derivativeProvider,
            double positionAbsoluteTolerance,
            double velocityAbsoluteTolerance,
            double massAbsoluteTolerance,
            double relativeTolerance,
            System.Collections.Generic.IList<DenseSegment> segments = null)
        {
            if (derivativeProvider == null)
                throw new ArgumentNullException(nameof(derivativeProvider));

            // Forward-only contract: with targetDt < 0 the RHS stages run forward while
            // the clock runs backward — silent garbage (audit S1/A1). All callers go
            // forward; backward motion fails loudly instead of corrupting silently.
            if (targetDt < 0d)
                throw new ArgumentOutOfRangeException(nameof(targetDt),
                    "Интегратор forward-only: targetDt=" + targetDt + " < 0. Обратный ход времени не поддержан.");

            if (targetDt == 0.0)
                return new SpacecraftIntegrationResult(initialState);

            if (!initialState.IsFinite)
                throw new InvalidOperationException(
                    "Начальное состояние корабля не-конечно: " + initialState + " — дальнейшая интеграция бессмысленна.");

            // FSAL (First Same As Last) init: k7 of an accepted step = k1 of the next.
            // The FULL derivative (including dMass/dt) is cached here, not just the
            // acceleration — otherwise reusing k1 for mass would be wrong.
            SpacecraftIntegrationState fsalDerivative = derivativeProvider(initialState, currentTimeSeconds);
            bool fsalValid = true;

            SpacecraftIntegrationState current = initialState;
            double time = currentTimeSeconds;
            double remainingDt = Math.Abs(targetDt);
            double direction = Math.Sign(targetDt);
            double dt = Math.Min(remainingDt, MaxStepSize);

            while (remainingDt > MinStepSize)
            {
                dt = Math.Min(dt, remainingDt);

                if (!fsalValid)
                {
                    fsalDerivative = derivativeProvider(current, time);
                    fsalValid = true;
                }

                DoPri5Step(
                    current, time,
                    dt,
                    derivativeProvider,
                    fsalDerivative,
                    out SpacecraftIntegrationState fifth,
                    out SpacecraftIntegrationState newFsalDerivative,
                    out double errorPos,
                    out double errorVel,
                    out double errorMass,
                    out ShampineCoeffs denseStep);

                // Separate error scaling: each physical quantity has its own
                // absolute tolerance; the relative tolerance is shared.
                double scalePos = positionAbsoluteTolerance + relativeTolerance * Math.Max(current.Position.Magnitude, fifth.Position.Magnitude);
                double scaleVel = velocityAbsoluteTolerance + relativeTolerance * Math.Max(current.Velocity.Magnitude, fifth.Velocity.Magnitude);
                double scaleMass = massAbsoluteTolerance + relativeTolerance * Math.Max(Math.Abs(current.Mass), Math.Abs(fifth.Mass));
                double normalizedError = Math.Max(errorPos / scalePos, Math.Max(errorVel / scaleVel, errorMass / scaleMass));

                if (normalizedError <= 1.0)
                {
                    if (segments != null)
                    {
                        segments.Add(new DenseSegment(time, time + (dt * direction), dt, current, fifth, denseStep));
                    }

                    current = fifth;
                    time += dt * direction;
                    remainingDt -= dt;

                    if (!current.IsFinite)
                    {
                        throw new InvalidOperationException(
                            "Интегратор получил не-конечное состояние (t=" + time.ToString("R") +
                            ", dt=" + dt.ToString("R") + ") — RHS вернул NaN/Inf (масса ≤ 0? сингулярность?).");
                    }

                    fsalDerivative = newFsalDerivative;
                    fsalValid = true;

                    double stepScale = Math.Min(Math.Max(
                        0.9 * Math.Pow(1.0 / Math.Max(normalizedError, 1e-10), 0.2),
                        0.2), 5.0);
                    dt = Math.Min(Math.Max(dt * stepScale, MinStepSize), MaxStepSize);
                }
                else
                {
                    double rejectScale = Math.Min(Math.Max(
                        0.9 * Math.Pow(1.0 / Math.Max(normalizedError, 1e-10), 0.2),
                        0.1), 0.5);
                    // Force-accept at MinStepSize: the step is recomputed with the REDUCED
                    // dt (as in OrbitIntegrator.StepForward); otherwise the 5th-order result
                    // would be accepted while the dense segment described the old large step.
                    double candidateDt = Math.Max(dt * rejectScale, MinStepSize);
                    if (candidateDt <= MinStepSize)
                    {
                        dt = MinStepSize;
                        DoPri5Step(
                            current, time,
                            dt,
                            derivativeProvider,
                            fsalDerivative,
                            out SpacecraftIntegrationState forced,
                            out SpacecraftIntegrationState forcedFsal,
                            out double forcedErrPos,
                            out double forcedErrVel,
                            out double forcedErrMass,
                            out ShampineCoeffs forcedDense);

                        if (segments != null)
                        {
                            segments.Add(new DenseSegment(time, time + (dt * direction), dt, current, forced, forcedDense));
                        }

                        current = forced;
                        time += dt * direction;
                        remainingDt -= dt;
                        if (!current.IsFinite)
                        {
                            throw new InvalidOperationException(
                                "Интегратор получил не-конечное состояние при force-accept (t=" + time.ToString("R") +
                                ", dt=" + dt.ToString("R") + ") — RHS вернул NaN/Inf.");
                        }

                        fsalDerivative = forcedFsal;
                        fsalValid = true;
                    }
                    else
                    {
                        dt = candidateDt;
                        fsalValid = false;
                    }
                }
            }

            return new SpacecraftIntegrationResult(current);
        }

        /// <summary>Integrates the full state to an exact target time.</summary>
        public static SpacecraftIntegrationResult StepToTime(
            SpacecraftIntegrationState initialState,
            double currentTimeSeconds,
            double targetTimeSeconds,
            DerivativeFunction derivativeProvider,
            double positionAbsoluteTolerance,
            double velocityAbsoluteTolerance,
            double massAbsoluteTolerance,
            double relativeTolerance,
            System.Collections.Generic.IList<DenseSegment> segments = null)
        {
            double dt = targetTimeSeconds - currentTimeSeconds;
            return StepForward(
                initialState, currentTimeSeconds, dt, derivativeProvider,
                positionAbsoluteTolerance, velocityAbsoluteTolerance,
                massAbsoluteTolerance, relativeTolerance, segments);
        }

        /// <summary>
        /// One DOPRI5 step over the full state. Stages and weights match
        /// OrbitIntegrator.DoPri5Step, but the state is wider: each stage k carries
        /// (dPosition/dt, dVelocity/dt, dMass/dt) at once.
        /// </summary>
        private static void DoPri5Step(
            SpacecraftIntegrationState state,
            double time,
            double dt,
            DerivativeFunction derivativeProvider,
            SpacecraftIntegrationState fsalDerivative,
            out SpacecraftIntegrationState fifth,
            out SpacecraftIntegrationState lastDerivative,
            out double errPos,
            out double errVel,
            out double errMass,
            out ShampineCoeffs dense)
        {
            // Stage 1 (FSAL)
            SpacecraftIntegrationState k1 = fsalDerivative;

            // Stage 2
            SpacecraftIntegrationState k2 = derivativeProvider(
                state + k1 * (dt * a21), time + dt * DOPRI5Coefficients.c2);

            // Stage 3
            SpacecraftIntegrationState k3 = derivativeProvider(
                state + k1 * (dt * a31) + k2 * (dt * a32), time + dt * DOPRI5Coefficients.c3);

            // Stage 4
            SpacecraftIntegrationState k4 = derivativeProvider(
                state + k1 * (dt * a41) + k2 * (dt * a42) + k3 * (dt * a43),
                time + dt * DOPRI5Coefficients.c4);

            // Stage 5
            SpacecraftIntegrationState k5 = derivativeProvider(
                state + k1 * (dt * a51) + k2 * (dt * a52) + k3 * (dt * a53) + k4 * (dt * a54),
                time + dt * DOPRI5Coefficients.c5);

            // Stage 6
            SpacecraftIntegrationState k6 = derivativeProvider(
                state + k1 * (dt * a61) + k2 * (dt * a62) + k3 * (dt * a63) + k4 * (dt * a64) + k5 * (dt * a65),
                time + dt * DOPRI5Coefficients.c6);

            // Stage 7 (FSAL candidate: evaluated at t+dt)
            SpacecraftIntegrationState s7 =
                state + k1 * (dt * a71) + k3 * (dt * a73) + k4 * (dt * a74) + k5 * (dt * a75) + k6 * (dt * a76);
            SpacecraftIntegrationState k7 = derivativeProvider(s7, time + dt * DOPRI5Coefficients.c7);

            // 5th-order solution
            fifth = state
                + k1 * (dt * b1) + k3 * (dt * b3) + k4 * (dt * b4) + k5 * (dt * b5) + k6 * (dt * b6);

            // 4th-order solution (for error estimation)
            SpacecraftIntegrationState fourth = state
                + k1 * (dt * bStar1) + k3 * (dt * bStar3) + k4 * (dt * bStar4)
                + k5 * (dt * bStar5) + k6 * (dt * bStar6) + k7 * (dt * bStar7);

            errPos = (fifth.Position - fourth.Position).Magnitude;
            errVel = (fifth.Velocity - fourth.Velocity).Magnitude;
            errMass = Math.Abs(fifth.Mass - fourth.Mass);

            // Shampine dense output (Hairer dopri5.f CONTD5): k7 here is f(t0+h, y1) —
            // exactly the K2 used by the C3/C4 formulas. Always computed (a few vector
            // ops); entries go to a segment only for accepted steps — the caller owns
            // the collector.
            SpacecraftIntegrationState dc1 = fifth - state;
            SpacecraftIntegrationState dc2 = (k1 * dt) - dc1;
            SpacecraftIntegrationState dc3 = dc1 - dc2 - (k7 * dt);
            SpacecraftIntegrationState dc4 =
                ((k1 * d1) + (k3 * d3) + (k4 * d4) + (k5 * d5) + (k6 * d6) + (k7 * d7)) * dt;
            dense = new ShampineCoeffs(dc1, dc2, dc3, dc4);

            // FSAL: k7 of this step = k1 of the next (if the step is accepted)
            lastDerivative = k7;
        }
    }
}
