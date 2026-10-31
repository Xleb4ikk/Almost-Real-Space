using System;
using Galilego.Core;
using GalilegoPhysicsTests.Support;
using Xunit;

namespace GalilegoPhysicsTests.Mathematics
{
    [Trait("Tier", "Fast")]
    public sealed class Matrix3x3Tests
    {
        [Fact]
        public void DeterminantAndInverse()
        {
            var m = new Matrix3x3(
                2d, -1d, 0d,
                -1d, 2d, -1d,
                0d, -1d, 2d);

            Asserts.Near(m.Determinant(), 4d, 1e-15, "det");
            Matrix3x3 inverse = m.Inverse();
            Matrix3x3 identity = Multiply(m, inverse);
            AssertIdentity(identity, 1e-14);
        }

        [Fact]
        public void SingularMatrixThrows()
        {
            var singular = new Matrix3x3(
                1d, 2d, 3d,
                2d, 4d, 6d,
                0d, 1d, 1d);
            Asserts.Near(singular.Determinant(), 0d, 1e-15, "singular det");
            Asserts.Throws<InvalidOperationException>(() => singular.Inverse(), "singular inverse");
        }

        [Fact]
        public void RotationMatrixIsOrthogonalAndRotatesAsExpected()
        {
            // Rz(90°).
            var rotation = new Matrix3x3(
                0d, -1d, 0d,
                1d, 0d, 0d,
                0d, 0d, 1d);

            Asserts.Near(rotation.Determinant(), 1d, 1e-15, "det R");
            Matrix3x3 inverse = rotation.Inverse();
            Matrix3x3 transpose = new Matrix3x3(
                rotation.M11, rotation.M21, rotation.M31,
                rotation.M12, rotation.M22, rotation.M32,
                rotation.M13, rotation.M23, rotation.M33);
            AssertMatrixNear(inverse, transpose, 1e-15, "R^-1 = R^T");
            Asserts.VectorNear(rotation * new Vector3d(1d, 0d, 0d), new Vector3d(0d, 1d, 0d), 1e-15, "R x");
        }

        private static Matrix3x3 Multiply(Matrix3x3 a, Matrix3x3 b)
        {
            return new Matrix3x3(
                (a.M11 * b.M11) + (a.M12 * b.M21) + (a.M13 * b.M31),
                (a.M11 * b.M12) + (a.M12 * b.M22) + (a.M13 * b.M32),
                (a.M11 * b.M13) + (a.M12 * b.M23) + (a.M13 * b.M33),
                (a.M21 * b.M11) + (a.M22 * b.M21) + (a.M23 * b.M31),
                (a.M21 * b.M12) + (a.M22 * b.M22) + (a.M23 * b.M32),
                (a.M21 * b.M13) + (a.M22 * b.M23) + (a.M23 * b.M33),
                (a.M31 * b.M11) + (a.M32 * b.M21) + (a.M33 * b.M31),
                (a.M31 * b.M12) + (a.M32 * b.M22) + (a.M33 * b.M32),
                (a.M31 * b.M13) + (a.M32 * b.M23) + (a.M33 * b.M33));
        }

        private static void AssertIdentity(Matrix3x3 identity, double tolerance)
        {
            AssertMatrixNear(identity, Matrix3x3.Identity, tolerance, "identity");
        }

        private static void AssertMatrixNear(Matrix3x3 actual, Matrix3x3 expected, double tolerance, string what)
        {
            Asserts.Near(actual.M11, expected.M11, tolerance, what + " M11");
            Asserts.Near(actual.M12, expected.M12, tolerance, what + " M12");
            Asserts.Near(actual.M13, expected.M13, tolerance, what + " M13");
            Asserts.Near(actual.M21, expected.M21, tolerance, what + " M21");
            Asserts.Near(actual.M22, expected.M22, tolerance, what + " M22");
            Asserts.Near(actual.M23, expected.M23, tolerance, what + " M23");
            Asserts.Near(actual.M31, expected.M31, tolerance, what + " M31");
            Asserts.Near(actual.M32, expected.M32, tolerance, what + " M32");
            Asserts.Near(actual.M33, expected.M33, tolerance, what + " M33");
        }
    }
}

