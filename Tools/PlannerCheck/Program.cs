using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Galilego.Core;
using Galilego.Planning;

// Standalone verification harness for the manoeuvre planner's pure math.
// It only *dumps* results; all checks live in an independent Python/mpmath verifier.

internal static class Program
{
    private static readonly StringBuilder Out = new StringBuilder();
    private static ulong _s = 0x9E3779B97F4A7C15UL;

    private static void W(string s) { Out.AppendLine(s); }

    private static double Rnd()
    {
        _s ^= _s << 13; _s ^= _s >> 7; _s ^= _s << 17;
        return (_s >> 11) * (1.0 / 9007199254740992.0);
    }

    private static double Rnd(double lo, double hi) { return lo + ((hi - lo) * Rnd()); }

    private static string D(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }

    private static void V(string tag, params double[] vals)
    {
        var sb = new StringBuilder(tag);
        for (int i = 0; i < vals.Length; i++) { sb.Append(',').Append(D(vals[i])); }
        W(sb.ToString());
    }

    private static void Vv(string tag, Vector3d a, Vector3d b) { V(tag, a.X, a.Y, a.Z, b.X, b.Y, b.Z); }

    private static Vector3d RandomUnit()
    {
        double z = Rnd(-1d, 1d);
        double phi = Rnd(0d, 2d * Math.PI);
        double r = Math.Sqrt(Math.Max(0d, 1d - (z * z)));
        return new Vector3d(r * Math.Cos(phi), r * Math.Sin(phi), z);
    }

    private const double MuSun = 1.32712440018e20;
    private const double Au = 1.495978707e11;

    private static void Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : "out.txt";

        LambertCases();
        KeplerCases();
        EscapeCases();
        FlybyCases();
        ArcCases();
        ConicCases();
        TransferEndToEnd();
        FlybyEndToEnd();
        Diag.Run(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)), "diag.txt"));

        System.IO.File.WriteAllText(path, Out.ToString());
        Console.WriteLine("wrote " + path + " (" + Out.Length + " chars)");
    }

    // ---------------------------------------------------------------- Lambert
    private static void LambertCases()
    {
        // generic random geometry, prograde about a random normal
        for (int i = 0; i < 40; i++)
        {
            double n1 = Rnd(0.4, 5.2) * Au;
            double n2 = Rnd(0.4, 5.2) * Au;
            double ang = Rnd(0.02, Math.PI - 0.02);
            Vector3d nd = RandomUnit();
            Vector3d u1 = RandomUnit();
            while (Math.Abs(Vector3d.Dot(u1, nd)) > 0.999) { u1 = RandomUnit(); }
            u1 = (u1 - (nd * Vector3d.Dot(u1, nd))).Normalized;
            Vector3d u2 = (u1 * Math.Cos(ang)) + (Vector3d.Cross(nd, u1) * Math.Sin(ang));
            Vector3d r1 = u1 * n1, r2 = u2 * n2;
            double tPar = Math.Pow(n1 + n2, 1.5) / Math.Sqrt(MuSun); // ~ parabolic time scale
            double dt = Rnd(0.12, 1.1) * tPar;
            bool ok = LambertLagrange.TrySolve(MuSun, r1, r2, dt, nd, out Vector3d v1, out Vector3d v2);
            W("L," + i + "," + D(MuSun) + "," + D(r1.X) + "," + D(r1.Y) + "," + D(r1.Z) + ","
              + D(r2.X) + "," + D(r2.Y) + "," + D(r2.Z) + "," + D(dt) + ","
              + D(nd.X) + "," + D(nd.Y) + "," + D(nd.Z) + ","
              + (ok ? "1" : "0") + "," + D(v1.X) + "," + D(v1.Y) + "," + D(v1.Z) + ","
              + D(v2.X) + "," + D(v2.Y) + "," + D(v2.Z));
        }

        // near-180 degree arcs (the Hohmann geometry)
        double[] eps = { 1e-10, 1e-8, 1e-6, 1e-4, 1e-3, 1e-2, 0.1, 0.5, 1.0 };
        for (int i = 0; i < eps.Length; i++)
        {
            double n1 = Au, n2 = 1.524 * Au;
            Vector3d r1 = new Vector3d(n1, 0d, 0d);
            double a = 0.5 * (n1 + n2);
            double dt = Math.PI * Math.Sqrt(a * a * a / MuSun);
            // exactly 180 deg: r2 = -n2 x, plus a tiny eps rotation
            Vector3d r2 = new Vector3d(-n2 * Math.Cos(eps[i]), n2 * Math.Sin(eps[i]), 0d);
            bool ok = LambertLagrange.TrySolve(MuSun, r1, r2, dt, new Vector3d(0d, 0d, 1d), out Vector3d v1, out Vector3d v2);
            W("L180," + i + "," + D(MuSun) + "," + D(r1.X) + "," + D(r1.Y) + "," + D(r1.Z) + ","
              + D(r2.X) + "," + D(r2.Y) + "," + D(r2.Z) + "," + D(dt) + ",0,0,1,"
              + (ok ? "1" : "0") + "," + D(v1.X) + "," + D(v1.Y) + "," + D(v1.Z) + ","
              + D(v2.X) + "," + D(v2.Y) + "," + D(v2.Z));
        }

        // hyperbolic (short time) cases
        for (int i = 0; i < 12; i++)
        {
            double n1 = Rnd(0.7, 2.0) * Au, n2 = Rnd(0.7, 4.0) * Au;
            double ang = Rnd(0.3, 2.6);
            Vector3d nd = new Vector3d(0d, 0d, 1d);
            Vector3d u1 = new Vector3d(Math.Cos(Rnd(0, 6)), Math.Sin(Rnd(0, 6)), 0d);
            u1 = (u1 - (nd * Vector3d.Dot(u1, nd))).Normalized;
            Vector3d u2 = (u1 * Math.Cos(ang)) + (Vector3d.Cross(nd, u1) * Math.Sin(ang));
            Vector3d r1 = u1 * n1, r2 = u2 * n2;
            double tPar = Math.Pow(n1 + n2, 1.5) / Math.Sqrt(MuSun);
            double dt = Rnd(0.02, 0.10) * tPar;
            bool ok = LambertLagrange.TrySolve(MuSun, r1, r2, dt, nd, out Vector3d v1, out Vector3d v2);
            W("Lh," + i + "," + D(MuSun) + "," + D(r1.X) + "," + D(r1.Y) + "," + D(r1.Z) + ","
              + D(r2.X) + "," + D(r2.Y) + "," + D(r2.Z) + "," + D(dt) + ","
              + D(nd.X) + "," + D(nd.Y) + "," + D(nd.Z) + ","
              + (ok ? "1" : "0") + "," + D(v1.X) + "," + D(v1.Y) + "," + D(v1.Z) + ","
              + D(v2.X) + "," + D(v2.Y) + "," + D(v2.Z));
        }

        // cross-check against the legacy BMWS solver on generic cases
        for (int i = 0; i < 6; i++)
        {
            double n1 = Rnd(0.8, 3.0) * Au, n2 = Rnd(0.8, 3.0) * Au;
            double ang = Rnd(0.4, 2.7);
            Vector3d r1 = new Vector3d(n1, 0d, 0d);
            Vector3d r2 = new Vector3d(n2 * Math.Cos(ang), n2 * Math.Sin(ang), 0d);
            double tPar = Math.Pow(n1 + n2, 1.5) / Math.Sqrt(MuSun);
            double dt = Rnd(0.25, 0.9) * tPar;
            bool ok1 = LambertLagrange.TrySolve(MuSun, r1, r2, dt, new Vector3d(0d, 0d, 1d), out Vector3d a1, out Vector3d a2);
            LambertSolution ls;
            bool ok2 = true;
            try { ls = LambertSolver.Solve(r1, r2, dt, MuSun, false); }
            catch (Exception) { ls = default; ok2 = false; }
            if (!ls.DepartureVelocity.IsFinite) { ok2 = false; }
            Vector3d b1 = ok2 ? ls.DepartureVelocity : Vector3d.Zero;
            W("Lcmp," + i + "," + (ok1 ? "1" : "0") + "," + (ok2 ? "1" : "0") + ","
              + D(a1.X) + "," + D(a1.Y) + "," + D(a1.Z) + "," + D(b1.X) + "," + D(b1.Y) + "," + D(b1.Z));
        }
    }

    // -------------------------------------------------------- universal Kepler
    private static void KeplerCases()
    {
        int idx = 0;
        for (int family = 0; family < 3; family++)
        {
            for (int i = 0; i < 10; i++)
            {
                double r0 = Rnd(6.4e6, 4.0e11);
                double mu = family == 0 ? MuSun : 3.986004418e14;
                double vc = Math.Sqrt(mu / r0);
                double v = vc * (family == 0 ? Rnd(0.5, 0.95) : family == 1 ? Rnd(0.98, 1.35) : Rnd(1.35, 2.2));
                Vector3d u = RandomUnit();
                Vector3d p = (RandomUnit() - (u * Vector3d.Dot(RandomUnit(), u)));
                Vector3d t = Vector3d.Cross(RandomUnit(), u).Normalized;
                Vector3d v0 = (u * (v * Rnd(-0.6, 0.6))) + (t * Math.Sqrt(Math.Max(1e-6, v * v - (v * Rnd(-0.6, 0.6)) * (v * Rnd(-0.6, 0.6)))));
                v0 = (u * (v * Math.Cos(1.0))) + (t * (v * Math.Sin(1.0)));
                double dt = Rnd(10d, 4e7);
                bool ok = UniversalKepler.TryPropagate(mu, u * r0, v0, dt, out Vector3d r, out Vector3d vv);
                W("K," + idx + "," + D(mu) + "," + D(u.X * r0) + "," + D(u.Y * r0) + "," + D(u.Z * r0) + ","
                  + D(v0.X) + "," + D(v0.Y) + "," + D(v0.Z) + "," + D(dt) + "," + (ok ? "1" : "0") + ","
                  + D(r.X) + "," + D(r.Y) + "," + D(r.Z) + "," + D(vv.X) + "," + D(vv.Y) + "," + D(vv.Z));
                idx++;
            }
        }

        // round trip (forward then back)
        int idx2 = 0;
        for (int i = 0; i < 8; i++)
        {
            double r0 = Rnd(7e6, 3e11), mu = MuSun;
            double v = Math.Sqrt(mu / r0) * Rnd(0.6, 1.3);
            Vector3d u = RandomUnit();
            Vector3d t = Vector3d.Cross(RandomUnit(), u).Normalized;
            Vector3d v0 = t * v;
            double dt = Rnd(1e3, 3e7);
            bool ok = UniversalKepler.TryPropagate(mu, u * r0, v0, dt, out Vector3d r, out Vector3d vv);
            Vector3d rb = Vector3d.Zero, vb = Vector3d.Zero;
            bool ok2 = ok && UniversalKepler.TryPropagate(mu, r, vv, -dt, out rb, out vb);
            W("Krt," + idx2 + "," + D(mu) + "," + D(u.X * r0) + "," + D(u.Y * r0) + "," + D(u.Z * r0) + ","
              + D(v0.X) + "," + D(v0.Y) + "," + D(v0.Z) + "," + D(dt) + "," + (ok2 ? "1" : "0") + ","
              + D(rb.X) + "," + D(rb.Y) + "," + D(rb.Z) + "," + D(vb.X) + "," + D(vb.Y) + "," + D(vb.Z));
            idx2++;
        }
    }

    // ----------------------------------------------------------------- escape
    private static void EscapeCases()
    {
        double mu = 3.986004418e14; // Earth
        for (int i = 0; i < 20; i++)
        {
            double r = Rnd(6.6e6, 4.2e7);
            Vector3d u = RandomUnit();
            Vector3d t = Vector3d.Cross(RandomUnit(), u).Normalized;
            double vc = Math.Sqrt(mu / r);
            Vector3d vShip = (t * vc) + (u * (vc * Rnd(-0.2, 0.2)));
            Vector3d vInf = RandomUnit() * Rnd(500d, 12000d);
            bool ok = EscapeSolver.TrySolve(mu, 6.6e6, u * r, vShip, vInf, out EscapeBurn b);
            W("E," + i + "," + D(mu) + "," + D(r) + "," + D(u.X) + "," + D(u.Y) + "," + D(u.Z) + ","
              + D(vShip.X) + "," + D(vShip.Y) + "," + D(vShip.Z) + ","
              + D(vInf.X) + "," + D(vInf.Y) + "," + D(vInf.Z) + "," + (ok ? "1" : "0") + ","
              + D(b.DvWorld.X) + "," + D(b.DvWorld.Y) + "," + D(b.DvWorld.Z) + ","
              + D(b.VPost.X) + "," + D(b.VPost.Y) + "," + D(b.VPost.Z) + ","
              + D(b.PeriapsisRadius) + "," + D(b.Eccentricity) + "," + (b.PeriapsisAhead ? "1" : "0"));
        }
    }

    // ------------------------------------------------------------------ flyby
    private static void FlybyCases()
    {
        double muJ = 1.26686534e17;
        double kind = 1;
        for (int i = 0; i < 24; i++)
        {
            double vi = Rnd(2000d, 20000d);
            double vo = i % 3 == 0 ? vi : Rnd(2000d, 25000d);
            Vector3d ui = RandomUnit();
            // rotation by an angle around a random axis
            double turn = Rnd(0.02, 1.8);
            Vector3d ax = Vector3d.Cross(ui, RandomUnit()).Normalized;
            if (!ax.IsFinite || ax.SqrMagnitude < 0.5) { ax = new Vector3d(0, 0, 1); }
            Vector3d uo = (ui * Math.Cos(turn)) + (Vector3d.Cross(ax, ui) * Math.Sin(turn));
            FlybyResult f = FlybyGeometry.Evaluate(muJ, 7.2e7, 4.82e10, ui * vi, uo * vo);
            W("F," + i + "," + D(muJ) + "," + D(ui.X * vi) + "," + D(ui.Y * vi) + "," + D(ui.Z * vi) + ","
              + D(uo.X * vo) + "," + D(uo.Y * vo) + "," + D(uo.Z * vo) + ","
              + (f.Feasible ? "1" : "0") + "," + D(f.TurnNeeded) + "," + D(f.TurnMin) + "," + D(f.TurnMax) + ","
              + D(f.PeriapsisRadius) + "," + D(f.PoweredDv) + "," + D(f.DeficitDv) + ","
              + D(f.PeriapsisDir.X) + "," + D(f.PeriapsisDir.Y) + "," + D(f.PeriapsisDir.Z) + ","
              + D(f.MotionDir.X) + "," + D(f.MotionDir.Y) + "," + D(f.MotionDir.Z) + ","
              + D(f.SpeedPeriIn) + "," + D(f.SpeedPeriOut) + "," + D(f.VelocityChange) + "," + D(f.ImpactParameterIn));
            kind++;
        }

        // degenerate / weak-turn cases (regression for the SOI wall and the zero axis)
        {
            double soi = 4.82e10;
            for (int i = 0; i < 4; i++)
            {
                double vi = 2500d + (i * 1200d);
                Vector3d ui = new Vector3d(1d, 0d, 0d);
                double turn = i == 3 ? 0d : 0.02d + (i * 0.03d);
                Vector3d uo = turn == 0d
                    ? ui
                    : (ui * Math.Cos(turn)) + (Vector3d.Cross(new Vector3d(0d, 0d, 1d), ui) * Math.Sin(turn));
                FlybyResult f = FlybyGeometry.Evaluate(muJ, 7.2e7, soi, ui * vi, uo * vi);
                W("Fdeg," + i + "," + D(muJ) + "," + D(ui.X * vi) + "," + D(ui.Y * vi) + "," + D(ui.Z * vi) + ","
                  + D(uo.X * vi) + "," + D(uo.Y * vi) + "," + D(uo.Z * vi) + ","
                  + (f.Feasible ? "1" : "0") + "," + D(f.TurnNeeded) + "," + D(f.TurnMin) + "," + D(f.TurnMax) + ","
                  + D(f.PeriapsisRadius) + "," + D(f.PoweredDv) + "," + D(f.DeficitDv) + ","
                  + D(f.PeriapsisDir.X) + "," + D(f.PeriapsisDir.Y) + "," + D(f.PeriapsisDir.Z) + ","
                  + D(f.MotionDir.X) + "," + D(f.MotionDir.Y) + "," + D(f.MotionDir.Z) + ","
                  + D(f.SpeedPeriIn) + "," + D(f.SpeedPeriOut) + "," + D(f.VelocityChange) + "," + D(f.ImpactParameterIn));
            }
        }
    }

    // ------------------------------------------------------------ arc min radius
    private static void ArcCases()
    {
        for (int i = 0; i < 30; i++)
        {
            double n1 = Rnd(0.3, 5) * Au, n2 = Rnd(0.3, 5) * Au;
            double ang = Rnd(0.05, Math.PI - 0.05);
            Vector3d r1 = new Vector3d(n1, 0d, 0d);
            Vector3d r2 = new Vector3d(n2 * Math.Cos(ang), n2 * Math.Sin(ang), 0d);
            double tPar = Math.Pow(n1 + n2, 1.5) / Math.Sqrt(MuSun);
            double dt = Rnd(0.2, 1.2) * tPar;
            if (!LambertLagrange.TrySolve(MuSun, r1, r2, dt, new Vector3d(0, 0, 1), out Vector3d v1, out _)) { continue; }
            double rmin = TransferScanner.ArcMinRadius(MuSun, r1, v1, r2);
            W("A," + i + "," + D(MuSun) + "," + D(r1.X) + "," + D(r1.Y) + "," + D(r1.Z) + ","
              + D(v1.X) + "," + D(v1.Y) + "," + D(v1.Z) + "," + D(r2.X) + "," + D(r2.Y) + "," + D(r2.Z) + ","
              + D(dt) + "," + D(rmin));
        }
    }

    // ---------------------------------------------------------------- conic math
    private static void ConicCases()
    {
        double[] dv = { 0d, 300d, 1500d, 4000d, 9000d };
        for (int i = 0; i < dv.Length; i++)
        {
            W("Cdv," + i + "," + D(ConicMath.EscapeDvCircular(3.986004418e14, 6.7e6, dv[i])) + ","
              + D(ConicMath.CaptureDvCircular(3.986004418e14, 6.7e6, dv[i])));
        }
        for (int i = 0; i < 10; i++)
        {
            double rp = Rnd(7e7, 4e10), vinf = Rnd(1000, 20000);
            double mu = 1.26686534e17;
            double e = ConicMath.FlybyEccentricity(mu, rp, vinf);
            double turn = ConicMath.FlybyTurn(mu, rp, vinf);
            W("Cfly," + i + "," + D(rp) + "," + D(vinf) + "," + D(e) + "," + D(turn) + ","
              + D(ConicMath.FlybyVelocityChange(mu, rp, vinf)));
        }
    }

    // --------------------------------------------------------------- end to end
    private sealed class SynthWorld : IPlannerWorld
    {
        public double MuSun = 1.32712440018e20;
        public int N = 5;
        public double[] Rb, MuB, RadB, SoiB, SafeB, Theta0, NMean;
        public double ShipR, ShipPhase, ShipIncl;
        public double Now;

        public SynthWorld()
        {
            Rb = new double[N]; MuB = new double[N]; RadB = new double[N]; SoiB = new double[N];
            SafeB = new double[N]; Theta0 = new double[N]; NMean = new double[N];
            MuB[0] = MuSun; RadB[0] = 6.957e8; Rb[0] = 0; NMean[0] = 0;
            SafeB[0] = 0; SoiB[0] = double.PositiveInfinity;
        }

        public void AddBody(int i, double r, double mu, double radius, double soi, double safe, double theta)
        {
            Rb[i] = r; MuB[i] = mu; RadB[i] = radius; SoiB[i] = soi; SafeB[i] = safe;
            Theta0[i] = theta; NMean[i] = Math.Sqrt(MuSun / (r * r * r));
        }

        public double UniverseNow { get { return Now; } }
        public int BodyCount { get { return N; } }
        public string BodyName(int i) { return "B" + i; }
        public int ParentOf(int i) { return i == 0 ? -1 : 0; }
        public double Mu(int i) { return MuB[i]; }
        public double Radius(int i) { return RadB[i]; }
        public double SoiRadius(int i) { return SoiB[i]; }
        public double SafeAltitude(int i) { return SafeB[i]; }

        public bool TryBodyState(int i, double t, out Vector3d p, out Vector3d v)
        {
            if (i == 0) { p = Vector3d.Zero; v = Vector3d.Zero; return true; }
            double th = Theta0[i] + (NMean[i] * t);
            double c = Math.Cos(th), s = Math.Sin(th);
            double sp = Math.Sqrt(MuSun / Rb[i]);
            p = new Vector3d(Rb[i] * c, Rb[i] * s, 0d);
            v = new Vector3d(-sp * s, sp * c, 0d);
            return true;
        }

        public bool TryShipState(out Vector3d p, out Vector3d v)
        {
            TryBodyState(1, Now, out Vector3d bp, out Vector3d bv);
            double th = ShipPhase + (Math.Sqrt(MuB[1] / (ShipR * ShipR * ShipR)) * Now);
            double c = Math.Cos(th), s = Math.Sin(th);
            double sp = Math.Sqrt(MuB[1] / ShipR);
            // incline about the x axis so the orbital normal differs from z
            var r = new Vector3d(ShipR * c, ShipR * s * Math.Cos(ShipIncl), ShipR * s * Math.Sin(ShipIncl));
            var vv = new Vector3d(-sp * s, sp * c * Math.Cos(ShipIncl), sp * c * Math.Sin(ShipIncl));
            p = bp + r; v = bv + vv;
            return true;
        }

        public int ShipBody() { return 1; }
    }

    private static void TransferEndToEnd()
    {
        TransferScenario("Tend", CaptureKind.Circular, true, 0d, 0d, 300e3, 400e3);
        TransferScenario("Tnone", CaptureKind.None, true, 0d, 0d, 300e3, 400e3);
        TransferScenario("Tprep", CaptureKind.Circular, false, 700e3, 0d, 300e3, 400e3);
        TransferScenario("Tincl", CaptureKind.Circular, true, 0d, 30d, 300e3, 400e3);
    }

    private static void TransferScenario(string tag, CaptureKind cap, bool useCurrent, double parkAlt, double inclDeg,
        double shipAlt, double arrAlt)
    {
        var w = new SynthWorld();
        double r1 = Au, r2 = 1.524 * Au;
        w.AddBody(1, r1, 3.986004418e14, 6.371e6, 9.24e8, 200e3, 0.0);
        w.AddBody(2, r2, 4.282837e13, 3.3895e6, 5.77e8, 200e3, 1.2);
        w.Now = 0d;
        w.ShipR = 6.371e6 + shipAlt;
        w.ShipPhase = 0.3;
        w.ShipIncl = inclDeg * Math.PI / 180d;

        var rq = new TransferRequest
        {
            From = 1, To = 2, UseCurrentOrbit = useCurrent, ParkAltitudeM = parkAlt,
            Capture = cap, ArrivalAltitudeM = arrAlt,
            LeadTimeS = 300d, ShortHorizonS = 3d * 86400d,
        };

        if (!PlannerEngine.TryBuildSpec(w, rq, w.Now, w.ShipR, out TransferSpec spec, out string why))
        {
            W(tag + ",specfail," + why.Replace(',', ' '));
            return;
        }

        PlannerEngine.DefaultWindow(w, rq, w.Now, out double start, out double span, out double tofMin, out double tofMax);
        W(tag + ",win," + D(start) + "," + D(span) + "," + D(tofMin) + "," + D(tofMax));

        List<TransferSolution> wins = PlannerEngine.FindWindows(spec, start, span, tofMin, tofMax, 120, out PorkchopGrid grid);
        W(tag + ",nwin," + wins.Count);
        if (wins.Count == 0) { return; }
        for (int i = 0; i < Math.Min(3, wins.Count); i++)
        {
            TransferSolution s = wins[i];
            W(tag + ",w" + i + "," + D(s.T0) + "," + D(s.Tof) + "," + D(s.Total) + "," + D(s.DepDv) + ","
              + D(s.ArrDv) + "," + D(s.VInfDep.Magnitude) + "," + D(s.VInfArr.Magnitude) + ","
              + D(s.VTransferDep.X) + "," + D(s.VTransferDep.Y) + "," + D(s.VTransferDep.Z) + ","
              + D(s.VTransferArr.X) + "," + D(s.VTransferArr.Y) + "," + D(s.VTransferArr.Z) + ","
              + D(s.RFrom.X) + "," + D(s.RFrom.Y) + "," + D(s.RFrom.Z) + ","
              + D(s.RTo.X) + "," + D(s.RTo.Y) + "," + D(s.RTo.Z));
        }

        double muA = w.Mu(1), minPer = w.Radius(1) + w.SafeAltitude(1);
        if (!w.TryShipState(out Vector3d sp, out Vector3d sv) || !w.TryBodyState(1, w.Now, out Vector3d bp, out Vector3d bv)) { return; }
        Vector3d rRel = sp - bp, vRel = sv - bv;
        PlanDraft d = PlannerEngine.MakeTransferDraft(w, rq, spec, wins[0], null);
        W(tag + ",draft," + (d.Valid ? "1" : "0") + "," + D(d.IdealDv) + "," + D(d.PatchedDv) + "," + D(d.RefinedDv)
          + "," + D(d.DepartTime) + "," + D(d.ArriveTime) + "," + (d.Preliminary ? "1" : "0") + ","
          + D(d.PlanePenaltyDv) + "," + d.Note.Replace(',', ' '));
        foreach (PlannedImpulse imp in d.Impulses)
        {
            W(tag + ",imp," + (int)imp.Role + "," + D(imp.Time) + "," + D(imp.DvWorld.Magnitude) + ","
              + D(imp.RelPos.Magnitude) + "," + D(imp.RelVel.Magnitude) + "," + (imp.FrameBody) + "," + imp.Label.Replace(',', ' '));
        }

        // search-resolution sensitivity: 600 samples (default) vs a dense scan
        double periodShip = 2d * Math.PI * Math.Sqrt(Math.Pow(1d / Math.Abs((2d / rRel.Magnitude) - (vRel.SqrMagnitude / muA)), 3d) / muA);
        double fromS = Math.Max(w.Now + rq.LeadTimeS, wins[0].T0 - periodShip);
        double toS = Math.Max(fromS + periodShip, wins[0].T0 + periodShip);
        IShipTrack shipS = new KeplerShipTrack(muA, w.Now, rRel, vRel);
        bool ok600 = ShipDeparture.Find(spec, muA, minPer, shipS, wins[0].T1, fromS, toS, 600, out DeparturePlan d600);
        bool ok8k = ShipDeparture.Find(spec, muA, minPer, shipS, wins[0].T1, fromS, toS, 8000, out DeparturePlan d8k);
        W(tag + ",search600," + (ok600 ? "1" : "0") + "," + D(d600.Burn.Dv + d600.ArrivalDv) + "," + D(d600.BurnTime));
        W(tag + ",search8k," + (ok8k ? "1" : "0") + "," + D(d8k.Burn.Dv + d8k.ArrivalDv) + "," + D(d8k.BurnTime));

        // brute-force the (t0, tof) plane with the verified Lambert and compare
        // the global minimum with what FindWindows returned
        double best = double.PositiveInfinity, bt0 = 0, btof = 0;
        const int N0 = 400, N1 = 300;
        for (int i = 0; i < N0; i++)
        {
            double t0b = start + (span * i / (N0 - 1d));
            for (int j = 0; j < N1; j++)
            {
                double tofb = tofMin + ((tofMax - tofMin) * j / (N1 - 1d));
                if (TransferScanner.Evaluate(spec, t0b, t0b + tofb, out TransferSolution sb) && sb.Total < best)
                {
                    best = sb.Total; bt0 = t0b; btof = tofb;
                }
            }
        }
        W(tag + ",brute," + D(best) + "," + D(bt0) + "," + D(btof) + "," + D(wins[0].Total) + ","
          + D(best - wins[0].Total));
    }

    private static void FlybyEndToEnd()
    {
        var w = new SynthWorld();
        w.AddBody(1, 1.0 * Au, 3.986004418e14, 6.371e6, 9.24e8, 200e3, 0.0);
        w.AddBody(2, 5.203 * Au, 1.26686534e17, 6.9911e7, 4.82e10, 0d, 2.1);
        w.AddBody(3, 9.537 * Au, 3.7931187e16, 5.8232e7, 5.46e10, 0d, 4.0);
        w.Now = 0d;
        w.ShipR = 6.371e6 + 300e3;
        w.ShipPhase = 0.3;
        w.ShipIncl = 0d;

        var rq = new FlybyRequest
        {
            From = 1, Via = 2, To = 3, Capture = CaptureKind.Circular, ArrivalAltitudeM = 400e3,
            LeadTimeS = 300d, ShortHorizonS = 3d * 86400d,
        };

        if (!FlybyPlanner.TryBuildSpec(w, rq, w.Now, w.ShipR, out FlybySpec spec, out string why))
        {
            W("Fend,specfail," + why.Replace(',', ' '));
            return;
        }

        FlybyPlanner.DefaultWindows(w, rq, w.Now, out double a0, out double a1, out double b0, out double b1, out double c0, out double c1);
        W("Fend,win," + D(a0) + "," + D(a1) + "," + D(b0) + "," + D(b1) + "," + D(c0) + "," + D(c1));

        List<FlybySolution> wins = FlybyPlanner.FindWindows(spec, a0, a1, b0, b1, c0, c1);
        W("Fend,nwin," + wins.Count);
        for (int i = 0; i < Math.Min(4, wins.Count); i++)
        {
            FlybySolution s = wins[i];
            W("Fend,w" + i + "," + D(s.T0) + "," + D(s.T1) + "," + D(s.T2) + "," + D(s.Total) + ","
              + D(s.DepDv) + "," + D(s.Flyby.PoweredDv) + "," + D(s.Flyby.DeficitDv) + "," + D(s.ArrDv) + ","
              + (s.Feasible ? "1" : "0") + "," + D(s.Flyby.PeriapsisRadius) + ","
              + D(s.Flyby.TurnNeeded) + "," + D(s.Flyby.TurnMax) + "," + D(s.Flyby.TurnMin) + ","
              + D(s.VInfIn.X) + "," + D(s.VInfIn.Y) + "," + D(s.VInfIn.Z) + ","
              + D(s.VInfOut.X) + "," + D(s.VInfOut.Y) + "," + D(s.VInfOut.Z) + ","
              + D(s.VLeg1Dep.X) + "," + D(s.VLeg1Dep.Y) + "," + D(s.VLeg1Dep.Z) + ","
              + D(s.RFrom.X) + "," + D(s.RFrom.Y) + "," + D(s.RFrom.Z) + ","
              + D(s.RVia.X) + "," + D(s.RVia.Y) + "," + D(s.RVia.Z) + ","
              + D(s.RTo.X) + "," + D(s.RTo.Y) + "," + D(s.RTo.Z) + ","
              + D(s.VLeg2Dep.X) + "," + D(s.VLeg2Dep.Y) + "," + D(s.VLeg2Dep.Z) + ","
              + D(s.HelioEnergyGain) + "," + D(s.FreeVelocityChange));
        }

        if (wins.Count == 0) { return; }

        // ---- knife-edge diagnostic: rebuild the actual departure arc and re-evaluate the flyby
        var leg1 = new TransferSpec
        {
            MuCentral = spec.MuCentral, CentralRadius = 0d, MinCentralRadius = spec.MinCentralRadius,
            From = spec.From, To = spec.Via, Normal = spec.Normal, Prograde = true,
            MuFrom = spec.MuFrom, ParkRadiusFrom = spec.ParkRadiusFrom, MuTo = spec.MuVia,
            ParkRadiusTo = spec.ViaMinPeriapsis, Capture = CaptureKind.None,
        };
        w.TryShipState(out Vector3d sp, out Vector3d sv);
        w.TryBodyState(1, w.Now, out Vector3d bp, out Vector3d bv);
        Vector3d rRel = sp - bp, vRel = sv - bv;
        double muA = w.Mu(1), minPer = w.Radius(1) + w.SafeAltitude(1);
        FlybySolution win = wins[0];
        double period = 2d * Math.PI * Math.Sqrt(Math.Pow(1d / Math.Abs((2d / rRel.Magnitude) - (vRel.SqrMagnitude / muA)), 3d) / muA);
        IShipTrack ship = new KeplerShipTrack(muA, w.Now, rRel, vRel);
        double from = Math.Max(w.Now + rq.LeadTimeS, win.T0 - period);
        if (ShipDeparture.Find(leg1, muA, minPer, ship, win.T1, from, Math.Max(from + period, win.T0 + period), 600, out DeparturePlan dep))
        {
            spec.Via.TryEvaluate(win.T1, out _, out Vector3d vG);
            Vector3d vInfAct = dep.Transfer.VTransferArr - vG;
            FlybyResult flAct = FlybyGeometry.Evaluate(spec.MuVia, spec.ViaMinPeriapsis, spec.ViaSoiRadius, vInfAct, win.VInfOut);
            FlybyResult flIdeal = FlybyGeometry.Evaluate(spec.MuVia, spec.ViaMinPeriapsis, spec.ViaSoiRadius, win.VInfIn, win.VInfOut);
            W("Fend,knife,actual:need=" + D(flAct.TurnNeeded) + ",max=" + D(flAct.TurnMax) + ",rp=" + D(flAct.PeriapsisRadius)
              + ",deficit=" + D(flAct.DeficitDv) + ",feas=" + (flAct.Feasible ? 1 : 0)
              + ",dvDep=" + D(dep.Burn.Dv) + ",dvArr=" + D(dep.ArrivalDv) + ",burn=" + D(dep.BurnTime) + ",t0=" + D(win.T0));
            W("Fend,knife,ideal:need=" + D(flIdeal.TurnNeeded) + ",max=" + D(flIdeal.TurnMax) + ",rp=" + D(flIdeal.PeriapsisRadius)
              + ",deficit=" + D(flIdeal.DeficitDv) + ",feas=" + (flIdeal.Feasible ? 1 : 0));
            W("Fend,knife,angles_deg:need=" + D(flAct.TurnNeeded * 180d / Math.PI) + ",max=" + D(flAct.TurnMax * 180d / Math.PI)
              + ",excess=" + D((flAct.TurnNeeded - flAct.TurnMax) * 180d / Math.PI));
        }

        PlanDraft d = FlybyPlanner.MakeDraft(w, rq, spec, wins[0], null);
        W("Fend,draft," + (d.Valid ? "1" : "0") + "," + D(d.IdealDv) + "," + D(d.PatchedDv) + "," + D(d.RefinedDv)
          + "," + D(d.DepartTime) + "," + D(d.ArriveTime) + "," + D(d.FlybyPeriapsisRadius) + "," + d.Note.Replace(',', ' '));
        foreach (PlannedImpulse imp in d.Impulses)
        {
            W("Fend,imp," + (int)imp.Role + "," + D(imp.Time) + "," + D(imp.DvWorld.Magnitude) + ","
              + D(imp.RelPos.Magnitude) + "," + (imp.FrameBody) + "," + imp.Label.Replace(',', ' '));
        }

        // an interior (non-boundary) window: the draft should build, and its cost
        // accounting can be compared with the actual impulse list
        if (wins.Count > 3)
        {
            FlybySolution wi = wins[3];
            PlanDraft di = FlybyPlanner.MakeDraft(w, rq, spec, wi, null);
            W("Fend,draft3," + (di.Valid ? "1" : "0") + "," + D(di.IdealDv) + "," + D(di.PatchedDv) + "," + D(di.RefinedDv)
              + "," + D(di.FlybyPeriapsisRadius) + "," + D(wi.Flyby.PoweredDv) + "," + D(wi.Flyby.DeficitDv) + ","
              + D(di.TotalDv) + "," + D(di.DepartTime) + "," + D(di.ArriveTime) + "," + di.Note.Replace(',', ' '));
            foreach (PlannedImpulse imp in di.Impulses)
            {
                W("Fend,imp3," + (int)imp.Role + "," + D(imp.Time) + "," + D(imp.DvWorld.Magnitude) + ","
                  + D(imp.RelPos.Magnitude) + "," + (imp.FrameBody) + "," + imp.Label.Replace(',', ' '));
            }
        }
    }
}





