using System;
using System.Diagnostics;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    private static int Test548_Bake100y()
    {
        StarSystem sys = TwelveBodySystem();
        var config = new BakeConfig { Degree = 12, MaxStepSeconds = 900d };

        var watch = Stopwatch.StartNew();
        var baked = EphemerisBaker.Bake(sys, 100d, config);
        watch.Stop();

        long totalBytes = 0;
        foreach (var kv in EphemerisBaker.EstimateBakeBytes(sys, 100d, config))
        {
            totalBytes += kv.Value;
        }

        Console.WriteLine("100-year bake: {0:F1} s, {1:F1} MB estimated, {2} bodies",
            watch.Elapsed.TotalSeconds, totalBytes / 1e6d, baked.Count);

        int expectedBodies = sys.AllBodies.Count - 1;
        Check(baked.Count == expectedBodies,
            "100-year bake covers every non-root body",
            baked.Count + "/" + expectedBodies);

        double spanSeconds = 100d * 365d * 86400d; // EphemerisBaker.YearSeconds
        foreach (var pair in baked)
        {
            BakedEphemeris ephemeris = pair.Value;
            bool covers = ephemeris.SegmentCount > 0 &&
                          ephemeris.EndSeconds >= spanSeconds - ephemeris.SegmentLengthSeconds;
            bool endpointsFinite =
                ephemeris.TryEvaluate(0d, out Vector3d p0, out Vector3d v0) &&
                ephemeris.TryEvaluate(ephemeris.EndSeconds - 1d, out Vector3d p1, out Vector3d v1) &&
                p0.IsFinite && v0.IsFinite && p1.IsFinite && v1.IsFinite;
            Check(covers, "100-year coverage: " + pair.Key.Name,
                ephemeris.EndSeconds.ToString("F0"));
            Check(endpointsFinite, "100-year endpoints finite: " + pair.Key.Name, "");
        }

        return 0;
    }
}
