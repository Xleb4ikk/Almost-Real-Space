using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Galilego.Core;
using Galilego.Events;
using Galilego.Spacecraft;
using Galilego.Universe;
using UnityEngine;

/// <summary>
/// Physics-parity probes for the PHYSICAL warp ladder (x1..x6): the same model time
/// must give the same trajectory, the same fuel burn and the same player motion,
/// only lived faster in real seconds. Runs through run_script in Play Mode.
/// Outside Assets on purpose: this file is not part of the game build.
/// </summary>
public static class WarpParityTests
{
    private static SimulationRunner R;
    private static VesselStep Step;
    private static MethodInfo StepPlayerMethod;

    private static bool Bind()
    {
        R = UnityEngine.Object.FindAnyObjectByType<SimulationRunner>();
        if (R == null)
        {
            return false;
        }

        FieldInfo field = typeof(SimulationRunner).GetField("step", BindingFlags.NonPublic | BindingFlags.Instance);
        Step = field != null ? field.GetValue(R) as VesselStep : null;
        StepPlayerMethod = typeof(SimulationRunner).GetMethod(
            "StepPlayer", BindingFlags.NonPublic | BindingFlags.Instance, null,
            new[] { typeof(double), typeof(double) }, null);
        return Step != null && StepPlayerMethod != null;
    }

    private static string F(double v)
    {
        return v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static OrbitingBody FindBody(string name)
    {
        for (int i = 0; i < R.SystemState.AllBodies.Count; i++)
        {
            if (R.SystemState.AllBodies[i].Name == name)
            {
                return R.SystemState.AllBodies[i];
            }
        }

        return null;
    }

    public static string Bodies()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        var sb = new StringBuilder("Bodies");
        foreach (OrbitingBody b in R.SystemState.AllBodies)
        {
            sb.Append("\n  ").Append(b.Name)
              .Append(" R=").Append(F(b.Radius))
              .Append(" atmTop=").Append(b.Atmosphere != null ? F(b.Atmosphere.TopAltitudeMeters) : "none")
              .Append(" mu=").Append(F(b.ResolveStandardGravitationalParameter()))
              .Append(" soi=").Append(F(b.SphereOfInfluenceRadius));
        }

        return sb.ToString();
    }

    /// <summary>Puts the ship on a near-circular orbit of the given body and clears the clock.</summary>
    private static void SetCircularOrbit(OrbitingBody body, double altitudeMeters)
    {
        double t = R.TimeSeconds;
        body.EvaluateWorldState(t, out Vector3d bp, out Vector3d bv);
        Vector3d radial = R.Ship.Position - bp;
        if (radial.SqrMagnitude < 1d)
        {
            radial = new Vector3d(1d, 0d, 0d);
        }

        radial = radial.Normalized;
        Vector3d tangent = Vector3d.Cross(body.SpinAxis, radial);
        if (tangent.SqrMagnitude < 1e-8d)
        {
            tangent = Vector3d.Cross(new Vector3d(0d, 1d, 0d), radial);
        }

        tangent = tangent.Normalized;
        double radius = body.Radius + altitudeMeters;
        double mu = body.ResolveStandardGravitationalParameter();
        double speed = Math.Sqrt(mu / radius);
        R.SetShipState(bp + (radial * radius), bv + (tangent * speed), VesselRegime.Flying);
        Step.ResetTimeAccumulator();
    }

    private struct Run
    {
        public int Factor;
        public double RealDt;
        public double ModelSeconds;
        public double DtModel;
        public double Mass;
        public double Fuel;
        public double Effective;
        public int OvershootFrames;
        public double OvershootTotal;
        public double FirstOvershoot;
        public double FirstFrameEnd;
        public double FirstFrameAfter;
        public Vector3d StartPosition;
        public Vector3d StartVelocity;
        public Vector3d Position;
        public Vector3d Velocity;
    }

    private static double FuelKg()
    {
        if (R.Propulsion == null)
        {
            return double.NaN;
        }

        var pools = new List<StagePoolState>();
        R.Propulsion.AppendState(pools);
        double sum = 0d;
        for (int i = 0; i < pools.Count; i++)
        {
            sum += pools[i].Kg;
        }

        return sum;
    }

    private static Run Simulate(
        SimulationRunner.WorldSnapshot start, int factor, double realDt, double modelSeconds, double throttle)
    {
        R.RestoreWorldSnapshot(start);
        R.Warp.SetPhysicalWarp(factor);
        R.Warp.ResetTicks(R.TimeSeconds);
        R.RawThrottle = throttle;
        Step.RawThrottle = throttle;

        var run = new Run
        {
            Factor = factor,
            RealDt = realDt,
            ModelSeconds = modelSeconds,
            StartPosition = R.Ship.Position,
            StartVelocity = R.Ship.Velocity
        };

        double t0 = R.TimeSeconds;
        int frames = (int)Math.Round(modelSeconds / (realDt * factor));
        for (int i = 0; i < frames; i++)
        {
            double frameEnd = R.TimeSeconds + (realDt * factor);
            Step.StepFlying((float)realDt, true);
            double over = R.TimeSeconds - frameEnd;
            if (over > 1e-9d)
            {
                if (run.OvershootFrames == 0)
                {
                    run.FirstOvershoot = over;
                    run.FirstFrameEnd = frameEnd;
                    run.FirstFrameAfter = R.TimeSeconds;
                }

                run.OvershootFrames++;
                run.OvershootTotal += over;
            }
        }

        run.DtModel = R.TimeSeconds - t0;
        run.Mass = R.Ship.Mass;
        run.Fuel = FuelKg();
        run.Effective = Step.LastEffectiveFactor;
        run.Position = R.Ship.Position;
        run.Velocity = R.Ship.Velocity;
        return run;
    }

    public static string Coast()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        OrbitingBody body = FindBody("Aeolus");
        if (body == null)
        {
            return "no Aeolus";
        }

        R.Paused = true;
        R.RawThrottle = 0d;
        Step.RawThrottle = 0d;
        R.Warp.SetPhysicalWarp(1);
        SetCircularOrbit(body, 1000000d);
        SimulationRunner.WorldSnapshot start = R.CaptureWorldSnapshot();
        double t0 = R.TimeSeconds;

        Run[] runs =
        {
            Simulate(start, 1, 1d / 60d, 600d, 0d),
            Simulate(start, 3, 1d / 60d, 600d, 0d),
            Simulate(start, 6, 1d / 60d, 600d, 0d),
            Simulate(start, 1, 1d / 30d, 600d, 0d),
            Simulate(start, 3, 1d / 30d, 600d, 0d),
            Simulate(start, 6, 1d / 30d, 600d, 0d),
            Simulate(start, 1, 1d / 20d, 600d, 0d),
            Simulate(start, 6, 1d / 20d, 600d, 0d)
        };

        var sb = new StringBuilder("Coast t0=" + F(t0) + " body=" + body.Name);
        for (int i = 0; i < runs.Length; i++)
        {
            Run r = runs[i];
            sb.Append("\n  x").Append(r.Factor).Append("@").Append(F(r.RealDt))
              .Append(" dtModel=").Append(F(r.DtModel))
              .Append(" eff=").Append(F(r.Effective))
              .Append(" dP=").Append(F((r.Position - r.StartPosition).Magnitude))
              .Append(" dV=").Append(F((r.Velocity - r.StartVelocity).Magnitude));
        }

        sb.Append("\n  diff x3@60 - x1@60 = ").Append(F((runs[1].Position - runs[0].Position).Magnitude))
          .Append(" m, ").Append(F((runs[1].Velocity - runs[0].Velocity).Magnitude)).Append(" m/s");
        sb.Append("\n  diff x6@60 - x1@60 = ").Append(F((runs[2].Position - runs[0].Position).Magnitude))
          .Append(" m, ").Append(F((runs[2].Velocity - runs[0].Velocity).Magnitude)).Append(" m/s");
        sb.Append("\n  diff x3@30 - x1@30 = ").Append(F((runs[4].Position - runs[3].Position).Magnitude))
          .Append(" m, ").Append(F((runs[4].Velocity - runs[3].Velocity).Magnitude)).Append(" m/s");
        sb.Append("\n  diff x6@30 - x1@30 = ").Append(F((runs[5].Position - runs[3].Position).Magnitude))
          .Append(" m, ").Append(F((runs[5].Velocity - runs[3].Velocity).Magnitude)).Append(" m/s");
        sb.Append("\n  baseline x1@30 - x1@60 = ").Append(F((runs[3].Position - runs[0].Position).Magnitude)).Append(" m");
        sb.Append("\n  x3 vs x6 @60 = ").Append(F((runs[1].Position - runs[2].Position).Magnitude)).Append(" m");
        sb.Append("\n  x1@20 - x3@60 = ").Append(F((runs[6].Position - runs[1].Position).Magnitude)).Append(" m");
        sb.Append("\n  x1@20 - x1@60 = ").Append(F((runs[6].Position - runs[0].Position).Magnitude)).Append(" m");
        sb.Append("\n  x6@20 - x6@60 = ").Append(F((runs[7].Position - runs[2].Position).Magnitude)).Append(" m");
        sb.Append("\n  x1@20 - x6@20 = ").Append(F((runs[6].Position - runs[7].Position).Magnitude)).Append(" m");
        return sb.ToString();
    }

    public static string Thrust()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        OrbitingBody body = FindBody("Aeolus");
        if (body == null)
        {
            return "no Aeolus";
        }

        R.Paused = true;
        R.RawThrottle = 0d;
        Step.RawThrottle = 0d;
        R.Warp.SetPhysicalWarp(1);
        SetCircularOrbit(body, 1000000d);
        if (R.Propulsion != null && R.Propulsion.ActiveStageIndex < 0)
        {
            R.Propulsion.Stage();
        }

        SimulationRunner.WorldSnapshot start = R.CaptureWorldSnapshot();

        double burnSeconds = 20d;
        double ve = R.Propulsion != null && R.Propulsion.ThrustN > 0d && R.Propulsion.MassFlowKgS > 0d
            ? R.Propulsion.ThrustN / R.Propulsion.MassFlowKgS
            : R.MainIspSeconds * 9.80665d;
        Run[] coast =
        {
            Simulate(start, 1, 1d / 60d, burnSeconds, 0d),
            Simulate(start, 1, 1d / 30d, burnSeconds, 0d),
            Simulate(start, 1, 1d / 20d, burnSeconds, 0d),
            Simulate(start, 3, 1d / 60d, burnSeconds, 0d),
            Simulate(start, 6, 1d / 60d, burnSeconds, 0d),
            Simulate(start, 3, 1d / 61d, burnSeconds, 0d)
        };
        Run[] burn =
        {
            Simulate(start, 1, 1d / 60d, burnSeconds, 1d),
            Simulate(start, 1, 1d / 30d, burnSeconds, 1d),
            Simulate(start, 1, 1d / 20d, burnSeconds, 1d),
            Simulate(start, 3, 1d / 60d, burnSeconds, 1d),
            Simulate(start, 6, 1d / 60d, burnSeconds, 1d),
            Simulate(start, 3, 1d / 61d, burnSeconds, 1d)
        };

        var sb = new StringBuilder("Thrust " + F(burnSeconds) + " s, legacy thruster=" + F(R.MainThrustNewtons) + " N"
            + " prop=" + (R.Propulsion != null ? "staged" : "null"));
        for (int i = 0; i < burn.Length; i++)
        {
            Vector3d dv = (burn[i].Velocity - burn[i].StartVelocity) - (coast[i].Velocity - coast[i].StartVelocity);
            double tsiolkovsky = ve * Math.Log(start.ShipMass / burn[i].Mass);
            sb.Append("\n  x").Append(burn[i].Factor).Append("@").Append(F(burn[i].RealDt))
              .Append(" dtModel=").Append(F(burn[i].DtModel))
              .Append(" overFrames=").Append(burn[i].OvershootFrames)
              .Append(" overTotal=").Append(F(burn[i].OvershootTotal))
              .Append(" firstOver=").Append(F(burn[i].FirstOvershoot))
              .Append(" eff=").Append(F(burn[i].Effective))
              .Append(" dv=").Append(F(dv.Magnitude))
              .Append(" |dv|=").Append(F(dv.Magnitude))
              .Append(" mass=").Append(F(burn[i].Mass))
              .Append(" fuel=").Append(F(burn[i].Fuel))
              .Append(" tsiol=").Append(F(tsiolkovsky));
        }

        Vector3d dv1 = (burn[0].Velocity - burn[0].StartVelocity) - (coast[0].Velocity - coast[0].StartVelocity);
        Vector3d dv30 = (burn[1].Velocity - burn[1].StartVelocity) - (coast[1].Velocity - coast[1].StartVelocity);
        Vector3d dv20 = (burn[2].Velocity - burn[2].StartVelocity) - (coast[2].Velocity - coast[2].StartVelocity);
        Vector3d dv3 = (burn[3].Velocity - burn[3].StartVelocity) - (coast[3].Velocity - coast[3].StartVelocity);
        Vector3d dv6 = (burn[4].Velocity - burn[4].StartVelocity) - (coast[4].Velocity - coast[4].StartVelocity);
        sb.Append("\n  x1@30-x1@60 dv = ").Append(F((dv30 - dv1).Magnitude)).Append(" m/s")
          .Append("  dm = ").Append(F(Math.Abs(burn[1].Mass - burn[0].Mass))).Append(" kg");
        sb.Append("\n  x1@20-x1@60 dv = ").Append(F((dv20 - dv1).Magnitude)).Append(" m/s")
          .Append("  dm = ").Append(F(Math.Abs(burn[2].Mass - burn[0].Mass))).Append(" kg");
        sb.Append("\n  x3@60-x1@60 dv = ").Append(F((dv3 - dv1).Magnitude)).Append(" m/s")
          .Append("  dm = ").Append(F(Math.Abs(burn[3].Mass - burn[0].Mass))).Append(" kg");
        sb.Append("\n  x6@60-x1@60 dv = ").Append(F((dv6 - dv1).Magnitude)).Append(" m/s")
          .Append("  dm = ").Append(F(Math.Abs(burn[4].Mass - burn[0].Mass))).Append(" kg");
        return sb.ToString();
    }

    /// <summary>
    /// The realistic switch: the throttle was sampled by a physical frame first,
    /// then the player selects Full x1000 without releasing it.
    /// </summary>
    public static string FullWarpThrottleSequence()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        OrbitingBody body = FindBody("Aeolus");
        if (body == null)
        {
            return "no Aeolus";
        }

        R.Paused = true;
        R.Warp.SetPhysicalWarp(1);
        R.RawThrottle = 1d;
        Step.RawThrottle = 1d;
        SetCircularOrbit(body, 1000000d);
        SimulationRunner.WorldSnapshot start = R.CaptureWorldSnapshot();

        var sb = new StringBuilder("FullWarpThrottleSequence");
        R.RestoreWorldSnapshot(start);
        R.Warp.SetPhysicalWarp(1);
        R.Warp.ResetTicks(R.TimeSeconds);
        R.RawThrottle = 1d;
        Step.RawThrottle = 1d;

        // One second of normal physics so the control snapshot really holds the gas.
        double t = R.TimeSeconds;
        double mStart = R.Ship.Mass;
        Step.StepFlying(1f, true);
        sb.Append(" physical: dt=").Append(F(R.TimeSeconds - t))
          .Append(" eff=").Append(F(Step.LastEffectiveFactor))
          .Append(" burned=").Append(F(mStart - R.Ship.Mass)).Append(" kg");

        // Now x1000 while the gas is still held.
        R.Warp.SetFullWarp(1000d);
        t = R.TimeSeconds;
        double m0 = R.Ship.Mass;
        Step.StepFlying(1f, true);
        sb.Append("\n  x1000 gas held: dt=").Append(F(R.TimeSeconds - t))
          .Append(" eff=").Append(F(Step.LastEffectiveFactor))
          .Append(" burned=").Append(F(m0 - R.Ship.Mass)).Append(" kg")
          .Append(" mode=").Append(R.Warp.Mode);

        // Release the gas: after the next control tick the snapshot is idle.
        R.RawThrottle = 0d;
        Step.RawThrottle = 0d;
        t = R.TimeSeconds;
        m0 = R.Ship.Mass;
        Step.StepFlying(1f, true);
        sb.Append("\n  x1000 gas released: dt=").Append(F(R.TimeSeconds - t))
          .Append(" eff=").Append(F(Step.LastEffectiveFactor))
          .Append(" burned=").Append(F(m0 - R.Ship.Mass)).Append(" kg")
          .Append(" mode=").Append(R.Warp.Mode);

        // Engine lit again while already in long warp.
        R.RawThrottle = 1d;
        Step.RawThrottle = 1d;
        t = R.TimeSeconds;
        m0 = R.Ship.Mass;
        Step.StepFlying(1f, true);
        sb.Append("\n  x1000 gas pressed in warp: dt=").Append(F(R.TimeSeconds - t))
          .Append(" eff=").Append(F(Step.LastEffectiveFactor))
          .Append(" burned=").Append(F(m0 - R.Ship.Mass)).Append(" kg")
          .Append(" mode=").Append(R.Warp.Mode);

        R.RawThrottle = 0d;
        Step.RawThrottle = 0d;
        return sb.ToString();
    }

    /// <summary>Reproduces the cheat "orbit around Terra at 200 km" and explains R1.</summary>
    public static string CheatOrbitDiag()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        CheatMenu cheat = UnityEngine.Object.FindAnyObjectByType<CheatMenu>();
        if (cheat == null)
        {
            return "no cheat menu";
        }

        OrbitingBody terra = FindBody("Terra");
        if (terra == null)
        {
            return "no Terra";
        }

        R.Paused = false;
        R.BuildMode = false;
        Galilego.Simulation.ContactZone.ContactZoneHost zone =
            UnityEngine.Object.FindAnyObjectByType<Galilego.Simulation.ContactZone.ContactZoneHost>();
        if (zone != null)
        {
            zone.ForceExit();
        }

        cheat.OrbitAltitudeMeters = 200000d;
        cheat.OrbitCapsule();
        R.Paused = true;
        R.RawThrottle = 0d;
        Step.RawThrottle = 0d;
        R.Warp.SetPhysicalWarp(1);
        Step.ResetTimeAccumulator();

        double t0 = R.TimeSeconds;
        terra.EvaluateWorldState(t0, out Vector3d bp, out Vector3d bv);
        Vector3d rel = R.Ship.Position - bp;
        Vector3d relVel = R.Ship.Velocity - bv;
        double r0 = rel.Magnitude;
        Vector3d gravity = R.SystemState.EvaluateShipAcceleration(R.Ship.Position, t0);
        double muField = gravity.Magnitude * r0 * r0;
        double muDeclared = terra.ResolveStandardGravitationalParameter();
        OrbitalElements eDeclared = OrbitalElements.FromState(rel, relVel, muDeclared);
        OrbitalElements eBook = OrbitalElements.FromState(rel, relVel, muField);

        var sb = new StringBuilder("CheatOrbitDiag t0=" + F(t0) + " body=" + terra.Name
            + " r=" + F(r0) + " alt=" + F(r0 - terra.Radius)
            + " atm=" + R.SystemState.IsInsideAtmosphere(R.Ship.Position, t0)
            + "\n  muDeclared=" + F(muDeclared) + " muField=|a|r2=" + F(muField)
            + " ratio=" + F(muField / muDeclared)
            + "\n  e(declared)=" + F(eDeclared.Eccentricity) + " peri=" + F(eDeclared.SemiMajorAxis * (1d - eDeclared.Eccentricity) - terra.Radius)
            + "\n  e(field)=" + F(eBook.Eccentricity) + " periAlt=" + F(eBook.SemiMajorAxis * (1d - eBook.Eccentricity) - terra.Radius));

        SimulationRunner.WorldSnapshot start = R.CaptureWorldSnapshot();
        for (int k = 0; k < 2; k++)
        {
            int factor = k == 0 ? 1 : 6;
            R.RestoreWorldSnapshot(start);
            R.Warp.SetPhysicalWarp(factor);
            R.Warp.ResetTicks(R.TimeSeconds);
            R.RawThrottle = 0d;
            Step.RawThrottle = 0d;
            double minAlt = double.PositiveInfinity;
            int cappedFrames = 0;
            int frames = (int)Math.Round(600d / ((1d / 60d) * factor));
            for (int i = 0; i < frames; i++)
            {
                Step.StepFlying(1f / 60f, true);
                terra.EvaluateWorldState(R.TimeSeconds, out Vector3d bpNow, out _);
                double alt = (R.Ship.Position - bpNow).Magnitude - terra.Radius;
                if (alt < minAlt)
                {
                    minAlt = alt;
                }

                if (Step.LastEffectiveFactor < factor - 1e-9d)
                {
                    cappedFrames++;
                }
            }

            sb.Append("\n  x").Append(factor).Append(": dtModel=").Append(F(R.TimeSeconds - t0))
              .Append(" minAlt=").Append(F(minAlt))
              .Append(" cappedFrames=").Append(cappedFrames).Append("/").Append(frames)
              .Append(" eff=").Append(F(Step.LastEffectiveFactor));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Regression for the gameplay gate: physical warp MUST survive an active engine
    /// in free flight. The parked-surface path may still cut warp on throttle, but it
    /// must only do so when the craft is actually parked (zone/Landed), not in the air.
    /// </summary>
    public static string ParkedGate()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        MethodInfo parked = typeof(SimulationRunner).GetMethod(
            "TryParkedWarpStep", BindingFlags.NonPublic | BindingFlags.Instance);
        if (parked == null)
        {
            return "no TryParkedWarpStep";
        }

        OrbitingBody body = FindBody("Aeolus");
        if (body == null)
        {
            return "no Aeolus";
        }

        R.Paused = true;
        R.Warp.SetPhysicalWarp(1);
        R.RawThrottle = 0d;
        Step.RawThrottle = 0d;
        SetCircularOrbit(body, 1000000d);

        var sb = new StringBuilder("ParkedGate");

        // 1. Free flight, engine lit: the parked path must not touch the warp.
        Step.Regime = VesselRegime.Flying;
        Step.RawThrottle = 1d;
        R.RawThrottle = 1d;
        R.Warp.SetPhysicalWarp(3);
        int resetsBefore = R.ParkedWarpThrottleResets;
        bool parkedResult = (bool)parked.Invoke(R, new object[] { 1f / 60f });
        sb.Append("\n  flying: handled=").Append(parkedResult)
          .Append(" mode=").Append(R.Warp.Mode)
          .Append(" factor=").Append(F(R.Warp.Factor))
          .Append(" resets=").Append(R.ParkedWarpThrottleResets - resetsBefore);

        // 2. Landed pad, engine lit: the parked path must cut warp to x1 (as designed).
        Step.Regime = VesselRegime.Landed;
        R.Warp.SetPhysicalWarp(3);
        resetsBefore = R.ParkedWarpThrottleResets;
        parkedResult = (bool)parked.Invoke(R, new object[] { 1f / 60f });
        sb.Append("\n  landed+gas: handled=").Append(parkedResult)
          .Append(" mode=").Append(R.Warp.Mode)
          .Append(" factor=").Append(F(R.Warp.Factor))
          .Append(" resets=").Append(R.ParkedWarpThrottleResets - resetsBefore);

        Step.Regime = VesselRegime.Flying;
        Step.RawThrottle = 0d;
        R.RawThrottle = 0d;
        return sb.ToString();
    }

    public static string AtmosphereCap()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        OrbitingBody body = null;
        for (int i = 0; i < R.SystemState.AllBodies.Count; i++)
        {
            OrbitingBody candidate = R.SystemState.AllBodies[i];
            if (candidate.Atmosphere != null && candidate.Atmosphere.TopAltitudeMeters > 1000d)
            {
                body = candidate;
                break;
            }
        }

        if (body == null)
        {
            return "no atmosphere body";
        }

        R.Paused = true;
        R.RawThrottle = 0d;
        Step.RawThrottle = 0d;
        R.Warp.SetPhysicalWarp(1);

        double t = R.TimeSeconds;
        body.EvaluateWorldState(t, out Vector3d bp, out Vector3d bv);
        Vector3d radial = R.Ship.Position - bp;
        if (radial.SqrMagnitude < 1d)
        {
            radial = new Vector3d(1d, 0d, 0d);
        }

        radial = radial.Normalized;
        double altitude = Math.Min(0.5d * body.Atmosphere.TopAltitudeMeters, 20000d);
        R.SetShipState(bp + (radial * (body.Radius + altitude)), bv, VesselRegime.Flying);
        Step.ResetTimeAccumulator();
        SimulationRunner.WorldSnapshot start = R.CaptureWorldSnapshot();

        double t0 = R.TimeSeconds;
        R.RestoreWorldSnapshot(start);
        R.Warp.SetPhysicalWarp(1);
        R.RawThrottle = 0d;
        Step.RawThrottle = 0d;
        Step.StepFlying(1f, true);
        double dt1 = R.TimeSeconds - t0;
        double eff1 = Step.LastEffectiveFactor;

        R.RestoreWorldSnapshot(start);
        R.Warp.SetPhysicalWarp(6);
        R.RawThrottle = 0d;
        Step.RawThrottle = 0d;
        Step.StepFlying(1f, true);
        double dt6 = R.TimeSeconds - t0;
        double eff6 = Step.LastEffectiveFactor;

        return "AtmosphereCap body=" + body.Name + " alt=" + F(altitude) + " m, 1 real second"
            + "\n  x1: dtModel=" + F(dt1) + " eff=" + F(eff1)
            + "\n  x6: dtModel=" + F(dt6) + " eff=" + F(eff6);
    }

    /// <summary>
    /// EVA on the physical ladder: the player must move through the same model time
    /// with the same relative motion as at x1 (jetpack + horizontal input).
    /// </summary>
    public static string Eva()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        OrbitingBody body = FindBody("Aeolus");
        if (body == null)
        {
            return "no Aeolus";
        }

        R.Paused = true;
        R.RawThrottle = 0d;
        Step.RawThrottle = 0d;
        R.Warp.SetPhysicalWarp(1);
        SetCircularOrbit(body, 1000000d);

        int[] factors = { 1, 1, 3, 6, 3, 6 };
        double[] dts = { 1d / 60d, 1d / 10d, 1d / 60d, 1d / 60d, 1d / 10d, 1d / 10d };
        double modelSeconds = 30d;
        var sb = new StringBuilder("Eva " + F(modelSeconds) + " model s, jetpack 0.5 m/s2, air 2 m/s");

        FieldInfo frameShipStartPositionField =
            typeof(SimulationRunner).GetField("frameShipStartPosition", BindingFlags.NonPublic | BindingFlags.Instance);
        FieldInfo frameShipStartTimeField =
            typeof(SimulationRunner).GetField("frameShipStartTime", BindingFlags.NonPublic | BindingFlags.Instance);
        FieldInfo evaRelativeValidField =
            typeof(SimulationRunner).GetField("evaRelativeValid", BindingFlags.NonPublic | BindingFlags.Instance);
        FieldInfo evaRelativeVelocityField =
            typeof(SimulationRunner).GetField("evaRelativeVelocity", BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo evaFrameVelocityMethod = typeof(SimulationRunner).GetMethod(
            "EvaFrameVelocity", BindingFlags.NonPublic | BindingFlags.Instance);

        // Make sure the player is really aboard before the snapshot: otherwise the
        // first exit is a no-op and we would step a stale far-away EVA player.
        R.SetPlayerOnSurface(R.Ship.Position, R.Ship.Velocity);
        R.TryEnterShip(1e6d);
        StepPlayerMethod.Invoke(R, new object[] { R.TimeSeconds, 0.5d });
        SimulationRunner.WorldSnapshot aboard = R.CaptureWorldSnapshot();

        Vector3d referenceOffset = Vector3d.Zero;
        Vector3d referenceRelVel = Vector3d.Zero;
        for (int k = 0; k < factors.Length; k++)
        {
            int factor = factors[k];
            double realDt = dts[k];
            R.RestoreWorldSnapshot(aboard);
            R.Warp.SetPhysicalWarp(factor);
            R.Warp.ResetTicks(R.TimeSeconds);
            R.RawThrottle = 0d;
            Step.RawThrottle = 0d;
            R.TryExitShip(new Vector3d(1d, 0d, 0d), 0d);
            evaRelativeValidField.SetValue(R, false);
            R.PlayerIntent.AirVelocity = new Vector3d(0d, 1d, 0d) * 2d;
            R.PlayerIntent.JetpackAccel = new Vector3d(0d, 0d, 1d) * 0.5d;
            R.PlayerIntent.JetpackToggle = true;

            if (k == 0)
            {
                Vector3d frameVel0 = (Vector3d)evaFrameVelocityMethod.Invoke(R, new object[] { R.TimeSeconds });
                sb.Append("\n  setup: mode=").Append(R.PlayerMode)
                  .Append(" exitOk=").Append(R.PlayerMode == PlayerMode.EVA)
                  .Append(" dist=").Append(F((R.PlayerPosition - R.Ship.Position).Magnitude))
                  .Append(" |vShip|=").Append(F(R.Ship.Velocity.Magnitude))
                  .Append(" |vPlayer|=").Append(F(R.PlayerVelocity.Magnitude))
                  .Append(" relValid=").Append(evaRelativeValidField.GetValue(R))
                  .Append(" relVel=").Append(F(((Vector3d)evaRelativeVelocityField.GetValue(R)).Magnitude))
                  .Append(" frameVel=").Append(F(frameVel0.Magnitude));
            }

            double t0 = R.TimeSeconds;
            int frames = (int)Math.Round(modelSeconds / (realDt * factor));
            for (int i = 0; i < frames; i++)
            {
                double frameStart = R.TimeSeconds;
                frameShipStartPositionField.SetValue(R, R.Ship.Position);
                frameShipStartTimeField.SetValue(R, frameStart);
                R.PlayerIntent.AirVelocity = new Vector3d(0d, 1d, 0d) * 2d;
                R.PlayerIntent.JetpackAccel = new Vector3d(0d, 0d, 1d) * 0.5d;
                Step.StepFlying((float)realDt, false);
                StepPlayerMethod.Invoke(R, new object[] { frameStart, realDt });
            }

            Vector3d offset = R.PlayerPosition - R.Ship.Position;
            Vector3d relVel = R.PlayerVelocity - R.Ship.Velocity;
            if (k == 0)
            {
                referenceOffset = offset;
                referenceRelVel = relVel;
            }

            sb.Append("\n  x").Append(factor).Append("@").Append(F(realDt))
              .Append(" jetpack=").Append(R.JetpackActive)
              .Append(" dtModel=").Append(F(R.TimeSeconds - t0))
              .Append(" eff=").Append(F(Step.LastEffectiveFactor))
              .Append(" |offset|=").Append(F(offset.Magnitude))
              .Append(" |relVel|=").Append(F(relVel.Magnitude))
              .Append(" offsetErrVsX1=").Append(F((offset - referenceOffset).Magnitude))
              .Append(" relVelErrVsX1=").Append(F((relVel - referenceRelVel).Magnitude))
              .Append(" relUpVel=").Append(F(Vector3d.Dot(relVel, new Vector3d(0d, 0d, 1d))));
        }

        return sb.ToString();
    }
}
