using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Cube-sphere math (testable, no Unity dependencies besides Vector3d).
    /// 6 cube faces; (u,v) ∈ [0,1]² → unit direction in the BODY-FIXED astro
    /// frame (Z-up, same basis as HeightfieldTerrain.LatLonToDirection).
    /// Normalizing after projection gives uniform sphere coverage without the
    /// pole singularities of a lat/lon grid. Continuity across face edges is a
    /// property of the mapping.
    /// </summary>
    internal static class CubeSphere
    {
        public const int FaceCount = 6;

        /// <summary>Direction (u,v) on face. u,v may exceed [0,1]
        /// (halo/extrapolation) — the mapping allows it.</summary>
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

        /// <summary>Direction at a quadtree node centre: depth 0 is the whole face,
        /// then [0,1]² split into 2^depth along each axis.</summary>
        public static Vector3d NodeCenterDirection(int face, int depth, int ix, int iy)
        {
            double size = 1d / (1 << depth);
            double u = (ix + 0.5d) * size;
            double v = (iy + 0.5d) * size;
            return Direction(face, u, v);
        }
    }
}
