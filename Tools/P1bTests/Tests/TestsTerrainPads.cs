using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Universe;
using Unity.Collections;
using Unity.Mathematics;

// T109 — ровные площадки в точках планеты (PQS-моды, аналог FlattenArea в KSP).
//
// Что тут может сломаться и ПОЧЕМУ это страшно:
//  1. Модификаторы добавлены в TerrainNoise.SampleHeight — единственную функцию
//     формы рельефа, от которой зависят и рендер, и физика, и нормали, и
//     декорации. Ошибка здесь не «сломает площадку», а тихо разъедется между
//     тем, что видно, и тем, по чему ходят.
//  2. ПУСТОЙ список обязан дать бит-в-бит прежний рельеф. Иначе включение
//     «просто списка площадок» сдвинет всю существующую карту мира, потому
//     что каждая сцена и каждый старый тест получат пустую таблицу.
//  3. Площадка должна быть ровной ВНУТРИ и плавно сходить наружу. Резкий диск
//     дал бы стену и излом нормалей.
//  4. Кламп уровня моря не должен топить площадку и не должен мешать раскопкам
//     ниже моря.
//  5. Порядок площадок при пересечении должен быть детерминирован, иначе «база
//     поверх базы» зависела бы от порядка обхода.

internal static partial class P1bTests
{
    private const double PadRadius = 1143000d;   // Terra из сцены
    private const double PadAmplitude = 9144d;   // EarthLike.asset
    private const double PadLatDeg = -27.8405d;
    private const double PadLonDeg = -119.3773d;

    private static double PadRad(double degrees)
    {
        return degrees * (Math.PI / 180d);
    }

    /// <summary>Смещение на метры вдоль меридиана (север).</summary>
    private static double PadOffsetNorth(double meters)
    {
        return PadRad(PadLatDeg) + (meters / PadRadius);
    }

    /// <summary>Смещение на метры вдоль параллели (восток), с учётом сжатия по широте.</summary>
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
        // 0. КОНТЕЙНЕР ДОЛЖЕН БЫТЬ ВАЛИДНЫМ ДАЖЕ ПРИ НУЛЕ ПЛОЩАДОК.
        //    Это не косметика. TerrainNoiseParams уходит в TerrainTileJob
        //    целиком, а планировщик job'ов Unity требует валидные контейнеры во
        //    ВСЕХ полях структуры джобы в момент Schedule — независимо от того,
        //    читает ли их код. На default(NativeArray) рендер падал целиком:
        //    «TerrainTileJob.Params.Mods has not been assigned or constructed»,
        //    и планета пропадала. Проверка IsCreated внутри SampleHeight не
        //    спасает — до неё управление не доходит.
        HeightfieldTerrain freshTerrain = MakePadTerrain();
        Check(freshTerrain.Mods.IsCreated, "T109 terrain-pads",
            "у свежего рельефа таблица площадок ВАЛИДНА (иначе рендер падает на Schedule)");

        HeightfieldTerrain noPads = MakePadTerrain();
        noPads.SetModifiers(new List<TerrainModifier>(), PadRadius);
        Check(noPads.Mods.IsCreated && noPads.Mods.Length == 0, "T109 terrain-pads",
            "пустой список даёт валидную пустую таблицу, а не default");

        // SetModifiers на пустом месте не должен ломать валидность: сюда
        // попадает рендер, который пересобирает params каждый кадр.
        noPads.SetModifiers(new List<TerrainModifier> { Pad(PadLatDeg, PadLonDeg, 100d, 300d, 250d) }, PadRadius);
        noPads.SetModifiers(new List<TerrainModifier>(), PadRadius);
        Check(noPads.Mods.IsCreated, "T109 terrain-pads",
            "после добавления и удаления площадки таблица остаётся валидной");

        // 1. Мешающая проверка: естественный рельеф обязан быть неровным, иначе
        //    все проверки ниже проходят на пустом месте и ничего не значат.
        HeightfieldTerrain plain = MakePadTerrain();
        double natCenter = plain.GetRawHeightMeters(null, PadRad(PadLatDeg), PadRad(PadLonDeg));
        double nat250 = plain.GetRawHeightMeters(null, PadOffsetNorth(250d), PadRad(PadLonDeg));
        double nat500 = plain.GetRawHeightMeters(null, PadOffsetNorth(500d), PadRad(PadLonDeg));
        Check(Math.Abs(natCenter - nat250) > 0.5d && Math.Abs(nat250 - nat500) > 0.5d,
            "T109 terrain-pads",
            "естественный рельеф неровный (иначе тесты площадок нечего проверять)");

        // 1. БИТ-В-БИТ: без площадок рельеф не меняется ни на ульпу. Сверяем
        //    пустую таблицу и выключенную площадку — это разные ветки, и обе
        //    обязаны быть мёртвыми по выходу.
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

        // 2. Ровное ядро: целевая высота, заведомо отличная от естественной.
        const double target = 250d;
        HeightfieldTerrain padded = MakePadTerrain();
        padded.SetModifiers(
            new List<TerrainModifier> { Pad(PadLatDeg, PadLonDeg, 120d, 400d, target) }, PadRadius);

        double centerHeight = padded.GetRawHeightMeters(null, PadRad(PadLatDeg), PadRad(PadLonDeg));
        Check(Math.Abs(centerHeight - target) < 1e-9, "T109 terrain-pads",
            "в центре площадки высота РОВНО целевой (" + centerHeight.ToString("F6") + " м)");
        Check(Math.Abs(natCenter - target) > 1d, "T109 terrain-pads",
            "естественная высота в центре отличается от целевой (тест не тривиален)");

        // 3. Ядро ровное по всей площади: север/юг/восток/запад внутри InnerRadius.
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

        // 4. Фартук. Вес подмешивания идёт от 0 у внешнего края к 1 у ядра,
        //    поэтому отклонение от целевой УБЫВАЕТ к центру.
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

        // 4a. ФОРМА фартука, а не только «монотонный». Вес обязан быть ровно
        //     smoothstep: нулевые производные на обоих концах — ради этого
        //     фартук и делается гладким, иначе на границе площадки в нормалях
        //     появляется излом, а здания на краю встают с наклоном.
        //     Проверяем не «плавно вообще», а конкретное значение на четверти:
        //     smoothstep(0.25) = 0.15625. Линейная интерполяция дала бы 0.25,
        //     жёсткий диск — 0 или 1. Одно число отличает все три варианта.
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

        // На половине фартука вес обязан быть ровно 0.5 — симметрия кривой.
        double halfAngle = Math.Acos(cosOuterProbe - ((cosOuterProbe - cosInnerProbe) * 0.5d));
        double halfDistance = halfAngle * PadRadius;
        double natHalf = plain.GetRawHeightMeters(null, PadOffsetNorth(halfDistance), PadRad(PadLonDeg));
        double padHalf = padded.GetRawHeightMeters(null, PadOffsetNorth(halfDistance), PadRad(PadLonDeg));
        double halfWeight = (padHalf - natHalf) / (target - natHalf);
        Check(Math.Abs(halfWeight - 0.5d) < 1e-3, "T109 terrain-pads",
            "фартук симметричен (вес на половине " + halfWeight.ToString("F5") + ")");

        // 5. Снаружи площадки высота не тронута БИТ-В-БИТ.
        double naturalFar = plain.GetRawHeightMeters(null, PadOffsetNorth(900d), PadRad(PadLonDeg));
        Check(naturalFar == padded.GetRawHeightMeters(null, PadOffsetNorth(900d), PadRad(PadLonDeg)),
            "T109 terrain-pads", "за внешним радиусом рельеф не тронут БИТ-В-БИТ");

        // 6. Нормаль над ядром = зенит. Косвенно, но строго: если бы ядро было
        //    чуть неровным, соседи на базе нормали (≈114 м) разошлись бы.
        double hNorth = padded.GetRawHeightMeters(null, PadOffsetNorth(114d), PadRad(PadLonDeg));
        double hSouth = padded.GetRawHeightMeters(null, PadOffsetNorth(-114d), PadRad(PadLonDeg));
        double hEast = padded.GetRawHeightMeters(null, PadRad(PadLatDeg), PadOffsetEast(114d));
        Check(Math.Abs(hNorth - target) < 1e-9 && Math.Abs(hSouth - target) < 1e-9 && Math.Abs(hEast - target) < 1e-9,
            "T109 terrain-pads",
            "внутри ядра высоты всех трёх соседей равны целевой → нормаль = зенит");

        // 7. Площадка над водой переживает кламп уровня моря. Флаг не нужен:
        //    кламп толкает высоту ВВЕРХ до моря, а площадка выше моря.
        HeightfieldTerrain shorePad = MakePadTerrain();
        shorePad.SetModifiers(
            new List<TerrainModifier> { Pad(PadLatDeg, PadLonDeg, 100d, 300d, 15d) }, PadRadius);
        Check(Math.Abs(shorePad.GetRawHeightMeters(null, PadRad(PadLatDeg), PadRad(PadLonDeg)) - 15d) < 1e-9
            && Math.Abs(shorePad.GetHeightMeters(null, PadRad(PadLatDeg), PadRad(PadLonDeg)) - 15d) < 1e-9,
            "T109 terrain-pads", "площадка +15 м над морем не тонет в клампе");

        // 8. Раскопка НИЖЕ моря: без флага её залило бы водой, с флагом — нет.
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

        // 9. Сравнение таблиц. Одинаковое содержимое из разных сборок должно
        //    считаться РАВНЫМ: иначе ParamsEqual в рендере пересобирал бы кэш
        //    впустую, а настоящая правка модов могла бы не заметиться.
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

        // 10. Порядок в списке решает при пересечении, и результат детерминирован.
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

        // 11. Круглая форма. Проверять высоту в ядре бессмысленно — там она
        //     ровно целевая где угодно. Круглость видна только по ВЕСУ
        //     подмешивания в фартуке: он обязан быть один и тот же на равных
        //     расстояниях в любом азимуте. Вес восстанавливаем как
        //     (h_площадка − h_естеств) / (цель − h_естеств).
        //     Если бы площадка «сплющивалась» в одну сторону, вес разошёлся бы
        //     по азимутам — и это не видно на глаз в перспективе сцены.
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

        // 12. Круглость в пределах, а не только по четырём румбам: точка на
        //     диагонали (север+восток на 45°).
        double diagonal = probe / Math.Sqrt(2d);
        double weightDiagonal = (padded.GetRawHeightMeters(null, PadOffsetNorth(diagonal), PadOffsetEast(diagonal))
            - plain.GetRawHeightMeters(null, PadOffsetNorth(diagonal), PadOffsetEast(diagonal))) / denominator;
        Check(Math.Abs(weightDiagonal - weightAtNorth) < 1e-3, "T109 terrain-pads",
            "площадка круглая и по диагонали (вес " + weightDiagonal.ToString("F4") + ")");

        return 0;
    }
}
