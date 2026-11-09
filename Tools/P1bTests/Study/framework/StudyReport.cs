using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace P1bStudy.Framework
{
    /// <summary>Cost (and time) needed to reach one error target, with an explicit status so a grid edge is never presented as a result.</summary>
    internal sealed class StudyCell
    {
        /// <summary>OK = price of the cheapest executed run whose error is at or below the target;
        /// EDGE = even the cheapest (coarsest) run is below the target, so the cost is an
        /// upper bound (cap-limited EDGE is exact, other EDGE is unresolved); FLOOR = only runs whose error is
        /// within their own double-precision accumulation noise reach the target; NR = not reached; SKIP = target
        /// below the reference floor margin; NA = method not valid for this problem.</summary>
        public string Status = "NR";
        public double Cost = double.NaN;
        public double Ms = double.NaN;
        public double Err = double.NaN;
        public double CostHit = double.NaN;
        public double ErrHit = double.NaN;
        public double CostMiss = double.NaN;
        public double ErrMiss = double.NaN;
        public double BracketRatio = double.NaN;
        public bool BracketWide;
        public bool BracketQuantized;
        public string Note = "";

        /// <summary>Kept for format compatibility; the single price rule never interpolates.</summary>
        public bool Interpolated;

        /// <summary>An EDGE cell with no cap evidence, or a cell that could not be bracketed. The winner is provisional.</summary>
        public bool Unresolved;

        /// <summary>The EDGE upper bound comes from a cap-limited run, so the cap itself makes it exact.</summary>
        public bool CapLimited;

        /// <summary>Relative control gap of the bracketing pair; the tie band when interpolation is unavailable.</summary>
        public double GridStep = double.NaN;

        /// <summary>Tie band for this cell (1.10 = 10%). NaN means use the caller's default.</summary>
        public double TieFactor = double.NaN;

        /// <summary>"rhs" or "ms"; which metric is stored in Cost.</summary>
        public string Metric = "rhs";
    }

    internal static class StudyReport
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void FinalizeCosts(List<StudyRow> rows, double keplerWeight)
        {
            foreach (StudyRow r in rows)
            {
                r.Cost = r.Calls + (keplerWeight * r.Aux);
            }
        }

        /// <summary>
        /// Price at the target: the cheapest EXECUTED run whose error is at or below the
        /// target. No interpolation and no extrapolation. The bracketing miss is reported
        /// as a diagnostic (cost_miss / err_miss / bracket_ratio), never as the price.
        /// A run whose error is below the double-precision round-off floor of its own step count
        /// is not evidence of accuracy (FLOOR). Cap-limited EDGE cells are exact.
        /// byMs selects wall-clock ms instead of RHS-equivalent cost.
        /// </summary>
        public static StudyCell CostAtTarget(
            List<StudyRow> variantRows, double target, StudyProblem p, bool byMs = false)
        {
            var pts = new List<StudyRow>();
            foreach (StudyRow r in variantRows)
            {
                if (r.Status == "ok" && r.Finite && MetricOf(r, byMs) > 0d)
                {
                    pts.Add(r);
                }
            }

            var cell = new StudyCell();
            cell.Metric = byMs ? "ms" : "rhs";
            if (pts.Count == 0)
            {
                cell.Status = "NR";
                cell.Note = "no finite point";
                return cell;
            }

            pts.Sort(delegate (StudyRow a, StudyRow b)
            {
                return MetricOf(a, byMs).CompareTo(MetricOf(b, byMs));
            });
            StudyRow cheapest = pts[0];
            StudyRow hit = null;
            StudyRow finest = cheapest;
            double floorAtCheapestBelow = double.NaN;
            for (int i = 0; i < pts.Count; i++)
            {
                StudyRow r = pts[i];
                if (r.Err < finest.Err)
                {
                    finest = r;
                }

                if (r.Err <= target)
                {
                    double floor = RoundoffFloor(p, r);
                    if (double.IsNaN(floorAtCheapestBelow))
                    {
                        floorAtCheapestBelow = floor;
                    }

                    // A run that claims an error below its own accumulation noise is
                    // not evidence that the method resolved the target; the column is
                    // measuring summation, so it must not be ranked.
                    if (!(target <= floor) && !(r.Err <= floor))
                    {
                        hit = r;
                        break;
                    }
                }
            }

            if (hit == null)
            {
                if (!double.IsNaN(floorAtCheapestBelow))
                {
                    cell.Status = "FLOOR";
                    cell.Note = "below round-off floor ("
                        + floorAtCheapestBelow.ToString("E2", Inv) + " m)";
                    return cell;
                }

                cell.Status = "NR";
                cell.Note = "finest error " + finest.Err.ToString("E2", Inv);
                return cell;
            }

            cell.CapLimited = hit.CapLimited > 0L || hit.Variant.Length > 0;
            cell.Cost = MetricOf(hit, byMs);
            cell.Ms = byMs ? cell.Cost : hit.Ms;
            cell.Err = hit.Err;

            double hitMetric = MetricOf(hit, byMs);
            cell.Cost = hitMetric;
            cell.Ms = byMs ? hitMetric : hit.Ms;
            cell.Err = hit.Err;
            cell.CostHit = hitMetric;
            cell.ErrHit = hit.Err;

            // Nearest cheaper executed run that did NOT reach the target. It defines
            // the bracket; the reported price is always the hit's real price.
            StudyRow nearestMiss = null;
            double missMetric = double.NegativeInfinity;
            foreach (StudyRow r in pts)
            {
                double m = MetricOf(r, byMs);
                if (r.Err > target && m > 0d && m < hitMetric && m > missMetric)
                {
                    missMetric = m;
                    nearestMiss = r;
                }
            }

            if (nearestMiss != null)
            {
                cell.CostMiss = missMetric;
                cell.ErrMiss = nearestMiss.Err;
                cell.BracketRatio = missMetric > 0d ? hitMetric / missMetric : double.NaN;
                cell.BracketWide = cell.BracketRatio > 1.10d;
                cell.BracketQuantized = IsQuantized(hit, nearestMiss);
                cell.GridStep = RelativeGap(hit.Control, nearestMiss.Control);
            }
            else
            {
                cell.CostMiss = double.NaN;
                cell.ErrMiss = double.NaN;
                cell.BracketRatio = double.NaN;
                cell.BracketWide = true;
            }

            if (ReferenceEquals(hit, cheapest))
            {
                cell.Status = "EDGE";
                cell.Unresolved = !cell.CapLimited;
                cell.Note = cell.CapLimited
                    ? "cap-limited run already below target"
                    : "cheapest tested point already below target; no coarser bracket";
                cell.TieFactor = cell.CapLimited ? 1d : double.NaN;
                return cell;
            }

            cell.Status = "OK";
            cell.TieFactor = 1.10d;
            cell.Note = nearestMiss == null
                ? "no cheaper above-target run; first passing point"
                : cell.BracketQuantized
                    ? "bracket is quantized by the checkpoint step grid; first passing point"
                    : cell.BracketWide
                        ? "bracket wider than 1.10; first passing point"
                        : "first passing point";
            cell.Unresolved = nearestMiss == null;
            return cell;
        }

        /// <summary>
        /// True when both ends of the bracket sit on the all-checkpoint fixed-step grid:
        /// with equal intervals the step count is a multiple of nck, so no intermediate
        /// run exists between (for example) 20 and 40 steps of WH-BM14A.
        /// </summary>
        private static bool IsQuantized(StudyRow hit, StudyRow miss)
        {
            if (hit == null || miss == null || hit.Kind != StudyKind.Fixed
                || !string.Equals(hit.CheckpointMode, "all", StringComparison.Ordinal)
                || hit.Nck <= 1 || hit.Steps <= 0L || miss.Steps <= 0L
                || hit.Steps == miss.Steps)
            {
                return false;
            }

            return hit.Steps % hit.Nck == 0L && miss.Steps % hit.Nck == 0L;
        }

        private static double MetricOf(StudyRow r, bool byMs)
        {
            return byMs ? r.Ms : r.Cost;
        }

        private static double RelativeGap(double a, double b)
        {
            double scale = Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), 1e-300d);
            return Math.Abs(a - b) / scale;
        }

        private static double InterpolateTime(StudyRow miss, StudyRow hit, double weight)
        {
            if (miss.Ms > 0d && hit.Ms > 0d
                && !double.IsNaN(miss.Ms) && !double.IsNaN(hit.Ms)
                && !double.IsInfinity(miss.Ms) && !double.IsInfinity(hit.Ms))
            {
                return Math.Exp(Math.Log(miss.Ms) + (weight * (Math.Log(hit.Ms) - Math.Log(miss.Ms))));
            }

            return hit.Ms;
        }

        /// <summary>
        /// Accumulation noise of one run: 16 eps times the state scale times sqrt(steps), a
        /// conservative random-walk bound. Below it an error difference is summation quality,
        /// not method order.
        /// </summary>
        public static double RoundoffFloor(StudyProblem p, StudyRow r)
        {
            if (r == null || r.Steps <= 0L)
            {
                return 0d;
            }

            return UnitRoundoff * Math.Max(p.ScaleLength, 1d) * Math.Sqrt(r.Steps);
        }

        public static List<string> Keys(List<StudyRow> rows)
        {
            var keys = new List<string>();
            foreach (StudyRow r in rows)
            {
                if (!keys.Contains(r.Key))
                {
                    keys.Add(r.Key);
                }
            }

            return keys;
        }

        public static List<StudyRow> RowsOf(List<StudyRow> rows, string key)
        {
            var list = new List<StudyRow>();
            foreach (StudyRow r in rows)
            {
                if (r.Key == key)
                {
                    list.Add(r);
                }
            }

            return list;
        }

        public static StudyCell CellFor(
            StudyScenarioResult res, string key, StudyTarget target, StudyOptions o, bool byMs = false)
        {
            List<StudyRow> rows = RowsOf(res.Rows, key);
            var cell = new StudyCell();
            if (rows.Count > 0 && rows[0].Status == "unsupported")
            {
                cell.Status = "NA";
                cell.Note = rows[0].Note;
                return cell;
            }

            double floor = double.IsNaN(res.Problem.Floor) ? 0d : res.Problem.Floor;
            if (target.Value < o.FloorMargin * floor)
            {
                cell.Status = "SKIP";
                cell.Note = "below reference floor (" + (o.FloorMargin * floor).ToString("E2", Inv) + " m required)";
                return cell;
            }

            cell = CostAtTarget(rows, target.Value, res.Problem, byMs);
            if (double.IsNaN(cell.TieFactor) && cell.Status == "OK")
            {
                cell.TieFactor = o.TieFactor;
            }

            return cell;
        }

        /// <summary>Per-variant findings that make a number untrustworthy. Every entry is a sentence a person can act on.</summary>
        public static List<string> Diagnose(StudyScenarioResult res, StudyOptions o)
        {
            var notes = new List<string>();
            StudyProblem p = res.Problem;
            double floor = double.IsNaN(p.Floor) ? 0d : p.Floor;
            if (!p.Certified || double.IsNaN(p.Floor))
            {
                notes.Add("REFERENCE NOT CERTIFIED: every number of this scenario is provisional (no reference floor)."
                    + (string.IsNullOrEmpty(p.ReferenceNote) ? "" : " Reference: " + p.ReferenceNote));
            }

            foreach (string key in Keys(res.Rows))
            {
                List<StudyRow> rows = RowsOf(res.Rows, key);
                if (rows[0].Status == "unsupported")
                {
                    continue;
                }

                int censored = 0;
                int errors = 0;
                int nonFinite = 0;
                string firstError = "";
                string firstNonFinite = "";
                int capBound = 0;
                foreach (StudyRow r in rows)
                {
                    if (r.Status == "censored")
                    {
                        censored++;
                    }

                    if (r.Status == "error")
                    {
                        errors++;
                        if (firstError.Length == 0)
                        {
                            firstError = r.Note;
                        }
                    }

                    if (r.Status == "ok" && !r.Finite)
                    {
                        nonFinite++;
                        if (firstNonFinite.Length == 0)
                        {
                            firstNonFinite = r.Note;
                        }
                    }

                    if (r.Kind == StudyKind.Adaptive && r.Steps > 0 && r.CapLimited * 2 >= r.Steps)
                    {
                        capBound++;
                    }
                }

                if (censored > 0)
                {
                    notes.Add(key + ": " + censored.ToString(Inv) + " run(s) hit the RHS budget (cost not reached = censored, not 'expensive').");
                }

                if (errors > 0)
                {
                    notes.Add(key + ": " + errors.ToString(Inv) + " run(s) failed: " + firstError);
                }

                if (nonFinite > 0)
                {
                    notes.Add(key + ": " + nonFinite.ToString(Inv)
                        + " run(s) did not complete the horizon: "
                        + (firstNonFinite.Length == 0 ? "no finite samples (reason not recorded)" : firstNonFinite)
                        + "; excluded from ranking, finer controls were still tried.");
                }

                if (capBound > 0)
                {
                    notes.Add(key + ": CAP-BOUND in " + capBound.ToString(Inv) + " run(s); the step cap, not the tolerance, sets the cost there.");
                }

                if (rows[0].Kind == StudyKind.Fixed)
                {
                    DiagnoseFixed(key, rows, p, floor, notes);
                }
            }

            DiagnoseAdaptiveCap(res, notes);
            DiagnoseTies(res, o, notes);
            DiagnoseRoundoff(res, o, notes);
            return notes;
        }

        /// <summary>
        /// A capped variant that is dramatically better than its uncapped twin is a
        /// result in itself: the controller does not survive the horizon without the
        /// cap, so the cap is load-bearing and must not be read as a cosmetic config.
        /// </summary>
        private static void DiagnoseAdaptiveCap(StudyScenarioResult res, List<string> notes)
        {
            var bases = new List<string>();
            foreach (StudyRow r in res.Rows)
            {
                if (r.Kind == StudyKind.Adaptive && !bases.Contains(r.Integrator))
                {
                    bases.Add(r.Integrator);
                }
            }

            foreach (string baseName in bases)
            {
                double uncapped = double.PositiveInfinity;
                bool uncappedFinite = false;
                bool uncappedSeen = false;
                string capVariant = null;
                double capped = double.PositiveInfinity;
                foreach (StudyRow r in res.Rows)
                {
                    if (r.Integrator != baseName || r.Status != "ok")
                    {
                        continue;
                    }

                    if (r.Variant.Length == 0)
                    {
                        uncappedSeen = true;
                        if (r.Finite)
                        {
                            uncappedFinite = true;
                            uncapped = Math.Min(uncapped, r.Err);
                        }
                    }
                    else if (r.Finite && r.Err < capped)
                    {
                        capped = r.Err;
                        capVariant = r.Key;
                    }
                }

                if (capVariant == null || !double.IsFinite(capped))
                {
                    continue;
                }

                double limit = 100d * Math.Max(capped, 1e-30d);
                bool uncappedBad = uncappedSeen && (!uncappedFinite || uncapped > limit);
                if (uncappedBad)
                {
                    notes.Add(baseName + ": the uncapped branch "
                        + (uncappedFinite
                            ? "reached only " + uncapped.ToString("E2", Inv) + " m"
                            : "produced no finite samples")
                        + " while " + capVariant + " reached " + capped.ToString("E2", Inv)
                        + " m; the step cap is load-bearing on this horizon.");
                }
            }
        }

        private static string BaseKey(string key)
        {
            int i = key.IndexOf(" [", StringComparison.Ordinal);
            return i >= 0 ? key.Substring(0, i) : key;
        }

        /// <summary>
        /// A cap is a configuration of a method, not a method. Name the cap variant only
        /// when it is materially different from the same method's uncapped run: the
        /// uncapped branch does not reach the target at all, or is more than a tie factor
        /// more expensive. Otherwise the plain method name is the honest label (the cap
        /// did not decide the result).
        /// </summary>
        private static string VariantLabel(
            StudyScenarioResult res, string key, StudyTarget target, StudyOptions o,
            double winnerCost, bool byMs = false)
        {
            // Every cap variant is its own method in summaries and tables. Never fold
            // "DOP853-adaptive [cap=600 s]" into the uncapped name.
            return key;
        }

        private static void DiagnoseTies(StudyScenarioResult res, StudyOptions o, List<string> notes)
        {
            foreach (StudyTarget target in o.Targets(res.Problem))
            {
                var keys = new List<string>();
                var costs = new List<double>();
                double best = double.PositiveInfinity;
                StudyCell bestCell = null;
                foreach (string key in Keys(res.Rows))
                {
                    StudyCell c = CellFor(res, key, target, o);
                    if (c.Status != "OK")
                    {
                        continue;
                    }

                    keys.Add(key);
                    costs.Add(c.Cost);
                    if (c.Cost < best)
                    {
                        best = c.Cost;
                        bestCell = c;
                    }
                }

                int ties = 0;
                string detail = "";
                double tieFactor = bestCell != null && !double.IsNaN(bestCell.TieFactor)
                    ? bestCell.TieFactor
                    : o.TieFactor;
                double limit = best * Math.Max(1d, tieFactor);
                for (int i = 0; i < keys.Count; i++)
                {
                    if (costs[i] <= limit)
                    {
                        ties++;
                        if (detail.Length < 160)
                        {
                            detail += (detail.Length == 0 ? "" : ", ")
                                + keys[i] + " (" + Num(costs[i]) + ")";
                        }
                    }
                }

                if (ties > 1)
                {
                    notes.Add("TIE at " + target.Label + ": " + detail
                        + " are within " + ((tieFactor - 1d) * 100d).ToString("F0", Inv)
                        + "%; do not read the ranking as resolved.");
                }
            }
        }

        private const double UnitRoundoff = 16d * 2.220446049250313e-16;

        /// <summary>
        /// A long integration at a large distance is limited by double-precision accumulation,
        /// not by the method's order. The estimate uses the state scale and the largest step
        /// count in the table: when the tightest target sits at that scale, the column measures
        /// summation quality (e.g. compensated Y6C), and the report must say so instead of
        /// implying that every other method converges too slowly.
        /// </summary>
        private static void DiagnoseRoundoff(StudyScenarioResult res, StudyOptions o, List<string> notes)
        {
            StudyProblem p = res.Problem;
            StudyRow bestRun = null;
            foreach (StudyRow r in res.Rows)
            {
                if (r.Status == "ok" && r.Finite && r.Steps > 0L
                    && (bestRun == null || r.Err < bestRun.Err))
                {
                    bestRun = r;
                }
            }

            if (bestRun == null)
            {
                return;
            }

            double estimate = RoundoffFloor(p, bestRun);
            if (!(estimate > 0d) || double.IsNaN(estimate) || double.IsInfinity(estimate))
            {
                return;
            }

            double tightest = double.PositiveInfinity;
            double floor = double.IsNaN(p.Floor) ? 0d : p.Floor;
            foreach (StudyTarget target in o.Targets(p))
            {
                if (target.Value >= o.FloorMargin * floor && target.Value < tightest)
                {
                    tightest = target.Value;
                }
            }

            int floorCells = 0;
            foreach (string key in Keys(res.Rows))
            {
                foreach (StudyTarget target in o.Targets(p))
                {
                    if (CellFor(res, key, target, o).Status == "FLOOR")
                    {
                        floorCells++;
                    }
                }
            }

            if (!(tightest <= estimate) && floorCells == 0)
            {
                return;
            }

            notes.Add("double-precision round-off estimate " + estimate.ToString("E2", Inv)
                + " m (16 eps * scale * sqrt(steps=" + bestRun.Steps.ToString("N0", Inv)
                + " of the most accurate run); each cell uses its own run's step count, and a run "
                + "whose error is within that noise is reported as 'below round-off floor', not as a ranking result"
                + (floorCells > 0 ? " (" + floorCells.ToString(Inv) + " cell(s) excluded)" : "")
                + ".");
        }

        private static void DiagnoseFixed(string key, List<StudyRow> rows, StudyProblem p, double floor, List<string> notes)
        {
            var pts = new List<StudyRow>();
            foreach (StudyRow r in rows)
            {
                if (r.Status == "ok" && r.Finite)
                {
                    pts.Add(r);
                }
            }

            pts.Sort(delegate (StudyRow a, StudyRow b) { return b.Control.CompareTo(a.Control); });
            var slopes = new List<double>();
            int grows = 0;
            int transient = 0;
            double transientCoarse = double.NaN;
            double transientFine = double.NaN;
            double transientRecover = double.NaN;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                StudyRow a = pts[i];
                StudyRow b = pts[i + 1];
                if (!(b.Control < a.Control))
                {
                    continue;
                }

                bool usable = a.Err > 30d * floor && b.Err > 30d * floor
                    && a.Err < 0.05d * p.ScaleLength && b.Err < 0.05d * p.ScaleLength;
                if (b.Err > 2d * a.Err && b.Err > 3d * floor && a.Err < 0.05d * p.ScaleLength)
                {
                    // A local bump at coarse h that a finer run later recovers from is
                    // pre-asymptotic, not a round-off floor. Only an increase that no
                    // finer h improves is terminal.
                    bool recovered = false;
                    double recoverControl = double.NaN;
                    for (int j = i + 2; j < pts.Count; j++)
                    {
                        if (pts[j].Err < a.Err)
                        {
                            recovered = true;
                            recoverControl = pts[j].Control;
                            break;
                        }
                    }

                    if (recovered)
                    {
                        if (transient == 0)
                        {
                            transientCoarse = a.Control;
                            transientFine = b.Control;
                            transientRecover = recoverControl;
                        }

                        transient++;
                    }
                    else
                    {
                        grows++;
                    }
                }

                if (usable && b.Err < a.Err)
                {
                    slopes.Add(Math.Log(a.Err / b.Err) / Math.Log(a.Control / b.Control));
                }
            }

            if (grows > 0)
            {
                notes.Add(key + ": error GROWS as h shrinks in " + grows.ToString(Inv)
                    + " place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').");
            }

            if (transient > 0)
            {
                notes.Add(key + ": error is non-monotonic at coarse h in " + transient.ToString(Inv)
                    + " place(s) but recovers at finer h (h="
                    + transientCoarse.ToString("G3", Inv) + " -> "
                    + transientFine.ToString("G3", Inv) + " s, recovers by h="
                    + transientRecover.ToString("G3", Inv)
                    + " s); that coarse branch is pre-asymptotic, not a round-off floor.");
            }

            double expected = rows[0].ExpectedOrder;
            if (slopes.Count >= 2 && !double.IsNaN(expected))
            {
                slopes.Sort();
                double med = slopes[slopes.Count / 2];
                if (med < expected - 1d)
                {
                    notes.Add(key + ": observed order " + med.ToString("F2", Inv) + " vs expected "
                        + expected.ToString("F0", Inv) + " (" + slopes.Count.ToString(Inv) + " pairs): check the implementation or the regime.");
                }
            }
        }

        // -------------------------------------------------------------------------------
        // Tables
        // -------------------------------------------------------------------------------

        /// <summary>Nominal force evaluations per step (GJ8 is one RHS per multistep step).</summary>
        public static int StagesOf(string name)
        {
            switch (name)
            {
                case "RK4": return 4;
                case "DOPRI5-fixed":
                case "DOPRI5-adaptive": return 6;
                case "DOP853-adaptive": return 12;
                case "PEFRL": return 4;
                case "Y6":
                case "Y6C": return 7;
                case "WH-Y6": return 7;
                case "Leapfrog": return 1;
                case "RKN12(10)": return 16;
                case "IAS15": return 7;
                case "SRKN14A": return 14;
                case "WH-BM14A": return 14;
                case "GJ8": return 1;
                case "Encke-IAS15": return 7;
                default: return 0;
            }
        }

        public static string Num(double v)
        {            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                return "-";
            }

            if (v >= 1e6 || (v != 0d && v < 1e-2))
            {
                return v.ToString("E2", Inv);
            }

            return v.ToString("N0", Inv);
        }

        private static string NumMs(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                return "-";
            }

            if (v < 1d)
            {
                return v.ToString("F3", Inv);
            }

            if (v < 1000d)
            {
                return v.ToString("F1", Inv);
            }

            return v.ToString("N0", Inv);
        }

        public static string CellText(StudyCell c)
        {
            switch (c.Status)
            {
                case "OK":
                    if (c.Metric == "ms")
                    {
                        return NumMs(c.Cost) + (c.Interpolated ? "*" : "") + " ms";
                    }

                    return Num(c.Cost) + (c.Interpolated ? "*" : "")
                        + (double.IsNaN(c.Ms) ? "" : " (" + NumMs(c.Ms) + " ms)");
                case "EDGE":
                    if (c.Metric == "ms")
                    {
                        return "<=" + NumMs(c.Cost) + " ms EDGE";
                    }

                    return "<=" + Num(c.Cost) + " EDGE";
                case "NA":
                    return "n/a";
                case "SKIP":
                    return "skip";
                case "FLOOR":
                    return c.Note;
                default:
                    // Keep the best achieved error visible: on the planet sweep the
                    // uncapped adaptive branch misses 100 m but does reach hundreds of
                    // metres, which is a real work-precision point, not "no data".
                    if (c.Note != null && c.Note.StartsWith("finest error ", StringComparison.Ordinal))
                    {
                        return "not reached (best " + c.Note.Substring("finest error ".Length) + ")";
                    }

                    return "not reached";
            }
        }

        /// <summary>
        /// Winner among OK cells, or an EDGE cell when its upper bound is already
        /// cheaper than every bracketed cell: in that case the method is definitely
        /// cheaper, so naming the bracketed cell and calling it "unresolved" hid a
        /// real winner (the lunar flyby at 10 m). `unresolved` is kept in the
        /// signature for callers, but a cheaper EDGE cell is now a resolved answer.
        /// OK cells within the winner's tie band are appended with "~": interpolated
        /// cells use TieFactor (10%), unbracketed cells use one grid step.
        /// </summary>
        public static string Winner(
            StudyScenarioResult res, StudyTarget target, StudyOptions o, out bool unresolved, bool byMs = false)
        {
            unresolved = false;
            var okKeys = new List<string>();
            var okCells = new List<StudyCell>();
            string bestEdgeKey = null;
            StudyCell bestEdgeCell = null;
            double bestEdge = double.PositiveInfinity;
            foreach (string key in Keys(res.Rows))
            {
                StudyCell c = CellFor(res, key, target, o, byMs);
                if (c.Status == "OK")
                {
                    okKeys.Add(key);
                    okCells.Add(c);
                }
                else if (c.Status == "EDGE" && c.Cost < bestEdge)
                {
                    bestEdge = c.Cost;
                    bestEdgeKey = key;
                    bestEdgeCell = c;
                }
            }

            string bestKey = null;
            StudyCell bestCell = null;
            double bestCost = double.PositiveInfinity;
            for (int i = 0; i < okKeys.Count; i++)
            {
                if (okCells[i].Cost < bestCost)
                {
                    bestCost = okCells[i].Cost;
                    bestKey = okKeys[i];
                    bestCell = okCells[i];
                }
            }

            if (bestEdgeKey != null && bestEdge < bestCost)
            {
                unresolved = bestEdgeCell != null && bestEdgeCell.Unresolved;
                return VariantLabel(res, bestEdgeKey, target, o, bestEdge, byMs)
                    + " (<= " + ValueText(bestEdge, byMs) + ") EDGE";
            }

            if (bestKey == null)
            {
                return "-";
            }

            unresolved = bestCell != null && bestCell.Unresolved;
            if (!unresolved && bestEdgeCell != null && bestEdgeCell.Unresolved
                && bestEdge <= bestCost * 1.25d)
            {
                // The EDGE upper bound is not below the bracketed winner, but it is
                // close enough that a coarser miss could still be cheaper: keep the
                // winner provisional.
                unresolved = true;
            }

            string text = VariantLabel(res, bestKey, target, o, bestCost, byMs)
                + " (" + ValueText(bestCost, byMs, bestCell != null && bestCell.Interpolated) + ")";
            double maxGridStep = 0d;
            for (int i = 0; i < okCells.Count; i++)
            {
                if (!okCells[i].Interpolated && !double.IsNaN(okCells[i].GridStep))
                {
                    maxGridStep = Math.Max(maxGridStep, okCells[i].GridStep);
                }
            }

            double tieFactor = bestCell != null && !double.IsNaN(bestCell.TieFactor)
                ? bestCell.TieFactor
                : o.TieFactor;
            // An adaptive interpolated cost is not directly comparable with a fixed-step
            // first-hit upper bound: widen the tie band to the worst first-hit grid.
            tieFactor = Math.Max(tieFactor, 1d + maxGridStep);
            double tieLimit = bestCost * Math.Max(1d, tieFactor);
            var tieKeys = new List<string>();
            var tieCosts = new List<double>();
            var tieInterpolated = new List<bool>();
            for (int i = 0; i < okKeys.Count; i++)
            {
                if (okKeys[i] == bestKey)
                {
                    continue;
                }

                if (okCells[i].Cost <= tieLimit)
                {
                    int at = tieKeys.IndexOf(okKeys[i]);
                    if (at < 0)
                    {
                        tieKeys.Add(okKeys[i]);
                        tieCosts.Add(okCells[i].Cost);
                        tieInterpolated.Add(okCells[i].Interpolated);
                    }
                }
            }

            int ties = 0;
            for (int i = 0; i < tieKeys.Count && ties < 3; i++)
            {
                text += " ~ " + VariantLabel(res, tieKeys[i], target, o, tieCosts[i], byMs)
                    + " (" + ValueText(tieCosts[i], byMs, tieInterpolated[i]) + ")";
                ties++;
            }

            if (ties > 0)
            {
                text += " [tie";
                if (ties > 1)
                {
                    text += "s";
                }

                text += " within " + ((tieFactor - 1d) * 100d).ToString("F0", Inv) + "%]";
            }

            return text;
        }

        private static string ValueText(double value, bool byMs, bool interpolated = false)
        {
            return (byMs ? NumMs(value) + " ms" : Num(value)) + (interpolated ? "*" : "");
        }

        public static void Print(TextWriter w, StudyScenarioResult res, StudyOptions o)
        {
            StudyProblem p = res.Problem;
            w.WriteLine();
            w.WriteLine("=== " + p.Label + " ===");
            w.WriteLine("reference floor: " + (double.IsNaN(p.Floor) ? "NOT CERTIFIED" : p.Floor.ToString("E2", Inv) + " m")
                + (string.IsNullOrEmpty(p.ReferenceNote) ? "" : " (" + p.ReferenceNote + ")")
                + "; Kepler-solve weight: " + res.KeplerWeight.ToString("F4", Inv) + " RHS"
                + " (CV " + (double.IsNaN(res.KeplerCv) ? "n/a" : (res.KeplerCv * 100d).ToString("F1", Inv) + "%")
                + ", " + res.KeplerSource + ")"
                + "; RHS budget: " + res.MaxCostRhs.ToString("N0", Inv)
                + "; nck=" + p.CheckpointTimes.Length.ToString(Inv)
                + "; checkpoint-mode=" + (p.CheckpointTimes.Length == 1 ? "end" : "all")
                + "; ref=" + p.ReferenceMethod + " [" + p.ReferenceFamily + "]"
                + " independent=" + (p.ReferenceIndependent ? "1" : "0")
                + " resolution=" + p.ReferenceResolution
                + " cross=" + (double.IsNaN(p.ReferenceCrossDiffM) ? "n/a" : p.ReferenceCrossDiffM.ToString("E3", Inv) + " m")
                + "; mode=" + p.Mode
                + "; build: " + (StudyCalibration.IsDebugBuild ? "DEBUG (ms not representative)" : "Release"));
            List<StudyTarget> targets = o.Targets(p);
            var header = new StringBuilder();
            header.Append("integrator".PadRight(30));
            foreach (StudyTarget t in targets)
            {
                header.Append(t.Label.PadRight(28));
            }

            w.WriteLine(header.ToString());
            foreach (string key in Keys(res.Rows))
            {
                var line = new StringBuilder();
                line.Append(key.PadRight(30));
                foreach (StudyTarget t in targets)
                {
                    line.Append(CellText(CellFor(res, key, t, o)).PadRight(28));
                }

                w.WriteLine(line.ToString());
            }

            if (!p.Certified)
            {
                w.WriteLine("  not ranked: reference is not certified (the table above is provisional)");
            }
            else
            {
                foreach (StudyTarget t in targets)
                {
                    bool unresolved;
                    string win = Winner(res, t, o, out unresolved);
                    w.WriteLine("  winner @ " + t.Label + " (RHS): " + win
                        + (unresolved ? "  [UNRESOLVED: no coarser bracket]" : ""));
                    string winMs = Winner(res, t, o, out unresolved, true);
                    w.WriteLine("  winner @ " + t.Label + " (ms):  " + winMs
                        + (unresolved ? "  [UNRESOLVED: no coarser bracket]" : ""));
                }
            }

            foreach (string note in Diagnose(res, o))
            {
                w.WriteLine("  ! " + note);
            }
        }

        public static void WriteCsv(string path, List<StudyScenarioResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("scenario,integrator,variant,config,control,status,finite,err_m,err_final_m,err_rel,calls,aux_ops,cost_rhs_eq,steps,rejected,cap_limited,ms,floor_m,certified,note,energy_drift_from_initial,angular_drift_from_initial,energy_drift_vs_ref,energy_drift_ref,angular_drift_vs_ref,angular_drift_ref,family,stages,kind,expected_order,mode,mode_tags,reference_family,reference_same_family,scale_length,archived,kepler_exact,kepler_weight,kepler_cv,kepler_weight_source,w_unreliable,nck,steps_forced_min,checkpoint_bound,checkpoint_mode,ref_method,ref_family_method,ref_independent,ref_resolution,ref_cross_diff_m,ref_second_method,calls_startup");
            foreach (StudyScenarioResult res in results)
            {
                foreach (StudyRow r in res.Rows)
                {
                    sb.Append(Csv(r.Scenario)).Append(',')
                        .Append(Csv(r.Integrator)).Append(',')
                        .Append(Csv(r.Variant)).Append(',')
                        .Append(Csv(r.Config)).Append(',')
                        .Append(r.Control.ToString("R", Inv)).Append(',')
                        .Append(r.Status).Append(',')
                        .Append(r.Finite ? "1" : "0").Append(',')
                        .Append(r.Err.ToString("R", Inv)).Append(',')
                        .Append(r.ErrFinal.ToString("R", Inv)).Append(',')
                        .Append((r.Err / res.Problem.ScaleLength).ToString("R", Inv)).Append(',')
                        .Append(r.Calls.ToString(Inv)).Append(',')
                        .Append(r.Aux.ToString(Inv)).Append(',')
                        .Append(r.Cost.ToString("R", Inv)).Append(',')
                        .Append(r.Steps.ToString(Inv)).Append(',')
                        .Append(r.Rejected.ToString(Inv)).Append(',')
                        .Append(r.CapLimited.ToString(Inv)).Append(',')
                        .Append(r.Ms.ToString("R", Inv)).Append(',')
                        .Append(res.Problem.Floor.ToString("R", Inv)).Append(',')
                        .Append(res.Problem.Certified ? "1" : "0").Append(',')
                        .Append(Csv(r.Note)).Append(',')
                        .Append(r.EnergyDriftFromInitial.ToString("R", Inv)).Append(',')
                        .Append(r.AngularDriftFromInitial.ToString("R", Inv)).Append(',')
                        .Append(r.EnergyDriftVsRef.ToString("R", Inv)).Append(',')
                        .Append(r.EnergyDriftRef.ToString("R", Inv)).Append(',')
                        .Append(r.AngularDriftVsRef.ToString("R", Inv)).Append(',')
                        .Append(r.AngularDriftRef.ToString("R", Inv)).Append(',')
                        .Append(Csv(r.Family)).Append(',')
                        .Append(r.Stages.ToString(Inv)).Append(',')
                        .Append(r.Kind == StudyKind.Fixed ? "Fixed" : "Adaptive").Append(',')
                        .Append(r.ExpectedOrder.ToString("R", Inv)).Append(',')
                        .Append(Csv(res.Problem.Mode)).Append(',')
                        .Append(Csv(TagText(res.Problem.ModeTags))).Append(',')
                        .Append(Csv(res.Problem.ReferenceFamily)).Append(',')
                        .Append(string.Equals(r.Family, res.Problem.ReferenceFamily, StringComparison.Ordinal) ? "1" : "0").Append(',')
                        .Append(res.Problem.ScaleLength.ToString("R", Inv)).Append(',')
                        .Append(res.Problem.Archived ? "1" : "0").Append(',')
                        .Append(res.Problem.KeplerExact ? "1" : "0").Append(',')
                        .Append(res.KeplerWeight.ToString("R", Inv)).Append(',')
                        .Append(res.KeplerCv.ToString("R", Inv)).Append(',')
                        .Append(Csv(res.KeplerSource)).Append(',')
                        .Append(double.IsNaN(res.KeplerCv) || res.KeplerCv > 0.10d ? "1" : "0").Append(',')
                        .Append(r.Nck.ToString(Inv)).Append(',')
                        .Append(r.StepsForcedMin.ToString(Inv)).Append(',')
                        .Append(r.CheckpointBound ? "1" : "0").Append(',')
                        .Append(r.CheckpointMode).Append(',')
                        .Append(Csv(res.Problem.ReferenceMethod)).Append(',')
                        .Append(Csv(res.Problem.ReferenceFamily)).Append(',')
                        .Append(res.Problem.ReferenceIndependent ? "1" : "0").Append(',')
                        .Append(Csv(res.Problem.ReferenceResolution)).Append(',')
                        .Append(res.Problem.ReferenceCrossDiffM.ToString("R", Inv)).Append(',')
                        .Append(Csv(res.Problem.ReferenceSecondMethod)).Append(',')
                        .Append(r.StartupCalls.ToString(Inv)).AppendLine();
                }
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>
        /// One row per scenario x method-variant x target, with both checkpoint modes.
        /// This is the machine-readable table the audit uses; neither mode is primary.
        /// </summary>
        public static void WriteCellsCsv(
            string path, List<StudyScenarioResult> all, List<StudyScenarioResult> endOnly, StudyOptions o)
        {
            var endByScenario = new Dictionary<string, StudyScenarioResult>(StringComparer.Ordinal);
            if (endOnly != null)
            {
                foreach (StudyScenarioResult r in endOnly)
                {
                    endByScenario[r.Problem.Label] = r;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("scenario,integrator,variant,key,target_m,status_all,cost_all_ckpt,err_all_ckpt,"
                + "status_end,cost_end_only,err_end_only,floor_m,nck_all,nck_end,interpolated_all,"
                + "cost_hit_all,err_hit_all,cost_miss_all,err_miss_all,bracket_ratio_all,bracket_wide_all,bracket_quantized_all,"
                + "cost_hit_end,err_hit_end,cost_miss_end,err_miss_end,bracket_ratio_end,bracket_wide_end,bracket_quantized_end,"
                + "note_all,note_end");
            foreach (StudyScenarioResult res in all)
            {
                StudyScenarioResult end;
                endByScenario.TryGetValue(res.Problem.Label, out end);
                foreach (string key in Keys(res.Rows))
                {
                    int cut = key.IndexOf(" [", StringComparison.Ordinal);
                    string integrator = cut >= 0 ? key.Substring(0, cut) : key;
                    string variant = cut >= 0 ? key.Substring(cut + 2, key.Length - cut - 3) : "";
                    foreach (StudyTarget target in o.Targets(res.Problem))
                    {
                        StudyCell ca = CellFor(res, key, target, o);
                        StudyCell ce = end != null ? CellFor(end, key, target, o) : new StudyCell();
                        if (end == null)
                        {
                            ce.Status = "NA";
                            ce.Note = "end-only run missing";
                        }

                        sb.Append(Csv(res.Problem.Label)).Append(',')
                            .Append(Csv(integrator)).Append(',')
                            .Append(Csv(variant)).Append(',')
                            .Append(Csv(key)).Append(',')
                            .Append(target.Value.ToString("R", Inv)).Append(',')
                            .Append(ca.Status).Append(',')
                            .Append(ca.Cost.ToString("R", Inv)).Append(',')
                            .Append(ca.Err.ToString("R", Inv)).Append(',')
                            .Append(ce.Status).Append(',')
                            .Append(ce.Cost.ToString("R", Inv)).Append(',')
                            .Append(ce.Err.ToString("R", Inv)).Append(',')
                            .Append(res.Problem.Floor.ToString("R", Inv)).Append(',')
                            .Append(res.Problem.CheckpointTimes.Length.ToString(Inv)).Append(',')
                            .Append((end != null ? end.Problem.CheckpointTimes.Length : 0).ToString(Inv)).Append(',')
                            .Append(ca.Interpolated ? "1" : "0").Append(',')
                            .Append(ca.CostHit.ToString("R", Inv)).Append(',')
                            .Append(ca.ErrHit.ToString("R", Inv)).Append(',')
                            .Append(ca.CostMiss.ToString("R", Inv)).Append(',')
                            .Append(ca.ErrMiss.ToString("R", Inv)).Append(',')
                            .Append(ca.BracketRatio.ToString("R", Inv)).Append(',')
                            .Append(ca.BracketWide ? "1" : "0").Append(',')
                            .Append(ca.BracketQuantized ? "1" : "0").Append(',')
                            .Append(ce.CostHit.ToString("R", Inv)).Append(',')
                            .Append(ce.ErrHit.ToString("R", Inv)).Append(',')
                            .Append(ce.CostMiss.ToString("R", Inv)).Append(',')
                            .Append(ce.ErrMiss.ToString("R", Inv)).Append(',')
                            .Append(ce.BracketRatio.ToString("R", Inv)).Append(',')
                            .Append(ce.BracketWide ? "1" : "0").Append(',')
                            .Append(ce.BracketQuantized ? "1" : "0").Append(',')
                            .Append(Csv(ca.Note)).Append(',')
                            .Append(Csv(ce.Note)).AppendLine();
                    }
                }
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static string TagText(string[] tags)
        {
            return tags == null || tags.Length == 0 ? "" : string.Join("|", tags);
        }

        private static string Csv(string s)
        {            if (s == null)
            {
                return "";
            }

            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
            {
                return s;
            }

            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        public static void WriteMarkdown(string path, List<StudyScenarioResult> results, StudyOptions o)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Integrator comparison");
            sb.AppendLine();
            sb.AppendLine("Cost = price of the cheapest EXECUTED run whose error is at or below the target, in RHS-equivalent calls of that scenario. No interpolation: `cost_hit` is a real run. The bracketing miss and `bracket_ratio` are diagnostic only; `bracket_wide=1` means the bracket is wider than 1.10 (or no miss exists). `bracket_quantized=1` means the all-checkpoint fixed-step grid has no intermediate step count. `ms` is wall-clock and noisy; use RHS for deterministic numbers.");
            sb.AppendLine("`EDGE` = even the cheapest tested point was already below the target (cost is an upper bound). A cap-limited EDGE is exact; a non-cap EDGE is marked **unresolved**. `~` marks methods within " + ((o.TieFactor - 1d) * 100d).ToString("F0", Inv) + "% of the winner: that ranking is not resolved. `not reached` = the finest run (or the RHS budget) stayed above the target. `below round-off floor` = the target is at or below the run's own accumulation noise. `n/a` = method not valid for this problem. Scenarios whose reference is not certified are listed in the detail but excluded from the summary. Costs of different scenarios are not comparable: each is in that scenario's own RHS unit.");
            sb.AppendLine("Reference-floor rule: a target below " + o.FloorMargin.ToString("G", Inv) + " x the scenario's reference floor is skipped.");
            sb.AppendLine("Build: " + (StudyCalibration.IsDebugBuild ? "**DEBUG - ms are not representative**" : "Release") + ". Per-run RHS budget: per scenario (see section headers).");
            void WriteWinnerSummary(string title, bool byMs)
            {
                sb.AppendLine();
                sb.AppendLine("## " + title);
                sb.AppendLine();
                if (results.Count == 0)
                {
                    return;
                }

                List<string> summary = o.SummaryLabels();
                sb.Append("| scenario |");
                foreach (string label in summary)
                {
                    sb.Append(' ').Append(label.Replace("|", "/")).Append(" |");
                }

                sb.AppendLine();
                sb.Append("|---|");
                foreach (string label in summary)
                {
                    sb.Append("---|");
                }

                sb.AppendLine();
                foreach (StudyScenarioResult res in results)
                {
                    if (!res.Problem.Certified)
                    {
                        continue;
                    }

                    sb.Append("| ").Append(res.Problem.Label).Append(" |");
                    foreach (StudyTarget t in o.Targets(res.Problem))
                    {
                        bool unresolved;
                        string win = Winner(res, t, o, out unresolved, byMs);
                        sb.Append(' ').Append(win).Append(unresolved ? " **unresolved**" : "").Append(" |");
                    }

                    sb.AppendLine();
                }

                var uncertified = new List<string>();
                foreach (StudyScenarioResult res in results)
                {
                    if (!res.Problem.Certified)
                    {
                        uncertified.Add(res.Problem.Label);
                    }
                }

                if (uncertified.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Not ranked (reference not certified; detail only): "
                        + string.Join(", ", uncertified) + ".");
                }
            }

            WriteWinnerSummary("Who wins where - RHS-equivalent calls", false);
            WriteWinnerSummary("Who wins where - wall-clock ms", true);

            foreach (StudyScenarioResult res in results)
            {
                StudyProblem p = res.Problem;
                sb.AppendLine();
                sb.AppendLine("## " + p.Label);
                sb.AppendLine();
                sb.AppendLine("Reference floor: " + (double.IsNaN(p.Floor) ? "not certified" : p.Floor.ToString("E2", Inv) + " m")
                    + (string.IsNullOrEmpty(p.ReferenceNote) ? "" : " (" + p.ReferenceNote + ")")
                    + ". Kepler-solve weight: " + res.KeplerWeight.ToString("F4", Inv) + " RHS (CV "
                    + (double.IsNaN(res.KeplerCv) ? "n/a" : (res.KeplerCv * 100d).ToString("F1", Inv) + "%")
                    + ", " + res.KeplerSource + "). RHS budget: "
                    + res.MaxCostRhs.ToString("N0", Inv) + ".");
                sb.AppendLine();
                List<StudyTarget> targets = o.Targets(p);
                sb.Append("| integrator |");
                foreach (StudyTarget t in targets)
                {
                    sb.Append(' ').Append(t.Label.Replace("|", "/")).Append(" |");
                }

                sb.AppendLine();
                sb.Append("|---|");
                foreach (StudyTarget t in targets)
                {
                    sb.Append("---|");
                }

                sb.AppendLine();
                foreach (string key in Keys(res.Rows))
                {
                    sb.Append("| ").Append(key).Append(" |");
                    foreach (StudyTarget t in targets)
                    {
                        sb.Append(' ').Append(CellText(CellFor(res, key, t, o))).Append(" |");
                    }

                    sb.AppendLine();
                }

                List<string> notes = Diagnose(res, o);
                if (notes.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Findings:");
                    foreach (string n in notes)
                    {
                        sb.AppendLine("- " + n);
                    }
                }
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
