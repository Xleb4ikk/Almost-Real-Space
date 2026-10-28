using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Spacecraft;

internal static partial class P1bTests
{
    private static SpacecraftIntegrationState KeplerExactState(double t)
    {
        const double e = 0.1d;
        double m = t % (2d * Math.PI);
        double eccentric = m;
        for (int i = 0; i < 60; i++)
        {
            eccentric -= (eccentric - (e * Math.Sin(eccentric)) - m) / (1d - (e * Math.Cos(eccentric)));
        }

        double s = Math.Sqrt(1d - (e * e));
        double d = 1d - (e * Math.Cos(eccentric));
        return new SpacecraftIntegrationState
        {
            Position = new Vector3d(Math.Cos(eccentric) - e, s * Math.Sin(eccentric), 0d),
            Velocity = new Vector3d(-Math.Sin(eccentric) / d, s * Math.Cos(eccentric) / d, 0d),
            Mass = 1d
        };
    }

    private static SpacecraftIntegrationState KeplerStateDerivative(SpacecraftIntegrationState state, double time)
    {
        double r = state.Position.Magnitude;
        double r3 = r * r * r;
        return new SpacecraftIntegrationState
        {
            Position = state.Velocity,
            Velocity = state.Position * (-1d / r3),
            Mass = 0d
        };
    }

    private static int Test547_SpacecraftDop853()
    {
        Console.WriteLine("--- Production spacecraft integrator: DOP853 vs DOPRI5, Kepler e=0.1, one orbit ---");

        double total = 2d * Math.PI;
        SpacecraftIntegrationState start = KeplerExactState(0d);
        SpacecraftIntegrationState exact = KeplerExactState(total);

        var segments = new List<DenseSegment>();
        SpacecraftIntegrationResult dop853 = SpacecraftOrbitIntegrator.StepForwardDop853(
            start, 0d, total, KeplerStateDerivative, 1e-9d, 1e-12d, 1e-9d, 1e-13d, segments);
        SpacecraftIntegrationResult dopri5 = SpacecraftOrbitIntegrator.StepForward(
            start, 0d, total, KeplerStateDerivative, 1e-9d, 1e-12d, 1e-9d, 1e-13d);

        double dop853Error = (dop853.State.Position - exact.Position).Magnitude;
        double dopri5Error = (dopri5.State.Position - exact.Position).Magnitude;
        SpacecraftIntegrationState denseEnd = DenseSegment.EvaluateTrack(segments, total);
        double denseError = (denseEnd.Position - dop853.State.Position).Magnitude;

        Console.WriteLine("DOP853  dp={0:E3} m", dop853Error);
        Console.WriteLine("DOPRI5  dp={0:E3} m", dopri5Error);

        Check(dop853.State.IsFinite, "DOP853 state finite", "");
        Check(dop853Error < dopri5Error, "DOP853 beats DOPRI5 on the Kepler orbit",
            dop853Error.ToString("E3") + " < " + dopri5Error.ToString("E3"));
        Check(segments.Count > 0 && denseError < 1e-9d,
            "DOP853 dense output endpoint matches the state",
            segments.Count + " segments, " + denseError.ToString("E3") + " m");
        Check(new SpacecraftPhysics().Integrator == SpacecraftIntegrator.Dop853,
            "spacecraft default integrator is DOP853", "");

        Console.WriteLine("[Test547_SpacecraftDop853] ok");
        return 0;
    }
}
