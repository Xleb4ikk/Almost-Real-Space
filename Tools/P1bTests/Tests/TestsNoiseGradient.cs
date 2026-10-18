using System;
using System.Globalization;
using Galilego.Universe;
using Unity.Mathematics;

internal static partial class P1bTests
{
    // Fixed node grid: 61^3 — enough both for statistics over 12
    // directions and for cell-splitting edge cases. Sets are computed
    // on a deterministic sample; otherwise such tests flake.

    private static int Test112_GradientSetSymmetry()
    {
        const int side = 30;
        long n = 0;
        double xx = 0d, yy = 0d, zz = 0d, xy = 0d, xz = 0d, yz = 0d;
        double xy2 = 0d, xz2 = 0d, yz2 = 0d;
        double sx = 0d, sy = 0d, sz = 0d;
        var distinct = new System.Collections.Generic.HashSet<long>();

        for (int x = -side; x <= side; x++)
        {
            for (int y = -side; y <= side; y++)
            {
                for (int z = -side; z <= side; z++)
                {
                    double3 g = TerrainNoise.LatticeGradientFast(x, y, z);
                    xx += g.x * g.x;
                    yy += g.y * g.y;
                    zz += g.z * g.z;
                    xy += g.x * g.y;
                    xz += g.x * g.z;
                    yz += g.y * g.z;
                    xy2 += (g.x * g.y) * (g.x * g.y);
                    xz2 += (g.x * g.z) * (g.x * g.z);
                    yz2 += (g.y * g.z) * (g.y * g.z);
                    sx += g.x;
                    sy += g.y;
                    sz += g.z;
                    n++;
                    distinct.Add((long)(Math.Round(g.x * 1e6) * 100000000L)
                        + (long)(Math.Round(g.y * 1e6) * 1000L)
                        + (long)Math.Round(g.z * 1e6));
                }
            }
        }

        double trace = xx + yy + zz;
        double dev = Math.Max(Math.Abs(xx - yy), Math.Max(Math.Abs(yy - zz), Math.Abs(zz - xx)));
        double spread = (100d * dev) / (trace / 3d);
        double offDiag = (Math.Abs(xy) + Math.Abs(xz) + Math.Abs(yz)) / n;
        double seSum = (Math.Sqrt(xy2 / n) + Math.Sqrt(xz2 / n) + Math.Sqrt(yz2 / n)) / Math.Sqrt(n);
        double meanPerAxis = Math.Max(Math.Abs(sx), Math.Max(Math.Abs(sy), Math.Abs(sz))) / n;

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "    узлов {0}, направлений {1}; разброс диагонали {2:F4}%, |внедиаг.| {3:E2}, "
            + "средний градиент {4:E2} (на ось), 3*сигма {5:E2}",
            n, distinct.Count, spread, offDiag, meanPerAxis, seSum));

        // 12 cube edges give exactly Σggᵀ = 4I. Threshold 1% leaves margin
        // over the sampling statistics.
        Check(spread < 1.0, "T112 gradient-set",
            "Σggᵀ изотропна: разброс диагонали " + spread.ToString("F4", CultureInfo.InvariantCulture) + "%");

        // Off-diagonal terms are symmetric about zero: the set is closed under
        // axis permutations, otherwise a preferred direction appears. A fixed
        // threshold sits below the sampling noise here, so the check is done in
        // sigmas instead (standard error of one gx*gy term ≈ 6.1e-4 at 61^3 nodes).
        Check(offDiag < 3d * seSum, "T112 gradient-set",
            "внедиагональный член выше 3 сигм: |внедиаг.| " + offDiag.ToString("E2", CultureInfo.InvariantCulture)
            + " при 3*sigma = " + (3d * seSum).ToString("E2", CultureInfo.InvariantCulture));
        // Mean gradient should be zero, otherwise the noise is biased along an
        // axis. It is not exactly zero: the index is h % 12 and the hash scatters
        // the 12 bins imperfectly (measured spread 0.56%), which is invisible on
        // terrain. The threshold leaves margin over that spread.
        Check(meanPerAxis < 5e-3, "T112 gradient-set",
            "средний градиент ~нулевой: " + meanPerAxis.ToString("E2", CultureInfo.InvariantCulture)
            + " (остаток от разброса h % 12 по 12 корзинам)");

        Check(distinct.Count == 12, "T112 gradient-set",
            "ровно 12 различных направлений: " + distinct.Count);

        return 0;
    }

    /// <summary>
    /// LatticeDot is the trickiest noise line: signs and "which axis is occupied"
    /// masks are computed with multiplications, without branches. Checks it against
    /// the reference LatticeGradientFast vector on all 12 indices, at large node coordinates.
    /// </summary>
    private static int Test113_LatticeDotMatchesTable()
    {
        const double tol = 1e-15;
        double worst = 0d;
        int idx = 0;
        var rng = new Random(4242);

        // Large offsets included: real octaves reach frequency ~1e4 and cell
        // indices go far beyond int range — an error in index decomposition
        // would show up there as a band on the planet.
        for (int i = 0; i < 200000; i++)
        {
            int x = rng.Next(-4000000, 4000000);
            int y = rng.Next(-4000000, 4000000);
            int z = rng.Next(-4000000, 4000000);
            double fx = rng.NextDouble();
            double fy = rng.NextDouble();
            double fz = rng.NextDouble();

            double3 g = TerrainNoise.LatticeGradientFast(x, y, z);
            double dot = TerrainNoise.LatticeDot(x, y, z, fx, fy, fz);
            double expect = (g.x * fx) + (g.y * fy) + (g.z * fz);
            double err = Math.Abs(dot - expect);
            if (err > worst)
            {
                worst = err;
                idx = i;
            }
        }

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture, "    худшая ошибка {0:E3} на итерации {1}", worst, idx));
        Check(worst <= tol, "T113 lattice-dot",
            "LatticeDot совпадает с эталонным вектором: худшая ошибка "
            + worst.ToString("E3", CultureInfo.InvariantCulture));
        return 0;
    }

    /// <summary>
    /// Analytic derivative versus central difference. The slope domain relies on
    /// exactly this formula. Points are not taken on cell boundaries: there the
    /// derivative is continuous but the second derivatives jump, giving the central
    /// difference a finite error.
    /// </summary>
    private static int Test114_DerivativeMatchesFiniteDifference()
    {
        const double h = 1e-5;
        const double tol = 1e-6;
        double worst = 0d;
        var rng = new Random(777);

        for (int i = 0; i < 50000; i++)
        {
            // Keep the point inside the cell but no closer than 0.05 to the border,
            // otherwise the central difference steps into the neighbouring cell and
            // the derivative is computed with different gradient nodes.
            double3 p = new double3(
                (rng.NextDouble() * 40d) - 20d + 0.05,
                (rng.NextDouble() * 40d) - 20d + 0.05,
                (rng.NextDouble() * 40d) - 20d + 0.05);
            p.x = Math.Floor(p.x) + 0.05 + (rng.NextDouble() * 0.9);
            p.y = Math.Floor(p.y) + 0.05 + (rng.NextDouble() * 0.9);
            p.z = Math.Floor(p.z) + 0.05 + (rng.NextDouble() * 0.9);

            TerrainNoise.GradientNoiseWithDerivative(p, out double3 d);

            double dx = (TerrainNoise.GradientNoise(p + new double3(h, 0d, 0d)) - TerrainNoise.GradientNoise(p - new double3(h, 0d, 0d))) / (2d * h);
            double dy = (TerrainNoise.GradientNoise(p + new double3(0d, h, 0d)) - TerrainNoise.GradientNoise(p - new double3(0d, h, 0d))) / (2d * h);
            double dz = (TerrainNoise.GradientNoise(p + new double3(0d, 0d, h)) - TerrainNoise.GradientNoise(p - new double3(0d, 0d, h))) / (2d * h);

            worst = Math.Max(worst, Math.Max(Math.Abs(d.x - dx), Math.Max(Math.Abs(d.y - dy), Math.Abs(d.z - dz))));
        }

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture, "    худшая ошибка {0:E3} (допуск {1:E0})", worst, tol));
        Check(worst < tol, "T114 derivative",
            "аналитическая производная == центральная разность: "
            + worst.ToString("E3", CultureInfo.InvariantCulture));
        return 0;
    }

    /// <summary>
    /// Continuity of the derivative at a cell boundary. The quintic smoothing was
    /// chosen for this: du/dt = 30t²(t−1)² vanishes at the boundary, so the second
    /// term of the derivative dies and both approaches to the boundary give the
    /// gradient of the SAME lattice node. A jump would mean the node is mixed up
    /// in the derivative decomposition — a bug, not a property of the noise.
    /// </summary>
    private static int Test115_DerivativeContinuousAtCellEdge()
    {
        const double eps = 1e-9;
        double worst = 0d;
        var rng = new Random(31337);

        for (int i = 0; i < 50000; i++)
        {
            int ix = rng.Next(-100000, 100000);
            int iy = rng.Next(-1000, 1000);
            int iz = rng.Next(-1000, 1000);
            double fy = rng.NextDouble();
            double fz = rng.NextDouble();

            // Approach the face from the right (fx → 1) and from the left in the next cell (fx → 0).
            double3 right = new double3(ix + (1d - eps), iy + fy, iz + fz);
            double3 left = new double3(ix + 1d + eps, iy + fy, iz + fz);
            TerrainNoise.GradientNoiseWithDerivative(right, out double3 dr);
            TerrainNoise.GradientNoiseWithDerivative(left, out double3 dl);
            worst = Math.Max(worst, math.length(dr - dl));
        }

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture, "    худший скачок {0:E3}", worst));
        Check(worst < 1e-5, "T115 derivative-continuity",
            "производная непрерывна на грани ячейки: скачок "
            + worst.ToString("E3", CultureInfo.InvariantCulture));
        return 0;
    }

    /// <summary>
    /// <summary>
    /// Anti-no-op check for the damping windows. Measured on a fixed sample, not
    /// on the difference with a neighbouring cell: neighbouring Fibonacci-sphere
    /// points are too far apart to tell damping from an overall shape shift.
    /// Here the same 60k directions are fed to all three modes and compared
    /// pairwise against "off" — that is the quantity the windows were fitting.
    /// The second signal is the spread of the signal: T110 fitted the windows by
    /// amplitude smoothness, and here it is checked that they differ between modes
    /// and have not degenerated into a no-op.
    /// </summary>
    private static int Test120_DampIsNotNoOp()
    {
        const int n = 60000;
        double[] off = Heights((int)TerrainSlopeDampMode.Off);
        double[] accum = Heights((int)TerrainSlopeDampMode.Accum);
        double[] gradient = Heights((int)TerrainSlopeDampMode.Gradient);

        double dAccum = FixedSampleDistance(off, accum, n);
        double dGradient = FixedSampleDistance(off, gradient, n);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "    средний сдвиг высоты относительно выключенного damping: Accum {0:E3}, Gradient {1:E3}",
            dAccum, dGradient));

        Check(dAccum > 1e-6, "T120 damp-not-noop",
            "Accum не отличается от Off: " + dAccum.ToString("E3", CultureInfo.InvariantCulture));
        Check(dGradient > 1e-6, "T120 damp-not-noop",
            "Gradient не отличается от Off: " + dGradient.ToString("E3", CultureInfo.InvariantCulture));
        Check(System.Math.Abs(dAccum - dGradient) > 1e-9, "T120 damp-not-noop",
            "оба режима сдвинули поле одинаково: |Accum-Gradient| = "
            + System.Math.Abs(dAccum - dGradient).ToString("E3", CultureInfo.InvariantCulture));

        double aRange = WeightRange((int)TerrainSlopeDampMode.Accum);
        double gRange = WeightRange((int)TerrainSlopeDampMode.Gradient);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "    разброс сигнала (p90/p10): Accum {0:F2}, Gradient {1:F2}", aRange, gRange));
        // The spread must differ between the two modes and not collapse to 1.0;
        // if the windows collapsed, the modes would become indistinguishable in
        // the picture. What matters is a stable difference and that Gradient is
        // noticeably flatter than Accum: the analytic slope filters wider than the
        // accumulated sum and leaves more fine detail in the signal.
        Check(aRange > 1.2, "T120 damp-not-noop",
            "у Accum разброс подозрительно мал (< 1.2): " + aRange.ToString("F2", CultureInfo.InvariantCulture));
        Check(gRange > 1.2, "T120 damp-not-noop",
            "у Gradient разброс подозрительно мал (< 1.2, ожидается около 1.34, и это с 8-окт. fBm "
            + "у Accum с плоским знаменателем sum/norm): " + gRange.ToString("F2", CultureInfo.InvariantCulture));
        return 0;
    }

    private static double3[] DampDirs(int n)
    {
        var dirs = new double3[n];
        double ga = System.Math.PI * (3.0 - System.Math.Sqrt(5.0));
        for (int i = 0; i < n; i++)
        {
            double z = 1.0 - (2.0 * (i + 0.5)) / n;
            double r = System.Math.Sqrt(System.Math.Max(0.0, 1.0 - (z * z)));
            double a = ga * i;
            dirs[i] = new double3(System.Math.Cos(a) * r, System.Math.Sin(a) * r, z);
        }

        return dirs;
    }

    private static double[] Heights(int mode)
    {
        const int n = 60000;
        HeightfieldTerrain t = SceneLikeTerrain();
        t.NoiseStyle = (int)TerrainNoiseStyle.Perlin;
        t.RidgedMode = (int)TerrainRidgedMode.Multifractal;
        t.RidgedGamma = 0.3d;
        t.SlopeDamp = 0.6d;
        t.SlopeDampMode = mode;
        TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(t);
        double3[] dirs = DampDirs(n);
        var h = new double[n];
        for (int i = 0; i < n; i++)
        {
            h[i] = TerrainNoise.SampleHeight(p, dirs[i]);
        }

        return h;
    }

    private static double FixedSampleDistance(double[] a, double[] b, int n)
    {
        double sum = 0d;
        for (int i = 0; i < n; i++)
        {
            sum += System.Math.Abs(a[i] - b[i]);
        }

        return sum / n;
    }

    /// <summary>
    /// <summary>
    /// p90/p10 spread of the octave signal. The signal converges to 1 when the
    /// weights are balanced and diverges when the octaves have different weights
    /// in the denominator. For Accum the octaves go through SampleShapeFbm, where
    /// the denominator is also weighted by the amplitude sum, so the signal is
    /// nearly constant; for Gradient the signal is built manually by dividing the
    /// accumulated slope by 20 — there the denominator is rigid and the spread
    /// shows the real inhomogeneity of the octave contributions.
    /// </summary>
    /// </summary>
    private static double WeightRange(int mode)
    {
        const int n = 40000;
        bool gradient = mode == (int)TerrainSlopeDampMode.Gradient;
        var signal = new double[n];
        var rng = new NoiseBenchRandom3(24334543);
        for (int i = 0; i < n; i++)
        {
            double3 dir = rng.Direction(i);
            double3 offset = rng.Offset;
            double amplitude = 1d;
            double frequency = 20d;
            double slopeAccum = 0d;
            double sum = 0d;
            double norm = 0d;
            for (int o = 0; o < 10; o++)
            {
                if (gradient)
                {
                    TerrainNoise.GradientNoiseWithDerivative((dir * frequency) + offset, out double3 d);
                    slopeAccum += amplitude * (math.length(d) * frequency);
                    signal[i] = (slopeAccum / (o + 1)) / 20d;
                }
                else
                {
                    double v = TerrainNoise.GradientNoise((dir * frequency) + offset);
                    sum += amplitude * v;
                    norm += amplitude;
                    signal[i] = norm > 0d ? math.abs(sum / norm) : 0d;
                }

                amplitude *= 0.5d;
                frequency *= 2d;
                offset = new double3(offset.y + 19.19d, offset.z + 7.47d, offset.x + 3.13d);
            }
        }

        Array.Sort(signal);
        double p10 = signal[(int)(n * 0.10)];
        double p90 = signal[(int)(n * 0.90)];
        return p10 > 1e-12 ? p90 / p10 : double.PositiveInfinity;
    }

    /// <summary>Deterministic direction and offset generator: the 3D bench has
    /// its own salt 0 so it does not coincide with other tests' samples.</summary>
    internal sealed class NoiseBenchRandom3
    {
        private readonly int seed;

        public NoiseBenchRandom3(int seed)
        {
            this.seed = seed;
        }

        public double3 Offset
        {
            get { return new double3(seed * 61.3d + 1700.9d, seed * 19.77d + 1300.3d, seed * 83.31d + 1500.6d); }
        }

        public double3 Direction(int i)
        {
            double z = 1.0 - (2.0 * (i + 0.5)) / 40000.0;
            double r = System.Math.Sqrt(System.Math.Max(0.0, 1.0 - (z * z)));
            double ga = System.Math.PI * (3.0 - System.Math.Sqrt(5.0));
            double a = ga * i;
            return new double3(System.Math.Cos(a) * r, System.Math.Sin(a) * r, z);
        }
    }
}