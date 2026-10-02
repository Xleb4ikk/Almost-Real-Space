using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Galilego.Universe
{
    /// <summary>Базовый примитив шума рельефа.</summary>
    public enum TerrainNoiseStyle
    {
        /// <summary>
        /// Value noise: на 8 углах ячейки стоят СКАЛЯРЫ, они трилинейно
        /// смешиваются. Было историческим дефолтом проекта.
        /// </summary>
        Value = 0,

        /// <summary>
        /// Градиентный шум Перлина: на 8 углах стоят ВЕКТОРЫ, берётся их
        /// скалярное произведение со смещением от угла. Та же решётка и тот же
        /// хеш, что у value noise, поэтому все солты и частоты профиля остаются
        /// валидными — меняется только интерпретация хеша.
        /// </summary>
        Perlin = 1
    }

    /// <summary>Как собирается ridged-составляющая рельефа.</summary>
    public enum TerrainRidgedMode
    {
        /// <summary>
        /// Сумма октав (1 − |v|) с последующим smoothstep. Даёт параллельные
        /// складки: каждая октава независима, связи между ними нет.
        /// </summary>
        Legacy = 0,

        /// <summary>
        /// Ridged multifractal (Musgrave): каждая октава домножается на вес,
        /// унаследованный от предыдущей. Хребты ветвятся и сливаются в сеть,
        /// долины остаются плоскими — вместо равномерной ряби.
        /// </summary>
        Multifractal = 1
    }

    /// <summary>
    /// <summary>Источник наклона для домена октав (SlopeDamp) в форме рельефа.</summary>
    public enum TerrainSlopeDampMode
    {
        /// <summary>Домена нет: все октавы равновесны. Поведение до изменений.</summary>
        Off = 0,

        /// <summary>
        /// Наклон берётся из накопленного значения формы. Дёшево, но меряет
        /// ВЫСОТУ, а не крутизну: деталь уходит из долин и вершин, а средние
        /// высоты остаются изъеденными.
        /// </summary>
        Accum = 1,

        /// <summary>
        /// Наклон берётся из аналитической производной октавы. Меряет крутизну
        /// по-настоящему: плато остаются гладкими, склоны и гребни изъеденными.
        /// Требует градиентного примитива (TerrainNoiseStyle.Perlin) — у value
        /// noise производная разрывна на границах ячеек, и домен по ней дрожал бы
        /// на стыках. При Value этот режим вырождается в Accum.
        /// </summary>
        Gradient = 2
    }

    /// <summary>
    /// Плоские параметры шума для Burst-job: только blittable-поля, без ссылок.
    /// Все double (касты float запрещены): внутренности шума обязаны идти в
    /// double — float не держит схему оффсетов (seed·91.7 при warp-offset уже
    /// ~5e6, ulp ~0.5; замер T84: 827 м расхождения). Float — только массивы
    /// масок на выходе (цвет физику не касается).
    /// </summary>
    public struct TerrainNoiseParams
    {
        public int Seed;
        public double BaseFrequency;
        public int Octaves;
        public double Lacunarity;
        public double Gain;
        public double ContinentFrequency;
        public int ContinentOctaves;
        public double ContinentThreshold;
        public double ContinentSharpness;
        public double ContinentDepth;
        public double RidgedMix;
        public int RidgedMode;
        public double RidgedSharpness;
        public double RidgedWeightGain;
        public double RidgedGamma;
        public double SlopeDamp;
        public int SlopeDampMode;
        public int NoiseStyle;

        /// <summary>
        /// Примитив для МАСОК и warp, отдельно от формы рельефа. По умолчанию
        /// следует NoiseStyle. Может быть поставлен в Value, чтобы срезать цену:
        /// маска continent читается как квантиль, и после нормировки по RMS (T111)
        /// примитив на её долю суши не влияет, а стоит заметно дешевле — value
        /// noise втрое быстрее градиентного. Форма при этом остаётся на Перлине.
        ///
        /// Сентинел «следуй за NoiseStyle» — минус единица, разбирается в
        /// FromTerrain. Поле намеренно БЕЗ инициализатора: проект на C# 9.0, где
        /// инициализаторы полей структуры недоступны, а стенд на .NET 8 их
        /// компилировал и тем самым скрывал несовместимость. Прямой
        /// `new TerrainNoiseParams()` даёт здесь 0, то есть Value, — поэтому
        /// параметры собираются через FromTerrain, как делает весь продакшн.
        /// </summary>
        public int MaskNoiseStyle;

        public double PlainMix;
        public double PlainFrequency;
        public int PlainOctaves;
        public double PlainThreshold;
        public double PlainSharpness;
        public double PlainElevation;

        /// <summary>
        /// Сжатие верхнего хвоста формы, выраженное в нормированных единицах
        /// (как ContinentDepth, НЕ в метрах). Нужно ridged multifractal: у него
        /// низкая типичная высота и тяжёлый хвост, поэтому калибровка по p99
        /// задирает вершины. Здесь сжатие применяется к далёкому хвосту и
        /// почти не трогает основную массу распределения.
        ///
        /// TailKnee <= 0 — сжатие выключено, форма не меняется. Это значение
        /// по умолчанию, и на нём legacy-профиль остаётся бит-в-бит прежним.
        /// </summary>
        public double TailKnee;

        /// <summary>Порог сжатия в нормированных единицах. Ниже него форма не тронута.</summary>
        public double TailThreshold;

        /// <summary>
        /// Сжатие нижнего (океанского) хвоста, в нормированных единицах, по
        /// образцу TailKnee. DepthKnee &lt;= 0 — выключено (по умолчанию).
        ///
        /// Нужно потому, что у ridged-профиля океанский хвост ещё тяжелее
        /// горного: −25 045 м при средней глубине −4 399 м, то есть дно имеет
        /// и лёгкую массу, и очень глубокий хвост. Уменьшение ContinentDepth
        /// поднимает оба сразу и заодно топит шельф, поэтому точечнее резать
        /// именно хвост.
        /// </summary>
        public double DepthKnee;

        /// <summary>Порог нижнего сжатия, нормированные единицы. Выше него
        /// форма не тронута (сжатие одностороннее и вниз).</summary>
        public double DepthThreshold;

        public double DetailMix;
        public double DetailFrequency;
        public int DetailOctaves;
        public double WarpStrength;
        public double WarpFrequency;
        public int WarpOctaves;
        public int WarpSeedOffset;

        /// <summary>Уровень моря (м) — для береговой полки (нормировка высот).</summary>
        public double SeaLevelMeters;

        /// <summary>Амплитуда рельефа (м) — для береговой полки (нормировка высот).</summary>
        public double AmplitudeMeters;

        /// <summary>Высота полки пляжа над морем (м). 0 = полка выключена.</summary>
        public double BeachShelfAltitudeMeters;

        /// <summary>Полуширина полки в единицах continent-маски. 0 = выключена.</summary>
        public double BeachShelfWidth;

        /// <summary>Макс. множитель ширины полки (шум по берегу). ≤1 = ширина постоянна.</summary>
        public double BeachShelfWidthMaxScale;

        /// <summary>Частота шума ширины полки (циклов на единичный вектор). 0 = выключен.</summary>
        public double BeachShelfWidthNoiseFrequency;

        /// <summary>Октав шума ширины полки.</summary>
        public int BeachShelfWidthNoiseOctaves;
        public double ColorNoiseFrequency;
        public int ColorNoiseOctaves;
        public int ColorNoiseSeedOffset;
        public double ColorDetailFrequency;
        public int ColorDetailOctaves;
        public int ColorDetailSeedOffset;

        /// <summary>Считать ли маску в job'е (иначе пишет 0). Экономит ~15% при выключенной маске.</summary>
        public bool ComputeMask;

        /// <summary>Считать ли мелкомасштабную цветовую деталь (моттлинг земли).</summary>
        public bool ComputeDetail;

        /// <summary>
        /// Ровные площадки в точках планеты (аналог PQS-мода FlattenArea в KSP).
        /// NativeArray — blittable, поэтому таблица проходит в эту структуру и
        /// дальше в [BurstCompile]-джобу рендера без managed-ссылок и без
        /// изменения подписей: джоба уже получает Params целиком.
        ///
        /// [ReadOnly] — НЕ косметика, а требование безопасности. Таблица
        /// читается и никогда не пишется, но контейнерное поле джобы без этой
        /// метки защита считает ЗАПИСЫВАЕМЫМ. А декорации строятся пачками:
        /// StepDecorBuilds планирует несколько GroundDecorCandidateJob в одном
        /// кадре с default(JobHandle), то есть БЕЗ зависимости друг на друга, и
        /// все получают один и тот же TerrainNoiseParams. Запись в общий
        /// контейнер без зависимости — исключение защиты job'ов, и оно сыпется
        /// каждый кадр:
        ///   «The previously scheduled job ... writes to ...Terrain.Mods. You
        ///    are trying to schedule a new job ... To guarantee safety, you must
        ///    include ... as a dependency».
        /// Это случилось и с пустой общей таблицей: защита работает по
        /// разметке поля, а не по длине массива.
        ///
        /// Владеет таблицей HeightfieldTerrain, а не этот метод: FromTerrain
        /// зовётся на КАЖДЫЙ GetRawHeightMeters (тысячи раз в секунду), и
        /// аллоцировать здесь нельзя — копируется только дескриптор.
        /// Таблица нулевой длины = модификаторов нет, и SampleHeight идёт по
        /// старой ветке (бит-в-бит legacy).
        /// </summary>
        [ReadOnly]
        public NativeArray<TerrainModifierData> Mods;

        public static TerrainNoiseParams FromTerrain(HeightfieldTerrain terrain)
        {
            // Домен по склону калиброван под Gain*Lacunarity == 1. При нарушении
            // окна неверны, и домен молча вырождается в no-op или в константу -
            // то есть профиль выглядит сломанным, а данные им не сломаны.
            // Поэтому здесь домен выключается ЯВНО, с одним логом на конфигурацию.
            //
            // Почему не пересчитывать окна на лету: правильные окна зависят от
            // распределения сигнала по всей сфере, то есть считаются перебором.
            // Считать их в FromTerrain нельзя - метод зовётся на каждом
            // GetRawHeightMeters, тысячи раз в секунду. Гасить домен дешевле и
            // предсказуемее, чем молча подгонять форму рельефа.
            double damp = terrain.SlopeDamp;
            int dampMode = terrain.SlopeDampMode;
            if (damp > 0d && dampMode != (int)TerrainSlopeDampMode.Off
                && !TerrainNoise.SlopeDampWindowsValid(terrain.Gain, terrain.Lacunarity))
            {
                TerrainNoise.LogDampWindowsOnce(terrain);
                damp = 0d;
                dampMode = (int)TerrainSlopeDampMode.Off;
            }

            return new TerrainNoiseParams
            {
                Seed = terrain.Seed,
                BaseFrequency = terrain.BaseFrequency,
                Octaves = terrain.Octaves,
                Lacunarity = terrain.Lacunarity,
                Gain = terrain.Gain,
                ContinentFrequency = terrain.ContinentFrequency,
                ContinentOctaves = terrain.ContinentOctaves,
                ContinentThreshold = terrain.ContinentThreshold,
                ContinentSharpness = terrain.ContinentSharpness,
                ContinentDepth = terrain.ContinentDepth,
                RidgedMix = terrain.RidgedMix,
                RidgedMode = terrain.RidgedMode,
                RidgedSharpness = terrain.RidgedSharpness,
                RidgedWeightGain = terrain.RidgedWeightGain,
                RidgedGamma = terrain.RidgedGamma,
                SlopeDamp = damp,
                SlopeDampMode = dampMode,
                NoiseStyle = terrain.NoiseStyle,
                MaskNoiseStyle = terrain.MaskNoiseStyle >= 0
                    ? terrain.MaskNoiseStyle
                    : terrain.NoiseStyle,
                PlainMix = terrain.PlainMix,
                PlainFrequency = terrain.PlainFrequency,
                PlainOctaves = terrain.PlainOctaves,
                PlainThreshold = terrain.PlainThreshold,
                PlainSharpness = terrain.PlainSharpness,
                PlainElevation = terrain.PlainElevation,
                TailKnee = terrain.TailKnee,
                TailThreshold = terrain.TailThreshold,
                DepthKnee = terrain.DepthKnee,
                DepthThreshold = terrain.DepthThreshold,
                DetailMix = terrain.DetailMix,
                DetailFrequency = terrain.DetailFrequency,
                DetailOctaves = terrain.DetailOctaves,
                WarpStrength = terrain.WarpStrength,
                WarpFrequency = terrain.WarpFrequency,
                WarpOctaves = terrain.WarpOctaves,
                WarpSeedOffset = terrain.WarpSeedOffset,
                SeaLevelMeters = terrain.SeaLevelMeters,
                AmplitudeMeters = terrain.AmplitudeMeters,
                BeachShelfAltitudeMeters = terrain.BeachShelfAltitudeMeters,
                BeachShelfWidth = terrain.BeachShelfWidth,
                BeachShelfWidthMaxScale = terrain.BeachShelfWidthMaxScale,
                BeachShelfWidthNoiseFrequency = terrain.BeachShelfWidthNoiseFrequency,
                BeachShelfWidthNoiseOctaves = terrain.BeachShelfWidthNoiseOctaves,
                ColorNoiseFrequency = terrain.ColorNoiseFrequency,
                ColorNoiseOctaves = terrain.ColorNoiseOctaves,
                ColorNoiseSeedOffset = terrain.ColorNoiseSeedOffset,
                ColorDetailFrequency = terrain.ColorDetailFrequency,
                ColorDetailOctaves = terrain.ColorDetailOctaves,
                ColorDetailSeedOffset = terrain.ColorDetailSeedOffset,
                ComputeMask = terrain.ColorNoiseFrequency > 0d && terrain.ColorNoiseStrength != 0d,
                ComputeDetail = terrain.ColorDetailFrequency > 0d && terrain.ColorDetailStrength != 0d,
                Mods = terrain.Mods
            };
        }
    }

    /// <summary>
    /// Burst-совместимая форма heightfield: та же формула, что HeightfieldTerrain
    /// (порядок операций зеркальный — расхождение только в последнем ulp
    /// кодогенерации, меряет T84). Чистые статики на double3/math: ни ссылок,
    /// ни аллокаций, ни System.Math. HeightfieldTerrain делегирует сюда форму —
    /// дублирования нет, паритет визуал/физика по построению, а не допуском.
    /// </summary>
    public static class TerrainNoise
    {
        /// <summary>
        /// Амплитуда собственного шума равнины, доля от PlainElevation. Без него
        /// стягивание к константе даёт идеально ровный стол с острыми ребрами на
        /// границе маски; 0.03 даёт уклон ~0.1°, что читается как равнина.
        /// </summary>
        private const double PlainRelief = 0.03d;

        /// <summary>Октав в шуме равнины. 4 — как замеренный поток.</summary>
        private const int PlainReliefOctaves = 4;

        public static double SampleHeight(TerrainNoiseParams p, double3 direction)
        {
            double gain = EffectiveGain(p.Gain);
            double lacunarity = EffectiveLacunarity(p.Lacunarity);
            if (p.WarpStrength <= 0d && p.RidgedMix <= 0d && p.ContinentFrequency <= 0d
                && (p.PlainMix <= 0d || p.PlainFrequency <= 0d)
                && (p.DetailMix <= 0d || p.DetailFrequency <= 0d)
                && lacunarity == 2d && gain == 0.5d)
            {
                return SampleFbmLegacy(p, direction);
            }

            double3 q = direction;
            if (p.WarpStrength > 0d)
            {
                q = ApplyWarp(p, q, gain, lacunarity);
            }

            double baseHeight = SampleShapeFbm(p, q, p.BaseFrequency, p.Octaves, 0, gain, lacunarity);

            double continent = 1d;
            double continentRaw = 0d;
            if (p.ContinentFrequency > 0d)
            {
                continentRaw = SampleFbmEx(q, p.ContinentFrequency, p.ContinentOctaves, p.Seed, 1, gain, lacunarity, p.MaskNoiseStyle);
                continent = Smoothstep01((continentRaw - (p.ContinentThreshold - p.ContinentSharpness))
                    / math.max(1e-9d, 2d * p.ContinentSharpness));
            }

            double h = baseHeight;
            if (p.RidgedMix > 0d)
            {
                double ridged = (SampleRidged(p, q, gain, lacunarity) * 2d) - 1d;
                double k = math.min(1d, math.max(0d, p.RidgedMix)) * continent;
                h = baseHeight + (ridged - baseHeight) * k;
            }

            h -= (1d - continent) * math.max(0d, p.ContinentDepth);

            double plainK = 0d;
            if (p.PlainMix > 0d && p.PlainFrequency > 0d)
            {
                // Равнины: низкочастотная маска (свой поток, salt 5) выделяет зоны,
                // где рельеф стягивается к низкому плато; между зонами остаются
                // хребты. Множитель continent — равнины только на суше.
                double plainNoise = SampleFbmEx(q, p.PlainFrequency, p.PlainOctaves, p.Seed, 5, gain, lacunarity, p.MaskNoiseStyle);
                double plainMask = Smoothstep01((plainNoise - (p.PlainThreshold - p.PlainSharpness))
                    / math.max(1e-9d, 2d * p.PlainSharpness));
                plainK = math.min(1d, math.max(0d, p.PlainMix)) * plainMask * continent;
                // Цель стягивания — не ровная полка, а собственные низкие холмы.
                // Берём первые PlainReliefOctaves октав той же формы (p.BaseFrequency,
                // соль 0): уклон выходит ~0.1° вместо идеального стола, и равнина
                // не наследует высокие октавы гор, как было при стягивании к
                // константе. Соль 0 общая с базовой формой намеренно: первые октавы
                // совпадают, поэтому полки лежат в долинах рельефа, а не в отрыве
                // от него.
                double flat = p.PlainElevation + PlainRelief * SampleFbmEx(
                    q, p.BaseFrequency, PlainReliefOctaves, p.Seed, 0, gain, lacunarity, p.NoiseStyle);
                h = flat + ((h - flat) * (1d - plainK));
            }

            if (p.DetailMix > 0d && p.DetailFrequency > 0d)
            {
                // Мелкомасштабная деталь (скалы/осыпи): высокочастотный свой поток
                // (salt 6), абсолютная доля амплитуды. Только на суше и гаснет в
                // равнинах — хребты становятся изрезанными, равнины остаются гладкими.
                double detail = SampleShapeFbm(p, q, p.DetailFrequency, p.DetailOctaves, 6, gain, lacunarity);
                double detailLand = continent * (1d - plainK);
                h += detail * p.DetailMix * detailLand;
            }

            h = ApplyTailCompression(p, h);
            h = ApplyDepthCompression(p, h);
            h = ApplyBeachShelf(p, h, continentRaw, q);

            // Ровные площадки — ПОСЛЕДНЕЙ операцией. Если применить их раньше,
            // фартук площадки затянуло бы обратно шумом детали/равнин, и ровное
            // ядро перестало бы быть ровным. Таблица модификаторов идёт по
            // собственному направлению, поэтому в warp/маску не вмешивается.
            if (p.Mods.IsCreated && p.Mods.Length > 0)
            {
                h = ApplyModifiers(p, h, direction);
            }

            return h;
        }

        /// <summary>
        /// Мягкое сжатие далёкого верхнего хвоста формы.
        ///
        /// Зачем. У ridged multifractal распределение с низкой типичной
        /// высотой и тяжёлым хвостом: p99 и максимум почти не коррелируют.
        /// Калибровать амплитуду по p99 - значит задирать вершины (у Perlin
        /// 10.9 км при p99 5.9 км), а резать хвост гаммой - значит сплющить
        /// гребни по всей длине, что видно на равнинах тоже. Здесь режется
        /// ТОЛЬКО хвост выше порога, то есть меньше 0.1% точек.
        ///
        /// Форма. Рациональное (коши) колено:
        ///     e = x - t;   y = t + e / (1 + e / TailKnee)
        /// Свойства: строго монотонна; на пороге наклон ровно 1, то есть
        /// первая производная непрерывна и ребра не появляется (вторая
        /// производная скачет, поэтому вторая производная для нормалей и не
        /// нужна); при e -> бесконечности y -> t + TailKnee, то есть у
        /// хвоста есть жёсткий потолок, а не просто "медленнее".
        ///
        /// Параметра крутины здесь нет СОЗНАТЕЛЬНО. Первая версия вводила ещё
        /// и TailCompress, но он входил только произведением с TailKnee, то
        /// есть просто дублировал ручку и давал мнимую настройку. Вдобавок
        /// ужесточение колена прижимает верхушку к потолку и рискует сделать
        /// "плоскую макушку" - ровно тот артефакт, которого здесь надо
        /// избежать. Мягкое колено к нему не склонно, поэтому хватает одной
        /// ручки: TailKnee = сколько ещё разрешено вырасти над порогом.
        ///
        /// Ставится ДО пляжа и ДО площадок: площадка задаёт высоту автором
        /// абсолютно, и сжатие после неё сдвинуло бы плиту с её уровня.
        /// Океанский хвост не трогается - порог односторонний, поэтому
        /// глубина моря не меняется.
        /// </summary>
        internal static double ApplyTailCompression(TerrainNoiseParams p, double h)
        {
            if (!(p.TailKnee > 0d) || h <= p.TailThreshold)
            {
                return h;
            }

            double e = h - p.TailThreshold;
            return p.TailThreshold + (e / (1d + (e / p.TailKnee)));
        }

        /// <summary>
        /// Мягкое сжатие нижнего (океанского) хвоста формы — зеркало
        /// ApplyTailCompression. Та же рациональная форма, но вниз:
        ///     u = t - x;   y = t - u / (1 + u / DepthKnee),  при x &lt; t
        /// Свойства те же: строго монотонна, наклон на пороге ровно 1 (первая
        /// производная непрерывна, ребра на шельфе не появляется), дно
        /// асимптотически упирается в t - DepthKnee.
        ///
        /// Односторонняя и вниз: точки выше порога не тронуты, поэтому
        /// береговая линия, SeaLevelMeters и пляжная полка не меняются, а
        /// поднимается только глубокое ложе. Ставится в том же месте, что и
        /// верхнее сжатие, - до пляжа и площадок.
        ///
        /// DepthKnee &lt;= 0 — выключено, и это значение по умолчанию: legacy
        /// профиль должен остаться бит-в-бит прежним.
        /// </summary>
        internal static double ApplyDepthCompression(TerrainNoiseParams p, double h)
        {
            if (!(p.DepthKnee > 0d) || h >= p.DepthThreshold)
            {
                return h;
            }

            double u = p.DepthThreshold - h;
            return p.DepthThreshold - (u / (1d + (u / p.DepthKnee)));
        }

        /// <summary>
        /// Площадки: ровное ядро и гладкий фартук по естественной высоте.
        ///
        /// Всё в НОРМИРОВАННОМ пространстве (SampleHeight возвращает форму
        /// ~[−1,1], множитель амплитуды ставит вызывающий). Деление на
        /// амплитуду делается только для модификаторов, которые реально
        /// задели эту точку — после раннего выхода по косинусу, поэтому в
        /// горячем цикле оно не стоит ни одного лишнего деления.
        ///
        /// Пересечения: модификаторы применяются по порядку в списке, каждый
        /// считает от результата предыдущего. Порядок детерминирован, значит
        /// результат воспроизводим; последний в списке при пересечении выигрывает.
        /// </summary>
        public static double ApplyModifiers(TerrainNoiseParams p, double h, double3 direction)
        {
            NativeArray<TerrainModifierData> mods = p.Mods;
            double amplitude = p.AmplitudeMeters;
            if (!(amplitude > 0d))
            {
                return h;
            }

            for (int i = 0; i < mods.Length; i++)
            {
                TerrainModifierData m = mods[i];

                // Скалярное произведение убывает с угловым расстоянием, поэтому
                // одно сравнение отсекает всё, что вне площадки, без acos.
                double dot = math.dot(direction, m.Direction);
                if (dot < m.CosOuter)
                {
                    continue;
                }

                // Ядро: t = 1. Фартук: 0 у внешнего края → 1 у внутреннего.
                double t = 1d;
                if (dot < m.CosInner)
                {
                    double span = m.CosInner - m.CosOuter;
                    t = span > 1e-15d ? (dot - m.CosOuter) / span : 1d;
                    t = math.min(1d, math.max(0d, t));
                }

                // Smoothstep: нулевые производные на обоих концах фартука, иначе
                // на границе площадки в нормалях появляется излом.
                t = t * t * (3d - (2d * t));

                double target = m.TargetHeightMeters / amplitude;
                h += (target - h) * t;
            }

            return h;
        }

        /// <summary>
        /// Есть ли здесь площадка, которой разрешено перебивать кламп уровня
        /// моря. Нужно ровно для раскопок ниже моря: GetHeightMeters поднимает
        /// всё ниже уровня моря обратно к воде, и без этой проверки сухой док
        /// молча наполнился бы.
        /// </summary>
        public static bool ModsOverrideSeaLevel(TerrainNoiseParams p, double3 direction)
        {
            NativeArray<TerrainModifierData> mods = p.Mods;
            if (!mods.IsCreated)
            {
                return false;
            }

            for (int i = 0; i < mods.Length; i++)
            {
                TerrainModifierData m = mods[i];
                if (m.OverridesSeaLevel == 0)
                {
                    continue;
                }

                if (math.dot(direction, m.Direction) >= m.CosOuter)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Береговая полка: у уреза воды (сырая continent-маска рядом с порогом
        /// со стороны суши) суша стягивается к пологой высоте над морем — иначе
        /// берег прыгает из воды сразу на склон/равнину и полосе пляжа негде
        /// лечь. Только суша (h выше моря): океанское дно не трогаем, иначе
        /// всплыли бы острова; без моря (SeaLevel −∞) и без континентов полка
        /// выключена. Все пороги в нуле — возврат h без изменений (legacy
        /// бит-в-бит). Вес — гладкий купол с нулевыми производными на краях,
        /// чтобы в нормалях не было излома.
        /// </summary>
        public static double ApplyBeachShelf(TerrainNoiseParams p, double h, double continentRaw)
        {
            // Старая сигнатура: ширина полки постоянна (без шума по берегу).
            return ApplyBeachShelfCore(p, h, continentRaw, false, default(double3));
        }

        /// <summary>
        /// Береговая полка с неравномерной шириной: <paramref name="direction"/> —
        /// точка на сфере (после warp), по ней считается шум ширины. Если
        /// BeachShelfWidthMaxScale ≤ 1 — бит-в-бит как ApplyBeachShelf без direction.
        /// </summary>
        public static double ApplyBeachShelf(TerrainNoiseParams p, double h, double continentRaw, double3 direction)
        {
            return ApplyBeachShelfCore(p, h, continentRaw, true, direction);
        }

        /// <summary>Контраст шума ширины пляжа: n·2.2 растягивает fBm (σ≈0.3) почти на весь диапазон 1×..Max×.</summary>
        private const double BeachWidthContrast = 2.2d;

        /// <summary>Gain шума ширины пляжа: выше обычного 0.5, чтобы мелкие октавы давали заметную локальную неровность.</summary>
        private const double BeachWidthNoiseGain = 0.6d;

        /// <summary>
        /// Множитель ширины полки в данной точке: от 1 до BeachShelfWidthMaxScale.
        /// Интерполяция ЛОГАРИФМИЧЕСКАЯ (Max^x), поэтому значения распределены
        /// геометрически: на карте есть и участки «в 2–3 раза», и «в 7», и
        /// «в 10–15 раз шире» базовой ширины. Гладкая по направлению — изломов нет.
        /// </summary>
        public static double BeachWidthScale(TerrainNoiseParams p, double3 direction)
        {
            double maxScale = p.BeachShelfWidthMaxScale;
            if (!(maxScale > 1d) || !(p.BeachShelfWidthNoiseFrequency > 0d))
            {
                return 1d;
            }

            int octaves = p.BeachShelfWidthNoiseOctaves < 1 ? 1 : p.BeachShelfWidthNoiseOctaves;
            double n = SampleFbmEx(
                direction, p.BeachShelfWidthNoiseFrequency, octaves, p.Seed, 9,
                BeachWidthNoiseGain, 2d, p.MaskNoiseStyle);
            double x = Smoothstep01(0.5d + (0.5d * math.clamp(n * BeachWidthContrast, -1d, 1d)));
            return math.exp(math.log(maxScale) * x);
        }

        private static double ApplyBeachShelfCore(
            TerrainNoiseParams p, double h, double continentRaw, bool varyWidth, double3 direction)
        {
            if (p.BeachShelfWidth <= 0d || p.BeachShelfAltitudeMeters <= 0d
                || p.ContinentFrequency <= 0d || p.SeaLevelMeters <= -1e29d)
            {
                return h;
            }

            double amp = math.max(1d, p.AmplitudeMeters);
            double seaN = p.SeaLevelMeters / amp;
            if (!(h > seaN))
            {
                return h;
            }

            double above = continentRaw - p.ContinentThreshold;
            if (!(above > 0d))
            {
                return h;
            }

            double width = math.max(1e-9d, p.BeachShelfWidth);
            if (varyWidth)
            {
                // Шум считаем только на суше у/над берегом (после ранних выходов).
                width *= BeachWidthScale(p, direction);
            }

            double coastDist = above / width;
            double t = math.min(1d, coastDist * coastDist);
            double w = (1d - t) * (1d - t);
            double shelf = seaN + (p.BeachShelfAltitudeMeters / amp);
            return h + ((shelf - h) * w);
        }

        public static double SampleColorNoise(TerrainNoiseParams p, double3 direction)
        {
            double gain = EffectiveGain(p.Gain);
            double lacunarity = EffectiveLacunarity(p.Lacunarity);
            return SampleFbmEx(
                direction, p.ColorNoiseFrequency, p.ColorNoiseOctaves,
                p.Seed + (p.ColorNoiseSeedOffset * 7919), 4, gain, lacunarity, p.MaskNoiseStyle);
        }

        /// <summary>
        /// Мелкомасштабная цветовая деталь ~[−1,1] (моттлинг земли: пятна
        /// почвы/света), свой поток (salt 7). Цвет физику не касается.
        /// </summary>
        public static double SampleColorDetailNoise(TerrainNoiseParams p, double3 direction)
        {
            double gain = EffectiveGain(p.Gain);
            double lacunarity = EffectiveLacunarity(p.Lacunarity);
            return SampleFbmEx(
                direction, p.ColorDetailFrequency, p.ColorDetailOctaves,
                p.Seed + (p.ColorDetailSeedOffset * 7919), 7, gain, lacunarity, p.MaskNoiseStyle);
        }

        /// <summary>
        /// Шум распределения декора (кластеры) ~[−1,1], свой поток (salt 8):
        /// не коррелирует ни с формой, ни с цветовой маской. Сид — от рельефа
        /// тела + оффсет слоя, поэтому слои и планеты не совпадают.
        /// </summary>
        public static double SampleDecorNoise(
            TerrainNoiseParams terrain, int seedOffset, double frequency, int octaves, double3 direction)
        {
            double gain = EffectiveGain(terrain.Gain);
            double lacunarity = EffectiveLacunarity(terrain.Lacunarity);
            return SampleFbmEx(
                direction, frequency, octaves,
                terrain.Seed + (seedOffset * 7919), 8, gain, lacunarity,
                terrain.MaskNoiseStyle >= 0 ? terrain.MaskNoiseStyle : terrain.NoiseStyle);
        }

        public static double EffectiveGain(double gain)
        {
            return gain > 0d && gain <= 1d ? gain : 0.5d;
        }

        public static double EffectiveLacunarity(double lacunarity)
        {
            return lacunarity >= 1d && lacunarity <= 8d ? lacunarity : 2d;
        }

        /// <summary>
        /// Пригодны ли окна домена для этих Gain/Lacunarity.
        ///
        /// Сигнал домена - (slopeAccum/(o+1))/BaseFrequency, и нормировка на
        /// (o+1) имеет смысл ТОЛЬКО если каждая октава вносит одинаковый вклад
        /// в накопленный наклон. Вклад октавы o пропорционален
        /// amplitude * frequency = gain^o * lacunarity^o = (gain*lacunarity)^o,
        /// то есть он постоянен ровно при Gain * Lacunarity == 1. При текущих
        /// 0.5 * 2 это выполнено, и окна 1.35..2.05 / 0.05..0.45 откалиброваны
        /// именно под это.
        ///
        /// Если произведение уедет (например Gain подняли до 0.6 и оставили
        /// Lacunarity = 2), вклады начинают расти геометрически, нормировка
        /// /(o+1) перестаёт делить на константу, сигнал уезжает из окна - и
        /// домен молча вырождается в no-op или в безвредную константу. Ни
        /// Assert, ни исключение здесь невозможны: код уходит в Burst-джобу, а
        /// Debug.Assert в Burst не компилируется. Поэтому проверка живёт в
        /// тесте (T131_DampWindowsValid), а здесь только предикат.
        /// </summary>
        public static bool SlopeDampWindowsValid(double gain, double lacunarity)
        {
            return math.abs((EffectiveGain(gain) * EffectiveLacunarity(lacunarity)) - 1d) < 1e-9d;
        }

        /// <summary>
        /// Один лог на каждую встретившуюся пару Gain/Lacunarity. FromTerrain
        /// зовётся тысячи раз в секунду, поэтому логать каждый раз нельзя - это
        /// сам по себе способ положить кадр.
        ///
        /// Хранилище - фиксированный массив на 8 ключей без аллокаций и без
        /// хеш-структур: в проекте одновременно живёт несколько профилей, а не
        /// тысячи, и 8 хватает с запасом. При переполнении логируется снова -
        /// это правильнее, чем замолчать навсегда.
        ///
        /// Ключ - два целых по 1/1024, этого достаточно: в профилях значения
        /// круглые (0.5, 0.6, 2, 2.5), а 1/1024 их различает.
        /// </summary>
        private static readonly long[] dampWindowsLogged = new long[8];

        internal static void LogDampWindowsOnce(HeightfieldTerrain terrain)
        {
            long key = ((long)(int)System.Math.Round(EffectiveGain(terrain.Gain) * 1024d) << 32)
                | (uint)(int)System.Math.Round(EffectiveLacunarity(terrain.Lacunarity) * 1024d);

            for (int i = 0; i < dampWindowsLogged.Length; i++)
            {
                if (dampWindowsLogged[i] == key)
                {
                    return;
                }
            }

            for (int i = 0; i < dampWindowsLogged.Length; i++)
            {
                if (dampWindowsLogged[i] == 0L)
                {
                    dampWindowsLogged[i] = key;
                    break;
                }
            }

            UnityEngine.Debug.LogWarning(
                "[TerrainNoise] SlopeDamp отключён: Gain " + terrain.Gain.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                + " * Lacunarity " + terrain.Lacunarity.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                + " = " + (EffectiveGain(terrain.Gain) * EffectiveLacunarity(terrain.Lacunarity)).ToString("F4", System.Globalization.CultureInfo.InvariantCulture)
                + " != 1, а окна домена откалиброваны под произведение 1.");
        }

        private static double SampleFbmLegacy(TerrainNoiseParams p, double3 direction)
        {
            int octaves = p.Octaves < 1 ? 1 : p.Octaves;
            double amplitude = 1d;
            double frequency = p.BaseFrequency < 1e-6d ? 1e-6d : p.BaseFrequency;
            double sum = 0d;
            double norm = 0d;
            double3 offset = new double3(p.Seed * 17.31d, p.Seed * 7.77d, p.Seed * 29.13d);
            for (int o = 0; o < octaves; o++)
            {
                sum += amplitude * Octave(p, (direction * frequency) + offset);
                norm += amplitude;
                amplitude *= 0.5d;
                frequency *= 2d;
                offset = new double3(offset.y + 19.19d, offset.z + 7.47d, offset.x + 3.13d);
            }

            return norm > 0d ? sum / norm : 0d;
        }

        private static double3 ApplyWarp(TerrainNoiseParams p, double3 direction, double gain, double lacunarity)
        {
            double frequency = p.WarpFrequency < 1e-6d ? 1e-6d : p.WarpFrequency;
            int octaves = p.WarpOctaves < 1 ? 1 : p.WarpOctaves;
            int seed = p.Seed + (p.WarpSeedOffset * 7919);
            double3 warp = new double3(
                SampleFbmEx(direction, frequency, octaves, seed, 100, gain, lacunarity, p.MaskNoiseStyle),
                SampleFbmEx(direction, frequency, octaves, seed, 200, gain, lacunarity, p.MaskNoiseStyle),
                SampleFbmEx(direction, frequency, octaves, seed, 300, gain, lacunarity, p.MaskNoiseStyle));
            return direction + (warp * p.WarpStrength);
        }

        /// <summary>
        /// Обычный fBm, без домена. Им собираются ВСЁ, кроме самой формы: маски
        /// континентов и равнин, warp, цветовая маска и деталь, шум декора.
        ///
        /// Домен по октавам сюда намеренно не вставлен: маска continent сдвинула
        /// бы береговую линию и статистику суши, на которой стоят T87 (доля суши
        /// в 0.15..0.85) и T101 (полка у уреза воды). Маска — это не рельеф, ей
        /// нужна ровная статистика, а не изъеденные склоны.
        /// </summary>
        /// <summary>Обёртка: все маски, кроме формы, зовут одну и ту же функцию.</summary>
        private static double SampleFbmEx(double3 direction, double baseFrequency, int octaves, int seed, int salt, double gain, double lacunarity, int noiseStyle)
        {
            int n = octaves < 1 ? 1 : octaves;
            double amplitude = 1d;
            double frequency = baseFrequency < 1e-6d ? 1e-6d : baseFrequency;
            double sum = 0d;
            double norm = 0d;
            double3 offset = SaltOffset(seed, salt);
            for (int o = 0; o < n; o++)
            {
                sum += amplitude * (noiseStyle == (int)TerrainNoiseStyle.Perlin
                    ? GradientNoise((direction * frequency) + offset)
                    : ValueNoise((direction * frequency) + offset));
                norm += amplitude;
                amplitude *= gain;
                frequency *= lacunarity;
                offset = new double3(offset.y + 19.19d, offset.z + 7.47d, offset.x + 3.13d);
            }

            return norm > 0d ? sum / norm : 0d;
        }

        /// <summary>
        /// fBm для САМОЙ ФОРМЫ рельефа, с доменом по октавам (SlopeDamp).
        ///
        /// Идея: чем круче склон, тем больше высокочастотной детали; на пологом
        /// месте её почти нет. Поэтому октава домножается на вес, посчитанный из
        /// накопленного наклона, а не на 1. Поля и вершины выходят сглаженными,
        /// склоны — изъеденными, как бывает в настоящем рельефе.
        ///
        /// Два источника наклона, выбор по SlopeDampMode (НЕ по примитиву):
        ///  • Gradient: аналитическая производная |∇| октавы. Меряет крутизну
        ///    по-настоящему, но стоит трёх лишних трилинейных смешиваний на октаву.
        ///  • Accum: накопленное значение 1 − k·|sum|. Почти бесплатно, но мерит
        ///    ВЫСОТУ, а не уклон — деталь уходит в вершины и долины.
        ///
        /// Источник выбирается отдельным параметром, а не «Перлин значит
        /// производная»: иначе нельзя проверить ни один режим в комбинации с
        /// другим примитивом — а сравнивать их нужно, это две разные картинки.
        ///
        /// При SlopeDampMode = Off или SlopeDamp ≤ 0 домена нет, и результат
        /// бит-в-бит равен SampleFbmEx.
        /// </summary>
        private static double SampleShapeFbm(TerrainNoiseParams p, double3 direction, double baseFrequency, int octaves, int salt, double gain, double lacunarity)
        {
            double damp = p.SlopeDamp;
            if (p.SlopeDampMode == (int)TerrainSlopeDampMode.Off || !(damp > 0d))
            {
                return SampleFbmEx(direction, baseFrequency, octaves, p.Seed, salt, gain, lacunarity, p.NoiseStyle);
            }

            // Производная есть только у градиентного примитива; под Value этот
            // режим молча вырождается в Accum.
            bool gradient = p.SlopeDampMode == (int)TerrainSlopeDampMode.Gradient
                && p.NoiseStyle == (int)TerrainNoiseStyle.Perlin;

            damp = math.min(1d, damp);
            int n = octaves < 1 ? 1 : octaves;
            double amplitude = 1d;
            double frequency = baseFrequency < 1e-6d ? 1e-6d : baseFrequency;
            double slopeAccum = 0d;
            double sum = 0d;
            double norm = 0d;
            double weight = 1d;
            double3 offset = SaltOffset(p.Seed, salt);
            for (int o = 0; o < n; o++)
            {
                double at = weight;
                double v;
                if (gradient)
                {
                    v = GradientNoiseWithDerivative((direction * frequency) + offset, out double3 d);
                    slopeAccum += amplitude * (math.length(d) * frequency);

                    // Сигнал домена — средний накопленный наклон на октаву, делённый
                    // на базовую частоту. Обе нормировки обязательны:
                    //
                    //  • деление на (o+1) — потому что каждая октава добавляет к
                    //    наклону одинаковый вклад (амплитуда падает как 1/частота),
                    //    и без этого сигнал растёт линейно с номером октавы: замер
                    //    T112d даёт прирост ровно 34 на октаву;
                    //  • деление на baseFrequency — потому что наклон пропорционален
                    //    частоте, и без этого константа насыщения зависела бы от
                    //    BaseFrequency профиля.
                    //
                    // Первая версия домена брала |∇|·freq текущей октавы и насыщала
                    // насыщением на 1. Это давало полный ноль эффекта: сигнал лежал
                    // в диапазоне 33..17362 при частотах 20..10240, то есть всегда
                    // выше порога, вес становился постоянным (1 − damp) и СОКРАЩАЛСЯ
                    // при нормировке sum/norm. T110 показал байт-идентичную
                    // статистику у Gradient и Off.
                    double signal = (slopeAccum / (o + 1)) / baseFrequency;
                    at = 1d - (damp * SlopeNorm(signal, SlopeWindowLo, SlopeWindowHi));
                }
                else
                {
                    v = p.NoiseStyle == (int)TerrainNoiseStyle.Perlin
                        ? GradientNoise((direction * frequency) + offset)
                        : ValueNoise((direction * frequency) + offset);
                }

                at = math.min(1d, math.max(0d, at));
                sum += amplitude * v * at;
                norm += amplitude * at;
                amplitude *= gain;
                frequency *= lacunarity;
                offset = new double3(offset.y + 19.19d, offset.z + 7.47d, offset.x + 3.13d);

                if (!gradient)
                {
                    // Накопленное значение — суррогат склона. Делим на норму
                    // текущей октавы, иначе вес дрейфует вместе с Gain.
                    weight = 1d - (damp * SlopeNorm(norm > 0d ? math.abs(sum / norm) : 0d, AccumWindowLo, AccumWindowHi));
                }
            }

            return norm > 0d ? sum / norm : 0d;
        }

        /// <summary>
        /// Сколько «детализации» соответствует данному наклону: 0 — гасить
        /// октаву полностью, 1 — пропустить без изменений. Мягкое насыщение
        /// со сдвигом окна.
        ///
        /// Окна РАЗНЫЕ для двух режимов, и это не перестраховка, а следствие
        /// замера (T112d): сигналы живут в разных масштабах, и с общим окном
        /// один из режимов молча вырождается в no-op. Накопленное значение всегда
        /// ниже окна градиентного режима — так и случилось, когда окно было одно.
        ///
        /// Градиентный режим: сигнал нормирован на средний накопленный наклон
        /// (делить на октаву и на BaseFrequency, иначе он растёт в 515 раз по
        /// частоте). Замер, октава 5 из 10: p10 = 1.38, p50 = 1.80, p90 = 2.02.
        ///
        /// Режим Accum: сигнал — |накопленная форма| в её собственном масштабе
        /// [0,1]. Замер, октава 5: p10 = 0.032, p50 = 0.164, p90 = 0.387.
        ///
        /// Обратите внимание на разброс: у Accum p90/p10 = 12.3, у градиентного
        /// 1.46. Домен по высоте даёт в восемь раз более широкий рабочий диапазон,
        /// то есть он гораздо заметнее меняет форму — «на 90% как производная»
        /// было бы неверно. Именно поэтому режимы выбирают глазами, а не по цене.
        ///
        /// Окно шире квантилей, чтобы у пологих склонов домен не схлопывался в
        /// ноль, а у обрывов уже доходил до единицы.
        ///
        /// Форма — smoothstep, а не жёсткий порог: жёсткий порог даёт скачок
        /// веса октавы, то есть скачок высоты, и на склоне появляется ступенька,
        /// которая тут же всплывает в нормалях и ломает T84.
        /// </summary>
        private static double SlopeNorm(double signal, double lo, double hi)
        {
            double s = math.min(1d, math.max(0d, (signal - lo) / (hi - lo)));
            return s * s * (3d - (2d * s));
        }

        /// <summary>Окно насыщения для сигнала домена по крутизне (см. T112d).</summary>
        private const double SlopeWindowLo = 1.35d;

        private const double SlopeWindowHi = 2.05d;

        /// <summary>Окно насыщения для сигнала домена по высоте (см. T112d).</summary>
        private const double AccumWindowLo = 0.05d;

        private const double AccumWindowHi = 0.45d;

        internal static double SampleRidged(TerrainNoiseParams p, double3 direction, double gain, double lacunarity)
        {
            if (p.RidgedMode == (int)TerrainRidgedMode.Multifractal)
            {
                return SampleRidgedMultifractal(p, direction, gain, lacunarity);
            }

            int n = p.Octaves < 1 ? 1 : p.Octaves;
            double amplitude = 1d;
            double frequency = p.BaseFrequency < 1e-6d ? 1e-6d : p.BaseFrequency;
            double sum = 0d;
            double norm = 0d;
            double3 offset = SaltOffset(p.Seed, 2);
            for (int o = 0; o < n; o++)
            {
                double v = Octave(p, (direction * frequency) + offset);
                sum += amplitude * (1d - (v >= 0d ? v : -v));
                norm += amplitude;
                amplitude *= gain;
                frequency *= lacunarity;
                offset = new double3(offset.y + 19.19d, offset.z + 7.47d, offset.x + 3.13d);
            }

            // smoothstep сохраняет средний уровень 0.5, но обостряет контраст:
            // узкие гребни и плоские долины вместо равномерной «ряби».
            return norm > 0d ? Smoothstep01(sum / norm) : 0d;
        }

        /// <summary>
        /// Складка октавы в гребень: 1 − |v|, возведённое в степень.
        ///
        /// ВЫЗОВЫ ИЗВЕСТНЫ ОГРАНИЧЕНИЯ. |v| насыщается единицей ДО вычитания, и
        /// это не страховка, а исправление бага: у нормированного градиентного
        /// примитива |v| доходит до 1.43 (T111), без насыщения s уходил в −0.43,
        /// квадрат делал его снова положительным, и на линии |v| = 1 возникал
        /// ЛОЖНЫЙ ВТОРИЧНЫЙ ХРЕБЕТ — ровно там, где должен быть перевал.
        ///
        /// Для value noise насыщение — точная нооперация: LatticeValue по
        /// построению в [−1,1], трилинейная смесь не выходит за выпуклую
        /// оболочку, поэтому min(|v|, 1) ничего не меняет и legacy-режим
        /// остаётся бит-в-бит прежним.
        /// </summary>
        internal static double RidgeFold(double v, int sharpness)
        {
            double a = v >= 0d ? v : -v;
            double s = 1d - (a >= 1d ? 1d : a);
            for (int k = 1; k < sharpness; k++)
            {
                s *= s;
            }

            return s;
        }

        /// <summary>
        /// Ridged multifractal (Musgrave). Отличие от Legacy в одном: октава
        /// домножается на ВЕС, унаследованный от предыдущей, а не идёт с
        /// фиксированной амплитудой. Из-за этого хребет, погасший на нижней
        /// октаве, гасит и верхние — и вместо параллельных складок получается
        /// ветвящаяся сеть, которую видно с любой высоты.
        ///
        /// Диапазон 0..1 держится ПОСТРОЕНИЕМ, но не «само собой»: складка
        /// RidgeFold насыщает |v| единицей. Без этого утверждение было бы верно
        /// только для value noise, а у нормированного Перлина |v| до 1.43.
        /// Домножение на вес из [0,1] не выводит, sum/norm — выпуклая комбинация.
        /// Это важно, потому что вызывающий на строке SampleHeight подмешивает
        /// ridged к baseHeight как «оба в одном диапазоне».
        ///
        /// Нормировка `sum/norm` вместо деления на gain-геометрию — сознательно:
        /// при домене знаменатель плавает, и фиксированная нормировка тянула бы
        /// средний уровень вниз.
        /// </summary>
        private static double SampleRidgedMultifractal(
            TerrainNoiseParams p, double3 direction, double gain, double lacunarity)
        {
            int n = p.Octaves < 1 ? 1 : p.Octaves;
            double amplitude = 1d;
            double frequency = p.BaseFrequency < 1e-6d ? 1e-6d : p.BaseFrequency;
            double sum = 0d;
            double norm = 0d;
            double weight = 1d;
            double weightGain = p.RidgedWeightGain > 0d ? p.RidgedWeightGain : 2d;
            int sharpness = SharpnessSteps(p.RidgedSharpness);
            double3 offset = SaltOffset(p.Seed, 2);
            for (int o = 0; o < n; o++)
            {
                double v = Octave(p, (direction * frequency) + offset);
                double s = RidgeFold(v, sharpness);

                // Первая октава не гасится: вес = 1, иначе нижний уровень рельефа
                // зависел бы от того, как счастливо лег хеш именно на октаве 0.
                s *= weight;
                sum += amplitude * s;
                norm += amplitude;
                double w = s * weightGain;
                weight = w >= 1d ? 1d : w;
                amplitude *= gain;
                frequency *= lacunarity;
                offset = new double3(offset.y + 19.19d, offset.z + 7.47d, offset.x + 3.13d);
            }

            if (!(norm > 0d))
            {
                return 0d;
            }

            return RidgeGamma(sum / norm, p.RidgedGamma);
        }

        /// <summary>
        /// Ремапа уровня гребня: s^γ на s в [0,1]. Один параметр, монотонная,
        /// диапазон держится по построению и топологию хребтов не меняет — при
        /// γ > 0 монотонное преобразование сохраняет и порядок, и число
        /// экстремумов. Именно поэтому γ, а не аффинная ремапа по двум
        /// параметрам: аффинная по среднему и σ уводит максимум за 1, а по краям
        /// диапазона проваливает среднее с 0.73 до 0.46 (замер T112c) — то есть
        /// подгонка разной формы распределений ломается в обе стороны.
        ///
        /// Совпадение с legacy по среднему и σ НЕ требуется и не нужно: legacy
        /// был узким и прижатым к верху из-за «усреднить октавы, потом
        /// сгладить», а разброс multifractal и есть смысл ветвления.
        ///
        /// γ = 1 — нооперация без вызова pow: это и дефолт, и путь legacy.
        /// Остаток по высоте гребней гасится AmplitudeMeters в пресете, не кодом.
        /// </summary>
        internal static double RidgeGamma(double s, double gamma)
        {
            if (!(s > 0d))
            {
                return s <= 0d ? 0d : s;
            }

            if (gamma > 0d && gamma < 1d)
            {
                return math.pow(s, gamma);
            }

            return s;
        }

        /// <summary>
        /// Показатель степени квантуется в целое: pow() в горячем цикле недопустим,
        /// а разница между 1.9 и 2.0 на глаз не отличается. 1 = без заострения,
        /// 2 = классическое signal², 3 и 4 — сильнее.
        /// </summary>
        private static int SharpnessSteps(double sharpness)
        {
            if (!(sharpness > 1d))
            {
                return 1;
            }

            int n = (int)math.round(sharpness);
            return n > 4 ? 4 : n;
        }

        private static double3 SaltOffset(int seed, int salt)
        {
            switch (salt)
            {
                case 1:
                    return new double3(seed * 57.31d + 101.3d, seed * 43.77d + 67.9d, seed * 71.13d + 37.1d);
                case 2:
                    return new double3(seed * 23.17d + 503.7d, seed * 89.31d + 311.3d, seed * 11.71d + 877.9d);
                case 4:
                    return new double3(seed * 17.13d + 2100.7d, seed * 37.31d + 1900.4d, seed * 73.17d + 2300.8d);
                case 5:
                    // Собственный поток маски равнин: НЕ default (там уже сидят
                    // база salt 0 и warp-Z salt 300) — иначе равнины коррелируют
                    // с warp'ом, а через него с хребтами.
                    return new double3(seed * 83.77d + 2600.3d, seed * 29.13d + 2900.7d, seed * 67.31d + 2700.1d);
                case 6:
                    // Поток мелкомасштабной детали (rock detail): снова свой,
                    // чтобы не коррелировать ни с формой, ни с масками.
                    return new double3(seed * 53.19d + 3600.9d, seed * 97.31d + 3300.5d, seed * 41.77d + 3900.3d);
                case 7:
                    // Поток мелкомасштабной цветовой детали (моттлинг).
                    return new double3(seed * 71.93d + 4600.1d, seed * 33.47d + 4900.7d, seed * 89.11d + 4300.5d);
                case 8:
                    // Поток распределения декора (кластеры травы/камней).
                    return new double3(seed * 47.11d + 6100.3d, seed * 31.79d + 6400.7d, seed * 73.31d + 6700.1d);
                case 9:
                    // Поток ширины пляжа (неравномерный берег): свой, чтобы зоны
                    // широкого/узкого песка не коррелировали ни с формой, ни с масками.
                    return new double3(seed * 37.91d + 7300.7d, seed * 61.17d + 7600.3d, seed * 29.53d + 7900.9d);
                case 100:
                    return new double3(seed * 91.7d + 1000.3d, seed * 47.31d + 700.7d, seed * 13.17d + 400.9d);
                case 200:
                    return new double3(seed * 31.7d + 1400.1d, seed * 77.13d + 1100.5d, seed * 53.71d + 900.2d);
                default:
                    return new double3(seed * 61.3d + 1700.9d, seed * 19.77d + 1300.3d, seed * 83.31d + 1500.6d);
            }
        }

        private static double Smoothstep01(double t)
        {
            if (t <= 0d)
            {
                return 0d;
            }

            if (t >= 1d)
            {
                return 1d;
            }

            return t * t * (3d - (2d * t));
        }

        private static double Quintic(double t)
        {
            return t * t * t * (t * ((t * 6d) - 15d) + 10d);
        }

        internal static double ValueNoise(double3 p)
        {
            // floor отдельно: дробная часть p - floor(p) всегда ∈ [0,1), а индекс
            // ячейки хешируется через long→int. Прямой (int)floor(p) ломается,
            // когда сид-оффсеты (seed·89.31 и т.п.) выходят за int.MaxValue —
            // тогда p - (int)floor(p) ~ 4e9 и Quintic взрывается в 1e298.
            double xf = math.floor(p.x);
            double yf = math.floor(p.y);
            double zf = math.floor(p.z);
            int ix = unchecked((int)(long)xf);
            int iy = unchecked((int)(long)yf);
            int iz = unchecked((int)(long)zf);
            double fx = p.x - xf;
            double fy = p.y - yf;
            double fz = p.z - zf;
            double ux = Quintic(fx);
            double uy = Quintic(fy);
            double uz = Quintic(fz);

            double c000 = LatticeValue(ix, iy, iz);
            double c100 = LatticeValue(ix + 1, iy, iz);
            double c010 = LatticeValue(ix, iy + 1, iz);
            double c110 = LatticeValue(ix + 1, iy + 1, iz);
            double c001 = LatticeValue(ix, iy, iz + 1);
            double c101 = LatticeValue(ix + 1, iy, iz + 1);
            double c011 = LatticeValue(ix, iy + 1, iz + 1);
            double c111 = LatticeValue(ix + 1, iy + 1, iz + 1);

            double x00 = c000 + (ux * (c100 - c000));
            double x10 = c010 + (ux * (c110 - c010));
            double x01 = c001 + (ux * (c101 - c001));
            double x11 = c011 + (ux * (c111 - c011));
            double y0 = x00 + (uy * (x10 - x00));
            double y1 = x01 + (uy * (x11 - x01));
            return y0 + (uz * (y1 - y0));
        }

        private static double LatticeValue(int x, int y, int z)
        {
            unchecked
            {
                int h = (x * 374761393) + (y * 668265263) + (z * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return ((h & 0xFFFF) / 32767.5d) - 1d;
            }
        }

        private const double Grad2 = 0.70710678118654752440d; // 1/sqrt(2)

        /// <summary>
        /// Нормировка градиентного примитива по RMS, в сравнение с value noise.
        ///
        /// Замерено (T111, 4M точек): RMS value 0.40038, RMS градиента 0.19475,
        /// отношение 2.05593. Без этой нормировки октавы Перлина были бы вчетверо
        /// тише, и три-четверти амплитуды профиля уходили бы в пустоту: RMS —
        /// это средняя энергия, а амплитуда контрастируется по пику. Подбирать
        /// ContinentThreshold под RMS нельзя, порог стоит на пике, и при
        /// ContinentDepth = 0.08 у EarthLike доля суши съезжает с 0.469 в 0.707.
        ///
        /// Нормировка именно по RMS, а не по максимуму, и это не одно и то же:
        /// пиковый предел не достигается пик-в-пик на практике, RMS - то, что
        /// реально даёт вклад в дисперсию. Верхняя граница формы выводится не
        /// отсюда, а из ShapeBound.
        /// </summary>
        private const double PerlinScale = 2.05593d;
        /// <summary>
        /// Предел нормированной ОДНОЙ октавы градиентного примитива, не формы.
        ///
        /// Октава ограничена сверху суммой |g_i| по трём ближайшим узлам. При
        /// |g| = 1 и |f| ≤ 1 сумма не превосходит √3, и достигает этого только
        /// в вершине ячейки с (1,1,1), где интерполяция даёт 1, то есть предел
        /// одной октавы равен √3/2 = 0.8660. Множитель PerlinScale уже учтён,
        /// и это замерено, а не выведено из симметрии (см. T111).
        ///
        /// ФОРМА целиком — это НЕ ShapeBound. При выключенном ContinentDepth
        /// сумма взвешенных октав нормирована и держится в пределах ±1.
        /// Стоит только включить ContinentDepth, как ContinentFrequency-слой
        /// (value noise, своя копия хеша) уводит форму вниз сверх предела
        /// одной октавы, и получить низ уже нельзя из ShapeBound. Измеренный
        /// минимум формы доходит до −2.05 при ShapeBound = 1.78.
        ///
        /// Настоящий контракт для потребителя Perlin-профиля:
        ///     |форма| ≤ ShapeBound + ContinentDepth      (уравновешено T128)
        /// а для legacy value noise граница действительно 1.
        ///
        /// Потребителей этой константы в коде нет и она не используется как
        /// утверждение о пределах: bounds чанков считаются по фактическим
        /// вершинам, hasWater оперирует SeaLevelMeters в метрах. Константа
        /// существует как проверяемое утверждение (T128) и как граница, по
        /// которой можно построить assert, если появится потребитель.
        /// </summary>
        public const double ShapeBound = 0.86602540378443864676d * PerlinScale;
        /// <summary>

        /// <summary>
        /// Хеш решётки ГРАДИЕНТНОГО примитива. ВАЖНО: это НЕ тот же хеш, что в
        /// LatticeValue, и он намеренно отличается множителем оси z.
        ///
        /// В LatticeValue (value noise и весь legacy-режим) множитель z равен
        /// 2147483647 = 2^31 - 1, то есть z*(2^31-1) = -z (mod 2^31): вклад оси z
        /// почти не перемешивается. Для скалярного хеша это безвредно - берутся
        /// младшие 16 бит, и у них хватает случайности. Для выбора направления
        /// из 12 градиентов это давало измеримую осевую анизотропию поля:
        /// энергия градиента расходилась на 1.84%, ось z была тяжелее на 5%
        /// (T127). Подстановка множителя 2654435761 убирает перекос до 0.043%,
        /// при этом НЕ трогает value noise, его распределение и все пины
        /// legacy - потому что копии хеша раздельные.
        ///
        /// Три копии (эта, плюс вызовы из LatticeDot и LatticeGradientFast)
        /// держать раздельно нельзя: они разойдутся при следующей правке.
        /// Функция одна, с inline-подсказкой - она в горячем пути 8 раз на октаву.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static int PerlinLatticeHash(int x, int y, int z)
        {
            unchecked
            {
                int h = (x * 374761393) + (y * 668265263) + (z * unchecked((int)2654435761u));
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return h;
            }
        }
        /// <summary>
        /// Скалярное произведение градиента в узле решётки на смещение — без
        /// сборки самого вектора.
        ///
        /// Вектор здесь не материализуется. Восемь углов ячейки означали бы
        /// восемь конструкций double3 по три сохранения каждое плюс лишние
        /// умножения на сборке. Вместо этого коэффициенты при fx/fy/fz считаются
        /// сразу из индекса, и на угол остаётся три умножения и два сложения.
        ///
        /// Ветвлений нет, и это не оптимизация ради скорости, а требование
        /// корректности по цене: индекс случая случаен, и предсказатель
        /// промахивался на каждом из восьми углов. Вариант со switch на 20
        /// случаев стоил x8.1 к value noise, ветвистый if — x3.7 (меряет T111).
        ///
        /// Набор — 12 рёбер куба, по 4 на плоскость, как в improved Perlin.
        /// Первая версия брала 8 диагоналей куба + 4 ребра плоскости xy + 4 ребра
        /// плоскости xz, и это была ошибка: рёбер yz там не было вовсе, и
        /// матрица вторых моментов выходила diag(6.67, 4.67, 4.67) — ось x была
        /// громче остальных на 42% по мощности. То есть та самая осевая
        /// анизотропия, ради которой value noise и менялся, осталась бы на месте
        /// под новым именем. При 12 рёбрах сумма g gᵀ равна 4I ровно, и набор
        /// замкнут относительно перестановок осей (проверяет T112).
        ///
        /// Про индекс: берётся h % 12, а НЕ привычное (ulong)h·12 >> 32. У
        /// хеша мёртв верхний бит: на 1.7M узлов решётки ни одно значение не
        /// оказалось ≥ 2^31, то есть h всегда неотрицателен, и схема на старших
        /// битах молча вырождалась в нулевой индекс — плоскость yz не
        /// встречалась ни разу (проверяет T112). Остаток от деления на константу
        /// компилируется в умножение со сдвигом; разброс по 12 корзинам 0.56%,
        /// и это неравномерность самого хеша, а не квантование остатка.
        /// </summary>
        internal static double LatticeDot(int x, int y, int z, double fx, double fy, double fz)
        {
            unchecked
            {
                int h = PerlinLatticeHash(x, y, z);

                // uint, а не int: h >= 0 сегодня, но если верхний бит хеша
                // оживёт, знаковый остаток дал бы отрицательный индекс.
                int idx = (int)((uint)h % 12u);
                int plane = idx >> 2; // 0 = плоскость xy, 1 = xz, 2 = yz
                int k = idx & 3;

                double sa = 1d - (2d * (k & 1));
                double sb = 1d - (2d * ((k >> 1) & 1));

                // Нулевая ось по плоскости: у xy занят z, у xz — y, у yz — x.
                double mx = 1d - (plane >> 1);
                double my = 1d - (plane & 1);
                double mz = (plane + 1) >> 1;

                // Из двух занятых осей (по возрастанию индекса) первую красит sa.
                // Для плоскостей xy и xz это ось x, для yz — ось y, поэтому знак y
                // переключается битом plane>>1. Плоскость xz ось y не красит
                // вовсе (my = 0), и значение там не важно.
                double sy = sb + ((plane >> 1) * (sa - sb));

                return ((sa * Grad2 * mx) * fx) + ((sy * Grad2 * my) * fy) + ((sb * Grad2 * mz) * fz);
            }
        }

        /// <summary>
        /// Градиент в узле решётки вектором — эталонная форма, из которой T112
        /// берёт набор направлений и сверяет с LatticeDot. В горячем пути не
        /// используется: материализация вектора там и есть лишняя работа.
        /// </summary>
        internal static double3 LatticeGradientFast(int x, int y, int z)
        {
            unchecked
            {
                int h = PerlinLatticeHash(x, y, z);

                int idx = (int)((uint)h % 12u);
                int plane = idx >> 2;
                int k = idx & 3;
                double sa = 1d - (2d * (k & 1));
                double sb = 1d - (2d * ((k >> 1) & 1));
                double mx = 1d - (plane >> 1);
                double my = 1d - (plane & 1);
                double mz = (plane + 1) >> 1;
                double sy = sb + ((plane >> 1) * (sa - sb));
                return new double3(sa * Grad2 * mx, sy * Grad2 * my, sb * Grad2 * mz);
            }
        }


        /// <summary>
        /// Одна октава выбранного примитива. Раньше был единственный ValueNoise на
        /// все случаи; теперь точка выбора одна и видна целиком, а не размазана по
        /// трём вызовам.
        /// </summary>
        private static double Octave(TerrainNoiseParams p, double3 at)
        {
            return p.NoiseStyle == (int)TerrainNoiseStyle.Perlin ? GradientNoise(at) : ValueNoise(at);
        }

        /// <summary>
        /// Примитив градиентного шума: 8 углов ячейки, интерполяция quintic,
        /// набор направлений из 12 рёбер куба. Возвращает ЗНАЧЕНИЕ ОДНОЙ ОКТАВЫ,
        /// уже умноженное на PerlinScale.
        ///
        /// ВАЖНО, старое утверждение здесь было неверным: масштаб НЕ 1.0 и
        /// функция НЕ обязана лежать в [-1, 1]. Это правда для value noise, где
        /// SampleHeight нормирует взвешенную сумму октав в [-1, 1] и потому
        /// |H| <= Amplitude (это и проверял T77). У Перлина нормировки нет -
        /// вместо неё RMS-множитель PerlinScale = 2.05593, и одна октава доходит
        /// до ShapeBound = 1.78.
        ///
        /// Поэтому потребитель SampleHeight обязан различать два случая:
        ///   NoiseStyle = Value  ->  |H| <= Amplitude, как и раньше;
        ///   NoiseStyle = Perlin ->  |H| <= (ShapeBound + ContinentDepth) *
        ///                           Amplitude, проверяет T128.
        /// Кто-то мог заменить PerlinScale на 1.0 ради "совместимости" с этим
        /// комментарием - тогда бы октавы сели вчетверо и порог суши уехал бы.
        /// </summary>
        internal static double GradientNoise(double3 p)
        {
            double xf = math.floor(p.x);
            double yf = math.floor(p.y);
            double zf = math.floor(p.z);
            int ix = unchecked((int)(long)xf);
            int iy = unchecked((int)(long)yf);
            int iz = unchecked((int)(long)zf);
            double fx = p.x - xf;
            double fy = p.y - yf;
            double fz = p.z - zf;
            double ux = Quintic(fx);
            double uy = Quintic(fy);
            double uz = Quintic(fz);

            double n000 = LatticeDot(ix, iy, iz, fx, fy, fz);
            double n100 = LatticeDot(ix + 1, iy, iz, fx - 1d, fy, fz);
            double n010 = LatticeDot(ix, iy + 1, iz, fx, fy - 1d, fz);
            double n110 = LatticeDot(ix + 1, iy + 1, iz, fx - 1d, fy - 1d, fz);
            double n001 = LatticeDot(ix, iy, iz + 1, fx, fy, fz - 1d);
            double n101 = LatticeDot(ix + 1, iy, iz + 1, fx - 1d, fy, fz - 1d);
            double n011 = LatticeDot(ix, iy + 1, iz + 1, fx, fy - 1d, fz - 1d);
            double n111 = LatticeDot(ix + 1, iy + 1, iz + 1, fx - 1d, fy - 1d, fz - 1d);

            return TrilinearBlend(n000, n100, n010, n110, n001, n101, n011, n111, ux, uy, uz) * PerlinScale;
        }

        /// <summary>
        /// Значение октавы Перлина вместе с аналитической производной — нужно
        /// домену по склону (SampleShapeFbm). Новых хешей не считает: 8
        /// скалярных произведений уже посчитаны для значения, производная — это
        /// ещё три трилинейных смешивания на ось.
        ///
        /// Специально НЕ используется для value noise: у него производная
        /// разрывна на границах ячеек (значение на углу просто меняется
        /// константой), и домен по ней дрожал бы на стыках.
        /// </summary>
        internal static double GradientNoiseWithDerivative(double3 p, out double3 derivative)
        {
            double xf = math.floor(p.x);
            double yf = math.floor(p.y);
            double zf = math.floor(p.z);
            int ix = unchecked((int)(long)xf);
            int iy = unchecked((int)(long)yf);
            int iz = unchecked((int)(long)zf);
            double fx = p.x - xf;
            double fy = p.y - yf;
            double fz = p.z - zf;
            double ux = Quintic(fx);
            double uy = Quintic(fy);
            double uz = Quintic(fz);

            // du/dt квинтического сглаживания: 30·t²(t−1)². Ноль на границах
            // ячейки — поэтому производная в целом непрерывна.
            double dudx = 30d * fx * fx * ((fx - 1d) * (fx - 1d));
            double dvdy = 30d * fy * fy * ((fy - 1d) * (fy - 1d));
            double dwdz = 30d * fz * fz * ((fz - 1d) * (fz - 1d));

            double3 g000 = LatticeGradientFast(ix, iy, iz);
            double3 g100 = LatticeGradientFast(ix + 1, iy, iz);
            double3 g010 = LatticeGradientFast(ix, iy + 1, iz);
            double3 g110 = LatticeGradientFast(ix + 1, iy + 1, iz);
            double3 g001 = LatticeGradientFast(ix, iy, iz + 1);
            double3 g101 = LatticeGradientFast(ix + 1, iy, iz + 1);
            double3 g011 = LatticeGradientFast(ix, iy + 1, iz + 1);
            double3 g111 = LatticeGradientFast(ix + 1, iy + 1, iz + 1);

            double n000 = LatticeDot(ix, iy, iz, fx, fy, fz);
            double n100 = LatticeDot(ix + 1, iy, iz, fx - 1d, fy, fz);
            double n010 = LatticeDot(ix, iy + 1, iz, fx, fy - 1d, fz);
            double n110 = LatticeDot(ix + 1, iy + 1, iz, fx - 1d, fy - 1d, fz);
            double n001 = LatticeDot(ix, iy, iz + 1, fx, fy, fz - 1d);
            double n101 = LatticeDot(ix + 1, iy, iz + 1, fx - 1d, fy, fz - 1d);
            double n011 = LatticeDot(ix, iy + 1, iz + 1, fx, fy - 1d, fz - 1d);
            double n111 = LatticeDot(ix + 1, iy + 1, iz + 1, fx - 1d, fy - 1d, fz - 1d);

            // ∂/∂x = трилинейная смесь gx по (u,v,w) + du/dx · разности n по x,
            // смешанные только по (v,w). Аналогично для y и z.
            double gx = TrilinearBlend(g000.x, g100.x, g010.x, g110.x, g001.x, g101.x, g011.x, g111.x, ux, uy, uz)
                + (dudx * Bilerp(n100 - n000, n110 - n010, n101 - n001, n111 - n011, uy, uz));
            double gy = TrilinearBlend(g000.y, g100.y, g010.y, g110.y, g001.y, g101.y, g011.y, g111.y, ux, uy, uz)
                + (dvdy * Bilerp(n010 - n000, n110 - n100, n011 - n001, n111 - n101, ux, uz));
            double gz = TrilinearBlend(g000.z, g100.z, g010.z, g110.z, g001.z, g101.z, g011.z, g111.z, ux, uy, uz)
                + (dwdz * Bilerp(n001 - n000, n101 - n100, n011 - n010, n111 - n110, ux, uy));

            // Производная масштабируется тем же коэффициентом, что и значение:
            // иначе домен по склону мерил бы |∇| в других единицах, и параметр
            // SlopeDamp перестал бы значить одно и то же при разных примитивах.
            derivative = new double3(gx, gy, gz) * PerlinScale;
            return TrilinearBlend(n000, n100, n010, n110, n001, n101, n011, n111, ux, uy, uz) * PerlinScale;
        }

        private static double TrilinearBlend(
            double c000, double c100, double c010, double c110,
            double c001, double c101, double c011, double c111,
            double ux, double uy, double uz)
        {
            double x00 = c000 + (ux * (c100 - c000));
            double x10 = c010 + (ux * (c110 - c010));
            double x01 = c001 + (ux * (c101 - c001));
            double x11 = c011 + (ux * (c111 - c011));
            double y0 = x00 + (uy * (x10 - x00));
            double y1 = x01 + (uy * (x11 - x01));
            return y0 + (uz * (y1 - y0));
        }

        private static double Bilerp(double c00, double c10, double c01, double c11, double u, double v)
        {
            double a = c00 + (u * (c10 - c00));
            double b = c01 + (u * (c11 - c01));
            return a + (v * (b - a));
        }
    }

    /// <summary>
    /// Пачка вершин тайла: направления (unit, body-fixed, double3) → форма
    /// (double) + цветовая маска (float; цвет физику не касается).
    /// Вызывает те же статики, что меряет T84.
    /// </summary>
    [BurstCompile]
    public struct TerrainTileJob : IJobParallelFor
    {
        [ReadOnly]
        public TerrainNoiseParams Params;

        [ReadOnly]
        public NativeArray<double3> Directions;

        public NativeArray<double> Heights;
        public NativeArray<float> ColorMasks;
        public NativeArray<float> ColorDetails;

        public void Execute(int index)
        {
            double3 direction = Directions[index];
            Heights[index] = TerrainNoise.SampleHeight(Params, direction);
            ColorMasks[index] = Params.ComputeMask ? (float)TerrainNoise.SampleColorNoise(Params, direction) : 0f;
            if (ColorDetails.IsCreated)
            {
                ColorDetails[index] = Params.ComputeDetail
                    ? (float)TerrainNoise.SampleColorDetailNoise(Params, direction)
                    : 0f;
            }
        }
    }
}