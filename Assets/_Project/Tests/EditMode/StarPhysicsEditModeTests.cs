using Galilego.Core;
using NUnit.Framework;

namespace Galilego.Tests.EditMode
{
    /// <summary>
    /// EditMode gates for the pure stellar model. The same numbers are exercised by
    /// the Core-only xUnit suite (tests/GalilegoPhysicsTests), so the Unity test
    /// runner and the fast CI path cannot diverge.
    /// </summary>
    public sealed class StarPhysicsEditModeTests
    {
        private const double Tolerance = 0.05d;

        private static StarState RedDwarf()
        {
            return StarPhysics.Derive(new StarDefinition(0.2d, adaptation01: 0.25d));
        }

        [Test]
        public void RedDwarfMatchesTaskReference()
        {
            StarState star = RedDwarf();

            Assert.That(star.LuminositySolar, Is.EqualTo(0.00568d).Within(0.00568d * Tolerance));
            Assert.That(star.RadiusSolar, Is.EqualTo(0.235d).Within(0.235d * Tolerance));
            Assert.That(star.RadiusKm, Is.EqualTo(163500d).Within(163500d * Tolerance));
            Assert.That(star.EffectiveTemperatureK, Is.EqualTo(3270d).Within(3270d * Tolerance));
            Assert.That(star.SurfaceGravityMps2, Is.EqualTo(990d).Within(990d * Tolerance));
            Assert.That(star.GranulationCellKm, Is.EqualTo(156d).Within(156d * Tolerance));
            Assert.That(star.GranulationCells, Is.EqualTo(2100d).Within(2100d * Tolerance));
        }

        [Test]
        public void RedDwarfColourMatchesTaskReference()
        {
            // The task's channel reference is the unadapted black-body vector cT;
            // cT' with adaptation = 0.25 is deliberately closer to white.
            StarState star = StarPhysics.Derive(new StarDefinition(0.2d, adaptation01: 0d));

            Assert.That(star.ChromaticityX, Is.EqualTo(0.419d).Within(0.01d));
            Assert.That(star.ChromaticityY, Is.EqualTo(0.397d).Within(0.01d));

            double max = System.Math.Max(star.ColorR, System.Math.Max(star.ColorG, star.ColorB));
            Assert.That(star.ColorR / max, Is.EqualTo(1.00d).Within(0.03d));
            Assert.That(star.ColorG / max, Is.EqualTo(0.53d).Within(0.04d));
            Assert.That(star.ColorB / max, Is.EqualTo(0.21d).Within(0.04d));
        }

        [Test]
        public void RedDwarfBrightnessMatchesTaskReference()
        {
            StarState star = RedDwarf();

            // The task's order is 1.4e3 ±30 %; the exact integral gives 1.64e3.
            Assert.That(star.DiscBrightness, Is.InRange(980d, 1820d));
            Assert.That(star.DiscBrightnessCompressed, Is.EqualTo(50d).Within(5d));
        }

        [Test]
        public void SolarStarMatchesTaskReference()
        {
            StarState star = StarPhysics.Derive(new StarDefinition(1d));

            Assert.That(star.EffectiveTemperatureK, Is.EqualTo(5772d).Within(1d));
            Assert.That(star.GranulationCells, Is.EqualTo(1400d).Within(1400d * Tolerance));
            Assert.That(star.RadiusSolar, Is.EqualTo(1d).Within(1e-9d));
            Assert.That(star.LuminositySolar, Is.EqualTo(1d).Within(1e-9d));
        }

        [Test]
        public void GeometryAtReferenceDistances()
        {
            StarState star = RedDwarf();
            const double deg = 180d / System.Math.PI;

            double diameter075 = star.AngularDiameterDeg(0.075d * StarPhysics.AuKm);
            Assert.That(diameter075, Is.EqualTo(1.67d).Within(0.05d));
            Assert.That(star.IrradianceWm2(0.075d * StarPhysics.AuKm) / StarPhysics.Esun1Au,
                Is.EqualTo(1.01d).Within(0.05d));
            Assert.That(star.ApparentMagnitude(0.075d * StarPhysics.AuKm),
                Is.EqualTo(-26.75d).Within(0.05d));

            double pixelAngle = StarPhysics.PixelAngleRad(60d / deg, 1080);
            Assert.That(2d * star.AngularRadiusRad(0.075d * StarPhysics.AuKm) / pixelAngle,
                Is.EqualTo(30d).Within(1d));

            Assert.That(star.AngularDiameterDeg(StarPhysics.AuKm) * 60d,
                Is.EqualTo(7.5d).Within(0.2d));
            Assert.That(star.ApparentMagnitude(StarPhysics.AuKm),
                Is.EqualTo(-21.1d).Within(0.1d));

            Assert.That(star.AngularDiameterDeg(5.2d * StarPhysics.AuKm) * 60d,
                Is.EqualTo(1.4d).Within(0.1d));
            Assert.That(star.ApparentMagnitude(5.2d * StarPhysics.AuKm),
                Is.EqualTo(-17.5d).Within(0.1d));

            StarPhysics.SubpixelDisc(
                star.AngularRadiusRad(5.2d * StarPhysics.AuKm), pixelAngle,
                out double drawAlpha, out double energyScale);
            Assert.That(drawAlpha, Is.GreaterThan(star.AngularRadiusRad(5.2d * StarPhysics.AuKm)));
            Assert.That(energyScale, Is.LessThan(1d));
        }

        [Test]
        public void EclipseFractionsCoverAllBranches()
        {
            Assert.That(StarPhysics.EclipseVisibleFraction(1d, 1d, 3d), Is.EqualTo(1d).Within(1e-12d));
            Assert.That(StarPhysics.EclipseVisibleFraction(1d, 1.5d, 0d), Is.EqualTo(0d).Within(1e-12d));
            Assert.That(StarPhysics.EclipseVisibleFraction(1d, 0.5d, 0d),
                Is.EqualTo(0.75d).Within(1e-12d));
            Assert.That(StarPhysics.EclipseVisibleFraction(1d, 1d, 2d), Is.EqualTo(1d).Within(1e-12d));

            double partial = StarPhysics.EclipseVisibleFraction(1d, 1d, 1d);
            Assert.That(partial, Is.InRange(0d, 1d));
            Assert.That(partial, Is.LessThan(1d));
        }

        [Test]
        public void SpotTemperaturesAndContrastFollowTheTask()
        {
            StarState star = RedDwarf();

            Assert.That(star.SpotUmbraTemperatureK, Is.EqualTo(2820d).Within(30d));
            Assert.That(star.SpotPenumbraTemperatureK, Is.EqualTo(3112d).Within(30d));
            Assert.That(star.SpotUmbraR, Is.EqualTo(0.317d).Within(0.01d));
            Assert.That(star.SpotUmbraG, Is.EqualTo(0.280d).Within(0.01d));
            Assert.That(star.SpotUmbraB, Is.EqualTo(0.222d).Within(0.01d));
            Assert.That(star.SpotMaxCoverage, Is.EqualTo(0.4d).Within(0.001d));
            Assert.That(star.SpotCoverage, Is.EqualTo(0.4d).Within(0.001d));
        }

        [Test]
        public void LuminosityAndRadiusPiecesAreContinuous()
        {
            Assert.That(StarPhysics.LuminositySolar(0.43d * (1d - 1e-6d)),
                Is.EqualTo(StarPhysics.LuminositySolar(0.43d * (1d + 1e-6d))).Within(0.01d));
            Assert.That(StarPhysics.LuminositySolar(2d * (1d - 1e-6d)),
                Is.EqualTo(StarPhysics.LuminositySolar(2d * (1d + 1e-6d))).Within(0.2d));
            Assert.That(StarPhysics.RadiusSolar(0.49d), Is.EqualTo(System.Math.Pow(0.49d, 0.9d)).Within(1e-9d));
            Assert.That(StarPhysics.RadiusSolar(0.51d), Is.EqualTo(System.Math.Pow(0.51d, 0.8d)).Within(1e-9d));
        }

        [Test]
        public void CompressionIsMonotonicAndFluxIsConserved()
        {
            double previous = 0d;
            for (double brightness = 1d; brightness < 1e8d; brightness *= 10d)
            {
                double compressed = StarPhysics.CompressBrightness(brightness);
                Assert.That(compressed, Is.GreaterThan(previous));
                previous = compressed;
            }

            double alpha = 0.00021d;
            double pixelAngle = 0.000969d;
            StarPhysics.SubpixelDisc(alpha, pixelAngle, out double draw, out double scale);
            Assert.That(scale * draw * draw, Is.EqualTo(alpha * alpha).Within(1e-18d));
        }

        [Test]
        public void MoffatProfileIntegratesToOne()
        {
            const double theta0 = 0.002618d;
            const double beta = 1.8d;
            const int steps = 20000;
            const double maxTheta = 50d * theta0;
            double integral = 0d;
            for (int i = 0; i < steps; i++)
            {
                double theta = ((i + 0.5d) / steps) * maxTheta;
                integral += 2d * System.Math.PI * theta
                    * StarPhysics.MoffatProfile(theta, theta0, beta) * (maxTheta / steps);
            }

            Assert.That(integral, Is.EqualTo(1d).Within(0.01d));
            Assert.That(StarPhysics.CoronaIntensity(1d, 2e-5d), Is.EqualTo(2e-5d).Within(1e-6d));
        }

        [Test]
        public void AnyMainSequenceMassIsFinite()
        {
            foreach (double mass in new[] { 0.08d, 0.1d, 0.2d, 0.5d, 1d, 2d, 10d, 55d, 100d })
            {
                StarState star = StarPhysics.Derive(new StarDefinition(mass));
                Assert.That(double.IsFinite(star.LuminositySolar), Is.True, "L at M=" + mass);
                Assert.That(double.IsFinite(star.RadiusSolar), Is.True, "R at M=" + mass);
                Assert.That(double.IsFinite(star.EffectiveTemperatureK), Is.True, "T at M=" + mass);
                Assert.That(star.ColorR, Is.GreaterThan(0d));
                Assert.That(star.ColorG, Is.GreaterThan(0d));
                Assert.That(star.ColorB, Is.GreaterThan(0d));
            }
        }
    }
}
