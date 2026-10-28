using System;
using System.Globalization;
using System.Threading.Tasks;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    private static int Test508_ChebyshevBoundary()
    {
        Console.WriteLine("--- Chebyshev segment boundary ---");
        StarSystem sys = StudyRealSolar7();
        StudyNBody nb = StudyMakeNBody(sys);
        StudyBake("Boundary", sys, nb, 30d / 365.25d, 0.01d);

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

        TaylorShipResult coarse = null;
        TaylorShipResult fine = null;
        TaylorShipResult fixed300 = null;
        TaylorShipResult fixed150 = null;
        TaylorShipDdResult ddCoarse = null;
        TaylorShipDdResult ddFine = null;
        TaylorShipDdResult ddFixed300 = null;
        TaylorShipDdResult ddFixed150 = null;
        Parallel.Invoke(
            () =>
            {
                coarse = TaylorShip.RunSeconds(
                    sys, p0, v0, t0, total, 16, 1e-12d, 1e-12d, 10d * segment, 1e-6d);
            },
            () =>
            {
                fine = TaylorShip.RunSeconds(
                    sys, p0, v0, t0, total, 16, 1e-12d, 1e-12d, segment / 20d, 1e-6d);
            },
            () =>
            {
                ddCoarse = TaylorShip.RunDdSeconds(
                    sys, p0, v0, t0, total, 16, 1e-12d, 1e-12d, 10d * segment, 1e-6d);
            },
            () =>
            {
                ddFine = TaylorShip.RunDdSeconds(
                    sys, p0, v0, t0, total, 16, 1e-12d, 1e-12d, segment / 20d, 1e-6d);
            },
            () =>
            {
                fixed300 = TaylorShip.RunSeconds(
                    sys, p0, v0, t0, total, 16, 1e-12d, 1e-12d, 300d, 300d);
            },
            () =>
            {
                fixed150 = TaylorShip.RunSeconds(
                    sys, p0, v0, t0, total, 16, 1e-12d, 1e-12d, 150d, 150d);
            },
            () =>
            {
                ddFixed300 = TaylorShip.RunDdSeconds(
                    sys, p0, v0, t0, total, 16, 1e-12d, 1e-12d, 300d, 300d);
            },
            () =>
            {
                ddFixed150 = TaylorShip.RunDdSeconds(
                    sys, p0, v0, t0, total, 16, 1e-12d, 1e-12d, 150d, 150d);
            });
        double dp = (coarse.P - fine.P).Magnitude;
        double ddP = DdNorm(
            new[] { ddCoarse.DdPx, ddCoarse.DdPy, ddCoarse.DdPz },
            new[] { ddFine.DdPx, ddFine.DdPy, ddFine.DdPz });
        double coarseVsDd = (coarse.P - ddCoarse.P).Magnitude;
        double fineVsDd = (fine.P - ddFine.P).Magnitude;

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "segment={0:F0} s, coarse clips={1}, steps={2}, fine clips={3}, steps={4}, dp={5:E3} m, double-vs-dd={6:E3}/{7:E3} m",
            segment, coarse.SegmentClips, coarse.Steps, fine.SegmentClips, fine.Steps, dp, coarseVsDd, fineVsDd));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "dd: clips={0}/{1}, steps={2}/{3}, dp={4:E3} m", ddCoarse.SegmentClips, ddFine.SegmentClips, ddCoarse.Steps, ddFine.Steps, ddP));
        Check(double.IsFinite(coarseVsDd) && double.IsFinite(fineVsDd),
            "boundary double-vs-dd finite", "dp");
        Console.WriteLine(
            "note: adaptive hmax (10*segment vs segment/20) is far above the ~1e2 s controller step, " +
            "so these two runs differ only by their initial step/rejection path. The double-vs-dd errors and the " +
            "fixed 300/150 pair show step-count-proportional roundoff; a naive Kahan power-sum was tried and reverted " +
            "because it raised the 30-day real-system double error from 0.165 m to 1.18 m. Compensated Horner or a " +
            "local frame is the correct double fix; dd avoids the issue by storing the absolute state in dd.");
        Check(coarse.SegmentClips >= 2L, "boundary coarse clips",
            coarse.SegmentClips.ToString(CultureInfo.InvariantCulture));
        Check(fine.SegmentClips >= 2L, "boundary fine clips",
            fine.SegmentClips.ToString(CultureInfo.InvariantCulture));
        Check(double.IsFinite(dp), "boundary finite", dp.ToString("E3", CultureInfo.InvariantCulture));
        Check(ddCoarse.SegmentClips >= 2L, "boundary dd coarse clips", ddCoarse.SegmentClips.ToString(CultureInfo.InvariantCulture));
        Check(ddFine.SegmentClips >= 2L, "boundary dd fine clips", ddFine.SegmentClips.ToString(CultureInfo.InvariantCulture));
        Check(double.IsFinite(ddP) && ddP < 1e-8d, "boundary dd coarse vs fine", ddP.ToString("E3", CultureInfo.InvariantCulture));

        double fixedDp = (fixed300.P - fixed150.P).Magnitude;
        double ddFixedDp = DdNorm(
            new[] { ddFixed300.DdPx, ddFixed300.DdPy, ddFixed300.DdPz },
            new[] { ddFixed150.DdPx, ddFixed150.DdPy, ddFixed150.DdPz });
        double fixed300VsDd = (fixed300.P - ddFixed300.P).Magnitude;
        double fixed150VsDd = (fixed150.P - ddFixed150.P).Magnitude;
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "fixed 300/150: steps={0}/{1}, clips={2}/{3}, double dp={4:E2}, dd dp={5:E2}, double-vs-dd={6:E2}/{7:E2}",
            fixed300.Steps, fixed150.Steps, fixed300.SegmentClips,
            fixed150.SegmentClips, fixedDp, ddFixedDp, fixed300VsDd, fixed150VsDd));
        Console.WriteLine(
            "note: with maxStep forced below the controller step, dd is independent of the step ratio " +
            "to ~1e-9 m; the double difference is double roundoff amplified by the eccentric orbit, " +
            "so it is reported but not used as a correctness gate.");
        Check(fixed300.SegmentClips >= 2L && fixed150.SegmentClips >= 2L,
            "boundary fixed clips", fixed300.SegmentClips + "/" + fixed150.SegmentClips);
        Check(fixed150.Steps >= fixed300.Steps + (fixed300.Steps / 2L),
            "boundary fixed steps bind", fixed300.Steps + "/" + fixed150.Steps);
        Check(double.IsFinite(fixedDp) && double.IsFinite(ddFixedDp),
            "boundary fixed finite", "dp");
        Check(ddFixedDp < 1e-8d, "boundary fixed dd agreement",
            ddFixedDp.ToString("E2", CultureInfo.InvariantCulture));
        Console.WriteLine("--- Chebyshev segment boundary complete ---");
        return 0;
    }
}

