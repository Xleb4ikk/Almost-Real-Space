using System;
using System.Collections.Generic;
using Galilego.Core;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Ship part: mass, centre position in the ship frame, shape, orientation
    /// and joint strength. Constructor without a shape — point mass. A shape sets
    /// only the part's own inertia tensor; mass is always explicit.
    /// </summary>
    public readonly struct Part
    {
        public readonly double MassKg;
        public readonly Vector3d LocalPosition;
        public readonly double JointStrengthNewtons;
        public readonly PartShape Shape;
        public readonly Vector3d Dimensions;
        public readonly QuaternionD Orientation;

        public Part(double massKg, Vector3d localPosition, double jointStrengthNewtons)
            : this(massKg, localPosition, jointStrengthNewtons, PartShape.Point, Vector3d.Zero, QuaternionD.Identity)
        {
        }

        public Part(
            double massKg,
            Vector3d localPosition,
            double jointStrengthNewtons,
            PartShape shape,
            Vector3d dimensions,
            QuaternionD orientation)
        {
            MassKg = massKg;
            LocalPosition = localPosition;
            JointStrengthNewtons = jointStrengthNewtons;
            Shape = shape;
            Dimensions = dimensions;
            Orientation = orientation;
        }
    }

    /// <summary>
    /// A part's own inertia tensor about its centre of mass, in the ship frame,
    /// per 1 kg of mass. Principal moments in the part's axes, rotated into the
    /// ship frame: I = Σ I_k · a_k a_kᵀ, where a_k is the part's k-th axis in the ship frame.
    /// </summary>
    public static class PartInertia
    {
        public static Matrix3x3 PerKgShipFrame(PartShape shape, Vector3d dimensions, QuaternionD orientation)
        {
            double ix;
            double iy;
            double iz;
            switch (shape)
            {
                case PartShape.Point:
                    ix = 0d;
                    iy = 0d;
                    iz = 0d;
                    break;

                case PartShape.Box:
                {
                    RequirePositive(dimensions.X, "Box: размер X");
                    RequirePositive(dimensions.Y, "Box: размер Y");
                    RequirePositive(dimensions.Z, "Box: размер Z");
                    double a = dimensions.X;
                    double b = dimensions.Y;
                    double c = dimensions.Z;
                    ix = ((b * b) + (c * c)) / 12d;
                    iy = ((a * a) + (c * c)) / 12d;
                    iz = ((a * a) + (b * b)) / 12d;
                    break;
                }

                case PartShape.Cylinder:
                {
                    RequirePositive(dimensions.X, "Cylinder: радиус");
                    RequirePositive(dimensions.Y, "Cylinder: высота");
                    double r = dimensions.X;
                    double h = dimensions.Y;
                    double rr = r * r;
                    ix = ((3d * rr) + (h * h)) / 12d;
                    iy = rr / 2d;
                    iz = ix;
                    break;
                }

                case PartShape.Frustum:
                {
                    RequirePositive(dimensions.X, "Frustum: нижний радиус");
                    RequirePositive(dimensions.Y, "Frustum: высота");
                    RequirePositive(dimensions.Z, "Frustum: верхний радиус");
                    // A frustum is approximated by a cylinder of equivalent (RMS) radius:
                    // the difference from the exact formula is small.
                    double h = dimensions.Y;
                    double rr = ((dimensions.X * dimensions.X) + (dimensions.Z * dimensions.Z)) * 0.5d;
                    ix = ((3d * rr) + (h * h)) / 12d;
                    iy = rr / 2d;
                    iz = ix;
                    break;
                }

                case PartShape.Sphere:
                {
                    RequirePositive(dimensions.X, "Sphere: радиус");
                    ix = 0.4d * dimensions.X * dimensions.X;
                    iy = ix;
                    iz = ix;
                    break;
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(shape), "Неизвестная форма детали.");
            }

            Vector3d ex = orientation.Rotate(new Vector3d(1d, 0d, 0d));
            Vector3d ey = orientation.Rotate(new Vector3d(0d, 1d, 0d));
            Vector3d ez = orientation.Rotate(new Vector3d(0d, 0d, 1d));

            return Sum(
                Scaled(ix, ex),
                Scaled(iy, ey),
                Scaled(iz, ez));
        }

        private static void RequirePositive(double value, string what)
        {
            if (!(value > 0d))
            {
                throw new ArgumentOutOfRangeException(what, "Размер обязан быть положительным: " + value);
            }
        }

        private static Matrix3x3 Scaled(double moment, Vector3d axis)
        {
            return new Matrix3x3(
                moment * axis.X * axis.X, moment * axis.X * axis.Y, moment * axis.X * axis.Z,
                moment * axis.Y * axis.X, moment * axis.Y * axis.Y, moment * axis.Y * axis.Z,
                moment * axis.Z * axis.X, moment * axis.Z * axis.Y, moment * axis.Z * axis.Z);
        }

        private static Matrix3x3 Sum(Matrix3x3 a, Matrix3x3 b, Matrix3x3 c)
        {
            return new Matrix3x3(
                a.M11 + b.M11 + c.M11, a.M12 + b.M12 + c.M12, a.M13 + b.M13 + c.M13,
                a.M21 + b.M21 + c.M21, a.M22 + b.M22 + c.M22, a.M23 + b.M23 + c.M23,
                a.M31 + b.M31 + c.M31, a.M32 + b.M32 + c.M32, a.M33 + b.M33 + c.M33);
        }
    }

    /// <summary>
    /// Assembly summary: total mass, centre of mass, inertia tensor about the
    /// centre of mass. Each part contributes its own tensor (shape) and the
    /// parallel-axis theorem from its centre to the assembly centre of mass:
    ///   I = Σ [ I_own + m (|d|² E − d dᵀ) ],  d = r − COM.
    /// For point parts I_own = 0. Summing from the origin would give wrong free rotation.
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
                Part p = parts[i];
                Vector3d d = p.LocalPosition - center;
                double m = p.MassKg;
                Matrix3x3 own = PartInertia.PerKgShipFrame(p.Shape, p.Dimensions, p.Orientation);

                ixx += (m * ((d.Y * d.Y) + (d.Z * d.Z))) + (m * own.M11);
                iyy += (m * ((d.X * d.X) + (d.Z * d.Z))) + (m * own.M22);
                izz += (m * ((d.X * d.X) + (d.Y * d.Y))) + (m * own.M33);
                ixy += (-m * d.X * d.Y) + (m * own.M12);
                ixz += (-m * d.X * d.Z) + (m * own.M13);
                iyz += (-m * d.Y * d.Z) + (m * own.M23);
            }

            return new AssemblyStats(mass, center, new Matrix3x3(
                ixx, ixy, ixz,
                ixy, iyy, iyz,
                ixz, iyz, izz));
        }
    }

    /// <summary>
    /// Converts part descriptions (PartDefinition) into an assembly for physics:
    /// masses are normalised to the ship's actual mass (masses set PROPORTIONS,
    /// fuel burns off). null if there are no parts or their total mass is not
    /// positive: then the breakup model's whole-ship path is used.
    /// </summary>
    public static class PartAssembly
    {
        public static List<Part> FromDefinitions(IReadOnlyList<PartDefinition> definitions, double shipMass)
        {
            if (definitions == null || definitions.Count == 0)
            {
                return null;
            }

            double definedMass = 0d;
            for (int i = 0; i < definitions.Count; i++)
            {
                definedMass += definitions[i].MassKg;
            }

            if (definedMass <= 0d)
            {
                return null;
            }

            double scale = shipMass / definedMass;
            var assembly = new List<Part>(definitions.Count);
            for (int i = 0; i < definitions.Count; i++)
            {
                PartDefinition d = definitions[i];
                var orientation = new QuaternionD(d.Orientation.x, d.Orientation.y, d.Orientation.z, d.Orientation.w);
                assembly.Add(new Part(
                    d.MassKg * scale,
                    new Vector3d(d.Offset.x, d.Offset.y, d.Offset.z),
                    d.JointStrengthNewtons,
                    d.Shape,
                    new Vector3d(d.Dimensions.x, d.Dimensions.y, d.Dimensions.z),
                    orientation.Normalized));
            }

            return assembly;
        }
    }
}
