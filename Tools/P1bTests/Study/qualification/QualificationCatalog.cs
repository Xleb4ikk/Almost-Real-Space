using System;
using System.Collections.Generic;
using System.Globalization;
using P1bStudy.Framework;

// Test564 catalogue, tier K (analytic solutions) and T (known N-body solutions).
// One QualificationProblem per TASK.md section 6; id is stable and never renamed.
//
// Each problem carries: id, tags (TASK.md section 7), profile, builder, oracle kind/id and
// the audit fields filled by QualificationAudit (G8). The runner skips a problem whose
// oracle status is not VALID.
internal static partial class P1bTests
{
    private static readonly CultureInfo QualificationCatalogInv = CultureInfo.InvariantCulture;

    /// <summary>Two-body scale used by K1–K6: mu = 1, a = 1, period = 2*pi.</summary>
    private const double QualificationKeplerMu = 1d;

    private const double QualificationKeplerPeriod = 2d * Math.PI;

    /// <summary>A7: M100 … M0.01, in metres (the problems' error unit).</summary>
    private static readonly double[] QualificationMeterGoals =
        { 100d, 10d, 1d, 0.1d, 0.01d };

    internal sealed class QualificationProblem
    {
        public string Id = "";
        public string Title = "";
        public string[] Tags = new string[0];
        public bool FullOnly;

        /// <summary>
        /// Meter goals of TASK.md Appendix A (A7), e.g. 100/10/1/0.1/0.01. Empty: no meter
        /// goals. A1-A3 are evaluated when <see cref="AnalyticLevels"/> is true.
        /// </summary>
        public double[] MeterTargets = new double[0];

        /// <summary>False for S/P/F tiers: A1-A3 are replaced by meter goals (A7.3).</summary>
        public bool AnalyticLevels = true;

        /// <summary>Central body name for conditions.csv; empty = derive from CentralMu.</summary>
        public string CentralBody = "";

        /// <summary>Force list for conditions.csv from A8: gravity, j2, drag, thrust, third-body, 1pn.</summary>
        public string[] Forces = new string[0];

        /// <summary>
        /// B6 Q goals: factors applied to the model error of the task (0.1 = Q10, 0.01 = Q1).
        /// Empty: no Q goals.
        /// </summary>
        public double[] QTargets = new double[0];

        /// <summary>E_model(rms8) of this task's model from layer V, metres (B6).</summary>
        public double QModelRms8 = double.NaN;

        /// <summary>E_model(Moon, geocentric) of this task's model from layer V, metres (B6).</summary>
        public double QModelMoon = double.NaN;

        /// <summary>Per-problem RHS budget for one cell; 0 = the profile default (B4.3).</summary>
        public long MaxCostRhs;

        /// <summary>G8 refinement resolution for the H audit; 0 = default.</summary>
        public int AuditCoarseSteps;

        /// <summary>B7: H-tier conditions.csv columns (empty for the other tiers).</summary>
        public string EpochWindow = "";

        /// <summary>B7: force-model name of the H tier: newton, 1pn, full409, satellites.</summary>
        public string ModelName = "";

        /// <summary>B7: body count of the H tier; 0 = not reported.</summary>
        public int NBodies;

        /// <summary>G8 audit route: empty (id rules), ship (certified local DOP853), planet (dd-Taylor pair).</summary>
        public string AuditKind = "";


        /// <summary>Builds (and caches) the problem. The returned instance is owned by the catalogue.</summary>
        public Func<StudyProblem> Build;

        /// <summary>analytic | reference.</summary>
        public string OracleKind = "";

        /// <summary>Stable oracle identifier written to runs.csv/oracle_audit.md.</summary>
        public string OracleId = "";

        public string OracleNote = "";

        // G8 audit results.
        public string Status = "PENDING";
        public double Floor = double.NaN;
        public double Residual = double.NaN;
        public double Cross = double.NaN;
        public string AuditNote = "";

        public StudyProblem Problem {
            get {
                if (problem == null)
                {
                    problem = Build();
                }

                return problem;
            }
        }

        /// <summary>Optional centre-of-mass function (3-vector) for the G3 report.</summary>
        public Func<double[], double[]> ComFn;

        private StudyProblem problem;

        public double[][] Oracle;
        public double[] OracleTimes;
    }

    // ---------------------------------------------------------------------------------
    // Catalogue
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Stable problem label. No commas: StudyCalibration's pin file splits its CSV on commas
    /// without quoting, so a comma inside a label would corrupt the scenario key.
    /// </summary>
    private static string QualificationLabel(string id, string title)
    {
        return ("Test564 " + id + " " + title).Replace(',', ';');
    }

    private static List<QualificationProblem> QualificationCatalogBuild(QualificationProfile profile)
    {
        var list = new List<QualificationProblem>();
        if (profile == QualificationProfile.Production)
        {
            // Appendix C, C4: only the ship scenarios the game actually runs. No K/T/R1/R2/P/F/H:
            // their forces (planets, N-body) are not the game's production model.
            QualificationAppendAppendixShips(list, profile, true);
            QualificationAppendRl(list, profile, true);
            QualificationDescribeProblemPhysics(list);
            return list;
        }

        QualificationAppendKepler(list);
        QualificationAppendKnownNBody(list);
        QualificationAppendRl(list, profile);
        QualificationAppendAppendixA(list, profile);
        QualificationAppendAppendixPlanetsAndF(list, profile);
        QualificationAppendAppendixH(list, profile);
        QualificationDescribeProblemPhysics(list);
        return list;
    }

    /// <summary>K1-K8 only: used by the production adapter self-check (C2).</summary>
    private static List<QualificationProblem> QualificationCatalogKepler()
    {
        var list = new List<QualificationProblem>();
        QualificationAppendKepler(list);
        QualificationDescribeProblemPhysics(list);
        return list;
    }

    private static void QualificationAppendKepler(List<QualificationProblem> list)
    {
        list.Add(QualificationKeplerProblem(
            "K1", "Kepler, e=0", 0d,
            new[] { "kepler", "ecc-low", "central-dominant" }));
        list.Add(QualificationKeplerProblem(
            "K2", "Kepler, e=0.1", 0.1d,
            new[] { "kepler", "ecc-low", "central-dominant" }));
        list.Add(QualificationKeplerProblem(
            "K3", "Kepler, e=0.5", 0.5d,
            new[] { "kepler", "ecc-mid", "central-dominant" }));
        list.Add(QualificationKeplerProblem(
            "K4", "Kepler, e=0.9", 0.9d,
            new[] { "kepler", "ecc-high", "central-dominant" }));
        list.Add(QualificationParabolicProblem());
        list.Add(QualificationHyperbolicProblem());
        list.Add(QualificationHarmonicProblem());
        list.Add(QualificationDampedProblem());
    }

    private static void QualificationAppendKnownNBody(List<QualificationProblem> list)
    {
        list.Add(QualificationFigureEightProblem());
        list.Add(QualificationLagrangeProblem());
        list.Add(QualificationPythagoreanProblem());
    }

    /// <summary>
    /// Force list and central body for conditions.csv (A8). Builders set these themselves
    /// where the body name is known (ships, planets); this fills the analytic K/T entries.
    /// No method is named here: it is problem metadata, not a support rule.
    /// </summary>
    private static void QualificationDescribeProblemPhysics(List<QualificationProblem> list)
    {
        foreach (QualificationProblem q in list)
        {
            if (q.Forces.Length > 0 || q.CentralBody.Length > 0)
            {
                continue;
            }

            switch (q.Id)
            {
                case "K1":
                case "K2":
                case "K3":
                case "K4":
                case "K5":
                case "K6":
                    q.CentralBody = "central field (mu=1, origin)";
                    q.Forces = new[] { "gravity" };
                    break;
                case "K7":
                    q.CentralBody = "origin (no central body)";
                    break;
                case "K8":
                    q.CentralBody = "origin (no central body)";
                    break;
                case "T1":
                case "T2":
                case "T3":
                    q.CentralBody = "barycentre (3 bodies)";
                    q.Forces = new[] { "gravity" };
                    break;
            }
        }
    }

    private static List<QualificationProblem> QualificationCatalogFor(QualificationProfile profile)
    {
        List<QualificationProblem> all = QualificationCatalogBuild(profile);
        if (profile == QualificationProfile.Full || profile == QualificationProfile.Production)
        {
            return all;
        }

        var smoke = new List<QualificationProblem>();
        foreach (QualificationProblem p in all)
        {
            if (!p.FullOnly)
            {
                smoke.Add(p);
            }
        }

        return smoke;
    }

    // ---------------------------------------------------------------------------------
    // K1–K4: elliptical Kepler, closed-form oracle
    // ---------------------------------------------------------------------------------

    private static QualificationProblem QualificationKeplerProblem(
        string id, string title, double e, string[] tags)
    {
        var q = new QualificationProblem();
        q.Id = id;
        q.Title = title;
        q.Tags = tags;
        q.OracleKind = "analytic";
        q.OracleId = "kepler-closed-form-v1";
        q.OracleNote = "closed-form Kepler equation (Newton on E)";

        const double a = 1d;
        double period = QualificationKeplerPeriod;
        double[] times = QualificationSplitHorizon(3d * period, 4);
        double[] v0 = QualificationEllipticState(QualificationKeplerMu, a, e, 0d);

        q.OracleTimes = times;
        q.Oracle = new double[times.Length][];
        for (int k = 0; k < times.Length; k++)
        {
            q.Oracle[k] = QualificationEllipticState(QualificationKeplerMu, a, e, times[k]);
        }

        double[] y0 = v0; // state at t = 0 (periapsis)
        q.Build = delegate
        {
            var p = new StudyProblem();
            p.Label = QualificationLabel(id, title);
            p.Dim = 6;
            p.PosDim = 3;
            p.ConservativeSecondOrder = true;
            p.CentralMu = QualificationKeplerMu;
            p.T0 = 0d;
            p.Y0 = y0;
            p.Rhs = QualificationKeplerRhs(QualificationKeplerMu);
            p.CheckpointTimes = (double[])times.Clone();
            p.CheckpointRef = QualificationCloneStates(q.Oracle);
            p.CharTime = period;
            p.ScaleLength = a;
            p.Certified = true;
            p.Floor = double.NaN; // set by the audit
            double mu = QualificationKeplerMu;
            p.EnergyFn = delegate (double[] s)
            {
                double r = Math.Sqrt((s[0] * s[0]) + (s[1] * s[1]) + (s[2] * s[2]));
                double v2 = (s[3] * s[3]) + (s[4] * s[4]) + (s[5] * s[5]);
                return (0.5d * v2) - (mu / r);
            };
            p.AngularMomentumFn = QualificationAngularMomentum;
            return p;
        };

        return q;
    }

    private static OdeRhs QualificationKeplerRhs(double mu)
    {
        return delegate (double t, double[] y, double[] dy)
        {
            double r2 = (y[0] * y[0]) + (y[1] * y[1]) + (y[2] * y[2]);
            double r3 = r2 * Math.Sqrt(r2);
            dy[0] = y[3];
            dy[1] = y[4];
            dy[2] = y[5];
            dy[3] = -mu * y[0] / r3;
            dy[4] = -mu * y[1] / r3;
            dy[5] = -mu * y[2] / r3;
        };
    }

    private static double QualificationAngularMomentum(double[] s)
    {
        double lx = (s[1] * s[5]) - (s[2] * s[4]);
        double ly = (s[2] * s[3]) - (s[0] * s[5]);
        double lz = (s[0] * s[4]) - (s[1] * s[3]);
        return Math.Sqrt((lx * lx) + (ly * ly) + (lz * lz));
    }

    /// <summary>State at time t on an ellipse (periapsis at t = 0), mu, semi-major axis a.</summary>
    private static double[] QualificationEllipticState(double mu, double a, double e, double t)
    {
        double n = Math.Sqrt(mu / (a * a * a));
        double m = n * t;
        double ea = m;
        for (int i = 0; i < 80; i++)
        {
            double d = (ea - (e * Math.Sin(ea)) - m) / (1d - (e * Math.Cos(ea)));
            ea -= d;
            if (Math.Abs(d) < 1e-16d)
            {
                break;
            }
        }

        double r = a * (1d - (e * Math.Cos(ea)));
        double sq = Math.Sqrt(Math.Max(0d, 1d - (e * e)));
        var y = new double[6];
        y[0] = a * (Math.Cos(ea) - e);
        y[1] = a * sq * Math.Sin(ea);
        y[2] = 0d;
        y[3] = -a * a * n * Math.Sin(ea) / r;
        y[4] = a * a * n * sq * Math.Cos(ea) / r;
        y[5] = 0d;
        return y;
    }

    /// <summary>
    /// State at time t on a parabola (periapsis at t = 0), periapsis distance q.
    /// Barker's equation D + D^3/3 = sqrt(mu/(2 q^3)) t, solved by Newton.
    /// </summary>
    private static double[] QualificationParabolicState(double mu, double q, double t)
    {
        double np = Math.Sqrt(mu / (2d * q * q * q));
        double rhs = np * t;
        double d = rhs;
        for (int i = 0; i < 200; i++)
        {
            double f = d + (d * d * d / 3d) - rhs;
            double fp = 1d + (d * d);
            double step = f / fp;
            d -= step;
            if (Math.Abs(step) <= 1e-16d * Math.Max(1d, Math.Abs(d)))
            {
                break;
            }
        }

        double vscale = Math.Sqrt(mu / (2d * q));
        var y = new double[6];
        y[0] = q * (1d - (d * d));
        y[1] = 2d * q * d;
        y[2] = 0d;
        y[3] = -vscale * 2d * d / (1d + (d * d));
        y[4] = vscale * 2d / (1d + (d * d));
        y[5] = 0d;
        return y;
    }

    /// <summary>State at time t on a hyperbola (periapsis at t = 0), mu, e &gt; 1, periapsis rp.</summary>
    private static double[] QualificationHyperbolicState(double mu, double e, double rp, double t)
    {
        double a = rp / (1d - e); // negative
        double n = Math.Sqrt(mu / (-a * -a * -a));
        double m = n * t;
        double h = Math.Asinh(m / e);
        if (!double.IsFinite(h))
        {
            h = m >= 0d ? 1d : -1d;
        }

        for (int i = 0; i < 200; i++)
        {
            double f = (e * Math.Sinh(h)) - h - m;
            double fp = (e * Math.Cosh(h)) - 1d;
            double step = f / fp;
            h -= step;
            if (Math.Abs(step) <= 1e-16d * Math.Max(1d, Math.Abs(h)))
            {
                break;
            }
        }

        double sh = Math.Sinh(h);
        double ch = Math.Cosh(h);
        double r = a * (1d - (e * ch));
        double sq = Math.Sqrt((e * e) - 1d);
        var y = new double[6];
        y[0] = a * (ch - e);
        y[1] = -a * sq * sh;
        y[2] = 0d;
        y[3] = -a * a * n * sh / r;
        y[4] = a * a * n * sq * ch / r;
        y[5] = 0d;
        return y;
    }

    // ---------------------------------------------------------------------------------
    // K5–K6
    // ---------------------------------------------------------------------------------

    private static QualificationProblem QualificationParabolicProblem()
    {
        var q = new QualificationProblem();
        q.Id = "K5";
        q.Title = "Parabolic, e=1";
        q.Tags = new[] { "kepler", "escape", "central-dominant" };
        q.OracleKind = "analytic";
        q.OracleId = "kepler-universal-v1";
        q.OracleNote = "KeplerUniversal propagation (TASK.md: parabolic oracle); audited against Barker closed form";

        const double qp = 1d;
        double[] times = QualificationSplitHorizon(10d, 4);
        q.OracleTimes = times;
        q.Oracle = new double[times.Length][];
        for (int k = 0; k < times.Length; k++)
        {
            q.Oracle[k] = QualificationUniversalState(QualificationKeplerMu, qp, Math.Sqrt(2d * QualificationKeplerMu / qp), times[k]);
        }

        double[] y0 = QualificationUniversalState(QualificationKeplerMu, qp, Math.Sqrt(2d * QualificationKeplerMu / qp), 0d);
        q.Build = delegate
        {
            var p = new StudyProblem();
            p.Label = QualificationLabel("K5", q.Title);
            p.Dim = 6;
            p.PosDim = 3;
            p.ConservativeSecondOrder = true;
            p.CentralMu = QualificationKeplerMu;
            p.T0 = 0d;
            p.Y0 = y0;
            p.Rhs = QualificationKeplerRhs(QualificationKeplerMu);
            p.CheckpointTimes = (double[])times.Clone();
            p.CheckpointRef = QualificationCloneStates(q.Oracle);
            p.CharTime = QualificationKeplerPeriod;
            p.ScaleLength = qp;
            p.Certified = true;
            double mu = QualificationKeplerMu;
            p.EnergyFn = delegate (double[] s)
            {
                double r = Math.Sqrt((s[0] * s[0]) + (s[1] * s[1]) + (s[2] * s[2]));
                double v2 = (s[3] * s[3]) + (s[4] * s[4]) + (s[5] * s[5]);
                return (0.5d * v2) - (mu / r);
            };
            p.AngularMomentumFn = QualificationAngularMomentum;
            return p;
        };
        return q;
    }

    private static QualificationProblem QualificationHyperbolicProblem()
    {
        var q = new QualificationProblem();
        q.Id = "K6";
        q.Title = "Hyperbolic, e=1.5 (flyby)";
        q.Tags = new[] { "kepler", "escape", "close-encounter", "central-dominant" };
        q.OracleKind = "analytic";
        q.OracleId = "kepler-universal-v1";
        q.OracleNote = "KeplerUniversal propagation; audited against hyperbolic Kepler closed form";

        const double e = 1.5d;
        const double rp = 1d;
        double[] times = QualificationSplitHorizon(10d, 4);
        q.OracleTimes = times;
        q.Oracle = new double[times.Length][];
        for (int k = 0; k < times.Length; k++)
        {
            q.Oracle[k] = QualificationUniversalState(
                QualificationKeplerMu, rp, Math.Sqrt(QualificationKeplerMu * (1d + e) / rp), times[k]);
        }

        double[] y0 = QualificationUniversalState(
            QualificationKeplerMu, rp, Math.Sqrt(QualificationKeplerMu * (1d + e) / rp), 0d);
        q.Build = delegate
        {
            var p = new StudyProblem();
            p.Label = QualificationLabel("K6", q.Title);
            p.Dim = 6;
            p.PosDim = 3;
            p.ConservativeSecondOrder = true;
            p.CentralMu = QualificationKeplerMu;
            p.T0 = 0d;
            p.Y0 = y0;
            p.Rhs = QualificationKeplerRhs(QualificationKeplerMu);
            p.CheckpointTimes = (double[])times.Clone();
            p.CheckpointRef = QualificationCloneStates(q.Oracle);
            p.CharTime = QualificationKeplerPeriod;
            p.ScaleLength = rp;
            p.Certified = true;
            double mu = QualificationKeplerMu;
            p.EnergyFn = delegate (double[] s)
            {
                double r = Math.Sqrt((s[0] * s[0]) + (s[1] * s[1]) + (s[2] * s[2]));
                double v2 = (s[3] * s[3]) + (s[4] * s[4]) + (s[5] * s[5]);
                return (0.5d * v2) - (mu / r);
            };
            p.AngularMomentumFn = QualificationAngularMomentum;
            return p;
        };
        return q;
    }

    /// <summary>Propagate the initial periapsis state [rp,0,0,0,vp,0] with KeplerUniversal.</summary>
    private static double[] QualificationUniversalState(double mu, double rp, double vp, double t)
    {
        var y = new[] { rp, 0d, 0d, 0d, vp, 0d };
        KeplerUniversal.Propagate(mu, y, 0, 3, t);
        return y;
    }

    // ---------------------------------------------------------------------------------
    // K7–K8: oscillators
    // ---------------------------------------------------------------------------------

    private static QualificationProblem QualificationHarmonicProblem()
    {
        var q = new QualificationProblem();
        q.Id = "K7";
        q.Title = "Harmonic oscillator, omega=1";
        q.Tags = new[] { "sanity" };
        q.OracleKind = "analytic";
        q.OracleId = "harmonic-closed-form-v1";
        q.OracleNote = "x = cos t, v = -sin t (closed form)";

        double[] times = QualificationSplitHorizon(4d * QualificationKeplerPeriod, 4);
        q.OracleTimes = times;
        q.Oracle = new double[times.Length][];
        for (int k = 0; k < times.Length; k++)
        {
            q.Oracle[k] = QualificationHarmonicState(times[k]);
        }

        q.Build = delegate
        {
            var p = new StudyProblem();
            p.Label = QualificationLabel("K7", q.Title);
            p.Dim = 6;
            p.PosDim = 3;
            p.ConservativeSecondOrder = true;
            p.CentralMu = 0d;
            p.T0 = 0d;
            p.Y0 = new[] { 1d, 0d, 0d, 0d, 1d, 0d };
            p.Rhs = delegate (double t, double[] y, double[] dy)
            {
                dy[0] = y[3];
                dy[1] = y[4];
                dy[2] = y[5];
                dy[3] = -y[0];
                dy[4] = -y[1];
                dy[5] = -y[2];
            };
            p.CheckpointTimes = (double[])times.Clone();
            p.CheckpointRef = QualificationCloneStates(q.Oracle);
            p.CharTime = QualificationKeplerPeriod;
            p.ScaleLength = 1d;
            p.Certified = true;
            p.EnergyFn = delegate (double[] s)
            {
                double x2 = (s[0] * s[0]) + (s[1] * s[1]) + (s[2] * s[2]);
                double v2 = (s[3] * s[3]) + (s[4] * s[4]) + (s[5] * s[5]);
                return 0.5d * (v2 + x2);
            };
            p.AngularMomentumFn = QualificationAngularMomentum;
            return p;
        };
        return q;
    }

    private static double[] QualificationHarmonicState(double t)
    {
        return new[]
        {
            Math.Cos(t), Math.Sin(t), 0d,
            -Math.Sin(t), Math.Cos(t), 0d
        };
    }

    private const double QualificationDampingGamma = 0.1d;

    private static QualificationProblem QualificationDampedProblem()
    {
        var q = new QualificationProblem();
        q.Id = "K8";
        q.Title = "Damped oscillator (velocity-dependent), gamma=0.1";
        q.Tags = new[] { "velocity-dependent", "sanity" };
        q.OracleKind = "analytic";
        q.OracleId = "damped-closed-form-v1";
        q.OracleNote = "underdamped closed form, x'' = -x - gamma x'";

        double[] times = QualificationSplitHorizon(4d * QualificationKeplerPeriod, 4);
        q.OracleTimes = times;
        q.Oracle = new double[times.Length][];
        for (int k = 0; k < times.Length; k++)
        {
            q.Oracle[k] = QualificationDampedState(times[k]);
        }

        q.Build = delegate
        {
            var p = new StudyProblem();
            p.Label = QualificationLabel("K8", q.Title);
            p.Dim = 6;
            p.PosDim = 3;
            p.ConservativeSecondOrder = false; // velocity-dependent force
            p.CentralMu = 0d;
            p.T0 = 0d;
            p.Y0 = new[] { 1d, 0d, 0d, 0d, 0d, 0d };
            p.Rhs = delegate (double t, double[] y, double[] dy)
            {
                double g = QualificationDampingGamma;
                dy[0] = y[3];
                dy[1] = y[4];
                dy[2] = y[5];
                dy[3] = -y[0] - (g * y[3]);
                dy[4] = -y[1] - (g * y[4]);
                dy[5] = -y[2] - (g * y[5]);
            };
            p.CheckpointTimes = (double[])times.Clone();
            p.CheckpointRef = QualificationCloneStates(q.Oracle);
            p.CharTime = QualificationKeplerPeriod;
            p.ScaleLength = 1d;
            p.Certified = true;
            // The damped oscillator has no conserved energy and its angular momentum decays:
            // no EnergyFn / AngularMomentumFn (G3 reports, and gates only on conservative tasks).
            return p;
        };
        return q;
    }

    private static double[] QualificationDampedState(double t)
    {
        double gamma = QualificationDampingGamma;
        double a = 0.5d * gamma;
        double w = Math.Sqrt(1d - (a * a));
        double x0 = 1d;
        double v0 = 0d;
        double c = x0;
        double d = (v0 + (a * x0)) / w;
        double e = Math.Exp(-a * t);
        double cs = Math.Cos(w * t);
        double sn = Math.Sin(w * t);
        double x = e * ((c * cs) + (d * sn));
        double v = e * (((-a * c) + (w * d)) * cs + (((-a * d) - (w * c)) * sn));
        return new[] { x, 0d, 0d, v, 0d, 0d };
    }

    // ---------------------------------------------------------------------------------
    // T1: figure-eight
    // ---------------------------------------------------------------------------------

    // Chenciner–Montgomery / Simó values, refined as in Study/scenarios/KnownProblems.cs:142.
    // Source of the 8-digit ICs: Chenciner & Montgomery 2000; the extra digits and the period
    // are the Newtonian refinement already used by Test505.
    private const double QualificationFigureEightX = 0.97000436d;
    private const double QualificationFigureEightY = -0.24308756818428653234d;
    private const double QualificationFigureEightVx = 0.46620369792022003799d;
    private const double QualificationFigureEightVy = 0.43236571164315760836d;
    private const double QualificationFigureEightPeriod = 6.32591409785902117500d;

    private static QualificationProblem QualificationFigureEightProblem()
    {
        var q = new QualificationProblem();
        q.Id = "T1";
        q.Title = "Figure-eight (Chenciner-Montgomery), 3 equal masses";
        q.Tags = new[] { "nbody-regular", "long-horizon" };
        q.OracleKind = "reference";
        q.OracleId = "dd-taylor-k32-v1";
        q.OracleNote = "dd-Taylor (order 32, tol 1e-30), cross-checked by fixed DOP853 5 levels";

        TaylorNBodyInput input = QualificationFigureEightInput();
        q.ComFn = QualificationComFn(input.Mu);
        const int periods = 10;
        double[] times = QualificationSplitHorizon(periods * QualificationFigureEightPeriod, 4);

        q.OracleTimes = times;
        q.Oracle = QualificationDdTaylorStates(input, times, 32, 1e-30d);

        q.Build = delegate
        {
            StudyProblem p = QualificationNBodyProblem(q.Id, q.Title, input, times, q.Oracle,
                QualificationFigureEightPeriod);
            p.EnergyFn = delegate (double[] s) { return QualificationNBodyEnergy(s, input.Mu); };
            return p;
        };
        return q;
    }

    private static TaylorNBodyInput QualificationFigureEightInput()
    {
        double x = QualificationFigureEightX;
        double y = QualificationFigureEightY;
        double vx = QualificationFigureEightVx;
        double vy = QualificationFigureEightVy;
        return new TaylorNBodyInput
        {
            Names = new[] { "A", "B", "C" },
            Mu = new[] { 1d, 1d, 1d },
            P0 = new[]
            {
                x, y, 0d,
                -x, -y, 0d,
                0d, 0d, 0d
            },
            V0 = new[]
            {
                vx, vy, 0d,
                vx, vy, 0d,
                -2d * vx, -2d * vy, 0d
            }
        };
    }

    // ---------------------------------------------------------------------------------
    // T2: Lagrange equilateral triangle, equal masses, rigid rotation
    // ---------------------------------------------------------------------------------

    private static QualificationProblem QualificationLagrangeProblem()
    {
        var q = new QualificationProblem();
        q.Id = "T2";
        q.Title = "Lagrange equilateral triangle, 3 equal masses";
        q.Tags = new[] { "nbody-regular" };
        q.OracleKind = "analytic";
        q.OracleId = "lagrange-rigid-rotation-v1";
        q.OracleNote = "exact rigid rotation, omega^2 = G*M_total/a^3";

        const double a = 1d;
        double totalMass = 3d;
        double omega = Math.Sqrt(totalMass / (a * a * a));
        double period = 2d * Math.PI / omega;

        var p0 = new[]
        {
            0d, 0d, 0d,
            a, 0d, 0d,
            a * 0.5d, a * Math.Sqrt(3d) * 0.5d, 0d
        };
        double cx = 0d;
        double cy = 0d;
        for (int i = 0; i < 3; i++)
        {
            cx += p0[3 * i];
            cy += p0[(3 * i) + 1];
        }

        cx /= totalMass;
        cy /= totalMass;
        var v0 = new double[9];
        for (int i = 0; i < 3; i++)
        {
            p0[3 * i] -= cx;
            p0[(3 * i) + 1] -= cy;
            v0[3 * i] = -omega * p0[(3 * i) + 1];
            v0[(3 * i) + 1] = omega * p0[3 * i];
        }

        var input = new TaylorNBodyInput
        {
            Names = new[] { "A", "B", "C" },
            Mu = new[] { 1d, 1d, 1d },
            P0 = (double[])p0.Clone(),
            V0 = (double[])v0.Clone()
        };
        q.ComFn = QualificationComFn(input.Mu);

        // Equal masses make the equilateral Lagrange solution linearly unstable (Routh:
        // (m1m2+m2m3+m3m1)/(M^2) = 1/3 > 1/27). Over ten periods any round-off grows to O(1)
        // and no method can converge; one period is the meaningful test of the exact solution.
        double[] times = QualificationSplitHorizon(1d * period, 4);
        q.OracleTimes = times;
        q.Oracle = new double[times.Length][];
        for (int k = 0; k < times.Length; k++)
        {
            q.Oracle[k] = QualificationRotatedState(input, omega * times[k]);
        }

        q.Build = delegate
        {
            var p = new StudyProblem();
            p.Label = QualificationLabel("T2", q.Title);
            p.Dim = 18;
            p.PosDim = 9;
            p.ConservativeSecondOrder = true;
            p.CentralMu = 0d;
            p.T0 = 0d;
            p.Y0 = QualificationConcat(input.P0, input.V0);
            p.Rhs = QualificationNBodyRhs(input.Mu);
            p.CheckpointTimes = (double[])times.Clone();
            p.CheckpointRef = QualificationCloneStates(q.Oracle);
            p.ErrorFn = QualificationPairError;
            p.CharTime = period;
            p.ScaleLength = a;
            p.Certified = true;
            return p;
        };
        return q;
    }

    private static double[] QualificationRotatedState(TaylorNBodyInput input, double theta)
    {
        double c = Math.Cos(theta);
        double s = Math.Sin(theta);
        var y = new double[18];
        for (int i = 0; i < 3; i++)
        {
            double x = input.P0[3 * i];
            double yy = input.P0[(3 * i) + 1];
            double vx = input.V0[3 * i];
            double vy = input.V0[(3 * i) + 1];
            y[3 * i] = (x * c) - (yy * s);
            y[(3 * i) + 1] = (x * s) + (yy * c);
            y[(3 * i) + 2] = 0d;
            y[9 + (3 * i)] = (vx * c) - (vy * s);
            y[9 + (3 * i) + 1] = (vx * s) + (vy * c);
            y[9 + (3 * i) + 2] = 0d;
        }

        return y;
    }

    // ---------------------------------------------------------------------------------
    // T3: Pythagorean three-body
    // ---------------------------------------------------------------------------------

    private static QualificationProblem QualificationPythagoreanProblem()
    {
        var q = new QualificationProblem();
        q.Id = "T3";
        q.Title = "Pythagorean (Burrau) three-body, masses 3/4/5";
        q.Tags = new[] { "nbody-chaotic", "close-encounter" };
        q.OracleKind = "reference";
        q.OracleId = "dd-taylor-k32-v1";
        q.OracleNote = "dd-Taylor (order 32, tol 1e-30); the fixed-step DOP853 5-level probe is reported as a chaos diagnostic";

        TaylorNBodyInput input = PythagoreanInput();
        q.ComFn = QualificationComFn(input.Mu);
        double[] times = new[] { 2d, 5d, 10d, 20d };
        q.OracleTimes = times;
        q.Oracle = QualificationDdTaylorStates(input, times, 32, 1e-30d);

        q.Build = delegate
        {
            StudyProblem p = QualificationNBodyProblem(q.Id, q.Title, input, times, q.Oracle, 1d);
            p.EnergyFn = delegate (double[] s) { return QualificationNBodyEnergy(s, input.Mu); };
            return p;
        };
        return q;
    }

    // ---------------------------------------------------------------------------------
    // N-body helpers
    // ---------------------------------------------------------------------------------

    private static StudyProblem QualificationNBodyProblem(
        string id, string title, TaylorNBodyInput input, double[] times, double[][] oracle,
        double charTime)
    {
        var p = new StudyProblem();
        p.Label = QualificationLabel(id, title);
        p.Dim = 2 * (3 * input.Names.Length);
        p.PosDim = 3 * input.Names.Length;
        p.ConservativeSecondOrder = true;
        p.CentralMu = 0d;
        p.T0 = 0d;
        p.Y0 = QualificationConcat(input.P0, input.V0);
        p.Rhs = QualificationNBodyRhs(input.Mu);
        p.CheckpointTimes = (double[])times.Clone();
        p.CheckpointRef = QualificationCloneStates(oracle);
        p.ErrorFn = QualificationPairError;
        p.CharTime = charTime;
        p.ScaleLength = 1d;
        p.Certified = true;
        return p;
    }

    private static OdeRhs QualificationNBodyRhs(double[] mu)
    {
        return delegate (double t, double[] y, double[] dy)
        {
            int n = mu.Length;
            int len = 3 * n;
            for (int i = 0; i < len; i++)
            {
                dy[i] = y[len + i];
                dy[len + i] = 0d;
            }

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double dx = y[3 * j] - y[3 * i];
                    double dyy = y[(3 * j) + 1] - y[(3 * i) + 1];
                    double dz = y[(3 * j) + 2] - y[(3 * i) + 2];
                    double r2 = (dx * dx) + (dyy * dyy) + (dz * dz);
                    double inv = 1d / (r2 * Math.Sqrt(r2));
                    double fi = mu[j] * inv;
                    double fj = mu[i] * inv;
                    dy[len + (3 * i)] += dx * fi;
                    dy[len + (3 * i) + 1] += dyy * fi;
                    dy[len + (3 * i) + 2] += dz * fi;
                    dy[len + (3 * j)] -= dx * fj;
                    dy[len + (3 * j) + 1] -= dyy * fj;
                    dy[len + (3 * j) + 2] -= dz * fj;
                }
            }
        };
    }

    /// <summary>Worst pairwise relative-position error over all body pairs (same metric as StudyBridgePlanetProblem).</summary>
    private static double QualificationPairError(double[] state, double[] reference)
    {
        int n = state.Length / 6;
        double worst = 0d;
        for (int i = 1; i < n; i++)
        {
            for (int j = 0; j < i; j++)
            {
                double dx = (state[3 * i] - state[3 * j]) - (reference[3 * i] - reference[3 * j]);
                double dy = (state[(3 * i) + 1] - state[(3 * j) + 1]) - (reference[(3 * i) + 1] - reference[(3 * j) + 1]);
                double dz = (state[(3 * i) + 2] - state[(3 * j) + 2]) - (reference[(3 * i) + 2] - reference[(3 * j) + 2]);
                double d = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                if (d > worst)
                {
                    worst = d;
                }
            }
        }

        return worst;
    }

    private static double QualificationNBodyEnergy(double[] state, double[] mu)
    {
        int n = mu.Length;
        int len = 3 * n;
        double kinetic = 0d;
        for (int i = 0; i < n; i++)
        {
            double vx = state[len + (3 * i)];
            double vy = state[len + (3 * i) + 1];
            double vz = state[len + (3 * i) + 2];
            kinetic += 0.5d * mu[i] * ((vx * vx) + (vy * vy) + (vz * vz));
        }

        double potential = 0d;
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                double dx = state[3 * i] - state[3 * j];
                double dy = state[(3 * i) + 1] - state[(3 * j) + 1];
                double dz = state[(3 * i) + 2] - state[(3 * j) + 2];
                potential -= mu[i] * mu[j] / Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            }
        }

        return kinetic + potential;
    }

    private static double[][] QualificationDdTaylorStates(
        TaylorNBodyInput input, double[] times, int order, double tolerance)
    {
        var states = new double[times.Length][];
        for (int k = 0; k < times.Length; k++)
        {
            DdNBodyResult run = RunTaylorNBodyDdOptSeconds(
                input, times[k], order, tolerance, tolerance, 0.05d, 1e-12d);
            var y = new double[2 * (3 * input.Names.Length)];
            for (int i = 0; i < 3 * input.Names.Length; i++)
            {
                y[i] = run.DdP[i].ToDouble;
                y[3 * input.Names.Length + i] = run.DdV[i].ToDouble;
            }

            states[k] = y;
        }

        return states;
    }

    private static double[] QualificationConcat(double[] p, double[] v)
    {
        var y = new double[p.Length + v.Length];
        Array.Copy(p, 0, y, 0, p.Length);
        Array.Copy(v, 0, y, p.Length, v.Length);
        return y;
    }

    /// <summary>Centre of mass of a [x, v] N-body state, weighted by the gravitational parameters.</summary>
    private static Func<double[], double[]> QualificationComFn(double[] mu)
    {
        int n = mu.Length;
        double total = 0d;
        for (int i = 0; i < n; i++)
        {
            total += mu[i];
        }

        return delegate (double[] s)
        {
            double x = 0d;
            double y = 0d;
            double z = 0d;
            for (int i = 0; i < n; i++)
            {
                x += mu[i] * s[3 * i];
                y += mu[i] * s[(3 * i) + 1];
                z += mu[i] * s[(3 * i) + 2];
            }

            return new[] { x / total, y / total, z / total };
        };
    }

    private static double[][] QualificationCloneStates(double[][] states)
    {
        var copy = new double[states.Length][];
        for (int k = 0; k < states.Length; k++)
        {
            copy[k] = (double[])states[k].Clone();
        }

        return copy;
    }

    /// <summary>Four evenly spaced checkpoints ending exactly at horizon.</summary>
    private static double[] QualificationSplitHorizon(double horizon, int count)
    {
        var times = new double[count];
        for (int k = 0; k < count; k++)
        {
            times[k] = horizon * (k + 1) / count;
        }

        return times;
    }
}
