using System;
using Galilego.Core;
using Xunit;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Independent gates for the star-disc occlusion geometry. The expected values
    /// are derived here from the angular radii, not read back from the code under
    /// test: a partial eclipse is the uncovered fraction of the disc area, and a
    /// body behind the star cannot occlude it.
    /// </summary>
    public sealed class EclipseGeometryTests
    {
        private const double Deg = Math.PI / 180d;

        // The scene's real values (OutdoorsScene): Sol R = 695 700 km at
        // 55.1876e9 m, Terra R = 1143 km, Artemis R = 412 km at 90e6 m.
        private const double SolRadius = 695700000d;
        private const double SolDistance = 55187600000d;
        private const double TerraRadius = 1143000d;
        private const double MoonRadius = 412000d;
        private const double MoonDistance = 90000000d;

        [Fact]
        public void LunarEclipseAtAlignmentIsTotal()
        {
            // Sun -- Terra -- Moon: the planet is the occluder, and its angular
            // radius from the Moon (atan(1143/90000) = 0.7277 deg) is larger than
            // the Sun's (0.7223 deg), so the centre of the disc is covered.
            Vector3d star = new Vector3d(-SolDistance, 0d, 0d);
            Vector3d terra = Vector3d.Zero;
            Vector3d moon = new Vector3d(MoonDistance, 0d, 0d);

            double visible = EclipseGeometry.VisibleFraction(
                moon, star, SolRadius, terra, TerraRadius);

            Assert.Equal(0d, visible, 12);
        }

        [Fact]
        public void NewMoonFromTerraIsAPartialEclipse()
        {
            // Sun -- Moon -- Terra: the Moon's disc is smaller than the Sun's, so
            // even perfect alignment leaves an annulus. The expected visible fraction
            // is 1 - (a_moon / a_sun)^2, with both angular radii from asin(R/d).
            Vector3d star = new Vector3d(-SolDistance, 0d, 0d);
            Vector3d terra = Vector3d.Zero;
            Vector3d moon = new Vector3d(-MoonDistance, 0d, 0d);

            double alpha = Math.Asin(SolRadius / SolDistance);
            double occluder = Math.Asin(MoonRadius / MoonDistance);
            double expected = 1d - (occluder * occluder) / (alpha * alpha);

            double visible = EclipseGeometry.VisibleFraction(
                terra, star, SolRadius, moon, MoonRadius);

            Assert.Equal(expected, visible, 9);
            Assert.InRange(visible, 0.86d, 0.87d); // ~13 % of the Sun's area
        }

        [Fact]
        public void AnOccluderBeyondTheStarDoesNothing()
        {
            Vector3d observer = Vector3d.Zero;
            Vector3d star = new Vector3d(-SolDistance, 0d, 0d);
            Vector3d behind = new Vector3d(SolDistance * 2d, 0d, 0d);

            double visible = EclipseGeometry.VisibleFraction(
                observer, star, SolRadius, behind, TerraRadius);

            Assert.Equal(1d, visible, 12);
        }

        [Fact]
        public void AngularRadiusUsesAsinNotSmallAngle()
        {
            // On the surface a sphere covers half the sky: the exact value is 90°.
            Assert.Equal(Math.PI * 0.5d, EclipseGeometry.AngularRadius(TerraRadius, TerraRadius), 12);
            Assert.Equal(0d, EclipseGeometry.AngularRadius(0d, 1d), 12);
            Assert.Equal(0d, EclipseGeometry.AngularRadius(TerraRadius, 0d), 12);
        }

        [Fact]
        public void OnlyALargerOccluderCoversTheWholeBody()
        {
            // Terra can cover Artemis from 90 000 km; Artemis cannot cover Terra.
            Assert.True(EclipseGeometry.CoversBody(MoonRadius, TerraRadius, MoonDistance));
            Assert.False(EclipseGeometry.CoversBody(TerraRadius, MoonRadius, MoonDistance));
            Assert.True(EclipseGeometry.CoversBody(TerraRadius, TerraRadius, MoonDistance));
        }

        [Fact]
        public void ClearSkyIsOneAndTheMoonBeyondTheStarIsIgnored()
        {
            Vector3d observer = new Vector3d(0d, 0d, 0d);
            Vector3d star = new Vector3d(-SolDistance, 0d, 0d);
            Vector3d side = new Vector3d(0d, MoonDistance, 0d);

            Assert.Equal(1d, EclipseGeometry.VisibleFraction(
                observer, star, SolRadius, side, MoonRadius), 12);
        }
    }
}
