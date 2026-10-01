using System;
using System.Globalization;
using Galilego.Universe;
using Unity.Mathematics;

/// <summary>
/// Разбор набора градиентов: матрица вторых моментов Σ g gᵀ и замыкатость набора
/// относительно перестановок осей. Нужен ДО выбора набора, а не после — по
/// замеру видно, что «сумма векторов нулевая» не гарантирует изотропности.
///
/// Направления берутся из НАСТОЯЩЕГО хеша решётки по реальным координатам
/// узлов: подставлять индексы 0..N нельзя, схема выбора опирается на старшие
/// биты и на малых h вырождается в нулевой индекс.
///
/// Печатает оба набора, проверок не содержит: это разбор, а не приёмка.
/// Приёмку симметрии делает T112.
/// </summary>
internal static partial class P1bTests
{
    private static int Test112b_GradientSetAnalysis()
    {
        // 61^3 узлов — достаточно, чтобы собрать статистику по всем плоскостям.
        const int n = 60;
        Report("было: 8 диагоналей + 4 ребра xy + 4 ребра xz", (x, y, z) => GradSetOld(LatticeHash(x, y, z)), n);
        Report("стало: 12 рёбер куба (h % 12)", GradSetNew, n);
        return 0;
    }

    /// <summary>Исторический хеш СТАРОГО набора из 8 диагоналей. Копия намеренная:
    /// этот тест показывает, что старый набор давал разброс 37.5%, поэтому хеш
    /// здесь заморожен на старом множителе z и НЕ должен совпадать с
    /// TerrainNoise.PerlinLatticeHash. Синхронизировать с продакшеном нельзя -
    /// это уничтожит то, что тест измеряет.</summary>
    private static int LatticeHash(int x, int y, int z)
    {
        unchecked
        {
            int h = (x * 374761393) + (y * 668265263) + (z * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return h;
        }
    }

    /// <summary>Направление по хешу узла — прежняя схема из 5 младших бит (отброшена).</summary>
    private static double3 GradSetOld(int h)
    {
        int g = h & 31;
        double sx = 1d - (2d * (g & 1));
        double sy = 1d - (2d * ((g >> 1) & 1));
        double sz = 1d - (2d * ((g >> 2) & 1));
        double diag = 1d - ((g >> 3) & 1);
        double edge = 1d - diag;
        double planeXz = (g >> 4) & 1;
        double takeY = diag + (edge * (1d - planeXz));
        double takeZ = diag + (edge * planeXz);
        double scale = (diag * 0.57735026918962576451d) + (edge * 0.70710678118654752440d);
        return new double3(sx * scale, sy * scale * takeY, sz * scale * takeZ);
    }

    /// <summary>12 рёбер куба — нынешняя схема, зовёт рабочий код.</summary>
    private static double3 GradSetNew(int x, int y, int z)
    {
        return TerrainNoise.LatticeGradientFast(x, y, z);
    }

    private static void Report(string label, Func<int, int, int, double3> pick, int side)
    {
        double xx = 0d, yy = 0d, zz = 0d, xy = 0d, xz = 0d, yz = 0d;
        double sx = 0d, sy = 0d, sz = 0d;
        int count = 0;
        var seen = new System.Collections.Generic.HashSet<long>();

        for (int x = -side; x <= side; x++)
        {
            for (int y = -side; y <= side; y++)
            {
                for (int z = -side; z <= side; z++)
                {
                    double3 g = pick(x, y, z);
                    xx += g.x * g.x;
                    yy += g.y * g.y;
                    zz += g.z * g.z;
                    xy += g.x * g.y;
                    xz += g.x * g.z;
                    yz += g.y * g.z;
                    sx += g.x;
                    sy += g.y;
                    sz += g.z;
                    count++;
                    seen.Add((long)(Math.Round(g.x * 1e6) * 100000000L)
                        + (long)(Math.Round(g.y * 1e6) * 1000L)
                        + (long)Math.Round(g.z * 1e6));
                }
            }
        }

        double trace = xx + yy + zz;
        double dev = Math.Max(Math.Abs(xx - yy), Math.Max(Math.Abs(yy - zz), Math.Abs(zz - xx)));
        double off = Math.Abs(xy) + Math.Abs(xz) + Math.Abs(yz);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "  {0}\n    узлов {1}, различных направлений {2}\n"
            + "    Σggᵀ диагональ = [{3:F1}, {4:F1}, {5:F1}]   сумма |внедиаг.| = {6:F3}\n"
            + "    сумма векторов = ({7:F4}, {8:F4}, {9:F4})   разброс диагонали {10:F3}%",
            label, count, seen.Count, xx, yy, zz, off, sx, sy, sz, (100d * dev) / (trace / 3d)));
    }
}
