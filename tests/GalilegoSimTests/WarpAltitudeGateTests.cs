using Galilego.Events;
using Xunit;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Contract of the owner's altitude bands (2026-11):
    ///   below 15 km — full warp forbidden (physical ×2…×6 stays legal);
    ///   15…80 km    — full warp up to ×50;
    ///   80 km and up — the whole ladder.
    /// The gate is pure arithmetic, so the boundaries are pinned here: a changed
    /// constant must fail a test, not silently move the player's ceiling.
    /// </summary>
    public sealed class WarpAltitudeGateTests
    {
        [Theory]
        [InlineData(0d, WarpAltitudeBand.Low)]
        [InlineData(14999d, WarpAltitudeBand.Low)]
        [InlineData(15000d, WarpAltitudeBand.Mid)]
        [InlineData(50000d, WarpAltitudeBand.Mid)]
        [InlineData(79999d, WarpAltitudeBand.Mid)]
        [InlineData(80000d, WarpAltitudeBand.High)]
        [InlineData(400000d, WarpAltitudeBand.High)]
        public void BandAt_UsesTheFifteenAndEightyKilometreEdges(double altitudeMeters, WarpAltitudeBand expected)
        {
            Assert.Equal(expected, WarpAltitudeGate.BandAt(altitudeMeters));
        }

        [Fact]
        public void BandAt_UnknownAltitudeIsTheStrictestBand()
        {
            // NaN/negative must never unlock warp: written as a positive test on purpose.
            Assert.Equal(WarpAltitudeBand.Low, WarpAltitudeGate.BandAt(double.NaN));
            Assert.Equal(WarpAltitudeBand.Low, WarpAltitudeGate.BandAt(-1d));
        }

        [Fact]
        public void MaxFullFactor_LowBandForbidsFullWarp()
        {
            // 0 is the "forbidden" sentinel: the physical ladder is still legal (×1…×6).
            Assert.Equal(0d, WarpAltitudeGate.MaxFullFactor(0d));
            Assert.Equal(0d, WarpAltitudeGate.MaxFullFactor(14999d));
        }

        [Fact]
        public void MaxFullFactor_MiddleBandCapsAtFifty()
        {
            Assert.Equal(50d, WarpAltitudeGate.MaxFullFactor(15000d));
            Assert.Equal(50d, WarpAltitudeGate.MaxFullFactor(79999d));
        }

        [Fact]
        public void MaxFullFactor_HighBandIsUnlimited()
        {
            Assert.True(double.IsPositiveInfinity(WarpAltitudeGate.MaxFullFactor(80000d)));
        }

        [Fact]
        public void CapReason_NamesTheBandThePlayerIsIn()
        {
            Assert.Equal("ниже 15 км", WarpAltitudeGate.CapReason(WarpAltitudeBand.Low));
            Assert.Equal("ниже 80 км", WarpAltitudeGate.CapReason(WarpAltitudeBand.Mid));
            Assert.Equal(string.Empty, WarpAltitudeGate.CapReason(WarpAltitudeBand.High));
        }
    }
}
