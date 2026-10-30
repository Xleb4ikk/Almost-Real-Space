using System;
using Galilego.Core;
using GalilegoPhysicsTests.Support;
using Xunit;

namespace GalilegoPhysicsTests.Mathematics
{
    public sealed class Vector3dTests
    {
        [Fact]
        public void ArithmeticDotCrossAndMagnitude()
        {
            var a = new Vector3d(1d, 2d, 3d);
            var b = new Vector3d(-4d, 5d, 6d);

            Asserts.VectorNear(a + b, new Vector3d(-3d, 7d, 9d), 0d, "a+b");
            Asserts.VectorNear(a - b, new Vector3d(5d, -3d, -3d), 0d, "a-b");
            Asserts.VectorNear(a * 2d, new Vector3d(2d, 4d, 6d), 0d, "a*2");
            Asserts.VectorNear(-a, new Vector3d(-1d, -2d, -3d), 0d, "-a");
            Asserts.Near(Vector3d.Dot(a, b), 24d, 0d, "dot");
            Asserts.VectorNear(Vector3d.Cross(a, b), new Vector3d(-3d, -18d, 13d), 0d, "cross");
            Asserts.Near(a.SqrMagnitude, 14d, 0d, "|a|ВІ");
            Asserts.Near(a.Magnitude, Math.Sqrt(14d), 1e-15, "|a|");
        }

        [Fact]
        public void NormalizedHandlesZeroAndNan()
        {
            Asserts.VectorNear(new Vector3d(3d, 0d, 4d).Normalized, new Vector3d(0.6d, 0d, 0.8d), 1e-16, "unit");
            Asserts.VectorNear(Vector3d.Zero.Normalized, Vector3d.Zero, 0d, "zero");
            Vector3d nan = new Vector3d(double.NaN, 0d, 0d).Normalized;
            Asserts.False(nan.IsFinite, "NaN must propagate through Normalized.");
        }

        [Fact]
        public void DivisionByZeroThrowsAndLerpClamps()
        {
            Asserts.Throws<DivideByZeroException>(() => { Vector3d unused = new Vector3d(1d, 1d, 1d) / 0d; },
                "vector / 0");

            Asserts.VectorNear(
                Vector3d.Lerp(new Vector3d(0d, 0d, 0d), new Vector3d(10d, 10d, 10d), -5d),
                Vector3d.Zero, 0d, "Lerp(-5)");
            Asserts.VectorNear(
                Vector3d.Lerp(new Vector3d(0d, 0d, 0d), new Vector3d(10d, 10d, 10d), 5d),
                new Vector3d(10d, 10d, 10d), 0d, "Lerp(5)");
            Asserts.VectorNear(
                Vector3d.Lerp(new Vector3d(0d, 0d, 0d), new Vector3d(10d, 10d, 10d), 0.25d),
                new Vector3d(2.5d, 2.5d, 2.5d), 0d, "Lerp(0.25)");
        }
    }
}

