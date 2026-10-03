using System;
using UnityEngine;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Сериализуемое описание детали. Offset — в системе корабля, м
    /// (локальная геометрия: например, танк выше, двигатель ниже).
    /// </summary>
    [Serializable]
    public class PartDefinition
    {
        public string Name = "Деталь";
        public double MassKg = 1000d;
        public double JointStrengthNewtons = 200000d;
        public Vector3 Offset;
    }
}
