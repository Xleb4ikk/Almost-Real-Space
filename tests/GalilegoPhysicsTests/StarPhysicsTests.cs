using System;
using Galilego.Core;
using Xunit;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Core-only gates for the stellar model (no UnityEngine, no Unity test runner).
    /// The same references are in the EditMode suite; this project is the fast CI
    /// path and the guard that StarPhysics never grows a Unity dependency.
    /// </summary>
    public sealed class StarPhysicsTests
    {
        private const double Tolerance = 0.05d;

        private static StarState RedDwarf() =>
            StarPhysics.Derive(new StarDefinition(0.2d, adaptation01: 0.25d));

        [Fact]
        public void RedDwarfMatchesTaskReference()
        {
            StarState star = RedDwarf();

            Assert.Equal(0.00568d, star.LuminositySolar, 5);
            Assert.Equal(0.235d, star.RadiusSolar, 3);
            Assert.InRange(star.RadiusKm, 163500d * (1d - Tolerance), 163500d * (1d + Tolerance));
            Assert.InRange(star.EffectiveTemperatureK, 3270d * (1d - Tolerance), 3270d * (1d + Tolerance));
            Assert.InRange(star.SurfaceGravityMps2, 990d * (1d - Tolerance), 990d * (1d + Tolerance));
            Assert.InRange(star.GranulationCellKm, 156d * (1d - Tolerance), 156d * (1d + Tolerance));
            Assert.InRange(star.GranulationCells, 2100d * (1d - Tolerance), 2100d * (1d + Tolerance));
        }

        [Fact]
        public void RedDwarfColourMatchesTaskReference()
        {
            // The task's channel reference is the unadapted black-body vector cT;
            // cT' with adaptation = 0.25 is deliberately closer to white.
            StarState star = StarPhysics.Derive(new StarDefinition(0.2d, adaptation01: 0d));

            Assert.Equal(0.419d, star.ChromaticityX, 2);
            Assert.Equal(0.397d, star.ChromaticityY, 2);

            double max = Math.Max(star.ColorR, Math.Max(star.ColorG, star.ColorB));
            Assert.InRange(star.ColorR / max, 0.97d, 1.03d);
            Assert.InRange(star.ColorG / max, 0.49d, 0.57d);
            Assert.InRange(star.ColorB / max, 0.17d, 0.25d);
        }

        [Fact]
        public void RedDwarfBrightnessMatchesTaskReference()
        {
            StarState star = RedDwarf();

            Assert.InRange(star.DiscBrightness, 980d, 1820d);
            Assert.InRange(star.DiscBrightnessCompressed, 45d, 55d);
        }

        [Fact]
        public void SolarStarMatchesTaskReference()
        {
            StarState star = StarPhysics.Derive(new StarDefinition(1d));

            Assert.Equal(5772d, star.EffectiveTemperatureK, 0);
            Assert.InRange(star.GranulationCells, 1400d * (1d - Tolerance), 1400d * (1d + Tolerance));
            Assert.Equal(1d, star.LuminositySolar, 9);
            Assert.Equal(1d, star.RadiusSolar, 9);
        }

        [Fact]
        public void GeometryAtReferenceDistances()
        {
            StarState star = RedDwarf();
            const double deg = 180d / Math.PI;

            Assert.InRange(star.AngularDiameterDeg(0.075d * StarPhysics.AuKm), 1.62d, 1.72d);
            Assert.InRange(star.IrradianceWm2(0.075d * StarPhysics.AuKm) / StarPhysics.Esun1Au, 0.96d, 1.06d);
            Assert.InRange(star.ApparentMagnitude(0.075d * StarPhysics.AuKm), -26.8d, -26.7d);

            double pixelAngle = StarPhysics.PixelAngleRad(60d / deg, 1080);
            Assert.InRange(2d * star.AngularRadiusRad(0.075d * StarPhysics.AuKm) / pixelAngle, 29d, 31d);

            Assert.InRange(star.AngularDiameterDeg(StarPhysics.AuKm) * 60d, 7.3d, 7.7d);
            Assert.InRange(star.ApparentMagnitude(StarPhysics.AuKm), -21.2d, -21.0d);

            Assert.InRange(star.AngularDiameterDeg(5.2d * StarPhysics.AuKm) * 60d, 1.3d, 1.5d);
            Assert.InRange(star.ApparentMagnitude(5.2d * StarPhysics.AuKm), -17.6d, -17.4d);

            StarPhysics.SubpixelDisc(
                star.AngularRadiusRad(5.2d * StarPhysics.AuKm), pixelAngle,
                out double drawAlpha, out double energyScale);
            Assert.True(drawAlpha > star.AngularRadiusRad(5.2d * StarPhysics.AuKm));
            Assert.InRange(energyScale, 0d, 1d);
        }

        [Fact]
        public void EclipseBranchesAreCorrect()
        {
            Assert.Equal(1d, StarPhysics.EclipseVisibleFraction(1d, 1d, 3d), 12);
            Assert.Equal(0d, StarPhysics.EclipseVisibleFraction(1d, 1.5d, 0d), 12);
            Assert.Equal(0.75d, StarPhysics.EclipseVisibleFraction(1d, 0.5d, 0d), 12);
            Assert.Equal(1d, StarPhysics.EclipseVisibleFraction(1d, 1d, 2d), 12);
            Assert.InRange(StarPhysics.EclipseVisibleFraction(1d, 1d, 1d), 0d, 1d);
        }

        [Fact]
        public void SpotPhysicsMatchesTaskReference()
        {
            StarState star = RedDwarf();

            Assert.InRange(star.SpotUmbraTemperatureK, 2790d, 2850d);
            Assert.InRange(star.SpotPenumbraTemperatureK, 3080d, 3145d);
            Assert.InRange(star.SpotUmbraR, 0.30d, 0.33d);
            Assert.InRange(star.SpotUmbraG, 0.27d, 0.29d);
            Assert.InRange(star.SpotUmbraB, 0.21d, 0.23d);
            Assert.Equal(0.4d, star.SpotMaxCoverage, 3);
            Assert.Equal(0.4d, star.SpotCoverage, 3);
        }

        [Fact]
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
                integral += 2d * Math.PI * theta * StarPhysics.MoffatProfile(theta, theta0, beta)
                    * (maxTheta / steps);
            }

            Assert.InRange(integral, 0.99d, 1.01d);
            Assert.InRange(StarPhysics.CoronaIntensity(1d, 2e-5d), 1.9e-5d, 2.1e-5d);
        }

        [Fact]
        public void AnyMainSequenceMassIsFiniteAndPhysical()
        {
            foreach (double mass in new[] { 0.08d, 0.1d, 0.2d, 0.5d, 1d, 2d, 10d, 55d, 100d })
            {
                StarState star = StarPhysics.Derive(new StarDefinition(mass));
                Assert.True(double.IsFinite(star.LuminositySolar), "L at M=" + mass);
                Assert.True(double.IsFinite(star.RadiusSolar), "R at M=" + mass);
                Assert.True(double.IsFinite(star.EffectiveTemperatureK), "T at M=" + mass);
                Assert.True(star.ColorR > 0d);
                Assert.True(star.ColorG > 0d);
                Assert.True(star.ColorB > 0d);
            }
        }

        [Fact]
        public void CompressionIsMonotonicAndSubpixelFluxIsConserved()
        {
            double previous = 0d;
            for (double brightness = 1d; brightness < 1e8d; brightness *= 10d)
            {
                double compressed = StarPhysics.CompressBrightness(brightness);
                Assert.True(compressed > previous);
                previous = compressed;
            }

            const double alpha = 0.00021d;
            const double pixelAngle = 0.000969d;
            StarPhysics.SubpixelDisc(alpha, pixelAngle, out double draw, out double scale);
            Assert.InRange(scale * draw * draw, alpha * alpha * (1d - 1e-12d), alpha * alpha * (1d + 1e-12d));
        }
    }
}
