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

        [Tooltip("Число октав шума распределения.")]
        public int DistributionOctaves = 4;

        [Tooltip("Сдвиг потока шума распределения (целый): слои не совпадают.")]
        public int DistributionSeedOffset = 0;

        [Tooltip("Порог кластеризации: ниже — реже. 0 = шум не режет.")]
        public double ClusterThreshold = 0d;

        [Header("Фильтры поверхности")]
        [Tooltip("Мин. высота над уровнем моря (м).")]
        public double MinAltitudeMeters = 0d;

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

        [Header("Масштаб и LOD")]
        public double MinScale = 0.7d;
        public double MaxScale = 1.3d;

        [Tooltip("Осевой офсет инстанса (м): приподнять модель, если её пивот не в основании. Отрицательный min.y меша × масштаб.")]
        public double GroundOffsetMeters = 0d;

        [Tooltip("Погружение в землю: доля масштаба инстанса (0.3 = закопать на 30% высоты). Для камней.")]
        public double GroundSinkFactor = 0d;

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
