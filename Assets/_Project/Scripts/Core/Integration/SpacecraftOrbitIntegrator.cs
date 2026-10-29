using System;

namespace Galilego.Core
{
    /// <summary>
    /// DOPRI5/DOP853 steppers for the full spacecraft state (position, velocity, mass).
    /// Uses the SAME DOPRI5Coefficients as OrbitIntegrator: not an alternative
    /// integrator, but the same Dormand-Prince 5(4) method applied to a wider
    /// state vector. OrbitIntegrator.cs is left untouched by this file.
    ///
    /// Step control (adaptive scaling, FSAL, step-update formulas) mirrors
    /// OrbitIntegrator.StepForward. The ONLY semantic difference: the error norm is
    /// computed separately for Position / Velocity / Mass, each with its own
    /// absolute tolerance вЂ” metres, m/s and kilograms have different scales, so a
    /// single error number for them is physically meaningless.
    /// </summary>
    /// <summary>
    /// Frame of the dominant body, used ONLY to scale the step-control error norm (never the dynamics).
    /// The integrator state lives in the world frame (1.5e11 m from the star at 1 AU), so rtolВ·|position| became
    /// a ~15 m tolerance for an orbit around a planet whose own radius is 7e6 m. Scaling by the distance and
    /// speed relative to the dominant body makes rtol mean what it says locally. The body is extrapolated
    /// linearly from the chunk start (fine for a scale factor over a few hours); a floating-point floor keeps the
    /// scale above the rounding noise of far-from-origin coordinates (otherwise a ship next to Neptune would
    /// get its steps rejected forever).
    /// </summary>
    public readonly struct ToleranceReference
    {
        /// <summary>Longer calls fall back to the absolute (world-frame) scale: linear extrapolation is only valid for short spans.</summary>
        public const double MaxSpanSeconds = 7200d;

        /// <summary>16 В· machine epsilon, applied to the absolute magnitudes of position and velocity.</summary>
        public const double FloatingPointFloor = 16d * 2.220446049250313e-16;

        public readonly Vector3d Position;
        public readonly Vector3d Velocity;
        public readonly double TimeSeconds;

        public ToleranceReference(Vector3d position, Vector3d velocity, double timeSeconds)
        {
            Position = position;
            Velocity = velocity;
            TimeSeconds = timeSeconds;
        }

        public Vector3d PositionAt(double timeSeconds)
        {
            return Position + (Velocity * (timeSeconds - TimeSeconds));
        }
    }

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

        // Butcher tableau coefficients вЂ” the same single source of truth as
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

        private static void EnsurePositiveMass(SpacecraftIntegrationState state, double time)
        {
            if (!(state.Mass > 0d))
            {
                throw new InvalidOperationException(
                    "Масса корабля " + state.Mass.ToString("R") + " кг на t=" + time.ToString("R") +
                    " — расход превышает запас.");
            }
        }

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
            System.Collections.Generic.IList<DenseSegment> segments = null,
            ToleranceReference? toleranceReference = null)
        {
            if (derivativeProvider == null)
                throw new ArgumentNullException(nameof(derivativeProvider));

            // Forward-only contract: with targetDt < 0 the RHS stages run forward while
            // the clock runs backward вЂ” silent garbage (audit S1/A1). All callers go
            // forward; backward motion fails loudly instead of corrupting silently.
            if (targetDt < 0d)
                throw new ArgumentOutOfRangeException(nameof(targetDt),
                    "РРЅС‚РµРіСЂР°С‚РѕСЂ forward-only: targetDt=" + targetDt + " < 0. РћР±СЂР°С‚РЅС‹Р№ С…РѕРґ РІСЂРµРјРµРЅРё РЅРµ РїРѕРґРґРµСЂР¶Р°РЅ.");

            if (targetDt == 0.0)
                return new SpacecraftIntegrationResult(initialState);

            if (!initialState.IsFinite)
                throw new InvalidOperationException(
                    "РќР°С‡Р°Р»СЊРЅРѕРµ СЃРѕСЃС‚РѕСЏРЅРёРµ РєРѕСЂР°Р±Р»СЏ РЅРµ-РєРѕРЅРµС‡РЅРѕ: " + initialState + " вЂ” РґР°Р»СЊРЅРµР№С€Р°СЏ РёРЅС‚РµРіСЂР°С†РёСЏ Р±РµСЃСЃРјС‹СЃР»РµРЅРЅР°.");

            // FSAL (First Same As Last) init: k7 of an accepted step = k1 of the next.
            // The FULL derivative (including dMass/dt) is cached here, not just the
            // acceleration вЂ” otherwise reusing k1 for mass would be wrong.
            SpacecraftIntegrationState fsalDerivative = derivativeProvider(initialState, currentTimeSeconds);
            bool fsalValid = true;

            SpacecraftIntegrationState current = initialState;
            double time = currentTimeSeconds;
            double remainingDt = Math.Abs(targetDt);
            double direction = Math.Sign(targetDt);
            double dt = Math.Min(remainingDt, MaxStepSize);
            bool useReference = toleranceReference.HasValue && remainingDt <= ToleranceReference.MaxSpanSeconds;
            ToleranceReference reference = useReference ? toleranceReference.Value : default;

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
                    out bool stageMassInvalid,
                    out SpacecraftIntegrationState fifth,
                    out SpacecraftIntegrationState newFsalDerivative,
                    out double errorPos,
                    out double errorVel,
                    out double errorMass,
                    out ShampineCoeffs denseStep);

                if (stageMassInvalid)
                {
                    if (dt <= MinStepSize)
                    {
                        throw new InvalidOperationException(
                            "DOPRI5 не смог подобрать шаг: масса на пробной стадии ≤ 0 при t=" +
                            time.ToString("R") + ".");
                    }

                    dt = Math.Max(MinStepSize, dt * 0.5d);
                    continue;
                }

                // Separate error scaling: each physical quantity has its own
                // absolute tolerance; the relative tolerance is shared.
                double scalePos;
                double scaleVel;
                if (useReference)
                {
                    // Local scale: distance/speed relative to the dominant body + a rounding-noise floor.
                    Vector3d refNow = reference.PositionAt(time);
                    Vector3d refNext = reference.PositionAt(time + (dt * direction));
                    scalePos = positionAbsoluteTolerance
                        + (relativeTolerance * Math.Max((current.Position - refNow).Magnitude, (fifth.Position - refNext).Magnitude))
                        + (ToleranceReference.FloatingPointFloor * Math.Max(current.Position.Magnitude, fifth.Position.Magnitude));
                    scaleVel = velocityAbsoluteTolerance
                        + (relativeTolerance * Math.Max((current.Velocity - reference.Velocity).Magnitude, (fifth.Velocity - reference.Velocity).Magnitude))
                        + (ToleranceReference.FloatingPointFloor * Math.Max(current.Velocity.Magnitude, fifth.Velocity.Magnitude));
                }
                else
                {
                    scalePos = positionAbsoluteTolerance + relativeTolerance * Math.Max(current.Position.Magnitude, fifth.Position.Magnitude);
                    scaleVel = velocityAbsoluteTolerance + relativeTolerance * Math.Max(current.Velocity.Magnitude, fifth.Velocity.Magnitude);
                }

                double scaleMass = massAbsoluteTolerance + relativeTolerance * Math.Max(Math.Abs(current.Mass), Math.Abs(fifth.Mass));
                double normalizedError = Math.Max(errorPos / scalePos, Math.Max(errorVel / scaleVel, errorMass / scaleMass));

                if (normalizedError <= 1.0)
                {
                    EnsurePositiveMass(fifth, time + (dt * direction));
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
                            out bool forcedStageMassInvalid,
                            out SpacecraftIntegrationState forced,
                            out SpacecraftIntegrationState forcedFsal,
                            out double forcedErrPos,
                            out double forcedErrVel,
                            out double forcedErrMass,
                            out ShampineCoeffs forcedDense);

                        if (forcedStageMassInvalid)
                        {
                            throw new InvalidOperationException(
                                "DOPRI5 не смог подобрать шаг: масса на пробной стадии ≤ 0 при t=" +
                                time.ToString("R") + ".");
                        }

                        EnsurePositiveMass(forced, time + (dt * direction));
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
                                "РРЅС‚РµРіСЂР°С‚РѕСЂ РїРѕР»СѓС‡РёР» РЅРµ-РєРѕРЅРµС‡РЅРѕРµ СЃРѕСЃС‚РѕСЏРЅРёРµ РїСЂРё force-accept (t=" + time.ToString("R") +
                                ", dt=" + dt.ToString("R") + ") вЂ” RHS РІРµСЂРЅСѓР» NaN/Inf.");
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

        public static SpacecraftIntegrationResult StepForwardDop853(
            SpacecraftIntegrationState initialState,
            double currentTimeSeconds,
            double targetDt,
            DerivativeFunction derivativeProvider,
            double positionAbsoluteTolerance,
            double velocityAbsoluteTolerance,
            double massAbsoluteTolerance,
            double relativeTolerance,
            System.Collections.Generic.IList<DenseSegment> segments = null,
            ToleranceReference? toleranceReference = null)
        {
            if (derivativeProvider == null)
                throw new ArgumentNullException(nameof(derivativeProvider));

            if (targetDt < 0d)
                throw new ArgumentOutOfRangeException(nameof(targetDt),
                    "DOP853 is forward-only: targetDt=" + targetDt + " < 0.");

            if (targetDt == 0.0)
                return new SpacecraftIntegrationResult(initialState);

            if (!initialState.IsFinite)
                throw new InvalidOperationException(
                    "Cannot integrate a non-finite initial state: " + initialState + ".");

            SpacecraftIntegrationState current = initialState;
            double time = currentTimeSeconds;
            double remainingDt = Math.Abs(targetDt);
            double direction = Math.Sign(targetDt);
            double dt = Math.Min(remainingDt, MaxStepSize);
            bool useReference = toleranceReference.HasValue && remainingDt <= ToleranceReference.MaxSpanSeconds;
            ToleranceReference reference = useReference ? toleranceReference.Value : default;
            bool collectDense = segments != null;

            while (remainingDt > MinStepSize)
            {
                dt = Math.Min(dt, remainingDt);

                DoP853Step(
                    current, time, dt,
                    derivativeProvider,
                    collectDense,
                    out bool stageMassInvalid,
                    out SpacecraftIntegrationState eighth,
                    out double errorPos,
                    out double errorVel,
                    out double errorMass,
                    out ShampineCoeffs denseStep);

                if (stageMassInvalid)
                {
                    if (dt <= MinStepSize)
                    {
                        throw new InvalidOperationException(
                            "DOP853 не смог подобрать шаг: масса на пробной стадии ≤ 0 при t=" +
                            time.ToString("R") + ".");
                    }

                    dt = Math.Max(MinStepSize, dt * 0.5d);
                    continue;
                }

                double scalePos;
                double scaleVel;
                if (useReference)
                {
                    Vector3d refNow = reference.PositionAt(time);
                    Vector3d refNext = reference.PositionAt(time + (dt * direction));
                    scalePos = positionAbsoluteTolerance
                        + (relativeTolerance * Math.Max((current.Position - refNow).Magnitude, (eighth.Position - refNext).Magnitude))
                        + (ToleranceReference.FloatingPointFloor * Math.Max(current.Position.Magnitude, eighth.Position.Magnitude));
                    scaleVel = velocityAbsoluteTolerance
                        + (relativeTolerance * Math.Max((current.Velocity - reference.Velocity).Magnitude, (eighth.Velocity - reference.Velocity).Magnitude))
                        + (ToleranceReference.FloatingPointFloor * Math.Max(current.Velocity.Magnitude, eighth.Velocity.Magnitude));
                }
                else
                {
                    scalePos = positionAbsoluteTolerance + (relativeTolerance * Math.Max(current.Position.Magnitude, eighth.Position.Magnitude));
                    scaleVel = velocityAbsoluteTolerance + (relativeTolerance * Math.Max(current.Velocity.Magnitude, eighth.Velocity.Magnitude));
                }

                double scaleMass = massAbsoluteTolerance + (relativeTolerance * Math.Max(Math.Abs(current.Mass), Math.Abs(eighth.Mass)));
                double normalizedError = Math.Max(errorPos / scalePos, Math.Max(errorVel / scaleVel, errorMass / scaleMass));

                if (normalizedError <= 1.0)
                {
                    EnsurePositiveMass(eighth, time + (dt * direction));
                    if (segments != null)
                    {
                        segments.Add(new DenseSegment(time, time + (dt * direction), dt, current, eighth, denseStep));
                    }

                    current = eighth;
                    time += dt * direction;
                    remainingDt -= dt;

                    if (!current.IsFinite)
                    {
                        throw new InvalidOperationException(
                            "DOP853 produced a non-finite state (t=" + time.ToString("R") +
                            ", dt=" + dt.ToString("R") + ").");
                    }

                    double stepScale = Math.Min(Math.Max(
                        0.9 * Math.Pow(1.0 / Math.Max(normalizedError, 1e-10), 1.0 / 8.0),
                        0.2), 5.0);
                    dt = Math.Min(Math.Max(dt * stepScale, MinStepSize), MaxStepSize);
                }
                else
                {
                    double rejectScale = Math.Min(Math.Max(
                        0.9 * Math.Pow(1.0 / Math.Max(normalizedError, 1e-10), 1.0 / 8.0),
                        0.1), 0.5);
                    double candidateDt = Math.Max(dt * rejectScale, MinStepSize);
                    if (candidateDt <= MinStepSize)
                    {
                        dt = MinStepSize;
                        DoP853Step(
                            current, time, dt,
                            derivativeProvider,
                            collectDense,
                            out bool forcedStageMassInvalid,
                            out SpacecraftIntegrationState forced,
                            out double forcedErrPos,
                            out double forcedErrVel,
                            out double forcedErrMass,
                            out ShampineCoeffs forcedDense);

                        if (forcedStageMassInvalid)
                        {
                            throw new InvalidOperationException(
                                "DOP853 не смог подобрать шаг: масса на пробной стадии ≤ 0 при t=" +
                                time.ToString("R") + ".");
                        }

                        EnsurePositiveMass(forced, time + (dt * direction));
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
                                "DOP853 produced a non-finite state at force-accept (t=" + time.ToString("R") +
                                ", dt=" + dt.ToString("R") + ").");
                        }
                    }
                    else
                    {
                        dt = candidateDt;
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
            System.Collections.Generic.IList<DenseSegment> segments = null,
            ToleranceReference? toleranceReference = null)
        {
            double dt = targetTimeSeconds - currentTimeSeconds;
            return StepForward(
                initialState, currentTimeSeconds, dt, derivativeProvider,
                positionAbsoluteTolerance, velocityAbsoluteTolerance,
                massAbsoluteTolerance, relativeTolerance, segments, toleranceReference);
        }

        public static SpacecraftIntegrationResult StepToTimeDop853(
            SpacecraftIntegrationState initialState,
            double currentTimeSeconds,
            double targetTimeSeconds,
            DerivativeFunction derivativeProvider,
            double positionAbsoluteTolerance,
            double velocityAbsoluteTolerance,
            double massAbsoluteTolerance,
            double relativeTolerance,
            System.Collections.Generic.IList<DenseSegment> segments = null,
            ToleranceReference? toleranceReference = null)
        {
            double dt = targetTimeSeconds - currentTimeSeconds;
            return StepForwardDop853(
                initialState, currentTimeSeconds, dt, derivativeProvider,
                positionAbsoluteTolerance, velocityAbsoluteTolerance,
                massAbsoluteTolerance, relativeTolerance, segments, toleranceReference);
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
            out bool stageMassInvalid,
            out SpacecraftIntegrationState fifth,
            out SpacecraftIntegrationState lastDerivative,
            out double errPos,
            out double errVel,
            out double errMass,
            out ShampineCoeffs dense)
        {
            stageMassInvalid = false;
            fifth = state;
            lastDerivative = default;
            errPos = 0d;
            errVel = 0d;
            errMass = 0d;
            dense = default;

            // Stage 1 (FSAL)
            SpacecraftIntegrationState k1 = fsalDerivative;

            // Stage 2
            SpacecraftIntegrationState s2 = state + k1 * (dt * a21);
            if (!(s2.Mass > 0d))
            {
                stageMassInvalid = true;
                return;
            }

            SpacecraftIntegrationState k2 = derivativeProvider(s2, time + dt * DOPRI5Coefficients.c2);

            // Stage 3
            SpacecraftIntegrationState s3 = state + k1 * (dt * a31) + k2 * (dt * a32);
            if (!(s3.Mass > 0d))
            {
                stageMassInvalid = true;
                return;
            }

            SpacecraftIntegrationState k3 = derivativeProvider(s3, time + dt * DOPRI5Coefficients.c3);

            // Stage 4
            SpacecraftIntegrationState s4 = state + k1 * (dt * a41) + k2 * (dt * a42) + k3 * (dt * a43);
            if (!(s4.Mass > 0d))
            {
                stageMassInvalid = true;
                return;
            }

            SpacecraftIntegrationState k4 = derivativeProvider(s4, time + dt * DOPRI5Coefficients.c4);

            // Stage 5
            SpacecraftIntegrationState s5 = state + k1 * (dt * a51) + k2 * (dt * a52)
                + k3 * (dt * a53) + k4 * (dt * a54);
            if (!(s5.Mass > 0d))
            {
                stageMassInvalid = true;
                return;
            }

            SpacecraftIntegrationState k5 = derivativeProvider(s5, time + dt * DOPRI5Coefficients.c5);

            // Stage 6
            SpacecraftIntegrationState s6 = state + k1 * (dt * a61) + k2 * (dt * a62)
                + k3 * (dt * a63) + k4 * (dt * a64) + k5 * (dt * a65);
            if (!(s6.Mass > 0d))
            {
                stageMassInvalid = true;
                return;
            }

            SpacecraftIntegrationState k6 = derivativeProvider(s6, time + dt * DOPRI5Coefficients.c6);

            // Stage 7 (FSAL candidate: evaluated at t+dt)
            SpacecraftIntegrationState s7 =
                state + k1 * (dt * a71) + k3 * (dt * a73) + k4 * (dt * a74) + k5 * (dt * a75) + k6 * (dt * a76);
            if (!(s7.Mass > 0d))
            {
                stageMassInvalid = true;
                return;
            }

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

            // Shampine dense output (Hairer dopri5.f CONTD5): k7 here is f(t0+h, y1) вЂ”
            // exactly the K2 used by the C3/C4 formulas. Always computed (a few vector
            // ops); entries go to a segment only for accepted steps вЂ” the caller owns
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

        private static void DoP853Step(
            SpacecraftIntegrationState state,
            double time,
            double dt,
            DerivativeFunction derivativeProvider,
            bool collectDense,
            out bool stageMassInvalid,
            out SpacecraftIntegrationState eighth,
            out double errPos,
            out double errVel,
            out double errMass,
            out ShampineCoeffs dense)
        {
            stageMassInvalid = false;
            eighth = state;
            errPos = 0d;
            errVel = 0d;
            errMass = 0d;
            dense = default;

            var k = new SpacecraftIntegrationState[12];
            k[0] = derivativeProvider(state, time);

            for (int s = 1; s < 12; s++)
            {
                SpacecraftIntegrationState stage = state;
                double[] row = DOP853Coefficients.A[s];
                for (int j = 0; j < s; j++)
                {
                    double a = row[j];
                    if (a != 0d)
                    {
                        stage += k[j] * (dt * a);
                    }
                }

                if (!(stage.Mass > 0d))
                {
                    stageMassInvalid = true;
                    return;
                }

                k[s] = derivativeProvider(stage, time + (dt * DOP853Coefficients.C[s]));
            }

            SpacecraftIntegrationState error = SpacecraftIntegrationState.Zero;
            for (int s = 0; s < 12; s++)
            {
                eighth += k[s] * (dt * DOP853Coefficients.B[s]);
                error += k[s] * (dt * DOP853Coefficients.E5[s]);
            }

            errPos = error.Position.Magnitude;
            errVel = error.Velocity.Magnitude;
            errMass = Math.Abs(error.Mass);

            if (collectDense)
            {
                if (!(eighth.Mass > 0d))
                {
                    stageMassInvalid = true;
                    return;
                }

                SpacecraftIntegrationState kEnd = derivativeProvider(eighth, time + dt);
                SpacecraftIntegrationState dc1 = eighth - state;
                SpacecraftIntegrationState dc2 = (k[0] * dt) - dc1;
                SpacecraftIntegrationState dc3 = dc1 - dc2 - (kEnd * dt);
                dense = new ShampineCoeffs(dc1, dc2, dc3, SpacecraftIntegrationState.Zero);
            }
        }
    }
}
