using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Universe;
using Unity.Collections;
using Unity.Mathematics;

// T109 — flat pads at points on a planet (PQS mods, analogous to KSP's FlattenArea).
//
// What can break here and WHY it matters:
//  1. The modifiers feed into TerrainNoise.SampleHeight — the single terrain-shape
//     function that rendering, physics, normals and decor all depend on. An error
//     here does not "break a pad", it silently diverges what is seen from what is
//     walked on.
//  2. An EMPTY list must give the previous terrain bit-for-bit. Otherwise merely
//     enabling "a list of pads" would shift the whole existing world map, because
//     every scene and every test would receive an empty table.
//  3. A pad must be flat INSIDE and blend smoothly outwards. A hard disc would give
//     a wall and kinked normals.
//  4. The sea-level clamp must not drown a pad and must not block digs below sea.
//  5. Pad order on overlap must be deterministic, otherwise "base over base" would
//     depend on traversal order.

internal static partial class P1bTests
{
    private const double PadRadius = 1143000d;   // Terra from the scene
    private const double PadAmplitude = 9144d;   // EarthLike.asset
    private const double PadLatDeg = -27.8405d;
    private const double PadLonDeg = -119.3773d;

    private static double PadRad(double degrees)
    {
        return degrees * (Math.PI / 180d);
    }

    /// <summary>Offset in metres along the meridian (north).</summary>
    private static double PadOffsetNorth(double meters)
    {
        return PadRad(PadLatDeg) + (meters / PadRadius);
    }

    /// <summary>Offset in metres along the parallel (east), accounting for latitude compression.</summary>
    private static double PadOffsetEast(double meters)
    {
        return PadRad(PadLonDeg) + (meters / (PadRadius * Math.Cos(PadRad(PadLatDeg))));
    }

    private static HeightfieldTerrain MakePadTerrain()
    {
        var terrain = new HeightfieldTerrain();
        terrain.Seed = 24334543;
        terrain.BaseFrequency = 2.2d;
        terrain.Octaves = 9;
        terrain.Gain = 0.5d;
        terrain.Lacunarity = 2d;
        terrain.AmplitudeMeters = PadAmplitude;
        terrain.SeaLevelMeters = 0d;
        terrain.ContinentFrequency = 1.1d;
        terrain.ContinentOctaves = 5;
        terrain.ContinentThreshold = 0.06d;
        terrain.ContinentSharpness = 0.2d;
        terrain.ContinentDepth = 1.2d;
        terrain.RidgedMix = 0.85d;
        terrain.WarpStrength = 0.15d;
        terrain.WarpFrequency = 0.9d;
        terrain.WarpOctaves = 3;
        return terrain;
    }

    private static TerrainModifier Pad(
        double lat, double lon, double inner, double outer, double target, bool seaOverride = false)
    {
        return new TerrainModifier
        {
            LatitudeDegrees = lat,
            LongitudeDegrees = lon,
            InnerRadiusMeters = inner,
            OuterRadiusMeters = outer,
            TargetHeightMeters = target,
            OverridesSeaLevel = seaOverride
        };
    }

    private static int Test109_TerrainPads()
    {
        // 0. THE CONTAINER MUST BE VALID EVEN WITH ZERO PADS.
        //    TerrainNoiseParams is passed into TerrainTileJob wholesale, and Unity's
        //    job scheduler requires valid containers in ALL fields at Schedule time,
        //    regardless of whether the code reads them.
        HeightfieldTerrain freshTerrain = MakePadTerrain();
        Check(freshTerrain.Mods.IsCreated, "T109 terrain-pads",
            "у свежего рельефа таблица площадок ВАЛИДНА (иначе рендер падает на Schedule)");

        HeightfieldTerrain noPads = MakePadTerrain();
        noPads.SetModifiers(new List<TerrainModifier>(), PadRadius);
        Check(noPads.Mods.IsCreated && noPads.Mods.Length == 0, "T109 terrain-pads",
            "пустой список даёт валидную пустую таблицу, а не default");

        // SetModifiers on an empty set must not break validity: the renderer lands
        // here, rebuilding params every frame.
        noPads.SetModifiers(new List<TerrainModifier> { Pad(PadLatDeg, PadLonDeg, 100d, 300d, 250d) }, PadRadius);
        noPads.SetModifiers(new List<TerrainModifier>(), PadRadius);
        Check(noPads.Mods.IsCreated, "T109 terrain-pads",
            "после добавления и удаления площадки таблица остаётся валидной");

        // 1. Control check: the natural terrain must be uneven, otherwise all the
        //    checks below pass vacuously and mean nothing.
        HeightfieldTerrain plain = MakePadTerrain();
        double natCenter = plain.GetRawHeightMeters(null, PadRad(PadLatDeg), PadRad(PadLonDeg));
        double nat250 = plain.GetRawHeightMeters(null, PadOffsetNorth(250d), PadRad(PadLonDeg));
        double nat500 = plain.GetRawHeightMeters(null, PadOffsetNorth(500d), PadRad(PadLonDeg));
        Check(Math.Abs(natCenter - nat250) > 0.5d && Math.Abs(nat250 - nat500) > 0.5d,
            "T109 terrain-pads",
            "естественный рельеф неровный (иначе тесты площадок нечего проверять)");

        // 1. BIT-EXACT: without pads the terrain does not change by a single ulp.
        //    Compare the empty table and a disabled pad — different branches, both
        //    must be no-ops at the output.
        HeightfieldTerrain emptyTable = MakePadTerrain();
        emptyTable.SetModifiers(new List<TerrainModifier>(), PadRadius);
        Check(natCenter == emptyTable.GetRawHeightMeters(null, PadRad(PadLatDeg), PadRad(PadLonDeg)),
            "T109 terrain-pads",
            "пустой список площадок не меняет высоту БИТ-В-БИТ");
        emptyTable.DisposeModifiers();

        HeightfieldTerrain disabled = MakePadTerrain();
        disabled.SetModifiers(
            new List<TerrainModifier> { Pad(PadLatDeg, PadLonDeg, 0d, 0d, 12345d) }, PadRadius);
        Check(natCenter == disabled.GetRawHeightMeters(null, PadRad(PadLatDeg), PadRad(PadLonDeg)),
            "T109 terrain-pads",
            "выключенная площадка (радиус 0) не трогает рельеф");

        // 2. Flat core: target height deliberately different from the natural one.
        const double target = 250d;
        HeightfieldTerrain padded = MakePadTerrain();
        padded.SetModifiers(
            new List<TerrainModifier> { Pad(PadLatDeg, PadLonDeg, 120d, 400d, target) }, PadRadius);

        double centerHeight = padded.GetRawHeightMeters(null, PadRad(PadLatDeg), PadRad(PadLonDeg));
        Check(Math.Abs(centerHeight - target) < 1e-9, "T109 terrain-pads",
            "в центре площадки высота РОВНО целевой (" + centerHeight.ToString("F6") + " м)");
        Check(Math.Abs(natCenter - target) > 1d, "T109 terrain-pads",
            "естественная высота в центре отличается от целевой (тест не тривиален)");

        // 3. Core flat over its whole area: north/south/east/west inside InnerRadius.
        double maxCoreDeviation = 0d;
        double[] probes = { 0d, 20d, 60d, 110d, -90d, -30d, 45d, -60d, 100d, -100d };
        for (int i = 0; i < probes.Length; i++)
        {
            double dev = Math.Abs(padded.GetRawHeightMeters(null, PadOffsetNorth(probes[i]), PadRad(PadLonDeg)) - target);
            if (dev > maxCoreDeviation)
            {
                maxCoreDeviation = dev;
            }

            dev = Math.Abs(padded.GetRawHeightMeters(null, PadRad(PadLatDeg), PadOffsetEast(probes[i])) - target);
            if (dev > maxCoreDeviation)
            {
                maxCoreDeviation = dev;
            }
        }

        Check(maxCoreDeviation < 1e-9, "T109 terrain-pads",
            "ядро ровное во всех " + (probes.Length * 2) + " пробах (макс. отклонение "
            + maxCoreDeviation.ToString("E2") + " м)");

        // 4. Apron. The blend weight goes from 0 at the outer edge to 1 at the core,
        //    so the deviation from the target DECREASES towards the centre.
        double outerDeviation = Math.Abs(nat500 - target);
        double previousDeviation = outerDeviation;
        bool monotone = true;
        for (int step = 1; step <= 40; step++)
        {
            double distance = 400d - (310d * (step / 40.0d));
            double h = padded.GetRawHeightMeters(null, PadOffsetNorth(distance), PadRad(PadLonDeg));
            double deviation = Math.Abs(h - target);

            if (deviation > previousDeviation + 1e-9d)
            {
                monotone = false;
            }

            previousDeviation = deviation;
        }

        Check(monotone, "T109 terrain-pads",
            "фартук монотонен: отклонение от целевой убывает к ядру");

        double atInnerEdge = padded.GetRawHeightMeters(null, PadOffsetNorth(120d), PadRad(PadLonDeg));
        Check(Math.Abs(atInnerEdge - target) < 1e-6, "T109 terrain-pads",
            "на внутренней границе фартука высота приходит к целевой ("
            + atInnerEdge.ToString("F6") + " м)");

        // 4a. The SHAPE of the apron, not just "monotone". The weight must be exactly
        //     smoothstep: zero derivatives at both ends, which is why the apron is
        //     smooth — otherwise normals kink at the pad edge and buildings on the rim
        //     sit tilted. Check a concrete value at the quarter point:
        //     smoothstep(0.25) = 0.15625 (linear would give 0.25, a hard disc 0 or 1).
        double cosOuterProbe = Math.Cos(400d / PadRadius);
        double cosInnerProbe = Math.Cos(120d / PadRadius);
        double quarterAngle = Math.Acos(cosOuterProbe - ((cosOuterProbe - cosInnerProbe) * 0.25d));
        double quarterDistance = quarterAngle * PadRadius;
        double natQuarter = plain.GetRawHeightMeters(null, PadOffsetNorth(quarterDistance), PadRad(PadLonDeg));
        double padQuarter = padded.GetRawHeightMeters(null, PadOffsetNorth(quarterDistance), PadRad(PadLonDeg));
        double quarterWeight = (padQuarter - natQuarter) / (target - natQuarter);
        Check(Math.Abs(quarterWeight - 0.15625d) < 1e-3, "T109 terrain-pads",
            "форма фартука — smoothstep (вес на четверти " + quarterWeight.ToString("F5")
            + " вместо 0,15625; линейная дала бы 0,25)");

        // At half the apron the weight must be exactly 0.5 — curve symmetry.
        double halfAngle = Math.Acos(cosOuterProbe - ((cosOuterProbe - cosInnerProbe) * 0.5d));
        double halfDistance = halfAngle * PadRadius;
        double natHalf = plain.GetRawHeightMeters(null, PadOffsetNorth(halfDistance), PadRad(PadLonDeg));
        double padHalf = padded.GetRawHeightMeters(null, PadOffsetNorth(halfDistance), PadRad(PadLonDeg));
        double halfWeight = (padHalf - natHalf) / (target - natHalf);
        Check(Math.Abs(halfWeight - 0.5d) < 1e-3, "T109 terrain-pads",
            "фартук симметричен (вес на половине " + halfWeight.ToString("F5") + ")");

        // 5. Outside the pad the height is untouched BIT-EXACT.
        double naturalFar = plain.GetRawHeightMeters(null, PadOffsetNorth(900d), PadRad(PadLonDeg));
        Check(naturalFar == padded.GetRawHeightMeters(null, PadOffsetNorth(900d), PadRad(PadLonDeg)),
            "T109 terrain-pads", "за внешним радиусом рельеф не тронут БИТ-В-БИТ");

        // 6. Normal above the core = zenith. Indirect but strict: if the core were
        //    even slightly uneven, the neighbours at the normal base (~114 m) would diverge.
        double hNorth = padded.GetRawHeightMeters(null, PadOffsetNorth(114d), PadRad(PadLonDeg));
        double hSouth = padded.GetRawHeightMeters(null, PadOffsetNorth(-114d), PadRad(PadLonDeg));
        double hEast = padded.GetRawHeightMeters(null, PadRad(PadLatDeg), PadOffsetEast(114d));
        Check(Math.Abs(hNorth - target) < 1e-9 && Math.Abs(hSouth - target) < 1e-9 && Math.Abs(hEast - target) < 1e-9,
            "T109 terrain-pads",
            "внутри ядра высоты всех трёх соседей равны целевой → нормаль = зенит");

        // 7. A pad above water survives the sea-level clamp. No flag needed: the
        //    clamp pushes height UP to sea level, and the pad is above sea level.
        HeightfieldTerrain shorePad = MakePadTerrain();
        shorePad.SetModifiers(
            new List<TerrainModifier> { Pad(PadLatDeg, PadLonDeg, 100d, 300d, 15d) }, PadRadius);
        Check(Math.Abs(shorePad.GetRawHeightMeters(null, PadRad(PadLatDeg), PadRad(PadLonDeg)) - 15d) < 1e-9
            && Math.Abs(shorePad.GetHeightMeters(null, PadRad(PadLatDeg), PadRad(PadLonDeg)) - 15d) < 1e-9,
            "T109 terrain-pads", "площадка +15 м над морем не тонет в клампе");

        // 8. Dig BELOW sea level: without the flag it would be flooded, with the flag it is not.
        HeightfieldTerrain digNoFlag = MakePadTerrain();
        digNoFlag.SetModifiers(
            new List<TerrainModifier> { Pad(PadLatDeg, PadLonDeg, 100d, 300d, -30d) }, PadRadius);
        Check(Math.Abs(digNoFlag.GetHeightMeters(null, PadRad(PadLatDeg), PadRad(PadLonDeg))) < 1e-9,
            "T109 terrain-pads", "без флага раскопка -30 м поднимается клампом до уровня моря");

        HeightfieldTerrain digFlag = MakePadTerrain();
        digFlag.SetModifiers(
            new List<TerrainModifier> { Pad(PadLatDeg, PadLonDeg, 100d, 300d, -30d, true) }, PadRadius);
        Check(Math.Abs(digFlag.GetHeightMeters(null, PadRad(PadLatDeg), PadRad(PadLonDeg)) + 30d) < 1e-9,
            "T109 terrain-pads", "с флагом раскопка -30 м остаётся сухой");

        // 9. Table comparison. Equal content from different builds must count as
        //    EQUAL: otherwise ParamsEqual in the renderer would rebuild its cache for
        //    nothing, and a real modifier edit could go unnoticed.
        var listA = new List<TerrainModifier> { Pad(PadLatDeg, PadLonDeg, 120d, 400d, target) };
        var listB = new List<TerrainModifier> { Pad(PadLatDeg, PadLonDeg, 120d, 400d, target) };
        NativeArray<TerrainModifierData> tableA = TerrainModifiers.Build(listA, PadRadius);
        NativeArray<TerrainModifierData> tableB = TerrainModifiers.Build(listB, PadRadius);
        Check(TerrainModifiers.TableEqual(tableA, tableB), "T109 terrain-pads",
            "одинаковые таблицы из разных сборок равны");

        listB[0] = Pad(PadLatDeg, PadLonDeg, 120d, 400d, target + 1d);
        NativeArray<TerrainModifierData> tableHeight = TerrainModifiers.Build(listB, PadRadius);
        Check(!TerrainModifiers.TableEqual(tableA, tableHeight), "T109 terrain-pads",
            "изменение целевой высоты ловится сравнением таблиц");

        listB[0] = Pad(PadLatDeg, PadLonDeg, 121d, 400d, target);
        NativeArray<TerrainModifierData> tableRadius = TerrainModifiers.Build(listB, PadRadius);
        Check(!TerrainModifiers.TableEqual(tableA, tableRadius), "T109 terrain-pads",
            "изменение радиуса ядра ловится сравнением таблиц");

        listB[0] = Pad(PadLatDeg + 0.5d, PadLonDeg, 120d, 400d, target);
        NativeArray<TerrainModifierData> tablePos = TerrainModifiers.Build(listB, PadRadius);
        Check(!TerrainModifiers.TableEqual(tableA, tablePos), "T109 terrain-pads",
            "перенос площадки ловится сравнением таблиц");

        tableA.Dispose();
        tableB.Dispose();
        tableHeight.Dispose();
        tableRadius.Dispose();
        tablePos.Dispose();

        // 10. List order decides on overlap, and the result is deterministic.
        HeightfieldTerrain overlap = MakePadTerrain();
        overlap.SetModifiers(new List<TerrainModifier>
        {
            Pad(PadLatDeg, PadLonDeg, 400d, 500d, 100d),
            Pad(PadLatDeg, PadLonDeg, 400d, 500d, 300d)
        }, PadRadius);
        double overlapAB = overlap.GetRawHeightMeters(null, PadRad(PadLatDeg), PadRad(PadLonDeg));

        HeightfieldTerrain overlapReversed = MakePadTerrain();
        overlapReversed.SetModifiers(new List<TerrainModifier>
        {
            Pad(PadLatDeg, PadLonDeg, 400d, 500d, 300d),
            Pad(PadLatDeg, PadLonDeg, 400d, 500d, 100d)
        }, PadRadius);
        double overlapBA = overlapReversed.GetRawHeightMeters(null, PadRad(PadLatDeg), PadRad(PadLonDeg));

        Check(Math.Abs(overlapAB - 300d) < 1e-9 && Math.Abs(overlapBA - 100d) < 1e-9,
            "T109 terrain-pads", "при пересечении побеждает последняя в списке, порядок детерминирован");

        // 11. Round shape. Checking the core height is pointless — there it equals the
        //     target anywhere. Roundness shows only in the apron blend WEIGHT: it must
        //     be the same at equal distances in any azimuth. Weight recovered as
        //     (h_pad − h_natural) / (target − h_natural). If the pad were squashed to
        //     one side, the weight would spread across azimuths — invisible by eye in
        //     the scene's perspective.
        double probe = 260d;
        double naturalProbe = plain.GetRawHeightMeters(null, PadOffsetNorth(probe), PadRad(PadLonDeg));
        double denominator = target - naturalProbe;
        Check(Math.Abs(denominator) > 1d, "T109 terrain-pads",
            "точка фартука для проверки круглости не вырождена");

        double weightAtNorth = (padded.GetRawHeightMeters(null, PadOffsetNorth(probe), PadRad(PadLonDeg)) - naturalProbe) / denominator;
        double weightAtSouth = (padded.GetRawHeightMeters(null, PadOffsetNorth(-probe), PadRad(PadLonDeg))
            - plain.GetRawHeightMeters(null, PadOffsetNorth(-probe), PadRad(PadLonDeg))) / denominator;
        double weightAtEast = (padded.GetRawHeightMeters(null, PadRad(PadLatDeg), PadOffsetEast(probe))
            - plain.GetRawHeightMeters(null, PadRad(PadLatDeg), PadOffsetEast(probe))) / denominator;
        double weightAtWest = (padded.GetRawHeightMeters(null, PadRad(PadLatDeg), PadOffsetEast(-probe))
            - plain.GetRawHeightMeters(null, PadRad(PadLatDeg), PadOffsetEast(-probe))) / denominator;

        double weightSpread = Math.Max(
            Math.Max(Math.Abs(weightAtNorth - weightAtSouth), Math.Abs(weightAtEast - weightAtWest)),
            Math.Max(Math.Abs(weightAtNorth - weightAtEast), Math.Abs(weightAtSouth - weightAtWest)));
        Check(weightSpread < 1e-3, "T109 terrain-pads",
            "площадка круглая: вес фартука одинаков по всем азимутам (разброс "
            + weightSpread.ToString("E2") + ", вес " + weightAtNorth.ToString("F4") + ")");

        // 12. Roundness between the compass points too: a diagonal point
        //     (north+east at 45°).
        double diagonal = probe / Math.Sqrt(2d);
        double weightDiagonal = (padded.GetRawHeightMeters(null, PadOffsetNorth(diagonal), PadOffsetEast(diagonal))
            - plain.GetRawHeightMeters(null, PadOffsetNorth(diagonal), PadOffsetEast(diagonal))) / denominator;
        Check(Math.Abs(weightDiagonal - weightAtNorth) < 1e-3, "T109 terrain-pads",
            "площадка круглая и по диагонали (вес " + weightDiagonal.ToString("F4") + ")");

        return 0;
    }
}
