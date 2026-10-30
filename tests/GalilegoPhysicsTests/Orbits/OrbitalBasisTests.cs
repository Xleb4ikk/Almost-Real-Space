using System;
using Galilego.Core;
using GalilegoPhysicsTests.Support;
using Xunit;

namespace GalilegoPhysicsTests.Orbits
{
    public sealed class OrbitalBasisTests
    {
        [Fact]
        public void BasisIsOrthonormalAndRightHanded()
        {
            var position = new Vector3d(7e6, 1e6, 2e6);
            var velocity = new Vector3d(-1e3, 7e3, 300d);
            OrbitalBasis.ComputeBasis(position, velocity, out Vector3d radial, out Vector3d normal, out Vector3d prograde);

            Asserts.Near(radial.Magnitude, 1d, 1e-15, "|R|");
            Asserts.Near(normal.Magnitude, 1d, 1e-15, "|N|");
            Asserts.Near(prograde.Magnitude, 1d, 1e-15, "|P|");
            Asserts.Near(Vector3d.Dot(radial, normal), 0d, 1e-15, "R·N");
            Asserts.Near(Vector3d.Dot(radial, prograde), 0d, 1e-15, "R·P");
            Asserts.Near(Vector3d.Dot(normal, prograde), 0d, 1e-15, "N·P");
            Asserts.VectorNear(Vector3d.Cross(radial, prograde), normal, 1e-15, "R×P=N");

            Asserts.VectorNear(radial, position.Normalized, 1e-15, "radial");
            Asserts.VectorNear(normal, Vector3d.Cross(position, velocity).Normalized, 1e-15, "normal");
            Asserts.True(Vector3d.Dot(prograde, velocity) > 0d, "prograde must point along the motion");
        }

        [Fact]
        public void DegenerateInputsAreHandledDeterministically()
        {
            OrbitalBasis.ComputeBasis(new Vector3d(1e7, 0d, 0d), new Vector3d(0d, 0d, 0d),
                out Vector3d radial, out Vector3d normal, out Vector3d prograde);
            Asserts.VectorNear(radial, new Vector3d(1d, 0d, 0d), 1e-15, "radial fallback");
            Asserts.Near(normal.Magnitude, 1d, 1e-15, "normal fallback");
            Asserts.Near(prograde.Magnitude, 1d, 1e-15, "prograde fallback");
            Asserts.Near(Vector3d.Dot(radial, normal), 0d, 1e-15, "degenerate R·N");

            Asserts.False(OrbitalBasis.TryComputeBasis(Vector3d.Zero, new Vector3d(1d, 0d, 0d),
                out _, out _, out _), "zero position must be rejected");
            Asserts.True(OrbitalBasis.TryComputeBasis(new Vector3d(1e7, 0d, 0d), new Vector3d(1d, 0d, 0d),
                out Vector3d parallelRadial, out Vector3d parallelNormal, out Vector3d parallelPrograde),
                "parallel state must use the documented fallback basis");
            Asserts.True(parallelRadial.IsFinite && parallelNormal.IsFinite && parallelPrograde.IsFinite,
                "fallback basis must be finite");
        }
    }
}
