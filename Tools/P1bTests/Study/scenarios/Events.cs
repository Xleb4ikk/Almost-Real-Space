using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Galilego.Core;
using Galilego.Events;
using Galilego.Spacecraft;
using Galilego.Universe;

internal static partial class P1bTests
{    // ------------------------------------------------------------------
    // Live n-body cost
    // ------------------------------------------------------------------

    private static void StudyLiveCost(StudyNBody custom, StudyNBody real)
    {
        Console.WriteLine();
        Console.WriteLine("=== Live n-body cost (Y6 7200 s, 1 year) ===");
        var c = StudyRefRun(custom, "Y6", 7200d, 1d, 0d);
        var r = StudyRefRun(real, "Y6", 7200d, 1d, 0d);
        Console.WriteLine("custom 6 bodies: calls={0:N0}, {1:F1} ms/year", c.Calls, c.Ms);
        Console.WriteLine("real 7 bodies:   calls={0:N0}, {1:F1} ms/year", r.Calls, r.Ms);
    }

    // ------------------------------------------------------------------
    // Event / warp path
    // ------------------------------------------------------------------

    private static void StudyEvent(StarSystem sys)
    {
        Console.WriteLine();
        Console.WriteLine("=== Warp event path (custom Terra, atmosphere shell 2e6 m) ===");
        OrbitingBody terra = sys.AllBodies[1];
        double mu = terra.ResolveStandardGravitationalParameter();
        double ra = 4.0e7d;
        double t0 = StudyShipT0;
        sys.EvaluateBodyState(terra, t0, out Vector3d bp, out Vector3d bv);
        double a = 0.5d * (1.543e6d + ra);
        double va = Math.Sqrt(mu * (2d / ra - 1d / a));
        var p0 = bp + new Vector3d(ra, 0d, 0d);
        var v0 = bv + new Vector3d(0d, va, 0d);

        var phys = new SpacecraftPhysics { ToleranceFrameSystem = sys };
        phys.Sources.Add(new CachedGravitySource(sys));
        var prop = new EventDrivenPropagator(phys)
        {
            MaxChunkSeconds = 600d,
            DetectionSubdivisions = 20,
            UseDenseRefinement = true,
            SystemForGrain = sys
        };
        prop.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereEntry(terra));
        var driver = new LongWarpDriver(phys, prop);
        var ship = new Spacecraft(p0, v0, 1000d);
        LongWarpResult res = driver.AdvanceToTarget(ship, t0, t0 + (2d * 86400d));
        Console.WriteLine("game: event={0}, t={1:F3} s, stopped={2}",
            res.StoppingEvent.HasValue ? res.StoppingEvent.Value.Kind.ToString() : "none",
            res.StoppingEvent.HasValue ? res.StoppingEvent.Value.TimeSeconds : -1d,
            !res.ReachedTarget);

        // Reference event time from the same ship Y6C at 5 s, linear bracket.
        double T = t0 + (2d * 86400d);
        var p = new[] { p0.X, p0.Y, p0.Z };
        var v = new[] { v0.X, v0.Y, v0.Z };
        double t = t0;
        double prevT = t0;
        sys.EvaluateBodyState(terra, t0, out Vector3d b0, out _);
        double prevG = Math.Sqrt(
            ((p[0] - b0.X) * (p[0] - b0.X)) + ((p[1] - b0.Y) * (p[1] - b0.Y)) + ((p[2] - b0.Z) * (p[2] - b0.Z)))
            - (terra.Radius + 2e6d);
        bool found = false;
        double eventT = -1d;
        while (t < T - 1e-9d && !found)
        {
            double hStep = Math.Min(5d, T - t);
            StudyShipFixedStep(sys, p, v, t, hStep);
            t += hStep;
            sys.EvaluateBodyState(terra, t, out Vector3d b, out _);
            double g = Math.Sqrt(
                ((p[0] - b.X) * (p[0] - b.X)) + ((p[1] - b.Y) * (p[1] - b.Y)) + ((p[2] - b.Z) * (p[2] - b.Z)))
                - (terra.Radius + 2e6d);
            if (prevG > 0d && g <= 0d)
            {
                eventT = prevT + (t - prevT) * (prevG / (prevG - g));
                found = true;
            }

            prevG = g;
            prevT = t;
        }

        Console.WriteLine("ref:  event={0}, t={1:F3} s", found ? "AtmosphereEntry" : "none", eventT);
        if (found && res.StoppingEvent.HasValue)
        {
            Console.WriteLine("event time error = {0:F3} s", res.StoppingEvent.Value.TimeSeconds - eventT);
        }
    }

    private static void StudyShipFixedStep(StarSystem sys, double[] p, double[] v, double t, double h)
    {
        double[] ws = StudyY6Weights();
        Vector3d acc = sys.EvaluateShipAccelerationCached(new Vector3d(p[0], p[1], p[2]), t);
        foreach (double w in ws)
        {
            double dt = w * h;
            for (int i = 0; i < 3; i++)
            {
                v[i] += 0.5d * dt * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
            }

            for (int i = 0; i < 3; i++)
            {
                p[i] += dt * v[i];
            }

            t += dt;
            acc = sys.EvaluateShipAccelerationCached(new Vector3d(p[0], p[1], p[2]), t);
            for (int i = 0; i < 3; i++)
            {
                v[i] += 0.5d * dt * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
            }
        }
    }
}

