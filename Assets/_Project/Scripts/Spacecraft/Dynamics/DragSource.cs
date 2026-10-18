using System;
using Galilego.Core;
using Galilego.Universe;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Physical drag contract (B1):
    ///   ρ(h)   = ρ0·exp(−h/H) from the body's AtmosphereProfile; h&lt;0 → ρ0 (total
    ///            function: trial RK sub-steps and bisection midpoints can be anywhere,
    ///            no exceptions); h ≥ Top → 0 by a HARD cut — the same boundary as the
    ///            Entry/Exit detectors (regime and force agree; the density discontinuity
    ///            at Top is deliberate, a sharp-cutoff model, not a bug).
    ///   v_atm  = bodyV + ω×r — atmosphere rigidly co-rotating with the body (no wind,
    ///            no heating: B1 is force only).
    ///   v_rel  = v_ship − v_atm. Key discipline (analogy to A4 with surface-relative
    ///            velocity): the ship's world velocity does NOT enter the formula.
    ///            A ship co-rotating with the atmosphere (v_rel=0) feels no drag
    ///            at any orbital velocity.
    ///   F      = −½·ρ·Cd·A·|v_rel|·v_rel — returned as Force (independent of mass;
    ///            division happens once in SpacecraftPhysics). MassFlow = 0.
    ///   Cd, A  — craft configuration (source fields), not body properties.
    /// Entry/Exit events are untouched: they are regime flags, drag is a continuous
    /// force. Blocking long warp inside the atmosphere follows automatically from the
    /// CanEnterLongWarp contribution check (a non-zero Force forbids entry).
    /// Pure, like the other sources.
    /// </summary>
    public sealed class DragSource : IDynamicsSource
    {
        private readonly OrbitingBody body;

        /// <summary>Drag coefficient (dimensionless).</summary>
        public double DragCoefficient = 1d;

        /// <summary>Reference area (m²).</summary>
        public double ReferenceAreaM2 = 10d;

        public DragSource(OrbitingBody body)
        {
            this.body = body ?? throw new ArgumentNullException(nameof(body));
        }

        public DynamicsContribution Evaluate(Vector3d position, Vector3d velocity, double mass, double timeSeconds)
        {
            AtmosphereProfile atmosphere = body.Atmosphere;
            if (atmosphere == null || DragCoefficient <= 0d || ReferenceAreaM2 <= 0d
                || atmosphere.ScaleHeightMeters <= 0d || atmosphere.SeaLevelDensityKgPerCubicMeter <= 0d)
            {
                return new DynamicsContribution(Vector3d.Zero, Vector3d.Zero, 0d, Vector3d.Zero);
            }

            body.EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out Vector3d bodyVelocity);
            Vector3d offset = position - bodyPosition;
            double altitude = offset.Magnitude - body.Radius;
            if (altitude >= atmosphere.TopAltitudeMeters)
            {
                return new DynamicsContribution(Vector3d.Zero, Vector3d.Zero, 0d, Vector3d.Zero);
            }

            if (altitude < 0d)
            {
                altitude = 0d;
            }

            double density = atmosphere.SeaLevelDensityKgPerCubicMeter * Math.Exp(-altitude / atmosphere.ScaleHeightMeters);
            Vector3d spinAxis = body.SpinAxis;
            double spinRate = body.SpinAngularSpeed;
            Vector3d atmosphereVelocity = bodyVelocity + Vector3d.Cross(spinAxis * spinRate, offset);
            Vector3d relativeVelocity = velocity - atmosphereVelocity;
            double relativeSpeed = relativeVelocity.Magnitude;
            if (relativeSpeed <= 0d || density <= 0d)
            {
                return new DynamicsContribution(Vector3d.Zero, Vector3d.Zero, 0d, Vector3d.Zero);
            }

            double dynamicPressure = 0.5d * density * relativeSpeed * relativeSpeed;
            Vector3d force = relativeVelocity * (-dynamicPressure * DragCoefficient * ReferenceAreaM2 / relativeSpeed);
            return new DynamicsContribution(Vector3d.Zero, force, 0d, Vector3d.Zero);
        }
    }
}
