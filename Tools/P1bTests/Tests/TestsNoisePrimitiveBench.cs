using System;
using System.Diagnostics;
using System.Globalization;
using Galilego.Universe;
using Unity.Mathematics;

internal static partial class P1bTests
{
    /// <summary>
    /// Замер примитива в чистом виде — без всей сборки SampleHeight.
    ///
    /// Нужен потому, что в полном конвейере разница схем неразделима: там сливаются
    /// warp, маски и ridged, и по времени нельзя понять, во сколько обошлась
    /// именно замена примитива. Здесь видно ровно это.
    ///
    /// Замер без делегатов и без лямбд: вызов через Func стоит больше, чем сам
    /// примитив, и спокойно съел бы весь эффект. Три явных повтора цикла —
    /// повторяемость здесь важнее компактности.
    /// </summary>
    private static int Test111_NoisePrimitiveBench()
    {
        const int n = 4000000;
        var pts = new double3[n];
        var rng = new Random(9001);

        // Широкий разброс координат: в реальности октавы доходят до частоты ~1e4
        // и индексы ячеек уезжают далеко за пределы int.
        for (int i = 0; i < n; i++)
        {
            double z = (rng.NextDouble() * 2d) - 1d;
            double a = rng.NextDouble() * Math.PI * 2d;
            double r = Math.Sqrt(Math.Max(0d, 1d - (z * z)));
            double s = rng.NextDouble() * 20000d;
            pts[i] = new double3(Math.Cos(a) * r * s, Math.Sin(a) * r * s, z * s);
        }

        ptsCache = pts;

        double valueMs = TimePrimitive(n, 0, out double valueMin, out double valueMax, out double valueRms);
        double perlinMs = TimePrimitive(n, 1, out double perlinMin, out double perlinMax, out double perlinRms);

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "  {0,-16} {1,8:F1} ms  {2,6:F1} ns/вызов  диапазон [{3:F4}, {4:F4}]  RMS {5:F5}",
            "ValueNoise", valueMs, (valueMs * 1e6) / n, valueMin, valueMax, valueRms));
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "  {0,-16} {1,8:F1} ms  {2,6:F1} ns/вызов  x{3,5:F2}  диапазон [{4:F4}, {5:F4}]  RMS {6:F5}",
            "Perlin", perlinMs, (perlinMs * 1e6) / n, perlinMs / valueMs, perlinMin, perlinMax, perlinRms));
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "  RMS value/perlin = {0:F5}   => константа нормировки октавы = {1:F5}",
            valueRms / perlinRms, perlinRms / valueRms));

        Check(!double.IsNaN(perlinMax) && !double.IsNaN(valueMax), "BENCH noise-primitive",
            "оба примитива дают числа");

        // Контракт диапазона. Прежний был неявным: value noise на углах ячейки
        // берёт значения из [−1,1], и трилинейная смесь не может выйти за
        // выпуклую оболочку, поэтому «форма в [−1,1]» выполнялось само собой.
        // У градиентного примитива такого свойства нет: после выравнивания по
        // RMS пик октавы уходит за единицу, и граница становится явной —
        // TerrainNoise.ShapeBound. Проверяем оба факта, потому что смена
        // неявного контракта на явный — как раз тот случай, который молча ломает
        // всё, что на него опиралось.
        Check(valueMax <= 1.0, "BENCH noise-primitive",
            "value noise держит неявную границу 1: пик "
            + valueMax.ToString("F4", CultureInfo.InvariantCulture));

        const double theoretical = 0.86602540378443864676d;
        Check(perlinMax > 1.0, "BENCH noise-primitive",
            "Перлин после нормировки по RMS действительно выходит за 1: пик "
            + perlinMax.ToString("F4", CultureInfo.InvariantCulture)
            + " — значит неявный контракт больше не действует");

        // Границу берём из самого кода, а не пересчитываем: после нормировки
        // RMS примитивов сравнялись, и обратное отношение дало бы 1 вместо
        // масштаба — то есть проверялось бы ровно то, чего нет.
        Check(perlinMax <= TerrainNoise.ShapeBound, "BENCH noise-primitive",
            "пик Перлина ≤ ShapeBound ("
            + TerrainNoise.ShapeBound.ToString("F4", CultureInfo.InvariantCulture)
            + "): " + perlinMax.ToString("F4", CultureInfo.InvariantCulture));

        // Нормировка примитива по RMS нужна, чтобы пороги профиля
        // (ContinentThreshold и подобные) сохраняли смысл: у Перлина RMS ниже,
        // и без масштаба доля суши уезжает за пределы допуска T87.
        Check(perlinRms > 0d && valueRms > 0d, "BENCH noise-primitive",
            "RMS примитивов посчитан: value " + valueRms.ToString("F5", CultureInfo.InvariantCulture)
            + ", perlin " + perlinRms.ToString("F5", CultureInfo.InvariantCulture)
            + ", нормировка " + (perlinRms / valueRms).ToString("F5", CultureInfo.InvariantCulture));
        return 0;
    }

    /// <summary>0 = value noise, 1 = градиентный Перлин. Три повтора, берём минимум.</summary>
    private static double TimePrimitive(int n, int which, out double min, out double max, out double rms)
    {
        double best = double.MaxValue;
        double sink = 0d;
        min = 0d;
        max = 0d;
        rms = 0d;
        for (int r = 0; r < 3; r++)
        {
            double lo = double.MaxValue;
            double hi = double.MinValue;
            double sq = 0d;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < n; i++)
            {
                double v = which == 0
                    ? TerrainNoise.ValueNoise(ptsCache[i])
                    : TerrainNoise.GradientNoise(ptsCache[i]);
                if (v < lo) lo = v;
                if (v > hi) hi = v;
                sq += v * v;
                sink += v;
            }

            sw.Stop();
            if (sw.Elapsed.TotalMilliseconds < best) best = sw.Elapsed.TotalMilliseconds;
            min = lo;
            max = hi;
            rms = Math.Sqrt(sq / n);
        }

        if (double.IsNaN(sink))
        {
            Console.WriteLine("  (!!) nan в сумме");
        }

        return best;
    }

    private static double3[] ptsCache;
}
