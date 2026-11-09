using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using P1bStudy.Framework;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;

internal static partial class P1bTests
{
    private const double LowG = 9.80665d;

    /// <summary>
    /// Low-altitude / low-speed suite. The force models here are deliberately
    /// simple and analytic where possible: the point is to expose stability and
    /// event costs, not to re-run the orbital matrix.
    /// </summary>
    private static int Test555_LowAltitudeAndLanding()
    {
        Console.WriteLine("--- Integrator framework: low-altitude flight and landing ---");
        var problems = new List<StudyProblem>();
        problems.Add(ParachuteProblem("Parachute / terminal 10 m/s", 10d, 1000d));
        problems.Add(ParachuteProblem("Parachute / terminal 50 m/s", 50d, 10000d));
        problems.Add(ParachuteProblem("Parachute / terminal 200 m/s", 200d, 10000d));
        problems.Add(AscentProblem("Vertical ascent TWR 1.2", 1.2d, 300d, 1000d, 100d));
        problems.Add(SuicideBurnProblem("Suicide burn / touchdown"));
        AddHopProblems(problems);
        problems.Add(AtmosphericEntryProblem());
        // ARCHIVED: the user's "near-surface" request means pure-gravity flight,
        // not landing/atmosphere. Keep the code and report for regression, but
        // exclude every Test555 scenario from the mode map and win counts.
        foreach (StudyProblem archived in problems)
        {
            archived.Archived = true;
        }

        var options = new StudyOptions();
        options.RelTargets = new double[0];
        options.AbsTargets = new[] { 100d, 10d, 1d, 0.1d, 0.01d };
        options.KeplerWeight = 1d;
        options.AdaptiveMaxSteps = new[] { double.PositiveInfinity, 1d };
        options.RhsCostMultiplier = 1d;
        options.MaxCostRhs = 4000000L;
        options.MaxPoints = 60;

        string filter = StudyBridgeEnv("P1B_SCENARIOS");
        List<IStudyIntegrator> integrators = StudyRegistry.Select(StudyBridgeEnv("P1B_INTEGRATORS"));
        var results = new List<StudyScenarioResult>();
        foreach (StudyProblem p in problems)
        {
            if (!StudyBridgeScenarioSelected(p.Label, filter))
            {
                continue;
            }

            Console.WriteLine();
            Console.WriteLine("sweeping: " + p.Label);
            StudyScenarioResult r = StudySweepRunner.Run(p, integrators, options, Console.Out);
            results.Add(r);
            StudyReport.Print(Console.Out, r, options);
        }

        string dir = StudyShipMatrixResultsDir();
        Directory.CreateDirectory(dir);
        StudyReport.WriteCsv(Path.Combine(dir, "Test555_LowAltitude.csv"), results);
        StudyReport.WriteMarkdown(Path.Combine(dir, "Test555_LowAltitude.md"), results, options);
        StudyModeSummary.Write(Path.Combine(dir, "Test555_ModeSummary.md"), "Test555", results, options);

        int failed = 0;
        int uncertified = 0;
        foreach (StudyScenarioResult r in results)
        {
            foreach (StudyRow row in r.Rows)
            {
                if (row.Status == "error")
                {
                    failed++;
                    Console.WriteLine("FAILED RUN " + row.Scenario + " / " + row.Key
                        + " / " + row.Config + ": " + row.Note);
                }
            }

            if (!r.Problem.Certified)
            {
                uncertified++;
                Console.WriteLine("UNCERTIFIED " + r.Problem.Label + ": " + r.Problem.ReferenceNote);
            }
        }

        Check(results.Count > 0, "low-altitude sweep ran scenarios",
            results.Count.ToString(CultureInfo.InvariantCulture) + " scenario(s)");
        Check(failed == 0, "low-altitude sweep: no failed runs",
            failed.ToString(CultureInfo.InvariantCulture) + " failed run(s)");
        Check(uncertified == 0, "low-altitude sweep: every scenario certified",
            uncertified.ToString(CultureInfo.InvariantCulture) + " uncertified scenario(s)");
        Console.WriteLine("report: " + Path.Combine(dir, "Test555_LowAltitude.md"));
        return 0;
    }

    private static StudyProblem ParachuteProblem(string label, double terminalSpeed, double height)
    {
        double k = LowG / (terminalSpeed * terminalSpeed);
        double tEnd = (terminalSpeed / LowG)
            * Math.Acosh(Math.Exp(LowG * height / (terminalSpeed * terminalSpeed)));
        var p = new StudyProblem
        {
            Label = label,
            Mode = "parachute",
            ModeTags = new[] { "atmosphere", "drag", "terminal-velocity", "1d", "non-conservative", "stability" },
            Dim = 2,
            PosDim = 1,
            T0 = 0d,
            Y0 = new[] { height, 0d },
            ConservativeSecondOrder = false,
            CentralMu = 0d,
            ScaleLength = height,
            CharTime = tEnd,
            Floor = 1e-6d,
            Certified = true,
            ReferenceNote = "analytic tanh terminal-velocity solution",
            AltitudeKm = height / 1000d,
            SpeedMps = terminalSpeed,
            Eccentricity = double.NaN
        };
        p.Rhs = delegate (double t, double[] y, double[] dy)
        {
            dy[0] = y[1];
            dy[1] = -LowG - (k * y[1] * Math.Abs(y[1]));
        };
        p.ErrorFn = delegate (double[] s, double[] r) { return Math.Abs(s[0] - r[0]); };
        FillAnalyticTimes(p, tEnd, 8, delegate (double t, double[] state)
        {
            state[0] = height - ((terminalSpeed * terminalSpeed) / LowG)
                * Math.Log(Math.Cosh(LowG * t / terminalSpeed));
            state[1] = -terminalSpeed * Math.Tanh(LowG * t / terminalSpeed);
        });
        return p;
    }

    private static StudyProblem AscentProblem(
        string label, double twr, double isp, double mass0, double burnSeconds)
    {
        double thrust = twr * mass0 * LowG;
        double flow = thrust / (isp * LowG);
        double massEnd = mass0 - (flow * burnSeconds);
        double vEnd = (thrust / flow) * Math.Log(mass0 / massEnd) - (LowG * burnSeconds);
        var p = new StudyProblem
        {
            Label = label,
            Mode = "ascent",
            ModeTags = new[] { "thrust", "variable-mass", "twr-1.2", "1d", "non-conservative" },
            Dim = 3,
            PosDim = 1,
            T0 = 0d,
            Y0 = new[] { 0d, 0d, mass0 },
            ConservativeSecondOrder = false,
            CentralMu = 0d,
            ScaleLength = 20000d,
            CharTime = burnSeconds,
            Floor = 1e-6d,
            Certified = true,
            ReferenceNote = "analytic rocket equation with constant g",
            AltitudeKm = 0d,
            SpeedMps = vEnd,
            Eccentricity = double.NaN
        };
        p.Rhs = delegate (double t, double[] y, double[] dy)
        {
            double mass = Math.Max(y[2], 1e-6d);
            dy[0] = y[1];
            dy[1] = (thrust / mass) - LowG;
            dy[2] = -flow;
        };
        p.ErrorFn = delegate (double[] s, double[] r) { return Math.Abs(s[0] - r[0]); };
        FillAnalyticTimes(p, burnSeconds, 8, delegate (double t, double[] state)
        {
            double mass = mass0 - (flow * t);
            state[0] = (thrust / flow) * (t - ((mass / flow) * Math.Log(mass0 / mass)))
                - (0.5d * LowG * t * t);
            state[1] = (thrust / flow) * Math.Log(mass0 / mass) - (LowG * t);
            state[2] = mass;
        });
        return p;
    }

    private static StudyProblem SuicideBurnProblem(string label)
    {
        const double height = 100d;
        const double v0 = -50d;
        const double coast = 1d;
        const double mass = 1000d;
        double h1 = height + (v0 * coast) - (0.5d * LowG * coast * coast);
        double v1 = v0 - (LowG * coast);
        double decel = (v1 * v1) / (2d * h1);
        double thrust = mass * (decel + LowG);
        double burn = -v1 / decel;
        double tEnd = coast + burn;

        var p = new StudyProblem
        {
            Label = label,
            Mode = "suicide-burn",
            ModeTags = new[] { "landing", "thrust", "thrust-jump", "event", "has-breakpoints", "1d", "non-conservative" },
            Dim = 2,
            PosDim = 1,
            T0 = 0d,
            Y0 = new[] { height, v0 },
            ConservativeSecondOrder = false,
            CentralMu = 0d,
            ScaleLength = height,
            CharTime = tEnd,
            Floor = 1e-6d,
            Certified = true,
            ReferenceNote = "analytic two-phase coast + constant-thrust touchdown",
            AltitudeKm = height / 1000d,
            SpeedMps = Math.Abs(v0),
            Eccentricity = double.NaN,
            HasBreakpoints = true
        };
        p.Rhs = delegate (double t, double[] y, double[] dy)
        {
            dy[0] = y[1];
            dy[1] = t < coast ? -LowG : (thrust / mass) - LowG;
        };
        p.Breakpoints = new[] { coast };
        p.ErrorFn = delegate (double[] s, double[] r) { return Math.Abs(s[0] - r[0]); };
        FillAnalyticTimes(p, tEnd, 8, delegate (double t, double[] state)
        {
            if (t <= coast)
            {
                state[0] = height + (v0 * t) - (0.5d * LowG * t * t);
                state[1] = v0 - (LowG * t);
            }
            else
            {
                double tau = t - coast;
                state[0] = h1 + (v1 * tau) + (0.5d * decel * tau * tau);
                state[1] = v1 + (decel * tau);
            }
        });
        return p;
    }

    private static void AddHopProblems(List<StudyProblem> problems)
    {
        double earthRadius = 6.371e6d;
        double earthMu = 3.986004418e14d;
        problems.Add(KeplerHopProblem("Ballistic hop 100 m/s", earthMu, earthRadius,
            100d, 1d, "ballistic-hop", 0d));
        problems.Add(KeplerHopProblem("Ballistic hop 1 km/s", earthMu, earthRadius,
            1000d, 1d, "ballistic-hop", 0d));
        problems.Add(KeplerHopProblem("Ballistic hop 5 km/s", earthMu, earthRadius,
            5000d, 1d, "ballistic-hop", 0d));

        double[] altitudesKm = { 0d, 10d, 100d, 400d, 35786d, 384400d };
        foreach (double altitude in altitudesKm)
        {
            double radius = earthRadius + (altitude * 1000d);
            problems.Add(KeplerHopProblem(
                "Altitude ladder " + altitude.ToString("G", CultureInfo.InvariantCulture) + " km",
                earthMu, radius, 100d, 1d, "altitude-ladder", altitude));
        }
    }

    private static StudyProblem KeplerHopProblem(
        string label, double mu, double radius, double up, double tangential,
        string mode, double altitudeKm)
    {
        var p0 = new Vector3d(radius, 0d, 0d);
        var v0 = new Vector3d(0d, up, tangential);
        OrbitalElements elements = OrbitalElements.FromState(p0, v0, mu);
        double period = elements.OrbitalPeriodSeconds;
        if (!(period > 0d) || !double.IsFinite(period))
        {
            period = 2d * Math.PI * Math.Sqrt(radius * radius * radius / mu);
        }

        var p = new StudyProblem
        {
            Label = label,
            Mode = mode,
            ModeTags = new[] { "pure-gravity", "central", "1d-radial", "altitude" },
            Dim = 6,
            PosDim = 3,
            T0 = 0d,
            Y0 = new[] { p0.X, p0.Y, p0.Z, v0.X, v0.Y, v0.Z },
            ConservativeSecondOrder = true,
            CentralMu = mu,
            ScaleLength = radius,
            CharTime = period,
            Floor = 1e-6d,
            Certified = true,
            ReferenceNote = "analytic universal-Kepler reference",
            AltitudeKm = altitudeKm,
            SpeedMps = v0.Magnitude,
            Eccentricity = elements.Eccentricity
        };
        p.Rhs = delegate (double t, double[] y, double[] dy)
        {
            double x = y[0];
            double yy = y[1];
            double z = y[2];
            double r2 = (x * x) + (yy * yy) + (z * z);
            double r3 = r2 * Math.Sqrt(r2);
            dy[0] = y[3];
            dy[1] = y[4];
            dy[2] = y[5];
            dy[3] = -mu * x / r3;
            dy[4] = -mu * yy / r3;
            dy[5] = -mu * z / r3;
        };
        p.ErrorFn = delegate (double[] s, double[] r)
        {
            double dx = s[0] - r[0];
            double dy = s[1] - r[1];
            double dz = s[2] - r[2];
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        };
        p.EnergyFn = delegate (double[] s)
        {
            double r2 = (s[0] * s[0]) + (s[1] * s[1]) + (s[2] * s[2]);
            double v2 = (s[3] * s[3]) + (s[4] * s[4]) + (s[5] * s[5]);
            return (0.5d * v2) - (mu / Math.Sqrt(r2));
        };
        p.AngularMomentumFn = delegate (double[] s)
        {
            double lx = (s[1] * s[5]) - (s[2] * s[4]);
            double ly = (s[2] * s[3]) - (s[0] * s[5]);
            double lz = (s[0] * s[4]) - (s[1] * s[3]);
            return Math.Sqrt((lx * lx) + (ly * ly) + (lz * lz));
        };

        int count = 16;
        var times = new double[count];
        var refs = new double[count][];
        for (int i = 0; i < count; i++)
        {
            double t = period * (i + 1) / count;
            times[i] = t;
            var state = (double[])p.Y0.Clone();
            KeplerUniversal.Propagate(mu, state, 0, 3, t);
            refs[i] = state;
        }

        p.CheckpointTimes = times;
        p.CheckpointRef = refs;
        return p;
    }

    private static StudyProblem AtmosphericEntryProblem()
    {
        StarSystem sys = StudyShipMatrixSystem(false, true);
        OrbitingBody earth = sys.AllBodies[1];
        double radius = earth.Radius + 120000d;
        double speed = 7800d;
        double angle = -1d * Math.PI / 180d;
        var c = new StudyShipMatrixCase
        {
            Label = "Atmospheric entry 120 km, 7.8 km/s, -1 deg",
            Sys = sys,
            Central = 1,
            P0 = new Vector3d(radius, 0d, 0d),
            V0 = new Vector3d(speed * Math.Sin(angle), speed * Math.Cos(angle), 0d),
            Mass0 = 1000d,
            Seconds = 600d,
            Perturbed = true,
            Drag = new DragSource(earth),
            Tau = 600d,
            StepScale = 0.25d,
            ScaleLength = earth.Radius,
            CertifiedReferenceStepSeconds = 1d,
            AllowUnratioedResolution = true,
            Mode = "atmosphere-entry",
            ModeTags = new[] { "earth", "atmosphere", "entry", "drag", "high-speed", "non-conservative" },
            AltitudeKm = 120d,
            SpeedMps = speed,
            Eccentricity = double.NaN,
            HasBreakpoints = false
        };
        StudyShipMatrixRefs refs = StudyShipMatrixBuildReferences(c);
        return StudyBridgeShipProblem(c, refs);
    }

    private static void FillAnalyticTimes(
        StudyProblem p, double end, int count, Action<double, double[]> fill)
    {
        var times = new double[count];
        var refs = new double[count][];
        for (int i = 0; i < count; i++)
        {
            double t = end * (i + 1) / count;
            times[i] = t;
            var state = new double[p.Dim];
            fill(t, state);
            refs[i] = state;
        }

        p.CheckpointTimes = times;
        p.CheckpointRef = refs;
    }
}
