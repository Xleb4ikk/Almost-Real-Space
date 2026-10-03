using System;
using System.Globalization;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    /// <summary>
    /// Зонд аномалии «стена с вертикальными рёбрами» (скриншот игрока,
    /// lat 5.715345 / lon 142.200892). Профиль и сид взяты из сцены:
    /// OutdoorsScene → BodyAuthoring.TerrainPreset = EarthLike_Perlin,
    /// TerrainSeed = 24334543, Radius = 1143000.
    ///
    /// Задача — ответить, есть ли пила МАСШТАБА СЕТКИ в самой функции высоты.
    /// Шаг сетки листа на MaxDepth 12: (π/2·R/2^12)/(TileResolution-1) ≈ 6.85 м.
    /// Если пилы в поле нет, то аномалия живёт в сборке меша/рендере, а не в шуме.
    /// </summary>
    private static HeightfieldTerrain SceneTerrain()
    {
        // Копия Assets/_Project/Profiles/Terrain/EarthLike_Perlin.asset один в один.
        return new HeightfieldTerrain
        {
            Seed = 24334543,
            AmplitudeMeters = 13662d,
            BaseFrequency = 20d,
            Octaves = 10,
            SeaLevelMeters = 0d,
            Lacunarity = 2d,
            Gain = 0.5d,
            RidgedMix = 0.7d,
            RidgedMode = 1,
            RidgedSharpness = 2d,
            RidgedWeightGain = 2d,
            RidgedGamma = 1d,
            SlopeDamp = 0d,
            SlopeDampMode = 0,
            NoiseStyle = 1,
            MaskNoiseStyle = -1,
            ContinentFrequency = 1.3d,
            ContinentOctaves = 5,
            ContinentThreshold = 0.14d,
            ContinentSharpness = 0.08d,
            ContinentDepth = 1.2d,
            ContinentGain = 0.55d,
            ContinentWarpStrength = 0.3d,
            ContinentWarpFrequency = 1.5d,
            ContinentWarpOctaves = 3,
            ContinentRidgeMix = 0.12d,
            ContinentRidgeFrequency = 2.5d,
            ContinentRidgeOctaves = 2,
            ContinentLatitudeBias = 0.08d,
            OceanFloorDepth = 0.42d,
            OceanShelfDepth = 0.02d,
            InteriorFloor = 0.035d,
            OrogenyFrequency = 1.1d,
            OrogenyOctaves = 3,
            OrogenyThreshold = -0.4d,
            OrogenySharpness = 0.3d,
            OrogenyFloor = 0.15d,
            OrogenyGain = 0.85d,
            PlainMix = 0.85d,
            PlainFrequency = 2d,
            PlainOctaves = 2,
            PlainThreshold = 0d,
            PlainSharpness = 0.25d,
            PlainElevation = 0.1d,
            TailKnee = 0.13d,
            TailThreshold = 0.5656d,
            DepthKnee = 0d,
            DepthThreshold = 0d,
            BeachShelfAltitudeMeters = 12d,
            BeachShelfWidth = 0.03d,
            BeachShelfWidthMaxScale = 14d,
            BeachShelfWidthNoiseFrequency = 150d,
            BeachShelfWidthNoiseOctaves = 4,
            DetailMix = 0d,
            DetailFrequency = 0d,
            DetailOctaves = 5,
            WarpStrength = 0.1d,
            WarpFrequency = 2d,
            WarpOctaves = 2,
            WarpSeedOffset = 0,
            ColorNoiseFrequency = 25d,
            ColorNoiseOctaves = 4,
            ColorNoiseStrength = 0.09d,
            ColorDetailFrequency = 400d,
            ColorDetailOctaves = 3,
            ColorDetailStrength = 0.15d
        };
    }

    private const double ProbeRadius = 1143000d;
    private const double PlayerLat = 5.715345d;
    private const double PlayerLon = 142.200892d;
    private const double LeafQuadMeters = 6.85d;

    private static OrbitingBody ProbeBody(HeightfieldTerrain t)
    {
        var body = new OrbitingBody
        {
            Name = "Terra",
            StandardGravitationalParameter = 1.28e13d,
            Radius = ProbeRadius,
            RotationPeriodSeconds = 86400d
        };
        body.Terrain = t;
        return body;
    }

    /// <summary>Смещение точки в метрах по северу/востоку от опорной lat/lon.</summary>
    private static void OffsetLatLon(double latDeg, double lonDeg, double northM, double eastM,
        out double latOut, out double lonOut)
    {
        double lat = latDeg * (Math.PI / 180d);
        double lon = lonDeg * (Math.PI / 180d);
        latOut = lat + (northM / ProbeRadius);
        lonOut = lon + (eastM / (ProbeRadius * Math.Cos(lat)));
    }

    private static char Band(double h)
    {
        if (h < 0d) return '~';
        if (h < 13d) return 's';
        if (h < 25d) return '.';
        if (h < 50d) return ':';
        if (h < 100d) return '+';
        if (h < 250d) return '*';
        if (h < 600d) return '#';
        if (h < 1500d) return '%';
        return '@';
    }

    private static int Test900_TerrainWallProbe()
    {
        HeightfieldTerrain terrain = SceneTerrain();
        OrbitingBody body = ProbeBody(terrain);
        var inv = CultureInfo.InvariantCulture;

        double latRad = PlayerLat * (Math.PI / 180d);
        double lonRad = PlayerLon * (Math.PI / 180d);
        double hRaw = terrain.GetRawHeightMeters(body, latRad, lonRad);
        double hClamped = terrain.GetHeightMeters(body, latRad, lonRad);
        Console.WriteLine(string.Format(inv,
            "    h(игрок lat {0} lon {1}) = {2:F2} м (сырая), {3:F2} м (с клампом моря). HUD в кадре: рельеф под игроком 26 м.",
            PlayerLat, PlayerLon, hRaw, hClamped));

        // ===== Карта окрестности: 41×41 по 150 м = ±3 км =====
        const int N = 41;
        const double Step = 150d;
        var map = new double[N, N];
        double minH = double.MaxValue;
        double maxH = double.MinValue;
        for (int i = 0; i < N; i++)
        {
            for (int j = 0; j < N; j++)
            {
                double north = (i - (N / 2)) * Step;
                double east = (j - (N / 2)) * Step;
                OffsetLatLon(PlayerLat, PlayerLon, north, east, out double la, out double lo);
                double h = terrain.GetRawHeightMeters(body, la, lo);
                map[i, j] = h;
                if (h < minH) minH = h;
                if (h > maxH) maxH = h;
            }
        }

        Console.WriteLine(string.Format(inv,
            "    карта ±3 км (шаг {0:F0} м): h от {1:F0} до {2:F0} м. Строки — север, столбцы — восток.",
            Step, minH, maxH));
        Console.WriteLine("    ~ море  s полка(<13)  . <25  : <50  + <100  * <250  # <600  % <1500  @ выше");
        for (int i = N - 1; i >= 0; i--)
        {
            var line = new System.Text.StringBuilder(N);
            for (int j = 0; j < N; j++)
            {
                line.Append(Band(map[i, j]));
            }

            Console.WriteLine("    " + line);
        }

        // ===== Где самый крутой уступ в карте =====
        double worstSlope = 0d;
        int wi = 0;
        int wj = 0;
        bool worstAlongEast = true;
        for (int i = 0; i < N; i++)
        {
            for (int j = 0; j < N; j++)
            {
                if (j + 1 < N)
                {
                    double s = Math.Abs(map[i, j + 1] - map[i, j]) / Step;
                    if (s > worstSlope)
                    {
                        worstSlope = s;
                        wi = i;
                        wj = j;
                        worstAlongEast = true;
                    }
                }

                if (i + 1 < N)
                {
                    double s = Math.Abs(map[i + 1, j] - map[i, j]) / Step;
                    if (s > worstSlope)
                    {
                        worstSlope = s;
                        wi = i;
                        wj = j;
                        worstAlongEast = false;
                    }
                }
            }
        }

        Console.WriteLine(string.Format(inv,
            "    максимальный уклон по карте {0:F3} ({1:F1}°) на клетке ({2},{3}), вдоль {4}",
            worstSlope, Math.Atan(worstSlope) * (180d / Math.PI), wi, wj, worstAlongEast ? "востока" : "севера"));

        // ===== Тонкий профиль 6.85 м через этот уступ =====
        double baseNorth = (wi - (N / 2)) * Step;
        double baseEast = (wj - (N / 2)) * Step;
        Console.WriteLine(string.Format(inv,
            "    профиль с шагом сетки листа {0:F2} м вдоль {1} (Δ от точки ({2:F0},{3:F0}) м):",
            LeafQuadMeters, worstAlongEast ? "востока" : "севера", baseNorth, baseEast));
        double prev = double.NaN;
        for (int k = -8; k <= 24; k++)
        {
            double north = baseNorth + (worstAlongEast ? 0d : (k * LeafQuadMeters));
            double east = baseEast + (worstAlongEast ? (k * LeafQuadMeters) : 0d);
            OffsetLatLon(PlayerLat, PlayerLon, north, east, out double la, out double lo);
            double h = terrain.GetRawHeightMeters(body, la, lo);
            string delta = double.IsNaN(prev) ? "      " : (h - prev).ToString("+0.0;-0.0", inv).PadLeft(7);
            Console.WriteLine(string.Format(inv, "      k={0,4}  h={1,10:F2} м  Δ={2}", k, h, delta));
            prev = h;
        }

        // ===== Метрика пилы масштаба сетки =====
        // Идём отрезками по 6.85 м в разные стороны. Пила = знак Δ скачет, а
        // размах зубца (минимум из двух соседних |Δ|) велик.
        var rng = new Random(20260127);
        int lines = 600;
        int perLine = 160;
        int zigzags = 0;
        int zigzagsOver5m = 0;
        int zigzagsOver20m = 0;
        double worstTooth = 0d;
        double worstToothN = 0d;
        double worstToothE = 0d;
        double worstStep = 0d;
        double worstStepN = 0d;
        double worstStepE = 0d;
        for (int line = 0; line < lines; line++)
        {
            double a = rng.NextDouble() * Math.PI * 2d;
            double startN = (rng.NextDouble() * 20000d) - 10000d;
            double startE = (rng.NextDouble() * 20000d) - 10000d;
            double dn = Math.Cos(a) * LeafQuadMeters;
            double de = Math.Sin(a) * LeafQuadMeters;
            double n = startN;
            double e = startE;
            OffsetLatLon(PlayerLat, PlayerLon, n, e, out double la, out double lo);
            double prevH = terrain.GetRawHeightMeters(body, la, lo);
            double prevDelta = double.NaN;
            for (int s = 1; s < perLine; s++)
            {
                n += dn;
                e += de;
                OffsetLatLon(PlayerLat, PlayerLon, n, e, out la, out lo);
                double h = terrain.GetRawHeightMeters(body, la, lo);
                double delta = h - prevH;
                if (Math.Abs(delta) > worstStep)
                {
                    worstStep = Math.Abs(delta);
                    worstStepN = n;
                    worstStepE = e;
                }

                if (!double.IsNaN(prevDelta) && prevDelta * delta < 0d)
                {
                    zigzags++;
                    double tooth = Math.Min(Math.Abs(prevDelta), Math.Abs(delta));
                    if (tooth > 5d) zigzagsOver5m++;
                    if (tooth > 20d) zigzagsOver20m++;
                    if (tooth > worstTooth)
                    {
                        worstTooth = tooth;
                        worstToothN = n;
                        worstToothE = e;
                    }
                }

                prevDelta = delta;
                prevH = h;
            }
        }

        int steps = lines * (perLine - 1);
        Console.WriteLine(string.Format(inv,
            "    пила: из {0} шагов по {1:F2} м разворот знака Δ на {2} ({3:P2}), из них зубец >5 м: {4}, >20 м: {5}",
            steps, LeafQuadMeters, zigzags, (double)zigzags / steps, zigzagsOver5m, zigzagsOver20m));
        Console.WriteLine(string.Format(inv,
            "    самый большой шаг |Δh| = {0:F2} м на ({1:F0},{2:F0}) м от игрока; самый большой зубец {3:F2} м на ({4:F0},{5:F0}) м",
            worstStep, worstStepN, worstStepE, worstTooth, worstToothN, worstToothE));

        Check(true, "T900 terrain-wall-probe", "зонд отработал");
        return 0;
    }
}
