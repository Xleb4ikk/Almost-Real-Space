using System;
using Galilego.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Геометрия поверхности тела. Жёсткий контракт (п.5/12 v3): высота h и
    /// нормаль n относятся К ОДНОЙ И ТОЙ ЖЕ геометрии — реализация обязана
    /// выводить n из той же функции формы, что и h. Рассинхрон «поверхность
    /// из одного места, нормаль из другого» запрещён: контактная точка
    /// (проекция) и разложение сил обязаны согласовываться, иначе трение
    /// держит там, где проекция уже отпустила, или наоборот.
    /// Реализации: SphericalTerrain (гладкая сфера) и HeightfieldTerrain
    /// (процедурный fBm-рельеф + уровень моря).
    /// </summary>
    public interface ITerrainModel
    {
        /// <summary>
        /// Высота поверхности над Radius (м) в тел-fixed координатах
        /// (lat/lon в радианах — те же, что выдаёт OrbitingBody.SurfaceLatLonAt).
        /// Вращение тела учтено самим фактом body-fixed координат.
        /// </summary>
        double GetHeightMeters(OrbitingBody body, double latitudeRadians, double longitudeRadians);

        /// <summary>
        /// Внешняя нормаль поверхности в точке relPos (относительно центра тела,
        /// мировые координаты) в момент timeSeconds. Время обязательно: рельеф
        /// вращается вместе с телом, нормаль в мировом базисе зависит от спина.
        /// </summary>
        Vector3d GetOutwardNormal(OrbitingBody body, Vector3d relativePosition, double timeSeconds);
    }

    /// <summary>
    /// Гладкая сфера: h≡0, нормаль — радиальная от центра тела. Точна по
    /// построению (не приближение): относительная позиция уже лежит на сфере.
    /// Без полюсов и тригонометрии — только нормализация.
    /// </summary>
    public sealed class SphericalTerrain : ITerrainModel
    {
        public double GetHeightMeters(OrbitingBody body, double latitudeRadians, double longitudeRadians)
        {
            return 0d;
        }

        public Vector3d GetOutwardNormal(OrbitingBody body, Vector3d relativePosition, double timeSeconds)
        {
            return relativePosition.Normalized;
        }
    }

    /// <summary>
    /// Процедурный рельеф: fBm value-noise на ТЕЛЕ-FIXED единичном направлении
    /// (не lat/lon-текстура — без швов и вырождений на полюсах). Детерминирован
    /// seed'ом: физика и рендер читают одну и ту же функцию.
    ///   H(lat,lon) = shape(dir(lat,lon))·Amplitude, кламп снизу уровнем моря.
    /// Море — жёсткий кламп: море физически плоское, посадка на воду = посадка.
    /// Фаза 1 (продвинутый heightfield): continent-маска (низкочастотный fBm →
    /// smoothstep океан/суша + глубина впадин), ridged-член (горные хребты,
    /// только на континентах) и domain-warp на НЕЗАВИСИМОМ шуме (свой поток
    /// сида — иначе складки коррелируют с маской континентов). Все новые
    /// параметры в нуле/дефолте дают бит-в-бит legacy-fBm (ветка SampleFbm
    /// ниже не тронута): старые сцены и тесты не меняются.
    /// Нормаль — из той же функции H через соседние точки, но мировые позиции
    /// соседей берутся body.GetSurfaceState (прямой маппинг lat/lon→world:
    /// tilt+spin учтены ЯДРОМ, не дубликатом математики): паритет по построению.
    /// </summary>
    public sealed class HeightfieldTerrain : ITerrainModel
    {
        /// <summary>Зерно шума: одинаковый seed = одинаковый рельеф.</summary>
        public int Seed;

        /// <summary>Амплитуда рельефа над/под Radius (м). Номинальная: с включённой
        /// глубиной континентов max |H| ≤ Amplitude·(1 + ContinentDepth).</summary>
        public double AmplitudeMeters = 1000d;

        /// <summary>Базовая частота (циклов на единичный вектор направления).</summary>
        public double BaseFrequency = 3d;

        /// <summary>Число октав (каждая ×Lacunarity частота, ×Gain амплитуда).</summary>
        public int Octaves = 5;

        /// <summary>Уровень моря над Radius (м). −∞ = моря нет.</summary>
        public double SeaLevelMeters = double.NegativeInfinity;

        /// <summary>Лакунарность fBm (множитель частоты на октаву). 2 = legacy; вне [1, 8] = legacy.</summary>
        public double Lacunarity = 2d;

        /// <summary>Затухание амплитуды на октаву. 0.5 = legacy; ≤0 или &gt;1 = legacy.</summary>
        public double Gain = 0.5d;
        /// <summary>Частота континентальной маски. ≤0 = выключена (маска ≡ 1, legacy).</summary>
        public double ContinentFrequency = 0d;

        /// <summary>Число октав маски континентов.</summary>
        public int ContinentOctaves = 3;

        /// <summary>Порог маски: выше — суша, ниже — океан.</summary>
        public double ContinentThreshold = 0d;

        /// <summary>Полуширина smoothstep-перехода маски.</summary>
        public double ContinentSharpness = 0.25d;

        /// <summary>Глубина океанических впадин в долях амплитуды (вычитается там, где маски нет).</summary>
        public double ContinentDepth = 0.75d;

        /// <summary>Доля ridged-шума 0..1 (горные хребты, только на континентах). 0 = выключен (legacy).</summary>
        public double RidgedMix = 0d;

        /// <summary>
        /// Сила равнин 0..1: в зонах маски рельеф стягивается к низкому плато.
        /// 0 = выключено (legacy, бит-в-бит). Равнины только на континентах.
        /// </summary>
        public double PlainMix = 0d;

        /// <summary>Частота низкочастотной маски равнин. ≤0 = выключена (legacy).</summary>
        public double PlainFrequency = 0d;

        /// <summary>Число октав маски равнин.</summary>
        public int PlainOctaves = 2;

        /// <summary>Порог маски равнин: выше — равнина.</summary>
        public double PlainThreshold = 0d;

        /// <summary>Полуширина smoothstep-перехода маски равнин.</summary>
        public double PlainSharpness = 0.3d;

        /// <summary>Нормализованная высота плато равнин (доля AmplitudeMeters).</summary>
        public double PlainElevation = 0.1d;

        /// <summary>
        /// Мелкомасштабная деталь (скалы/осыпи) как доля AmplitudeMeters.
        /// Добавляется высокочастотным потоком только на суше, гаснет в равнинах.
        /// 0 = выключено (legacy).
        /// </summary>
        public double DetailMix = 0d;

        /// <summary>Базовая частота детали (циклов на единичный вектор). ≤0 = выключена.</summary>
        public double DetailFrequency = 0d;

        /// <summary>Число октав детали.</summary>
        public int DetailOctaves = 5;

        /// <summary>Сила domain-warp в единицах направления (типично 0.05..0.3). 0 = выключен (legacy).</summary>
        public double WarpStrength = 0d;

        /// <summary>Базовая частота warp-шума.</summary>
        public double WarpFrequency = 1d;

        /// <summary>Число октав warp-шума.</summary>
        public int WarpOctaves = 2;

        /// <summary>Сдвиг warp-потока (целый). Warp всегда на независимом шуме:
        /// другие константы сида + этот оффсет — корреляции с базовым fBm нет по построению.</summary>
        public int WarpSeedOffset = 0;

        /// <summary>
        /// Порог rock-override в тангенсе угла склона: круче — скала на любой
        /// высоте суши (включая пляж). ≤0 = выключен (legacy: скала только по
        /// высоте). ВАЖНО: это guard, а не буквальное сравнение — при 0 любой
        /// ненулевой наклон проходил бы порог «≥ 0» почти везде.
        /// </summary>
        public double ColorRockSlopeTan = 0d;

        /// <summary>Полуширина smoothstep-бленда в скалу (в единицах tan).</summary>
        public double ColorRockSlopeWidth = 0.1d;

        /// <summary>
        /// Минимальная нормированная высота t=(h−sea)/amp для rock-override:
        /// скала только на крупных горах, пляж и низменности остаются зелёными.
        /// ≤0 = без порога по высоте (legacy: скала по одному склону).
        /// </summary>
        public double ColorRockHeightMin = 0d;

        /// <summary>
        /// Макс. склон для снега (tan): круче — скала вместо снега. ≤0 =
        /// выключен (legacy: снег чисто по высоте). Guard симметричен rock:
        /// при 0 условие «slope ≤ 0» не проходило бы почти нигде.
        /// </summary>
        public double ColorSnowSlopeTan = 0d;

        /// <summary>
        /// Частота шума цветовой маски (разбивка полос). ≤0 = выключена.
        /// Независимый поток сида (salt 4) — разрывы цвета не коррелируют
        /// ни с хребтами, ни с warp.
        /// </summary>
        public double ColorNoiseFrequency = 0d;

        /// <summary>Число октав шума маски.</summary>
        public int ColorNoiseOctaves = 3;

        /// <summary>
        /// Сила маски: сдвиг нормализованной высоты t перед bands. 0 = выкл.
        /// Маска вычисляется ДО слоёв (сдвигает пороги, а не красит поверх).
        /// </summary>
        public double ColorNoiseStrength = 0d;

        /// <summary>Сдвиг потока маски (целый).</summary>
        public int ColorNoiseSeedOffset = 0;

        /// <summary>
        /// Частота мелкомасштабной цветовой детали (моттлинг земли). ≤0 = выкл.
        /// Отдельный поток (salt 7): пятна почвы на земле.
        /// </summary>
        public double ColorDetailFrequency = 0d;

        /// <summary>Число октав цветовой детали.</summary>
        public int ColorDetailOctaves = 3;

        /// <summary>Сила моттлинга 0..1 (0 = выкл).</summary>
        public double ColorDetailStrength = 0d;

        /// <summary>Сдвиг потока цветовой детали (целый).</summary>
        public int ColorDetailSeedOffset = 0;

        /// <summary>
        /// Палитра поверхности (sRGB). Рендер переводит её в Linear на лету;
        /// пресет живёт в TerrainProfile. Дефолт = старые константы TerrainPalette.
        /// </summary>
        public TerrainPaletteData Palette = new TerrainPaletteData();

        /// <summary>Текстуры рельефа из профиля (null = процедурная палитра).</summary>
        public Texture2D TextureLow;
        public Texture2D TextureMid;
        public Texture2D TextureHigh;
        public Texture2D TextureSteep;
        public Texture2D TextureOcclusion;
        public double TextureScale = 0.04d;
        public double LowMidBlendStart = 30d;
        public double LowMidBlendEnd = 60d;
        public double MidHighBlendStart = 2500d;
        public double MidHighBlendEnd = 3500d;
        public double SteepBlendStart = 0.7d;
        public double SteepBlendEnd = 1.4d;

        /// <summary>
        /// Скопировать профиль в живое runtime-представление. ЕДИНСТВЕННОЕ место
        /// копирования (T91): раньше ~39 полей дублировались в BodyAuthoring,
        /// BodyBlueprint, SystemBlueprint.Build и ApplyToTerrain.
        /// Seed — параметр: он per-body, профиль — пресет.
        /// </summary>
        public void ApplyProfile(TerrainProfile profile, int seed)
        {
            if (profile == null)
            {
                return;
            }

            Seed = seed;
            AmplitudeMeters = profile.AmplitudeMeters;
            BaseFrequency = profile.BaseFrequency;
            Octaves = profile.Octaves;
            SeaLevelMeters = profile.SeaLevelMeters;
            Lacunarity = profile.Lacunarity;
            Gain = profile.Gain;
            ContinentFrequency = profile.ContinentFrequency;
            ContinentOctaves = profile.ContinentOctaves;
            ContinentThreshold = profile.ContinentThreshold;
            ContinentSharpness = profile.ContinentSharpness;
            ContinentDepth = profile.ContinentDepth;
            RidgedMix = profile.RidgedMix;
            PlainMix = profile.PlainMix;
            PlainFrequency = profile.PlainFrequency;
            PlainOctaves = profile.PlainOctaves;
            PlainThreshold = profile.PlainThreshold;
            PlainSharpness = profile.PlainSharpness;
            PlainElevation = profile.PlainElevation;
            DetailMix = profile.DetailMix;
            DetailFrequency = profile.DetailFrequency;
            DetailOctaves = profile.DetailOctaves;
            WarpStrength = profile.WarpStrength;
            WarpFrequency = profile.WarpFrequency;
            WarpOctaves = profile.WarpOctaves;
            WarpSeedOffset = profile.WarpSeedOffset;
            ColorRockSlopeTan = profile.ColorRockSlopeTan;
            ColorRockSlopeWidth = profile.ColorRockSlopeWidth;
            ColorRockHeightMin = profile.ColorRockHeightMin;
            ColorSnowSlopeTan = profile.ColorSnowSlopeTan;
            ColorNoiseFrequency = profile.ColorNoiseFrequency;
            ColorNoiseOctaves = profile.ColorNoiseOctaves;
            ColorNoiseStrength = profile.ColorNoiseStrength;
            ColorNoiseSeedOffset = profile.ColorNoiseSeedOffset;
            ColorDetailFrequency = profile.ColorDetailFrequency;
            ColorDetailOctaves = profile.ColorDetailOctaves;
            ColorDetailStrength = profile.ColorDetailStrength;
            ColorDetailSeedOffset = profile.ColorDetailSeedOffset;
            Palette = profile.Palette ?? new TerrainPaletteData();
            TextureLow = profile.TextureLow;
            TextureMid = profile.TextureMid;
            TextureHigh = profile.TextureHigh;
            TextureSteep = profile.TextureSteep;
            TextureOcclusion = profile.TextureOcclusion;
            TextureScale = profile.TextureScale;
            LowMidBlendStart = profile.LowMidBlendStart;
            LowMidBlendEnd = profile.LowMidBlendEnd;
            MidHighBlendStart = profile.MidHighBlendStart;
            MidHighBlendEnd = profile.MidHighBlendEnd;
            SteepBlendStart = profile.SteepBlendStart;
            SteepBlendEnd = profile.SteepBlendEnd;
        }

        /// <summary>Собрать runtime-рельеф из профиля (копия палитры — ассет не мутируем).</summary>
        public static HeightfieldTerrain FromProfile(TerrainProfile profile, int seed)
        {
            var terrain = new HeightfieldTerrain();
            terrain.ApplyProfile(profile, seed);
            terrain.Palette = terrain.Palette.Clone();
            return terrain;
        }

        /// <summary>Угловой шаг соседей для нормали (рад): разрешает 5 октав с запасом.</summary>
        private const double NormalEpsilonRadians = 1e-4d;

        public double GetHeightMeters(OrbitingBody body, double latitudeRadians, double longitudeRadians)
        {
            // Форма считается ЕДИНСТВЕННОЙ реализацией — TerrainNoise (Burst):
            // физика и рендер читают одну функцию, дублирования нет.
            Vector3d direction = LatLonToDirection(latitudeRadians, longitudeRadians);
            double height = TerrainNoise.SampleHeight(
                TerrainNoiseParams.FromTerrain(this),
                new double3(direction.X, direction.Y, direction.Z)) * AmplitudeMeters;
            if (height < SeaLevelMeters)
            {
                height = SeaLevelMeters;
            }

            return height;
        }

        public Vector3d GetOutwardNormal(OrbitingBody body, Vector3d relativePosition, double timeSeconds)
        {
            body.EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out _);
            body.SurfaceLatLonAt(bodyPosition + relativePosition, timeSeconds, out double latDeg, out double lonDeg);
            double lat = latDeg * (Math.PI / 180d);
            double lon = lonDeg * (Math.PI / 180d);

            double latUp = Math.Min(lat + NormalEpsilonRadians, 1.5707963267948966d - 1e-9d);
            double h0 = GetHeightMeters(body, lat, lon);
            double hUp = GetHeightMeters(body, latUp, lon);
            double hEast = GetHeightMeters(body, lat, lon + NormalEpsilonRadians);

            body.GetSurfaceState(lat * (180d / Math.PI), lon * (180d / Math.PI), h0, timeSeconds, out Vector3d p0, out _);
            body.GetSurfaceState(latUp * (180d / Math.PI), lon * (180d / Math.PI), hUp, timeSeconds, out Vector3d p1, out _);
            body.GetSurfaceState(lat * (180d / Math.PI), (lon + NormalEpsilonRadians) * (180d / Math.PI), hEast, timeSeconds, out Vector3d p2, out _);

            Vector3d normal = Vector3d.Cross(p1 - p0, p2 - p0).Normalized;
            if (Vector3d.Dot(normal, relativePosition) < 0d)
            {
                normal = -normal;
            }

            return normal;
        }

        /// <summary>
        /// Шум цветовой маски в ~[−1, 1] по lat/lon (радианы): сдвигает границы
        /// высотных bands, разбивая ровные полосы. Чистая функция направления —
        /// детерминирована seed'ом, вызывается рендером на вершину (не физикой).
        /// </summary>
        public double SampleColorNoise(double latitudeRadians, double longitudeRadians)
        {
            Vector3d direction = LatLonToDirection(latitudeRadians, longitudeRadians);
            return TerrainNoise.SampleColorNoise(
                TerrainNoiseParams.FromTerrain(this),
                new double3(direction.X, direction.Y, direction.Z));
        }

        /// <summary>Мелкомасштабная цветовая деталь (моттлинг) в ~[−1, 1] по lat/lon.</summary>
        public double SampleColorDetailNoise(double latitudeRadians, double longitudeRadians)
        {
            Vector3d direction = LatLonToDirection(latitudeRadians, longitudeRadians);
            return TerrainNoise.SampleColorDetailNoise(
                TerrainNoiseParams.FromTerrain(this),
                new double3(direction.X, direction.Y, direction.Z));
        }

        /// <summary>Тел-fixed направление из lat/lon (радианы) — тот же базис, что в GetSurfaceState.</summary>
        private static Vector3d LatLonToDirection(double lat, double lon)
        {
            double cosLat = Math.Cos(lat);
            return new Vector3d(cosLat * Math.Cos(lon), cosLat * Math.Sin(lon), Math.Sin(lat));
        }

    }
}
