using System;

namespace Galilego.Core
{
    /// <summary>
    /// Матрица 3×3, double. Нужна ровно для одного: тензор инерции сборки
    /// (I·ω и I⁻¹·τ в уравнениях Эйлера). Обратная — аналитически через
    /// присоединённую (вырожденная → исключение, не NaN). Больше ничего
    /// не умеет сознательно: не transport-библиотека.
    /// </summary>
    public readonly struct Matrix3x3
    {
        public readonly double M11;
        public readonly double M12;
        public readonly double M13;
        public readonly double M21;
        public readonly double M22;
        public readonly double M23;
        public readonly double M31;
        public readonly double M32;
        public readonly double M33;

        public Matrix3x3(
            double m11, double m12, double m13,
            double m21, double m22, double m23,
            double m31, double m32, double m33)
        {
            M11 = m11;
            M12 = m12;
            M13 = m13;
            M21 = m21;
            M22 = m22;
            M23 = m23;
            M31 = m31;
            M32 = m32;
            M33 = m33;
        }

        public static Matrix3x3 Identity => new Matrix3x3(
            1d, 0d, 0d,
            0d, 1d, 0d,
            0d, 0d, 1d);

        public static Vector3d operator *(Matrix3x3 m, Vector3d v)
        {
            return new Vector3d(
                ((m.M11 * v.X) + (m.M12 * v.Y)) + (m.M13 * v.Z),
                ((m.M21 * v.X) + (m.M22 * v.Y)) + (m.M23 * v.Z),
                ((m.M31 * v.X) + (m.M32 * v.Y)) + (m.M33 * v.Z));
        }

        public double Determinant()
        {
            return ((M11 * ((M22 * M33) - (M23 * M32)))
                - (M12 * ((M21 * M33) - (M23 * M31))))
                + (M13 * ((M21 * M32) - (M22 * M31)));
        }

        public Matrix3x3 Inverse()
        {
            double det = Determinant();
            if (det == 0d)
            {
                throw new InvalidOperationException("Вырожденная матрица 3×3 не обращается.");
            }

            double inv = 1d / det;
            return new Matrix3x3(
                (((M22 * M33) - (M23 * M32)) * inv), (((M13 * M32) - (M12 * M33)) * inv), (((M12 * M23) - (M13 * M22)) * inv),
                (((M23 * M31) - (M21 * M33)) * inv), (((M11 * M33) - (M13 * M31)) * inv), (((M13 * M21) - (M11 * M23)) * inv),
                (((M21 * M32) - (M22 * M31)) * inv), (((M12 * M31) - (M11 * M32)) * inv), (((M11 * M22) - (M12 * M21)) * inv));
        }
    }
}
