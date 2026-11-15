using System;

namespace Galilego.Universe
{
    /// <summary>
    /// Acceleration callback for <see cref="Rkn1210Stepper"/>: pure gravity-like
    /// second-order field, a(t, p). Implementations must not retain the arrays and
    /// must not allocate on the hot path. The bake passes the existing
    /// <c>EphemerisBaker.Accel</c> through a closure (physics is not duplicated).
    /// </summary>
    public delegate void Rkn1210Acceleration(double timeSeconds, double[] positions, double[] accelerations);

    /// <summary>
    /// Adaptive RKN12(10)17M stepper for N-body conservative gravity:
    ///   p'' = a(t, p),  p in R^(3n).
    ///
    /// One <see cref="TryStep"/> evaluates stages 1..16 (16 acceleration calls);
    /// stage 0 is cached by <see cref="Begin"/> and by every <see cref="Accept"/>,
    /// so a step costs 16 new RHS calls, not 17 (RKN12 is not FSAL).
    ///
    /// Error norm is per-body and relative to the parent (TASK_RKN12 §6.5):
    /// for every body with parent >= 0 the position/velocity error of the RELATIVE
    /// state (i - parent) is scaled by
    ///   atol + rtol * max(|relative state now|, |relative state trial|).
    /// The root's absolute error does not enter the norm (otherwise the absolute
    /// translation of a star would mask a moon's error).
    ///
    /// The stepper is pure C# and allocation-free in the hot path. It does not
    /// know about dense output, drag, thrust, mass or events; velocity-dependent
    /// forces are an invalid call by contract. Node forcing, accept/reject and
    /// MinStep policy live in the bake loop (EphemerisBaker.RunBake).
    /// </summary>
    public sealed class Rkn1210Stepper
    {
        /// <summary>17 stages, C[0]..C[16].</summary>
        public const int StageCount = 17;

        /// <summary>New acceleration evaluations per TryStep (stages 1..16).</summary>
        public const int RhsPerAttempt = 16;

        private readonly int bodies;
        private readonly int dim;
        private readonly int[] parent;
        private readonly double rtol;
        private readonly double atolPos;
        private readonly double atolVel;
        private readonly Rkn1210Acceleration acceleration;

        private readonly double[][] k;
        private readonly double[] stagePositions;
        private readonly double[] errorPos;
        private readonly double[] errorVel;

        /// <summary>Body that produced the last error norm (diagnostics only).</summary>
        public int LastErrorBody { get; private set; }

        /// <summary>True when the last norm was set by velocity, false by position.</summary>
        public bool LastErrorIsVelocity { get; private set; }

        public Rkn1210Stepper(
            int bodyCount,
            int[] parentIndex,
            double relativeTolerance,
            double absolutePositionTolerance,
            double absoluteVelocityTolerance,
            Rkn1210Acceleration accelerationCallback)
        {
            if (bodyCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(bodyCount), bodyCount, "At least one body is required.");
            }

            if (parentIndex == null || parentIndex.Length != bodyCount)
            {
                throw new ArgumentException("parentIndex must have one entry per body.", nameof(parentIndex));
            }

            if (!(relativeTolerance > 0d) || !double.IsFinite(relativeTolerance))
            {
                throw new ArgumentOutOfRangeException(nameof(relativeTolerance), relativeTolerance,
                    "Relative tolerance must be finite and positive.");
            }

            if (!(absolutePositionTolerance >= 0d) || !double.IsFinite(absolutePositionTolerance))
            {
                throw new ArgumentOutOfRangeException(nameof(absolutePositionTolerance), absolutePositionTolerance,
                    "Position tolerance must be finite and non-negative.");
            }

            if (!(absoluteVelocityTolerance >= 0d) || !double.IsFinite(absoluteVelocityTolerance))
            {
                throw new ArgumentOutOfRangeException(nameof(absoluteVelocityTolerance), absoluteVelocityTolerance,
                    "Velocity tolerance must be finite and non-negative.");
            }

            if (accelerationCallback == null)
            {
                throw new ArgumentNullException(nameof(accelerationCallback));
            }

            bodies = bodyCount;
            dim = 3 * bodyCount;
            parent = (int[])parentIndex.Clone();
            for (int i = 0; i < parent.Length; i++)
            {
                if (parent[i] < -1 || parent[i] >= bodies || parent[i] == i)
                {
                    throw new ArgumentException(
                        "parentIndex[" + i + "] must be -1 or a valid other body index, got " + parent[i] + ".",
                        nameof(parentIndex));
                }
            }

            rtol = relativeTolerance;
            atolPos = absolutePositionTolerance;
            atolVel = absoluteVelocityTolerance;
            acceleration = accelerationCallback;

            k = new double[StageCount][];
            for (int s = 0; s < StageCount; s++)
            {
                k[s] = new double[dim];
            }

            stagePositions = new double[dim];
            errorPos = new double[dim];
            errorVel = new double[dim];
        }

        /// <summary>Body count n; state vectors are 3n long.</summary>
        public int BodyCount => bodies;

        /// <summary>State dimension 3n.</summary>
        public int Dimension => dim;

        /// <summary>
        /// Caches stage 0 (the acceleration at the accepted state). Must be called
        /// once before the first <see cref="TryStep"/> and after every accepted step
        /// through <see cref="Accept"/>.
        /// </summary>
        public void Begin(double timeSeconds, double[] positions, double[] velocities)
        {
            ValidateState(positions, velocities);
            acceleration(timeSeconds, positions, k[0]);
            EnsureFinite(k[0], timeSeconds, 0d, -1, "start acceleration");
        }

        /// <summary>
        /// Tries (t, p, v) -> (t + h, pNew, vNew) and returns the scaled error norm.
        /// The input state is never mutated; rejected trial states are simply
        /// discarded by the caller. No allocation on the hot path.
        /// </summary>
        public double TryStep(
            double timeSeconds, double stepSeconds,
            double[] positions, double[] velocities,
            double[] newPositions, double[] newVelocities)
        {
            ValidateState(positions, velocities);
            if (newPositions == null || newPositions.Length != dim)
            {
                throw new ArgumentException("newPositions must be 3n long.", nameof(newPositions));
            }

            if (newVelocities == null || newVelocities.Length != dim)
            {
                throw new ArgumentException("newVelocities must be 3n long.", nameof(newVelocities));
            }

            if (!(stepSeconds > 0d) || !double.IsFinite(stepSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(stepSeconds), stepSeconds,
                    "Step must be finite and positive.");
            }

            if (!double.IsFinite(timeSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(timeSeconds), timeSeconds, "Time must be finite.");
            }

            for (int i = 1; i < StageCount; i++)
            {
                double ci = Rkn1210Coefficients.C[i];
                double[] row = Rkn1210Coefficients.A[i];
                for (int j = 0; j < dim; j++)
                {
                    double sum = 0d;
                    for (int m = 0; m < i; m++)
                    {
                        sum += row[m] * k[m][j];
                    }

                    stagePositions[j] = positions[j] + (stepSeconds * ((ci * velocities[j]) + (stepSeconds * sum)));
                }

                acceleration(timeSeconds + (stepSeconds * ci), stagePositions, k[i]);
            }

            for (int j = 0; j < dim; j++)
            {
                double sb = 0d;
                double sbp = 0d;
                double sbt = 0d;
                double sbpt = 0d;
                for (int i = 0; i < StageCount; i++)
                {
                    double kij = k[i][j];
                    sb += Rkn1210Coefficients.B[i] * kij;
                    sbp += Rkn1210Coefficients.BP[i] * kij;
                    sbt += Rkn1210Coefficients.BT[i] * kij;
                    sbpt += Rkn1210Coefficients.BPT[i] * kij;
                }

                newPositions[j] = positions[j] + (stepSeconds * velocities[j]) + (stepSeconds * stepSeconds * sb);
                newVelocities[j] = velocities[j] + (stepSeconds * sbp);
                errorPos[j] = stepSeconds * stepSeconds * sbt;
                errorVel[j] = stepSeconds * sbpt;
            }

            EnsureFinite(newPositions, timeSeconds, stepSeconds, -1, "trial position");
            EnsureFinite(newVelocities, timeSeconds, stepSeconds, -1, "trial velocity");
            return ComputeErrorNorm(timeSeconds, stepSeconds, positions, velocities, newPositions, newVelocities);
        }

        /// <summary>Commits an accepted trial state and refreshes stage 0.</summary>
        public void Accept(double timeSeconds, double[] positions, double[] velocities)
        {
            ValidateState(positions, velocities);
            acceleration(timeSeconds, positions, k[0]);
            EnsureFinite(k[0], timeSeconds, 0d, -1, "accepted acceleration");
        }

        private double ComputeErrorNorm(
            double timeSeconds, double stepSeconds,
            double[] positions, double[] velocities,
            double[] newPositions, double[] newVelocities)
        {
            double worst = 0d;
            int worstBody = -1;
            bool worstIsVelocity = false;

            for (int i = 0; i < bodies; i++)
            {
                int p = parent[i];
                if (p < 0)
                {
                    continue; // the root participates in the dynamics, not in the fit norm
                }

                int i3 = 3 * i;
                int p3 = 3 * p;
                double rx = positions[i3] - positions[p3];
                double ry = positions[i3 + 1] - positions[p3 + 1];
                double rz = positions[i3 + 2] - positions[p3 + 2];
                double vx = velocities[i3] - velocities[p3];
                double vy = velocities[i3 + 1] - velocities[p3 + 1];
                double vz = velocities[i3 + 2] - velocities[p3 + 2];

                double nrx = newPositions[i3] - newPositions[p3];
                double nry = newPositions[i3 + 1] - newPositions[p3 + 1];
                double nrz = newPositions[i3 + 2] - newPositions[p3 + 2];
                double nvx = newVelocities[i3] - newVelocities[p3];
                double nvy = newVelocities[i3 + 1] - newVelocities[p3 + 1];
                double nvz = newVelocities[i3 + 2] - newVelocities[p3 + 2];

                double scalePos = atolPos + (rtol * Math.Max(Magnitude(rx, ry, rz), Magnitude(nrx, nry, nrz)));
                double scaleVel = atolVel + (rtol * Math.Max(Magnitude(vx, vy, vz), Magnitude(nvx, nvy, nvz)));
                if (scalePos < 1e-300d)
                {
                    scalePos = 1e-300d;
                }

                if (scaleVel < 1e-300d)
                {
                    scaleVel = 1e-300d;
                }

                double ex = errorPos[i3] - errorPos[p3];
                double ey = errorPos[i3 + 1] - errorPos[p3 + 1];
                double ez = errorPos[i3 + 2] - errorPos[p3 + 2];
                double evx = errorVel[i3] - errorVel[p3];
                double evy = errorVel[i3 + 1] - errorVel[p3 + 1];
                double evz = errorVel[i3 + 2] - errorVel[p3 + 2];

                double posNorm = Magnitude(ex, ey, ez) / scalePos;
                double velNorm = Magnitude(evx, evy, evz) / scaleVel;
                if (posNorm > worst)
                {
                    worst = posNorm;
                    worstBody = i;
                    worstIsVelocity = false;
                }

                if (velNorm > worst)
                {
                    worst = velNorm;
                    worstBody = i;
                    worstIsVelocity = true;
                }
            }

            LastErrorBody = worstBody;
            LastErrorIsVelocity = worstIsVelocity;
            if (!double.IsFinite(worst))
            {
                throw StepFailure(timeSeconds, stepSeconds, worstBody,
                    "non-finite scaled error norm (" + worst + ")");
            }

            return worst;
        }

        private void ValidateState(double[] positions, double[] velocities)
        {
            if (positions == null || positions.Length != dim)
            {
                throw new ArgumentException("positions must be 3n long.", nameof(positions));
            }

            if (velocities == null || velocities.Length != dim)
            {
                throw new ArgumentException("velocities must be 3n long.", nameof(velocities));
            }
        }

        private void EnsureFinite(double[] values, double timeSeconds, double stepSeconds, int body, string what)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (!double.IsFinite(values[i]))
                {
                    throw StepFailure(timeSeconds, stepSeconds, body >= 0 ? body : i / 3,
                        what + " is not finite at component " + i + " (" + values[i] + ")");
                }
            }
        }

        private static InvalidOperationException StepFailure(
            double timeSeconds, double stepSeconds, int body, string reason)
        {
            return new InvalidOperationException(
                "RKN12(10) step failed: " + reason +
                "; t=" + timeSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                " s, h=" + stepSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                " s, body=" + body + ".");
        }

        private static double Magnitude(double x, double y, double z)
        {
            return Math.Sqrt((x * x) + (y * y) + (z * z));
        }
    }
}
