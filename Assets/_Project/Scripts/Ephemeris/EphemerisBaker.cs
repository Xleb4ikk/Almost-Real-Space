using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Ephemeris bake configuration. Defaults: segment = smallest subtree period / 4
    /// (clamped to [2; 64] days), degree 12, DOP853 substep from the phase-error budget
    /// of the fastest orbit.
    /// </summary>
    public sealed class BakeConfig
    {
        /// <summary>Chebyshev interpolant degree per segment (degree+1 nodes).</summary>
        public int Degree = 12;

        /// <summary>
        /// Phase-error budget per DOP853 substep: n·h &lt;= PhasePerStep, where n is the mean
        /// motion of the system's fastest orbit. Local DOP853 error ~ (n·h)⁵/120; smaller
        /// value — more accurate and slower.
        /// </summary>
        public double PhasePerStep = 0.02d;

        /// <summary>Absolute DOP853 substep cap (s); the former stepSeconds parameter.</summary>
        public double MaxStepSeconds = 900d;

        /// <summary>
        /// BG2 quantization position-error budget (m) per segment: the quantum step
        /// is chosen so the worst case Σ|T_j|·(step/2) &lt;= budget/2. Smaller — more
        /// accurate and a heavier file.
        /// </summary>
        public double QuantBudgetMeters = 2d;

        /// <summary>Segment = smallest subtree period / SegmentPeriodDivisor.</summary>
        public double SegmentPeriodDivisor = 4d;

        public double MinSegmentSeconds = 2d * 86400d;

        public double MaxSegmentSeconds = 64d * 86400d;

        /// <summary>Validates the ranges; throws loudly on garbage.</summary>
        public void Validate()
        {
            if (Degree < 1 || Degree > 32)
            {
                throw new ArgumentOutOfRangeException(nameof(Degree), Degree, "Степень обязана быть в [1, 32].");
            }

            if (PhasePerStep <= 0d || PhasePerStep > 0.2d)
            {
                throw new ArgumentOutOfRangeException(nameof(PhasePerStep), PhasePerStep, "n·h обязан быть в (0, 0.2].");
            }

            if (MaxStepSeconds <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxStepSeconds), MaxStepSeconds, "Кап подшага обязан быть положительным.");
            }

            if (QuantBudgetMeters <= 0d || QuantBudgetMeters > 1000d)
            {
                throw new ArgumentOutOfRangeException(nameof(QuantBudgetMeters), QuantBudgetMeters, "Бюджет квантования обязан быть в (0, 1000] м.");
            }

            if (SegmentPeriodDivisor <= 0d || MinSegmentSeconds <= 0d || MaxSegmentSeconds < MinSegmentSeconds)
            {
                throw new ArgumentOutOfRangeException(nameof(SegmentPeriodDivisor), "Неконсистентное правило длины сегмента.");
            }
        }
    }

    public static class EphemerisBaker
    {
        public static void ExportFiles(StarSystem sys, string directory)
        {
            if (sys == null)
            {
                throw new ArgumentNullException("sys");
            }

            Directory.CreateDirectory(directory);
            StringBuilder manifest = new StringBuilder();
            var used = new HashSet<string>();
            foreach (OrbitingBody body in sys.AllBodies)
            {
                if (body.Baked == null || body.Baked.Coeffs == null)
                {
                    continue;
                }

                string file = Sanitize(body.Name, used) + ".bin";
                body.Baked.WriteToFile(Path.Combine(directory, file));
                manifest.Append(body.Name);
                manifest.Append('\t');
                manifest.Append(file);
                manifest.Append('\n');
            }

            File.WriteAllText(Path.Combine(directory, "manifest.txt"), manifest.ToString());
        }

        /// <summary>
        /// Attaches baked ephemerides from a directory (via manifest.txt). Loud contract:
        /// EVERY non-root body must receive an ephemeris — a body missing from the
        /// manifest or with an unreadable file would silently stay on its Keplerian rail.
        /// </summary>
        public static void AttachFiles(StarSystem sys, string directory)
        {
            if (sys == null)
            {
                throw new ArgumentNullException("sys");
            }

            string[] lines = File.ReadAllLines(Path.Combine(directory, "manifest.txt"));
            var table = new Dictionary<string, string>();
            for (int i = 0; i < lines.Length; i++)
            {
                int tab = lines[i].IndexOf('\t');
                if (tab <= 0)
                {
                    continue;
                }

                string name = lines[i].Substring(0, tab);
                string file = lines[i].Substring(tab + 1).Trim();
                if (!table.ContainsKey(name))
                {
                    table[name] = file;
                }
            }

            var attached = new List<OrbitingBody>();
            var failures = new List<string>();
            foreach (OrbitingBody body in sys.AllBodies)
            {
                if (body.Parent == null)
                {
                    continue; // the star is neither baked nor attached
                }

                if (!table.TryGetValue(body.Name, out string file))
                {
                    failures.Add(body.Name + ": нет записи в манифесте");
                    continue;
                }

                BakedEphemeris e = BakedEphemeris.OpenFile(Path.Combine(directory, file));
                if (e == null)
                {
                    failures.Add(body.Name + ": файл \"" + file + "\" не читается");
                    continue;
                }

                BakedEphemeris previous = body.Baked;
                body.Baked = e;
                if (previous != null && !ReferenceEquals(previous, e) && previous.FilePath != null)
                {
                    previous.Dispose();
                }

                attached.Add(body);
            }

            if (failures.Count > 0)
            {
                // Roll back a partial attach: detach what was already attached
                // (disposing file-backed ones) so the system stays consistently unattached.
                foreach (OrbitingBody body in attached)
                {
                    BakedEphemeris e = body.Baked;
                    body.Baked = null;
                    if (e != null && e.FilePath != null)
                    {
                        e.Dispose();
                    }
                }

                throw new InvalidOperationException(
                    "AttachFiles: " + failures.Count + " тел(а) без эфемериды — " + string.Join("; ", failures) +
                    ". Испечённый набор обязан покрывать все тела системы.");
            }

            sys.InvalidatePositionCache();
        }

        private static string Sanitize(string name, HashSet<string> used)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool bad = char.IsControl(c);
                for (int k = 0; k < invalid.Length && !bad; k++)
                {
                    bad = c == invalid[k];
                }

                sb.Append(bad ? '_' : c);
            }

            string file = sb.ToString().Trim();
            if (file.Length == 0)
            {
                file = "body";
            }

            string candidate = file;
            int suffix = 2;
            while (used.Contains(candidate))
            {
                candidate = file + "_" + suffix;
                suffix++;
            }

            used.Add(candidate);
            return candidate;
        }

        /// <summary>
        /// Body segment length. Rule: the minimum orbital period of the SUBTREE
        /// (own and all descendants) divided by 4, clamped to [2; 64] days.
        /// The subtree matters because a moon wiggles the planet's heliocentric
        /// orbit with the moon's period (planet offset from the barycentre), so the
        /// segment must be shorter than that ripple period.
        /// </summary>
        public static double DefaultSegmentLengthSeconds(OrbitingBody body)
        {
            double best = double.PositiveInfinity;
            AccumulatePeriod(body, ref best);
            for (int i = 0; i < body.Children.Count; i++)
            {
                AccumulateSubtreePeriods(body.Children[i], ref best);
            }

            if (double.IsPositiveInfinity(best))
            {
                return 32d * 86400d;
            }

            double seg = best / 4d;
            if (seg < 2d * 86400d)
            {
                seg = 2d * 86400d;
            }

            if (seg > 64d * 86400d)
            {
                seg = 64d * 86400d;
            }

            return seg;
        }

        private static void AccumulateSubtreePeriods(OrbitingBody body, ref double best)
        {
            AccumulatePeriod(body, ref best);
            for (int i = 0; i < body.Children.Count; i++)
            {
                AccumulateSubtreePeriods(body.Children[i], ref best);
            }
        }

        private static void AccumulatePeriod(OrbitingBody body, ref double best)
        {
            if (body.Parent == null)
            {
                return;
            }

            double a = body.SemiMajorAxis;
            if (a <= 0d)
            {
                return;
            }

            double mu = body.Parent.ResolveStandardGravitationalParameter() + body.ResolveSubtreeStandardGravitationalParameter();
            if (mu <= 0d)
            {
                return;
            }

            double period = 2d * Math.PI * Math.Sqrt(a * a * a / mu);
            if (period > 0d && period < best)
            {
                best = period;
            }
        }

        public static void BakeAndAttach(
            StarSystem sys,
            double spanYears,
            double stepSeconds,
            int degree,
            Func<OrbitingBody, double> segmentLengthFor)
        {
            if (segmentLengthFor == null)
            {
                segmentLengthFor = DefaultSegmentLengthSeconds;
            }

            var baked = Bake(sys, spanYears, stepSeconds, degree, segmentLengthFor);
            foreach (var kv in baked)
            {
                kv.Key.Baked = kv.Value;
            }

            sys.InvalidatePositionCache();
        }

        /// <summary>Legacy signature: step and degree via BakeConfig.</summary>
        public static Dictionary<OrbitingBody, BakedEphemeris> Bake(
            StarSystem sys,
            double spanYears,
            double stepSeconds,
            int degree,
            Func<OrbitingBody, double> segmentLengthFor)
        {
            var config = new BakeConfig { Degree = degree, MaxStepSeconds = stepSeconds };
            return Bake(sys, spanYears, config, segmentLengthFor, null, null);
        }

        /// <summary>
        /// Bake into memory (all coefficients in RAM — for tests and small spans).
        /// For 1024-year spans use BakeToFiles: same engine, but segments are
        /// streamed to file and peak RAM is O(1) per body.
        /// </summary>
        public static Dictionary<OrbitingBody, BakedEphemeris> Bake(
            StarSystem sys,
            double spanYears,
            BakeConfig config,
            Func<OrbitingBody, double> segmentLengthFor = null,
            Action<double> progress = null,
            Func<bool> cancelled = null)
        {
            if (segmentLengthFor == null)
            {
                segmentLengthFor = DefaultSegmentLengthSeconds;
            }

            var memory = new MemorySink();
            RunBake(sys, spanYears, config, segmentLengthFor, memory, progress, cancelled);
            return memory.Result;
        }

        /// <summary>
        /// B1: streaming bake to files (one file per body + manifest).
        /// A segment is written to the FileStream as soon as it is ready — only the
        /// current segment's node buffer is in RAM. Returned BakedEphemeris instances
        /// reference files (FilePath) without Coeffs; RAM is bounded by
        /// WindowSegments regardless of span length.
        /// compress=true — files in BG2 containers: coefficient quantization with the
        /// config.QuantBudgetMeters error budget (position guarantee &lt;= budget/2),
        /// then zigzag-varint + gzip. Typical compression 2-4× vs BE1.
        /// </summary>
        public static Dictionary<OrbitingBody, BakedEphemeris> BakeToFiles(
            StarSystem sys,
            string directory,
            double spanYears,
            BakeConfig config,
            Func<OrbitingBody, double> segmentLengthFor = null,
            Action<double> progress = null,
            Func<bool> cancelled = null,
            bool compress = false)
        {
            if (segmentLengthFor == null)
            {
                segmentLengthFor = DefaultSegmentLengthSeconds;
            }

            Directory.CreateDirectory(directory);
            using (var sink = new FileSink(sys, directory, compress, config.QuantBudgetMeters))
            {
                RunBake(sys, spanYears, config, segmentLengthFor, sink, progress, cancelled);
                return sink.Result;
            }
        }

        /// <summary>
        /// B4: estimates bake file sizes without integration (instant).
        /// Key = body, value = its file bytes (header + segments).
        /// Exact for plain BE1 files. For BG2 this is an upper bound:
        /// the actual size is ~2.5-4× smaller (quantization+varint+gzip),
        /// since varint segment length is data-dependent.
        /// </summary>
        public static Dictionary<OrbitingBody, long> EstimateBakeBytes(
            StarSystem sys,
            double spanYears,
            BakeConfig config,
            Func<OrbitingBody, double> segmentLengthFor = null)
        {
            if (segmentLengthFor == null)
            {
                segmentLengthFor = DefaultSegmentLengthSeconds;
            }

            config.Validate();
            double spanSeconds = spanYears * YearSeconds;
            ValidateBakeable(sys, spanSeconds, config, segmentLengthFor);
            var result = new Dictionary<OrbitingBody, long>();
            int stride = BakedEphemeris.AxisStride(config.Degree);
            foreach (OrbitingBody body in sys.AllBodies)
            {
                if (body.Parent == null)
                {
                    continue;
                }

                double spanSecondsLocal = spanSeconds;
                double segLen = AdjustedSegmentLength(spanSecondsLocal, segmentLengthFor(body));
                int segCount = (int)Math.Ceiling(spanSecondsLocal / segLen);
                result[body] = 27L + ContinuationBytesTotal + ((long)segCount * stride * 8L);
            }

            return result;
        }

        private const double YearSeconds = 365d * 86400d;

        /// <summary>BE2/BG3 tail: 6 doubles of the Keplerian continuation.</summary>
        private const long ContinuationBytesTotal = 48L;

        // ════════════════════════════════════════════════════════════════════
        // Core: global n-body DOP853, samples at the Chebyshev-Lobatto nodes of
        // each segment, coefficients by direct transform (DCT-III), JPL DE-style.
        // ════════════════════════════════════════════════════════════════════

        private interface ISegmentSink
        {
            void RegisterLayout(int bodyIndex, OrbitingBody body, double segLen, int segCount, int degree);

            void WriteSegment(int bodyIndex, int segIndex, double[] coeffs);

            /// <summary>Body Keplerian continuation: [a, e, i°, Ω°, ω°, M0°] — angles in DEGREES (as in OrbitalElements), not radians.</summary>
            void WriteContinuation(int bodyIndex, double[] elements);
        }

        private sealed class MemorySink : ISegmentSink
        {
            internal double[][] Coeffs;
            private readonly List<OrbitingBody> bakedBodies = new List<OrbitingBody>();
            private readonly List<int> segCounts = new List<int>();
            private readonly List<double> segLens = new List<double>();
            private readonly List<int> degrees = new List<int>();
            private int stride;

            public void RegisterLayout(int bodyIndex, OrbitingBody body, double segLen, int segCount, int degree)
            {
                stride = BakedEphemeris.AxisStride(degree);
                bakedBodies.Add(body);
                segCounts.Add(segCount);
                segLens.Add(segLen);
                degrees.Add(degree);
                continuations = new double[bakedBodies.Count][];
                Coeffs = new double[bakedBodies.Count][];
                for (int i = 0; i < bakedBodies.Count; i++)
                {
                    Coeffs[i] = new double[segCounts[i] * stride];
                }
            }

            public void WriteSegment(int bodyIndex, int segIndex, double[] coeffs)
            {
                Array.Copy(coeffs, 0, Coeffs[bodyIndex], segIndex * stride, stride);
            }

            public void WriteContinuation(int bodyIndex, double[] elements)
            {
                continuations[bodyIndex] = (double[])elements.Clone();
            }

            private double[][] continuations;

            public Dictionary<OrbitingBody, BakedEphemeris> Result
            {
                get
                {
                    var result = new Dictionary<OrbitingBody, BakedEphemeris>();
                    for (int i = 0; i < bakedBodies.Count; i++)
                    {
                        result[bakedBodies[i]] = new BakedEphemeris
                        {
                            T0Seconds = 0d,
                            SegmentLengthSeconds = segLens[i],
                            Degree = degrees[i],
                            SegmentCount = segCounts[i],
                            Coeffs = Coeffs[i],
                            Continuation = continuations != null ? continuations[i] : null
                        };
                    }

                    return result;
                }
            }
        }

        private sealed class FileSink : ISegmentSink, IDisposable
        {
            private readonly List<OrbitingBody> bakedBodies = new List<OrbitingBody>();
            private readonly List<Stream> streams = new List<Stream>();
            private readonly List<FileStream> files = new List<FileStream>();
            private readonly List<string> paths = new List<string>();
            private readonly Dictionary<OrbitingBody, BakedEphemeris> result = new Dictionary<OrbitingBody, BakedEphemeris>();
            private byte[] writeBuf;
            private readonly bool compress;
            private double quantBudget;
            private int v2Stride;
            private double[][] continuations;

            internal FileSink(StarSystem sys, string directory, bool compress, double quantBudgetMeters)
            {
                this.compress = compress;
                quantBudget = quantBudgetMeters;
                var used = new HashSet<string>();
                var manifest = new StringBuilder();
                foreach (OrbitingBody body in sys.AllBodies)
                {
                    if (body.Parent == null)
                    {
                        continue;
                    }

                    // File name matches ExportFiles (same Sanitize) so AttachFiles
                    // can read the baked files without reconfiguration. Format:
                    // BE2 (plain) / BG3 (gzip+quantized); the 48 B continuation
                    // goes to the tail in Dispose, after gzip is closed.
                    string file = Sanitize(body.Name, used) + ".bin";
                    string path = Path.Combine(directory, file);
                    FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                    Stream stream = fs;
                    if (compress)
                    {
                        // BG3: magic outside gzip, payload = [27-byte header][varint segments].
                        // leaveOpen: gzip Dispose would close the underlying FileStream,
                        // but the continuation tail is appended after that.
                        fs.WriteByte(66);
                        fs.WriteByte(71);
                        fs.WriteByte(51);
                        stream = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionLevel.Optimal, true);
                    }
                    // Plain (BE2): no outer magic — the magic sits inside the 27-byte
                    // header (RegisterLayout writes "BE2"), as in WritePayload.

                    files.Add(fs);
                    streams.Add(stream);
                    paths.Add(path);
                    bakedBodies.Add(body);
                    manifest.Append(body.Name);
                    manifest.Append('\t');
                    manifest.Append(file);
                    manifest.Append('\n');
                }

                File.WriteAllText(Path.Combine(directory, "manifest.txt"), manifest.ToString());
            }

            public void RegisterLayout(int bodyIndex, OrbitingBody body, double segLen, int segCount, int degree)
            {
                int stride = BakedEphemeris.AxisStride(degree);
                v2Stride = stride;
                writeBuf = compress ? new byte[6 + (stride * 10)] : new byte[stride * 8];
                continuations = new double[bakedBodies.Count][];
                byte[] head = new byte[27];
                head[0] = (byte)66;
                // Inner header magic: BG3/BG2 carry a "BE1" payload header
                // (legacy BG1 convention), plain BE2 carries "BE2" (no outer magic).
                head[1] = (byte)69;
                head[2] = compress ? (byte)49 : (byte)50;
                BakedEphemeris.WriteInt32(head, 3, degree);
                BakedEphemeris.WriteInt32(head, 7, segCount);
                BakedEphemeris.WriteFloat64(head, 11, 0d);
                BakedEphemeris.WriteFloat64(head, 19, segLen);
                streams[bodyIndex].Write(head, 0, head.Length);

                result[bakedBodies[bodyIndex]] = new BakedEphemeris
                {
                    T0Seconds = 0d,
                    SegmentLengthSeconds = segLen,
                    Degree = degree,
                    SegmentCount = segCount,
                    FilePath = paths[bodyIndex],
                    Compressed = compress,
                    Quantized = compress,
                    HasContinuationBlock = true
                };
            }

            public void WriteContinuation(int bodyIndex, double[] elements)
            {
                continuations[bodyIndex] = (double[])elements.Clone();
                // Returned BakedEphemeris instances reference the file: the continuation
                // is already written to the tail, and is needed in memory too (until the file is opened).
                result[bakedBodies[bodyIndex]].Continuation = continuations[bodyIndex];
            }

            public void WriteSegment(int bodyIndex, int segIndex, double[] coeffs)
            {
                if (compress)
                {
                    // BG2: error-budget quantization + zigzag-varint.
                    int n = BakedEphemeris.EncodeSegmentV2(coeffs, v2Stride, quantBudget, writeBuf);
                    streams[bodyIndex].Write(writeBuf, 0, n);
                    return;
                }

                for (int i = 0; i < coeffs.Length; i++)
                {
                    BakedEphemeris.WriteFloat64(writeBuf, i * 8, coeffs[i]);
                }

                streams[bodyIndex].Write(writeBuf, 0, writeBuf.Length);
            }

            public Dictionary<OrbitingBody, BakedEphemeris> Result => result;

            public void Dispose()
            {
                for (int i = 0; i < streams.Count; i++)
                {
                    Stream s = streams[i];
                    if (s is System.IO.Compression.GZipStream)
                    {
                        s.Dispose(); // gzip must close before the tail can be appended
                    }
                }

                // The continuation block goes to the tail of each file (outside compression):
                // OpenFile seeks to it from the end without unpacking the segments.
                for (int i = 0; i < files.Count; i++)
                {
                    byte[] tail = new byte[BakedEphemeris.ContinuationBytes];
                    for (int j = 0; j < 6; j++)
                    {
                        BakedEphemeris.WriteFloat64(tail, j * 8,
                            continuations != null && continuations[i] != null ? continuations[i][j] : double.NaN);
                    }

                    files[i].Write(tail, 0, tail.Length);
                    files[i].Dispose();
                }

                streams.Clear();
                files.Clear();
            }
        }

        private static double AdjustedSegmentLength(double spanSeconds, double requested)
        {
            int segCount = (int)Math.Ceiling(spanSeconds / requested);
            return spanSeconds / segCount;
        }

        /// <summary>
        /// Bakeability validation: the delivered segment must be shorter than the
        /// fastest orbit period in the body's subtree. Otherwise the curve wraps the
        /// orbit many times and the 13-node Chebyshev interpolant aliases into SILENT
        /// garbage. The restriction cascades upward: a moon's ripple sits in the
        /// planet's rail, so ancestors of an unbakeable body are unbakeable too.
        /// Throws loudly with the list of offenders.
        /// </summary>
        private static void ValidateBakeable(
            StarSystem sys,
            double spanSeconds,
            BakeConfig config,
            Func<OrbitingBody, double> segmentLengthFor)
        {
            var bodies = sys.AllBodies;
            int n = bodies.Count;
            var index = new Dictionary<OrbitingBody, int>(n);
            for (int i = 0; i < n; i++)
            {
                index[bodies[i]] = i;
            }

            bool[] unbakeable = new bool[n];
            var names = new List<string>();
            for (int i = n - 1; i >= 0; i--) // children first: cascade upward
            {
                OrbitingBody body = bodies[i];
                if (body.Parent == null)
                {
                    continue;
                }

                double subtreeMin = SubtreeMinPeriodSeconds(body);
                double delivered = AdjustedSegmentLength(spanSeconds, segmentLengthFor(body));
                bool own = !double.IsPositiveInfinity(subtreeMin) && delivered > subtreeMin * (1d + 1e-9d);
                bool childBad = false;
                foreach (OrbitingBody child in body.Children)
                {
                    if (index.TryGetValue(child, out int c) && unbakeable[c])
                    {
                        childBad = true;
                        break;
                    }
                }

                if (own || childBad)
                {
                    unbakeable[i] = true;
                    names.Add(body.Name + (own
                        ? " (период поддерева " + (subtreeMin / 3600.0).ToString("F1") + "ч, сегмент " + (delivered / 3600.0).ToString("F1") + "ч — " + (delivered / subtreeMin).ToString("F1") + " витков орбиты на сегмент)"
                        : " (несёт рябь быстрого потомка)"));
                }
            }

            if (names.Count > 0)
            {
                throw new InvalidOperationException(
                    "Выпечка невозможна: " + names.Count + " тел(а) с более чем одним витком орбиты на сегмент — " +
                    string.Join("; ", names) +
                    ". Решения: раздвинуть быструю луну, уменьшить минимум сегмента в правиле, или оставить тело на кеплеровых рельсах без выпечки.");
            }
        }

        /// <summary>Minimum orbital period of the body and all descendants (s).</summary>
        private static double SubtreeMinPeriodSeconds(OrbitingBody body)
        {
            double best = double.PositiveInfinity;
            AccumulatePeriod(body, ref best);
            for (int i = 0; i < body.Children.Count; i++)
            {
                AccumulateSubtreePeriods(body.Children[i], ref best);
            }

            return best;
        }

        /// <summary>
        /// Bake engine. Bodies are integrated with a global n-body DOP853; the substep is
        /// capped by the phase-error budget of the fastest orbit; samples land exactly
        /// on the Chebyshev-Lobatto nodes of each segment; when the nodes are full, the
        /// DCT produces coefficients and the sink writes them. No allocations in the hot loop.
        /// </summary>
        private static void RunBake(
            StarSystem sys,
            double spanYears,
            BakeConfig config,
            Func<OrbitingBody, double> segmentLengthFor,
            ISegmentSink sink,
            Action<double> progress,
            Func<bool> cancelled)
        {
            if (sys == null)
            {
                throw new ArgumentNullException("sys");
            }

            if (segmentLengthFor == null)
            {
                throw new ArgumentNullException("segmentLengthFor");
            }

            config.Validate();

            double span = spanYears * YearSeconds;
            ValidateBakeable(sys, span, config, segmentLengthFor);

            var bodies = sys.AllBodies;
            int n = bodies.Count;
            double g = PhysicsSolver.GravitationalConstant;
            double[] m = new double[n];
            double[] p = new double[3 * n];
            double[] v = new double[3 * n];
            for (int i = 0; i < n; i++)
            {
                bodies[i].EvaluateWorldState(0d, out Vector3d bp, out Vector3d bv);
                m[i] = bodies[i].ResolveStandardGravitationalParameter() / g;
                p[3 * i] = bp.X;
                p[3 * i + 1] = bp.Y;
                p[3 * i + 2] = bp.Z;
                v[3 * i] = bv.X;
                v[3 * i + 1] = bv.Y;
                v[3 * i + 2] = bv.Z;
            }

            // Hierarchy barycentric correction (leaf-first). The rails give each child's
            // offset relative to its parent (Jacobi: the child's subtree barycentre about
            // the parent). To make the n-body reproduce those SAME relative orbits, the
            // whole subtree (parent + all descendants) is shifted RIGIDLY so that its
            // barycentre lands on the rail position of the parent. Shifting only the
            // parent (as before) stretched every child's relative position and velocity
            // by (1 + m_child/m_parent): wrong periods, wrong start distance.
            // Child offsets are taken from the untouched rail copies, so a child's own
            // earlier (leaf-first) shift does not leak into its parent's balance.
            double[] railP = (double[])p.Clone();
            double[] railV = (double[])v.Clone();
            for (int i = n - 1; i >= 0; i--)
            {
                var b = bodies[i];
                if (b.Children.Count == 0 || m[i] <= 0d)
                {
                    continue;
                }

                double mSub = m[i];
                double sx = 0d;
                double sy = 0d;
                double sz = 0d;
                double svx = 0d;
                double svy = 0d;
                double svz = 0d;
                for (int j = 0; j < b.Children.Count; j++)
                {
                    int c = IndexOf(bodies, n, b.Children[j]);
                    if (c < 0)
                    {
                        continue;
                    }

                    double mc = SubtreeMass(bodies, m, n, c);
                    mSub += mc;
                    sx += mc * (railP[3 * c] - railP[3 * i]);
                    sy += mc * (railP[3 * c + 1] - railP[3 * i + 1]);
                    sz += mc * (railP[3 * c + 2] - railP[3 * i + 2]);
                    svx += mc * (railV[3 * c] - railV[3 * i]);
                    svy += mc * (railV[3 * c + 1] - railV[3 * i + 1]);
                    svz += mc * (railV[3 * c + 2] - railV[3 * i + 2]);
                }

                ShiftSubtree(bodies, n, i, p, v, -sx / mSub, -sy / mSub, -sz / mSub, -svx / mSub, -svy / mSub, -svz / mSub);
            }

            int[] parentIdx = new int[n];
            for (int i = 0; i < n; i++)
            {
                parentIdx[i] = -1;
                if (bodies[i].Parent != null)
                {
                    parentIdx[i] = IndexOf(bodies, n, bodies[i].Parent);
                }
            }

            // Substep cap from the fastest period (n·h &lt;= PhasePerStep), not a hard
            // constant: systems without fast moons bake proportionally faster.
            double dtMax = config.MaxStepSeconds;
            for (int i = 0; i < n; i++)
            {
                if (parentIdx[i] < 0)
                {
                    continue;
                }

                double a = bodies[i].SemiMajorAxis;
                if (a <= 0d)
                {
                    continue;
                }

                double muLocal = bodies[parentIdx[i]].ResolveStandardGravitationalParameter()
                    + bodies[i].ResolveSubtreeStandardGravitationalParameter();
                if (muLocal <= 0d)
                {
                    continue;
                }

                double period = 2d * Math.PI * Math.Sqrt(a * a * a / muLocal);
                if (period > 0d)
                {
                    double phaseStep = period * config.PhasePerStep / (2d * Math.PI);
                    if (phaseStep < dtMax)
                    {
                        dtMax = phaseStep;
                    }
                }
            }

            // Segments/nodes per body.
            int bakedCount = 0;
            int[] bodySlot = new int[n]; // index into the baker's arrays (root excluded)
            var bakedBodies = new List<OrbitingBody>();
            var segLen = new List<double>();
            var segCount = new List<int>();
            var nodeOffsets = new List<double[]>(); // L·(1−cos(kπ/N))/2, k=0..N
            var sampleBuf = new List<double[]>();   // 3·(N+1)
            var fill = new List<int>();
            var curSeg = new List<int>();
            var nextNode = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (parentIdx[i] < 0)
                {
                    continue;
                }

                double requested = segmentLengthFor(bodies[i]);
                double len = AdjustedSegmentLength(span, requested);
                int count = (int)Math.Ceiling(span / len);
                int degree = config.Degree;
                var offs = new double[degree + 1];
                for (int k = 0; k <= degree; k++)
                {
                    offs[k] = len * (1d - Math.Cos(k * Math.PI / degree)) * 0.5d;
                }

                bodySlot[i] = bakedCount;
                bakedBodies.Add(bodies[i]);
                segLen.Add(len);
                segCount.Add(count);
                nodeOffsets.Add(offs);
                sampleBuf.Add(new double[3 * (degree + 1)]);
                fill.Add(0);
                curSeg.Add(0);
                nextNode.Add(0);
                bakedCount++;
            }

            for (int s = 0; s < bakedCount; s++)
            {
                sink.RegisterLayout(s, bakedBodies[s], segLen[s], segCount[s], config.Degree);
            }

            double maxEnd = 0d;
            for (int s = 0; s < bakedCount; s++)
            {
                double end = segCount[s] * segLen[s];
                if (end > maxEnd)
                {
                    maxEnd = end;
                }
            }

            // DCT cosine table (built once per degree).
            int degreeN = config.Degree;
            var cosTable = new double[degreeN + 1, degreeN + 1];
            for (int j = 0; j <= degreeN; j++)
            {
                for (int k = 0; k <= degreeN; k++)
                {
                    cosTable[j, k] = Math.Cos(j * k * Math.PI / degreeN);
                }
            }

        var stageP = new double[12][];
        var stageV = new double[12][];
        for (int s = 0; s < 12; s++)
        {
            stageP[s] = new double[3 * n];
            stageV[s] = new double[3 * n];
        }
        var tp = new double[3 * n];
        var tv = new double[3 * n];
        var coeffs = new double[BakedEphemeris.AxisStride(degreeN)];

        // Parallel Accel pays off only for large n: scheduler overhead exceeds the
        // work for small systems, hence the threshold of 64 (small systems stay
        // strictly sequential, with the same summation order). Buffers — one allocation.
        double[][] accelPartials = null;
        if (n >= ParallelAccelBodies)
        {
            int workers = Math.Min(Environment.ProcessorCount, n);
            accelPartials = new double[workers][];
            for (int w = 0; w < workers; w++)
            {
                accelPartials[w] = new double[3 * n];
            }
        }

            double t = 0d;
            double nextProgress = 0d;
            while (t < maxEnd - 1e-9d)
            {
                if (cancelled != null && cancelled())
                {
                    throw new OperationCanceledException("Выпечка эфемерид отменена.");
                }

                // Nearest node across all bodies.
                double nextT = double.PositiveInfinity;
                for (int i = 0; i < n; i++)
                {
                    if (parentIdx[i] < 0)
                    {
                        continue;
                    }

                    int slot = bodySlot[i];
                    if (curSeg[slot] < segCount[slot])
                    {
                        double nodeT = curSeg[slot] * segLen[slot] + nodeOffsets[slot][nextNode[slot]];
                        if (nodeT < nextT)
                        {
                            nextT = nodeT;
                        }
                    }
                }

                // Termination is ALL BODIES DONE, not "t reached maxEnd": the last
                // node can sit a couple of ulps below maxEnd−1e-9 due to rounding,
                // and the inner loop would then march forever with nextT=+Inf.
                if (nextT == double.PositiveInfinity)
                {
                    break;
                }

                // Substeps up to the node: the last one lands exactly on nextT (no drift).
                // Integrator — DOP853: its secular energy drift at h=900 is negligible
                // relative to the required accuracy over the baked span.
                while (t < nextT - 1e-9d)
                {
                    double h = Math.Min(dtMax, nextT - t);
                    Array.Copy(v, stageP[0], 3 * n);
                    Accel(p, stageV[0], m, g, n, accelPartials);
                    for (int s = 1; s < 12; s++)
                    {
                        Array.Copy(p, tp, 3 * n);
                        Array.Copy(v, tv, 3 * n);
                        double[] row = DOP853Coefficients.A[s];
                        for (int j = 0; j < s; j++)
                        {
                            double a = row[j];
                            if (a == 0d)
                            {
                                continue;
                            }
                            double ha = h * a;
                            double[] kp = stageP[j];
                            double[] kv = stageV[j];
                            for (int i = 0; i < 3 * n; i++)
                            {
                                tp[i] += ha * kp[i];
                                tv[i] += ha * kv[i];
                            }
                        }
                        Accel(tp, stageV[s], m, g, n, accelPartials);
                        Array.Copy(tv, stageP[s], 3 * n);
                    }
                    for (int i = 0; i < 3 * n; i++)
                    {
                        double dp = 0d;
                        double dv = 0d;
                        for (int s = 0; s < 12; s++)
                        {
                            dp += DOP853Coefficients.B[s] * stageP[s][i];
                            dv += DOP853Coefficients.B[s] * stageV[s][i];
                        }
                        p[i] += h * dp;
                        v[i] += h * dv;
                    }
                    t += h;
                }

                t = nextT; // exact landing: samples are taken at the nodes, not "almost at" them

                // Sample all bodies whose node is due.
                for (int i = 0; i < n; i++)
                {
                    if (parentIdx[i] < 0)
                    {
                        continue;
                    }

                    int slot = bodySlot[i];
                    if (curSeg[slot] >= segCount[slot])
                    {
                        continue;
                    }

                    double nodeT = curSeg[slot] * segLen[slot] + nodeOffsets[slot][nextNode[slot]];
                    if (Math.Abs(nodeT - t) > 1e-6d + 1e-12d * t)
                    {
                        continue;
                    }

                    double[] buf = sampleBuf[slot];
                    int o = 3 * nextNode[slot];
                    buf[o] = p[3 * i] - p[3 * parentIdx[i]];
                    buf[o + 1] = p[3 * i + 1] - p[3 * parentIdx[i] + 1];
                    buf[o + 2] = p[3 * i + 2] - p[3 * parentIdx[i] + 2];
                    nextNode[slot]++;

                    if (nextNode[slot] > degreeN)
                    {
                        FitSegmentChebyshev(buf, degreeN, cosTable, coeffs);
                        sink.WriteSegment(slot, curSeg[slot], coeffs);
                        curSeg[slot]++;
                        nextNode[slot] = 0;
                    }
                }

                if (progress != null && t >= nextProgress)
                {
                    progress(Math.Min(1d, t / maxEnd));
                    nextProgress = t + maxEnd * 0.01d;
                }
            }

            // Keplerian continuation: final integration states (each body reached its
            // EndSeconds within fp) are captured as elements relative to the parent —
            // the same frame as the rails. Past the rail's end the body flies this
            // Keplerian orbit; the seam is seamless by construction.
            var finalRel = new double[6];
            for (int i = 0; i < n; i++)
            {
                int parent = parentIdx[i];
                if (parent < 0)
                {
                    continue;
                }

                double muLocal = g * (m[parent] + SubtreeMass(bodies, m, n, i));
                finalRel[0] = p[3 * i] - p[3 * parent];
                finalRel[1] = p[(3 * i) + 1] - p[(3 * parent) + 1];
                finalRel[2] = p[(3 * i) + 2] - p[(3 * parent) + 2];
                finalRel[3] = v[3 * i] - v[3 * parent];
                finalRel[4] = v[(3 * i) + 1] - v[(3 * parent) + 1];
                finalRel[5] = v[(3 * i) + 2] - v[(3 * parent) + 2];
                OrbitalElements el = OrbitalElements.FromState(
                    new Vector3d(finalRel[0], finalRel[1], finalRel[2]),
                    new Vector3d(finalRel[3], finalRel[4], finalRel[5]),
                    muLocal);
                var cont = new double[6];
                cont[0] = el.SemiMajorAxis;
                cont[1] = el.Eccentricity;
                cont[2] = el.InclinationDegrees;
                cont[3] = el.LongitudeOfAscendingNodeDegrees;
                cont[4] = el.ArgumentOfPeriapsisDegrees;
                cont[5] = el.MeanAnomalyDegrees;
                sink.WriteContinuation(bodySlot[i], cont);
            }
        }

        /// <summary>
        /// Direct Chebyshev transform of Lobatto nodes into coefficients.
        /// Nodes were sampled as t_k = T0 + L·(1−cos(kπ/N))/2, i.e. x_k =
        /// −cos(kπ/N) — the REVERSE of the standard order, which yields the factor
        /// (−1)^j: a_j = (−1)^j·(2/(N·c_j))·Σ_k (1/c_k)·y_k·cos(jkπ/N), c_0 = c_N = 2.
        /// The reader derives velocity as dP/dx·(2/L).
        /// </summary>
        private static void FitSegmentChebyshev(double[] samples, int degree, double[,] cosTable, double[] dest)
        {
            int stride = 3 * (degree + 1);
            for (int axis = 0; axis < 3; axis++)
            {
                for (int j = 0; j <= degree; j++)
                {
                    double cj = (j == 0 || j == degree) ? 2d : 1d;
                    double sum = 0d;
                    for (int k = 0; k <= degree; k++)
                    {
                        double ck = (k == 0 || k == degree) ? 2d : 1d;
                        sum += samples[3 * k + axis] * cosTable[j, k] / ck;
                    }

                    double a = 2d * sum / (degree * cj);
                    if ((j & 1) != 0)
                    {
                        a = -a;
                    }

                    dest[axis * (degree + 1) + j] = a;
                }
            }
        }

        private static int IndexOf(System.Collections.Generic.IReadOnlyList<OrbitingBody> bodies, int n, OrbitingBody b)
        {
            for (int k = 0; k < n; k++)
            {
                if (ReferenceEquals(bodies[k], b))
                {
                    return k;
                }
            }

            return -1;
        }

        /// <summary>Rigid shift of body idx and ALL its descendants (positions and velocities): relative vectors inside the subtree stay untouched.</summary>
        private static void ShiftSubtree(
            System.Collections.Generic.IReadOnlyList<OrbitingBody> bodies, int n, int idx, double[] p, double[] v,
            double dx, double dy, double dz, double dvx, double dvy, double dvz)
        {
            p[3 * idx] += dx;
            p[(3 * idx) + 1] += dy;
            p[(3 * idx) + 2] += dz;
            v[3 * idx] += dvx;
            v[(3 * idx) + 1] += dvy;
            v[(3 * idx) + 2] += dvz;
            var b = bodies[idx];
            for (int j = 0; j < b.Children.Count; j++)
            {
                int c = IndexOf(bodies, n, b.Children[j]);
                if (c >= 0)
                {
                    ShiftSubtree(bodies, n, c, p, v, dx, dy, dz, dvx, dvy, dvz);
                }
            }
        }

        private static double SubtreeMass(System.Collections.Generic.IReadOnlyList<OrbitingBody> bodies, double[] m, int n, int idx)
        {
            double total = m[idx];
            var b = bodies[idx];
            for (int j = 0; j < b.Children.Count; j++)
            {
                int c = IndexOf(bodies, n, b.Children[j]);
                if (c >= 0)
                {
                    total += SubtreeMass(bodies, m, n, c);
                }
            }

            return total;
        }

        /// <summary>
        /// Body-count threshold for parallel Accel: below it the sequential path runs
        /// (scheduler overhead exceeds the work for small systems; summation order is kept).
        /// </summary>
        private const int ParallelAccelBodies = 64;

        private static void Accel(double[] p, double[] a, double[] m, double g, int n, double[][] partials = null)
        {
            if (partials != null)
            {
                AccelParallel(p, a, m, g, n, partials);
                return;
            }

            for (int i = 0; i < 3 * n; i++)
            {
                a[i] = 0d;
            }

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double dx = p[3 * j] - p[3 * i];
                    double dy = p[3 * j + 1] - p[3 * i + 1];
                    double dz = p[3 * j + 2] - p[3 * i + 2];
                    double r2 = (dx * dx) + (dy * dy) + (dz * dz);
                    double r = Math.Sqrt(r2);
                    double s = g / (r2 * r);
                    double fi = s * m[j];
                    double fj = s * m[i];
                    a[3 * i] += fi * dx;
                    a[3 * i + 1] += fi * dy;
                    a[3 * i + 2] += fi * dz;
                    a[3 * j] -= fj * dx;
                    a[3 * j + 1] -= fj * dy;
                    a[3 * j + 2] -= fj * dz;
                }
            }
        }

        /// <summary>
        /// Parallel full rows: each worker computes its bodies i against ALL j≠i
        /// (pairs are computed twice — the cost of a vectorizable decomposition)
        /// into its own lock-free buffer; merge after the barrier. Workers are
        /// strided (i = w, w+W, …); buffers are preallocated, zero allocations per step.
        /// </summary>
        private static void AccelParallel(double[] p, double[] a, double[] m, double g, int n, double[][] partials)
        {
            int workers = partials.Length;
            System.Threading.Tasks.Parallel.For(0, workers, w =>
            {
                double[] buf = partials[w];
                for (int i = w; i < n; i += workers)
                {
                    double pxi = p[3 * i];
                    double pyi = p[3 * i + 1];
                    double pzi = p[3 * i + 2];
                    double ax = 0d;
                    double ay = 0d;
                    double az = 0d;
                    for (int j = 0; j < n; j++)
                    {
                        if (j == i)
                        {
                            continue;
                        }

                        double dx = p[3 * j] - pxi;
                        double dy = p[3 * j + 1] - pyi;
                        double dz = p[3 * j + 2] - pzi;
                        double r2 = (dx * dx) + (dy * dy) + (dz * dz);
                        double r = Math.Sqrt(r2);
                        double f = (g / (r2 * r)) * m[j];
                        ax += f * dx;
                        ay += f * dy;
                        az += f * dz;
                    }

                    buf[3 * i] = ax;
                    buf[3 * i + 1] = ay;
                    buf[3 * i + 2] = az;
                }
            });

            for (int w = 0; w < workers; w++)
            {
                double[] buf = partials[w];
                for (int i = w; i < n; i += workers)
                {
                    a[3 * i] += buf[3 * i];
                    a[3 * i + 1] += buf[3 * i + 1];
                    a[3 * i + 2] += buf[3 * i + 2];
                }
            }
        }
    }
}
