using System;
using System.Globalization;
using Galilego.Universe;
using Unity.Mathematics;

internal static partial class P1bTests
{
    /// <summary>
    /// Сжатие верхнего хвоста формы: три варианта параметров, цель - максимум
    /// 8.8..9.0 км при p99 около 5.7 км. Показывает, что сжатие трогает
    /// именно хвост, а не основную массу распределения, и что оно не ломает
    /// нормали: у рационального колена наклон на пороге ровно 1, поэтому
    /// первая производная непрерывна и ребра не появляется.
    /// </summary>
    private static int Test130_TailCompression()
    {
        const int n = 4000000;
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "    выборка {0} направлений. Профиль = EarthLike_Perlin.asset (Amp 13662, ridgedMix 0.7, threshold -0.4)\n", n));

        Variant("A. без сжатия (TailKnee=0)", 0d, 0.5656d, n);
        Variant("B. TailKnee=0.13", 0.13d, 0.5656d, n);
        Variant("C. TailKnee=0.15", 0.15d, 0.5656d, n);
        Variant("D. TailKnee=0.18", 0.18d, 0.5656d, n);

        Console.WriteLine();
        Console.WriteLine("    профиль вдоль меридиана через самую высокую точку (B, TailKnee=0.13):");
        Meridian(0.13d, 0.5656d);

        return 0;
    }

    private static HeightfieldTerrain PerlinTuned(double knee, double threshold)
    {
        HeightfieldTerrain t = SceneLikeTerrain();
        t.NoiseStyle = (int)TerrainNoiseStyle.Perlin;
        t.MaskNoiseStyle = -1;
        t.RidgedMode = (int)TerrainRidgedMode.Multifractal;
        t.RidgedSharpness = 2;
        t.RidgedWeightGain = 2d;
        t.RidgedGamma = 1d;
        t.AmplitudeMeters = 13662d;
        t.RidgedMix = 0.7d;
        t.ContinentThreshold = -0.4d;
        t.ContinentDepth = 1.2d;
        t.WarpStrength = 0.1d;
        t.Octaves = 10;
        t.Gain = 0.5d;
        t.SlopeDamp = 0d;
        t.SlopeDampMode = (int)TerrainSlopeDampMode.Off;
        t.TailKnee = knee;
        t.TailThreshold = threshold;
        return t;
    }

    private static void Variant(string label, double knee, double threshold, int n)
    {
        HeightfieldTerrain t = PerlinTuned(knee, threshold);
        TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(t);
        double[] h = new double[n];
        double ga = System.Math.PI * (3.0 - System.Math.Sqrt(5.0));
        for (int i = 0; i < n; i++)
        {
            double z = 1.0 - (2.0 * (i + 0.5)) / n;
            double r = System.Math.Sqrt(System.Math.Max(0.0, 1.0 - (z * z)));
            double a = ga * i;
            h[i] = TerrainNoise.SampleHeight(p, new double3(System.Math.Cos(a) * r, System.Math.Sin(a) * r, z));
        }

        Array.Sort(h);
        double amp = p.AmplitudeMeters;
        double p50 = h[n / 2] * amp;
        double p99 = h[(int)(n * 0.99)] * amp;
        double p999 = h[(int)(n * 0.999)] * amp;
        double p9999 = h[(int)(n * 0.9999)] * amp;
        double max = h[n - 1] * amp;
        double min = h[0] * amp;

        double land = 0d;
        for (int i = 0; i < n; i++)
        {
            if (h[i] > 0d)
            {
                land += 1d;
            }
        }

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "  {0,-34} p50 {1,7:N0}  p99 {2,7:N0}  p99.9 {3,7:N0}  p99.99 {4,7:N0}  max {5,7:N0}  min {6,8:N0}  суша {7,6:P2}  (м)",
            label, p50, p99, p999, p9999, max, min, land / n));
    }

    /// <summary>
    /// Профиль вдоль меридиана через глобальный максимум. Показывает, что
    /// сжатие не оставляет "плоскую макушку": вершина остаётся острой, просто
    /// ниже. Плоская макушка была бы признаком жёсткого клиппинга.
    /// </summary>
    private static void Meridian(double knee, double threshold)
    {
        const int scan = 400000;
        HeightfieldTerrain t = PerlinTuned(knee, threshold);
        TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(t);
        TerrainNoiseParams pRaw = TerrainNoiseParams.FromTerrain(PerlinTuned(0d, 0d));

        // Ищем глобальный максимум по сканированию сферы.
        double best = double.NegativeInfinity;
        double3 bestDir = new double3(0d, 0d, 1d);
        double ga = System.Math.PI * (3.0 - System.Math.Sqrt(5.0));
        for (int i = 0; i < scan; i++)
        {
            double z = 1.0 - (2.0 * (i + 0.5)) / scan;
            double r = System.Math.Sqrt(System.Math.Max(0.0, 1.0 - (z * z)));
            double a = ga * i;
            double3 d = new double3(System.Math.Cos(a) * r, System.Math.Sin(a) * r, z);
            double v = TerrainNoise.SampleHeight(p, d);
            if (v > best)
            {
                best = v;
                bestDir = d;
            }
        }

        // Перпендикуляр к направлению максимума, в нём идёт меридиан.
        double3 up = System.Math.Abs(bestDir.z) < 0.9d ? new double3(0d, 0d, 1d) : new double3(1d, 0d, 0d);
        double3 e1 = SafeNormalize(math.cross(up, bestDir));
        double3 e2 = math.cross(bestDir, e1);

        double amp = p.AmplitudeMeters;
        double rawAmp = pRaw.AmplitudeMeters;
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "    вершина: сжатая {0:N0} м, без сжатия {1:N0} м, направление ({2:F3}, {3:F3}, {4:F3})",
            best * amp, TerrainNoise.SampleHeight(pRaw, bestDir) * rawAmp,
            bestDir.x, bestDir.y, bestDir.z));

        const double stepDeg = 0.05d;
        for (int d = -6; d <= 6; d++)
        {
            double ang = d * stepDeg * System.Math.PI / 180d;
            double3 dir = math.normalize((bestDir * System.Math.Cos(ang)) + (e1 * System.Math.Sin(ang)));
            double hc = TerrainNoise.SampleHeight(p, dir) * amp;
            double hr = TerrainNoise.SampleHeight(pRaw, dir) * rawAmp;
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "      {0,6:F2}°  сжатая {1,8:N0} м   без сжатия {2,8:N0} м   разница {3,7:N0} м",
                d * stepDeg, hc, hr, hr - hc));
        }
    }

    private static double3 SafeNormalize(double3 v)
    {
        double len = math.length(v);
        return len > 1e-12d ? v / len : new double3(1d, 0d, 0d);
    }

    /// <summary>
    /// Две проверки, которые обязаны выполняться, иначе сжатие портит форму.
    ///
    /// 1) Непрерывность наклона на пороге. Рациональное колено специально
    ///    построено так, что y'(t) = 1 с обеих сторон. Если это сломать
    ///    (например заменить на min/max), на пороге появится излом, и в
    ///    нормалях поверхности будет видимая полоса. Проверяем численно.
    ///
    /// 2) Отсутствие плоской макушки. Потолок у сжатия асимптотический,
    ///    поэтому вершина остаётся острой: сжатие срезает высоту, но не
    ///    обнуляет наклон. Проверяем, что у вершины наклон остаётся
    ///    ненулевым - плоская крыша означала бы, что это клиппинг, а не колено.
    /// </summary>
    private static int Test131_TailCompressionCrease()
    {
        double t = 0.5656d, knee = 0.13d;
        HeightfieldTerrain p = PerlinTuned(knee, t);
        TerrainNoiseParams np = TerrainNoiseParams.FromTerrain(p);

        // 1) Наклон слева и справа от порога.
        const double h = 1e-7d;
        double left = TerrainNoise.ApplyTailCompression(np, t - h);
        double right = TerrainNoise.ApplyTailCompression(np, t + h);
        double slopeLeft = (TerrainNoise.ApplyTailCompression(np, t) - left) / h;
        double slopeRight = (right - TerrainNoise.ApplyTailCompression(np, t)) / h;
        double slopeGap = System.Math.Abs(slopeLeft - slopeRight);

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "    наклон у порога: слева {0:F9}, справа {1:F9}, расхождение {2:E2}",
            slopeLeft, slopeRight, slopeGap));

        Check(slopeGap < 1e-5d, "T131 tail-crease",
            "на пороге сжатия излом: расхождение наклона " + slopeGap.ToString("E2", CultureInfo.InvariantCulture));

        // 2) Вершина не срезана в ноль: у самой высокой точки наклон остаётся.
        TerrainNoiseParams pRaw = TerrainNoiseParams.FromTerrain(PerlinTuned(0d, 0d));
        const int scan = 200000;
        double best = double.NegativeInfinity;
        double3 bestDir = new double3(0d, 0d, 1d);
        double ga = System.Math.PI * (3.0 - System.Math.Sqrt(5.0));
        for (int i = 0; i < scan; i++)
        {
            double z = 1.0 - (2.0 * (i + 0.5)) / scan;
            double r = System.Math.Sqrt(System.Math.Max(0.0, 1.0 - (z * z)));
            double a = ga * i;
            double3 d = new double3(System.Math.Cos(a) * r, System.Math.Sin(a) * r, z);
            double v = TerrainNoise.SampleHeight(np, d);
            if (v > best)
            {
                best = v;
                bestDir = d;
            }
        }

        const double e = 1e-4d;
        double up1 = TerrainNoise.SampleHeight(np, bestDir + new double3(e, 0d, 0d));
        double dn1 = TerrainNoise.SampleHeight(np, bestDir - new double3(e, 0d, 0d));
        double up2 = TerrainNoise.SampleHeight(np, bestDir + new double3(0d, e, 0d));
        double dn2 = TerrainNoise.SampleHeight(np, bestDir - new double3(0d, e, 0d));
        double slope = math.length(new double3(up1 - dn1, up2 - dn2, 0d)) / (2d * e);

        // Для сравнения: наклон у вершины БЕЗ сжатия.
        double rawBest = double.NegativeInfinity;
        double3 rawDir = new double3(0d, 0d, 1d);
        for (int i = 0; i < scan; i++)
        {
            double z = 1.0 - (2.0 * (i + 0.5)) / scan;
            double r = System.Math.Sqrt(System.Math.Max(0.0, 1.0 - (z * z)));
            double a = ga * i;
            double3 d = new double3(System.Math.Cos(a) * r, System.Math.Sin(a) * r, z);
            double v = TerrainNoise.SampleHeight(pRaw, d);
            if (v > rawBest)
            {
                rawBest = v;
                rawDir = d;
            }
        }

        double rawSlope = math.length(new double3(
            TerrainNoise.SampleHeight(pRaw, rawDir + new double3(e, 0d, 0d)) - TerrainNoise.SampleHeight(pRaw, rawDir - new double3(e, 0d, 0d)),
            TerrainNoise.SampleHeight(pRaw, rawDir + new double3(0d, e, 0d)) - TerrainNoise.SampleHeight(pRaw, rawDir - new double3(0d, e, 0d)),
            0d)) / (2d * e);

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "    наклон у вершины: сжатой {0:F3}, без сжатия {1:F3} (сохранён {2:P1})",
            slope, rawSlope, rawSlope > 0d ? slope / rawSlope : 0d));

        Check(slope > 0.15d * rawSlope, "T131 tail-crease",
            "вершина срезана в плоскую крышу: наклон " + slope.ToString("F3", CultureInfo.InvariantCulture)
            + " против " + rawSlope.ToString("F3", CultureInfo.InvariantCulture) + " без сжатия");

        return 0;
    }

    /// <summary>
    /// Пункт 5: окна домена валидны только при Gain * Lacunarity == 1.
    /// </summary>
    private static int Test132_DampWindowsValid()
    {
        double[] gains = { 0.5d, 0.6d, 0.4d };
        double[] lacun = { 2d, 2d, 2.5d };

        for (int i = 0; i < gains.Length; i++)
        {
            double product = TerrainNoise.EffectiveGain(gains[i]) * TerrainNoise.EffectiveLacunarity(lacun[i]);
            bool ok = TerrainNoise.SlopeDampWindowsValid(gains[i], lacun[i]);
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "    Gain {0:F2} * Lacunarity {1:F2} = {2:F4}  -> окна {3}",
                gains[i], lacun[i], product, ok ? "пригодны" : "НЕПРИГОДНЫ"));
        }

        // Текущие профили обязаны быть пригодны.
        HeightfieldTerrain legacy = SceneLikeTerrain();
        legacy.SlopeDamp = 0.6d;
        Check(TerrainNoise.SlopeDampWindowsValid(legacy.Gain, legacy.Lacunarity), "T132 damp-windows",
            "legacy-профиль: Gain*Lacunarity != 1 при SlopeDamp > 0, окна домена откалиброваны под произведение 1");

        HeightfieldTerrain perlin = PerlinTuned(0.13d, 0.5656d);
        perlin.SlopeDamp = 0.6d;
        Check(TerrainNoise.SlopeDampWindowsValid(perlin.Gain, perlin.Lacunarity), "T132 damp-windows",
            "Perlin-профиль: Gain*Lacunarity != 1 при SlopeDamp > 0");

        // И отрицательный контроль: убеждаемся, что предикат ловит поломку.
        Check(!TerrainNoise.SlopeDampWindowsValid(0.6d, 2d), "T132 damp-windows",
            "предикат не ловит Gain 0.6 * Lacunarity 2 = 1.2, то есть он бесполезен");
        Check(!TerrainNoise.SlopeDampWindowsValid(0.5d, 2.5d), "T132 damp-windows",
            "предикат не ловит Gain 0.5 * Lacunarity 2.5 = 1.25");

        // И что поломка действительно ломает сигнал: вклад октавы в наклон
        // растёт геометрически, и нормировка /(o+1) перестаёт делить на
        // константу. Показываем это числами, а не только флагом.
        Console.WriteLine("    вклад октавы в накопленный наклон (амплитуда * частота):");
        foreach (double g in new double[] { 0.5d, 0.6d })
        {
            double prod = g * 2d;
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "      Gain {0:F2}: октавы 0..5 = {1}", g,
                string.Join(" ", ContributionRow(g, 2d))));
            Check(System.Math.Abs(prod - 1d) < 1e-9d || g == 0.6d,
                "T132 damp-windows", "контроль соотношения сломан");
        }

        // Конвертация обязана реально гасить домен, а не только ругаться в лог.
        HeightfieldTerrain broken = PerlinTuned(0.13d, 0.5656d);
        broken.SlopeDamp = 0.6d;
        broken.SlopeDampMode = (int)TerrainSlopeDampMode.Accum;
        broken.Gain = 0.6d;
        broken.Lacunarity = 2d;
        TerrainNoiseParams converted = TerrainNoiseParams.FromTerrain(broken);
        Check(!(converted.SlopeDamp > 0d), "T132 damp-windows",
            "при Gain*Lacunarity != 1 домен не отключён при конвертации: SlopeDamp = "
            + converted.SlopeDamp.ToString("F3", CultureInfo.InvariantCulture));
        Check(converted.SlopeDampMode == (int)TerrainSlopeDampMode.Off, "T132 damp-windows",
            "при Gain*Lacunarity != 1 режим домена не сброшен в Off при конвертации");

        // И наоборот: валидная конфигурация проходит нетронутой.
        HeightfieldTerrain valid = PerlinTuned(0.13d, 0.5656d);
        valid.SlopeDamp = 0.6d;
        valid.SlopeDampMode = (int)TerrainSlopeDampMode.Gradient;
        valid.Gain = 0.5d;
        valid.Lacunarity = 2d;
        TerrainNoiseParams okConverted = TerrainNoiseParams.FromTerrain(valid);
        Check(okConverted.SlopeDamp > 0d
            && okConverted.SlopeDampMode == (int)TerrainSlopeDampMode.Gradient,
            "T132 damp-windows",
            "валидный профиль потерял домен при конвертации: SlopeDamp = "
            + okConverted.SlopeDamp.ToString("F3", CultureInfo.InvariantCulture));

        return 0;
    }

    private static string[] ContributionRow(double gain, double lacunarity)
    {
        var row = new string[6];
        double amplitude = 1d, frequency = 1d;
        for (int o = 0; o < 6; o++)
        {
            row[o] = (amplitude * frequency).ToString("F4", CultureInfo.InvariantCulture);
            amplitude *= gain;
            frequency *= lacunarity;
        }
        return row;
    }
}

