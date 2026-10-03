using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// РОВНАЯ ПЛОЩАДКА В ТОЧКЕ ПЛАНЕТЫ — аналог PQS-мода FlattenArea из KSP,
    /// где под каждую площадку движок принудительно сглаживает шум.
    ///
    /// Зачем это нужно именно здесь: амплитуда рельефа Terra 9144 м с
    /// ridged-горным членом, и случайно выбранная точка чаще всего лежит на
    /// склоне 10-30°. Поставить там базу можно, ходить по ней — нет.
    /// Площадка решает это в корне: не «здание подстроилось под склон», а
    /// склон убран.
    ///
    /// ГЛАВНОЕ ПРЕИМУЩЕСТВО перед подгонкой объектов: правка вносится в
    /// ЕДИНСТВЕННУЮ функцию формы рельефа (TerrainNoise.SampleHeight), а значит
    /// автоматически достаётся рендер-мешу, физике корабля и игрока, нормалям,
    /// декорациям, глубине воды и редакторскому превью. Паритет визуал/физика
    /// не «поддерживается», а не может разойтись: расхождению негде взяться.
    ///
    /// Форма: ровное ядро радиусом InnerRadius и гладкий фартук
    /// InnerRadius→OuterRadius. Фартук обязателен — жёсткий диск дал бы у края
    /// видимую стену и излом в нормалях.
    /// </summary>
    [Serializable]
    public struct TerrainModifier
    {
        [Tooltip("Широта центра площадки, градусы.")]
        public double LatitudeDegrees;

        [Tooltip("Долгота центра площадки, градусы.")]
        public double LongitudeDegrees;

        [Tooltip("Радиус ровного ядра, м. Внутри него высота ТОЧНО целевая. " +
            "Держи ядро заметно шире 240 м: нормаль рельефа усредняется примерно по 114 м " +
            "(столько берёт посадка корабля), и на ядре уже 100 м здания на краю встанут " +
            "с наклоном в доли градуса.")]
        public double InnerRadiusMeters;

        [Tooltip("Радиус, на котором влияние сходит на нет, м. Фартук InnerRadius→OuterRadius.")]
        public double OuterRadiusMeters;

        [Tooltip("Высота площадки, м. Задаётся автора при размещении, а не вручную: " +
            "иначе площадка либо повиснет, либо уйдёт в яму.")]
        public double TargetHeightMeters;

        [Tooltip("Площадка перебивает кламп уровня моря. Нужно только для раскопок НИЖЕ уровня моря " +
            "(сухой док, котлован): иначе GetHeightMeters поднимет их обратно до воды. " +
            "Обычная площадка над водой работает и без этого флага.")]
        public bool OverridesSeaLevel;

        /// <summary>Модификатор выключен — им нельзя «случайно» испортить рельеф.</summary>
        public bool IsActive =>
            OuterRadiusMeters > 0d && !double.IsNaN(TargetHeightMeters) && !double.IsInfinity(TargetHeightMeters);
    }

    /// <summary>
    /// Пересчитанная форма модификатора для горячего цикла: направление вместо
    /// lat/lon и косинусы угловых радиусов вместо метров. Всё, что можно было
    /// посчитать один раз при сборке, посчитано — в SampleHeight остаётся одно
    /// скалярное произведение и одно сравнение на модификатор.
    ///
    /// Blittable по построению (double3 + double'ы + два byte), поэтому
    /// лежит в NativeArray и проходит через Burst-джобу рендера без
    /// managed-ссылок.
    /// </summary>
    public struct TerrainModifierData
    {
        /// <summary>Единичное тело-fixed направление центра площадки.</summary>
        public double3 Direction;

        /// <summary>cos(OuterRadius / Radius) — за этой границей модификатор не действует.</summary>
        public double CosOuter;

        /// <summary>cos(InnerRadius / Radius) — внутри этого значения влияние полное.</summary>
        public double CosInner;

        /// <summary>Целевая высота, м. Делится на амплитуду только для затронутых вершин.</summary>
        public double TargetHeightMeters;

        /// <summary>См. TerrainModifier.OverridesSeaLevel.</summary>
        public byte OverridesSeaLevel;
    }

    /// <summary>Сборка таблицы модификаторов и её сравнение — общая логика для кэшей.</summary>
    public static class TerrainModifiers
    {
        /// <summary>
        /// ПУСТАЯ, НО ВАЛИДНАЯ таблица. Общая на все тела и никогда не
        /// освобождается.
        ///
        /// Зачем она нужна, хотя «нет модификаторов» вроде бы означает «ничего не
        /// хранить»: планировщик job'ов Unity требует, чтобы ВСЕ контейнерные
        /// поля структуры джобы были валидны в момент Schedule — независимо от
        /// того, читает ли их код. На default(NativeArray) рендер падал целиком:
        /// «The UNKNOWN_OBJECT_TYPE TerrainTileJob.Params.Mods has not been
        /// assigned or constructed», планета пропадала. Проверка IsCreated внутри
        /// SampleHeight не спасает — до неё управление не доходит.
        ///
        /// Таблица нулевой длины, поэтому шарить её безопасно: памяти за ней
        /// нет, а защита job'ов для пустых контейнеров — no-op. Правило
        /// освобождения: таблица длины 0 всегда ЭТОТ объект, и Dispose её не
        /// трогает (см. Release).
        /// </summary>
        public static readonly NativeArray<TerrainModifierData> Empty =
            new NativeArray<TerrainModifierData>(0, Allocator.Persistent);

        /// <summary>
        /// Освободить таблицу. Нулевую не трогаем — это общий Empty, и его
        /// Dispose сломал бы все тела сразу.
        /// </summary>
        public static void Release(ref NativeArray<TerrainModifierData> table)
        {
            if (table.IsCreated && table.Length > 0)
            {
                table.Dispose();
            }

            table = Empty;
        }

        /// <summary>
        /// Пересобрать таблицу под тело. Возвращает <see cref="Empty"/>, если
        /// активных модификаторов нет: SampleHeight тогда работает ровно как
        /// раньше (контракт «пустые параметры дают бит-в-бит legacy-fBm», на
        /// нём стоят все тесты рельефа).
        /// </summary>
        public static NativeArray<TerrainModifierData> Build(
            System.Collections.Generic.IList<TerrainModifier> source, double bodyRadiusMeters)
        {
            int count = 0;
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    if (source[i].IsActive)
                    {
                        count++;
                    }
                }
            }

            if (count == 0 || bodyRadiusMeters <= 0d)
            {
                return Empty;
            }

            var table = new NativeArray<TerrainModifierData>(count, Allocator.Persistent);
            int slot = 0;
            for (int i = 0; i < source.Count; i++)
            {
                TerrainModifier m = source[i];
                if (!m.IsActive)
                {
                    continue;
                }

                double lat = m.LatitudeDegrees * (Math.PI / 180d);
                double lon = m.LongitudeDegrees * (Math.PI / 180d);
                double cosLat = Math.Cos(lat);

                // Угловой радиус не может превышать четверть окружности: за cos
                // начинает расти обратно, и «площадка радиуса 2R» превратилась бы
                // в кольцо на другой стороне планеты. Косинусы считаем от
                // дуговой длины, потому что и радиус площадки, и расстояние
                // между точками здесь — метры поверхности.
                double inner = Math.Min(Math.Max(m.InnerRadiusMeters / bodyRadiusMeters, 0d), Math.PI * 0.5d);
                double outer = Math.Min(Math.Max(m.OuterRadiusMeters / bodyRadiusMeters, inner), Math.PI * 0.5d);

                table[slot++] = new TerrainModifierData
                {
                    Direction = new double3(cosLat * Math.Cos(lon), cosLat * Math.Sin(lon), Math.Sin(lat)),
                    CosOuter = Math.Cos(outer),
                    CosInner = Math.Cos(inner),
                    TargetHeightMeters = m.TargetHeightMeters,
                    OverridesSeaLevel = (byte)(m.OverridesSeaLevel ? 1 : 0)
                };
            }

            return table;
        }

        /// <summary>
        /// Содержательное равенство таблиц. NativeArray == сравнивает указатель,
        /// а не данные, поэтому одинаковый по смыслу набор из разных сборок
        /// признавался бы разным и кэш рендера пересобирался бы впустую (а
        /// хуже — наоборот, реальное изменение модов могло бы не заметиться).
        /// </summary>
        public static bool TableEqual(NativeArray<TerrainModifierData> a, NativeArray<TerrainModifierData> b)
        {
            if (!a.IsCreated || !b.IsCreated)
            {
                return a.Length == b.Length;
            }

            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                TerrainModifierData x = a[i];
                TerrainModifierData y = b[i];
                if (!x.Direction.Equals(y.Direction)
                    || x.CosOuter != y.CosOuter
                    || x.CosInner != y.CosInner
                    || x.TargetHeightMeters != y.TargetHeightMeters
                    || x.OverridesSeaLevel != y.OverridesSeaLevel)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Подпись таблицы для хеша системы — чтобы правка мода пересобирала кэш.</summary>
        public static void MixInto(ref long hash, NativeArray<TerrainModifierData> table)
        {
            hash = (hash * 31L) + table.Length;
            for (int i = 0; i < table.Length; i++)
            {
                TerrainModifierData m = table[i];
                hash = (hash * 31L) + BitConverter.DoubleToInt64Bits(m.CosOuter);
                hash = (hash * 31L) + BitConverter.DoubleToInt64Bits(m.CosInner);
                hash = (hash * 31L) + BitConverter.DoubleToInt64Bits(m.TargetHeightMeters);
                hash = (hash * 31L) + m.OverridesSeaLevel;
            }
        }
    }
}
