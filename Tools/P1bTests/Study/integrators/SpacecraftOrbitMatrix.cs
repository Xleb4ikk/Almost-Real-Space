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
    }

    private sealed class StudyShipMatrixRef
    {
        public Vector3d P;
        public Vector3d V;
        public double M;
        public OrbitalElements Elements;
    }

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
        if (t <= start || t >= end)
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

    private static StarSystem StudyShipMatrixSystem(bool perturbed)
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

        StudyNBody nb = StudyMakeNBody(sys);
        StudyBake("ship matrix " + (perturbed ? "perturbed" : "gravity"),
            sys, nb, perturbed ? 0.25d : 1.2d, 0d);
        return sys;
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
        IReadOnlyList<OrbitingBody> bodies = c.Sys.AllBodies;
        OrbitingBody central = bodies[c.Central];
        c.Sys.EvaluateBodyState(central, t, out Vector3d bc, out _);
        double mu = central.ResolveStandardGravitationalParameter();
        Vector3d total = PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
            relP, Vector3d.Zero, mu);
        double referenceRadius = central.J2ReferenceRadius > 0d
            ? central.J2ReferenceRadius
            : central.Radius;
        total += PhysicsSolver.CalculateJ2Acceleration(
            relP, central.SpinAxis, mu, central.J2, referenceRadius);
        for (int i = 0; i < bodies.Count; i++)
        {
            if (i == c.Central)
            {
                continue;
            }

            double otherMu = bodies[i].ResolveStandardGravitationalParameter();
            if (otherMu == 0d)
            {
                continue;
            }

            c.Sys.EvaluateBodyState(bodies[i], t, out Vector3d bp, out _);
            Vector3d dc = bc - bp;
            total += PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                dc + relP, Vector3d.Zero, otherMu);
            total -= PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                dc, Vector3d.Zero, otherMu);
        }

        return total;
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

    private static StudyShipMatrixRef StudyShipMatrixReference(
        StudyShipMatrixCase c, double posTol, double velTol, double massTol, double rtol)
    {
        var acc = new StudyShipMatrixAcc(c);
        var start = new SpacecraftIntegrationState
        {
            Position = c.P0,
            Velocity = c.V0,
            Mass = c.Mass0
        };
        SpacecraftIntegrationResult r = SpacecraftOrbitIntegrator.StepForwardDop853(
            start, StudyShipT0, c.Seconds, acc.Evaluate,
            posTol, velTol, massTol, rtol);
        return StudyShipMatrixRefFromState(c, r.State.Position, r.State.Velocity, r.State.Mass);
    }

    private static StudyShipMatrixRef StudyShipMatrixWorldReference(StudyShipMatrixCase c)
    {
        var r = StudyShipMatrixWorld(c, true, 1e-9d, 1e-12d, 1e-9d, 1e-13d, 60d);
        return StudyShipMatrixRefFromState(c, r.p, r.v, r.m);
    }

    private static StudyShipMatrixRef StudyShipMatrixIndependentReference(
        StudyShipMatrixCase c, out double selfError, out string kind)
    {
        if (!c.Perturbed && !c.Thrust)
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
                c.Sys.EvaluateBodyState(central, StudyShipT0 + c.Seconds,
                    out Vector3d bpT, out Vector3d bvT);
                kind = "Taylor-dd K20/K24";
                return StudyShipMatrixRefFromState(c, t1.P - bpT, t1.V - bvT, c.Mass0);
            }

            double h = c.ReferenceStepSeconds > 0d
                ? c.ReferenceStepSeconds
                : Math.Max(2.5d, Math.Min(600d, c.Tau / 1000d));
            var r1 = StudyShipY6CDd(
                c.Sys, bp + c.P0, bv + c.V0, StudyShipT0, c.Seconds / 86400d, h);
            var r2 = StudyShipY6CDd(
                c.Sys, bp + c.P0, bv + c.V0, StudyShipT0, c.Seconds / 86400d, h * 0.5d);
            selfError = (r1.p - r2.p).Magnitude;
            c.Sys.EvaluateBodyState(central, StudyShipT0 + c.Seconds,
                out Vector3d bp1, out Vector3d bv1);
            kind = "Y6C-dd h=" + h.ToString("G6", CultureInfo.InvariantCulture) + " s";
            return StudyShipMatrixRefFromState(c, r1.p - bp1, r1.v - bv1, c.Mass0);
        }

        StudyShipMatrixRef tight = StudyShipMatrixReference(c, 1e-9d, 1e-12d, 1e-9d, 1e-13d);
        StudyShipMatrixRef tighter = StudyShipMatrixReference(c, 1e-10d, 1e-13d, 1e-10d, 1e-14d);
        selfError = (tighter.P - tight.P).Magnitude;
        kind = "DOP853 self-converged";
        return selfError <= 10d ? tighter : tight;
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
        double rtol = 1e-11d, double chunkSeconds = 600d)
    {
        OrbitingBody central = c.Sys.AllBodies[c.Central];
        c.Sys.EvaluateBodyState(central, StudyShipT0, out Vector3d bp, out Vector3d bv);
        var counting = new CountingSource(new CachedGravitySource(c.Sys));
        var phys = new SpacecraftPhysics
        {
            Integrator = dop853 ? SpacecraftIntegrator.Dop853 : SpacecraftIntegrator.Dopri5,
            ToleranceFrameSystem = c.Sys,
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
        double remaining = c.Seconds;
        while (remaining > 1e-9d)
        {
            double chunk = Math.Min(chunkSeconds, remaining);
            phys.Step(ship, t, chunk);
            t += chunk;
            remaining -= chunk;
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
            acc.EvaluateArray(s, t, k1);
            calls++;
            for (int i = 0; i < 7; i++)
            {
                tmp[i] = s[i] + 0.5d * h * k1[i];
            }

            acc.EvaluateArray(tmp, t + 0.5d * h, k2);
            calls++;
            for (int i = 0; i < 7; i++)
            {
                tmp[i] = s[i] + 0.5d * h * k2[i];
            }

            acc.EvaluateArray(tmp, t + 0.5d * h, k3);
            calls++;
            for (int i = 0; i < 7; i++)
            {
                tmp[i] = s[i] + h * k3[i];
            }

            acc.EvaluateArray(tmp, t + h, k4);
            calls++;
            for (int i = 0; i < 7; i++)
            {
                s[i] += h / 6d * (k1[i] + 2d * k2[i] + 2d * k3[i] + k4[i]);
            }

            t += h;
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
            acc.EvaluateArray(s, t, k[0]);
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

                acc.EvaluateArray(tmp, t + (c[stage] * h), k[stage]);
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

            t += h;
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
            double dt = xi * h;
            s[0] += dt * s[3];
            s[1] += dt * s[4];
            s[2] += dt * s[5];
            s[6] += dt * ds[6];
            t += dt;
            acc.EvaluateArray(s, t, ds);
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
            t += dt;
            acc.EvaluateArray(s, t, ds);
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
            t += dt;
            acc.EvaluateArray(s, t, ds);
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
            t += dt;
            acc.EvaluateArray(s, t, ds);
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
            t += dt;
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
        Sample(s, sl, st);
        acc.EvaluateArray(st, t, ds);
        calls++;
        for (long n = 0; n < steps; n++)
        {
            foreach (double w in ws)
            {
                double dt = w * h;
                for (int i = 3; i < 6; i++)
                {
                    Add(s, sl, i, 0.5d * dt * ds[i]);
                }

                for (int i = 0; i < 3; i++)
                {
                    Add(s, sl, i, dt * (s[i + 3] + (sl == null ? 0d : sl[i + 3])));
                }

                Add(s, sl, 6, dt * ds[6]);
                t += dt;
                Sample(s, sl, st);
                acc.EvaluateArray(st, t, ds);
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
        double[] s, double t, double h, StudyShipMatrixAcc acc, ref long calls)
    {
        var ds = new double[7];
        double mu = acc.Case.Sys.AllBodies[acc.Case.Central]
            .ResolveStandardGravitationalParameter();
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
        acc.EvaluateArray(s, t + h, ds);
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
        for (long n = 0; n < steps; n++)
        {
            foreach (double w in ws)
            {
                double dt = w * h;
                StudyShipMatrixWhStep(s, t, dt, acc, ref calls);
                t += dt;
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
            foreach (double tol in new[] { 1e-6d, 1e-9d })
            {
                string cfg = "tol=" + tol.ToString("G3", CultureInfo.InvariantCulture);
                try
                {
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
        StudyShipMatrixCase c, StudyShipMatrixRef reference, double refSelf,
        List<StudyShipMatrixRow> rows)
    {
        Console.WriteLine();
        Console.WriteLine("=== " + c.Label + " (" + (c.Seconds / 86400d).ToString("G6", CultureInfo.InvariantCulture) + " d) ===");
        Console.WriteLine("reference self-convergence: " + refSelf.ToString("E3", CultureInfo.InvariantCulture) + " m");
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

        StudyShipMatrixPareto(rows, "local", c.ScaleLength);
        StudyShipMatrixPareto(rows, "world", c.ScaleLength);
    }

    private static void StudyShipMatrixPareto(List<StudyShipMatrixRow> rows, string frame, double scaleLength)
    {
        var valid = new List<StudyShipMatrixRow>();
        foreach (StudyShipMatrixRow r in rows)
        {
            if (r.Frame == frame && r.Finite && double.IsFinite(r.Dp) && r.Calls > 0L
                && r.Dp < 0.25d * scaleLength)
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
        foreach (double target in new[] { 1d, 10d, 100d, 1000d })
        {
            StudyShipMatrixRow bestCalls = null;
            StudyShipMatrixRow bestMs = null;
            foreach (StudyShipMatrixRow r in valid)
            {
                if (r.Dp > target)
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

            Console.WriteLine("  <= " + target.ToString("G", CultureInfo.InvariantCulture) + " m: calls "
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

        double[] tolerances = { 1e-3d, 1e-6d, 1e-9d };
        var errors = new double[tolerances.Length];
        for (int i = 0; i < tolerances.Length; i++)
        {
            double tol = tolerances[i];
            var r = StudyShipMatrixAdaptive(c, true, tol, tol * 1e-3d, tol, 1e-12d);
            errors[i] = (r.state.Position - exact.p).Magnitude;
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "DOP853 tol={0:E1} err={1:E3} m calls={2:N0}",
                tol, errors[i], r.calls));
            Check(errors[i] < Math.Max(1e3d * tol, 1e-5d),
                "tolerance bound " + tol.ToString("E1", CultureInfo.InvariantCulture),
                errors[i].ToString("E3", CultureInfo.InvariantCulture));
        }

        Check(errors[1] <= Math.Max(10d * errors[0], 1e-6d),
            "tolerance scaling 1e-6 vs 1e-3",
            errors[1].ToString("E3", CultureInfo.InvariantCulture));
        Check(errors[2] <= Math.Max(10d * errors[1], 1e-6d),
            "tolerance scaling 1e-9 vs 1e-6",
            errors[2].ToString("E3", CultureInfo.InvariantCulture));
    }

    private static void StudyShipMatrixGate(
        StudyShipMatrixCase c, double refSelf, List<StudyShipMatrixRow> rows)
    {
        double localGate = 1d;
        double worldGate = 1d;
        double refSelfGate = 10d;
        if (c.Label.IndexOf("GTO", StringComparison.Ordinal) >= 0)
        {
            worldGate = 2d;
        }
        else if (c.Label.IndexOf("Molniya", StringComparison.Ordinal) >= 0)
        {
            worldGate = 5d;
        }
        else if (c.Label.IndexOf("GEO", StringComparison.Ordinal) >= 0)
        {
            worldGate = 2d;
        }
        else if (c.Label.IndexOf("Sun", StringComparison.Ordinal) >= 0)
        {
            worldGate = 5d;
            localGate = 100d;
        }

        if (c.Label.IndexOf("1000 orbits", StringComparison.Ordinal) >= 0)
        {
            localGate = 100d;
            worldGate = 100d;
            refSelfGate = 20d;
        }
        else if (c.Label.IndexOf("HEO", StringComparison.Ordinal) >= 0
            || c.Label.IndexOf("Hyperbolic", StringComparison.Ordinal) >= 0)
        {
            localGate = 1d;
            worldGate = 1d;
        }
        else if (c.Label.IndexOf("world chunk", StringComparison.Ordinal) >= 0)
        {
            localGate = 10d;
            worldGate = 10d;
            refSelfGate = 20d;
        }

        if (c.Thrust)
        {
            worldGate = 5d;
        }

        StudyShipMatrixRow bestLocal = null;
        StudyShipMatrixRow production = null;
        StudyShipMatrixRow fixed600 = null;
        foreach (StudyShipMatrixRow r in rows)
        {
            if (r.Frame == "local" && r.Finite && double.IsFinite(r.Dp)
                && (bestLocal == null || r.Dp < bestLocal.Dp))
            {
                bestLocal = r;
            }

            if (r.Method == "DOP853" && r.Frame == "world"
                && r.Note == "production" && r.Finite)
            {
                production = r;
            }

            if (r.Method == "DOP853" && r.Frame == "world"
                && r.Note == "trial-mass check")
            {
                fixed600 = r;
            }
        }

        Check(refSelf < refSelfGate, "matrix reference convergence " + c.Label,
            refSelf.ToString("E3", CultureInfo.InvariantCulture) + " m");
        Check(production != null && production.Dp < worldGate,
            "matrix production DOP853 " + c.Label,
            production == null ? "missing"
                : production.Dp.ToString("E3", CultureInfo.InvariantCulture) + " m < "
                  + worldGate.ToString("G3", CultureInfo.InvariantCulture) + " m");
        Check(bestLocal != null && bestLocal.Dp < localGate,
            "matrix best local " + c.Label,
            bestLocal == null ? "missing"
                : bestLocal.Method + "/" + bestLocal.Config + " dp="
                  + bestLocal.Dp.ToString("E3", CultureInfo.InvariantCulture) + " m < "
                  + localGate.ToString("G3", CultureInfo.InvariantCulture) + " m");
        Check(bestLocal != null && bestLocal.DEnergy < 1e-6d,
            "matrix best local dE/E " + c.Label,
            bestLocal == null ? "missing"
                : bestLocal.DEnergy.ToString("E2", CultureInfo.InvariantCulture));
        if (c.Thrust)
        {
            Check(fixed600 != null && fixed600.Finite && fixed600.Dp < 10d,
                "matrix thrust 600 s production chunk " + c.Label,
                fixed600 == null ? "missing"
                    : fixed600.Dp.ToString("E3", CultureInfo.InvariantCulture) + " m");
        }
    }

    private static int Test549_SpacecraftOrbitIntegrators()
    {
        Console.WriteLine("--- Spacecraft orbit integrator matrix: all methods, all conditions ---");
        int failuresBefore = failures;

        StarSystem gravity = StudyShipMatrixSystem(false);
        StarSystem perturbed = StudyShipMatrixSystem(true);
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
        cases.Add(StudyShipMatrixCaseOrbit("Earth Molniya 500 x 40000 km",
            gravity, 1, 6.371e6d + 5e5d, 6.371e6d + 4e7d, 63.4d, 0d, 270d, 7d * day, false, true, 1d));
        cases.Add(StudyShipMatrixCaseOrbit("Earth GEO 35786 km",
            gravity, 1, 4.2164e7d, 4.2164e7d, 0d, 0d, 0d, 30d * day, false, true, 1d));
        cases.Add(StudyShipMatrixCaseOrbit("Sun 1 AU circular",
            gravity, 0, StudyAU, StudyAU, 0d, 0d, 0d, year, false, true, 1d));
        cases.Add(StudyShipMatrixCaseOrbit("Sun 0.5 x 2 AU eccentric",
            gravity, 0, 0.5d * StudyAU, 2d * StudyAU, 0d, 0d, 0d, year, false, true, 1d));
        cases.Add(StudyShipMatrixCaseOrbit("Earth 400 km + J2",
            perturbed, 1, 6.371e6d + 4e5d, 6.371e6d + 4e5d, 0d, 0d, 0d, day, true, false, 1d));
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
            heo.ReferenceStepSeconds = 10d;
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

        foreach (StudyShipMatrixCase c in cases)
        {
            var caseWatch = Stopwatch.StartNew();
            StudyShipMatrixRef reference;
            double refSelf;
            string referenceKind;
            try
            {
                reference = StudyShipMatrixIndependentReference(c, out refSelf, out referenceKind);
            }
            catch (Exception ex)
            {
                Console.WriteLine("reference failed for " + c.Label + ": " + StudyShipMatrixNote(ex));
                Check(false, "reference " + c.Label, StudyShipMatrixNote(ex));
                continue;
            }

            StudyShipMatrixRef worldReference = reference;
            Console.WriteLine("reference: " + referenceKind + ", self="
                + refSelf.ToString("E3", CultureInfo.InvariantCulture) + " m");

            double[] rk4 = c.Thrust
                ? new[] { 1d, 2d, 4d }
                : StudyShipMatrixSteps(c.Tau, 128d, c.StepScale);
            double[] y6 = c.Thrust
                ? new[] { 2d, 4d, 8d }
                : StudyShipMatrixSteps(c.Tau, 32d, c.StepScale);
            var rows = new List<StudyShipMatrixRow>();
            StudyShipMatrixRunAll(c, reference, worldReference, rk4, y6, rows);
            StudyShipMatrixPrint(c, reference, refSelf, rows);
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

            StudyShipMatrixGate(c, refSelf, rows);
            caseWatch.Stop();
            Console.WriteLine("case time: " + caseWatch.ElapsedMilliseconds + " ms");
        }

        StudyShipMatrixOrderAndTolerance();
        StudyShipMatrixCsv(allRows, allLabels, csvPath);
        StudyShipMatrixScoreboard(allRows, allLabels);
        Console.WriteLine("failed checks: " + (failures - failuresBefore));
        Console.WriteLine("--- Spacecraft orbit integrator matrix complete ---");
        return 0;
    }

}
