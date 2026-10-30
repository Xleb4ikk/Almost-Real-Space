using System;
using Galilego.Core;
using GalilegoPhysicsTests.Support;
using Xunit;

namespace GalilegoPhysicsTests.Orbits
{
    [Trait("Tier", "Fast")]
    public sealed class CelestialBodyTests
    {
        [Fact]
        public void DefaultMuAndExplicitOverride()
        {
            var body = new CelestialBody(5.972e24, new Vector3d(1d, 2d, 3d), Vector3d.Zero);
            Asserts.Near(body.StandardGravitationalParameter, PhysicsSolver.GravitationalConstant * 5.972e24, 1e-9, "default mu");

            var explicitMu = new CelestialBody(1d, Vector3d.Zero, Vector3d.Zero, 42d);
            Asserts.Near(explicitMu.StandardGravitationalParameter, 42d, 0d, "explicit mu");

            var zeroMass = new CelestialBody(0d, Vector3d.Zero, Vector3d.Zero);
            Asserts.Near(zeroMass.StandardGravitationalParameter, 0d, 0d, "zero mass mu");
        }

        [Fact]
        public void NegativeMassThrowsAndSetStateUpdatesPosition()
        {
            Asserts.Throws<ArgumentOutOfRangeException>(
                () => { var unused = new CelestialBody(-1d, Vector3d.Zero, Vector3d.Zero); },
                "negative mass");

            var body = new CelestialBody(1d, Vector3d.Zero, Vector3d.Zero);
            body.SetState(new Vector3d(3d, 4d, 5d), new Vector3d(6d, 7d, 8d));
            Asserts.VectorNear(body.Position, new Vector3d(3d, 4d, 5d), 0d, "position");
            Asserts.VectorNear(body.Velocity, new Vector3d(6d, 7d, 8d), 0d, "velocity");
        }

        [Fact]
        public void MuConversionsRoundTrip()
        {
            const double mass = 1.2345e24;
            double mu = PhysicsSolver.MassToStandardGravitationalParameter(mass);
            Asserts.Near(PhysicsSolver.StandardGravitationalParameterToMass(mu), mass, 1e-9, "mass round-trip");
        }
    }
}
