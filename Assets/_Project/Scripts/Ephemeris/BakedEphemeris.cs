using System;
using System.IO;
using Galilego.Core;

namespace Galilego.Universe
{
    [Serializable]
    public sealed class BakedEphemeris : IDisposable
    {
        /// <summary>Magic of a plain BE1 file (legacy, no continuation).</summary>
        private static readonly byte[] MagicPlain = { (byte)66, (byte)69, (byte)49 };

        /// <summary>
        /// BE2 file magic: [BE2][27-byte header][float64 segments][48-byte tail continuation].
        /// The continuation lives in the tail, outside the segment stream: FileSink
        /// streams gzip segments and OpenFile seeks to the tail without full unpacking.
        /// </summary>
        private static readonly byte[] MagicPlain2 = { (byte)66, (byte)69, (byte)50 };

        /// <summary>Magic of the gzip BG1 container: [BG1][gzip(BE1 data)]. Legacy.</summary>
        private static readonly byte[] MagicGzip = { (byte)66, (byte)71, (byte)49 };

        /// <summary>
        /// Magic of the quantized BG2 container (legacy, no continuation):
        /// [BG2][gzip([27-byte BE1 header][segments: 6B (E_x,E_y,E_z,bits_x,bits_y,bits_z) + zigzag-varint quanta])].
        /// Each segment carries its own E/bits, so window reads need no prior amplitudes.
        /// Position error is guaranteed &lt;= budget/2 (Σ|T_j| &lt;= 13, step &lt;= budget/13).
        /// </summary>
        private static readonly byte[] MagicGz2 = { (byte)66, (byte)71, (byte)50 };

        /// <summary>
        /// BG3 magic: like BG2 plus a Keplerian continuation block in the file tail
        /// (outside gzip): [BG3][gzip([27-byte header][varint segments])][48-byte continuation].
        /// </summary>
        private static readonly byte[] MagicGz3 = { (byte)66, (byte)71, (byte)51 };

        public double T0Seconds;
        public double SegmentLengthSeconds;
        public int Degree;
        public int SegmentCount;
        public double[] Coeffs;
        public string FilePath;
        public int WindowSegments = 4;

        /// <summary>File in a BG1 container (gzip): window reads decompress sequentially.</summary>
        public bool Compressed;

        /// <summary>File in BG2 format (quantized varint segments inside gzip).</summary>
        public bool Quantized;

        /// <summary>The payload has a continuation block (BE2/BG3): 75-byte header instead of 27.</summary>
        public bool HasContinuationBlock;

        private double[] window;
        private int windowBase = -1;
        private FileStream gzFile;
        private System.IO.Compression.GZipStream gzStream;
        private long gzPos;
        private byte[] skipBuf;
        private V2Reader v2Reader;
        private int v2Segs;
        public long WindowLoads;

        /// <summary>
        /// Buffered reader for the BG2 varint stream on top of gzip (a 4 KB buffer
        /// turns per-byte reads during skimming into chunk reads).
        /// </summary>
        private sealed class V2Reader
        {
            private readonly Stream stream;
            private readonly byte[] buf = new byte[4096];
            private int pos;
            private int len;

            internal V2Reader(Stream s)
            {
                stream = s;
            }

            internal int ReadByte()
            {
                if (pos >= len)
                {
                    len = stream.Read(buf, 0, buf.Length);
                    pos = 0;
                    if (len <= 0)
                    {
                        return -1;
                    }
                }

                return buf[pos++];
            }

            internal bool ReadVar(out long value)
            {
                ulong z = 0UL;
                int shift = 0;
                while (true)
                {
                    int b = ReadByte();
                    if (b < 0)
                    {
                        value = 0L;
                        return false;
                    }

                    z |= ((ulong)(b & 0x7F)) << shift;
                    if ((b & 0x80) == 0)
                    {
                        break;
                    }

                    shift += 7;
                    if (shift > 63)
                    {
                        value = 0L;
                        return false;
                    }
                }

                // Zigzag decode: v = (z>>1) XOR −(z&1) — XOR with all ones for
                // odd values, NOT arithmetic negation.
                value = (long)(z >> 1);
                if ((z & 1UL) != 0UL)
                {
                    value = ~value;
                }

                return true;
            }

            /// <summary>Decodes one BG2 segment into dest starting at destOff.</summary>
            internal bool DecodeSegment(int stride, double[] dest, int destOff)
            {
                int perAxis = stride / 3;
                int[] e = new int[3];
                int[] bits = new int[3];
                double[] mult = new double[3];
                for (int ax = 0; ax < 3; ax++)
                {
                    int eb = ReadByte();
                    int bb = ReadByte();
                    if (eb < 0 || bb < 0)
                    {
                        return false;
                    }

                    e[ax] = eb;
                    bits[ax] = bb;
                    mult[ax] = Math.Pow(2d, eb - bb);
                }

                for (int i = 0; i < stride; i++)
                {
                    if (!ReadVar(out long q))
                    {
                        return false;
                    }

                    dest[destOff + i] = q * mult[i / perAxis];
                }

                return true;
            }

            /// <summary>Skips one segment (skimming forward without decoding coefficients).</summary>
            internal bool SkipSegment(int stride)
            {
                for (int i = 0; i < 6; i++)
                {
                    if (ReadByte() < 0)
                    {
                        return false;
                    }
                }

                for (int i = 0; i < stride; i++)
                {
                    if (!ReadVar(out _))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// Quantization K from the error budget: quantum step 2^(E−bits) = 2^−K &lt;= budget/13
        /// guarantees a position error &lt;= budget/2 (worst case Σ|T_j(x)| &lt;= 13).
        /// </summary>
        internal static int KFromBudget(double budget)
        {
            double k = Math.Ceiling(Math.Log(13d / budget, 2d));
            if (k < 1d)
            {
                k = 1d;
            }

            return (int)k;
        }

        /// <summary>
        /// Encodes one segment in BG2 format: 6 bytes (E and bits per axis) +
        /// zigzag-varint quantized coefficients. Returns the number of bytes
        /// written. Throws loudly when the amplitude is outside the format range.
        /// </summary>
        internal static int EncodeSegmentV2(double[] coeffs, int stride, double budget, byte[] dest)
        {
            int perAxis = stride / 3;
            int k = KFromBudget(budget);
            int p = 0;
            for (int ax = 0; ax < 3; ax++)
            {
                double max = 0d;
                int baseOff = ax * perAxis;
                for (int j = 0; j < perAxis; j++)
                {
                    double v = Math.Abs(coeffs[baseOff + j]);
                    if (v > max)
                    {
                        max = v;
                    }
                }

                int e = max > 0d ? (int)Math.Ceiling(Math.Log(max, 2d)) : 0;
                if (e < 0)
                {
                    e = 0;
                }

                if (e > 63 || e + k > 45)
                {
                    throw new InvalidOperationException(
                        "Амплитуда рельсы " + max.ToString("E2") + " (E=" + e + ") вне диапазона BG2 при бюджете " +
                        budget.ToString("F1") + " м — уменьшите масштаб системы или увеличьте бюджет квантования.");
                }

                dest[p++] = (byte)e;
                dest[p++] = (byte)(e + k);
            }

            for (int ax = 0; ax < 3; ax++)
            {
                double mult = Math.Pow(2d, dest[(ax * 2) + 1] - dest[ax * 2]);
                int baseOff = ax * perAxis;
                for (int j = 0; j < perAxis; j++)
                {
                    long q = (long)Math.Round(coeffs[baseOff + j] * mult);
                    ulong z = (ulong)((q << 1) ^ (q >> 63));
                    while (z >= 0x80UL)
                    {
                        dest[p++] = (byte)(z | 0x80UL);
                        z >>= 7;
                    }

                    dest[p++] = (byte)z;
                }
            }

            return p;
        }

        public static int AxisStride(int degree)
        {
            return 3 * (degree + 1);
        }

        public double EndSeconds
        {
            get { return T0Seconds + SegmentCount * SegmentLengthSeconds; }
        }

        /// <summary>
        /// Keplerian continuation past the end of the rail: [a, e, inc°, Ω°, ω°, M0°]
        /// relative to the parent, captured from the rail at EndSeconds (the continuation epoch).
        /// null = no continuation (legacy BE1/BG1/BG2 formats or invalid fitting).
        /// </summary>
        public double[] Continuation;

        /// <summary>Size of the continuation block in the file (6 doubles).</summary>
        internal const int ContinuationBytes = 48;

        /// <summary>Why TryEvaluate failed — for diagnostics instead of a false "out of range".</summary>
        public enum FailReason
        {
            /// <summary>TryEvaluate should have succeeded — no reason for failure.</summary>
            None,
            /// <summary>Time is outside the baked span.</summary>
            OutOfRange,
            /// <summary>No data (Coeffs/structure not populated).</summary>
            NoData,
            /// <summary>Structure is corrupt (Degree/segments out of range).</summary>
            Corrupt
        }

        /// <summary>
        /// Reason TryEvaluate returned (or would return) false for timeSeconds.
        /// Cheap (no window reads).
        /// </summary>
        public FailReason GetFailReason(double timeSeconds)
        {
            if (Degree < 1 || SegmentCount <= 0 || SegmentLengthSeconds <= 0d)
            {
                return FailReason.Corrupt;
            }

            if (FilePath == null && Coeffs == null)
            {
                return FailReason.NoData;
            }

            double dt = timeSeconds - T0Seconds;
            double boundaryTol = 1e-6d + Math.Abs(EndSeconds) * 1e-12d;
            if (dt < -boundaryTol || dt > SegmentCount * SegmentLengthSeconds + boundaryTol)
            {
                return FailReason.OutOfRange;
            }

            return FailReason.None;
        }

        /// <summary>
        /// Keplerian continuation for timeSeconds BEYOND the end of the rail (only the
        /// past-end fallback calls it). Elements are captured at EndSeconds, so the seam
        /// is position-continuous by construction. mu is the same μ_local as the Keplerian
        /// rail (parent μ + body subtree μ); the formula matches EvaluateLocalOffset exactly.
        /// </summary>
        public bool TryEvaluateContinuation(double timeSeconds, double mu, out Vector3d position, out Vector3d velocity)
        {
            position = Vector3d.Zero;
            velocity = Vector3d.Zero;
            if (Continuation == null || Continuation.Length != 6)
            {
                return false;
            }

            double boundaryTol = 1e-6d + Math.Abs(EndSeconds) * 1e-12d;
            if (timeSeconds <= EndSeconds + boundaryTol)
            {
                // Inside the rail TryEvaluate must have answered; only the
                // past-end fallback lands here.
                return false;
            }

            double a = Continuation[0];
            double e = Continuation[1];
            if (!(a > 0d) || !(e >= 0d) || !(e < 1d))
            {
                // Invalid fitting (hyperbola/escape/garbage) — fail loudly.
                return false;
            }

            double inc = KeplerMath.DegreesToRadians(Continuation[2]);
            double raan = KeplerMath.DegreesToRadians(Continuation[3]);
            double argp = KeplerMath.DegreesToRadians(Continuation[4]);
            double m0 = KeplerMath.DegreesToRadians(Continuation[5]);
            double meanMotion = Math.Sqrt(mu / (a * a * a));
            double meanAnomaly = KeplerMath.NormalizeAngle(m0 + (meanMotion * (timeSeconds - EndSeconds)));
            double eccentricAnomaly = KeplerMath.SolveEccentricAnomaly(meanAnomaly, e);

            double cosE = Math.Cos(eccentricAnomaly);
            double sinE = Math.Sin(eccentricAnomaly);
            double radius = a * (1d - (e * cosE));
            double orbitalYScale = Math.Sqrt(1d - (e * e));

            Vector3d orbitalPosition = new Vector3d(
                a * (cosE - e),
                a * orbitalYScale * sinE,
                0d);
            double velocityFactor = Math.Sqrt(mu * a) / radius;
            Vector3d orbitalVelocity = new Vector3d(
                -velocityFactor * sinE,
                velocityFactor * orbitalYScale * cosE,
                0d);

            position = KeplerMath.RotateOrbitalToWorld(orbitalPosition, raan, inc, argp);
            velocity = KeplerMath.RotateOrbitalToWorld(orbitalVelocity, raan, inc, argp);
            return true;
        }

        /// <summary>
        /// Window cursor gate: TryEvaluate mutates shared state (window, gzip cursor,
        /// v2 reader), so the whole call runs under lock (a gzip stream must not be
        /// read by two hands at once). TryEvaluateContinuation is pure math;
        /// LoadRangeToMemory opens its own streams and returns a new instance.
        /// Attach and public-field mutations only happen before the system runs,
        /// from the main thread.
        /// </summary>
        public bool TryEvaluate(double timeSeconds, out Vector3d position, out Vector3d velocity)
        {
            if (FilePath == null)
            {
                // In-memory instance: no shared state, no race — no gate needed
                // (hot path for baking/tests without lock overhead).
                return TryEvaluateCore(timeSeconds, true, out position, out velocity);
            }

            lock (windowGate)
            {
                return TryEvaluateCore(timeSeconds, true, out position, out velocity);
            }
        }

        /// <summary>
        /// Position only (no DerivSum): the gravity RHS needs positions, not velocities, so the
        /// hot path skips roughly half of the polynomial work. Same range rules and locking as TryEvaluate.
        /// </summary>
        public bool TryEvaluatePosition(double timeSeconds, out Vector3d position)
        {
            if (FilePath == null)
            {
                return TryEvaluateCore(timeSeconds, false, out position, out _);
            }

            lock (windowGate)
            {
                return TryEvaluateCore(timeSeconds, false, out position, out _);
            }
        }

        private readonly object windowGate = new object();

        /// <summary>
        /// Segment index with a relative snap: at a boundary k*L the quotient
        /// (t-T0)/L can round to k-epsilon, and a plain floor then returns the
        /// previous segment, which extrapolates the old polynomial across the
        /// whole next step. Snap by a few ulps of the quotient instead.
        /// </summary>
        public int SegmentIndex(double timeSeconds)
        {
            if (SegmentCount <= 0 || !(SegmentLengthSeconds > 0d))
            {
                return 0;
            }

            double x = (timeSeconds - T0Seconds) / SegmentLengthSeconds;
            double snap = 1e-12d * Math.Max(1d, Math.Abs(x));
            int seg = (int)Math.Floor(x + snap);
            if (seg < 0)
            {
                seg = 0;
            }
            else if (seg >= SegmentCount)
            {
                seg = SegmentCount - 1;
            }

            return seg;
        }

        /// <summary>End time of segment <paramref name="seg"/>.</summary>
        public double SegmentEndSeconds(int seg)
        {
            return T0Seconds + ((seg + 1d) * SegmentLengthSeconds);
        }

        private bool TryEvaluateCore(double timeSeconds, bool wantVelocity, out Vector3d position, out Vector3d velocity)
        {
            position = Vector3d.Zero;
            velocity = Vector3d.Zero;
            if (Degree < 1 || SegmentCount <= 0 || SegmentLengthSeconds <= 0d)
            {
                return false;
            }

            double dt = timeSeconds - T0Seconds;
            // Boundary tolerance is RELATIVE: EndSeconds is recomputed with fp rounding
            // and differs from the span by a few ulps on ~3e10 s spans, so a query exactly
            // at the end must not fail; beyond a few ulps it still fails loudly.
            double boundaryTol = 1e-6d + Math.Abs(T0Seconds + SegmentCount * SegmentLengthSeconds) * 1e-12d;
            if (dt < -boundaryTol || dt > SegmentCount * SegmentLengthSeconds + boundaryTol)
            {
                return false;
            }

            if (dt < 0d)
            {
                dt = 0d;
            }

            int seg = SegmentIndex(timeSeconds);

            double[] src;
            int basis;
            if (FilePath == null)
            {
                if (Coeffs == null)
                {
                    return false;
                }

                src = Coeffs;
                basis = seg * AxisStride(Degree);
            }
            else
            {
                if (!EnsureWindow(seg))
                {
                    return false;
                }

                src = window;
                basis = (seg - windowBase) * AxisStride(Degree);
            }

            double x = 2d * ((timeSeconds - (T0Seconds + seg * SegmentLengthSeconds)) / SegmentLengthSeconds) - 1d;
            if (x < -1d)
            {
                x = -1d;
            }
            else if (x > 1d)
            {
                x = 1d;
            }

            int width = Degree + 1;
            position = new Vector3d(
                Clenshaw(src, basis, Degree, x),
                Clenshaw(src, basis + width, Degree, x),
                Clenshaw(src, basis + 2 * width, Degree, x));
            if (!wantVelocity)
            {
                return position.IsFinite;
            }

            double scale = 2d / SegmentLengthSeconds;
            velocity = new Vector3d(
                DerivSum(src, basis, Degree, x) * scale,
                DerivSum(src, basis + width, Degree, x) * scale,
                DerivSum(src, basis + 2 * width, Degree, x) * scale);
            return position.IsFinite && velocity.IsFinite;
        }

        private bool EnsureWindow(int seg)
        {
            int w = WindowSegments;
            if (w < 1)
            {
                w = 1;
            }

            if (window != null && seg >= windowBase && seg < windowBase + w && seg < SegmentCount)
            {
                return true;
            }

            int stride = AxisStride(Degree);
            int count = w;
            if (seg + count > SegmentCount)
            {
                count = SegmentCount - seg;
            }

            if (count <= 0)
            {
                return false;
            }

            try
            {
                if (window == null || window.Length < w * stride)
                {
                    window = new double[w * stride];
                }

                if (Quantized)
                {
                    ReadWindowV2(seg, count, stride);
                }
                else if (Compressed)
                {
                    ReadWindowCompressed(seg, count, stride);
                }
                else
                {
                    ReadWindowPlain(seg, count, stride);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                // Missing/corrupt/inaccessible file is an environment error, not
                // "out of range": silently returning false would pass garbage off
                // as a legitimate TryEvaluate verdict. InvalidDataException means
                // a broken gzip — also environment, also loud, with the file name.
                throw new InvalidOperationException("Не удалось прочитать файл эфемериды: " + FilePath, ex);
            }

            windowBase = seg;
            WindowLoads++;
            return true;
        }

        private void ReadWindowPlain(int seg, int count, int stride)
        {
            // Header = 27 B (including magic) for all formats; the continuation
            // lives in the file tail, outside the segment stream.
            using (FileStream fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fs.Seek(27L + ((long)seg * stride * 8L), SeekOrigin.Begin);
                byte[] buf = new byte[count * stride * 8];
                if (!ReadFully(fs, buf))
                {
                    throw new IOException("Неожиданный конец файла эфемериды.");
                }

                for (int i = 0; i < count * stride; i++)
                {
                    window[i] = ReadFloat64(buf, i * 8);
                }
            }
        }

        /// <summary>
        /// BG1 window read: gzip cannot seek, but time moves forward — read
        /// sequentially and discard the extra decompressed bytes in chunks.
        /// A jump backwards reopens the file. Worst-case skimming costs tens of
        /// ms; forward time is free.
        /// </summary>
        private void ReadWindowCompressed(int seg, int count, int stride)
        {
            long target = 27L + (long)seg * stride * 8L;
            if (gzFile == null || gzPos > target)
            {
                ResetCompressedStream();
                gzFile = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                gzFile.Seek(3, SeekOrigin.Begin); // BG1 magic
                gzStream = new System.IO.Compression.GZipStream(gzFile, System.IO.Compression.CompressionMode.Decompress);
                gzPos = 0;
            }

            if (skipBuf == null)
            {
                skipBuf = new byte[65536];
            }

            while (gzPos < target)
            {
                int chunk = (int)Math.Min(skipBuf.Length, target - gzPos);
                if (!ReadFully(gzStream, skipBuf, chunk))
                {
                    throw new IOException("Неожиданный конец сжатого файла эфемериды.");
                }

                gzPos += chunk;
            }

            byte[] data = new byte[count * stride * 8];
            if (!ReadFully(gzStream, data, data.Length))
            {
                throw new IOException("Неожиданный конец сжатого файла эфемериды.");
            }

            gzPos += data.Length;
            for (int i = 0; i < count * stride; i++)
            {
                window[i] = ReadFloat64(data, i * 8);
            }
        }

        /// <summary>
        /// BG2 window read: the varint segment stream is decoded sequentially;
        /// a jump backwards reopens the file (like BG1). Each segment is
        /// self-contained (E/bits inside), so skimming is a cheap varint read
        /// without decoding coefficients.
        /// </summary>
        private void ReadWindowV2(int seg, int count, int stride)
        {
            if (gzFile == null || v2Segs > seg)
            {
                ResetCompressedStream();
                gzFile = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                gzFile.Seek(3, SeekOrigin.Begin); // BG2 magic
                gzStream = new System.IO.Compression.GZipStream(gzFile, System.IO.Compression.CompressionMode.Decompress);
                gzPos = 0;
                v2Segs = 0;
                v2Reader = new V2Reader(gzStream);
                // Payload header = 27 B for BG2/BG3: the continuation lies in the
                // file tail, outside the gzip stream.
                for (int i = 0; i < 27; i++)
                {
                    if (v2Reader.ReadByte() < 0)
                    {
                        throw new IOException("Неожиданный конец сжатого файла на заголовке.");
                    }
                }
            }

            while (v2Segs < seg)
            {
                if (!v2Reader.SkipSegment(stride))
                {
                    throw new IOException("Неожиданный конец BG2 при скимме к сегменту " + seg + ".");
                }

                v2Segs++;
            }

            for (int s = 0; s < count; s++)
            {
                if (!v2Reader.DecodeSegment(stride, window, s * stride))
                {
                    throw new IOException("Неожиданный конец BG2 в окне у сегмента " + (seg + s) + ".");
                }

                v2Segs++;
            }
        }

        private void ResetCompressedStream()
        {
            if (gzStream != null)
            {
                gzStream.Dispose();
                gzStream = null;
            }

            if (gzFile != null)
            {
                gzFile.Dispose();
                gzFile = null;
            }

            v2Reader = null;
            v2Segs = 0;
            gzPos = 0;
        }

        /// <summary>
        /// Explicitly releases the file descriptor (otherwise the FileStream lives
        /// until the GC finalizer and can block file rewrite/deletion). The gate is
        /// required: ResetCompressedStream mutates the same state as TryEvaluateCore.
        /// After Dispose, TryEvaluate honestly returns false.
        /// </summary>
        public void Dispose()
        {
            lock (windowGate)
            {
                ResetCompressedStream();
            }

            windowBase = -1;
        }

        private static bool ReadFully(Stream stream, byte[] buf)
        {
            return ReadFully(stream, buf, buf.Length);
        }

        private static bool ReadFully(Stream stream, byte[] buf, int length)
        {
            int got = 0;
            while (got < length)
            {
                int r = stream.Read(buf, got, length - got);
                if (r <= 0)
                {
                    return false;
                }

                got += r;
            }

            return true;
        }

        /// <summary>Full BG1 decompression into memory (for LoadRangeToMemory).</summary>
        private byte[] DecompressWhole()
        {
            using (FileStream fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fs.Seek(3, SeekOrigin.Begin);
                using (var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress))
                using (var ms = new MemoryStream())
                {
                    gz.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }

        /// <summary>
        /// Plain BE2 file: [BE2][27-byte header][float64 segments][48-byte continuation].
        /// The continuation block is always present; without a fitting it holds NaN
        /// (readers treat it as "no continuation" — loud past the end).
        /// </summary>
        public void WriteToFile(string path)
        {
            WritePayload(path, MagicPlain2, null);
        }

        /// <summary>
        /// Compressed BG3 file: [BG3][gzip([27-byte header][varint segments])][48-byte continuation].
        /// Quantization with a position-error budget: guarantee &lt;= budget/2 per segment
        /// (see KFromBudget). The continuation sits in the tail, outside gzip, so segment
        /// streaming is preserved and OpenFile can seek to it.
        /// </summary>
        public void WriteToFileCompressed(string path, double quantBudgetMeters = 2d)
        {
            int stride = AxisStride(Degree);
            int kCap = 6 + (stride * 10);
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.WriteByte(MagicGz3[0]);
                fs.WriteByte(MagicGz3[1]);
                fs.WriteByte(MagicGz3[2]);
                using (var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionLevel.Optimal, true))
                {
                    byte[] head = new byte[27];
                    head[0] = MagicPlain[0];
                    head[1] = MagicPlain[1];
                    head[2] = MagicPlain[2];
                    WriteInt32(head, 3, Degree);
                    WriteInt32(head, 7, SegmentCount);
                    WriteFloat64(head, 11, T0Seconds);
                    WriteFloat64(head, 19, SegmentLengthSeconds);
                    gz.Write(head, 0, head.Length);

                    byte[] seg = new byte[kCap];
                    double[] one = new double[stride];
                    for (int s = 0; s < SegmentCount; s++)
                    {
                        Array.Copy(Coeffs, s * stride, one, 0, stride);
                        int n = EncodeSegmentV2(one, stride, quantBudgetMeters, seg);
                        gz.Write(seg, 0, n);
                    }
                }

                // The continuation goes to the tail, after gzip is closed (it must be appended last).
                byte[] tail = new byte[ContinuationBytes];
                for (int i = 0; i < 6; i++)
                {
                    WriteFloat64(tail, i * 8, Continuation != null && Continuation.Length == 6 ? Continuation[i] : double.NaN);
                }

                fs.Write(tail, 0, tail.Length);
            }
        }

        private void WritePayload(string path, byte[] magic, System.IO.Compression.CompressionLevel? compress)
        {
            int stride = AxisStride(Degree);
            byte[] buf = new byte[27 + (SegmentCount * stride * 8) + ContinuationBytes];
            buf[0] = magic[0];
            buf[1] = magic[1];
            buf[2] = magic[2];
            WriteInt32(buf, 3, Degree);
            WriteInt32(buf, 7, SegmentCount);
            WriteFloat64(buf, 11, T0Seconds);
            WriteFloat64(buf, 19, SegmentLengthSeconds);
            for (int i = 0; i < Coeffs.Length; i++)
            {
                WriteFloat64(buf, 27 + (i * 8), Coeffs[i]);
            }

            for (int i = 0; i < 6; i++)
            {
                WriteFloat64(buf, 27 + (Coeffs.Length * 8) + (i * 8), Continuation != null && Continuation.Length == 6 ? Continuation[i] : double.NaN);
            }

            if (compress.HasValue)
            {
                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var gz = new System.IO.Compression.GZipStream(fs, compress.Value))
                {
                    gz.Write(buf, 0, buf.Length);
                }
            }
            else
            {
                File.WriteAllBytes(path, buf);
            }
        }

        public static BakedEphemeris OpenFile(string path)
        {
            FileInfo info = new FileInfo(path);
            if (!info.Exists || info.Length < 3L)
            {
                return null;
            }

            byte[] magic = new byte[3];
            using (FileStream fs0 = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (!ReadFully(fs0, magic))
                {
                    return null;
                }
            }

            bool quantized = magic[0] == MagicGz2[0] && magic[1] == MagicGz2[1] && magic[2] == MagicGz2[2];
            bool quantized3 = magic[0] == MagicGz3[0] && magic[1] == MagicGz3[1] && magic[2] == MagicGz3[2];
            bool plain2 = magic[0] == MagicPlain2[0] && magic[1] == MagicPlain2[1] && magic[2] == MagicPlain2[2];
            bool compressed = quantized || quantized3 || (magic[0] == MagicGzip[0] && magic[1] == MagicGzip[1] && magic[2] == MagicGzip[2]);
            bool hasContinuation = quantized3 || plain2;
            if (!compressed && !plain2 && (magic[0] != MagicPlain[0] || magic[1] != MagicPlain[1] || magic[2] != MagicPlain[2]))
            {
                return null;
            }

            // BE2/BG3: the continuation is 48 B in the file tail, outside compression:
            // OpenFile reads it by seek, without unpacking the segments.
            int payloadHeader = 27;
            byte[] head = new byte[payloadHeader];
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (compressed)
                {
                    fs.Seek(3, SeekOrigin.Begin);
                    using (var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress))
                    {
                        if (!ReadFully(gz, head))
                        {
                            return null;
                        }
                    }
                }
                else
                {
                    if (info.Length < payloadHeader || !ReadFully(fs, head))
                    {
                        return null;
                    }
                }
            }

            int degree = ReadInt32(head, 3);
            int segCount = ReadInt32(head, 7);
            if (degree < 1 || degree > 32 || segCount <= 0)
            {
                return null;
            }

            if (!compressed && info.Length != (long)payloadHeader + ((long)segCount * AxisStride(degree) * 8L) + (hasContinuation ? ContinuationBytes : 0L))
            {
                return null;
            }

            double[] continuation = null;
            if (hasContinuation)
            {
                if (info.Length < ContinuationBytes)
                {
                    return null;
                }

                continuation = new double[6];
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    fs.Seek(-ContinuationBytes, SeekOrigin.End);
                    byte[] tail = new byte[ContinuationBytes];
                    if (!ReadFully(fs, tail))
                    {
                        return null;
                    }

                    for (int i = 0; i < 6; i++)
                    {
                        continuation[i] = ReadFloat64(tail, i * 8);
                    }
                }

                if (double.IsNaN(continuation[0]))
                {
                    continuation = null; // fitting failed — loud fallback past the end
                }
            }

            BakedEphemeris e = new BakedEphemeris
            {
                T0Seconds = ReadFloat64(head, 11),
                SegmentLengthSeconds = ReadFloat64(head, 19),
                Degree = degree,
                SegmentCount = segCount,
                FilePath = path,
                Compressed = compressed,
                Quantized = quantized || quantized3,
                HasContinuationBlock = hasContinuation,
                Continuation = continuation
            };
            return e;
        }

        /// <summary>
        /// Loads a range into memory (planning windows). Hard cap on the number of
        /// segments: an oversized call throws loudly instead of silently eating RAM.
        /// </summary>
        public BakedEphemeris LoadRangeToMemory(double fromSeconds, double toSeconds, int maxSegments = 1000000)
        {
            if (fromSeconds < T0Seconds)
            {
                fromSeconds = T0Seconds;
            }

            if (toSeconds > EndSeconds)
            {
                toSeconds = EndSeconds;
            }

            int segA = (int)((fromSeconds - T0Seconds) / SegmentLengthSeconds);
            int segB = (int)((toSeconds - T0Seconds) / SegmentLengthSeconds);
            if (segA < 0)
            {
                segA = 0;
            }

            if (segB >= SegmentCount)
            {
                segB = SegmentCount - 1;
            }

            if (segB < segA)
            {
                return null;
            }

            if (segB - segA + 1 > maxSegments)
            {
                throw new ArgumentOutOfRangeException(nameof(toSeconds),
                    "Диапазон [" + fromSeconds + "; " + toSeconds + "] = " + (segB - segA + 1) +
                    " сегментов превышает кап " + maxSegments + " — используйте оконное чтение по FilePath.");
            }

            int stride = AxisStride(Degree);
            double[] slice = new double[(segB - segA + 1) * stride];
            if (FilePath == null)
            {
                Array.Copy(Coeffs, segA * stride, slice, 0, slice.Length);
            }
            else if (Quantized)
            {
                byte[] whole = DecompressWhole();
                using (var ms = new MemoryStream(whole))
                {
                    var rd = new V2Reader(ms);
                    for (int i = 0; i < 27; i++)
                    {
                        if (rd.ReadByte() < 0)
                        {
                            throw new IOException("Неожиданный конец сжатого файла на заголовке.");
                        }
                    }

                    for (int i = 0; i < segA; i++)
                    {
                        if (!rd.SkipSegment(stride))
                        {
                            throw new IOException("Неожиданный конец сжатого файла при пропуске до сегмента " + segA + ".");
                        }
                    }

                    for (int s = 0; s <= segB - segA; s++)
                    {
                        if (!rd.DecodeSegment(stride, slice, s * stride))
                        {
                            throw new IOException("Неожиданный конец сжатого файла при декодировании сегмента " + (segA + s) + ".");
                        }
                    }
                }
            }
            else if (Compressed)
            {
                byte[] whole = DecompressWhole();
                for (int i = 0; i < slice.Length; i++)
                {
                    slice[i] = ReadFloat64(whole, (int)(27L + ((long)segA * stride + i) * 8));
                }
            }
            else
            {
                // Same fail-loudly contract as EnsureWindow: IOException and
                // UnauthorizedAccess are environment errors, not "out of range";
                // silently returning null would produce a garbage verdict.
                try
                {
                    using (FileStream fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        fs.Seek(27L + ((long)segA * stride * 8L), SeekOrigin.Begin);
                        byte[] buf = new byte[slice.Length * 8];
                        if (!ReadFully(fs, buf))
                        {
                            throw new IOException("Неожиданный конец файла эфемериды.");
                        }

                        for (int i = 0; i < slice.Length; i++)
                        {
                            slice[i] = ReadFloat64(buf, i * 8);
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    throw new InvalidOperationException("Не удалось прочитать файл эфемериды: " + FilePath, ex);
                }
            }

            BakedEphemeris e = new BakedEphemeris();
            e.T0Seconds = T0Seconds + segA * SegmentLengthSeconds;
            e.SegmentLengthSeconds = SegmentLengthSeconds;
            e.Degree = Degree;
            e.SegmentCount = segB - segA + 1;
            e.Coeffs = slice;
            // The slice does not share the continuation array with the parent:
            // the fields are publicly mutable, so mutating one must not affect the other.
            e.Continuation = Continuation != null ? (double[])Continuation.Clone() : null;
            return e;
        }

        public static double Clenshaw(double[] c, int offset, int degree, double x)
        {
            double b2 = 0d;
            double b1 = 0d;
            for (int k = degree; k >= 1; k--)
            {
                double b0 = c[offset + k] + 2d * x * b1 - b2;
                b2 = b1;
                b1 = b0;
            }

            return c[offset] + x * b1 - b2;
        }

        public static double DerivSum(double[] c, int offset, int degree, double x)
        {
            if (degree < 1)
            {
                return 0d;
            }

            double t0 = 1d;
            double t1 = x;
            double d0 = 0d;
            double d1 = 1d;
            double s = c[offset + 1];
            for (int k = 1; k < degree; k++)
            {
                double t2 = 2d * x * t1 - t0;
                double d2 = 2d * t1 + 2d * x * d1 - d0;
                s += c[offset + k + 1] * d2;
                t0 = t1;
                t1 = t2;
                d0 = d1;
                d1 = d2;
            }

            return s;
        }

        public string ToPortableString()
        {
            int stride = AxisStride(Degree);
            byte[] buf = new byte[3 + 4 + 4 + 8 + 8 + Coeffs.Length * 8];
            buf[0] = (byte)66;
            buf[1] = (byte)69;
            buf[2] = (byte)49;
            WriteInt32(buf, 3, Degree);
            WriteInt32(buf, 7, SegmentCount);
            WriteFloat64(buf, 11, T0Seconds);
            WriteFloat64(buf, 19, SegmentLengthSeconds);
            for (int i = 0; i < Coeffs.Length; i++)
            {
                WriteFloat64(buf, 27 + i * 8, Coeffs[i]);
            }

            return Convert.ToBase64String(buf);
        }

        public static BakedEphemeris FromPortableString(string s)
        {
            byte[] buf = Convert.FromBase64String(s);
            if (buf.Length < 27 || buf[0] != 66 || buf[1] != 69 || buf[2] != 49)
            {
                return null;
            }

            int degree = ReadInt32(buf, 3);
            int segCount = ReadInt32(buf, 7);
            if (degree < 1 || degree > 32 || segCount <= 0)
            {
                return null;
            }

            int stride = AxisStride(degree);
            if (buf.Length != 27 + segCount * stride * 8)
            {
                return null;
            }

            BakedEphemeris e = new BakedEphemeris();
            e.T0Seconds = ReadFloat64(buf, 11);
            e.SegmentLengthSeconds = ReadFloat64(buf, 19);
            e.Degree = degree;
            e.SegmentCount = segCount;
            e.Coeffs = new double[segCount * stride];
            for (int i = 0; i < e.Coeffs.Length; i++)
            {
                e.Coeffs[i] = ReadFloat64(buf, 27 + i * 8);
            }

            return e;
        }

        internal static void WriteInt32(byte[] buf, int off, int v)
        {
            buf[off] = (byte)(v & 255);
            buf[off + 1] = (byte)((v >> 8) & 255);
            buf[off + 2] = (byte)((v >> 16) & 255);
            buf[off + 3] = (byte)((v >> 24) & 255);
        }

        private static int ReadInt32(byte[] buf, int off)
        {
            return buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24);
        }

        internal static void WriteFloat64(byte[] buf, int off, double v)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            for (int i = 0; i < 8; i++)
            {
                buf[off + i] = (byte)((bits >> (8 * i)) & 255L);
            }
        }

        private static double ReadFloat64(byte[] buf, int off)
        {
            long bits = 0L;
            for (int i = 0; i < 8; i++)
            {
                bits |= ((long)buf[off + i]) << (8 * i);
            }

            return BitConverter.Int64BitsToDouble(bits);
        }
    }
}
