using System;
using Galilego.Core;

namespace Galilego.Flight
{
    public static class AttitudeMath
    {
        /// <summary>Кватернион body→мир по осям корабля, записанным в мировых координатах (ортонормированный базис).</summary>
        public static QuaternionD FromAxes(Vector3d x, Vector3d y, Vector3d z)
        {
            // Матрица R имеет столбцы x, y, z.
            double m00 = x.X, m01 = y.X, m02 = z.X;
            double m10 = x.Y, m11 = y.Y, m12 = z.Y;
            double m20 = x.Z, m21 = y.Z, m22 = z.Z;
            double tr = m00 + m11 + m22, qx, qy, qz, qw;
            if (tr > 0d)
            {
                double s = Math.Sqrt(tr + 1d) * 2d;
                qw = 0.25d * s; qx = (m21 - m12) / s; qy = (m02 - m20) / s; qz = (m10 - m01) / s;
            }
            else if (m00 > m11 && m00 > m22)
            {
                double s = Math.Sqrt(1d + m00 - m11 - m22) * 2d;
                qw = (m21 - m12) / s; qx = 0.25d * s; qy = (m01 + m10) / s; qz = (m02 + m20) / s;
            }
            else if (m11 > m22)
            {
                double s = Math.Sqrt(1d + m11 - m00 - m22) * 2d;
                qw = (m02 - m20) / s; qx = (m01 + m10) / s; qy = 0.25d * s; qz = (m12 + m21) / s;
            }
            else
            {
                double s = Math.Sqrt(1d + m22 - m00 - m11) * 2d;
                qw = (m10 - m01) / s; qx = (m02 + m20) / s; qy = (m12 + m21) / s; qz = 0.25d * s;
            }

            return new QuaternionD(qx, qy, qz, qw).Normalized;
        }
    }
}
