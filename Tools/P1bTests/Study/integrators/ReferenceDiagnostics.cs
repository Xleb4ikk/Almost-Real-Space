using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;

internal static partial class P1bTests
{
    private static bool StudyDiagDdSame(StudyDd a, StudyDd b)
    {
        return BitConverter.DoubleToInt64Bits(a.Hi) == BitConverter.DoubleToInt64Bits(b.Hi)
            && BitConverter.DoubleToInt64Bits(a.Lo) == BitConverter.DoubleToInt64Bits(b.Lo);
    }

    private static bool StudyDiagVecSame(Vector3d a, Vector3d b)
    {
        return BitConverter.DoubleToInt64Bits(a.X) == BitConverter.DoubleToInt64Bits(b.X)
            && BitConverter.DoubleToInt64Bits(a.Y) == BitConverter.DoubleToInt64Bits(b.Y)
            && BitConverter.DoubleToInt64Bits(a.Z) == BitConverter.DoubleToInt64Bits(b.Z);
    }

    private static bool[] StudyDiagMask(StarSystem sys, params string[] names)
    {
        var mask = new bool[sys.AllBodies.Count];
        foreach (string name in names)
        {
            bool found = false;
            for (int i = 0; i < sys.AllBodies.Count; i++)
            {
                if (sys.AllBodies[i].Name == name)
                {
                    mask[i] = true;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw new ArgumentException("body not found: " + name);
            }
        }

        return mask;
    }

    private static void StudyDiagClockDrift()
    {
        Console.WriteLine();
        Console.WriteLine("=== E0: old t += dt clock drift, 1 year ===");
        double[] ws = StudyY6Weights();
        double[] cw = StudyCumulativeWeights(ws);
        foreach (double h in new[] { 600d, 75d })
        {
            double T = 365.25d * 86400d;
            long full = (long)Math.Floor(T / h);
            double rem = T - (full * h);
            double t = 0d;
            double maxDrift = 0d;
            for (long s = 0; s <= full; s++)
            {
                double hStep = s < full ? h : rem;
                if (hStep <= 1e-9d)
                {
                    break;
                }

                double tBase = s * h;
                for (int k = 0; k < ws.Length; k++)
                {
                    t += ws[k] * hStep;
                    double expected = tBase + (cw[k + 1] * hStep);
                    maxDrift = Math.Max(maxDrift, Math.Abs(t - expected));
                }
            }

            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "h={0,6:G6} max |t - exact|={1:E3} s, x30km/s={2:E2} m",
                h, maxDrift, maxDrift * 3.0e4d));
        }
    }

    private static void StudyDiagOneBody()
    {
        Console.WriteLine();
        Console.WriteLine("=== E1: one-body Molniya Y6C-dd vs analytic Kepler ===");
        const double mu = 3.986004418e14d;
        const double earthRadius = 6.371e6d;
        double rp = earthRadius + 5e5d;
        double ra = earthRadius + 4e7d;
        double a = 0.5d * (rp + ra);
        double vp = Math.Sqrt(mu * ((2d / rp) - (1d / a)));
        var body = new OrbitingBody
        {
            Name = "Earth",
            StandardGravitationalParameter = mu,
            Radius = earthRadius
        };
        var sys = new StarSystem(body);
        var p0 = new Vector3d(rp, 0d, 0d);
        var v0 = new Vector3d(0d, vp, 0d);
        double seconds = 7d * 86400d;
        StudyKeplerDrift(p0, v0, mu, seconds, out Vector3d re, out Vector3d ve);

        double h0 = 86.1636d;
        double[] hs = { h0, h0 * 0.5d, h0 * 0.25d, h0 * 0.125d, h0 * 0.0625d };
        var dps = new double[hs.Length];
        var dvs = new double[hs.Length];
        for (int i = 0; i < hs.Length; i++)
        {
            var r = StudyShipY6CDd(sys, p0, v0, 0d, seconds / 86400d, hs[i]);
            dps[i] = (r.p - re).Magnitude;
            dvs[i] = (r.v - ve).Magnitude;
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "h={0,12:G6} dp={1:E3} m dv={2:E3} m/s calls={3:N0}",
                hs[i], dps[i], dvs[i], r.calls));
        }

        for (int i = 1; i < hs.Length; i++)
        {
            double ratio = dps[i - 1] / Math.Max(dps[i], 1e-30d);
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "dp ratio h/{0} vs h/{1}: {2:F2}",
                1 << (i - 1), 1 << i, ratio));
        }

        double midRatio = dps[1] / Math.Max(dps[2], 1e-30d);
        Check(midRatio > 32d && midRatio < 128d,
            "E1 one-body Molniya ratio",
            midRatio.ToString("F2", CultureInfo.InvariantCulture));
        Check(dps[dps.Length - 1] < 1e-3d,
            "E1 one-body Molniya error < 1 mm",
            dps[dps.Length - 1].ToString("E3", CultureInfo.InvariantCulture) + " m");
    }

    private static void StudyDiagOneBodySun()
    {
        Console.WriteLine();
        Console.WriteLine("=== E1b: one-body Sun circular Y6C-dd vs analytic ===");
        const double mu = 1.32712440018e20d;
        var body = new OrbitingBody
        {
            Name = "Sun",
            StandardGravitationalParameter = mu,
            Radius = 6.96e8d
        };
        var sys = new StarSystem(body);
        double speed = Math.Sqrt(mu / StudyAU);
        var p0 = new Vector3d(StudyAU, 0d, 0d);
        var v0 = new Vector3d(0d, speed, 0d);
        double seconds = 365.25d * 86400d;
        var exact = StudyShipMatrixAnalyticCircle(StudyAU, speed, seconds);
        sys.EvaluateBodyState(body, 0d, out Vector3d rootPos, out _);
        Console.WriteLine("root position: " + rootPos);
        var dop = StudyShipDop853Local(
            sys, 0, p0, v0, 0d, seconds / 86400d, 1e-12d, 1e-15d, 1e-14d);
        Console.WriteLine("DOP853 local dp="
            + (dop.p - exact.p).Magnitude.ToString("E3", CultureInfo.InvariantCulture) + " m");
        var exactShort = StudyShipMatrixAnalyticCircle(StudyAU, speed, 30d * 86400d);
        var shortRun = StudyShipY6CDd(sys, p0, v0, 0d, 30d, 600d);
        Console.WriteLine("30 d Y6C-dd h=600 dp="
            + (shortRun.p - exactShort.p).Magnitude.ToString("E3", CultureInfo.InvariantCulture)
            + " m (double ulp at 1 AU, not a convergence measurement)");
        Console.WriteLine("DOP853 local 30 d tolerance sweep:");
        double[] dopTols = { 1e-6d, 1e-8d, 1e-10d };
        var dopErrors = new double[dopTols.Length];
        for (int i = 0; i < dopTols.Length; i++)
        {
            double tol = dopTols[i];
            var run = StudyShipDop853Local(
                sys, 0, p0, v0, 0d, 30d,
                tol, tol * 1e-3d, tol * 1e-3d, directForce: true);
            dopErrors[i] = (run.p - exactShort.p).Magnitude;
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "  tol={0:E1} dp={1:E3} m calls={2:N0}",
                tol, dopErrors[i], run.calls));
        }

        for (int i = 0; i < dopErrors.Length; i++)
        {
            Check(dopErrors[i] < 1d,
                "E1b DOP853 30 d tol=" + dopTols[i].ToString("E1", CultureInfo.InvariantCulture),
                dopErrors[i].ToString("E3", CultureInfo.InvariantCulture) + " m < 1 m");
        }

        // At 1 AU the absolute state has ulp ~3e-5 m; 4320 steps random-walk to a
        // ~1.5e-3 m floor. This sweep is a floor diagnostic, not a proof of
        // tolerance control. Tolerance control is measured in local coordinates
        // (E6c, 6.8e6 m) and by the production tolerance test below.

        StudyDd3 f0 = StudyForceDd(sys, StudyDd3.From(p0), 0d);
        double exactAx = -mu / (StudyAU * StudyAU);
        Console.WriteLine("force X hi=" + f0.X.Hi.ToString("R", CultureInfo.InvariantCulture)
            + " lo=" + f0.X.Lo.ToString("R", CultureInfo.InvariantCulture));
        Console.WriteLine("exact Ax=" + exactAx.ToString("R", CultureInfo.InvariantCulture)
            + " rel=" + (((f0.X.Hi + f0.X.Lo) - exactAx) / exactAx).ToString("E3", CultureInfo.InvariantCulture));
        double weightSum = 0d;
        foreach (double w in StudyY6Weights())
        {
            weightSum += w;
        }

        Console.WriteLine("Y6 weight sum=" + weightSum.ToString("R", CultureInfo.InvariantCulture)
            + " delta=" + (weightSum - 1d).ToString("E3", CultureInfo.InvariantCulture));

        double[] hs = { 600d, 300d, 150d, 75d };
        var dps = new double[hs.Length];
        var runs = new Vector3d[hs.Length];
        for (int i = 0; i < hs.Length; i++)
        {
            var r = StudyShipY6CDd(sys, p0, v0, 0d, seconds / 86400d, hs[i]);
            dps[i] = (r.p - exact.p).Magnitude;
            runs[i] = r.p;
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "h={0,8:G6} dp={1:E3} m calls={2:N0}",
                hs[i], dps[i], r.calls));
        }

        Console.WriteLine("h=600 vs DOP853 dp="
            + (runs[0] - dop.p).Magnitude.ToString("E3", CultureInfo.InvariantCulture) + " m");
        Console.WriteLine("h=600 p=" + runs[0]);
        Console.WriteLine("exact p=" + exact.p);

        for (int i = 1; i < hs.Length; i++)
        {
            double ratio = dps[i - 1] / Math.Max(dps[i], 1e-30d);
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "dp ratio h/{0} vs h/{1}: {2:F2}",
                1 << (i - 1), 1 << i, ratio));
        }

        Check((dop.p - exact.p).Magnitude < 0.5d,
            "E1b DOP853 local Sun one-body",
            (dop.p - exact.p).Magnitude.ToString("E3", CultureInfo.InvariantCulture) + " m < 0.5 m");
        Check(dps[0] < 1e-5d,
            "E1b Y6C-dd Sun double floor (ulp-limited, not convergence)",
            dps[0].ToString("E3", CultureInfo.InvariantCulture) + " m < 1E-05 m");
    }

    private static void StudyDiagDeterminism()
    {
        Console.WriteLine();
        Console.WriteLine("=== E2: field determinism ===");
        StarSystem sys = StudyShipMatrixSystem(false);
        double t = StudyShipT0 + (30d * 86400d);
        var p = new Vector3d(6.771e6d, 1.0e6d, 2.0e5d);
        StudyDd3 f0 = StudyForceDd(sys, StudyDd3.From(p), t);

        var rng = new Random(12345);
        for (int i = 0; i < 2000; i++)
        {
            double tt = StudyShipT0 + (rng.NextDouble() * 30d) * 86400d;
            sys.EvaluateBodyState(sys.AllBodies[1], tt, out _, out _);
            sys.EvaluateShipAccelerationCached(p, tt);
        }

        for (int k = -100; k <= 100; k++)
        {
            double tt = t + (k * 3600d);
            sys.EvaluateBodyState(sys.AllBodies[1], tt, out _, out _);
            sys.EvaluateShipAccelerationCached(p, tt);
        }

        StudyDd3 f1 = StudyForceDd(sys, StudyDd3.From(p), t);
        bool forceSame = StudyDiagDdSame(f0.X, f1.X)
            && StudyDiagDdSame(f0.Y, f1.Y)
            && StudyDiagDdSame(f0.Z, f1.Z);
        Console.WriteLine("StudyForceDd bitwise: " + (forceSame ? "same" : "DIFFERENT"));
        if (!forceSame)
        {
            Console.WriteLine("  f0=" + f0.ToVector3d());
            Console.WriteLine("  f1=" + f1.ToVector3d());
        }

        Check(forceSame, "E2 StudyForceDd determinism",
            forceSame ? "bitwise" : "changed");

        StarSystem fresh = StudyShipMatrixSystem(false);
        Vector3d a0 = fresh.EvaluateShipAccelerationCached(p, t);
        Vector3d a1 = sys.EvaluateShipAccelerationCached(p, t);
        bool accSame = StudyDiagVecSame(a0, a1);
        Console.WriteLine("EvaluateShipAccelerationCached bitwise: "
            + (accSame ? "same" : "DIFFERENT"));
        if (!accSame)
        {
            Console.WriteLine("  a0=" + a0);
            Console.WriteLine("  a1=" + a1);
        }

        Check(accSame, "E2 cached acceleration determinism",
            accSame ? "bitwise" : "changed");
    }

    private static void StudyDiagRun(
        StudyShipMatrixCase c, string label, double h,
        bool[] mask, bool frozen)
    {
        OrbitingBody central = c.Sys.AllBodies[c.Central];
        c.Sys.EvaluateBodyState(central, StudyShipT0, out Vector3d bp, out Vector3d bv);
        var r1 = StudyShipY6CDd(
            c.Sys, bp + c.P0, bv + c.V0, StudyShipT0, c.Seconds / 86400d,
            h, mask, frozen, StudyShipT0);
        var r2 = StudyShipY6CDd(
            c.Sys, bp + c.P0, bv + c.V0, StudyShipT0, c.Seconds / 86400d,
            h * 0.5d, mask, frozen, StudyShipT0);
        var r3 = StudyShipY6CDd(
            c.Sys, bp + c.P0, bv + c.V0, StudyShipT0, c.Seconds / 86400d,
            h * 0.25d, mask, frozen, StudyShipT0);
        double d12 = (r1.p - r2.p).Magnitude;
        double d23 = (r2.p - r3.p).Magnitude;
        double ratio = d12 / Math.Max(d23, 1e-30d);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0,-38} h={1,9:G6} d12={2,11:E3} d23={3,11:E3} ratio={4,7:F2}",
            label, h, d12, d23, ratio));
    }

    private static void StudyDiagBisection()
    {
        Console.WriteLine();
        Console.WriteLine("=== E3: bisection by bodies and time ===");
        StarSystem gravity = StudyShipMatrixSystem(false);
        const double day = 86400d;
        const double year = 365.25d * 86400d;

        StudyShipMatrixCase molniya = StudyShipMatrixCaseOrbit(
            "diag Molniya", gravity, 1,
            6.371e6d + 5e5d, 6.371e6d + 4e7d,
            63.4d, 0d, 270d, 7d * day, false, true, 1d);
        double molniyaH = Math.Max(2.5d, Math.Min(600d, molniya.Tau / 1000d));
        Console.WriteLine();
        Console.WriteLine("Molniya h0=" + molniyaH.ToString("G6", CultureInfo.InvariantCulture) + " s");
        StudyDiagRun(molniya, "full, normal time", molniyaH, null, false);
        StudyDiagRun(molniya, "full, frozen time", molniyaH, null, true);
        StudyDiagRun(molniya, "Sun+Earth", molniyaH,
            StudyDiagMask(gravity, "Sun", "Earth"), false);
        StudyDiagRun(molniya, "Sun+Earth, frozen", molniyaH,
            StudyDiagMask(gravity, "Sun", "Earth"), true);
        StudyDiagRun(molniya, "Sun+Earth+Moon", molniyaH,
            StudyDiagMask(gravity, "Sun", "Earth", "Moon"), false);
        StudyDiagRun(molniya, "Sun+Earth+Moon, frozen", molniyaH,
            StudyDiagMask(gravity, "Sun", "Earth", "Moon"), true);
        StudyDiagRun(molniya, "Sun+Earth+Moon+Jupiter", molniyaH,
            StudyDiagMask(gravity, "Sun", "Earth", "Moon", "Jupiter"), false);

        StudyShipMatrixCase sun = StudyShipMatrixCaseOrbit(
            "diag Sun", gravity, 0, StudyAU, StudyAU,
            0d, 0d, 0d, year, false, true, 1d);
        double sunH = Math.Max(2.5d, Math.Min(600d, sun.Tau / 1000d));
        Console.WriteLine();
        Console.WriteLine("Sun h0=" + sunH.ToString("G6", CultureInfo.InvariantCulture) + " s");
        StudyDiagRun(sun, "full, normal time", sunH, null, false);
        StudyDiagRun(sun, "full, frozen time", sunH, null, true);
        StudyDiagRun(sun, "Sun only", sunH,
            StudyDiagMask(gravity, "Sun"), false);
        StudyDiagRun(sun, "Sun+Earth", sunH,
            StudyDiagMask(gravity, "Sun", "Earth"), false);
        StudyDiagRun(sun, "Sun+Earth, frozen", sunH,
            StudyDiagMask(gravity, "Sun", "Earth"), true);
        StudyDiagRun(sun, "Sun+Earth+Jupiter", sunH,
            StudyDiagMask(gravity, "Sun", "Earth", "Jupiter"), false);
    }

    private static void StudyDiagProductionChunks()
    {
        Console.WriteLine();
        Console.WriteLine("=== E4: production 1000 orbits vs chunk ===");
        StarSystem sys = StudyShipMatrixSystem(false);
        const double day = 86400d;
        StudyShipMatrixCase c = StudyShipMatrixCaseOrbit(
            "diag 1000 orbits", sys, 1,
            6.371e6d + 4e5d, 6.371e6d + 4e5d,
            0d, 0d, 0d, day, false, true, 1d);
        c.Seconds = 1000d * c.Tau;
        StudyShipMatrixRef local = StudyShipMatrixLocalReference(
            c, out double self, out double err, out string kind);
        Console.WriteLine("local ref: " + kind + " self="
            + self.ToString("E3", CultureInfo.InvariantCulture) + " m est="
            + err.ToString("E2", CultureInfo.InvariantCulture) + " m");
        double maxDp = 0d;
        double minDp = double.PositiveInfinity;
        foreach (double chunk in new[] { 60d, 600d, 6000d, 86400d })
        {
            var r = StudyShipMatrixWorld(c, true, chunkSeconds: chunk);
            double dp = (r.p - local.P).Magnitude;
            maxDp = Math.Max(maxDp, dp);
            minDp = Math.Min(minDp, dp);
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "chunk={0,8:G6} dp={1:E3} m calls={2:N0} ms={3:F0}",
                chunk, dp, r.calls, r.ms));
        }

        Check(err < 0.1d,
            "E4 certified reference resolution",
            err.ToString("E2", CultureInfo.InvariantCulture) + " m < 0.1 m");
        // Requirement: <= 20 m over 64 days (~0.3 m/day); the local-frame step path
        // must not regress to the old 60 m absolute-coordinate drift.
        Check(maxDp < 1d,
            "E4 production chunk accuracy",
            maxDp.ToString("E2", CultureInfo.InvariantCulture) + " m < 1 m over 1000 orbits");
        Check(maxDp + err < 1d,
            "E4 total bound (production + reference)",
            maxDp.ToString("E2", CultureInfo.InvariantCulture) + " + "
            + err.ToString("E2", CultureInfo.InvariantCulture) + " = "
            + (maxDp + err).ToString("E2", CultureInfo.InvariantCulture) + " m < 1 m");
        Check(maxDp - minDp < 1d,
            "E4 chunk invariance",
            (maxDp - minDp).ToString("E2", CultureInfo.InvariantCulture) + " m < 1 m");

        Console.WriteLine("without ToleranceFrameSystem:");
        foreach (double chunk in new[] { 60d, 600d, 6000d, 86400d })
        {
            var r = StudyShipMatrixWorld(
                c, true, chunkSeconds: chunk, useToleranceFrame: false);
            double dp = (r.p - local.P).Magnitude;
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "chunk={0,8:G6} dp={1:E3} m calls={2:N0} ms={3:F0}",
                chunk, dp, r.calls, r.ms));
        }

        Console.WriteLine("tight tolerance 1e-9/1e-12/1e-13:");
        foreach (double chunk in new[] { 60d, 600d, 86400d })
        {
            var r = StudyShipMatrixWorld(
                c, true, 1e-9d, 1e-12d, 1e-9d, 1e-13d, chunk);
            double dp = (r.p - local.P).Magnitude;
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "chunk={0,8:G6} dp={1:E3} m calls={2:N0} ms={3:F0}",
                chunk, dp, r.calls, r.ms));
        }

        Console.WriteLine("tolerance-frame boundary and single chunk:");
        foreach (double chunk in new[] { 7200d, 7201d, c.Seconds })
        {
            var r = StudyShipMatrixWorld(c, true, chunkSeconds: chunk);
            double dp = (r.p - local.P).Magnitude;
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "chunk={0,10:G6} dp={1:E3} m calls={2:N0} ms={3:F0}",
                chunk, dp, r.calls, r.ms));
        }
    }

    private static void StudyDiagFloatingOrigin()
    {
        Console.WriteLine();
        Console.WriteLine("=== E5: floating-origin local production vs world ===");
        StarSystem sys = StudyShipMatrixSystem(false);
        const double day = 86400d;
        StudyShipMatrixCase c = StudyShipMatrixCaseOrbit(
            "diag 1000 orbits", sys, 1,
            6.371e6d + 4e5d, 6.371e6d + 4e5d,
            0d, 0d, 0d, day, false, true, 1d);
        c.Seconds = 1000d * c.Tau;
        StudyShipMatrixRef local = StudyShipMatrixLocalReference(
            c, out double self, out double err, out string kind);
        var localDop = StudyShipDop853Local(
            sys, 1, c.P0, c.V0, StudyShipT0, c.Seconds / 86400d,
            1e-9d, 1e-12d, 1e-12d, directForce: true);
        double localDopDp = (localDop.p - local.P).Magnitude;
        Console.WriteLine("local DOP853 dp vs local ref = "
            + localDopDp.ToString("E3", CultureInfo.InvariantCulture) + " m");
        var world = StudyShipMatrixWorld(c, true, chunkSeconds: 600d);
        double worldDp = (world.p - local.P).Magnitude;
        Console.WriteLine("world chunk 600 dp vs local ref = "
            + worldDp.ToString("E3", CultureInfo.InvariantCulture) + " m");
        Check(err < 0.1d,
            "E5 certified reference resolution",
            err.ToString("E3", CultureInfo.InvariantCulture) + " m < 0.1 m");
        Check(localDopDp < 1d,
            "E5 local DOP853 vs certified",
            localDopDp.ToString("E2", CultureInfo.InvariantCulture) + " m < 1 m");
        Check(worldDp < 1d,
            "E5 production world vs certified",
            worldDp.ToString("E2", CultureInfo.InvariantCulture) + " m < 1 m");
        Check(worldDp + err < 1d && localDopDp + err < 1d,
            "E5 total bound (production + reference)",
            "world " + (worldDp + err).ToString("E2", CultureInfo.InvariantCulture)
            + " m, local " + (localDopDp + err).ToString("E2", CultureInfo.InvariantCulture)
            + " m < 1 m");
    }

    private static double StudyDiagTranslationRun(
        double shift, double chunk, double span, out long calls, out double ms,
        Vector3d initialOffset = default, double posTol = 1e-4d, double relativeTolerance = 1e-11d)
    {
        const double mu = 3.986004418e14d;
        const double radius = 6.371e6d;
        const double r = radius + 4e5d;
        double speed = Math.Sqrt(mu / r);
        var earth = new OrbitingBody
        {
            Name = "Earth",
            StandardGravitationalParameter = mu,
            Radius = radius,
            RootPosition = new Vector3d(shift, 0d, 0d)
        };
        var sys = new StarSystem(earth);
        var phys = new SpacecraftPhysics
        {
            ToleranceFrameSystem = sys,
            PositionAbsoluteTolerance = posTol,
            VelocityAbsoluteTolerance = posTol * 1e-4d,
            MassAbsoluteTolerance = posTol,
            RelativeTolerance = relativeTolerance
        };
        var counting = new CountingSource(new CachedGravitySource(sys));
        phys.Sources.Add(counting);
        var ship = new Spacecraft(
            new Vector3d(shift + r, 0d, 0d) + initialOffset,
            new Vector3d(0d, speed, 0d),
            1000d);
        var sw = Stopwatch.StartNew();
        var clock = new KahanAccumulator(0d);
        while (clock.Sum < span - 1e-9d)
        {
            double dt = Math.Min(chunk, span - clock.Sum);
            phys.Step(ship, clock.Sum, dt);
            clock.Add(dt);
        }

        sw.Stop();
        calls = counting.Calls;
        ms = sw.Elapsed.TotalMilliseconds;
        Vector3d rel = ship.Position - new Vector3d(shift, 0d, 0d);
        var exact = StudyShipMatrixAnalyticCircle(r, speed, span);
        return (rel - exact.p).Magnitude;
    }

    private static void StudyDiagTranslation()
    {
        Console.WriteLine();
        Console.WriteLine("=== E6: production translation, statistics (static Earth) ===");
        const double mu = 3.986004418e14d;
        const double radius = 6.371e6d;
        const double r = radius + 4e5d;
        double period = 2d * Math.PI * Math.Sqrt(r * r * r / mu);
        double span = 1000d * period;
        double[] chunks = { 600d, 86400d };
        foreach (double chunk in chunks)
        {
            var dps = new List<double>();
            for (int k = 0; k < 8; k++)
            {
                double shift = 1.5e11d + (k * 1237d);
                double dp = StudyDiagTranslationRun(
                    shift, chunk, span, out _, out _);
                dps.Add(dp);
            }

            dps.Sort();
            double median = 0.5d * (dps[3] + dps[4]);
            double max = dps[dps.Count - 1];
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "1000 orbits shift=1.5e11+k*1237 chunk={0,6:G4}: median={1:E3} max={2:E3} m",
                chunk, median, max));
            Check(max < 1d,
                "E6 max 1000 orbits chunk=" + chunk.ToString("G4", CultureInfo.InvariantCulture),
                max.ToString("E2", CultureInfo.InvariantCulture) + " m < 1 m");

            double far = StudyDiagTranslationRun(
                1.5e12d, chunk, span, out _, out _);
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "1000 orbits shift=1.5e12 chunk={0,6:G4}: dp={1:E3} m (not gated)",
                chunk, far));
        }

        // Real-time path: the game runs 1/60 s frames at warp x1. The local frame
        // must also cover chunks below MinSpanForToleranceFrameSeconds.
        Console.WriteLine("E6 real-time (1 day, shift=1.5e11):");
        foreach (double chunk in new[] { 1d, 1d / 60d })
        {
            var dps = new List<double>();
            int reps = chunk < 0.1d ? 1 : 3;
            for (int k = 0; k < reps; k++)
            {
                double shift = 1.5e11d + (k * 1237d);
                double dp = StudyDiagTranslationRun(
                    shift, chunk, 86400d, out long calls, out double ms);
                dps.Add(dp);
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  chunk={0,7:G6} shift={1:E1} dp={2:E3} m calls={3:N0} ms={4:F0}",
                    chunk, shift, dp, calls, ms));
            }

            dps.Sort();
            double max = dps[dps.Count - 1];
            // Real-time at 1x is a one-day run, not 64 days: 1/60 s means 5.18M
            // local updates and the floor is ulp(local position) per update. The
            // 64-day mission budget is measured on chunked warp steps (E4).
            Console.WriteLine("    extrapolated to 64 d (informational, not gated): "
                + (max * 64d).ToString("E3", CultureInfo.InvariantCulture) + " m");
            Check(max < 0.1d,
                "E6 real-time chunk=" + chunk.ToString("G4", CultureInfo.InvariantCulture),
                max.ToString("E3", CultureInfo.InvariantCulture) + " m/day < 0.1 m/day");
        }
    }
    private static void StudyDiagSunRootFrame()
    {
        Console.WriteLine();
        Console.WriteLine("=== E7: Sun root-frame indirect term: production vs certified local ===");
        StarSystem sys = StudyShipMatrixSystem(false);
        const double year = 365.25d * 86400d;
        StudyShipMatrixCase c = StudyShipMatrixCaseOrbit(
            "Sun 1 AU circular", sys, 0, StudyAU, StudyAU,
            0d, 0d, 0d, year, false, true, 1d);
        StudyShipCertifiedRef cert = StudyShipCertifiedReference(c);
        Console.WriteLine("certified local: " + cert.Detail);
        var world = StudyShipMatrixWorld(c, true, chunkSeconds: 600d);
        double dp = (world.p - cert.P).Magnitude;
        Console.WriteLine("production 600 s vs certified = "
            + dp.ToString("E3", CultureInfo.InvariantCulture) + " m");
        var dd = StudyShipMatrixDdReference(
            c, out double ddSelf, out double ddError, out double ddRatio, out string ddKind);
        Console.WriteLine("certified vs DD = "
            + (cert.P - dd.P).Magnitude.ToString("E3", CultureInfo.InvariantCulture)
            + " m; DD " + ddKind);
        Check(cert.Valid && cert.PositionError < 0.1d,
            "E7 certified reference resolution",
            cert.PositionError.ToString("E3", CultureInfo.InvariantCulture) + " m < 0.1 m");
        Check(dp < 1d,
            "E7 Sun root-frame production",
            dp.ToString("E2", CultureInfo.InvariantCulture) + " m < 1 m");
        Check(dp + cert.PositionError < 1d,
            "E7 total bound (production + reference)",
            dp.ToString("E2", CultureInfo.InvariantCulture) + " + "
            + cert.PositionError.ToString("E2", CultureInfo.InvariantCulture) + " < 1 m");
    }

    private static double StudyDiagLocalForceError(
        StarSystem sys, OrbitingBody central, Vector3d offset, double t,
        out string label, out double absoluteError)
    {
        sys.EvaluateBodyState(central, t, out Vector3d bp, out _);
        Vector3d shipPos = bp + offset;
        Vector3d local = sys.EvaluateShipAccelerationLocal(offset, central, t);
        StudyDd3 fShip = StudyForceDd(sys, StudyDd3.From(shipPos), t);
        StudyDd3 fBody = StudyForceDd(sys, StudyDd3.From(bp), t);
        Vector3d absoluteRel = (fShip - fBody).ToVector3d();
        label = central.Name;
        absoluteError = (local - absoluteRel).Magnitude;
        return absoluteError / Math.Max(absoluteRel.Magnitude, 1e-12);
    }

    private static void StudyDiagLocalForce()
    {
        Console.WriteLine();
        Console.WriteLine("=== E8: local force vs absolute DD (random states, J2, Moon/Jupiter/Io) ===");
        StarSystem sys = StudyShipMatrixSystem(false);
        sys.AllBodies[1].J2 = 1.08263e-3d;
        sys.AllBodies[1].J2ReferenceRadius = sys.AllBodies[1].Radius;
        sys.AllBodies[2].J2 = 2.03e-4d;
        sys.AllBodies[2].J2ReferenceRadius = sys.AllBodies[2].Radius;
        sys.AllBodies[3].J2 = 1.4736e-2d;
        sys.AllBodies[3].J2ReferenceRadius = sys.AllBodies[3].Radius;
        var rng = new Random(20261030);
        double worst = 0d;
        string worstLabel = "none";
        double worstAbsolute = 0d;
        string worstAbsoluteLabel = "none";

        OrbitingBody sun = new OrbitingBody
        {
            Name = "Sun",
            StandardGravitationalParameter = 1.32712440018e20d,
            Radius = 6.96e8d
        };
        OrbitingBody jupiter = new OrbitingBody
        {
            Name = "Jupiter",
            StandardGravitationalParameter = 1.26686534e17d,
            Radius = 7.1492e7d,
            SemiMajorAxis = 5.2d * StudyAU,
            Parent = sun
        };
        jupiter.J2 = 1.4736e-2d;
        jupiter.J2ReferenceRadius = jupiter.Radius;
        sun.Children.Add(jupiter);
        OrbitingBody io = new OrbitingBody
        {
            Name = "Io",
            StandardGravitationalParameter = 5.959916e12d,
            Radius = 1.8216e6d,
            SemiMajorAxis = 4.217e8d,
            Eccentricity = 0.0041d,
            Parent = jupiter
        };
        jupiter.Children.Add(io);
        var ioSystem = new StarSystem(sun);

        foreach (double offsetDays in new[] { 0d, 30d, 180d })
        {
            double t = StudyShipT0 + (offsetDays * 86400d);
            foreach (int index in new[] { 0, 1, 2, 3 })
            {
                OrbitingBody central = sys.AllBodies[index];
                for (int k = 0; k < 16; k++)
                {
                    Vector3d dir = new Vector3d(
                        rng.NextDouble() - 0.5d,
                        rng.NextDouble() - 0.5d,
                        rng.NextDouble() - 0.5d).Normalized;
                    double distance = central.Radius * (1.2d + (4d * rng.NextDouble()));
                    double err = StudyDiagLocalForceError(
                        sys, central, dir * distance, t,
                        out string label, out double absoluteError);
                    if (err >= worst)
                    {
                        worst = err;
                        worstLabel = label;
                    }

                    if (absoluteError >= worstAbsolute)
                    {
                        worstAbsolute = absoluteError;
                        worstAbsoluteLabel = label;
                    }
                }
            }

            for (int k = 0; k < 16; k++)
            {
                Vector3d dir = new Vector3d(
                    rng.NextDouble() - 0.5d,
                    rng.NextDouble() - 0.5d,
                    rng.NextDouble() - 0.5d).Normalized;
                double distance = io.Radius * (1.2d + (4d * rng.NextDouble()));
                double err = StudyDiagLocalForceError(
                    ioSystem, io, dir * distance, t,
                    out string label, out double absoluteError);
                if (err > worst)
                {
                    worst = err;
                    worstLabel = label;
                }

                if (absoluteError > worstAbsolute)
                {
                    worstAbsolute = absoluteError;
                    worstAbsoluteLabel = label;
                }
            }
        }

        double horizon = 2d * 86400d;
        double implied = 0.5d * worstAbsolute * horizon * horizon;
        Console.WriteLine("max relative error = " + worst.ToString("E3", CultureInfo.InvariantCulture)
            + " (" + worstLabel + "); max absolute = "
            + worstAbsolute.ToString("E3", CultureInfo.InvariantCulture) + " m/s^2 ("
            + worstAbsoluteLabel + ") -> " + implied.ToString("E3", CultureInfo.InvariantCulture)
            + " m over 2 d");
        Check(worst < 1e-10d,
            "E8 local force vs absolute DD",
            worst.ToString("E2", CultureInfo.InvariantCulture) + " < 1E-10 relative");
        Check(implied < 1d,
            "E8 absolute force -> 1 m over 2 d",
            implied.ToString("E3", CultureInfo.InvariantCulture) + " m < 1 m");
    }

    private static void StudyDiagSoiTransition()
    {
        Console.WriteLine();
        Console.WriteLine("=== E9: Earth -> Moon SOI -> Earth, dominant-body switches ===");
        // The baked N-body ephemeris is dynamically consistent with the full force,
        // so subtracting the body's Newtonian acceleration in the local frame is
        // exact. A Kepler hierarchy (analytic positions) is NOT consistent: the
        // Moon's prescribed acceleration ignores the Sun, and a Moon-centred step
        // then drifts hundreds of km against an Earth-centred reference even though
        // both integrators are correct. Do not "simplify" this fixture to analytic
        // moving bodies.
        StarSystem sys = StudyShipMatrixSystem(false);
        const double day = 86400d;
        OrbitingBody earth = sys.AllBodies[1];
        OrbitingBody moon = sys.AllBodies[2];
        sys.EvaluateBodyState(moon, StudyShipT0, out Vector3d mp, out Vector3d mv);
        sys.EvaluateBodyState(earth, StudyShipT0, out Vector3d ep, out Vector3d ev);
        Vector3d relP = (mp - ep) + new Vector3d(1.0e8d, 2.0e7d, 0d);
        Vector3d relV = (mv - ev) + new Vector3d(-1000d, 0d, 0d);
        var c = new StudyShipMatrixCase
        {
            Label = "SOI transition",
            Sys = sys,
            Central = 1,
            P0 = relP,
            V0 = relV,
            Mass0 = 1000d,
            Seconds = 2d * day,
            Tau = 80000d,
            AllowTaylor = false
        };
        StudyShipCertifiedRef cert = StudyShipCertifiedReference(c);
        Console.WriteLine("certified local: " + cert.Detail);
        var phys = new SpacecraftPhysics
        {
            ToleranceFrameSystem = sys,
            PositionAbsoluteTolerance = 1e-4d,
            VelocityAbsoluteTolerance = 1e-8d,
            MassAbsoluteTolerance = 1e-4d,
            RelativeTolerance = 1e-11d
        };
        var counting = new CountingSource(new CachedGravitySource(sys));
        phys.Sources.Add(counting);
        var ship = new Spacecraft(ep + relP, ev + relV, 1000d);
        double muEarth = earth.ResolveStandardGravitationalParameter();
        double EarthEnergy(double time)
        {
            sys.EvaluateBodyState(earth, time, out Vector3d bp, out Vector3d bv);
            Vector3d r = ship.Position - bp;
            Vector3d v = ship.Velocity - bv;
            return 0.5d * v.SqrMagnitude - (muEarth / r.Magnitude);
        }

        OrbitingBody lastBody = null;
        int switches = 0;
        var switchDe = new List<double>();
        var switchAt = new List<int>();
        var allDe = new List<double>();
        double t = StudyShipT0;
        double end = StudyShipT0 + c.Seconds;
        double before = EarthEnergy(t);
        while (t < end - 1e-9d)
        {
            double dt = Math.Min(600d, end - t);
            phys.Step(ship, t, dt);
            t += dt;
            double after = EarthEnergy(t);
            double de = Math.Abs(after - before) / Math.Max(Math.Abs(before), 1e-30d);
            int chunkIndex = allDe.Count;
            allDe.Add(de);
            before = after;
            if (ship.HasLocalState)
            {
                if (lastBody != null && !ReferenceEquals(lastBody, ship.LocalBody))
                {
                    switches++;
                    switchDe.Add(de);
                    switchAt.Add(chunkIndex);
                }

                lastBody = ship.LocalBody;
            }
        }

        sys.EvaluateBodyState(earth, end, out Vector3d epEnd, out _);
        double dp = ((ship.Position - epEnd) - cert.P).Magnitude;

        double worstSwitchRatio = 0d;
        var switchSet = new HashSet<int>(switchAt);
        for (int i = 0; i < switchDe.Count; i++)
        {
            int index = switchAt[i];
            double neighbor = 0d;
            if (index > 0)
            {
                neighbor = Math.Max(neighbor, allDe[index - 1]);
            }

            if (index + 1 < allDe.Count)
            {
                neighbor = Math.Max(neighbor, allDe[index + 1]);
            }

            if (!(neighbor > 0d))
            {
                for (int j = 0; j < allDe.Count; j++)
                {
                    if (!switchSet.Contains(j))
                    {
                        neighbor = Math.Max(neighbor, allDe[j]);
                    }
                }
            }

            worstSwitchRatio = Math.Max(worstSwitchRatio,
                switchDe[i] / Math.Max(neighbor, 1e-30d));
        }

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "switches={0}, dp={1:E3} m, worst switch dE/neighbor={2:E2}, calls={3:N0}",
            switches, dp, worstSwitchRatio, counting.Calls));
        Check(switches >= 2,
            "E9 dominant-body switches",
            switches.ToString(CultureInfo.InvariantCulture) + " >= 2");
        Check(cert.Resolved && cert.FinestDelta < 0.1d,
            "E9 resolved reference (not an error bound)",
            cert.FinestDelta.ToString("E3", CultureInfo.InvariantCulture) + " m < 0.1 m");
        Check(dp < 1d,
            "E9 SOI trajectory vs certified",
            dp.ToString("E2", CultureInfo.InvariantCulture) + " m < 1 m");
        Check(worstSwitchRatio < 10d,
            "E9 energy continuity at switch",
            worstSwitchRatio.ToString("E2", CultureInfo.InvariantCulture) + " < 10x");
    }
    private static void StudyDiagTranslationPerturbed()
    {
        Console.WriteLine();
        Console.WriteLine("=== E6b: initial-state perturbation (1e-6 m), not shift ===");
        double period = 2d * Math.PI * Math.Sqrt(
            (6.371e6d + 4e5d) * (6.371e6d + 4e5d) * (6.371e6d + 4e5d) / 3.986004418e14d);
        double span = 1000d * period;
        foreach (double chunk in new[] { 600d, 86400d })
        {
            var dps = new List<double>();
            for (int k = 0; k < 8; k++)
            {
                double angle = 2d * Math.PI * k / 8d;
                var offset = new Vector3d(
                    1e-6d * Math.Cos(angle), 1e-6d * Math.Sin(angle), 0d);
                dps.Add(StudyDiagTranslationRun(
                    1.5e11d, chunk, span, out _, out _, offset));
            }

            dps.Sort();
            double median = 0.5d * (dps[3] + dps[4]);
            double max = dps[dps.Count - 1];
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "1000 orbits chunk={0,6:G4} perturb 1e-6: median={1:E3} max={2:E3} m",
                chunk, median, max));
            Check(max < 1d,
                "E6b perturbation max chunk=" + chunk.ToString("G4", CultureInfo.InvariantCulture),
                max.ToString("E2", CultureInfo.InvariantCulture) + " m < 1 m");
        }
    }

    private static void StudyDiagTranslationTolerance()
    {
        Console.WriteLine();
        Console.WriteLine("=== E6c: production tolerance sweep on the E6 configuration ===");
        double period = 2d * Math.PI * Math.Sqrt(
            (6.371e6d + 4e5d) * (6.371e6d + 4e5d) * (6.371e6d + 4e5d) / 3.986004418e14d);
        double span = 1000d * period;
        var tolerances = new[]
        {
            (1e-2d, 1e-10d), (1e-3d, 1e-11d), (1e-4d, 1e-11d),
            (1e-5d, 1e-12d), (1e-6d, 1e-13d)
        };
        var errors = new List<double>();
        foreach (var pair in tolerances)
        {
            double dp = StudyDiagTranslationRun(
                1.5e11d, 600d, span, out _, out _, default, pair.Item1, pair.Item2);
            errors.Add(dp);
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "posTol={0:E0} rtol={1:E0} dp={2:E3} m",
                pair.Item1, pair.Item2, dp));
        }

        Check(errors[errors.Count - 1] <= errors[0] * 1.05d,
            "E6c tolerance controls error",
            errors[errors.Count - 1].ToString("E2", CultureInfo.InvariantCulture)
            + " m (tight) <= 1.05x " + errors[0].ToString("E2", CultureInfo.InvariantCulture)
            + " m (loose)");

        // The plateau is the per-chunk tolerance accumulating over all chunks, not a
        // hidden force floor: error <= N_chunks * (posTol + rtol * localRadius).
        double chunks = Math.Ceiling(span / 600d);
        double localRadius = 6.371e6d + 4e5d;
        for (int i = 0; i < tolerances.Length; i++)
        {
            double perChunk = tolerances[i].Item1 + (tolerances[i].Item2 * localRadius);
            double bound = chunks * perChunk;
            Check(errors[i] <= bound,
                "E6c tolerance budget " + i.ToString(CultureInfo.InvariantCulture),
                errors[i].ToString("E3", CultureInfo.InvariantCulture) + " m <= "
                + bound.ToString("E2", CultureInfo.InvariantCulture) + " m");
        }

        for (int i = 1; i < errors.Count; i++)
        {
            Check(errors[i] <= errors[i - 1] * 1.05d,
                "E6c monotonic " + i.ToString(CultureInfo.InvariantCulture),
                errors[i].ToString("E3", CultureInfo.InvariantCulture) + " <= 1.05x "
                + errors[i - 1].ToString("E3", CultureInfo.InvariantCulture));
        }
    }

    private static void StudyDiagKahanTime()
    {
        Console.WriteLine();
        Console.WriteLine("=== E10: Kahan clock, 86400 s in 1/60 s steps ===");
        const int steps = 86400 * 60;
        const double dt = 1d / 60d;
        var kahan = new KahanAccumulator(0d);
        double naive = 0d;
        for (int i = 0; i < steps; i++)
        {
            kahan.Add(dt);
            naive += dt;
        }

        double kahanError = Math.Abs(kahan.Sum - 86400d);
        double naiveError = Math.Abs(naive - 86400d);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "kahan |t-86400|={0:E3} s, naive |t-86400|={1:E3} s",
            kahanError, naiveError));
        Check(kahanError < 1e-9d,
            "E10 Kahan 1/60 s clock",
            kahanError.ToString("E3", CultureInfo.InvariantCulture) + " s < 1E-09 s");
    }
    private static int Test550_ReferenceDiagnostics()
    {
        Console.WriteLine("--- Reference diagnostics: one-body, determinism, bisection ---");
        int before = failures;
        StudyDiagClockDrift();
        StudyDiagOneBody();
        StudyDiagOneBodySun();
        StudyDiagDeterminism();
        StudyDiagBisection();
        StudyDiagProductionChunks();
        StudyDiagFloatingOrigin();
        StudyDiagTranslation();
        StudyDiagSunRootFrame();
        StudyDiagLocalForce();
        StudyDiagSoiTransition();
        StudyDiagTranslationPerturbed();
        StudyDiagTranslationTolerance();
        StudyDiagKahanTime();
        Console.WriteLine("failed checks: " + (failures - before));
        Console.WriteLine("--- Reference diagnostics complete ---");
        return 0;
    }
}
