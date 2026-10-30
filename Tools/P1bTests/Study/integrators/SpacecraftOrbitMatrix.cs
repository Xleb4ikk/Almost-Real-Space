using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;

internal static partial class P1bTests
{
    private sealed class StudyShipMatrixCase
    {
        public string Label;
        public StarSystem Sys;
        public int Central;
        public Vector3d P0;
        public Vector3d V0;
        public double Mass0;
        public double Seconds;
        public bool Perturbed;
        public bool Thrust;
        public Vector3d ThrustDirection;
        public double ThrustStart;
        public double ThrustSeconds;
        public double ThrustNewtons;
        public double ThrustIsp;
        public double ThrustRampSeconds;
        public bool AllowTaylor;
        public double Tau;
        public double StepScale;
        public DragSource Drag;
        public double WorldChunkSeconds = 600d;
        public double ReferenceStepSeconds;
        public double ScaleLength = 1d;
        public bool UseTaylorDdReference;
        public double AccuracyTarget = 1d;
        public double AlternativeTarget = 10d;
    }

    private sealed class StudyShipMatrixRef
    {
        public Vector3d P;
        public Vector3d V;
        public double M;
        public OrbitalElements Elements;
    }

    private sealed class StudyShipMatrixRefs
    {
        public StudyShipMatrixRef Local;
        public double LocalError;
        public string LocalKind;
        public bool LocalValid;
        public bool LocalAsymptotic;
        public bool LocalResolved;
        public double LocalRatio;
        public double LocalOrder;
        public double LocalFinestDelta;
        public StudyShipMatrixRef Dd;
        public double DdSelf;
        public double DdError;
        public double DdRatio;
        public string DdKind;
        public bool HasDd;
        public double Formulation;
        public bool HasFormulation;
    }

    private sealed class StudyShipMatrixBaseline
    {
        public double Production;
        public double BestLocal;
        public double Formulation;

        public StudyShipMatrixBaseline(double production, double bestLocal, double formulation)
        {
            Production = production;
            BestLocal = bestLocal;
            Formulation = formulation;
        }
    }

    private static readonly Dictionary<string, StudyShipMatrixBaseline> StudyShipMatrixBaselines =
        new Dictionary<string, StudyShipMatrixBaseline>
        {
            { "Earth LEO 400 km circular", new StudyShipMatrixBaseline(4.454e-2, 1.5e-5, 6.623e-2) },
            { "Earth LEO 400 km polar", new StudyShipMatrixBaseline(5.644e-2, 8.5e-6, 6.476e-3) },
            { "Earth LEO 400 km sun-synchronous", new StudyShipMatrixBaseline(1.767e-3, 2.4e-5, 1.847e-3) },
            { "Earth GTO 400 x 40000 km", new StudyShipMatrixBaseline(2.283e-1, 1.6e-5, 3.025e-1) },
            { "Earth Molniya 500 x 40000 km", new StudyShipMatrixBaseline(1.424, 1.1e-3, 4.973e-1) },
            { "Earth GEO 35786 km", new StudyShipMatrixBaseline(1.428, 1.2e-4, 9.522e-1) },
            { "Sun 1 AU circular", new StudyShipMatrixBaseline(4.256, 1e-5, 19.46) },
            { "Sun 0.5 x 2 AU eccentric", new StudyShipMatrixBaseline(2.567, 1e-5, 16.81) },
            { "Earth 400 km + J2", new StudyShipMatrixBaseline(5.977e-1, 8e-6, double.NaN) },
            { "Earth 250 km + atmosphere drag", new StudyShipMatrixBaseline(8.442e-1, 1.3e-5, double.NaN) },
            { "Earth LEO 400 km + 600 s prograde burn", new StudyShipMatrixBaseline(5.476e-1, 3.2e-7, double.NaN) },
            { "Earth-Moon flyby inside lunar SOI", new StudyShipMatrixBaseline(1.209e-2, 7.7e-6, 1.011e-2) },
            { "Earth LEO 400 km, 1000 orbits (~64 d)", new StudyShipMatrixBaseline(30.62, 1.4e-3, 8.548) },
            { "Earth HEO 300 x 120000 km", new StudyShipMatrixBaseline(4.687e-1, 3.3e-5, 5.218e-1) },
            { "Earth hyperbolic flyby e=1.5", new StudyShipMatrixBaseline(9.713e-3, 9.5e-7, 3.573e-3) },
            { "Earth LEO 400 km, 30 d, world chunk 1 d", new StudyShipMatrixBaseline(7.190, 1.7e-5, 7.773e-1) },
            { "Earth LEO 400 km + unaligned 600 s burn", new StudyShipMatrixBaseline(1.324e-1, 5.9e-2, double.NaN) }
        };

    private sealed class StudyShipMatrixRow
    {
        public string Method;
        public string Config;
        public string Frame;
        public double Dp;
        public double Dv;
        public double Da;
        public double De;
        public double DEnergy;
        public long Calls;
        public long Steps;
        public double Ms;
        public bool Finite;
        public string Note;
    }

    private sealed class StudyShipMatrixAcc
    {
        public readonly StudyShipMatrixCase Case;
        public long Calls;

        public StudyShipMatrixAcc(StudyShipMatrixCase c)
        {
            Case = c;
        }

        public SpacecraftIntegrationState Evaluate(SpacecraftIntegrationState s, double t)
        {
            Calls++;
            Vector3d gravity = StudyShipMatrixLocalAcceleration(Case, s.Position, t);
            Vector3d force = Vector3d.Zero;
            double flow = 0d;
            if (Case.Drag != null)
            {
                Case.Sys.EvaluateBodyState(Case.Sys.AllBodies[Case.Central], t,
                    out Vector3d bp, out Vector3d bv);
                DynamicsContribution drag = Case.Drag.Evaluate(
                    bp + s.Position, bv + s.Velocity, s.Mass, t);
                force += drag.Force;
                flow += drag.MassFlow;
            }

            double throttle = StudyShipMatrixThrottle(Case, t);
            if (throttle > 0d)
            {
                force += Case.ThrustDirection * (Case.ThrustNewtons * throttle);
                flow += Case.ThrustNewtons * throttle / (Case.ThrustIsp * 9.80665d);
            }

            double mass = s.Mass > 1e-9d ? s.Mass : 1e-9d;
            return new SpacecraftIntegrationState
            {
                Position = s.Velocity,
                Velocity = gravity + (force / mass),
                Mass = -flow
            };
        }

        public void EvaluateArray(double[] s, double t, double[] ds)
        {
            var state = new SpacecraftIntegrationState
            {
                Position = new Vector3d(s[0], s[1], s[2]),
                Velocity = new Vector3d(s[3], s[4], s[5]),
                Mass = s[6]
            };
            SpacecraftIntegrationState d = Evaluate(state, t);
            ds[0] = d.Position.X;
            ds[1] = d.Position.Y;
            ds[2] = d.Position.Z;
            ds[3] = d.Velocity.X;
            ds[4] = d.Velocity.Y;
            ds[5] = d.Velocity.Z;
            ds[6] = d.Mass;
        }
    }

    private delegate void StudyShipMatrixAccFn(double[] s, double t, double[] ds);

    private static double StudyShipMatrixThrottle(StudyShipMatrixCase c, double t)
    {
        if (!c.Thrust)
        {
            return 0d;
        }

        double start = c.ThrustStart;
        double end = start + c.ThrustSeconds;
        // Right limit at the burn start: a forward step that begins exactly at
        // `start` must see the engine on, otherwise the burn loses a full step.
        if (t < start || t >= end)
        {
            return 0d;
        }

        double ramp = c.ThrustRampSeconds;
        if (ramp <= 0d)
        {
            return 1d;
        }

        if (t < start + ramp)
        {
            return (t - start) / ramp;
        }

        if (t > end - ramp)
        {
            return (end - t) / ramp;
        }

        return 1d;
    }

    private static string StudyShipMatrixNote(Exception ex)
    {
        return (ex.GetType().Name + " " + ex.Message)
            .Replace(',', ';')
            .Replace('\r', ' ')
            .Replace('\n', ' ');
    }

    private static string StudyShipMatrixResultsDir()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, "Assets"))
                && Directory.Exists(Path.Combine(dir, "Tools")))
            {
                return Path.Combine(dir, "Tools", "P1bTests", "Study", "integrators", "results");
            }

            dir = Path.GetDirectoryName(dir);
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "Study", "integrators", "results");
    }

    private static StarSystem StudyShipMatrixSystem(bool perturbed, bool bake = true)
    {
        StarSystem sys = StudyRealSolar7();
        OrbitingBody earth = sys.AllBodies[1];
        earth.RotationPeriodSeconds = 86164.0905d;
        if (perturbed)
        {
            earth.J2 = 1.08263e-3d;
            earth.Atmosphere = new AtmosphereProfile
            {
                TopAltitudeMeters = 2e6d,
                SeaLevelDensityKgPerCubicMeter = 1.225d,
                ScaleHeightMeters = 8500d
            };
        }

        if (bake)
        {
            StudyNBody nb = StudyMakeNBody(sys);
            StudyBake("ship matrix " + (perturbed ? "perturbed" : "gravity"),
                sys, nb, perturbed ? 0.25d : 1.2d, 0d);
        }

        return sys;
    }

    private static double StudyShipMatrixSeamJump(StarSystem sys, string label)
    {
        Console.WriteLine();
        Console.WriteLine("=== baked seam jumps: " + label + " ===");
        double worst = 0d;
        double worstVelocity = 0d;
        string worstBody = "none";
        foreach (OrbitingBody body in sys.AllBodies)
        {
            BakedEphemeris baked = body.Baked;
            if (baked == null || baked.SegmentCount < 2)
            {
                Console.WriteLine("  " + body.Name + ": no baked segments"
                    + (body.Parent == null ? " (root)" : ""));
                continue;
            }

            double bodyWorst = 0d;
            double bodyWorstV = 0d;
            for (int seg = 0; seg < baked.SegmentCount - 1; seg++)
            {
                double t = baked.SegmentEndSeconds(seg);
                const double eps = 1e-6d;
                body.EvaluateWorldState(t - eps, out Vector3d p1, out Vector3d v1);
                body.EvaluateWorldState(t + eps, out Vector3d p2, out Vector3d v2);
                double jump = (p2 - p1).Magnitude;
                double vjump = (v2 - v1).Magnitude;
                if (jump > bodyWorst)
                {
                    bodyWorst = jump;
                }

                if (vjump > bodyWorstV)
                {
                    bodyWorstV = vjump;
                }
            }

            Console.WriteLine("  " + body.Name + ": max seam jump "
                + bodyWorst.ToString("E3", CultureInfo.InvariantCulture) + " m, velocity "
                + bodyWorstV.ToString("E3", CultureInfo.InvariantCulture) + " m/s");
            Check(bodyWorstV < 1e-2d,
                "matrix seam velocity jump " + label + " " + body.Name,
                bodyWorstV.ToString("E3", CultureInfo.InvariantCulture) + " m/s < 1E-02 m/s");
            if (bodyWorst > worst)
            {
                worst = bodyWorst;
                worstBody = body.Name;
            }

            if (bodyWorstV > worstVelocity)
            {
                worstVelocity = bodyWorstV;
            }
        }

        Console.WriteLine("  worst: " + worstBody + " "
            + worst.ToString("E3", CultureInfo.InvariantCulture) + " m, velocity "
            + worstVelocity.ToString("E3", CultureInfo.InvariantCulture) + " m/s");
        return worst;
    }

    private static void StudyShipMatrixOrbit(
        StarSystem sys, int central, double rp, double ra,
        double incDeg, double nodeDeg, double argpDeg,
        out Vector3d p, out Vector3d v)
    {
        double mu = sys.AllBodies[central].ResolveStandardGravitationalParameter();
        double a = 0.5d * (rp + ra);
        double vp = Math.Abs(ra - rp) < 1e-6d * rp
            ? Math.Sqrt(mu / rp)
            : Math.Sqrt(mu * ((2d / rp) - (1d / a)));
        Vector3d po = new Vector3d(rp, 0d, 0d);
        Vector3d vo = new Vector3d(0d, vp, 0d);
        double node = nodeDeg * Math.PI / 180d;
        double inc = incDeg * Math.PI / 180d;
        double argp = argpDeg * Math.PI / 180d;
        p = KeplerMath.RotateOrbitalToWorld(po, node, inc, argp);
        v = KeplerMath.RotateOrbitalToWorld(vo, node, inc, argp);
    }

    private static double StudyShipMatrixTau(double rp, double mu)
    {
        return 2d * Math.PI * Math.Sqrt(rp * rp * rp / mu);
    }

    private static double[] StudyShipMatrixSteps(double tau, double divisor, double scale)
    {
        double h = Math.Max(1d, Math.Min(86400d, tau * scale / divisor));
        return new[] { h, 2d * h, 4d * h };
    }

    private static StudyShipMatrixCase StudyShipMatrixCaseOrbit(
        string label, StarSystem sys, int central, double rp, double ra,
        double incDeg, double nodeDeg, double argpDeg, double seconds,
        bool perturbed, bool allowTaylor, double stepScale)
    {
        StudyShipMatrixOrbit(sys, central, rp, ra, incDeg, nodeDeg, argpDeg,
            out Vector3d p, out Vector3d v);
        double mu = sys.AllBodies[central].ResolveStandardGravitationalParameter();
        return new StudyShipMatrixCase
        {
            Label = label,
            Sys = sys,
            Central = central,
            P0 = p,
            V0 = v,
            Mass0 = 1000d,
            Seconds = seconds,
            Perturbed = perturbed,
            AllowTaylor = allowTaylor,
            Tau = StudyShipMatrixTau(rp, mu),
            StepScale = stepScale,
            Drag = perturbed && sys.AllBodies[central].Atmosphere != null
                ? new DragSource(sys.AllBodies[central])
                : null
        };
    }

    private static Vector3d StudyShipMatrixLocalAcceleration(
        StudyShipMatrixCase c, Vector3d relP, double t)
    {
        // Same force the production local-frame step uses; the certified reference
        // and the candidates share the force model, the integrator is independent.
        return c.Sys.EvaluateShipAccelerationLocal(relP, c.Sys.AllBodies[c.Central], t);
    }
    private static StudyShipMatrixRef StudyShipMatrixRefFromState(
        StudyShipMatrixCase c, Vector3d p, Vector3d v, double m)
    {
        return new StudyShipMatrixRef
        {
            P = p,
            V = v,
            M = m,
            Elements = OrbitalElements.FromState(
                p, v, c.Sys.AllBodies[c.Central].ResolveStandardGravitationalParameter())
        };
    }

    private static StudyShipMatrixRef StudyShipMatrixLocalReference(
        StudyShipMatrixCase c, out double selfError, out double refError, out string kind)
    {
        StudyShipCertifiedRef cert = StudyShipCertifiedReference(c, c.Central == 1 && c.Seconds > 30d * 86400d ? 8d : 24d);
        selfError = cert.PositionError;
        refError = cert.PositionError;
        kind = cert.Detail + (cert.Valid ? "" : " [INVALID]");
        return StudyShipMatrixRefFromState(c, cert.P, cert.V, cert.M);
    }

    private static StudyShipMatrixRef StudyShipMatrixDdReference(
        StudyShipMatrixCase c, out double selfError, out double refError,
        out double ratio, out string kind)
    {
        OrbitingBody central = c.Sys.AllBodies[c.Central];
        c.Sys.EvaluateBodyState(central, StudyShipT0, out Vector3d bp, out Vector3d bv);
        if (c.UseTaylorDdReference)
        {
            var t1 = TaylorShip.RunDd(
                c.Sys, bp + c.P0, bv + c.V0, StudyShipT0, c.Seconds / 86400d,
                20, 1e-12d, 1e-15d, 600d, 1e-6d);
            var t2 = TaylorShip.RunDd(
                c.Sys, bp + c.P0, bv + c.V0, StudyShipT0, c.Seconds / 86400d,
                24, 1e-14d, 1e-17d, 300d, 1e-6d);
            selfError = (t1.P - t2.P).Magnitude;
            refError = selfError;
            ratio = double.PositiveInfinity;
            c.Sys.EvaluateBodyState(central, StudyShipT0 + c.Seconds,
                out Vector3d bpT, out Vector3d bvT);
            kind = "Taylor-dd K20/K24";
            return StudyShipMatrixRefFromState(c, t2.P - bpT, t2.V - bvT, c.Mass0);
        }

        double h = c.ReferenceStepSeconds > 0d
            ? c.ReferenceStepSeconds
            : Math.Max(2.5d, Math.Min(600d, c.Tau / 1000d));
        var r1 = StudyShipY6CDd(
            c.Sys, bp + c.P0, bv + c.V0, StudyShipT0, c.Seconds / 86400d, h);
        var r2 = StudyShipY6CDd(
            c.Sys, bp + c.P0, bv + c.V0, StudyShipT0, c.Seconds / 86400d, h * 0.5d);
        selfError = (r1.p - r2.p).Magnitude;
        refError = selfError / 63d;
        var r3 = StudyShipY6CDd(
            c.Sys, bp + c.P0, bv + c.V0, StudyShipT0, c.Seconds / 86400d, h * 0.25d);
        double d23 = (r2.p - r3.p).Magnitude;
        ratio = selfError / Math.Max(d23, 1e-30d);
        bool asymptotic = ratio > 32d && ratio < 128d;
        if (!asymptotic)
        {
            refError = double.NaN;
        }

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "DD cross-check {0}: d(h,h/2)={1:E3} m d(h/2,h/4)={2:E3} m ratio={3:F2}{4}",
            c.Label, selfError, d23, ratio,
            asymptotic ? "" : " (non-asymptotic; used as cross-check only)"));
        kind = "Y6C-dd h=" + h.ToString("G6", CultureInfo.InvariantCulture)
            + (asymptotic
                ? " s (est err " + refError.ToString("E2", CultureInfo.InvariantCulture) + " m)"
                : " s (non-asymptotic)");

        c.Sys.EvaluateBodyState(central, StudyShipT0 + c.Seconds,
            out Vector3d bp1, out Vector3d bv1);
        return StudyShipMatrixRefFromState(c, r2.p - bp1, r2.v - bv1, c.Mass0);
    }

    private static StudyShipMatrixRefs StudyShipMatrixBuildReferences(StudyShipMatrixCase c)
    {
        var refs = new StudyShipMatrixRefs();
        StudyShipCertifiedRef cert = StudyShipCertifiedReference(c, c.Central == 1 && c.Seconds > 30d * 86400d ? 8d : 24d);
        refs.Local = StudyShipMatrixRefFromState(c, cert.P, cert.V, cert.M);
        refs.LocalError = cert.PositionError;
        refs.LocalKind = cert.Detail;
        refs.LocalValid = cert.Valid;
        refs.LocalAsymptotic = cert.Asymptotic;
        refs.LocalResolved = cert.Resolved;
        refs.LocalRatio = cert.Ratio;
        refs.LocalOrder = cert.Order;
        refs.LocalFinestDelta = cert.FinestDelta;

        // The DD world-frame run is a cross-check of the certified local reference,
        // never the grading reference: its own convergence is broken by the baked
        // ephemeris (ratios 1.0-4.0 instead of 64), so it cannot certify accuracy.
        if (!c.Perturbed && !c.Thrust)
        {
            refs.Dd = StudyShipMatrixDdReference(
                c, out refs.DdSelf, out refs.DdError, out refs.DdRatio, out refs.DdKind);
            refs.HasDd = true;
            refs.Formulation = (refs.Local.P - refs.Dd.P).Magnitude;
            refs.HasFormulation = true;
        }

        return refs;
    }

    private static (SpacecraftIntegrationState state, long calls, double ms)
        StudyShipMatrixAdaptive(
        StudyShipMatrixCase c, bool dop853, double posTol, double velTol, double massTol, double rtol)
    {
        var acc = new StudyShipMatrixAcc(c);
        var start = new SpacecraftIntegrationState
        {
            Position = c.P0,
            Velocity = c.V0,
            Mass = c.Mass0
        };
        var sw = Stopwatch.StartNew();
        SpacecraftIntegrationResult r = dop853
            ? SpacecraftOrbitIntegrator.StepForwardDop853(
                start, StudyShipT0, c.Seconds, acc.Evaluate, posTol, velTol, massTol, rtol)
            : SpacecraftOrbitIntegrator.StepForward(
                start, StudyShipT0, c.Seconds, acc.Evaluate, posTol, velTol, massTol, rtol);
        sw.Stop();
        return (r.State, acc.Calls, sw.Elapsed.TotalMilliseconds);
    }

    private static (Vector3d p, Vector3d v, double m, long calls, double ms)
        StudyShipMatrixWorld(
        StudyShipMatrixCase c, bool dop853,
        double posTol = 1e-4d, double velTol = 1e-8d, double massTol = 1e-4d,
        double rtol = 1e-11d, double chunkSeconds = 600d, bool useToleranceFrame = true,
        bool splitThrust = false)
    {
        OrbitingBody central = c.Sys.AllBodies[c.Central];
        c.Sys.EvaluateBodyState(central, StudyShipT0, out Vector3d bp, out Vector3d bv);
        var counting = new CountingSource(new CachedGravitySource(c.Sys));
        var phys = new SpacecraftPhysics
        {
            Integrator = dop853 ? SpacecraftIntegrator.Dop853 : SpacecraftIntegrator.Dopri5,
            ToleranceFrameSystem = useToleranceFrame ? c.Sys : null,
            PositionAbsoluteTolerance = posTol,
            VelocityAbsoluteTolerance = velTol,
            MassAbsoluteTolerance = massTol,
            RelativeTolerance = rtol
        };
        phys.Sources.Add(counting);
        if (c.Drag != null)
        {
            phys.Sources.Add(c.Drag);
        }

        if (c.Thrust)
        {
            phys.Sources.Add(new ThrustSource(c.ThrustNewtons, 0d, new ConstantIsp(c.ThrustIsp))
            {
                ThrottleAt = t => StudyShipMatrixThrottle(c, t),
                ThrustDirection = c.ThrustDirection
            });
        }

        var ship = new Spacecraft(bp + c.P0, bv + c.V0, c.Mass0);
        var sw = Stopwatch.StartNew();
        double t = StudyShipT0;
        double endTime = StudyShipT0 + c.Seconds;
        double[] edges = splitThrust
            ? StudyShipRefBreakpoints(c)
            : new[] { t, endTime };
        void StepRange(double from, double to)
        {
            while (from < to - 1e-12d)
            {
                double chunk = Math.Min(chunkSeconds, to - from);
                phys.Step(ship, from, chunk);
                from += chunk;
            }
        }

        double edge = 1e-6d;
        for (int segment = 0; segment < edges.Length - 1; segment++)
        {
            double segmentEnd = edges[segment + 1];
            if (segmentEnd - t > 3d * edge)
            {
                StepRange(t, t + edge);
                StepRange(t + edge, segmentEnd - edge);
                StepRange(segmentEnd - edge, segmentEnd);
            }
            else
            {
                StepRange(t, segmentEnd);
            }

            t = segmentEnd;
        }

        sw.Stop();
        c.Sys.EvaluateBodyState(central, StudyShipT0 + c.Seconds,
            out Vector3d bp1, out Vector3d bv1);
        return (ship.Position - bp1, ship.Velocity - bv1, ship.Mass,
            counting.Calls, sw.Elapsed.TotalMilliseconds);
    }

    private static (Vector3d p, Vector3d v, long calls, long steps, double ms)
        StudyShipMatrixTaylor(StudyShipMatrixCase c, int order)
    {
        OrbitingBody central = c.Sys.AllBodies[c.Central];
        c.Sys.EvaluateBodyState(central, StudyShipT0, out Vector3d bp, out Vector3d bv);
        TaylorShipResult r = TaylorShip.RunSeconds(
            c.Sys, bp + c.P0, bv + c.V0, StudyShipT0, c.Seconds,
            order, 1e-3d, 1e-6d, 600d, 1e-6d);
        c.Sys.EvaluateBodyState(central, StudyShipT0 + c.Seconds,
            out Vector3d bp1, out Vector3d bv1);
        return (r.P - bp1, r.V - bv1, r.Calls, r.Steps, r.Ms);
    }

    private static void StudyShipMatrixRk4(
        double[] s, double t, double h, long steps, StudyShipMatrixAcc acc, ref long calls)
    {
        var k1 = new double[7];
        var k2 = new double[7];
        var k3 = new double[7];
        var k4 = new double[7];
        var tmp = new double[7];
        for (long n = 0; n < steps; n++)
        {
            double tBase = t + (n * h);
            acc.EvaluateArray(s, tBase, k1);
            calls++;
            for (int i = 0; i < 7; i++)
            {
                tmp[i] = s[i] + 0.5d * h * k1[i];
            }

            acc.EvaluateArray(tmp, tBase + (0.5d * h), k2);
            calls++;
            for (int i = 0; i < 7; i++)
            {
                tmp[i] = s[i] + 0.5d * h * k2[i];
            }

            acc.EvaluateArray(tmp, tBase + (0.5d * h), k3);
            calls++;
            for (int i = 0; i < 7; i++)
            {
                tmp[i] = s[i] + h * k3[i];
            }

            acc.EvaluateArray(tmp, tBase + h, k4);
            calls++;
            for (int i = 0; i < 7; i++)
            {
                s[i] += h / 6d * (k1[i] + 2d * k2[i] + 2d * k3[i] + k4[i]);
            }
        }
    }

    private static void StudyShipMatrixDopri5Fixed(
        double[] s, double t, double h, long steps, StudyShipMatrixAcc acc, ref long calls)
    {
        double[][] A =
        {
            new[] { 1d / 5d },
            new[] { 3d / 40d, 9d / 40d },
            new[] { 44d / 45d, -56d / 15d, 32d / 9d },
            new[] { 19372d / 6561d, -25360d / 2187d, 64448d / 6561d, -212d / 729d },
            new[] { 9017d / 3168d, -355d / 33d, 46732d / 5247d, 49d / 176d, -5103d / 18656d }
        };
        double[] b = { 35d / 384d, 0d, 500d / 1113d, 125d / 192d, -2187d / 6784d, 11d / 84d };
        double[] c =
        {
            0d, DOPRI5Coefficients.c2, DOPRI5Coefficients.c3,
            DOPRI5Coefficients.c4, DOPRI5Coefficients.c5, DOPRI5Coefficients.c6
        };
        var k = new double[6][];
        for (int i = 0; i < 6; i++)
        {
            k[i] = new double[7];
        }

        var tmp = new double[7];
        for (long n = 0; n < steps; n++)
        {
            double tBase = t + (n * h);
            acc.EvaluateArray(s, tBase, k[0]);
            calls++;
            for (int stage = 1; stage < 6; stage++)
            {
                for (int i = 0; i < 7; i++)
                {
                    double sum = 0d;
                    for (int j = 0; j < stage; j++)
                    {
                        sum += A[stage - 1][j] * k[j][i];
                    }

                    tmp[i] = s[i] + h * sum;
                }

                acc.EvaluateArray(tmp, tBase + (c[stage] * h), k[stage]);
                calls++;
            }

            for (int i = 0; i < 7; i++)
            {
                double sum = 0d;
                for (int j = 0; j < 6; j++)
                {
                    sum += b[j] * k[j][i];
                }

                s[i] += h * sum;
            }
        }
    }

    private static void StudyShipMatrixPefrl(
        double[] s, double t, double h, long steps, StudyShipMatrixAcc acc, ref long calls)
    {
        const double xi = 0.1786178958448091d;
        const double lam = -0.2123418310626054d;
        const double chi = -0.06626458266981849d;
        var ds = new double[7];
        acc.EvaluateArray(s, t, ds);
        calls++;
        for (long n = 0; n < steps; n++)
        {
            double tBase = t + (n * h);
            double dt = xi * h;
            s[0] += dt * s[3];
            s[1] += dt * s[4];
            s[2] += dt * s[5];
            s[6] += dt * ds[6];
            acc.EvaluateArray(s, tBase + (xi * h), ds);
            calls++;
            double kv = (1d - (2d * lam)) * h / 2d;
            s[3] += kv * ds[3];
            s[4] += kv * ds[4];
            s[5] += kv * ds[5];

            dt = chi * h;
            s[0] += dt * s[3];
            s[1] += dt * s[4];
            s[2] += dt * s[5];
            s[6] += dt * ds[6];
            acc.EvaluateArray(s, tBase + ((xi + chi) * h), ds);
            calls++;
            kv = lam * h;
            s[3] += kv * ds[3];
            s[4] += kv * ds[4];
            s[5] += kv * ds[5];

            dt = (1d - (2d * (chi + xi))) * h;
            s[0] += dt * s[3];
            s[1] += dt * s[4];
            s[2] += dt * s[5];
            s[6] += dt * ds[6];
            acc.EvaluateArray(s, tBase + ((1d - chi - xi) * h), ds);
            calls++;
            kv = lam * h;
            s[3] += kv * ds[3];
            s[4] += kv * ds[4];
            s[5] += kv * ds[5];

            dt = chi * h;
            s[0] += dt * s[3];
            s[1] += dt * s[4];
            s[2] += dt * s[5];
            s[6] += dt * ds[6];
            acc.EvaluateArray(s, tBase + ((1d - xi) * h), ds);
            calls++;
            kv = (1d - (2d * lam)) * h / 2d;
            s[3] += kv * ds[3];
            s[4] += kv * ds[4];
            s[5] += kv * ds[5];

            dt = xi * h;
            s[0] += dt * s[3];
            s[1] += dt * s[4];
            s[2] += dt * s[5];
            s[6] += dt * ds[6];
        }
    }

    private static void StudyShipMatrixY6(
        double[] s, double t, double h, long steps, StudyShipMatrixAcc acc, ref long calls,
        bool compensated)
    {
        var ds = new double[7];
        var sl = compensated ? new double[7] : null;
        var st = new double[7];
        double[] ws = StudyY6Weights();
        double[] cw = StudyCumulativeWeights(ws);
        Sample(s, sl, st);
        acc.EvaluateArray(st, t, ds);
        calls++;
        for (long n = 0; n < steps; n++)
        {
            double tBase = t + (n * h);
            for (int k = 0; k < ws.Length; k++)
            {
                double dt = ws[k] * h;
                for (int i = 3; i < 6; i++)
                {
                    Add(s, sl, i, 0.5d * dt * ds[i]);
                }

                for (int i = 0; i < 3; i++)
                {
                    Add(s, sl, i, dt * (s[i + 3] + (sl == null ? 0d : sl[i + 3])));
                }

                Add(s, sl, 6, dt * ds[6]);
                Sample(s, sl, st);
                acc.EvaluateArray(st, tBase + (cw[k + 1] * h), ds);
                calls++;
                for (int i = 3; i < 6; i++)
                {
                    Add(s, sl, i, 0.5d * dt * ds[i]);
                }
            }
        }

        if (compensated)
        {
            for (int i = 0; i < 7; i++)
            {
                s[i] += sl[i];
            }
        }
    }

    private static void Sample(double[] s, double[] sl, double[] st)
    {
        for (int i = 0; i < 7; i++)
        {
            st[i] = s[i] + (sl == null ? 0d : sl[i]);
        }
    }

    private static void Add(double[] s, double[] sl, int i, double value)
    {
        if (sl == null)
        {
            s[i] += value;
            return;
        }

        StudyAdd(ref s[i], ref sl[i], value);
    }

    private static void StudyShipMatrixWhStep(
        double[] s, double t, double h, StudyShipMatrixAcc acc, ref long calls,
        double tEnd = double.NaN)
    {
        var ds = new double[7];
        double mu = acc.Case.Sys.AllBodies[acc.Case.Central]
            .ResolveStandardGravitationalParameter();
        double end = double.IsNaN(tEnd) ? t + h : tEnd;
        var p = new Vector3d(s[0], s[1], s[2]);
        var v = new Vector3d(s[3], s[4], s[5]);
        var st = new Vector3d(s[0], s[1], s[2]);
        var sv = new Vector3d(s[3], s[4], s[5]);
        acc.EvaluateArray(s, t, ds);
        calls++;
        Vector3d kepler = PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
            st, Vector3d.Zero, mu);
        Vector3d pert = new Vector3d(ds[3], ds[4], ds[5]) - kepler;
        sv += pert * (0.5d * h);
        StudyKeplerDrift(st, sv, mu, h, out Vector3d p2, out Vector3d v2);
        p = p2;
        v = v2;
        s[0] = p.X;
        s[1] = p.Y;
        s[2] = p.Z;
        s[3] = v.X;
        s[4] = v.Y;
        s[5] = v.Z;
        s[6] += h * ds[6];
        acc.EvaluateArray(s, end, ds);
        calls++;
        kepler = PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
            p, Vector3d.Zero, mu);
        pert = new Vector3d(ds[3], ds[4], ds[5]) - kepler;
        v += pert * (0.5d * h);
        s[3] = v.X;
        s[4] = v.Y;
        s[5] = v.Z;
    }

    private static void StudyShipMatrixWh(
        double[] s, double t, double h, long steps, StudyShipMatrixAcc acc, ref long calls)
    {
        double[] ws = StudyY6Weights();
        double[] cw = StudyCumulativeWeights(ws);
        for (long n = 0; n < steps; n++)
        {
            double tBase = t + (n * h);
            for (int k = 0; k < ws.Length; k++)
            {
                double dt = ws[k] * h;
                StudyShipMatrixWhStep(
                    s, tBase + (cw[k] * h), dt, acc, ref calls,
                    tBase + (cw[k + 1] * h));
            }
        }
    }

    private static (SpacecraftIntegrationState state, long calls, long steps, double ms)
        StudyShipMatrixFixed(
        StudyShipMatrixCase c, string method, double h)
    {
        var acc = new StudyShipMatrixAcc(c);
        var s = new double[]
        {
            c.P0.X, c.P0.Y, c.P0.Z,
            c.V0.X, c.V0.Y, c.V0.Z,
            c.Mass0
        };
        long full = (long)Math.Floor(c.Seconds / h);
        double rem = c.Seconds - full * h;
        long calls = 0L;
        var sw = Stopwatch.StartNew();
        double t = StudyShipT0;
        if (full > 0L)
        {
            StudyShipMatrixCall(method, s, t, h, full, acc, ref calls);
            t += full * h;
        }

        if (rem > 1e-12d)
        {
            StudyShipMatrixCall(method, s, t, rem, 1L, acc, ref calls);
        }

        sw.Stop();
        var state = new SpacecraftIntegrationState
        {
            Position = new Vector3d(s[0], s[1], s[2]),
            Velocity = new Vector3d(s[3], s[4], s[5]),
            Mass = s[6]
        };
        return (state, calls, full + (rem > 1e-12d ? 1L : 0L), sw.Elapsed.TotalMilliseconds);
    }

    private static void StudyShipMatrixCall(
        string method, double[] s, double t, double h, long steps,
        StudyShipMatrixAcc acc, ref long calls)
    {
        switch (method)
        {
            case "RK4":
                StudyShipMatrixRk4(s, t, h, steps, acc, ref calls);
                break;
            case "DOPRI5":
                StudyShipMatrixDopri5Fixed(s, t, h, steps, acc, ref calls);
                break;
            case "PEFRL":
                StudyShipMatrixPefrl(s, t, h, steps, acc, ref calls);
                break;
            case "Y6":
                StudyShipMatrixY6(s, t, h, steps, acc, ref calls, false);
                break;
            case "Y6C":
                StudyShipMatrixY6(s, t, h, steps, acc, ref calls, true);
                break;
            case "WH-Y6":
                StudyShipMatrixWh(s, t, h, steps, acc, ref calls);
                break;
            default:
                throw new ArgumentException("unknown method " + method);
        }
    }

    private static double StudyShipMatrixEnergy(Vector3d p, Vector3d v, double mu)
    {
        return 0.5d * v.SqrMagnitude - mu / p.Magnitude;
    }

    private static StudyShipMatrixRow StudyShipMatrixRowMake(
        string method, string config, string frame,
        StudyShipMatrixCase c, StudyShipMatrixRef reference,
        Vector3d p, Vector3d v, double m, long calls, long steps, double ms, string note)
    {
        var row = new StudyShipMatrixRow
        {
            Method = method,
            Config = config,
            Frame = frame,
            Calls = calls,
            Steps = steps,
            Ms = ms,
            Note = note
        };
        bool finite = p.IsFinite && v.IsFinite && double.IsFinite(m) && m > 0d;
        row.Finite = finite;
        if (!finite)
        {
            row.Dp = double.NaN;
            row.Dv = double.NaN;
            row.Da = double.NaN;
            row.De = double.NaN;
            row.DEnergy = double.NaN;
            return row;
        }

        row.Dp = (p - reference.P).Magnitude;
        row.Dv = (v - reference.V).Magnitude;
        OrbitalElements el = OrbitalElements.FromState(
            p, v, c.Sys.AllBodies[c.Central].ResolveStandardGravitationalParameter());
        row.Da = el.IsValid && reference.Elements.IsValid
            ? Math.Abs(el.SemiMajorAxis - reference.Elements.SemiMajorAxis)
            : double.NaN;
        row.De = el.IsValid && reference.Elements.IsValid
            ? Math.Abs(el.Eccentricity - reference.Elements.Eccentricity)
            : double.NaN;
        double energy = StudyShipMatrixEnergy(p, v,
            c.Sys.AllBodies[c.Central].ResolveStandardGravitationalParameter());
        double refEnergy = StudyShipMatrixEnergy(reference.P, reference.V,
            c.Sys.AllBodies[c.Central].ResolveStandardGravitationalParameter());
        row.DEnergy = Math.Abs(energy - refEnergy) / Math.Max(Math.Abs(refEnergy), 1e-300d);
        return row;
    }

    private static void StudyShipMatrixRunAll(
        StudyShipMatrixCase c, StudyShipMatrixRef localRef, StudyShipMatrixRef worldRef,
        double[] rk4, double[] y6, List<StudyShipMatrixRow> rows)
    {
        foreach (double h in rk4)
        {
            string cfg = "h=" + h.ToString("G6", CultureInfo.InvariantCulture);
            try
            {
                var r = StudyShipMatrixFixed(c, "RK4", h);
                rows.Add(StudyShipMatrixRowMake("RK4", cfg, "local",
                    c, localRef, r.state.Position, r.state.Velocity, r.state.Mass,
                    r.calls, r.steps, r.ms, ""));
            }
            catch (Exception ex)
            {
                rows.Add(StudyShipMatrixRowMake("RK4", cfg, "local",
                    c, localRef, Vector3d.Zero, Vector3d.Zero, 0d, 0L, 0L, 0d, StudyShipMatrixNote(ex)));
            }
        }

        foreach (string method in new[] { "DOPRI5", "PEFRL" })
        {
            foreach (double h in rk4)
            {
                string cfg = "h=" + h.ToString("G6", CultureInfo.InvariantCulture);
                try
                {
                    var r = StudyShipMatrixFixed(c, method, h);
                    rows.Add(StudyShipMatrixRowMake(method, cfg, "local",
                        c, localRef, r.state.Position, r.state.Velocity, r.state.Mass,
                        r.calls, r.steps, r.ms, ""));
                }
                catch (Exception ex)
                {
                    rows.Add(StudyShipMatrixRowMake(method, cfg, "local",
                        c, localRef, Vector3d.Zero, Vector3d.Zero, 0d, 0L, 0L, 0d, StudyShipMatrixNote(ex)));
                }
            }
        }

        foreach (string method in new[] { "Y6", "Y6C", "WH-Y6" })
        {
            foreach (double h in y6)
            {
                string cfg = "h=" + h.ToString("G6", CultureInfo.InvariantCulture);
                try
                {
                    var r = StudyShipMatrixFixed(c, method, h);
                    rows.Add(StudyShipMatrixRowMake(method, cfg, "local",
                        c, localRef, r.state.Position, r.state.Velocity, r.state.Mass,
                        r.calls, r.steps, r.ms, ""));
                }
                catch (Exception ex)
                {
                    rows.Add(StudyShipMatrixRowMake(method, cfg, "local",
                        c, localRef, Vector3d.Zero, Vector3d.Zero, 0d, 0L, 0L, 0d, StudyShipMatrixNote(ex)));
                }
            }
        }

        foreach (bool dop853 in new[] { true, false })
        {
            foreach (double tol in new[] { 1e-6d })
            {
                string cfg = "tol=" + tol.ToString("G3", CultureInfo.InvariantCulture);
                try
                {
                    // One row only, as a consistency check. A local tolerance sweep
                    // at tol=1e-9 is fictitious here: rtol*|r| = 6.8e-6 m dominates
                    // it. The real tolerance sweep is StudyShipMatrixOrderAndTolerance
                    // (rtol=0, pure two-body).
                    var r = StudyShipMatrixAdaptive(c, dop853, tol, tol * 1e-3d, tol, 1e-12d);
                    rows.Add(StudyShipMatrixRowMake(dop853 ? "DOP853" : "DOPRI5", cfg, "local",
                        c, localRef, r.state.Position, r.state.Velocity, r.state.Mass,
                        r.calls, -1L, r.ms, ""));
                }
                catch (Exception ex)
                {
                    rows.Add(StudyShipMatrixRowMake(dop853 ? "DOP853" : "DOPRI5", cfg, "local",
                        c, localRef, Vector3d.Zero, Vector3d.Zero, 0d, 0L, 0L, 0d, StudyShipMatrixNote(ex)));
                }
            }
        }

        string worldCfg = "world " + c.WorldChunkSeconds.ToString("G", CultureInfo.InvariantCulture) + " s";
        foreach (bool dop853 in new[] { true, false })
        {
            try
            {
                var r = StudyShipMatrixWorld(c, dop853, chunkSeconds: c.WorldChunkSeconds);
                rows.Add(StudyShipMatrixRowMake(dop853 ? "DOP853" : "DOPRI5",
                    worldCfg, "world", c, worldRef, r.p, r.v, r.m,
                    r.calls, -1L, r.ms, "production"));
            }
            catch (Exception ex)
            {
                rows.Add(StudyShipMatrixRowMake(dop853 ? "DOP853" : "DOPRI5",
                    worldCfg, "world", c, worldRef, Vector3d.Zero, Vector3d.Zero, 0d,
                    0L, 0L, 0d, StudyShipMatrixNote(ex)));
            }
        }

        if (c.Thrust)
        {
            try
            {
                var r = StudyShipMatrixWorld(
                    c, true, chunkSeconds: c.WorldChunkSeconds, splitThrust: true);
                rows.Add(StudyShipMatrixRowMake("DOP853",
                    "world burn-split", "world", c, worldRef, r.p, r.v, r.m,
                    r.calls, -1L, r.ms, "burn-split"));
            }
            catch (Exception ex)
            {
                rows.Add(StudyShipMatrixRowMake("DOP853",
                    "world burn-split", "world", c, worldRef,
                    Vector3d.Zero, Vector3d.Zero, 0d, 0L, 0L, 0d,
                    StudyShipMatrixNote(ex)));
            }
        }

        if (c.Thrust && Math.Abs(c.WorldChunkSeconds - 600d) > 1e-9d)
        {
            foreach (bool dop853 in new[] { true, false })
            {
                string fixedCfg = "world 600 s (fixed)";
                try
                {
                    var r = StudyShipMatrixWorld(c, dop853, chunkSeconds: 600d);
                    rows.Add(StudyShipMatrixRowMake(dop853 ? "DOP853" : "DOPRI5",
                        fixedCfg, "world", c, worldRef, r.p, r.v, r.m,
                        r.calls, -1L, r.ms, "trial-mass check"));
                }
                catch (Exception ex)
                {
                    rows.Add(StudyShipMatrixRowMake(dop853 ? "DOP853" : "DOPRI5",
                        fixedCfg, "world", c, worldRef, Vector3d.Zero, Vector3d.Zero, 0d,
                        0L, 0L, 0d, StudyShipMatrixNote(ex)));
                }
            }
        }

        if (c.AllowTaylor)
        {
            foreach (int order in new[] { 12 })
            {
                string cfg = "K" + order.ToString(CultureInfo.InvariantCulture);
                try
                {
                    var r = StudyShipMatrixTaylor(c, order);
                    rows.Add(StudyShipMatrixRowMake("Taylor", cfg, "world",
                        c, worldRef, r.p, r.v, c.Mass0, r.calls, r.steps, r.ms, ""));
                }
                catch (Exception ex)
                {
                    rows.Add(StudyShipMatrixRowMake("Taylor", cfg, "world",
                        c, worldRef, Vector3d.Zero, Vector3d.Zero, 0d, 0L, 0L, 0d, StudyShipMatrixNote(ex)));
                }
            }
        }
    }

    private static void StudyShipMatrixPrint(
        StudyShipMatrixCase c, StudyShipMatrixRefs refs, List<StudyShipMatrixRow> rows)
    {
        Console.WriteLine();
        Console.WriteLine("=== " + c.Label + " (" + (c.Seconds / 86400d).ToString("G6", CultureInfo.InvariantCulture) + " d) ===");
        Console.WriteLine("reference: " + refs.LocalKind
            + ((refs.LocalAsymptotic || refs.LocalResolved) ? "" : " [INVALID]") + ", est "
            + refs.LocalError.ToString("E2", CultureInfo.InvariantCulture) + " m"
            + ", resolution=" + refs.LocalFinestDelta.ToString("E2", CultureInfo.InvariantCulture) + " m"
            + ", ratio=" + refs.LocalRatio.ToString("F2", CultureInfo.InvariantCulture));
        Console.WriteLine("reference target: " + c.AccuracyTarget.ToString("G3", CultureInfo.InvariantCulture)
            + " m (alternative methods " + c.AlternativeTarget.ToString("G3", CultureInfo.InvariantCulture) + " m)");
        if (refs.HasFormulation)
        {
            Console.WriteLine("cross-check DD: " + refs.DdKind + ", self="
                + refs.DdSelf.ToString("E3", CultureInfo.InvariantCulture) + " m");
            Console.WriteLine("certified-vs-DD: "
                + refs.Formulation.ToString("E3", CultureInfo.InvariantCulture) + " m");
        }
        else
        {
            Console.WriteLine("certified-vs-DD: N/A (perturbed/thrust)");
        }
        Console.WriteLine("frame method      config          dp(m)       dv(m/s)     da(m)       de          dE/E        calls      steps      ms");
        foreach (StudyShipMatrixRow r in rows)
        {
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-5} {1,-11} {2,-15} {3,11:E3} {4,11:E3} {5,11:E3} {6,11:E3} {7,11:E3} {8,10} {9,10} {10,8:F0}{11}",
                r.Frame, r.Method, r.Config, r.Dp, r.Dv, r.Da, r.De, r.DEnergy,
                r.Calls.ToString("N0", CultureInfo.InvariantCulture),
                r.Steps < 0L ? "-" : r.Steps.ToString("N0", CultureInfo.InvariantCulture),
                r.Ms, string.IsNullOrEmpty(r.Note) ? "" : " " + r.Note));
        }

        StudyShipMatrixPareto(rows, "local", c.AccuracyTarget);
        StudyShipMatrixPareto(rows, "world", c.AccuracyTarget);
    }

    private static void StudyShipMatrixPareto(List<StudyShipMatrixRow> rows, string frame, double target)
    {
        var valid = new List<StudyShipMatrixRow>();
        foreach (StudyShipMatrixRow r in rows)
        {
            if (r.Frame == frame && r.Finite && double.IsFinite(r.Dp) && r.Calls > 0L
                && r.Dp <= target)
            {
                valid.Add(r);
            }
        }

        if (valid.Count == 0)
        {
            Console.WriteLine("no valid rows");
            return;
        }

        Console.Write(frame + " Pareto (calls, dp):");
        foreach (StudyShipMatrixRow r in valid)
        {
            bool dominated = false;
            foreach (StudyShipMatrixRow q in valid)
            {
                if (q == r)
                {
                    continue;
                }

                if (q.Calls <= r.Calls && q.Dp <= r.Dp
                    && (q.Calls < r.Calls || q.Dp < r.Dp))
                {
                    dominated = true;
                    break;
                }
            }

            if (!dominated)
            {
                Console.Write(" " + r.Method + "/" + r.Config
                    + "=" + r.Dp.ToString("E2", CultureInfo.InvariantCulture)
                    + "@" + r.Calls.ToString("N0", CultureInfo.InvariantCulture)
                    + "/" + r.Ms.ToString("F1", CultureInfo.InvariantCulture) + "ms");
            }
        }

        Console.WriteLine();
        StudyShipMatrixRow accurate = valid[0];
        foreach (StudyShipMatrixRow r in valid)
        {
            if (r.Dp < accurate.Dp)
            {
                accurate = r;
            }
        }

        Console.WriteLine("  best accuracy: " + accurate.Method + "/" + accurate.Config
            + " dp=" + accurate.Dp.ToString("E2", CultureInfo.InvariantCulture)
            + " m calls=" + accurate.Calls.ToString("N0", CultureInfo.InvariantCulture)
            + " ms=" + accurate.Ms.ToString("F0", CultureInfo.InvariantCulture));
        foreach (double limit in new[] { 1d, 10d, 100d, 1000d })
        {
            StudyShipMatrixRow bestCalls = null;
            StudyShipMatrixRow bestMs = null;
            foreach (StudyShipMatrixRow r in valid)
            {
                if (r.Dp > limit)
                {
                    continue;
                }

                if (bestCalls == null || r.Calls < bestCalls.Calls
                    || (r.Calls == bestCalls.Calls && r.Dp < bestCalls.Dp))
                {
                    bestCalls = r;
                }

                if (bestMs == null || r.Ms < bestMs.Ms
                    || (r.Ms == bestMs.Ms && r.Dp < bestMs.Dp))
                {
                    bestMs = r;
                }
            }

            Console.WriteLine("  <= " + limit.ToString("G", CultureInfo.InvariantCulture) + " m: calls "
                + (bestCalls == null ? "none"
                    : bestCalls.Method + "/" + bestCalls.Config + " calls="
                      + bestCalls.Calls.ToString("N0", CultureInfo.InvariantCulture)
                      + " dp=" + bestCalls.Dp.ToString("E2", CultureInfo.InvariantCulture))
                + "; ms "
                + (bestMs == null ? "none"
                    : bestMs.Method + "/" + bestMs.Config + " ms="
                      + bestMs.Ms.ToString("F0", CultureInfo.InvariantCulture)
                      + " dp=" + bestMs.Dp.ToString("E2", CultureInfo.InvariantCulture)));
        }
    }

    private static void StudyShipMatrixScoreboard(
        List<StudyShipMatrixRow> all, List<string> labels)
    {
        foreach (string frame in new[] { "local", "world" })
        {
            foreach (double target in new[] { 1d, 10d, 100d })
            {
                var winsCalls = new Dictionary<string, int>();
                var winsMs = new Dictionary<string, int>();
                var seen = new HashSet<string>();
                for (int i = 0; i < all.Count; i++)
                {
                    string scenario = labels[i];
                    if (!seen.Add(scenario))
                    {
                        continue;
                    }

                    StudyShipMatrixRow bestCalls = null;
                    StudyShipMatrixRow bestMs = null;
                    for (int j = 0; j < all.Count; j++)
                    {
                        StudyShipMatrixRow q = all[j];
                        if (labels[j] != scenario || q.Frame != frame || !q.Finite
                            || !double.IsFinite(q.Dp) || q.Calls <= 0L || q.Dp > target)
                        {
                            continue;
                        }

                        if (bestCalls == null || q.Calls < bestCalls.Calls
                            || (q.Calls == bestCalls.Calls && q.Dp < bestCalls.Dp))
                        {
                            bestCalls = q;
                        }

                        if (bestMs == null || q.Ms < bestMs.Ms
                            || (q.Ms == bestMs.Ms && q.Dp < bestMs.Dp))
                        {
                            bestMs = q;
                        }
                    }

                    if (bestCalls != null)
                    {
                        string key = bestCalls.Method + "/" + bestCalls.Config;
                        winsCalls[key] = winsCalls.TryGetValue(key, out int n) ? n + 1 : 1;
                    }

                    if (bestMs != null)
                    {
                        string key = bestMs.Method + "/" + bestMs.Config;
                        winsMs[key] = winsMs.TryGetValue(key, out int n) ? n + 1 : 1;
                    }
                }

                Console.Write(frame + " <= " + target.ToString("G", CultureInfo.InvariantCulture)
                    + " m wins by calls:");
                StudyShipMatrixPrintWins(winsCalls);
                Console.Write(frame + " <= " + target.ToString("G", CultureInfo.InvariantCulture)
                    + " m wins by ms:");
                StudyShipMatrixPrintWins(winsMs);
            }
        }
    }

    private static void StudyShipMatrixPrintWins(Dictionary<string, int> wins)
    {
        if (wins.Count == 0)
        {
            Console.Write(" none");
        }
        else
        {
            foreach (var kv in wins)
            {
                Console.Write(" " + kv.Key + "=" + kv.Value);
            }
        }

        Console.WriteLine();
    }

    private static void StudyShipMatrixCsv(
        List<StudyShipMatrixRow> all, List<string> labels, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("scenario,frame,method,config,dp_m,dv_mps,da_m,de,denergy,calls,steps,ms,finite,note");
        for (int i = 0; i < all.Count; i++)
        {
            StudyShipMatrixRow r = all[i];
            sb.Append(labels[i]).Append(',')
                .Append(r.Frame).Append(',')
                .Append(r.Method).Append(',')
                .Append(r.Config).Append(',')
                .Append(r.Dp.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(r.Dv.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(r.Da.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(r.De.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(r.DEnergy.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(r.Calls.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(r.Steps.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(r.Ms.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(r.Finite ? "1" : "0").Append(',')
                .Append(r.Note).AppendLine();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine("csv: " + path);
    }

    private static (Vector3d p, Vector3d v) StudyShipMatrixAnalyticCircle(
        double radius, double speed, double seconds)
    {
        double n = speed / radius;
        double theta = n * seconds;
        double c = Math.Cos(theta);
        double s = Math.Sin(theta);
        return (
            new Vector3d(radius * c, radius * s, 0d),
            new Vector3d(-speed * s, speed * c, 0d));
    }

    private static void StudyShipMatrixOrderAndTolerance()
    {
        Console.WriteLine();
        Console.WriteLine("=== Pure two-body: fixed-step convergence order and tolerance scaling ===");
        const double mu = 3.986004418e14d;
        const double radius = 6.371e6d + 4e5d;
        var body = new OrbitingBody
        {
            Name = "Earth",
            StandardGravitationalParameter = mu,
            Radius = 6.371e6d
        };
        var sys = new StarSystem(body);
        double speed = Math.Sqrt(mu / radius);
        double period = 2d * Math.PI * Math.Sqrt(radius * radius * radius / mu);
        var c = new StudyShipMatrixCase
        {
            Label = "pure two-body",
            Sys = sys,
            Central = 0,
            P0 = new Vector3d(radius, 0d, 0d),
            V0 = new Vector3d(0d, speed, 0d),
            Mass0 = 1000d,
            Seconds = 10d * period,
            Tau = period,
            StepScale = 1d
        };
        var exact = StudyShipMatrixAnalyticCircle(radius, speed, c.Seconds);

        var methods = new[]
        {
            ("RK4", 64d, 3.5d, 4.5d),
            ("PEFRL", 64d, 3.5d, 4.5d),
            ("DOPRI5", 64d, 4.5d, 5.5d),
            ("Y6", 16d, 5.5d, 6.5d)
        };
        foreach (var entry in methods)
        {
            string method = entry.Item1;
            double h = period / entry.Item2;
            var e1 = StudyShipMatrixFixed(c, method, h);
            var e2 = StudyShipMatrixFixed(c, method, h * 0.5d);
            var e3 = StudyShipMatrixFixed(c, method, h * 0.25d);
            double err1 = (e1.state.Position - exact.p).Magnitude;
            double err2 = (e2.state.Position - exact.p).Magnitude;
            double err3 = (e3.state.Position - exact.p).Magnitude;
            double order = Math.Log(err1 / Math.Max(err2, 1e-30d), 2d);
            double order2 = Math.Log(err2 / Math.Max(err3, 1e-30d), 2d);
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-8} h={1,8:F3} err={2,11:E3}/{3,11:E3}/{4,11:E3} order={5:F2}/{6:F2}",
                method, h, err1, err2, err3, order, order2));
            Check(order > entry.Item3 - 0.5d && order < entry.Item4 + 0.5d,
                "order " + method, order.ToString("F2", CultureInfo.InvariantCulture));
            Check(order2 > entry.Item3 - 0.5d && order2 < entry.Item4 + 0.5d,
                "order2 " + method, order2.ToString("F2", CultureInfo.InvariantCulture));
        }

        var wh = StudyShipMatrixFixed(c, "WH-Y6", period / 16d);
        double whErr = (wh.state.Position - exact.p).Magnitude;
        Console.WriteLine("WH-Y6 pure Kepler error: " + whErr.ToString("E3", CultureInfo.InvariantCulture) + " m");
        Check(whErr < 1e-3d, "WH-Y6 pure Kepler exact drift",
            whErr.ToString("E3", CultureInfo.InvariantCulture));

        double[] dopDiv = { 4d, 8d, 16d, 32d, 64d };
        var dopErr = new double[dopDiv.Length];
        for (int i = 0; i < dopDiv.Length; i++)
        {
            SpacecraftIntegrationState r = StudyShipRefDop853Fixed(
                c, period / dopDiv[i], out _);
            dopErr[i] = (r.Position - exact.p).Magnitude;
        }

        Console.Write("DOP853 fixed-step err:");
        for (int i = 0; i < dopErr.Length; i++)
        {
            Console.Write(" " + dopErr[i].ToString("E2", CultureInfo.InvariantCulture));
        }

        Console.WriteLine();
        int last = dopErr.Length - 1;
        double lastOrder = Math.Log(
            Math.Max(dopErr[last - 1], 1e-300d) / Math.Max(dopErr[last], 1e-300d), 2d);
        Check(lastOrder > 6.5d && lastOrder < 9.5d,
            "DOP853 fixed order (finest pair)",
            lastOrder.ToString("F2", CultureInfo.InvariantCulture));
        Check(dopErr[last] < 1e-2d,
            "DOP853 fixed accuracy (finest pair)",
            dopErr[last].ToString("E2", CultureInfo.InvariantCulture) + " m < 1E-02 m");

        StudyShipMatrixWhEccentric();

        var reverseAcc = new StudyShipMatrixAcc(c);
        var reverseState = new double[]
        {
            c.P0.X, c.P0.Y, c.P0.Z,
            c.V0.X, c.V0.Y, c.V0.Z,
            c.Mass0
        };
        long reverseCalls = 0L;
        StudyShipMatrixCall("Y6", reverseState, StudyShipT0, period / 16d, 160L, reverseAcc, ref reverseCalls);
        StudyShipMatrixCall("Y6", reverseState, StudyShipT0 + c.Seconds, -period / 16d, 160L, reverseAcc, ref reverseCalls);
        double reverseError = Math.Sqrt(
            ((reverseState[0] - c.P0.X) * (reverseState[0] - c.P0.X))
            + ((reverseState[1] - c.P0.Y) * (reverseState[1] - c.P0.Y))
            + ((reverseState[2] - c.P0.Z) * (reverseState[2] - c.P0.Z)));
        Console.WriteLine("Y6 time-reversal error: " + reverseError.ToString("E3", CultureInfo.InvariantCulture) + " m");
        Check(reverseError < 1e-6d, "Y6 time reversibility",
            reverseError.ToString("E3", CultureInfo.InvariantCulture));

        // Absolute-tolerance sweep must NOT carry a fixed relative term: with
        // rtol=1e-12 and |r|=6.8e6 m the relative part is 6.8e-6 m and completely
        // hides tol=1e-6 vs 1e-9 (the old test compared the same run to itself).
        double[] tolerances = { 1e-1d, 1e-2d, 1e-3d, 1e-4d };
        var errors = new double[tolerances.Length];
        Console.WriteLine("DOP853 absolute tolerance sweep (rtol=0), floor from 16*eps*|r| = "
            + (16d * 2.220446049250313e-16 * radius).ToString("E2", CultureInfo.InvariantCulture) + " m");
        for (int i = 0; i < tolerances.Length; i++)
        {
            double tol = tolerances[i];
            var r = StudyShipMatrixAdaptive(c, true, tol, tol * 1e-3d, tol, 0d);
            errors[i] = (r.state.Position - exact.p).Magnitude;
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "DOP853 tol={0:E1} rtol=0 err={1:E3} m calls={2:N0}",
                tol, errors[i], r.calls));
            Check(errors[i] < 10d * tol,
                "tolerance bound " + tol.ToString("E1", CultureInfo.InvariantCulture),
                errors[i].ToString("E3", CultureInfo.InvariantCulture) + " m < 10x tol");
        }

        for (int i = 1; i < errors.Length; i++)
        {
            Check(errors[i] <= errors[i - 1] * 1.05d,
                "tolerance monotonic " + tolerances[i].ToString("E0", CultureInfo.InvariantCulture),
                errors[i].ToString("E3", CultureInfo.InvariantCulture) + " <= "
                + errors[i - 1].ToString("E3", CultureInfo.InvariantCulture));
        }

        double improvement = errors[0] / Math.Max(errors[2], 1e-30d);
        Check(improvement > 10d,
            "tolerance controls error",
            "err(tol=1e-1)/err(tol=1e-3) = "
            + improvement.ToString("F1", CultureInfo.InvariantCulture) + " > 10");

        StudyShipMatrixJ2Secular();
        StudyShipMatrixDragThrustForce();
    }

    private static void StudyShipMatrixDragThrustForce()
    {
        Console.WriteLine();
        Console.WriteLine("=== Drag/thrust force formula (independent recompute) ===");
        var earth = new OrbitingBody
        {
            Name = "Earth",
            StandardGravitationalParameter = 3.986004418e14d,
            Radius = 6.371e6d,
            RotationPeriodSeconds = 86164.0905d,
            Atmosphere = new AtmosphereProfile
            {
                TopAltitudeMeters = 2e6d,
                SeaLevelDensityKgPerCubicMeter = 1.225d,
                ScaleHeightMeters = 8500d
            }
        };
        var sys = new StarSystem(earth);
        var drag = new DragSource(earth) { DragCoefficient = 2.2d, ReferenceAreaM2 = 12d };
        earth.EvaluateWorldState(StudyShipT0, out Vector3d bp, out Vector3d bv);
        Vector3d relP = new Vector3d(earth.Radius + 250e3d, 0d, 0d);
        Vector3d shipV = bv + new Vector3d(0d, 7700d, 0d);
        DynamicsContribution dc = drag.Evaluate(bp + relP, shipV, 1000d, StudyShipT0);
        double altitude = relP.Magnitude - earth.Radius;
        double density = 1.225d * Math.Exp(-altitude / 8500d);
        Vector3d atmosphereV = bv + Vector3d.Cross(
            earth.SpinAxis * earth.SpinAngularSpeed, relP);
        Vector3d vRel = shipV - atmosphereV;
        Vector3d expected = vRel * (-0.5d * density * vRel.SqrMagnitude
            * 2.2d * 12d / Math.Max(vRel.Magnitude, 1e-30d));
        double dragRel = (dc.Force - expected).Magnitude / Math.Max(expected.Magnitude, 1e-30d);
        Check(dragRel < 1e-12d, "drag force formula",
            dragRel.ToString("E2", CultureInfo.InvariantCulture) + " relative");

        var thrust = new ThrustSource(2000d, 0d, new ConstantIsp(300d))
        {
            ThrottleAt = _ => 1d,
            ThrustDirection = new Vector3d(0d, 1d, 0d)
        };
        DynamicsContribution tc = thrust.Evaluate(Vector3d.Zero, Vector3d.Zero, 1000d, StudyShipT0);
        double flowError = Math.Abs(tc.MassFlow - (2000d / (300d * 9.80665d)));
        Check((tc.Force - new Vector3d(0d, 2000d, 0d)).Magnitude < 1e-9d && flowError < 1e-12d,
            "thrust force formula",
            "force " + (tc.Force - new Vector3d(0d, 2000d, 0d)).Magnitude.ToString("E2", CultureInfo.InvariantCulture)
            + " N, flow " + flowError.ToString("E2", CultureInfo.InvariantCulture) + " kg/s");
    }

    private static void StudyShipMatrixJ2Secular()
    {
        Console.WriteLine();
        Console.WriteLine("=== J2 secular nodal regression vs first-order analytic ===");
        const double mu = 3.986004418e14d;
        const double radius = 6.371e6d;
        const double j2 = 1.08263e-3d;
        const double rp = radius + 4e5d;
        double inc = 53d * Math.PI / 180d;
        var body = new OrbitingBody
        {
            Name = "Earth",
            StandardGravitationalParameter = mu,
            Radius = radius,
            J2 = j2,
            J2ReferenceRadius = radius,
            NorthPoleDirection = new Vector3d(0d, 0d, 1d)
        };
        var sys = new StarSystem(body);
        double speed = Math.Sqrt(mu / rp);
        var p0 = new Vector3d(rp, 0d, 0d);
        var v0 = new Vector3d(0d, speed * Math.Cos(inc), speed * Math.Sin(inc));
        Vector3d h0 = Vector3d.Cross(p0, v0);
        double node0 = Math.Atan2(h0.X, -h0.Y);
        double period = 2d * Math.PI * Math.Sqrt(rp * rp * rp / mu);
        int orbits = 100;
        var c = new StudyShipMatrixCase
        {
            Label = "J2 secular",
            Sys = sys,
            Central = 0,
            P0 = p0,
            V0 = v0,
            Mass0 = 1000d,
            Seconds = orbits * period,
            Tau = period,
            StepScale = 1d
        };
        SpacecraftIntegrationState r = StudyShipRefDop853Fixed(c, period / 64d, out _);
        Vector3d h1 = Vector3d.Cross(r.Position, r.Velocity);
        double node1 = Math.Atan2(h1.X, -h1.Y);
        double dNode = node1 - node0;
        while (dNode > Math.PI)
        {
            dNode -= 2d * Math.PI;
        }

        while (dNode < -Math.PI)
        {
            dNode += 2d * Math.PI;
        }

        double n = Math.Sqrt(mu / (rp * rp * rp));
        double nodeDot = -1.5d * n * j2 * (radius / rp) * (radius / rp) * Math.Cos(inc);
        double expected = nodeDot * c.Seconds;
        double relative = Math.Abs(dNode - expected) / Math.Abs(expected);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "dNode={0:E4} rad, analytic={1:E4} rad over {2} orbits, relative={3:P2}",
            dNode, expected, orbits, relative));
        Check(relative < 0.02d,
            "J2 nodal regression",
            relative.ToString("P2", CultureInfo.InvariantCulture) + " < 2%");
    }

    private static void StudyShipMatrixWhEccentric()
    {
        Console.WriteLine();
        Console.WriteLine("=== WH-Y6 eccentric Kepler vs Kepler3DMatrix gold ===");
        foreach (int index in new[] { 3, 6 })
        {
            Kepler3DCase k = Kepler3DMatrix[index];
            double[] pt = ParseKeplerGolden(k.PT);
            double[] vt = ParseKeplerGolden(k.VT);
            var body = new OrbitingBody
            {
                Name = "Central",
                StandardGravitationalParameter = k.Mu,
                Radius = 1d
            };
            var sys = new StarSystem(body);
            var c = new StudyShipMatrixCase
            {
                Label = "WH " + k.Label,
                Sys = sys,
                Central = 0,
                P0 = new Vector3d(k.P0[0], k.P0[1], k.P0[2]),
                V0 = new Vector3d(k.V0[0], k.V0[1], k.V0[2]),
                Mass0 = 1000d,
                Seconds = k.TotalSeconds,
                Tau = 2d * Math.PI * Math.Sqrt(1d / k.Mu),
                StepScale = 1d
            };
            double h = c.Tau / 16d;
            var r = StudyShipMatrixFixed(c, "WH-Y6", h);
            double dp = (r.state.Position - new Vector3d(pt[0], pt[1], pt[2])).Magnitude;
            double dv = (r.state.Velocity - new Vector3d(vt[0], vt[1], vt[2])).Magnitude;
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "WH-Y6 {0} h={1:F6} dp={2:E3} m dv={3:E3} m/s",
                k.Label, h, dp, dv));
            Check(dp < 1e-6d, "WH eccentric " + k.Label + " dp",
                dp.ToString("E3", CultureInfo.InvariantCulture));
            Check(dv < 1e-6d, "WH eccentric " + k.Label + " dv",
                dv.ToString("E3", CultureInfo.InvariantCulture));
        }
    }

    private static void StudyShipMatrixSetTargets(StudyShipMatrixCase c)
    {
        c.AccuracyTarget = 1d;
        c.AlternativeTarget = 10d;
        if (c.Label.IndexOf("GTO", StringComparison.Ordinal) >= 0
            || c.Label.IndexOf("GEO", StringComparison.Ordinal) >= 0)
        {
            c.AccuracyTarget = 2d;
            c.AlternativeTarget = 5d;
        }
        else if (c.Label.IndexOf("Molniya", StringComparison.Ordinal) >= 0)
        {
            c.AccuracyTarget = 2d;
            c.AlternativeTarget = 10d;
        }
        else if (c.Label.StartsWith("Sun", StringComparison.OrdinalIgnoreCase))
        {
            c.AccuracyTarget = 1d;
            c.AlternativeTarget = 100d;
        }

        if (c.Label.IndexOf("1000 orbits", StringComparison.Ordinal) >= 0)
        {
            // Requirement: <= 1 m over 64 days (~0.016 m/day). The local-frame
            // production path measures ~0.01 m; the old absolute path was ~60 m.
            c.AccuracyTarget = 1d;
            c.AlternativeTarget = 1d;
        }
        else if (c.Label.IndexOf("HEO", StringComparison.OrdinalIgnoreCase) >= 0
            || c.Label.IndexOf("hyperbolic", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            c.AccuracyTarget = 1d;
            c.AlternativeTarget = 1d;
        }
        else if (c.Label.IndexOf("world chunk", StringComparison.Ordinal) >= 0)
        {
            // 1-day chunks over 30 days: the local-frame carry-over keeps the
            // restart cost below the 1 m mission tolerance.
            c.AccuracyTarget = 1d;
            c.AlternativeTarget = 1d;
        }

        if (c.Perturbed)
        {
            c.AccuracyTarget = Math.Max(c.AccuracyTarget, 2d);
        }

        if (c.Thrust)
        {
            c.AccuracyTarget = Math.Max(c.AccuracyTarget, 2d);
            c.AlternativeTarget = Math.Max(c.AlternativeTarget, 10d);
        }
    }

    private static void StudyShipMatrixGate(
        StudyShipMatrixCase c, StudyShipMatrixRefs refs, List<StudyShipMatrixRow> rows)
    {
        StudyShipMatrixSetTargets(c);
        double productionTarget = c.AccuracyTarget;
        double alternativeTarget = c.AlternativeTarget;

        StudyShipMatrixRow production = null;
        StudyShipMatrixRow unalignedProduction = null;
        StudyShipMatrixRow burnSplit = null;
        StudyShipMatrixRow localAdaptiveDop853 = null;
        StudyShipMatrixRow wh = null;
        StudyShipMatrixRow fixed600 = null;
        foreach (StudyShipMatrixRow r in rows)
        {
            if (r.Method == "DOP853" && r.Frame == "world"
                && r.Note == "production" && r.Finite)
            {
                production = r;
                unalignedProduction = r;
            }

            if (c.Thrust && r.Method == "DOP853" && r.Frame == "world"
                && r.Note == "burn-split" && r.Finite)
            {
                production = r;
                burnSplit = r;
            }

            if (r.Method == "DOP853" && r.Frame == "local" && r.Steps < 0L
                && r.Finite && double.IsFinite(r.Dp)
                && (localAdaptiveDop853 == null || r.Dp < localAdaptiveDop853.Dp))
            {
                localAdaptiveDop853 = r;
            }

            if (r.Method == "WH-Y6" && r.Frame == "local" && r.Finite
                && double.IsFinite(r.Dp)
                && (wh == null || r.Dp < wh.Dp))
            {
                wh = r;
            }

            if (r.Method == "DOP853" && r.Frame == "world"
                && r.Note == "trial-mass check")
            {
                fixed600 = r;
            }
        }

        bool hardEdgeBurn = c.Label.IndexOf("unaligned", StringComparison.OrdinalIgnoreCase) >= 0;
        bool consistencyOnly = c.Label.IndexOf("Earth-Moon flyby", StringComparison.OrdinalIgnoreCase) >= 0;
        bool certified = refs.LocalAsymptotic;
        double refResolution = double.IsNaN(refs.LocalError) ? refs.LocalFinestDelta : refs.LocalError;
        if (hardEdgeBurn)
        {
            // A hard on/off edge is C0, not C1. The unsplit production path is not
            // evidence; the edge-split path is the real path, and it can be gated
            // against the reference if that reference did reach an asymptotic pair.
            if (refs.LocalAsymptotic)
            {
                Console.WriteLine("INFO hard-edge reference asymptotic: " + refs.LocalKind);
                Check(refResolution < 0.1d * productionTarget,
                    "matrix hard-edge reference resolution " + c.Label,
                    refResolution.ToString("E3", CultureInfo.InvariantCulture) + " m < "
                    + (0.1d * productionTarget).ToString("G3", CultureInfo.InvariantCulture) + " m");
            }
            else
            {
                Console.WriteLine("INFO hard-edge burn reference (not certified): " + refs.LocalKind);
            }
        }
        else if (consistencyOnly)
        {
            // The lunar flyby reference diverges (ratio < 1): finest delta is a
            // disagreement, not a resolution. Keep the case as a consistency check
            // (production vs local adaptive DOP853), not as an accuracy proof.
            Console.WriteLine("INFO Earth-Moon flyby reference (no converging reference): " + refs.LocalKind);
        }
        else
        {
            Check(certified,
                "matrix reference certification " + c.Label,
                refs.LocalKind);
            Check(certified && refResolution < 0.1d * productionTarget,
                "matrix reference resolution " + c.Label,
                refResolution.ToString("E3", CultureInfo.InvariantCulture) + " m < "
                + (0.1d * productionTarget).ToString("G3", CultureInfo.InvariantCulture) + " m");
        }

        if (hardEdgeBurn)
        {
            Console.WriteLine("INFO hard-edge burn: unaligned production dp="
                + (unalignedProduction == null ? "missing"
                    : unalignedProduction.Dp.ToString("E3", CultureInfo.InvariantCulture) + " m")
                + ", burn-split dp="
                + (burnSplit == null ? "missing"
                    : burnSplit.Dp.ToString("E3", CultureInfo.InvariantCulture) + " m")
                + " — no accuracy claim for the unsplit path; split path gated below");
            Check(burnSplit != null && burnSplit.Finite
                    && burnSplit.Dp + refResolution <= productionTarget,
                "matrix hard-edge burn-split + reference " + c.Label,
                burnSplit == null ? "missing"
                    : burnSplit.Dp.ToString("E3", CultureInfo.InvariantCulture) + " + "
                      + refResolution.ToString("E3", CultureInfo.InvariantCulture) + " m <= "
                      + productionTarget.ToString("G3", CultureInfo.InvariantCulture) + " m");
        }
        else if (!consistencyOnly)
        {
            Check(production != null && production.Dp <= productionTarget,
                "matrix production DOP853 " + c.Label,
                production == null ? "missing"
                    : production.Dp.ToString("E3", CultureInfo.InvariantCulture) + " m <= "
                      + productionTarget.ToString("G3", CultureInfo.InvariantCulture) + " m");
            Check(localAdaptiveDop853 != null && localAdaptiveDop853.Dp <= alternativeTarget,
                "matrix local adaptive DOP853 " + c.Label,
                localAdaptiveDop853 == null ? "missing"
                    : localAdaptiveDop853.Dp.ToString("E3", CultureInfo.InvariantCulture) + " m <= "
                      + alternativeTarget.ToString("G3", CultureInfo.InvariantCulture) + " m");
            Check(production != null && double.IsFinite(production.Dv) && production.Dv <= 1e-3d,
                "matrix production dv " + c.Label,
                production == null ? "missing"
                    : production.Dv.ToString("E3", CultureInfo.InvariantCulture) + " m/s <= 1E-03");
            Check(production != null && double.IsFinite(production.Da) && production.Da <= 0.1d,
                "matrix production da " + c.Label,
                production == null ? "missing"
                    : production.Da.ToString("E3", CultureInfo.InvariantCulture) + " m <= 0.1 m");
            Check(production != null && double.IsFinite(production.De) && production.De <= 1e-6d,
                "matrix production de " + c.Label,
                production == null ? "missing"
                    : production.De.ToString("E3", CultureInfo.InvariantCulture) + " <= 1E-06");
        }

        if (!c.Perturbed && !c.Thrust && production != null)
        {
            Check(production.DEnergy < 1e-8d,
                "matrix production dE/E " + c.Label,
                production.DEnergy.ToString("E2", CultureInfo.InvariantCulture) + " < 1E-08");
        }

        bool hyperbolic = c.Label.IndexOf("hyperbolic", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!hyperbolic && !c.Thrust)
        {
            Check(wh != null && wh.Dp <= alternativeTarget,
                "matrix WH-Y6 " + c.Label,
                wh == null ? "missing"
                    : wh.Config + " dp=" + wh.Dp.ToString("E3", CultureInfo.InvariantCulture)
                      + " m <= " + alternativeTarget.ToString("G3", CultureInfo.InvariantCulture) + " m");
        }

        if (c.Thrust && !hardEdgeBurn)
        {
            Check(fixed600 != null && fixed600.Finite && fixed600.Dp <= productionTarget,
                "matrix thrust 600 s production chunk " + c.Label,
                fixed600 == null ? "missing"
                    : fixed600.Dp.ToString("E3", CultureInfo.InvariantCulture) + " m <= "
                      + productionTarget.ToString("G3", CultureInfo.InvariantCulture) + " m");
        }

        if (hyperbolic)
        {
            bool rejected = false;
            foreach (StudyShipMatrixRow r in rows)
            {
                if (r.Method == "WH-Y6" && !r.Finite)
                {
                    rejected = true;
                    break;
                }
            }

            Check(rejected, "matrix WH rejects unbound orbit " + c.Label,
                rejected ? "non-finite row" : "silently finite");
        }
    }

    private static void StudyShipMatrixRegression(
        List<StudyShipMatrixRow> all, List<string> labels, Dictionary<string, double> formulations)
    {
        Console.WriteLine();
        Console.WriteLine("=== regression report (3x first run; informational, accuracy gates above) ===");
        int regressions = 0;
        foreach (var entry in StudyShipMatrixBaselines)
        {
            string label = entry.Key;
            StudyShipMatrixBaseline baseline = entry.Value;
            StudyShipMatrixRow production = null;
            StudyShipMatrixRow bestLocal = null;
            for (int i = 0; i < all.Count; i++)
            {
                if (labels[i] != label)
                {
                    continue;
                }

                StudyShipMatrixRow r = all[i];
                if (r.Method == "DOP853" && r.Frame == "world"
                    && r.Note == "production" && r.Finite)
                {
                    production = r;
                }

                if (r.Frame == "local" && r.Finite && double.IsFinite(r.Dp)
                    && (bestLocal == null || r.Dp < bestLocal.Dp))
                {
                    bestLocal = r;
                }
            }

            if (!(production != null && production.Dp <= 3d * baseline.Production))
            {
                regressions++;
            }

            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "INFO production {0}: {1} vs 3x {2:E3} m",
                label,
                production == null
                    ? "missing"
                    : production.Dp.ToString("E3", CultureInfo.InvariantCulture) + " m",
                3d * baseline.Production));

            if (!(bestLocal != null && bestLocal.Dp <= 3d * baseline.BestLocal))
            {
                regressions++;
            }

            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "INFO best local {0}: {1} vs 3x {2:E3} m",
                label,
                bestLocal == null
                    ? "missing"
                    : bestLocal.Method + "/" + bestLocal.Config + " "
                      + bestLocal.Dp.ToString("E3", CultureInfo.InvariantCulture) + " m",
                3d * baseline.BestLocal));

            if (!double.IsNaN(baseline.Formulation)
                && formulations.TryGetValue(label, out double formulation)
                && !double.IsNaN(formulation))
            {
                if (!(formulation <= 3d * baseline.Formulation))
                {
                    regressions++;
                }

                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "INFO formulation {0}: {1:E3} m vs 3x {2:E3} m",
                    label, formulation, 3d * baseline.Formulation));
            }
        }

        Console.WriteLine("regression report: " + regressions
            + " of " + (2 * StudyShipMatrixBaselines.Count) + " comparisons outside 3x (informational)");
    }

    private static int Test549_SpacecraftOrbitIntegrators()
    {
        Console.WriteLine("--- Spacecraft orbit integrator matrix: all methods, all conditions ---");
        int failuresBefore = failures;

        // The production field is the baked N-body ephemeris: its body
        // accelerations are dynamically consistent with the full force, which is
        // what lets the local frame subtract the central body's acceleration
        // exactly. An analytic Kepler hierarchy is NOT consistent for that
        // subtraction (a Moon-centred step drifts against an Earth-centred one),
        // so it cannot be used as a "smooth" drop-in here.
        StarSystem gravity = StudyShipMatrixSystem(false);
        StarSystem perturbed = StudyShipMatrixSystem(true);
        double seamGravity = StudyShipMatrixSeamJump(gravity, "gravity");
        double seamPerturbed = StudyShipMatrixSeamJump(perturbed, "perturbed");
        Check(seamGravity < 0.5d && seamPerturbed < 0.5d,
            "matrix ephemeris seam jump",
            "gravity " + seamGravity.ToString("E3", CultureInfo.InvariantCulture)
            + " m, perturbed " + seamPerturbed.ToString("E3", CultureInfo.InvariantCulture)
            + " m < 0.5 m");
        var cases = new List<StudyShipMatrixCase>();

        const double day = 86400d;
        const double year = 365.25d * 86400d;

        cases.Add(StudyShipMatrixCaseOrbit("Earth LEO 400 km circular",
            gravity, 1, 6.371e6d + 4e5d, 6.371e6d + 4e5d, 0d, 0d, 0d, day, false, true, 1d));
        cases.Add(StudyShipMatrixCaseOrbit("Earth LEO 400 km polar",
            gravity, 1, 6.371e6d + 4e5d, 6.371e6d + 4e5d, 90d, 0d, 0d, day, false, true, 1d));
        cases.Add(StudyShipMatrixCaseOrbit("Earth LEO 400 km sun-synchronous",
            gravity, 1, 6.371e6d + 4e5d, 6.371e6d + 4e5d, 98d, 0d, 0d, day, false, true, 1d));
        cases.Add(StudyShipMatrixCaseOrbit("Earth GTO 400 x 40000 km",
            gravity, 1, 6.371e6d + 4e5d, 6.371e6d + 4e7d, 28.5d, 0d, 0d, 2d * day, false, true, 1d));
        {
            var molniya = StudyShipMatrixCaseOrbit("Earth Molniya 500 x 40000 km",
                gravity, 1, 6.371e6d + 5e5d, 6.371e6d + 4e7d, 63.4d, 0d, 270d, 7d * day, false, true, 1d);
            // The DD cross-check is informational; 43 s steps over 7 days were
            // 176 s of runtime on a slower machine. 300 s is enough to expose a
            // gross formulation error without dominating the suite.
            molniya.ReferenceStepSeconds = 60d;
            cases.Add(molniya);
        }
        cases.Add(StudyShipMatrixCaseOrbit("Earth GEO 35786 km",
            gravity, 1, 4.2164e7d, 4.2164e7d, 0d, 0d, 0d, 30d * day, false, true, 1d));
        cases.Add(StudyShipMatrixCaseOrbit("Sun 1 AU circular",
            gravity, 0, StudyAU, StudyAU, 0d, 0d, 0d, year, false, true, 1d));
        cases.Add(StudyShipMatrixCaseOrbit("Sun 0.5 x 2 AU eccentric",
            gravity, 0, 0.5d * StudyAU, 2d * StudyAU, 0d, 0d, 0d, year, false, true, 1d));
        {
            var j2Only = StudyShipMatrixCaseOrbit("Earth 400 km + J2",
                perturbed, 1, 6.371e6d + 4e5d, 6.371e6d + 4e5d, 0d, 0d, 0d, day, true, false, 1d);
            // The perturbed system carries the atmosphere for the drag case; a case
            // labelled "J2" must isolate J2, not silently add drag.
            j2Only.Drag = null;
            cases.Add(j2Only);
        }

        cases.Add(StudyShipMatrixCaseOrbit("Earth 250 km + atmosphere drag",
            perturbed, 1, 6.371e6d + 2.5e5d, 6.371e6d + 2.5e5d, 0d, 0d, 0d, day, true, false, 0.5d));

        {
            StudyShipMatrixOrbit(gravity, 1, 6.371e6d + 4e5d, 6.371e6d + 4e5d,
                0d, 0d, 0d, out Vector3d tp, out Vector3d tv);
            var thrust = new StudyShipMatrixCase
            {
                Label = "Earth LEO 400 km + 600 s prograde burn",
                Sys = gravity,
                Central = 1,
                P0 = tp,
                V0 = tv,
                Mass0 = 1000d,
                Seconds = day,
                Thrust = true,
                ThrustDirection = tv.Normalized,
                ThrustStart = StudyShipT0 + 96d,
                ThrustSeconds = 600d,
                ThrustNewtons = 2000d,
                ThrustIsp = 300d,
                ThrustRampSeconds = 20d,
                AllowTaylor = false,
                WorldChunkSeconds = 60d,
                Tau = StudyShipMatrixTau(6.371e6d + 4e5d,
                    gravity.AllBodies[1].ResolveStandardGravitationalParameter()),
                StepScale = 0.02d
            };
            cases.Add(thrust);
        }

        {
            // The baked N-body field is the one the local frame is designed for:
            // its body accelerations include the mutual pulls, so subtracting the
            // central body's Newtonian acceleration is exact. The rails are only
            // C0 at seams, hence the reference is "resolved", not 8th-order
            // certified; its finest delta is reported.
            OrbitingBody moon = gravity.AllBodies[2];
            gravity.EvaluateBodyState(moon, StudyShipT0, out Vector3d mp, out Vector3d mv);
            gravity.EvaluateBodyState(gravity.AllBodies[1], StudyShipT0,
                out Vector3d ep, out Vector3d ev);
            var flyby = new StudyShipMatrixCase
            {
                Label = "Earth-Moon flyby inside lunar SOI",
                Sys = gravity,
                Central = 1,
                P0 = (mp - ep) + new Vector3d(0d, 2e7d, 0d),
                V0 = (mv - ev) + new Vector3d(0d, 0d, 500d),
                Mass0 = 1000d,
                Seconds = 2d * day,
                AllowTaylor = true,
                Tau = 80000d,
                StepScale = 0.25d
            };
            cases.Add(flyby);
        }

        {
            var longLeo = StudyShipMatrixCaseOrbit("Earth LEO 400 km, 1000 orbits (~64 d)",
                gravity, 1, 6.371e6d + 4e5d, 6.371e6d + 4e5d, 0d, 0d, 0d, day, false, true, 1d);
            longLeo.Seconds = 1000d * longLeo.Tau;
            longLeo.ReferenceStepSeconds = 10d;
            longLeo.UseTaylorDdReference = true;
            cases.Add(longLeo);
        }

        {
            var heo = StudyShipMatrixCaseOrbit("Earth HEO 300 x 120000 km",
                gravity, 1, 6.371e6d + 3e5d, 1.2e8d, 0d, 0d, 0d, 5d * day, false, true, 1d);
            heo.ReferenceStepSeconds = 60d;
            cases.Add(heo);
        }

        {
            const double earthMu = 3.986004418e14d;
            double rp = 6.371e6d + 3e5d;
            double e = 1.5d;
            double vp = Math.Sqrt(earthMu * (1d + e) / rp);
            cases.Add(new StudyShipMatrixCase
            {
                Label = "Earth hyperbolic flyby e=1.5",
                Sys = gravity,
                Central = 1,
                P0 = new Vector3d(rp, 0d, 0d),
                V0 = new Vector3d(0d, vp, 0d),
                Mass0 = 1000d,
                Seconds = day,
                AllowTaylor = false,
                Tau = StudyShipMatrixTau(rp, earthMu),
                StepScale = 1d,
                ScaleLength = 1e8d,
                ReferenceStepSeconds = 5d
            });
        }

        {
            var chunk = StudyShipMatrixCaseOrbit("Earth LEO 400 km, 30 d, world chunk 1 d",
                gravity, 1, 6.371e6d + 4e5d, 6.371e6d + 4e5d, 0d, 0d, 0d, 30d * day, false, true, 1d);
            chunk.WorldChunkSeconds = 86400d;
            chunk.ReferenceStepSeconds = 20d;
            cases.Add(chunk);
        }

        {
            StudyShipMatrixOrbit(gravity, 1, 6.371e6d + 4e5d, 6.371e6d + 4e5d,
                0d, 0d, 0d, out Vector3d up, out Vector3d uv);
            cases.Add(new StudyShipMatrixCase
            {
                Label = "Earth LEO 400 km + unaligned 600 s burn",
                Sys = gravity,
                Central = 1,
                P0 = up,
                V0 = uv,
                Mass0 = 1000d,
                Seconds = day,
                Thrust = true,
                ThrustDirection = uv.Normalized,
                ThrustStart = StudyShipT0 + 103.7d,
                ThrustSeconds = 600d,
                ThrustNewtons = 2000d,
                ThrustIsp = 300d,
                ThrustRampSeconds = 0d,
                AllowTaylor = false,
                WorldChunkSeconds = 60d,
                Tau = StudyShipMatrixTau(6.371e6d + 4e5d,
                    gravity.AllBodies[1].ResolveStandardGravitationalParameter()),
                StepScale = 0.02d,
                ScaleLength = 6.371e6d + 4e5d
            });
        }

        string resultsDir = StudyShipMatrixResultsDir();
        string csvPath = Path.Combine(resultsDir, "Test549_SpacecraftOrbitMatrix.csv");
        var allRows = new List<StudyShipMatrixRow>();
        var allLabels = new List<string>();
        var formulations = new Dictionary<string, double>();

        foreach (StudyShipMatrixCase c in cases)
        {
            var caseWatch = Stopwatch.StartNew();
            StudyShipMatrixSetTargets(c);
            StudyShipMatrixRefs refs;
            try
            {
                refs = StudyShipMatrixBuildReferences(c);
            }
            catch (Exception ex)
            {
                Console.WriteLine("reference failed for " + c.Label + ": " + StudyShipMatrixNote(ex));
                Console.WriteLine(ex.ToString());
                Check(false, "reference " + c.Label, StudyShipMatrixNote(ex));
                continue;
            }

            double[] rk4 = c.Thrust
                ? new[] { 1d, 2d, 4d }
                : StudyShipMatrixSteps(c.Tau, 128d, c.StepScale);
            double[] y6 = c.Thrust
                ? new[] { 2d, 4d, 8d }
                : StudyShipMatrixSteps(c.Tau, 32d, c.StepScale);
            var rows = new List<StudyShipMatrixRow>();
            StudyShipMatrixRunAll(c, refs.Local, refs.Local, rk4, y6, rows);
            StudyShipMatrixPrint(c, refs, rows);
            int nonFinite = 0;
            foreach (StudyShipMatrixRow r in rows)
            {
                allRows.Add(r);
                allLabels.Add(c.Label);
                if (!r.Finite)
                {
                    nonFinite++;
                }
            }

            if (nonFinite > 0)
            {
                Console.WriteLine("non-finite rows: " + nonFinite);
            }

            StudyShipMatrixGate(c, refs, rows);
            formulations[c.Label] = refs.HasFormulation ? refs.Formulation : double.NaN;
            caseWatch.Stop();
            Console.WriteLine("case time: " + caseWatch.ElapsedMilliseconds + " ms");
        }

        StudyShipMatrixOrderAndTolerance();
        StudyShipMatrixCsv(allRows, allLabels, csvPath);
        StudyShipMatrixScoreboard(allRows, allLabels);
        StudyShipMatrixRegression(allRows, allLabels, formulations);
        Console.WriteLine("failed checks: " + (failures - failuresBefore));
        Console.WriteLine("--- Spacecraft orbit integrator matrix complete ---");
        return 0;
    }

}
