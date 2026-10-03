using System;
using System.Collections.Generic;
using Galilego.Core;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Деталь корабля как масс-точка: масса, положение в теле-фиксированной
    /// системе, прочность стыка. Без геометрии сознательно (этап R2): этого
    /// достаточно для точной массы, центра масс и тензора инерции.
    /// </summary>
    public readonly struct Part
    {
        public readonly double MassKg;
        public readonly Vector3d LocalPosition;
        public readonly double JointStrengthNewtons;

        public Part(double massKg, Vector3d localPosition, double jointStrengthNewtons)
        {
            MassKg = massKg;
            LocalPosition = localPosition;
            JointStrengthNewtons = jointStrengthNewtons;
        }
    }

    /// <summary>
    /// Сводка сборки: суммарная масса, центр масс, тензор инерции.
    /// Тензор считается СТРОГО от центра масс (d = r − COM):
    /// уравнения Эйлера в форме ω̇=I⁻¹(τ−ω×Iω) верны только для тензора
    /// относительно COM; счёт от начала координат (пропущенная теорема
    /// Гюйгенса–Штейнера) даёт неправильное свободное вращение. Ловушка R2a
    /// проверяет именно это на намеренно смещённой конфигурации.
    /// </summary>
    public readonly struct AssemblyStats
    {
        public readonly double TotalMassKg;
        public readonly Vector3d CenterOfMass;
        public readonly Matrix3x3 Inertia;

        public AssemblyStats(double totalMassKg, Vector3d centerOfMass, Matrix3x3 inertia)
        {
            TotalMassKg = totalMassKg;
            CenterOfMass = centerOfMass;
            Inertia = inertia;
        }

        public static AssemblyStats Compute(IReadOnlyList<Part> parts)
        {
            if (parts == null || parts.Count == 0)
            {
                throw new ArgumentException("Сборка обязана содержать хотя бы одну деталь.", nameof(parts));
            }

            double mass = 0d;
            Vector3d weighted = Vector3d.Zero;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].MassKg <= 0d)
                {
                    throw new ArgumentOutOfRangeException(nameof(parts), "Масса детали обязана быть положительной.");
                }

                mass += parts[i].MassKg;
                weighted += parts[i].LocalPosition * parts[i].MassKg;
            }

            Vector3d center = weighted / mass;

            double ixx = 0d;
            double iyy = 0d;
            double izz = 0d;
            double ixy = 0d;
            double ixz = 0d;
            double iyz = 0d;
            for (int i = 0; i < parts.Count; i++)
            {
                Vector3d d = parts[i].LocalPosition - center;
                double m = parts[i].MassKg;
                ixx += m * ((d.Y * d.Y) + (d.Z * d.Z));
                iyy += m * ((d.X * d.X) + (d.Z * d.Z));
                izz += m * ((d.X * d.X) + (d.Y * d.Y));
                ixy -= m * d.X * d.Y;
                ixz -= m * d.X * d.Z;
                iyz -= m * d.Y * d.Z;
            }

            return new AssemblyStats(mass, center, new Matrix3x3(
                ixx, ixy, ixz,
                ixy, iyy, iyz,
                ixz, iyz, izz));
        }
    }
}
