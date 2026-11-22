using System;
using Galilego.Core;
using Xunit;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Core-only gates for the moon's reflected light. These pin the three numbers
    /// that make the game's night physically shaped rather than hand-tuned:
    /// the lunar phase law (quarter ≈ 1/11 of full), the full-disc irradiance
    /// ratio for Artemis' real radius/orbit, and the eclipse geometry.
    /// </summary>
    public sealed class MoonlightPhysicsTests
    {
        private const double Deg = Math.PI / 180d;

        // Artemis as authored in the scene: R = 412 km, a = 90 000 km.
        private const double ArtemisRadius = 412000d;
        private const double ArtemisDistance = 90000000d;

        [Fact]
        public void FullPhaseIsUnityAndQuarterIsAboutOneEleventh()
        {
            Assert.Equal(1d, MoonlightPhysics.PhaseFunction(0d), 12);

            double quarter = MoonlightPhysics.PhaseFunction(90d * Deg);
            Assert.InRange(quarter, 0.089d, 0.093d);
            Assert.InRange(1d / quarter, 10.7d, 11.3d);
        }

        [Fact]
        public void NewMoonReflectsNothing()
        {
            Assert.Equal(0d, MoonlightPhysics.PhaseFunction(Math.PI), 12);
            Assert.InRange(MoonlightPhysics.PhaseFunction(179.9d * Deg), 0d, 1e-3d);
        }

        [Fact]
        public void IlluminatedFractionMatchesTheDisc()
        {
            Assert.Equal(1d, MoonlightPhysics.IlluminatedFraction(0d), 12);
            Assert.Equal(0.5d, MoonlightPhysics.IlluminatedFraction(90d * Deg), 12);
            Assert.Equal(0d, MoonlightPhysics.IlluminatedFraction(Math.PI), 12);
        }

        [Fact]
        public void PhaseAngleIsTheStarMoonObserverAngle()
        {
            Vector3d star = new Vector3d(1d, 0d, 0d);
            Vector3d moon = Vector3d.Zero;
            Vector3d observer = new Vector3d(0d, 1d, 0d);

            Assert.Equal(90d * Deg, MoonlightPhysics.PhaseAngleRad(moon, star, observer), 12);
            Assert.Equal(0d, MoonlightPhysics.PhaseAngleRad(moon, star, star * 2d), 12);
        }

        [Fact]
        public void ArtemisFullMoonRatioIsTheRealMoonsOrder()
        {
            // p · (R/d)² = 0.136 · (412 000 / 90 000 000)².
            double ratio = MoonlightPhysics.IrradianceRatioToSun(
                MoonlightPhysics.MoonGeometricAlbedo, ArtemisRadius, ArtemisDistance, 0d);

            Assert.InRange(ratio, 2.7e-6d, 3.0e-6d);
        }

        [Fact]
        public void SceneGainMakesTheFullMoonVisibleAtNight()
        {
            double ratio = MoonlightPhysics.IrradianceRatioToSun(
                MoonlightPhysics.MoonGeometricAlbedo, ArtemisRadius, ArtemisDistance, 0d);
            double scene = MoonlightPhysics.SceneIntensity(ratio, 16d);

            // 16 EV turns the 2.9e-6 physical ratio into ~0.19 of the noon sun.
            Assert.InRange(scene, 0.17d, 0.21d);
            Assert.Equal(ratio * 65536d, scene, 12);
        }

        [Fact]
        public void FullMoonIlluminanceIsOneToThreeLux()
        {
            // The scene's star sits at 0.369 AU: ~10 000 W/m² at Terra.
            const double starIrradiance = 10000d;
            double ratio = MoonlightPhysics.IrradianceRatioToSun(
                MoonlightPhysics.MoonGeometricAlbedo, ArtemisRadius, ArtemisDistance, 0d);
            double lux = MoonlightPhysics.IlluminanceLux(ratio, starIrradiance);

            // The scene's sun is 7.3× Earth's irradiance (0.369 AU), so the
            // full moon is 7.3× Earth's 0.25 lx: ~2.7 lx, still the same
            // moon/sun ratio of 2.9e-6.
            Assert.InRange(lux, 2.4d, 3.0d);
        }

        [Fact]
        public void LunarEclipseGeometryDarkensTheMoon()
        {
            // Sol at 0.369 AU (R☉ = 695 700 km), Terra R = 1143 km, moon at 90 000 km.
            Vector3d star = new Vector3d(-55187600000d, 0d, 0d);
            Vector3d terra = Vector3d.Zero;
            Vector3d moon = new Vector3d(90000000d, 0d, 0d);

            double aligned = MoonlightPhysics.LunarEclipseVisibility(
                moon, terra, 1143000d, star, 695700000d);
            Assert.Equal(0d, aligned, 6);

            Vector3d side = new Vector3d(0d, 90000000d, 0d);
            double clear = MoonlightPhysics.LunarEclipseVisibility(
                side, terra, 1143000d, star, 695700000d);
            Assert.Equal(1d, clear, 6);
        }
    }
}
