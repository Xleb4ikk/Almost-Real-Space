using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GalilegoPhysicsTests.Support
{
    /// <summary>
    /// Deterministic step/RHS budgets measured by GALILEGO_CALIBRATE=1.
    /// They are a regression gate for step control, not a performance test.
    /// </summary>
    internal static class StepBudget
    {
        private static readonly Dictionary<(string Scenario, string Integrator, double Tolerance), (int Steps, long Calls)> Budget =
            new Dictionary<(string, string, double), (int, long)>
            {
                [("kepler-circular", "Dopri5", 1E-06d)] = (27, 170L),
                [("kepler-circular", "Dopri5", 1E-09d)] = (106, 644L),
                [("kepler-circular", "Dopri5", 1E-12d)] = (423, 2553L),
                [("kepler-circular", "Dop853", 1E-06d)] = (16, 252L),
                [("kepler-circular", "Dop853", 1E-09d)] = (48, 743L),
                [("kepler-circular", "Dop853", 1E-12d)] = (149, 2258L),
                [("kepler-e01", "Dopri5", 1E-06d)] = (27, 170L),
                [("kepler-e01", "Dopri5", 1E-09d)] = (106, 644L),
                [("kepler-e01", "Dop853", 1E-06d)] = (17, 256L),
                [("kepler-e01", "Dop853", 1E-09d)] = (48, 743L),
                [("kepler-e05", "Dopri5", 1E-06d)] = (40, 346L),
                [("kepler-e05", "Dopri5", 1E-09d)] = (129, 789L),
                [("kepler-e05", "Dopri5", 1E-12d)] = (513, 3093L),
                [("kepler-e05", "Dop853", 1E-06d)] = (27, 472L),
                [("kepler-e05", "Dop853", 1E-09d)] = (64, 994L),
                [("kepler-e05", "Dop853", 1E-12d)] = (201, 3049L),
                [("kepler-e09", "Dopri5", 1E-09d)] = (201, 1228L),
                [("kepler-e09", "Dopri5", 1E-12d)] = (800, 4822L),
                [("kepler-e09", "Dop853", 1E-06d)] = (49, 879L),
                [("kepler-e09", "Dop853", 1E-09d)] = (119, 1841L),
                [("kepler-e099", "Dopri5", 1E-09d)] = (301, 1835L),
                [("kepler-e099", "Dopri5", 1E-12d)] = (1200, 7236L),
                [("kepler-e099", "Dop853", 1E-06d)] = (76, 1394L),
                [("kepler-e099", "Dop853", 1E-09d)] = (196, 3139L),
                [("kepler-hyperbolic", "Dopri5", 1E-06d)] = (11, 74L),
                [("kepler-hyperbolic", "Dopri5", 1E-09d)] = (40, 255L),
                [("kepler-hyperbolic", "Dop853", 1E-06d)] = (10, 173L),
                [("kepler-hyperbolic", "Dop853", 1E-09d)] = (26, 413L),
                [("kepler-near-parabolic", "Dopri5", 1E-09d)] = (142, 888L),
                [("kepler-near-parabolic", "Dopri5", 1E-12d)] = (547, 3325L),
                [("kepler-near-parabolic", "Dop853", 1E-06d)] = (37, 622L),
                [("kepler-near-parabolic", "Dop853", 1E-09d)] = (96, 1507L),
                [("kepler-retrograde", "Dopri5", 1E-06d)] = (27, 170L),
                [("kepler-retrograde", "Dopri5", 1E-09d)] = (106, 644L),
                [("kepler-retrograde", "Dop853", 1E-06d)] = (16, 252L),
                [("kepler-retrograde", "Dop853", 1E-09d)] = (48, 743L),
                [("kepler-inclined", "Dopri5", 1E-06d)] = (27, 170L),
                [("kepler-inclined", "Dopri5", 1E-09d)] = (109, 669L),
                [("kepler-inclined", "Dop853", 1E-06d)] = (17, 278L),
                [("kepler-inclined", "Dop853", 1E-09d)] = (51, 788L),
                [("thrust-tsiolkovsky", "Dopri5", 1E-07d)] = (4, 39L),
                [("thrust-tsiolkovsky", "Dopri5", 1E-09d)] = (9, 76L),
                [("thrust-tsiolkovsky", "Dop853", 1E-07d)] = (1, 16L),
                [("thrust-tsiolkovsky", "Dop853", 1E-09d)] = (3, 57L),
                [("drift-harmonic", "Dopri5", 1E-06d)] = (34, 247L),
                [("drift-harmonic", "Dopri5", 1E-09d)] = (126, 771L),
                [("drift-harmonic", "Dopri5", 1E-12d)] = (503, 3033L),
                [("drift-harmonic", "Dop853", 1E-06d)] = (12, 192L),
                [("drift-harmonic", "Dop853", 1E-09d)] = (36, 563L),
                [("drift-harmonic", "Dop853", 1E-12d)] = (112, 1703L),
                [("restricted-3body", "Dopri5", 1E-06d)] = (17, 110L),
                [("restricted-3body", "Dopri5", 1E-09d)] = (66, 404L),
                [("restricted-3body", "Dop853", 1E-06d)] = (10, 162L),
                [("restricted-3body", "Dop853", 1E-09d)] = (30, 473L),
                [("restricted-flyby", "Dopri5", 1E-06d)] = (27, 170L),
                [("restricted-flyby", "Dopri5", 1E-09d)] = (62, 387L),
                [("restricted-flyby", "Dopri5", 1E-12d)] = (236, 1424L),
                [("restricted-flyby", "Dop853", 1E-06d)] = (26, 391L),
                [("restricted-flyby", "Dop853", 1E-09d)] = (46, 702L),
                [("restricted-flyby", "Dop853", 1E-12d)] = (135, 2048L),
                [("kepler-circular-100p", "Dopri5", 1E-10d)] = (16806, 100851L),
                [("kepler-circular-100p", "Dopri5", 1E-12d)] = (42214, 253299L),
                [("kepler-circular-100p", "Dop853", 1E-10d)] = (6888, 103343L),
                [("kepler-circular-100p", "Dop853", 1E-12d)] = (14841, 222638L),
                [("kepler-e05-50p", "Dopri5", 1E-10d)] = (10201, 61221L),
                [("kepler-e05-50p", "Dopri5", 1E-12d)] = (25642, 153867L),
                [("kepler-e05-50p", "Dop853", 1E-10d)] = (4620, 69323L),
                [("kepler-e05-50p", "Dop853", 1E-12d)] = (9982, 149764L),
                [("kepler-e09-10p", "Dopri5", 1E-10d)] = (3180, 19102L),
                [("kepler-e09-10p", "Dopri5", 1E-12d)] = (7998, 48010L),
                [("kepler-e09-10p", "Dop853", 1E-10d)] = (1729, 25980L),
                [("kepler-e09-10p", "Dop853", 1E-12d)] = (3737, 56100L),
                [("kepler-hyperbolic-1d", "Dopri5", 1E-09d)] = (183, 1113L),
                [("kepler-hyperbolic-1d", "Dopri5", 1E-12d)] = (359, 2169L),
                [("kepler-hyperbolic-1d", "Dop853", 1E-09d)] = (165, 2498L),
                [("kepler-hyperbolic-1d", "Dop853", 1E-12d)] = (240, 3634L),
            };

        // Production integrator call (segments == null). This is not the full
        // SpacecraftPhysics.Step path: the local-frame wrapper and its extra
        // gravity evaluations are covered by Test549, not by this Core-only suite.
        // Only DOP853 differs from the dense path: dense output adds the contd8
        // stages (k12 + three extras) per accepted step, and k12 is carried into
        // the next step as k[0]. Rejected attempts no longer pay for dense stages.
        private static readonly Dictionary<(string Scenario, string Integrator, double Tolerance), long> Production =
            new Dictionary<(string, string, double), long>
            {
                [("kepler-circular", "Dop853", 1E-06d)] = 203L,
                [("kepler-circular", "Dop853", 1E-09d)] = 598L,
                [("kepler-circular", "Dop853", 1E-12d)] = 1810L,
                [("kepler-e01", "Dop853", 1E-06d)] = 204L,
                [("kepler-e01", "Dop853", 1E-09d)] = 598L,
                [("kepler-e05", "Dop853", 1E-06d)] = 390L,
                [("kepler-e05", "Dop853", 1E-09d)] = 801L,
                [("kepler-e05", "Dop853", 1E-12d)] = 2445L,
                [("kepler-e09", "Dop853", 1E-06d)] = 731L,
                [("kepler-e09", "Dop853", 1E-09d)] = 1483L,
                [("kepler-e099", "Dop853", 1E-06d)] = 1165L,
                [("kepler-e099", "Dop853", 1E-09d)] = 2550L,
                [("kepler-hyperbolic", "Dop853", 1E-06d)] = 142L,
                [("kepler-hyperbolic", "Dop853", 1E-09d)] = 334L,
                [("kepler-near-parabolic", "Dop853", 1E-06d)] = 510L,
                [("kepler-near-parabolic", "Dop853", 1E-09d)] = 1218L,
                [("kepler-retrograde", "Dop853", 1E-06d)] = 203L,
                [("kepler-retrograde", "Dop853", 1E-09d)] = 598L,
                [("kepler-inclined", "Dop853", 1E-06d)] = 226L,
                [("kepler-inclined", "Dop853", 1E-09d)] = 634L,
                [("thrust-tsiolkovsky", "Dop853", 1E-07d)] = 12L,
                [("thrust-tsiolkovsky", "Dop853", 1E-09d)] = 47L,
                [("drift-harmonic", "Dop853", 1E-06d)] = 155L,
                [("drift-harmonic", "Dop853", 1E-09d)] = 454L,
                [("drift-harmonic", "Dop853", 1E-12d)] = 1366L,
                [("restricted-3body", "Dop853", 1E-06d)] = 131L,
                [("restricted-3body", "Dop853", 1E-09d)] = 382L,
                [("restricted-flyby", "Dop853", 1E-06d)] = 312L,
                [("restricted-flyby", "Dop853", 1E-09d)] = 563L,
                [("restricted-flyby", "Dop853", 1E-12d)] = 1642L,
                [("kepler-circular-100p", "Dop853", 1E-10d)] = 82678L,
                [("kepler-circular-100p", "Dop853", 1E-12d)] = 178114L,
                [("kepler-e05-50p", "Dop853", 1E-10d)] = 55462L,
                [("kepler-e05-50p", "Dop853", 1E-12d)] = 119817L,
                [("kepler-e09-10p", "Dop853", 1E-10d)] = 20792L,
                [("kepler-e09-10p", "Dop853", 1E-12d)] = 44888L,
                [("kepler-hyperbolic-1d", "Dop853", 1E-09d)] = 2002L,
                [("kepler-hyperbolic-1d", "Dop853", 1E-12d)] = 2913L,
            };

        public static IReadOnlyList<(string Scenario, string Integrator, double Tolerance)> Keys =>
            Budget.Keys.OrderBy(key => key.Scenario, StringComparer.Ordinal)
                .ThenBy(key => key.Integrator, StringComparer.Ordinal)
                .ThenBy(key => key.Tolerance)
                .ToList();

        public static IReadOnlyList<(string Scenario, string Integrator, double Tolerance)> ProductionKeys =>
            Production.Keys.OrderBy(key => key.Scenario, StringComparer.Ordinal)
                .ThenBy(key => key.Integrator, StringComparer.Ordinal)
                .ThenBy(key => key.Tolerance)
                .ToList();

        public static string FormatProductionEntry(string scenario, string integrator, double tolerance, long calls)
        {
            return "                [(\"" + scenario + "\", \"" + integrator + "\", " +
                   tolerance.ToString("R", CultureInfo.InvariantCulture) + "d)] = " + calls + "L,";
        }

        public static bool TryGetProduction(string scenario, string integrator, double tolerance,
            out long calls)
        {
            return Production.TryGetValue((scenario, integrator, tolerance), out calls);
        }

        public static string FormatEntry(string scenario, string integrator, double tolerance, int steps, long calls)
        {
            return "                [(\"" + scenario + "\", \"" + integrator + "\", " +
                   tolerance.ToString("R", CultureInfo.InvariantCulture) + "d)] = (" + steps + ", " + calls + "L),";
        }

        public static bool TryGet(string scenario, string integrator, double tolerance,
            out int steps, out long calls)
        {
            if (Budget.TryGetValue((scenario, integrator, tolerance), out var value))
            {
                steps = value.Steps;
                calls = value.Calls;
                return true;
            }

            steps = 0;
            calls = 0L;
            return false;
        }
    }
}
