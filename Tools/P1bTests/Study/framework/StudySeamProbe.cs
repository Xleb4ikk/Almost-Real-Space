using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Galilego.Universe;

namespace P1bStudy.Framework
{
    /// <summary>
    /// T7 measurement only: ephemeris type, segment length, seams in the window, and
    /// cost/error of three methods with and without landing on every baked seam.
    /// It never changes the default sweep.
    /// </summary>
    internal static class StudySeamProbe
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void Run(
            TextWriter log, string csvPath, string label, StarSystem sys, StudyProblem p,
            double dopRtol, double rknRtol, double whH)
        {
            double[] seams = SeamTimes(sys, p);
            log.WriteLine();
            log.WriteLine("=== seam probe: " + label + " ===");
            log.WriteLine("window: " + p.T0.ToString("G6", Inv) + " .. " + p.EndTime.ToString("G6", Inv)
                + " s; checkpoints=" + p.CheckpointTimes.Length.ToString(Inv)
                + "; seams in window=" + seams.Length.ToString(Inv));
            foreach (OrbitingBody body in sys.AllBodies)
            {
                BakedEphemeris baked = body.Baked;
                string kind = baked == null
                    ? "analytic rails"
                    : "baked Chebyshev degree=" + baked.Degree.ToString(Inv)
                      + " seg=" + baked.SegmentLengthSeconds.ToString("G6", Inv)
                      + " s count=" + baked.SegmentCount.ToString(Inv);
                log.WriteLine("  " + body.Name + ": " + kind);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(csvPath) ?? ".");
            bool writeHeader = !File.Exists(csvPath);
            var sb = new System.Text.StringBuilder();
            if (writeHeader)
            {
                sb.AppendLine("scenario,method,seam_stops,control,calls,aux_ops,cost_rhs_eq,err_m,steps,nck,seams");
            }

            double w = StudyCalibration.Resolve(p, new StudyOptions()).Weight;
            RunOne(sb, log, p, seams, label, "DOP853-adaptive", new Dop853AdaptiveIntegrator(), dopRtol, w, true);
            RunOne(sb, log, p, seams, label, "DOP853-adaptive", new Dop853AdaptiveIntegrator(), dopRtol, w, false);
            RunOne(sb, log, p, seams, label, "RKN12(10)", new Rkn1210Integrator(), rknRtol, w, true);
            RunOne(sb, log, p, seams, label, "RKN12(10)", new Rkn1210Integrator(), rknRtol, w, false);
            RunOne(sb, log, p, seams, label, "WH-BM14A", new KeplerSplit14AIntegrator(), whH, w, true);
            RunOne(sb, log, p, seams, label, "WH-BM14A", new KeplerSplit14AIntegrator(), whH, w, false);
            File.AppendAllText(csvPath, sb.ToString());
        }

        private static void RunOne(
            System.Text.StringBuilder sb, TextWriter log, StudyProblem p, double[] seams,
            string label, string name, IStudyIntegrator integ, double control, double w, bool withSeams)
        {
            StudyProblem q = p.ShallowClone();
            if (withSeams)
            {
                q.Breakpoints = Merge(p.Breakpoints, seams);
            }

            try
            {
                StudyConfig cfg = integ.MakeConfig(q, control, double.PositiveInfinity);
                var ctx = new StudyContext(q, 20000000L);
                StudyRun run = integ.Run(q, cfg, ctx);
                var row = new StudyRow();
                StudySweepRunner.EvaluateError(q, run, row);
                double cost = ctx.Calls + (w * ctx.AuxOps);
                sb.Append(Csv(label)).Append(',')
                    .Append(Csv(name)).Append(',')
                    .Append(withSeams ? "1" : "0").Append(',')
                    .Append(control.ToString("R", Inv)).Append(',')
                    .Append(ctx.Calls.ToString(Inv)).Append(',')
                    .Append(ctx.AuxOps.ToString(Inv)).Append(',')
                    .Append(cost.ToString("R", Inv)).Append(',')
                    .Append(row.Err.ToString("R", Inv)).Append(',')
                    .Append(run.Steps.ToString(Inv)).Append(',')
                    .Append(q.CheckpointTimes.Length.ToString(Inv)).Append(',')
                    .Append(seams.Length.ToString(Inv)).AppendLine();
                log.WriteLine("  " + name + (withSeams ? " +seams" : " -seams")
                    + ": control=" + control.ToString("G6", Inv)
                    + " calls=" + ctx.Calls.ToString("N0", Inv)
                    + " aux=" + ctx.AuxOps.ToString("N0", Inv)
                    + " cost=" + cost.ToString("N0", Inv)
                    + " err=" + row.Err.ToString("E3", Inv) + " m"
                    + " steps=" + run.Steps.ToString("N0", Inv));
            }
            catch (Exception ex)
            {
                log.WriteLine("  " + name + (withSeams ? " +seams" : " -seams")
                    + ": FAILED " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static double[] SeamTimes(StarSystem sys, StudyProblem p)
        {
            var times = new List<double>();
            foreach (OrbitingBody body in sys.AllBodies)
            {
                BakedEphemeris baked = body.Baked;
                if (baked == null || !(baked.SegmentLengthSeconds > 0d) || baked.SegmentCount <= 1)
                {
                    continue;
                }

                for (int k = 1; k < baked.SegmentCount; k++)
                {
                    double t = baked.T0Seconds + (k * baked.SegmentLengthSeconds);
                    if (t > p.T0 + 1e-6d && t < p.EndTime - 1e-6d)
                    {
                        times.Add(t);
                    }
                }
            }

            times.Sort();
            var unique = new List<double>();
            foreach (double t in times)
            {
                if (unique.Count == 0 || t - unique[unique.Count - 1] > 1e-6d)
                {
                    unique.Add(t);
                }
            }

            return unique.ToArray();
        }

        private static double[] Merge(double[] a, double[] b)
        {
            var all = new List<double>();
            if (a != null)
            {
                all.AddRange(a);
            }

            if (b != null)
            {
                all.AddRange(b);
            }

            all.Sort();
            var unique = new List<double>();
            foreach (double t in all)
            {
                if (unique.Count == 0 || t - unique[unique.Count - 1] > 1e-9d)
                {
                    unique.Add(t);
                }
            }

            return unique.ToArray();
        }

        private static string Csv(string s)
        {
            if (s == null)
            {
                return "";
            }

            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
            {
                return s;
            }

            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
