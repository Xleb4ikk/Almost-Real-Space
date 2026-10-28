using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

internal static partial class P1bTests
{
    private sealed class JplCompareModel
    {
        public readonly string Name;
        public readonly StudyNBody Nb;
        public readonly bool Pn;
        public readonly bool Eih;
        public readonly bool J2;
        public readonly int AsteroidStart;
        public readonly bool EarthJ2;

        public JplCompareModel(
            string name, StudyNBody nb, bool pn, bool eih, bool j2 = false, int asteroidStart = -1,
            bool earthJ2 = false)
        {
            Name = name;
            Nb = nb;
            Pn = pn;
            Eih = eih;
            J2 = j2;
            AsteroidStart = asteroidStart;
            EarthJ2 = earthJ2;
        }
    }

    private static (double dp, double dv) JplStateError(
        JplFile jpl, StudyNBody nb, double[] p, double[] v, double days, string body, bool geo)
    {
        int sun = JplIndex(nb, "Sun");
        int index = JplIndex(nb, body);
        int baseIndex = geo ? JplIndex(nb, "Earth") : sun;
        JplState state = jpl.Bodies.First(b => b.Name == body)
            .States.First(s => Math.Abs(s.Days - days) < 1e-9d);
        double basePx = 0d;
        double basePy = 0d;
        double basePz = 0d;
        double baseVx = 0d;
        double baseVy = 0d;
        double baseVz = 0d;
        if (geo)
        {
            JplState earth = jpl.Bodies.First(b => b.Name == "Earth")
                .States.First(s => Math.Abs(s.Days - days) < 1e-9d);
            basePx = earth.P[0];
            basePy = earth.P[1];
            basePz = earth.P[2];
            baseVx = earth.V[0];
            baseVy = earth.V[1];
            baseVz = earth.V[2];
        }

        double dx = (p[3 * index] - p[3 * baseIndex]) - (state.P[0] - basePx);
        double dy = (p[(3 * index) + 1] - p[(3 * baseIndex) + 1]) - (state.P[1] - basePy);
        double dz = (p[(3 * index) + 2] - p[(3 * baseIndex) + 2]) - (state.P[2] - basePz);
        double dvx = (v[3 * index] - v[3 * baseIndex]) - (state.V[0] - baseVx);
        double dvy = (v[(3 * index) + 1] - v[(3 * baseIndex) + 1]) - (state.V[1] - baseVy);
        double dvz = (v[(3 * index) + 2] - v[(3 * baseIndex) + 2]) - (state.V[2] - baseVz);
        double dp = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        double dv = Math.Sqrt((dvx * dvx) + (dvy * dvy) + (dvz * dvz));
        return (dp, dv);
    }

    private static int JplFullComparison(string path, string label, bool gate)
    {
        Console.WriteLine("--- Full solar-system comparison against JPL Horizons (DE441), " + label + " ---");
        Check(File.Exists(path), "JPL vectors file", path);
        if (!File.Exists(path))
        {
            return 0;
        }

        JplFile jpl = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(path));
        Console.WriteLine("epoch: " + jpl.Epoch + " | frame: " + jpl.Frame + " | source: " + jpl.Source);
        if (gate)
        {
            Console.WriteLine("note: Test538 checks the same full small-body model on the independent 2016-2026 window.");
        }
        string[] ten =
        {
            "Mercury", "Venus", "Earth", "Moon", "Mars",
            "Jupiter", "Saturn", "Uranus", "Neptune"
        };
        var fullNames = new List<string>(ten);
        foreach (JplBody body in jpl.Bodies)
        {
            if (body.Name.StartsWith("Ast", StringComparison.Ordinal)
                || body.Name.StartsWith("Kbo", StringComparison.Ordinal)
                || body.Name.StartsWith("Kbr", StringComparison.Ordinal))
            {
                fullNames.Add(body.Name);
            }
        }

        StudyNBody nb10 = JplNBody(jpl, ten, true, true);
        StudyNBody nbFullSmall = JplNBody(jpl, fullNames.ToArray(), true, true);
        var models = new[]
        {
            new JplCompareModel("Newton", nb10, false, false),
            new JplCompareModel("1PN", nb10, true, false),
            new JplCompareModel("EIH", nb10, true, true),
            new JplCompareModel("1PN+J2", nb10, true, false, true),
            new JplCompareModel("409small", nbFullSmall, true, false, false, 10),
            new JplCompareModel("409small+J2", nbFullSmall, true, false, true, 10),
            new JplCompareModel("409small+EJ2", nbFullSmall, true, false, false, 10, true),
            new JplCompareModel("409small+J2+EJ2", nbFullSmall, true, false, true, 10, true)
        };
        double[] horizons = { 365d, 1825d, 3650d };
        string[] bodies =
        {
            "Mercury", "Venus", "Earth", "Mars", "Jupiter",
            "Saturn", "Uranus", "Neptune", "Moon"
        };
        var err = new (double dp, double dv)[models.Length, horizons.Length, bodies.Length];
        int jobs = Environment.ProcessorCount;
        string jobsEnv = Environment.GetEnvironmentVariable("JPL_JOBS");
        if (!string.IsNullOrEmpty(jobsEnv)
            && int.TryParse(jobsEnv, NumberStyles.Integer, CultureInfo.InvariantCulture, out int requested)
            && requested > 0)
        {
            jobs = Math.Min(requested, Environment.ProcessorCount);
        }

        var watch = Stopwatch.StartNew();
        Parallel.For(
            0,
            models.Length * horizons.Length,
            new ParallelOptions { MaxDegreeOfParallelism = jobs },
            index =>
            {
                int m = index / horizons.Length;
                int h = index % horizons.Length;
                var run = JplDop853(
                    models[m].Nb, horizons[h], 1e-6d, 1e-9d, 1e-13d,
                    models[m].Pn, models[m].Eih, models[m].J2, 86400d, models[m].AsteroidStart, models[m].EarthJ2);
                for (int b = 0; b < bodies.Length; b++)
                {
                    bool geo = bodies[b] == "Moon";
                    err[m, h, b] = JplStateError(
                        jpl, models[m].Nb, run.p, run.v, horizons[h], bodies[b], geo);
                }
            });
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "parallel integration: {0} ms, jobs={1}", watch.ElapsedMilliseconds, jobs));

        Console.WriteLine();
        Console.WriteLine("10-year position error vs Horizons (km)");
        Console.Write(string.Format(CultureInfo.InvariantCulture, "{0,-11}", "body"));
        for (int m = 0; m < models.Length; m++)
        {
            Console.Write(string.Format(CultureInfo.InvariantCulture, "{0,12}", models[m].Name));
        }

        Console.WriteLine();
        for (int b = 0; b < bodies.Length; b++)
        {
            string name = bodies[b] == "Moon" ? "Moon(geo)" : bodies[b];
            Console.Write(string.Format(CultureInfo.InvariantCulture, "{0,-11}", name));
            for (int m = 0; m < models.Length; m++)
            {
                Console.Write(string.Format(CultureInfo.InvariantCulture, "{0,12:F2}",
                    err[m, 2, b].dp / 1000d));
            }

            Console.WriteLine();
        }

        Console.Write(string.Format(CultureInfo.InvariantCulture, "{0,-11}", "RMS(8)"));
        for (int m = 0; m < models.Length; m++)
        {
            double sum = 0d;
            for (int b = 0; b < 8; b++)
            {
                double km = err[m, 2, b].dp / 1000d;
                sum += km * km;
            }

            Console.Write(string.Format(CultureInfo.InvariantCulture, "{0,12:F2}", Math.Sqrt(sum / 8d)));
        }

        Console.WriteLine();

        Console.WriteLine();
        Console.WriteLine("10-year velocity error vs Horizons (m/s)");
        Console.Write(string.Format(CultureInfo.InvariantCulture, "{0,-11}", "body"));
        for (int m = 0; m < models.Length; m++)
        {
            Console.Write(string.Format(CultureInfo.InvariantCulture, "{0,12}", models[m].Name));
        }

        Console.WriteLine();
        for (int b = 0; b < bodies.Length; b++)
        {
            string name = bodies[b] == "Moon" ? "Moon(geo)" : bodies[b];
            Console.Write(string.Format(CultureInfo.InvariantCulture, "{0,-11}", name));
            for (int m = 0; m < models.Length; m++)
            {
                Console.Write(string.Format(CultureInfo.InvariantCulture, "{0,12:E2}",
                    err[m, 2, b].dv));
            }

            Console.WriteLine();
        }

        Console.WriteLine();
        Console.WriteLine("10-year change vs 1PN (km); negative means closer to Horizons");
        Console.Write(string.Format(CultureInfo.InvariantCulture, "{0,-11}", "body"));
        for (int m = 0; m < models.Length; m++)
        {
            if (m != 1)
            {
                Console.Write(string.Format(CultureInfo.InvariantCulture, "{0,12}", models[m].Name));
            }
        }

        Console.WriteLine();
        for (int b = 0; b < bodies.Length; b++)
        {
            string name = bodies[b] == "Moon" ? "Moon(geo)" : bodies[b];
            Console.Write(string.Format(CultureInfo.InvariantCulture, "{0,-11}", name));
            for (int m = 0; m < models.Length; m++)
            {
                if (m != 1)
                {
                    Console.Write(string.Format(CultureInfo.InvariantCulture, "{0,12:F2}",
                        (err[m, 2, b].dp - err[1, 2, b].dp) / 1000d));
                }
            }

            Console.WriteLine();
        }

        Console.WriteLine();
        Console.WriteLine("Jupiter/Saturn/Mars position error at 1, 5, 10 years (km)");
        for (int b = 0; b < bodies.Length; b++)
        {
            if (bodies[b] != "Jupiter" && bodies[b] != "Saturn" && bodies[b] != "Mars")
            {
                continue;
            }

            Console.WriteLine(bodies[b] + ":");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0,-12}{1,12}{2,12}{3,12}", "model", "1 y", "5 y", "10 y"));
            for (int m = 0; m < models.Length; m++)
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-12}{1,12:F2}{2,12:F2}{3,12:F2}",
                    models[m].Name,
                    err[m, 0, b].dp / 1000d,
                    err[m, 1, b].dp / 1000d,
                    err[m, 2, b].dp / 1000d));
            }
        }

        int jupiter = 4;
        int saturn = 5;
        bool finite = true;
        for (int m = 0; m < models.Length && finite; m++)
        {
            for (int h = 0; h < horizons.Length && finite; h++)
            {
                for (int b = 0; b < bodies.Length && finite; b++)
                {
                    finite = double.IsFinite(err[m, h, b].dp) && double.IsFinite(err[m, h, b].dv);
                }
            }
        }

        Check(finite, "all models finite", "");
        if (gate)
        {
            Check(Math.Abs(err[2, 2, jupiter].dp - err[1, 2, jupiter].dp) < 1000d,
                "EIH Jupiter shift is sub-km",
                ((err[2, 2, jupiter].dp - err[1, 2, jupiter].dp) / 1000d)
                    .ToString("F2", CultureInfo.InvariantCulture) + " km");
            Check(Math.Abs(err[2, 2, saturn].dp - err[1, 2, saturn].dp) < 1000d,
                "EIH Saturn shift is sub-km",
                ((err[2, 2, saturn].dp - err[1, 2, saturn].dp) / 1000d)
                    .ToString("F2", CultureInfo.InvariantCulture) + " km");
            Check(err[4, 2, jupiter].dp < err[1, 2, jupiter].dp, "409small improves Jupiter",
                (err[4, 2, jupiter].dp / 1000d).ToString("F2", CultureInfo.InvariantCulture) + " km");
            Check(err[4, 2, saturn].dp < err[1, 2, saturn].dp, "409small improves Saturn",
                (err[4, 2, saturn].dp / 1000d).ToString("F2", CultureInfo.InvariantCulture) + " km");
        }

        Console.WriteLine("--- Full comparison complete ---");
        return 0;
    }

    private static int Test537_JplFullComparison()
    {
        return JplFullComparison(
            Path.Combine("Study", "jpl", "horizons_real_2026_10y.json"), "2026-2036", true);
    }

    private static int Test538_JplHoldout()
    {
        return JplFullComparison(
            Path.Combine("Study", "jpl", "horizons_real_2016_10y.json"), "2016-2026 holdout", false);
    }

    private static int Test540_JplInitialAccel()
    {
        string path = Path.Combine("Study", "jpl", "horizons_real_2026_10y.json");
        Check(File.Exists(path), "JPL vectors file", path);
        if (!File.Exists(path))
        {
            return 0;
        }

        JplFile jpl = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(path));
        string[] ten =
        {
            "Mercury", "Venus", "Earth", "Moon", "Mars",
            "Jupiter", "Saturn", "Uranus", "Neptune"
        };
        StudyNBody nb = JplNBody(jpl, ten, true, true);
        int n = nb.Count;
        var a1 = new double[3 * n];
        var a2 = new double[3 * n];
        JplAcc1Pn(nb.P0, nb.V0, a1, nb.Mu, n, true);
        JplAccEih(nb.P0, nb.V0, a2, nb.Mu, n);
        Console.WriteLine("initial relative acceleration difference EIH - 1PN (m/s^2):");
        for (int i = 1; i < n; i++)
        {
            double dx = (a2[3 * i] - a2[0]) - (a1[3 * i] - a1[0]);
            double dy = (a2[(3 * i) + 1] - a2[1]) - (a1[(3 * i) + 1] - a1[1]);
            double dz = (a2[(3 * i) + 2] - a2[2]) - (a1[(3 * i) + 2] - a1[2]);
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-9} {1,12:E3} {2,12:E3} {3,12:E3}  |d|={4,10:E3}",
                nb.Names[i], dx, dy, dz, Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz))));
        }

        double marsMass = nb.Mu[JplIndex(nb, "Mars")] / 6.6743e-11d;
        double moonMass = 1.0659e16d + 1.4762e15d;
        double moonRatio = moonMass / marsMass;
        Check(moonRatio > 1e-8d && moonRatio < 5e-8d, "Mars moon mass ratio ~2e-8",
            moonRatio.ToString("E1", CultureInfo.InvariantCulture));
        Console.WriteLine("--- Initial acceleration probe complete ---");
        return 0;
    }

    private static int Test542_JplEpochConsistency()
    {
        string path2026 = Path.Combine("Study", "jpl", "horizons_real_2026_10y.json");
        string path2016 = Path.Combine("Study", "jpl", "horizons_real_2016_10y.json");
        Check(File.Exists(path2026) && File.Exists(path2016), "JPL vectors files", path2026);
        if (!File.Exists(path2026) || !File.Exists(path2016))
        {
            return 0;
        }

        JplFile jpl2026 = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(path2026));
        JplFile jpl2016 = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(path2016));
        string[] ten =
        {
            "Mercury", "Venus", "Earth", "Moon", "Mars",
            "Jupiter", "Saturn", "Uranus", "Neptune"
        };
        double worstGm = 0d;
        for (int i = 0; i < ten.Length; i++)
        {
            double a = jpl2026.Bodies.First(b => b.Name == ten[i]).Mu;
            double b = jpl2016.Bodies.First(x => x.Name == ten[i]).Mu;
            worstGm = Math.Max(worstGm, Math.Abs(a - b) / Math.Max(Math.Abs(a), 1d));
        }

        Check(worstGm < 1e-12d, "epoch GMs identical", worstGm.ToString("E2", CultureInfo.InvariantCulture));

        double worst0 = 0d;
        JplFile[] files = { jpl2026, jpl2016 };
        for (int f = 0; f < files.Length; f++)
        {
            StudyNBody nb = JplNBody(files[f], ten, true, true);
            for (int b = 0; b < ten.Length; b++)
            {
                var e0 = JplStateError(files[f], nb, nb.P0, nb.V0, 0d, ten[b], false);
                worst0 = Math.Max(worst0, e0.dp);
                worst0 = Math.Max(worst0, e0.dv * 1e6d);
            }
        }

        Check(worst0 < 1e-3d, "epoch day-0 states exact", worst0.ToString("E2", CultureInfo.InvariantCulture));
        Console.WriteLine("--- Epoch consistency probe complete ---");
        return 0;
    }

    private static (double dp, double dv) JplRelativeError(
        JplFile jpl, StudyNBody nb, double[] p, double[] v, double days, string body, string center)
    {
        int bi = JplIndex(nb, body);
        int ci = JplIndex(nb, center);
        JplState bs = jpl.Bodies.First(x => x.Name == body)
            .States.First(s => Math.Abs(s.Days - days) < 1e-9d);
        JplState cs = jpl.Bodies.First(x => x.Name == center)
            .States.First(s => Math.Abs(s.Days - days) < 1e-9d);
        double dx = (p[3 * bi] - p[3 * ci]) - (bs.P[0] - cs.P[0]);
        double dy = (p[(3 * bi) + 1] - p[(3 * ci) + 1]) - (bs.P[1] - cs.P[1]);
        double dz = (p[(3 * bi) + 2] - p[(3 * ci) + 2]) - (bs.P[2] - cs.P[2]);
        double dvx = (v[3 * bi] - v[3 * ci]) - (bs.V[0] - cs.V[0]);
        double dvy = (v[(3 * bi) + 1] - v[(3 * ci) + 1]) - (bs.V[1] - cs.V[1]);
        double dvz = (v[(3 * bi) + 2] - v[(3 * ci) + 2]) - (bs.V[2] - cs.V[2]);
        return (Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)),
            Math.Sqrt((dvx * dvx) + (dvy * dvy) + (dvz * dvz)));
    }

    private static int Test544_JplSatelliteComparison()
    {
        Console.WriteLine("--- Moon vs Galilean moons and Titan, 2026-2036 ---");
        string path = Path.Combine("Study", "jpl", "horizons_real_2026_10y.json");
        Check(File.Exists(path), "JPL vectors file", path);
        if (!File.Exists(path))
        {
            return 0;
        }

        JplFile jpl = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(path));
        string[] names =
        {
            "Mercury", "Venus", "Earth", "Moon", "Mars",
            "JupiterCenter", "Io", "Europa", "Ganymede", "Callisto",
            "SaturnCenter", "Mimas", "Enceladus", "Tethys", "Dione",
            "Rhea", "Titan", "Hyperion", "Iapetus",
            "Uranus", "Neptune"
        };
        StudyNBody nb = JplNBody(jpl, names, true, true);
        var run = JplDop853(nb, 3650d, 1e-6d, 1e-9d, 1e-13d, true, false, false, 21600d, -1, true, true);
        string[] moons =
        {
            "Moon", "Io", "Europa", "Ganymede", "Callisto",
            "Mimas", "Enceladus", "Tethys", "Dione", "Rhea",
            "Titan", "Hyperion", "Iapetus"
        };
        string[] centers =
        {
            "Earth", "JupiterCenter", "JupiterCenter", "JupiterCenter", "JupiterCenter",
            "SaturnCenter", "SaturnCenter", "SaturnCenter", "SaturnCenter", "SaturnCenter",
            "SaturnCenter", "SaturnCenter", "SaturnCenter"
        };
        Console.WriteLine("moon           planet        dp(km)      dv(m/s)");
        for (int i = 0; i < moons.Length; i++)
        {
            var e = JplRelativeError(jpl, nb, run.p, run.v, 3650d, moons[i], centers[i]);
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-14} {1,-13} {2,10:F2} {3,12:E2}",
                moons[i], centers[i], e.dp / 1000d, e.dv));
            Check(double.IsFinite(e.dp), "satellite " + moons[i] + " finite", "");
        }

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "JupiterCenter vs JPL: {0:F2} km", JplBodyError(jpl, nb, run.p, 3650d, "JupiterCenter", false) / 1000d));
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "SaturnCenter vs JPL:  {0:F2} km", JplBodyError(jpl, nb, run.p, 3650d, "SaturnCenter", false) / 1000d));
        Console.WriteLine("--- Satellite comparison complete ---");
        return 0;
    }

    private static int Test543_JplFull409Probe()
    {
        string path = Path.Combine("Study", "jpl", "horizons_real_2026_10y.json");
        Check(File.Exists(path), "JPL vectors file", path);
        if (!File.Exists(path))
        {
            return 0;
        }

        JplFile jpl = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(path));
        string[] ten =
        {
            "Mercury", "Venus", "Earth", "Moon", "Mars",
            "Jupiter", "Saturn", "Uranus", "Neptune"
        };
        var names = new List<string>(ten);
        foreach (JplBody body in jpl.Bodies)
        {
            if (body.Name.StartsWith("Ast", StringComparison.Ordinal) || body.Name.StartsWith("Kbo", StringComparison.Ordinal) || body.Name.StartsWith("Kbr", StringComparison.Ordinal))
            {
                names.Add(body.Name);
            }
        }

        StudyNBody nb = JplNBody(jpl, names.ToArray(), true, true);
        var run = JplDop853(nb, 3650d, 1e-6d, 1e-9d, 1e-13d, true, false, false, 86400d, ten.Length);
        Console.WriteLine("full409 bodies=" + nb.Count + " small bodies=" + (nb.Count - ten.Length - 1));
        double jupiter = JplBodyError(jpl, nb, run.p, 3650d, "Jupiter", false);
        double saturn = JplBodyError(jpl, nb, run.p, 3650d, "Saturn", false);
        double mars = JplBodyError(jpl, nb, run.p, 3650d, "Mars", false);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "Jupiter {0:F2} km  Saturn {1:F2} km  Mars {2:F2} km  steps {3}",
            jupiter / 1000d, saturn / 1000d, mars / 1000d, run.steps));
        Check(double.IsFinite(jupiter) && double.IsFinite(saturn) && double.IsFinite(mars),
            "full409 finite", "");
        Console.WriteLine("--- Full409 probe complete ---");
        return 0;
    }

    private static int Test541_JplConvergence()
    {
        Console.WriteLine("--- 2026 10-body 1PN: integrator convergence (10 years) ---");
        string path = Path.Combine("Study", "jpl", "horizons_real_2026_10y.json");
        Check(File.Exists(path), "JPL vectors file", path);
        if (!File.Exists(path))
        {
            return 0;
        }

        JplFile jpl = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(path));
        string[] ten =
        {
            "Mercury", "Venus", "Earth", "Moon", "Mars",
            "Jupiter", "Saturn", "Uranus", "Neptune"
        };
        StudyNBody nb = JplNBody(jpl, ten, true, true);
        string[] names = { "base", "tol", "step", "both" };
        double[] atols = { 1e-6d, 1e-7d, 1e-6d, 1e-7d };
        double[] velAtoLs = { 1e-9d, 1e-10d, 1e-9d, 1e-10d };
        double[] rtols = { 1e-13d, 1e-14d, 1e-13d, 1e-14d };
        double[] maxSteps = { 86400d, 86400d, 43200d, 43200d };
        string[] bodies = { "Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune" };
        var err = new double[names.Length, bodies.Length];
        Console.WriteLine("run    Jupiter   Saturn     Mars    RMS(8)");
        for (int r = 0; r < names.Length; r++)
        {
            var run = JplDop853(
                nb, 3650d, atols[r], velAtoLs[r], rtols[r], true, false, false, maxSteps[r]);
            double sum = 0d;
            for (int b = 0; b < bodies.Length; b++)
            {
                err[r, b] = JplBodyError(jpl, nb, run.p, 3650d, bodies[b], false) / 1000d;
                sum += err[r, b] * err[r, b];
            }

            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-5} {1,8:F3} {2,8:F3} {3,8:F3} {4,8:F3}",
                names[r], err[r, 4], err[r, 5], err[r, 3], Math.Sqrt(sum / bodies.Length)));
        }

        Console.WriteLine("change vs base (km):");
        for (int r = 1; r < names.Length; r++)
        {
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-5} Jupiter {1,8:F3}  Saturn {2,8:F3}  Mars {3,8:F3}",
                names[r],
                err[r, 4] - err[0, 4],
                err[r, 5] - err[0, 5],
                err[r, 3] - err[0, 3]));
        }

        Console.WriteLine("--- Convergence probe complete ---");
        return 0;
    }

    private static int Test539_JplShortHorizon()
    {
        string[] mainFiles = { "horizons_real_2026_10y.json", "horizons_real_2016_10y.json" };
        string[] shortFiles = { "horizons_short_2026.json", "horizons_short_2016.json" };
        string[] ten =
        {
            "Mercury", "Venus", "Earth", "Moon", "Mars",
            "Jupiter", "Saturn", "Uranus", "Neptune"
        };
        string[] bodies = { "Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune" };
        double[] days = { 1d, 10d, 30d, 90d, 180d, 365d };
        double worst0 = 0d;
        for (int epoch = 0; epoch < mainFiles.Length; epoch++)
        {
            string mainPath = Path.Combine("Study", "jpl", mainFiles[epoch]);
            string shortPath = Path.Combine("Study", "jpl", shortFiles[epoch]);
            Check(File.Exists(mainPath), "JPL vectors file", mainPath);
            Check(File.Exists(shortPath), "JPL short file", shortPath);
            if (!File.Exists(mainPath) || !File.Exists(shortPath))
            {
                return 0;
            }

            JplFile main = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(mainPath));
            JplFile jpl = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(shortPath));
            StudyNBody nb = JplNBody(main, ten, true, true);
            Console.WriteLine("epoch " + jpl.Epoch + " day-0 relative state error (m):");
            for (int b = 0; b < bodies.Length; b++)
            {
                var e0 = JplStateError(jpl, nb, nb.P0, nb.V0, 0d, bodies[b], false);
                worst0 = Math.Max(worst0, e0.dp);
                Console.Write(string.Format(CultureInfo.InvariantCulture,
                    " {0}={1:E2}", bodies[b], e0.dp));
            }

            Console.WriteLine();
            var err = new double[bodies.Length, days.Length];
            var marsRad = new double[days.Length];
            var marsAlong = new double[days.Length];
            var marsNormal = new double[days.Length];
            for (int h = 0; h < days.Length; h++)
            {
                var run = JplDop853(nb, days[h], 1e-6d, 1e-9d, 1e-13d, true);
                for (int b = 0; b < bodies.Length; b++)
                {
                    err[b, h] = JplStateError(jpl, nb, run.p, run.v, days[h], bodies[b], false).dp / 1000d;
                }

                int sun = JplIndex(nb, "Sun");
                int mars = JplIndex(nb, "Mars");
                JplState ms = jpl.Bodies.First(x => x.Name == "Mars")
                    .States.First(s => Math.Abs(s.Days - days[h]) < 1e-9d);
                double dx = (run.p[3 * mars] - run.p[3 * sun]) - ms.P[0];
                double dy = (run.p[(3 * mars) + 1] - run.p[(3 * sun) + 1]) - ms.P[1];
                double dz = (run.p[(3 * mars) + 2] - run.p[(3 * sun) + 2]) - ms.P[2];
                double rx = run.p[3 * mars] - run.p[3 * sun];
                double ry = run.p[(3 * mars) + 1] - run.p[(3 * sun) + 1];
                double rz = run.p[(3 * mars) + 2] - run.p[(3 * sun) + 2];
                double vx = run.v[3 * mars] - run.v[3 * sun];
                double vy = run.v[(3 * mars) + 1] - run.v[(3 * sun) + 1];
                double vz = run.v[(3 * mars) + 2] - run.v[(3 * sun) + 2];
                double rn = Math.Sqrt((rx * rx) + (ry * ry) + (rz * rz));
                double hx = (ry * vz) - (rz * vy);
                double hy = (rz * vx) - (rx * vz);
                double hz = (rx * vy) - (ry * vx);
                double hn = Math.Sqrt((hx * hx) + (hy * hy) + (hz * hz));
                double tx = ((hy * rz) - (hz * ry)) / (hn * rn);
                double ty = ((hz * rx) - (hx * rz)) / (hn * rn);
                double tz = ((hx * ry) - (hy * rx)) / (hn * rn);
                marsRad[h] = ((dx * rx) + (dy * ry) + (dz * rz)) / rn;
                marsAlong[h] = (dx * tx) + (dy * ty) + (dz * tz);
                marsNormal[h] = ((dx * hx) + (dy * hy) + (dz * hz)) / hn;
            }

            Console.WriteLine("epoch " + jpl.Epoch + " position error (km):");
            Console.Write(string.Format(CultureInfo.InvariantCulture, "{0,-10}", "body"));
            for (int h = 0; h < days.Length; h++)
            {
                Console.Write(string.Format(CultureInfo.InvariantCulture, "{0,10:F0}", days[h]));
            }

            Console.WriteLine();
            for (int b = 0; b < bodies.Length; b++)
            {
                Console.Write(string.Format(CultureInfo.InvariantCulture, "{0,-10}", bodies[b]));
                for (int h = 0; h < days.Length; h++)
                {
                    Console.Write(string.Format(CultureInfo.InvariantCulture, "{0,10:F3}", err[b, h]));
                }

                Console.WriteLine();
            }

            Console.WriteLine("Mars error decomposition (m):");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-6}{1,12}{2,12}{3,12}", "day", "radial", "along", "normal"));
            for (int h = 0; h < days.Length; h++)
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-6:F0}{1,12:F2}{2,12:F2}{3,12:F2}",
                    days[h], marsRad[h], marsAlong[h], marsNormal[h]));
            }
        }

        Check(worst0 < 1e-3d, "day-0 relative state", worst0.ToString("E2", CultureInfo.InvariantCulture) + " m");
        Console.WriteLine("--- Short-horizon probe complete ---");
        return 0;
    }
}
