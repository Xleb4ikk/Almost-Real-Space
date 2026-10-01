using System;
using System.Globalization;
using Galilego.Universe;
using Unity.Mathematics;

internal static partial class P1bTests
{
    /// <summary>
    /// Распределение сигнала домена. Нужно, чтобы выбрать константу насыщения
    /// SlopeNorm по данным, а не на глаз (В7а: p10 → ≈0.1, p90 → ≈0.9).
    ///
    /// Три кандидата, потому что текущий на глаз промахнулся: SlopeNorm
    /// насыщался единицей на всех октавах, вес становился постоянным
    /// 1 − damp и СОКРАЩАЛСЯ при нормировке sum/norm. На форме рельефа это давало
    /// ноль изменения — T110 показал байт-идентичную статистику у Gradient и Off.
    ///
    ///   A  |∇_o|·freq_o              — сигнал текущей октавы
    ///   B  Σ amp·|∇|·freq / Σ amp    — средний наклон накопленного (частотонезависим)
    ///   C  Σ amp·|∇|·freq            — накопленный наклон как есть
    ///
    /// Диапазон частот широкий: BaseFrequency 20 и десять октав при Lacunarity 2
    /// дают от 20 до 10240, то есть частоты различаются в 512 раз. Один порог
    /// насыщения на все октавы поэтому в принципе не подходит — и это, скорее
    /// всего, и есть причина, по которой A насыщался везде.
    /// </summary>
    private static int Test112d_DampSignalHistogram()
    {
        const int n = 40000;
        const int octaves = 10;
        const double baseFrequency = 20d;
        const double gain = 0.5d;
        const double lacunarity = 2d;

        var a = new double[n * octaves];
        var b = new double[n * octaves];
        var c = new double[n * octaves];
        var rng = new Random(20260930);
        var rngOffset = new Random3(24334543);

        for (int i = 0; i < n; i++)
        {
            double3 dir = rngOffset.Direction(i);
            double amplitude = 1d;
            double frequency = baseFrequency;
            double slopeAccum = 0d;
            double ampAccum = 0d;
            double3 offset = rngOffset.Offset;

            for (int o = 0; o < octaves; o++)
            {
                TerrainNoise.GradientNoiseWithDerivative((dir * frequency) + offset, out double3 d);
                double slope = math.length(d) * frequency;
                a[(o * n) + i] = slope;
                slopeAccum += amplitude * slope;
                ampAccum += amplitude;
                c[(o * n) + i] = slopeAccum;
                b[(o * n) + i] = ampAccum > 0d ? slopeAccum / ampAccum : 0d;
                amplitude *= gain;
                frequency *= lacunarity;
            }
        }

        for (int o = 0; o < octaves; o++)
        {
            ReportOctave("A |∇|·freq   ", a, n, octaves, o, baseFrequency * Math.Pow(lacunarity, o));
            ReportOctave("B накопл./амп", b, n, octaves, o, baseFrequency * Math.Pow(lacunarity, o));
            ReportOctave("C накопленный", c, n, octaves, o, baseFrequency * Math.Pow(lacunarity, o));
        }

        // Сигнал режима Accum — |накопленное значение| в нормированной форме.
        // Он живёт в своём масштабе (форма рельефа в [−1,1]), и окно насыщения у
        // него должно быть своё: с общим окном градиентного режима Accum
        // молча вырождается в no-op, потому что его сигнал всегда ниже порога.
        var accum = new double[n * octaves];
        for (int i = 0; i < n; i++)
        {
            double3 dir = rngOffset.Direction(i);
            double amplitude = 1d;
            double frequency = baseFrequency;
            double sum = 0d;
            double norm = 0d;
            double3 offset = rngOffset.Offset;
            for (int o = 0; o < octaves; o++)
            {
                double v = TerrainNoise.GradientNoise((dir * frequency) + offset);
                sum += amplitude * v;
                norm += amplitude;
                accum[(o * n) + i] = norm > 0d ? Math.Abs(sum / norm) : 0d;
                amplitude *= gain;
                frequency *= lacunarity;
                offset = new double3(offset.y + 19.19d, offset.z + 7.47d, offset.x + 3.13d);
            }
        }

        Console.WriteLine("  сигнал режима Accum, |накопленная форма|:");
        for (int o = 0; o < octaves; o++)
        {
            ReportOctave("Accum", accum, n, octaves, o, baseFrequency * Math.Pow(lacunarity, o));
        }

        return 0;
    }

    private static void ReportOctave(string label, double[] v, int n, int octaves, int o, double freq)
    {
        var slice = new double[n];
        Array.Copy(v, o * n, slice, 0, n);
        Array.Sort(slice);
        double p10 = slice[(int)(n * 0.10)];
        double p50 = slice[(int)(n * 0.50)];
        double p90 = slice[(int)(n * 0.90)];
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "    {0} октава {1} (freq {2,7:F0}): p10={3,9:F3}  p50={4,9:F3}  p90={5,9:F3}  p90/p10={6,8:F2}",
            label, o, freq, p10, p50, p90, p10 > 1e-12 ? p90 / p10 : double.PositiveInfinity));
    }

    /// <summary>Детерминированные направления и сид-оффсот формы для замеров домена.</summary>
    private sealed class Random3
    {
        private readonly int seed;

        public Random3(int seed)
        {
            this.seed = seed;
        }

        public double3 Offset
        {
            get
            {
                return new double3(
                    seed * 61.3d + 1700.9d, seed * 19.77d + 1300.3d, seed * 83.31d + 1500.6d);
            }
        }

        public double3 Direction(int i)
        {
            double z = 1.0 - (2.0 * (i + 0.5)) / 40000.0;
            double r = Math.Sqrt(Math.Max(0.0, 1.0 - (z * z)));
            double ga = Math.PI * (3.0 - Math.Sqrt(5.0));
            double a = ga * i;
            return new double3(Math.Cos(a) * r, Math.Sin(a) * r, z);
        }
    }
}
