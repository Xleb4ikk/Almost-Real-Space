using System;
using Galilego.Core;
using GalilegoPhysicsTests.Support;
using Xunit;

namespace GalilegoPhysicsTests.Mathematics
{
    public sealed class KahanTests
    {
        [Fact]
        public void CancellationCaseNaiveLosesKahanKeepsTheSmallTerm()
        {
            double naive = 0d;
            var kahan = new KahanAccumulator(0d);
            double[] values = new double[102];
            values[0] = 1e16d;
            for (int i = 1; i <= 100; i++)
            {
                values[i] = 1d;
            }

            values[101] = -1e16d;
            foreach (double value in values)
            {
                naive += value;
                kahan.Add(value);
            }

            Asserts.Near(naive, 0d, 0d, "naive cancellation");
            Asserts.Near(kahan.Sum, 100d, 1e-15, "Kahan cancellation");
        }

        [Fact]
        public void LongAccumulationIsAtLeastHundredTimesBetter()
        {
            double naive = 0d;
            var kahan = new KahanAccumulator(0d);
            for (int i = 0; i < 1000000; i++)
            {
                naive += 0.1d;
                kahan.Add(0.1d);
            }

            double naiveError = System.Math.Abs(naive - 100000d);
            double kahanError = System.Math.Abs(kahan.Sum - 100000d);
            Asserts.True(kahanError < 1e-7, "Kahan sum error is " + kahanError.ToString("E3"));
            Asserts.True(kahanError <= naiveError * 0.01d,
                "Kahan is not at least 100x better: naive=" + naiveError.ToString("E3") +
                ", Kahan=" + kahanError.ToString("E3"));
        }

        [Fact]
        public void ResetRestartsCompensation()
        {
            var kahan = new KahanAccumulator(10d);
            kahan.Add(1e16d);
            kahan.Add(1d);
            kahan.Reset(3d);
            kahan.Add(0.5d);
            Asserts.Near(kahan.Sum, 3.5d, 0d, "reset");
        }
    }
}

