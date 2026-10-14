using Galilego.Spacecraft;
using UnityEngine;

namespace Galilego.Building
{
    /// <summary>Вид детали в каталоге строительства.</summary>
    public enum PartKind
    {
        Engine = 0,
        FuelTank = 1,
        Separator = 2,
        Capsule = 3
    }

    /// <summary>
    /// Форма топливного бака: прямой цилиндр или обрезанный конус (бак-
    /// переходник между ступенями разных диаметров: снизу один радиус,
    /// сверху другой, образующая — прямая).
    /// </summary>
    public enum TankForm
    {
        Cylinder = 0,
        Cone = 1
    }

    /// <summary>
    /// Заготовка детали для слота панели: вид и его размеры. Размеры хранятся
    /// В СЛОТЕ, а не глобально по виду: в панели можно заранее подготовить баки
    /// разных форм и диаметров (например, цилиндр 2.5 м и конус 5→2.5 м) и
    /// ставить их по очереди, не перенастраивая размеры заново.
    /// </summary>
    public struct PartPreset
    {
        public PartKind Kind;

        /// <summary>Форма бака (у прочих деталей не значима).</summary>
        public TankForm Form;

        /// <summary>Бак — нижний радиус; отделитель — радиус; двигатель — не значим.</summary>
        public float Radius;

        /// <summary>Верхний радиус бака (равен Radius — цилиндр).</summary>
        public float TopRadius;

        /// <summary>Длина бака, м.</summary>
        public float Height;

        /// <summary>Наполнение бака: компоненты и их доли объёма (у прочих деталей null).</summary>
        public PropellantLoad Load;

        /// <summary>Двигатель (Id из EngineSpecs; у прочих деталей не значим, null — по умолчанию).</summary>
        public string EngineId;
    }

    /// <summary>
    /// Размеры и физика детали для данного экземпляра (радиусы/высота задаются
    /// игроком для бака и отделителя, у двигателя — из габаритов модели).
    /// Все высоты — относительно ПИВОТА детали вдоль оси Y места (зенит).
    /// Пивот бака — дно, отделителя — середина кольца, двигателя — низ
    /// габаритов по центру оси.
    /// </summary>
    public readonly struct PartMetrics
    {
        /// <summary>Нижний радиус тела вращения, м (бак: радиус дна).</summary>
        public readonly float Radius;

        /// <summary>
        /// Верхний радиус тела вращения, м. Равен Radius — деталь цилиндрическая;
        /// отличается — обрезанный конус (у отделителя и двигателя всегда равен
        /// нижнему: их форма не конусная).
        /// </summary>
        public readonly float TopRadius;

        /// <summary>Полная высота формы вдоль оси, м.</summary>
        public readonly float Height;

        /// <summary>Центр формы относительно пивота, м.</summary>
        public readonly float Center;

        /// <summary>Низ формы относительно пивота, м.</summary>
        public readonly float Bottom;

        /// <summary>Верх формы относительно пивота, м.</summary>
        public readonly float Top;

        public readonly double MassKg;
        public readonly double JointStrengthNewtons;
        public readonly double BreakTorqueNm;

        /// <summary>Сухая масса (металл + изоляция) без топлива, кг.</summary>
        public readonly double DryMassKg;

        /// <summary>Масса топлива в баке, кг (0 у прочих деталей).</summary>
        public readonly double PropellantMassKg;

        /// <summary>Внутренний объём формы, м³ (0 у прочих деталей).</summary>
        public readonly double VolumeM3;

        /// <summary>Наполнение бака (компоненты и их доли объёма; null у прочих деталей).</summary>
        public readonly PropellantLoad Load;

        public PartMetrics(float radius, float topRadius, float height, float center, float bottom, float top,
            double massKg, double jointStrengthNewtons, double breakTorqueNm)
            : this(radius, topRadius, height, center, bottom, top, massKg, jointStrengthNewtons, breakTorqueNm,
                massKg, 0d, 0d, null)
        {
        }

        public PartMetrics(float radius, float topRadius, float height, float center, float bottom, float top,
            double massKg, double jointStrengthNewtons, double breakTorqueNm,
            double dryMassKg, double propellantMassKg, double volumeM3, PropellantLoad load)
        {
            Radius = radius;
            TopRadius = topRadius > 0.01f ? topRadius : radius;
            Height = height;
            Center = center;
            Bottom = bottom;
            Top = top;
            MassKg = massKg;
            JointStrengthNewtons = jointStrengthNewtons;
            BreakTorqueNm = breakTorqueNm;
            DryMassKg = dryMassKg;
            PropellantMassKg = propellantMassKg;
            VolumeM3 = volumeM3;
            Load = load;
        }
    }

    /// <summary>
    /// Деталь, поставленная игроком: вид, размеры, положение пивота в ОСЯХ МЕСТА
    /// (x — север, y — зенит, z — восток), родитель по снапу и занятость узлов
    /// (на узел можно посадить только одну деталь). Метрики кэшируются при
    /// постановке: параметры размера фиксируются на момент постановки.
    /// </summary>
    public sealed class PlacedPart
    {
        public PartKind Kind;

        /// <summary>Форма бака на момент постановки (у прочих деталей не значима).</summary>
        public TankForm Form;

        public float SizeA;
        public float SizeB;

        /// <summary>
        /// Паспорт двигателя на момент постановки (у прочих деталей null):
        /// имя детали для физики, топливная пара и характеристики.
        /// </summary>
        public EngineSpec EngineSpec;

        /// <summary>Верхний радиус бака на момент постановки (равен SizeA — цилиндр).</summary>
        public float SizeTop;

        public PartMetrics Metrics;
        public Vector3 Pivot;
        public int ParentIndex = -1;

        /// <summary>Как деталь пристыкована: true — низом на ВЕРХ родителя.</summary>
        public bool ParentOnTop;

        /// <summary>На верхний узел уже что-то посажено.</summary>
        public bool TopOccupied;

        /// <summary>На нижний узел уже что-то посажено.</summary>
        public bool BottomOccupied;

        /// <summary>Визуал детали: живёт под трансформом корабля, умеет краснеть
        /// (удаление по прицелу) и возвращать родные материалы.</summary>
        public PartCatalog.PartVisual Visual;
    }
}
