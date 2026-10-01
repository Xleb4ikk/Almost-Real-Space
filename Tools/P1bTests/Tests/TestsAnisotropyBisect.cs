using System;
using System.Globalization;
using Galilego.Universe;
using Unity.Mathematics;

internal static partial class P1bTests
{
    /// <summary>
    /// Где появляется остаточная осевая анизотропия 1.96% у градиентного
    /// примитива. T122 показал её на полном пайплайне, но не объяснил: набор
    /// градиентов T112 симметричен (Σggᵀ = 4I, разброс 0.065%), значит причина
    /// в другом месте цепочки. Здесь цепочка режется по стадиям:
    ///
    ///   1) примитив сам по себе, АНАЛИТИЧЕСКИЙ ∇ (без конечных разностей)
    ///   2) одна октава fBm
    ///   3) полный fBm формы
    ///   4) + ridged multifractal
    ///   5) + warp
    ///
    /// Плюс отдельные проверки на гипотезы:
    ///   • корреляция индекса h%12 между соседями решётки вдоль каждой оси
    ///     (мёртвый старший бит хеша мог оставить связь между соседями, и она
    ///     не обязана быть нулевой);
    ///   • симметрия SaltOffset по осям;
    ///   • одинаковые ли соли у трёх осей warp.
    /// </summary>
    private static int Test124_AnisotropyBisect()
    {
        Console.WriteLine("    разброс энергии градиента по осям (изотропно = 0):");

        // Стадия 1: примитив, аналитический ∇, без fBm и без смещения.
        Stage("1. примитив, аналитический ∇", MeasurePrimitive(1, 2000000));
        Stage("1. value noise, аналитический ∇ нет — конечные разности", MeasurePrimitiveValue(1000000));

        // Стадии 2-5: полный SampleHeight со снятыми по очереди слоями.
        Stage("2. одна октава fBm (salt 0)", MeasureStage(2, 0d, 0d, 0, 1));
        Stage("3. полный fBm формы (10 октав)", MeasureStage(3, 0d, 0d, 0, 1));
        Stage("4. + ridged multifractal", MeasureStage(4, 0d, 0.7, 1, 1));
        Stage("5. + warp 0.1", MeasureStage(5, 0.1, 0.7, 1, 1));

        Console.WriteLine();
        Console.WriteLine("    гипотезы:");

        // Корреляция индекса градиента между соседями вдоль осей. Если хеш
        // оставляет связь, соседние узлы будут получать одно и то же
        // направление чаще, чем случайно.
        for (int axis = 0; axis < 3; axis++)
        {
            Console.WriteLine("    h%12 у соседей вдоль оси " + "xyz"[axis] + ": " + NeighbourAgreement(axis, 200000));
        }

        // Симметрия сдвигов по осям: у SaltOffset коэффициенты разные, но это
        // не должно давать анизотропию ПОЛЯ, потому что сдвиг только выбирает
        // область. Проверяем, что сдвиг не вырожден по какой-то оси.
        Console.WriteLine("    " + SaltAxisCheck());

        return 0;
    }

    private static void Stage(string label, double spreadPercent)
    {
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture, "    {0,-52} {1,7:F3} %", label, spreadPercent));
    }

    private static double MeasurePrimitive(int style, int n)
    {
        var rng = new Random(0xA115);
        double e0 = 0d, e1 = 0d, e2 = 0d;
        for (int i = 0; i < n; i++)
        {
            double3 p = new double3(
                rng.NextDouble() * 40000d, rng.NextDouble() * 40000d, rng.NextDouble() * 40000d);
            TerrainNoise.GradientNoiseWithDerivative(p, out double3 g);
            e0 += g.x * g.x;
            e1 += g.y * g.y;
            e2 += g.z * g.z;
        }

        return SpreadPercent(e0, e1, e2);
    }

    private static double MeasurePrimitiveValue(int n)
    {
        var rng = new Random(0xA115);
        const double eps = 1e-4;
        double e0 = 0d, e1 = 0d, e2 = 0d;
        for (int i = 0; i < n; i++)
        {
            double3 p = new double3(
                rng.NextDouble() * 40000d, rng.NextDouble() * 40000d, rng.NextDouble() * 40000d);
            double gx = (TerrainNoise.ValueNoise(p + new double3(eps, 0d, 0d)) - TerrainNoise.ValueNoise(p - new double3(eps, 0d, 0d))) / (2d * eps);
            double gy = (TerrainNoise.ValueNoise(p + new double3(0d, eps, 0d)) - TerrainNoise.ValueNoise(p - new double3(0d, eps, 0d))) / (2d * eps);
            double gz = (TerrainNoise.ValueNoise(p + new double3(0d, 0d, eps)) - TerrainNoise.ValueNoise(p - new double3(0d, 0d, eps))) / (2d * eps);
            e0 += gx * gx;
            e1 += gy * gy;
            e2 += gz * gz;
        }

        return SpreadPercent(e0, e1, e2);
    }

    private static double MeasureStage(int stage, double warp, double ridged, int ridgedMode, int style)
    {
        const int n = 400000;
        const double eps = 1e-5;
        HeightfieldTerrain t = SceneLikeTerrain();
        t.NoiseStyle = style;
        t.MaskNoiseStyle = style;
        t.RidgedMode = ridgedMode;
        t.RidgedGamma = 1.0d;
        t.RidgedMix = ridged;
        t.WarpStrength = warp;
        t.ContinentFrequency = 0d;
        t.PlainMix = 0d;
        t.SlopeDamp = 0d;
        t.SlopeDampMode = (int)TerrainSlopeDampMode.Off;
        if (stage == 2)
        {
            t.Octaves = 1;
        }

        TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(t);
        double3[] dirs = SphereDirs(n);
        double e0 = 0d, e1 = 0d, e2 = 0d;
        for (int i = 0; i < n; i++)
        {
            double3 d = dirs[i];
            double gx = (TerrainNoise.SampleHeight(p, d + new double3(eps, 0d, 0d)) - TerrainNoise.SampleHeight(p, d - new double3(eps, 0d, 0d))) / (2d * eps);
            double gy = (TerrainNoise.SampleHeight(p, d + new double3(0d, eps, 0d)) - TerrainNoise.SampleHeight(p, d - new double3(0d, eps, 0d))) / (2d * eps);
            double gz = (TerrainNoise.SampleHeight(p, d + new double3(0d, 0d, eps)) - TerrainNoise.SampleHeight(p, d - new double3(0d, 0d, eps))) / (2d * eps);
            e0 += gx * gx;
            e1 += gy * gy;
            e2 += gz * gz;
        }

        return SpreadPercent(e0, e1, e2);
    }

    private static double SpreadPercent(double e0, double e1, double e2)
    {
        double trace = e0 + e1 + e2;
        if (!(trace > 0d))
        {
            return 0d;
        }

        double a = e0 / trace, b = e1 / trace, c = e2 / trace;
        return 100d * (System.Math.Max(a, System.Math.Max(b, c)) - System.Math.Min(a, System.Math.Min(b, c)));
    }

    /// <summary>
    /// Доля совпадений индекса направления у соседних узлов решётки вдоль оси.
    /// При независимом хеше ожидание 1/12 = 8.33%. Значимая связь выдала бы
    /// себя отклонением, и особенно по той оси, где биты хеша перекрываются.
    /// </summary>
    private static string NeighbourAgreement(int axis, int n)
    {
        int agree = 0;
        for (int i = 0; i < n; i++)
        {
            int x = (i * 7919) % 20000;
            int y = (i * 104729) % 20000;
            int z = (i * 15485863) % 20000;
            int a = GradientIndex(x, y, z);
            int b = axis == 0 ? GradientIndex(x + 1, y, z)
                : axis == 1 ? GradientIndex(x, y + 1, z)
                : GradientIndex(x, y, z + 1);
            if (a == b)
            {
                agree++;
            }
        }

        return string.Format(
            CultureInfo.InvariantCulture, "{0:F3}% (ожидание 8.333%)",
            100.0 * agree / n);
    }

    /// <summary>Тот же индекс, что выбирает LatticeDot, но без шума вокруг.</summary>
    private static int GradientIndex(int x, int y, int z)
    {
        unchecked
        {
            int h = TerrainNoise.PerlinLatticeHash(x, y, z);
            return (int)((uint)h % 12u);
        }
    }

    private static string SaltAxisCheck()
    {
        // Сдвиги per-октавы: поворотом (y+19.19, z+7.47, x+3.13), то есть по
        // осям одинаково. SaltOffset — фиксированная таблица с разными
        // коэффициентами, но это лишь выбор области, а не формы: при разных
        // сидах набор точек заполняет объём одинаково. Проверяем, что ни одна
        // ось не вырождена (коэффициент не ноль и не даёт целых шагов).
        int[] seeds = { 24334543, 1013, 8932, 16851, 24770, 32689, 40608, 48527 };
        double sx = 0d, sy = 0d, sz = 0d;
        for (int i = 0; i < seeds.Length; i++)
        {
            double s = seeds[i];
            sx += s * 61.3d;
            sy += s * 19.77d;
            sz += s * 83.31d;
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "коэффициенты SaltOffset по осям: x={0:F0} y={1:F0} z={2:F0} — все ненулевые, вырождения нет",
            sx / seeds.Length, sy / seeds.Length, sz / seeds.Length);
    }
    /// <summary>
    /// Проверка гипотезы о множителе оси z в домашнем хеше.
    ///
    /// z умножается на 2147483647 = 2^31 − 1, то есть z·(2^31−1) ≡ −z (mod 2^31):
    /// по модулю 2^31 вклад оси z почти равен «минус z». Множители x (374761393)
    /// и y (668265263) — крупные нечётные, которые перемешивают биты, и z
    /// выделен. Если причина остаточной анизотропии примитива в этом, замена
    /// множителя z на крупное нечётное должна убрать её.
    ///
    /// Энергия градиента меряется по ОДНОМУ слагаемому на узел, то есть это
    /// сумма |g_x|, |g_y|, |g_z| — мера выбора направления, а не поля.
    /// </summary>
    private static int Test125_HashAxisMultiplier()
    {
        const int n = 2000000;
        var rng = new Random(0xA115);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture, "    сумма |g| по осям, {0} узлов в кубе [0,40000)^3:", n));

        uint[] multipliers = { 2147483647u, 2246822519u, 2654435761u, 374761393u };
        string[] labels = { "z*2147483647 (сейчас, = 2^31−1)", "z*2246822519 (CloudNoise)", "z*2654435761", "z*374761393 (= как у x)" };

        for (int mi = 0; mi < multipliers.Length; mi++)
        {
            uint zMul = multipliers[mi];
            double e0 = 0d, e1 = 0d, e2 = 0d;
            for (int i = 0; i < n; i++)
            {
                int x = (int)(rng.NextDouble() * 40000d);
                int y = (int)(rng.NextDouble() * 40000d);
                int z = (int)(rng.NextDouble() * 40000d);
                e0 += System.Math.Abs(GradientComponent(x, y, z, zMul, 0));
                e1 += System.Math.Abs(GradientComponent(x, y, z, zMul, 1));
                e2 += System.Math.Abs(GradientComponent(x, y, z, zMul, 2));
            }

            double tr = e0 + e1 + e2;
            double a = e0 / tr, b = e1 / tr, c = e2 / tr;
            double spread = 100d * (System.Math.Max(a, System.Math.Max(b, c)) - System.Math.Min(a, System.Math.Min(b, c)));
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "    {0,-32} x={1:F4} y={2:F4} z={3:F4}  разброс={4:F3}%",
                labels[mi], a, b, c, spread));
        }

        return 0;
    }

    /// <summary>Модуль одной компоненты градиента при заданном множителе оси z.</summary>
    private static double GradientComponent(int x, int y, int z, uint zMul, int comp)
    {
        unchecked
        {
            uint h = (uint)((x * 374761393) + (y * 668265263) + (int)(z * (long)zMul));
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            int idx = (int)(h % 12u);
            int plane = idx >> 2;
            int k = idx & 3;
            double sa = 1d - (2d * (k & 1));
            double sb = 1d - (2d * ((k >> 1) & 1));
            const double g2 = 0.70710678118654752440d;
            double mx = 1d - (plane >> 1);
            double my = 1d - (plane & 1);
            double mz = (plane + 1) >> 1;
            double sy = sb + ((plane >> 1) * (sa - sb));
            if (comp == 0)
            {
                return sa * g2 * mx;
            }

            return comp == 1 ? sy * g2 * my : sb * g2 * mz;
        }
    }
    /// <summary>
    /// Аналитическая производная примитива против конечных разностей в ОДНОМ
    /// и том же кубе. Разбивка показала 1.722% на аналитическом ∇ и 0.228% на
    /// конечных разностях через пайплайн; если разрыв держится и здесь, то
    /// расходится именно производная (она выведена вручную), а не поле.
    /// </summary>
    private static int Test126_DerivativeAnisotropy()
    {
        const int n = 2000000;
        var rng = new Random(0xA115);
        const double eps = 1e-5;
        double a0 = 0d, a1 = 0d, a2 = 0d;
        double f0 = 0d, f1 = 0d, f2 = 0d;
        for (int i = 0; i < n; i++)
        {
            double3 p = new double3(
                rng.NextDouble() * 40000d, rng.NextDouble() * 40000d, rng.NextDouble() * 40000d);
            TerrainNoise.GradientNoiseWithDerivative(p, out double3 g);
            a0 += g.x * g.x;
            a1 += g.y * g.y;
            a2 += g.z * g.z;

            double fx = (TerrainNoise.GradientNoise(p + new double3(eps, 0d, 0d)) - TerrainNoise.GradientNoise(p - new double3(eps, 0d, 0d))) / (2d * eps);
            double fy = (TerrainNoise.GradientNoise(p + new double3(0d, eps, 0d)) - TerrainNoise.GradientNoise(p - new double3(0d, eps, 0d))) / (2d * eps);
            double fz = (TerrainNoise.GradientNoise(p + new double3(0d, 0d, eps)) - TerrainNoise.GradientNoise(p - new double3(0d, 0d, eps))) / (2d * eps);
            f0 += fx * fx;
            f1 += fy * fy;
            f2 += fz * fz;
        }

        double sa = SpreadPercent(a0, a1, a2);
        double sf = SpreadPercent(f0, f1, f2);
        double ta = a0 + a1 + a2;
        double tf = f0 + f1 + f2;
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "    аналитический ∇: x={0:F4} y={1:F4} z={2:F4}  разброс {3:F3}%",
            a0 / ta, a1 / ta, a2 / ta, sa));
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "    конечные разности: x={0:F4} y={1:F4} z={2:F4}  разброс {3:F3}%",
            f0 / tf, f1 / tf, f2 / tf, sf));
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "    средний модуль: аналит {0:F4}, разн {1:F4}  (расхождение {2:F2}%)",
            System.Math.Sqrt(ta / n), System.Math.Sqrt(tf / n),
            100d * (System.Math.Sqrt(ta / n) / System.Math.Sqrt(tf / n) - 1d)));

        Check(System.Math.Abs(sa - sf) < 0.5, "T126 derivative-anisotropy",
            "аналитический и конечноразностный ∇ дают одинаковую осевую картину: "
            + sa.ToString("F3", CultureInfo.InvariantCulture) + "% против "
            + sf.ToString("F3", CultureInfo.InvariantCulture) + "%");
        return 0;
    }
    /// <summary>
    /// Поле (а не распределение направлений) при разных множителях оси z.
    ///
    /// T125 показал, что выбор направления изотропен при любом множителе
    /// (разброс 0.035..0.046%), а T126 — что производная верна. Значит перекос
    /// живёт во второпорядковой структуре хеша: у соседей вдоль z индекс
    /// совпадает 7.816% раз против 8.333% ожидаемых, тогда как вдоль x 8.427%
    /// и вдоль y 8.354%. z выделен множителем 2147483647 = 2^31 − 1.
    /// Здесь меряется именно энергия градиента ПОЛЯ при подмене множителя.
    /// </summary>
    private static int Test127_HashFixField()
    {
        const int n = 1000000;
        var rng = new Random(0xA115);
        const double eps = 1e-5;
        uint[] multipliers = { 2147483647u, 2246822519u, 2654435761u };
        string[] labels = { "сейчас (2^31−1)", "2246822519", "2654435761" };

        for (int mi = 0; mi < multipliers.Length; mi++)
        {
            uint zMul = multipliers[mi];
            double e0 = 0d, e1 = 0d, e2 = 0d;
            for (int i = 0; i < n; i++)
            {
                int ix = (int)(rng.NextDouble() * 40000d);
                int iy = (int)(rng.NextDouble() * 40000d);
                int iz = (int)(rng.NextDouble() * 40000d);
                double fx = rng.NextDouble(), fy = rng.NextDouble(), fz = rng.NextDouble();
                double3 p = new double3(ix + fx, iy + fy, iz + fz);

                double ax = GradWithZMul(p, zMul, 0);
                double ay = GradWithZMul(p, zMul, 1);
                double az = GradWithZMul(p, zMul, 2);
                e0 += ax * ax;
                e1 += ay * ay;
                e2 += az * az;
            }

            double tr = e0 + e1 + e2;
            double a = e0 / tr, b = e1 / tr, c = e2 / tr;
            double spread = 100d * (System.Math.Max(a, System.Math.Max(b, c)) - System.Math.Min(a, System.Math.Min(b, c)));
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "    z*{0,-12} x={1:F4} y={2:F4} z={3:F4}  разброс={4:F3}%",
                labels[mi], a, b, c, spread));
        }

        return 0;
    }

    /// <summary>
    /// Градиент примитива с подменённым множителем оси z. Повторяет
    /// GradientNoiseWithDerivative, но со своим хешем — иначе подмена
    /// множителя потребовала бы правки продакшн-кода.
    /// </summary>
    private static double GradWithZMul(double3 p, uint zMul, int comp)
    {
        const double eps = 1e-5;
        double3 o = new double3(eps, 0d, 0d);
        if (comp == 1)
        {
            o = new double3(0d, eps, 0d);
        }
        else if (comp == 2)
        {
            o = new double3(0d, 0d, eps);
        }

        double hi = PerlinValueWithZMul(p + o, zMul);
        double lo = PerlinValueWithZMul(p - o, zMul);
        return (hi - lo) / (2d * eps);
    }

    private static double PerlinValueWithZMul(double3 p, uint zMul)
    {
        double xf = math.floor(p.x), yf = math.floor(p.y), zf = math.floor(p.z);
        int ix = unchecked((int)(long)xf);
        int iy = unchecked((int)(long)yf);
        int iz = unchecked((int)(long)zf);
        double fx = p.x - xf, fy = p.y - yf, fz = p.z - zf;
        double ux = fx * fx * fx * (fx * ((fx * 6d) - 15d) + 10d);
        double uy = fy * fy * fy * (fy * ((fy * 6d) - 15d) + 10d);
        double uz = fz * fz * fz * (fz * ((fz * 6d) - 15d) + 10d);
        double n000 = DotWithZMul(ix, iy, iz, fx, fy, fz, zMul);
        double n100 = DotWithZMul(ix + 1, iy, iz, fx - 1d, fy, fz, zMul);
        double n010 = DotWithZMul(ix, iy + 1, iz, fx, fy - 1d, fz, zMul);
        double n110 = DotWithZMul(ix + 1, iy + 1, iz, fx - 1d, fy - 1d, fz, zMul);
        double n001 = DotWithZMul(ix, iy, iz + 1, fx, fy, fz - 1d, zMul);
        double n101 = DotWithZMul(ix + 1, iy, iz + 1, fx - 1d, fy, fz - 1d, zMul);
        double n011 = DotWithZMul(ix, iy + 1, iz + 1, fx, fy - 1d, fz - 1d, zMul);
        double n111 = DotWithZMul(ix + 1, iy + 1, iz + 1, fx - 1d, fy - 1d, fz - 1d, zMul);
        double x00 = n000 + (ux * (n100 - n000));
        double x10 = n010 + (ux * (n110 - n010));
        double x01 = n001 + (ux * (n101 - n001));
        double x11 = n011 + (ux * (n111 - n011));
        double y0 = x00 + (uy * (x10 - x00));
        double y1 = x01 + (uy * (x11 - x01));
        return y0 + (uz * (y1 - y0));
    }

    private static double DotWithZMul(int x, int y, int z, double fx, double fy, double fz, uint zMul)
    {
        unchecked
        {
            uint h = (uint)((x * 374761393) + (y * 668265263) + (int)(z * (long)zMul));
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            int idx = (int)(h % 12u);
            int plane = idx >> 2;
            int k = idx & 3;
            double sa = 1d - (2d * (k & 1));
            double sb = 1d - (2d * ((k >> 1) & 1));
            const double g2 = 0.70710678118654752440d;
            double mx = 1d - (plane >> 1);
            double my = 1d - (plane & 1);
            double mz = (plane + 1) >> 1;
            double sy = sb + ((plane >> 1) * (sa - sb));
            return ((sa * g2 * mx) * fx) + ((sy * g2 * my) * fy) + ((sb * g2 * mz) * fz);
        }
    }
}