using UnityEngine;

namespace Galilego.Building
{
    /// <summary>Вид детали в каталоге строительства.</summary>
    public enum PartKind
    {
        Engine = 0,
        FuelTank = 1,
        Separator = 2
    }

    /// <summary>
    /// Размеры и физика детали для данного экземпляра (радиус/высота задаются
    /// игроком для бака и отделителя, у двигателя — из габаритов модели).
    /// Все высоты — относительно ПИВОТА детали вдоль оси Y места (зенит).
    /// Пивот бака — дно, отделителя — середина кольца, двигателя — низ
    /// габаритов по центру оси.
    /// </summary>
    public readonly struct PartMetrics
    {
        /// <summary>Радиус цилиндра оболочки, м.</summary>
        public readonly float Radius;

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

        public PartMetrics(float radius, float height, float center, float bottom, float top,
            double massKg, double jointStrengthNewtons, double breakTorqueNm)
        {
            Radius = radius;
            Height = height;
            Center = center;
            Bottom = bottom;
            Top = top;
            MassKg = massKg;
            JointStrengthNewtons = jointStrengthNewtons;
            BreakTorqueNm = breakTorqueNm;
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
        public float SizeA;
        public float SizeB;
        public PartMetrics Metrics;
        public Vector3 Pivot;
        public int ParentIndex = -1;

        /// <summary>На верхний узел уже что-то посажено.</summary>
        public bool TopOccupied;

        /// <summary>На нижний узел уже что-то посажено.</summary>
        public bool BottomOccupied;

        /// <summary>Визуальный объект детали (живёт под трансформом корабля).</summary>
        public GameObject Visual;
    }
}
