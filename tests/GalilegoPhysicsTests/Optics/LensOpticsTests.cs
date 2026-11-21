using System;
using Galilego.Core;
using Xunit;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Core-only gates for the camera optics (no UnityEngine). These pin the two
    /// properties that make the flare physical rather than decorative: the veil
    /// carries exactly the scattered fraction of the source flux, and a ghost is
    /// diluted by the area of the aperture image it is defocused into.
    /// </summary>
    public sealed class LensOpticsTests
    {
        private const double Deg = Math.PI / 180d;
        private const double Alpha = 0.0127d;      // Sun at 0.368 AU, rad
        private const double Theta0 = 1.5d * Deg;
        private const double Beta = 2.2d;
        private const double Radiance = 77d;       // rendered V'

        [Fact]
        public void VeilIntegratesToTheScatteredFraction()
        {
            const double scatter = 0.015d;
            const double maxTheta = 100d * Theta0;
            const int steps = 40000;
            double integral = 0d;
            for (int i = 0; i < steps; i++)
            {
                double theta = ((i + 0.5d) / steps) * maxTheta;
                integral += 2d * Math.PI * theta
                    * LensOptics.VeilRadiance(Radiance, Alpha, scatter, theta, Theta0, Beta)
                    * (maxTheta / steps);
            }

            double expected = scatter * Radiance * Math.PI * Alpha * Alpha;
            Assert.InRange(integral / expected, 0.999d, 1.001d);
        }

        [Fact]
        public void VeilIsMonotonicAndScalesWithSourceArea()
        {
            double previous = double.PositiveInfinity;
            for (double theta = 0d; theta < 20d * Deg; theta += 0.05d * Deg)
            {
                double value = LensOptics.VeilRadiance(Radiance, Alpha, 0.02d, theta, Theta0, Beta);
                Assert.True(value <= previous, "veil must fall with angle");
                previous = value;
            }

            double one = LensOptics.VeilPeakRadiance(Radiance, Alpha, 0.02d, Theta0, Beta);
            double two = LensOptics.VeilPeakRadiance(Radiance, 2d * Alpha, 0.02d, Theta0, Beta);
            Assert.InRange(two / one, 4d - 1e-9d, 4d + 1e-9d);
        }

        [Fact]
        public void GhostIsDilutedByTheApertureImageArea()
        {
            const double reflectance = 8e-3d;

            // Overfilled: the Sun is larger than the ghost -> full reflectance.
            Assert.InRange(
                LensOptics.GhostRadiance(Radiance, Alpha, 0.5d * Alpha, reflectance),
                reflectance * Radiance * (1d - 1e-12d),
                reflectance * Radiance * (1d + 1e-12d));

            // Equal sizes: full reflectance (ratio clamped at 1).
            Assert.Equal(
                reflectance * Radiance,
                LensOptics.GhostRadiance(Radiance, Alpha, Alpha, reflectance),
                9);

            // Twice the radius: a quarter of the radiance.
            Assert.Equal(
                0.25d * reflectance * Radiance,
                LensOptics.GhostRadiance(Radiance, Alpha, 2d * Alpha, reflectance),
                9);

            Assert.Equal(0d, LensOptics.GhostRadiance(Radiance, Alpha, 0d, reflectance), 12);
        }

        [Fact]
        public void GhostsAreReflectionsThroughTheOpticalAxis()
        {
            LensOptics.GhostCenter(0.3d, -0.2d, 1d, out double mx, out double my);
            Assert.Equal(-0.3d, mx, 12);
            Assert.Equal(0.2d, my, 12);

            LensOptics.GhostCenter(0.3d, -0.2d, 0.5d, out double hx, out double hy);
            Assert.Equal(-0.15d, hx, 12);
            Assert.Equal(0.1d, hy, 12);

            // k = 2 puts the ghost on the far side at twice the distance.
            LensOptics.GhostCenter(0.3d, -0.2d, 2d, out double cx, out double cy);
            Assert.Equal(-0.6d, cx, 12);
            Assert.Equal(0.4d, cy, 12);
        }

        [Fact]
        public void DiffractionScaleIsLambdaOverAperture()
        {
            // 24 mm at f/8: D = 3 mm, λ/D = 1.83e-4 rad = 0.0105°.
            double wide = LensOptics.DiffractionAngleRad(550e-9d, 24d, 8d);
            Assert.Equal(550e-9d / 3e-3d, wide, 12);

            // Stopping down four stops makes D four times smaller and the spike
            // four times wider: the reason a real sun star only shows at f/16.
            double stopped = LensOptics.DiffractionAngleRad(550e-9d, 24d, 32d);
            Assert.Equal(4d * wide, stopped, 12);

            Assert.Equal(0d, LensOptics.DiffractionAngleRad(550e-9d, 0d, 8d), 12);
        }

        [Fact]
        public void BladeCountMapsToRayCount()
        {
            Assert.Equal(0, LensOptics.DiffractionLineCount(0));
            Assert.Equal(0, LensOptics.DiffractionLineCount(2));
            Assert.Equal(3, LensOptics.DiffractionLineCount(6));    // 6 rays
            Assert.Equal(4, LensOptics.DiffractionLineCount(8));    // 8 rays
            Assert.Equal(5, LensOptics.DiffractionLineCount(5));    // 10 rays
            Assert.Equal(9, LensOptics.DiffractionLineCount(9));    // 18 rays
        }

        [Fact]
        public void FieldFalloffIsCosFourth()
        {
            Assert.Equal(1d, LensOptics.CameraVignetting(0d), 12);
            double tan30 = Math.Tan(30d * Deg);
            Assert.Equal(1d / 16d, LensOptics.CameraVignetting(Math.Sqrt(3d)), 5);
            Assert.InRange(LensOptics.CameraVignetting(tan30), 0.55d, 0.58d);
        }

        [Fact]
        public void SpikeWidthNeverGoesBelowThePixel()
        {
            double pixel = 1.07e-3d;
            Assert.Equal(pixel, LensOptics.SpikeDrawWidthRad(1.83e-4d, pixel), 12);
            Assert.Equal(2e-3d, LensOptics.SpikeDrawWidthRad(2e-3d, pixel), 12);
            Assert.Equal(0d, LensOptics.SpikeDrawWidthRad(0d, pixel), 12);
        }

        [Fact]
        public void EyeGlareIsAFractionOfTheSourceRadiance()
        {
            // The eye's GSF is normalised to what the source LOOKS like: the peak
            // is exactly centreFraction of the source radiance, not a flux share.
            double peak = LensOptics.EyeGlareRadiance(Radiance, 0.12d, 0d, 1.5d * Deg, 2d);
            Assert.Equal(0.12d * Radiance, peak, 9);

            double atOne = LensOptics.EyeGlareRadiance(Radiance, 0.12d, 1d * Deg, 1.5d * Deg, 2d);
            Assert.InRange(atOne / peak, 0.47d, 0.49d);      // (1+0.444)^-2

            double atTen = LensOptics.EyeGlareRadiance(Radiance, 0.12d, 10d * Deg, 1.5d * Deg, 2d);
            Assert.InRange(atTen / peak, 4.0e-4d, 5.5e-4d);

            // At 10° the eye still carries ~5e-4 of the disc where a 3 % camera
            // veil has already fallen to ~1e-4: that is why the sky next to the
            // Sun is washed out but the rest of the sky is not.
            double cameraVeil = LensOptics.VeilRadiance(
                Radiance, Alpha, 0.03d, 10d * Deg, 0.6d * Deg, 2.6d);
            Assert.True(atTen > 2d * cameraVeil);
        }

        [Fact]
        public void CiliaryCoronaIsAPointSourcePhenomenon()
        {
            const double pointScale = 0.1d * Deg;
            const double sunAlpha = 0.0128d;   // 0.73°: the solar disc in this system

            Assert.Equal(1d, LensOptics.CiliaryCoronaVisibility(0d, pointScale), 9);
            Assert.Equal(1d, LensOptics.CiliaryCoronaVisibility(0.5d * pointScale, pointScale), 9);
            Assert.Equal(0.5d, LensOptics.CiliaryCoronaVisibility(1.5d * pointScale, pointScale), 9);
            Assert.Equal(0d, LensOptics.CiliaryCoronaVisibility(2d * pointScale, pointScale), 9);

            // The Sun's image is ~400× the eye's diffraction core: no star at all.
            Assert.Equal(0d, LensOptics.CiliaryCoronaVisibility(sunAlpha, pointScale), 9);
        }

        [Fact]
        public void PointSourcesScaleWithSolidAngle()
        {
            const double psf = 0.1d * Deg;
            const double sunAlpha = 0.0128d;

            // The solar disc is ~7× the eye's PSF core: fully resolved.
            Assert.Equal(1d, LensOptics.PointSourceFactor(sunAlpha, psf), 9);
            Assert.Equal(1d, LensOptics.PointSourceFactor(10d * psf, psf), 9);
            Assert.Equal(0.25d, LensOptics.PointSourceFactor(0.5d * psf, psf), 9);
            Assert.Equal(0.01d, LensOptics.PointSourceFactor(0.1d * psf, psf), 9);
            Assert.Equal(0d, LensOptics.PointSourceFactor(0d, psf), 9);
        }

        [Fact]
        public void SmoothstepMatchesTheDefinition()
        {
            Assert.Equal(0d, LensOptics.Smoothstep(0d, 1d, -2d), 12);
            Assert.Equal(0d, LensOptics.Smoothstep(0d, 1d, 0d), 12);
            Assert.Equal(1d, LensOptics.Smoothstep(0d, 1d, 1d), 12);
            Assert.Equal(1d, LensOptics.Smoothstep(0d, 1d, 2d), 12);
            Assert.Equal(0.5d, LensOptics.Smoothstep(0d, 1d, 0.5d), 12);
            Assert.Equal(0.15625d, LensOptics.Smoothstep(0d, 1d, 0.25d), 12);
        }

        [Fact]
        public void DegenerateInputsProduceNoLight()
        {
            Assert.Equal(0d, LensOptics.VeilPeakRadiance(0d, Alpha, 0.02d, Theta0, Beta), 12);
            Assert.Equal(0d, LensOptics.VeilPeakRadiance(Radiance, 0d, 0.02d, Theta0, Beta), 12);
            Assert.Equal(0d, LensOptics.VeilPeakRadiance(Radiance, Alpha, 0d, Theta0, Beta), 12);
            Assert.Equal(0d, LensOptics.VeilPeakRadiance(Radiance, Alpha, 0.02d, 0d, Beta), 12);
            Assert.Equal(0d, LensOptics.VeilPeakRadiance(Radiance, Alpha, 0.02d, Theta0, 1d), 12);
            Assert.Equal(0d, LensOptics.GhostRadiance(0d, Alpha, Alpha, 1e-2d), 12);
            Assert.Equal(0d, LensOptics.VeilProfile(1d, Theta0, 1d), 12);
        }
    }
}
