using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;
using Ship = Galilego.Spacecraft.Spacecraft;

namespace Galilego.Events
{
    /// <summary>
    /// Landing state: position projected onto the surface, normal speed removed,
    /// tangential speed kept. NormalSpeed/TangentialSpeed are measured at the
    /// instant of contact (before removal), not the result.
    /// </summary>
    public readonly struct LandedState
    {
        public readonly double LatitudeDegrees;
        public readonly double LongitudeDegrees;
        public readonly Vector3d Position;
        public readonly Vector3d Velocity;
        public readonly double NormalSpeed;
        public readonly double TangentialSpeed;

        public LandedState(double latitudeDegrees, double longitudeDegrees, Vector3d position, Vector3d velocity, double normalSpeed, double tangentialSpeed)
        {
            LatitudeDegrees = latitudeDegrees;
            LongitudeDegrees = longitudeDegrees;
            Position = position;
            Velocity = velocity;
            NormalSpeed = normalSpeed;
            TangentialSpeed = tangentialSpeed;
        }
    }

    /// <summary>Touchdown outcome: either a landing or breakup specs (not spawn).</summary>
    public readonly struct TouchdownOutcome
    {
        public readonly bool IsLanding;
        public readonly LandedState Landed;
        public readonly IReadOnlyList<PartSeparationSpec> Specs;

        public TouchdownOutcome(LandedState landed)
        {
            IsLanding = true;
            Landed = landed;
            Specs = null;
        }

        public TouchdownOutcome(IReadOnlyList<PartSeparationSpec> specs)
        {
            IsLanding = false;
            Landed = default;
            Specs = specs;
        }
    }

    public readonly struct AtmosphereResult
    {
        public readonly bool Inside;

        public AtmosphereResult(bool inside)
        {
            Inside = inside;
        }
    }

    /// <summary>
    /// The new dominant body is taken from the event as-is: the driver already put
    /// the transition key (key1) there. The reaction does NOT recompute SOI —
    /// physical logic must not be duplicated in the reaction layer.
    /// </summary>
    public readonly struct FocusResult
    {
        public readonly OrbitingBody NewBody;

        public FocusResult(OrbitingBody newBody)
        {
            NewBody = newBody;
        }
    }

    /// <summary>
    /// Reactions to events. The driver stays detect-only (P1a invariant): a
    /// recorded EventOccurrence arrives here, and nothing goes back to the
    /// propagator. The Propagator is deliberately not imported: everything needed
    /// (kind, body, source detector, state) is already in the event. The game loop
    /// holds the vessel mode (Flying/Landed/Destroyed); Spacecraft is mutated here
    /// only by the landing projection and the mass clamp.
    /// </summary>
    public static class EventReactions
    {
        /// <summary>
        /// Contact. The criterion is the NORMAL component of the impact v_n = v_impact·n:
        /// a large tangential with a small normal is a hard sliding landing (goes to
        /// SurfaceMotion with initial v_t), not a falsely soft one. The boundary is
        /// deterministic: |v_n| <= tolerance → landing.
        /// The normal here is radial; ITerrainNormal will generalize it later.
        /// </summary>
        public static TouchdownOutcome ApplyTouchdown(Ship ship, EventOccurrence occurrence, OrbitingBody body, IBreakupModel breakup, double spawnEpsilonMeters, System.Collections.Generic.IReadOnlyList<Part> parts = null)
        {
            if (ship == null)
                throw new ArgumentNullException(nameof(ship));
            if (body == null)
                throw new ArgumentNullException(nameof(body));
            if (breakup == null)
                throw new ArgumentNullException(nameof(breakup));
            if (occurrence.Kind != EventKind.Touchdown)
                throw new ArgumentException("ApplyTouchdown принимает только Touchdown.", nameof(occurrence));

            double t = occurrence.TimeSeconds;
            body.SurfaceLatLonAt(ship.Position, t, out double latDeg, out double lonDeg);
            // Soft splashdown hook (stage "ship lands on water"): landing for now
            // goes by the clamped altitude (sea plane) with no behavior change.
            // Future rule: splash.IsWater, depth greater than landing draft, and
            // |v_n| within the splashdown tolerance → landing instead of breakup.
            WaterQuery.SplashdownInfo splash = WaterQuery.GetSplashdownInfo(body, latDeg, lonDeg);
            double groundAltitude = GroundAltitude(body, latDeg, lonDeg);
            body.GetSurfaceState(latDeg, lonDeg, groundAltitude, t, out Vector3d surfacePosition, out Vector3d surfaceVelocity);
            body.EvaluateWorldState(t, out Vector3d bodyPosition, out _);

            Vector3d normal = body.Terrain != null
                ? body.Terrain.GetOutwardNormal(body, ship.Position - bodyPosition, t).Normalized
                : (ship.Position - bodyPosition).Normalized;
            Vector3d impactVelocity = ship.Velocity - surfaceVelocity;
            double normalSpeed = Vector3d.Dot(impactVelocity, normal);
            Vector3d tangentialVelocity = impactVelocity - (normal * normalSpeed);
            double tangentialSpeed = tangentialVelocity.Magnitude;

            if (Math.Abs(normalSpeed) <= body.CrashToleranceMps)
            {
                LandedState landed = LandShip(ship, latDeg, lonDeg, surfacePosition, surfaceVelocity, tangentialVelocity, normalSpeed, tangentialSpeed);
                return new TouchdownOutcome(landed);
            }

            var input = new BreakupInput(
                ship.Mass, impactVelocity, surfaceVelocity,
                surfacePosition, normal, spawnEpsilonMeters, parts);
            return new TouchdownOutcome(breakup.PlanBreakup(input));
        }

        /// <summary>
        /// Landed → Flying transition (the mode machine's LiftoffHook). The liftoff
        /// criterion is synchronized with SurfaceMotion: free acceleration in the
        /// surface frame a = thrust + gravity − ω×(ω×r) − 2ω×v_t; contact holds iff
        /// a·n <= ContactEpsilon. The centrifugal term matters: at high spin
        /// (ω²R &gt; |g|) contact does not hold even with zero thrust. ω = 0 reduces
        /// the formula to (thrust+gravity)·n &gt; 0. On liftoff the ship gets the
        /// surface velocity plus the kept tangential velocity (the same path as
        /// LandShip): a co-rotating ship keeps its speed (no double counting), and one
        /// placed with v=0 on a rotating planet is correctly carried by its rotation.
        /// Otherwise it stays: projection onto the surface plus removed normal speed
        /// (the same LandShip path, not a second implementation). Deterministic
        /// boundary: |a·n| <= eps → contact holds. The normal here is radial;
        /// ITerrainNormal will generalize it later. Parameters: thrustAccelWorld and
        /// gravityAccelWorld are accelerations AT THE SHIP's point at timeSeconds
        /// (gravityAccelWorld = starSystem.EvaluateShipAcceleration(ship.Position,
        /// timeSeconds)); gravity at another point/time desynchronizes the criterion.
        /// </summary>
        public static VesselRegime TryLiftoff(Ship ship, OrbitingBody body, Vector3d thrustAccelWorld, Vector3d gravityAccelWorld, double timeSeconds, out LandedState landedState)
        {
            if (ship == null)
                throw new ArgumentNullException(nameof(ship));
            if (body == null)
                throw new ArgumentNullException(nameof(body));

            body.SurfaceLatLonAt(ship.Position, timeSeconds, out double latDeg, out double lonDeg);
            body.GetSurfaceState(latDeg, lonDeg, GroundAltitude(body, latDeg, lonDeg), timeSeconds, out Vector3d surfacePosition, out Vector3d surfaceVelocity);
            body.EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out _);

            Vector3d normal = body.Terrain != null
                ? body.Terrain.GetOutwardNormal(body, ship.Position - bodyPosition, timeSeconds).Normalized
                : (ship.Position - bodyPosition).Normalized;
            Vector3d impactVelocity = ship.Velocity - surfaceVelocity;
            double normalSpeed = Vector3d.Dot(impactVelocity, normal);
            Vector3d tangentialVelocity = impactVelocity - (normal * normalSpeed);

            Vector3d omegaVector = body.SpinAxis * body.SpinAngularSpeed;
            Vector3d freeAcceleration = thrustAccelWorld + gravityAccelWorld
                - Vector3d.Cross(omegaVector, Vector3d.Cross(omegaVector, ship.Position - bodyPosition))
                - (Vector3d.Cross(omegaVector, tangentialVelocity) * 2d);
            if (Vector3d.Dot(freeAcceleration, normal) > SurfaceMotion.ContactEpsilon)
            {
                ship.Velocity = surfaceVelocity + tangentialVelocity;
                landedState = default;
                return VesselRegime.Flying;
            }

            landedState = LandShip(ship, latDeg, lonDeg, surfacePosition, surfaceVelocity, tangentialVelocity, normalSpeed, tangentialVelocity.Magnitude);
            return VesselRegime.Landed;
        }

        private static LandedState LandShip(Ship ship, double latDeg, double lonDeg, Vector3d surfacePosition, Vector3d surfaceVelocity, Vector3d tangentialVelocity, double normalSpeed, double tangentialSpeed)
        {
            ship.Position = surfacePosition;
            ship.Velocity = surfaceVelocity + tangentialVelocity;
            return new LandedState(latDeg, lonDeg, surfacePosition, ship.Velocity, normalSpeed, tangentialSpeed);
        }

        /// <summary>Surface height at a point (lat/lon, degrees) for the body model; sphere when null.</summary>
        private static double GroundAltitude(OrbitingBody body, double latDeg, double lonDeg)
        {
            return body.Terrain?.GetHeightMeters(body, latDeg * (Math.PI / 180d), lonDeg * (Math.PI / 180d)) ?? 0d;
        }

        /// <summary>
        /// Propellant depletion: mass = max(mass, dry) — a clamp with exact
        /// equality mass == dry after the reaction, not "not less than dry".
        /// Dry mass comes from the event's source detector.
        /// </summary>
        public static double ApplyDepletion(Ship ship, EventOccurrence occurrence)
        {
            if (ship == null)
                throw new ArgumentNullException(nameof(ship));
            if (occurrence.Kind != EventKind.PropellantDepleted)
                throw new ArgumentException("ApplyDepletion принимает только PropellantDepleted.", nameof(occurrence));

            var detector = occurrence.SourceDetector as PropellantDepletionDetector;
            if (detector == null)
            {
                throw new InvalidOperationException("У события PropellantDepleted нет детектора-источника.");
            }

            ship.Mass = Math.Max(ship.Mass, detector.DryMassKg);
            return ship.Mass;
        }

        public static AtmosphereResult ApplyAtmosphere(EventOccurrence occurrence)
        {
            if (occurrence.Kind != EventKind.AtmosphereEntry && occurrence.Kind != EventKind.AtmosphereExit)
            {
                throw new ArgumentException("ApplyAtmosphere принимает только вход/выход.", nameof(occurrence));
            }

            return new AtmosphereResult(occurrence.Kind == EventKind.AtmosphereEntry);
        }

        public readonly struct EphemerisEndResult
        {
            public readonly double TimeSeconds;

            public EphemerisEndResult(double timeSeconds)
            {
                TimeSeconds = timeSeconds;
            }
        }

        public static EphemerisEndResult ApplyEphemerisEnd(EventOccurrence occurrence)
        {
            if (occurrence.Kind != EventKind.EphemerisEnd)
            {
                throw new ArgumentException("ApplyEphemerisEnd принимает только EphemerisEnd.", nameof(occurrence));
            }

            return new EphemerisEndResult(occurrence.TimeSeconds);
        }

        public static FocusResult ApplySoi(EventOccurrence occurrence)
        {
            if (occurrence.Kind != EventKind.SoiChange)
            {
                throw new ArgumentException("ApplySoi принимает только SoiChange.", nameof(occurrence));
            }

            if (occurrence.Body == null)
            {
                throw new InvalidOperationException("У события SoiChange нет нового тела.");
            }

            return new FocusResult(occurrence.Body);
        }
    }
}
