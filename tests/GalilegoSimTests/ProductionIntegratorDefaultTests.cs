using Galilego.Spacecraft;
using Xunit;

namespace GalilegoPhysicsTests
{
    public sealed class ProductionIntegratorDefaultTests
    {
        [Fact]
        public void ProductionUsesDop853ByDefault()
        {
            // DOPRI5 is a certified option only at tight tolerances on
            // high-eccentricity orbits. The default must stay DOP853.
            var physics = new SpacecraftPhysics();
            Assert.Equal(SpacecraftIntegrator.Dop853, physics.Integrator);
        }
    }
}
