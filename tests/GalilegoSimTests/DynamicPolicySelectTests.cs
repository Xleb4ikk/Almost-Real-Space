using System.Collections.Generic;
using Galilego.Spacecraft;
using Xunit;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// D2/T1: one row per DECISION_TABLE.md regime and one pair per boundary of
    /// TASK_D.md §6. The selection is a pure function, so these cases pin the table
    /// without touching physics. Rows that are not Select conditions (GTO/HEO/GEO/
    /// flyby/hypothetical WH-Y6/IAS15) are represented by the context the game
    /// actually passes for them and must fall through to DOP853.
    /// </summary>
    public sealed class DynamicPolicySelectTests
    {
        private const double EarthMu = 3.986004418e14d;
        private const double MuNone = 0d;

        public static IEnumerable<object[]> Cases()
        {
            // name, chunk, flight, sun, sunAllowed, coast, altitude, vertical, mu, e, ported, expected
            yield return Case("R0 default context", 30d, false, false, false, false, 0d, 0d, MuNone, 0d, false,
                SpacecraftIntegrator.Dop853);
            yield return Case("R0 thrust/drag (CanCoast=false) with ported flag", 600d, false, false, false, false,
                400_000d, 0d, EarthMu, 0d, true, SpacecraftIntegrator.Dop853);

            yield return Case("R1 ported LEO 400 km, e=0, 600 s", 600d, false, false, false, true,
                400_000d, 0d, EarthMu, 0d, true, SpacecraftIntegrator.WhBm14a);
            yield return Case("R1 ported e=0.049999", 600d, false, false, false, true,
                400_000d, 0d, EarthMu, 0.049999d, true, SpacecraftIntegrator.WhBm14a);
            yield return Case("R1 boundary e=0.05 is exclusive", 600d, false, false, false, true,
                400_000d, 0d, EarthMu, 0.05d, true, SpacecraftIntegrator.Dop853);
            yield return Case("R1 e is NaN (unknown)", 600d, false, false, false, true,
                400_000d, 0d, EarthMu, double.NaN, true, SpacecraftIntegrator.Dop853);
            yield return Case("R1 CentralMu = 0 (unknown body)", 600d, false, false, false, true,
                400_000d, 0d, MuNone, 0d, true, SpacecraftIntegrator.Dop853);
            yield return Case("R1 CanCoast=false", 600d, false, false, false, false,
                400_000d, 0d, EarthMu, 0d, true, SpacecraftIntegrator.Dop853);
            yield return Case("R1 FlightModelActive=true", 600d, true, false, false, true,
                400_000d, 0d, EarthMu, 0d, true, SpacecraftIntegrator.Dop853);
            yield return Case("R1 SunDominant=true", 600d, false, true, false, true,
                400_000d, 0d, EarthMu, 0d, true, SpacecraftIntegrator.Dop853);

            yield return Case("R1 boundary altitude 200 000 m is inclusive", 600d, false, false, false, true,
                200_000d, 0d, EarthMu, 0d, true, SpacecraftIntegrator.WhBm14a);
            yield return Case("R1 below 200 000 m", 600d, false, false, false, true,
                199_999.999d, 0d, EarthMu, 0d, true, SpacecraftIntegrator.Dop853);
            yield return Case("R1 boundary altitude 2 000 000 m is inclusive", 600d, false, false, false, true,
                2_000_000d, 0d, EarthMu, 0d, true, SpacecraftIntegrator.WhBm14a);
            yield return Case("R1 above 2 000 000 m", 600d, false, false, false, true,
                2_000_000.001d, 0d, EarthMu, 0d, true, SpacecraftIntegrator.Dop853);
            yield return Case("R1 boundary chunk 600 s is inclusive", 600d, false, false, false, true,
                400_000d, 0d, EarthMu, 0d, true, SpacecraftIntegrator.WhBm14a);
            yield return Case("R1 chunk below 600 s", 599.999d, false, false, false, true,
                400_000d, 0d, EarthMu, 0d, true, SpacecraftIntegrator.Dop853);
            yield return Case("R1 ported flag off", 600d, false, false, false, true,
                400_000d, 0d, EarthMu, 0d, false, SpacecraftIntegrator.Dop853);

            yield return Case("R2 surface 0.05 s above touchdown guard", 0.05d, true, false, false, false,
                5_000d, -50d, EarthMu, 0d, false, SpacecraftIntegrator.Dopri5);
            yield return Case("R2 surface exactly at 0.05 s grain", 0.05d + 1e-12d, true, false, false, false,
                500d, 0d, EarthMu, 0d, false, SpacecraftIntegrator.Dopri5);
            yield return Case("R2 surface below 10 km boundary", 0.05d, true, false, false, false,
                9_999.999d, 0d, EarthMu, 0d, false, SpacecraftIntegrator.Dopri5);
            yield return Case("R2 boundary altitude 10 000 m is exclusive", 0.05d, true, false, false, false,
                10_000d, 0d, EarthMu, 0d, false, SpacecraftIntegrator.Dop853);
            yield return Case("R2 chunk above 0.05 s", 0.5d, true, false, false, false,
                500d, 0d, EarthMu, 0d, false, SpacecraftIntegrator.Dop853);
            yield return Case("R2 touchdown guard below 2 km", 0.05d, true, false, false, false,
                1_000d, -50d, EarthMu, 0d, false, SpacecraftIntegrator.Dop853);

            yield return Case("R3 sun coast at 1e10 m is inclusive", 30d, false, true, true, true,
                1e10d, 0d, MuNone, 0d, false, SpacecraftIntegrator.Dopri5);
            yield return Case("R3 sun coast below 1e10 m", 30d, false, true, true, true,
                1e10d - 1d, 0d, MuNone, 0d, false, SpacecraftIntegrator.Dop853);
            yield return Case("R3 sun coast allowlist off", 30d, false, true, false, true,
                1e11d, 0d, MuNone, 0d, false, SpacecraftIntegrator.Dop853);
            yield return Case("R3 sun coast with thrust/drag", 30d, false, true, true, false,
                1e11d, 0d, MuNone, 0d, false, SpacecraftIntegrator.Dop853);

            yield return Case("R4 GTO e=0.745 stays DOP853", 600d, false, false, false, true,
                400_000d, 0d, EarthMu, 0.745d, true, SpacecraftIntegrator.Dop853);
            yield return Case("R5 HEO e=0.7 stays DOP853", 600d, false, false, false, true,
                300_000d, 0d, EarthMu, 0.7d, true, SpacecraftIntegrator.Dop853);
            yield return Case("R6 GEO altitude stays DOP853", 600d, false, false, false, true,
                35_786_000d, 0d, EarthMu, 0d, true, SpacecraftIntegrator.Dop853);
            yield return Case("R7 hyperbolic flyby e=1.5 stays DOP853", 600d, false, false, false, true,
                400_000d, 0d, EarthMu, 1.5d, true, SpacecraftIntegrator.Dop853);
            yield return Case("R8 circular below the band (150 km) stays DOP853", 600d, false, false, false, true,
                150_000d, 0d, EarthMu, 0d, true, SpacecraftIntegrator.Dop853);
            yield return Case("R8 circular above the band (3 000 km) stays DOP853", 600d, false, false, false, true,
                3_000_000d, 0d, EarthMu, 0d, true, SpacecraftIntegrator.Dop853);
            yield return Case("R9 near-circular coast, J2 body: Select has no J2 input", 600d, false, false, false,
                true, 400_000d, 0d, EarthMu, 0d, false, SpacecraftIntegrator.Dop853);
            yield return Case("R10 close-encounter context stays DOP853", 600d, false, false, false, true,
                1_000_000d, 0d, EarthMu, 0.9d, true, SpacecraftIntegrator.Dop853);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void SelectMatchesDecisionTable(
            string name, double chunk, bool flight, bool sun, bool sunAllowed, bool coast,
            double altitude, double vertical, double mu, double eccentricity, bool ported,
            SpacecraftIntegrator expected)
        {
            var context = new IntegratorContext(
                chunk, flight, sun, sunAllowed, coast, altitude, vertical,
                mu, eccentricity, ported);
            SpacecraftIntegrator actual = IntegratorPolicy.Select(context);
            Assert.True(expected == actual, name + ": expected " + expected + ", got " + actual);
        }

        private static object[] Case(
            string name, double chunk, bool flight, bool sun, bool sunAllowed, bool coast,
            double altitude, double vertical, double mu, double eccentricity, bool ported,
            SpacecraftIntegrator expected)
        {
            return new object[]
            {
                name, chunk, flight, sun, sunAllowed, coast,
                altitude, vertical, mu, eccentricity, ported, expected
            };
        }
    }
}
