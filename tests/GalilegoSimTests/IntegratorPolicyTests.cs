using Galilego.Spacecraft;
using Xunit;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// The per-chunk policy is a pure function: same context, same integrator.
    /// These tests pin the v1 decision boundaries (surface 0.05 s, touchdown
    /// guard, Sun coast with no non-gravity sources) without touching physics.
    /// </summary>
    public sealed class IntegratorPolicyTests
    {
        [Fact]
        public void SurfaceShortChunkSelectsDopri5()
        {
            Assert.Equal(SpacecraftIntegrator.Dopri5, Select(
                chunk: 0.05d, flight: true, sun: false, coast: false,
                altitude: 500d, vertical: 0d));
        }

        [Fact]
        public void SurfaceChunkExactlyAtGrainSelectsDopri5()
        {
            Assert.Equal(SpacecraftIntegrator.Dopri5, Select(
                chunk: 0.05d + 1e-12d, flight: true, sun: false, coast: true,
                altitude: 500d, vertical: 50d));
        }

        [Fact]
        public void SurfaceChunkAboveControlGrainStaysDop853()
        {
            Assert.Equal(SpacecraftIntegrator.Dop853, Select(
                chunk: 0.5d, flight: true, sun: false, coast: false,
                altitude: 500d, vertical: 0d));
        }

        [Fact]
        public void SurfaceAboveTenKilometresStaysDop853()
        {
            Assert.Equal(SpacecraftIntegrator.Dop853, Select(
                chunk: 0.05d, flight: true, sun: false, coast: false,
                altitude: 20_000d, vertical: 0d));
        }

        [Fact]
        public void DescendingAboveTouchdownGuardSelectsDopri5()
        {
            Assert.Equal(SpacecraftIntegrator.Dopri5, Select(
                chunk: 0.05d, flight: true, sun: false, coast: false,
                altitude: 5_000d, vertical: -50d));
        }

        [Fact]
        public void DescendingBelowTouchdownGuardStaysDop853()
        {
            Assert.Equal(SpacecraftIntegrator.Dop853, Select(
                chunk: 0.05d, flight: true, sun: false, coast: false,
                altitude: 1_000d, vertical: -50d));
        }

        [Fact]
        public void SunCoastSelectsDopri5()
        {
            Assert.Equal(SpacecraftIntegrator.Dopri5, Select(
                chunk: 1d / 60d, flight: false, sun: true, coast: true,
                altitude: 1e11d, vertical: 0d));
        }

        [Fact]
        public void SunCoastNotAllowedStaysDop853()
        {
            Assert.Equal(SpacecraftIntegrator.Dop853, Select(
                chunk: 1d / 60d, flight: false, sun: true, coast: true,
                altitude: 1e11d, vertical: 0d, sunAllowed: false));
        }

        [Fact]
        public void InnerHeliocentricSunCoastStaysDop853()
        {
            Assert.Equal(SpacecraftIntegrator.Dop853, Select(
                chunk: 1d / 60d, flight: false, sun: true, coast: true,
                altitude: 1e9d, vertical: 0d));
        }

        [Fact]
        public void SunCoastWithThrustOrDragStaysDop853()
        {
            Assert.Equal(SpacecraftIntegrator.Dop853, Select(
                chunk: 1d / 60d, flight: false, sun: true, coast: false,
                altitude: 1e11d, vertical: 0d));
        }

        [Fact]
        public void EarthCoastStaysDop853()
        {
            Assert.Equal(SpacecraftIntegrator.Dop853, Select(
                chunk: 1d / 60d, flight: false, sun: false, coast: true,
                altitude: 200_000d, vertical: 0d));
        }

        [Fact]
        public void DefaultContextStaysDop853()
        {
            Assert.Equal(SpacecraftIntegrator.Dop853, Select(
                chunk: 30d, flight: false, sun: false, coast: false,
                altitude: 0d, vertical: 0d));
        }

        private static SpacecraftIntegrator Select(
            double chunk,
            bool flight,
            bool sun,
            bool coast,
            double altitude,
            double vertical,
            bool sunAllowed = true)
        {
            return IntegratorPolicy.Select(new IntegratorContext(
                chunk, flight, sun, sunAllowed, coast, altitude, vertical));
        }
    }
}
