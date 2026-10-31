using System;
using System.Collections.Generic;
using System.Linq;
using GalilegoPhysicsTests.Support.Scenarios;

namespace GalilegoPhysicsTests.Support
{
    internal sealed class ToleranceProfile
    {
        public double OraclePositionMeters;
        public double OracleVelocityMetersPerSecond;
        public double OracleMassRelative;
        public double EnergyRelative;
        public double AngularMomentumRelative;
        public double LaplaceRungeLenzAbsolute;
        public double ReversibilityPositionMeters;
        public double ReversibilityVelocityMetersPerSecond;
        public double ChunkingPositionMeters;
        public double ChunkingVelocityMetersPerSecond;
        public double DenseInterpolationMeters;
    }

    /// <summary>
    /// Per-(scenario, integrator, tolerance) regression gates measured by
    /// GALILEGO_CALIBRATE=1. There is no class lumping and no linear scaling:
    /// every matrix row owns its gate, with ~3x headroom over the measured error.
    /// ToleranceTableConsistencyTests fails when a gate drifts outside that band.
    ///
    /// These are regression locks, not accuracy requirements. Required accuracy
    /// is gated independently against the orbit's periapsis in
    /// ScenarioExecution.MatchesPhysicalAccuracy and AbsoluteAccuracyCeilingTests.
    /// </summary>
    internal static class ToleranceTable
    {
        private static readonly Dictionary<(IntegratorKind, string, double), ToleranceProfile> Rows =
            new Dictionary<(IntegratorKind, string, double), ToleranceProfile>
            {
                [(IntegratorKind.Dop853, "drift-harmonic", 1e-12d)] =
                    Gates(2.906400e-7, 3.930000e-10, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "drift-harmonic", 1e-9d)] =
                    Gates(1.863000e-4, 3.663000e-7, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "drift-harmonic", 1e-6d)] =
                    Gates(2.148900e+0, 3.591000e-3, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "drift-harmonic", 1e-12d)] =
                    Gates(8.532000e-5, 1.895700e-7, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "drift-harmonic", 1e-9d)] =
                    Gates(9.093000e-2, 1.946400e-4, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "drift-harmonic", 1e-6d)] =
                    Gates(1.060200e+2, 1.883400e-1, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "kepler-circular", 1e-12d)] =
                    Gates(3.270000e-7, 3.909000e-10, 0d, 7.851000e-15, 1.170000e-14, 1.548000e-14, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "kepler-circular", 1e-9d)] =
                    Gates(6.906000e-5, 6.690000e-8, 0d, 2.593800e-12, 3.891000e-12, 3.489000e-12, 5.369199e-06, 5.729451e-09, 1.180244e-05, 1.165571e-08, 6.690000e-5),
                [(IntegratorKind.Dop853, "kepler-circular", 1e-6d)] =
                    Gates(6.801000e-1, 6.231000e-4, 0d, 2.531700e-8, 3.798000e-8, 3.321000e-8, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "kepler-circular", 1e-12d)] =
                    Gates(2.204100e-4, 2.322600e-7, 0d, 2.527500e-12, 3.792000e-12, 6.810000e-12, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "kepler-circular", 1e-9d)] =
                    Gates(1.168200e-1, 9.816000e-5, 0d, 2.265000e-9, 3.396000e-9, 7.380000e-9, 4.580610e-01, 4.858488e-04, 1.994936e-03, 1.302204e-06, 1.177200e-1),
                [(IntegratorKind.Dopri5, "kepler-circular", 1e-6d)] =
                    Gates(7.581000e+2, 8.091000e-1, 0d, 2.284500e-6, 3.426000e-6, 1.310700e-5, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "kepler-circular-100p", 1e-12d)] =
                    Gates(2.886900e-4, 3.111000e-7, 0d, 4.539000e-14, 6.804000e-14, 6.036000e-14, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "kepler-circular-100p", 1e-10d)] =
                    Gates(8.955000e-4, 9.657000e-7, 0d, 1.829100e-13, 2.742900e-13, 1.502100e-13, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "kepler-circular-100p", 1e-12d)] =
                    Gates(2.502300e+0, 2.697300e-3, 0d, 2.532000e-10, 3.798000e-10, 6.966000e-12, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "kepler-circular-100p", 1e-10d)] =
                    Gates(2.398800e+2, 2.585700e-1, 0d, 2.433300e-8, 3.651000e-8, 7.035000e-10, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "kepler-e01", 1e-9d)] =
                    Gates(3.609000e-4, 2.288400e-7, 0d, 3.522000e-12, 6.549000e-12, 1.292700e-11, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "kepler-e01", 1e-6d)] =
                    Gates(2.149800e+0, 1.691100e-3, 0d, 3.912000e-8, 6.825000e-8, 1.167000e-7, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "kepler-e01", 1e-9d)] =
                    Gates(2.531100e-1, 1.574700e-4, 0d, 1.891500e-9, 3.519000e-9, 8.964000e-9, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "kepler-e01", 1e-6d)] =
                    Gates(6.792000e+2, 5.490000e-1, 0d, 2.942700e-6, 4.737000e-6, 1.451100e-5, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "kepler-e05", 1e-12d)] =
                    Gates(2.909700e-7, 4.173000e-10, 0d, 4.101000e-15, 6.318000e-15, 1.059300e-14, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "kepler-e05", 1e-9d)] =
                    Gates(5.640000e-4, 8.007000e-7, 0d, 4.770000e-12, 3.036000e-11, 3.021000e-11, 1.236076e-03, 1.737795e-06, 9.500118e-05, 1.272210e-07, 5.847000e-4),
                [(IntegratorKind.Dop853, "kepler-e05", 1e-6d)] =
                    Gates(1.688700e+0, 1.997400e-3, 0d, 2.411100e-8, 8.964000e-8, 2.275500e-7, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "kepler-e05", 1e-12d)] =
                    Gates(6.816000e-4, 9.813000e-7, 0d, 2.161500e-12, 4.389000e-12, 1.020600e-11, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "kepler-e05", 1e-9d)] =
                    Gates(1.352700e+0, 1.874700e-3, 0d, 1.844700e-9, 6.183000e-9, 1.353300e-8, 1.005773e+00, 1.423379e-03, 8.861724e-03, 1.145355e-05, 1.358700e+0),
                [(IntegratorKind.Dopri5, "kepler-e05", 1e-6d)] =
                    Gates(2.520900e+3, 3.486000e+0, 0d, 2.453700e-6, 9.459000e-6, 2.817000e-5, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "kepler-e05-50p", 1e-12d)] =
                    Gates(1.062300e-3, 1.507800e-6, 0d, 4.182000e-14, 1.031700e-13, 6.360000e-14, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "kepler-e05-50p", 1e-10d)] =
                    Gates(4.470000e-2, 6.348000e-5, 0d, 1.725300e-12, 3.981000e-12, 6.567000e-12, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "kepler-e05-50p", 1e-12d)] =
                    Gates(1.459200e+0, 2.070300e-3, 0d, 5.280000e-11, 1.284600e-10, 1.402200e-10, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "kepler-e05-50p", 1e-10d)] =
                    Gates(1.287600e+2, 1.825200e-1, 0d, 4.584000e-9, 1.187700e-8, 2.993700e-8, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "kepler-e09", 1e-9d)] =
                    Gates(5.898000e-3, 8.535000e-5, 0d, 9.903000e-13, 6.840000e-12, 6.339000e-12, 9.914670e-03, 1.436051e-04, 2.569528e-05, 3.755139e-07, 5.898000e-3),
                [(IntegratorKind.Dop853, "kepler-e09", 1e-6d)] =
                    Gates(1.369800e+1, 1.982400e-1, 0d, 5.250000e-9, 3.558000e-8, 2.298000e-8, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "kepler-e09", 1e-9d)] =
                    Gates(1.631400e+1, 2.361900e-1, 0d, 1.150200e-9, 1.128600e-8, 1.564800e-8, 6.684021e+00, 9.681213e-02, 1.181436e-02, 1.716204e-04, 1.631400e+1),
                [(IntegratorKind.Dopri5, "kepler-e09", 1e-12d)] =
                    Gates(9.219000e-3, 1.335300e-4, 0d, 1.022700e-12, 8.922000e-12, 1.050300e-11, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "kepler-e09-10p", 1e-12d)] =
                    Gates(4.557000e-4, 6.603000e-6, 0d, 1.042500e-14, 2.245500e-14, 5.790000e-14, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "kepler-e09-10p", 1e-10d)] =
                    Gates(9.816000e-3, 1.422300e-4, 0d, 1.789800e-13, 6.981000e-13, 2.468100e-12, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "kepler-e09-10p", 1e-12d)] =
                    Gates(6.279000e-1, 9.096000e-3, 0d, 7.437000e-12, 2.571900e-11, 3.117000e-11, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "kepler-e09-10p", 1e-10d)] =
                    Gates(5.859000e+1, 8.484000e-1, 0d, 6.555000e-10, 2.378700e-9, 4.494000e-9, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "kepler-e099", 1e-9d)] =
                    Gates(1.727400e-1, 7.731000e-2, 0d, 5.241000e-13, 1.101900e-11, 4.734000e-12, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "kepler-e099", 1e-6d)] =
                    Gates(4.296000e+2, 1.922400e+2, 0d, 1.059000e-8, 6.084000e-8, 4.395000e-8, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "kepler-e099", 1e-9d)] =
                    Gates(4.884000e+2, 2.185800e+2, 0d, 1.030500e-9, 1.116600e-8, 7.830000e-9, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "kepler-e099", 1e-12d)] =
                    Gates(2.654100e-1, 1.187700e-1, 0d, 6.267000e-13, 1.007400e-11, 4.197000e-12, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "kepler-hyperbolic", 1e-9d)] =
                    Gates(3.528000e-5, 3.921000e-8, 0d, 1.726200e-12, 5.514000e-12, 1.672800e-11, 4.220358e-06, 1.130054e-08, 3.776604e-06, 1.171651e-09, 0d),
                [(IntegratorKind.Dop853, "kepler-hyperbolic", 1e-6d)] =
                    Gates(6.840000e-2, 7.704000e-5, 0d, 4.965000e-9, 1.334100e-8, 3.699000e-8, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "kepler-hyperbolic", 1e-9d)] =
                    Gates(3.894000e-2, 4.548000e-5, 0d, 3.489000e-9, 9.708000e-9, 2.242500e-8, 1.468972e-02, 8.430369e-06, 4.536795e-03, 1.539315e-06, 0d),
                [(IntegratorKind.Dopri5, "kepler-hyperbolic", 1e-6d)] =
                    Gates(6.798000e+1, 8.151000e-2, 0d, 3.213000e-6, 5.850000e-6, 2.081700e-5, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "kepler-hyperbolic-1d", 1e-12d)] =
                    Gates(3.615000e-6, 3.711000e-11, 0d, 2.006400e-15, 1.353900e-13, 1.592100e-13, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "kepler-hyperbolic-1d", 1e-9d)] =
                    Gates(4.992000e-4, 6.276000e-9, 0d, 3.399000e-13, 4.329000e-12, 6.162000e-12, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "kepler-hyperbolic-1d", 1e-12d)] =
                    Gates(4.872000e-4, 4.326000e-8, 0d, 2.506500e-12, 1.325400e-11, 2.264400e-11, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "kepler-hyperbolic-1d", 1e-9d)] =
                    Gates(1.303500e+0, 3.192000e-5, 0d, 1.531200e-9, 1.006800e-8, 1.579800e-8, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "kepler-inclined", 1e-9d)] =
                    Gates(3.462000e-4, 4.137000e-7, 0d, 4.122000e-12, 9.282000e-12, 1.917300e-11, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "kepler-inclined", 1e-6d)] =
                    Gates(2.416500e+0, 3.822000e-3, 0d, 9.270000e-8, 1.810500e-7, 4.293000e-7, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "kepler-inclined", 1e-9d)] =
                    Gates(3.888000e-1, 3.861000e-4, 0d, 2.012400e-9, 4.326000e-9, 1.075200e-8, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "kepler-inclined", 1e-6d)] =
                    Gates(3.033000e+2, 2.004900e-1, 0d, 5.373000e-6, 8.283000e-6, 3.522000e-5, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "kepler-near-parabolic", 1e-9d)] =
                    Gates(9.033000e-5, 1.210500e-6, 0d, 3.315000e-13, 3.057000e-12, 1.998000e-12, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "kepler-near-parabolic", 1e-6d)] =
                    Gates(2.247300e-1, 3.021000e-3, 0d, 8.388000e-10, 7.482000e-9, 5.886000e-9, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "kepler-near-parabolic", 1e-9d)] =
                    Gates(2.579400e-1, 3.492000e-3, 0d, 9.585000e-10, 9.948000e-9, 6.462000e-9, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "kepler-near-parabolic", 1e-12d)] =
                    Gates(1.381500e-4, 1.859400e-6, 0d, 6.495000e-13, 1.062000e-11, 4.452000e-12, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "kepler-retrograde", 1e-9d)] =
                    Gates(6.906000e-5, 6.690000e-8, 0d, 2.593800e-12, 3.891000e-12, 3.489000e-12, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "kepler-retrograde", 1e-6d)] =
                    Gates(6.801000e-1, 6.231000e-4, 0d, 2.531700e-8, 3.798000e-8, 3.321000e-8, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "kepler-retrograde", 1e-9d)] =
                    Gates(1.168200e-1, 9.816000e-5, 0d, 2.265000e-9, 3.396000e-9, 7.380000e-9, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "kepler-retrograde", 1e-6d)] =
                    Gates(7.581000e+2, 8.091000e-1, 0d, 2.284500e-6, 3.426000e-6, 1.310700e-5, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "restricted-3body", 1e-9d)] =
                    Gates(5.550000e-5, 4.461000e-8, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "restricted-3body", 1e-6d)] =
                    Gates(4.677000e-1, 3.753000e-4, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "restricted-3body", 1e-9d)] =
                    Gates(1.164900e-1, 8.889000e-5, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "restricted-3body", 1e-6d)] =
                    Gates(5.115000e+2, 5.013000e-1, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "restricted-flyby", 1e-12d)] =
                    Gates(1.462200e-3, 1.066800e-4, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "restricted-flyby", 1e-9d)] =
                    Gates(1.380600e-3, 1.066800e-4, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "restricted-flyby", 1e-6d)] =
                    Gates(1.605900e+0, 8.055000e-4, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "restricted-flyby", 1e-12d)] =
                    Gates(1.356900e-3, 1.066800e-4, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "restricted-flyby", 1e-9d)] =
                    Gates(1.632600e-1, 1.438500e-4, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "restricted-flyby", 1e-6d)] =
                    Gates(3.171000e+2, 2.257800e-1, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "thrust-tsiolkovsky", 1e-9d)] =
                    Gates(1.288200e-9, 8.208000e-13, 1.137000e-15, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dop853, "thrust-tsiolkovsky", 1e-7d)] =
                    Gates(3.726000e-7, 2.530500e-10, 1.000000e-15, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "thrust-tsiolkovsky", 1e-9d)] =
                    Gates(5.715000e-7, 2.316900e-9, 1.000000e-15, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d),
                [(IntegratorKind.Dopri5, "thrust-tsiolkovsky", 1e-7d)] =
                    Gates(2.951700e-5, 1.189800e-7, 1.000000e-15, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d),
            };

        public static IReadOnlyList<(IntegratorKind Integrator, string Scenario, double Tolerance)> Keys =>
            Rows.Keys
                .OrderBy(key => key.Item2, StringComparer.Ordinal)
                .ThenBy(key => key.Item1)
                .ThenBy(key => key.Item3)
                .ToList();

        public static ToleranceProfile For(IntegratorKind integrator, string scenarioId, double tolerance)
        {
            if (Rows.TryGetValue((integrator, scenarioId, tolerance), out ToleranceProfile profile))
            {
                return profile;
            }

            throw new ArgumentOutOfRangeException(nameof(tolerance),
                "No tolerance gates for " + scenarioId + " / " + integrator +
                " at tol=" + tolerance.ToString("R") + "; run GALILEGO_CALIBRATE=1 and update ToleranceTable.");
        }

        private static ToleranceProfile Gates(
            double oraclePosition, double oracleVelocity, double mass,
            double energy, double angular, double lrl,
            double reversibilityPosition, double reversibilityVelocity,
            double chunkingPosition, double chunkingVelocity, double dense)
        {
            return new ToleranceProfile
            {
                OraclePositionMeters = oraclePosition,
                OracleVelocityMetersPerSecond = oracleVelocity,
                OracleMassRelative = mass,
                EnergyRelative = energy,
                AngularMomentumRelative = angular,
                LaplaceRungeLenzAbsolute = lrl,
                ReversibilityPositionMeters = reversibilityPosition,
                ReversibilityVelocityMetersPerSecond = reversibilityVelocity,
                ChunkingPositionMeters = chunkingPosition,
                ChunkingVelocityMetersPerSecond = chunkingVelocity,
                DenseInterpolationMeters = dense
            };
        }
    }
}
