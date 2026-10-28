using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Galilego.Core;

internal static partial class P1bTests
{
    private sealed class JplState
    {
        [JsonPropertyName("days")] public double Days { get; set; }
        [JsonPropertyName("p")] public double[] P { get; set; }
        [JsonPropertyName("v")] public double[] V { get; set; }
    }

    private sealed class JplBody
    {
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("mu")] public double Mu { get; set; }
        [JsonPropertyName("mu_body")] public double MuBody { get; set; }
        [JsonPropertyName("states")] public JplState[] States { get; set; }
    }

    private sealed class JplFile
    {
        [JsonPropertyName("epoch")] public string Epoch { get; set; }
        [JsonPropertyName("frame")] public string Frame { get; set; }
        [JsonPropertyName("units")] public string Units { get; set; }
        [JsonPropertyName("source")] public string Source { get; set; }
        [JsonPropertyName("days")] public double[] Days { get; set; }
        [JsonPropertyName("bodies")] public JplBody[] Bodies { get; set; }
    }

    private static StudyNBody JplNBody(JplFile jpl, string[] names)
    {
        return JplNBody(jpl, names, true, false);
    }

    private static StudyNBody JplNBody(
        JplFile jpl, string[] names, bool useSystemGm, bool barycentric,
        bool centerWithSystemGm = false)
    {
        var byName = new Dictionary<string, JplBody>(StringComparer.Ordinal);
        foreach (JplBody body in jpl.Bodies)
        {
            byName[body.Name] = body;
        }

        int n = names.Length + 1;
        var nb = new StudyNBody
        {
            Names = new string[n],
            Mu = new double[n],
            P0 = new double[3 * n],
            V0 = new double[3 * n]
        };
        nb.Names[0] = "Sun";
        // DE441 value (Park et al. 2021, Table 2), not the older IAU 1.32712440018e20.
        nb.Mu[0] = 1.32712440041279419e20d;
        for (int i = 0; i < names.Length; i++)
        {
            JplBody body = byName[names[i]];
            JplState state = body.States[0];
            nb.Names[i + 1] = body.Name;
            nb.Mu[i + 1] = useSystemGm || body.MuBody <= 0d ? body.Mu : body.MuBody;
            if (centerWithSystemGm && body.Name == "JupiterCenter" && useSystemGm)
            {
                nb.Mu[i + 1] = byName["Jupiter"].Mu;
            }
            for (int k = 0; k < 3; k++)
            {
                nb.P0[(3 * (i + 1)) + k] = state.P[k];
                nb.V0[(3 * (i + 1)) + k] = state.V[k];
            }
        }

        if (barycentric)
        {
            double totalMu = 0d;
            for (int i = 0; i < n; i++)
            {
                totalMu += nb.Mu[i];
            }

            double rsx = 0d, rsy = 0d, rsz = 0d, vsx = 0d, vsy = 0d, vsz = 0d;
            for (int i = 1; i < n; i++)
            {
                rsx -= nb.Mu[i] * nb.P0[3 * i];
                rsy -= nb.Mu[i] * nb.P0[(3 * i) + 1];
                rsz -= nb.Mu[i] * nb.P0[(3 * i) + 2];
                vsx -= nb.Mu[i] * nb.V0[3 * i];
                vsy -= nb.Mu[i] * nb.V0[(3 * i) + 1];
                vsz -= nb.Mu[i] * nb.V0[(3 * i) + 2];
            }

            rsx /= totalMu;
            rsy /= totalMu;
            rsz /= totalMu;
            vsx /= totalMu;
            vsy /= totalMu;
            vsz /= totalMu;
            for (int i = 1; i < n; i++)
            {
                nb.P0[3 * i] += rsx;
                nb.P0[(3 * i) + 1] += rsy;
                nb.P0[(3 * i) + 2] += rsz;
                nb.V0[3 * i] += vsx;
                nb.V0[(3 * i) + 1] += vsy;
                nb.V0[(3 * i) + 2] += vsz;
            }

            nb.P0[0] = rsx;
            nb.P0[1] = rsy;
            nb.P0[2] = rsz;
            nb.V0[0] = vsx;
            nb.V0[1] = vsy;
            nb.V0[2] = vsz;
        }

        return nb;
    }

    // Regression envelopes for the Newtonian 10-body model: not physics budgets.
    // Mercury is dominated by the missing 1PN term (Test531); the other bodies
    // by asteroids, initial-condition rounding and unmodelled effects.
    private static double JplGateMeters(string body, double days)
    {
        if (body == "Mercury")
        {
            return days < 400d ? 1e5d : (days < 2000d ? 1.5e6d : 5e6d);
        }

        if (body == "Venus" || body == "Jupiter")
        {
            return days < 400d ? 2e5d : (days < 2000d ? 1.5e6d : 3e6d);
        }

        if (body == "Earth")
        {
            return days < 400d ? 1.5e5d : (days < 2000d ? 7e5d : 1.5e6d);
        }

        if (body == "Moon_geo")
        {
            return days < 400d ? 1e5d : (days < 2000d ? 5e5d : 1e6d);
        }

        return days < 400d ? 1e5d : (days < 2000d ? 1e6d : 1.5e6d);
    }

    private static void JplGate(string model, string body, double days, double dp)
    {
        if (model != "10-body")
        {
            return;
        }

        double gate = JplGateMeters(body, days);
        Check(dp < gate, "jpl " + body + " " + days + " d accuracy",
            dp.ToString("E3", System.Globalization.CultureInfo.InvariantCulture) + " < "
            + gate.ToString("G3", System.Globalization.CultureInfo.InvariantCulture) + " m");
    }

    private static int JplIndex(StudyNBody nb, string name)
    {
        for (int i = 0; i < nb.Count; i++)
        {
            if (nb.Names[i] == name)
            {
                return i;
            }
        }

        return -1;
    }

    private static int Test530_JplRealism()
    {
        Console.WriteLine("--- Real solar system vs JPL Horizons (DE441) ---");
        string path = Path.Combine("Study", "jpl", "horizons_real_2026_10y.json");
        Check(File.Exists(path), "JPL vectors file", path);
        if (!File.Exists(path))
        {
            return 0;
        }

        JplFile jpl = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(path));
        Console.WriteLine("epoch: " + jpl.Epoch + " | frame: " + jpl.Frame + " | source: " + jpl.Source);

        string[] seven = { "Earth", "Moon", "Venus", "Mars", "Jupiter", "Saturn" };
        string[] ten = { "Mercury", "Venus", "Earth", "Moon", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune" };
        StudyNBody nbSeven = JplNBody(jpl, seven);
        StudyNBody nbTen = JplNBody(jpl, ten);
        double[] horizons = { 365d, 1825d, 3650d };

        var results = new DdNBodyResult[horizons.Length * 2];
        int[] order = { 4, 5, 2, 3, 0, 1 };
        Parallel.For(
            0,
            order.Length,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Min(order.Length, Environment.ProcessorCount) },
            k =>
            {
                int job = order[k];
                int hi = job / 2;
                bool tenBody = (job & 1) != 0;
                StudyNBody nb = tenBody ? nbTen : nbSeven;
                var input = new TaylorNBodyInput
                {
                    Names = nb.Names,
                    Mu = nb.Mu,
                    P0 = nb.P0,
                    V0 = nb.V0
                };
                results[job] = RunTaylorNBodyDdOpt(
                    input, horizons[hi] / 365.25d, 12, 1e-9d, 1e-12d, 3600d, 1e-6d);
            });

        Console.WriteLine();
        Console.WriteLine("run      horizon   steps      calls          ms");
        for (int i = 0; i < results.Length; i++)
        {
            int hi = i / 2;
            DdNBodyResult r = results[i];
            Console.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0,-8} {1,6:F0} d {2,8} {3,10} {4,11:F0}",
                (i & 1) == 0 ? "7-body" : "10-body", horizons[hi], r.Steps, r.Calls, r.Ms));
        }

        Console.WriteLine();
        Console.WriteLine("model       horizon   body       dp(helio, m)   dv(m/s)");
        for (int hi = 0; hi < horizons.Length; hi++)
        {
            double days = horizons[hi];
            for (int model = 0; model < 2; model++)
            {
                DdNBodyResult r = results[(2 * hi) + model];
                StudyNBody nb = model == 0 ? nbSeven : nbTen;
                int sun = JplIndex(nb, "Sun");
                for (int i = 1; i < nb.Count; i++)
                {
                    JplBody body = jpl.Bodies.First(b => b.Name == nb.Names[i]);
                    JplState state = body.States.First(s => Math.Abs(s.Days - days) < 1e-9d);
                    double dx = ((r.P[3 * i] - r.P[3 * sun]) - state.P[0]);
                    double dy = ((r.P[(3 * i) + 1] - r.P[(3 * sun) + 1]) - state.P[1]);
                    double dz = ((r.P[(3 * i) + 2] - r.P[(3 * sun) + 2]) - state.P[2]);
                    double dp = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                    double dvx = (r.V[3 * i] - r.V[3 * sun]) - state.V[0];
                    double dvy = (r.V[(3 * i) + 1] - r.V[(3 * sun) + 1]) - state.V[1];
                    double dvz = (r.V[(3 * i) + 2] - r.V[(3 * sun) + 2]) - state.V[2];
                    double dv = Math.Sqrt((dvx * dvx) + (dvy * dvy) + (dvz * dvz));
                    Console.WriteLine(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0,-11} {1,6:F0} d  {2,-9} {3,14:E3}  {4,12:E3}",
                        model == 0 ? "7-body" : "10-body", days, nb.Names[i], dp, dv));
                    Check(double.IsFinite(dp) && double.IsFinite(dv),
                        "jpl " + model + " " + days + " " + nb.Names[i] + " finite",
                        dp.ToString("E2", System.Globalization.CultureInfo.InvariantCulture));
                    JplGate(model == 0 ? "7-body" : "10-body", nb.Names[i], days, dp);
                    if (nb.Names[i] == "Moon")
                    {
                        int earth = JplIndex(nb, "Earth");
                        JplState earthJpl = jpl.Bodies.First(b => b.Name == "Earth")
                            .States.First(s => Math.Abs(s.Days - days) < 1e-9d);
                        double gx = (r.P[3 * i] - r.P[3 * earth]) - (state.P[0] - earthJpl.P[0]);
                        double gy = (r.P[(3 * i) + 1] - r.P[(3 * earth) + 1]) - (state.P[1] - earthJpl.P[1]);
                        double gz = (r.P[(3 * i) + 2] - r.P[(3 * earth) + 2]) - (state.P[2] - earthJpl.P[2]);
                        double gdp = Math.Sqrt((gx * gx) + (gy * gy) + (gz * gz));
                        double gvx = (r.V[3 * i] - r.V[3 * earth]) - (state.V[0] - earthJpl.V[0]);
                        double gvy = (r.V[(3 * i) + 1] - r.V[(3 * earth) + 1]) - (state.V[1] - earthJpl.V[1]);
                        double gvz = (r.V[(3 * i) + 2] - r.V[(3 * earth) + 2]) - (state.V[2] - earthJpl.V[2]);
                        double gdv = Math.Sqrt((gvx * gvx) + (gvy * gvy) + (gvz * gvz));
                        Console.WriteLine(string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "{0,-11} {1,6:F0} d  {2,-9} {3,14:E3}  {4,12:E3}",
                            model == 0 ? "7-body" : "10-body", days, "Moon(geo)", gdp, gdv));
                        JplGate(model == 0 ? "7-body" : "10-body", "Moon_geo", days, gdp);
                    }
                }
            }
        }

        Console.WriteLine("--- JPL realism complete ---");
        return 0;
    }
}
