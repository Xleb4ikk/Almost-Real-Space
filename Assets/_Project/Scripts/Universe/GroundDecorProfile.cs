using System;
using System.Collections.Generic;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Один слой декора местности (растительность/камни): near/far геометрия,
    /// распределение по шуму, фильтры поверхности (высота/склон/биом), масштаб
    /// и дистанции LOD. Зерно берётся из тела (BodyAuthoring.TerrainSeed) —
    /// пресет слоя общий, рельеф у каждой планеты свой.
    /// </summary>
    [Serializable]
    public sealed class GroundDecorLayer
    {
        /// <summary>
        /// Верх песчаной полосы в нормированной высоте палитры (t = h/amp +
        /// mask·strength): зеркалит порог Sand в TerrainPalette.BaseColor.
        /// Растительность обязана стоять выше.
        /// </summary>
        public const double SandTopNormalizedHeight = 0.03d;

        /// <summary>
        /// Низ скальной зоны в нормированной высоте палитры: зеркалит порог
        /// перехода lowland→Rock в TerrainPalette.BaseColor. Выше — только камни.
        /// </summary>
        public const double RockBottomNormalizedHeight = 0.45d;

        /// <summary>
        /// Нижняя граница зелени палитры по влажности (wet01 из той же маски,
        /// что красит рельеф): зеркалит переход DryGrass→Grass в
        /// TerrainPalette.BiomeColor. Трава/деревья/луговые цветы — не ниже.
        /// </summary>
        public const double GreenWetMin = 0.38d;

        public string Name = "Grass";
        public bool Enabled = true;

        [Header("Геометрия (near/far)")]
        [Tooltip("Меши ближнего LOD (solid, GPU-instanced). Каждому инстансу детерминированно достаётся один — разнообразие видов.")]
        public Mesh[] NearMeshes;

        [Tooltip("Меш дальнего LOD (биллборд с альфа-клипом). null = дальний = ближний меш.")]
        public Mesh FarBillboardMesh;

        public Material NearMaterial;
        public Material FarMaterial;

        [Header("Распределение")]
        [Tooltip("Средний шаг кандидатов по поверхности (м): меньше — плотнее и дороже.")]
        public double SpacingMeters = 2d;

        [Tooltip("Максимум инстансов на чанк (защита от лавины при мелком шаге).")]
        public int MaxInstancesPerChunk = 1200;

        [Tooltip("Множитель плотности 0..1 после шума распределения и склона.")]
        public double Density = 1d;

        [Tooltip("Частота шума распределения (кластеры).")]
        public double DistributionFrequency = 5000d;

        [Tooltip("Размер пятна кластеризации (м, диаметр базовой волны): " +
                 "переводится в частоту по радиусу тела (планета-независимо). " +
                 "0 = использовать DistributionFrequency как есть.")]
        public double ClusterPatchMeters = 0d;

        [Tooltip("Число октав шума распределения.")]
        public int DistributionOctaves = 4;

        [Tooltip("Сдвиг потока шума распределения (целый): слои не совпадают.")]
        public int DistributionSeedOffset = 0;

        [Tooltip("Порог кластеризации: ниже — реже. 0 = шум не режет.")]
        public double ClusterThreshold = 0d;

        [Tooltip("Ширина мягкого края островов в единицах шума распределения: " +
                 "0 = жёсткий порог (старое поведение). Больше — плавнее граница пятна.")]
        public double ClusterFade = 0d;

        [Header("Фильтры поверхности")]
        [Tooltip("Мин. высота над уровнем моря (м).")]
        public double MinAltitudeMeters = 0d;

        [Tooltip("Мин. нормированная высота (доля амплитуды, с той же цветовой " +
                 "маской, что у палитры): ниже — песчаная полоса пляжа. 0.03 = " +
                 "граница песка в шейдере рельефа. 0 = фильтр выключен.")]
        public double MinNormalizedHeight = 0d;

        [Tooltip("Макс. нормированная высота (доля амплитуды, с той же цветовой " +
                 "маской, что у палитры): выше — скалы/снег, растительности нет. 0.45 = " +
                 "начало скальной зоны в шейдере рельефа. <=0 = фильтр выключен " +
                 "(камни — единственный слой без верхней границы).")]
        public double MaxNormalizedHeight = 0d;

        [Tooltip("Макс. высота над уровнем моря (м).")]
        public double MaxAltitudeMeters = 1e9d;

        [Tooltip("Макс. тангенс склона: круче — не сажаем.")]
        public double MaxSlopeTan = 1.2d;

        [Tooltip("Не сажать в воду (сырая высота ≤ уровня моря).")]
        public bool AvoidWater = true;

        [Tooltip("Биом по влажности: мин. wet01 (та же маска, что красит рельеф).")]
        public double WetMin = 0d;

        [Tooltip("Биом по влажности: макс. wet01.")]
        public double WetMax = 1d;

        [Tooltip("Не применять полярный фейд (тундра/снег) к слою: растительность " +
                 "гаснет к полюсам как визуальный биом, а камни/валуны лежат и на снегу. " +
                 "false = слой ограничен широтой (дефолт для растительности).")]
        public bool IgnoreLatitude;

        [Tooltip("Ширина мягкого края по влажности (0 = жёсткий порог). " +
                 "Убирает резкую границу травы по биому — плотность гаснет плавно.")]
        public double WetFade = 0d;

        [Header("Масштаб и LOD")]
        public double MinScale = 0.7d;
        public double MaxScale = 1.3d;

        [Tooltip("Осевой офсет инстанса (м): приподнять модель, если её пивот не в основании. Отрицательный min.y меша × масштаб.")]
        public double GroundOffsetMeters = 0d;

        [Tooltip("Погружение в землю: доля масштаба инстанса (0.3 = закопать на 30% высоты). Для камней.")]
        public double GroundSinkFactor = 0d;

        [Header("Ветер (направление по зонам)")]
        [Tooltip("Частота крупных ветровых зон. 0 = зональный ветер выключен " +
                 "(инстансы смотрят полностью случайно, как раньше).")]
        public double WindZoneFrequency = 0d;
        public int WindZoneOctaves = 3;
        [Tooltip("Смещение шумового потока (см. TerrainNoise.SampleDecorNoise). " +
                 "Оставь зазор с DistributionSeedOffset других слоёв, чтобы " +
                 "ветровые зоны не совпали с их кластерами.")]
        public int WindZoneSeedOffset = 20;
        public double WindLeanMinDegrees = 0d;
        public double WindLeanMaxDegrees = 0d;
        [Tooltip("Разброс угла вокруг направления зоны (градусы). При " +
                 "WindZoneFrequency=0 держи 360 — тогда поведение как раньше " +
                 "(полностью случайный поворот).")]
        public double WindJitterDegrees = 360d;

        [Header("Закапывание (разброс на инстанс)")]
        [Tooltip("-1 = не задано: используем старое поведение (один " +
                 "GroundSinkFactor на весь слой, как сейчас у камней).")]
        public double MinGroundSinkFactor = -1d;
        public double MaxGroundSinkFactor = -1d;

        [Tooltip("Класть меш плашмя на землю (ромашки-пятачки): локальный Z меша смотрит вдоль нормали, а не Y.")]
        public bool FlatOnGround;

        [Header("Тени")]
        [Tooltip("Писать слой в shadow map HDRP. Тысячи инстансов травы — дорого: каст ограничивается радиусом ниже.")]
        public bool CastShadows = true;

        [Tooltip("Радиус каста теней (м, от камеры): 0 = без ограничения. Для травы дешёвый компромисс: настоящие тени вблизи, дальше только приём.")]
        public float ShadowCastDistanceMeters = 0f;

        [Header("Коллизия")]
        [Tooltip("Стволы слоя физически не пропускают игрока (цилиндр по нормали).")]
        public bool Collides;

        [Tooltip("Радиус ствола для коллизии (м).")]
        public double CollisionRadiusMeters = 0.5d;

        [Tooltip("Высота цилиндра коллизии (м).")]
        public double CollisionHeightMeters = 20d;

        [Tooltip("Степень отбора по склону: больше — резче падает плотность на склонах.")]
        public double SteepPower = 8d;

        [Tooltip("Ближний LOD внутри этой дистанции (м); дальше — биллборд до MaxDistance.")]
        public float NearDistanceMeters = 80f;

        [Tooltip("Ограничитель плотности кандидатов на чанк (ячеек на ось). Больше — плотнее декор на крупных LOD.")]
        public int MaxCellsPerAxis = 64;

        [Tooltip("Дальше этой дистанции слой не рисуется (м).")]
        public float MaxDistanceMeters = 600f;

        [Tooltip("Масштаб экспоненциального затухания плотности от камеры (м). " +
                 "Больше 0 — плотность падает как exp(−(d−core)/scale): основная " +
                 "масса инстансов у игрока, дальше редко, и лимит чанка не режет " +
                 "острова. 0 = линейное затухание NearDistance..MaxDistance.")]
        public float DensityFalloffMeters = 0f;

        [Tooltip("Радиус «плоского ядра» плотности (м): до этой дистанции от центра " +
                 "облака плотность полная, дальше гаснет по DensityFalloffMeters. " +
                 "Ядро больше порога пересборки — плотность под игроком не «дышит». " +
                 "0 = затухание от самого центра (старое поведение).")]
        public float DensityCoreMeters = 0f;

        [Tooltip("Плотность и затухание — НА КАЖДЫЙ ПОДТУФТ, а не на клетку: " +
                 "край острова получается плавным, а не блочным (важно, когда " +
                 "ячейка сетки крупнее шага и внутри неё много подтуфт).")]
        public bool PerInstanceDensity;

        [Tooltip("Плотность на дальней границе (0..1). 1 = не резать по дистанции; " +
                 "меньше — плавно прореживаем дальние чанки (дешевле cpu/гпу).")]
        public double FarDensity = 1d;

        [Tooltip("Запас (м) к MaxDistanceMeters: декор строится чуть раньше, " +
                 "чем чанк войдёт в зону видимости — без «выскакивания» травы.")]
        public float SpawnMarginMeters = 150f;

        [Tooltip("Максимум подтуфт на ячейку сетки. Подтуфты включаются " +
                 "АДАПТИВНО, когда ячейка крупнее SpacingMeters (грузный LOD/" +
                 "высота): внутри ячейки сажается сетка jitter-подтуфт с шагом " +
                 "≈ SpacingMeters, чтобы острова травы не рассыпались. " +
                 "1 = одна туфта на ячейку (старое поведение).")]
        public int SubInstancesPerCell = 1;
    }

    /// <summary>
    /// Набор слоёв декора (трава/камни/деревья) для тела. Живёт в ассете
    /// GroundDecorProfileAsset; планета ссылается на пресет с BodyAuthoring.
    /// </summary>
    [Serializable]
    public sealed class GroundDecorProfile
    {
        public List<GroundDecorLayer> Layers = new List<GroundDecorLayer>();
    }
}
