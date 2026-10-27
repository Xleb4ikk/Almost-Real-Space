using System;
using System.Globalization;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    private static BakedEphemeris QuadraticEphemeris(
        double[] p0, double[] v0, double[] accel, double segmentLength, int segments)
    {
        int stride = BakedEphemeris.AxisStride(2);
        var e = new BakedEphemeris
        {
            T0Seconds = 0d,
            SegmentLengthSeconds = segmentLength,
            Degree = 2,
            SegmentCount = segments,
            Coeffs = new double[segments * stride]
        };
        double half = segmentLength * 0.5d;
        for (int s = 0; s < segments; s++)
        {
            double mid = (s * segmentLength) + half;
            int basis = s * stride;
            for (int axis = 0; axis < 3; axis++)
            {
                double a = p0[axis] + (v0[axis] * mid) + (0.5d * accel[axis] * mid * mid);
                double b = half * (v0[axis] + (accel[axis] * mid));
                double c = 0.5d * accel[axis] * half * half;
                e.Coeffs[basis + (axis * 3)] = a + (0.5d * c);
                e.Coeffs[basis + (axis * 3) + 1] = b;
                e.Coeffs[basis + (axis * 3) + 2] = 0.5d * c;
            }
        }

        return e;
    }

    private static int Test512_ShipMovingBody()
    {
        Console.WriteLine("--- Ship with moving Chebyshev body vs mpmath Taylor ---");
        var root = new OrbitingBody
        {
            Name = "Sun",
            StandardGravitationalParameter = 1d,
            Radius = 0.1d
        };
        var child = new OrbitingBody
        {
            Name = "Earth",
            StandardGravitationalParameter = 0.01d,
            Radius = 0.01d,
            Parent = root,
            SemiMajorAxis = 1d
        };
        child.Baked = QuadraticEphemeris(
            new[] { 1d, 0d, 0d }, new[] { 0d, 1d, 0d }, new[] { -1d, 0d, 0d }, 0.25d, 4);
        root.Children.Add(child);
        var sys = new StarSystem(root);

        var p0 = new Vector3d(1.05d, 0d, 0d);
        var v0 = new Vector3d(0d, 1d + Math.Sqrt(0.01d / 0.05d), 0d);
        var r = TaylorShip.RunSeconds(
            sys, p0, v0, 0d, 1d, 16, 1e-14d, 1e-14d, 1d, 1e-12d);
        double[] pt = Golden(new[]
        {
            "0.660467858174189867745524661255119585453935421", "0.947149947013007094492872495196349185315965319", "0.0"
        });
        double[] vt = Golden(new[]
        {
            "-0.743298711527857424532540756356573307563987684", "0.952221625324129935877301231309932825610886477", "0.0"
        });
        var dd = TaylorShip.RunDdSeconds(
            sys, p0, v0, 0d, 1d, 24, 1e-22d, 1e-22d, 1d, 1e-14d);
        double dp = Distance(r.P, pt);
        double dv = Distance(r.V, vt);
        StudyDd dx = StudyDd.Sub(dd.DdPx, DdFromDecimal("0.660467858174189867745524661255119585453935421"));
        StudyDd dy = StudyDd.Sub(dd.DdPy, DdFromDecimal("0.947149947013007094492872495196349185315965319"));
        StudyDd dz = StudyDd.Sub(dd.DdPz, DdFromDecimal("0.0"));
        StudyDd dvx = StudyDd.Sub(dd.DdVx, DdFromDecimal("-0.743298711527857424532540756356573307563987684"));
        StudyDd dvy = StudyDd.Sub(dd.DdVy, DdFromDecimal("0.952221625324129935877301231309932825610886477"));
        StudyDd dvz = StudyDd.Sub(dd.DdVz, DdFromDecimal("0.0"));
        double ddDp = StudyDd.Sqrt(StudyDd.Add(
            StudyDd.Add(StudyDd.Mul(dx, dx), StudyDd.Mul(dy, dy)), StudyDd.Mul(dz, dz))).ToDouble;
        double ddDv = StudyDd.Sqrt(StudyDd.Add(
            StudyDd.Add(StudyDd.Mul(dvx, dvx), StudyDd.Mul(dvy, dvy)), StudyDd.Mul(dvz, dvz))).ToDouble;
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "double: dp={0:E3} dv={1:E3} clips={2} steps={3} | dd: dp={4:E3} dv={5:E3} clips={6} steps={7}",
            dp, dv, r.SegmentClips, r.Steps, ddDp, ddDv, dd.SegmentClips, dd.Steps));
        Check(double.IsFinite(dp) && double.IsFinite(dv), "moving body finite", "dp/dv");
        Check(double.IsFinite(ddDp) && double.IsFinite(ddDv), "moving body dd finite", "dp/dv");
        Check(dp < 1e-13d, "moving body dp", dp.ToString("E3", CultureInfo.InvariantCulture));
        Check(dv < 1e-13d, "moving body dv", dv.ToString("E3", CultureInfo.InvariantCulture));
        Check(ddDp < 1e-15d, "moving body dd dp", ddDp.ToString("E3", CultureInfo.InvariantCulture));
        Check(ddDv < 1e-15d, "moving body dd dv", ddDv.ToString("E3", CultureInfo.InvariantCulture));
        Check(r.SegmentClips >= 3L, "moving body clips", r.SegmentClips.ToString(CultureInfo.InvariantCulture));
        Check(dd.SegmentClips >= 3L, "moving body dd clips", dd.SegmentClips.ToString(CultureInfo.InvariantCulture));
        CheckReferenceMargin("moving body", Math.Max(ddDp, ddDv), 1e-40d);
        Console.WriteLine("--- Ship with moving Chebyshev body complete ---");
        return 0;
    }
}











