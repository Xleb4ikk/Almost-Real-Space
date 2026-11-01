using System;
using System.Collections.Generic;
using Galilego.Core;
using GalilegoPhysicsTests.Support;
using Xunit;

namespace GalilegoPhysicsTests.Orbits
{
    [Trait("Tier", "Fast")]
    public sealed class PhysicsSolverTests
    {
        private const double Mu = 3.986004418e14d;

        [Fact]
        public void PointMassAccelerationIsInverseSquareAndPointsToTheBody()
        {
            var position = new Vector3d(7e6, 0d, 0d);
            Vector3d acceleration = PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                position, Vector3d.Zero, Mu);
            double expected = Mu / (7e6 * 7e6);
            Asserts.Near(acceleration.X, -expected, 1e-18, "acceleration x");
            Asserts.Near(acceleration.Magnitude, expected, 1e-18, "acceleration magnitude");

            Asserts.VectorNear(
                PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(position, Vector3d.Zero, 0d),
                Vector3d.Zero, 0d, "zero mu");
            Asserts.VectorNear(
                PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(Vector3d.Zero, Vector3d.Zero, Mu),
                Vector3d.Zero, 0d, "zero distance");
        }

        [Fact]
        public void TryAccelerationRejectsPositionsInsideTheBody()
        {
            var outside = new Vector3d(7e6, 0d, 0d);
            Asserts.True(PhysicsSolver.TryCalculateAccelerationFromStandardGravitationalParameter(
                outside, Vector3d.Zero, Mu, 6.371e6, out Vector3d acceleration), "outside must be valid");
            Asserts.Finite(acceleration, "outside acceleration");

            Asserts.False(PhysicsSolver.TryCalculateAccelerationFromStandardGravitationalParameter(
                new Vector3d(6e6, 0d, 0d), Vector3d.Zero, Mu, 6.371e6, out Vector3d inside), "inside must be invalid");
            Asserts.VectorNear(inside, Vector3d.Zero, 0d, "inside acceleration");

            Asserts.True(PhysicsSolver.IsInsideBodySqr(6e6 * 6e6, 6.371e6), "inside predicate");
            Asserts.False(PhysicsSolver.IsInsideBodySqr(7e6 * 7e6, 6.371e6), "outside predicate");
            Asserts.False(PhysicsSolver.IsInsideBodySqr(0d, 0d), "zero-radius body");
        }

        [Fact]
        public void J2TermIsInwardAndCancelsBeyondTheCutoff()
        {
            const double radius = 6.371e6d;
            const double j2 = 1.08263e-3d;
            double r = radius + 200e3;
            double scale = 1.5d * j2 * Mu * radius * radius / (r * r * r * r);
            var equator = new Vector3d(r, 0d, 0d);
            Vector3d equatorial = PhysicsSolver.CalculateJ2Acceleration(
                equator, new Vector3d(0d, 0d, 1d), Mu, j2, radius);
            Asserts.True(equatorial.X < 0d, "J2 equatorial term must point inward");
            Asserts.Finite(equatorial, "J2 equatorial");
            // Analytic value: a_eq = -(3/2)·J2·μ·R²/r⁴, a_pole = +3·J2·μ·R²/r⁴.
            // A sign-only check cannot catch a wrong J2 coefficient or radius.
            Asserts.Near(equatorial.X, -scale, scale * 1e-12, "J2 equatorial magnitude");
            Asserts.Near(equatorial.Magnitude, scale, scale * 1e-12, "J2 equatorial vector magnitude");

            var pole = new Vector3d(0d, 0d, r);
            Vector3d polar = PhysicsSolver.CalculateJ2Acceleration(
                pole, new Vector3d(0d, 0d, 1d), Mu, j2, radius);
            Asserts.True(polar.Z > 0d, "J2 polar term is outward (the bulge reduces polar gravity)");
            Asserts.Near(polar.Z, 2d * scale, scale * 1e-12, "J2 polar magnitude");
            Asserts.Near(polar.Magnitude, 2d * scale, scale * 1e-12, "J2 polar vector magnitude");

            Asserts.VectorNear(PhysicsSolver.CalculateJ2Acceleration(
                new Vector3d(1e9, 0d, 0d), new Vector3d(0d, 0d, 1d), Mu, j2, radius),
                Vector3d.Zero, 0d, "beyond cutoff");
            Asserts.VectorNear(PhysicsSolver.CalculateJ2Acceleration(
                equator, new Vector3d(0d, 0d, 1d), Mu, 0d, radius),
                Vector3d.Zero, 0d, "zero J2");
        }
    }
}
