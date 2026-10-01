using System;
using System.Globalization;
using Galilego.Core;
using Galilego.Universe;
using Unity.Mathematics;

internal static partial class P1bTests
{
    /// <summary>
    /// Плитка 0.5°/65 вершин вокруг ПИКА рельефа, нормали double vs float
    /// end-to-end — тот же замер, что T84 сценарий 2.
    ///
    /// Две вещи обязательны, и обе были сделаны неправильно в первой версии:
    ///
    ///  1) Плитка центрируется на пике, а не на произвольной точке. Иначе
    ///     замер попадает в равнину и показывает запас, которого нет: первая
    ///     версия брала lat 0.31 / lon 1.77 и выдала 3e-7 рад.
    ///  2) Сетка float должна быть НАСТОЯЩИМ float32 (UnityEngine.Vector3).
    ///     Хранить её в Vector3d — значит квантования не происходит, обе сетки
    ///     совпадают, и угол получается нулевым по построению. Первая версия
    ///     делала именно это, приводила к float только высоту, и поэтому
    ///     «запас 4000×» был не подтверждён ничем.
    ///
    /// Контроль: legacy-конфигурация T84 обязана дать ≈5.4e-4. Если не даёт —
    /// тест сломан, а не рельеф изменился.
    /// </summary>
    private static double WorstNormalAngleAtPeak(OrbitingBody body, HeightfieldTerrain t)
    {
        const int res = 65;
        const int m = res + 2;
        const double tileSpanDeg = 0.5d;
        double step = tileSpanDeg / (res - 1);
        body.EvaluateWorldState(0d, out Vector3d bodyPos, out _);

        // Пик: та же грубая решётка, что и в T84, только по lat/lon.
        double peakLat = 0d;
        double peakLon = 0d;
        double maxH = double.MinValue;
        const int scan = 121;
        for (int i = 0; i < scan; i++)
        {
            double lat = -1.4835298d + (2.9670596d * i / (scan - 1));
            for (int j = 0; j < scan; j++)
            {
                double lon = -System.Math.PI + (2d * System.Math.PI * j / (scan - 1));
                double h = t.GetRawHeightMeters(body, lat, lon);
                if (h >= maxH)
                {
                    maxH = h;
                    peakLat = lat;
                    peakLon = lon;
                }
            }
        }

        TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(t);
        var posD = new Vector3d[m, m];
        var posF = new UnityEngine.Vector3[m, m];
        for (int row = 0; row < m; row++)
        {
            double latDeg = (peakLat * (180d / System.Math.PI)) + ((row - ((m - 1) * 0.5d)) * step);
            if (latDeg > 88d)
            {
                latDeg = 88d;
            }

            if (latDeg < -88d)
            {
                latDeg = -88d;
            }

            double lat = latDeg * (System.Math.PI / 180d);
            for (int col = 0; col < m; col++)
            {
                double lonDeg = (peakLon * (180d / System.Math.PI)) + ((col - ((m - 1) * 0.5d)) * step);
                double lon = lonDeg * (System.Math.PI / 180d);
                double cosLat = System.Math.Cos(lat);

                double hd = t.GetHeightMeters(body, lat, lon);
                body.GetSurfaceState(latDeg, lonDeg, hd, 0d, out Vector3d worldD, out _);
                Vector3d relD = worldD - bodyPos;
                posD[row, col] = relD;

                // Настоящий float32: высота и мировые координаты приводятся к
                // float, как при записи в вершинный буфер.
                float hf = (float)hd;
                body.GetSurfaceState(latDeg, lonDeg, hf, 0d, out Vector3d worldF, out _);
                Vector3d relF = worldF - bodyPos;
                posF[row, col] = new UnityEngine.Vector3((float)relF.X, (float)relF.Y, (float)relF.Z);
            }
        }

        double worst = 0d;
        for (int row = 0; row < res; row++)
        {
            for (int col = 0; col < res; col++)
            {
                int r = row + 1;
                int c = col + 1;
                Vector3d dLonD = posD[r, c + 1] - posD[r, c - 1];
                Vector3d dLatD = posD[r + 1, c] - posD[r - 1, c];
                UnityEngine.Vector3 fLon = posF[r, c + 1] - posF[r, c - 1];
                UnityEngine.Vector3 fLat = posF[r + 1, c] - posF[r - 1, c];
                Vector3d dLonF = new Vector3d(fLon.x, fLon.y, fLon.z);
                Vector3d dLatF = new Vector3d(fLat.x, fLat.y, fLat.z);
                Vector3d nD = Vector3d.Cross(dLatD, dLonD).Normalized;
                Vector3d nF = Vector3d.Cross(dLatF, dLonF).Normalized;
                double ang = System.Math.Acos(System.Math.Min(1d, System.Math.Max(-1d, Vector3d.Dot(nD, nF))));
                if (ang > worst)
                {
                    worst = ang;
                }
            }
        }

        return worst;
    }

    /// <summary>
    /// Фаза D: warp по ступеням на плитке у пика. Warp сдвигает точку выборки и
    /// повышает константу Липшица поля, поэтому поднимать его сразу — значит
    /// гадать, что съело запас: ridged, домен или сам warp. Меряем и угол, и
    /// долю суши (воронка T87/T101).
    /// </summary>
    private static int Test121_WarpSteps()
    {
        OrbitingBody body = MakeTerrainBody();
        const double angleLimit = 2e-3;

        // Контроль: та же конфигурация, что в T84. Если она не даёт ≈5.4e-4,
        // замер сломан и новые числа читать нельзя.
        HeightfieldTerrain control = MakeTerrainAdvanced(42);
        double controlAngle = WorstNormalAngleAtPeak(body, control);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "    контроль (legacy advanced, T84 конфиг): {0:E3} рад  (T84 даёт 5.36E-04)", controlAngle));

        double[] steps = { 0.10, 0.15, 0.20, 0.25 };
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "    {0,-24} {1,12} {2,10} {3,10} {4,12}",
            "perlin+multifr, warp", "угол, рад", "доля суши", "Δ от 0.1", "p99, м"));

        double firstLand = 0d;
        double prevAngle = 0d;
        bool controlOk = controlAngle > 1e-4 && controlAngle < 2e-3;
        bool angleOk = true;
        for (int i = 0; i < steps.Length; i++)
        {
            HeightfieldTerrain t = PerlinPreset();
            t.WarpStrength = steps[i];
            double angle = WorstNormalAngleAtPeak(body, t);
            double land = LandFraction(t, 120000);
            if (i == 0)
            {
                firstLand = land;
            }

            if (angle > angleLimit)
            {
                angleOk = false;
            }

            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "    {0,-24} {1,12:E3} {2,10:F4} {3,10:+0.0000;-0.0000} {4,12:F0}  {5}",
                "warp " + steps[i].ToString("F2", CultureInfo.InvariantCulture), angle, land,
                land - firstLand, P99(t, 120000),
                angle > angleLimit ? "ПРЕВЫШЕН" : "ok"));

            if (i > 0 && angle < prevAngle * 0.5d)
            {
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "        ВНИМАНИЕ: угол упал в {0:F1}× при росте warp — кривизна обязана расти",
                    prevAngle / angle));
            }

            prevAngle = angle;
        }

        Check(controlOk, "T121 warp-steps",
            "контроль воспроизводит T84: " + controlAngle.ToString("E3", CultureInfo.InvariantCulture) + " рад");
        Check(angleOk, "T121 warp-steps",
            "worstNormalAngle ≤ 2e-3 рад на всех ступенях warp 0.1..0.25 у Перлина");
        return 0;
    }

    /// <summary>Конфигурация EarthLike_Perlin без гамма-сдвига (γ=1).</summary>
    private static HeightfieldTerrain PerlinPreset()
    {
        HeightfieldTerrain t = SceneLikeTerrain();
        t.NoiseStyle = (int)TerrainNoiseStyle.Perlin;
        t.MaskNoiseStyle = (int)TerrainNoiseStyle.Perlin;
        t.RidgedMode = (int)TerrainRidgedMode.Multifractal;
        t.RidgedGamma = 1.0d;
        t.SlopeDamp = 0d;
        t.SlopeDampMode = (int)TerrainSlopeDampMode.Off;
        return t;
    }

    private static double P99(HeightfieldTerrain t, int n)
    {
        double3[] dirs = SphereDirs(n);
        TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(t);
        var h = new double[n];
        for (int i = 0; i < n; i++)
        {
            h[i] = TerrainNoise.SampleHeight(p, dirs[i]) * t.AmplitudeMeters;
        }

        Array.Sort(h);
        return h[(int)(n * 0.99)];
    }
}
