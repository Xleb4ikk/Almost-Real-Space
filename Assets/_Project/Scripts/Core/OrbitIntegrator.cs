using System;

namespace Galilego.Core
{
    /// <summary>
    /// Unified DOPRI5 (Dormand-Prince 5(4)) orbit integrator with adaptive error control.
    /// NOTE: historical comments claimed exact parity with FullTrajectoryJob (Burst twin).
    /// That file is not in this repo, so parity is NOT automatically verified — the managed
    /// baseline is pinned instead by golden-master test T34. Any intentional behavior change
    /// must update T34 explicitly, not silently.
    /// </summary>
    public static class OrbitIntegrator
    {
        // Configuration constants
        public const double DefaultAbsoluteTolerance = 1e-6;  // 1 um position error
        public const double DefaultRelativeTolerance = 1e-9;  // 1 ppb relative error
        public const double DefaultMaxStepSize = 600.0;       // 10 minutes
        public const double DefaultMinStepSize = 1e-6;        // 1 microsecond
        
        // DOPRI5 Butcher tableau coefficients come straight from DOPRI5Coefficients
        // (single source of truth). No local aliases: stage math below is more
        // verbose, but there is exactly one place holding the numbers.

        /// <summary>
        /// Integrate forward by a target time step using adaptive DOPRI5.
        /// Legacy B9 cluster: live path uses SpacecraftOrbitIntegrator via SpacecraftPhysics.
        /// </summary>
        [Obsolete("Legacy B9 cluster: use SpacecraftPhysics (SpacecraftOrbitIntegrator). Chain: StepShip -> OrbitIntegrator -> RK4/CelestialBody.")]
        public static IntegrationResult StepForward(
            Vector3d position,
            Vector3d velocity,
            double currentTime,
            double targetDt,
            Func<Vector3d, double, Vector3d> accelerationProvider,
            double absoluteTolerance = DefaultAbsoluteTolerance,
            double relativeTolerance = DefaultRelativeTolerance)
        {
            if (accelerationProvider == null)
                throw new ArgumentNullException(nameof(accelerationProvider));
            
            // Forward-only контракт (аудит S1/A1): при targetDt<0 стадии идут
            // вперёд, а часы назад — молчаливый мусор. Обратный ход — громко.
            if (targetDt < 0.0)
                throw new ArgumentOutOfRangeException(nameof(targetDt),
                    "Интегратор forward-only: targetDt=" + targetDt + " < 0.");
            
            if (targetDt == 0.0)
                return new IntegrationResult(position, velocity);
            
            // Initialize FSAL (First Same As Last)
            Vector3d fsalAccel = accelerationProvider(position, currentTime);
            bool fsalValid = true;
            
            Vector3d currentPos = position;
            Vector3d currentVel = velocity;
            double time = currentTime;
            double remainingDt = Math.Abs(targetDt);
            double direction = Math.Sign(targetDt);
            double dt = Math.Min(remainingDt, DefaultMaxStepSize);
            
            while (remainingDt > DefaultMinStepSize)
            {
                // Clamp step to remaining time
                dt = Math.Min(dt, remainingDt);
                
                // If FSAL invalid (first step or after reject), compute k1
                if (!fsalValid)
                {
                    fsalAccel = accelerationProvider(currentPos, time);
                    fsalValid = true;
                }
                
                // Perform DOPRI5 step (use absolute dt, direction handled externally)
                var result = DoPri5Step(
                    currentPos, currentVel, time,
                    dt,  // Always positive, direction handled in time update
                    accelerationProvider,
                    fsalAccel,
                    out Vector3d newFsalAccel,
                    out double errorPos,
                    out double errorVel);
                
                // Scaled error
                double scalePos = absoluteTolerance + relativeTolerance * Math.Max(currentPos.Magnitude, result.Position.Magnitude);
                double scaleVel = absoluteTolerance + relativeTolerance * Math.Max(currentVel.Magnitude, result.Velocity.Magnitude);
                double normalizedError = Math.Max(errorPos / scalePos, errorVel / scaleVel);
                
                if (normalizedError <= 1.0)
                {
                    // ACCEPT step
                    currentPos = result.Position;
                    currentVel = result.Velocity;
                    time += dt * direction;
                    remainingDt -= dt;
                    if (!(currentPos.IsFinite && currentVel.IsFinite))
                    {
                        throw new InvalidOperationException(
                            "Интегратор получил не-конечное состояние (t=" + time.ToString("R") +
                            ", dt=" + dt.ToString("R") + ") — accelerationProvider вернул NaN/Inf.");
                    }
                    
                    // FSAL: k7 of accepted step = k1 of next step
                    fsalAccel = newFsalAccel;
                    fsalValid = true;
                    
                    // Step size control
                    double stepScale = Math.Min(Math.Max(
                        0.9 * Math.Pow(1.0 / Math.Max(normalizedError, 1e-10), 0.2),
                        0.2), 5.0);
                    dt = Math.Min(Math.Max(dt * stepScale, DefaultMinStepSize), DefaultMaxStepSize);
                }
                else
                {
                    // REJECT step
                    double rejectScale = Math.Min(Math.Max(
                        0.9 * Math.Pow(1.0 / Math.Max(normalizedError, 1e-10), 0.2),
                        0.1), 0.5);
                    // Force-accept при клампе к MinStepSize: пересчитываем шаг с
                    // УМЕНЬШЕННЫМ dt, а не принимаем результат старого крупного
                    // шага — иначе состояние, time, remainingDt и dense-сегмент
                    // расходились бы (результат считался со старым dt).
                    double candidateDt = Math.Max(dt * rejectScale, DefaultMinStepSize);
                    if (candidateDt <= DefaultMinStepSize)
                    {
                        dt = DefaultMinStepSize;
                        result = DoPri5Step(
                            currentPos, currentVel, time,
                            dt,
                            accelerationProvider,
                            fsalAccel,
                            out newFsalAccel,
                            out errorPos,
                            out errorVel);
                        currentPos = result.Position;
                        currentVel = result.Velocity;
                        time += dt * direction;
                        remainingDt -= dt;
                        if (!(currentPos.IsFinite && currentVel.IsFinite))
                        {
                            throw new InvalidOperationException(
                                "Интегратор получил не-конечное состояние при force-accept (t=" + time.ToString("R") + ").");
                        }
                        fsalAccel = newFsalAccel;
                        fsalValid = true;
                    }
                    else
                    {
                        dt = candidateDt;
                        // REJECT: FSAL invalid for next attempt
                        fsalValid = false;
                    }
                }
            }
            
            return new IntegrationResult(currentPos, currentVel);
        }

        /// <summary>
        /// Integrate to an exact target time.
        /// Legacy B9 cluster: see StepForward.
        /// </summary>
        [Obsolete("Legacy B9 cluster: use SpacecraftPhysics (SpacecraftOrbitIntegrator). Chain: StepShip -> OrbitIntegrator -> RK4/CelestialBody.")]
#pragma warning disable 0618 // Legacy B9 cluster: internal delegation within obsolete surface.
        public static IntegrationResult StepToTime(
            Vector3d position,
            Vector3d velocity,
            double currentTime,
            double targetTime,
            Func<Vector3d, double, Vector3d> accelerationProvider,
            double absoluteTolerance = DefaultAbsoluteTolerance,
            double relativeTolerance = DefaultRelativeTolerance)
        {
            double dt = targetTime - currentTime;
            return StepForward(position, velocity, currentTime, dt, accelerationProvider, absoluteTolerance, relativeTolerance);
        }
#pragma warning restore 0618

        /// <summary>
        /// Perform a single DOPRI5 integration step.
        /// Single adaptive DOPRI5 stage evaluation (see class note on Burst parity).
        /// </summary>
        private static DoPri5Result DoPri5Step(
            Vector3d pos,
            Vector3d vel,
            double time,
            double dt,
            Func<Vector3d, double, Vector3d> accelerationProvider,
            Vector3d fsalAccel,
            out Vector3d lastAccel,
            out double errPos,
            out double errVel)
        {
            // Stage 1 (FSAL)
            Vector3d k1v = fsalAccel;
            Vector3d k1p = vel;

            // Stage 2
            Vector3d pos2 = pos + k1p * (dt * DOPRI5Coefficients.a21);
            Vector3d vel2 = vel + k1v * (dt * DOPRI5Coefficients.a21);
            Vector3d a2 = accelerationProvider(pos2, time + dt * (1.0 / 5.0));

            // Stage 3
            Vector3d pos3 = pos + (k1p * (dt * DOPRI5Coefficients.a31) + vel2 * (dt * DOPRI5Coefficients.a32));
            Vector3d vel3 = vel + (k1v * (dt * DOPRI5Coefficients.a31) + a2 * (dt * DOPRI5Coefficients.a32));
            Vector3d a3 = accelerationProvider(pos3, time + dt * (3.0 / 10.0));
            
            // Stage 4
            Vector3d pos4 = pos + (k1p * (dt * DOPRI5Coefficients.a41) + vel2 * (dt * DOPRI5Coefficients.a42) + vel3 * (dt * DOPRI5Coefficients.a43));
            Vector3d vel4 = vel + (k1v * (dt * DOPRI5Coefficients.a41) + a2 * (dt * DOPRI5Coefficients.a42) + a3 * (dt * DOPRI5Coefficients.a43));
            Vector3d a4 = accelerationProvider(pos4, time + dt * (4.0 / 5.0));
            
            // Stage 5
            Vector3d pos5 = pos + (k1p * (dt * DOPRI5Coefficients.a51) + vel2 * (dt * DOPRI5Coefficients.a52) + vel3 * (dt * DOPRI5Coefficients.a53) + vel4 * (dt * DOPRI5Coefficients.a54));
            Vector3d vel5 = vel + (k1v * (dt * DOPRI5Coefficients.a51) + a2 * (dt * DOPRI5Coefficients.a52) + a3 * (dt * DOPRI5Coefficients.a53) + a4 * (dt * DOPRI5Coefficients.a54));
            Vector3d a5 = accelerationProvider(pos5, time + dt * (8.0 / 9.0));
            
            // Stage 6
            Vector3d pos6 = pos + (k1p * (dt * DOPRI5Coefficients.a61) + vel2 * (dt * DOPRI5Coefficients.a62) + vel3 * (dt * DOPRI5Coefficients.a63) + vel4 * (dt * DOPRI5Coefficients.a64) + vel5 * (dt * DOPRI5Coefficients.a65));
            Vector3d vel6 = vel + (k1v * (dt * DOPRI5Coefficients.a61) + a2 * (dt * DOPRI5Coefficients.a62) + a3 * (dt * DOPRI5Coefficients.a63) + a4 * (dt * DOPRI5Coefficients.a64) + a5 * (dt * DOPRI5Coefficients.a65));
            Vector3d a6 = accelerationProvider(pos6, time + dt);
            
            // Stage 7
            Vector3d pos7 = pos + (k1p * (dt * DOPRI5Coefficients.a71) + vel3 * (dt * DOPRI5Coefficients.a73) + vel4 * (dt * DOPRI5Coefficients.a74) + vel5 * (dt * DOPRI5Coefficients.a75) + vel6 * (dt * DOPRI5Coefficients.a76));
            Vector3d vel7 = vel + (k1v * (dt * DOPRI5Coefficients.a71) + a3 * (dt * DOPRI5Coefficients.a73) + a4 * (dt * DOPRI5Coefficients.a74) + a5 * (dt * DOPRI5Coefficients.a75) + a6 * (dt * DOPRI5Coefficients.a76));
            Vector3d a7 = accelerationProvider(pos7, time + dt);
            
            // 5th order solution
            Vector3d fifthPos = pos + (k1p * (dt * DOPRI5Coefficients.b1) + vel3 * (dt * DOPRI5Coefficients.b3) + vel4 * (dt * DOPRI5Coefficients.b4) + vel5 * (dt * DOPRI5Coefficients.b5) + vel6 * (dt * DOPRI5Coefficients.b6));
            Vector3d fifthVel = vel + (k1v * (dt * DOPRI5Coefficients.b1) + a3 * (dt * DOPRI5Coefficients.b3) + a4 * (dt * DOPRI5Coefficients.b4) + a5 * (dt * DOPRI5Coefficients.b5) + a6 * (dt * DOPRI5Coefficients.b6));

            // 4th order solution (for error estimation)
            Vector3d fourthPos = pos + (k1p * (dt * DOPRI5Coefficients.bStar1) + vel3 * (dt * DOPRI5Coefficients.bStar3) + vel4 * (dt * DOPRI5Coefficients.bStar4) + vel5 * (dt * DOPRI5Coefficients.bStar5) + vel6 * (dt * DOPRI5Coefficients.bStar6) + vel7 * (dt * DOPRI5Coefficients.bStar7));
            Vector3d fourthVel = vel + (k1v * (dt * DOPRI5Coefficients.bStar1) + a3 * (dt * DOPRI5Coefficients.bStar3) + a4 * (dt * DOPRI5Coefficients.bStar4) + a5 * (dt * DOPRI5Coefficients.bStar5) + a6 * (dt * DOPRI5Coefficients.bStar6) + a7 * (dt * DOPRI5Coefficients.bStar7));
            
            // Error = difference between 5th and 4th order
            errPos = (fifthPos - fourthPos).Magnitude;
            errVel = (fifthVel - fourthVel).Magnitude;
            
            // FSAL: k7 of this step = k1 of next step (if accepted)
            lastAccel = a7;
            
            return new DoPri5Result(fifthPos, fifthVel);
        }

        /// <summary>
        /// Internal result structure for DOPRI5 step.
        /// </summary>
        private struct DoPri5Result
        {
            public Vector3d Position;
            public Vector3d Velocity;

            public DoPri5Result(Vector3d position, Vector3d velocity)
            {
                Position = position;
                Velocity = velocity;
            }
        }
    }
}
