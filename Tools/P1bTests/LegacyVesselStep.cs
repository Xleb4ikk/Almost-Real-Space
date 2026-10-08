using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Debris;
using Galilego.Events;
using Galilego.Spacecraft;
using Galilego.Universe;
using Ship = Galilego.Spacecraft.Spacecraft;

// ВРЕМЕННЫЙ СЛЕПОК логики шага SimulationRunner (StepFlying, StepLanded,
// HandleOccurrence, ApplyBreakup, NormalizeAssembly) ДО выноса (шаг 0.4 ТЗ v2).
// Нужен ровно один раз: трейсы S1–S5, снятые через него, сравниваются побайтно
// с трейсами вынесенного шага. После проверки удаляется. Это не продакшн-код.
internal sealed class LegacyVesselStep
{
    public readonly StarSystem SystemState;
    public readonly WarpController Warp;
    public readonly EventDrivenPropagator Propagator;
    public readonly LongWarpDriver Driver;
    public readonly ThrustSource MainThrust;
    public readonly IBreakupModel BreakupModel;
    public readonly DebrisPool Debris;
    public readonly PartDefinition[] Parts;
    public readonly double SpawnEpsilonMeters;
    public readonly double DebrisLifetimeSeconds;

    public Ship Ship;
    public VesselRegime Regime;
    public OrbitingBody DominantBody;
    public double TimeSeconds { get; private set; }
    public double RawThrottle;

    /// <summary>Наблюдаемость для трейсов: вызывается после каждого обработанного события.</summary>
    public Action<EventOccurrence> OccurrenceHandled;

    private KahanAccumulator time = new KahanAccumulator(0d);
    private static readonly SphericalTerrain surfaceTerrain = new SphericalTerrain();
    private const double SurfaceMaxStepSeconds = 0.5d;

    public LegacyVesselStep(
        StarSystem systemState, WarpController warp, EventDrivenPropagator propagator,
        LongWarpDriver driver, ThrustSource mainThrust, IBreakupModel breakupModel,
        DebrisPool debris, PartDefinition[] parts, double spawnEpsilonMeters, double debrisLifetimeSeconds)
    {
        SystemState = systemState;
        Warp = warp;
        Propagator = propagator;
        Driver = driver;
        MainThrust = mainThrust;
        BreakupModel = breakupModel;
        Debris = debris;
        Parts = parts;
        SpawnEpsilonMeters = spawnEpsilonMeters;
        DebrisLifetimeSeconds = debrisLifetimeSeconds;
        TimeSeconds = 0d;
        Regime = VesselRegime.Flying;
    }

    public void ResetTimeAccumulator()
    {
        time.Reset(TimeSeconds);
    }

    public void StepFlying(float realDt, bool playerInShip)
    {
        bool dbg = Environment.GetEnvironmentVariable("P1B_TRACE_DEBUG") == "1";
        if (dbg)
        {
            Console.WriteLine("    [sf] computeFrame t=" + TimeSeconds.ToString("F3"));
        }

        WarpFrameDecision decision = EphemerisRuntime.ComputeFrame(
            SystemState, Warp, Ship.Position, TimeSeconds, realDt);
        if (dbg)
        {
            Console.WriteLine("    [sf] factor=" + decision.EffectiveFactor + " long=" + decision.UseLongWarp);
        }
        double playerCap = playerInShip ? double.PositiveInfinity : 4d;
        double factor = Math.Min(decision.EffectiveFactor, playerCap);
        double frameEnd = TimeSeconds + (realDt * factor);

        if (decision.UseLongWarp && playerCap == double.PositiveInfinity)
        {
            Warp.PrepareForLongWarp(TimeSeconds);
            Driver.CancelRequested = false;
            LongWarpResult result = Driver.AdvanceToTarget(Ship, TimeSeconds, frameEnd);
            TimeSeconds = result.ReachedTimeSeconds;
            time.Reset(TimeSeconds);
            return;
        }

        double throttle = Warp.EffectiveThrottle(RawThrottle, factor);
        bool thrustActive = MainThrust != null && throttle > 0d;
        while (TimeSeconds < frameEnd - 1e-12d)
        {
            double chunk = Warp.GetMaxChunkSeconds(TimeSeconds, OrbitIntegrator.DefaultMaxStepSize, thrustActive);
            chunk = Math.Min(chunk, frameEnd - TimeSeconds);
            if (chunk <= 0d)
            {
                chunk = Math.Min(OrbitIntegrator.DefaultMinStepSize, frameEnd - TimeSeconds);
            }

            Warp.SampleControl(TimeSeconds, throttle);
            if (dbg)
            {
                Console.WriteLine("    [sf] propagate chunk=" + chunk.ToString("F6"));
            }

            EventOccurrence? occurrence = Propagator.Propagate(Ship, TimeSeconds, chunk);
            if (dbg)
            {
                Console.WriteLine("    [sf] propagate done, occ=" + (occurrence.HasValue ? occurrence.Value.Kind.ToString() : "нет"));
            }

            if (occurrence.HasValue)
            {
                TimeSeconds = occurrence.Value.TimeSeconds;
                time.Reset(TimeSeconds);
                HandleOccurrence(occurrence.Value);
                if (Regime != VesselRegime.Flying)
                {
                    return;
                }
            }
            else
            {
                time.Add(chunk);
                TimeSeconds = time.Sum;
            }
        }
    }

    public void StepLanded(float realDt, bool playerInShip)
    {
        WarpFrameDecision decision = EphemerisRuntime.ComputeFrame(
            SystemState, Warp, Ship.Position, TimeSeconds, realDt);
        double playerCap = playerInShip ? double.PositiveInfinity : 4d;
        double factor = Math.Min(Math.Min(decision.EffectiveFactor, WarpController.AtmosphereMaxWarpFactor), playerCap);
        double frameEnd = TimeSeconds + (realDt * factor);
        double throttle = Warp.EffectiveThrottle(RawThrottle, factor);
        bool thrustActive = MainThrust != null && throttle > 0d;

        while (TimeSeconds < frameEnd - 1e-12d)
        {
            if (thrustActive)
            {
                Vector3d gravity = SystemState.EvaluateShipAcceleration(Ship.Position, TimeSeconds);
                DynamicsContribution thrust = MainThrust.Evaluate(Ship.Position, Ship.Velocity, Ship.Mass, TimeSeconds);
                if (EventReactions.TryLiftoff(Ship, DominantBody, thrust.Force / Ship.Mass, gravity, TimeSeconds, out _)
                    == VesselRegime.Flying)
                {
                    Regime = VesselRegime.Flying;
                    Warp.ResetTicks(TimeSeconds);
                    time.Reset(TimeSeconds);
                    return;
                }
            }

            double chunk = Warp.GetMaxChunkSeconds(TimeSeconds, SurfaceMaxStepSeconds, thrustActive);
            chunk = Math.Min(chunk, frameEnd - TimeSeconds);
            if (chunk <= 0d)
            {
                chunk = Math.Min(SurfaceMaxStepSeconds, frameEnd - TimeSeconds);
            }

            Warp.SampleControl(TimeSeconds, throttle);
            SurfaceMotionResult result = SurfaceMotion.Step(
                Ship.Position, Ship.Velocity, DominantBody, surfaceTerrain,
                (position, t) => SystemState.EvaluateShipAcceleration(position, t),
                TimeSeconds, chunk);
            Ship.Position = result.Position;
            Ship.Velocity = result.Velocity;
            time.Add(chunk);
            TimeSeconds = time.Sum;
            if (result.Regime == VesselRegime.Flying)
            {
                Regime = VesselRegime.Flying;
                Warp.ResetTicks(TimeSeconds);
                return;
            }
        }
    }

    private void HandleOccurrence(EventOccurrence occurrence)
    {
        // Хук ДО мутаций: наблюдатель видит состояние удара (скорость/позицию
        // в момент события), а не результат реакции.
        OccurrenceHandled?.Invoke(occurrence);
        switch (occurrence.Kind)
        {
            case EventKind.Touchdown:
                TouchdownOutcome outcome = EventReactions.ApplyTouchdown(
                    Ship, occurrence, occurrence.Body, BreakupModel, SpawnEpsilonMeters,
                    NormalizeAssembly(Ship.Mass));
                if (outcome.IsLanding)
                {
                    Regime = VesselRegime.Landed;
                    DominantBody = occurrence.Body;
                    Warp.ResetTicks(occurrence.TimeSeconds);
                }
                else
                {
                    ApplyBreakup(occurrence, outcome.Specs);
                }

                break;
            case EventKind.PropellantDepleted:
                EventReactions.ApplyDepletion(Ship, occurrence);
                break;
            case EventKind.EphemerisEnd:
                break;
        }
    }

    private List<Part> NormalizeAssembly(double shipMass)
    {
        if (Parts == null || Parts.Length == 0)
        {
            return null;
        }

        double definedMass = 0d;
        for (int i = 0; i < Parts.Length; i++)
        {
            definedMass += Parts[i].MassKg;
        }

        if (definedMass <= 0d)
        {
            return null;
        }

        double scale = shipMass / definedMass;
        var assembly = new List<Part>(Parts.Length);
        for (int i = 0; i < Parts.Length; i++)
        {
            assembly.Add(new Part(
                Parts[i].MassKg * scale,
                new Vector3d(Parts[i].Offset.x, Parts[i].Offset.y, Parts[i].Offset.z),
                Parts[i].JointStrengthNewtons));
        }

        return assembly;
    }

    private void ApplyBreakup(EventOccurrence occurrence, System.Collections.Generic.IReadOnlyList<PartSeparationSpec> specs)
    {
        double brokenMass = 0d;
        for (int i = 0; i < specs.Count; i++)
        {
            Debris.Spawn(specs[i].Position, specs[i].Velocity, specs[i].Mass,
                occurrence.TimeSeconds, DebrisLifetimeSeconds);
            brokenMass += specs[i].Mass;
        }

        double survivorMass = Ship.Mass - brokenMass;
        DominantBody = occurrence.Body;
        if (survivorMass > 1e-9d)
        {
            Ship.Mass = survivorMass;
            Regime = VesselRegime.Landed;
            Warp.ResetTicks(occurrence.TimeSeconds);
        }
        else
        {
            Regime = VesselRegime.Destroyed;
            Warp.SetWarpFactor(WarpController.MinWarpFactor);
        }
    }
}
