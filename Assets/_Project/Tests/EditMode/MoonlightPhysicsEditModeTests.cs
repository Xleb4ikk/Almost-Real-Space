using System;
using Galilego.Core;
using NUnit.Framework;

namespace Galilego.Tests.EditMode
{
    /// <summary>
    /// Unity-side gates for the pure moonlight model. The same numbers are pinned by
    /// the Core xUnit suite (tests/GalilegoPhysicsTests/Optics), so the two runners
    /// cannot diverge.
    /// </summary>
    public sealed class MoonlightPhysicsEditModeTests
    {
        private const double Deg = Math.PI / 180d;
        private const double ArtemisRadius = 412000d;
        private const double ArtemisDistance = 90000000d;

        [Test]
        public void LunaPhaseLawHasTheOppositionSurge()
        {
            Assert.That(MoonlightPhysics.PhaseFunction(0d), Is.EqualTo(1d).Within(1e-12d));
            Assert.That(MoonlightPhysics.PhaseFunction(90d * Deg), Is.EqualTo(0.091d).Within(0.003d));
            Assert.That(MoonlightPhysics.PhaseFunction(Math.PI), Is.EqualTo(0d).Within(1e-12d));
        }

        [Test]
        public void ArtemisFullMoonIs2_9eMinus6OfTheStar()
        {
            double ratio = MoonlightPhysics.IrradianceRatioToSun(
                MoonlightPhysics.MoonGeometricAlbedo, ArtemisRadius, ArtemisDistance, 0d);

            Assert.That(ratio, Is.EqualTo(2.85e-6d).Within(0.1e-6d));
        }

        [Test]
        public void NightGainMakesTheMoonVisible()
        {
            double ratio = MoonlightPhysics.IrradianceRatioToSun(
                MoonlightPhysics.MoonGeometricAlbedo, ArtemisRadius, ArtemisDistance, 0d);
            double scene = MoonlightPhysics.SceneIntensity(ratio, 16d);

            Assert.That(scene, Is.EqualTo(0.187d).Within(0.01d));
        }

        [Test]
        public void LunarEclipseZeroesTheLightAtAlignment()
        {
            Vector3d star = new Vector3d(-55187600000d, 0d, 0d);
            Vector3d terra = Vector3d.Zero;
            Vector3d moon = new Vector3d(90000000d, 0d, 0d);

            double aligned = MoonlightPhysics.LunarEclipseVisibility(
                moon, terra, 1143000d, star, 695700000d);
            Assert.That(aligned, Is.EqualTo(0d).Within(1e-6d));
        }
    }
}
