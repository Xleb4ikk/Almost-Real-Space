using System;
using System.Globalization;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    private static int Test508_ChebyshevBoundary()
    {
        Console.WriteLine("--- Chebyshev segment boundary ---");
        StarSystem sys = StudyRealSolar7();
        StudyNBody nb = StudyMakeNBody(sys);
        StudyBake("Boundary", sys, nb, 30d / 365.25d);

        double segment = double.PositiveInfinity;
        foreach (OrbitingBody body in sys.AllBodies)
        {
            if (body.Parent == null || body.Baked == null)
            {
                continue;
            }

            segment = Math.Min(segment, body.Baked.SegmentLengthSeconds);
        }

        Check(double.IsFinite(segment) && segment > 0d, "boundary segment",
            segment.ToString("F0", CultureInfo.InvariantCulture));

        OrbitingBody earth = sys.AllBodies[1];
        double mu = earth.ResolveStandardGravitationalParameter();
        double t0 = StudyShipT0;
        double total = 3d * segment;
        sys.EvaluateBodyState(earth, t0, out Vector3d bp, out Vector3d bv);
        StudyShipOrbit(6.771e6d, 6.771e6d, mu, out Vector3d relP, out Vector3d relV);
        var p0 = bp + relP;
        var v0 = bv + relV;

        var coarse = TaylorShip.RunSeconds(
            sys, p0, v0, t0, total, 16, 1e-12d, 1e-12d, 10d * segment, 1e-6d);
        var fine = TaylorShip.RunSeconds(
            sys, p0, v0, t0, total, 16, 1e-12d, 1e-12d, segment / 20d, 1e-6d);
        var ddCoarse = TaylorShip.RunDdSeconds(
            sys, p0, v0, t0, total, 16, 1e-12d, 1e-12d, 10d * segment, 1e-6d);
        var ddFine = TaylorShip.RunDdSeconds(
            sys, p0, v0, t0, total, 16, 1e-12d, 1e-12d, segment / 20d, 1e-6d);
        double dp = (coarse.P - fine.P).Magnitude;
        double ddP = (ddCoarse.P - ddFine.P).Magnitude;

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "segment={0:F0} s, coarse clips={1}, steps={2}, fine clips={3}, steps={4}, dp={5:E3} m",
            segment, coarse.SegmentClips, coarse.Steps, fine.SegmentClips, fine.Steps, dp));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "dd: clips={0}/{1}, steps={2}/{3}, dp={4:E3} m", ddCoarse.SegmentClips, ddFine.SegmentClips, ddCoarse.Steps, ddFine.Steps, ddP));
        Check(coarse.SegmentClips >= 2L, "boundary coarse clips",
            coarse.SegmentClips.ToString(CultureInfo.InvariantCulture));
        Check(fine.SegmentClips >= 2L, "boundary fine clips",
            fine.SegmentClips.ToString(CultureInfo.InvariantCulture));
        Check(double.IsFinite(dp), "boundary finite", dp.ToString("E3", CultureInfo.InvariantCulture));
        Check(dp < 2d, "boundary coarse vs fine", dp.ToString("E3", CultureInfo.InvariantCulture));
        Check(ddCoarse.SegmentClips >= 2L, "boundary dd coarse clips", ddCoarse.SegmentClips.ToString(CultureInfo.InvariantCulture));
        Check(ddFine.SegmentClips >= 2L, "boundary dd fine clips", ddFine.SegmentClips.ToString(CultureInfo.InvariantCulture));
        Check(double.IsFinite(ddP) && ddP < 2d, "boundary dd coarse vs fine", ddP.ToString("E3", CultureInfo.InvariantCulture));
        Console.WriteLine("--- Chebyshev segment boundary complete ---");
        return 0;
    }
}

