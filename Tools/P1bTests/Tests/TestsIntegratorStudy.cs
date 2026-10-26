using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Galilego.Core;
using Galilego.Events;
using Galilego.Spacecraft;
using Galilego.Universe;

internal static partial class P1bTests
{
    // =====================================================================
    // Integrator study harness.
    //
    // The numbers in the report under test are reproduced on this machine:
    //   - the 7-body real-solar fixture (IC7) from the report;
    //   - the project's custom system (Sol, Terra, Luna, Vesper, Jove, Ares);
    //   - the real game path: EphemerisBaker -> SpacecraftPhysics ->
    //     EventDrivenPropagator/LongWarpDriver, not a private copy;
    //   - independent Y6C + Neumaier references with an h/2 convergence gate.
    //
    // Nothing here changes the game. The test is a measurement, not a gate:
    // it prints tables and only fails if the reference itself does not converge.
    // =====================================================================

    private const double StudyYear = 365.25d * 86400d;
    private const double StudyAU = 1.495978707e11d;
    // The report's ship runs start two days after the bake epoch: the Y6 composition
    // contains negative sub-step weights, so the force must never see t < 0.
    private const double StudyShipT0 = 172800d;

    private sealed class StudyNBody
    {
        public string[] Names;
        public double[] Mu;
        public double[] P0;
        public double[] V0;

        public int Count => Names.Length;
    }

    private sealed class StudyRefResult
    {
        public double[] P;
        public double[] V;
        public List<StudySample> Samples;
        public long Calls;
        public double Ms;
    }

    private readonly struct StudySample
    {
        public readonly double T;
        public readonly double[] P;

        public StudySample(double t, double[] p)
        {
            T = t;
            P = p;
        }
    }

    // ------------------------------------------------------------------
    // Fixtures
    // ------------------------------------------------------------------

    private static BodyBlueprint StudyBody(
        string name, int parent, double mu, double radius, double a, double e,
        double incDeg, double nodeDeg, double argpDeg, double meanDeg,
        double northY = 0d, double northZ = 1d)
    {
        return new BodyBlueprint
        {
            Name = name,
            ParentIndex = parent,
            StandardGravitationalParameter = mu,
            Radius = radius,
            SemiMajorAxis = a,
            Eccentricity = e,
            InclinationDegrees = incDeg,
            LongitudeOfAscendingNodeDegrees = nodeDeg,
            ArgumentOfPeriapsisDegrees = argpDeg,
            MeanAnomalyAtEpochDegrees = meanDeg,
            EpochTimeSeconds = 0d,
            NorthPoleY = northY,
            NorthPoleZ = northZ
        };
    }

    /// <summary>
    /// The user's system:
    ///   Sol (current scene), Terra (current scene) + Luna (~0.28 R_H),
    ///   Vesper (1.3 M_Terra at 0.7 a_Terra), Jove (0.4 M_Jupiter at 1.4 AU),
    ///   Ares (radius scaled like Terra from Mars, surface gravity = Mars).
    /// </summary>
    private static StarSystem StudyCustomSystem()
    {
        double terraMu = 1.28e13d;
        double terraR = 1.143e6d;
        double terraA = 5.51876e10d;

        // Ares: R = (R_Terra / R_Earth) * R_Mars; g = g_Mars -> M = g R^2 / G.
        double radiusScale = terraR / 6.371e6d;
        double aresR = radiusScale * 3.3895e6d;
        double aresG = 3.71d;
        // mu = g R^2 (not g R^2 / G: that is the mass, not the gravitational parameter).
        double aresMu = aresG * aresR * aresR;

        var bp = new SystemBlueprint();
        var sol = StudyBody("Sol", -1, 2.65433e19d, 2.345e8d, 0d, 0d, 0d, 0d, 0d, 0d);
        bp.Bodies.Add(sol);
        bp.Bodies.Add(StudyBody("Terra", 0, terraMu, terraR, terraA, 0.017d, 0d, 0d, 0d, 0d));
        bp.Bodies.Add(StudyBody("Luna", 1, 1.5e11d, 3.1e5d, 0.85e8d, 0d, 5d, 0d, 0d, 0d));
        bp.Bodies.Add(StudyBody("Vesper", 0, 1.664e13d, 1.247e6d, 0.7d * terraA, 0.01d, 2d, 40d, 0d, 60d));
        bp.Bodies.Add(StudyBody("Jove", 0, 5.06746e16d, 5.267e7d, 1.4d * StudyAU, 0.02d, 1.5d, 120d, 0d, 150d));
        bp.Bodies.Add(StudyBody("Ares", 0, aresMu, aresR, 2.2d * StudyAU, 0.05d, 1d, 200d, 30d, 250d));

        bp.Bodies[1].Atmosphere = new AtmosphereProfile
        {
            TopAltitudeMeters = 2e6d,
            SeaLevelDensityKgPerCubicMeter = 1.2d,
            ScaleHeightMeters = 8500d
        };
        return bp.Build();
    }

    /// <summary>
    /// The report's 7-body real-solar fixture (common.py / ic7.cs).
    /// </summary>
    private static StarSystem StudyRealSolar7()
    {
        var bp = new SystemBlueprint();
        bp.Bodies.Add(StudyBody("Sun", -1, 1.32712440018e20d, 6.96e8d, 0d, 0d, 0d, 0d, 0d, 0d));
        bp.Bodies.Add(StudyBody("Earth", 0, 3.986004418e14d, 6.371e6d, 1.495978707e11d, 0.0167d, 0d, 0d, 102.9d, 100d));
        bp.Bodies.Add(StudyBody("Moon", 1, 4.9028e12d, 1.737e6d, 3.844e8d, 0.0549d, 5.145d, 125d, 318d, 135d));
        bp.Bodies.Add(StudyBody("Jupiter", 0, 1.26686534e17d, 6.9911e7d, 7.785e11d, 0.0489d, 1.303d, 100.5d, 273.9d, 20d));
        bp.Bodies.Add(StudyBody("Saturn", 0, 3.7931187e16d, 5.8232e7d, 1.4335e12d, 0.0565d, 2.485d, 113.7d, 339.4d, 317d));
        bp.Bodies.Add(StudyBody("Venus", 0, 3.24859e14d, 6.0518e6d, 1.0821e11d, 0.0068d, 3.39d, 76.7d, 54.9d, 50d));
        bp.Bodies.Add(StudyBody("Mars", 0, 4.282837e13d, 3.3895e6d, 2.2794e11d, 0.0934d, 1.85d, 49.6d, 286.5d, 19.4d));
        return bp.Build();
    }

    private static StudyNBody StudyMakeNBody(StarSystem sys)
    {
        int n = sys.AllBodies.Count;
        var nb = new StudyNBody
        {
            Names = new string[n],
            Mu = new double[n],
            P0 = new double[3 * n],
            V0 = new double[3 * n]
        };

        var index = new Dictionary<OrbitingBody, int>(n);
        for (int i = 0; i < n; i++)
        {
            OrbitingBody body = sys.AllBodies[i];
            index[body] = i;
            nb.Names[i] = body.Name;
            nb.Mu[i] = body.ResolveStandardGravitationalParameter();
            sys.EvaluateBodyState(body, 0d, out Vector3d p, out Vector3d v);
            nb.P0[3 * i] = p.X;
            nb.P0[(3 * i) + 1] = p.Y;
            nb.P0[(3 * i) + 2] = p.Z;
            nb.V0[3 * i] = v.X;
            nb.V0[(3 * i) + 1] = v.Y;
            nb.V0[(3 * i) + 2] = v.Z;
        }

        // Same hierarchy correction as EphemerisBaker.RunBake: the rails give each
        // child's subtree barycentre about the parent; the n-body must start from
        // the SAME rigidly shifted states, otherwise the Earth/Moon-phase error is
        // mistaken for an integrator error.
        double[] railP = (double[])nb.P0.Clone();
        double[] railV = (double[])nb.V0.Clone();
        for (int i = n - 1; i >= 0; i--)
        {
            OrbitingBody body = sys.AllBodies[i];
            if (body.Children.Count == 0 || nb.Mu[i] <= 0d)
            {
                continue;
            }

            double mSub = nb.Mu[i];
            double sx = 0d, sy = 0d, sz = 0d;
            double svx = 0d, svy = 0d, svz = 0d;
            for (int j = 0; j < body.Children.Count; j++)
            {
                OrbitingBody child = body.Children[j];
                int c = index[child];
                double mc = child.ResolveSubtreeStandardGravitationalParameter();
                mSub += mc;
                sx += mc * (railP[3 * c] - railP[3 * i]);
                sy += mc * (railP[(3 * c) + 1] - railP[(3 * i) + 1]);
                sz += mc * (railP[(3 * c) + 2] - railP[(3 * i) + 2]);
                svx += mc * (railV[3 * c] - railV[3 * i]);
                svy += mc * (railV[(3 * c) + 1] - railV[(3 * i) + 1]);
                svz += mc * (railV[(3 * c) + 2] - railV[(3 * i) + 2]);
            }

            StudyShiftSubtree(
                body, index, nb.P0, nb.V0,
                -sx / mSub, -sy / mSub, -sz / mSub,
                -svx / mSub, -svy / mSub, -svz / mSub);
        }

        double totalMass = 0d;
        double cpx = 0d, cpy = 0d, cpz = 0d;
        double cvx = 0d, cvy = 0d, cvz = 0d;
        for (int i = 0; i < n; i++)
        {
            totalMass += nb.Mu[i];
            cpx += nb.Mu[i] * nb.P0[3 * i];
            cpy += nb.Mu[i] * nb.P0[(3 * i) + 1];
            cpz += nb.Mu[i] * nb.P0[(3 * i) + 2];
            cvx += nb.Mu[i] * nb.V0[3 * i];
            cvy += nb.Mu[i] * nb.V0[(3 * i) + 1];
            cvz += nb.Mu[i] * nb.V0[(3 * i) + 2];
        }

        cpx /= totalMass;
        cpy /= totalMass;
        cpz /= totalMass;
        cvx /= totalMass;
        cvy /= totalMass;
        cvz /= totalMass;
        for (int i = 0; i < n; i++)
        {
            nb.P0[3 * i] -= cpx;
            nb.P0[(3 * i) + 1] -= cpy;
            nb.P0[(3 * i) + 2] -= cpz;
            nb.V0[3 * i] -= cvx;
            nb.V0[(3 * i) + 1] -= cvy;
            nb.V0[(3 * i) + 2] -= cvz;
        }

        return nb;
    }

    private static void StudyShiftSubtree(
        OrbitingBody body, Dictionary<OrbitingBody, int> index, double[] p, double[] v,
        double dx, double dy, double dz, double dvx, double dvy, double dvz)
    {
        int i = index[body];
        p[3 * i] += dx;
        p[(3 * i) + 1] += dy;
        p[(3 * i) + 2] += dz;
        v[3 * i] += dvx;
        v[(3 * i) + 1] += dvy;
        v[(3 * i) + 2] += dvz;
        for (int j = 0; j < body.Children.Count; j++)
        {
            StudyShiftSubtree(body.Children[j], index, p, v, dx, dy, dz, dvx, dvy, dvz);
        }
    }

    // ------------------------------------------------------------------
    // Independent n-body reference (same method family as the report)
    // ------------------------------------------------------------------

    private static void StudyAcc(double[] p, double[] a, double[] mu)
    {
        int n = mu.Length;
        for (int i = 0; i < 3 * n; i++)
        {
            a[i] = 0d;
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                double dx = p[3 * j] - p[3 * i];
                double dy = p[(3 * j) + 1] - p[(3 * i) + 1];
                double dz = p[(3 * j) + 2] - p[(3 * i) + 2];
                double r2 = (dx * dx) + (dy * dy) + (dz * dz);
                double inv = 1d / (r2 * Math.Sqrt(r2));
                double fi = mu[j] * inv;
                double fj = mu[i] * inv;
                a[3 * i] += fi * dx;
                a[(3 * i) + 1] += fi * dy;
                a[(3 * i) + 2] += fi * dz;
                a[3 * j] -= fj * dx;
                a[(3 * j) + 1] -= fj * dy;
                a[(3 * j) + 2] -= fj * dz;
            }
        }
    }

    private static void StudyAccC(double[] p, double[] pl, double[] a, double[] mu)
    {
        int n = mu.Length;
        for (int i = 0; i < 3 * n; i++)
        {
            a[i] = 0d;
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                double dx = (p[3 * j] - p[3 * i]) + (pl[3 * j] - pl[3 * i]);
                double dy = (p[(3 * j) + 1] - p[(3 * i) + 1]) + (pl[(3 * j) + 1] - pl[(3 * i) + 1]);
                double dz = (p[(3 * j) + 2] - p[(3 * i) + 2]) + (pl[(3 * j) + 2] - pl[(3 * i) + 2]);
                double r2 = (dx * dx) + (dy * dy) + (dz * dz);
                double inv = 1d / (r2 * Math.Sqrt(r2));
                double fi = mu[j] * inv;
                double fj = mu[i] * inv;
                a[3 * i] += fi * dx;
                a[(3 * i) + 1] += fi * dy;
                a[(3 * i) + 2] += fi * dz;
                a[3 * j] -= fj * dx;
                a[(3 * j) + 1] -= fj * dy;
                a[(3 * j) + 2] -= fj * dz;
            }
        }
    }

    private static void StudyAdd(ref double hi, ref double lo, double inc)
    {
        double t = hi + inc;
        if (Math.Abs(hi) >= Math.Abs(inc))
        {
            lo += (hi - t) + inc;
        }
        else
        {
            lo += (inc - t) + hi;
        }

        hi = t;
    }

    private static readonly double StudyY1 = 0.78451361047755726382d;
    private static readonly double StudyY2 = 0.23557321335935813368d;
    private static readonly double StudyY3 = -1.17767998417887100695d;

    private static double[] StudyY6Weights()
    {
        double w0 = 1d - 2d * (StudyY1 + StudyY2 + StudyY3);
        return new[] { StudyY1, StudyY2, StudyY3, w0, StudyY3, StudyY2, StudyY1 };
    }

    // DOP853 (Dormand-Prince 8(5,3)), Hairer/Wanner tableau via SciPy.
    private static readonly double[][] StudyDop853A =
    {
        new double[12],
        new[] { 0.526001519587677318785587544488e-1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
        new[] { 1.97250569845378994544595329183e-2, 5.91751709536136983633785987549e-2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
        new[] { 2.95875854768068491816892993775e-2, 0, 8.87627564304205475450678981324e-2, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
        new[] { 2.41365134159266685502369798665e-1, 0, -8.84549479328286085344864962717e-1, 9.24834003261792003115737966543e-1, 0, 0, 0, 0, 0, 0, 0, 0 },
        new[] { 3.7037037037037037037037037037e-2, 0, 0, 1.70828608729473871279604482173e-1, 1.25467687566822425016691814123e-1, 0, 0, 0, 0, 0, 0, 0 },
        new[] { 3.7109375e-2, 0, 0, 1.70252211019544039314978060272e-1, 6.02165389804559606850219397283e-2, -1.7578125e-2, 0, 0, 0, 0, 0, 0 },
        new[] { 3.70920001185047927108779319836e-2, 0, 0, 1.70383925712239993810214054705e-1, 1.07262030446373284651809199168e-1, -1.53194377486244017527936158236e-2, 8.27378916381402288758473766002e-3, 0, 0, 0, 0, 0 },
        new[] { 6.24110958716075717114429577812e-1, 0, 0, -3.36089262944694129406857109825, -8.68219346841726006818189891453e-1, 2.75920996994467083049415600797e1, 2.01540675504778934086186788979e1, -4.34898841810699588477366255144e1, 0, 0, 0, 0 },
        new[] { 4.77662536438264365890433908527e-1, 0, 0, -2.48811461997166764192642586468, -5.90290826836842996371446475743e-1, 2.12300514481811942347288949897e1, 1.52792336328824235832596922938e1, -3.32882109689848629194453265587e1, -2.03312017085086261358222928593e-2, 0, 0, 0 },
        new[] { -9.3714243008598732571704021658e-1, 0, 0, 5.18637242884406370830023853209, 1.09143734899672957818500254654, -8.14978701074692612513997267357, -1.85200656599969598641566180701e1, 2.27394870993505042818970056734e1, 2.49360555267965238987089396762, -3.0467644718982195003823669022, 0, 0 },
        new[] { 2.27331014751653820792359768449, 0, 0, -1.05344954667372501984066689879e1, -2.00087205822486249909675718444, -1.79589318631187989172765950534e1, 2.79488845294199600508499808837e1, -2.85899827713502369474065508674, -8.87285693353062954433549289258, 1.23605671757943030647266201528e1, 6.43392746015763530355970484046e-1, 0 }
    };

    private static readonly double[] StudyDop853B =
    {
        5.42937341165687622380535766363e-2, 0, 0, 0, 0,
        4.45031289275240888144113950566, 1.89151789931450038304281599044,
        -5.8012039600105847814672114227, 3.1116436695781989440891606237e-1,
        -1.52160949662516078556178806805e-1, 2.01365400804030348374776537501e-1,
        4.47106157277725905176885569043e-2
    };

    private static readonly double[] StudyDop853C =
    {
        0, 5.26001519587677318785587544488e-2, 7.89002279381515978178381316732e-2,
        1.18350341907227396972937717510e-1, 2.81649658092772603273242802490e-1,
        3.33333333333333333333333333333e-1, 2.5e-1, 3.07692307692307692307692307692e-1,
        6.51282051282051282051282051282e-1, 6e-1, 8.57142857142857142857142857142e-1, 1d
    };

    private static readonly double[] StudyDop853E5 =
    {
        1.312004499419488073250102996e-2, 0, 0, 0, 0,
        -1.225156446376204440720569753, -4.957589496572501915214079952e-1,
        1.664377182454986536961530415, -3.503288487499736816886487290e-1,
        3.341791187130174790297318841e-1, 8.192320648511571246570742613e-2,
        -2.235530786388629525884427845e-2
    };



    private static void StudyRk4Step(double[] p, double[] v, double h, double[] mu, long steps, ref long calls)
    {
        int n = mu.Length;
        int L = 3 * n;
        var a1 = new double[L];
        var a2 = new double[L];
        var a3 = new double[L];
        var a4 = new double[L];
        var tp = new double[L];
        var v2 = new double[L];
        var v3 = new double[L];
        var v4 = new double[L];
        for (long s = 0; s < steps; s++)
        {
            StudyAcc(p, a1, mu);
            calls++;
            for (int i = 0; i < L; i++)
            {
                tp[i] = p[i] + (0.5d * h * v[i]);
                v2[i] = v[i] + (0.5d * h * a1[i]);
            }

            StudyAcc(tp, a2, mu);
            calls++;
            for (int i = 0; i < L; i++)
            {
                tp[i] = p[i] + (0.5d * h * v2[i]);
                v3[i] = v[i] + (0.5d * h * a2[i]);
            }

            StudyAcc(tp, a3, mu);
            calls++;
            for (int i = 0; i < L; i++)
            {
                tp[i] = p[i] + (h * v3[i]);
                v4[i] = v[i] + (h * a3[i]);
            }

            StudyAcc(tp, a4, mu);
            calls++;
            for (int i = 0; i < L; i++)
            {
                p[i] += h / 6d * (v[i] + (2d * v2[i]) + (2d * v3[i]) + v4[i]);
                v[i] += h / 6d * (a1[i] + (2d * a2[i]) + (2d * a3[i]) + a4[i]);
            }
        }
    }

    private static void StudyDopri5Fixed(double[] p, double[] v, double h, double[] mu, long steps, ref long calls)
    {
        int n = mu.Length;
        int L = 3 * n;
        double[][] A =
        {
            new[] { 1d / 5d },
            new[] { 3d / 40d, 9d / 40d },
            new[] { 44d / 45d, -56d / 15d, 32d / 9d },
            new[] { 19372d / 6561d, -25360d / 2187d, 64448d / 6561d, -212d / 729d },
            new[] { 9017d / 3168d, -355d / 33d, 46732d / 5247d, 49d / 176d, -5103d / 18656d }
        };
        double[] b = { 35d / 384d, 0d, 500d / 1113d, 125d / 192d, -2187d / 6784d, 11d / 84d };
        var kp = new double[6][];
        var kv = new double[6][];
        for (int s = 0; s < 6; s++)
        {
            kp[s] = new double[L];
            kv[s] = new double[L];
        }

        var tp = new double[L];
        var tv = new double[L];
        Array.Copy(v, kp[0], L);
        StudyAcc(p, kv[0], mu);
        calls++;
        for (long st = 0; st < steps; st++)
        {
            for (int s = 1; s < 6; s++)
            {
                for (int i = 0; i < L; i++)
                {
                    double sp = 0d;
                    double sv = 0d;
                    for (int j = 0; j < s; j++)
                    {
                        sp += A[s - 1][j] * kp[j][i];
                        sv += A[s - 1][j] * kv[j][i];
                    }

                    tp[i] = p[i] + (h * sp);
                    tv[i] = v[i] + (h * sv);
                }

                Array.Copy(tv, kp[s], L);
                StudyAcc(tp, kv[s], mu);
                calls++;
            }

            for (int i = 0; i < L; i++)
            {
                double sp = 0d;
                double sv = 0d;
                for (int j = 0; j < 6; j++)
                {
                    sp += b[j] * kp[j][i];
                    sv += b[j] * kv[j][i];
                }

                p[i] += h * sp;
                v[i] += h * sv;
            }

            Array.Copy(v, kp[0], L);
            StudyAcc(p, kv[0], mu);
            calls++;
        }
    }

    private static void StudyPefrl(double[] p, double[] v, double h, double[] mu, long steps, ref long calls)
    {
        const double xi = 0.1786178958448091d;
        const double lam = -0.2123418310626054d;
        const double chi = -0.06626458266981849d;
        int L = 3 * mu.Length;
        var a = new double[L];
        for (long s = 0; s < steps; s++)
        {
            for (int i = 0; i < L; i++)
            {
                p[i] += xi * h * v[i];
            }

            StudyAcc(p, a, mu);
            calls++;
            for (int i = 0; i < L; i++)
            {
                v[i] += (1d - (2d * lam)) * h / 2d * a[i];
            }

            for (int i = 0; i < L; i++)
            {
                p[i] += chi * h * v[i];
            }

            StudyAcc(p, a, mu);
            calls++;
            for (int i = 0; i < L; i++)
            {
                v[i] += lam * h * a[i];
            }

            for (int i = 0; i < L; i++)
            {
                p[i] += (1d - (2d * (chi + xi))) * h * v[i];
            }

            StudyAcc(p, a, mu);
            calls++;
            for (int i = 0; i < L; i++)
            {
                v[i] += lam * h * a[i];
            }

            for (int i = 0; i < L; i++)
            {
                p[i] += chi * h * v[i];
            }

            StudyAcc(p, a, mu);
            calls++;
            for (int i = 0; i < L; i++)
            {
                v[i] += (1d - (2d * lam)) * h / 2d * a[i];
            }

            for (int i = 0; i < L; i++)
            {
                p[i] += xi * h * v[i];
            }
        }
    }

    private static void StudyY6(double[] p, double[] v, double h, double[] mu, long steps, ref long calls)
    {
        int L = 3 * mu.Length;
        var a = new double[L];
        double[] ws = StudyY6Weights();
        StudyAcc(p, a, mu);
        calls++;
        for (long s = 0; s < steps; s++)
        {
            foreach (double w in ws)
            {
                double dt = w * h;
                for (int i = 0; i < L; i++)
                {
                    v[i] += 0.5d * dt * a[i];
                }

                for (int i = 0; i < L; i++)
                {
                    p[i] += dt * v[i];
                }

                StudyAcc(p, a, mu);
                calls++;
                for (int i = 0; i < L; i++)
                {
                    v[i] += 0.5d * dt * a[i];
                }
            }
        }
    }

    private static void StudyY6C(
        double[] p, double[] v, double h, double[] mu, long steps, ref long calls,
        List<StudySample> samples = null, double sampleEvery = 0d, double t0 = 0d)
    {
        int L = 3 * mu.Length;
        var a = new double[L];
        var pl = new double[L];
        var vl = new double[L];
        double[] ws = StudyY6Weights();
        double t = t0;
        double nextSample = samples != null && sampleEvery > 0d ? t0 + sampleEvery : double.PositiveInfinity;
        StudyAccC(p, pl, a, mu);
        calls++;
        for (long s = 0; s < steps; s++)
        {
            foreach (double w in ws)
            {
                double dt = w * h;
                for (int i = 0; i < L; i++)
                {
                    StudyAdd(ref v[i], ref vl[i], 0.5d * dt * a[i]);
                }

                for (int i = 0; i < L; i++)
                {
                    StudyAdd(ref p[i], ref pl[i], dt * (v[i] + vl[i]));
                }

                StudyAccC(p, pl, a, mu);
                calls++;
                for (int i = 0; i < L; i++)
                {
                    StudyAdd(ref v[i], ref vl[i], 0.5d * dt * a[i]);
                }
            }

            t += h;
            if (samples != null && sampleEvery > 0d && t >= nextSample - 1e-9d)
            {
                samples.Add(new StudySample(t, StudySampleP(p, pl, L)));
                nextSample += sampleEvery;
            }
        }

        if (samples != null && sampleEvery > 0d
            && (samples.Count == 0 || samples[samples.Count - 1].T < t - 1e-9d))
        {
            samples.Add(new StudySample(t, StudySampleP(p, pl, L)));
        }

        for (int i = 0; i < L; i++)
        {
            p[i] += pl[i];
            v[i] += vl[i];
        }
    }

    private static void StudyStep(
        string method, double[] p, double[] v, double h, double[] mu, long steps, ref long calls)
    {
        switch (method)
        {
            case "RK4":
                StudyRk4Step(p, v, h, mu, steps, ref calls);
                break;
            case "DOPRI5":
                StudyDopri5Fixed(p, v, h, mu, steps, ref calls);
                break;
            case "PEFRL":
                StudyPefrl(p, v, h, mu, steps, ref calls);
                break;
            case "Y6":
                StudyY6(p, v, h, mu, steps, ref calls);
                break;
            case "Y6C":
                StudyY6C(p, v, h, mu, steps, ref calls);
                break;
            default:
                throw new ArgumentException("Unknown method " + method);
        }
    }

    private static double[] StudySampleP(double[] p, double[] pl, int length)
    {
        var s = new double[length];
        for (int i = 0; i < length; i++)
        {
            s[i] = p[i] + (pl == null ? 0d : pl[i]);
        }

        return s;
    }

    private static StudyRefResult StudyRefRun(
        StudyNBody nb, string method, double h, double years, double sampleEvery)
    {
        int n = nb.Count;
        int L = 3 * n;
        var p = (double[])nb.P0.Clone();
        var v = (double[])nb.V0.Clone();
        double T = years * StudyYear;
        long fullSteps = (long)Math.Floor(T / h);
        double remainder = T - (fullSteps * h);
        var samples = sampleEvery > 0d ? new List<StudySample>() : null;
        long calls = 0;
        var sw = Stopwatch.StartNew();
        if (fullSteps > 0)
        {
            if (method == "Y6C")
            {
                StudyY6C(p, v, h, nb.Mu, fullSteps, ref calls, samples, sampleEvery, 0d);
            }
            else
            {
                StudyStep(method, p, v, h, nb.Mu, fullSteps, ref calls);
            }
        }

        if (remainder > 1e-9d)
        {
            if (method == "Y6C")
            {
                StudyY6C(p, v, remainder, nb.Mu, 1, ref calls, samples, sampleEvery, fullSteps * h);
            }
            else
            {
                StudyStep(method, p, v, remainder, nb.Mu, 1, ref calls);
            }
        }

        sw.Stop();
        return new StudyRefResult { P = p, V = v, Samples = samples, Calls = calls, Ms = sw.Elapsed.TotalMilliseconds };
    }

    private static double StudyRel(double[] p, int i, int j, double[] r)
    {
        double dx = (p[3 * i] - p[3 * j]) - (r[3 * i] - r[3 * j]);
        double dy = (p[(3 * i) + 1] - p[(3 * j) + 1]) - (r[(3 * i) + 1] - r[(3 * j) + 1]);
        double dz = (p[(3 * i) + 2] - p[(3 * j) + 2]) - (r[(3 * i) + 2] - r[(3 * j) + 2]);
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static double StudyEnergy(double[] p, double[] v, double[] mu)
    {
        int n = mu.Length;
        double t = 0d;
        double u = 0d;
        for (int i = 0; i < n; i++)
        {
            t += 0.5d * mu[i] * ((v[3 * i] * v[3 * i]) + (v[(3 * i) + 1] * v[(3 * i) + 1]) + (v[(3 * i) + 2] * v[(3 * i) + 2]));
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                double dx = p[3 * i] - p[3 * j];
                double dy = p[(3 * i) + 1] - p[(3 * j) + 1];
                double dz = p[(3 * i) + 2] - p[(3 * j) + 2];
                u -= mu[i] * mu[j] / Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            }
        }

        return t + u;
    }

    // ------------------------------------------------------------------
    // System table
    // ------------------------------------------------------------------

    private static void StudySystemTable(string label, StarSystem sys)
    {
        Console.WriteLine();
        Console.WriteLine("=== " + label + " ===");
        Console.WriteLine(
            "{" + "body,-8} {" + "parent,-8} {" + "mu, m3/s2,12} {" + "R, m,10} {" + "a, m,12} {" +
            "e,6} {" + "T, d,10} {" + "Hill, m,11} {" + "seg, d,7} {" + "g, m/s2,8} {" + "rho, g/cm3,9}");
        double fastest = double.PositiveInfinity;
        foreach (OrbitingBody body in sys.AllBodies)
        {
            double mu = body.ResolveStandardGravitationalParameter();
            double g = mu / (body.Radius * body.Radius);
            double volume = (4d / 3d) * Math.PI * body.Radius * body.Radius * body.Radius;
            double density = mu / PhysicsSolver.GravitationalConstant / volume / 1000d;
            double period = 0d;
            if (body.Parent != null)
            {
                double muLocal = body.Parent.ResolveStandardGravitationalParameter()
                    + body.ResolveSubtreeStandardGravitationalParameter();
                double a3 = body.SemiMajorAxis * body.SemiMajorAxis * body.SemiMajorAxis;
                period = 2d * Math.PI * Math.Sqrt(a3 / muLocal);
                if (period < fastest)
                {
                    fastest = period;
                }
            }

            double seg = EphemerisBaker.DefaultSegmentLengthSeconds(body);
            Console.WriteLine(
                "{0,-8} {1,-8} {2,12:E3} {3,10:E3} {4,12:E3} {5,6:F3} {6,10:F2} {7,11:E3} {8,7:F2} {9,8:F2} {10,9:F2}",
                body.Name,
                body.Parent == null ? "-" : body.Parent.Name,
                mu,
                body.Radius,
                body.SemiMajorAxis,
                body.Eccentricity,
                period / 86400d,
                body.HillSphereRadius,
                seg / 86400d,
                g,
                density);
        }

        double phaseStep = fastest * 0.02d / (2d * Math.PI);
        double bakeStep = Math.Min(900d, phaseStep);
        Console.WriteLine("fastest orbit = {0:F2} d; phase budget step = {1:F0} s; bake step = {2:F0} s",
            fastest / 86400d, phaseStep, bakeStep);
    }

    // ------------------------------------------------------------------
    // Planet table
    // ------------------------------------------------------------------

    private static void StudyPlanetTable(
        string label, StudyNBody nb, double years, double refH, bool convergence)
    {
        Console.WriteLine();
        Console.WriteLine("=== " + label + ": " + years + " y, reference Y6C h=" + refH + " s ===");
        var reference = StudyRefRun(nb, "Y6C", refH, years, 0d);
        if (convergence)
        {
            var half = StudyRefRun(nb, "Y6C", refH * 0.5d, years, 0d);
            double worst = 0d;
            for (int i = 0; i < nb.Count; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    worst = Math.Max(worst, StudyRel(reference.P, i, j, half.P));
                }
            }

            Console.WriteLine("reference self-convergence (h vs h/2): worst pair = {0:E3} m", worst);
        }

        double e0 = StudyEnergy(nb.P0, nb.V0, nb.Mu);
        Console.WriteLine("{" + "system,-24} {" + "method,-8} {" + "h, s,6} {" + "calls,12} {" +
            "Luna/Terra, m,14} {" + "Terra/Sol, m,13} {" + "outer, m,12} {" + "dE/E,10} {" + "ms,9}");
        var rows = new[]
        {
            ("RK4", 900d),
            ("RK4", 3600d),
            ("DOPRI5", 3600d),
            ("PEFRL", 3600d),
            ("PEFRL", 7200d),
            ("Y6", 1800d),
            ("Y6", 3600d),
            ("Y6", 7200d)
        };
        var rowResults = new (double inner, double mid, double outer, double de, long calls, double ms)[rows.Length];
        Parallel.For(0, rows.Length, idx =>
        {
            (string method, double h) = rows[idx];
            var r = StudyRefRun(nb, method, h, years, 0d);
            double e = StudyEnergy(r.P, r.V, nb.Mu);
            double de = Math.Abs((e - e0) / e0);
            double inner = StudyRel(r.P, 2, 1, reference.P);
            double mid = StudyRel(r.P, 1, 0, reference.P);
            double outer = StudyRel(r.P, nb.Count - 1, 0, reference.P);
            rowResults[idx] = (inner, mid, outer, de, r.Calls, r.Ms);
        });
        for (int idx = 0; idx < rows.Length; idx++)
        {
            (string method, double h) = rows[idx];
            var row = rowResults[idx];
            Console.WriteLine(
                "{0,-24} {1,-8} {2,6:F0} {3,12:N0} {4,14:E3} {5,13:E3} {6,12:E3} {7,10:E2} {8,9:F0}",
                label, method, h, row.calls, row.inner, row.mid, row.outer, row.de, row.ms);
        }

        Console.WriteLine("reference: calls={0:N0}, {1:F0} ms", reference.Calls, reference.Ms);
    }

    private static void StudyBake(string label, StarSystem sys, StudyNBody nb, double years)
    {
        Console.WriteLine();
        Console.WriteLine("=== " + label + ": bake " + years + " y, RK4 900 s, degree 12, quant 2 m ===");
        var config = new BakeConfig { Degree = 12, MaxStepSeconds = 900d, PhasePerStep = 0.02d, QuantBudgetMeters = 2d };
        var sw = Stopwatch.StartNew();
        Dictionary<OrbitingBody, BakedEphemeris> eph = EphemerisBaker.Bake(sys, years, config);
        sw.Stop();
        double bakeMs = sw.Elapsed.TotalMilliseconds;
        foreach (var kv in eph)
        {
            kv.Key.Baked = kv.Value;
        }

        sys.InvalidatePositionCache();

        // EphemerisBaker counts a year as 365 days; StudyYear is 365.25.
        double bakeYears = years * 365d / 365.25d;
        var refSamples = StudyRefRun(nb, "Y6C", 112.5d, bakeYears, 30d * 86400d);
        double worst = 0d;
        var perBody = new double[nb.Count];
        for (int s = 0; s < refSamples.Samples.Count; s++)
        {
            StudySample sample = refSamples.Samples[s];
            double[] rp = sample.P;
            sys.EvaluateBodyState(sys.AllBodies[0], sample.T, out Vector3d rootP, out _);
            for (int i = 1; i < nb.Count; i++)
            {
                sys.EvaluateBodyState(sys.AllBodies[i], sample.T, out Vector3d p, out _);
                double dx = (p.X - rootP.X) - (rp[3 * i] - rp[0]);
                double dy = (p.Y - rootP.Y) - (rp[(3 * i) + 1] - rp[1]);
                double dz = (p.Z - rootP.Z) - (rp[(3 * i) + 2] - rp[2]);
                double err = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                if (err > perBody[i])
                {
                    perBody[i] = err;
                }

                if (err > worst)
                {
                    worst = err;
                }
            }
        }

        long segs = 0L;
        foreach (var kv in eph)
        {
            segs += kv.Value.SegmentCount;
        }

        Console.WriteLine("bake: {0:F0} ms, segments={1:N0}, worst position error vs Y6C = {2:E3} m", bakeMs, segs, worst);
        Console.Write("per body:");
        for (int i = 1; i < nb.Count; i++)
        {
            Console.Write(" {0}={1:E2} m", nb.Names[i], perBody[i]);
        }

        Console.WriteLine();

        string dir = Path.Combine(Path.GetTempPath(), "study_bake_" + Guid.NewGuid().ToString("N"));
        try
        {
            StarSystem fileSys = label.IndexOf("Custom", StringComparison.OrdinalIgnoreCase) >= 0 ? StudyCustomSystem() : StudyRealSolar7();
            var sw2 = Stopwatch.StartNew();
            EphemerisBaker.BakeToFiles(fileSys, dir, years, config, null, null, null, true);
            sw2.Stop();
            EphemerisBaker.AttachFiles(fileSys, dir);
            long bytes = 0L;
            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                bytes += new FileInfo(f).Length;
            }

            double worstQ = 0d;
            for (int s = 0; s < refSamples.Samples.Count; s++)
            {
                StudySample sample = refSamples.Samples[s];
                double[] rp = sample.P;
                fileSys.EvaluateBodyState(fileSys.AllBodies[0], sample.T, out Vector3d rootP, out _);
                for (int i = 1; i < nb.Count; i++)
                {
                    fileSys.EvaluateBodyState(fileSys.AllBodies[i], sample.T, out Vector3d p, out _);
                    double dx = (p.X - rootP.X) - (rp[3 * i] - rp[0]);
                    double dy = (p.Y - rootP.Y) - (rp[(3 * i) + 1] - rp[1]);
                    double dz = (p.Z - rootP.Z) - (rp[(3 * i) + 2] - rp[2]);
                    worstQ = Math.Max(worstQ, Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)));
                }
            }

            Console.WriteLine("compressed files: {0:F0} ms, {1:N0} bytes; worst vs Y6C = {2:E3} m",
                sw2.Elapsed.TotalMilliseconds, bytes, worstQ);
        }
        finally
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch (IOException)
            {
            }
        }

        Console.WriteLine("bake step (actual formula): {0:F0} s", StudyBakeStep(sys));
    }

    private static double StudyBakeStep(StarSystem sys)
    {
        double fastest = double.PositiveInfinity;
        foreach (OrbitingBody body in sys.AllBodies)
        {
            if (body.Parent == null)
            {
                continue;
            }

            double muLocal = body.Parent.ResolveStandardGravitationalParameter()
                + body.ResolveSubtreeStandardGravitationalParameter();
            double a3 = body.SemiMajorAxis * body.SemiMajorAxis * body.SemiMajorAxis;
            double period = 2d * Math.PI * Math.Sqrt(a3 / muLocal);
            if (period < fastest)
            {
                fastest = period;
            }
        }

        return Math.Min(900d, fastest * 0.02d / (2d * Math.PI));
    }

    // ------------------------------------------------------------------
    // Ship table
    // ------------------------------------------------------------------

    private static void StudyShipOrbit(
        double rp, double ra, double mu, out Vector3d relP, out Vector3d relV)
    {
        double a = 0.5d * (rp + ra);
        relP = new Vector3d(rp, 0d, 0d);
        double vp = ra == rp
            ? Math.Sqrt(mu / rp)
            : Math.Sqrt(mu * (2d / rp - 1d / a));
        relV = new Vector3d(0d, vp, 0d);
    }

    /// <summary>
    /// Acceleration of the ship relative to the central body. Both samples use the
    /// same world-frame field, so the frame's indirect term cancels; only the
    /// difference of the field at the ship and at the central body remains.
    /// </summary>
    private static Vector3d StudyLocalAcceleration(
        StarSystem sys, int central, Vector3d relP, double t)
    {
        sys.EvaluateBodyState(sys.AllBodies[central], t, out Vector3d bp, out _);
        Vector3d shipAcc = sys.EvaluateShipAccelerationCached(bp + relP, t);
        Vector3d centralAcc = sys.EvaluateShipAccelerationCached(bp, t);
        return shipAcc - centralAcc;
    }

    /// <summary>
    /// Same relative acceleration, but each body's differential term is formed
    /// from relative vectors instead of subtracting two ~mu/r^2 fields. The test
    /// bodies have J2 = 0; the root indirect term cancels in a relative frame.
    /// </summary>
    private static Vector3d StudyLocalAccelerationDirect(
        StarSystem sys, int central, Vector3d relP, double t)
    {
        var bodies = sys.AllBodies;
        sys.EvaluateBodyState(bodies[central], t, out Vector3d bc, out _);
        Vector3d total = Vector3d.Zero;
        for (int i = 0; i < bodies.Count; i++)
        {
            OrbitingBody body = bodies[i];
            double mu = body.ResolveStandardGravitationalParameter();
            if (mu == 0d)
            {
                continue;
            }

            if (i == central)
            {
                total += PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                    relP, Vector3d.Zero, mu);
                continue;
            }

            sys.EvaluateBodyState(body, t, out Vector3d bp, out _);
            Vector3d dc = bc - bp;
            total += PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                dc + relP, Vector3d.Zero, mu);
            total -= PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                dc, Vector3d.Zero, mu);
        }

        return total;
    }

    // Double-double arithmetic for a reference that is not limited by double rounding.
    private readonly struct StudyDd
    {
        public readonly double Hi;
        public readonly double Lo;

        public StudyDd(double hi, double lo)
        {
            Hi = hi;
            Lo = lo;
        }

        public static readonly StudyDd Zero = new StudyDd(0d, 0d);
        public static StudyDd From(double x) => new StudyDd(x, 0d);

        public static StudyDd Add(StudyDd a, StudyDd b)
        {
            double s = a.Hi + b.Hi;
            double v = s - a.Hi;
            double e = (a.Hi - (s - v)) + (b.Hi - v) + a.Lo + b.Lo;
            double r = s + e;
            return new StudyDd(r, e - (r - s));
        }

        public static StudyDd Sub(StudyDd a, StudyDd b) =>
            Add(a, new StudyDd(-b.Hi, -b.Lo));

        public static StudyDd Mul(StudyDd a, StudyDd b)
        {
            double p = a.Hi * b.Hi;
            double e = Math.FusedMultiplyAdd(a.Hi, b.Hi, -p);
            e += (a.Hi * b.Lo) + (a.Lo * b.Hi);
            double r = p + e;
            return new StudyDd(r, e - (r - p));
        }

        public static StudyDd Div(StudyDd a, StudyDd b)
        {
            double q1 = a.Hi / b.Hi;
            StudyDd r = Sub(a, Mul(b, From(q1)));
            double q2 = r.Hi / b.Hi;
            StudyDd q = Add(From(q1), From(q2));
            r = Sub(a, Mul(b, q));
            double q3 = r.Hi / b.Hi;
            return Add(q, From(q3));
        }

        public static StudyDd Sqrt(StudyDd a)
        {
            if (!(a.Hi > 0d))
            {
                return Zero;
            }

            double x = Math.Sqrt(a.Hi);
            StudyDd r = Sub(a, Mul(From(x), From(x)));
            double c = r.Hi / (2d * x);
            return Add(From(x), From(c));
        }

        public static StudyDd operator +(StudyDd a, StudyDd b) => Add(a, b);
        public static StudyDd operator -(StudyDd a, StudyDd b) => Sub(a, b);
        public static StudyDd operator *(StudyDd a, StudyDd b) => Mul(a, b);
        public static StudyDd operator /(StudyDd a, StudyDd b) => Div(a, b);
        public double ToDouble => Hi + Lo;
    }

    private readonly struct StudyDd3
    {
        public readonly StudyDd X;
        public readonly StudyDd Y;
        public readonly StudyDd Z;

        public StudyDd3(StudyDd x, StudyDd y, StudyDd z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly StudyDd3 Zero = new StudyDd3(StudyDd.Zero, StudyDd.Zero, StudyDd.Zero);
        public static StudyDd3 From(Vector3d v) =>
            new StudyDd3(StudyDd.From(v.X), StudyDd.From(v.Y), StudyDd.From(v.Z));
        public Vector3d ToVector3d() => new Vector3d(X.ToDouble, Y.ToDouble, Z.ToDouble);

        public StudyDd SqrMagnitude => StudyDd.Add(StudyDd.Add(StudyDd.Mul(X, X), StudyDd.Mul(Y, Y)), StudyDd.Mul(Z, Z));
        public StudyDd Magnitude => StudyDd.Sqrt(SqrMagnitude);

        public static StudyDd3 operator +(StudyDd3 a, StudyDd3 b) =>
            new StudyDd3(StudyDd.Add(a.X, b.X), StudyDd.Add(a.Y, b.Y), StudyDd.Add(a.Z, b.Z));
        public static StudyDd3 operator -(StudyDd3 a, StudyDd3 b) =>
            new StudyDd3(StudyDd.Sub(a.X, b.X), StudyDd.Sub(a.Y, b.Y), StudyDd.Sub(a.Z, b.Z));
        public static StudyDd3 operator *(StudyDd3 a, StudyDd s) =>
            new StudyDd3(StudyDd.Mul(a.X, s), StudyDd.Mul(a.Y, s), StudyDd.Mul(a.Z, s));
        public static StudyDd3 operator *(StudyDd3 a, double s) => a * StudyDd.From(s);
    }

    private static StudyDd3 StudyForceDd(StarSystem sys, StudyDd3 p, double t)
    {
        var bodies = sys.AllBodies;
        int n = bodies.Count;
        sys.EvaluateBodyState(bodies[0], t, out Vector3d rootPos, out _);
        StudyDd3 total = StudyDd3.Zero;
        for (int i = 0; i < n; i++)
        {
            OrbitingBody body = bodies[i];
            double mu = body.ResolveStandardGravitationalParameter();
            if (mu == 0d)
            {
                continue;
            }

            sys.EvaluateBodyState(body, t, out Vector3d bp, out _);
            StudyDd3 d = StudyDd3.From(bp) - p;
            StudyDd r2 = d.SqrMagnitude;
            StudyDd inv = StudyDd.From(1d) / (r2 * StudyDd.Sqrt(r2));
            total += d * (StudyDd.From(mu) * inv);
        }

        // Root-fixed frame: subtract the root's acceleration from its direct
        // children (subtree mu), not the field value at the root (which is 0).
        Vector3d rootAccel = Vector3d.Zero;
        for (int i = 1; i < n; i++)
        {
            OrbitingBody body = bodies[i];
            if (!ReferenceEquals(body.Parent, bodies[0]))
            {
                continue;
            }

            sys.EvaluateBodyState(body, t, out Vector3d bp, out _);
            rootAccel += PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                rootPos, bp, body.ResolveSubtreeStandardGravitationalParameter());
        }

        return total - StudyDd3.From(rootAccel);
    }

    private static (Vector3d p, Vector3d v, long calls, double ms) StudyShipY6CDd(
        StarSystem sys, Vector3d p0, Vector3d v0, double t0, double days, double h)
    {
        double T = t0 + (days * 86400d);
        double t = t0;
        StudyDd3 p = StudyDd3.From(p0);
        StudyDd3 v = StudyDd3.From(v0);
        double[] ws = StudyY6Weights();
        long calls = 0;
        var sw = Stopwatch.StartNew();
        StudyDd3 a = StudyForceDd(sys, p, t);
        calls++;
        while (t < T - 1e-9d)
        {
            double hStep = Math.Min(h, T - t);
            foreach (double w in ws)
            {
                double dt = w * hStep;
                v += a * (0.5d * dt);
                p += v * dt;
                t += dt;
                a = StudyForceDd(sys, p, t);
                calls++;
                v += a * (0.5d * dt);
            }
        }

        sw.Stop();
        return (p.ToVector3d(), v.ToVector3d(), calls, sw.Elapsed.TotalMilliseconds);
    }

    private static (Vector3d p, Vector3d v) StudyShipRefA(
        StarSystem sys, Vector3d p0, Vector3d v0, double t0, double days, double h)
    {
        double T = t0 + days * 86400d;
        var p = new[] { p0.X, p0.Y, p0.Z };
        var v = new[] { v0.X, v0.Y, v0.Z };
        var pl = new double[3];
        var vl = new double[3];
        double t = t0;
        long calls = 0;
        double[] ws = StudyY6Weights();
        Vector3d acc = sys.EvaluateShipAccelerationCached(
            new Vector3d(p[0] + pl[0], p[1] + pl[1], p[2] + pl[2]), t);
        calls++;
        while (t < T - 1e-9d)
        {
            double hStep = Math.Min(h, T - t);
            foreach (double w in ws)
            {
                double dt = w * hStep;
                for (int i = 0; i < 3; i++)
                {
                    StudyAdd(ref v[i], ref vl[i], 0.5d * dt * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z)));
                }

                for (int i = 0; i < 3; i++)
                {
                    StudyAdd(ref p[i], ref pl[i], dt * (v[i] + vl[i]));
                }

                t += dt;
                acc = sys.EvaluateShipAccelerationCached(
                    new Vector3d(p[0] + pl[0], p[1] + pl[1], p[2] + pl[2]), t);
                calls++;
                for (int i = 0; i < 3; i++)
                {
                    StudyAdd(ref v[i], ref vl[i], 0.5d * dt * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z)));
                }
            }
        }

        return (new Vector3d(p[0] + pl[0], p[1] + pl[1], p[2] + pl[2]),
            new Vector3d(v[0] + vl[0], v[1] + vl[1], v[2] + vl[2]));
    }

    /// <summary>
    /// Y6C reference in the central body's local frame. The local state is small
    /// (planet radius, not solar-system radius), so the integration does not pay the
    /// absolute-coordinate cancellation that dominates the world-frame ship test.
    /// </summary>
    private static (Vector3d p, Vector3d v) StudyShipRefLocal(
        StarSystem sys, int central, Vector3d relP0, Vector3d relV0,
        double t0, double days, double h)
    {
        double T = t0 + (days * 86400d);
        var p = new[] { relP0.X, relP0.Y, relP0.Z };
        var v = new[] { relV0.X, relV0.Y, relV0.Z };
        var pl = new double[3];
        var vl = new double[3];
        double t = t0;
        long step = 0L;
        double[] ws = StudyY6Weights();
        Vector3d acc = StudyLocalAcceleration(
            sys, central, new Vector3d(p[0] + pl[0], p[1] + pl[1], p[2] + pl[2]), t);
        while (t < T - 1e-9d)
        {
            double target = Math.Min(T, t0 + ((step + 1L) * h));
            double hStep = target - t;
            foreach (double w in ws)
            {
                double dt = w * hStep;
                for (int i = 0; i < 3; i++)
                {
                    StudyAdd(ref v[i], ref vl[i], 0.5d * dt * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z)));
                }

                for (int i = 0; i < 3; i++)
                {
                    StudyAdd(ref p[i], ref pl[i], dt * (v[i] + vl[i]));
                }

                t += dt;
                acc = StudyLocalAcceleration(
                    sys, central, new Vector3d(p[0] + pl[0], p[1] + pl[1], p[2] + pl[2]), t);
                for (int i = 0; i < 3; i++)
                {
                    StudyAdd(ref v[i], ref vl[i], 0.5d * dt * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z)));
                }
            }

            t = target;
            step++;
        }

        return (new Vector3d(p[0] + pl[0], p[1] + pl[1], p[2] + pl[2]),
            new Vector3d(v[0] + vl[0], v[1] + vl[1], v[2] + vl[2]));
    }

    private static (Vector3d p, Vector3d v, long calls, double ms) StudyShipFixed(
        StarSystem sys, Vector3d p0, Vector3d v0, double t0, double days, double h, string method)
    {
        double T = t0 + days * 86400d;
        var p = new[] { p0.X, p0.Y, p0.Z };
        var v = new[] { v0.X, v0.Y, v0.Z };
        double t = t0;
        long calls = 0;
        var sw = Stopwatch.StartNew();
        double[] ws = StudyY6Weights();
        if (method == "Y6")
        {
            Vector3d acc = sys.EvaluateShipAccelerationCached(
                new Vector3d(p[0], p[1], p[2]), t);
            calls++;
            while (t < T - 1e-9d)
            {
                double hStep = Math.Min(h, T - t);
                foreach (double w in ws)
                {
                    double dt = w * hStep;
                    for (int i = 0; i < 3; i++)
                    {
                        v[i] += 0.5d * dt * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
                    }

                    for (int i = 0; i < 3; i++)
                    {
                        p[i] += dt * v[i];
                    }

                    t += dt;
                    acc = sys.EvaluateShipAccelerationCached(
                        new Vector3d(p[0], p[1], p[2]), t);
                    calls++;
                    for (int i = 0; i < 3; i++)
                    {
                        v[i] += 0.5d * dt * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
                    }
                }
            }
        }
        else if (method == "PEFRL")
        {
            const double xi = 0.1786178958448091d;
            const double lam = -0.2123418310626054d;
            const double chi = -0.06626458266981849d;
            while (t < T - 1e-9d)
            {
                double hStep = Math.Min(h, T - t);
                for (int i = 0; i < 3; i++)
                {
                    p[i] += xi * hStep * v[i];
                }

                t += xi * hStep;
                Vector3d acc = sys.EvaluateShipAccelerationCached(new Vector3d(p[0], p[1], p[2]), t);
                calls++;
                for (int i = 0; i < 3; i++)
                {
                    v[i] += (1d - (2d * lam)) * hStep / 2d * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
                }

                for (int i = 0; i < 3; i++)
                {
                    p[i] += chi * hStep * v[i];
                }

                t += chi * hStep;
                acc = sys.EvaluateShipAccelerationCached(new Vector3d(p[0], p[1], p[2]), t);
                calls++;
                for (int i = 0; i < 3; i++)
                {
                    v[i] += lam * hStep * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
                }

                for (int i = 0; i < 3; i++)
                {
                    p[i] += (1d - (2d * (chi + xi))) * hStep * v[i];
                }

                t += (1d - (2d * (chi + xi))) * hStep;
                acc = sys.EvaluateShipAccelerationCached(new Vector3d(p[0], p[1], p[2]), t);
                calls++;
                for (int i = 0; i < 3; i++)
                {
                    v[i] += lam * hStep * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
                }

                for (int i = 0; i < 3; i++)
                {
                    p[i] += chi * hStep * v[i];
                }

                t += chi * hStep;
                acc = sys.EvaluateShipAccelerationCached(new Vector3d(p[0], p[1], p[2]), t);
                calls++;
                for (int i = 0; i < 3; i++)
                {
                    v[i] += (1d - (2d * lam)) * hStep / 2d * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
                }

                for (int i = 0; i < 3; i++)
                {
                    p[i] += xi * hStep * v[i];
                }

                t += xi * hStep;
            }
        }
        else
        {
            throw new ArgumentException("Unknown ship method " + method);
        }

        sw.Stop();
        return (new Vector3d(p[0], p[1], p[2]), new Vector3d(v[0], v[1], v[2]),
            calls, sw.Elapsed.TotalMilliseconds);
    }

    private static (Vector3d p, Vector3d v, long calls, double ms) StudyShipFixedLocal(
        StarSystem sys, int central, Vector3d relP0, Vector3d relV0,
        double t0, double days, double h, string method, bool directForce = false)
    {
        double T = t0 + (days * 86400d);
        var p = new[] { relP0.X, relP0.Y, relP0.Z };
        var v = new[] { relV0.X, relV0.Y, relV0.Z };
        double t = t0;
        long step = 0L;
        long calls = 0;
        var sw = Stopwatch.StartNew();
        double[] ws = StudyY6Weights();
        Vector3d Acc(Vector3d rp, double tt) => directForce
            ? StudyLocalAccelerationDirect(sys, central, rp, tt)
            : StudyLocalAcceleration(sys, central, rp, tt);
        if (method == "Y6")
        {
            Vector3d acc = Acc(new Vector3d(p[0], p[1], p[2]), t);
            calls++;
            while (t < T - 1e-9d)
            {
                double target = Math.Min(T, t0 + ((step + 1L) * h));
                double hStep = target - t;
                foreach (double w in ws)
                {
                    double dt = w * hStep;
                    for (int i = 0; i < 3; i++)
                    {
                        v[i] += 0.5d * dt * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
                    }

                    for (int i = 0; i < 3; i++)
                    {
                        p[i] += dt * v[i];
                    }

                    t += dt;
                    acc = Acc(new Vector3d(p[0], p[1], p[2]), t);
                    calls++;
                    for (int i = 0; i < 3; i++)
                    {
                        v[i] += 0.5d * dt * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
                    }
                }

                t = target;
                step++;
            }
        }
        else if (method == "PEFRL")
        {
            const double xi = 0.1786178958448091d;
            const double lam = -0.2123418310626054d;
            const double chi = -0.06626458266981849d;
            while (t < T - 1e-9d)
            {
                double target = Math.Min(T, t0 + ((step + 1L) * h));
                double hStep = target - t;
                for (int i = 0; i < 3; i++)
                {
                    p[i] += xi * hStep * v[i];
                }

                t += xi * hStep;
                Vector3d acc = Acc(new Vector3d(p[0], p[1], p[2]), t);
                calls++;
                for (int i = 0; i < 3; i++)
                {
                    v[i] += (1d - (2d * lam)) * hStep / 2d * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
                }

                for (int i = 0; i < 3; i++)
                {
                    p[i] += chi * hStep * v[i];
                }

                t += chi * hStep;
                acc = Acc(new Vector3d(p[0], p[1], p[2]), t);
                calls++;
                for (int i = 0; i < 3; i++)
                {
                    v[i] += lam * hStep * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
                }

                for (int i = 0; i < 3; i++)
                {
                    p[i] += (1d - (2d * (chi + xi))) * hStep * v[i];
                }

                t += (1d - (2d * (chi + xi))) * hStep;
                acc = Acc(new Vector3d(p[0], p[1], p[2]), t);
                calls++;
                for (int i = 0; i < 3; i++)
                {
                    v[i] += lam * hStep * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
                }

                for (int i = 0; i < 3; i++)
                {
                    p[i] += chi * hStep * v[i];
                }

                t += chi * hStep;
                acc = Acc(new Vector3d(p[0], p[1], p[2]), t);
                calls++;
                for (int i = 0; i < 3; i++)
                {
                    v[i] += (1d - (2d * lam)) * hStep / 2d * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
                }

                for (int i = 0; i < 3; i++)
                {
                    p[i] += xi * hStep * v[i];
                }

                t += xi * hStep;
                t = target;
                step++;
            }
        }
        else
        {
            throw new ArgumentException("Unknown ship method " + method);
        }

        sw.Stop();
        return (new Vector3d(p[0], p[1], p[2]), new Vector3d(v[0], v[1], v[2]),
            calls, sw.Elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// Wisdom-Holman-style step for the ship: the dominant body's Kepler motion is
    /// analytic (so a high-e perigee needs no small step), the rest is a kick.
    /// Composition with the Y6 weights makes the outer map 6th order.
    /// </summary>
    private static void StudyKeplerDrift(
        Vector3d r, Vector3d v, double mu, double dt, out Vector3d r2, out Vector3d v2)
    {
        double rMag = r.Magnitude;
        double vv = v.SqrMagnitude;
        double energy = 0.5d * vv - mu / rMag;
        double a = -mu / (2d * energy);
        if (!(a > 0d) || dt == 0d)
        {
            r2 = r;
            v2 = v;
            return;
        }

        Vector3d h = Vector3d.Cross(r, v);
        double hMag = h.Magnitude;
        double e = Math.Sqrt(Math.Max(0d, 1d - (hMag * hMag) / (mu * a)));
        if (e < 1e-8d)
        {
            double n = Math.Sqrt(mu / (rMag * rMag * rMag));
            double theta = n * dt;
            double c = Math.Cos(theta);
            double s = Math.Sin(theta);
            r2 = (r * c) + (v * (s / n));
            v2 = (v * c) - (r * (n * s));
            return;
        }

        OrbitalElements el = OrbitalElements.FromState(r, v, mu);
        if (!el.IsValid || !(el.Eccentricity < 1d) || !(el.SemiMajorAxis > 0d))
        {
            r2 = r;
            v2 = v;
            return;
        }

        double ea = el.Eccentricity;
        double sma = el.SemiMajorAxis;
        double nMean = Math.Sqrt(mu / (sma * sma * sma));
        double m0 = el.MeanAnomalyDegrees * Math.PI / 180d;
        double mean = m0 + (nMean * dt);
        double ecc = KeplerMath.SolveEccentricAnomaly(mean, ea);
        double cosE = Math.Cos(ecc);
        double sinE = Math.Sin(ecc);
        double radius = sma * (1d - (ea * cosE));
        double yScale = Math.Sqrt(1d - (ea * ea));
        var orbitalP = new Vector3d(sma * (cosE - ea), sma * yScale * sinE, 0d);
        double factor = Math.Sqrt(mu * sma) / radius;
        var orbitalV = new Vector3d(-factor * sinE, factor * yScale * cosE, 0d);
        double node = el.LongitudeOfAscendingNodeDegrees * Math.PI / 180d;
        double inc = el.InclinationDegrees * Math.PI / 180d;
        double argp = el.ArgumentOfPeriapsisDegrees * Math.PI / 180d;
        r2 = KeplerMath.RotateOrbitalToWorld(orbitalP, node, inc, argp);
        v2 = KeplerMath.RotateOrbitalToWorld(orbitalV, node, inc, argp);
    }

    private static void StudyWhStep(
        StarSystem sys, int central, double mu, ref Vector3d p, ref Vector3d v,
        double t, double dt, ref long calls)
    {
        sys.EvaluateBodyState(sys.AllBodies[central], t, out Vector3d bc, out Vector3d bv);
        Vector3d total = sys.EvaluateShipAccelerationCached(p, t);
        calls++;
        Vector3d centralBodyAccel = sys.EvaluateShipAccelerationCached(bc, t);
        calls++;
        Vector3d pert = total
            - PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(p, bc, mu)
            - centralBodyAccel;
        v += pert * (0.5d * dt);

        sys.EvaluateBodyState(sys.AllBodies[central], t + dt, out Vector3d bc2, out Vector3d bv2);
        StudyKeplerDrift(p - bc, v - bv, mu, dt, out Vector3d relP2, out Vector3d relV2);
        p = bc2 + relP2;
        v = bv2 + relV2;

        total = sys.EvaluateShipAccelerationCached(p, t + dt);
        calls++;
        centralBodyAccel = sys.EvaluateShipAccelerationCached(bc2, t + dt);
        calls++;
        pert = total
            - PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(p, bc2, mu)
            - centralBodyAccel;
        v += pert * (0.5d * dt);
    }

    private static (Vector3d p, Vector3d v, long calls, double ms) StudyShipWh(
        StarSystem sys, int central, Vector3d p0, Vector3d v0, double t0, double days, double h)
    {
        double mu = sys.AllBodies[central].ResolveStandardGravitationalParameter();
        double T = t0 + (days * 86400d);
        double t = t0;
        var p = p0;
        var v = v0;
        long calls = 0;
        var sw = Stopwatch.StartNew();
        double[] ws = StudyY6Weights();
        while (t < T - 1e-9d)
        {
            double hStep = Math.Min(h, T - t);
            foreach (double w in ws)
            {
                double dt = w * hStep;
                StudyWhStep(sys, central, mu, ref p, ref v, t, dt, ref calls);
                t += dt;
            }
        }

        sw.Stop();
        return (p, v, calls, sw.Elapsed.TotalMilliseconds);
    }

    private static void StudyWhStepLocal(
        StarSystem sys, int central, double mu, ref Vector3d relP, ref Vector3d relV,
        double t, double dt, ref long calls, bool directForce)
    {
        sys.EvaluateBodyState(sys.AllBodies[central], t, out Vector3d bc, out _);
        Vector3d direct = PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
            bc + relP, bc, mu);
        Vector3d pert;
        if (directForce)
        {
            pert = StudyLocalAccelerationDirect(sys, central, relP, t) - direct;
            calls++;
        }
        else
        {
            Vector3d total = sys.EvaluateShipAccelerationCached(bc + relP, t);
            Vector3d centralBodyAccel = sys.EvaluateShipAccelerationCached(bc, t);
            calls += 2;
            pert = total - centralBodyAccel - direct;
        }

        relV += pert * (0.5d * dt);

        sys.EvaluateBodyState(sys.AllBodies[central], t + dt, out Vector3d bc2, out _);
        StudyKeplerDrift(relP, relV, mu, dt, out Vector3d relP2, out Vector3d relV2);
        relP = relP2;
        relV = relV2;

        direct = PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
            bc2 + relP, bc2, mu);
        if (directForce)
        {
            pert = StudyLocalAccelerationDirect(sys, central, relP, t + dt) - direct;
            calls++;
        }
        else
        {
            Vector3d total = sys.EvaluateShipAccelerationCached(bc2 + relP, t + dt);
            Vector3d centralBodyAccel = sys.EvaluateShipAccelerationCached(bc2, t + dt);
            calls += 2;
            pert = total - centralBodyAccel - direct;
        }

        relV += pert * (0.5d * dt);
    }

    private static (Vector3d p, Vector3d v, long calls, double ms) StudyShipWhLocal(
        StarSystem sys, int central, Vector3d relP0, Vector3d relV0,
        double t0, double days, double h, bool directForce = false)
    {
        double mu = sys.AllBodies[central].ResolveStandardGravitationalParameter();
        double T = t0 + (days * 86400d);
        double t = t0;
        long step = 0L;
        Vector3d relP = relP0;
        Vector3d relV = relV0;
        long calls = 0;
        var sw = Stopwatch.StartNew();
        double[] ws = StudyY6Weights();
        while (t < T - 1e-9d)
        {
            double target = Math.Min(T, t0 + ((step + 1L) * h));
            double hStep = target - t;
            foreach (double w in ws)
            {
                double dt = w * hStep;
                StudyWhStepLocal(
                    sys, central, mu, ref relP, ref relV, t, dt, ref calls, directForce);
                t += dt;
            }

            t = target;
            step++;
        }

        sw.Stop();
        return (relP, relV, calls, sw.Elapsed.TotalMilliseconds);
    }

    private static (Vector3d p, Vector3d v, long calls, double ms) StudyShipDop853(
        StarSystem sys, int central, Vector3d p0, Vector3d v0, double t0, double days,
        double posAtol = 1e-4d, double velAtol = 1e-8d, double rtol = 1e-11d)
    {
        const double maxStep = 600d;
        const double minStep = 1e-6d;
        double T = t0 + (days * 86400d);
        double t = t0;
        double dt = Math.Min(maxStep, T - t0);
        var p = p0;
        var v = v0;
        var kp = new Vector3d[12];
        var kv = new Vector3d[12];
        bool fsal = false;
        Vector3d fsalP = Vector3d.Zero;
        Vector3d fsalV = Vector3d.Zero;
        long calls = 0;
        var sw = Stopwatch.StartNew();
        while (t < T - 1e-9d)
        {
            double h = Math.Min(dt, T - t);
            Vector3d kp0;
            Vector3d kv0;
            if (fsal)
            {
                kp0 = fsalP;
                kv0 = fsalV;
            }
            else
            {
                kp0 = v;
                kv0 = sys.EvaluateShipAccelerationCached(p, t);
                calls++;
            }

            kp[0] = kp0;
            kv[0] = kv0;
            for (int s = 1; s < 12; s++)
            {
                Vector3d dp = Vector3d.Zero;
                Vector3d dv = Vector3d.Zero;
                for (int j = 0; j < s; j++)
                {
                    double a = StudyDop853A[s][j];
                    if (a == 0d)
                    {
                        continue;
                    }

                    dp += kp[j] * a;
                    dv += kv[j] * a;
                }

                Vector3d ps = p + (dp * h);
                Vector3d vs = v + (dv * h);
                kp[s] = vs;
                kv[s] = sys.EvaluateShipAccelerationCached(ps, t + (StudyDop853C[s] * h));
                calls++;
            }

            Vector3d pn = p;
            Vector3d vn = v;
            for (int j = 0; j < 12; j++)
            {
                double b = StudyDop853B[j];
                if (b == 0d)
                {
                    continue;
                }

                pn += kp[j] * (h * b);
                vn += kv[j] * (h * b);
            }

            Vector3d aNew = sys.EvaluateShipAccelerationCached(pn, t + h);
            calls++;

            Vector3d ep = Vector3d.Zero;
            Vector3d ev = Vector3d.Zero;
            for (int j = 0; j < 12; j++)
            {
                double e = StudyDop853E5[j];
                if (e == 0d)
                {
                    continue;
                }

                ep += kp[j] * (h * e);
                ev += kv[j] * (h * e);
            }

            sys.EvaluateBodyState(sys.AllBodies[central], t, out Vector3d bc, out Vector3d bv);
            sys.EvaluateBodyState(sys.AllBodies[central], t + h, out Vector3d bc2, out Vector3d bv2);
            double floor = ToleranceReference.FloatingPointFloor;
            double scaleP = posAtol
                + (rtol * Math.Max((p - bc).Magnitude, (pn - bc2).Magnitude))
                + (floor * Math.Max(p.Magnitude, pn.Magnitude));
            double scaleV = velAtol
                + (rtol * Math.Max((v - bv).Magnitude, (vn - bv2).Magnitude))
                + (floor * Math.Max(v.Magnitude, vn.Magnitude));
            double err = Math.Max(ep.Magnitude / scaleP, ev.Magnitude / scaleV);

            if (err <= 1d)
            {
                p = pn;
                v = vn;
                t += h;
                fsalP = vn;
                fsalV = aNew;
                fsal = true;
                double factor = Math.Min(5d, Math.Max(0.2d,
                    0.9d * Math.Pow(1d / Math.Max(err, 1e-10d), 1d / 8d)));
                dt = Math.Min(maxStep, Math.Max(minStep, h * factor));
            }
            else
            {
                double factor = Math.Min(0.5d, Math.Max(0.1d,
                    0.9d * Math.Pow(1d / Math.Max(err, 1e-10d), 1d / 8d)));
                dt = Math.Max(minStep, h * factor);
                fsal = false;
            }
        }

        sw.Stop();
        return (p, v, calls, sw.Elapsed.TotalMilliseconds);
    }

    private static (Vector3d p, Vector3d v, long calls, double ms) StudyShipDop853Local(
        StarSystem sys, int central, Vector3d relP0, Vector3d relV0,
        double t0, double days, double posAtol = 1e-4d, double velAtol = 1e-8d,
        double rtol = 1e-11d, bool directForce = false)
    {
        const double maxStep = 600d;
        const double minStep = 1e-6d;
        double T = t0 + (days * 86400d);
        double t = t0;
        double tLo = 0d;
        double dt = Math.Min(maxStep, T - t0);
        var p = relP0;
        var v = relV0;
        var kp = new Vector3d[12];
        var kv = new Vector3d[12];
        bool fsal = false;
        Vector3d fsalP = Vector3d.Zero;
        Vector3d fsalV = Vector3d.Zero;
        long calls = 0;
        var sw = Stopwatch.StartNew();
        Vector3d Acc(Vector3d rp, double tt) => directForce
            ? StudyLocalAccelerationDirect(sys, central, rp, tt)
            : StudyLocalAcceleration(sys, central, rp, tt);
        while (t < T - 1e-9d)
        {
            double h = Math.Min(dt, T - t);
            Vector3d kp0;
            Vector3d kv0;
            if (fsal)
            {
                kp0 = fsalP;
                kv0 = fsalV;
            }
            else
            {
                kp0 = v;
                kv0 = Acc(p, t);
                calls++;
            }

            kp[0] = kp0;
            kv[0] = kv0;
            for (int s = 1; s < 12; s++)
            {
                Vector3d dp = Vector3d.Zero;
                Vector3d dv = Vector3d.Zero;
                for (int j = 0; j < s; j++)
                {
                    double a = StudyDop853A[s][j];
                    if (a == 0d)
                    {
                        continue;
                    }

                    dp += kp[j] * a;
                    dv += kv[j] * a;
                }

                Vector3d ps = p + (dp * h);
                Vector3d vs = v + (dv * h);
                kp[s] = vs;
                kv[s] = Acc(ps, t + (StudyDop853C[s] * h));
                calls++;
            }

            Vector3d pn = p;
            Vector3d vn = v;
            for (int j = 0; j < 12; j++)
            {
                double b = StudyDop853B[j];
                if (b == 0d)
                {
                    continue;
                }

                pn += kp[j] * (h * b);
                vn += kv[j] * (h * b);
            }

            Vector3d aNew = Acc(pn, t + h);
            calls++;

            Vector3d ep = Vector3d.Zero;
            Vector3d ev = Vector3d.Zero;
            for (int j = 0; j < 12; j++)
            {
                double e = StudyDop853E5[j];
                if (e == 0d)
                {
                    continue;
                }

                ep += kp[j] * (h * e);
                ev += kv[j] * (h * e);
            }

            double floor = ToleranceReference.FloatingPointFloor;
            double scaleP = posAtol
                + (rtol * Math.Max(p.Magnitude, pn.Magnitude))
                + (floor * Math.Max(p.Magnitude, pn.Magnitude));
            double scaleV = velAtol
                + (rtol * Math.Max(v.Magnitude, vn.Magnitude))
                + (floor * Math.Max(v.Magnitude, vn.Magnitude));
            double err = Math.Max(ep.Magnitude / scaleP, ev.Magnitude / scaleV);
            if (!double.IsFinite(err))
            {
                err = 1e3d;
            }

            if (err <= 1d)
            {
                p = pn;
                v = vn;
                StudyAdd(ref t, ref tLo, h);
                fsalP = vn;
                fsalV = aNew;
                fsal = true;
                double factor = Math.Min(5d, Math.Max(0.2d,
                    0.9d * Math.Pow(1d / Math.Max(err, 1e-10d), 1d / 8d)));
                dt = Math.Min(maxStep, Math.Max(minStep, h * factor));
            }
            else
            {
                double factor = Math.Min(0.5d, Math.Max(0.1d,
                    0.9d * Math.Pow(1d / Math.Max(err, 1e-10d), 1d / 8d)));
                dt = Math.Max(minStep, h * factor);
                fsal = false;
            }
        }

        sw.Stop();
        return (p, v, calls, sw.Elapsed.TotalMilliseconds);
    }

    private static (Vector3d p, Vector3d v, long calls, double ms) StudyShipGame(
        StarSystem sys, Vector3d p0, Vector3d v0, double t0, double days)
    {
        var phys = new SpacecraftPhysics { ToleranceFrameSystem = sys };
        var counting = new CountingSource(new CachedGravitySource(sys));
        phys.Sources.Add(counting);
        var prop = new EventDrivenPropagator(phys)
        {
            MaxChunkSeconds = 600d,
            DetectionSubdivisions = 20,
            UseDenseRefinement = true
        };
        var ship = new Spacecraft(p0, v0, 1000d);
        var sw = Stopwatch.StartNew();
        // Propagate takes a DURATION, not an absolute target time.
        prop.Propagate(ship, t0, days * 86400d);
        sw.Stop();
        return (ship.Position, ship.Velocity, counting.Calls, sw.Elapsed.TotalMilliseconds);
    }

    private static StarSystem StudyCloneBakedSystem(StarSystem source, bool custom)
    {
        StarSystem clone = custom ? StudyCustomSystem() : StudyRealSolar7();
        int n = Math.Min(clone.AllBodies.Count, source.AllBodies.Count);
        for (int i = 0; i < n; i++)
        {
            clone.AllBodies[i].Baked = source.AllBodies[i].Baked;
        }

        clone.InvalidatePositionCache();
        return clone;
    }

    private static void StudyShipTable(
        string label, StarSystem sys, StudyNBody nb, int central, double rp, double ra, double days)
    {
        bool custom = label.IndexOf("Custom", StringComparison.Ordinal) >= 0;
        Console.WriteLine();
        Console.WriteLine("=== " + label + ": rp={0:E3} m, ra={1:E3} m, {2} d ===", rp, ra, days);
        Console.WriteLine("ship state is local to the central body; the game DOPRI5 row is the actual world-state path");
        Console.WriteLine("'direct' forms differential acceleration from relative vectors; 'field-diff' subtracts two world-field samples");
        OrbitingBody body = sys.AllBodies[central];
        double mu = body.ResolveStandardGravitationalParameter();
        double t0 = StudyShipT0;
        double T = t0 + days * 86400d;
        sys.EvaluateBodyState(body, t0, out Vector3d bp, out Vector3d bv);
        StudyShipOrbit(rp, ra, mu, out Vector3d relP, out Vector3d relV);
        Vector3d p0 = bp + relP;
        Vector3d v0 = bv + relV;

        var refA = StudyShipRefLocal(sys, central, relP, relV, t0, days, 2.5d);
        var refAHalf = StudyShipRefLocal(sys, central, relP, relV, t0, days, 1.25d);

        // Full n-body reference B: bodies at t0, then the massless ship.
        var bodiesAtT0 = StudyRefRun(nb, "Y6C", 10d, t0 / StudyYear, 0d);
        var shipNb = new StudyNBody
        {
            Names = new string[nb.Count + 1],
            Mu = new double[nb.Count + 1],
            P0 = new double[3 * (nb.Count + 1)],
            V0 = new double[3 * (nb.Count + 1)]
        };
        Array.Copy(bodiesAtT0.P, shipNb.P0, 3 * nb.Count);
        Array.Copy(bodiesAtT0.V, shipNb.V0, 3 * nb.Count);
        Array.Copy(nb.Mu, shipNb.Mu, nb.Count);
        Array.Copy(nb.Names, shipNb.Names, nb.Count);
        shipNb.Names[nb.Count] = "Ship";
        shipNb.Mu[nb.Count] = 0d;
        shipNb.P0[3 * nb.Count] = bodiesAtT0.P[3 * central] + relP.X;
        shipNb.P0[(3 * nb.Count) + 1] = bodiesAtT0.P[(3 * central) + 1] + relP.Y;
        shipNb.P0[(3 * nb.Count) + 2] = bodiesAtT0.P[(3 * central) + 2] + relP.Z;
        shipNb.V0[3 * nb.Count] = bodiesAtT0.V[3 * central] + relV.X;
        shipNb.V0[(3 * nb.Count) + 1] = bodiesAtT0.V[(3 * central) + 1] + relV.Y;
        shipNb.V0[(3 * nb.Count) + 2] = bodiesAtT0.V[(3 * central) + 2] + relV.Z;

        var refB = StudyRefRun(shipNb, "Y6C", 5d, days / 365.25d, 0d);
        int ship = nb.Count;
        Vector3d refBRel = new Vector3d(
            refB.P[3 * ship] - refB.P[3 * central],
            refB.P[(3 * ship) + 1] - refB.P[(3 * central) + 1],
            refB.P[(3 * ship) + 2] - refB.P[(3 * central) + 2]);

        sys.EvaluateBodyState(body, T, out Vector3d bpT, out _);
        Vector3d refARel = refA.p;

        Console.WriteLine("ref A local self-convergence (2.5 s vs 1.25 s): dp={0:E3} m, dv={1:E3} m/s",
            (refA.p - refAHalf.p).Magnitude, (refA.v - refAHalf.v).Magnitude);
        Console.WriteLine("ref B (full n-body, 5 s): pre={0:N0}+{1:N0} calls, {2:F0}+{3:F0} ms",
            bodiesAtT0.Calls, refB.Calls, bodiesAtT0.Ms, refB.Ms);
        Console.WriteLine("A vs B at end: {0:E3} m", (refARel - refBRel).Magnitude);

        (Vector3d p, Vector3d v, long calls, double ms) game = default;
        (Vector3d p, Vector3d v, long calls, double ms) dop853 = default;
        (Vector3d p, Vector3d v, long calls, double ms) dop853Direct = default;
        (Vector3d p, Vector3d v, long calls, double ms) y6_60 = default;
        (Vector3d p, Vector3d v, long calls, double ms) y6_30 = default;
        (Vector3d p, Vector3d v, long calls, double ms) pefrl_30 = default;
        (Vector3d p, Vector3d v, long calls, double ms) wh_120 = default;
        (Vector3d p, Vector3d v, long calls, double ms) wh_240 = default;
        (Vector3d p, Vector3d v, long calls, double ms) wh_480 = default;
        long gameAlloc = 0L;
        long dopAlloc = 0L;
        long dopDirectAlloc = 0L;
        long y6_60Alloc = 0L;
        long y6_30Alloc = 0L;
        long pefrlAlloc = 0L;
        long wh120Alloc = 0L;
        long wh240Alloc = 0L;
        long wh480Alloc = 0L;

        Parallel.Invoke(
            () =>
            {
                StarSystem methodSys = StudyCloneBakedSystem(sys, custom);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                game = StudyShipGame(methodSys, p0, v0, t0, days);
                gameAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            },
            () =>
            {
                StarSystem methodSys = StudyCloneBakedSystem(sys, custom);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                dop853 = StudyShipDop853Local(methodSys, central, relP, relV, t0, days);
                dopAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            },
            () =>
            {
                StarSystem methodSys = StudyCloneBakedSystem(sys, custom);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                dop853Direct = StudyShipDop853Local(
                    methodSys, central, relP, relV, t0, days, directForce: true);
                dopDirectAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            },
            () =>
            {
                StarSystem methodSys = StudyCloneBakedSystem(sys, custom);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                y6_60 = StudyShipFixedLocal(
                    methodSys, central, relP, relV, t0, days, 60d, "Y6", directForce: true);
                y6_60Alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            },
            () =>
            {
                StarSystem methodSys = StudyCloneBakedSystem(sys, custom);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                y6_30 = StudyShipFixedLocal(
                    methodSys, central, relP, relV, t0, days, 30d, "Y6", directForce: true);
                y6_30Alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            },
            () =>
            {
                StarSystem methodSys = StudyCloneBakedSystem(sys, custom);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                pefrl_30 = StudyShipFixedLocal(
                    methodSys, central, relP, relV, t0, days, 30d, "PEFRL", directForce: true);
                pefrlAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            },
            () =>
            {
                StarSystem methodSys = StudyCloneBakedSystem(sys, custom);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                wh_120 = StudyShipWhLocal(
                    methodSys, central, relP, relV, t0, days, 120d, directForce: true);
                wh120Alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            },
            () =>
            {
                StarSystem methodSys = StudyCloneBakedSystem(sys, custom);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                wh_240 = StudyShipWhLocal(
                    methodSys, central, relP, relV, t0, days, 240d, directForce: true);
                wh240Alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            },
            () =>
            {
                StarSystem methodSys = StudyCloneBakedSystem(sys, custom);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                wh_480 = StudyShipWhLocal(
                    methodSys, central, relP, relV, t0, days, 480d, directForce: true);
                wh480Alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            });

        Vector3d gameRel = game.p - bpT;
        Console.WriteLine("{" + "system,-24} {" + "method,-22} {" + "h, s,6} {" + "calls,11} {" +
            "err vs A, m,13} {" + "err vs B, m,13} {" + "ms,8} {" + "us/call,9} {" + "KiB,8}");
        PrintShipRow(label, "DOPRI5 (game, world)", 0d, game.calls, gameRel, refARel, refBRel, game.ms, gameAlloc);
        PrintShipRow(label, "DOP853 field-diff", 0d, dop853.calls, dop853.p, refARel, refBRel, dop853.ms, dopAlloc);
        PrintShipRow(label, "DOP853 direct", 0d, dop853Direct.calls, dop853Direct.p, refARel, refBRel, dop853Direct.ms, dopDirectAlloc);
        PrintShipRow(label, "Y6 direct", 60d, y6_60.calls, y6_60.p, refARel, refBRel, y6_60.ms, y6_60Alloc);
        PrintShipRow(label, "Y6 direct", 30d, y6_30.calls, y6_30.p, refARel, refBRel, y6_30.ms, y6_30Alloc);
        PrintShipRow(label, "PEFRL direct", 30d, pefrl_30.calls, pefrl_30.p, refARel, refBRel, pefrl_30.ms, pefrlAlloc);
        PrintShipRow(label, "WH Y6 direct", 120d, wh_120.calls, wh_120.p, refARel, refBRel, wh_120.ms, wh120Alloc);
        PrintShipRow(label, "WH Y6 direct", 240d, wh_240.calls, wh_240.p, refARel, refBRel, wh_240.ms, wh240Alloc);
        PrintShipRow(label, "WH Y6 direct", 480d, wh_480.calls, wh_480.p, refARel, refBRel, wh_480.ms, wh480Alloc);
        Console.WriteLine();
    }

    private static void PrintShipRow(
        string label, string method, double h, long calls, Vector3d rel,
        Vector3d refARel, Vector3d refBRel, double ms, long allocBytes)
    {
        double usPerCall = calls > 0L ? ms * 1000d / calls : 0d;
        Console.WriteLine(
            "{0,-24} {1,-22} {2,6:F0} {3,11:N0} {4,13:E3} {5,13:E3} {6,8:F0} {7,9:F2} {8,8:N0}",
            label, method, h, calls, (rel - refARel).Magnitude, (rel - refBRel).Magnitude,
            ms, usPerCall, allocBytes / 1024d);
    }

    // ------------------------------------------------------------------
    // Live n-body cost
    // ------------------------------------------------------------------

    private static void StudyLiveCost(StudyNBody custom, StudyNBody real)
    {
        Console.WriteLine();
        Console.WriteLine("=== Live n-body cost (Y6 7200 s, 1 year) ===");
        var c = StudyRefRun(custom, "Y6", 7200d, 1d, 0d);
        var r = StudyRefRun(real, "Y6", 7200d, 1d, 0d);
        Console.WriteLine("custom 6 bodies: calls={0:N0}, {1:F1} ms/year", c.Calls, c.Ms);
        Console.WriteLine("real 7 bodies:   calls={0:N0}, {1:F1} ms/year", r.Calls, r.Ms);
    }

    // ------------------------------------------------------------------
    // Event / warp path
    // ------------------------------------------------------------------

    private static void StudyEvent(StarSystem sys)
    {
        Console.WriteLine();
        Console.WriteLine("=== Warp event path (custom Terra, atmosphere shell 2e6 m) ===");
        OrbitingBody terra = sys.AllBodies[1];
        double mu = terra.ResolveStandardGravitationalParameter();
        double ra = 4.0e7d;
        double t0 = StudyShipT0;
        sys.EvaluateBodyState(terra, t0, out Vector3d bp, out Vector3d bv);
        double a = 0.5d * (1.543e6d + ra);
        double va = Math.Sqrt(mu * (2d / ra - 1d / a));
        var p0 = bp + new Vector3d(ra, 0d, 0d);
        var v0 = bv + new Vector3d(0d, va, 0d);

        var phys = new SpacecraftPhysics { ToleranceFrameSystem = sys };
        phys.Sources.Add(new CachedGravitySource(sys));
        var prop = new EventDrivenPropagator(phys)
        {
            MaxChunkSeconds = 600d,
            DetectionSubdivisions = 20,
            UseDenseRefinement = true,
            SystemForGrain = sys
        };
        prop.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereEntry(terra));
        var driver = new LongWarpDriver(phys, prop);
        var ship = new Spacecraft(p0, v0, 1000d);
        LongWarpResult res = driver.AdvanceToTarget(ship, t0, t0 + (2d * 86400d));
        Console.WriteLine("game: event={0}, t={1:F3} s, stopped={2}",
            res.StoppingEvent.HasValue ? res.StoppingEvent.Value.Kind.ToString() : "none",
            res.StoppingEvent.HasValue ? res.StoppingEvent.Value.TimeSeconds : -1d,
            !res.ReachedTarget);

        // Reference event time from the same ship Y6C at 5 s, linear bracket.
        double T = t0 + (2d * 86400d);
        var p = new[] { p0.X, p0.Y, p0.Z };
        var v = new[] { v0.X, v0.Y, v0.Z };
        double t = t0;
        double prevT = t0;
        sys.EvaluateBodyState(terra, t0, out Vector3d b0, out _);
        double prevG = Math.Sqrt(
            ((p[0] - b0.X) * (p[0] - b0.X)) + ((p[1] - b0.Y) * (p[1] - b0.Y)) + ((p[2] - b0.Z) * (p[2] - b0.Z)))
            - (terra.Radius + 2e6d);
        bool found = false;
        double eventT = -1d;
        while (t < T - 1e-9d && !found)
        {
            double hStep = Math.Min(5d, T - t);
            StudyShipFixedStep(sys, p, v, t, hStep);
            t += hStep;
            sys.EvaluateBodyState(terra, t, out Vector3d b, out _);
            double g = Math.Sqrt(
                ((p[0] - b.X) * (p[0] - b.X)) + ((p[1] - b.Y) * (p[1] - b.Y)) + ((p[2] - b.Z) * (p[2] - b.Z)))
                - (terra.Radius + 2e6d);
            if (prevG > 0d && g <= 0d)
            {
                eventT = prevT + (t - prevT) * (prevG / (prevG - g));
                found = true;
            }

            prevG = g;
            prevT = t;
        }

        Console.WriteLine("ref:  event={0}, t={1:F3} s", found ? "AtmosphereEntry" : "none", eventT);
        if (found && res.StoppingEvent.HasValue)
        {
            Console.WriteLine("event time error = {0:F3} s", res.StoppingEvent.Value.TimeSeconds - eventT);
        }
    }

    private static void StudyShipFixedStep(StarSystem sys, double[] p, double[] v, double t, double h)
    {
        double[] ws = StudyY6Weights();
        Vector3d acc = sys.EvaluateShipAccelerationCached(new Vector3d(p[0], p[1], p[2]), t);
        foreach (double w in ws)
        {
            double dt = w * h;
            for (int i = 0; i < 3; i++)
            {
                v[i] += 0.5d * dt * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
            }

            for (int i = 0; i < 3; i++)
            {
                p[i] += dt * v[i];
            }

            t += dt;
            acc = sys.EvaluateShipAccelerationCached(new Vector3d(p[0], p[1], p[2]), t);
            for (int i = 0; i < 3; i++)
            {
                v[i] += 0.5d * dt * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
            }
        }
    }

    // ------------------------------------------------------------------
    // Test cases
    // ------------------------------------------------------------------

    private static void StudyReferenceRow(
        string method, Vector3d rel, Vector3d refARel, Vector3d refBRel,
        long calls, double ms, long allocBytes)
    {
        double us = calls > 0L ? ms * 1000d / calls : 0d;
        Console.WriteLine(
            "{0,-18} {1,12:E3} {2,12:E3} {3,10:N0} {4,8:F0} {5,7:F2} {6,9:N0}",
            method, (rel - refARel).Magnitude, (rel - refBRel).Magnitude,
            calls, ms, us, allocBytes / 1024d);
    }

    private static void StudyReferenceCase(
        string label, StarSystem sys, StudyNBody nb, int central, double rp, double ra, double days, bool dd)
    {
        Console.WriteLine();
        Console.WriteLine("=== Reference convergence: " + label + " ===");
        OrbitingBody body = sys.AllBodies[central];
        double mu = body.ResolveStandardGravitationalParameter();
        double t0 = StudyShipT0;
        double T = t0 + (days * 86400d);
        sys.EvaluateBodyState(body, t0, out Vector3d bp, out Vector3d bv);
        StudyShipOrbit(rp, ra, mu, out Vector3d relP, out Vector3d relV);
        var p0 = bp + relP;
        var v0 = bv + relV;
        sys.EvaluateBodyState(body, T, out Vector3d bpT, out _);

        var r5 = StudyShipRefA(sys, p0, v0, t0, days, 5d);
        var r25 = StudyShipRefA(sys, p0, v0, t0, days, 2.5d);
        var r125 = StudyShipRefA(sys, p0, v0, t0, days, 1.25d);
        var r0625 = StudyShipRefA(sys, p0, v0, t0, days, 0.625d);
        double d1 = (r5.p - r25.p).Magnitude;
        double d2 = (r25.p - r125.p).Magnitude;
        double d3 = (r125.p - r0625.p).Magnitude;
        Console.WriteLine("Y6C 5->2.5: {0:E3} m; 2.5->1.25: {1:E3}; 1.25->0.625: {2:E3}; ratios {3:F1}/{4:F1}",
            d1, d2, d3, d1 / Math.Max(d2, 1e-30), d2 / Math.Max(d3, 1e-30));
        Console.WriteLine("reference error estimate (0.625 s): {0:E3} m", d3 / 63d);

        Vector3d refARel = r0625.p - bpT;

        var bodiesAtT0 = StudyRefRun(nb, "Y6C", 5d, t0 / StudyYear, 0d);
        var shipNb = new StudyNBody
        {
            Names = new string[nb.Count + 1],
            Mu = new double[nb.Count + 1],
            P0 = new double[3 * (nb.Count + 1)],
            V0 = new double[3 * (nb.Count + 1)]
        };
        Array.Copy(bodiesAtT0.P, shipNb.P0, 3 * nb.Count);
        Array.Copy(bodiesAtT0.V, shipNb.V0, 3 * nb.Count);
        Array.Copy(nb.Mu, shipNb.Mu, nb.Count);
        Array.Copy(nb.Names, shipNb.Names, nb.Count);
        shipNb.Names[nb.Count] = "Ship";
        shipNb.Mu[nb.Count] = 0d;
        shipNb.P0[3 * nb.Count] = bodiesAtT0.P[3 * central] + relP.X;
        shipNb.P0[(3 * nb.Count) + 1] = bodiesAtT0.P[(3 * central) + 1] + relP.Y;
        shipNb.P0[(3 * nb.Count) + 2] = bodiesAtT0.P[(3 * central) + 2] + relP.Z;
        shipNb.V0[3 * nb.Count] = bodiesAtT0.V[3 * central] + relV.X;
        shipNb.V0[(3 * nb.Count) + 1] = bodiesAtT0.V[(3 * central) + 1] + relV.Y;
        shipNb.V0[(3 * nb.Count) + 2] = bodiesAtT0.V[(3 * central) + 2] + relV.Z;
        var refB5 = StudyRefRun(shipNb, "Y6C", 5d, days / 365.25d, 0d);
        var refB25 = StudyRefRun(shipNb, "Y6C", 2.5d, days / 365.25d, 0d);
        int ship = nb.Count;
        Vector3d refBRel = new Vector3d(
            refB5.P[3 * ship] - refB5.P[3 * central],
            refB5.P[(3 * ship) + 1] - refB5.P[(3 * central) + 1],
            refB5.P[(3 * ship) + 2] - refB5.P[(3 * central) + 2]);
        Vector3d refBRel25 = new Vector3d(
            refB25.P[3 * ship] - refB25.P[3 * central],
            refB25.P[(3 * ship) + 1] - refB25.P[(3 * central) + 1],
            refB25.P[(3 * ship) + 2] - refB25.P[(3 * central) + 2]);
        Console.WriteLine("A vs B: {0:E3} m; B self 5 vs 2.5: {1:E3} m",
            (refARel - refBRel).Magnitude, (refBRel - refBRel25).Magnitude);

        Console.WriteLine("{0,-18} {1,12} {2,12} {3,10} {4,8} {5,7} {6,9}",
            "method", "err A,m", "err B,m", "calls", "ms", "us/call", "KiB");

        long a0 = GC.GetAllocatedBytesForCurrentThread();
        var game = StudyShipGame(sys, p0, v0, t0, days);
        long gameAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        StudyReferenceRow("DOPRI5 (game)", game.p - bpT, refARel, refBRel, game.calls, game.ms, gameAlloc);

        a0 = GC.GetAllocatedBytesForCurrentThread();
        var dop = StudyShipDop853(sys, central, p0, v0, t0, days);
        long dopAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        StudyReferenceRow("DOP853", dop.p - bpT, refARel, refBRel, dop.calls, dop.ms, dopAlloc);

        a0 = GC.GetAllocatedBytesForCurrentThread();
        var dopTight = StudyShipDop853(sys, central, p0, v0, t0, days, 1e-12d, 1e-15d, 1e-14d);
        long tightAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        StudyReferenceRow("DOP853 tight", dopTight.p - bpT, refARel, refBRel, dopTight.calls, dopTight.ms, tightAlloc);

        a0 = GC.GetAllocatedBytesForCurrentThread();
        var y6 = StudyShipFixed(sys, p0, v0, t0, days, 30d, "Y6");
        long y6Alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        StudyReferenceRow("Y6 30 s", y6.p - bpT, refARel, refBRel, y6.calls, y6.ms, y6Alloc);

        a0 = GC.GetAllocatedBytesForCurrentThread();
        var wh = StudyShipWh(sys, central, p0, v0, t0, days, 480d);
        long whAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        StudyReferenceRow("WH 480 s", wh.p - bpT, refARel, refBRel, wh.calls, wh.ms, whAlloc);

        if (dd)
        {
            a0 = GC.GetAllocatedBytesForCurrentThread();
            var ddRun = StudyShipY6CDd(sys, p0, v0, t0, days, 5d);
            long ddAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            StudyReferenceRow("Y6C double-double", ddRun.p - bpT, refARel, refBRel, ddRun.calls, ddRun.ms, ddAlloc);
        }

        Console.WriteLine("ref B 5 s: calls={0:N0}, {1:F0} ms", refB5.Calls, refB5.Ms);
    }

    private static void StudyKeplerReference()
    {
        Console.WriteLine();
        Console.WriteLine("=== Exact Kepler one-body reference (30 d) ===");
        double mu = 3.986004418e14d;
        double r = 6.771e6d;
        double v = Math.Sqrt(mu / r);
        var p0 = new Vector3d(r, 0d, 0d);
        var v0 = new Vector3d(0d, v, 0d);
        var star = new OrbitingBody { Name = "TestBody", StandardGravitationalParameter = mu, Radius = 1d };
        StarSystem one = new StarSystem(star);
        StudyKeplerDrift(p0, v0, mu, 30d * 86400d, out Vector3d re, out Vector3d ve);

        var y6c = StudyShipRefA(one, p0, v0, 0d, 30d, 5d);
        var dop = StudyShipDop853(one, 0, p0, v0, 0d, 30d, 1e-12d, 1e-15d, 1e-14d);
        var dd = StudyShipY6CDd(one, p0, v0, 0d, 30d, 5d);
        var y6 = StudyShipFixed(one, p0, v0, 0d, 30d, 30d, "Y6");
        Console.WriteLine("exact vs Y6C 5 s:   dp={0:E3} m, dv={1:E3} m/s",
            (y6c.p - re).Magnitude, (y6c.v - ve).Magnitude);
        Console.WriteLine("exact vs DOP853 tight: dp={0:E3} m, dv={1:E3} m/s",
            (dop.p - re).Magnitude, (dop.v - ve).Magnitude);
        Console.WriteLine("exact vs Y6C double-double: dp={0:E3} m, dv={1:E3} m/s",
            (dd.p - re).Magnitude, (dd.v - ve).Magnitude);
        Console.WriteLine("exact vs Y6 30 s:   dp={0:E3} m, dv={1:E3} m/s",
            (y6.p - re).Magnitude, (y6.v - ve).Magnitude);
    }

    private static void StudyTimeReversal()
    {
        Console.WriteLine();
        Console.WriteLine("=== Y6C time reversal, exact one-body (30 d) ===");
        double mu = 3.986004418e14d;
        double r = 6.771e6d;
        double v = Math.Sqrt(mu / r);
        var p0 = new Vector3d(r, 0d, 0d);
        var v0 = new Vector3d(0d, v, 0d);
        var star = new OrbitingBody { Name = "TestBody", StandardGravitationalParameter = mu, Radius = 1d };
        StarSystem one = new StarSystem(star);
        var fwd = StudyShipRefA(one, p0, v0, 0d, 30d, 5d);
        var back = StudyShipRefA(one, fwd.p, -fwd.v, 30d * 86400d, 30d, 5d);
        Console.WriteLine("|p_back-p0|={0:E3} m, |v_back+v0|={1:E3} m/s",
            (back.p - p0).Magnitude, (back.v + v0).Magnitude);
    }

    private static void StudyPlanetReference(StudyNBody nb, double years)
    {
        Console.WriteLine();
        Console.WriteLine("=== Planet reference convergence: " + years + " y ===");
        var r112 = StudyRefRun(nb, "Y6C", 112.5d, years, 0d);
        var r56 = StudyRefRun(nb, "Y6C", 56.25d, years, 0d);
        var r28 = StudyRefRun(nb, "Y6C", 28.125d, years, 0d);
        Console.WriteLine("112.5->56.25: {0:E3} m; 56.25->28.125: {1:E3} m; ratio {2:F1}",
            StudyRel(r112.P, 2, 1, r56.P),
            StudyRel(r56.P, 2, 1, r28.P),
            StudyRel(r112.P, 2, 1, r56.P) / Math.Max(StudyRel(r56.P, 2, 1, r28.P), 1e-30));
        var y6 = StudyRefRun(nb, "Y6", 7200d, years, 0d);
        var rk4 = StudyRefRun(nb, "RK4", 900d, years, 0d);
        double e0 = StudyEnergy(nb.P0, nb.V0, nb.Mu);
        Console.WriteLine("Y6 7200: error={0:E3} m, dE/E={1:E2}",
            StudyRel(y6.P, 2, 1, r28.P),
            Math.Abs((StudyEnergy(y6.P, y6.V, nb.Mu) - e0) / e0));
        Console.WriteLine("RK4 900: error={0:E3} m, dE/E={1:E2}",
            StudyRel(rk4.P, 2, 1, r28.P),
            Math.Abs((StudyEnergy(rk4.P, rk4.V, nb.Mu) - e0) / e0));
    }

    private static int Test503_ReferenceConvergence()
    {
        Console.WriteLine("--- Reference convergence ---");
        StarSystem real = StudyRealSolar7();
        StudyNBody realNb = StudyMakeNBody(real);
        StudyBake("Reference real", real, realNb, 1d);

        StarSystem custom = StudyCustomSystem();
        StudyNBody customNb = StudyMakeNBody(custom);
        StudyBake("Reference custom", custom, customNb, 1d);

        StudyReferenceCase("Earth LEO 400 km", real, realNb, 1, 6.771e6d, 6.771e6d, 30d, false);
        StudyReferenceCase("Earth ecc 400/40000", real, realNb, 1, 6.771e6d, 4.6371e7d, 30d, true);
        StudyReferenceCase("Terra LEO 400 km", custom, customNb, 1, 1.543e6d, 1.543e6d, 30d, false);
        StudyReferenceCase("Terra ecc 400/40000", custom, customNb, 1, 1.543e6d, 4.114e7d, 30d, true);

        StudyKeplerReference();
        StudyTimeReversal();
        StudyPlanetReference(realNb, 10d);

        Console.WriteLine("--- Reference convergence complete ---");
        return 0;
    }

    private static int Test500_IntegratorStudyFast()
    {
        Console.WriteLine("--- Integrator study: fast ---");
        StarSystem custom = StudyCustomSystem();
        StudyNBody customNb = StudyMakeNBody(custom);
        StarSystem real = StudyRealSolar7();
        StudyNBody realNb = StudyMakeNBody(real);
        StudySystemTable("Custom system", custom);
        StudySystemTable("Real solar 7-body (report fixture)", real);

        StudyPlanetTable("Real solar 7-body 10 y", realNb, 10d, 112.5d, true);
        StudyBake("Real solar 7-body", real, realNb, 10d);
        StudyPlanetTable("Custom 10 y", customNb, 10d, 112.5d, true);
        StudyBake("Custom", custom, customNb, 10d);

        StudyShipTable("Real solar: Earth LEO 400 km", real, realNb, 1, 6.371e6d + 4e5d, 6.371e6d + 4e5d, 30d);
        StudyShipTable("Real solar: Earth eccentric 400/40000 km", real, realNb, 1, 6.371e6d + 4e5d, 6.371e6d + 4e7d, 30d);
        StudyShipTable("Real solar: Sun 1 AU coast", real, realNb, 0, StudyAU, StudyAU, 30d);
        StudyShipTable("Real solar: Sun high-e 0.1/5 AU", real, realNb, 0, 0.1d * StudyAU, 5d * StudyAU, 30d);
        StudyShipTable("Custom: Terra LEO 400 km", custom, customNb, 1, 1.143e6d + 4e5d, 1.143e6d + 4e5d, 30d);
        StudyShipTable("Custom: Terra eccentric 400/40000 km", custom, customNb, 1, 1.143e6d + 4e5d, 1.143e6d + 4e7d, 30d);

        StudyLiveCost(customNb, realNb);
        StudyEvent(custom);
        Console.WriteLine("--- Integrator study: fast complete ---");
        return 0;
    }

    private static int Test501_IntegratorStudySlow()
    {
        Console.WriteLine("--- Integrator study: slow ---");
        StarSystem custom = StudyCustomSystem();
        StudyNBody customNb = StudyMakeNBody(custom);
        StarSystem real = StudyRealSolar7();
        StudyNBody realNb = StudyMakeNBody(real);

        StudyPlanetTable("Custom 100 y", customNb, 100d, 112.5d, false);
        StudyBake("Custom", custom, customNb, 100d);
        StudyPlanetTable("Real solar 7-body 100 y", realNb, 100d, 112.5d, false);
        StudyBake("Real solar 7-body", real, realNb, 100d);

        StudyLiveCost(customNb, realNb);
        Console.WriteLine("--- Integrator study: slow complete ---");
        return 0;
    }
}

