using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Математика cube-sphere (тестируемая, без Unity-зависимостей кроме Vector3d).
    /// 6 граней куба; (u,v) ∈ [0,1]² → единичное направление в ТЕЛ-FIXED
    /// астрокадре (Z-up, тот же базис, что у HeightfieldTerrain.LatLonToDirection).
    /// Нормализация после проекции даёт равномерное покрытие сферы без
    /// полюсных вырождений lat/lon-сетки. Непрерывность через рёбра граней —
    /// свойство отображения, проверяется T85.
    /// </summary>
    internal static class CubeSphere
    {
        public const int FaceCount = 6;

        /// <summary>Направление (u,v) на грани face. u,v могут выходить за [0,1]
        /// (halo/экстраполяция) — отображение это допускает.</summary>
        public static Vector3d Direction(int face, double u, double v)
        {
            double a = (u * 2d) - 1d;
            double b = (v * 2d) - 1d;
            double x;
            double y;
            double z;
            switch (face)
            {
                case 0: x = 1d; y = b; z = -a; break;
                case 1: x = -1d; y = b; z = a; break;
                case 2: x = a; y = 1d; z = -b; break;
                case 3: x = a; y = -1d; z = b; break;
                case 4: x = a; y = b; z = 1d; break;
                default: x = -a; y = b; z = -1d; break;
            }

            return new Vector3d(x, y, z).Normalized;
        }

        /// <summary>Направление в центре узла quadtree: depth 0 — вся грань,
        /// далее деление [0,1]² на 2^depth по каждой оси.</summary>
        public static Vector3d NodeCenterDirection(int face, int depth, int ix, int iy)
        {
            double size = 1d / (1 << depth);
            double u = (ix + 0.5d) * size;
            double v = (iy + 0.5d) * size;
            return Direction(face, u, v);
        }
    }
}
