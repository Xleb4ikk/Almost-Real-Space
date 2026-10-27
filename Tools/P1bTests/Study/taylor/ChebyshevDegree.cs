using System;
using System.Globalization;
using Galilego.Universe;

internal static partial class P1bTests
{
    private static double ScaledJetError(TaylorJet actual, double[] expected, double factor)
    {
        double scale = 1d;
        for (int i = 0; i < expected.Length; i++)
        {
            scale = Math.Max(scale, Math.Abs(expected[i] * factor));
        }

        double worst = 0d;
        for (int i = 0; i < expected.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(actual.C[i] - (expected[i] * factor)) / scale);
        }

        return worst;
    }

    private static int Test515_ChebyshevDegree()
    {
        Console.WriteLine("--- Chebyshev degree 12 vs mpmath ---");
        const int degree = 12;
        int stride = BakedEphemeris.AxisStride(degree);
        var baked = new BakedEphemeris
        {
            T0Seconds = 0d,
            SegmentLengthSeconds = 1d,
            Degree = degree,
            SegmentCount = 1,
            Coeffs = new double[stride]
        };
        for (int axis = 0; axis < 3; axis++)
        {
            for (int k = 0; k <= degree; k++)
            {
                baked.Coeffs[(axis * (degree + 1)) + k] = (axis + 1d) / (k + 1d);
            }
        }

        double[] pos = Golden(new[]
        {
            "0.7149939864759815107704297551337551337551",
            "0.9402281711467713011842031746031746031746",
            "12.90784955064746307249827505827505827506",
            "-79.56228737432384605090909090909090909091",
            "-657.8840469572016023656343656343656343656",
            "2782.942252476898181356421356421356421356",
            "11289.38588155672761727161727161727161727",
            "-43278.24078506252929292929292929292929293",
            "-68723.45562158943278943278943278943278943",
            "310695.6108024242424242424242424242424242",
            "33536.34467505827505827505827505827505828",
            "-831870.2933333333333333333333333333333333",
            "645277.5384615384615384615384615384615385"
        });
        double[] vel = Golden(new[]
        {
            "0.9402281711467713011842031746031746031746",
            "25.81569910129492614499655011655011655012",
            "-238.6868621229715381527272727272727272727",
            "-2631.536187828806409462537462537462537463",
            "13914.71126238449090678210678210678210678",
            "67736.31528934036570362970362970362970363",
            "-302947.6854954377050505050505050505050505",
            "-549787.6449727154623154623154623154623155",
            "2796260.497221818181818181818181818181818",
            "335363.4467505827505827505827505827505828",
            "-9150573.226666666666666666666666666666667",
            "7743330.461538461538461538461538461538462",
            "0.0"
        });

        TaylorLocalState local = TaylorChebyshev.LocalState(baked, 0.37d, degree);
        TaylorJet[] px = { local.X, local.Y, local.Z };
        TaylorJet[] vx = { local.Vx, local.Vy, local.Vz };
        double worst = 0d;
        for (int axis = 0; axis < 3; axis++)
        {
            worst = Math.Max(worst, ScaledJetError(px[axis], pos, axis + 1d));
            worst = Math.Max(worst, ScaledJetError(vx[axis], vel, axis + 1d));
        }

        Console.WriteLine("degree-12 jet error: " + worst.ToString("E2", CultureInfo.InvariantCulture));
        Check(worst < 1e-12d, "Chebyshev degree 12", worst.ToString("E2", CultureInfo.InvariantCulture));
        CheckReferenceMargin("Chebyshev degree 12", worst, 1e-50d);

        const double t0 = 1e8d;
        const double length = 86400d;
        const int segments = 10;
        var large = new BakedEphemeris
        {
            T0Seconds = t0,
            SegmentLengthSeconds = length,
            Degree = degree,
            SegmentCount = segments,
            Coeffs = new double[segments * stride]
        };
        for (int s = 0; s < segments; s++)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                for (int k = 0; k <= degree; k++)
                {
                    large.Coeffs[(s * stride) + (axis * (degree + 1)) + k] = (axis + 1d) / (k + 1d);
                }
            }
        }

        TaylorLocalState largeLocal = TaylorChebyshev.LocalState(
            large, t0 + (5d * length) + (0.37d * length), degree);
        TaylorJet[] lpx = { largeLocal.X, largeLocal.Y, largeLocal.Z };
        TaylorJet[] lvx = { largeLocal.Vx, largeLocal.Vy, largeLocal.Vz };
        var largePos = new double[pos.Length];
        var largeVel = new double[vel.Length];
        double scale = 1d;
        double ratio = 1d / length;
        for (int n = 0; n < pos.Length; n++)
        {
            largePos[n] = pos[n] * scale;
            largeVel[n] = vel[n] * scale * ratio;
            scale *= ratio;
        }

        double largeWorst = 0d;
        for (int axis = 0; axis < 3; axis++)
        {
            largeWorst = Math.Max(largeWorst, ScaledJetError(lpx[axis], largePos, axis + 1d));
            largeWorst = Math.Max(largeWorst, ScaledJetError(lvx[axis], largeVel, axis + 1d));
        }

        Console.WriteLine("large-t segment-5 jet error: " + largeWorst.ToString("E2", CultureInfo.InvariantCulture));
        Check(largeWorst < 1e-12d, "Chebyshev large t",
            largeWorst.ToString("E2", CultureInfo.InvariantCulture));
        CheckReferenceMargin("Chebyshev large t", largeWorst, 1e-50d);
        Console.WriteLine("--- Chebyshev degree 12 complete ---");
        return 0;
    }
}

