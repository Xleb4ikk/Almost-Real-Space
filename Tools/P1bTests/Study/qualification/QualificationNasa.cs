using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

// TASK.md Appendix B: layer V (validation against NASA/JPL Horizons DE441).
//
// Layer V answers "does the model reproduce NASA?" with ONE fixed integrator and a varying
// force model. It is a report plus the G9 reproducibility gate, never a method status: the
// NASA residual mixes model and integrator error (TASK.md B1) and is not used for ranking.
//
// Every number here comes from the existing Study/jpl code through the same calls the
// unchanged tests use (JplNBody / JplDop853 / JplStateError), so G9 compares like with like:
// |stand - old test| <= max(0.01 km, 0.5 %).
internal static partial class P1bTests
{
    private const string QualificationNasaValidationHeader =
        "window,model,body,days,dp_km,dv_ms,geo";

    /// <summary>Yearly slices 1..10 plus day 0 (B5: rms8_all over all annual slices).</summary>
    private const int QualificationNasaLastYear = 10;

    private static readonly string[] QualificationNasaBodies =
    {
        "Mercury", "Venus", "Earth", "Mars", "Jupiter",
        "Saturn", "Uranus", "Neptune", "Moon"
    };

    /// <summary>Layer V rows of this run; the H tier reads E_model from here (B6).</summary>
    private static List<QualificationNasaRow> qualificationNasaRows;

    /// <summary>
    /// E_model of a task's model and window in metres: rms8_end (B5) or the geocentric Moon
    /// error at 10 years. Recomputed from layer V every run, never hardcoded.
    /// </summary>
    private static double QualificationNasaModelErrorMeters(string window, string model, bool moon)
    {
        if (qualificationNasaRows == null)
        {
            return double.NaN;
        }

        if (moon)
        {
            return QualificationNasaValue(qualificationNasaRows, window, model, "Moon", 3650d) * 1000d;
        }

        return QualificationNasaRms(qualificationNasaRows, window, model, 3650d, out _) * 1000d;
    }

    private sealed class QualificationNasaWindow
    {
        public string Name;
        public string File;
        public JplFile Data;
        public StudyNBody TenBody;
        public StudyNBody FullBody;
        public StudyNBody SatelliteBody;
    }

    private sealed class QualificationNasaModel
    {
        public string Name;
        public bool Pn;
        public bool Eih;
        public bool J2;
        public bool UseSmallBodies;
        public int AsteroidStart = -1;
        public bool EarthJ2;
    }

    private sealed class QualificationNasaRow
    {
        public string Window;
        public string Model;
        public string Body;
        public double Days;
        public double DpKm;
        public double DvMs;
        public bool Geo;
    }

    private sealed class QualificationNasaSatelliteRow
    {
        public string Window;
        public string Moon;
        public string Center;
        public double Days;
        public double DpKm;
        public double DvMs;
    }

    private sealed class QualificationNasaCheck
    {
        public string Name;
        public string Detail;
        public bool Ok;
    }

    private sealed class QualificationNasaG9
    {
        public string Window;
        public string Model;
        public string Body;
        public double Days;
        public double DpKm;
    }

    // ---------------------------------------------------------------------------------
    // Scope of layer V
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// B1: window 2026 only. B2 adds the 2016 holdout window. B4.3: the smoke profile keeps
    /// only the 2026 window, so the minutes-long budget is preserved.
    /// </summary>
    private static List<QualificationNasaWindow> QualificationNasaWindows(QualificationProfile profile)
    {
        var list = new List<QualificationNasaWindow>();
        list.Add(new QualificationNasaWindow
        {
            Name = "2026-2036",
            File = "horizons_real_2026_10y.json"
        });
        if (profile == QualificationProfile.Full)
        {
            list.Add(new QualificationNasaWindow
            {
                Name = "2016-2026",
                File = "horizons_real_2016_10y.json"
            });
        }

        return list;
    }

    /// <summary>
    /// B1: the three 10-body models. B2 adds the four 409-small-body models, i.e. the full
    /// Test537/538 set of eight. Smoke keeps the 10-body subset (B4.3).
    /// </summary>
    private static List<QualificationNasaModel> QualificationNasaModels(QualificationProfile profile)
    {
        var list = new List<QualificationNasaModel>();
        list.Add(new QualificationNasaModel { Name = "Newton" });
        list.Add(new QualificationNasaModel { Name = "1PN", Pn = true });
        list.Add(new QualificationNasaModel { Name = "EIH", Pn = true, Eih = true });
        if (profile == QualificationProfile.Full)
        {
            list.Add(new QualificationNasaModel { Name = "1PN+J2", Pn = true, J2 = true });
            list.Add(new QualificationNasaModel
            {
                Name = "409small", Pn = true, UseSmallBodies = true, AsteroidStart = 10
            });
            list.Add(new QualificationNasaModel
            {
                Name = "409small+J2", Pn = true, J2 = true, UseSmallBodies = true, AsteroidStart = 10
            });
            list.Add(new QualificationNasaModel
            {
                Name = "409small+EJ2", Pn = true, EarthJ2 = true,
                UseSmallBodies = true, AsteroidStart = 10
            });
            list.Add(new QualificationNasaModel
            {
                Name = "409small+J2+EJ2", Pn = true, J2 = true, EarthJ2 = true,
                UseSmallBodies = true, AsteroidStart = 10
            });
        }

        return list;
    }

    // ---------------------------------------------------------------------------------
    // Layer V entry point
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Runs V0 and then layer V, writes nasa_validation.csv / nasa_satellites.csv and
    /// NASA_VALIDATION.md. Returns false when V0 fails: B5 says the layer is not published.
    /// </summary>
    private static bool QualificationRunNasaValidation(
        string dir, QualificationProfile profile, TextWriter log)
    {
        List<QualificationNasaCheck> checks = QualificationNasaV0(log);
        bool v0 = true;
        foreach (QualificationNasaCheck check in checks)
        {
            v0 &= check.Ok;
        }

        if (!v0)
        {
            log.WriteLine("V0: FAILED - layer V is not published (TASK.md B5)");
            return false;
        }

        List<QualificationNasaWindow> windows = QualificationNasaWindows(profile);
        List<QualificationNasaModel> models = QualificationNasaModels(profile);
        bool satellites = profile == QualificationProfile.Full;
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var rows = new List<QualificationNasaRow>();
        foreach (QualificationNasaWindow window in windows)
        {
            string path = Path.Combine("Study", "jpl", window.File);
            if (!File.Exists(path))
            {
                log.WriteLine("V: missing data file " + path + "; layer V is not published");
                return false;
            }

            window.Data = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(path));
            window.TenBody = JplNBody(window.Data, QualificationNasaNames(), true, true);
            if (profile == QualificationProfile.Full)
            {
                window.FullBody = JplNBody(
                    window.Data, QualificationNasaSmallBodyNames(window.Data), true, true);
                // Test544 (the satellite model) exists only for the 2026 window: the 2016
                // Horizons file has no Jupiter/Saturn centres and no moons.
                if (window.Name == "2026-2036")
                {
                    window.SatelliteBody = JplNBody(
                        window.Data, QualificationNasaSatelliteNames(), true, true);
                }
            }
        }

        double[] horizons = new double[QualificationNasaLastYear];
        for (int k = 1; k <= QualificationNasaLastYear; k++)
        {
            horizons[k - 1] = 365d * k;
        }

        int cells = windows.Count * models.Count * horizons.Length;
        var block = new QualificationNasaRow[cells][];
        var bodyNames = QualificationNasaBodies;
        Parallel.For(
            0,
            cells,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            index =>
            {
                int perModel = horizons.Length;
                int perWindow = models.Count * perModel;
                int w = index / perWindow;
                int rest = index % perWindow;
                int m = rest / perModel;
                int h = rest % perModel;
                QualificationNasaWindow window = windows[w];
                QualificationNasaModel model = models[m];
                double days = horizons[h];
                StudyNBody nb = QualificationNasaBody(window, model);
                (double[] p, double[] v, long calls, long steps) run = JplDop853(
                    nb, days, 1e-6d, 1e-9d, 1e-13d,
                    model.Pn, model.Eih, model.J2, 86400d, model.AsteroidStart, model.EarthJ2);
                var cell = new QualificationNasaRow[bodyNames.Length];
                for (int b = 0; b < bodyNames.Length; b++)
                {
                    bool geo = bodyNames[b] == "Moon";
                    (double dp, double dv) = JplStateError(
                        window.Data, nb, run.p, run.v, days, bodyNames[b], geo);
                    cell[b] = new QualificationNasaRow
                    {
                        Window = window.Name,
                        Model = model.Name,
                        Body = bodyNames[b],
                        Days = days,
                        DpKm = dp / 1000d,
                        DvMs = dv,
                        Geo = geo
                    };
                }

                block[index] = cell;
            });

        foreach (QualificationNasaRow[] cell in block)
        {
            rows.AddRange(cell);
        }

        // Day 0: a real measurement, not an assumed zero (V0.1 proves it is tiny).
        foreach (QualificationNasaWindow window in windows)
        {
            foreach (QualificationNasaModel model in models)
            {
                StudyNBody nb = QualificationNasaBody(window, model);
                for (int b = 0; b < bodyNames.Length; b++)
                {
                    bool geo = bodyNames[b] == "Moon";
                    (double dp, double dv) = JplStateError(
                        window.Data, nb, nb.P0, nb.V0, 0d, bodyNames[b], geo);
                    rows.Add(new QualificationNasaRow
                    {
                        Window = window.Name,
                        Model = model.Name,
                        Body = bodyNames[b],
                        Days = 0d,
                        DpKm = dp / 1000d,
                        DvMs = dv,
                        Geo = geo
                    });
                }
            }
        }

        rows.Sort(QualificationNasaRowOrder);
        qualificationNasaRows = rows;
        QualificationNasaWriteCsv(Path.Combine(dir, "nasa_validation.csv"), rows);

        List<QualificationNasaSatelliteRow> satelliteRows = new List<QualificationNasaSatelliteRow>();
        if (satellites)
        {
            satelliteRows = QualificationNasaSatelliteRows(windows, log);
            QualificationNasaWriteSatelliteCsv(
                Path.Combine(dir, "nasa_satellites.csv"), satelliteRows);
        }

        List<QualificationNasaG9> reference = QualificationNasaG9Load();
        int drift = 0;
        int compared = 0;
        foreach (QualificationNasaG9 expected in reference)
        {
            double actual = expected.Model == "satellites"
                ? QualificationNasaSatelliteValue(
                    satelliteRows, expected.Window, expected.Body, expected.Days)
                : QualificationNasaValue(
                    rows, expected.Window, expected.Model, expected.Body, expected.Days);
            if (double.IsNaN(actual))
            {
                continue;
            }

            compared++;
            double tolerance = Math.Max(0.01d, 0.005d * Math.Abs(expected.DpKm));
            if (!(Math.Abs(actual - expected.DpKm) <= tolerance))
            {
                drift++;
                log.WriteLine("G9 DRIFT " + expected.Window + " " + expected.Model + " "
                    + expected.Body + " " + expected.Days.ToString("F0", QualificationInv)
                    + " d: stand " + actual.ToString("F3", QualificationInv)
                    + " km, reference " + expected.DpKm.ToString("F3", QualificationInv)
                    + " km, tol " + tolerance.ToString("F3", QualificationInv));
            }
        }

        log.WriteLine("V: " + rows.Count.ToString(QualificationInv) + " row(s), "
            + satelliteRows.Count.ToString(QualificationInv) + " satellite row(s), "
            + compared.ToString(QualificationInv) + " G9 comparison(s), "
            + drift.ToString(QualificationInv) + " drift, "
            + watch.ElapsedMilliseconds.ToString(QualificationInv) + " ms");

        string report = QualificationNasaReportText(
            windows, models, rows, satelliteRows, reference, checks, drift, profile);
        File.WriteAllText(Path.Combine(dir, "NASA_VALIDATION.md"), report, new UTF8Encoding(false));
        log.WriteLine("V: NASA_VALIDATION.md written (layer V is a report, not a method status)");
        return drift == 0;
    }

    private static StudyNBody QualificationNasaBody(
        QualificationNasaWindow window, QualificationNasaModel model)
    {
        return model.UseSmallBodies ? window.FullBody : window.TenBody;
    }

    private static string[] QualificationNasaSmallBodyNames(JplFile jpl)
    {
        var names = new List<string>(QualificationNasaNames());
        foreach (JplBody body in jpl.Bodies)
        {
            if (body.Name.StartsWith("Ast", StringComparison.Ordinal)
                || body.Name.StartsWith("Kbo", StringComparison.Ordinal)
                || body.Name.StartsWith("Kbr", StringComparison.Ordinal))
            {
                names.Add(body.Name);
            }
        }

        return names.ToArray();
    }

    /// <summary>Exactly the 21-body set of Test544 (centres use the system GM).</summary>
    private static string[] QualificationNasaSatelliteNames()
    {
        return new[]
        {
            "Mercury", "Venus", "Earth", "Moon", "Mars",
            "JupiterCenter", "Io", "Europa", "Ganymede", "Callisto",
            "SaturnCenter", "Mimas", "Enceladus", "Tethys", "Dione",
            "Rhea", "Titan", "Hyperion", "Iapetus",
            "Uranus", "Neptune"
        };
    }

    /// <summary>
    /// Test544 path: one 10-year run per window with J2/J4/J6 of Jupiter and Saturn, J2 of the
    /// moons and Earth J2 for the Moon; errors are relative to the planet centre.
    /// </summary>
    private static List<QualificationNasaSatelliteRow> QualificationNasaSatelliteRows(
        List<QualificationNasaWindow> windows, TextWriter log)
    {
        var moons = new[]
        {
            "Moon", "Io", "Europa", "Ganymede", "Callisto",
            "Mimas", "Enceladus", "Tethys", "Dione", "Rhea",
            "Titan", "Hyperion", "Iapetus"
        };
        var centers = new[]
        {
            "Earth", "JupiterCenter", "JupiterCenter", "JupiterCenter", "JupiterCenter",
            "SaturnCenter", "SaturnCenter", "SaturnCenter", "SaturnCenter", "SaturnCenter",
            "SaturnCenter", "SaturnCenter", "SaturnCenter"
        };
        var rows = new List<QualificationNasaSatelliteRow>();
        foreach (QualificationNasaWindow window in windows)
        {
            StudyNBody nb = window.SatelliteBody;
            if (nb == null)
            {
                continue;
            }
            var run = JplDop853(
                nb, 3650d, 1e-6d, 1e-9d, 1e-13d, true, false, false, 21600d, -1, true, true);
            for (int i = 0; i < moons.Length; i++)
            {
                (double dp, double dv) = JplRelativeError(
                    window.Data, nb, run.p, run.v, 3650d, moons[i], centers[i]);
                rows.Add(new QualificationNasaSatelliteRow
                {
                    Window = window.Name,
                    Moon = moons[i],
                    Center = centers[i],
                    Days = 3650d,
                    DpKm = dp / 1000d,
                    DvMs = dv
                });
            }

            log.WriteLine("V: satellites " + window.Name
                + " JupiterCenter vs JPL "
                + (JplBodyError(window.Data, nb, run.p, 3650d, "JupiterCenter", false) / 1000d)
                    .ToString("F2", QualificationInv)
                + " km, SaturnCenter "
                + (JplBodyError(window.Data, nb, run.p, 3650d, "SaturnCenter", false) / 1000d)
                    .ToString("F2", QualificationInv) + " km");

            // Test544 also prints the two planet centres; keep them under the same model so
            // G9 covers every number the unchanged test prints.
            foreach (string center in new[] { "JupiterCenter", "SaturnCenter" })
            {
                double dp = JplBodyError(window.Data, nb, run.p, 3650d, center, false);
                rows.Add(new QualificationNasaSatelliteRow
                {
                    Window = window.Name,
                    Moon = center,
                    Center = "-",
                    Days = 3650d,
                    DpKm = dp / 1000d,
                    DvMs = double.NaN
                });
            }
        }

        return rows;
    }

    private static void QualificationNasaWriteSatelliteCsv(
        string path, List<QualificationNasaSatelliteRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("window,moon,center,days,dp_km,dv_ms");
        foreach (QualificationNasaSatelliteRow row in rows)
        {
            sb.Append(QualificationCsvField(row.Window)).Append(',')
                .Append(QualificationCsvField(row.Moon)).Append(',')
                .Append(QualificationCsvField(row.Center)).Append(',')
                .Append(QualificationCsvNumber(row.Days)).Append(',')
                .Append(QualificationCsvNumber(row.DpKm)).Append(',')
                .Append(QualificationCsvNumber(row.DvMs)).AppendLine();
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static double QualificationNasaSatelliteValue(
        List<QualificationNasaSatelliteRow> rows, string window, string moon, double days)
    {
        foreach (QualificationNasaSatelliteRow row in rows)
        {
            if (row.Window == window && row.Moon == moon
                && Math.Abs(row.Days - days) < 1e-9d)
            {
                return row.DpKm;
            }
        }

        return double.NaN;
    }

    private static string[] QualificationNasaNames()
    {
        return new[]
        {
            "Mercury", "Venus", "Earth", "Moon", "Mars",
            "Jupiter", "Saturn", "Uranus", "Neptune"
        };
    }

    private static int QualificationNasaRowOrder(QualificationNasaRow a, QualificationNasaRow b)
    {
        int c = string.CompareOrdinal(a.Window, b.Window);
        if (c != 0)
        {
            return c;
        }

        c = string.CompareOrdinal(a.Model, b.Model);
        if (c != 0)
        {
            return c;
        }

        c = a.Days.CompareTo(b.Days);
        if (c != 0)
        {
            return c;
        }

        return string.CompareOrdinal(a.Body, b.Body);
    }

    private static void QualificationNasaWriteCsv(string path, List<QualificationNasaRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(QualificationNasaValidationHeader);
        foreach (QualificationNasaRow row in rows)
        {
            sb.Append(QualificationCsvField(row.Window)).Append(',')
                .Append(QualificationCsvField(row.Model)).Append(',')
                .Append(QualificationCsvField(row.Body)).Append(',')
                .Append(QualificationCsvNumber(row.Days)).Append(',')
                .Append(QualificationCsvNumber(row.DpKm)).Append(',')
                .Append(QualificationCsvNumber(row.DvMs)).Append(',')
                .Append(row.Geo ? "1" : "0").AppendLine();
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    // ---------------------------------------------------------------------------------
    // V0: the precondition of B5 (same checks as Test539/540/542)
    // ---------------------------------------------------------------------------------

    private static List<QualificationNasaCheck> QualificationNasaV0(TextWriter log)
    {
        var checks = new List<QualificationNasaCheck>();
        string[] epochs = { "2026-2036", "2016-2026" };
        string[] mainFiles = { "horizons_real_2026_10y.json", "horizons_real_2016_10y.json" };
        string[] shortFiles = { "horizons_short_2026.json", "horizons_short_2016.json" };
        string[] bodies =
        {
            "Mercury", "Venus", "Earth", "Mars",
            "Jupiter", "Saturn", "Uranus", "Neptune"
        };

        // V0.1 day-0 relative states, exactly Test539: the short file is the oracle for day 0.
        for (int e = 0; e < epochs.Length; e++)
        {
            string mainPath = Path.Combine("Study", "jpl", mainFiles[e]);
            string shortPath = Path.Combine("Study", "jpl", shortFiles[e]);
            if (!File.Exists(mainPath) || !File.Exists(shortPath))
            {
                checks.Add(new QualificationNasaCheck
                {
                    Name = "day-0 relative state " + epochs[e],
                    Detail = "missing data file",
                    Ok = false
                });
                continue;
            }

            JplFile main = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(mainPath));
            JplFile shortData = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(shortPath));
            StudyNBody nb = JplNBody(main, QualificationNasaNames(), true, true);
            double worst = 0d;
            string worstBody = "all";
            for (int b = 0; b < bodies.Length; b++)
            {
                (double dp, double dv) = JplStateError(
                    shortData, nb, nb.P0, nb.V0, 0d, bodies[b], false);
                if (dp > worst)
                {
                    worst = dp;
                    worstBody = bodies[b];
                }
            }

            bool ok = worst < 1e-3d;
            checks.Add(new QualificationNasaCheck
            {
                Name = "day-0 relative state " + epochs[e],
                Detail = QualificationFormat(worst) + " m (" + worstBody + ") < 1E-03 m",
                Ok = ok
            });
        }

        // V0.2 initial accelerations and the Mars-moon mass ratio, exactly Test540.
        string path2026 = Path.Combine("Study", "jpl", "horizons_real_2026_10y.json");
        if (File.Exists(path2026))
        {
            JplFile jpl = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(path2026));
            StudyNBody nb = JplNBody(jpl, QualificationNasaNames(), true, true);
            int n = nb.Count;
            var a1 = new double[3 * n];
            var a2 = new double[3 * n];
            JplAcc1Pn(nb.P0, nb.V0, a1, nb.Mu, n, true);
            JplAccEih(nb.P0, nb.V0, a2, nb.Mu, n);
            double worst = 0d;
            bool finite = true;
            for (int i = 1; i < n; i++)
            {
                double dx = (a2[3 * i] - a2[0]) - (a1[3 * i] - a1[0]);
                double dy = (a2[(3 * i) + 1] - a2[1]) - (a1[(3 * i) + 1] - a1[1]);
                double dz = (a2[(3 * i) + 2] - a2[2]) - (a1[(3 * i) + 2] - a1[2]);
                double d = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                finite &= double.IsFinite(d);
                worst = Math.Max(worst, d);
            }

            double marsMass = nb.Mu[JplIndex(nb, "Mars")] / 6.6743e-11d;
            double moonMass = 1.0659e16d + 1.4762e15d;
            double ratio = moonMass / marsMass;
            checks.Add(new QualificationNasaCheck
            {
                Name = "initial acceleration EIH-1PN finite",
                Detail = "max |d a| = " + QualificationFormat(worst) + " m/s^2",
                Ok = finite
            });
            checks.Add(new QualificationNasaCheck
            {
                Name = "Mars moon mass ratio",
                Detail = QualificationFormat(ratio) + " in [1E-08, 5E-08]",
                Ok = ratio > 1e-8d && ratio < 5e-8d
            });
        }
        else
        {
            checks.Add(new QualificationNasaCheck
            {
                Name = "initial acceleration EIH-1PN finite",
                Detail = "missing data file",
                Ok = false
            });
        }

        // V0.3 epoch consistency, exactly Test542: GMs identical, day-0 states exact.
        string path2016 = Path.Combine("Study", "jpl", "horizons_real_2016_10y.json");
        if (File.Exists(path2026) && File.Exists(path2016))
        {
            JplFile jpl2026 = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(path2026));
            JplFile jpl2016 = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(path2016));
            double worstGm = 0d;
            foreach (string body in QualificationNasaNames())
            {
                double a = jpl2026.Bodies.First(x => x.Name == body).Mu;
                double b = jpl2016.Bodies.First(x => x.Name == body).Mu;
                worstGm = Math.Max(worstGm, Math.Abs(a - b) / Math.Max(Math.Abs(a), 1d));
            }

            checks.Add(new QualificationNasaCheck
            {
                Name = "epoch GMs identical",
                Detail = QualificationFormat(worstGm) + " < 1E-12",
                Ok = worstGm < 1e-12d
            });
        }

        foreach (QualificationNasaCheck check in checks)
        {
            log.WriteLine("V0 " + (check.Ok ? "PASS " : "FAIL ") + check.Name + " | " + check.Detail);
        }

        return checks;
    }

    // ---------------------------------------------------------------------------------
    // G9 reference (transcribed from the B0 run of the unchanged tests)
    // ---------------------------------------------------------------------------------

    private static List<QualificationNasaG9> QualificationNasaG9Load()
    {
        var list = new List<QualificationNasaG9>();
        string path = Path.Combine(QualificationRoot(), "nasa_g9_reference.csv");
        if (!File.Exists(path))
        {
            return list;
        }

        string[] lines = File.ReadAllLines(path);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)
                || line.StartsWith("window,", StringComparison.Ordinal))
            {
                continue;
            }

            string[] cells = line.Split(',');
            if (cells.Length < 5)
            {
                continue;
            }

            list.Add(new QualificationNasaG9
            {
                Window = cells[0].Trim(),
                Model = cells[1].Trim(),
                Body = cells[2].Trim(),
                Days = QualificationParseDouble(cells[3].Trim()),
                DpKm = QualificationParseDouble(cells[4].Trim())
            });
        }

        return list;
    }

    // ---------------------------------------------------------------------------------
    // NASA_VALIDATION.md
    // ---------------------------------------------------------------------------------

    private static double QualificationNasaRms(
        List<QualificationNasaRow> rows, string window, string model, double days, out double maxKm)
    {
        maxKm = 0d;
        double sum = 0d;
        int count = 0;
        foreach (QualificationNasaRow row in rows)
        {
            if (row.Window != window || row.Model != model || Math.Abs(row.Days - days) > 1e-9d
                || row.Body == "Moon")
            {
                continue;
            }

            sum += row.DpKm * row.DpKm;
            count++;
            maxKm = Math.Max(maxKm, Math.Abs(row.DpKm));
        }

        return count == 0 ? double.NaN : Math.Sqrt(sum / count);
    }

    private static double QualificationNasaValue(
        List<QualificationNasaRow> rows, string window, string model, string body, double days)
    {
        foreach (QualificationNasaRow row in rows)
        {
            if (row.Window == window && row.Model == model && row.Body == body
                && Math.Abs(row.Days - days) < 1e-9d)
            {
                return row.DpKm;
            }
        }

        return double.NaN;
    }

    private static string QualificationNasaKm(double value)
    {
        return double.IsNaN(value) ? "n/a" : value.ToString("F2", QualificationInv);
    }

    private static string QualificationNasaReportText(
        List<QualificationNasaWindow> windows, List<QualificationNasaModel> models,
        List<QualificationNasaRow> rows, List<QualificationNasaSatelliteRow> satelliteRows,
        List<QualificationNasaG9> reference,
        List<QualificationNasaCheck> checks, int drift, QualificationProfile profile)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# NASA_VALIDATION: layer V (model against JPL Horizons DE441)");
        sb.AppendLine();
        sb.AppendLine("Generated from `nasa_validation.csv` and `nasa_satellites.csv` of this run;");
        sb.AppendLine("no hand edits. Every number below is a MODEL residual: the integrator is fixed");
        sb.AppendLine("(`JplDop853`, rtol 1e-13, maxStep 1 d; satellites 0.25 d), only the force model");
        sb.AppendLine("changes. It never ranks a method (TASK.md B1).");
        sb.AppendLine();
        sb.AppendLine("Scope of this run (profile `" + profile.ToString().ToLowerInvariant() + "`): "
            + "windows " + string.Join(" + ", windows.ConvertAll(w => w.Name))
            + ", models " + string.Join(", ", models.ConvertAll(m => m.Name))
            + (satelliteRows.Count > 0 ? ", satellites (Test544 path)" : "")
            + ".");
        sb.AppendLine();
        sb.AppendLine("## V0 precondition (TASK.md B5)");
        sb.AppendLine();
        sb.AppendLine("| check | result | detail |");
        sb.AppendLine("|---|---|---|");
        foreach (QualificationNasaCheck check in checks)
        {
            sb.Append("| ").Append(check.Name).Append(" | ")
                .Append(check.Ok ? "PASS" : "FAIL").Append(" | ")
                .Append(check.Detail).AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("## G9 reproducibility against the unchanged tests");
        sb.AppendLine();
        sb.AppendLine("Reference values: `nasa_g9_reference.csv`, transcribed from the B0 run of");
        sb.AppendLine("`Test537_JplFullComparison`, `Test538_JplHoldout` and `Test544_JplSatelliteComparison`");
        sb.AppendLine("(commit `05b7869`, see `INVENTORY.md` -> B-G9).");
        sb.AppendLine("Gate: `|stand - reference| <= max(0.01 km, 0.5 %)` per body and model.");
        sb.AppendLine();
        sb.AppendLine("| window | model | body | day | stand (km) | reference (km) | delta (km) | tol (km) | status |");
        sb.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---|");
        int compared = 0;
        int failed = 0;
        foreach (QualificationNasaG9 expected in reference)
        {
            double actual = expected.Model == "satellites"
                ? QualificationNasaSatelliteValue(
                    satelliteRows, expected.Window, expected.Body, expected.Days)
                : QualificationNasaValue(
                    rows, expected.Window, expected.Model, expected.Body, expected.Days);
            if (double.IsNaN(actual))
            {
                continue;
            }

            compared++;
            double tolerance = Math.Max(0.01d, 0.005d * Math.Abs(expected.DpKm));
            double delta = actual - expected.DpKm;
            bool ok = Math.Abs(delta) <= tolerance;
            if (!ok)
            {
                failed++;
            }

            sb.Append("| ").Append(expected.Window).Append(" | ").Append(expected.Model)
                .Append(" | ").Append(expected.Body).Append(" | ")
                .Append(expected.Days.ToString("F0", QualificationInv)).Append(" | ")
                .Append(actual.ToString("F4", QualificationInv)).Append(" | ")
                .Append(expected.DpKm.ToString("F4", QualificationInv)).Append(" | ")
                .Append(delta.ToString("+0.0000;-0.0000;0.0000", QualificationInv)).Append(" | ")
                .Append(tolerance.ToString("F4", QualificationInv)).Append(" | ")
                .Append(ok ? "ok" : "DRIFT").AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("Compared " + compared.ToString(QualificationInv) + " value(s), "
            + failed.ToString(QualificationInv) + " outside tolerance"
            + (drift == failed ? "" : " (log drift counter " + drift.ToString(QualificationInv) + ")")
            + ". A drift is an ERROR of the adapter, not a method status (TASK.md B5).");
        sb.AppendLine();
        sb.AppendLine("## Position error vs Horizons at 10 years (km)");
        foreach (QualificationNasaWindow window in windows)
        {
            sb.AppendLine();
            sb.AppendLine("Window " + window.Name + ".");
            sb.AppendLine();
            sb.Append("| body |");
            foreach (QualificationNasaModel model in models)
            {
                sb.Append(' ').Append(model.Name).Append(" |");
            }

            sb.AppendLine();
            sb.Append("|---|");
            foreach (QualificationNasaModel model in models)
            {
                sb.Append("---:|");
            }

            sb.AppendLine();
            foreach (string body in QualificationNasaBodies)
            {
                sb.Append("| ").Append(body == "Moon" ? "Moon(geo)" : body).Append(" |");
                foreach (QualificationNasaModel model in models)
                {
                    sb.Append(' ').Append(QualificationNasaKm(
                        QualificationNasaValue(rows, window.Name, model.Name, body, 3650d)))
                        .Append(" |");
                }

                sb.AppendLine();
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Metrics of TASK.md B5");
        sb.AppendLine();
        sb.AppendLine("`rms8_end` = RMS over the eight planets at 10 years (the `Study/jpl` README");
        sb.AppendLine("definition); `rms8_all` = RMS over all 11 annual slices (day 0 included, it is");
        sb.AppendLine("measured, not assumed); `max_all` = worst annual RMS(8) over the slices; the Moon");
        sb.AppendLine("is geocentric and reported separately.");
        sb.AppendLine();
        sb.AppendLine("| window | model | rms8_end (km) | rms8_all (km) | max_all (km) | Moon 1 y | Moon 5 y | Moon 10 y |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
        foreach (QualificationNasaWindow window in windows)
        {
            foreach (QualificationNasaModel model in models)
            {
                double sum = 0d;
                double worstSlice = 0d;
                int slices = 0;
                for (int year = 0; year <= QualificationNasaLastYear; year++)
                {
                    double sliceRms = QualificationNasaRms(
                        rows, window.Name, model.Name, 365d * year, out _);
                    if (double.IsNaN(sliceRms))
                    {
                        continue;
                    }

                    sum += sliceRms * sliceRms;
                    worstSlice = Math.Max(worstSlice, sliceRms);
                    slices++;
                }

                double rmsAll = slices == 0 ? double.NaN : Math.Sqrt(sum / slices);
                double end = QualificationNasaRms(rows, window.Name, model.Name, 3650d, out _);
                sb.Append("| ").Append(window.Name).Append(" | ").Append(model.Name).Append(" | ")
                    .Append(QualificationNasaKm(end)).Append(" | ")
                    .Append(QualificationNasaKm(rmsAll)).Append(" | ")
                    .Append(QualificationNasaKm(worstSlice)).Append(" | ")
                    .Append(QualificationNasaKm(QualificationNasaValue(
                        rows, window.Name, model.Name, "Moon", 365d))).Append(" | ")
                    .Append(QualificationNasaKm(QualificationNasaValue(
                        rows, window.Name, model.Name, "Moon", 1825d))).Append(" | ")
                    .Append(QualificationNasaKm(QualificationNasaValue(
                        rows, window.Name, model.Name, "Moon", 3650d))).AppendLine(" |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## E_model for the Q goals (TASK.md B6)");
        sb.AppendLine();
        sb.AppendLine("| window | model | rms8_end (km) | Moon 10 y (km) |");
        sb.AppendLine("|---|---|---:|---:|");
        foreach (QualificationNasaWindow window in windows)
        {
            foreach (QualificationNasaModel model in models)
            {
                double end = QualificationNasaRms(rows, window.Name, model.Name, 3650d, out _);
                sb.Append("| ").Append(window.Name).Append(" | ").Append(model.Name).Append(" | ")
                    .Append(QualificationNasaKm(end)).Append(" | ")
                    .Append(QualificationNasaKm(QualificationNasaValue(
                        rows, window.Name, model.Name, "Moon", 3650d))).AppendLine(" |");
            }
        }

        if (satelliteRows.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Satellites at 10 years (Test544 path, km relative to the planet centre)");
            sb.AppendLine();
            sb.AppendLine("| window | moon | centre | dp (km) | dv (m/s) |");
            sb.AppendLine("|---|---|---|---:|---:|");
            foreach (QualificationNasaSatelliteRow row in satelliteRows)
            {
                sb.Append("| ").Append(row.Window).Append(" | ").Append(row.Moon).Append(" | ")
                    .Append(row.Center).Append(" | ")
                    .Append(row.DpKm.ToString("F2", QualificationInv)).Append(" | ")
                    .Append(row.DvMs.ToString("E2", QualificationInv)).AppendLine(" |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Limitations (unchanged block, source: `Study/jpl/README.md` section 4)");
        sb.AppendLine();
        sb.AppendLine("1. Model structure (solar J2, the small-body set) was chosen after inspecting the");
        sb.AppendLine("   2026 residuals; 2016 is a holdout for the parameter values, not for the structure.");
        sb.AppendLine("2. The full EIH is worse than 1PN on the Moon and the inner planets; the reason is not");
        sb.AppendLine("   understood. EIH with the 409 small bodies was not run.");
        sb.AppendLine("3. `rms8_end` comes from a file sampled once per year; Mercury (88 d) and the Moon");
        sb.AppendLine("   (27 d) are phase-sensitive at 365 d sampling.");
        sb.AppendLine("4. The 409 small bodies are test particles: they feel the planets but do not perturb");
        sb.AppendLine("   each other.");
        sb.AppendLine("5. The satellites are limited by the model (thousands of km), not by the integrator.");
        sb.AppendLine("6. This uses the study DOP853, not the production game integrator.");

        return sb.ToString();
    }
}
