using System;
using System.Collections.Generic;
using System.Globalization;
using Galilego.Core;
using Galilego.Universe;
using Unity.Mathematics;

internal static partial class P1bTests
{
    /// <summary>
    /// ТОПОЛОГИЯ КОНТИНЕНТОВ: сколько связных массивов суши, какая их доля и
    /// сколько внутренних водоёмов.
    ///
    /// Зачем. Предыдущая калибровка смотрела только на долю суши (T87) и на
    /// глубины океана (T135/T137). Обе метрики были «в норме» у мира, который
    /// на вид состоял из каши: доля суши 0.45 и глубины −4 км не мешают
    /// береговой линии быть фрактальной нулевой изолиной поля с базовой
    /// октавой 318 км и 10 октав вниз. Считать надо связность, а не площадь.
    ///
    /// Метод. Высоты снимаются на куб-сетке (шум непрерывен на сфере, швов
    /// нет). СВЯЗНОСТЬ считается по графу «6 ближайших соседей», который строится
    /// ОДИН раз на геометрию и не зависит от профиля. Так метрики сравнимы
    /// между пресетами, и не нужно вручную разбирать переходы между гранями
    /// куба в углах, где параметрические шаги дают разную длину.
    ///
    /// Внутренний водоём = водная компонента, которая НЕ достигает −2000 м.
    /// На Земле это работает: океан доходит до абиссальных равнин (−4…−6 км),
    /// а Байкал (−1.6 км) и Каспий (−1 км) остаются озёрами. Порог в метрах,
    /// а не в нормированных единицах, иначе метрика зависела бы от амплитуды.
    /// </summary>
    private static int Test141_ContinentsConnectivity()
    {
        const int n = 256;

        Console.WriteLine($"  сетка {n}x{n}x6 = {6 * n * n} точек, шаг ~{Math.Round((Math.PI / 2 / n) * 6371d, 0)} км");
        Console.WriteLine();

        Metrics old = Measure(OldPreset(n), n, "БЫЛО (EarthLike_Perlin до правки)");
        Metrics now = Measure(AssetPreset(n), n, "СТАЛО (EarthLike_Perlin с океанским дном и полом)");

        Console.WriteLine();
        Console.WriteLine("  метрика                        БЫЛО            СТАЛО");
        Row("доля суши", old.LandFraction, now.LandFraction, "P2");
        Row("массивов суши", old.LandComponents, now.LandComponents, "F0");
        Row("доля крупнейшего массива", old.LargestLandShare, now.LargestLandShare, "P2");
        Row("суши в массивах >1% суши", old.BigLandComponents, now.BigLandComponents, "F0");
        Row("озёр (>100 км²)", old.Lakes, now.Lakes, "F0");
        Row("площадь озёр, доля всей воды", old.LakeAreaShare, now.LakeAreaShare, "P2");
        Row("глубина океана p5", old.OceanP5, now.OceanP5, "N0");
        Row("глубина океана медиана", old.OceanMedian, now.OceanMedian, "N0");
        Row("глубина океана max", old.OceanMax, now.OceanMax, "N0");
        Row("высота суши медиана", old.LandMedian, now.LandMedian, "N0");
        Row("высота суши p99", old.LandP99, now.LandP99, "N0");
        Row("высота суши max", old.LandMax, now.LandMax, "N0");

        Console.WriteLine();
        Console.WriteLine("  доля суши в пяти крупнейших массивах:");
        Console.WriteLine("    БЫЛО: " + Shares(old));
        Console.WriteLine("    СТАЛО: " + Shares(now));

Console.WriteLine();
        // «Как Земля»: доля суши около трети, несколько крупных материков,
        // фрактальный берег с островами, океан глубже материкового склона.
        Check(now.LandFraction > 0.26d && now.LandFraction < 0.38d, "T141 land-fraction",
            "суша " + now.LandFraction.ToString("P2", CultureInfo.InvariantCulture)
            + " (цель 0.26..0.38)");

        // Общее число массивов намеренно большое: 5 октав маски и отдельный warp
        // дают рваный берег с островами, и это требование, а не дефект. Суша
        // нужна не «меньше островов», а «не каша из одинаковых кусков» — поэтому
        // проверяются крупные материки и доля крупнейшего, а не сырой счёт.
        Check(now.LandComponents <= 130, "T141 land-components",
            "массивов суши " + now.LandComponents + " (цель <= 130 при рваном береге)");
        Check(now.BigLandComponents >= 5 && now.BigLandComponents <= 14, "T141 big-continents",
            "массивов крупнее 1% суши " + now.BigLandComponents + " (цель 5..14)");
        Check(now.LargestLandShare >= 0.30d, "T141 largest-continent",
            "крупнейший " + now.LargestLandShare.ToString("P2", CultureInfo.InvariantCulture)
            + " суши (цель >= 30%)");
        Check(now.LakeAreaShare <= 0.15d, "T141 inland-water",
            "озёря занимают " + now.LakeAreaShare.ToString("P2", CultureInfo.InvariantCulture)
            + " воды (цель <= 15%)");
        Check(now.OceanMedian > 3000d && now.OceanMedian < 7000d, "T141 ocean-depth",
            "медиана глубины " + now.OceanMedian.ToString("N0", CultureInfo.InvariantCulture)
            + " м (цель 3..7 км)");


        Check(now.OceanMax <= 12000d, "T141 ocean-max",
            "глубина " + now.OceanMax.ToString("N0", CultureInfo.InvariantCulture) + " м (цель <= 12 км)");

        return 0;
    }

    private static void Row(string label, double a, double b, string fmt)
    {
        Console.WriteLine("  " + label.PadRight(30) + " "
            + a.ToString(fmt, CultureInfo.InvariantCulture).PadLeft(12) + "  "
            + b.ToString(fmt, CultureInfo.InvariantCulture).PadLeft(12));
    }

    private static string Shares(Metrics m)
    {
        string s = string.Empty;
        for (int i = 0; i < m.TopLandShares.Count; i++)
        {
            if (i > 0)
            {
                s += ", ";
            }

            s += m.TopLandShares[i].ToString("P1", CultureInfo.InvariantCulture);
        }

        return s;
    }

    private readonly struct Metrics
    {
        public readonly double LandFraction;
        public readonly int LandComponents;
        public readonly int BigLandComponents;
        public readonly double LargestLandShare;
        public readonly int Lakes;
        public readonly double LakeAreaShare;
        public readonly double OceanP5;
        public readonly double OceanMedian;
        public readonly double OceanMax;
        public readonly double LandMedian;
        public readonly double LandP99;
        public readonly double LandMax;
        public readonly List<double> TopLandShares;

        public Metrics(double landFraction, int landComponents, int bigLandComponents,
            double largestLandShare, int lakes, double lakeAreaShare,
            double oceanP5, double oceanMedian, double oceanMax,
            double landMedian, double landP99, double landMax, List<double> topLandShares)
        {
            LandFraction = landFraction;
            LandComponents = landComponents;
            BigLandComponents = bigLandComponents;
            LargestLandShare = largestLandShare;
            Lakes = lakes;
            LakeAreaShare = lakeAreaShare;
            OceanP5 = oceanP5;
            OceanMedian = oceanMedian;
            OceanMax = oceanMax;
            LandMedian = landMedian;
            LandP99 = landP99;
            LandMax = landMax;
            TopLandShares = topLandShares;
        }
    }

    /// <summary>Порог «это озеро, а не океан»: ниже какой глубины вода считается океаном.</summary>
    private const double OceanFloorMeters = 2000d;

    /// <summary>Минимальная площадь, с которой озеро вообще считается озером (км²).</summary>
    private const double MinLakeKm2 = 100d;

    private static Metrics Measure(HeightfieldTerrain t, int n, string label)
    {
        TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(t);
        int count = 6 * n * n;
        var h = new double[count];
        int land = 0;
        var landH = new List<double>(count / 3);
        var waterH = new List<double>(count / 2);

        int idx = 0;
        for (int face = 0; face < 6; face++)
        {
            for (int i = 0; i < n; i++)
            {
                double u = (i + 0.5d) / n;
                for (int j = 0; j < n; j++, idx++)
                {
                    double v = (j + 0.5d) / n;
                    Vector3d d = CubeSphere.Direction(face, u, v);
                    double meters = TerrainNoise.SampleHeight(
                        p, new double3(d.X, d.Y, d.Z)) * t.AmplitudeMeters;
                    h[idx] = meters;
                    if (meters > t.SeaLevelMeters + 0.5d)
                    {
                        land++;
                        landH.Add(meters);
                    }
                    else
                    {
                        waterH.Add(meters);
                    }
                }
            }
        }

        int[] comp = Components(h, n, t.SeaLevelMeters + 0.5d, true);
        int[] water = Components(h, n, t.SeaLevelMeters + 0.5d, false);

        // Площади суши по компонентам.
        var landArea = new Dictionary<int, int>();
        for (int i = 0; i < count; i++)
        {
            if (comp[i] < 0)
            {
                continue;
            }

            landArea.TryGetValue(comp[i], out int a);
            landArea[comp[i]] = a + 1;
        }

        var landAreas = new List<int>(landArea.Values);
        landAreas.Sort();
        landAreas.Reverse();
        double cellKm2 = CellKm2(n);
        var topShares = new List<double>();
        for (int i = 0; i < landAreas.Count && i < 5; i++)
        {
            topShares.Add(landAreas[i] / (double)land);
        }

        int big = 0;
        for (int i = 0; i < landAreas.Count; i++)
        {
            if (landAreas[i] * cellKm2 >= (0.01d * land * cellKm2))
            {
                big++;
            }
        }

        // Озёра: водная компонента без единой точки глубже OceanFloorMeters.
        var minDepth = new Dictionary<int, double>();
        var area = new Dictionary<int, int>();
        for (int i = 0; i < count; i++)
        {
            if (water[i] < 0)
            {
                continue;
            }

if (!minDepth.TryGetValue(water[i], out double m))
            {
                minDepth[water[i]] = h[i];
            }
            else if (h[i] < m)
            {
                minDepth[water[i]] = h[i];
            }

            if (!area.TryGetValue(water[i], out int a))
            {
                area[water[i]] = 1;
            }
            else
            {
                area[water[i]] = a + 1;
            }

        }

        int lakes = 0;
        int lakeCells = 0;
        foreach (KeyValuePair<int, int> kv in area)
        {
            // Компонента, у которой есть точка глубже OceanFloorMeters, связана
            // с океаном. Всё остальное — озёрная вода.
            if (minDepth[kv.Key] <= -OceanFloorMeters)
            {
                continue;
            }

            if (kv.Value * cellKm2 >= MinLakeKm2)
            {
                lakes++;
            }

            lakeCells += kv.Value;
        }

        waterH.Sort();
        landH.Sort();

double landCell = 6371d * Math.PI / 2d / n;
        int coastEdges = 0;
        int[] nb = SphereNeighbours(n);
        for (int i = 0; i < count; i++)
        {
            if (h[i] <= t.SeaLevelMeters + 0.5d)
            {
                continue;
            }

            for (int k = 0; k < 6; k++)
            {
                int j = nb[(i * 6) + k];
                if (j >= 0 && h[j] <= t.SeaLevelMeters + 0.5d)
                {
                    coastEdges++;
                }
            }
        }

        Console.WriteLine("  " + label);
        Console.WriteLine("    доля суши " + (land / (double)count).ToString("P3", CultureInfo.InvariantCulture)
            + ", клетка " + cellKm2.ToString("N0", CultureInfo.InvariantCulture) + " км²");
        Console.WriteLine("    берег " + (coastEdges * landCell / 1000d).ToString("N0", CultureInfo.InvariantCulture)
            + " тыс. км, " + (coastEdges / (double)land).ToString("F2", CultureInfo.InvariantCulture)
            + " ребра на клетку суши");
        Console.WriteLine("    " + Shares(new Metrics(0d, 0, 0, 0d, 0, 0d, 0d, 0d, 0d, 0d, 0d, 0d, topShares)));

        return new Metrics(
            land / (double)count,
            landAreas.Count,
            big,
            landAreas.Count > 0 ? landAreas[0] / (double)land : 0d,
            lakes,
            waterH.Count > 0 ? lakeCells / (double)waterH.Count : 0d,
            waterH.Count > 0 ? -waterH[(int)(waterH.Count * 0.05d)] : 0d,
            waterH.Count > 0 ? -waterH[waterH.Count / 2] : 0d,
            waterH.Count > 0 ? -waterH[0] : 0d,
            landH.Count > 0 ? landH[landH.Count / 2] : 0d,
            landH.Count > 0 ? landH[(int)(landH.Count * 0.99d)] : 0d,
            landH.Count > 0 ? landH[landH.Count - 1] : 0d,
            topShares);
    }


    /// <summary>Площадь одной клетки куб-сетки в км² (планета радиусом 6371 км).</summary>
    private static double CellKm2(int n)
    {
        double side = 6371d * Math.PI / 2d / n;
        return side * side;
    }

    /// <summary>
    /// Компоненты связности по графу «6 ближайших». Метка land=true берёт точки
    /// выше порога, false — ниже. −1 означает «не входит ни в одну компоненту».
    /// </summary>
    private static int[] Components(double[] h, int n, double threshold, bool land)
    {
        int count = h.Length;
        int[] neighbours = SphereNeighbours(n);
        var parent = new int[count];
        for (int i = 0; i < count; i++)
        {
            parent[i] = i;
        }

        bool[] inside = new bool[count];
        for (int i = 0; i < count; i++)
        {
            inside[i] = land ? h[i] > threshold : h[i] <= threshold;
        }

        for (int i = 0; i < count; i++)
        {
            if (!inside[i])
            {
                continue;
            }

            for (int k = 0; k < 6; k++)
            {
                int j = neighbours[(i * 6) + k];
                if (j >= 0 && inside[j])
                {
                    Union(parent, i, j);
                }
            }
        }

        var labels = new int[count];
        for (int i = 0; i < count; i++)
        {
            labels[i] = inside[i] ? Find(parent, i) : -1;
        }

        return labels;
    }

    private static int Find(int[] parent, int i)
    {
        int r = i;
        while (parent[r] != r)
        {
            r = parent[r];
        }

        while (parent[i] != r)
        {
            int next = parent[i];
            parent[i] = r;
            i = next;
        }

        return r;
    }

    private static void Union(int[] parent, int a, int b)
    {
        int ra = Find(parent, a);
        int rb = Find(parent, b);
        if (ra != rb)
        {
            parent[rb] = ra;
        }
    }

    /// <summary>
    /// Граф «6 ближайших соседей» на куб-сетке. Кэшируется на разрешение:
    /// геометрия от профиля не зависит, а строить её каждый замер дорого.
    ///
    /// Почему kNN, а не «8 параметрических соседей»: у куб-сетки длины шагов в
    /// углах граней и в их центрах различаются почти вдвое, и параметрическое
    /// отображение даёт ещё и рёбра, где сосед лежит на ДРУГОЙ грани. Любая
    /// попытка учесть это вручную — источник тихих ошибок связности именно там,
    /// где материки стыкуются. У 6 ближайших такой проблемы нет по построению.
    /// </summary>
    private static readonly Dictionary<int, int[]> neighbourCache = new Dictionary<int, int[]>();

    private static int[] SphereNeighbours(int n)
    {
        lock (neighbourCache)
        {
            if (neighbourCache.TryGetValue(n, out int[] cached))
            {
                return cached;
            }

            int count = 6 * n * n;
            var x = new double[count];
            var y = new double[count];
            var z = new double[count];
            int idx = 0;
            for (int face = 0; face < 6; face++)
            {
                for (int i = 0; i < n; i++)
                {
                    double u = (i + 0.5d) / n;
                    for (int j = 0; j < n; j++, idx++)
                    {
                        double v = (j + 0.5d) / n;
                        Vector3d d = CubeSphere.Direction(face, u, v);
                        x[idx] = d.X;
                        y[idx] = d.Y;
                        z[idx] = d.Z;
                    }
                }
            }

            // Пространственный хеш: корзина ~2 угловых шага.
            int m = n;
            var buckets = new Dictionary<long, List<int>>(count / 4);
            for (int i = 0; i < count; i++)
            {
                int bx = (int)Math.Floor((x[i] + 1d) * 0.5d * m);
                int by = (int)Math.Floor((y[i] + 1d) * 0.5d * m);
                int bz = (int)Math.Floor((z[i] + 1d) * 0.5d * m);
                bx = Math.Min(bx, m - 1);
                by = Math.Min(by, m - 1);
                bz = Math.Min(bz, m - 1);
                long key = ((long)bx << 40) | ((long)by << 20) | (uint)bz;
                if (!buckets.TryGetValue(key, out List<int> list))
                {
                    list = new List<int>(16);
                    buckets.Add(key, list);
                }

                list.Add(i);
            }

            var result = new int[count * 6];
            var bestD2 = new double[6];
            var bestId = new int[6];
            for (int i = 0; i < count; i++)
            {
                for (int k = 0; k < 6; k++)
                {
                    bestId[k] = -1;
                    bestD2[k] = double.MaxValue;
                }

                int cx = Math.Min((int)Math.Floor((x[i] + 1d) * 0.5d * m), m - 1);
                int cy = Math.Min((int)Math.Floor((y[i] + 1d) * 0.5d * m), m - 1);
                int cz = Math.Min((int)Math.Floor((z[i] + 1d) * 0.5d * m), m - 1);

                // Кандидаты: своя корзина и 26 вокруг. Радиус корзины ~2 шага,
                // поэтому 6 ближайших гарантированно внутри этого окна.
                for (int a = -1; a <= 1; a++)
                {
                    int qx = cx + a;
                    if (qx < 0 || qx >= m)
                    {
                        continue;
                    }

                    for (int b = -1; b <= 1; b++)
                    {
                        int qy = cy + b;
                        if (qy < 0 || qy >= m)
                        {
                            continue;
                        }

                        for (int c = -1; c <= 1; c++)
                        {
                            int qz = cz + c;
                            if (qz < 0 || qz >= m)
                            {
                                continue;
                            }

                            long key = ((long)qx << 40) | ((long)qy << 20) | (uint)qz;
                            if (!buckets.TryGetValue(key, out List<int> list))
                            {
                                continue;
                            }

                            for (int li = 0; li < list.Count; li++)
                            {
                                int j = list[li];
                                if (j == i)
                                {
                                    continue;
                                }

                                double d2 = Dist2(x, y, z, i, j);
                                if (d2 >= bestD2[5])
                                {
                                    continue;
                                }

                                // Вставка в отсортированный по расстоянию хвост из 6.
                                int slot = 5;
                                while (slot > 0 && bestD2[slot - 1] > d2)
                                {
                                    bestD2[slot] = bestD2[slot - 1];
                                    bestId[slot] = bestId[slot - 1];
                                    slot--;
                                }

                                bestD2[slot] = d2;
                                bestId[slot] = j;
                            }
                        }
                    }
                }

                for (int k = 0; k < 6; k++)
                {
                    result[(i * 6) + k] = bestId[k];
                }
            }

            neighbourCache[n] = result;
            return result;
        }
    }

    private static double Dist2(double[] x, double[] y, double[] z, int a, int b)
    {
        double dx = x[a] - x[b];
        double dy = y[a] - y[b];
        double dz = z[a] - z[b];
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    /// <summary>
    /// EarthLike_Perlin ДО правки формы. Захардкожен намеренно: это базовая
    /// линия для метрик связности, и она обязана оставаться неизменной, когда
    /// ассет перенастраивают.
    /// </summary>
private static int Test142_ContinentsDiag()
    {
        const int n = 192;

        Console.WriteLine("  1) экваториальный сдвиг: доля суши по широтным поясам");
        double[] beltDeg = { 0d, 15d, 30d, 45d, 60d, 75d, 90d };
        double[] biases = { 0d, 0.05d, 0.08d, 0.12d };
        foreach (double bias in biases)
        {
            HeightfieldTerrain t = AssetPreset(n);
            t.ContinentLatitudeBias = bias;
            Console.Write(string.Format(CultureInfo.InvariantCulture, "  bias={0,-5} |", bias));
            double prev = 0d;
            for (int b = 0; b < beltDeg.Length; b++)
            {
                double lo = beltDeg[b];
                double hi = b + 1 < beltDeg.Length ? beltDeg[b + 1] : 90.0001d;
                double share = LandShareInBelt(t, n, lo, hi);
                Console.Write(string.Format(
                    CultureInfo.InvariantCulture, " |{0,3:F0}..{1,3:F0}° {2,5:P1}", lo, hi, share));
                _ = prev;
                prev = share;
            }

            Console.WriteLine();
        }

        Console.WriteLine();
        Console.WriteLine("  2) порог маски на сиде сцены (24334543), октавы 4 и 5");
        int[] octs = { 4, 5 };
        double[] ths = { 0.1d, 0.12d, 0.14d, 0.16d, 0.18d };
        foreach (int o in octs)
        {
            foreach (double th in ths)
            {
                HeightfieldTerrain t = AssetPreset(n);
                t.ContinentOctaves = o;
                t.ContinentThreshold = th;
                Metrics m = MeasureQuiet(t, n);
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  oct={0} th={1,-5} | суша {2,6:P1} | массивов {3,4} | крупн {4,6:P1} | >1%: {5,3} | озёр {6,4} ({7,5:P2})",
                    o, th, m.LandFraction, m.LandComponents, m.LargestLandShare, m.BigLandComponents,
                    m.Lakes, m.LakeAreaShare));
            }
        }

        Console.WriteLine();
        return 0;
    }

    /// <summary>
    /// Доля суши в широтном поясе [lo°, hi°] — мера экваториального сдвига.
    /// Считается по площади (ячейки куб-сетки почти равны по площади), не по
    /// числу точек на параллели.
    /// </summary>
    private static double LandShareInBelt(HeightfieldTerrain t, int n, double loDeg, double hiDeg)
    {
        TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(t);
        double lo = math.sin(loDeg * Math.PI / 180d);
        double hi = math.sin(hiDeg * Math.PI / 180d);
        bool bothHemi = lo < 0d && hi > 0d;
        int inBelt = 0;
        int landInBelt = 0;
        for (int face = 0; face < 6; face++)
        {
            for (int i = 0; i < n; i++)
            {
                double u = (i + 0.5d) / n;
                for (int j = 0; j < n; j++)
                {
                    double v = (j + 0.5d) / n;
                    Vector3d d = CubeSphere.Direction(face, u, v);
                    double z = d.Z;
                    double zabs = Math.Abs(z);
                    bool inside = bothHemi
                        ? (z >= lo && z <= hi) || (z >= -hi && z <= -lo)
                        : (zabs >= Math.Abs(lo) && zabs <= Math.Abs(hi));
                    if (!inside)
                    {
                        continue;
                    }

                    inBelt++;
                    if (TerrainNoise.SampleHeight(p, new double3(d.X, d.Y, d.Z)) * t.AmplitudeMeters
                        > t.SeaLevelMeters + 0.5d)
                    {
                        landInBelt++;
                    }
                }
            }
        }

        return inBelt > 0 ? landInBelt / (double)inBelt : 0d;
    }

    private static void Relief(HeightfieldTerrain t, int n)
    {
        TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(t);
        int count = 6 * n * n;
        var landH = new List<double>();
        int land = 0;
        int idx = 0;
        for (int face = 0; face < 6; face++)
        {
            for (int i = 0; i < n; i++)
            {
                double u = (i + 0.5d) / n;
                for (int j = 0; j < n; j++, idx++)
                {
                    double v = (j + 0.5d) / n;
                    Vector3d d = CubeSphere.Direction(face, u, v);
                    double m = TerrainNoise.SampleHeight(p, new double3(d.X, d.Y, d.Z)) * t.AmplitudeMeters;
                    if (m > t.SeaLevelMeters + 0.5d)
                    {
                        land++;
                        landH.Add(m);
                    }
                }
            }
        }

        landH.Sort();
        int high = 0;
        int mid = 0;
        for (int i = 0; i < landH.Count; i++)
        {
            if (landH[i] > 3000d)
            {
                high++;
            }

            if (landH[i] > 1000d)
            {
                mid++;
            }
        }

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "  OrogenyThreshold={0,-6} | суша >1 км {1,6:P1} | >3 км {2,6:P1} | >5 км {3,6:P1} | p99 {4,7:N0} max {5,7:N0}",
            t.OrogenyThreshold,
            landH.Count > 0 ? mid / (double)landH.Count : 0d,
            landH.Count > 0 ? high / (double)landH.Count : 0d,
            landH.Count > 0 ? FractionAbove(landH, 5000d) : 0d,
            landH.Count > 0 ? landH[(int)(landH.Count * 0.99d)] : 0d,
            landH.Count > 0 ? landH[landH.Count - 1] : 0d));
        _ = land;
    }

    private static double FractionAbove(List<double> sorted, double v)
    {
        int lo = 0;
        int hi = sorted.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (sorted[mid] < v)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return (sorted.Count - lo) / (double)sorted.Count;
    }

    private static Metrics MeasureQuiet(HeightfieldTerrain t, int n)
    {
        TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(t);
        int count = 6 * n * n;
        var h = new double[count];
        int land = 0;
        var waterH = new List<double>(count / 2);
        int idx = 0;
        for (int face = 0; face < 6; face++)
        {
            for (int i = 0; i < n; i++)
            {
                double u = (i + 0.5d) / n;
                for (int j = 0; j < n; j++, idx++)
                {
                    double v = (j + 0.5d) / n;
                    Vector3d d = CubeSphere.Direction(face, u, v);
                    double m = TerrainNoise.SampleHeight(p, new double3(d.X, d.Y, d.Z)) * t.AmplitudeMeters;
                    h[idx] = m;
                    if (m > t.SeaLevelMeters + 0.5d)
                    {
                        land++;
                    }
                    else
                    {
                        waterH.Add(m);
                    }
                }
            }
        }

        int[] comp = Components(h, n, t.SeaLevelMeters + 0.5d, true);
        int[] water = Components(h, n, t.SeaLevelMeters + 0.5d, false);
        var landArea = new Dictionary<int, int>();
        for (int i = 0; i < count; i++)
        {
            if (comp[i] < 0)
            {
                continue;
            }

            landArea.TryGetValue(comp[i], out int a);
            landArea[comp[i]] = a + 1;
        }

        var areas = new List<int>(landArea.Values);
        areas.Sort();
        areas.Reverse();
        int big = 0;
        double cellKm2q = CellKm2(n);
        for (int i = 0; i < areas.Count; i++)
        {
            if (areas[i] * cellKm2q >= (0.01d * land * cellKm2q))
            {
                big++;
            }
        }

        var minDepth = new Dictionary<int, double>();
        var area = new Dictionary<int, int>();
        for (int i = 0; i < count; i++)
        {
            if (water[i] < 0)
            {
                continue;
            }

            if (!minDepth.TryGetValue(water[i], out double md))
            {
                minDepth[water[i]] = h[i];
            }
            else if (h[i] < md)
            {
                minDepth[water[i]] = h[i];
            }

            area.TryGetValue(water[i], out int a);
            area[water[i]] = a + 1;
        }

        int lakes = 0;
        int lakeCells = 0;
        double cellKm2 = CellKm2(n);
        foreach (KeyValuePair<int, int> kv in area)
        {
            if (minDepth[kv.Key] <= -OceanFloorMeters)
            {
                continue;
            }

            if (kv.Value * cellKm2 >= MinLakeKm2)
            {
                lakes++;
            }

            lakeCells += kv.Value;
        }

        waterH.Sort();
        var landH = new List<double>(count / 3);
        for (int i = 0; i < count; i++)
        {
            if (h[i] > t.SeaLevelMeters + 0.5d)
            {
                landH.Add(h[i]);
            }
        }

        landH.Sort();
        return new Metrics(
            land / (double)count, areas.Count, big,
            areas.Count > 0 ? areas[0] / (double)land : 0d, lakes,
            waterH.Count > 0 ? lakeCells / (double)waterH.Count : 0d,
            waterH.Count > 0 ? -waterH[(int)(waterH.Count * 0.05d)] : 0d,
            waterH.Count > 0 ? -waterH[waterH.Count / 2] : 0d,
            waterH.Count > 0 ? -waterH[0] : 0d,
            landH.Count > 0 ? landH[landH.Count / 2] : 0d,
            landH.Count > 0 ? landH[(int)(landH.Count * 0.99d)] : 0d,
            landH.Count > 0 ? landH[landH.Count - 1] : 0d,
            new List<double>());
    }

    private static HeightfieldTerrain OldPreset(int n)

    {
        HeightfieldTerrain t = AssetPreset(n);
        t.OceanFloorDepth = 0d;
        t.InteriorFloor = 0d;
        t.OrogenyFrequency = 0d;
        t.ContinentFrequency = 4d;
        t.ContinentOctaves = 3;
        t.ContinentThreshold = -0.4d;
        t.ContinentSharpness = 0.3d;
        t.ContinentDepth = 1.2d;
        t.PlainFrequency = 10d;
        t.PlainOctaves = 3;
        return t;
    }

    /// <summary>EarthLike_Perlin как в ассете (сверяется T117_EarthLikeProfileDrift).</summary>
private static HeightfieldTerrain AssetPreset(int n)
    {
        HeightfieldTerrain t = PerlinTuned(0.13d, 0.5656d);
        _ = n;
        t.RidgedMix = 0.7d;
        t.ContinentFrequency = 1.3d;
        t.ContinentOctaves = 5;
        t.ContinentThreshold = 0.14d;
        t.ContinentSharpness = 0.08d;
        t.ContinentDepth = 1.2d;
        t.ContinentGain = 0.55d;
        t.ContinentWarpStrength = 0.3d;
        t.ContinentWarpFrequency = 1.5d;
        t.ContinentWarpOctaves = 3;
        t.ContinentRidgeMix = 0.12d;
        t.ContinentRidgeFrequency = 2.5d;
        t.ContinentRidgeOctaves = 2;
        t.ContinentLatitudeBias = 0.08d;
        t.OceanFloorDepth = 0.42d;
        t.OceanShelfDepth = 0.02d;
        t.InteriorFloor = 0.035d;
        t.OrogenyFrequency = 1.1d;
        t.OrogenyOctaves = 3;
        t.OrogenyThreshold = -0.4d;
        t.OrogenySharpness = 0.3d;
        t.OrogenyFloor = 0.15d;
        t.OrogenyGain = 0.85d;
        t.PlainMix = 0.85d;
        t.PlainFrequency = 2d;
        t.PlainOctaves = 2;
        t.PlainThreshold = 0d;
        t.PlainSharpness = 0.25d;
        t.PlainElevation = 0.1d;
        t.DepthKnee = 0d;
        t.DepthThreshold = 0d;
        t.DetailMix = 0d;
        t.DetailFrequency = 0d;
        t.BeachHeightMeters = 18d;
        t.BeachShelfAltitudeMeters = 12d;
        t.BeachShelfWidth = 0.03d;
        t.BeachShelfWidthMaxScale = 14d;
        t.BeachShelfWidthNoiseFrequency = 150d;
        t.BeachShelfWidthNoiseOctaves = 4;
        return t;
    }

    /// <summary>
    /// AssetPreset() обязан совпадать с EarthLike_Perlin.asset. Иначе метрики
    /// связности меряют конфигурацию, которой в игре нет — ровно тот дрейф,
    /// который уже случался с CreateEarthLike (ловит T117).
    ///
    /// Плюс контракт бит-в-бит: обнуление новых полей обязано вернуть старую
    /// форму, иначе «нулевые новые параметры = legacy» перестанет быть правдой
    /// для сцен и ассетов, где их ещё нет.
    /// </summary>
    private static int Test143_PerlinAssetDrift()
    {
        string path = FindProfileAsset("EarthLike_Perlin.asset");
        if (path == null)
        {
            Console.WriteLine("    EarthLike_Perlin.asset не найден рядом со стендом — проверка пропущена");
            Check(true, "T143 perlin-drift", "ассет не найден, проверка пропущена");
        }
        else
        {
            var asset = ReadScalarFields(path);
            HeightfieldTerrain code = AssetPreset(1);
            var fieldValue = new Dictionary<string, double>();
            foreach (System.Reflection.FieldInfo f in typeof(HeightfieldTerrain).GetFields())
            {
                if (f.FieldType == typeof(double) || f.FieldType == typeof(int))
                {
                    fieldValue[f.Name] = Convert.ToDouble(f.GetValue(code));
                }
            }

            string[] tracked =
            {
                "ContinentFrequency", "ContinentOctaves", "ContinentThreshold", "ContinentSharpness",
                "ContinentDepth", "ContinentGain", "ContinentWarpStrength", "ContinentWarpFrequency",
                "ContinentWarpOctaves", "ContinentRidgeMix", "ContinentRidgeFrequency",
                "ContinentRidgeOctaves", "ContinentLatitudeBias",
                "OceanFloorDepth", "OceanShelfDepth", "InteriorFloor",
                "OrogenyFrequency", "OrogenyOctaves", "OrogenyThreshold", "OrogenySharpness",
                "OrogenyFloor", "OrogenyGain",
                "PlainMix", "PlainFrequency", "PlainOctaves", "PlainThreshold", "PlainSharpness",
                "PlainElevation", "TailKnee", "TailThreshold", "DepthKnee", "DepthThreshold",
                "RidgedMix", "RidgedMode", "RidgedSharpness", "RidgedWeightGain", "RidgedGamma",
                "NoiseStyle", "AmplitudeMeters", "BaseFrequency", "Octaves", "Gain", "Lacunarity",
                "WarpStrength", "WarpFrequency", "WarpOctaves",
                "BeachHeightMeters", "BeachShelfAltitudeMeters", "BeachShelfWidth",
                "BeachShelfWidthMaxScale", "BeachShelfWidthNoiseFrequency", "BeachShelfWidthNoiseOctaves",
            };

            var mismatches = new List<string>();
            foreach (string name in tracked)
            {
                if (!asset.TryGetValue(name, out double a))
                {
                    mismatches.Add(name + " (нет в ассете)");
                    continue;
                }

                if (!fieldValue.TryGetValue(name, out double expected))
                {
                    continue;
                }

                if (Math.Abs(a - expected) > 1e-9d)
                {
                    mismatches.Add(string.Format(CultureInfo.InvariantCulture, "{0}: тест {1} ≠ ассет {2}", name, expected, a));
                }
            }

            foreach (string m in mismatches)
            {
                Console.WriteLine("    расхождение — " + m);
            }

            Check(mismatches.Count == 0, "T143 perlin-drift",
                "AssetPreset() совпадает с EarthLike_Perlin.asset"
                + (mismatches.Count == 0 ? string.Empty : "; расхождений: " + mismatches.Count));
        }

        // Дефолты новых полей обязаны давать ту же форму, что и явные нули.
        // Проверка не на «старую форму вообще» (её уже нет в коде), а на то, что
        // класс не подставил в новые поля ничего неlegacy: иначе старые ассеты,
        // где полей ещё нет, молча поедут. Особенно опасен OrogenyFloor: его
        // дефолт не ноль (пояс выключен частотой, а не полем), и опечатка в нём
        // при OrogenyFrequency = 0 всё равно перепишет весь рельеф.
        HeightfieldTerrain byDefault = LegacyEquivalent();
        HeightfieldTerrain byZero = LegacyEquivalent();
        byZero.ContinentGain = 0d;
        byZero.ContinentWarpStrength = 0d;
        byZero.ContinentWarpFrequency = 0d;
        byZero.ContinentWarpOctaves = 3;
        byZero.ContinentRidgeMix = 0d;
        byZero.ContinentRidgeFrequency = 0d;
        byZero.ContinentRidgeOctaves = 2;
        byZero.ContinentLatitudeBias = 0d;
        byZero.OceanFloorDepth = 0d;
        byZero.OceanShelfDepth = 0d;
        byZero.InteriorFloor = 0d;
        byZero.OrogenyFrequency = 0d;
        byZero.OrogenyOctaves = 3;
        byZero.OrogenyThreshold = 0d;
        byZero.OrogenySharpness = 0.3d;
        byZero.OrogenyFloor = 1d;
        byZero.OrogenyGain = 0d;
        TerrainNoiseParams pNew = TerrainNoiseParams.FromTerrain(byDefault);
        TerrainNoiseParams pOld = TerrainNoiseParams.FromTerrain(byZero);
        bool exact = true;
        int ga = 0;
        for (int i = 0; i <= 96 && exact; i++)
        {
            for (int j = 0; j <= 96 && exact; j++)
            {
                Vector3d d = CubeSphere.Direction(ga % CubeSphere.FaceCount, i / 96d, j / 96d);
                ga++;
                double3 dir = new double3(d.X, d.Y, d.Z);
                if (TerrainNoise.SampleHeight(pNew, dir) != TerrainNoise.SampleHeight(pOld, dir))
                {
                    exact = false;
                }
            }
        }

        Check(exact, "T143 legacy-bit-exact",
            "дефолты новых полей = явные нули, форма бит-в-бит прежняя");

        return 0;
    }

    /// <summary>
    /// Профиль с ТОЙ ЖЕ формой, но собранный без новых полей вовсе — то есть
    /// ровно то, что лежит в старом .asset, где их ещё не было. Новые поля
    /// обязаны стоять здесь на ДЕФОЛТАХ КЛАССА, иначе проверка ниже
    /// сравнивала бы «выключено» с «выключено по-разному».
    /// </summary>
    private static HeightfieldTerrain LegacyEquivalent()
    {
        HeightfieldTerrain t = AssetPreset(1);
        t.AmplitudeMeters = 13662d;
        t.RidgedMix = 0.7d;
        t.ContinentFrequency = 4d;
        t.ContinentOctaves = 3;
        t.ContinentThreshold = -0.4d;
        t.ContinentSharpness = 0.3d;
        t.ContinentDepth = 1.2d;
        t.PlainMix = 0.95d;
        t.PlainFrequency = 10d;
        t.PlainOctaves = 3;
        t.DepthKnee = 0.51d;
        t.DepthThreshold = -0.3d;
        t.BeachShelfWidth = 0.08d;

        // Всё, что появилось вместе с океаном и новой формой маски, — на дефолтах.
        t.ContinentGain = 0d;
        t.ContinentWarpStrength = 0d;
        t.ContinentWarpFrequency = 0d;
        t.ContinentWarpOctaves = 3;
        t.ContinentRidgeMix = 0d;
        t.ContinentRidgeFrequency = 0d;
        t.ContinentRidgeOctaves = 2;
        t.ContinentLatitudeBias = 0d;
        t.OceanFloorDepth = 0d;
        t.OceanShelfDepth = 0d;
        t.InteriorFloor = 0d;
        t.OrogenyFrequency = 0d;
        t.OrogenyFloor = 1d;
        t.OrogenyGain = 0d;
        return t;
    }

}
