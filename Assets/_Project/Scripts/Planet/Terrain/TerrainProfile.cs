using System;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Палитра поверхности (11 цветов) в привычном sRGB — как настраивается в
    /// инспекторе. Переводом в Linear занимается TerrainPalette.ToLinear:
    /// единственный путь sRGB→Linear, поэтому цвета из ассета и старые
    /// константы палитры не расходятся (доказано T92).
    /// </summary>
    [Serializable]
    public sealed class TerrainPaletteData
    {
        public Color Sand = new Color(0.80f, 0.73f, 0.55f, 1f);
        public Color Desert = new Color(0.74f, 0.60f, 0.36f, 1f);
        public Color DryGrass = new Color(0.50f, 0.52f, 0.27f, 1f);
        public Color Grass = new Color(0.102f, 0.290f, 0.145f, 1f);
        public Color Forest = new Color(0.12f, 0.28f, 0.10f, 1f);
        public Color Tundra = new Color(0.46f, 0.44f, 0.37f, 1f);
        public Color Rock = new Color(0.45f, 0.41f, 0.36f, 1f);
        public Color Snow = new Color(0.96f, 0.97f, 0.99f, 1f);
        public Color Sea = new Color(0.05f, 0.20f, 0.42f, 1f);
        public Color Soil = new Color(0.50f, 0.40f, 0.26f, 1f);
        public Color Lush = new Color(0.13f, 0.40f, 0.12f, 1f);

        public Color SandLinear => TerrainPalette.ToLinear(Sand);
        public Color DesertLinear => TerrainPalette.ToLinear(Desert);
        public Color DryGrassLinear => TerrainPalette.ToLinear(DryGrass);
        public Color GrassLinear => TerrainPalette.ToLinear(Grass);
        public Color ForestLinear => TerrainPalette.ToLinear(Forest);
        public Color TundraLinear => TerrainPalette.ToLinear(Tundra);
        public Color RockLinear => TerrainPalette.ToLinear(Rock);
        public Color SnowLinear => TerrainPalette.ToLinear(Snow);
        public Color SeaLinear => TerrainPalette.ToLinear(Sea);
        public Color SoilLinear => TerrainPalette.ToLinear(Soil);
        public Color LushLinear => TerrainPalette.ToLinear(Lush);

        public TerrainPaletteData Clone()
        {
            // Color — структура, MemberwiseClone копирует значения полностью.
            return (TerrainPaletteData)MemberwiseClone();
        }

        /// <summary>Совпадают ли палитры по значениям (для live-тюнинга рендера).</summary>
        public bool Matches(TerrainPaletteData other)
        {
            return other != null
                && Sand == other.Sand
                && Desert == other.Desert
                && DryGrass == other.DryGrass
                && Grass == other.Grass
                && Forest == other.Forest
                && Tundra == other.Tundra
                && Rock == other.Rock
                && Snow == other.Snow
                && Sea == other.Sea
                && Soil == other.Soil
                && Lush == other.Lush;
        }
    }

    /// <summary>
    /// Профиль процедурного рельефа: форма (fBm/континенты/хребты/равнины/
    /// деталь/warp), цветовые пороги и палитра. Чистые данные без
    /// UnityEngine-поведения — живут в ассете (TerrainProfileAsset), копируются
    /// в HeightfieldTerrain при сборке системы (T91) и сериализуются в тестах.
    /// Зерно шума остаётся на теле (BodyAuthoring.TerrainSeed): один профиль-
    /// пресет можно вешать на разные тела с разными сидами.
    /// Нулевые/дефолтные значения параметров дают бит-в-бит legacy-fBm —
    /// контракт HeightfieldTerrain не меняется.
    /// </summary>
    [Serializable]
    public sealed class TerrainProfile
    {
        [Header("Форма")]
        [Tooltip("Амплитуда рельефа над/под средним радиусом (м).")]
        public double AmplitudeMeters = 1000d;

        [Tooltip("Базовая частота (циклов на единичный вектор).")]
        public double BaseFrequency = 3d;

        [Tooltip("Число октав.")]
        public int Octaves = 5;

        [Tooltip("Уровень моря над средним радиусом (м). Ниже −1e30 = моря нет.")]
        public double SeaLevelMeters = -1e30d;

        [Tooltip("Лакунарность fBm: 2 = legacy.")]
        public double Lacunarity = 2d;

        [Tooltip("Затухание амплитуды на октаву: 0.5 = legacy.")]
        public double Gain = 0.5d;

        [Tooltip("Доля ridged-шума 0..1 (горные хребты, только на континентах). 0 = выключен.")]
        public double RidgedMix = 0d;

        /// <summary>Сборка ridged-составляющей: 0 = сумма октав (legacy), 1 = multifractal (ветвящиеся хребты).</summary>
        public int RidgedMode = (int)TerrainRidgedMode.Legacy;

        /// <summary>Заострение гребня в multifractal, квантуется в целое 1..4. 2 = классика.</summary>
        public double RidgedSharpness = 2d;

        /// <summary>Насколько сильно вес верхней октавы зависит от нижней (ridged multifractal).</summary>
        public double RidgedWeightGain = 2d;

        /// <summary>
        /// Ремапа уровня гребня в multifractal: s^γ. 1 = без ремапы. Подбирается
        /// по доле суши, остаток по высоте гасится AmplitudeMeters.
        /// </summary>
        public double RidgedGamma = 1d;

        /// <summary>Сила домена по октавам 0..1 для ФОРМЫ рельефа. 0 = выключено.</summary>
        public double SlopeDamp = 0d;

        /// <summary>Источник наклона для домена: Accum (по высоте) или Gradient (по крутизне).</summary>
        public int SlopeDampMode = (int)TerrainSlopeDampMode.Off;

        /// <summary>
        /// Базовый примитив: 0 = value noise (legacy), 1 = градиентный Перлин.
        /// Дефолт legacy — см. пояснение в HeightfieldTerrain.NoiseStyle: Unity
        /// подставляет инициализатор в ассеты без этого поля, и побитовые
        /// проверки висят на дефолтах.
        /// </summary>
        public int NoiseStyle = (int)TerrainNoiseStyle.Value;


        /// <summary>Примитив для МАСОК и warp: 0 = следует NoiseStyle.</summary>
        public int MaskNoiseStyle = -1;
        [Header("Континенты (океан/суша)")]
        [Tooltip("Частота континентальной маски. 0 = выключена.")]
        public double ContinentFrequency = 0d;

        [Tooltip("Число октав маски континентов.")]
        public int ContinentOctaves = 3;

        [Tooltip("Порог маски: выше — суша, ниже — океан.")]
        public double ContinentThreshold = 0d;

        [Tooltip("Полуширина smoothstep-перехода маски.")]
        public double ContinentSharpness = 0.25d;

        [Tooltip("Глубина океанических впадин в долях амплитуды.")]
        public double ContinentDepth = 0.75d;

        [Header("Континенты: отдельный gain, warp и форма")]
        [Tooltip("Затухание амплитуды маски континентов. 0 = следовать за общим Gain. Отдельная ручка: при общем 0.5 верхние октавы маски гаснут слишком быстро и берег остаётся из двух-трёх гладких пятен.")]
        public double ContinentGain = 0d;

        [Tooltip("Сила ОТДЕЛЬНОГО warp'а континентальной маски. 0 = маска на общем warp'е рельефа. Нужен, чтобы «круглость» материков не наследовала масштаб горного warp'а.")]
        public double ContinentWarpStrength = 0d;

        [Tooltip("Частота warp'а континентальной маски. Ниже ContinentFrequency даёт крупные изгибы берега, выше — мельче.")]
        public double ContinentWarpFrequency = 0d;

        [Tooltip("Октав warp'а континентальной маски.")]
        public int ContinentWarpOctaves = 3;

        [Tooltip("Вклад вытянутого «хребтового» члена маски: continentRaw += ContinentRidgeMix·(1−2·|fBm|). 0 = выключено. Тянет материки в длинные цепи вместо круглых пятен, но сдвигает порог в сторону суши.")]
        public double ContinentRidgeMix = 0d;

        [Tooltip("Частота ridge-члена маски континентов.")]
        public double ContinentRidgeFrequency = 0d;

        [Tooltip("Октав ridge-члена маски континентов.")]
        public int ContinentRidgeOctaves = 2;

        [Tooltip("Экваториальный сдвиг маски, continentRaw += ContinentLatitudeBias·(1−2·|sin φ|). 0 = выключено. Долю суши сам по себе не меняет (член центрирован), двигает только распределение по широте.")]
        public double ContinentLatitudeBias = 0d;

        [Header("Океан и внутренность суши")]
        [Tooltip("Глубина абиссального ложа в нормированных единицах (>0 включает НОВЫЙ путь океана, при этом ContinentDepth не используется). Океан тогда не «шум ниже нуля», а настоящее дно: глубина растёт по мере удаления от берега, поэтому окраины пологие, а ложе плоское.")]
        public double OceanFloorDepth = 0d;

        [Tooltip("Глубина шельфа у берега в нормированных единицах. На суше всегда ноль, в океане даёт пологое мелководье перед окраиной. 0 = шельфа нет.")]
        public double OceanShelfDepth = 0d;

        [Tooltip("Пол внутренности в нормированных единицах (>0 включает). Суша не может опуститься ниже continent·InteriorFloor, поэтому во внутренности материков нет внутренних озёр и морей, а у берега (continent≈0) пол равен нулю и острова/заливы сохраняются.")]
        public double InteriorFloor = 0d;

        [Header("Горные пояса (рельеф не везде одинаково горный)")]
        [Tooltip("Частота маски горных поясов. 0 = выключена (legacy: размах рельефа везде одинаков). Низкая частота = пояса шириной в тысячи километров, как на Земле.")]
        public double OrogenyFrequency = 0d;

        [Tooltip("Число октав маски горных поясов.")]
        public int OrogenyOctaves = 3;

        [Tooltip("Порог маски поясов: выше — горы.")]
        public double OrogenyThreshold = 0d;

        [Tooltip("Полуширина smoothstep-перехода маски поясов.")]
        public double OrogenySharpness = 0.3d;

        [Tooltip("Доля размаха рельефа вне поясов (0..1). Малое значение даёт спокойные низменности вместо горной каши по всей суше. 1 = без поясов (legacy).")]
        public double OrogenyFloor = 1d;

        [Tooltip("Доля размаха, добавляемая в поясе поверх OrogenyFloor.")]
        public double OrogenyGain = 0d;

        [Header("Равнины")]
        [Tooltip("Сила равнин 0..1: в зонах маски рельеф стягивается к низкому плато. 0 = выключено.")]
        public double PlainMix = 0d;

        [Tooltip("Частота низкочастотной маски равнин. 0 = выключена.")]
        public double PlainFrequency = 0d;

        [Tooltip("Число октав маски равнин.")]
        public int PlainOctaves = 2;

        [Tooltip("Порог маски равнин: выше — равнина.")]
        public double PlainThreshold = 0d;

        [Tooltip("Полуширина smoothstep-перехода маски равнин.")]
        public double PlainSharpness = 0.3d;

        [Tooltip("Нормализованная высота плато равнин (доля амплитуды).")]
        public double PlainElevation = 0.1d;

        [Header("Сжатие хвоста (только для ridged-профилей)")]
        [Tooltip("Мягкое сжатие верхнего хвоста формы, нормированные единицы. Это сколько ещё разрешено вырасти над порогом: потолок = TailThreshold + TailKnee. 0 = выключено (по умолчанию), тогда поведение прежнее. Нужно, чтобы калибровка по p99 не задирала вершины: у ridged-профилей тяжёлый хвост.")]
        public double TailKnee = 0d;

        [Tooltip("Порог сжатия (нормированные единицы). Ниже него форма не тронута. Ставить примерно на p99.9.")]
        public double TailThreshold = 0d;

        [Header("Сжатие глубокого ложа (только для океанских хвостов)")]
        [Tooltip("Мягкое сжатие нижнего хвоста формы, нормированные единицы. Зеркало TailKnee: дно упирается в DepthThreshold − DepthKnee. 0 = выключено (по умолчанию). Нужно, если ложе уходит глубже реального океана: у ridged-профиля хвост достигает −25 км при средней глубине −4.4 км.")]
        public double DepthKnee = 0d;

        [Tooltip("Порог нижнего сжатия (нормированные единицы), со знаком. Выше него форма не тронута, поэтому берег и шельф не меняются. Ставить примерно на p5 глубин.")]
        public double DepthThreshold = 0d;

        [Header("Пляж (полоса у воды)")]
        [Tooltip("Высота пляжа над морем (м): ниже — песок в цвете и запрет спавна всего декора. 0 = пляжа нет (legacy).")]
        public double BeachHeightMeters = 0d;

        [Tooltip("Высота полки пляжа над морем (м): береговой рельеф стягивается к ней у уреза воды. 0 = полка выключена (legacy).")]
        public double BeachShelfAltitudeMeters = 0d;

        [Tooltip("Полуширина полки в единицах continent-маски (расстояние сырого шума от берегового порога). 0 = полка выключена (legacy).")]
        public double BeachShelfWidth = 0d;

        [Tooltip("Макс. множитель ширины полки пляжа (>1 включает неравномерный берег). Ширина по берегу плавно меняется от 1× до этого значения по низкочастотному шуму: где-то узкая полоса, где-то песчаная равнина в 10–15 раз шире. ≤1 = ширина везде одинаковая (legacy).")]
        public double BeachShelfWidthMaxScale = 1d;

        [Tooltip("Частота шума ширины пляжа (циклов на единичный вектор; длина волны ≈ радиус / частота). 0 = выключено. Ниже — крупные зоны, выше — частая смена ширины.")]
        public double BeachShelfWidthNoiseFrequency = 0d;

        [Tooltip("Октав шума ширины пляжа: первая задаёт крупные зоны, остальные добавляют локальную неровность.")]
        public int BeachShelfWidthNoiseOctaves = 4;

        [Header("Деталь и domain warp")]
        [Tooltip("Мелкомасштабная деталь как доля амплитуды (0 = выключена).")]
        public double DetailMix = 0d;

        [Tooltip("Базовая частота детали (циклов на единичный вектор). 0 = выключена.")]
        public double DetailFrequency = 0d;

        [Tooltip("Число октав детали.")]
        public int DetailOctaves = 5;

        [Tooltip("Сила domain-warp в единицах направления (типично 0.05..0.3). 0 = выключен.")]
        public double WarpStrength = 0d;

        [Tooltip("Базовая частота warp-шума (независимый поток сида).")]
        public double WarpFrequency = 1d;

        [Tooltip("Число октав warp-шума.")]
        public int WarpOctaves = 2;

        [Tooltip("Сдвиг warp-потока (целый).")]
        public int WarpSeedOffset = 0;

        [Header("Цвет рельефа (скалы, снег, биомы)")]
        [Tooltip("Порог rock-override в tan склона (круче — скала на любой высоте). 0 = выключен.")]
        public double ColorRockSlopeTan = 0d;

        [Tooltip("Полуширина бленда в скалу (единицы tan).")]
        public double ColorRockSlopeWidth = 0.1d;

        [Tooltip("Мин. нормированная высота для скалы: камни только на крупных горах. 0 = без порога.")]
        public double ColorRockHeightMin = 0d;

        [Tooltip("Макс. склон для снега в tan (круче — скала). 0 = выключен.")]
        public double ColorSnowSlopeTan = 0d;

        [Tooltip("Частота шума цветовой маски (разбивка полос). 0 = выключена.")]
        public double ColorNoiseFrequency = 0d;

        [Tooltip("Число октав шума маски.")]
        public int ColorNoiseOctaves = 3;

        [Tooltip("Сила маски (сдвиг t перед bands). 0 = выключена.")]
        public double ColorNoiseStrength = 0d;

        [Tooltip("Сдвиг потока маски (целый).")]
        public int ColorNoiseSeedOffset = 0;

        [Tooltip("Частота мелкомасштабной цветовой детали (моттлинг земли). 0 = выключена.")]
        public double ColorDetailFrequency = 0d;

        [Tooltip("Число октав цветовой детали.")]
        public int ColorDetailOctaves = 3;

        [Tooltip("Сила моттлинга 0..1 (0 = выключена).")]
        public double ColorDetailStrength = 0d;

        [Tooltip("Сдвиг потока цветовой детали (целый).")]
        public int ColorDetailSeedOffset = 0;

        [Header("Палитра")]
        public TerrainPaletteData Palette = new TerrainPaletteData();

        [Header("Текстуры рельефа (null = только процедурная палитра)")]
        [Tooltip("Альбедо низкогорья (обычно до ~60 м над морем).")]
        public Texture2D TextureLow;

        [Tooltip("Альбедо среднего пояса.")]
        public Texture2D TextureMid;

        [Tooltip("Альбедо высокогорья.")]
        public Texture2D TextureHigh;

        [Tooltip("Альбедо крутых склонов (скалы).")]
        public Texture2D TextureSteep;

        [Tooltip("Карта затенения (мягкий AO рельефа).")]
        public Texture2D TextureOcclusion;

        [Tooltip("Повторов текстуры на метр (0.04 = тайл 25 м).")]
        public double TextureScale = 0.04d;

        public double LowMidBlendStart = 30d;
        public double LowMidBlendEnd = 60d;
        public double MidHighBlendStart = 2500d;
        public double MidHighBlendEnd = 3500d;
        public double SteepBlendStart = 0.7d;
        public double SteepBlendEnd = 1.4d;

        public TerrainProfile Clone()
        {
            TerrainProfile copy = (TerrainProfile)MemberwiseClone();
            copy.Palette = Palette != null ? Palette.Clone() : new TerrainPaletteData();
            return copy;
        }

        /// <summary>
        /// Пресет «планета как Земля»: невысокий рельеф (≈0.8% радиуса),
        /// континенты + редкие хребты + равнины + лёгкий warp, slope-цвет и
        /// noise-маска. Раньше жил в BodyAuthoring.ApplyEarthLikeTerrainPreset —
        /// теперь это ассет EarthLike, на который ссылается тело.
        ///
        /// ИСТОЧНИК ИСТИНЫ — .asset, а не этот метод. Игра грузит
        /// Assets/_Project/Profiles/Terrain/EarthLike.asset из OutdoorsScene, и
        /// этот метод игрой не вызывается вообще — только тестами. Он обязан
        /// совпадать с ассетом, иначе тесты меряют конфигурацию, которой нет в
        /// игре; сверяет это T117_EarthLikeProfileDrift, который читает .asset
        /// как текст.
        ///
        /// Амплитуда остаётся параметром радиуса: ассет хранит фиксированные
        /// 9144 м, а для другого тела масштаб должен считаться от его радиуса.
        ///
        /// Океан/внутренность/горные пояса здесь ОСТАЮТСЯ НУЛЯМИ, и это не
        /// упущение: EarthLike.asset — legacy-профиль на value noise, и его
        /// контракт — бит-в-бит прежняя форма. Новая модель океана включена в
        /// EarthLike_Perlin.asset, на который ссылается OutdoorsScene.
        /// </summary>
        public static TerrainProfile CreateEarthLike(double radius)
        {
            return new TerrainProfile
            {
                AmplitudeMeters = System.Math.Max(200d, radius * 0.008d),
                BaseFrequency = 20d,
                Octaves = 10,
                SeaLevelMeters = 0d,
                Lacunarity = 2d,
                Gain = 0.5d,
                ContinentFrequency = 4d,
                ContinentOctaves = 3,
                ContinentThreshold = -0.1d,
                ContinentSharpness = 0.3d,
                ContinentDepth = 1.2d,
                RidgedMix = 0.7d,
                PlainMix = 0.85d,
                PlainFrequency = 1.8d,
                PlainOctaves = 2,
                PlainThreshold = 0.05d,
                PlainSharpness = 0.25d,
                PlainElevation = 0.1d,
                BeachHeightMeters = 12d,
                BeachShelfAltitudeMeters = 8d,
                BeachShelfWidth = 0.08d,
                BeachShelfWidthMaxScale = 14d,
                BeachShelfWidthNoiseFrequency = 150d,
                BeachShelfWidthNoiseOctaves = 4,
                DetailMix = 0d,
                DetailFrequency = 0d,
                DetailOctaves = 5,
                WarpStrength = 0.1d,
                WarpFrequency = 2d,
                WarpOctaves = 2,
                ColorRockSlopeTan = 0.6d,
                ColorRockSlopeWidth = 0.15d,
                ColorRockHeightMin = 0.4d,
                ColorSnowSlopeTan = 0.5d,
                ColorNoiseFrequency = 25d,
                ColorNoiseOctaves = 4,
                ColorNoiseStrength = 0.09d,
                ColorDetailFrequency = 400d,
                ColorDetailOctaves = 3,
                ColorDetailStrength = 0.15d
            };
        }

        /// <summary>
        /// Диагностика окон домена по склону. Возвращает текст предупреждения
        /// или null, если всё в порядке.
        ///
        /// Отдельный метод, а не OnValidate, потому что TerrainProfile - обычный
        /// [Serializable] класс, а не UnityEngine.Object: Unity не вызывает
        /// OnValidate на таких и молча бы его не звал. Реальная точка входа -
        /// TerrainProfileAsset.OnValidate, который держит этот профиль.
        ///
        /// Почему только предупреждение, а не автопочинка: при
        /// Gain * Lacunarity != 1 нормировка сигнала на (o+1) перестаёт делить
        /// на константу, и правильные окна зависят от профиля целиком. Угадать
        /// их можно, но это будет молчаливая подгонка формы рельефа. Сама
        /// защита в рантайме стоит в TerrainNoiseParams.FromTerrain.
        /// </summary>
        public string ValidateSlopeDampWindows()
        {
            if (!(SlopeDamp > 0d) || SlopeDampMode == (int)TerrainSlopeDampMode.Off)
            {
                return null;
            }

            if (TerrainNoise.SlopeDampWindowsValid(Gain, Lacunarity))
            {
                return null;
            }

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            return "Gain " + Gain.ToString("F3", ci)
                + " * Lacunarity " + Lacunarity.ToString("F3", ci)
                + " = " + (Gain * Lacunarity).ToString("F4", ci)
                + ", а окна домена по склону откалиброваны под произведение 1."
                + " Домен SlopeDamp будет отключён при загрузке (см. TerrainNoiseParams.FromTerrain):"
                + " при != 1 вклад октавы в наклон растёт как (Gain*Lacunarity)^o, и нормировка"
                + " на (o+1) перестаёт делить на константу. Верните Gain*Lacunarity = 1"
                + " (обычно Gain 0.5 при Lacunarity 2) либо выключите SlopeDamp.";
        }
    }
}
