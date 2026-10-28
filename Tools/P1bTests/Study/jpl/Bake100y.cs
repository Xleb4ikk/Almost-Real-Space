using System;
using System.Diagnostics;
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
        Check(baked.Count > 0, "100-year bake produced ephemerides", "");
        return 0;
    }
}
