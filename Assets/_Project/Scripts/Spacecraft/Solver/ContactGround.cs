using System;
using Galilego.Core;

namespace Galilego.Spacecraft.Solver
{
    /// <summary>
    /// Contact geometry for the solver. Query: depth &gt; 0 means the point is below the
    /// surface (m), normal is the unit outward surface normal at that point.
    /// Pure C#, no Unity types: the surface is a function, like ITerrainModel.
    /// </summary>
    public interface IContactGround
    {
        void Query(Vector3d point, out double depth, out Vector3d normal);
    }

    /// <summary>Plane n·p = offset, n is the outward normal (unit).</summary>
    public sealed class PlaneGround : IContactGround
    {
        private readonly Vector3d normal;
        private readonly double offset;

        public PlaneGround(Vector3d normal, double offset)
        {
            if (!(normal.Magnitude > 0d))
            {
                throw new ArgumentException("Нормаль плоскости не может быть нулевой.", nameof(normal));
            }

            this.normal = normal.Normalized;
            this.offset = offset;
        }

        public void Query(Vector3d point, out double depth, out Vector3d outwardNormal)
        {
            depth = offset - Vector3d.Dot(normal, point);
            outwardNormal = normal;
        }
    }

    /// <summary>
    /// Surface y = h(x, z), y up. The normal derives from the SAME function h by central
    /// differences with step Footprint (m): n = normalize(−∂h/∂x, 1, −∂h/∂z). A small Footprint
    /// sees fine bumps (a rock), a large one averages them out (ITerrainModel contract).
    /// </summary>
    public sealed class HeightFieldGround : IContactGround
    {
        private readonly Func<double, double, double> height;
        private readonly double footprint;

        public HeightFieldGround(Func<double, double, double> height, double footprintMeters)
        {
            if (height == null)
            {
                throw new ArgumentNullException(nameof(height));
            }

            if (!(footprintMeters > 0d))
            {
                throw new ArgumentOutOfRangeException(nameof(footprintMeters), "Footprint обязан быть положительным.");
            }

            this.height = height;
            footprint = footprintMeters;
        }

        public double Footprint => footprint;

        public double Height(double x, double z) => height(x, z);

        public Vector3d NormalAt(double x, double z)
        {
            double dhdx = (height(x + footprint, z) - height(x - footprint, z)) / (2d * footprint);
            double dhdz = (height(x, z + footprint) - height(x, z - footprint)) / (2d * footprint);
            return new Vector3d(-dhdx, 1d, -dhdz).Normalized;
        }

        public void Query(Vector3d point, out double depth, out Vector3d outwardNormal)
        {
            depth = height(point.X, point.Z) - point.Y;
            outwardNormal = NormalAt(point.X, point.Z);
        }
    }
}
