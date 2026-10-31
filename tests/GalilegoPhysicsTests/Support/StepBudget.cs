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
                [("kepler-circular", "Dop853", 1E-06d)] = (16, 272L),
                [("kepler-circular", "Dop853", 1E-09d)] = (48, 800L),
                [("kepler-circular", "Dop853", 1E-12d)] = (149, 2416L),
                [("kepler-e01", "Dopri5", 1E-06d)] = (27, 170L),
                [("kepler-e01", "Dopri5", 1E-09d)] = (106, 644L),
                [("kepler-e01", "Dop853", 1E-06d)] = (17, 272L),
                [("kepler-e01", "Dop853", 1E-09d)] = (48, 800L),
                [("kepler-e05", "Dopri5", 1E-06d)] = (40, 346L),
                [("kepler-e05", "Dopri5", 1E-09d)] = (129, 789L),
                [("kepler-e05", "Dopri5", 1E-12d)] = (513, 3093L),
                [("kepler-e05", "Dop853", 1E-06d)] = (27, 528L),
                [("kepler-e05", "Dop853", 1E-09d)] = (64, 1072L),
                [("kepler-e05", "Dop853", 1E-12d)] = (201, 3264L),
                [("kepler-e09", "Dopri5", 1E-09d)] = (201, 1228L),
                [("kepler-e09", "Dopri5", 1E-12d)] = (800, 4822L),
                [("kepler-e09", "Dop853", 1E-06d)] = (49, 992L),
                [("kepler-e09", "Dop853", 1E-09d)] = (119, 1984L),
                [("kepler-e099", "Dopri5", 1E-09d)] = (301, 1835L),
                [("kepler-e099", "Dopri5", 1E-12d)] = (1200, 7236L),
                [("kepler-e099", "Dop853", 1E-06d)] = (76, 1584L),
                [("kepler-e099", "Dop853", 1E-09d)] = (196, 3424L),
                [("kepler-hyperbolic", "Dopri5", 1E-06d)] = (11, 74L),
                [("kepler-hyperbolic", "Dopri5", 1E-09d)] = (40, 255L),
                [("kepler-hyperbolic", "Dop853", 1E-06d)] = (10, 192L),
                [("kepler-hyperbolic", "Dop853", 1E-09d)] = (26, 448L),
                [("kepler-near-parabolic", "Dopri5", 1E-09d)] = (142, 888L),
                [("kepler-near-parabolic", "Dopri5", 1E-12d)] = (547, 3325L),
                [("kepler-near-parabolic", "Dop853", 1E-06d)] = (37, 688L),
                [("kepler-near-parabolic", "Dop853", 1E-09d)] = (96, 1632L),
                [("kepler-retrograde", "Dopri5", 1E-06d)] = (27, 170L),
                [("kepler-retrograde", "Dopri5", 1E-09d)] = (106, 644L),
                [("kepler-retrograde", "Dop853", 1E-06d)] = (16, 272L),
                [("kepler-retrograde", "Dop853", 1E-09d)] = (48, 800L),
                [("kepler-inclined", "Dopri5", 1E-06d)] = (27, 170L),
                [("kepler-inclined", "Dopri5", 1E-09d)] = (109, 669L),
                [("kepler-inclined", "Dop853", 1E-06d)] = (17, 304L),
                [("kepler-inclined", "Dop853", 1E-09d)] = (51, 848L),
                [("thrust-tsiolkovsky", "Dopri5", 1E-07d)] = (4, 39L),
                [("thrust-tsiolkovsky", "Dopri5", 1E-09d)] = (9, 76L),
                [("thrust-tsiolkovsky", "Dop853", 1E-07d)] = (1, 16L),
                [("thrust-tsiolkovsky", "Dop853", 1E-09d)] = (3, 64L),
                [("drift-harmonic", "Dopri5", 1E-06d)] = (34, 247L),
                [("drift-harmonic", "Dopri5", 1E-09d)] = (126, 771L),
                [("drift-harmonic", "Dopri5", 1E-12d)] = (503, 3033L),
                [("drift-harmonic", "Dop853", 1E-06d)] = (12, 208L),
                [("drift-harmonic", "Dop853", 1E-09d)] = (36, 608L),
                [("drift-harmonic", "Dop853", 1E-12d)] = (112, 1824L),
                [("restricted-3body", "Dopri5", 1E-06d)] = (17, 110L),
                [("restricted-3body", "Dopri5", 1E-09d)] = (66, 404L),
                [("restricted-3body", "Dop853", 1E-06d)] = (10, 176L),
                [("restricted-3body", "Dop853", 1E-09d)] = (30, 512L),
                [("restricted-flyby", "Dopri5", 1E-06d)] = (27, 170L),
                [("restricted-flyby", "Dopri5", 1E-09d)] = (62, 387L),
                [("restricted-flyby", "Dopri5", 1E-12d)] = (236, 1424L),
                [("restricted-flyby", "Dop853", 1E-06d)] = (26, 416L),
                [("restricted-flyby", "Dop853", 1E-09d)] = (46, 752L),
                [("restricted-flyby", "Dop853", 1E-12d)] = (135, 2192L),
                [("kepler-circular-100p", "Dopri5", 1E-10d)] = (16806, 100851L),
                [("kepler-circular-100p", "Dopri5", 1E-12d)] = (42214, 253299L),
                [("kepler-circular-100p", "Dop853", 1E-10d)] = (6888, 110240L),
                [("kepler-circular-100p", "Dop853", 1E-12d)] = (14841, 237488L),
                [("kepler-e05-50p", "Dopri5", 1E-10d)] = (10201, 61221L),
                [("kepler-e05-50p", "Dopri5", 1E-12d)] = (25642, 153867L),
                [("kepler-e05-50p", "Dop853", 1E-10d)] = (4620, 73952L),
                [("kepler-e05-50p", "Dop853", 1E-12d)] = (9982, 159760L),
                [("kepler-e09-10p", "Dopri5", 1E-10d)] = (3180, 19102L),
                [("kepler-e09-10p", "Dopri5", 1E-12d)] = (7998, 48010L),
                [("kepler-e09-10p", "Dop853", 1E-10d)] = (1729, 27728L),
                [("kepler-e09-10p", "Dop853", 1E-12d)] = (3737, 59856L),
                [("kepler-hyperbolic-1d", "Dopri5", 1E-09d)] = (183, 1113L),
                [("kepler-hyperbolic-1d", "Dopri5", 1E-12d)] = (359, 2169L),
                [("kepler-hyperbolic-1d", "Dop853", 1E-09d)] = (165, 2672L),
                [("kepler-hyperbolic-1d", "Dop853", 1E-12d)] = (240, 3888L),
            };

        // Production path (segments == null, as in SpacecraftPhysics.Step).
        // Only DOP853 differs from the dense path: its dense output costs 3 extra
        // RHS calls per accepted step.
        private static readonly Dictionary<(string Scenario, string Integrator, double Tolerance), long> Production =
            new Dictionary<(string, string, double), long>
            {
                [("kepler-circular", "Dop853", 1E-06d)] = 204L,
                [("kepler-circular", "Dop853", 1E-09d)] = 600L,
                [("kepler-circular", "Dop853", 1E-12d)] = 1812L,
                [("kepler-e01", "Dop853", 1E-06d)] = 204L,
                [("kepler-e01", "Dop853", 1E-09d)] = 600L,
                [("kepler-e05", "Dop853", 1E-06d)] = 396L,
                [("kepler-e05", "Dop853", 1E-09d)] = 804L,
                [("kepler-e05", "Dop853", 1E-12d)] = 2448L,
                [("kepler-e09", "Dop853", 1E-06d)] = 744L,
                [("kepler-e09", "Dop853", 1E-09d)] = 1488L,
                [("kepler-e099", "Dop853", 1E-06d)] = 1188L,
                [("kepler-e099", "Dop853", 1E-09d)] = 2568L,
                [("kepler-hyperbolic", "Dop853", 1E-06d)] = 144L,
                [("kepler-hyperbolic", "Dop853", 1E-09d)] = 336L,
                [("kepler-near-parabolic", "Dop853", 1E-06d)] = 516L,
                [("kepler-near-parabolic", "Dop853", 1E-09d)] = 1224L,
                [("kepler-retrograde", "Dop853", 1E-06d)] = 204L,
                [("kepler-retrograde", "Dop853", 1E-09d)] = 600L,
                [("kepler-inclined", "Dop853", 1E-06d)] = 228L,
                [("kepler-inclined", "Dop853", 1E-09d)] = 636L,
                [("thrust-tsiolkovsky", "Dop853", 1E-07d)] = 12L,
                [("thrust-tsiolkovsky", "Dop853", 1E-09d)] = 48L,
                [("drift-harmonic", "Dop853", 1E-06d)] = 156L,
                [("drift-harmonic", "Dop853", 1E-09d)] = 456L,
                [("drift-harmonic", "Dop853", 1E-12d)] = 1368L,
                [("restricted-3body", "Dop853", 1E-06d)] = 132L,
                [("restricted-3body", "Dop853", 1E-09d)] = 384L,
                [("restricted-flyby", "Dop853", 1E-06d)] = 312L,
                [("restricted-flyby", "Dop853", 1E-09d)] = 564L,
                [("restricted-flyby", "Dop853", 1E-12d)] = 1644L,
                [("kepler-circular-100p", "Dop853", 1E-10d)] = 82680L,
                [("kepler-circular-100p", "Dop853", 1E-12d)] = 178116L,
                [("kepler-e05-50p", "Dop853", 1E-10d)] = 55464L,
                [("kepler-e05-50p", "Dop853", 1E-12d)] = 119820L,
                [("kepler-e09-10p", "Dop853", 1E-10d)] = 20796L,
                [("kepler-e09-10p", "Dop853", 1E-12d)] = 44892L,
                [("kepler-hyperbolic-1d", "Dop853", 1E-09d)] = 2004L,
                [("kepler-hyperbolic-1d", "Dop853", 1E-12d)] = 2916L,
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
