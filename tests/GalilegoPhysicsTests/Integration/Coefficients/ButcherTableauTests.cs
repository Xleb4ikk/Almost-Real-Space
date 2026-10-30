using System;
using Galilego.Core;
using GalilegoPhysicsTests.Support;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests.Integration.Coefficients
{
    [Trait("Tier", "Fast")]
    public sealed class ButcherTableauTests
    {
        private readonly ITestOutputHelper output;

        public ButcherTableauTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void Dopri5_RowSumsWeightsAndOrderFive()
        {
            ExactRational[][] a = Dopri5A();
            ExactRational[] b = Dopri5B();
            ExactRational[] c = Dopri5C();

            double rowResidual = OrderConditions.MaxRowSumResidual(a, c);
            double weightResidual = OrderConditions.SumResidual(b, 1d);
            double orderResidual = OrderConditions.MaxSeriesResidual(a, b, 5);
            double bStarResidual = OrderConditions.SumResidual(Dopri5BStar(), 1d);

            output.WriteLine("DOPRI5: rows a=c: " + rowResidual.ToString("E3") +
                             "; sum b - 1: " + weightResidual.ToString("E3") +
                             "; sum b* - 1: " + bStarResidual.ToString("E3") +
                             "; order 5: " + orderResidual.ToString("E3") + ".");

            Asserts.True(rowResidual < 1e-15, "DOPRI5: a row sum != c: " + rowResidual.ToString("E3"));
            Asserts.True(weightResidual < 1e-15, "DOPRI5: sum b != 1: " + weightResidual.ToString("E3"));
            Asserts.True(bStarResidual < 1e-15, "DOPRI5: sum b* != 1: " + bStarResidual.ToString("E3"));
            // The tableau is stored as doubles; exact rational arithmetic on those
            // doubles leaves a ~2e-15 residual (the coefficients' binary rounding).
            Asserts.True(orderResidual < 1e-14, "DOPRI5 is not order 5: residual " + orderResidual.ToString("E3"));
        }

        [Fact]
        public void Dopri5_FsalRowEqualsWeights()
        {
            ExactRational[] b = Dopri5B();
            double[] row =
            {
                DOPRI5Coefficients.a71, 0d, DOPRI5Coefficients.a73,
                DOPRI5Coefficients.a74, DOPRI5Coefficients.a75, DOPRI5Coefficients.a76, 0d
            };

            double worst = 0d;
            for (int i = 0; i < row.Length; i++)
            {
                double residual = (ExactRational.FromDouble(row[i]) - b[i]).AbsToDouble();
                worst = Math.Max(worst, residual);
            }

            output.WriteLine("DOPRI5 FSAL: max |a7-b| = " + worst.ToString("E3") + ".");
            Asserts.True(worst < 1e-16, "DOPRI5: the FSAL row does not equal the b weights.");
        }

        [Fact]
        public void Dop853_RowSumsWeightsEmbeddedAndOrder()
        {
            ExactRational[][] a = OrderConditions.ToRational(DOP853Coefficients.A);
            ExactRational[] b = OrderConditions.ToRational(DOP853Coefficients.B);
            ExactRational[] c = OrderConditions.ToRational(DOP853Coefficients.C);
            ExactRational[] e5 = OrderConditions.ToRational(DOP853Coefficients.E5);

            double rowResidual = OrderConditions.MaxRowSumResidual(a, c);
            double weightResidual = OrderConditions.SumResidual(b, 1d);
            double embeddedResidual = OrderConditions.SumResidual(e5, 0d);
            double orderResidual = OrderConditions.MaxSeriesResidual(a, b, 8);

            output.WriteLine("DOP853: rows a=c: " + rowResidual.ToString("E3") +
                             "; sum b - 1: " + weightResidual.ToString("E3") +
                             "; sum E5: " + embeddedResidual.ToString("E3") +
                             "; order 8: " + orderResidual.ToString("E3") + ".");

            // DOP853 coefficients are published to ~1e-18 decimal places; exact
            // rational arithmetic on their double representations leaves ~2e-15
            // in the row sums and ~1e-14 in order-8 series coefficients.
            Asserts.True(rowResidual < 2e-15, "DOP853: a row sum != c: " + rowResidual.ToString("E3"));
            Asserts.True(weightResidual < 1e-15, "DOP853: sum b != 1: " + weightResidual.ToString("E3"));
            Asserts.True(embeddedResidual < 1e-15, "DOP853: sum E5 != 0: " + embeddedResidual.ToString("E3"));
            Asserts.True(orderResidual < 5e-14, "DOP853 is not order 8: residual " + orderResidual.ToString("E3"));
        }

        private static ExactRational[][] Dopri5A()
        {
            return new[]
            {
                Row(),
                Row(DOPRI5Coefficients.a21),
                Row(DOPRI5Coefficients.a31, DOPRI5Coefficients.a32),
                Row(DOPRI5Coefficients.a41, DOPRI5Coefficients.a42, DOPRI5Coefficients.a43),
                Row(DOPRI5Coefficients.a51, DOPRI5Coefficients.a52, DOPRI5Coefficients.a53, DOPRI5Coefficients.a54),
                Row(DOPRI5Coefficients.a61, DOPRI5Coefficients.a62, DOPRI5Coefficients.a63, DOPRI5Coefficients.a64, DOPRI5Coefficients.a65),
                Row(DOPRI5Coefficients.a71, 0d, DOPRI5Coefficients.a73, DOPRI5Coefficients.a74, DOPRI5Coefficients.a75, DOPRI5Coefficients.a76)
            };
        }

        private static ExactRational[] Dopri5B()
        {
            return new[]
            {
                ExactRational.FromDouble(DOPRI5Coefficients.b1),
                ExactRational.Zero,
                ExactRational.FromDouble(DOPRI5Coefficients.b3),
                ExactRational.FromDouble(DOPRI5Coefficients.b4),
                ExactRational.FromDouble(DOPRI5Coefficients.b5),
                ExactRational.FromDouble(DOPRI5Coefficients.b6),
                ExactRational.Zero
            };
        }

        private static ExactRational[] Dopri5BStar()
        {
            return new[]
            {
                ExactRational.FromDouble(DOPRI5Coefficients.bStar1),
                ExactRational.Zero,
                ExactRational.FromDouble(DOPRI5Coefficients.bStar3),
                ExactRational.FromDouble(DOPRI5Coefficients.bStar4),
                ExactRational.FromDouble(DOPRI5Coefficients.bStar5),
                ExactRational.FromDouble(DOPRI5Coefficients.bStar6),
                ExactRational.FromDouble(DOPRI5Coefficients.bStar7)
            };
        }

        private static ExactRational[] Dopri5C()
        {
            return new[]
            {
                ExactRational.Zero,
                ExactRational.FromDouble(DOPRI5Coefficients.c2),
                ExactRational.FromDouble(DOPRI5Coefficients.c3),
                ExactRational.FromDouble(DOPRI5Coefficients.c4),
                ExactRational.FromDouble(DOPRI5Coefficients.c5),
                ExactRational.FromDouble(DOPRI5Coefficients.c6),
                ExactRational.FromDouble(DOPRI5Coefficients.c7)
            };
        }

        private static ExactRational[] Row(params double[] values)
        {
            var row = new ExactRational[7];
            for (int i = 0; i < row.Length; i++)
            {
                row[i] = ExactRational.Zero;
            }

            for (int i = 0; i < values.Length; i++)
            {
                row[i] = ExactRational.FromDouble(values[i]);
            }

            return row;
        }
    }
}
