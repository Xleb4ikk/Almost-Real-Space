using System;
using UnityEngine;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Форма детали для тензора инерции. Point — масса без геометрии (этап R2,
    /// как раньше). Размеры в Dimensions, м:
    ///   Box:      полные размеры вдоль осей детали (x, y, z);
    ///   Cylinder: x = радиус, y = высота вдоль оси Y детали, центр в середине;
    ///   Sphere:   x = радиус.
    /// </summary>
    public enum PartShape
    {
        Point = 0,
        Box = 1,
        Cylinder = 2,
        Sphere = 3,
    }

    /// <summary>
    /// Сериализуемое описание детали. Offset — центр детали в системе корабля, м.
    /// Orientation — поворот системы детали относительно системы корабля
    /// (кватернион x, y, z, w; по умолчанию тождественный). Масса — явная:
    /// форма задаёт только инерцию, плотность не используется.
    /// ParentIndex — индекс родительской детали в массиве, -1 для корня.
    /// JointAnchor и EngineOffset — точки в системе детали, м.
    /// BreakTorqueNm — предел момента стыка; JointStrengthNewtons — предел силы.
    /// Поля стыков в этой фазе только данные: граф проверяется в фазе 2.
    /// </summary>
    [Serializable]
    public class PartDefinition
    {
        public string Name = "Деталь";
        public double MassKg = 1000d;
        public double JointStrengthNewtons = 200000d;
        public double BreakTorqueNm = 1e12d;
        public Vector3 Offset;
        public PartShape Shape = PartShape.Point;
        public Vector3 Dimensions;
        public Vector4 Orientation = new Vector4(0f, 0f, 0f, 1f);
        public int ParentIndex = -1;
        public Vector3 JointAnchor;
        public Vector3 EngineOffset;
    }
}
