using System;
using Galilego.Core;
using Galilego.Universe;

namespace Galilego.Events
{
    /// <summary>
    /// Adaptive event-detection grain. Rule: "only finer, never coarser" — the
    /// result is always max(base, adaptive). Coarsening the grain for slow orbits is
    /// forbidden: it would reintroduce the multi-crossing risk (comment on
    /// DetectionSubdivisions). Subdividing is allowed and needed when the system's
    /// characteristic time is small: a fast hyperbolic flyby, a tight/grazing
    /// encounter, later burns with short times.
    ///
    /// The characteristic time τ comes from Kepler elements relative to the dominant
    /// body (one FromState + one tree walk per chunk, not per sub-chunk and not per
    /// RHS — the cost is negligible). Ellipse: period 2π√(a³/μ). Hyperbola: the period
    /// analogue with |a|, 2π√(|a|³/μ) — for a deep flyby |a| is small and the grain
    /// honestly gets finer. Invalid elements fall back to the base.
    /// </summary>
    public static class DetectionGrain
    {
        public const int MaxSubdivisions = 200;

        public const double GrainDivisor = 20d;

        /// <summary>
        /// Characteristic time of the state in seconds. PositiveInfinity = unknown; the
        /// caller must fall back to the base grain.
        /// </summary>
        public static double CharacteristicTimeSeconds(StarSystem system, SpacecraftIntegrationState state, double timeSeconds)
        {
            if (system == null)
            {
                return double.PositiveInfinity;
            }

            OrbitingBody dominant = system.FindDominantBody(state.Position, timeSeconds);
            if (dominant == null)
            {
                return double.PositiveInfinity;
            }

            double mu = dominant.ResolveStandardGravitationalParameter();
            if (mu <= 0d)
            {
                return double.PositiveInfinity;
            }

            dominant.EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out Vector3d bodyVelocity);
            OrbitalElements elements = OrbitalElements.FromState(state.Position - bodyPosition, state.Velocity - bodyVelocity, mu);
            if (!elements.IsValid)
            {
                return double.PositiveInfinity;
            }

            if (elements.IsBound)
            {
                return elements.OrbitalPeriodSeconds;
            }

            double semiMajor = elements.SemiMajorAxis;
            if (double.IsNaN(semiMajor) || double.IsInfinity(semiMajor) || semiMajor == 0d)
            {
                return double.PositiveInfinity;
            }

            return 2d * Math.PI * Math.Sqrt(Math.Abs(semiMajor * semiMajor * semiMajor) / mu);
        }

        /// <summary>
        /// Number of sub-chunks per MaxChunkSeconds. Never less than baseDivisions
        /// (never coarser than the base) and never more than cap. NaN/Inf/non-positive
        /// τ — exactly baseDivisions.
        /// </summary>
        public static int Subdivisions(double maxChunkSeconds, int baseDivisions, double tauSeconds, int cap)
        {
            if (double.IsNaN(tauSeconds) || double.IsInfinity(tauSeconds) || tauSeconds <= 0d)
            {
                return baseDivisions;
            }

            double desiredGrain = tauSeconds / GrainDivisor;
            double baseGrain = maxChunkSeconds / baseDivisions;
            if (desiredGrain >= baseGrain)
            {
                return baseDivisions;
            }

            int adaptive = (int)Math.Ceiling(maxChunkSeconds / desiredGrain);
            if (adaptive < baseDivisions)
            {
                adaptive = baseDivisions;
            }

            if (adaptive > cap)
            {
                adaptive = cap;
            }

            return adaptive;
        }

        public static int Subdivisions(double maxChunkSeconds, int baseDivisions, double tauSeconds)
        {
            return Subdivisions(maxChunkSeconds, baseDivisions, tauSeconds, MaxSubdivisions);
        }

        /// <summary>
        /// Conservative predicate: "a surface crossing is possible within the chunk".
        /// Sub-chunking (the DetectionSubdivisions floor) is needed ONLY if the ship can
        /// physically reach a threshold sphere: an altitude detector (atmosphere/surface)
        /// or any body's SOI boundary. The g0/g1 sign test at sub-chunk boundaries catches
        /// a crossing only if it is longer than the grain; if the sphere is unreachable
        /// within the chunk there is no crossing and the floor of 20 is pure cost.
        /// Reachability is estimated with margin: the gap to the sphere
        /// ≤ (|v_ship|+|v_body|)·chunk·safety. Other events are not lost in the coarse
        /// grain: PropellantDepletion is monotone mass (no double crossing), TimedEvent is
        /// clock-based, SOI is a key change whose sphere is checked right here.
        /// </summary>
        public static bool NeedsFineGrain(
            StarSystem system,
            System.Collections.Generic.IReadOnlyList<ICrossingDetector> crossingDetectors,
            SpacecraftIntegrationState state,
            double timeSeconds,
            double chunkSeconds,
            double safetyFactor = 2d)
        {
            if (system == null || chunkSeconds <= 0d)
            {
                return true; // no data to estimate — conservative
            }

            for (int i = 0; i < crossingDetectors.Count; i++)
            {
                if (crossingDetectors[i] is AltitudeCrossingDetector alt)
                {
                    OrbitingBody body = alt.Body;
                    body.EvaluateWorldState(timeSeconds, out Vector3d bp, out Vector3d bv);
                    double gap = (state.Position - bp).Magnitude - (body.Radius + alt.ThresholdAltitude);
                    if (gap <= ReachableDistance(state.Velocity, bv, chunkSeconds, safetyFactor))
                    {
                        return true;
                    }
                }
            }

            System.Collections.Generic.IReadOnlyList<OrbitingBody> bodies = system.AllBodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                OrbitingBody body = bodies[i];
                if (body.Parent == null)
                {
                    continue; // the root has no SOI
                }

                body.EvaluateWorldState(timeSeconds, out Vector3d bp, out Vector3d bv);
                double gap = (state.Position - bp).Magnitude - body.SphereOfInfluenceRadius;
                if (gap <= ReachableDistance(state.Velocity, bv, chunkSeconds, safetyFactor))
                {
                    return true;
                }
            }

            return false;
        }

        private static double ReachableDistance(Vector3d shipVelocity, Vector3d bodyVelocity, double chunkSeconds, double safetyFactor)
        {
            return (shipVelocity.Magnitude + bodyVelocity.Magnitude) * chunkSeconds * safetyFactor;
        }
    }
}
