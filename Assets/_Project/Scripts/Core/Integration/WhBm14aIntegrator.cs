using System;
using System.Collections.Generic;

namespace Galilego.Core
{
    /// <summary>
    /// WH-BM14A: Blanes-Moan 14A composition (order 6) on the Wisdom-Holman Kepler split.
    /// Each substep is a kick by the perturbation (total acceleration minus the central
    /// point-mass term) followed by an exact universal-Kepler drift.
    ///
    /// Ported from the study implementation (Tools/P1bTests/Study/framework/StudyAdvancedSteppers.cs,
    /// KeplerSplit14AStepper) after the C3 production run showed it 7-10x faster than the game
    /// DOP853 at 600 s in planetocentric LEO, with absolute errors of 1e-6..2e-5 m at the 1 m goal.
    ///
    /// It is a FIXED-STEP method: the caller passes the game chunk (600 s in the long-warp
    /// regime). The port is guarded by IntegratorPolicy and falls back to DOP853 outside the
    /// narrow regime; this class only performs one step and never decides where it is used.
    ///
    /// Dense output is a cubic Hermite segment for rendering. Event refinement must re-integrate
    /// from the step start (EventDrivenPropagator.UseDenseRefinement = false when this method is
    /// selected); the Hermite segment is not certified for event roots.
    /// </summary>
    public static class WhBm14aIntegrator
    {
        // Blanes-Moan SRKN14A composition weights (sum A = sum B = 1, symmetric).
        // Same mathematical constants as the study stepper.
        private static readonly double[] A =
        {
            0.0378593198406116, 0.102635633102435, -0.0258678882665587,
            0.314241403071447, -0.130144459517415, 0.106417700369543,
            -0.00879424312851058, 0.2073050690568954, -0.00879424312851058,
            0.106417700369543, -0.130144459517415, 0.314241403071447,
            -0.0258678882665587, 0.102635633102435, 0.0378593198406116
        };

        private static readonly double[] B =
        {
            0.0, 0.09171915262446165, 0.183983170005006, -0.05653436583288827,
            0.004914688774712854, 0.143761127168358, 0.328567693746804,
            -0.19641146648645422, -0.19641146648645422, 0.328567693746804,
            0.143761127168358, 0.004914688774712854, -0.05653436583288827,
            0.183983170005006, 0.09171915262446165
        };

        /// <summary>
        /// One fixed step of h seconds in the central field mu. <paramref name="derivative"/>
        /// returns the TOTAL local acceleration (central + perturbations) at (state, time).
        /// The central term is subtracted for the kicks; the drift is exact Kepler.
        /// Throws on a non-positive mu, mass flow, non-finite state or a failed Kepler solve;
        /// the caller (SpacecraftPhysics) turns that into a DOP853 fallback for the chunk.
        /// </summary>
        public static SpacecraftIntegrationResult StepForward(
            SpacecraftIntegrationState state,
            double timeSeconds,
            double h,
            SpacecraftOrbitIntegrator.DerivativeFunction derivative,
            double centralMu,
            IList<DenseSegment> segments = null,
            SpacecraftStepDiagnostics diagnostics = null)
        {
            if (diagnostics != null)
            {
                diagnostics.Attempts++;
            }

            if (!(h > 0d))
            {
                throw new InvalidOperationException("WH-BM14A needs a positive step.");
            }

            if (!state.IsFinite || !(centralMu > 0d))
            {
                throw new InvalidOperationException("WH-BM14A needs a finite state and mu > 0.");
            }

            SpacecraftIntegrationState initial = state;
            double t = timeSeconds;
            for (int m = 0; m < A.Length; m++)
            {
                if (B[m] != 0d)
                {
                    SpacecraftIntegrationState total = derivative(state, t);
                    if (Math.Abs(total.Mass) > 1e-15d)
                    {
                        throw new InvalidOperationException("WH-BM14A does not support mass flow.");
                    }

                    double r2 = state.Position.SqrMagnitude;
                    double r = Math.Sqrt(r2);
                    if (!(r > 0d))
                    {
                        throw new InvalidOperationException("WH-BM14A state is at the central body.");
                    }

                    double centralScale = -centralMu / (r2 * r);
                    Vector3d perturbation = total.Velocity - (state.Position * centralScale);
                    state.Velocity += perturbation * (B[m] * h);
                }

                if (A[m] != 0d)
                {
                    double dh = A[m] * h;
                    KeplerUniversalDrift.Propagate(centralMu, ref state, dh);
                    t += dh;
                }
            }

            if (!state.IsFinite)
            {
                throw new InvalidOperationException("WH-BM14A produced a non-finite state.");
            }

            if (segments != null)
            {
                // Cubic Hermite for rendering. The endpoints and their derivatives are exact
                // for the accepted step; this segment is NOT used for event roots (the
                // propagator re-integrates from the step start when this method is selected).
                SpacecraftIntegrationState f0 = derivative(initial, timeSeconds);
                SpacecraftIntegrationState f1 = derivative(state, timeSeconds + h);
                SpacecraftIntegrationState c1 = state - initial;
                SpacecraftIntegrationState c2 = (f0 * h) - c1;
                SpacecraftIntegrationState c3 = c1 - c2 - (f1 * h);
                segments.Add(new DenseSegment(
                    timeSeconds, timeSeconds + h, h, initial, state,
                    new ShampineCoeffs(c1, c2, c3, SpacecraftIntegrationState.Zero)));
            }

            if (diagnostics != null)
            {
                diagnostics.Accepted++;
            }

            return new SpacecraftIntegrationResult(state);
        }
    }
}
