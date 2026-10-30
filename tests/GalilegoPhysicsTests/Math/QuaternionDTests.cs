using System;
using Galilego.Core;
using GalilegoPhysicsTests.Support;
using Xunit;

namespace GalilegoPhysicsTests.Mathematics
{
    public sealed class QuaternionDTests
    {
        [Fact]
        public void IdentityAndNormalization()
        {
            Asserts.Near(QuaternionD.Identity.NormSquared, 1d, 0d, "identity norm");
            QuaternionD scaled = new QuaternionD(1d, 2d, 3d, 4d);
            QuaternionD unit = scaled.Normalized;
            Asserts.Near(unit.NormSquared, 1d, 1e-15, "normalized norm");
            Asserts.Near(new QuaternionD(0d, 0d, 0d, 0d).Normalized.W, 1d, 0d, "zero normalizes to identity");
        }

        [Fact]
        public void RotateMatchesKnownAxisAngle()
        {
            QuaternionD quarterTurn = QuaternionD.FromAxisAngle(new Vector3d(0d, 0d, 1d), System.Math.PI / 2d);
            Asserts.VectorNear(quarterTurn.Rotate(new Vector3d(1d, 0d, 0d)), new Vector3d(0d, 1d, 0d), 1e-15, "Rz(90)x");
            Asserts.VectorNear(quarterTurn.Rotate(new Vector3d(0d, 1d, 0d)), new Vector3d(-1d, 0d, 0d), 1e-15, "Rz(90)y");

            QuaternionD xTurn = QuaternionD.FromAxisAngle(new Vector3d(1d, 0d, 0d), System.Math.PI / 2d);
            Asserts.VectorNear(xTurn.Rotate(new Vector3d(0d, 1d, 0d)), new Vector3d(0d, 0d, 1d), 1e-15, "Rx(90)y");
        }

        [Fact]
        public void CompositionAppliesRightFactorFirstAndInverseRestores()
        {
            QuaternionD x = QuaternionD.FromAxisAngle(new Vector3d(1d, 0d, 0d), 0.7d);
            QuaternionD z = QuaternionD.FromAxisAngle(new Vector3d(0d, 0d, 1d), -0.4d);
            Vector3d v = new Vector3d(0.2d, -1.1d, 0.3d);

            Vector3d composed = (x * z).Rotate(v);
            Vector3d sequential = x.Rotate(z.Rotate(v));
            Asserts.VectorNear(composed, sequential, 1e-15, "(x*z).Rotate = x.Rotate(z.Rotate)");

            Vector3d restored = x.Conjugated.Rotate(x.Rotate(v));
            Asserts.VectorNear(restored, v, 1e-15, "q^-1 q v");
        }

        [Fact]
        public void RotatePreservesNormAndDerivativeMatchesSmallStep()
        {
            QuaternionD q = QuaternionD.FromAxisAngle(new Vector3d(0.3d, -0.5d, 0.8d), 1.1d).Normalized;
            Vector3d v = new Vector3d(1.3d, -2.2d, 0.4d);
            Asserts.Near(q.Rotate(v).Magnitude, v.Magnitude, 1e-15, "rotation preserves |v|");

            Vector3d omegaBody = new Vector3d(0.4d, -0.2d, 0.7d);
            QuaternionD derivative = QuaternionD.Derivative(q, omegaBody);
            double h = 1e-7d;
            QuaternionD propagated = (q + derivative * h).Normalized;
            QuaternionD exact = q * QuaternionD.FromAxisAngle(omegaBody, h).Normalized;
            double quaternionError =
                (propagated.X - exact.X) * (propagated.X - exact.X) +
                (propagated.Y - exact.Y) * (propagated.Y - exact.Y) +
                (propagated.Z - exact.Z) * (propagated.Z - exact.Z) +
                (propagated.W - exact.W) * (propagated.W - exact.W);
            Asserts.True(quaternionError < 1e-14, "q + h*qdot does not match the exact small rotation.");
        }
    }
}

