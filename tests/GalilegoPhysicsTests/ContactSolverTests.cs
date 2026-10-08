using System;
using Galilego.Core;
using Galilego.Spacecraft.Solver;
using Xunit;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Фаза 2: чистые функции решателя твёрдых тел (ТЗ v2, раздел 7).
    /// Полные приёмочные кейсы 204–207 — в стенде P1bTests; здесь — юнит-проверки
    /// математики, которые должны держаться независимо от стенда.
    /// </summary>
    public sealed class ContactSolverTests
    {
        [Fact]
        public void RotationExponential_IsUnitAndMatchesAxisAngle()
        {
            QuaternionD q = RigidAssemblySolver.RotationExponential(new Vector3d(0d, 0d, Math.PI / 2d));
            Vector3d turned = q.Rotate(new Vector3d(1d, 0d, 0d));

            Assert.True(Math.Abs(q.NormSquared - 1d) < 1e-14);
            Assert.True(Math.Abs(turned.X) < 1e-12);
            Assert.True(Math.Abs(turned.Y - 1d) < 1e-12);
            Assert.True(Math.Abs(turned.Z) < 1e-12);
        }

        [Fact]
        public void EulerRk4_RotationAboutPrincipalAxis_KeepsRate()
        {
            // ω ∥ главной оси: ω × Iω = 0, поэтому скорость постоянна.
            Vector3d omega = new Vector3d(0d, 0d, 2d);
            Vector3d next = RigidAssemblySolver.IntegrateEulerRk4(
                omega, Vector3d.Zero, new Vector3d(1d, 2d, 3d), 1e-3);

            Assert.True(Math.Abs(next.X) < 1e-15);
            Assert.True(Math.Abs(next.Y) < 1e-15);
            Assert.True(Math.Abs(next.Z - 2d) < 1e-15);
        }

        [Fact]
        public void FreeFall_VelocityIsExactlyGravityTimesTime()
        {
            var solver = new RigidAssemblySolver(1e-3, iterations: 1);
            var body = new RigidBody(4d, new Vector3d(1d, 1d, 1d), Vector3d.Zero, QuaternionD.Identity);
            solver.AddBody(body);
            Vector3d g = new Vector3d(0d, -9.81d, 0d);

            for (int i = 0; i < 100; i++)
            {
                body.ForceWorld = g * body.Mass;
                solver.Step();
            }

            Vector3d expected = g * solver.Time;
            Assert.True((body.Velocity - expected).Magnitude < 1e-12);
        }

        [Fact]
        public void InverseInertiaWorld_IdentityOrientation_ScalesByPrincipalMoments()
        {
            var body = new RigidBody(1d, new Vector3d(2d, 4d, 8d), Vector3d.Zero, QuaternionD.Identity);
            Vector3d r = body.ApplyInverseInertiaWorld(QuaternionD.Identity, new Vector3d(1d, 1d, 1d));

            Assert.True(Math.Abs(r.X - 0.5d) < 1e-15);
            Assert.True(Math.Abs(r.Y - 0.25d) < 1e-15);
            Assert.True(Math.Abs(r.Z - 0.125d) < 1e-15);
        }

        [Fact]
        public void RigidJoint_SeparationIsZeroAtCreation()
        {
            var solver = new RigidAssemblySolver(1e-3, iterations: 4);
            int a = solver.AddBody(new RigidBody(10d, new Vector3d(1d, 1d, 1d), new Vector3d(0d, 0d, 0d), QuaternionD.Identity));
            int b = solver.AddBody(new RigidBody(5d, new Vector3d(1d, 1d, 1d), new Vector3d(2d, 0d, 0d), QuaternionD.Identity));
            int j = solver.AddRigidJoint(a, b, new Vector3d(1d, 0d, 0d));

            Assert.True(solver.JointSeparation(j) < 1e-12);
        }
    }
}
