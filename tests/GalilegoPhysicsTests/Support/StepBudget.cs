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
                [("kepler-circular", "Dopri5", 1.0E-6d)] = (27, 170L),
                [("kepler-circular", "Dopri5", 1.0E-9d)] = (106, 644L),
                [("kepler-circular", "Dopri5", 1.0E-12d)] = (423, 2553L),
                [("kepler-circular", "Dop853", 1.0E-6d)] = (16, 221L),
                [("kepler-circular", "Dop853", 1.0E-9d)] = (48, 650L),
                [("kepler-circular", "Dop853", 1.0E-12d)] = (149, 1963L),
                [("kepler-e01", "Dopri5", 1.0E-6d)] = (27, 170L),
                [("kepler-e01", "Dopri5", 1.0E-9d)] = (106, 644L),
                [("kepler-e01", "Dop853", 1.0E-6d)] = (17, 221L),
                [("kepler-e01", "Dop853", 1.0E-9d)] = (48, 650L),
                [("kepler-e05", "Dopri5", 1.0E-6d)] = (40, 346L),
                [("kepler-e05", "Dopri5", 1.0E-9d)] = (129, 789L),
                [("kepler-e05", "Dopri5", 1.0E-12d)] = (513, 3093L),
                [("kepler-e05", "Dop853", 1.0E-6d)] = (27, 429L),
                [("kepler-e05", "Dop853", 1.0E-9d)] = (64, 871L),
                [("kepler-e05", "Dop853", 1.0E-12d)] = (201, 2652L),
                [("kepler-e09", "Dopri5", 1.0E-6d)] = (68, 647L),
                [("kepler-e09", "Dopri5", 1.0E-9d)] = (201, 1228L),
                [("kepler-e09", "Dop853", 1.0E-6d)] = (49, 806L),
                [("kepler-e09", "Dop853", 1.0E-9d)] = (119, 1612L),
                [("kepler-e099", "Dopri5", 1.0E-6d)] = (95, 914L),
                [("kepler-e099", "Dopri5", 1.0E-9d)] = (301, 1835L),
                [("kepler-e099", "Dop853", 1.0E-6d)] = (76, 1287L),
                [("kepler-e099", "Dop853", 1.0E-9d)] = (196, 2782L),
                [("kepler-hyperbolic", "Dopri5", 1.0E-6d)] = (11, 74L),
                [("kepler-hyperbolic", "Dopri5", 1.0E-9d)] = (40, 255L),
                [("kepler-hyperbolic", "Dop853", 1.0E-6d)] = (10, 156L),
                [("kepler-hyperbolic", "Dop853", 1.0E-9d)] = (26, 364L),
                [("kepler-near-parabolic", "Dopri5", 1.0E-6d)] = (41, 282L),
                [("kepler-near-parabolic", "Dopri5", 1.0E-9d)] = (142, 888L),
                [("kepler-near-parabolic", "Dop853", 1.0E-6d)] = (37, 559L),
                [("kepler-near-parabolic", "Dop853", 1.0E-9d)] = (96, 1326L),
                [("kepler-retrograde", "Dopri5", 1.0E-6d)] = (27, 170L),
                [("kepler-retrograde", "Dopri5", 1.0E-9d)] = (106, 644L),
                [("kepler-retrograde", "Dop853", 1.0E-6d)] = (16, 221L),
                [("kepler-retrograde", "Dop853", 1.0E-9d)] = (48, 650L),
                [("kepler-inclined", "Dopri5", 1.0E-6d)] = (27, 170L),
                [("kepler-inclined", "Dopri5", 1.0E-9d)] = (109, 669L),
                [("kepler-inclined", "Dop853", 1.0E-6d)] = (17, 247L),
                [("kepler-inclined", "Dop853", 1.0E-9d)] = (51, 689L),
                [("thrust-tsiolkovsky", "Dopri5", 1.0E-7d)] = (4, 39L),
                [("thrust-tsiolkovsky", "Dopri5", 1.0E-9d)] = (9, 76L),
                [("thrust-tsiolkovsky", "Dop853", 1.0E-7d)] = (1, 13L),
                [("thrust-tsiolkovsky", "Dop853", 1.0E-9d)] = (3, 52L),
                [("drift-linear", "Dopri5", 1.0E-9d)] = (6, 37L),
                [("drift-linear", "Dop853", 1.0E-9d)] = (6, 78L),
                [("restricted-3body", "Dopri5", 1.0E-6d)] = (6, 37L),
                [("restricted-3body", "Dopri5", 1.0E-9d)] = (6, 37L),
                [("restricted-3body", "Dop853", 1.0E-6d)] = (6, 78L),
                [("restricted-3body", "Dop853", 1.0E-9d)] = (6, 78L),
                [("restricted-flyby", "Dopri5", 1.0E-6d)] = (27, 170L),
                [("restricted-flyby", "Dopri5", 1.0E-9d)] = (62, 387L),
                [("restricted-flyby", "Dopri5", 1.0E-12d)] = (236, 1424L),
                [("restricted-flyby", "Dop853", 1.0E-6d)] = (26, 338L),
                [("restricted-flyby", "Dop853", 1.0E-9d)] = (46, 611L),
                [("restricted-flyby", "Dop853", 1.0E-12d)] = (135, 1781L),
                [("kepler-circular-100p", "Dopri5", 1.0E-10d)] = (16806, 100851L),
                [("kepler-circular-100p", "Dopri5", 1.0E-12d)] = (42214, 253299L),
                [("kepler-circular-100p", "Dop853", 1.0E-10d)] = (6888, 89570L),
                [("kepler-circular-100p", "Dop853", 1.0E-12d)] = (14841, 192959L),
                [("kepler-e05-50p", "Dopri5", 1.0E-10d)] = (10201, 61221L),
                [("kepler-e05-50p", "Dopri5", 1.0E-12d)] = (25642, 153867L),
                [("kepler-e05-50p", "Dop853", 1.0E-10d)] = (4620, 60086L),
                [("kepler-e05-50p", "Dop853", 1.0E-12d)] = (9982, 129805L),
                [("kepler-e09-10p", "Dopri5", 1.0E-10d)] = (3180, 19102L),
                [("kepler-e09-10p", "Dopri5", 1.0E-12d)] = (7998, 48010L),
                [("kepler-e09-10p", "Dop853", 1.0E-10d)] = (1729, 22529L),
                [("kepler-e09-10p", "Dop853", 1.0E-12d)] = (3737, 48633L),
                [("kepler-hyperbolic-1d", "Dopri5", 1.0E-9d)] = (183, 1113L),
                [("kepler-hyperbolic-1d", "Dopri5", 1.0E-12d)] = (359, 2169L),
                [("kepler-hyperbolic-1d", "Dop853", 1.0E-9d)] = (165, 2171L),
                [("kepler-hyperbolic-1d", "Dop853", 1.0E-12d)] = (240, 3159L),
            };

        public static IReadOnlyList<(string Scenario, string Integrator, double Tolerance)> Keys =>
            Budget.Keys.OrderBy(key => key.Scenario, StringComparer.Ordinal)
                .ThenBy(key => key.Integrator, StringComparer.Ordinal)
                .ThenBy(key => key.Tolerance)
                .ToList();

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
