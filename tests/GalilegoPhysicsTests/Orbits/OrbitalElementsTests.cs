using System;
using Galilego.Core;
using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests.Orbits
{
    [Trait("Tier", "Fast")]
    public sealed class OrbitalElementsTests
    {
        private const double Mu = 3.986004418e14d;
        private readonly ITestOutputHelper output;

        public OrbitalElementsTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void StateToElementsToState_RoundTripsOnAGrid()
        {
            var cases = new[]
            {
                new Case(7.0e6, 0.0, 0.0, 0.0, 0.0, 0.0),
                new Case(7.0e6, 1e-9, 1e-9, 10.0, 20.0, 30.0),
                new Case(7.0e6, 0.0, 180.0, 0.0, 0.0, 0.0),
                new Case(8.0e6, 0.1, 63.4, 30.0, 45.0, 10.0),
                new Case(1.0e7, 0.5, 25.0, 40.0, 60.0, 20.0),
                new Case(1.0e7, 0.9, 0.0, 0.0, 0.0, 40.0),
                new Case(1.0e7, 0.99, 90.0, 120.0, 210.0, 30.0),
                new Case(1.0e7, 0.999, 179.999, 10.0, 20.0, 5.0),
                new Case(-1.4e7, 1.5, 0.0, 0.0, 0.0, 0.0),
                new Case(-2.0e7, 2.0, 40.0, 70.0, 110.0, -30.0)
            };

            double worstPosition = 0d;
            double worstVelocity = 0d;
            foreach (Case test in cases)
            {
                InitialOrbit.StateFromElements(
                    Mu, test.A, test.E, test.I, test.Raan, test.Argp, test.Nu,
                    out var position, out var velocity);

                OrbitalElements elements = OrbitalElements.FromState(
                    position.ToVector3d(), velocity.ToVector3d(), Mu);
                Asserts.True(elements.IsValid, "elements invalid for e=" + test.E + ", i=" + test.I);
                Asserts.Near(elements.Eccentricity, test.E, 1e-9, "e");

                StateFromElements.Reconstruct(elements, Mu, out Vector3d restoredPosition, out Vector3d restoredVelocity);
                double positionError = (restoredPosition - position.ToVector3d()).Magnitude / position.Magnitude;
                double velocityError = (restoredVelocity - velocity.ToVector3d()).Magnitude / velocity.Magnitude;
                worstPosition = Math.Max(worstPosition, positionError);
                worstVelocity = Math.Max(worstVelocity, velocityError);
            }

            output.WriteLine("Round-trip: max relative position error = " + worstPosition.ToString("E3") +
                             ", velocity = " + worstVelocity.ToString("E3") + ".");
            Asserts.True(worstPosition < 1e-9, "position round-trip error " + worstPosition.ToString("E3"));
            Asserts.True(worstVelocity < 1e-9, "velocity round-trip error " + worstVelocity.ToString("E3"));
        }

        [Fact]
        public void EllipticElementsExposeInvariants()
        {
            InitialOrbit.StateFromElements(
                Mu, 1.0e7, 0.5, 25.0, 40.0, 60.0, 20.0,
                out var position, out var velocity);

            OrbitalElements elements = OrbitalElements.FromState(position.ToVector3d(), velocity.ToVector3d(), Mu);
            Asserts.True(elements.IsValid && elements.IsBound, "elliptic must be valid and bound");
            Asserts.Near(elements.SemiMajorAxis, 1.0e7, 1e-3, "a");
            Asserts.Near(elements.PeriapsisDistance, 5.0e6, 1e-3, "rp");
            Asserts.Near(elements.ApoapsisDistance, 1.5e7, 1e-3, "ra");
            Asserts.Near(elements.OrbitalPeriodSeconds, 2d * Math.PI * Math.Sqrt(1e21 / Mu), 1e-6, "period");
            Asserts.Near(elements.SpecificAngularMomentum, Math.Sqrt(Mu * 1e7 * (1d - 0.25d)), 1e-3, "h");
            Asserts.Near(elements.SpecificOrbitalEnergy, -Mu / (2e7), 1e-6, "energy");

            Asserts.True(elements.TryGetApsisPositions(out Vector3d periapsis, out Vector3d apoapsis), "apsides");
            Asserts.Near(periapsis.Magnitude, 5.0e6, 1e-3, "periapsis radius");
            Asserts.Near(apoapsis.Magnitude, 1.5e7, 1e-3, "apoapsis radius");
            Asserts.VectorNear(periapsis, -apoapsis * (1d / 3d), 1e-3, "apsides are opposite");

            Asserts.True(elements.TryGetTimeToApsides(Mu, out double toPeriapsis, out double toApoapsis), "times");
            Asserts.True(toPeriapsis >= 0d && toPeriapsis <= elements.OrbitalPeriodSeconds, "time to periapsis out of range");
            Asserts.True(toApoapsis >= 0d && toApoapsis <= elements.OrbitalPeriodSeconds, "time to apoapsis out of range");

            InitialOrbit.StateFromElements(
                Mu, 1.0e7, 0.5, 25.0, 40.0, 60.0, 0.0,
                out var periapsisStatePosition, out var periapsisStateVelocity);
            OrbitalElements atPeriapsis = OrbitalElements.FromState(
                periapsisStatePosition.ToVector3d(), periapsisStateVelocity.ToVector3d(), Mu);
            Asserts.True(atPeriapsis.TryGetTimeToApsides(Mu, out double fromPeriapsis, out double toApoapsisFromPeriapsis), "times at periapsis");
            Asserts.Near(fromPeriapsis, 0d, 1e-9, "time to periapsis at periapsis");
            Asserts.Near(toApoapsisFromPeriapsis, elements.OrbitalPeriodSeconds / 2d, 1e-3, "time to apoapsis at periapsis");
        }

        [Fact]
        public void HyperbolicElementsHaveNoApoapsisOrPeriod()
        {
            InitialOrbit.StateFromElements(
                Mu, -1.4e7, 1.5, 0.0, 0.0, 0.0, 0.0,
                out var position, out var velocity);

            OrbitalElements elements = OrbitalElements.FromState(position.ToVector3d(), velocity.ToVector3d(), Mu);
            Asserts.True(elements.IsValid && !elements.IsBound, "hyperbolic must be valid and unbound");
            Asserts.True(elements.SemiMajorAxis < 0d, "hyperbolic a must be negative");
            Asserts.True(double.IsPositiveInfinity(elements.OrbitalPeriodSeconds), "hyperbolic period");
            Asserts.True(double.IsNaN(elements.MeanAnomalyDegrees), "hyperbolic mean anomaly");

            Asserts.True(elements.TryGetApsisPositions(out Vector3d periapsis, out Vector3d apoapsis), "apsides");
            Asserts.Near(periapsis.Magnitude, 7.0e6, 1e-3, "rp");
            Asserts.VectorNear(apoapsis, Vector3d.Zero, 0d, "no apoapsis");
        }

        [Fact]
        public void InvalidInputsReturnInvalid()
        {
            Asserts.False(OrbitalElements.FromState(Vector3d.Zero, new Vector3d(0d, 1d, 0d), Mu).IsValid, "zero r");
            Asserts.False(OrbitalElements.FromState(new Vector3d(1e7, 0d, 0d), new Vector3d(0d, 0d, 0d), Mu).IsValid, "zero v");
            Asserts.False(OrbitalElements.FromState(new Vector3d(1e7, 0d, 0d), new Vector3d(1d, 0d, 0d), Mu).IsValid, "radial");
            Asserts.False(OrbitalElements.FromState(new Vector3d(1e7, 0d, 0d), new Vector3d(0d, 1d, 0d), 0d).IsValid, "mu=0");
        }

        [Fact]
        public void SoiAndHillRadiiFollowTheDocumentedFormulas()
        {
            Asserts.Near(OrbitalElements.CalculateSphereOfInfluenceRadius(1.5e11, 5.972e24, 1.989e30),
                1.5e11 * Math.Pow(5.972e24 / 1.989e30, 0.4), 1e-6, "SOI");
            Asserts.Near(OrbitalElements.CalculateHillRadius(1.5e11, 0.0167, 5.972e24, 1.989e30),
                1.5e11 * (1d - 0.0167) * Math.Pow(5.972e24 / (3d * 1.989e30), 1d / 3d), 1e-6, "Hill");
            Asserts.Near(OrbitalElements.CalculateSphereOfInfluenceRadius(0d, 1d, 1d), 0d, 0d, "SOI zero a");
            Asserts.Near(OrbitalElements.CalculateHillRadius(1d, -1d, 0d, 1d), 0d, 0d, "Hill zero m");
        }

        [Fact]
        public void HyperbolicTimeToApsidesDistinguishesInboundAndOutbound()
        {
            InitialOrbit.StateFromElements(
                Mu, -1.4e7, 1.5, 0d, 0d, 0d, -60d,
                out var inboundPosition, out var inboundVelocity);
            OrbitalElements inbound = OrbitalElements.FromState(
                inboundPosition.ToVector3d(), inboundVelocity.ToVector3d(), Mu);
            Asserts.True(inbound.TryGetTimeToApsides(Mu, out double inboundTime, out double inboundApo), "inbound");
            Asserts.True(inboundTime > 0d && !double.IsNaN(inboundTime), "inbound time to periapsis");
            Asserts.True(double.IsNaN(inboundApo), "hyperbola has no apoapsis");

            InitialOrbit.StateFromElements(
                Mu, -1.4e7, 1.5, 0d, 0d, 0d, 60d,
                out var outboundPosition, out var outboundVelocity);
            OrbitalElements outbound = OrbitalElements.FromState(
                outboundPosition.ToVector3d(), outboundVelocity.ToVector3d(), Mu);
            Asserts.True(outbound.TryGetTimeToApsides(Mu, out double outboundTime, out double outboundApo), "outbound");
            Asserts.True(double.IsNaN(outboundTime), "outbound hyperbola has no future periapsis");
            Asserts.True(double.IsNaN(outboundApo), "outbound hyperbola has no apoapsis");
        }

        [Fact]
        public void NearParabolicHyperbolicApsidesAreRejected()
        {
            InitialOrbit.StateFromElements(
                Mu, -1e12, 1.0000000005, 0d, 0d, 0d, 0d,
                out var position, out var velocity);
            OrbitalElements elements = OrbitalElements.FromState(
                position.ToVector3d(), velocity.ToVector3d(), Mu);
            Asserts.True(elements.IsValid && !elements.IsBound, "near-parabolic hyperbola must be valid and unbound");
            Asserts.False(elements.TryGetTimeToApsides(Mu, out _, out _),
                "near-parabolic hyperbola apsides are documented as unsupported");
        }

        private readonly struct Case
        {
            public readonly double A;
            public readonly double E;
            public readonly double I;
            public readonly double Raan;
            public readonly double Argp;
            public readonly double Nu;

            public Case(double a, double e, double i, double raan, double argp, double nu)
            {
                A = a;
                E = e;
                I = i;
                Raan = raan;
                Argp = argp;
                Nu = nu;
            }
        }
    }
}
